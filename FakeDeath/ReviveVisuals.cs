using System.Diagnostics;
using dc;
using dc.h2d;
using dc.pr;
using HaxeProxy.Runtime;

namespace DeadCellsMultiplayerMod;

internal static class ReviveVisuals
{
    const double GaugeLift = 50.0;
    const int GaugeWidth = 34;
    const int GaugeHeight = 4;
    const double PopSeconds = 0.14;
    const double FadeSeconds = 0.22;
    const double FlashSeconds = 0.14;
    const double FillLerpPerSecond = 14.0;
    const double StreamCycleSeconds = 0.55;
    const int StreamMotes = 9;
    const double ChestLift = 28.0;
    const double BurstSeconds = 0.85;
    const double DeathBurstSeconds = 0.75;
    const double MarkLift = 34.0;
    const double MaxFrameSeconds = 0.1;

    const int Rim = 0x0a0c12;
    const int Edge = 0xd9a441;
    const int EdgeLight = 0xffe39a;
    const int Well = 0x111b28;
    const int FillTop = 0xa8f6ff;
    const int FillMid = 0x3cc8f2;
    const int FillLow = 0x1c78b0;
    const int White = 0xffffff;
    const int CellCore = 0xeaffff;
    const int CellGlow = 0x5fd7ff;
    const int DeepBlue = 0x2a5cc0;
    const int BloodDark = 0x5a0d12;
    const int Blood = 0xc4221d;
    const int Smoke = 0x2a2230;
    const int EyeGlow = 0xff3d2e;

    static readonly string[] Skull =
    [
        "..kkkkk..",
        ".kwwwwwk.",
        "kwwwwwwwk",
        "kwrrwrrwk",
        "kwrrwrrwk",
        "kswwkwwsk",
        ".kwwwwwk.",
        "..kwkwk..",
        "...kkk..."
    ];

    static readonly (double Vx, double Vy)[] Shards =
    [
        (-120, -210), (-75, -260), (-30, -290), (30, -285), (78, -250), (118, -205),
        (-150, -120), (150, -130), (-95, -170), (95, -165), (-55, -330), (55, -320),
        (-170, -60), (170, -70)
    ];

    sealed class Channel
    {
        public Level Level = null!;
        public Graphics Gauge = null!;
        public Graphics Stream = null!;
        public double X;
        public double Y;
        public double SrcX;
        public double SrcY;
        public bool HasSource;
        public double Ratio;
        public double Shown;
        public bool Waiting;
        public double Age;
        public double FadeAge = -1;
        public double FlashAge = -1;
        public long Frame;
    }

    sealed class Burst
    {
        public Level Level = null!;
        public Graphics Gfx = null!;
        public double Age;
        public bool Death;
    }

    sealed class Mark
    {
        public Level Level = null!;
        public Graphics Gfx = null!;
        public double X;
        public double Y;
        public double Age;
        public long Frame;
    }

    struct Painter
    {
        Graphics _g;
        int _color;
        double _alpha;
        bool _open;

        public Painter(Graphics g)
        {
            _g = g;
            _color = -1;
            _alpha = -1;
            _open = false;
            g.clear();
        }

        public void Px(int color, double alpha, double x, double y, double w, double h)
        {
            if (alpha <= 0.01 || w <= 0 || h <= 0)
                return;
            alpha = System.Math.Min(1.0, System.Math.Round(alpha * 20.0) / 20.0);
            if (!_open || color != _color || alpha != _alpha)
            {
                if (_open)
                    _g.endFill();
                var c = color;
                var a = alpha;
                _g.beginFill(Ref<int>.From(ref c), Ref<double>.From(ref a));
                _color = color;
                _alpha = alpha;
                _open = true;
            }
            _g.drawRect(System.Math.Round(x), System.Math.Round(y), w, h);
        }

        public void End()
        {
            if (_open)
                _g.endFill();
            _open = false;
        }
    }

    static readonly Dictionary<int, Channel> _channels = new();
    static readonly Dictionary<int, Mark> _marks = new();
    static readonly List<Burst> _bursts = new();
    static readonly List<int> _scratch = new();
    static long _frame;
    static long _lastTickTicks;
    static double _time;

