using DeadCellsMultiplayerMod;
using DeadCellsMultiplayerMod.PortableCore;

public sealed partial class NetNode
{
    public readonly record struct RemoteDlcOwnership(int Id, string? Username, int OwnedMask);

    public void SendDlcOwned(int ownedMask)
    {
        if (!HasAnyConnection())
            return;
        SendRaw(CoopDlcMask.BuildOwnedLine(ownedMask));
    }

    public void SendDlcSession(int sessionMask, int revision)
    {
        if (_role != NetRole.Host || !HasAnyConnection())
            return;
        SendRaw(CoopDlcMask.BuildSessionLine(sessionMask, revision));
    }

    public List<RemoteDlcOwnership> GetRemoteDlcOwnership()
    {
        lock (_sync)
        {
            var result = new List<RemoteDlcOwnership>(_remotes.Count);
            foreach (var state in _remotes.Values)
            {
                if (state.HasRemote)
                    result.Add(new RemoteDlcOwnership(state.Id, state.Username, state.DlcOwnedMask));
            }
            return result;
        }
    }

    bool TryHandleDlcLine(string line, int? senderId, bool forceSenderId)
    {
        if (line.StartsWith(CoopDlcMask.OwnedTag + "|", StringComparison.Ordinal))
        {
            if (!senderId.HasValue)
                return true;
            if (!CoopDlcMask.TryParseOwned(line[(CoopDlcMask.OwnedTag.Length + 1)..], out var owned))
            {
                _log.Warning("[NetNode][DLC] Malformed {Line}", line);
                return true;
            }

            var remoteId = senderId.Value;
            lock (_sync)
            {
                var state = GetOrCreateRemoteLocked(remoteId);
                state.DlcOwnedMask = owned;
                state.HasRemote = true;
                _hasRemote = true;
            }

            CoopDlcSession.OnRemoteOwnedMask(remoteId, owned);
            return true;
        }

        if (line.StartsWith(CoopDlcMask.SessionTag + "|", StringComparison.Ordinal))
        {
            if (_role == NetRole.Host)
                return true;
            if (!CoopDlcMask.TryParseSession(line[(CoopDlcMask.SessionTag.Length + 1)..], out var mask, out var revision))
            {
                _log.Warning("[NetNode][DLC] Malformed {Line}", line);
                return true;
            }

            CoopDlcSession.OnHostSessionMask(mask, revision);
            return true;
        }

        return false;
    }
}

/*
 * ═══════════════════════════════════════
 *  NETNODE DLC — КАК РАБОТАЕТ
 * ═══════════════════════════════════════
 *  DLCOWN: пир сообщает свою реальную маску DLC; хранится в RemotePlayerState.DlcOwnedMask.
 *  DLCSET: хост рассылает выбранный набор сессии; клиент применяет через CoopDlcSession.
 *  Обработка идёт в HandleLine (главный поток) — CoopDlcSession трогает прокси игры.
 *  Пир у клиента — всегда хост (senderId = 1).
 * ═══════════════════════════════════════
*/
