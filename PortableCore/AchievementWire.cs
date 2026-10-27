using System.Globalization;

namespace DeadCellsMultiplayerMod.PortableCore;

internal readonly record struct AchievementMessage(int PlayerId, string Name);

internal static class AchievementWire
{
    public const string Tag = "ACH";
    public const int MaxNameLength = 64;

    public static bool IsValidName(string? name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > MaxNameLength)
            return false;
        foreach (var c in name)
        {
            if (!(c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '_'))
                return false;
        }
        return true;
    }

    public static string BuildLine(int playerId, string name) =>
        string.Create(CultureInfo.InvariantCulture, $"{Tag}|{playerId}|{name}");

    public static bool TryParse(string payload, out AchievementMessage message)
    {
        message = default;
        var parts = payload.Trim().Split('|');
        if (parts.Length != 2)
            return false;
        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var playerId) || playerId < 0)
            return false;
        if (!IsValidName(parts[1]))
            return false;

        message = new AchievementMessage(playerId, parts[1]);
        return true;
    }
}

/*
 * ═══════════════════════════════════════
 *  ACHIEVEMENT WIRE — КАК РАБОТАЕТ
 * ═══════════════════════════════════════
 *  ACH|id|name — игра игрока id выдала Steam-ачивку name (API-имя, [A-Za-z0-9_], до 64 символов).
 *  Получатель сам решает, открывать ли её у себя (настройка ShareAchievements).
 * ═══════════════════════════════════════
*/
