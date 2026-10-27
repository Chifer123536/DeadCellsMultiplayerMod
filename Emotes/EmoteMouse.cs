using System.Runtime.InteropServices;

namespace DeadCellsMultiplayerMod.Emotes;

internal static class EmoteMouse
{
    [StructLayout(LayoutKind.Sequential)]
    struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate uint SdlGetRelativeMouseState(out float x, out float y);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate IntPtr SdlGetFocus();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate byte SdlGetWindowRelativeMouseMode(IntPtr window);

    [DllImport("user32.dll")]
    static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    static extern bool GetClientRect(IntPtr window, out Rect rect);

    [DllImport("user32.dll")]
    static extern bool ClientToScreen(IntPtr window, ref Point point);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr GetModuleHandleW(string moduleName);

    static SdlGetRelativeMouseState? _sdlRelativeState;
    static SdlGetWindowRelativeMouseMode? _sdlRelativeMode;
    static SdlGetFocus? _sdlMouseFocus;
    static SdlGetFocus? _sdlKeyboardFocus;

    static bool _captured;
    static bool _sdlRelative;
    static IntPtr _window;
    static Point _restore;
    static Point _center;

    public static bool IsCaptured => _captured;

    public static bool IsGameForeground()
    {
        try
        {
            if (!OperatingSystem.IsWindows())
                return true;
            var window = GetForegroundWindow();
            if (window == IntPtr.Zero)
                return false;
            GetWindowThreadProcessId(window, out var processId);
            return processId == (uint)Environment.ProcessId;
        }
        catch
        {
            return true;
        }
    }

    public static bool Capture()
    {
        Release();
        try
        {
            if (!OperatingSystem.IsWindows())
                return false;

            var window = GetForegroundWindow();
            if (window == IntPtr.Zero)
                return false;
            GetWindowThreadProcessId(window, out var processId);
            if (processId != (uint)Environment.ProcessId)
                return false;

            if (IsSdlRelativeMode())
            {
                _sdlRelativeState!(out _, out _);
                _window = window;
                _sdlRelative = true;
                _captured = true;
                return true;
            }

            if (!GetClientRect(window, out var rect) || rect.Right - rect.Left < 4 || rect.Bottom - rect.Top < 4)
                return false;
            var center = new Point { X = (rect.Right - rect.Left) / 2, Y = (rect.Bottom - rect.Top) / 2 };
            if (!ClientToScreen(window, ref center) || !GetCursorPos(out var restore))
                return false;
            if (!SetCursorPos(center.X, center.Y))
                return false;

            _window = window;
            _restore = restore;
            _center = center;
            _sdlRelative = false;
            _captured = true;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool ReadDelta(out double dx, out double dy)
    {
        dx = 0.0;
        dy = 0.0;
        if (!_captured)
            return false;

        try
        {
            if (GetForegroundWindow() != _window)
            {
                _captured = false;
                return false;
            }

            if (_sdlRelative)
            {
                _sdlRelativeState!(out var rx, out var ry);
                if (float.IsFinite(rx) && float.IsFinite(ry))
                {
                    dx = rx;
                    dy = ry;
                }
                return true;
            }

            if (!GetCursorPos(out var point))
                return false;
            dx = point.X - _center.X;
            dy = point.Y - _center.Y;
            if (dx != 0.0 || dy != 0.0)
                SetCursorPos(_center.X, _center.Y);
            return true;
        }
        catch
        {
            _captured = false;
            return false;
        }
    }

    public static void Release()
    {
        if (!_captured)
            return;
        _captured = false;
        if (_sdlRelative)
            return;
        try
        {
            SetCursorPos(_restore.X, _restore.Y);
        }
        catch
        {
        }
    }

    static bool IsSdlRelativeMode()
    {
        if (!ResolveSdl())
            return false;

        try
        {
            var window = _sdlMouseFocus!();
            if (window == IntPtr.Zero)
                window = _sdlKeyboardFocus!();
            return window != IntPtr.Zero && _sdlRelativeMode!(window) != 0;
        }
        catch
        {
            return false;
        }
    }

    static bool ResolveSdl()
    {
        if (_sdlRelativeState != null)
            return true;

        try
        {
            var module = GetModuleHandleW("SDL3.dll");
            if (module == IntPtr.Zero)
                return false;
            if (!NativeLibrary.TryGetExport(module, "SDL_GetRelativeMouseState", out var relativeState) ||
                !NativeLibrary.TryGetExport(module, "SDL_GetWindowRelativeMouseMode", out var relativeMode) ||
                !NativeLibrary.TryGetExport(module, "SDL_GetMouseFocus", out var mouseFocus) ||
                !NativeLibrary.TryGetExport(module, "SDL_GetKeyboardFocus", out var keyboardFocus))
                return false;

            _sdlRelativeMode = Marshal.GetDelegateForFunctionPointer<SdlGetWindowRelativeMouseMode>(relativeMode);
            _sdlMouseFocus = Marshal.GetDelegateForFunctionPointer<SdlGetFocus>(mouseFocus);
            _sdlKeyboardFocus = Marshal.GetDelegateForFunctionPointer<SdlGetFocus>(keyboardFocus);
            _sdlRelativeState = Marshal.GetDelegateForFunctionPointer<SdlGetRelativeMouseState>(relativeState);
            return true;
        }
        catch
        {
            _sdlRelativeState = null;
            return false;
        }
    }
}

/*
 * ═══════════════════════════════════════
 *  EMOTE MOUSE — КАК РАБОТАЕТ
 * ═══════════════════════════════════════
 *  Ход мыши на время колеса, только если активное окно принадлежит игре.
 *  SDL-сборка (deadcells_gl): при relative mouse mode SDL3 прибивает курсор к центру,
 *  поэтому дельта берётся из SDL_GetRelativeMouseState уже загруженного SDL3.dll
 *  (GetModuleHandle, второй копии не грузим). Capture сбрасывает накопленное, курсор не трогается.
 *  Без relative mode: курсор ставится в центр клиентской области, ReadDelta = смещение от центра
 *  за кадр с возвратом в центр, Release возвращает курсор на исходное место.
 *  Окно игры перестало быть активным — захват снимается без возврата курсора.
 * ═══════════════════════════════════════
*/
