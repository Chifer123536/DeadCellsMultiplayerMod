using DeadCellsMultiplayerMod.PortableCore;

public sealed partial class NetNode
{
    readonly PendingQueue<ReviveHoldMessage> _pendingReviveHolds = new(PendingControlStateLimit);
    readonly PendingQueue<ReviveDoneMessage> _pendingReviveDones = new(PendingControlStateLimit);

    public void SendReviveHold(int targetId, bool active)
    {
        if (!HasAnyConnection() || ID <= 0 || targetId <= 0 || targetId == ID)
            return;
        _ = SendLineSafe(ReviveWire.BuildHoldLine(ID, targetId, active) + "\n");
    }

    public void SendReviveDone(int reviverId, double x, double y, string? levelId)
    {
        if (!HasAnyConnection() || ID <= 0 || reviverId <= 0 || reviverId == ID)
            return;
        _ = SendLineSafe(ReviveWire.BuildDoneLine(ID, reviverId, x, y, levelId) + "\n");
    }

    internal bool TryConsumeReviveHolds(out List<ReviveHoldMessage> holds)
        => TryConsumePending(_pendingReviveHolds, out holds);

    internal bool TryConsumeReviveDones(out List<ReviveDoneMessage> dones)
        => TryConsumePending(_pendingReviveDones, out dones);

    void RemovePendingReviveMessagesForUser(int userId)
    {
        _pendingReviveHolds.RemoveAll(m => m.ReviverId == userId || m.TargetId == userId);
        _pendingReviveDones.RemoveAll(m => m.ReviverId == userId || m.TargetId == userId);
    }

    bool TryHandleReviveLine(string line, int? senderId, bool forceSenderId, out string? forwardLine)
    {
        forwardLine = null;
        if (line.StartsWith(ReviveWire.HoldTag + "|", StringComparison.Ordinal))
        {
            if (!ReviveWire.TryParseHold(line[(ReviveWire.HoldTag.Length + 1)..], out var hold))
            {
                _log.Warning("[NetNode][Revive] Malformed {Line}", line);
                return true;
            }

            if (forceSenderId && senderId.HasValue)
                hold = ReviveWire.WithReviver(hold, senderId.Value);
            if (hold.ReviverId == hold.TargetId)
                return true;
            if (_role == NetRole.Host && senderId.HasValue)
                forwardLine = ReviveWire.BuildHoldLine(hold.ReviverId, hold.TargetId, hold.Active) + "\n";
            if (hold.ReviverId != ID)
            {
                lock (_sync)
                {
                    AddBoundedLocked(_pendingReviveHolds, hold, PendingControlStateLimit);
                    _hasRemote = true;
                }
            }
            return true;
        }

        if (line.StartsWith(ReviveWire.DoneTag + "|", StringComparison.Ordinal))
        {
            if (!ReviveWire.TryParseDone(line[(ReviveWire.DoneTag.Length + 1)..], out var done))
            {
                _log.Warning("[NetNode][Revive] Malformed {Line}", line);
                return true;
            }

            if (forceSenderId && senderId.HasValue)
                done = ReviveWire.WithTarget(done, senderId.Value);
            if (done.ReviverId == done.TargetId)
                return true;
            if (_role == NetRole.Host && senderId.HasValue)
                forwardLine = ReviveWire.BuildDoneLine(done.TargetId, done.ReviverId, done.X, done.Y, done.LevelId) + "\n";
            if (done.TargetId != ID)
            {
                lock (_sync)
                {
                    AddBoundedLocked(_pendingReviveDones, done, PendingControlStateLimit);
                    _hasRemote = true;
                }
            }
            return true;
        }

        return false;
    }
}

/*
 * ═══════════════════════════════════════
 *  NETNODE REVIVE — КАК РАБОТАЕТ
 * ═══════════════════════════════════════
 *  RVHOLD шлёт поднимающий, RVDONE — поднятый (формат в ReviveWire).
 *  Хост подменяет первый id на senderId и пересылает остальным; свои строки не ставятся в очередь.
 *  Очереди забирает ModEntry.UpdateFakeDeathFlow на кадре героя, чистятся вместе с PDOWN/PREVIVE.
 * ═══════════════════════════════════════
*/