    public static void ShowChannel(int key, Level? level, double x, double y, double ratio, bool waiting, bool hasSource, double srcX, double srcY)
    {
        if (level == null || !double.IsFinite(x) || !double.IsFinite(y))
            return;

        if (_channels.TryGetValue(key, out var channel) && !ReferenceEquals(channel.Level, level))
        {
            RemoveChannel(channel);
            _channels.Remove(key);
            channel = null;
        }

        if (channel != null && channel.FlashAge >= 0)
            return;

        if (channel != null)
            channel.FadeAge = -1;
        else
        {
            var gauge = CreateLayer(level, additive: false);
            var stream = CreateLayer(level, additive: true);
            if (gauge == null || stream == null)
            {
                SafeRemove(gauge);
                SafeRemove(stream);
                return;
            }

            channel = new Channel { Level = level, Gauge = gauge, Stream = stream, Shown = 0.0 };
            _channels[key] = channel;
        }

        channel.X = x;
        channel.Y = y;
        channel.Ratio = System.Math.Clamp(ratio, 0.0, 1.0);
        channel.Waiting = waiting;
        channel.HasSource = hasSource && double.IsFinite(srcX) && double.IsFinite(srcY);
        channel.SrcX = srcX;
        channel.SrcY = srcY;
        channel.Frame = _frame;
    }

    public static void CompleteChannel(int key)
    {
        if (!_channels.TryGetValue(key, out var channel) || channel.FlashAge >= 0)
            return;
        channel.Ratio = 1.0;
        channel.Shown = 1.0;
        channel.FlashAge = 0.0;
        channel.FadeAge = -1;
    }

    public static void ShowDeathMark(int key, Level? level, double x, double y)
    {
        if (level == null || !double.IsFinite(x) || !double.IsFinite(y))
            return;

        if (_marks.TryGetValue(key, out var mark) && !ReferenceEquals(mark.Level, level))
        {
            SafeRemove(mark.Gfx);
            _marks.Remove(key);
            mark = null;
        }

        if (mark == null)
        {
            var gfx = CreateLayer(level, additive: false);
            if (gfx == null)
                return;
            mark = new Mark { Level = level, Gfx = gfx };
            _marks[key] = mark;
        }

        mark.X = x;
        mark.Y = y;
        mark.Frame = _frame;
    }

    public static void PlayReviveBurst(Level? level, double x, double y) => AddBurst(level, x, y, death: false);

    public static void PlayDeathBurst(Level? level, double x, double y) => AddBurst(level, x, y, death: true);

    public static void Clear()
    {
        foreach (var channel in _channels.Values)
            RemoveChannel(channel);
        _channels.Clear();
        foreach (var mark in _marks.Values)
            SafeRemove(mark.Gfx);
        _marks.Clear();
        foreach (var burst in _bursts)
            SafeRemove(burst.Gfx);
        _bursts.Clear();
    }

    public static void Tick()
    {
        var now = Stopwatch.GetTimestamp();
        var dt = _lastTickTicks == 0 ? 0.0 : (now - _lastTickTicks) / (double)Stopwatch.Frequency;
        _lastTickTicks = now;
        dt = System.Math.Clamp(dt, 0.0, MaxFrameSeconds);
        _time += dt;

        if (_channels.Count > 0)
            TickChannels(dt);
        if (_marks.Count > 0)
            TickMarks(dt);
        if (_bursts.Count > 0)
            TickBursts(dt);

        _frame++;
    }

    static void TickChannels(double dt)
    {
        _scratch.Clear();
        foreach (var pair in _channels)
        {
            var channel = pair.Value;
            if (!IsAlive(channel.Level, channel.Gauge) || !IsAlive(channel.Level, channel.Stream))
            {
                _scratch.Add(pair.Key);
                continue;
            }

            channel.Age += dt;
            if (channel.FlashAge >= 0)
            {
                channel.FlashAge += dt;
                if (channel.FlashAge >= FlashSeconds + FadeSeconds)
                {
                    _scratch.Add(pair.Key);
                    continue;
                }
            }
            else if (channel.Frame != _frame)
            {
                if (channel.FadeAge < 0)
                    channel.FadeAge = 0;
                channel.FadeAge += dt;
                channel.Ratio = 0.0;
                if (channel.FadeAge >= FadeSeconds)
                {
                    _scratch.Add(pair.Key);
                    continue;
                }
            }
            else
            {
                channel.FadeAge = -1;
            }

            var lerp = System.Math.Min(1.0, dt * FillLerpPerSecond);
            channel.Shown += (channel.Ratio - channel.Shown) * lerp;
            if (System.Math.Abs(channel.Ratio - channel.Shown) < 0.004)
                channel.Shown = channel.Ratio;

            try
            {
                DrawChannel(channel);
            }
            catch
            {
                _scratch.Add(pair.Key);
            }
        }

        foreach (var key in _scratch)
        {
            if (_channels.Remove(key, out var dead))
                RemoveChannel(dead);
        }
        _scratch.Clear();
    }

