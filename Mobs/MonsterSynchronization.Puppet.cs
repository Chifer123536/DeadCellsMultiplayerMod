using System;
using System.Collections.Generic;
using dc.en;
using DeadCellsMultiplayerMod.Mobs.Bosses;
using DeadCellsMultiplayerMod.Network;
using DeadCellsMultiplayerMod.PortableCore;

namespace DeadCellsMultiplayerMod.Mobs.MobsSynchronization
{
    public partial class MobsSynchronization
    {
        const double PuppetGroundedYTolerancePx = 6.0;
        const double PuppetAirborneDyEpsilon = 0.03;
        const double PuppetLandDetectDy = 0.08;
        const double PuppetLocalHitWindowMs = 250.0;
        const double PuppetBumpEpsilon = 0.0005;
        const double PuppetMaxVelocityRaw = 8.0;
        const double PuppetAttackMaxHoldMs = 300.0;
        const int PuppetAttackGateLimit = 128;

        sealed class MobPuppet
        {
            public readonly MobPuppetTrack Track = new();
            public readonly PuppetImpulseOffset Impulse = new();
            public string Anim = string.Empty;
            public bool HasPose;
            public double LastSetX = double.NaN;
            public double LastSetDy;
            public bool LastAirborne;
            public bool LandedLocally;
            public double LastLocalHitMs = double.NegativeInfinity;
        }

        readonly record struct GatedMobAttack(Mob Mob, NetNode.MobAttack Attack, double ArrivalMs);

        static readonly Dictionary<Mob, MobPuppet> s_mobPuppets = new(ReferenceEqualityComparer.Instance);
        static readonly List<GatedMobAttack> s_gatedMobAttacks = new();
        static readonly List<GatedMobAttack> s_dueMobAttacksScratch = new();
        static readonly double s_hostWireEpochMs = NetNode.MonotonicMs();

        static double HostWireClockMs() => Math.Floor(NetNode.MonotonicMs() - s_hostWireEpochMs) + 1.0;

        static bool IsMobPuppetModeEnabled() => MultiplayerSettingsStorage.MobPuppets;

        static void PushMobPuppetSampleLocked(Mob mob, double hostMs, double x, double y, int dir, double dx, double dy, string? anim, bool cut)
        {
            if (mob == null || BossSyncHelpers.IsBossMob(mob))
                return;

            if (!s_mobPuppets.TryGetValue(mob, out var puppet))
            {
                puppet = new MobPuppet();
                s_mobPuppets[mob] = puppet;
            }

            if (cut)
                puppet.Impulse.Reset();
            puppet.Track.Push(hostMs, x, y, dir, dx, dy, anim, cut);
        }

        static void RemoveMobPuppetLocked(Mob mob) => s_mobPuppets.Remove(mob);

        static void ClearMobPuppetsLocked()
        {
            s_mobPuppets.Clear();
            s_gatedMobAttacks.Clear();
        }

        static bool TryGateMobPuppetAttack(Mob mob, in NetNode.MobAttack attack)
        {
            if (attack.HostMs <= 0.0 || !IsMobPuppetModeEnabled())
                return false;

            var nowMs = NetNode.MonotonicMs();
            lock (Sync)
            {
                if (!s_mobPuppets.ContainsKey(mob))
                    return false;

                var hasEarlier = false;
                for (var i = 0; i < s_gatedMobAttacks.Count; i++)
                {
                    if (ReferenceEquals(s_gatedMobAttacks[i].Mob, mob))
                    {
                        hasEarlier = true;
                        break;
                    }
                }

                var renderMs = NetNode.MobClock.Advance(nowMs);
                if (!hasEarlier && (!double.IsFinite(renderMs) || renderMs >= attack.HostMs))
                    return false;

                if (s_gatedMobAttacks.Count >= PuppetAttackGateLimit)
                    return false;

                s_gatedMobAttacks.Add(new GatedMobAttack(mob, attack, nowMs));
                MobPuppetStats.RecordGatedAttack();
                return true;
            }
        }

