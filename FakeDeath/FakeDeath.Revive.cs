using System;
using System.Diagnostics;
using dc.en;
using DeadCellsMultiplayerMod.MultiplayerModUI.lifeUI;
using DeadCellsMultiplayerMod.PortableCore;
using HaxeProxy.Runtime;
using ModCore.Utilities;

namespace DeadCellsMultiplayerMod
{
    public partial class ModEntry
    {
        const int MaxRevivesPerLevel = 1;
        const double ReviveKeepDistancePx = 84.0;
        const double ReviveConfirmTimeoutSeconds = 2.5;
        const double ReviveImmunitySeconds = 2.0;
        const double ReviveRiseLockSeconds = 0.5;
        const double RiseAnimSpeed = 0.75;
        const double DownedAllDownGraceSeconds = 1.25;
        const double CrossLevelAllDownGraceSeconds = 4.0;
        const double NoFlaskRetrySeconds = 0.5;
        const double NoFlaskMessageCooldownSeconds = 3.0;
        const int ReviveFxColor = 0x5fd7ff;
        const int ImmuneAffectId = 28;

        int _localRevivesThisLevel;
        bool _localDeadForLevel;
        bool _localDownAnnouncePending;
        string _reviveBudgetLevelId = string.Empty;
        dc.pr.Game? _reviveBudgetGame;
        int _incomingHoldReviverId;
        long _incomingHoldStartedTicks;
        int _pendingReviveTargetId;
        long _pendingReviveSentTicks;
        long _nextNoFlaskMessageTicks;

        static bool IsPassageLevelId(string? levelId) =>
            !string.IsNullOrEmpty(levelId) && levelId.StartsWith("T_", StringComparison.Ordinal);

        static bool IsSameLevelOrUnknown(string? a, string? b) =>
            string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b) || string.Equals(a, b, StringComparison.Ordinal);

        void ResetReviveBudget(string reason)
        {
            if (_localRevivesThisLevel > 0)
                Logger.Information("[NetMod][Revive] revive budget reset ({Reason})", reason);
            _localRevivesThisLevel = 0;
            _reviveBudgetLevelId = string.Empty;
            _reviveBudgetGame = null;
        }

        void ResetReviveTransientState()
        {
            _localDeadForLevel = false;
            _localDownAnnouncePending = false;
            _incomingHoldReviverId = 0;
            _incomingHoldStartedTicks = 0;
            _pendingReviveTargetId = 0;
            _pendingReviveSentTicks = 0;
            ReviveVisuals.Clear();
        }

        void UpdateReviveBudgetAfterLevelChange(Hero? hero, string levelId, bool wasDowned, bool wasDead)
        {
            if (hero == null)
                return;

            dc.pr.Game? game = null;
            var isSubLevel = false;
            try { game = hero._level?.game; } catch { }
            try { isSubLevel = hero._level?.isSubLevel ?? false; } catch { }

            if (game != null && !ReferenceEquals(game, _reviveBudgetGame))
            {
                ResetReviveBudget("new run");
                _reviveBudgetGame = game;
            }

            if (!string.IsNullOrWhiteSpace(levelId))
            {
                if (IsPassageLevelId(levelId))
                {
                    ResetReviveBudget("passage " + levelId);
                    _reviveBudgetGame = game;
                }
                else if (!isSubLevel && !string.Equals(levelId, _reviveBudgetLevelId, StringComparison.Ordinal))
                {
                    ResetReviveBudget("level " + levelId);
                    _reviveBudgetGame = game;
                    _reviveBudgetLevelId = levelId;
                }
            }

            if (!wasDowned)
                return;

            var net = _net;
            if (net != null && net.IsAlive && _netRole != NetRole.None)
            {
                double x = 0, y = 0;
                try { x = hero.spr?.x ?? 0; y = hero.spr?.y ?? 0; } catch { }
                try { net.SendPlayerDownState(false, x, y, levelId); } catch { }
            }

            Logger.Information("[NetMod][Revive] arrived at {LevelId} alive (wasDead={WasDead})", levelId, wasDead);
            if (wasDead)
                MultiplayerUI.PushSystemMessage(Localize("You are back in the fight."), 5.0, 1.0);
        }

