using DeadCellsMultiplayerMod;
using DeadCellsMultiplayerMod.PortableCore;

public sealed partial class NetNode
{
    public void SendCoopBalance(string line)
    {
        if (_role != NetRole.Host || !HasAnyConnection())
            return;
        SendRaw(line);
    }

    bool TryHandleBalanceLine(string line)
    {
        if (!line.StartsWith(CoopBalance.Tag + "|", StringComparison.Ordinal))
            return false;
        if (_role == NetRole.Host)
            return true;

        if (!CoopBalance.TryParsePayload(line[(CoopBalance.Tag.Length + 1)..], out var difficulty, out var mobDamage))
        {
            _log.Warning("[NetNode][Balance] Malformed {Line}", line);
            return true;
        }

        lock (_sync) _hasRemote = true;
        CoopBalanceSync.OnHostLine(difficulty, mobDamage);
        return true;
    }
}

/*
 * ═══════════════════════════════════════
 *  NETNODE BALANCE — КАК РАБОТАЕТ
 * ═══════════════════════════════════════
 *  BALANCE|<сложность>|<урон мобов> шлёт только хост (reliable), клиент отдаёт в CoopBalanceSync.
 *  Хост входящие BALANCE игнорирует. Обработка в HandleLine на главном потоке.
 * ═══════════════════════════════════════
*/
