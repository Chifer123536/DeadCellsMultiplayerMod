using DeadCellsMultiplayerMod;
using DeadCellsMultiplayerMod.PortableCore;

public sealed partial class NetNode
{
    public void SendAchievement(string name)
    {
        if (!HasAnyConnection() || !AchievementWire.IsValidName(name))
            return;
        SendRaw(AchievementWire.BuildLine(ID, name));
    }

    bool TryHandleAchievementLine(string line, int? senderId, bool forceSenderId, out string? forwardLine)
    {
        forwardLine = null;
        if (!line.StartsWith(AchievementWire.Tag + "|", StringComparison.Ordinal))
            return false;

        if (!AchievementWire.TryParse(line[(AchievementWire.Tag.Length + 1)..], out var message))
        {
            _log.Warning("[NetNode][Achievement] Malformed {Line}", line);
            return true;
        }

        if (forceSenderId)
            message = message with { PlayerId = senderId!.Value };
        if (_role == NetRole.Host && senderId.HasValue)
            forwardLine = AchievementWire.BuildLine(message.PlayerId, message.Name) + "\n";
        if (message.PlayerId != ID)
            SteamAchievementBridge.OnRemoteAchievement(message);
        return true;
    }
}

/*
 * ═══════════════════════════════════════
 *  NETNODE ACHIEVEMENT — КАК РАБОТАЕТ
 * ═══════════════════════════════════════
 *  ACH шлёт сам игрок со своим ID. Хост подменяет id на senderId и пересылает остальным.
 *  Свои строки (id == ID) игнорируются. Пир без ACH (тот же протокол 22) строку молча пропускает.
 * ═══════════════════════════════════════
*/
