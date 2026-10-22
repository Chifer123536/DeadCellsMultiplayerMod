using DeadCellsMultiplayerMod.PortableCore;
using Xunit;

namespace DeadCellsMultiplayerMod.ProtocolReplay.Tests;

public sealed class CoopBalanceTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void SoloIsVanilla(int difficulty)
    {
        var values = CoopBalance.For((CoopDifficulty)difficulty, 1);
        Assert.Equal(1.0, values.MobHp);
        Assert.Equal(1.0, values.BossHp);
        Assert.Equal(1.0, values.MobDamage);
    }

    [Fact]
    public void StandardDuoValues()
    {
        var values = CoopBalance.For(CoopDifficulty.Standard, 2);
        Assert.Equal(1.6, values.MobHp, 6);
        Assert.Equal(2.5, values.BossHp, 6);
        Assert.Equal(1.15, values.MobDamage, 6);
    }

    [Fact]
    public void DifficultiesAreOrdered()
    {
        for (var players = 2; players <= 4; players++)
        {
            var relaxed = CoopBalance.For(CoopDifficulty.Relaxed, players);
            var standard = CoopBalance.For(CoopDifficulty.Standard, players);
            var hard = CoopBalance.For(CoopDifficulty.Hard, players);
            Assert.True(relaxed.MobHp < standard.MobHp && standard.MobHp < hard.MobHp);
            Assert.True(relaxed.BossHp < standard.BossHp && standard.BossHp < hard.BossHp);
            Assert.True(relaxed.MobDamage < standard.MobDamage && standard.MobDamage < hard.MobDamage);
        }
    }

    [Fact]
    public void PlayerCountIsClamped()
    {
        Assert.Equal(CoopBalance.For(CoopDifficulty.Hard, 4), CoopBalance.For(CoopDifficulty.Hard, 10));
        Assert.Equal(CoopBalance.For(CoopDifficulty.Hard, 1), CoopBalance.For(CoopDifficulty.Hard, 0));
        Assert.Equal(CoopBalance.For(CoopDifficulty.Hard, 1), CoopBalance.For(CoopDifficulty.Hard, -3));
    }

    [Theory]
    [InlineData(10, 1.15, 12)]
    [InlineData(1, 1.15, 1)]
    [InlineData(1, 0.25, 1)]
    [InlineData(0, 2.0, 0)]
    [InlineData(-5, 2.0, -5)]
    [InlineData(100, 1.0, 100)]
    [InlineData(100, double.NaN, 100)]
    [InlineData(100, 50.0, 800)]
    [InlineData(100, -1.0, 100)]
    public void ScaleDamage(int damage, double multiplier, int expected)
    {
        Assert.Equal(expected, CoopBalance.ScaleDamage(damage, multiplier));
    }

    [Fact]
    public void ScaleDamageDoesNotOverflow()
    {
        Assert.Equal(int.MaxValue, CoopBalance.ScaleDamage(int.MaxValue, 8.0));
    }

    [Fact]
    public void LineRoundTrips()
    {
        var line = CoopBalance.BuildLine(CoopDifficulty.Hard, 1.3);
        Assert.Equal("BALANCE|hard|1.3", line);
        Assert.True(CoopBalance.TryParsePayload(line["BALANCE|".Length..], out var difficulty, out var damage));
        Assert.Equal(CoopDifficulty.Hard, difficulty);
        Assert.Equal(1.3, damage, 6);
    }

    [Theory]
    [InlineData("")]
    [InlineData("hard")]
    [InlineData("insane|1.2")]
    [InlineData("hard|abc")]
    [InlineData("hard|-1")]
    [InlineData("hard|NaN")]
    [InlineData("hard|Infinity")]
    public void RejectsMalformedPayload(string payload)
    {
        Assert.False(CoopBalance.TryParsePayload(payload, out _, out _));
    }

    [Fact]
    public void ParseFallsBackToStandardAndNextCycles()
    {
        Assert.Equal(CoopDifficulty.Standard, CoopBalance.Parse(null));
        Assert.Equal(CoopDifficulty.Standard, CoopBalance.Parse("whatever"));
        Assert.Equal(CoopDifficulty.Hard, CoopBalance.Parse(" HARD "));
        Assert.Equal(CoopDifficulty.Standard, CoopBalance.Next(CoopDifficulty.Relaxed));
        Assert.Equal(CoopDifficulty.Hard, CoopBalance.Next(CoopDifficulty.Standard));
        Assert.Equal(CoopDifficulty.Relaxed, CoopBalance.Next(CoopDifficulty.Hard));
    }
}

/*
 * ═══════════════════════════════════════
 *  COOP BALANCE TESTS
 * ═══════════════════════════════════════
 *  Соло = ваниль, стандарт вдвоём = ×1.6 / ×2.5 / ×1.15, сложности строго упорядочены,
 *  игроки зажаты 1..4, урон не обнуляется и не переполняется, провод BALANCE туда-обратно и мусор.
 * ═══════════════════════════════════════
*/
