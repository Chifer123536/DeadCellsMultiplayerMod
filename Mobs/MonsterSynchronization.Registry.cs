using System;
using System.Collections.Generic;
using dc.en;
using DeadCellsMultiplayerMod.Mobs.Bosses;

namespace DeadCellsMultiplayerMod.Mobs.MobsSynchronization
{
    /// <summary>
    /// Host-owned NetId registry. Native game entity ids and client entity-list indexes are never
    /// identities. The host assigns monotonic NetIds per level generation; clients only bind local
    /// <see cref="Mob"/> references to those NetIds via MOBREG or a one-shot type+spawn state bind.
    /// </summary>
    public partial class MobsSynchronization
    {
        /// <summary>One-shot bind radius for unbound locals matching a host registry/state entry.</summary>
        private const double ClientRegistryBindMaxDistancePx = 24.0 * 8.0;

        private static readonly List<NetNode.MobRegistryEntry> s_mobRegistryScratch = new();
        private static int s_lastHostMobRegistryToken;
        private static double s_lastHostMobRegistrySendFrame = -99999.0;
        private static int s_hostMobRegistryResendsRemaining;

        private static bool IsHostAuthorityForNetIds() =>
            LobbySession.NetRef?.IsHost == true;

        /// <summary>
        /// Host assigns NetIds in walk order (assignment order only). Clients track unbound mobs
        /// and wait for MOBREG / state packets — they never invent NetIds from list index.
        /// </summary>
        private static void AssignHostNetIdsForRebuildLocked(IReadOnlyList<Mob> candidateTrackedMobs)
        {
            // Continue the session-monotonic allocator instead of restarting at 0. See the note in
            // ResetMobTrackingStateLocked: an id must never be reused for a different enemy, because
            // a same-level rebuild produces an identical wire generation and cannot fence the reuse.
            for (var i = 0; i < candidateTrackedMobs.Count; i++)
            {
                var mob = candidateTrackedMobs[i];
                if (mob == null)
                    continue;

                trackedMobs.Add(mob);
                trackedMobIndices[mob] = trackedMobs.Count - 1;

                if (!IsHostAuthorityForNetIds())
                    continue;

                if (nextRuntimeSyncId < 1)
                    nextRuntimeSyncId = 1;
                var netId = nextRuntimeSyncId++;
                MobToId[mob] = netId;
                IdToMob[netId] = mob;
                StampHostBossNetIdLocked(mob, netId);
            }
        }

        private static void StampHostBossNetIdLocked(Mob mob, int netId)
        {
            if (mob == null || netId < 0)
                return;

            try
            {
                if (!BossSyncHelpers.IsBossMob(mob))
                    return;
            }
            catch
            {
                return;
            }

            // Fold boss bid into the same NetId space for this level generation.
            // EntityId is 1-based on the wire (0 = none). NetId remains 0-based in maps.
            var entityId = netId + 1;
            s_hostBossIdentities.Remove(mob);
            s_hostBossIdentities.Add(mob, new HostBossIdentity { EntityId = entityId });
            TrackHostPrimaryBossLocked(mob, entityId);
        }

        private static void QueueHostMobRegistryAfterRebuild()
        {
            if (!IsHostAuthorityForNetIds())
                return;

            lock (Sync)
            {
                s_hostMobRegistryResendsRemaining = HostAuthoritativeBootstrapResyncCount;
                s_lastHostMobRegistryToken = s_levelIdentityToken;
                s_lastHostMobRegistrySendFrame = -99999.0;
            }
        }

        /// <summary>
        /// Re-arms the MOBREG broadcast after a runtime mob registration, without disturbing a
        /// bootstrap burst that is still in flight.
        /// </summary>
        /// <remarks>
        /// A single resend is not enough: a client that is mid-level-load (generation not ready yet)
        /// or that loses the packet ends up with a host-only mob forever — states for its NetId fail
        /// resolution and, being non-boss, are dropped without replica creation. MOBREG is
        /// idempotent on the client, so a short burst is cheap insurance.
        /// </remarks>
        private static void MarkHostMobRegistryDirtyForRuntimeSpawn()
        {
            if (!IsHostAuthorityForNetIds())
                return;

            lock (Sync)
            {
                if (s_hostMobRegistryResendsRemaining < RuntimeSpawnRegistryResendCount)
                    s_hostMobRegistryResendsRemaining = RuntimeSpawnRegistryResendCount;
            }
        }

