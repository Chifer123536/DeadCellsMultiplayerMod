namespace DeadCellsMultiplayerMod.Core.Abstractions;

/// <summary>
/// Provides display scaling information based on window mode.
/// </summary>
public interface IDisplayScaleProvider
{
    /// <summary>
    /// Gets the appropriate nickname scale for the current display mode.
    /// </summary>
    double GetNicknameScale();

    /// <summary>
    /// Forces recalculation of the cached scale value.
    /// </summary>
    void InvalidateCache();
}
