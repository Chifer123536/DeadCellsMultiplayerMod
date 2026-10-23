using dc;
using dc.h2d;
using HaxeProxy.Runtime;
using ModCore.Utilities;

namespace DeadCellsMultiplayerMod.Emotes;

internal sealed class EmoteWheel
{
    public const int SlotCount = 8;
    public const double SlotSpan = System.Math.PI * 2.0 / SlotCount;

    const int ArcSegments = 10;
    const double OpenDurationMs = 120.0;
    const double OpenStartScale = 0.85;
    const double HoverIconScale = 1.25;

    const int RimColor = 0x081018;
    const int FillColor = 0x0d1b2a;
    const double FillAlpha = 0.86;
    const int SheenColor = 0x2a4560;
    const int HoverFillColor = 0x1b4256;
    const int GoldColor = 0xf0b13c;
    const int LabelColor = 0xf4f1e8;

    readonly dc.h2d.Object _root;
    readonly Graphics[] _sectors = new Graphics[SlotCount];
    readonly Graphics[] _icons = new Graphics[SlotCount];
    readonly dc.ui.Text? _label;
    readonly double _innerRadius;
    readonly double _outerRadius;
    readonly double _gap;
    readonly double _rim;
    readonly double _push;
    readonly long _openedAt;
    int _hovered = -1;
    bool _opened;
    bool _disposed;

    EmoteWheel(dc.h2d.Object parent, double centerX, double centerY, double unit)
    {
        _innerRadius = 66.0 * unit;
        _outerRadius = 150.0 * unit;
        _gap = 5.0 * unit;
        _rim = System.Math.Max(2.0, System.Math.Round(3.0 * unit));
        _push = 10.0 * unit;
        _openedAt = Environment.TickCount64;

        _root = new dc.h2d.Object(null);
        parent.addChildAt(_root, Const.Class.ROOT_DP_HUD);
        _root.x = System.Math.Round(centerX);
        _root.y = System.Math.Round(centerY);
        ApplyOpenProgress(0.0);

        var pixel = System.Math.Max(2.0, System.Math.Round(4.0 * unit));
        var iconRadius = (_innerRadius + _outerRadius) * 0.5;
        for (var i = 0; i < SlotCount; i++)
        {
            var sector = new Graphics(_root);
            _sectors[i] = sector;
            DrawSector(sector, i, false);

            var icon = new Graphics(sector);
            var angle = SlotAngle(i);
            icon.x = System.Math.Round(System.Math.Cos(angle) * iconRadius);
            icon.y = System.Math.Round(System.Math.Sin(angle) * iconRadius);
            var def = EmoteCatalog.Get(i);
            if (def != null)
                EmoteIcons.Draw(icon, def.Icon, 0.0, 0.0, pixel, 1.0);
            _icons[i] = icon;
        }

        var hub = new Graphics(_root);
        DrawHub(hub, _innerRadius - 10.0 * unit);

        try
        {
            _label = Assets.Class.makeText(" ".AsHaxeString(), LabelColor, null, _root);
            _label.textColor = LabelColor;
        }
        catch
        {
            _label = null;
        }

        SetLabel(LobbySession.Localize("Emotes"));
    }

    public int Hovered => _hovered;

    public static EmoteWheel? TryOpen()
    {
        try
        {
            var parent = Main.Class.ME?.root;
            if (parent == null)
                return null;

            ReadStageSize(out var width, out var height);
            if (width <= 0 || height <= 0)
                return null;

            var unit = System.Math.Clamp(height / 1080.0, 0.6, 3.0);
            return new EmoteWheel(parent, width * 0.5, height * 0.5, unit);
        }
        catch (Exception ex)
        {
            LobbySession.Log?.Warning("[Emotes] Wheel open failed: {Message}", ex.Message);
            return null;
        }
    }

    public static double SlotAngle(int slot) => -System.Math.PI * 0.5 + slot * SlotSpan;

    public static int SlotFromVector(double x, double y)
    {
        var angle = System.Math.Atan2(y, x) + System.Math.PI * 0.5;
        var slot = (int)System.Math.Round(angle / SlotSpan) % SlotCount;
        return slot < 0 ? slot + SlotCount : slot;
    }