        static void ReleaseDueMobPuppetAttacks()
        {
            if (IsSyncQuiescedForTransition())
                return;

            var nowMs = NetNode.MonotonicMs();
            lock (Sync)
            {
                if (s_gatedMobAttacks.Count == 0)
                    return;

                var renderMs = NetNode.MobClock.Advance(nowMs);
                var force = !IsMobPuppetModeEnabled() || !double.IsFinite(renderMs);
                s_dueMobAttacksScratch.Clear();
                for (var i = 0; i < s_gatedMobAttacks.Count;)
                {
                    var gated = s_gatedMobAttacks[i];
                    var expired = nowMs - gated.ArrivalMs >= PuppetAttackMaxHoldMs;
                    if (force || renderMs >= gated.Attack.HostMs || expired)
                    {
                        if (expired && !force && renderMs < gated.Attack.HostMs)
                            MobPuppetStats.RecordLateAttack();
                        s_dueMobAttacksScratch.Add(gated);
                        s_gatedMobAttacks.RemoveAt(i);
                        continue;
                    }
                    i++;
                }
            }

            for (var i = 0; i < s_dueMobAttacksScratch.Count; i++)
            {
                var gated = s_dueMobAttacksScratch[i];
                bool tracked;
                lock (Sync)
                    tracked = s_mobPuppets.ContainsKey(gated.Mob);
                if (!tracked)
                    continue;

                bool destroyed;
                try { destroyed = gated.Mob.destroyed; }
                catch { destroyed = true; }
                if (destroyed)
                    continue;

                var attack = gated.Attack;
                TryQueueClientMobAttack(gated.Mob, attack.SkillId, attack.RequiresTargetInArea, attack.Data, attack.TargetUserId, attack.Dir);
            }

            s_dueMobAttacksScratch.Clear();
        }

        static void MarkMobPuppetLocalHit(Mob mob)
        {
            lock (Sync)
            {
                if (s_mobPuppets.TryGetValue(mob, out var puppet))
                    puppet.LastLocalHitMs = NetNode.MonotonicMs();
            }
        }

        static bool TryGetMobPuppetAnimLocked(Mob mob, out string anim)
        {
            anim = string.Empty;
            if (!IsMobPuppetModeEnabled() || !s_mobPuppets.TryGetValue(mob, out var puppet) || !puppet.HasPose)
                return false;
            anim = puppet.Anim;
            return true;
        }

        static bool TryApplyMobPuppet(Mob self)
        {
            if (!IsMobPuppetModeEnabled())
                return false;

            MobPuppet? puppet;
            ClientMobState target;
            lock (Sync)
            {
                if (!s_mobPuppets.TryGetValue(self, out puppet) || !clientMobTargets.TryGetValue(self, out target))
                    return false;
            }

            if (BossSyncHelpers.IsBossMob(self))
                return false;

            var nowMs = NetNode.MonotonicMs();
            var renderMs = NetNode.MobClock.Advance(nowMs);
            if (!puppet.Track.TrySample(renderMs, out var pose))
            {
                lock (Sync)
                    puppet.HasPose = false;
                return false;
            }
            MobPuppetStats.RecordPose(pose.Extrapolated);

            lock (Sync)
            {
                puppet.Anim = pose.Anim;
                puppet.HasPose = true;
            }

            ApplyAuthoritativeLifeState(self, target.Life, target.MaxLife);
            if (!ShouldProcessClientVisualState(self))
            {
                puppet.LastSetX = double.NaN;
                puppet.LastAirborne = false;
                return true;
            }

            bool hasGravity;
            try { hasGravity = self.hasGravity; }
            catch { hasGravity = true; }

            var currentX = GetWorldX(self);
            var currentY = GetWorldY(self);

            CapturePuppetLocalImpulse(self, puppet, currentX, nowMs);
            var x = pose.X + puppet.Impulse.Sample(nowMs);

            double localDy;
            try { localDy = self.dy + self.bdy; }
            catch { localDy = 0.0; }

            var airborne = !hasGravity || System.Math.Abs(pose.Dy) > PuppetAirborneDyEpsilon;
            if (hasGravity && puppet.LastAirborne && puppet.LastSetDy > PuppetLandDetectDy && System.Math.Abs(localDy) < 1e-9)
                puppet.LandedLocally = true;
            if (!airborne || pose.Dy < -PuppetAirborneDyEpsilon)
                puppet.LandedLocally = false;

            bool setY;
            if (!hasGravity)
                setY = true;
            else if (airborne)
                setY = !puppet.LandedLocally;
            else if (System.Math.Abs(currentY - pose.Y) > PuppetGroundedYTolerancePx)
            {
                setY = true;
                MobPuppetStats.RecordGroundSnap();
            }
            else
                setY = false;

            try
            {
                SetPuppetPosition(self, x, pose.Y, setY);
            }
            catch
            {
                return false;
            }

            ApplyPuppetVelocity(self, pose, hasGravity, airborne, setY);

            puppet.LastSetX = x;
            puppet.LastAirborne = hasGravity && airborne;
            puppet.LastSetDy = hasGravity && airborne && setY ? pose.Dy : 0.0;

            if (pose.Dir != 0)
                self.dir = pose.Dir;

            if (target.ForcePositionSnap || target.ForceVerticalPositionSnap)
                ClearClientForcePositionSnapAfterApply(self, target);
            return true;
        }

