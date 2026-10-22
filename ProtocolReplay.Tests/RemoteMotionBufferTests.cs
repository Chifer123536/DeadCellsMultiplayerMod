using DeadCellsMultiplayerMod.PortableCore;
using Xunit;

namespace DeadCellsMultiplayerMod.ProtocolReplay.Tests;

public sealed class RemoteMotionBufferTests
{
    const double FrameMs = 1000.0 / 60.0;
    const double SenderOrigin = 1_000_000.0;
    const double ClockOffset = 5_000.0;
    const double SpeedPxPerMs = 0.3;

    readonly record struct Packet(double SenderMs, double ArrivalMs, double X, double Y);

    static List<Packet> BuildStream(int frames, double latencyMs, double jitterMs, double lossRate, int seed, Func<double, double>? xOf = null)
    {
        var rng = new Random(seed);
        var packets = new List<Packet>(frames);
        xOf ??= s => s * SpeedPxPerMs;
        for (var k = 0; k < frames; k++)
        {
            var local = k * FrameMs;
            if (rng.NextDouble() < lossRate)
                continue;
            var arrival = local + ClockOffset + latencyMs + rng.NextDouble() * jitterMs;
            packets.Add(new Packet(SenderOrigin + local, arrival, xOf(local), 100.0));
        }

        packets.Sort((a, b) => a.ArrivalMs.CompareTo(b.ArrivalMs));
        return packets;
    }

    static List<double> RenderX(RemoteMotionBuffer buffer, List<Packet> packets, int frames, double phaseMs = 7.3)
    {
        var xs = new List<double>(frames);
        var next = 0;
        for (var j = 0; j < frames; j++)
        {
            var now = ClockOffset + j * FrameMs + phaseMs;
            while (next < packets.Count && packets[next].ArrivalMs <= now)
            {
                var p = packets[next++];
                buffer.Push(p.SenderMs, p.ArrivalMs, p.X, p.Y, 1);
            }

            if (buffer.TrySample(now, out var x, out _, out _))
                xs.Add(x);
        }

        return xs;
    }

    static double MaxStepError(List<double> xs, int warmup)
    {
        var expected = SpeedPxPerMs * FrameMs;
        var worst = 0.0;
        for (var i = warmup + 1; i < xs.Count; i++)
            worst = Math.Max(worst, Math.Abs(xs[i] - xs[i - 1] - expected) / expected);
        return worst;
    }

    [Fact]
    public void SteadyStream_IsSmooth()
    {
        var buffer = new RemoteMotionBuffer();
        var xs = RenderX(buffer, BuildStream(600, 40, 0, 0, 1), 600);

        Assert.True(MaxStepError(xs, 60) < 0.06, $"step error {MaxStepError(xs, 60):F3}");
        Assert.InRange(buffer.TargetDelayMs, 30.0, 40.0);
    }

    [Fact]
    public void JitteryReorderedStream_StaysMonotonicAndSmooth()
    {
        var buffer = new RemoteMotionBuffer();
        var xs = RenderX(buffer, BuildStream(1200, 60, 40, 0, 2), 1200);

        for (var i = 121; i < xs.Count; i++)
            Assert.True(xs[i] >= xs[i - 1], $"went backwards at frame {i}");
        Assert.True(MaxStepError(xs, 120) < 0.12, $"step error {MaxStepError(xs, 120):F3}");
        Assert.True(buffer.TargetDelayMs > 40.0, $"delay {buffer.TargetDelayMs:F1} did not adapt to jitter");
    }

    [Fact]
    public void LossyStream_HasNoVisibleJumps()
    {
        var buffer = new RemoteMotionBuffer();
        var xs = RenderX(buffer, BuildStream(1200, 50, 15, 0.2, 3), 1200);

        Assert.True(MaxStepError(xs, 120) < 0.15, $"step error {MaxStepError(xs, 120):F3}");
    }

    [Fact]
    public void Teleport_IsNotInterpolated()
    {
        var buffer = new RemoteMotionBuffer();
        var xs = RenderX(buffer, BuildStream(300, 40, 10, 0, 4, s => s < 2000 ? 0.0 : 5000.0), 300);

        Assert.Contains(xs, x => x == 0.0);
        Assert.Contains(xs, x => x == 5000.0);
        Assert.DoesNotContain(xs, x => x > 0.0 && x < 5000.0);
    }

    [Fact]
    public void StaleAndDuplicatePackets_AreIgnored()
    {
        var buffer = new RemoteMotionBuffer();
        buffer.Push(100, 1100, 10, 0, 1);
        buffer.Push(200, 1200, 20, 0, 1);
        buffer.Push(150, 1210, 999, 0, 1);
        buffer.Push(200, 1215, 999, 0, 1);

        Assert.True(buffer.TrySample(1400, out var x, out _, out _));
        Assert.NotEqual(999.0, x);
        Assert.True(x <= 20.0 + SpeedPxPerMs * 66.0 + 1e-6);
    }

    [Fact]
    public void StreamStop_ExtrapolatesAtMostCap()
    {
        var buffer = new RemoteMotionBuffer();
        var packets = BuildStream(120, 40, 0, 0, 5);
        RenderX(buffer, packets, 120);
        var last = packets[^1];

        Assert.True(buffer.TrySample(last.ArrivalMs + 2000, out var x, out _, out _));
        Assert.True(x <= last.X + SpeedPxPerMs * 66.0 + 1e-6, $"overshoot {x - last.X:F1}px");
    }

    [Fact]
    public void Events_ReleaseWithRenderClock()
    {
        var buffer = new RemoteMotionBuffer();
        Assert.True(buffer.IsDue(buffer.StampForEvent(0)));

        var packets = BuildStream(120, 40, 0, 0, 6);
        RenderX(buffer, packets, 60);
        var arrival = packets[59].ArrivalMs;
        var stamp = buffer.StampForEvent(arrival + 1);

        Assert.Equal(packets.Where(p => p.ArrivalMs <= ClockOffset + 59 * FrameMs + 7.3).Max(p => p.SenderMs), stamp);
        Assert.False(buffer.IsDue(stamp));

        var now = ClockOffset + 59 * FrameMs + 7.3;
        var released = false;
        for (var j = 0; j < 30 && !released; j++)
        {
            now += FrameMs;
            buffer.TrySample(now, out _, out _, out _);
            released = buffer.IsDue(stamp);
        }

        Assert.True(released);
    }

    [Fact]
    public void LongGap_ResetsTimebase()
    {
        var buffer = new RemoteMotionBuffer();
        buffer.Push(100, 1100, 10, 0, 1);
        buffer.Push(117, 1117, 15, 0, 1);
        buffer.Push(5000, 9000, 500, 0, 1);

        Assert.True(buffer.TrySample(9100, out var x, out _, out _));
        Assert.Equal(500.0, x);
    }
}

/*
 * ═══════════════════════════════════════
 *  REMOTE MOTION BUFFER TESTS
 * ═══════════════════════════════════════
 *  Симуляция потока позиций 60 Гц: задержка, джиттер (переупорядочивание), потери, телепорт, обрыв.
 *  Метрика плавности — отклонение шага между кадрами рендера от равномерного движения.
 * ═══════════════════════════════════════
*/
