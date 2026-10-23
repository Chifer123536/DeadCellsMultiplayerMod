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

    static bool _captured;
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
        try
        {
            SetCursorPos(_restore.X, _restore.Y);
        }
        catch
        {
        }
    }
}

/*
 * ═══════════════════════════════════════
 *  EMOTE MOUSE — КАК РАБОТАЕТ
 * ═══════════════════════════════════════
 *  Захват мыши на время колеса через user32: только если активное окно принадлежит игре.
 *  Capture: запомнить позицию, поставить курсор в центр клиентской области окна.
 *  ReadDelta: смещение от центра за кадр, курсор сразу возвращается в центр —
 *  ход мыши не упирается в края экрана и не зависит от скрытого курсора.
 *  Окно игры перестало быть активным — захват снимается без возврата курсора (чужое окно не трогаем).
 *  Release: вернуть курсор туда, где он был до открытия.
 * ═══════════════════════════════════════
*/
