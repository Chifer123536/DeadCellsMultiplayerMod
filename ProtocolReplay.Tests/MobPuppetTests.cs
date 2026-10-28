using DeadCellsMultiplayerMod.PortableCore;
using Xunit;

namespace DeadCellsMultiplayerMod.ProtocolReplay.Tests;

public sealed class MobPuppetTests
{
    const double HostTickMs = 1000.0 / 30.0;
    const double RenderFrameMs = 1000.0 / 60.0;
    const double HostOrigin = 50_000.0;
    const double ClockOffset = 7_000.0;
    const double SpeedPxPerMs = 0.2;

    readonly record struct Packet(double HostMs, double ArrivalMs, double X);

    static List<Packet> BuildStream(int ticks, double latencyMs, double jitterMs, double lossRate, int seed)
    {
        var rng = new Random(seed);
        var packets = new List<Packet>(ticks);
        for (var k = 0; k < ticks; k++)
        {
            var host = HostOrigin + k * HostTickMs;
            if (rng.NextDouble() < lossRate)
                continue;
            var arrival = host + ClockOffset + latencyMs + rng.NextDouble() * jitterMs;
            packets.Add(new Packet(host, arrival, (host - HostOrigin) * SpeedPxPerMs));
        }

        packets.Sort((a, b) => a.ArrivalMs.CompareTo(b.ArrivalMs));
        return packets;
    }

    static List<double> Render(HostSnapshotClock clock, MobPuppetTrack track, List<Packet> packets, double durationMs)
    {
        var xs = new List<double>();
        var next = 0;
        for (var now = HostOrigin + ClockOffset; now < HostOrigin + ClockOffset + durationMs; now += RenderFrameMs)
        {
            while (next < packets.Count && packets[next].ArrivalMs <= now)
            {
                var p = packets[next++];
                clock.Observe(p.HostMs, p.ArrivalMs, realtime: true);
                track.Push(p.HostMs, p.X, 100.0, 1, SpeedPxPerMs * HostTickMs / 24.0, 0.0, "walk", cut: false);
            }

            var render = clock.Advance(now);
            if (track.TrySample(render, out var pose))
                xs.Add(pose.X);
        }

        return xs;
    }

    static double MaxStepError(List<double> xs, int warmup)
    {
        var expected = SpeedPxPerMs * RenderFrameMs;
        var worst = 0.0;
        for (var i = warmup + 1; i < xs.Count; i++)
            worst = Math.Max(worst, Math.Abs(xs[i] - xs[i - 1] - expected) / expected);
        return worst;
    }

    [Fact]
    public void Clock_SteadyStream_DelayCoversOneAndHalfTicks()
    {
        var clock = new HostSnapshotClock();
        foreach (var p in BuildStream(300, 40, 0, 0, 1))
            clock.Observe(p.HostMs, p.ArrivalMs, realtime: true);

        Assert.InRange(clock.TargetDelayMs, 55.0, 65.0);
        var now = HostOrigin + 300 * HostTickMs + ClockOffset + 40;
        Assert.InRange(clock.HostNow(now), HostOrigin + 300 * HostTickMs - 1, HostOrigin + 300 * HostTickMs + 1);
    }

    [Fact]
    public void Clock_LateReliablePackets_DoNotInflateJitter()
    {
        var clock = new HostSnapshotClock();
        foreach (var p in BuildStream(300, 40, 0, 0, 2))
        {
            clock.Observe(p.HostMs, p.ArrivalMs, realtime: true);
            clock.Observe(p.HostMs - 200, p.ArrivalMs + 5, realtime: false);
        }

        Assert.True(clock.JitterMs < 1.0, $"jitter {clock.JitterMs:F2}");
    }

    [Fact]
    public void Clock_HostRestart_Resyncs()
    {
        var clock = new HostSnapshotClock();
        foreach (var p in BuildStream(60, 40, 0, 0, 3))
            clock.Observe(p.HostMs, p.ArrivalMs, realtime: true);

        var arrival = HostOrigin + ClockOffset + 10_000;
        clock.Observe(1.0, arrival, realtime: true);
        Assert.InRange(clock.HostNow(arrival), 0.0, 2.0);
    }

    [Fact]
    public void Clock_UnsyncedReturnsNaN()
    {
        var clock = new HostSnapshotClock();
        Assert.True(double.IsNaN(clock.Advance(1000)));
        Assert.False(clock.IsSynced);
    }

    [Fact]
    public void Puppet_SteadyStream_IsSmooth()
    {
        var xs = Render(new HostSnapshotClock(), new MobPuppetTrack(), BuildStream(300, 40, 0, 0, 4), 9000);
        Assert.True(MaxStepError(xs, 60) < 0.08, $"step error {MaxStepError(xs, 60):F3}");
    }

