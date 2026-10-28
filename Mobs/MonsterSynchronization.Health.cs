using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using dc.en;
using DeadCellsMultiplayerMod.Mobs.Bosses;
using DeadCellsMultiplayerMod.PortableCore;
using Serilog;

namespace DeadCellsMultiplayerMod.Mobs.MobsSynchronization
{
    public partial class MobsSynchronization
    {
        enum HostStateSendKind
        {
            Dirty,
            Boss,
            Active,
            CatchUp,
            Boot,
            Tombstone
        }

        const int HostStateSendKindCount = (int)HostStateSendKind.Tombstone + 1;
        static readonly string[] HostStateSendKindNames = { "dirty", "boss", "active", "catchup", "boot", "tombstone" };

        const double MobHealthWindowSeconds = 5.0;
        const double HostAuditIntervalFrames = 60.0;
        const double HostCongestedActiveKeyframeIntervalFrames = 30.0;
        const double HostCongestedBossKeyframeIntervalFrames = 9.0;
        const double HostCongestedMoveIntervalScale = 2.0;
        const double HostCongestedStateMinIntervalFrames = 4.0;
        const int AuditExtraRepairStreak = 3;
        const int AuditHpRepairStreak = 3;
        const double AuditDesyncRelogSeconds = 10.0;
        const double AuditDesyncChangeLogSeconds = 4.0;
        const double AuditCleanRelogSeconds = 30.0;

        static bool s_hostSendCongested;
        static readonly long[] s_hostStateEntries = new long[HostStateSendKindCount];
        static readonly long[] s_hostStateBytes = new long[HostStateSendKindCount];
        static long s_hostForceStateEntries;
        static long s_hostMoveEntries;
        static long s_hostMoveBytes;
        static long s_hostSkippedBossPasses;
        static long s_hostSkippedActivePasses;
        static long s_hostSkippedCatchUpPasses;
        static long s_hostDeferredStates;
        static long s_hostDeferredMoves;
        static long s_hostSkippedTombstoneResends;
        static long s_hostAuditsSent;
        static long s_mobHealthWindowStartTicks;
        static double s_lastHostAuditFrame = -99999.0;
        static readonly Dictionary<int, int> s_hostStateSendsBySyncId = new();
        static readonly Dictionary<int, double> s_hostLastStateSendFrameBySyncId = new();
        static readonly List<MobAuditEntry> s_hostAuditAliveScratch = new();
        static readonly List<int> s_hostAuditDeadScratch = new();

        static readonly MobAuditStreaks s_clientAuditExtraStreaks = new();
        static readonly MobAuditStreaks s_clientAuditHpStreaks = new();
        static readonly Dictionary<int, int> s_clientAuditLastHostLife = new();
        static readonly List<MobAuditLocal> s_clientAuditLocalScratch = new();
        static readonly List<NetNode.MobDie> s_clientAuditRepairDiesScratch = new();
        static long s_clientAuditsCompared;
        static long s_clientAuditsDesynced;
        static long s_clientAuditRepairedExtras;
        static long s_clientAuditRepairedHp;
        static long s_clientAuditGenSkips;
        static string s_clientAuditLastSummary = "none";
        static string s_clientAuditLastLoggedShape = string.Empty;
        static long s_clientAuditLastLogTicks;
        static bool s_clientAuditLastClean = true;
        static long s_clientAuditDesyncSinceTicks;

        static void BeginHostHealthFrame(NetNode net)
        {
            s_hostSendCongested = net.IsOutgoingCongested;
        }

        static void RecordHostStateBatch(HostStateSendKind kind, IReadOnlyList<NetNode.MobStateSnapshot> batch, int bytes)
        {
            s_hostStateEntries[(int)kind] += batch.Count;
            s_hostStateBytes[(int)kind] += Math.Max(0, bytes);
            if (kind == HostStateSendKind.Tombstone)
                return;
            var frame = GetCurrentFrame(null);
            for (var i = 0; i < batch.Count; i++)
            {
                var syncId = batch[i].Index;
                s_hostStateSendsBySyncId.TryGetValue(syncId, out var sends);
                s_hostStateSendsBySyncId[syncId] = sends + 1;
                s_hostLastStateSendFrameBySyncId[syncId] = frame;
            }
        }

