using dc;
using dc.en;
using dc.pr;
using dc.tool;
using dc.ui;
using DeadCellsMultiplayerMod.PortableCore;
using HaxeProxy.Runtime;
using ModCore.Utilities;

namespace DeadCellsMultiplayerMod;

internal static class ScrollChoiceSync
{
    const long ConfigTickIntervalMs = 250;
    const long MirrorCloseDelayMs = 450;
    const long RemotePickTimeoutMs = 180_000;
    const double MirrorAlpha = 0.72;

    sealed class RemotePick
    {
        public int PlayerId;
        public int Seq;
        public string KindId = string.Empty;
        public int Index;
        public long StartedAt;
        public bool MirrorFailed;
    }

    static bool _installed;
    static ScrollChoiceMode? _hostMode;
    static ScrollChoiceMode? _sentMode;
    static long _nextConfigTickAt;

    static int _localSeq;
    static TierSelect? _localSelect;
    static bool _localOpenSent;
    static int _localSentIndex = -1;

    static readonly Dictionary<int, RemotePick> _remotePicks = new();
    static readonly List<int> _scratchIds = new();

    static TierSelect? _mirror;
    static int _mirrorPlayerId = -1;
    static int _mirrorSeq;
    static long _mirrorCloseAt;
    static bool _creatingMirror;
    static bool _applyingRemote;

    static bool _resumeOwed;
    static bool _syncResuming;

    public static void Install()
    {
        if (_installed)
            return;
        Hook__TierSelect.__constructor__ += Hook_TierSelect_ctor;
        Hook_TierSelect.select += Hook_TierSelect_select;
        Hook_TierSelect.validate += Hook_TierSelect_validate;
        Hook_TierSelect.onDispose += Hook_TierSelect_onDispose;
        Hook_Game.resume += Hook_Game_resume;
        _installed = true;
    }

    public static ScrollChoiceMode HostMode => ScrollChoiceWire.ParseMode(MultiplayerSettingsStorage.ScrollChoiceMode);

    public static ScrollChoiceMode SessionMode
    {
        get
        {
            var net = LobbySession.NetRef;
            if (net != null && net.IsAlive && !net.IsHost)
                return _hostMode ?? ScrollChoiceMode.Off;
            return HostMode;
        }
    }

    public static bool HoldsPause =>
        SessionMode != ScrollChoiceMode.Off &&
        IsCoopActive(out _) &&
        (_localSelect != null || _remotePicks.Count > 0 || _mirror != null);

    public static void SetHostMode(ScrollChoiceMode mode)
    {
        MultiplayerSettingsStorage.ScrollChoiceMode = ScrollChoiceWire.ToWire(mode);
        _nextConfigTickAt = 0;
    }

    public static void OnHostConfig(ScrollChoiceMode mode)
    {
        if (_hostMode != mode)
            LobbySession.Log?.Information("[ScrollChoice] Host mode {Mode}", ScrollChoiceWire.ToWire(mode));
        _hostMode = mode;
    }

    public static void OnRemoteConnected()
    {
        _sentMode = null;
        _nextConfigTickAt = 0;
    }

    public static void OnRemoteOpen(ScrollOpenMessage message)
    {
        if (_remotePicks.TryGetValue(message.PlayerId, out var existing) && existing.Seq > message.Seq)
            return;

        if (existing != null && _mirrorPlayerId == message.PlayerId)
            CloseMirror();

        _remotePicks[message.PlayerId] = new RemotePick
        {
            PlayerId = message.PlayerId,
            Seq = message.Seq,
            KindId = message.KindId,
            Index = message.Index,
            StartedAt = Environment.TickCount64
        };
        LobbySession.Log?.Information(
            "[ScrollChoice] Player {Id} opened {Kind} seq={Seq}",
            message.PlayerId,
            message.KindId,
            message.Seq);
    }

    public static void OnRemoteSelect(ScrollIndexMessage message)
    {
        if (!_remotePicks.TryGetValue(message.PlayerId, out var pick) || pick.Seq != message.Seq)
            return;

        pick.Index = message.Index;
        if (_mirror != null && _mirrorPlayerId == message.PlayerId && _mirrorSeq == message.Seq && _mirrorCloseAt == 0)
            ApplyMirrorIndex(message.Index);
    }

