using HaxeProxy.Runtime;
using ModCore.Utilities;

namespace DeadCellsMultiplayerMod.Core.Models;

/// <summary>
/// Represents the state of a nickname label attached to a ghost entity.
/// </summary>
public sealed class GhostLabelState
{
    public dc.ui.Text Label { get; }
    public string TextValue { get; set; }
    public int TextLength { get; set; }

    public GhostLabelState(dc.ui.Text label, string textValue)
    {
        Label = label ?? throw new ArgumentNullException(nameof(label));
        TextValue = textValue ?? throw new ArgumentNullException(nameof(textValue));
        TextLength = textValue.Length;
    }

    public bool IsAttached => Label.parent != null;

    public void UpdateText(string newText)
    {
        if (string.Equals(TextValue, newText, StringComparison.Ordinal))
            return;

        try
        {
            Label.set_text(newText.AsHaxeString());
            TextValue = newText;
            TextLength = newText.Length;
        }
        catch
        {
            // Label update failed, will be cleaned up on next pass
        }
    }

    public void Remove()
    {
        try
        {
            Label.remove();
        }
        catch
        {
            // Already removed or invalid
        }
    }
}
