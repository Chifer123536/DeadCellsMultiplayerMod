using dc;
using DeadCellsMultiplayerMod.Core.Abstractions;
using DeadCellsMultiplayerMod.Core.Models;
using HaxeProxy.Runtime;
using ModCore.Utilities;
using dc.libs.heaps.slib;

namespace DeadCellsMultiplayerMod.Ghost.Rendering;

/// <summary>
/// Manages nickname labels for ghost entities.
/// </summary>
public sealed class GhostLabelManager : IGhostRenderer
{
    private readonly Dictionary<dc.Entity, GhostLabelState> _labels = new();
    private readonly List<dc.Entity> _staleLabels = new();
    private readonly IDisplayScaleProvider _scaleProvider;

    private const double LabelYOffset = -80.0;
    private const double LabelXOffsetPerChar = 2.5;
    private const double LabelAlpha = 0.8;

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

        var targetScale = _scaleProvider.GetNicknameScale();
        _staleLabels.Clear();

        foreach (var pair in _labels)
        {
            var entity = pair.Key;
            var state = pair.Value;
            var label = state.Label;

            if (!IsValidLabel(entity, label))
            {
                if (entity != null)
                    _staleLabels.Add(entity);
                continue;
            }

            UpdateLabelPosition(entity, state, label, targetScale);
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

    private void CreateLabel(dc.Entity entity, string text)
    {
        try
        {
            var nicknameColor = dc.ui.Text.Class.COLORS.get("ST".AsHaxeString());
            var label = Assets.Class.makeText(text.AsHaxeString(), nicknameColor, null, entity.spr);
            var targetScale = _scaleProvider.GetNicknameScale();

            label.y = LabelYOffset;
            label.x = -LabelXOffsetPerChar * text.Length;
            label.alpha = LabelAlpha;
            label.customScale = targetScale;
            label.onResize();
            label.textColor = nicknameColor;

            _labels[entity] = new GhostLabelState(label, text);
        }
        catch
        {
            // Label creation failed
        }
    }

    private void UpdateLabelPosition(dc.Entity entity, GhostLabelState state, dc.ui.Text label, double targetScale)
    {
        var targetX = -LabelXOffsetPerChar * state.TextLength;

        label.customScale = targetScale;
        label.onResize();

        if (entity.dir < 0)
        {
            label.scaleX = -label.scaleX;
            label.x = -targetX;
        }
        else
        {
            label.x = targetX;
        }

        label.y = LabelYOffset;
    }

    private static bool IsValidLabel(dc.Entity? entity, dc.ui.Text? label)
    {
        return entity != null &&
               label != null &&
               entity.spr != null &&
               label.parent != null;
    }

    private static string NormalizeText(string? text)
    {
        return string.IsNullOrWhiteSpace(text) ? "Guest" : text;
    }
}