        static void RecordHostMoveBatch(int entries, int bytes)
        {
            s_hostMoveEntries += entries;
            s_hostMoveBytes += Math.Max(0, bytes);
        }

        static bool ShouldDeferHostStateForCongestion(int syncId, HostMobDirtyFlags flags)
        {
            if (!s_hostSendCongested || (flags & HostMobDirtyFlags.ForceState) != 0)
                return false;
            if (!s_hostLastStateSendFrameBySyncId.TryGetValue(syncId, out var lastFrame))
                return false;
            var elapsed = GetCurrentFrame(null) - lastFrame;
            if (elapsed < 0.0 || elapsed >= HostCongestedStateMinIntervalFrames)
                return false;

            s_hostDeferredStates++;
            return true;
        }

        static double ScaleHostMoveIntervalForCongestion(double interval, HostMobSyncPriority priority)
        {
            if (!s_hostSendCongested || priority == HostMobSyncPriority.Active)
                return interval;
            return interval * HostCongestedMoveIntervalScale;
        }

        static void ClearHostHealthStateLocked()
        {
            s_hostLastStateSendFrameBySyncId.Clear();
            s_hostStateSendsBySyncId.Clear();
            s_lastHostAuditFrame = -99999.0;
        }

        static void FlushHostMobAudit(NetNode net)
        {
            if (!IsHost(net) || IsSyncQuiescedForTransition())
                return;
            if (!TryGetCurrentLevelIdentityToken(out var identityToken))
                return;

            var frame = GetCurrentFrame(null);
            if (frame >= s_lastHostAuditFrame && frame - s_lastHostAuditFrame < HostAuditIntervalFrames)
                return;
            s_lastHostAuditFrame = frame;

            s_hostAuditAliveScratch.Clear();
            s_hostAuditDeadScratch.Clear();
            lock (Sync)
            {
                for (var i = 0; i < trackedMobs.Count; i++)
                {
                    var mob = trackedMobs[i];
                    if (mob == null || !MobToId.TryGetValue(mob, out var syncId) || syncId <= 0)
                        continue;

                    try
                    {
                        if (mob.destroyed || mob.life <= 0)
                            continue;
                        s_hostAuditAliveScratch.Add(new MobAuditEntry(syncId, mob.life));
                    }
                    catch
                    {
                    }
                }

                foreach (var pair in s_hostDeathTombstonesBySyncId)
                {
                    if (pair.Value.Generation == identityToken)
                        s_hostAuditDeadScratch.Add(pair.Key);
                }
            }

            net.SendMobAudit(identityToken, s_hostAuditAliveScratch, s_hostAuditDeadScratch);
            s_hostAuditsSent++;
        }

        static void ConsumeIncomingMobAudit(NetNode net)
        {
            if (IsSyncQuiescedForTransition())
                return;
            if (!net.TryConsumeMobAudit(out var audit) || audit == null)
                return;

            s_clientAuditLocalScratch.Clear();
            lock (Sync)
            {
                if (!IsPacketGenerationCurrentLocked(audit.Generation))
                {
                    s_clientAuditGenSkips++;
                    return;
                }

                foreach (var pair in IdToMob)
                {
                    var mob = pair.Value;
                    if (mob == null || pair.Key <= 0)
                        continue;

                    try
                    {
                        if (mob._level != null && !DoesLevelMatchCurrentIdentityLocked(mob._level))
                            continue;
                        var alive = !mob.destroyed && mob.life > 0;
                        s_clientAuditLocalScratch.Add(new MobAuditLocal(pair.Key, mob.life, alive, BossSyncHelpers.IsBossMob(mob)));
                    }
                    catch
                    {
                    }
                }
            }

            var result = MobAuditDiff.Compare(audit, s_clientAuditLocalScratch);
            s_clientAuditsCompared++;
            if (!result.IsClean)
                s_clientAuditsDesynced++;

            var tombstoned = 0;
            lock (Sync)
            {
                foreach (var syncId in result.MissingOnClient)
                {
                    if (HasClientMobTombstoneLocked(syncId))
                        tombstoned++;
                }
            }

            RepairAuditExtras(audit, result);
            RepairAuditHp(audit, result);
            LogAuditResult(audit, result, tombstoned);
        }

