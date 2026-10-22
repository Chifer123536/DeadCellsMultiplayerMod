using DeadCellsMultiplayerMod.PortableCore;
using Xunit;

namespace DeadCellsMultiplayerMod.ProtocolReplay.Tests;

public sealed class CoopDlcMaskTests
{
    [Fact]
    public void Resolve_UsesOnlyDlcOwnedByEveryone()
    {
        var host = CoopDlcMask.All;
        var client = CoopDlcMask.RiseOfTheGiant | CoopDlcMask.BadSeed | CoopDlcMask.ReturnToCastlevania;

        var mask = CoopDlcMask.Resolve(CoopDlcMask.All, host, new[] { client });

        Assert.Equal(client, mask);
    }

    [Fact]
    public void Resolve_RespectsHostPreference()
    {
        var preference = CoopDlcMask.All & ~CoopDlcMask.ReturnToCastlevania;

        var mask = CoopDlcMask.Resolve(preference, CoopDlcMask.All, new[] { CoopDlcMask.All });

        Assert.Equal(preference, mask);
    }

    [Fact]
    public void Resolve_CannotEnableDlcTheHostLacks()
    {
        var mask = CoopDlcMask.Resolve(CoopDlcMask.All, CoopDlcMask.RiseOfTheGiant, new[] { CoopDlcMask.All });

        Assert.Equal(CoopDlcMask.RiseOfTheGiant, mask);
    }

    [Fact]
    public void Lines_RoundTrip()
    {
        var owned = CoopDlcMask.BadSeed | CoopDlcMask.FatalFalls;
        var ownedLine = CoopDlcMask.BuildOwnedLine(owned);
        Assert.True(CoopDlcMask.TryParseOwned(ownedLine[(CoopDlcMask.OwnedTag.Length + 1)..], out var parsedOwned));
        Assert.Equal(owned, parsedOwned);

        var sessionLine = CoopDlcMask.BuildSessionLine(CoopDlcMask.TheQueenAndTheSea, 7);
        Assert.True(CoopDlcMask.TryParseSession(sessionLine[(CoopDlcMask.SessionTag.Length + 1)..], out var mask, out var revision));
        Assert.Equal(CoopDlcMask.TheQueenAndTheSea, mask);
        Assert.Equal(7, revision);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("-1")]
    [InlineData("64")]
    public void MalformedOwned_IsRejected(string payload)
    {
        Assert.False(CoopDlcMask.TryParseOwned(payload, out _));
    }

    [Theory]
    [InlineData("3")]
    [InlineData("3|x")]
    [InlineData("128|1")]
    [InlineData("3|-2")]
    public void MalformedSession_IsRejected(string payload)
    {
        Assert.False(CoopDlcMask.TryParseSession(payload, out _, out _));
    }

    [Fact]
    public void Describe_ListsNamesInGameOrder()
    {
        Assert.Equal("The Bad Seed, Return to Castlevania", CoopDlcMask.Describe(CoopDlcMask.BadSeed | CoopDlcMask.ReturnToCastlevania));
        Assert.Equal("-", CoopDlcMask.Describe(0));
    }
}

/*
 * ═══════════════════════════════════════
 *  COOP DLC MASK TESTS
 * ═══════════════════════════════════════
 *  Пересечение владения, предпочтение хоста, запрет включить чужое DLC, формат DLCOWN/DLCSET.
 * ═══════════════════════════════════════
*/
