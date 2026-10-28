using System.Globalization;

namespace DeadCellsMultiplayerMod.PortableCore;

internal enum DownStateKind
{
    Up = 0,
    Downed = 1,
    Dead = 2
}

internal readonly record struct ReviveHoldMessage(int ReviverId, int TargetId, bool Active);

internal readonly record struct ReviveDoneMessage(int TargetId, int ReviverId, double X, double Y, string LevelId);

internal static class ReviveWire
{
    public const string HoldTag = "RVHOLD";
    public const string DoneTag = "RVDONE";
    public const int MaxLevelIdChars = 96;

    public static int ToCode(DownStateKind kind) => (int)kind;

    public static DownStateKind FromFlags(bool isDowned, bool isDead) =>
        !isDowned ? DownStateKind.Up : isDead ? DownStateKind.Dead : DownStateKind.Downed;

    public static bool TryParseDownState(string? value, out DownStateKind kind)
    {
        kind = DownStateKind.Up;
        if (!int.TryParse(value?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var code))
            return false;
        if (code < (int)DownStateKind.Up || code > (int)DownStateKind.Dead)
            return false;
        kind = (DownStateKind)code;
        return true;
    }

    public static string BuildHoldLine(int reviverId, int targetId, bool active) =>
        string.Create(CultureInfo.InvariantCulture, $"{HoldTag}|{reviverId}|{targetId}|{(active ? 1 : 0)}");

    public static string BuildDoneLine(int targetId, int reviverId, double x, double y, string? levelId) =>
        string.Create(CultureInfo.InvariantCulture, $"{DoneTag}|{targetId}|{reviverId}|{x:0.##}|{y:0.##}|{SanitizeLevelId(levelId)}");

    public static bool TryParseHold(string payload, out ReviveHoldMessage message)
    {
        message = default;
        var parts = payload.Trim().Split('|');
        if (parts.Length != 3)
            return false;
        if (!TryParseId(parts[0], out var reviverId) ||
            !TryParseId(parts[1], out var targetId) ||
            reviverId == targetId ||
            !TryParseInt(parts[2], out var active) ||
            active is not (0 or 1))
            return false;

        message = new ReviveHoldMessage(reviverId, targetId, active == 1);
        return true;
    }

    public static bool TryParseDone(string payload, out ReviveDoneMessage message)
    {
        message = default;
        var parts = payload.Trim().Split('|');
        if (parts.Length != 5)
            return false;
        if (!TryParseId(parts[0], out var targetId) ||
            !TryParseId(parts[1], out var reviverId) ||
            targetId == reviverId ||
            !TryParseDouble(parts[2], out var x) ||
            !TryParseDouble(parts[3], out var y))
            return false;

        var levelId = parts[4].Trim();
        if (levelId.Length > MaxLevelIdChars)
            return false;

        message = new ReviveDoneMessage(targetId, reviverId, x, y, levelId);
        return true;
    }

    public static ReviveHoldMessage WithReviver(ReviveHoldMessage message, int senderId) =>
        message with { ReviverId = senderId };

    public static ReviveDoneMessage WithTarget(ReviveDoneMessage message, int senderId) =>
        message with { TargetId = senderId };

    static string SanitizeLevelId(string? levelId)
    {
        var value = (levelId ?? string.Empty)
            .Replace("|", "/", StringComparison.Ordinal)
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", string.Empty, StringComparison.Ordinal)
            .Trim();
        return value.Length > MaxLevelIdChars ? value[..MaxLevelIdChars] : value;
    }

    static bool TryParseId(string value, out int id) => TryParseInt(value, out id) && id > 0;

    static bool TryParseInt(string value, out int result) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);

    static bool TryParseDouble(string value, out double result) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result) && double.IsFinite(result);
}

/*
 * ═══════════════════════════════════════
 *  REVIVE WIRE — КАК РАБОТАЕТ
 * ═══════════════════════════════════════
 *  PDOWN|id|state|... — state: 0 встал, 1 упал (можно поднять), 2 мёртв до следующего перехода.
 *  RVHOLD|reviver|target|1/0 — шлёт поднимающий: начал / бросил удерживать.
 *  RVDONE|target|reviver|x|y|level — шлёт поднятый: подтверждение, по нему поднимающий
 *    тратит флягу, все рисуют вспышку в x,y если уровень совпал.
 *  Первый id — всегда отправитель; хост подменяет его на senderId и пересылает остальным.
 * ═══════════════════════════════════════
*/