        static void RepairAuditExtras(MobAuditSnapshot audit, MobAuditResult result)
        {
            var due = s_clientAuditExtraStreaks.Update(audit.Generation, result.ExtraOnClient, AuditExtraRepairStreak);
            if (due.Count == 0)
                return;

            s_clientAuditRepairDiesScratch.Clear();
            lock (Sync)
            {
                foreach (var syncId in due)
                {
                    if (!IdToMob.TryGetValue(syncId, out var mob) || mob == null || BossSyncHelpers.IsBossMob(mob))
                        continue;

                    double x = 0, y = 0;
                    try
                    {
                        x = GetWorldX(mob);
                        y = GetWorldY(mob);
                    }
                    catch
                    {
                    }

                    s_clientAuditRepairDiesScratch.Add(new NetNode.MobDie(1, syncId, x, y, audit.Generation));
                }
            }

            if (s_clientAuditRepairDiesScratch.Count == 0)
                return;

            ApplyIncomingMobDies(s_clientAuditRepairDiesScratch);
            s_clientAuditRepairedExtras += s_clientAuditRepairDiesScratch.Count;
            Log.Warning(
                "[MobAudit] repair: finished {Count} client-only mobs the host has not had alive for {Streak} audits ids={Ids}",
                s_clientAuditRepairDiesScratch.Count,
                AuditExtraRepairStreak,
                MobAuditDiff.FormatIds(due));
        }

        static void RepairAuditHp(MobAuditSnapshot audit, MobAuditResult result)
        {
            var stable = new List<int>(result.HpMismatch.Count);
            var hostLifeById = new Dictionary<int, int>(result.HpMismatch.Count);
            foreach (var mismatch in result.HpMismatch)
            {
                hostLifeById[mismatch.SyncId] = mismatch.HostLife;
                if (s_clientAuditLastHostLife.TryGetValue(mismatch.SyncId, out var previousHostLife) &&
                    previousHostLife == mismatch.HostLife)
                {
                    stable.Add(mismatch.SyncId);
                }
            }

            s_clientAuditLastHostLife.Clear();
            foreach (var pair in hostLifeById)
                s_clientAuditLastHostLife[pair.Key] = pair.Value;

            var due = s_clientAuditHpStreaks.Update(audit.Generation, stable, AuditHpRepairStreak);
            if (due.Count == 0)
                return;

            var repaired = new List<int>(due.Count);
            foreach (var syncId in due)
            {
                Mob? mob;
                lock (Sync)
                {
                    if (!IdToMob.TryGetValue(syncId, out mob) || mob == null)
                        continue;
                }

                try
                {
                    if (mob.destroyed || BossSyncHelpers.IsBossMob(mob) || !hostLifeById.TryGetValue(syncId, out var hostLife))
                        continue;
                    ApplyAuthoritativeLifeState(mob, hostLife, 0);
                    repaired.Add(syncId);
                }
                catch (Exception ex)
                {
                    Log.Debug("[MobAudit] hp repair failed syncId={SyncId}: {Message}", syncId, ex.Message);
                }
            }

            if (repaired.Count == 0)
                return;

            s_clientAuditRepairedHp += repaired.Count;
            Log.Warning(
                "[MobAudit] repair: set host HP on {Count} mobs whose HP stayed different for {Streak} audits ids={Ids}",
                repaired.Count,
                AuditHpRepairStreak,
                MobAuditDiff.FormatIds(repaired));
        }

