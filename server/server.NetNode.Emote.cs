using DeadCellsMultiplayerMod.Emotes;
using DeadCellsMultiplayerMod.PortableCore;

public sealed partial class NetNode
{
    public void SendEmote(int seq, int emoteId, bool animate)
    {
        if (!HasAnyConnection())
            return;
        SendRaw(EmoteWire.BuildLine(ID, seq, emoteId, animate));
    }

    public void SendEmoteStop(int seq)
    {
        if (!HasAnyConnection())
            return;
        SendRaw(EmoteWire.BuildStopLine(ID, seq));
    }

    bool TryHandleEmoteLine(string line, int? senderId, bool forceSenderId, out string? forwardLine)
    {
        forwardLine = null;
        if (!line.StartsWith(EmoteWire.Tag + "|", StringComparison.Ordinal))
            return false;

        if (!EmoteWire.TryParse(line[(EmoteWire.Tag.Length + 1)..], out var message))
        {
            _log.Warning("[NetNode][Emote] Malformed {Line}", line);
            return true;
        }

        if (forceSenderId)
            message = message with { PlayerId = senderId!.Value };
        if (_role == NetRole.Host && senderId.HasValue)
            forwardLine = EmoteWire.BuildLine(message.PlayerId, message.Seq, message.EmoteId, message.Animate) + "\n";
        if (message.PlayerId != ID)
            EmoteSync.OnRemoteEmote(message);
        return true;
    }
}

/*
 * ═══════════════════════════════════════
 *  NETNODE EMOTE — КАК РАБОТАЕТ
 * ═══════════════════════════════════════
 *  EMOTE шлёт сам игрок со своим ID. Хост подменяет id на senderId и пересылает остальным.
 *  Свои строки (id == ID) игнорируются. Обработка в HandleLine на главном потоке.
 * ═══════════════════════════════════════
*/
