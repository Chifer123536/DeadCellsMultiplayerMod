using dc;
using dc.h2d;
using dc.pr;
using HaxeProxy.Runtime;

namespace DeadCellsMultiplayerMod.Emotes;

internal static class EmoteBubbles
{
    const long PopMs = 120;
    const long LifeMs = 2200;
    const long FadeMs = 300;
    const double PopStartScale = 0.4;
    const double HeadOffsetY = -110.0;

    const int RimColor = 0x081018;
    const int PanelColor = 0x16263a;
    const int EdgeColor = 0xf0b13c;
    const int PanelSize = 15;

    sealed class Bubble
    {
        public Entity Owner = null!;
        public Level? Level;
        public Graphics Gfx = null!;
        public long StartedAt;
    }

    static readonly Dictionary<int, Bubble> _bubbles = new();
    static readonly List<int> _scratch = new();
    static dc.h2d.col.Point? _point;

    public static void Show(int key, Entity owner, string[] icon)
    {
        Hide(key);
        if (owner == null || icon == null)
            return;

        try
        {
            var level = owner._level;
            var scroller = level?.scroller;
            if (scroller == null || owner.spr == null)
                return;

            var gfx = new Graphics(null);
            scroller.addChildAt(gfx, Const.Class.DP_CTX_UI);
            DrawBubble(gfx, icon);

            var bubble = new Bubble
            {
                Owner = owner,
                Level = level,
                Gfx = gfx,
                StartedAt = Environment.TickCount64
            };
            _bubbles[key] = bubble;
            Place(bubble, 0.0, 0.0);
        }
        catch (Exception ex)
        {
            LobbySession.Log?.Warning("[Emotes] Bubble failed: {Message}", ex.Message);
        }
    }

    public static void Hide(int key)
    {
        if (!_bubbles.Remove(key, out var bubble))
            return;
        Remove(bubble);
    }

    public static void Clear()
    {
        foreach (var bubble in _bubbles.Values)
            Remove(bubble);
        _bubbles.Clear();
    }

    public static void Tick(long now)
    {
        if (_bubbles.Count == 0)
            return;

        _scratch.Clear();
        foreach (var pair in _bubbles)
        {
            var bubble = pair.Value;
            var age = now - bubble.StartedAt;
            if (age >= LifeMs || !IsAttached(bubble))
            {
                _scratch.Add(pair.Key);
                continue;
            }

            var pop = System.Math.Clamp((double)age / PopMs, 0.0, 1.0);
            var scale = PopStartScale + (1.0 - PopStartScale) * EaseOutBack(pop);
            var fadeStart = LifeMs - FadeMs;
            var alpha = age <= fadeStart ? 1.0 : 1.0 - (double)(age - fadeStart) / FadeMs;
            Place(bubble, scale, System.Math.Clamp(alpha, 0.0, 1.0));
        }

        foreach (var key in _scratch)
            Hide(key);
        _scratch.Clear();
    }

    static bool IsAttached(Bubble bubble)
    {
        try
        {
            var owner = bubble.Owner;
            return !owner.destroyed &&
                   owner.spr != null &&
                   ReferenceEquals(owner._level, bubble.Level) &&
                   bubble.Gfx.parent != null;
        }
        catch
        {
            return false;
        }
    }

    static void Place(Bubble bubble, double scale, double alpha)
    {
        var gfx = bubble.Gfx;
        var spr = bubble.Owner.spr;
        var parent = gfx.parent;
        if (spr == null || parent == null)
            return;

        double x;
        double y;
        if (ReferenceEquals(spr.parent, parent))
        {
            x = spr.x;
            y = spr.y;
        }
        else
        {
            _point ??= CreatePoint();
            _point.x = 0.0;
            _point.y = 0.0;
            spr.localToGlobal(_point);
            parent.globalToLocal(_point);
            x = _point.x;
            y = _point.y;
        }

        gfx.x = System.Math.Round(x);
        gfx.y = System.Math.Round(y + HeadOffsetY);
        gfx.scaleX = scale;
        gfx.scaleY = scale;
        gfx.alpha = alpha;
        gfx.visible = spr.visible;
        gfx.posChanged = true;
    }

    static void DrawBubble(Graphics g, string[] icon)
    {
        var half = PanelSize / 2;
        var top = -PanelSize - 4;

        FillRect(g, RimColor, 1.0, -half - 2, top - 2, PanelSize + 4, PanelSize + 4);
        FillRect(g, EdgeColor, 1.0, -half - 1, top - 1, PanelSize + 2, PanelSize + 2);
        FillRect(g, PanelColor, 0.95, -half, top, PanelSize, PanelSize);

        FillRect(g, RimColor, 1.0, -3, top + PanelSize + 1, 7, 1);
        FillRect(g, RimColor, 1.0, -2, top + PanelSize + 2, 5, 1);
        FillRect(g, RimColor, 1.0, -1, top + PanelSize + 3, 3, 1);
        FillRect(g, EdgeColor, 1.0, -2, top + PanelSize + 1, 5, 1);
        FillRect(g, EdgeColor, 1.0, -1, top + PanelSize + 2, 3, 1);
        FillRect(g, EdgeColor, 1.0, 0, top + PanelSize + 3, 1, 1);

        EmoteIcons.Draw(g, icon, 0.5, top + PanelSize * 0.5, 1.0, 1.0);
    }

    static void FillRect(Graphics g, int color, double alpha, double x, double y, double w, double h)
    {
        var c = color;
        var a = alpha;
        g.beginFill(Ref<int>.From(ref c), Ref<double>.From(ref a));
        g.drawRect(x, y, w, h);
        g.endFill();
    }

    static double EaseOutBack(double t)
    {
        const double c1 = 1.70158;
        const double c3 = c1 + 1.0;
        var p = t - 1.0;
        return 1.0 + c3 * p * p * p + c1 * p * p;
    }

    static void Remove(Bubble bubble)
    {
        try
        {
            bubble.Gfx.remove();
        }
        catch
        {
        }
    }

    static dc.h2d.col.Point CreatePoint()
    {
        var x = 0.0;
        var y = 0.0;
        return new dc.h2d.col.Point(Ref<double>.From(ref x), Ref<double>.From(ref y));
    }
}

/*
 * ═══════════════════════════════════════
 *  EMOTE BUBBLES — КАК РАБОТАЕТ
 * ═══════════════════════════════════════
 *  Пузырь — Graphics в level.scroller на слое DP_CTX_UI (не прячется за декором),
 *  размеры в пикселях мира: панель 15×15 с золотой кромкой, хвостик вниз, иконка 9×9.
 *  Каждый кадр ставится над spr владельца (общий родитель → spr.x/y, иначе localToGlobal).
 *  Появление 120 мс (ease-out-back), жизнь 2.2 с, затухание последние 300 мс.
 *  Ключ — id игрока: новый пузырь того же игрока заменяет старый.
 *  Удаляется, если владелец уничтожен, сменил уровень или пузырь отцеплен.
 * ═══════════════════════════════════════
*/
