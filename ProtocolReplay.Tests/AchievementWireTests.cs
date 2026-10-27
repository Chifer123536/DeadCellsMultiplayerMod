using DeadCellsMultiplayerMod.PortableCore;
using Xunit;

namespace DeadCellsMultiplayerMod.ProtocolReplay.Tests;

public sealed class AchievementWireTests
{
    [Fact]
    public void RoundTrips()
    {
        var line = AchievementWire.BuildLine(2, "Achievement_KillBoss_1");
        Assert.Equal("ACH|2|Achievement_KillBoss_1", line);

        Assert.True(AchievementWire.TryParse(Payload(line), out var message));
        Assert.Equal(new AchievementMessage(2, "Achievement_KillBoss_1"), message);
    }

    [Theory]
    [InlineData("2")]
    [InlineData("2|")]
    [InlineData("-1|Name")]
    [InlineData("x|Name")]
    [InlineData("2|Bad Name")]
    [InlineData("2|Name|extra")]
    [InlineData("2|Имя")]
    public void RejectsMalformed(string payload)
    {
        Assert.False(AchievementWire.TryParse(payload, out _));
    }

    [Fact]
    public void RejectsOverlongName()
    {
        Assert.False(AchievementWire.IsValidName(new string('A', AchievementWire.MaxNameLength + 1)));
        Assert.True(AchievementWire.IsValidName(new string('A', AchievementWire.MaxNameLength)));
    }

    static string Payload(string line) => line[(AchievementWire.Tag.Length + 1)..];
}
