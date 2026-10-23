using DeadCellsMultiplayerMod;
using DeadCellsMultiplayerMod.PortableCore;

public sealed partial class NetNode
{
    public void SendScrollConfig(string line)
    {
        if (_role != NetRole.Host || !HasAnyConnection())
            return;
        SendRaw(line);
    }

    public void SendScrollOpen(int seq, string kindId, int index)
    {
        if (!HasAnyConnection() || !ScrollChoiceWire.IsValidKindId(kindId))
            return;
        SendRaw(ScrollChoiceWire.BuildOpenLine(ID, seq, kindId, index));
    }

    public void SendScrollSelect(int seq, int index)
    {
        if (!HasAnyConnection())
            return;
        SendRaw(ScrollChoiceWire.BuildSelectLine(ID, seq, index));
    }

    public void SendScrollEnd(int seq, int index)
    {
        if (!HasAnyConnection())
            return;
        SendRaw(ScrollChoiceWire.BuildEndLine(ID, seq, index));
    }

    public void SendScrollEndOnBehalf(int playerId, int seq)
    {
        if (_role != NetRole.Host || !HasAnyConnection())
            return;
        SendRaw(ScrollChoiceWire.BuildEndLine(playerId, seq, -1));
    }

    public bool IsRemotePresent(int playerId)
    {
        lock (_sync)
            return _remotes.TryGetValue(playerId, out var state) && state.HasRemote;
    }

    bool TryHandleScrollChoiceLine(string line, int? senderId, bool forceSenderId, out string? forwardLine)
    {
        forwardLine = null;
        if (!line.StartsWith("SCROLL", StringComparison.Ordinal))
            return false;

        var bar = line.IndexOf('|');
        if (bar <= 0)
            return false;

        var tag = line[..bar];
        var payload = line[(bar + 1)..];

        switch (tag)
        {
            case ScrollChoiceWire.ConfigTag:
            {
                if (_role == NetRole.Host)
                    return true;
                if (!ScrollChoiceWire.TryParseConfig(payload, out var mode))
                {
                    _log.Warning("[NetNode][Scroll] Malformed {Line}", line);
                    return true;
                }
                lock (_sync) _hasRemote = true;
                ScrollChoiceSync.OnHostConfig(mode);
                return true;
            }

            case ScrollChoiceWire.OpenTag:
            {
                if (!ScrollChoiceWire.TryParseOpen(payload, out var open))
                {
                    _log.Warning("[NetNode][Scroll] Malformed {Line}", line);
                    return true;
                }
                if (forceSenderId)
                    open = open with { PlayerId = senderId!.Value };
                if (_role == NetRole.Host && senderId.HasValue)
                    forwardLine = ScrollChoiceWire.BuildOpenLine(open.PlayerId, open.Seq, open.KindId, open.Index) + "\n";
                if (open.PlayerId != ID)
                    ScrollChoiceSync.OnRemoteOpen(open);
                return true;
            }

            case ScrollChoiceWire.SelectTag:
            case ScrollChoiceWire.EndTag:
            {
                if (!ScrollChoiceWire.TryParseIndexMessage(payload, out var message))
                {
                    _log.Warning("[NetNode][Scroll] Malformed {Line}", line);
                    return true;
                }
                if (forceSenderId)
                    message = message with { PlayerId = senderId!.Value };

                var isEnd = tag == ScrollChoiceWire.EndTag;
                if (_role == NetRole.Host && senderId.HasValue)
                {
                    forwardLine = (isEnd
                        ? ScrollChoiceWire.BuildEndLine(message.PlayerId, message.Seq, message.Index)
                        : ScrollChoiceWire.BuildSelectLine(message.PlayerId, message.Seq, message.Index)) + "\n";
                }

                if (message.PlayerId != ID)
                {
                    if (isEnd)
                        ScrollChoiceSync.OnRemoteEnd(message);
                    else
                        ScrollChoiceSync.OnRemoteSelect(message);
                }
                return true;
            }
        }

        return false;
    }
}

/*
 * ═══════════════════════════════════════
 *  NETNODE SCROLL CHOICE — КАК РАБОТАЕТ
 * ═══════════════════════════════════════
 *  SCROLLCFG шлёт только хост, клиент отдаёт режим в ScrollChoiceSync.
 *  SCROLLOPEN/SEL/END шлёт выбирающий со своим ID. Хост подменяет id на senderId
 *  и пересылает остальным клиентам. Свои же строки (id == ID) игнорируются.
 *  Ушедшего выбирающего хост закрывает за него: SCROLLEND|id|seq|-1.
 *  Обработка в HandleLine на главном потоке.
 * ═══════════════════════════════════════
*/