        static void LogAuditResult(MobAuditSnapshot audit, MobAuditResult result, int tombstoned)
        {
            var summary = string.Create(
                CultureInfo.InvariantCulture,
                $"host={result.HostAlive} client={result.ClientAlive} matched={result.Matched} missing={result.MissingOnClient.Count} deadOnClient={result.DeadOnClientOnly.Count} extra={result.ExtraOnClient.Count} hp={result.HpMismatch.Count}");
            s_clientAuditLastSummary = summary;

            var now = Stopwatch.GetTimestamp();
            var sinceLog = s_clientAuditLastLogTicks == 0
                ? double.MaxValue
                : (double)(now - s_clientAuditLastLogTicks) / Stopwatch.Frequency;

            if (result.IsClean)
            {
                if (!s_clientAuditLastClean)
                {
                    var desyncSeconds = (double)(now - s_clientAuditDesyncSinceTicks) / Stopwatch.Frequency;
                    Log.Information("[MobAudit] ok again after {Seconds:F0}s: {Summary}", desyncSeconds, summary);
                    s_clientAuditLastLogTicks = now;
                }
                else if (sinceLog >= AuditCleanRelogSeconds)
                {
                    Log.Information("[MobAudit] ok {Summary} hostQ={Queue}KB hostRtt={Rtt}ms", summary, audit.HostBacklogBytes / 1024, audit.HostRttMs);
                    s_clientAuditLastLogTicks = now;
                }

                s_clientAuditLastClean = true;
                s_clientAuditLastLoggedShape = string.Empty;
                return;
            }

            var startedNow = s_clientAuditLastClean;
            if (startedNow)
                s_clientAuditDesyncSinceTicks = now;
            s_clientAuditLastClean = false;

            var shape = string.Join(
                ";",
                MobAuditDiff.FormatIds(result.MissingOnClient, 64),
                MobAuditDiff.FormatIds(result.DeadOnClientOnly, 64),
                MobAuditDiff.FormatIds(result.ExtraOnClient, 64));
            var relogDue = startedNow ||
                           sinceLog >= (shape == s_clientAuditLastLoggedShape ? AuditDesyncRelogSeconds : AuditDesyncChangeLogSeconds);
            if (!relogDue)
                return;

            s_clientAuditLastLoggedShape = shape;
            s_clientAuditLastLogTicks = now;
            Log.Warning(
                "[MobAudit] DESYNC {Summary} | missing={Missing} (recovering {Tombstoned}) deadOnClient={DeadOnClient} extra={Extra} (hostDead {ConfirmedDead}) hp={Hp} | hostQ={Queue}KB hostRtt={Rtt}ms",
                summary,
                MobAuditDiff.FormatIds(result.MissingOnClient),
                tombstoned,
                MobAuditDiff.FormatIds(result.DeadOnClientOnly),
                MobAuditDiff.FormatIds(result.ExtraOnClient),
                result.ExtraConfirmedDead.Count,
                FormatHpMismatches(result.HpMismatch),
                audit.HostBacklogBytes / 1024,
                audit.HostRttMs);
        }

        static string FormatHpMismatches(List<MobAuditHpMismatch> mismatches)
        {
            if (mismatches.Count == 0)
                return "[]";

            var sb = new StringBuilder("[");
            var shown = Math.Min(6, mismatches.Count);
            for (var i = 0; i < shown; i++)
            {
                if (i > 0)
                    sb.Append(',');
                var m = mismatches[i];
                sb.Append(m.SyncId.ToString(CultureInfo.InvariantCulture)).Append(':')
                  .Append(m.ClientLife.ToString(CultureInfo.InvariantCulture)).Append('/')
                  .Append(m.HostLife.ToString(CultureInfo.InvariantCulture));
            }
            if (mismatches.Count > shown)
                sb.Append(",+").Append(mismatches.Count - shown);
            return sb.Append(']').ToString();
        }

        static void ResetClientAuditState()
        {
            s_clientAuditExtraStreaks.Reset();
            s_clientAuditHpStreaks.Reset();
            s_clientAuditLastHostLife.Clear();
            s_clientAuditLastClean = true;
            s_clientAuditLastLoggedShape = string.Empty;
        }

