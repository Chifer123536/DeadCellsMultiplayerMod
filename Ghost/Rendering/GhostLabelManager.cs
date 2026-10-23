using dc;
using DeadCellsMultiplayerMod.Core.Abstractions;
using DeadCellsMultiplayerMod.Core.Models;
using HaxeProxy.Runtime;
using ModCore.Utilities;

namespace DeadCellsMultiplayerMod.Ghost.Rendering;

public sealed class GhostLabelManager : IGhostRenderer
{
    readonly Dictionary<dc.Entity, GhostLabelState> _labels = new();
    readonly List<dc.Entity> _staleLabels = new();
    readonly IDisplayScaleProvider _scaleProvider;

    const double LabelYOffset = -80.0;
    const double LabelAlpha = 0.8;

    public GhostLabelManager(IDisplayScaleProvider scaleProvider)
    {
        _scaleProvider = scaleProvider ?? throw new ArgumentNullException(nameof(scaleProvider));
    }

    public void SetLabel(dc.Entity entity, string? text)
    {
        if (entity == null) return;

        var normalizedText = NormalizeText(text);

        if (_labels.TryGetValue(entity, out var existing))
        {
            if (existing.IsAttached)
            {
                existing.UpdateText(normalizedText);
                return;
            }

            existing.Remove();
            _labels.Remove(entity);
        }

        CreateLabel(entity, normalizedText);
    }

    public void UpdateLabels()
    {
        if (_labels.Count == 0) return;

        var layout = ReadLayout();
        _staleLabels.Clear();

        foreach (var pair in _labels)
        {
            var entity = pair.Key;
            var state = pair.Value;

            if (!IsValidLabel(entity, state.Label))
            {
                if (entity != null)
                    _staleLabels.Add(entity);
                continue;
            }

            ApplyLayout(entity, state, layout);
        }

        CleanupStaleLabels();
    }

    public void CleanupStaleLabels()
    {
        if (_staleLabels.Count == 0) return;

        foreach (var entity in _staleLabels)
        {
            _labels.Remove(entity);
        }
    }

    void CreateLabel(dc.Entity entity, string text)
    {
        try
        {
            var nicknameColor = dc.ui.Text.Class.COLORS.get("ST".AsHaxeString());
            var label = Assets.Class.makeText(text.AsHaxeString(), nicknameColor, null, entity.spr);
            label.alpha = LabelAlpha;

            var state = new GhostLabelState(label, text);
            _labels[entity] = state;

            ApplyLayout(entity, state, ReadLayout());
            label.textColor = nicknameColor;
        }
        catch
        {
        }
    }

    GhostLabelLayout ReadLayout()
    {
        var scale = _scaleProvider.GetNicknameScale();

        try
        {
            var win = dc.hxd.Window.Class.getInstance();
            if (win != null)
                return new GhostLabelLayout(scale, win.get_width(), win.get_height());
        }
        catch
        {
        }

        return new GhostLabelLayout(scale, 0, 0);
    }

    static void ApplyLayout(dc.Entity entity, GhostLabelState state, GhostLabelLayout layout)
    {
        var label = state.Label;

        if (state.AppliedLayout != layout)
        {
            label.customScale = layout.Scale;
            label.onResize();
            state.AppliedLayout = layout;
        }

        var scale = System.Math.Abs(label.scaleY);
        var halfWidth = label.get_textWidth() * scale * 0.5;
        var dir = entity.dir < 0 ? -1.0 : 1.0;

        label.scaleX = scale * dir;
        label.x = -halfWidth * dir;
        label.y = LabelYOffset;
        label.posChanged = true;
    }

    static bool IsValidLabel(dc.Entity? entity, dc.ui.Text? label)
    {
        return entity != null &&
               label != null &&
               entity.spr != null &&
               label.parent != null;
    }

    static string NormalizeText(string? text)
    {
        return string.IsNullOrWhiteSpace(text) ? "Guest" : text;
    }
}

/*
 * ═══════════════════════════════════════
 *  GHOST LABEL MANAGER
 * ═══════════════════════════════════════
 *  Ник — дочерний dc.ui.Text на entity.spr, координаты в локальных единицах спрайта.
 *  Центр: x = -textWidth * scale / 2 (реальная ширина глифов, не оценка по символам).
 *  dir < 0: спрайт зеркален → scaleX текста и x тоже зеркалятся, текст читается слева направо.
 *  onResize (смена шрифта + пересборка глифов) только при смене scale или размера окна.
 *  posChanged = true вручную: прямая запись x/scaleX в Haxe-поле не помечает матрицу грязной.
 * ═══════════════════════════════════════
*/
