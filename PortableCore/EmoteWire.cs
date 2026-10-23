using System.Globalization;

namespace DeadCellsMultiplayerMod.PortableCore;

internal readonly record struct EmoteMessage(int PlayerId, int Seq, int EmoteId, bool Animate)
{
    public bool IsStop => EmoteId == EmoteWire.StopId;
}

internal static class EmoteWire
{
    public const string Tag = "EMOTE";
    public const int StopId = -1;
    public const int MaxEmoteId = 31;
    const int AnimateFlag = 1;

    public static bool IsValidEmoteId(int emoteId) => emoteId >= 0 && emoteId <= MaxEmoteId;

    public static string BuildLine(int playerId, int seq, int emoteId, bool animate) =>
        string.Create(CultureInfo.InvariantCulture, $"{Tag}|{playerId}|{seq}|{emoteId}|{(animate ? AnimateFlag : 0)}");

    public static string BuildStopLine(int playerId, int seq) => BuildLine(playerId, seq, StopId, false);

    public static bool TryParse(string payload, out EmoteMessage message)
    {
        message = default;
        var parts = payload.Trim().Split('|');
        if (parts.Length != 4)
            return false;
        if (!TryParseInt(parts[0], out var playerId) || playerId < 0 ||
            !TryParseInt(parts[1], out var seq) || seq <= 0 ||
            !TryParseInt(parts[2], out var emoteId) ||
            (emoteId != StopId && !IsValidEmoteId(emoteId)) ||
            !TryParseInt(parts[3], out var flags) || flags < 0)
            return false;

        message = new EmoteMessage(playerId, seq, emoteId, emoteId != StopId && (flags & AnimateFlag) != 0);
        return true;
    }

    static bool TryParseInt(string value, out int result) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
}

/*
 * ═══════════════════════════════════════
 *  EMOTE WIRE — КАК РАБОТАЕТ
 * ═══════════════════════════════════════
 *  EMOTE|id|seq|emoteId|flags — игрок показал эмоцию emoteId (0..31).
 *    flags bit0 = 1: герой играет анимацию эмоции, 0: только значок над головой.
 *  EMOTE|id|seq|-1|0          — эмоция с этим seq закончилась, призрак возвращается к своей анимации.
 *  seq растёт у отправителя с каждой новой эмоцией; стоп с чужим seq игнорируется.
 * ═══════════════════════════════════════
*/