        static void FlushMobHealth(NetNode net)
        {
            var now = Stopwatch.GetTimestamp();
            if (s_mobHealthWindowStartTicks == 0)
            {
                s_mobHealthWindowStartTicks = now;
                return;
            }

            var seconds = (double)(now - s_mobHealthWindowStartTicks) / Stopwatch.Frequency;
            if (seconds < MobHealthWindowSeconds)
                return;
            s_mobHealthWindowStartTicks = now;

            string level;
            int generation;
            int tracked;
            int mapped;
            int visible;
            int tombstones;
            lock (Sync)
            {
                level = GetLevelTraceIdSafe(currentLevel);
                generation = s_levelIdentityToken;
                tracked = trackedMobs.Count;
                mapped = IdToMob.Count;
                visible = hostClientInterestUsersBySyncId.Count;
                tombstones = s_clientMobTombstonesBySyncId.Count;
            }

            if (IsHost(net))
            {
                if (tracked == 0 && s_hostMoveEntries == 0 && Sum(s_hostStateEntries) == 0)
                    return;
                FlushHostMobHealth(level, generation, tracked, visible, seconds);
            }
            else if (IsClient(net))
            {
                if (tracked == 0 && mapped == 0 && s_clientAuditsCompared == 0)
                    return;
                FlushClientMobHealth(level, generation, tracked, mapped, tombstones);
            }
        }

        static void FlushHostMobHealth(string level, int generation, int tracked, int visible, double seconds)
        {
            var sb = new StringBuilder(256);
            sb.Append("[MobHealth] host level=").Append(level).Append(" gen=").Append(generation)
              .Append(" tracked=").Append(tracked).Append(" clientVisible=").Append(visible)
              .Append(" congested=").Append(s_hostSendCongested ? "yes" : "no")
              .Append(" | states");
            long stateEntries = 0;
            for (var i = 0; i < HostStateSendKindCount; i++)
            {
                stateEntries += s_hostStateEntries[i];
                if (s_hostStateEntries[i] == 0)
                    continue;
                sb.Append(' ').Append(HostStateSendKindNames[i]).Append('=').Append(s_hostStateEntries[i])
                  .Append('/').Append((s_hostStateBytes[i] / 1024.0 / seconds).ToString("0.0", CultureInfo.InvariantCulture)).Append("KB/s");
            }
            if (stateEntries == 0)
                sb.Append(" none");
            if (s_hostForceStateEntries > 0)
                sb.Append(" (force ").Append(s_hostForceStateEntries).Append(')');
            sb.Append(" | moves=").Append(s_hostMoveEntries).Append('/')
              .Append((s_hostMoveBytes / 1024.0 / seconds).ToString("0.0", CultureInfo.InvariantCulture)).Append("KB/s");

            var skipped = s_hostSkippedBossPasses + s_hostSkippedActivePasses + s_hostSkippedCatchUpPasses +
                          s_hostDeferredStates + s_hostDeferredMoves + s_hostSkippedTombstoneResends;
            if (skipped > 0)
            {
                sb.Append(" | trimmed boss=").Append(s_hostSkippedBossPasses)
                  .Append(" active=").Append(s_hostSkippedActivePasses)
                  .Append(" catchup=").Append(s_hostSkippedCatchUpPasses)
                  .Append(" states=").Append(s_hostDeferredStates)
                  .Append(" moves=").Append(s_hostDeferredMoves)
                  .Append(" deathResends=").Append(s_hostSkippedTombstoneResends);
            }

            sb.Append(" | hot=").Append(FormatHotMobs(3)).Append(" audits=").Append(s_hostAuditsSent);
            Log.Information(sb.ToString());

            Array.Clear(s_hostStateEntries);
            Array.Clear(s_hostStateBytes);
            s_hostForceStateEntries = 0;
            s_hostMoveEntries = 0;
            s_hostMoveBytes = 0;
            s_hostSkippedBossPasses = 0;
            s_hostSkippedActivePasses = 0;
            s_hostSkippedCatchUpPasses = 0;
            s_hostDeferredStates = 0;
            s_hostDeferredMoves = 0;
            s_hostSkippedTombstoneResends = 0;
            s_hostAuditsSent = 0;
            s_hostStateSendsBySyncId.Clear();
        }

        static long Sum(long[] values)
        {
            long total = 0;
            foreach (var value in values)
                total += value;
            return total;
        }