    static void TickMarks(double dt)
    {
        _scratch.Clear();
        foreach (var pair in _marks)
        {
            var mark = pair.Value;
            if (mark.Frame != _frame || !IsAlive(mark.Level, mark.Gfx))
            {
                _scratch.Add(pair.Key);
                continue;
            }

            mark.Age += dt;
            try
            {
                DrawMark(mark);
            }
            catch
            {
                _scratch.Add(pair.Key);
            }
        }

        foreach (var key in _scratch)
        {
            if (_marks.Remove(key, out var dead))
                SafeRemove(dead.Gfx);
        }
        _scratch.Clear();
    }

    static void TickBursts(double dt)
    {
        for (var i = _bursts.Count - 1; i >= 0; i--)
        {
            var burst = _bursts[i];
            burst.Age += dt;
            var life = burst.Death ? DeathBurstSeconds : BurstSeconds;
            var keep = burst.Age < life && IsAlive(burst.Level, burst.Gfx);
            if (keep)
            {
                try
                {
                    if (burst.Death)
                        DrawDeathBurst(burst);
                    else
                        DrawReviveBurst(burst);
                }
                catch
                {
                    keep = false;
                }
            }

            if (!keep)
            {
                SafeRemove(burst.Gfx);
                _bursts.RemoveAt(i);
            }
        }
    }

    static void DrawChannel(Channel channel)
    {
        var gauge = channel.Gauge;
        var pop = System.Math.Clamp(channel.Age / PopSeconds, 0.0, 1.0);
        var scale = 0.6 + 0.4 * EaseOutBack(pop);
        var alpha = 1.0;
        if (channel.FadeAge >= 0)
            alpha = 1.0 - channel.FadeAge / FadeSeconds;
        else if (channel.FlashAge > FlashSeconds)
            alpha = 1.0 - (channel.FlashAge - FlashSeconds) / FadeSeconds;
        alpha = System.Math.Clamp(alpha, 0.0, 1.0);

        gauge.x = System.Math.Round(channel.X);
        gauge.y = System.Math.Round(channel.Y - GaugeLift);
        gauge.scaleX = scale;
        gauge.scaleY = scale;
        gauge.alpha = alpha;
        gauge.visible = true;
        gauge.posChanged = true;
        DrawGauge(gauge, channel.Shown, channel.FlashAge, channel.Waiting);

        var stream = channel.Stream;
        stream.x = System.Math.Round(channel.X);
        stream.y = System.Math.Round(channel.Y);
        stream.alpha = alpha;
        stream.visible = true;
        stream.posChanged = true;
        DrawStream(stream, channel);
    }

    static void DrawGauge(Graphics g, double shown, double flashAge, bool waiting)
    {
        var p = new Painter(g);
        const int w = GaugeWidth;
        const int h = GaugeHeight;
        const int left = -w / 2;
        const int top = -h / 2;

        p.Px(Rim, 0.92, left - 2, top - 2, w + 4, h + 4);
        p.Px(Edge, 1.0, left - 1, top - 1, w + 2, h + 2);
        p.Px(EdgeLight, 0.7, left - 1, top - 1, w + 2, 1);
        p.Px(Well, 1.0, left, top, w, h);

        var fill = (int)System.Math.Round(w * System.Math.Clamp(shown, 0.0, 1.0));
        if (fill > 0)
        {
            var fillAlpha = waiting ? 0.72 + 0.28 * System.Math.Sin(_time * 11.0) : 1.0;
            p.Px(FillTop, fillAlpha, left, top, fill, 1);
            p.Px(FillMid, fillAlpha, left, top + 1, fill, h - 2);
            p.Px(FillLow, fillAlpha, left, top + h - 1, fill, 1);

            if (!waiting && fill > 4)
            {
                var band = (int)((_time * 52.0) % (fill + 8)) - 4;
                var bx = left + band;
                var bw = 2;
                if (bx < left)
                {
                    bw += bx - left;
                    bx = left;
                }
                if (bx + bw > left + fill)
                    bw = left + fill - bx;
                if (bw > 0)
                    p.Px(White, 0.45, bx, top, bw, h - 1);
            }

            if (fill < w)
            {
                p.Px(White, 0.95, left + fill - 1, top, 1, h);
                p.Px(FillTop, 0.5, left + fill, top, 1, h);

                for (var s = 0; s < 4; s++)
                {
                    var phase = Frac(_time * 1.7 + s * 0.25);
                    var sx = left + fill - 1 + ((s & 1) == 0 ? -1 : 1) * (int)(phase * 3.0);
                    var sy = top - 1 - (int)(phase * 8.0);
                    p.Px(s == 0 ? White : FillTop, 1.0 - phase, sx, sy, 1, 1);
                }
            }
        }

        for (var q = 1; q < 4; q++)
            p.Px(Rim, 0.5, left + (int)System.Math.Round(w * q / 4.0), top, 1, h);

        DrawDiamond(ref p, left - 4);
        DrawDiamond(ref p, left + w + 3);

        if (flashAge >= 0 && flashAge < FlashSeconds)
            p.Px(White, 0.9 * (1.0 - flashAge / FlashSeconds), left - 1, top - 1, w + 2, h + 2);

        p.End();
    }