        private static void FlushHostMobRegistry(NetNode net)
        {
            if (!IsHost(net) || IsSyncQuiescedForTransition())
                return;
            if (!TryGetCurrentLevelIdentityToken(out var identityToken))
                return;

            var frame = GetCurrentFrame(null);
            lock (Sync)
            {
                if (s_lastHostMobRegistryToken != identityToken)
                {
                    s_lastHostMobRegistryToken = identityToken;
                    s_hostMobRegistryResendsRemaining = HostAuthoritativeBootstrapResyncCount;
                    s_lastHostMobRegistrySendFrame = -99999.0;
                }

                if (s_hostMobRegistryResendsRemaining <= 0)
                    return;

                if (frame - s_lastHostMobRegistrySendFrame < HostAuthoritativeBootstrapResyncIntervalFrames)
                    return;

                s_lastHostMobRegistrySendFrame = frame;
                s_hostMobRegistryResendsRemaining--;
                BuildHostMobRegistryEntriesLocked(identityToken, s_mobRegistryScratch);
            }

            if (s_mobRegistryScratch.Count == 0)
                return;

            net.SendMobRegistry(identityToken, s_mobRegistryScratch);
            s_mobRegistryScratch.Clear();
        }

        private static void BuildHostMobRegistryEntriesLocked(int generation, List<NetNode.MobRegistryEntry> dst)
        {
            dst.Clear();
            for (var i = 0; i < trackedMobs.Count; i++)
            {
                var mob = trackedMobs[i];
                if (mob == null || !MobToId.TryGetValue(mob, out var netId) || netId < 0)
                    continue;

                double x;
                double y;
                try
                {
                    x = GetWorldX(mob);
                    y = GetWorldY(mob);
                }
                catch
                {
                    continue;
                }

                var type = BuildMobStateTypeSignature(mob);
                dst.Add(new NetNode.MobRegistryEntry(netId, generation, type, x, y));
            }
        }