        static string FormatHotMobs(int top)
        {
            if (s_hostStateSendsBySyncId.Count == 0)
                return "[]";

            var entries = new List<KeyValuePair<int, int>>(s_hostStateSendsBySyncId);
            entries.Sort((a, b) => b.Value.CompareTo(a.Value));
            var sb = new StringBuilder("[");
            var shown = Math.Min(top, entries.Count);
            for (var i = 0; i < shown; i++)
            {
                if (i > 0)
                    sb.Append(',');
                string type;
                lock (Sync)
                    type = hostMobTypeBySyncId.TryGetValue(entries[i].Key, out var t) ? t : "?";
                sb.Append(entries[i].Key.ToString(CultureInfo.InvariantCulture)).Append(':').Append(ShortType(type))
                  .Append('x').Append(entries[i].Value.ToString(CultureInfo.InvariantCulture));
            }
            return sb.Append(']').ToString();
        }

        static string ShortType(string type)
        {
            if (string.IsNullOrEmpty(type))
                return "?";
            var bar = type.IndexOf('|');
            return bar > 0 ? type[..bar] : type;
        }

        static void FlushClientMobHealth(string level, int generation, int tracked, int mapped, int tombstones)
        {
            Log.Information(
                "[MobHealth] client level={Level} gen={Gen} tracked={Tracked} mapped={Mapped} tombstones={Tombstones} resolveFails={ResolveFails} hitResolveFails={HitResolveFails} missingSync={MissingSync} | audits={Audits} desynced={Desynced} genSkips={GenSkips} repaired extra={RepairedExtra} hp={RepairedHp} | last {Summary}",
                level,
                generation,
                tracked,
                mapped,
                tombstones,
                s_diagResolveFails,
                s_diagHitResolveFails,
                s_diagMissingSyncPackets,
                s_clientAuditsCompared,
                s_clientAuditsDesynced,
                s_clientAuditGenSkips,
                s_clientAuditRepairedExtras,
                s_clientAuditRepairedHp,
                s_clientAuditLastSummary);

            lock (Sync)
            {
                s_diagTombstoneLookups = 0;
                s_diagTombstoneHits = 0;
                s_diagResolveFails = 0;
                s_diagHitResolveFails = 0;
                s_diagMissingSyncPackets = 0;
            }
            s_clientAuditsCompared = 0;
            s_clientAuditsDesynced = 0;
            s_clientAuditGenSkips = 0;
            s_clientAuditRepairedExtras = 0;
            s_clientAuditRepairedHp = 0;
        }
    }
}

/*
 * ═══════════════════════════════════════
 *  MOB SYNC HEALTH — КАК РАБОТАЕТ
 * ═══════════════════════════════════════
 *  [MobHealth] раз в 5 с. Хост: tracked/clientVisible, отправленные состояния по причине
 *  (dirty — изменилось, boss/active/catchup — повторные ключевые кадры, boot — старт уровня,
 *  tombstone — повторы смертей) в шт. и КБ/с,
 *  MOBMOVE, что срезано при перегрузке, hot = три моба с наибольшим числом состояний, число сверок.
 *  Клиент: tracked/mapped/надгробия, промахи резолва, итог сверок, сколько починено.
 *  Перегрузка (NetNode.IsOutgoingCongested, очередь Steam ≥ 24 КБ): boss-кадр 3→9, active 8→30 кадров,
 *  catch-up пропускается (кроме старта уровня), интервал MOBMOVE не-активных ×2,
 *  dirty-состояние без ForceState не чаще раза в 4 кадра на моба, повторы смертей (надгробия) на паузе.
 *  Первая MOBDIE, события, удары, регистрация не трогаются.
 *  Сверка: хост раз в 60 кадров шлёт MOBAUDIT (живые syncId+HP, надгробия текущего gen).
 *  Клиент сравнивает с IdToMob текущего уровня → [MobAudit] ok (раз в 30 с) / DESYNC
 *  (новая картина — не чаще раза в 4 с, та же — раз в 10 с).
 *  Починка (не боссы): жив только у клиента 3 сверки подряд → тот же путь, что MOBDIE;
 *  HP расходится 3 сверки подряд при неизменном HP хоста → ApplyAuthoritativeLifeState(HP хоста).
 * ═══════════════════════════════════════
*/
