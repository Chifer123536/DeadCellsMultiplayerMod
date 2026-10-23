using DeadCellsMultiplayerMod.PortableCore;
using Xunit;

namespace DeadCellsMultiplayerMod.ProtocolReplay.Tests;

public sealed class ScrollChoiceWireTests
{
    [Fact]
    public void OpenRoundTrips()
    {
        var line = ScrollChoiceWire.BuildOpenLine(3, 7, "BTUp", 1);
        Assert.Equal("SCROLLOPEN|3|7|BTUp|1", line);

        Assert.True(ScrollChoiceWire.TryParseOpen(Payload(line), out var message));
        Assert.Equal(new ScrollOpenMessage(3, 7, "BTUp", 1), message);
    }

    [Fact]
    public void SelectAndEndRoundTrip()
    {
        Assert.True(ScrollChoiceWire.TryParseIndexMessage(Payload(ScrollChoiceWire.BuildSelectLine(2, 4, 2)), out var select));
        Assert.Equal(new ScrollIndexMessage(2, 4, 2), select);

        Assert.True(ScrollChoiceWire.TryParseIndexMessage(Payload(ScrollChoiceWire.BuildEndLine(2, 4, -1)), out var end));
        Assert.Equal(new ScrollIndexMessage(2, 4, -1), end);
    }

    [Theory]
    [InlineData("3|7|BT|Up|1")]
    [InlineData("3|7|BT Up|1")]
    [InlineData("3|0|BTUp|1")]
    [InlineData("-1|7|BTUp|1")]
    [InlineData("3|7||1")]
    [InlineData("3|7|BTUp|99")]
    [InlineData("3|7|BTUp|-2")]
    [InlineData("3|7|BTUp")]
    public void OpenRejectsMalformed(string payload)
    {
        Assert.False(ScrollChoiceWire.TryParseOpen(payload, out _));
    }

    [Theory]
    [InlineData("1|1")]
    [InlineData("1|1|x")]
    [InlineData("1|1|2|3")]
    [InlineData("a|1|2")]
    public void IndexRejectsMalformed(string payload)
    {
        Assert.False(ScrollChoiceWire.TryParseIndexMessage(payload, out _));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void ConfigRoundTrips(int value)
    {
        var mode = (ScrollChoiceMode)value;
        Assert.True(ScrollChoiceWire.TryParseConfig(Payload(ScrollChoiceWire.BuildConfigLine(mode)), out var parsed));
        Assert.Equal(mode, parsed);
    }

    [Fact]
    public void ConfigRejectsUnknownMode()
    {
        Assert.False(ScrollChoiceWire.TryParseConfig("sometimes", out _));
    }

    [Fact]
    public void UnknownStoredModeFallsBackToMirror()
    {
        Assert.Equal(ScrollChoiceMode.PauseAndMirror, ScrollChoiceWire.ParseMode(null));
        Assert.Equal(ScrollChoiceMode.PauseAndMirror, ScrollChoiceWire.ParseMode("garbage"));
        Assert.Equal(ScrollChoiceMode.Off, ScrollChoiceWire.ParseMode(" OFF "));
    }

    static string Payload(string line) => line[(line.IndexOf('|') + 1)..];
}