    public static void OnRemoteEnd(ScrollIndexMessage message)
    {
        if (!_remotePicks.TryGetValue(message.PlayerId, out var pick) || pick.Seq != message.Seq)
            return;

        _remotePicks.Remove(message.PlayerId);
        LobbySession.Log?.Information(
            "[ScrollChoice] Player {Id} closed seq={Seq} choice={Index}",
            message.PlayerId,
            message.Seq,
            message.Index);

        if (_mirror == null || _mirrorPlayerId != message.PlayerId || _mirrorSeq != message.Seq)
            return;

        if (message.Index < 0)
        {
            CloseMirror();
            return;
        }

        ApplyMirrorIndex(message.Index);
        _mirrorCloseAt = Environment.TickCount64 + MirrorCloseDelayMs;
    }

    public static void Tick()
    {
        if (!IsCoopActive(out var net))
        {
            if (_localSelect != null || _remotePicks.Count > 0 || _mirror != null || _resumeOwed)
                ResetState();
            _hostMode = null;
            _sentMode = null;
            return;
        }

        var now = Environment.TickCount64;
        TickHostConfig(net, now);
        ExpireRemotePicks(net, now);
        TickMirror(now);
        EnforcePause();
    }

    static bool IsCoopActive(out NetNode net)
    {
        net = LobbySession.NetRef!;
        if (net == null || !net.IsAlive)
            return false;
        return !net.IsHost || NetNode.ConnectedClientCount > 0;
    }

    static void TickHostConfig(NetNode net, long now)
    {
        if (!net.IsHost || now < _nextConfigTickAt)
            return;
        _nextConfigTickAt = now + ConfigTickIntervalMs;

        var mode = HostMode;
        if (_sentMode == mode)
            return;

        _sentMode = mode;
        net.SendScrollConfig(ScrollChoiceWire.BuildConfigLine(mode));
        LobbySession.Log?.Information("[ScrollChoice] Sent mode {Mode}", ScrollChoiceWire.ToWire(mode));
    }

    static void ExpireRemotePicks(NetNode net, long now)
    {
        if (_remotePicks.Count == 0)
            return;

        _scratchIds.Clear();
        foreach (var pick in _remotePicks.Values)
        {
            var timedOut = now - pick.StartedAt > RemotePickTimeoutMs;
            var gone = net.IsHost && !net.IsRemotePresent(pick.PlayerId);
            if (!timedOut && !gone)
                continue;

            _scratchIds.Add(pick.PlayerId);
            if (gone)
                net.SendScrollEndOnBehalf(pick.PlayerId, pick.Seq);
            LobbySession.Log?.Warning(
                "[ScrollChoice] Dropped pick of player {Id} seq={Seq} ({Reason})",
                pick.PlayerId,
                pick.Seq,
                gone ? "disconnected" : "timeout");
        }

        foreach (var id in _scratchIds)
            _remotePicks.Remove(id);
        _scratchIds.Clear();
    }

    static void TickMirror(long now)
    {
        if (_mirror != null)
        {
            var dead = SafeRead(() => _mirror.destroyed, true);
            var pickGone = _mirrorCloseAt == 0 && !_remotePicks.ContainsKey(_mirrorPlayerId);
            var due = _mirrorCloseAt > 0 && now >= _mirrorCloseAt;
            var disabled = _mirrorCloseAt == 0 && SessionMode != ScrollChoiceMode.PauseAndMirror;
            if (dead || pickGone || due || disabled)
                CloseMirror();
            return;
        }

        if (SessionMode == ScrollChoiceMode.PauseAndMirror)
            TryOpenMirror();
    }

