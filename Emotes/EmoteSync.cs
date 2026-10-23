using System.Globalization;
using dc;
using dc.en;
using dc.hl.types;
using dc.libs.heaps.slib;
using dc.pr;
using dc.tool;
using DeadCellsMultiplayerMod.Ghost.GhostBase;
using DeadCellsMultiplayerMod.MultiplayerModUI.Connection;
using DeadCellsMultiplayerMod.PortableCore;
using HaxeProxy.Runtime;
using ModCore.Utilities;

namespace DeadCellsMultiplayerMod.Emotes;

internal static class EmoteSync
{
    const int KeyT = 84;
    const int KeyW = 87;
    const int KeyA = 65;
    const int KeyS = 83;
    const int KeyD = 68;
    const int KeyLeft = 37;
    const int KeyUp = 38;
    const int KeyRight = 39;
    const int KeyDown = 40;

    const int PadLeftStickPush = 10;
    const int PadLeftXNeg = 17;
    const int PadLeftXPos = 18;
    const int PadLeftYNeg = 20;
    const int PadLeftYPos = 21;
    const int PadRightXNeg = 23;
    const int PadRightXPos = 24;
    const int PadRightYNeg = 26;
    const int PadRightYPos = 27;

    const double StickDeadZone = 0.5;
    const double StickCalibrationMin = 0.3;
    const double MouseRadiusPx = 90.0;
    const double MouseDeadZonePx = 26.0;
    const double MouseMoveEpsilonPx = 1.5;
    const double MoveCancelSpeed = 0.05;
    const long EmoteCooldownMs = 350;
    const long OnceTimeoutMs = 15_000;
    const long RemoteTimeoutMs = 120_000;
    const int LocalBubbleKey = 0;

    const string HoverSound = "sfx/ui/menu_click1.wav";
    const string FireSound = "sfx/ui/menu_select.wav";

    enum InputSource
    {
        None,
        Mouse,
        Stick,
        Keys
    }

    sealed class RemoteEmote
    {
        public int Seq;
        public EmoteDef Def = null!;
        public GhostKing? Ghost;
        public long StartedAt;
        public bool Animated;
    }

    static EmoteWheel? _wheel;
    static Hero? _wheelHero;
    static bool _wheelByPad;
    static ControllerAccess? _lockedAccess;
    static bool _keyWasDown;
    static bool _padWasDown;
    static double _pointerX;
    static double _pointerY;
    static double _lastMouseX;
    static double _lastMouseY;
    static InputSource _source;
    static bool _keysArmed;
    static bool _leftStickArmed;
    static double _padYSign = -1.0;

    static int _seq;
    static EmoteDef? _active;
    static Hero? _activeHero;
    static long _activeStartedAt;
    static long _lastEmoteAt;

    static readonly Dictionary<int, RemoteEmote> _remote = new();
    static readonly List<int> _scratchIds = new();

    public static bool SuppressesHeroAnim(string anim) => _active != null && _active.UsesAnim(anim);

    public static void Tick()
    {
        var now = Environment.TickCount64;
        var net = LobbySession.NetRef;
        if (net == null || !net.IsAlive)
        {
            if (_wheel != null)
                CloseWheel(false);
            if (_active != null)
                EndLocal(true, false);
            if (_remote.Count > 0)
                _remote.Clear();
            EmoteBubbles.Clear();
            _keyWasDown = false;
            _padWasDown = false;
            return;
        }

        TickWheel(now);
        TickLocalEmote(now);
        TickRemoteEmotes(now);
        EmoteBubbles.Tick(now);
    }

    public static void OnRemoteEmote(EmoteMessage message)
    {
        var now = Environment.TickCount64;
        var id = message.PlayerId;

        if (message.IsStop)
        {
            if (_remote.TryGetValue(id, out var current) && current.Seq == message.Seq)
            {
                _remote.Remove(id);
                EndRemote(id, current, true);
            }
            return;
        }

        var def = EmoteCatalog.Get(message.EmoteId);
        if (def == null)
            return;

        if (_remote.Remove(id, out var previous))
            EndRemote(id, previous, !message.Animate);

        ModEntry.TryGetGhost(id, out var ghost);
        var remote = new RemoteEmote
        {
            Seq = message.Seq,
            Def = def,
            Ghost = ghost,
            StartedAt = now
        };

        if (ghost != null)
        {
            if (message.Animate)
                remote.Animated = PlayEmoteAnim(ghost.spr, def);
            EmoteBubbles.Show(id, ghost, def.Icon);
        }

        _remote[id] = remote;
    }

