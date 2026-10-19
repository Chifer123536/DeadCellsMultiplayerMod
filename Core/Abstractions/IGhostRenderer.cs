namespace DeadCellsMultiplayerMod.Core.Abstractions;

/// <summary>
/// Interface for rendering ghost player avatars.
/// </summary>
public interface IGhostRenderer
{
    /// <summary>
    /// Updates visual labels (nicknames) for all ghosts.
    /// </summary>
    void UpdateLabels();

    /// <summary>
    /// Sets or updates the label for a specific entity.
    /// </summary>
    void SetLabel(dc.Entity entity, string? text);

    /// <summary>
    /// Removes stale labels that are no longer attached to valid entities.
    /// </summary>
    void CleanupStaleLabels();
}