        void ConsumeReviveWireMessages(NetNode net)
        {
            var localId = net.id;
            if (net.TryConsumeReviveHolds(out var holds))
            {
                try
                {
                    var now = Stopwatch.GetTimestamp();
                    for (int i = 0; i < holds.Count; i++)
                        ApplyReviveHold(holds[i], localId, now);
                }
                finally
                {
                    NetNode.ReleaseConsumedList(holds);
                }
            }

            if (net.TryConsumeReviveDones(out var dones))
            {
                try
                {
                    for (int i = 0; i < dones.Count; i++)
                        ApplyReviveDone(dones[i], localId);
                }
                finally
                {
                    NetNode.ReleaseConsumedList(dones);
                }
            }
        }

        void ApplyReviveHold(ReviveHoldMessage hold, int localId, long now)
        {
            if (hold.ReviverId == localId)
                return;

            if (hold.TargetId == localId)
            {
                if (!_localFakeDead || _localDeadForLevel)
                    return;
                if (hold.Active)
                {
                    _incomingHoldReviverId = hold.ReviverId;
                    _incomingHoldStartedTicks = now;
                }
                else if (_incomingHoldReviverId == hold.ReviverId)
                {
                    _incomingHoldReviverId = 0;
                    _incomingHoldStartedTicks = 0;
                }
                return;
            }

            if (!_remoteDowned.TryGetValue(hold.TargetId, out var state) || state == null || state.IsDead)
                return;
            if (hold.Active)
            {
                state.HoldReviverId = hold.ReviverId;
                state.HoldStartedTicks = now;
            }
            else if (state.HoldReviverId == hold.ReviverId)
            {
                state.HoldReviverId = 0;
                state.HoldStartedTicks = 0;
            }
        }

        void ApplyReviveDone(ReviveDoneMessage done, int localId)
        {
            if (done.TargetId == localId)
                return;

            ReviveVisuals.CompleteChannel(done.TargetId);
            if (done.ReviverId == localId)
            {
                if (_pendingReviveTargetId == done.TargetId)
                {
                    _pendingReviveTargetId = 0;
                    _pendingReviveSentTicks = 0;
                }

                var spent = me != null && TryConsumeOneFlask(me);
                Logger.Information("[NetMod][Revive] #{TargetId} confirmed revive, flask spent={Spent}", done.TargetId, spent);
            }

            var level = me?._level;
            if (level == null || !IsSameLevelOrUnknown(GetCurrentLevelId(), done.LevelId))
                return;

            ReviveVisuals.PlayReviveBurst(level, done.X, done.Y);
            if (TryGetClientIndex(localId, done.TargetId, out var idx))
            {
                var ghost = clients[idx];
                if (ghost != null)
                {
                    try { level.fx?.revive(ghost, ReviveFxColor); } catch { }
                }
            }
        }

        void ProcessReviveHold(NetNode net)
        {
            var hero = me;
            var now = Stopwatch.GetTimestamp();
            ExpirePendingRevive(now);

            if (hero == null || _remoteDowned.Count == 0 || !ReviveInput.IsReviveHoldInputDown(hero))
            {
                CancelReviveHold(net);
                return;
            }

            var target = _reviveHoldTargetId > 0
                ? FindDownedInRange(_reviveHoldTargetId, ReviveKeepDistancePx)
                : FindNearestReviveTarget();
            if (target == null || target.IsDead)
            {
                CancelReviveHold(net);
                return;
            }

            if (_pendingReviveTargetId == target.UserId)
                return;

            if (_reviveHoldTargetId != target.UserId)
            {
                CancelReviveHold(net);
                if (_nextReviveAttemptTicks != 0 && now < _nextReviveAttemptTicks)
                    return;

                if (!HasFlaskCharge(hero))
                {
                    if (now >= _nextNoFlaskMessageTicks)
                    {
                        Logger.Information("[NetMod][Revive] cannot revive #{TargetId}: no flask charges", target.UserId);
                        MultiplayerUI.PushSystemMessage(Localize("You need a health flask charge to revive."), 4.0, 1.0);
                        _nextNoFlaskMessageTicks = now + (long)(Stopwatch.Frequency * NoFlaskMessageCooldownSeconds);
                    }
                    _nextReviveAttemptTicks = now + (long)(Stopwatch.Frequency * NoFlaskRetrySeconds);
                    return;
                }

                _reviveHoldTargetId = target.UserId;
                _reviveHoldStartedTicks = now;
                net.SendReviveHold(target.UserId, true);
                return;
            }

            if (HoldRatio(_reviveHoldStartedTicks, now) < 1.0)
                return;

            if (!HasFlaskCharge(hero))
            {
                CancelReviveHold(net);
                MultiplayerUI.PushSystemMessage(Localize("You need a health flask charge to revive."), 4.0, 1.0);
                _nextNoFlaskMessageTicks = now + (long)(Stopwatch.Frequency * NoFlaskMessageCooldownSeconds);
                _nextReviveAttemptTicks = now + (long)(Stopwatch.Frequency * NoFlaskRetrySeconds);
                return;
            }

            Logger.Information("[NetMod][Revive] sent revive request to #{TargetId} level={LevelId}", target.UserId, GetCurrentLevelId());
            net.SendPlayerReviveRequest(target.UserId);
            _pendingReviveTargetId = target.UserId;
            _pendingReviveSentTicks = now;
            _reviveHoldTargetId = 0;
            _reviveHoldStartedTicks = 0;
            _nextReviveAttemptTicks = now + (long)(Stopwatch.Frequency * ReviveAttemptCooldownSeconds);
        }