    static void TickWheel(long now)
    {
        var keyDown = IsKeyDown(KeyT);
        var padDown = IsPadDown(PadLeftStickPush);
        var keyPressed = keyDown && !_keyWasDown;
        var padPressed = padDown && !_padWasDown;
        _keyWasDown = keyDown;
        _padWasDown = padDown;

        if (_wheel == null)
        {
            if ((keyPressed || padPressed) && CanOpenWheel(out var hero, out var access))
                OpenWheel(hero, access, padPressed && !keyPressed);
            return;
        }

        if (!CanKeepWheel())
        {
            CloseWheel(false);
            return;
        }

        _wheel.Update(now);
        UpdateHover();

        var held = _wheelByPad ? padDown : keyDown;
        if (!held)
            CloseWheel(true);
    }

    static bool CanOpenWheel(out Hero hero, out ControllerAccess access)
    {
        hero = ModEntry.me;
        access = null!;
        try
        {
            if (hero == null || hero.destroyed || hero.life <= 0 || hero._level == null)
                return false;

            var game = Game.Class.ME;
            if (game == null || game.destroyed || game.paused || game.hasCinematic())
                return false;

            if (ConnectionUI.IsTextPromptOpen())
                return false;
            var textInput = LobbySession.GetActiveTextInput();
            if (textInput != null && LobbySession.IsTextInputActive(textInput))
                return false;

            if (hero.controller is not ControllerAccess heroAccess || heroAccess.manualLock)
                return false;
            var controller = heroAccess.parent;
            if (controller == null || controller.isLocked)
                return false;
            if (HasForeignExclusive(controller, heroAccess))
                return false;

            access = heroAccess;
            return true;
        }
        catch
        {
            return false;
        }
    }

    static bool CanKeepWheel()
    {
        try
        {
            var hero = ModEntry.me;
            if (hero == null || !ReferenceEquals(hero, _wheelHero) || hero.destroyed || hero.life <= 0)
                return false;

            var game = Game.Class.ME;
            if (game == null || game.destroyed || game.paused || game.hasCinematic())
                return false;

            if (!EmoteMouse.IsGameForeground())
                return false;

            var access = _lockedAccess;
            var controller = access?.parent;
            if (access == null || controller == null || controller.isLocked)
                return false;
            return !HasForeignExclusive(controller, access);
        }
        catch
        {
            return false;
        }
    }

    static bool HasForeignExclusive(Controller controller, ControllerAccess access)
    {
        var exclusive = controller.exclusiveId?.ToString();
        return !string.IsNullOrEmpty(exclusive) &&
               !string.Equals(exclusive, access.id?.ToString(), StringComparison.Ordinal);
    }

    static void OpenWheel(Hero hero, ControllerAccess access, bool byPad)
    {
        var wheel = EmoteWheel.TryOpen();
        if (wheel == null)
            return;

        _wheel = wheel;
        _wheelHero = hero;
        _wheelByPad = byPad;
        _lockedAccess = access;
        access.manualLock = true;

        _source = InputSource.None;
        _keysArmed = !AnyDirectionKeyDown();
        _leftStickArmed = !IsLeftStickActive(access.parent);
        _pointerX = 0.0;
        _pointerY = 0.0;
        ReadMouse(out _lastMouseX, out _lastMouseY);
        EmoteMouse.Capture();
    }

    static void CloseWheel(bool fire)
    {
        var wheel = _wheel;
        var hero = _wheelHero;
        _wheel = null;
        _wheelHero = null;
        var slot = wheel?.Hovered ?? -1;
        wheel?.Dispose();
        EmoteMouse.Release();

        var access = _lockedAccess;
        _lockedAccess = null;
        if (access != null)
        {
            try
            {
                if (access.manualLock)
                    access.manualLock = false;
            }
            catch
            {
            }
        }

        if (!fire || hero == null || !ReferenceEquals(hero, ModEntry.me))
            return;

        var def = EmoteCatalog.Get(slot);
        if (def != null)
            Fire(hero, def);
    }

