namespace DeadCellsMultiplayerMod.PortableCore;

internal readonly record struct PuppetPose(
    double X,
    double Y,
    int Dir,
    double Dx,
    double Dy,
    string Anim,
    bool Extrapolated,
    double AgeMs);

internal sealed class MobPuppetTrack
{
    const int Capacity = 16;
    const double MaxExtrapolationMs = 50.0;
    const double TeleportBasePx = 96.0;
    const double TeleportPxPerMs = 1.2;
    const double HoldGapMs = 100.0;
    const double HoldLeadMs = 34.0;
    const double StreamResetMs = 5000.0;
    const double StillVelocityRaw = 0.001;

    readonly record struct Sample(double HostMs, double X, double Y, int Dir, double Dx, double Dy, string Anim, bool Cut);

    readonly Sample[] _ring = new Sample[Capacity];
    int _start;
    int _count;

    public bool HasSamples => _count > 0;
    public int Count => _count;
    public double NewestHostMs => _count > 0 ? At(_count - 1).HostMs : double.NaN;

    public void Clear()
    {
        _start = 0;
        _count = 0;
    }

    public bool Push(double hostMs, double x, double y, int dir, double dx, double dy, string? anim, bool cut)
    {
        if (!double.IsFinite(hostMs) || hostMs <= 0.0 || !double.IsFinite(x) || !double.IsFinite(y))
            return false;

        var safeAnim = anim ?? string.Empty;
        if (_count > 0)
        {
            var newest = At(_count - 1);
            if (hostMs < newest.HostMs - StreamResetMs)
            {
                Clear();
            }
            else
            {
                if (hostMs <= newest.HostMs)
                    return false;
                if (!cut && hostMs - newest.HostMs > HoldGapMs &&
                    Math.Abs(newest.Dx) < StillVelocityRaw && Math.Abs(newest.Dy) < StillVelocityRaw)
                    Append(newest with { HostMs = hostMs - HoldLeadMs, Dx = 0.0, Dy = 0.0, Cut = false });
            }
        }

        Append(new Sample(hostMs, x, y, dir, Finite(dx), Finite(dy), safeAnim, cut));
        return true;
    }

    public bool TrySample(double renderMs, out PuppetPose pose)
    {
        pose = default;
        if (_count == 0 || !double.IsFinite(renderMs))
            return false;

        Trim(renderMs);

        var oldest = At(0);
        if (_count == 1 || renderMs <= oldest.HostMs)
        {
            pose = ToPose(oldest, oldest.X, oldest.Y, false, renderMs);
            return true;
        }

        var newest = At(_count - 1);
        if (renderMs >= newest.HostMs)
        {
            pose = Extrapolate(renderMs);
            return true;
        }

        var i = _count - 2;
        while (i > 0 && At(i).HostMs > renderMs)
            i--;

        var a = At(i);
        var b = At(i + 1);
        if (b.Cut || IsTeleport(a, b))
        {
            pose = ToPose(a, a.X, a.Y, false, renderMs);
            return true;
        }

        var span = b.HostMs - a.HostMs;
        var t = span > 0.0 ? (renderMs - a.HostMs) / span : 1.0;
        var x = a.X + (b.X - a.X) * t;
        var y = a.Y + (b.Y - a.Y) * t;
        var basis = t >= 0.5 ? b : a;
        pose = new PuppetPose(x, y, basis.Dir, basis.Dx, basis.Dy, a.Anim, false, renderMs - a.HostMs);
        return true;
    }

    PuppetPose Extrapolate(double renderMs)
    {
        var newest = At(_count - 1);
        if (_count < 2 || newest.Cut)
            return ToPose(newest, newest.X, newest.Y, renderMs > newest.HostMs, renderMs);

        var prev = At(_count - 2);
        var span = newest.HostMs - prev.HostMs;
        if (span <= 0.0 || IsTeleport(prev, newest))
            return ToPose(newest, newest.X, newest.Y, renderMs > newest.HostMs, renderMs);

        var ahead = Math.Min(renderMs - newest.HostMs, MaxExtrapolationMs);
        var x = newest.X + (newest.X - prev.X) / span * ahead;
        var y = newest.Y + (newest.Y - prev.Y) / span * ahead;
        return ToPose(newest, x, y, ahead > 0.0, renderMs);
    }

    static PuppetPose ToPose(Sample s, double x, double y, bool extrapolated, double renderMs) =>
        new(x, y, s.Dir, s.Dx, s.Dy, s.Anim, extrapolated, renderMs - s.HostMs);

    static bool IsTeleport(Sample a, Sample b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var limit = TeleportBasePx + TeleportPxPerMs * Math.Max(0.0, b.HostMs - a.HostMs);
        return dx * dx + dy * dy > limit * limit;
    }

    static double Finite(double v) => double.IsFinite(v) ? v : 0.0;

    void Trim(double renderMs)
    {
        while (_count > 2 && At(1).HostMs <= renderMs)
        {
            _start = (_start + 1) % Capacity;
            _count--;
        }
    }

    void Append(Sample sample)
    {
        if (_count == Capacity)
        {
            _start = (_start + 1) % Capacity;
            _count--;
        }

        _ring[(_start + _count) % Capacity] = sample;
        _count++;
    }

    Sample At(int index) => _ring[(_start + index) % Capacity];
}

/*
 * ═══════════════════════════════════════
 *  MOB PUPPET TRACK — КАК РАБОТАЕТ
 * ═══════════════════════════════════════
 *  Буфер сэмплов одного моба по времени хоста: x, y, dir, dx, dy (сырые единицы игры), анимация, флаг разрыва.
 *  Push: старые/дубли отбрасываются; время назад > 5 с — очистка (новый поток хоста).
 *  Пауза > 100 мс без разрыва, а прошлый сэмпл стоял (|dx|, |dy| < 0.001) → вставляется его копия
 *  за 34 мс до нового: хост не шлёт неподвижного моба, он стоял до последнего кадра, а не полз всю паузу.
 *  Двигавшийся моб с паузой (потери) интерполируется как есть.
 *  TrySample(время рендера): lerp между соседями; разрыв (cut от хоста) или скачок > 96 + 1.2·dt px —
 *  держим прошлую точку до времени новой; за последним сэмплом экстраполяция по скорости ≤ 50 мс, дальше стоп.
 *  Dir/dx/dy — от ближайшего сэмпла, анимация — от сэмпла до времени рендера. AgeMs — возраст опорного сэмпла.
 *  Не потокобезопасен: пишется и читается из игрового потока.
 * ═══════════════════════════════════════
*/