    static void TryOpenMirror()
    {
        if (_localSelect != null || _remotePicks.Count == 0)
            return;

        RemotePick? pick = null;
        foreach (var candidate in _remotePicks.Values)
        {
            if (candidate.MirrorFailed)
                continue;
            if (pick == null || candidate.StartedAt < pick.StartedAt)
                pick = candidate;
        }
        if (pick == null)
            return;

        var hero = ModEntry.me;
        var game = Game.Class.ME;
        if (hero == null || game == null ||
            SafeRead(() => hero.destroyed || hero.life <= 0 || game.destroyed || game.hasCinematic(), true))
            return;

        TierSelect? mirror = null;
        _creatingMirror = true;
        try
        {
            var item = InventItem.Class.fromItem(pick.KindId.AsHaxeString(), null);
            if (item != null)
                mirror = new TierSelect(hero, hero, item);
        }
        catch (Exception ex)
        {
            LobbySession.Log?.Warning("[ScrollChoice] Mirror for {Kind} failed: {Message}", pick.KindId, ex.Message);
        }
        finally
        {
            _creatingMirror = false;
        }

        if (mirror == null)
        {
            pick.MirrorFailed = true;
            return;
        }

        _mirror = mirror;
        _mirrorPlayerId = pick.PlayerId;
        _mirrorSeq = pick.Seq;
        _mirrorCloseAt = 0;
        DecorateMirror(mirror, pick.PlayerId);
        ApplyMirrorIndex(pick.Index);
    }

    static void DecorateMirror(TierSelect mirror, int playerId)
    {
        try
        {
            if (mirror.controller != null)
                mirror.controller.manualLock = true;
            if (mirror.root != null)
                mirror.root.alpha = MirrorAlpha;

            var name = ResolvePlayerName(playerId);
            mirror.title?.set_text(LobbySession.FormatLocalized("{0} is choosing...", name).AsHaxeString());
        }
        catch (Exception ex)
        {
            LobbySession.Log?.Warning("[ScrollChoice] Mirror decoration failed: {Message}", ex.Message);
        }
    }

    static string ResolvePlayerName(int playerId)
    {
        var net = LobbySession.NetRef;
        if (net != null && net.TryGetRemoteUsername(playerId, out var name) && !string.IsNullOrWhiteSpace(name))
            return name!;
        return LobbySession.Localize(playerId == 1 ? "Host" : "Player");
    }

    static void ApplyMirrorIndex(int index)
    {
        var mirror = _mirror;
        if (mirror == null || index < 0)
            return;

        try
        {
            var count = mirror.tiers?.length ?? 0;
            if (index >= count || mirror.curTierId == index)
                return;

            _applyingRemote = true;
            var instant = false;
            mirror.select(index, Ref<bool>.From(ref instant));
        }
        catch (Exception ex)
        {
            LobbySession.Log?.Warning("[ScrollChoice] Mirror select failed: {Message}", ex.Message);
        }
        finally
        {
            _applyingRemote = false;
        }
    }

    static void CloseMirror()
    {
        var mirror = _mirror;
        _mirror = null;
        _mirrorPlayerId = -1;
        _mirrorSeq = 0;
        _mirrorCloseAt = 0;
        if (mirror == null)
            return;

        try
        {
            if (!mirror.destroyed)
                mirror.destroy();
        }
        catch
        {
        }
    }

    static void EnforcePause()
    {
        var game = Game.Class.ME;
        if (game == null || SafeRead(() => game.destroyed, true))
        {
            _resumeOwed = false;
            return;
        }

        if (HoldsPause)
        {
            _resumeOwed = true;
            if (!game.paused)
                game.modalPause(Ref<bool>.Null);
            return;
        }

        if (!_resumeOwed)
            return;

        _resumeOwed = false;
        if (!game.paused)
            return;

        _syncResuming = true;
        try
        {
            game.resume();
        }
        finally
        {
            _syncResuming = false;
        }
    }

    static void ResetState()
    {
        CloseMirror();
        _remotePicks.Clear();
        _localSelect = null;
        _localOpenSent = false;
        _localSentIndex = -1;

        var owed = _resumeOwed;
        _resumeOwed = false;
        var game = Game.Class.ME;
        if (!owed || game == null || SafeRead(() => game.destroyed || !game.paused, true))
            return;

        _syncResuming = true;
        try
        {
            game.resume();
        }
        catch
        {
        }
        finally
        {
            _syncResuming = false;
        }
    }