    static void UpdateHover()
    {
        var wheel = _wheel;
        var controller = _lockedAccess?.parent;
        if (wheel == null)
            return;

        var mouseMoved = ReadMouseDelta(out var mouseDx, out var mouseDy);
        var slot = wheel.Hovered;
        if (controller != null && TryReadSticks(controller, out var sx, out var sy))
        {
            _source = InputSource.Stick;
            slot = EmoteWheel.SlotFromVector(sx, sy);
        }
        else if (TryReadKeys(out var kx, out var ky))
        {
            _source = InputSource.Keys;
            slot = EmoteWheel.SlotFromVector(kx, ky);
        }
        else if (mouseMoved)
        {
            if (_source != InputSource.Mouse)
            {
                _pointerX = 0.0;
                _pointerY = 0.0;
            }
            _source = InputSource.Mouse;
            MovePointer(mouseDx, mouseDy);
        }

        var mouseActive = _source == InputSource.Mouse;
        if (mouseActive)
        {
            slot = _pointerX * _pointerX + _pointerY * _pointerY < MouseDeadZonePx * MouseDeadZonePx
                ? -1
                : EmoteWheel.SlotFromVector(_pointerX, _pointerY);
        }
        wheel.SetPointer(mouseActive, _pointerX / MouseRadiusPx, _pointerY / MouseRadiusPx);

        if (slot == wheel.Hovered)
            return;

        wheel.SetHovered(slot);
        if (slot >= 0)
            PlayUiSound(HoverSound, 0.35);
    }

    static void MovePointer(double dx, double dy)
    {
        _pointerX += dx;
        _pointerY += dy;
        var length = System.Math.Sqrt(_pointerX * _pointerX + _pointerY * _pointerY);
        if (length <= MouseRadiusPx)
            return;
        var k = MouseRadiusPx / length;
        _pointerX *= k;
        _pointerY *= k;
    }

    static bool ReadMouseDelta(out double dx, out double dy)
    {
        if (EmoteMouse.IsCaptured)
            return EmoteMouse.ReadDelta(out dx, out dy) && (dx != 0.0 || dy != 0.0);

        dx = 0.0;
        dy = 0.0;
        if (!ReadMouse(out var x, out var y))
            return false;
        dx = x - _lastMouseX;
        dy = y - _lastMouseY;
        _lastMouseX = x;
        _lastMouseY = y;
        return System.Math.Abs(dx) + System.Math.Abs(dy) > MouseMoveEpsilonPx;
    }

    static void Fire(Hero hero, EmoteDef def)
    {
        var now = Environment.TickCount64;
        if (now - _lastEmoteAt < EmoteCooldownMs)
            return;
        _lastEmoteAt = now;

        var net = LobbySession.NetRef;
        if (_active != null)
            EndLocal(false, true);

        _seq++;
        _active = def;
        _activeHero = hero;
        _activeStartedAt = now;

        var animated = CanAnimate(hero, def) && PlayEmoteAnim(hero.spr, def);
        if (!animated)
        {
            _active = null;
            _activeHero = null;
        }

        EmoteBubbles.Show(LocalBubbleKey, hero, def.Icon);
        PlayUiSound(FireSound, 0.5);
        if (net != null && net.IsAlive)
            net.SendEmote(_seq, def.Id, animated);
        LobbySession.Log?.Information("[Emotes] {Emote} seq={Seq} animated={Animated}", def.Label, _seq, animated);
    }