        static void CapturePuppetLocalImpulse(Mob self, MobPuppet puppet, double currentX, double nowMs)
        {
            if (!double.IsFinite(puppet.LastSetX) || nowMs - puppet.LastLocalHitMs > PuppetLocalHitWindowMs)
                return;

            double bdx;
            try { bdx = self.bdx; }
            catch { return; }
            if (!double.IsFinite(bdx) || System.Math.Abs(bdx) <= PuppetBumpEpsilon)
                return;

            var moved = currentX - puppet.LastSetX;
            var push = bdx * PixelsPerCase;
            if (System.Math.Sign(moved) != System.Math.Sign(push))
                return;

            var holdMs = NetHealth.LastRttMs + NetNode.MobClock.TargetDelayMs;
            puppet.Impulse.Add(System.Math.Sign(push) * System.Math.Min(System.Math.Abs(push), System.Math.Abs(moved)), nowMs, holdMs);
        }

        static void SetPuppetPosition(Mob mob, double x, double y, bool setY)
        {
            PuppetCellMath.ToCellX(x, out var cx, out var xr);
            if (!setY)
            {
                mob.setPosCase(cx, mob.cy, xr, mob.yr);
                return;
            }

            PuppetCellMath.ToCellFeetY(y, out var cy, out var yr);
            mob.setPosCase(cx, cy, xr, yr);
        }

        static void ApplyPuppetVelocity(Mob mob, in PuppetPose pose, bool hasGravity, bool airborne, bool setY)
        {
            try
            {
                if (!HasLocalQueuedOrChargingSkill(mob) && !IsClientNetworkAttackActive(mob))
                    mob.dx = System.Math.Clamp(pose.Dx, -PuppetMaxVelocityRaw, PuppetMaxVelocityRaw);

                if (!setY)
                    return;

                if (!hasGravity || airborne)
                {
                    mob.dy = System.Math.Clamp(pose.Dy, -PuppetMaxVelocityRaw, PuppetMaxVelocityRaw);
                    mob.bdy = 0;
                    return;
                }

                mob.dy = 0;
                mob.bdy = 0;
                mob.fallStartY = pose.Y;
            }
            catch
            {
            }
        }
    }
}

/*
 * ═══════════════════════════════════════
 *  MOB PUPPETS — КАК РАБОТАЕТ
 * ═══════════════════════════════════════
 *  Клиент, обычные мобы (боссы — старый путь), настройка MobPuppets.
 *  Хост штампует MOBMOVE/MOBSTATE своим временем (мс Stopwatch от старта процесса + 1).
 *  Приём: каждая принятая позиция → MobPuppetTrack моба (x, y, dir, dx, dy, анимация, разрыв = ForcePositionSnap).
 *  Часы: NetNode.MobClock (общие для всех мобов), время рендера = сейчас хоста − адаптивная задержка.
 *  fixedUpdate клиента: ванильный шаг физики идёт (onLand, стены, скиллы реплики живы), затем поза из буфера:
 *   X — всегда из буфера + локальный отброс (PuppetImpulseOffset, держится RTT + задержка, гаснет 120 мс);
 *   Y — летуны и прыжок хоста из буфера; на земле — локальная физика, правка если расхождение > 6 px;
 *   приземление физикой раньше буфера (dy был > 0.08, стал 0) → Y больше не тянем до земли/нового прыжка,
 *   иначе onLand сработал бы дважды.
 *  Позиция ставится через setPosCase c yr ∈ (0, 1]: стоящий моб (yr = 1) не уезжает в клетку пола.
 *  dx = хоста (кроме локального скилла/сетевой атаки), dy = хоста в воздухе, 0 при правке на земле.
 *  Анимация и dir — из того же сэмпла, что и позиция (postUpdate берёт Anim куклы вместо последней цели).
 *  Атака (attack|…|hostMs): ждёт, пока время рендера дойдёт до hostMs (≤ 300 мс, ≤ 128 в очереди),
 *  атаки одного моба выходят по порядку; замах, хитбокс и позиция куклы совпадают с картинкой хоста.
 *  Нет часов или сэмплов → старый ApplyInterpolatedState.
 * ═══════════════════════════════════════
*/