        void CancelReviveHold(NetNode? net)
        {
            if (_reviveHoldTargetId > 0)
            {
                try { net?.SendReviveHold(_reviveHoldTargetId, false); } catch { }
            }
            ResetReviveHold();
        }

        void ExpirePendingRevive(long now)
        {
            if (_pendingReviveTargetId <= 0)
                return;

            var stillDowned = _remoteDowned.TryGetValue(_pendingReviveTargetId, out var state) && state != null && !state.IsDead;
            var timedOut = now - _pendingReviveSentTicks > (long)(Stopwatch.Frequency * ReviveConfirmTimeoutSeconds);
            if (stillDowned && !timedOut)
                return;

            if (stillDowned)
            {
                Logger.Warning("[NetMod][Revive] #{TargetId} did not confirm the revive", _pendingReviveTargetId);
                MultiplayerUI.PushSystemMessage(Localize("Revive failed. Get closer and try again."), 4.0, 1.0);
            }
            _pendingReviveTargetId = 0;
            _pendingReviveSentTicks = 0;
        }

        double HoldRatio(long startedTicks, long now)
        {
            if (startedTicks == 0)
                return 0.0;
            var elapsed = (now - startedTicks) / (double)Stopwatch.Frequency;
            return System.Math.Clamp(elapsed / ReviveHoldSeconds, 0.0, 1.0);
        }

        bool IsHoldDisplayExpired(long startedTicks, long now) =>
            startedTicks == 0 ||
            now - startedTicks > (long)(Stopwatch.Frequency * (ReviveHoldSeconds + ReviveConfirmTimeoutSeconds));

        RemoteDownedState? FindDownedInRange(int userId, double maxDistancePx)
        {
            if (me == null || !_remoteDowned.TryGetValue(userId, out var state) || state == null)
                return null;
            if (!IsSameLevelOrUnknown(GetCurrentLevelId(), state.LevelId))
                return null;

            var dx = state.X - (me.spr?.x ?? 0);
            var dy = state.Y - (me.spr?.y ?? 0);
            return dx * dx + dy * dy <= maxDistancePx * maxDistancePx ? state : null;
        }

        RemoteDownedState? FindNearestDownedInRange(double maxDistancePx, bool includeDead)
        {
            if (me == null || _remoteDowned.Count == 0)
                return null;

            var localLevelId = GetCurrentLevelId();
            var x = me.spr?.x ?? 0;
            var y = me.spr?.y ?? 0;
            var bestDistSq = maxDistancePx * maxDistancePx;
            RemoteDownedState? nearest = null;
            foreach (var state in _remoteDowned.Values)
            {
                if (state == null || state.UserId <= 0 || (!includeDead && state.IsDead))
                    continue;
                if (!IsSameLevelOrUnknown(localLevelId, state.LevelId))
                    continue;

                var dx = state.X - x;
                var dy = state.Y - y;
                var distSq = dx * dx + dy * dy;
                if (distSq <= bestDistSq)
                {
                    bestDistSq = distSq;
                    nearest = state;
                }
            }

            return nearest;
        }