    static void Hook_TierSelect_ctor(Hook__TierSelect.orig___constructor__ orig, TierSelect self, Hero hero, Entity sourceEnt, InventItem i)
    {
        if (_creatingMirror || self is TierSelectFree || SessionMode == ScrollChoiceMode.Off || !IsCoopActive(out var net))
        {
            orig(self, hero, sourceEnt, i);
            return;
        }

        CloseMirror();
        _localSeq++;
        _localSelect = self;
        _localOpenSent = false;
        _localSentIndex = -1;

        orig(self, hero, sourceEnt, i);

        try
        {
            var kindId = i?.getItemKind()?.ToString();
            var index = self.curTierId;
            _localSentIndex = index;
            if (kindId != null && ScrollChoiceWire.IsValidKindId(kindId))
            {
                net.SendScrollOpen(_localSeq, kindId, index);
                _localOpenSent = true;
                LobbySession.Log?.Information("[ScrollChoice] Opened {Kind} seq={Seq}", kindId, _localSeq);
            }
        }
        catch (Exception ex)
        {
            LobbySession.Log?.Warning("[ScrollChoice] Open broadcast failed: {Message}", ex.Message);
        }
    }

    static void Hook_TierSelect_select(Hook_TierSelect.orig_select orig, TierSelect self, int id, Ref<bool> instant)
    {
        if (_creatingMirror)
        {
            orig(self, id, instant);
            return;
        }

        if (ReferenceEquals(self, _mirror))
        {
            if (_applyingRemote)
                orig(self, id, instant);
            return;
        }

        orig(self, id, instant);

        if (!ReferenceEquals(self, _localSelect) || !_localOpenSent)
            return;

        var index = SafeRead(() => self.curTierId, -1);
        if (index < 0 || index == _localSentIndex)
            return;

        _localSentIndex = index;
        if (IsCoopActive(out var net))
            net.SendScrollSelect(_localSeq, index);
    }

    static void Hook_TierSelect_validate(Hook_TierSelect.orig_validate orig, TierSelect self)
    {
        if (ReferenceEquals(self, _mirror))
            return;

        if (ReferenceEquals(self, _localSelect))
            FinishLocalPick(SafeRead(() => self.curTierId, -1));

        orig(self);
    }

    static void Hook_TierSelect_onDispose(Hook_TierSelect.orig_onDispose orig, TierSelect self)
    {
        if (ReferenceEquals(self, _localSelect))
            FinishLocalPick(-1);
        if (ReferenceEquals(self, _mirror))
        {
            _mirror = null;
            _mirrorPlayerId = -1;
            _mirrorSeq = 0;
            _mirrorCloseAt = 0;
        }

        orig(self);
    }

    static void FinishLocalPick(int index)
    {
        var sent = _localOpenSent;
        var seq = _localSeq;
        _localSelect = null;
        _localOpenSent = false;
        _localSentIndex = -1;

        if (sent && IsCoopActive(out var net))
        {
            net.SendScrollEnd(seq, index);
            LobbySession.Log?.Information("[ScrollChoice] Closed seq={Seq} choice={Index}", seq, index);
        }
    }

    static void Hook_Game_resume(Hook_Game.orig_resume orig, Game self)
    {
        if (!_syncResuming && HoldsPause)
        {
            _resumeOwed = true;
            return;
        }

        orig(self);
    }

    static T SafeRead<T>(Func<T> getter, T fallback)
    {
        try
        {
            return getter();
        }
        catch
        {
            return fallback;
        }
    }
}

/*
 * ═══════════════════════════════════════
 *  SCROLL CHOICE SYNC — КАК РАБОТАЕТ
 * ═══════════════════════════════════════
 *  Режим (off / pause / mirror) задаёт хост, клиенты получают его через SCROLLCFG.
 *  Свой TierSelect: хук конструктора шлёт SCROLLOPEN, select → SCROLLSEL, validate/onDispose → SCROLLEND.
 *  Пока кто-то выбирает, HoldsPause = true: Tick ставит Game на modalPause, хук resume
 *  не даёт снять паузу раньше времени, после последнего выбора resume делаем сами.
 *  Зеркало: обычный TierSelect по id свитка (InventItem.fromItem), ввод заблокирован
 *  (manualLock + хуки select/validate), alpha 0.72, заголовок «<ник> выбирает...».
 *  Выбор применяется только у выбирающего; зеркало закрывается destroy() через 450 мс.
 *  Ушедшего игрока хост закрывает сам, любой выбор живёт не дольше 3 минут.
 * ═══════════════════════════════════════
*/
