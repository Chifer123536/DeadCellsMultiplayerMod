using System.Globalization;

namespace DeadCellsMultiplayerMod.PortableCore;

internal enum ScrollChoiceMode
{
    Off = 0,
    Pause = 1,
    PauseAndMirror = 2
}

internal readonly record struct ScrollOpenMessage(int PlayerId, int Seq, string KindId, int Index);

internal readonly record struct ScrollIndexMessage(int PlayerId, int Seq, int Index);

internal static class ScrollChoiceWire
{
    public const string ConfigTag = "SCROLLCFG";
    public const string OpenTag = "SCROLLOPEN";
    public const string SelectTag = "SCROLLSEL";
    public const string EndTag = "SCROLLEND";

    public const int MaxKindIdChars = 64;
    public const int MaxTierIndex = 16;

    public static readonly ScrollChoiceMode[] AllModes = [ScrollChoiceMode.Off, ScrollChoiceMode.Pause, ScrollChoiceMode.PauseAndMirror];

    public static string ToWire(ScrollChoiceMode mode) => mode switch
    {
        ScrollChoiceMode.Off => "off",
        ScrollChoiceMode.Pause => "pause",
        _ => "mirror"
    };

    public static ScrollChoiceMode ParseMode(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "off" => ScrollChoiceMode.Off,
        "pause" => ScrollChoiceMode.Pause,
        _ => ScrollChoiceMode.PauseAndMirror
    };

    public static bool IsValidKindId(string? kindId)
    {
        if (string.IsNullOrEmpty(kindId) || kindId.Length > MaxKindIdChars)
            return false;
        foreach (var c in kindId)
        {
            if (!(c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_'))
                return false;
        }
        return true;
    }

    public static string BuildConfigLine(ScrollChoiceMode mode) => $"{ConfigTag}|{ToWire(mode)}";

    public static bool TryParseConfig(string payload, out ScrollChoiceMode mode)
    {
        mode = ScrollChoiceMode.Off;
        var value = payload.Trim();
        if (value is not ("off" or "pause" or "mirror"))
            return false;
        mode = ParseMode(value);
        return true;
    }

    public static string BuildOpenLine(int playerId, int seq, string kindId, int index) =>
        string.Create(CultureInfo.InvariantCulture, $"{OpenTag}|{playerId}|{seq}|{kindId}|{index}");

    public static string BuildSelectLine(int playerId, int seq, int index) =>
        string.Create(CultureInfo.InvariantCulture, $"{SelectTag}|{playerId}|{seq}|{index}");

    public static string BuildEndLine(int playerId, int seq, int index) =>
        string.Create(CultureInfo.InvariantCulture, $"{EndTag}|{playerId}|{seq}|{index}");

    public static bool TryParseOpen(string payload, out ScrollOpenMessage message)
    {
        message = default;
        var parts = payload.Trim().Split('|');
        if (parts.Length != 4)
            return false;
        if (!TryParseInt(parts[0], out var playerId) || playerId < 0 ||
            !TryParseInt(parts[1], out var seq) || seq <= 0 ||
            !IsValidKindId(parts[2]) ||
            !TryParseIndex(parts[3], out var index))
            return false;

        message = new ScrollOpenMessage(playerId, seq, parts[2], index);
        return true;
    }

    public static bool TryParseIndexMessage(string payload, out ScrollIndexMessage message)
    {
        message = default;
        var parts = payload.Trim().Split('|');
        if (parts.Length != 3)
            return false;
        if (!TryParseInt(parts[0], out var playerId) || playerId < 0 ||
            !TryParseInt(parts[1], out var seq) || seq <= 0 ||
            !TryParseIndex(parts[2], out var index))
            return false;

        message = new ScrollIndexMessage(playerId, seq, index);
        return true;
    }

    static bool TryParseIndex(string value, out int index) =>
        TryParseInt(value, out index) && index >= -1 && index <= MaxTierIndex;

    static bool TryParseInt(string value, out int result) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
}

/*
 * ═══════════════════════════════════════
 *  SCROLL CHOICE WIRE — КАК РАБОТАЕТ
 * ═══════════════════════════════════════
 *  SCROLLCFG|off|pause|mirror           — режим от хоста.
 *  SCROLLOPEN|id|seq|kindId|i           — игрок открыл выбор свитка (id предмета + текущий пункт).
 *  SCROLLSEL|id|seq|i                   — курсор сдвинулся.
 *  SCROLLEND|id|seq|i                   — выбор сделан (i) или окно закрыто без выбора (-1).
 *  seq растёт у каждого выбирающего, старые SEL/END отбрасываются. kindId только [A-Za-z0-9_].
 * ═══════════════════════════════════════
*/
