using System.Buffers.Binary;
using System.Globalization;

namespace DeadCellsMultiplayerMod.PortableCore;

internal readonly record struct MobAuditEntry(int SyncId, int Life);

internal readonly record struct MobAuditLocal(int SyncId, int Life, bool Alive, bool IsBoss);

internal readonly record struct MobAuditHpMismatch(int SyncId, int ClientLife, int HostLife);

internal sealed class MobAuditSnapshot
{
    public int Generation { get; init; }
    public int HostBacklogBytes { get; init; }
    public int HostRttMs { get; init; }
    public MobAuditEntry[] Alive { get; init; } = Array.Empty<MobAuditEntry>();
    public int[] Dead { get; init; } = Array.Empty<int>();
}

internal sealed class MobAuditResult
{
    public int HostAlive { get; set; }
    public int ClientAlive { get; set; }
    public int Matched { get; set; }
    public List<int> MissingOnClient { get; } = new();
    public List<int> DeadOnClientOnly { get; } = new();
    public List<int> ExtraOnClient { get; } = new();
    public List<int> ExtraConfirmedDead { get; } = new();
    public List<MobAuditHpMismatch> HpMismatch { get; } = new();

    public bool IsClean =>
        MissingOnClient.Count == 0 && DeadOnClientOnly.Count == 0 && ExtraOnClient.Count == 0 && HpMismatch.Count == 0;
}

internal static class MobAuditWire
{
    public const string Tag = "MOBAUDIT";
    public const int MaxEntries = 4096;
    const byte Version = 1;
    const int MaxBase64Chars = 256 * 1024;

    public static string BuildLine(
        int generation,
        int hostBacklogBytes,
        int hostRttMs,
        IReadOnlyList<MobAuditEntry> alive,
        IReadOnlyList<int> dead)
    {
        var aliveCount = Math.Min(alive?.Count ?? 0, MaxEntries);
        var deadCount = Math.Min(dead?.Count ?? 0, MaxEntries);
        var raw = new byte[1 + 2 + aliveCount * 8 + 2 + deadCount * 4];
        var span = raw.AsSpan();
        span[0] = Version;
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(1, 2), (ushort)aliveCount);
        var offset = 3;
        for (var i = 0; i < aliveCount; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(offset, 4), alive![i].SyncId);
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(offset + 4, 4), alive[i].Life);
            offset += 8;
        }

        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(offset, 2), (ushort)deadCount);
        offset += 2;
        for (var i = 0; i < deadCount; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(offset, 4), dead![i]);
            offset += 4;
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{Tag}|{generation}|{Math.Max(0, hostBacklogBytes)}|{Math.Max(0, hostRttMs)}|{Convert.ToBase64String(raw)}");
    }

    public static bool TryParse(string payload, out MobAuditSnapshot snapshot)
    {
        snapshot = new MobAuditSnapshot();
        if (string.IsNullOrWhiteSpace(payload))
            return false;

        var parts = payload.Trim().Split('|');
        if (parts.Length != 4 ||
            !TryParseInt(parts[0], out var generation) || generation == 0 ||
            !TryParseInt(parts[1], out var backlog) || backlog < 0 ||
            !TryParseInt(parts[2], out var rtt) || rtt < 0 ||
            parts[3].Length == 0 || parts[3].Length > MaxBase64Chars)
            return false;

        byte[] raw;
        try
        {
            raw = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        if (raw.Length < 5 || raw[0] != Version)
            return false;

        var span = raw.AsSpan();
        var aliveCount = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(1, 2));
        var offset = 3;
        if (aliveCount > MaxEntries || offset + aliveCount * 8 + 2 > raw.Length)
            return false;

        var alive = new MobAuditEntry[aliveCount];
        for (var i = 0; i < aliveCount; i++)
        {
            alive[i] = new MobAuditEntry(
                BinaryPrimitives.ReadInt32LittleEndian(span.Slice(offset, 4)),
                BinaryPrimitives.ReadInt32LittleEndian(span.Slice(offset + 4, 4)));
            offset += 8;
        }

        var deadCount = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(offset, 2));
        offset += 2;
        if (deadCount > MaxEntries || offset + deadCount * 4 != raw.Length)
            return false;

        var dead = new int[deadCount];
        for (var i = 0; i < deadCount; i++)
        {
            dead[i] = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(offset, 4));
            offset += 4;
        }

        snapshot = new MobAuditSnapshot
        {
            Generation = generation,
            HostBacklogBytes = backlog,
            HostRttMs = rtt,
            Alive = alive,
            Dead = dead
        };
        return true;
    }

    static bool TryParseInt(string value, out int result) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
}