    static void DrawDiamond(ref Painter p, int cx)
    {
        p.Px(Rim, 0.92, cx - 2, 0, 5, 1);
        p.Px(Rim, 0.92, cx - 1, -1, 3, 3);
        p.Px(Rim, 0.92, cx, -2, 1, 5);
        p.Px(Edge, 1.0, cx - 1, 0, 3, 1);
        p.Px(Edge, 1.0, cx, -1, 1, 3);
        p.Px(EdgeLight, 1.0, cx, 0, 1, 1);
    }

    static void DrawStream(Graphics g, Channel channel)
    {
        var p = new Painter(g);
        var power = 0.35 + 0.65 * channel.Shown;

        var spread = 4 + (int)(10 * channel.Shown);
        p.Px(CellGlow, 0.22 * power, -spread, -1, spread * 2, 1);
        p.Px(CellGlow, 0.12 * power, -spread + 2, -2, spread * 2 - 4, 1);
        for (var r = 0; r < 5; r++)
        {
            var phase = Frac(_time * 0.9 + r * 0.2);
            var rx = (int)(System.Math.Sin(r * 2.3 + _time * 1.3) * spread * 0.8);
            var ry = -2 - (int)(phase * 20.0);
            p.Px(r % 2 == 0 ? CellCore : CellGlow, (1.0 - phase) * 0.8 * power, rx, ry, 1, 1);
        }

        if (channel.HasSource && channel.FlashAge < 0)
        {
            var sx = channel.SrcX - channel.X;
            var sy = channel.SrcY - ChestLift - channel.Y;
            const double tx = 0.0;
            const double ty = -8.0;
            var cx = (sx + tx) * 0.5;
            var cy = System.Math.Min(sy, ty) - 26.0;

            for (var i = 0; i < StreamMotes; i++)
            {
                var t = Frac(_time / StreamCycleSeconds + i / (double)StreamMotes);
                var u = 1.0 - t;
                var x = u * u * sx + 2 * u * t * cx + t * t * tx;
                var y = u * u * sy + 2 * u * t * cy + t * t * ty;
                x += System.Math.Sin(_time * 7.0 + i * 1.7) * 1.5;
                var a = System.Math.Min(1.0, t * 4.0) * System.Math.Min(1.0, (1.0 - t) * 5.0) * power;
                var size = t < 0.75 ? 2 : 1;
                p.Px(CellGlow, a * 0.5, x - 1, y - 1, size + 2, size + 2);
                p.Px(t < 0.5 ? CellGlow : CellCore, a, x, y, size, size);
            }
        }

        p.End();
    }

