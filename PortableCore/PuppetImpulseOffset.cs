namespace DeadCellsMultiplayerMod.PortableCore;

internal sealed class PuppetImpulseOffset
{
    const double MaxOffsetPx = 96.0;
    const double MinHoldMs = 60.0;
    const double MaxHoldMs = 400.0;
    const double FadeMs = 120.0;

    double _offsetPx;
    double _peakPx;
    double _holdUntilMs = double.NaN;

    public bool IsActive => _offsetPx != 0.0;

    public void Reset()
    {
        _offsetPx = 0.0;
        _peakPx = 0.0;
        _holdUntilMs = double.NaN;
    }

    public void Add(double deltaPx, double nowMs, double holdMs)
    {
        if (!double.IsFinite(deltaPx) || !double.IsFinite(nowMs) || deltaPx == 0.0)
            return;

        _offsetPx = Math.Clamp(_offsetPx + deltaPx, -MaxOffsetPx, MaxOffsetPx);
        _peakPx = _offsetPx;
        var hold = Math.Clamp(double.IsFinite(holdMs) ? holdMs : MinHoldMs, MinHoldMs, MaxHoldMs);
        _holdUntilMs = nowMs + hold;
    }

    public double Sample(double nowMs)
    {
        if (_offsetPx == 0.0 || !double.IsFinite(nowMs))
            return _offsetPx;
        if (double.IsNaN(_holdUntilMs) || nowMs <= _holdUntilMs)
            return _offsetPx;

        var t = (nowMs - _holdUntilMs) / FadeMs;
        if (t >= 1.0)
        {
            Reset();
            return 0.0;
        }

        _offsetPx = _peakPx * (1.0 - t);
        return _offsetPx;
    }
}

/*
 * ═══════════════════════════════════════
 *  PUPPET IMPULSE OFFSET — КАК РАБОТАЕТ
 * ═══════════════════════════════════════
 *  Визуальное смещение куклы по X от локального отброса (удар клиента по реплике).
 *  Add: копит смещение (≤ 96 px), держит его holdMs (60–400 мс, вызывающий передаёт RTT + задержку буфера —
 *  столько идёт отброс хоста до буфера), затем линейно гасит за 120 мс.
 *  Новый удар во время затухания продолжает с текущего значения и перезапускает удержание.
 * ═══════════════════════════════════════
*/