        bool HasCrossLevelDownedTeammate()
        {
            if (_remoteDowned.Count == 0)
                return false;
            var localLevelId = GetCurrentLevelId();
            foreach (var state in _remoteDowned.Values)
            {
                if (state != null && !IsSameLevelOrUnknown(localLevelId, state.LevelId))
                    return true;
            }
            return false;
        }

        static bool HasFlaskCharge(Hero hero)
        {
            try
            {
                var heal = hero.mainSkillsManager?.getMainSkill(dc.tool.mainSkills.Heal.Class) as dc.tool.mainSkills.Heal;
                return heal != null && heal.get_healings() > 0;
            }
            catch
            {
                return false;
            }
        }

        bool TryGetLocalCorpseFeet(out double x, out double y)
        {
            if (_localDeadCine != null && _localDeadCine.TryGetCorpsePixelPosition(out x, out y) &&
                double.IsFinite(x) && double.IsFinite(y))
                return true;

            x = _localDownedX;
            y = _localDownedY;
            return double.IsFinite(x) && double.IsFinite(y) && (x != 0 || y != 0);
        }

        bool TryGetRemoteFeet(NetNode net, int userId, out double x, out double y)
        {
            x = 0;
            y = 0;
            if (me == null || !TryGetClientIndex(net.id, userId, out var idx))
                return false;

            var ghost = clients[idx];
            if (ghost == null)
                return false;

            try
            {
                if (ghost.destroyed || ghost.spr == null || !ReferenceEquals(ghost._level, me._level))
                    return false;
                x = ghost.spr.x;
                y = ghost.spr.y;
                return double.IsFinite(x) && double.IsFinite(y);
            }
            catch
            {
                return false;
            }
        }

        void AnnounceLocalDownIfPending()
        {
            if (!_localDownAnnouncePending || me == null)
                return;
            _localDownAnnouncePending = false;

            if (_localDeadForLevel)
            {
                MultiplayerUI.PushSystemMessage(Localize("You died. You will be back at the next passage."), 7.0, 1.0);
                if (TryGetLocalCorpseFeet(out var x, out var y))
                    ReviveVisuals.PlayDeathBurst(me._level, x, y);
            }
            else
            {
                MultiplayerUI.PushSystemMessage(Localize("You are down! A teammate can revive you."), 5.0, 1.0);
            }
        }

        void ApplyLocalReviveEffects(Hero hero, NetNode net, int reviverId, double x, double y)
        {
            _localRevivesThisLevel++;
            _incomingHoldReviverId = 0;
            _incomingHoldStartedTicks = 0;

            try { hero.setAffectS(ImmuneAffectId, ReviveImmunitySeconds, Ref<double>.Null, null); } catch { }
            PlayRiseAnimation(hero);
            try { hero.lockControlsS(ReviveRiseLockSeconds); } catch { }

            var level = hero._level;
            ReviveVisuals.CompleteChannel(net.id);
            ReviveVisuals.PlayReviveBurst(level, x, y);
            try { level?.fx?.revive(hero, ReviveFxColor); } catch { }

            net.SendReviveDone(reviverId, x, y, GetCurrentLevelId());
            Logger.Information(
                "[NetMod][Revive] revived by #{ReviverId}, revives used {Used}/{Max} in {LevelId}",
                reviverId,
                _localRevivesThisLevel,
                MaxRevivesPerLevel,
                string.IsNullOrEmpty(_reviveBudgetLevelId) ? GetCurrentLevelId() : _reviveBudgetLevelId);

            MultiplayerUI.PushSystemMessage(
                _localRevivesThisLevel >= MaxRevivesPerLevel
                    ? Localize("Revived! Your next fall in this level is final.")
                    : Localize("Revived!"),
                5.0,
                1.0);
        }

        static void PlayRiseAnimation(Hero hero)
        {
            try
            {
                var anim = hero.spr?._animManager?.play("rise".AsHaxeString(), null, null);
                var stack = anim?.stack;
                if (stack == null || stack.length <= 0)
                    return;
                if (stack.getDyn(stack.length - 1) is dc.libs.heaps.slib._AnimManager.AnimInstance top)
                    top.speed = RiseAnimSpeed;
            }
            catch
            {
            }
        }

