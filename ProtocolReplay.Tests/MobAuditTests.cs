using DeadCellsMultiplayerMod.Network;
using DeadCellsMultiplayerMod.PortableCore;
using Xunit;

namespace DeadCellsMultiplayerMod.ProtocolReplay.Tests;

public sealed class MobAuditTests
{
    static string Payload(string line) => line[(MobAuditWire.Tag.Length + 1)..];

    [Fact]
    public void AuditRoundTrips()
    {
        var alive = new[] { new MobAuditEntry(5, 120), new MobAuditEntry(9, 1), new MobAuditEntry(300, 4500) };
        var dead = new[] { 7, 11 };
        var line = MobAuditWire.BuildLine(-123456, 40960, 85, alive, dead);
        Assert.StartsWith("MOBAUDIT|-123456|40960|85|", line);

        Assert.True(MobAuditWire.TryParse(Payload(line), out var audit));
        Assert.Equal(-123456, audit.Generation);
        Assert.Equal(40960, audit.HostBacklogBytes);
        Assert.Equal(85, audit.HostRttMs);
        Assert.Equal(alive, audit.Alive);
        Assert.Equal(dead, audit.Dead);
    }

    [Fact]
    public void EmptyAuditRoundTrips()
    {
        var line = MobAuditWire.BuildLine(42, 0, 0, Array.Empty<MobAuditEntry>(), Array.Empty<int>());
        Assert.True(MobAuditWire.TryParse(Payload(line), out var audit));
        Assert.Empty(audit.Alive);
        Assert.Empty(audit.Dead);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0|0|0|AQAAAAA=")]
    [InlineData("5|-1|0|AQAAAAA=")]
    [InlineData("5|0|0|not-base64!")]
    [InlineData("5|0|0|AgAAAAA=")]
    [InlineData("5|0|0|AQEAAAA=")]
    [InlineData("5|0|0")]
    public void MalformedAuditIsRejected(string payload)
    {
        Assert.False(MobAuditWire.TryParse(payload, out _));
    }

    [Fact]
    public void TruncatedAuditIsRejected()
    {
        var line = MobAuditWire.BuildLine(3, 0, 0, new[] { new MobAuditEntry(1, 10) }, new[] { 2 });
        var parts = Payload(line).Split('|');
        var raw = Convert.FromBase64String(parts[3]);
        var cut = Convert.ToBase64String(raw.AsSpan(0, raw.Length - 1));
        Assert.False(MobAuditWire.TryParse($"{parts[0]}|{parts[1]}|{parts[2]}|{cut}", out _));
    }

    [Fact]
    public void CompareFindsEveryKindOfDrift()
    {
        var host = new MobAuditSnapshot
        {
            Generation = 1,
            Alive = new[]
            {
                new MobAuditEntry(1, 100),
                new MobAuditEntry(2, 100),
                new MobAuditEntry(3, 50),
                new MobAuditEntry(4, 80),
            },
            Dead = new[] { 6 }
        };
        var local = new[]
        {
            new MobAuditLocal(1, 100, true, false),
            new MobAuditLocal(2, 60, true, false),
            new MobAuditLocal(3, 0, false, false),
            new MobAuditLocal(5, 30, true, false),
            new MobAuditLocal(6, 10, true, false),
            new MobAuditLocal(7, 0, false, false),
        };

        var result = MobAuditDiff.Compare(host, local);

        Assert.False(result.IsClean);
        Assert.Equal(4, result.HostAlive);
        Assert.Equal(4, result.ClientAlive);
        Assert.Equal(2, result.Matched);
        Assert.Equal(new[] { 4 }, result.MissingOnClient);
        Assert.Equal(new[] { 3 }, result.DeadOnClientOnly);
        Assert.Equal(new[] { 5, 6 }, result.ExtraOnClient);
        Assert.Equal(new[] { 6 }, result.ExtraConfirmedDead);
        Assert.Equal(new[] { new MobAuditHpMismatch(2, 60, 100) }, result.HpMismatch);
    }

    [Fact]
    public void CompareIsCleanWhenInSync()
    {
        var host = new MobAuditSnapshot { Generation = 1, Alive = new[] { new MobAuditEntry(1, 100), new MobAuditEntry(2, 9) } };
        var local = new[] { new MobAuditLocal(1, 99, true, false), new MobAuditLocal(2, 9, true, true) };
        Assert.True(MobAuditDiff.Compare(host, local).IsClean);
    }

    [Theory]
    [InlineData(100, 98, false)]
    [InlineData(100, 97, false)]
    [InlineData(100, 96, true)]
    [InlineData(1000, 975, false)]
    [InlineData(1000, 960, true)]
    [InlineData(5, 2, true)]
    public void HpToleranceIsTwoOrThreePercent(int client, int host, bool mismatch)
    {
        Assert.Equal(mismatch, MobAuditDiff.IsHpMismatch(client, host));
    }

    [Fact]
    public void StreaksFireAtThresholdThenEveryRetry()
    {
        var streaks = new MobAuditStreaks();
        var ids = new[] { 4 };
        Assert.Empty(streaks.Update(1, ids, 3));
        Assert.Empty(streaks.Update(1, ids, 3));
        Assert.Equal(ids, streaks.Update(1, ids, 3));
        for (var i = 0; i < MobAuditStreaks.RetryEvery - 1; i++)
            Assert.Empty(streaks.Update(1, ids, 3));
        Assert.Equal(ids, streaks.Update(1, ids, 3));
    }

    [Fact]
    public void StreakBreaksWhenIdDisappearsOrGenerationChanges()
    {
        var streaks = new MobAuditStreaks();
        streaks.Update(1, new[] { 4 }, 3);
        streaks.Update(1, new[] { 4 }, 3);
        streaks.Update(1, Array.Empty<int>(), 3);
        Assert.Empty(streaks.Update(1, new[] { 4 }, 3));

        streaks.Update(1, new[] { 4 }, 3);
        Assert.Empty(streaks.Update(2, new[] { 4 }, 3));
    }

    [Fact]
    public void FormatIdsTruncates()
    {
        Assert.Equal("[]", MobAuditDiff.FormatIds(Array.Empty<int>()));
        Assert.Equal("[1,2,+3]", MobAuditDiff.FormatIds(new[] { 1, 2, 3, 4, 5 }, 2));
    }

    [Theory]
    [InlineData("12|340|500|1|Prison|7|1.0", (int)NetLineKind.Pos)]
    [InlineData("MOBSTATE2|AAAA", (int)NetLineKind.MobState)]
    [InlineData("MOBSTATE|1|2", (int)NetLineKind.MobState)]
    [InlineData("MOBMOVE|x", (int)NetLineKind.MobMove)]
    [InlineData("MOBEVENT|x", (int)NetLineKind.MobEvent)]
    [InlineData("MOBDIE|1|2", (int)NetLineKind.MobDie)]
    [InlineData("MOBREG|x", (int)NetLineKind.MobReg)]
    [InlineData("MOBHIT|x", (int)NetLineKind.MobHit)]
    [InlineData("MOBATK|x", (int)NetLineKind.MobAtk)]
    [InlineData("MOBDRAW|x", (int)NetLineKind.MobDraw)]
    [InlineData("MOBAUDIT|1|0|0|AA", (int)NetLineKind.MobAudit)]
    [InlineData("ANIM|1|idle", (int)NetLineKind.Anim)]
    [InlineData("HEADANIM|1|x", (int)NetLineKind.Anim)]
    [InlineData("HP|1|2", (int)NetLineKind.Hp)]
    [InlineData("ATK|1", (int)NetLineKind.Atk)]
    [InlineData("PING|12.5", (int)NetLineKind.Ping)]
    [InlineData("PING", (int)NetLineKind.Ping)]
    [InlineData("PONG|12.5", (int)NetLineKind.Pong)]
    [InlineData("EXITCOMMIT|1", (int)NetLineKind.Exit)]
    [InlineData("LGRAPH|x", (int)NetLineKind.Level)]
    [InlineData("INTERDOOR|x", (int)NetLineKind.Inter)]
    [InlineData("PDOWN|1", (int)NetLineKind.Other)]
    [InlineData("", (int)NetLineKind.Other)]
    public void LinesAreClassifiedByTag(string line, int kind)
    {
        Assert.Equal((NetLineKind)kind, NetHealth.Classify(line));
    }

    [Fact]
    public void KindsAreSortedByBytesAndTailIsFolded()
    {
        var lines = new long[NetHealth.KindCount];
        var bytes = new long[NetHealth.KindCount];
        lines[(int)NetLineKind.Pos] = 50;
        bytes[(int)NetLineKind.Pos] = 5 * 1024;
        lines[(int)NetLineKind.MobState] = 10;
        bytes[(int)NetLineKind.MobState] = 50 * 1024;
        lines[(int)NetLineKind.Ping] = 5;
        bytes[(int)NetLineKind.Ping] = 100;

        Assert.Equal("mstate 10.0(2) pos 1.0(10) rest 0.0(1)", NetHealth.FormatKinds(lines, bytes, 5.0, 2));
        Assert.Equal("-", NetHealth.FormatKinds(new long[NetHealth.KindCount], new long[NetHealth.KindCount], 5.0, 3));
    }
}

/*
 * ═══════════════════════════════════════
 *  MOB AUDIT TESTS — ЧТО ПРОВЕРЯЕТ
 * ═══════════════════════════════════════
 *  MOBAUDIT: круговой разбор, пустой аудит, отказ на мусор/обрезку/чужую версию.
 *  Compare: matched / missing / deadOnClient / extra / hostDead / HP, допуск HP max(2, 3%).
 *  MobAuditStreaks: срабатывание на N-й сверке, повтор каждые 5, сброс при пропаже id и смене gen.
 *  NetHealth: классификация строк по тегу и сводка типов по байтам с хвостом «rest».
 * ═══════════════════════════════════════
*/