        private static void ConsumeIncomingMobRegistry(NetNode net)
        {
            if (!net.TryConsumeMobRegistry(out var entries) || entries == null || entries.Count == 0)
                return;

            try
            {
                var rejectedCount = 0;
                var rejectedGeneration = 0;
                lock (Sync)
                {
                    // Reserved set: group-spawned mobs (Flies, Bats) sit at identical coordinates.
                    // Binding must consume each local candidate at most once per batch, otherwise
                    // two registry entries grab the same mob and its twin stays unbound forever
                    // ("mob exists only for one player"). With consumption in place the ambiguity
                    // gap check is also relaxed inside TryBind (see reservedMobs handling there):
                    // identical-position same-type candidates are interchangeable.
                    s_registryBindReservedScratch.Clear();
                    for (var i = 0; i < entries.Count; i++)
                    {
                        var entry = entries[i];
                        if (entry.NetId <= 0)
                            continue;
                        if (!ShouldAcceptPacketGenerationLocked(entry.Generation, ref rejectedCount, ref rejectedGeneration))
                            continue;

                        if (TryGetTrackedMobBySyncIdLocked(entry.NetId, out var already) &&
                            already != null &&
                            DoesMobMatchStateType(already, entry.Type))
                        {
                            continue;
                        }

                        if (TryBindUnboundMobByTypeAndSpawnLocked(
                                entry.NetId,
                                entry.Type,
                                entry.X,
                                entry.Y,
                                reservedMobs: s_registryBindReservedScratch,
                                out var bound,
                                out var duplicateTwin) &&
                            bound != null)
                        {
                            s_registryBindReservedScratch.Add(bound);
                            MobSyncTrace.LogBindSyncId(
                                "mobreg_bind",
                                entry.NetId,
                                entry.Type ?? string.Empty,
                                entry.X,
                                entry.Y);
                            continue;
                        }

                        if (duplicateTwin)
                        {
                            // A same-cell same-type mob already holds a different NetId for this
                            // enemy (host wrapper/proxy duplication). Never build a duplicate
                            // replica — the enemy is already present locally under the other id.
                            MobSyncTrace.LogIncomingMappingMismatch(
                                "registry_duplicate_twin",
                                entry.NetId,
                                entry.Type ?? string.Empty,
                                string.Empty,
                                "same_enemy_already_bound");
                            continue;
                        }

                        // No local mob to bind. For level-bootstrap mobs this means the packet was
                        // early and a later resync repairs it, but for a host runtime spawn (malaise
                        // wave, summon, elite replacement) the mob simply does not exist here and
                        // never will. Build the replica so the second player can see and fight it.
                        //
                        // Duplicate guard: if an UNBOUND same-type candidate still sits near this
                        // entry, a later MOBREG burst can bind it — spawning a replica now would
                        // double the enemy on this client. Defer instead.
                        if (HasUnboundSameTypeCandidateNearLocked(entry.Type, entry.X, entry.Y))
                        {
                            MobSyncTrace.LogBindSyncId(
                                "mobreg_defer_unbound_candidate",
                                entry.NetId,
                                entry.Type ?? string.Empty,
                                entry.X,
                                entry.Y);
                            continue;
                        }

                        var spawned = TryCreateClientMobReplica(entry.Type, entry.X, entry.Y);
                        if (spawned != null)
                        {
                            MobToId[spawned] = entry.NetId;
                            IdToMob[entry.NetId] = spawned;
                            if (FindTrackedMobIndexLocked(spawned) < 0)
                            {
                                trackedMobs.Add(spawned);
                                trackedMobIndices[spawned] = trackedMobs.Count - 1;
                            }
                            s_registryBindReservedScratch.Add(spawned);

                            MobSyncTrace.LogBindSyncId(
                                "mobreg_spawn",
                                entry.NetId,
                                entry.Type ?? string.Empty,
                                entry.X,
                                entry.Y);
                            Tools.GameTrace.Event("mob", "client spawned replica netId={NetId} type={Type} x={X:F0} y={Y:F0}", entry.NetId, entry.Type, entry.X, entry.Y);
                        }
                    }
                    s_registryBindReservedScratch.Clear();
                }
            }
            finally
            {
                NetNode.ReleaseConsumedList(entries);
            }
        }

        private static readonly HashSet<Mob> s_registryBindReservedScratch = new(ReferenceEqualityComparer.Instance);

        /// <summary>
        /// True when an unbound, type-matching tracked mob sits within the registry bind radius of
        /// (x,y) — i.e. a future MOBREG bind can still claim this entry without a replica spawn.
        /// </summary>
        private static bool HasUnboundSameTypeCandidateNearLocked(string? type, double x, double y)
        {
            if (string.IsNullOrWhiteSpace(type) || !double.IsFinite(x) || !double.IsFinite(y))
                return false;

            var maxDistanceSq = ClientRegistryBindMaxDistancePx * ClientRegistryBindMaxDistancePx;
            for (var i = 0; i < trackedMobs.Count; i++)
            {
                var mob = trackedMobs[i];
                if (mob == null || s_registryBindReservedScratch.Contains(mob))
                    continue;
                if (MobToId.ContainsKey(mob))
                    continue;
                if (!DoesMobMatchStateType(mob, type))
                    continue;

                try
                {
                    var dx = GetWorldX(mob) - x;
                    var dy = GetWorldY(mob) - y;
                    if (dx * dx + dy * dy <= maxDistanceSq)
                        return true;
                }
                catch
                {
                }
            }

            return false;
        }

        /// <summary>
        /// One-shot bind for an unbound local mob: matching type + nearest spawn within distance.
        /// Never steals a healthy NetId mapping from another living mob.
        /// </summary>
        private static bool TryBindUnboundMobByTypeAndSpawnLocked(
            int netId,
            string? type,
            double x,
            double y,
            HashSet<Mob>? reservedMobs,
            out Mob? bound)
        {
            return TryBindUnboundMobByTypeAndSpawnLocked(netId, type, x, y, reservedMobs, out bound, out _);
        }