    static void DrawReviveBurst(Burst burst)
    {
        var p = new Painter(burst.Gfx);
        var a = burst.Age;

        var grow = System.Math.Clamp(a / 0.1, 0.0, 1.0);
        var shrink = a < 0.2 ? 1.0 : System.Math.Clamp(1.0 - (a - 0.2) / 0.42, 0.0, 1.0);
        var halfW = (int)System.Math.Round(6.0 * shrink);
        var height = (int)System.Math.Round(76.0 * grow);
        if (halfW > 0 && height > 0)
        {
            var shimmer = (int)(a * 30.0);
            for (var row = 0; row < height; row += 2)
            {
                var jag = (row * 7 + shimmer) % 3 - 1;
                var rw = System.Math.Max(1, halfW + jag);
                var fade = 1.0 - row / (double)height;
                p.Px(CellGlow, 0.55 * fade, -rw, -row - 2, rw * 2, 2);
                if (halfW >= 2)
                    p.Px(White, 0.9 * fade, -1, -row - 2, 2, 2);
            }
        }

        var ringT = System.Math.Clamp(a / 0.42, 0.0, 1.0);
        var ringR = 32.0 * (1.0 - (1.0 - ringT) * (1.0 - ringT));
        var ringA = 1.0 - ringT;
        for (var k = 0; k < 18; k++)
        {
            var ang = k * System.Math.PI * 2.0 / 18.0;
            p.Px(k % 3 == 0 ? CellCore : CellGlow, ringA, System.Math.Cos(ang) * ringR - 1, System.Math.Sin(ang) * ringR * 0.25 - 1, 2, 1);
        }

        const double shardLife = 0.65;
        if (a < shardLife)
        {
            var fade = 1.0 - a / shardLife;
            var color = a < 0.22 ? CellCore : a < 0.42 ? CellGlow : DeepBlue;
            var size = a < 0.4 ? 2 : 1;
            for (var s = 0; s < Shards.Length; s++)
            {
                var (vx, vy) = Shards[s];
                var x = vx * a * 0.55;
                var y = -20.0 + vy * a * 0.55 + 0.5 * 520.0 * a * a;
                p.Px(color, fade, x, y, size, size);
            }
        }

        var q = System.Math.Clamp((a - 0.08) / 0.45, 0.0, 1.0);
        if (q < 1.0)
        {
            for (var c = 0; c < 6; c++)
            {
                var ang = c * System.Math.PI / 3.0 + q * 3.2;
                var rad = 30.0 * (1.0 - q);
                var x = System.Math.Cos(ang) * rad;
                var y = -ChestLift + System.Math.Sin(ang) * rad * 0.8;
                p.Px(CellGlow, 0.85, x - 1, y - 1, 3, 3);
                p.Px(CellCore, 1.0, x, y, 1, 1);
            }
        }
        else if (a < 0.68)
        {
            var flash = 1.0 - (a - 0.53) / 0.15;
            p.Px(White, flash, -2, -ChestLift, 5, 1);
            p.Px(White, flash, 0, -ChestLift - 2, 1, 5);
        }

        p.End();
    }

    static void DrawDeathBurst(Burst burst)
    {
        var p = new Painter(burst.Gfx);
        var a = burst.Age;
        var fade = 1.0 - a / DeathBurstSeconds;

        for (var s = 0; s < Shards.Length; s++)
        {
            var (vx, vy) = Shards[s];
            var x = vx * a * 0.42;
            var y = -14.0 + vy * a * 0.35 + 0.5 * 640.0 * a * a;
            if (y > 2)
                y = 2;
            p.Px(s % 3 == 0 ? Blood : BloodDark, fade, x, y, 2, 2);
        }

        for (var m = 0; m < 7; m++)
        {
            var phase = System.Math.Clamp(a / DeathBurstSeconds + m * 0.04, 0.0, 1.0);
            var x = System.Math.Sin(m * 1.9 + a * 4.0) * (4 + m * 2);
            var y = -10.0 - phase * 34.0 - m * 3;
            p.Px(Smoke, (1.0 - phase) * 0.75, x, y, 2, 2);
        }

        var ringT = System.Math.Clamp(a / 0.35, 0.0, 1.0);
        var ringR = 22.0 * ringT;
        for (var k = 0; k < 14; k++)
        {
            var ang = k * System.Math.PI * 2.0 / 14.0;
            p.Px(Blood, 1.0 - ringT, System.Math.Cos(ang) * ringR - 1, System.Math.Sin(ang) * ringR * 0.25 - 1, 2, 1);
        }

        p.End();
    }

