using DeadCellsMultiplayerMod.Emotes;
using DeadCellsMultiplayerMod.PortableCore;
using Xunit;

namespace DeadCellsMultiplayerMod.ProtocolReplay.Tests;

public sealed class EmoteWireTests
{
    [Fact]
    public void StartRoundTrips()
    {
        var line = EmoteWire.BuildLine(3, 7, 5, true);
        Assert.Equal("EMOTE|3|7|5|1", line);

        Assert.True(EmoteWire.TryParse(Payload(line), out var message));
        Assert.Equal(new EmoteMessage(3, 7, 5, true), message);
        Assert.False(message.IsStop);
    }

    [Fact]
    public void IconOnlyStartRoundTrips()
    {
        Assert.True(EmoteWire.TryParse(Payload(EmoteWire.BuildLine(2, 1, 0, false)), out var message));
        Assert.Equal(new EmoteMessage(2, 1, 0, false), message);
    }

    [Fact]
    public void StopRoundTripsWithoutAnimateFlag()
    {
        var line = EmoteWire.BuildStopLine(4, 9);
        Assert.Equal("EMOTE|4|9|-1|0", line);

        Assert.True(EmoteWire.TryParse(Payload(line), out var message));
        Assert.True(message.IsStop);
        Assert.False(message.Animate);
        Assert.Equal(9, message.Seq);
    }

    [Fact]
    public void StopIgnoresAnimateFlag()
    {
        Assert.True(EmoteWire.TryParse("4|9|-1|1", out var message));
        Assert.True(message.IsStop);
        Assert.False(message.Animate);
    }

    [Theory]
    [InlineData("3|7|5")]
    [InlineData("3|7|5|1|0")]
    [InlineData("3|0|5|1")]
    [InlineData("-1|7|5|1")]
    [InlineData("3|7|32|1")]
    [InlineData("3|7|-2|0")]
    [InlineData("3|7|5|-1")]
    [InlineData("3|7|x|1")]
    [InlineData("")]
    public void RejectsMalformed(string payload)
    {
        Assert.False(EmoteWire.TryParse(payload, out _));
    }

    [Fact]
    public void CatalogIdsMatchWheelSlotsAndFitWire()
    {
        Assert.Equal(EmoteWheel.SlotCount, EmoteCatalog.All.Length);
        for (var i = 0; i < EmoteCatalog.All.Length; i++)
        {
            var def = EmoteCatalog.All[i];
            Assert.Equal(i, def.Id);
            Assert.True(EmoteWire.IsValidEmoteId(def.Id));
            Assert.NotEmpty(def.Anims);
            Assert.Equal(EmoteIcons.Size, def.Icon.Length);
            Assert.All(def.Icon, row => Assert.Equal(EmoteIcons.Size, row.Length));
        }
    }

    [Theory]
    [InlineData(0.0, -1.0, 0)]
    [InlineData(1.0, -1.0, 1)]
    [InlineData(1.0, 0.0, 2)]
    [InlineData(1.0, 1.0, 3)]
    [InlineData(0.0, 1.0, 4)]
    [InlineData(-1.0, 1.0, 5)]
    [InlineData(-1.0, 0.0, 6)]
    [InlineData(-1.0, -1.0, 7)]
    [InlineData(-0.2, -1.0, 0)]
    public void SlotFromVectorGoesClockwiseFromTop(double x, double y, int expected)
    {
        Assert.Equal(expected, EmoteWheel.SlotFromVector(x, y));
    }

    static string Payload(string line) => line[(line.IndexOf('|') + 1)..];
}

/*
 * ═══════════════════════════════════════
 *  EMOTE WIRE TESTS
 * ═══════════════════════════════════════
 *  Круговой разбор EMOTE (старт, только значок, стоп), отбраковка битых строк,
 *  соответствие каталога слотам колеса и картам иконок 9×9, направление → слот по часовой.
 * ═══════════════════════════════════════
*/