    public void Update(long now)
    {
        if (_disposed || _opened)
            return;

        var t = System.Math.Clamp((now - _openedAt) / OpenDurationMs, 0.0, 1.0);
        ApplyOpenProgress(t);
        _opened = t >= 1.0;
    }

    public void SetHovered(int slot)
    {
        if (_disposed || slot == _hovered || slot >= SlotCount)
            return;

        var previous = _hovered;
        _hovered = slot;

        try
        {
            if (previous >= 0)
                SetSectorState(previous, false);
            if (slot >= 0)
                SetSectorState(slot, true);

            var def = EmoteCatalog.Get(slot);
            SetLabel(LobbySession.Localize(def?.Label ?? "Emotes"));
        }
        catch (Exception ex)
        {
            LobbySession.Log?.Warning("[Emotes] Wheel hover redraw failed: {Message}", ex.Message);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        try
        {
            _root.remove();
        }
        catch
        {
        }
    }

    void ApplyOpenProgress(double t)
    {
        var ease = 1.0 - (1.0 - t) * (1.0 - t);
        var scale = OpenStartScale + (1.0 - OpenStartScale) * ease;
        _root.scaleX = scale;
        _root.scaleY = scale;
        _root.alpha = ease;
        _root.posChanged = true;
    }

    void SetSectorState(int slot, bool hovered)
    {
        var sector = _sectors[slot];
        var angle = SlotAngle(slot);
        var push = hovered ? _push : 0.0;
        sector.x = System.Math.Round(System.Math.Cos(angle) * push);
        sector.y = System.Math.Round(System.Math.Sin(angle) * push);
        sector.posChanged = true;
        DrawSector(sector, slot, hovered);

        var icon = _icons[slot];
        var iconScale = hovered ? HoverIconScale : 1.0;
        icon.scaleX = iconScale;
        icon.scaleY = iconScale;
        icon.posChanged = true;

        if (hovered)
            _root.addChildAt(sector, SlotCount - 1);
    }

    void SetLabel(string text)
    {
        var label = _label;
        if (label == null)
            return;

        try
        {
            label.set_text(text.AsHaxeString());
            var scaleX = System.Math.Abs(label.scaleX);
            var scaleY = System.Math.Abs(label.scaleY);
            label.x = System.Math.Round(-label.get_textWidth() * scaleX * 0.5);
            label.y = System.Math.Round(-label.get_textHeight() * scaleY * 0.5);
            label.posChanged = true;
        }
        catch
        {
        }
    }

    void DrawSector(Graphics g, int slot, bool hovered)
    {
        g.clear();
        var angle = SlotAngle(slot);
        if (hovered)
        {
            FillRingSector(g, angle, _innerRadius - _rim, _outerRadius + _rim, _gap - _rim * 2.0, Darken(GoldColor, 0.75), GoldColor, 1.0);
            FillRingSector(g, angle, _innerRadius, _outerRadius, _gap, Darken(HoverFillColor, 0.7), HoverFillColor, 0.95);
            FillRingSector(g, angle, _outerRadius - _rim, _outerRadius, _gap, GoldColor, GoldColor, 0.55);
            return;
        }

        FillRingSector(g, angle, _innerRadius - _rim, _outerRadius + _rim, _gap - _rim * 2.0, RimColor, RimColor, 0.95);
        FillRingSector(g, angle, _innerRadius, _outerRadius, _gap, Darken(FillColor, 0.6), FillColor, FillAlpha);
        FillRingSector(g, angle, _outerRadius - _rim, _outerRadius, _gap, SheenColor, SheenColor, 0.8);
    }

    void DrawHub(Graphics g, double radius)
    {
        FillCircle(g, radius + _rim * 2.0, RimColor, 0.95);
        FillCircle(g, radius + _rim, GoldColor, 0.9);
        FillCircle(g, radius, FillColor, 0.96);
    }

    static void FillCircle(Graphics g, double radius, int color, double alpha)
    {
        var c = color;
        var a = alpha;
        g.beginFill(Ref<int>.From(ref c), Ref<double>.From(ref a));
        g.drawCircle(0.0, 0.0, radius, Ref<int>.Null);
        g.endFill();
    }

    static void FillRingSector(Graphics g, double centerAngle, double innerRadius, double outerRadius, double gap, int innerColor, int outerColor, double alpha)
    {
        if (innerRadius <= 0.0 || outerRadius <= innerRadius)
            return;

        var half = SlotSpan * 0.5;
        var gapHalf = System.Math.Max(0.0, gap) * 0.5;
        var innerHalf = half - System.Math.Asin(System.Math.Min(1.0, gapHalf / innerRadius));
        var outerHalf = half - System.Math.Asin(System.Math.Min(1.0, gapHalf / outerRadius));
        if (innerHalf <= 0.0 || outerHalf <= 0.0)
            return;

        SplitColor(innerColor, out var inR, out var inG, out var inB);
        SplitColor(outerColor, out var outR, out var outG, out var outB);

        var fill = outerColor;
        var fillAlpha = alpha;
        g.beginFill(Ref<int>.From(ref fill), Ref<double>.From(ref fillAlpha));
        for (var k = 0; k < ArcSegments; k++)
        {
            var t0 = (double)k / ArcSegments;
            var t1 = (double)(k + 1) / ArcSegments;
            var o0 = centerAngle - outerHalf + outerHalf * 2.0 * t0;
            var o1 = centerAngle - outerHalf + outerHalf * 2.0 * t1;
            var i0 = centerAngle - innerHalf + innerHalf * 2.0 * t0;
            var i1 = centerAngle - innerHalf + innerHalf * 2.0 * t1;

            g.addVertex(System.Math.Cos(o0) * outerRadius, System.Math.Sin(o0) * outerRadius, outR, outG, outB, alpha, Ref<double>.Null, Ref<double>.Null);
            g.addVertex(System.Math.Cos(o1) * outerRadius, System.Math.Sin(o1) * outerRadius, outR, outG, outB, alpha, Ref<double>.Null, Ref<double>.Null);
            g.addVertex(System.Math.Cos(i1) * innerRadius, System.Math.Sin(i1) * innerRadius, inR, inG, inB, alpha, Ref<double>.Null, Ref<double>.Null);
            g.addVertex(System.Math.Cos(i0) * innerRadius, System.Math.Sin(i0) * innerRadius, inR, inG, inB, alpha, Ref<double>.Null, Ref<double>.Null);
            g.flush();
        }
        g.endFill();
    }

    static void SplitColor(int color, out double r, out double g, out double b)
    {
        r = ((color >> 16) & 0xff) / 255.0;
        g = ((color >> 8) & 0xff) / 255.0;
        b = (color & 0xff) / 255.0;
    }

    static int Darken(int color, double factor)
    {
        var r = (int)(((color >> 16) & 0xff) * factor);
        var g = (int)(((color >> 8) & 0xff) * factor);
        var b = (int)((color & 0xff) * factor);
        return (r << 16) | (g << 8) | b;
    }

    static void ReadStageSize(out double width, out double height)
    {
        width = dc.libs.Process.Class.CUSTOM_STAGE_WIDTH;
        height = dc.libs.Process.Class.CUSTOM_STAGE_HEIGHT;
        if (width > 0 && height > 0)
            return;

        var win = dc.hxd.Window.Class.getInstance();
        if (win == null)
            return;
        if (width <= 0)
            width = win.get_width();
        if (height <= 0)
            height = win.get_height();
    }
}

/*
 * ═══════════════════════════════════════
 *  EMOTE WHEEL — КАК РАБОТАЕТ
 * ═══════════════════════════════════════
 *  Слой Main.root / ROOT_DP_HUD, центр экрана, размеры от высоты сцены (1080p = 1.0).
 *  8 кольцевых секторов: 0 сверху, по часовой. Сектор — свой Graphics из четырёхугольников
 *  (addVertex ×4 + flush), зазор между секторами постоянной ширины через asin(gap/2r).
 *  Иконка — дочерний Graphics сектора. Наведение перерисовывает только старый и новый сектор:
 *  золотая кромка, выдвиг наружу, иконка ×1.25, сектор поднимается наверх списка детей.
 *  В центре диск с названием эмоции. Открытие 120 мс: масштаб 0.85 → 1 и альфа 0 → 1.
 * ═══════════════════════════════════════
*/