    static bool CanAnimate(Hero hero, EmoteDef def)
    {
        try
        {
            var spr = hero.spr;
            if (spr == null || System.Math.Abs(hero.dx) > MoveCancelSpeed || System.Math.Abs(hero.dy) > MoveCancelSpeed)
                return false;

            var group = spr.groupName?.ToString();
            if (group != null && ModEntry.IsHeroAttackAnim(group))
                return false;

            foreach (var step in def.Steps)
            {
                if (!HasGroup(spr, step.Group))
                    return false;
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    static bool PlayEmoteAnim(HSprite? spr, EmoteDef def)
    {
        try
        {
            var anim = spr?._animManager;
            if (spr == null || anim == null || def.Steps.Length == 0)
                return false;
            foreach (var step in def.Steps)
            {
                if (!HasGroup(spr, step.Group))
                    return false;
            }

            for (var i = 0; i < def.Steps.Length; i++)
            {
                var step = def.Steps[i];
                var played = anim.play(step.Group.AsHaxeString(), step.Plays > 0 ? step.Plays : 1, i == 0 ? null : true);
                if (step.Reverse)
                    played.reverse();
                if (step.Plays == EmoteStep.LoopForever)
                    played.loop(null);
                else if (step.Plays == EmoteStep.HoldLastFrame)
                    played.pauseOnLastFrame();
            }
            return true;
        }
        catch (Exception ex)
        {
            LobbySession.Log?.Warning("[Emotes] Anim {Emote} failed: {Message}", def.Label, ex.Message);
            return false;
        }
    }

    static void TickLocalEmote(long now)
    {
        var def = _active;
        if (def == null)
            return;

        var hero = _activeHero;
        try
        {
            if (hero == null || hero.destroyed || !ReferenceEquals(hero, ModEntry.me) || hero.spr == null)
            {
                EndLocal(true, false);
                return;
            }

            var group = hero.spr.groupName?.ToString();
            var inEmote = group != null && def.UsesAnim(group);
            var moving = System.Math.Abs(hero.dx) > MoveCancelSpeed || System.Math.Abs(hero.dy) > MoveCancelSpeed;
            var timedOut = def.EndsOnItsOwn && now - _activeStartedAt > OnceTimeoutMs;

            if (!inEmote)
                EndLocal(true, false);
            else if (moving || timedOut)
                EndLocal(true, true);
        }
        catch
        {
            EndLocal(true, false);
        }
    }

    static void EndLocal(bool sendStop, bool stopAnim)
    {
        var hero = _activeHero;
        var seq = _seq;
        _active = null;
        _activeHero = null;

        if (stopAnim && hero != null)
        {
            try
            {
                hero.spr?._animManager?.stopWithStateAnims();
            }
            catch
            {
            }
        }

        var net = LobbySession.NetRef;
        if (sendStop && net != null && net.IsAlive)
            net.SendEmoteStop(seq);
    }

    static void TickRemoteEmotes(long now)
    {
        if (_remote.Count == 0)
            return;

        _scratchIds.Clear();
        foreach (var pair in _remote)
        {
            var remote = pair.Value;
            var ghostGone = remote.Ghost != null && SafeRead(() => remote.Ghost.destroyed, true);
            var limit = remote.Def.EndsOnItsOwn ? OnceTimeoutMs : RemoteTimeoutMs;
            if (ghostGone || now - remote.StartedAt > limit)
                _scratchIds.Add(pair.Key);
        }

        foreach (var id in _scratchIds)
        {
            if (_remote.Remove(id, out var remote))
                EndRemote(id, remote, true);
        }
        _scratchIds.Clear();
    }

    static void EndRemote(int playerId, RemoteEmote remote, bool restore)
    {
        if (!restore || !remote.Animated || remote.Ghost == null)
            return;

        try
        {
            if (!ModEntry.TryGetGhost(playerId, out var ghost) || !ReferenceEquals(ghost, remote.Ghost))
                return;
            var group = ghost.spr?.groupName?.ToString();
            if (group != null && remote.Def.UsesAnim(group))
                ModEntry.RestoreGhostBodyAnim(playerId);
        }
        catch (Exception ex)
        {
            LobbySession.Log?.Warning("[Emotes] Ghost restore failed: {Message}", ex.Message);
        }
    }

    static bool TryReadSticks(Controller controller, out double x, out double y)
    {
        x = 0.0;
        y = 0.0;
        if (TryReadStick(controller, false, out x, out y))
            return true;

        var leftActive = TryReadStick(controller, true, out var lx, out var ly);
        if (!_leftStickArmed)
        {
            if (!leftActive)
                _leftStickArmed = true;
            return false;
        }

        x = lx;
        y = ly;
        return leftActive;
    }

    static bool IsLeftStickActive(Controller? controller) =>
        controller != null && TryReadStick(controller, true, out _, out _);

    static bool TryReadStick(Controller controller, bool left, out double x, out double y)
    {
        x = 0.0;
        y = 0.0;
        try
        {
            var device = controller.gc?.device;
            if (device == null)
                return false;

            var config = dc.hxd.Pad.Class.DEFAULT_CONFIG;
            var xIndex = left ? config.analogX : config.ranalogX;
            var yIndex = left ? config.analogY : config.ranalogY;
            var upKey = left ? PadLeftYPos : PadRightYPos;

            if (TryReadAxis(device.values, xIndex, out var rawX) && TryReadAxis(device.values, yIndex, out var rawY))
            {
                if (System.Math.Abs(rawY) > StickCalibrationMin && controller.padIsDown(upKey))
                    _padYSign = rawY > 0.0 ? -1.0 : 1.0;
                x = rawX;
                y = rawY * _padYSign;
            }
            else
            {
                x = (controller.padIsDown(left ? PadLeftXPos : PadRightXPos) ? 1.0 : 0.0) -
                    (controller.padIsDown(left ? PadLeftXNeg : PadRightXNeg) ? 1.0 : 0.0);
                y = (controller.padIsDown(left ? PadLeftYNeg : PadRightYNeg) ? 1.0 : 0.0) -
                    (controller.padIsDown(upKey) ? 1.0 : 0.0);
            }

            return x * x + y * y >= StickDeadZone * StickDeadZone;
        }
        catch
        {
            return false;
        }
    }

    static bool TryReadAxis(ArrayBytes_Float? values, int index, out double value)
    {
        value = 0.0;
        if (values == null || index < 0 || index >= values.length)
            return false;

        var raw = values.getDyn(index);
        if (raw == null)
            return false;
        value = raw is double d ? d : Convert.ToDouble(raw, CultureInfo.InvariantCulture);
        return !double.IsNaN(value);
    }

    static bool TryReadKeys(out double x, out double y)
    {
        var left = IsKeyDown(KeyA) || IsKeyDown(KeyLeft);
        var right = IsKeyDown(KeyD) || IsKeyDown(KeyRight);
        var up = IsKeyDown(KeyW) || IsKeyDown(KeyUp);
        var down = IsKeyDown(KeyS) || IsKeyDown(KeyDown);
        x = (right ? 1.0 : 0.0) - (left ? 1.0 : 0.0);
        y = (down ? 1.0 : 0.0) - (up ? 1.0 : 0.0);

        if (!_keysArmed)
        {
            if (!left && !right && !up && !down)
                _keysArmed = true;
            return false;
        }

        return x != 0.0 || y != 0.0;
    }

    static bool AnyDirectionKeyDown() => TryReadKeysRaw();

    static bool TryReadKeysRaw() =>
        IsKeyDown(KeyA) || IsKeyDown(KeyLeft) || IsKeyDown(KeyD) || IsKeyDown(KeyRight) ||
        IsKeyDown(KeyW) || IsKeyDown(KeyUp) || IsKeyDown(KeyS) || IsKeyDown(KeyDown);

    static bool ReadMouse(out double x, out double y)
    {
        x = 0.0;
        y = 0.0;
        try
        {
            var win = dc.hxd.Window.Class.getInstance();
            if (win == null)
                return false;
            x = win.curMouseX;
            y = win.curMouseY;
            return true;
        }
        catch
        {
            return false;
        }
    }

    static bool IsKeyDown(int code)
    {
        try
        {
            return dc.hxd.Key.Class.isDown(code);
        }
        catch
        {
            return false;
        }
    }

    static bool IsPadDown(int key)
    {
        try
        {
            var controller = ModEntry.me?.controller?.parent;
            return controller != null && controller.padIsDown(key);
        }
        catch
        {
            return false;
        }
    }

    static bool HasGroup(HSprite spr, string group)
    {
        var groups = spr.lib?.groups;
        return groups != null && groups.exists(group.AsHaxeString());
    }

    static void PlayUiSound(string path, double volume)
    {
        try
        {
            var audio = Audio.Class.ME;
            var loader = dc.hxd.Res.Class.get_loader();
            if (audio == null || loader == null)
                return;
            var sound = loader.loadCache(path.AsHaxeString(), dc.hxd.res.Sound.Class) as dc.hxd.res.Sound;
            if (sound != null)
                audio.playUIEvent(sound, volume);
        }
        catch
        {
        }
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
 *  EMOTE SYNC — КАК РАБОТАЕТ
 * ═══════════════════════════════════════
 *  Колесо: держать T (клавиатура) или L3 (PadKey 10), отпустил — сработала наведённая эмоция.
 *  Открывается только в сетевой сессии, у живого героя, без паузы, катсцены, текстового ввода
 *  и чужого exclusive; закрывается без эмоции, если что-то из этого нарушилось или игра
 *  потеряла фокус. Пока открыто — hero.controller.manualLock = true, при любом закрытии
 *  снимается (катсцены игры этот лок не используют, застрявший герой хуже).
 *  Наведение: стик (правый сразу, левый — после возврата в центр) → стрелки/WASD (после
 *  отпускания зажатых при открытии) → мышь. Мышь — виртуальный указатель: дельта за кадр
 *  (EmoteMouse держит курсор в центре окна), сумма ограничена кругом 90 px, ближе 26 px к центру
 *  = ничего. Указатель рисуется ромбом в центре колеса.
 *  Знак оси Y стика калибруется по тоглу PadKey «вверх» (21/27), без осей — цифровые тоглы.
 *  Своя эмоция: цепочка шагов EmoteDef.Steps, анимация только если герой стоит и не атакует,
 *  иначе только значок. ANIM для групп активной эмоции не шлётся (SuppressesHeroAnim).
 *  Конец: группа сменилась (цепочка доиграла или игра перебила), герой сдвинулся
 *  (stopWithStateAnims) или конечная цепочка идёт дольше 15 с → EMOTE стоп.
 *  Чужая эмоция: та же цепочка на призраке, на стоп — RestoreGhostBodyAnim, если призрак
 *  всё ещё в группе эмоции.
 *  Пузырь над головой — EmoteBubbles, ключ 0 = свой герой, иначе id игрока.
 * ═══════════════════════════════════════
*/