    [Fact]
    public void Puppet_JitterAndLoss_StaysMonotonic()
    {
        var xs = Render(new HostSnapshotClock(), new MobPuppetTrack(), BuildStream(600, 60, 35, 0.1, 5), 18000);
        for (var i = 61; i < xs.Count; i++)
            Assert.True(xs[i] + 1e-9 >= xs[i - 1], $"backwards at {i}: {xs[i - 1]:F2} -> {xs[i]:F2}");
        Assert.True(MaxStepError(xs, 120) < 1.0, $"step error {MaxStepError(xs, 120):F3}");
    }

    [Fact]
    public void Track_CutHoldsUntilItsTime()
    {
        var track = new MobPuppetTrack();
        track.Push(1000, 10, 100, 1, 0, 0, "a", cut: false);
        track.Push(1033, 12, 100, 1, 0, 0, "a", cut: false);
        track.Push(1066, 500, 100, 1, 0, 0, "b", cut: true);

        Assert.True(track.TrySample(1050, out var mid));
        Assert.Equal(12.0, mid.X, 6);
        Assert.True(track.TrySample(1066, out var at));
        Assert.Equal(500.0, at.X, 6);
        Assert.Equal("b", at.Anim);
    }

    [Fact]
    public void Track_LongGap_HoldsOldPositionUntilJustBeforeNewSample()
    {
        var track = new MobPuppetTrack();
        track.Push(1000, 10, 100, 1, 0, 0, "idle", cut: false);
        track.Push(3000, 14, 100, 1, 0.1, 0, "walk", cut: false);

        Assert.True(track.TrySample(2500, out var held));
        Assert.Equal(10.0, held.X, 6);
        Assert.True(track.TrySample(2983, out var moving));
        Assert.InRange(moving.X, 11.9, 12.1);
    }

    [Fact]
    public void Track_ExtrapolationIsCapped()
    {
        var track = new MobPuppetTrack();
        track.Push(1000, 0, 100, 1, 0, 0, "", cut: false);
        track.Push(1100, 10, 100, 1, 0, 0, "", cut: false);

        Assert.True(track.TrySample(1400, out var pose));
        Assert.True(pose.Extrapolated);
        Assert.Equal(15.0, pose.X, 6);
    }

    [Fact]
    public void Track_TeleportByDistance_DoesNotSlide()
    {
        var track = new MobPuppetTrack();
        track.Push(1000, 0, 100, 1, 0, 0, "", cut: false);
        track.Push(1033, 400, 100, 1, 0, 0, "", cut: false);

        Assert.True(track.TrySample(1020, out var pose));
        Assert.Equal(0.0, pose.X, 6);
    }

    [Fact]
    public void Track_RejectsStaleAndResetsOnHostRestart()
    {
        var track = new MobPuppetTrack();
        Assert.True(track.Push(10_000, 1, 1, 1, 0, 0, "", false));
        Assert.False(track.Push(9_990, 2, 1, 1, 0, 0, "", false));
        Assert.True(track.Push(5, 3, 1, 1, 0, 0, "", false));
        Assert.Equal(1, track.Count);
        Assert.Equal(5.0, track.NewestHostMs);
    }

    [Fact]
    public void Impulse_HoldsThenFades()
    {
        var offset = new PuppetImpulseOffset();
        offset.Add(20, 1000, 150);
        Assert.Equal(20.0, offset.Sample(1100), 6);
        Assert.Equal(20.0, offset.Sample(1150), 6);
        Assert.Equal(10.0, offset.Sample(1210), 6);
        Assert.Equal(0.0, offset.Sample(1300), 6);
        Assert.False(offset.IsActive);
    }

    [Fact]
    public void Impulse_IsClamped()
    {
        var offset = new PuppetImpulseOffset();
        offset.Add(80, 0, 100);
        offset.Add(80, 10, 100);
        Assert.Equal(96.0, offset.Sample(20), 6);
    }

    [Theory]
    [InlineData(264.0, 10, 1.0)]
    [InlineData(240.0, 9, 1.0)]
    [InlineData(250.0, 10, 10.0 / 24.0)]
    public void CellMath_FeetStayAboveFloorCell(double y, int cell, double frac)
    {
        PuppetCellMath.ToCellFeetY(y, out var cy, out var yr);
        Assert.Equal(cell, cy);
        Assert.Equal(frac, yr, 9);
    }

    [Fact]
    public void CellMath_XUsesFloor()
    {
        PuppetCellMath.ToCellX(264.0, out var cx, out var xr);
        Assert.Equal(11, cx);
        Assert.Equal(0.0, xr, 9);
    }
}
