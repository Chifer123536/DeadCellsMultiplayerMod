namespace DeadCellsMultiplayerMod.PortableCore;

internal sealed class HostSnapshotClock
{
    const double MinDelayMs = 50.0;
    const double MaxDelayMs = 250.0;
    const double DelaySafetyMs = 10.0;
    const double OffsetRelaxPerMs = 0.01;
    const double JitterAttack = 0.3;
    const double JitterRelease = 0.01;
    const double IntervalBlend = 0.05;
    const double MinIntervalSampleMs = 1.0;
    const double MaxIntervalSampleMs = 250.0;
    const double ClockSteerLimit = 0.05;
    const double ClockSteerSpanMs = 250.0;
    const double ClockResyncMs = 300.0;
    const double StreamResetMs = 5000.0;

    readonly object _sync = new();
    double _offsetMs = double.NaN;
    double _lastArrivalMs = double.NaN;
    double _lastSenderMs = double.NaN;
    double _jitterMs;
    double _intervalMs = 1000.0 / 30.0;
    double _renderMs = double.NaN;
    double _lastNowMs = double.NaN;

    public bool IsSynced
    {
        get { lock (_sync) return !double.IsNaN(_offsetMs); }
    }

    public double JitterMs
    {
        get { lock (_sync) return _jitterMs; }
    }

    public double IntervalMs
    {
        get { lock (_sync) return _intervalMs; }
    }

    public double TargetDelayMs
    {
        get { lock (_sync) return TargetDelayLocked(); }
    }

    public void Reset()
    {
        lock (_sync)
            ResetLocked();
    }

    public void Observe(double senderMs, double arrivalMs, bool realtime)
    {
        if (!double.IsFinite(senderMs) || !double.IsFinite(arrivalMs) || senderMs <= 0.0)
            return;

        lock (_sync)
        {
            var offset = arrivalMs - senderMs;
            if (!double.IsNaN(_offsetMs) && Math.Abs(offset - _offsetMs) > StreamResetMs)
                ResetLocked();

            if (double.IsNaN(_offsetMs))
            {
                _offsetMs = offset;
                _lastArrivalMs = arrivalMs;
                _lastSenderMs = senderMs;
                return;
            }

            if (!realtime)
            {
                if (offset < _offsetMs)
                    _offsetMs = offset;
                return;
            }

            var relaxed = _offsetMs + OffsetRelaxPerMs * Math.Max(0.0, arrivalMs - _lastArrivalMs);
            _offsetMs = Math.Min(offset, relaxed);

            var lateness = offset - _offsetMs;
            _jitterMs += (lateness - _jitterMs) * (lateness > _jitterMs ? JitterAttack : JitterRelease);

            var dt = senderMs - _lastSenderMs;
            if (dt >= MinIntervalSampleMs && dt < MaxIntervalSampleMs)
                _intervalMs += (dt - _intervalMs) * IntervalBlend;

            _lastArrivalMs = arrivalMs;
            if (senderMs > _lastSenderMs)
                _lastSenderMs = senderMs;
        }
    }

    public double Advance(double nowMs)
    {
        if (!double.IsFinite(nowMs))
            return double.NaN;

        lock (_sync)
        {
            if (double.IsNaN(_offsetMs))
                return double.NaN;

            var target = nowMs - _offsetMs - TargetDelayLocked();
            if (double.IsNaN(_renderMs) || double.IsNaN(_lastNowMs) || Math.Abs(target - _renderMs) > ClockResyncMs)
            {
                _renderMs = target;
            }
            else
            {
                var dt = Math.Max(0.0, nowMs - _lastNowMs);
                var steer = Math.Clamp((target - _renderMs) / ClockSteerSpanMs, -ClockSteerLimit, ClockSteerLimit);
                _renderMs += dt * (1.0 + steer);
            }

            _lastNowMs = nowMs;
            return _renderMs;
        }
    }

    public double HostNow(double nowMs)
    {
        lock (_sync)
            return double.IsNaN(_offsetMs) ? double.NaN : nowMs - _offsetMs;
    }

    double TargetDelayLocked() =>
        Math.Clamp(_intervalMs * 1.5 + _jitterMs * 1.5 + DelaySafetyMs, MinDelayMs, MaxDelayMs);

    void ResetLocked()
    {
        _offsetMs = double.NaN;
        _lastArrivalMs = double.NaN;
        _lastSenderMs = double.NaN;
        _jitterMs = 0.0;
        _intervalMs = 1000.0 / 30.0;
        _renderMs = double.NaN;
        _lastNowMs = double.NaN;
    }
}

/*
 * ═══════════════════════════════════════
 *  HOST SNAPSHOT CLOCK — КАК РАБОТАЕТ
 * ═══════════════════════════════════════
 *  Общие часы хоста для всех мобов клиента. Источник — время хоста (мс Stopwatch) в MOBMOVE/MOBSTATE.
 *  Observe(realtime): смещение = min(приход − отправка) с подъёмом 1%/мс прихода, джиттер (быстрая атака,
 *  медленный спад), интервал пакетов (dt 1–250 мс). Надёжные пакеты (realtime=false) только опускают
 *  смещение: их опоздание из-за упорядоченного канала не раздувает джиттер.
 *  Расхождение смещения > 5 с (перезапуск хоста) → полный сброс.
 *  Задержка показа = 1.5·интервал + 1.5·джиттер + 10 мс, в пределах 50–250 мс.
 *  Advance(now): время рендера = now − смещение − задержка; идёт с реальным временем, подруливает ±5%,
 *  при ошибке > 300 мс прыгает. HostNow — текущее время хоста без задержки.
 *  Потокобезопасно: Observe из сетевого потока, Advance из игрового.
 * ═══════════════════════════════════════
*/