internal static class MobAuditDiff
{
    public static bool IsHpMismatch(int clientLife, int hostLife)
    {
        var diff = Math.Abs(clientLife - hostLife);
        var tolerance = Math.Max(2, Math.Max(clientLife, hostLife) * 3 / 100);
        return diff > tolerance;
    }

    public static MobAuditResult Compare(MobAuditSnapshot host, IReadOnlyList<MobAuditLocal> local)
    {
        var result = new MobAuditResult();
        var hostLife = new Dictionary<int, int>(host.Alive.Length);
        foreach (var entry in host.Alive)
        {
            if (entry.SyncId > 0)
                hostLife[entry.SyncId] = entry.Life;
        }
        result.HostAlive = hostLife.Count;

        var dead = new HashSet<int>(host.Dead);
        var seen = new HashSet<int>();
        foreach (var mob in local)
        {
            if (mob.SyncId <= 0 || !seen.Add(mob.SyncId))
                continue;
            if (mob.Alive)
                result.ClientAlive++;

            if (hostLife.TryGetValue(mob.SyncId, out var life))
            {
                if (!mob.Alive)
                {
                    result.DeadOnClientOnly.Add(mob.SyncId);
                    continue;
                }

                result.Matched++;
                if (IsHpMismatch(mob.Life, life))
                    result.HpMismatch.Add(new MobAuditHpMismatch(mob.SyncId, mob.Life, life));
                continue;
            }

            if (!mob.Alive)
                continue;

            result.ExtraOnClient.Add(mob.SyncId);
            if (dead.Contains(mob.SyncId))
                result.ExtraConfirmedDead.Add(mob.SyncId);
        }

        foreach (var syncId in hostLife.Keys)
        {
            if (!seen.Contains(syncId))
                result.MissingOnClient.Add(syncId);
        }

        result.MissingOnClient.Sort();
        result.DeadOnClientOnly.Sort();
        result.ExtraOnClient.Sort();
        result.ExtraConfirmedDead.Sort();
        result.HpMismatch.Sort((a, b) => a.SyncId.CompareTo(b.SyncId));
        return result;
    }

    public static string FormatIds(IReadOnlyList<int> ids, int max = 8)
    {
        if (ids.Count == 0)
            return "[]";

        var shown = Math.Min(max, ids.Count);
        var parts = new string[shown];
        for (var i = 0; i < shown; i++)
            parts[i] = ids[i].ToString(CultureInfo.InvariantCulture);
        var tail = ids.Count > shown ? $",+{ids.Count - shown}" : string.Empty;
        return "[" + string.Join(",", parts) + tail + "]";
    }
}

internal sealed class MobAuditStreaks
{
    public const int RetryEvery = 5;

    readonly Dictionary<int, int> _streaks = new();
    readonly List<int> _drop = new();
    int _generation;

    public void Reset()
    {
        _streaks.Clear();
        _generation = 0;
    }

    public List<int> Update(int generation, IReadOnlyList<int> ids, int threshold)
    {
        if (generation != _generation)
        {
            _streaks.Clear();
            _generation = generation;
        }

        var present = new HashSet<int>(ids);
        _drop.Clear();
        foreach (var syncId in _streaks.Keys)
        {
            if (!present.Contains(syncId))
                _drop.Add(syncId);
        }
        foreach (var syncId in _drop)
            _streaks.Remove(syncId);

        var due = new List<int>();
        foreach (var syncId in ids)
        {
            _streaks.TryGetValue(syncId, out var streak);
            streak++;
            _streaks[syncId] = streak;
            if (streak >= threshold && (streak - threshold) % RetryEvery == 0)
                due.Add(syncId);
        }
        return due;
    }
}

/*
 * ═══════════════════════════════════════
 *  MOB AUDIT — КАК РАБОТАЕТ
 * ═══════════════════════════════════════
 *  MOBAUDIT|gen|hostQueueBytes|hostRttMs|base64 — хост раз в ~2 с: живые мобы уровня (syncId, life)
 *  и недавно умершие (надгробия). Бинарь: ver=1, u16 N, N×(i32 id, i32 life), u16 M, M×i32 id.
 *  Compare на клиенте по мобам с привязанным syncId:
 *    matched — жив у обоих; HpMismatch — разница HP > max(2, 3%);
 *    DeadOnClientOnly — у клиента мёртв, у хоста жив; MissingOnClient — у хоста жив, у клиента нет реплики;
 *    ExtraOnClient — у клиента жив, у хоста нет (ExtraConfirmedDead — хост знает его смерть).
 *  MobAuditStreaks: id подряд в N сверках одного gen → «пора чинить», повтор каждые 5 сверок; смена gen — сброс.
 * ═══════════════════════════════════════
*/