        /// <summary>
        /// One-shot bind for an unbound local mob: matching type + nearest spawn within distance.
        /// Never steals a healthy NetId mapping from another living mob.
        /// </summary>
        /// <param name="duplicateTwin">
        /// True when no unbound candidate exists but a same-cell same-type mob that is ALREADY
        /// bound to a different NetId was found. That means the host handed one native enemy two
        /// NetIds (wrapper/proxy duplication); the enemy already exists locally, so the caller must
        /// NOT build a replica for this entry.
        /// </param>
        private static bool TryBindUnboundMobByTypeAndSpawnLocked(
            int netId,
            string? type,
            double x,
            double y,
            HashSet<Mob>? reservedMobs,
            out Mob? bound,
            out bool duplicateTwin)
        {
            bound = null;
            duplicateTwin = false;
            if (netId <= 0 || string.IsNullOrWhiteSpace(type))
                return false;
            if (!double.IsFinite(x) || !double.IsFinite(y))
                return false;

            var maxDistanceSq = ClientRegistryBindMaxDistancePx * ClientRegistryBindMaxDistancePx;
            QuantizeWorldPositionToCells(x, y, out var entryCx, out var entryCy);
            var bestDistanceSq = double.MaxValue;
            var secondBestDistanceSq = double.MaxValue;
            Mob? best = null;
            var bestCellExact = false;
            var secondBestCellExact = false;
            var candidateCount = 0;
            var twinCount = 0;

            for (var i = 0; i < trackedMobs.Count; i++)
            {
                var mob = trackedMobs[i];
                if (mob == null || (reservedMobs != null && reservedMobs.Contains(mob)))
                    continue;
                if (!IsStateRebindCandidateLocked(mob))
                    continue;
                if (!DoesMobMatchStateType(mob, type))
                    continue;

                double dx;
                double dy;
                try
                {
                    dx = GetWorldX(mob) - x;
                    dy = GetWorldY(mob) - y;
                }
                catch
                {
                    continue;
                }

                if (!double.IsFinite(dx) || !double.IsFinite(dy))
                    continue;

                var distanceSq = dx * dx + dy * dy;
                if (distanceSq > maxDistanceSq)
                    continue;

                GetMobWorldCells(mob, out var mobCx, out var mobCy);
                var cellExact = mobCx == entryCx && mobCy == entryCy;

                var isBound = MobToId.TryGetValue(mob, out _);
                if (isBound)
                {
                    // Already owned by another NetId. A same-cell same-type "twin" means the host
                    // gave one native enemy two ids; the enemy is already present, so remember the
                    // twin for the caller instead of spawning a duplicate replica.
                    if (cellExact)
                        twinCount++;
                    continue;
                }

                candidateCount++;
                var prefer = best == null ||
                             (cellExact && !bestCellExact) ||
                             (cellExact == bestCellExact && distanceSq < bestDistanceSq);
                if (prefer)
                {
                    if (best != null)
                    {
                        secondBestDistanceSq = bestDistanceSq;
                        secondBestCellExact = bestCellExact;
                    }
                    bestDistanceSq = distanceSq;
                    bestCellExact = cellExact;
                    best = mob;
                }
                else if (cellExact == bestCellExact && distanceSq < secondBestDistanceSq)
                {
                    secondBestDistanceSq = distanceSq;
                    secondBestCellExact = cellExact;
                }
            }

            if (best == null)
            {
                duplicateTwin = twinCount > 0;
                return false;
            }

            if (candidateCount > 1 &&
                secondBestDistanceSq < double.MaxValue &&
                secondBestCellExact == bestCellExact)
            {
                var gap = Math.Sqrt(secondBestDistanceSq) - Math.Sqrt(bestDistanceSq);
                // Registry batch binds pass a reservation set: identical-position same-type
                // candidates (group spawns like Flies) are interchangeable, and the reservation
                // guarantees each local mob is consumed by at most one NetId in the batch. The
                // ambiguity gap check would otherwise permanently refuse both entries.
                if (reservedMobs == null && !IsBossRelatedEntity(type) && gap < ClientStateRebindMinimumGapPx)
                    return false;
            }

            TryRebindTrackedMobSyncIdLocked(best, netId);
            bound = best;
            return true;
        }
    }
}