    static void DrawMark(Mark mark)
    {
        var gfx = mark.Gfx;
        var pop = System.Math.Clamp(mark.Age / 0.18, 0.0, 1.0);
        var scale = 0.5 + 0.5 * EaseOutBack(pop);
        var bob = System.Math.Round(System.Math.Sin(mark.Age * 2.2));

        gfx.x = System.Math.Round(mark.X);
        gfx.y = System.Math.Round(mark.Y - MarkLift + bob);
        gfx.scaleX = scale;
        gfx.scaleY = scale;
        gfx.alpha = System.Math.Clamp(mark.Age / 0.12, 0.0, 1.0);
        gfx.visible = true;
        gfx.posChanged = true;

        var p = new Painter(gfx);
        var eye = 0.65 + 0.35 * System.Math.Sin(mark.Age * 9.0);
        var height = Skull.Length;
        var width = Skull[0].Length;
        var left = -width / 2;
        var top = -height / 2;

        p.Px(Rim, 0.35, left - 1, top + height, width + 2, 1);
        for (var y = 0; y < height; y++)
        {
            var row = Skull[y];
            var x = 0;
            while (x < row.Length)
            {
                var ch = row[x];
                var start = x;
                while (x < row.Length && row[x] == ch)
                    x++;
                if (ch == '.')
                    continue;

                var (color, alpha) = ch switch
                {
                    'k' => (0x15111b, 1.0),
                    'w' => (0xe9dcc0, 1.0),
                    's' => (0xb3a283, 1.0),
                    'r' => (EyeGlow, eye),
                    _ => (0xffffff, 1.0)
                };
                if (ch == 'r')
                    p.Px(0x15111b, 1.0, left + start, top + y, x - start, 1);
                p.Px(color, alpha, left + start, top + y, x - start, 1);
            }
        }

        p.End();
    }

    static void AddBurst(Level? level, double x, double y, bool death)
    {
        if (level == null || !double.IsFinite(x) || !double.IsFinite(y))
            return;

        var gfx = CreateLayer(level, additive: !death);
        if (gfx == null)
            return;

        try
        {
            gfx.x = System.Math.Round(x);
            gfx.y = System.Math.Round(y);
            gfx.posChanged = true;
        }
        catch
        {
            SafeRemove(gfx);
            return;
        }

        _bursts.Add(new Burst { Level = level, Gfx = gfx, Death = death });
    }

    static Graphics? CreateLayer(Level level, bool additive)
    {
        try
        {
            if (level.destroyed)
                return null;
            var scroller = level.scroller;
            if (scroller == null)
                return null;

            var gfx = new Graphics(null);
            scroller.addChildAt(gfx, Const.Class.DP_CTX_UI);
            if (additive)
                gfx.blendMode = new BlendMode.Add();
            return gfx;
        }
        catch (Exception ex)
        {
            LobbySession.Log?.Warning("[Revive] visual layer failed: {Message}", ex.Message);
            return null;
        }
    }

    static bool IsAlive(Level level, Graphics gfx)
    {
        try
        {
            return !level.destroyed && gfx.parent != null;
        }
        catch
        {
            return false;
        }
    }

    static void RemoveChannel(Channel channel)
    {
        SafeRemove(channel.Gauge);
        SafeRemove(channel.Stream);
    }

    static void SafeRemove(Graphics? gfx)
    {
        if (gfx == null)
            return;
        try { gfx.remove(); } catch { }
    }

    static double Frac(double v) => v - System.Math.Floor(v);

    static double EaseOutBack(double t)
    {
        const double c1 = 1.70158;
        const double c3 = c1 + 1.0;
        var p = t - 1.0;
        return 1.0 + c3 * p * p * p + c1 * p * p;
    }
}

/*
 * ═══════════════════════════════════════
 *  REVIVE VISUALS — КАК РАБОТАЕТ
 * ═══════════════════════════════════════
 *  Вся графика — Graphics в level.scroller на слое DP_CTX_UI, координаты в пикселях мира,
 *  прямоугольники по целым пикселям → чистый пиксель-арт при зуме игры.
 *  Канал подъёма (ключ = id лежащего): полоса 34×4 в золотой рамке с ромбами и делениями
 *    по 25%, заливка клеточно-голубая с бликом и искрами на кромке; поток клеток по дуге
 *    от груди поднимающего к телу + свечение под телом. Канал живёт, пока его вызывают
 *    каждый кадр (ShowChannel); пропуск кадра → слив и затухание 0.22 с.
 *  CompleteChannel — белая вспышка полосы и затухание.
 *  ReviveBurst 0.85 с: световой столп, кольцо по земле, разлёт осколков, клетки
 *    по спирали в грудь, финальная звёздочка. Режим Add.
 *  DeathBurst 0.75 с: тёмно-красные осколки, дым, кольцо. DeathMark — череп над телом
 *    мёртвого до следующего перехода, покачивание и мерцание глаз.
 *  Tick() раз в кадр из UpdateFakeDeathFlow; Clear() при смене уровня и сбросе.
 * ═══════════════════════════════════════
*/
