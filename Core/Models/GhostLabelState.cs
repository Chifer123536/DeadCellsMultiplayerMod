using HaxeProxy.Runtime;
using ModCore.Utilities;

namespace DeadCellsMultiplayerMod.Core.Models;

public readonly record struct GhostLabelLayout(double Scale, int WindowWidth, int WindowHeight);

public sealed class GhostLabelState
{
    public dc.ui.Text Label { get; }
    public string TextValue { get; set; }
    public int TextLength { get; set; }
    public GhostLabelLayout? AppliedLayout { get; set; }

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
        }
    }
}

/*
 * ═══════════════════════════════════════
 *  GHOST LABEL STATE
 * ═══════════════════════════════════════
 *  Ник над сущностью: dc.ui.Text + текущий текст.
 *  AppliedLayout — scale/размер окна, под которые последний раз вызван onResize.
 *  Ошибки Haxe в set_text/remove глушатся, мёртвый лейбл чистит менеджер.
 * ═══════════════════════════════════════
*/