        void UpdateReviveVisuals(NetNode net)
        {
            var hero = me;
            var level = hero?._level;
            if (hero == null || level == null)
                return;

            var now = Stopwatch.GetTimestamp();
            var localLevelId = GetCurrentLevelId();

            if (_localFakeDead && TryGetLocalCorpseFeet(out var corpseX, out var corpseY))
            {
                if (_localDeadForLevel)
                {
                    ReviveVisuals.ShowDeathMark(net.id, level, corpseX, corpseY);
                }
                else if (_incomingHoldReviverId > 0)
                {
                    if (IsHoldDisplayExpired(_incomingHoldStartedTicks, now))
                    {
                        _incomingHoldReviverId = 0;
                        _incomingHoldStartedTicks = 0;
                    }
                    else
                    {
                        var ratio = HoldRatio(_incomingHoldStartedTicks, now);
                        var hasSource = TryGetRemoteFeet(net, _incomingHoldReviverId, out var sx, out var sy);
                        ReviveVisuals.ShowChannel(net.id, level, corpseX, corpseY, ratio, ratio >= 1.0, hasSource, sx, sy);
                    }
                }
            }

            if (_remoteDowned.Count == 0)
                return;

            var heroX = hero.spr?.x ?? 0;
            var heroY = hero.spr?.y ?? 0;
            foreach (var state in _remoteDowned.Values)
            {
                if (state == null || state.UserId <= 0 || !IsSameLevelOrUnknown(localLevelId, state.LevelId))
                    continue;

                if (state.IsDead)
                {
                    ReviveVisuals.ShowDeathMark(state.UserId, level, state.X, state.Y);
                    continue;
                }

                if (!_localFakeDead && state.UserId == _reviveHoldTargetId)
                {
                    ReviveVisuals.ShowChannel(state.UserId, level, state.X, state.Y, HoldRatio(_reviveHoldStartedTicks, now), false, true, heroX, heroY);
                }
                else if (!_localFakeDead && state.UserId == _pendingReviveTargetId)
                {
                    ReviveVisuals.ShowChannel(state.UserId, level, state.X, state.Y, 1.0, true, true, heroX, heroY);
                }
                else if (state.HoldReviverId > 0)
                {
                    if (IsHoldDisplayExpired(state.HoldStartedTicks, now))
                    {
                        state.HoldReviverId = 0;
                        state.HoldStartedTicks = 0;
                        continue;
                    }

                    var ratio = HoldRatio(state.HoldStartedTicks, now);
                    var hasSource = TryGetRemoteFeet(net, state.HoldReviverId, out var sx, out var sy);
                    ReviveVisuals.ShowChannel(state.UserId, level, state.X, state.Y, ratio, ratio >= 1.0, hasSource, sx, sy);
                }
            }
        }
    }
}

/*
 * ═══════════════════════════════════════
 *  REVIVE — КАК РАБОТАЕТ
 * ═══════════════════════════════════════
 *  Бюджет: 1 подъём на уровень. Сброс — вход в переход T_* (магазин клеток), новый не-подуровень
 *    с другим id или новый Game (рестарт/новый забег). Подуровни бюджет не трогают.
 *  Второе падение в том же уровне → мёртв до перехода (PDOWN state 2): подъём недоступен,
 *    череп над телом, PREVIVE игнорируется. Выход с напарником → в T_* сброс fake-death,
 *    игрок жив, остальным уходит PDOWN «встал».
 *  Удержание: старт только с зарядом фляги, RVHOLD 1/0 другим для полосы; ReviveHoldSeconds →
 *    PREVIVE и ожидание RVDONE до 2.5 с (полоса пульсирует). Фляга тратится только по RVDONE,
 *    нет ответа → сообщение, фляга цела. Держать можно в радиусе 84 px от тела.
 *  Поднятый: Immune (affect 28) 2 с, анимация "rise", лок управления 0.5 с, вспышка и Fx.revive.
 *  Все лежат или мертвы (в т.ч. на разных уровнях) → общий рестарт; с лежащим на другом
 *    уровне пауза 4 с вместо 1.25 с, чтобы успело прийти «встал» после перехода.
 * ═══════════════════════════════════════
*/
