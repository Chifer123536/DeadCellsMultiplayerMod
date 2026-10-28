using DeadCellsMultiplayerMod.PortableCore;
using Xunit;

namespace DeadCellsMultiplayerMod.ProtocolReplay.Tests;

public sealed class ReviveWireTests
{
    [Theory]
    [InlineData(false, false, "0", 0)]
    [InlineData(false, true, "0", 0)]
    [InlineData(true, false, "1", 1)]
    [InlineData(true, true, "2", 2)]
    public void DownStateCodeRoundTrips(bool isDowned, bool isDead, string code, int expectedKind)
    {
        var expected = (DownStateKind)expectedKind;
        var kind = ReviveWire.FromFlags(isDowned, isDead);
        Assert.Equal(expected, kind);
        Assert.Equal(code, ReviveWire.ToCode(kind).ToString());
        Assert.True(ReviveWire.TryParseDownState(code, out var parsed));
        Assert.Equal(expected, parsed);
    }

    [Theory]
    [InlineData("3")]
    [InlineData("-1")]
    [InlineData("true")]
    [InlineData("")]
    [InlineData(null)]
    public void DownStateRejectsUnknownCodes(string? code)
    {
        Assert.False(ReviveWire.TryParseDownState(code, out _));
    }

    [Fact]
    public void HoldRoundTrips()
    {
        var line = ReviveWire.BuildHoldLine(2, 1, true);
        Assert.Equal("RVHOLD|2|1|1", line);
        Assert.True(ReviveWire.TryParseHold(Payload(line, ReviveWire.HoldTag), out var hold));
        Assert.Equal(new ReviveHoldMessage(2, 1, true), hold);

        Assert.True(ReviveWire.TryParseHold(Payload(ReviveWire.BuildHoldLine(3, 2, false), ReviveWire.HoldTag), out var stop));
        Assert.False(stop.Active);
    }

    [Fact]
    public void DoneRoundTripsWithLevel()
    {
        var line = ReviveWire.BuildDoneLine(1, 2, 16129.84, 1704.0, "PrisonStart");
        Assert.Equal("RVDONE|1|2|16129.84|1704|PrisonStart", line);
        Assert.True(ReviveWire.TryParseDone(Payload(line, ReviveWire.DoneTag), out var done));
        Assert.Equal(1, done.TargetId);
        Assert.Equal(2, done.ReviverId);
        Assert.Equal(16129.84, done.X, 2);
        Assert.Equal(1704.0, done.Y, 2);
        Assert.Equal("PrisonStart", done.LevelId);
    }

    [Fact]
    public void DoneSanitizesLevelSeparators()
    {
        var line = ReviveWire.BuildDoneLine(1, 2, 0, 0, "a|b\nc");
        Assert.True(ReviveWire.TryParseDone(Payload(line, ReviveWire.DoneTag), out var done));
        Assert.Equal("a/bc", done.LevelId);
    }

    [Fact]
    public void HostRewritesSenderId()
    {
        Assert.True(ReviveWire.TryParseHold("4|1|1", out var hold));
        Assert.Equal(3, ReviveWire.WithReviver(hold, 3).ReviverId);

        Assert.True(ReviveWire.TryParseDone("4|1|0|0|", out var done));
        Assert.Equal(3, ReviveWire.WithTarget(done, 3).TargetId);
    }

    [Theory]
    [InlineData("2|1")]
    [InlineData("2|1|1|0")]
    [InlineData("2|2|1")]
    [InlineData("0|1|1")]
    [InlineData("2|1|2")]
    [InlineData("x|1|1")]
    public void HoldRejectsMalformed(string payload)
    {
        Assert.False(ReviveWire.TryParseHold(payload, out _));
    }

    [Theory]
    [InlineData("1|2|0|0")]
    [InlineData("1|1|0|0|L")]
    [InlineData("1|2|NaN|0|L")]
    [InlineData("1|2|0|x|L")]
    [InlineData("-1|2|0|0|L")]
    public void DoneRejectsMalformed(string payload)
    {
        Assert.False(ReviveWire.TryParseDone(payload, out _));
    }

    static string Payload(string line, string tag) => line[(tag.Length + 1)..];
}
