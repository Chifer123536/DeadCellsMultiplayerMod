using DeadCellsMultiplayerMod.Core.Abstractions;
using DeadCellsMultiplayerMod.Core.Constants;

namespace DeadCellsMultiplayerMod.Core.Services;

/// <summary>
/// Provides cached display scaling based on window display mode.
/// </summary>
public sealed class DisplayScaleProvider : IDisplayScaleProvider
{
    private int _cachedDisplayMode = int.MinValue;
    private int _cachedFullScreenMode = int.MinValue;
    private double _cachedNicknameScale = GameConstants.NicknameScaleWindowed;

    public double GetNicknameScale()
    {
        try
        {
            var win = dc.hxd.Window.Class.getInstance();
            if (win == null)
                return _cachedNicknameScale;

            var displayMode = int.MinValue;
            var sdlWin = win.window;
            if (sdlWin != null)
                displayMode = sdlWin.displayMode;

            var mode = win.fullScreenMode;
            if (_cachedDisplayMode == displayMode && _cachedFullScreenMode == mode)
                return _cachedNicknameScale;

            _cachedDisplayMode = displayMode;
            _cachedFullScreenMode = mode;
            _cachedNicknameScale = ResolveNicknameScale(displayMode, mode);
            return _cachedNicknameScale;
        }
        catch
        {
            return _cachedNicknameScale;
        }
    }

    public void InvalidateCache()
    {
        _cachedDisplayMode = int.MinValue;
        _cachedFullScreenMode = int.MinValue;
    }

    private static double ResolveNicknameScale(int displayMode, int fullScreenMode)
    {
        if (displayMode == GameConstants.FullscreenDisplayMode ||
            displayMode == GameConstants.BorderlessDisplayMode)
            return GameConstants.NicknameScaleFullscreen;

        if (displayMode == GameConstants.WindowedDisplayMode)
            return GameConstants.NicknameScaleWindowed;

        if (fullScreenMode == GameConstants.FullscreenDisplayMode ||
            fullScreenMode == GameConstants.BorderlessDisplayMode)
            return GameConstants.NicknameScaleFullscreen;

        if (fullScreenMode == GameConstants.WindowedDisplayMode)
            return GameConstants.NicknameScaleWindowed;

        return GameConstants.NicknameScaleWindowed;
    }
}
