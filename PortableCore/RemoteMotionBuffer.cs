namespace DeadCellsMultiplayerMod.PortableCore;

internal sealed class RemoteMotionBuffer
{
    const int Capacity = 64;
    const double MinDelayMs = 30.0;
    const double MaxDelayMs = 250.0;
    const double DelaySafetyMs = 6.0;
    const double MaxExtrapolationMs = 66.0;
    const double OffsetRelaxPerMs = 0.01;
    const double JitterAttack = 0.3;
    const double JitterRelease = 0.01;
    const double IntervalBlend = 0.05;
    const double MaxIntervalSampleMs = 250.0;
    const double ClockSteerLimit = 0.05;
    const double ClockSteerSpanMs = 250.0;
    const double ClockResyncMs = 300.0;
    const double StreamGapResetMs = 1500.0;
    const double EventPairWindowMs = 50.0;
    const double TeleportBasePx = 96.0;
    const double TeleportPxPerMs = 1.2;

    readonly record struct Sample(double SenderMs, double X, double Y, int Dir);

    readonly Sample[] _ring = new Sample[Capacity];
    int _start;
    int _count;
    double _offsetMs = double.NaN;
    double _lastArrivalMs = double.NaN;
    double _jitterMs;
    double _intervalMs = 1000.0 / 60.0;
    double _renderMs = double.NaN;
    double _lastSampleNowMs = double.NaN;

    public bool HasSamples => _count > 0;
    public double RenderClockMs => _renderMs;
    public double JitterMs => _jitterMs;
    public double IntervalMs => _intervalMs;
    public double TargetDelayMs => Math.Clamp(_intervalMs * 1.5 + _jitterMs * 1.5 + DelaySafetyMs, MinDelayMs, MaxDelayMs);

    public void Reset()
    {
        _start = 0;
        _count = 0;
        _offsetMs = double.NaN;
        _lastArrivalMs = double.NaN;
        _jitterMs = 0.0;
        _intervalMs = 1000.0 / 60.0;
        _renderMs = double.NaN;
        _lastSampleNowMs = double.NaN;
    }

    public void Push(double senderMs, double arrivalMs, double x, double y, int dir)
    {
        if (!double.IsFinite(senderMs) || !double.IsFinite(arrivalMs) || !double.IsFinite(x) || !double.IsFinite(y))
            return;

        if (_count > 0)
        {
            var newest = At(_count - 1);
            if (senderMs <= newest.SenderMs)
                return;
            if (senderMs - newest.SenderMs > StreamGapResetMs || arrivalMs - _lastArrivalMs > StreamGapResetMs)
                Reset();
        }

        var offset = arrivalMs - senderMs;
        if (double.IsNaN(_offsetMs))
        {
            _offsetMs = offset;
        }
        else
        {
            var relaxed = _offsetMs + OffsetRelaxPerMs * Math.Max(0.0, arrivalMs - _lastArrivalMs);
            _offsetMs = Math.Min(offset, relaxed);
        }

        var lateness = offset - _offsetMs;
        _jitterMs += (lateness - _jitterMs) * (lateness > _jitterMs ? JitterAttack : JitterRelease);

        if (_count > 0)
        {
            var dt = senderMs - At(_count - 1).SenderMs;
            if (dt > 0.0 && dt < MaxIntervalSampleMs)
                _intervalMs += (dt - _intervalMs) * IntervalBlend;
        }

        _lastArrivalMs = arrivalMs;
        Append(new Sample(senderMs, x, y, dir));
    }

    public double StampForEvent(double arrivalMs)
    {
        if (_count == 0 || double.IsNaN(_offsetMs))
            return double.NaN;
        if (arrivalMs - _lastArrivalMs <= EventPairWindowMs)
            return At(_count - 1).SenderMs;
        return arrivalMs - _offsetMs;
    }

    public bool IsDue(double stampMs)
    {
        return double.IsNaN(stampMs) || double.IsNaN(_renderMs) || _renderMs >= stampMs;
    }

    public bool TrySample(double nowMs, out double x, out double y, out int dir)
    {
        x = 0.0;
        y = 0.0;
        dir = 0;
        if (_count == 0 || !double.IsFinite(nowMs))
            return false;

        AdvanceClock(nowMs);
        Trim();

        var r = _renderMs;
        var oldest = At(0);
        if (_count == 1 || r <= oldest.SenderMs)
        {
            x = oldest.X;
            y = oldest.Y;
            dir = oldest.Dir;
            return true;
        }

        var newest = At(_count - 1);
        if (r >= newest.SenderMs)
        {
            Extrapolate(r, out x, out y, out dir);
            return true;
        }

        var i = _count - 2;
        while (i > 0 && At(i).SenderMs > r)
            i--;

        var a = At(i);
        var b = At(i + 1);
        var span = b.SenderMs - a.SenderMs;
        if (IsTeleport(a, b))
        {
            x = a.X;
            y = a.Y;
            dir = a.Dir;
            return true;
        }

        var t = span > 0.0 ? (r - a.SenderMs) / span : 1.0;
        x = a.X + (b.X - a.X) * t;
        y = a.Y + (b.Y - a.Y) * t;
        dir = t >= 0.5 ? b.Dir : a.Dir;
        return true;
    }

    void AdvanceClock(double nowMs)
    {
        var target = nowMs - _offsetMs - TargetDelayMs;
        if (double.IsNaN(_renderMs) || double.IsNaN(_lastSampleNowMs) || Math.Abs(target - _renderMs) > ClockResyncMs)
        {
            _renderMs = target;
        }
        else
        {
            var dt = Math.Max(0.0, nowMs - _lastSampleNowMs);
            var steer = Math.Clamp((target - _renderMs) / ClockSteerSpanMs, -ClockSteerLimit, ClockSteerLimit);
            _renderMs += dt * (1.0 + steer);
        }

        _lastSampleNowMs = nowMs;
    }

    void Extrapolate(double r, out double x, out double y, out int dir)
    {
        var newest = At(_count - 1);
        x = newest.X;
        y = newest.Y;
        dir = newest.Dir;
        if (_count < 2)
            return;

        var prev = At(_count - 2);
        var span = newest.SenderMs - prev.SenderMs;
        if (span <= 0.0 || IsTeleport(prev, newest))
            return;

        var ahead = Math.Min(r - newest.SenderMs, MaxExtrapolationMs);
        x += (newest.X - prev.X) / span * ahead;
        y += (newest.Y - prev.Y) / span * ahead;
    }

    static bool IsTeleport(Sample a, Sample b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var limit = TeleportBasePx + TeleportPxPerMs * Math.Max(0.0, b.SenderMs - a.SenderMs);
        return dx * dx + dy * dy > limit * limit;
    }

    void Trim()
    {
        while (_count > 2 && At(1).SenderMs <= _renderMs)
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
 *  REMOTE MOTION BUFFER — КАК РАБОТАЕТ
 * ═══════════════════════════════════════
 *  Снапшот-интерполяция позиции удалённого игрока.
 *  Push: сэмпл (время отправителя, время прихода, x, y, dir). Старые/дубли отбрасываются.
 *  Смещение часов = min(приход − отправка) с медленным подъёмом (смена маршрута).
 *  Джиттер = опоздание относительно min, быстрая атака / медленный спад.
 *  Задержка показа = 1.5·интервал + 1.5·джиттер + 6 мс, в пределах 30–250 мс.
 *  Часы рендера идут за реальным временем, подруливают скорость ±5% к цели, при ошибке > 300 мс прыгают.
 *  TrySample: lerp между соседними сэмплами; телепорт (дистанция > 96 + 1.2·dt) — без lerp;
 *  за последним сэмплом — экстраполяция по скорости до 66 мс, дальше стоп.
 *  StampForEvent/IsDue: анимации и атаки выпускаются по тем же часам рендера, синхронно с позицией.
 *  Потокобезопасности нет — вызывающий держит свой lock.
 * ═══════════════════════════════════════
*/
