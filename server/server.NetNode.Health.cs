using System.Diagnostics;
using System.Globalization;
using DeadCellsMultiplayerMod.Network;
using DeadCellsMultiplayerMod.PortableCore;

public sealed partial class NetNode
{
    const int SendQueueSampleIntervalMs = 100;

    int _outgoingBacklogBytes;
    int _outgoingCongested;
    long _lastSendQueueSampleTicks;
    MobAuditSnapshot? _pendingMobAudit;

    public int OutgoingBacklogBytes => Volatile.Read(ref _outgoingBacklogBytes);

    public bool IsOutgoingCongested => Volatile.Read(ref _outgoingCongested) != 0;

    static string BuildPingLine() =>
        string.Create(CultureInfo.InvariantCulture, $"PING|{MonotonicMs():0.0}\n");

    bool TryHandleKeepAliveFastPath(string line, int? senderId)
    {
        if (line.Length < 4 || line[0] != 'P')
            return false;

        if (line.StartsWith("PONG|", StringComparison.Ordinal))
        {
            if (double.TryParse(line.AsSpan(5), NumberStyles.Float, CultureInfo.InvariantCulture, out var sentMs))
                NetHealth.RecordRtt(MonotonicMs() - sentMs);
            return true;
        }

        if (!line.StartsWith("PING", StringComparison.Ordinal))
            return false;

        lock (_sync) _hasRemote = true;
        if (line.Length > 5 && line[4] == '|')
            SendLineToPeer(senderId, "PONG|" + line[5..] + "\n");
        return true;
    }

    void SendLineToPeer(int? senderId, string line)
    {
        if (_role != NetRole.Host)
        {
            _ = SendLineSafe(line);
            return;
        }

        if (!senderId.HasValue)
            return;

        SteamClientConnection? steamClient = null;
        ClientConnection? tcpClient = null;
        lock (_clientsLock)
        {
            if (_useSteamTransport)
                _steamClients.TryGetValue(senderId.Value, out steamClient);
            else
                _clients.TryGetValue(senderId.Value, out tcpClient);
        }

        if (steamClient != null)
            _ = SendLineToSteamClientSafe(steamClient, line);
        else if (tcpClient != null)
            _ = SendLineToClientSafe(tcpClient, line);
    }

    int CountHealthPeers()
    {
        if (_role == NetRole.Host)
        {
            lock (_clientsLock)
                return _useSteamTransport ? _steamClients.Count : _clients.Count;
        }

        lock (_sync)
            return _hasRemote ? 1 : 0;
    }

    void RunHealthTick()
    {
        SampleSendQueue();
        if (_role == NetRole.Host || _role == NetRole.Client)
            NetHealth.TryFlush(_log, _role.ToString(), CountHealthPeers(), BuildMotionHealthDetail);
    }

    string BuildMotionHealthDetail()
    {
        var sb = new System.Text.StringBuilder(96);
        lock (_sync)
        {
            foreach (var pair in _remotes)
            {
                var buffer = pair.Value.Motion.Buffer;
                if (!buffer.HasSamples)
                    continue;
                if (sb.Length > 0)
                    sb.Append(' ');
                sb.Append('p').Append(pair.Key.ToString(CultureInfo.InvariantCulture))
                  .Append(" int=").Append(buffer.IntervalMs.ToString("0", CultureInfo.InvariantCulture))
                  .Append(" jit=").Append(buffer.JitterMs.ToString("0", CultureInfo.InvariantCulture))
                  .Append(" delay=").Append(buffer.TargetDelayMs.ToString("0", CultureInfo.InvariantCulture)).Append("ms");
            }
        }

        if (MobClock.IsSynced &&
            DeadCellsMultiplayerMod.PortableCore.MobPuppetStats.TryDrain(out var poses, out var extrapolated, out var groundSnaps, out var gated, out var late))
        {
            if (sb.Length > 0)
                sb.Append(' ');
            sb.Append("mobs int=").Append(MobClock.IntervalMs.ToString("0", CultureInfo.InvariantCulture))
              .Append(" jit=").Append(MobClock.JitterMs.ToString("0", CultureInfo.InvariantCulture))
              .Append(" delay=").Append(MobClock.TargetDelayMs.ToString("0", CultureInfo.InvariantCulture)).Append("ms")
              .Append(" extrap=").Append(poses > 0 ? (extrapolated * 100 / poses).ToString(CultureInfo.InvariantCulture) : "0").Append('%')
              .Append(" ysnap=").Append(groundSnaps.ToString(CultureInfo.InvariantCulture))
              .Append(" atk=").Append(gated.ToString(CultureInfo.InvariantCulture))
              .Append('/').Append(late.ToString(CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }

    void SampleSendQueue()
    {
        var bridge = _steamBridge;
        if (!_useSteamTransport || bridge == null)
            return;

        var now = Stopwatch.GetTimestamp();
        if (_lastSendQueueSampleTicks != 0 &&
            now - _lastSendQueueSampleTicks < Stopwatch.Frequency * SendQueueSampleIntervalMs / 1000L)
            return;
        _lastSendQueueSampleTicks = now;

        var maxBytes = 0;
        var maxPackets = 0;
        var relay = -1;
        var sampled = false;
        if (_role == NetRole.Host)
        {
            List<ulong> peers;
            lock (_clientsLock)
            {
                if (_steamClients.Count == 0)
                    return;
                peers = new List<ulong>(_steamClients.Count);
                foreach (var c in _steamClients.Values)
                    peers.Add(c.SteamId.m_SteamID);
            }

            foreach (var peer in peers)
            {
                if (!bridge.TryGetSendQueue(peer, out var bytes, out var packets, out var peerRelay))
                    continue;
                sampled = true;
                maxBytes = Math.Max(maxBytes, bytes);
                maxPackets = Math.Max(maxPackets, packets);
                relay = Math.Max(relay, peerRelay);
            }
        }
        else if (_role == NetRole.Client && _steamHostId.m_SteamID != 0UL)
        {
            sampled = bridge.TryGetSendQueue(_steamHostId.m_SteamID, out maxBytes, out maxPackets, out relay);
        }

        if (!sampled)
        {
            Volatile.Write(ref _outgoingBacklogBytes, 0);
            Volatile.Write(ref _outgoingCongested, 0);
            return;
        }

        var wasCongested = Volatile.Read(ref _outgoingCongested) != 0;
        var congested = wasCongested
            ? maxBytes > NetHealth.CongestionOffBytes
            : maxBytes >= NetHealth.CongestionOnBytes;
        Volatile.Write(ref _outgoingBacklogBytes, maxBytes);
        Volatile.Write(ref _outgoingCongested, congested ? 1 : 0);
        NetHealth.RecordSendQueue(maxBytes, maxPackets, relay, congested);
    }

    internal void SendMobAudit(int generation, IReadOnlyList<MobAuditEntry> alive, IReadOnlyList<int> dead)
    {
        if (_role != NetRole.Host || !HasAnyConnection())
            return;

        var line = MobAuditWire.BuildLine(generation, OutgoingBacklogBytes, NetHealth.LastRttMs, alive, dead);
        if (line.Length > 0)
            _ = SendLineSafe(line + "\n");
    }

    internal bool TryConsumeMobAudit(out MobAuditSnapshot? audit)
    {
        lock (_sync)
        {
            audit = _pendingMobAudit;
            _pendingMobAudit = null;
        }
        return audit != null;
    }
}

/*
 * ═══════════════════════════════════════
 *  NETNODE HEALTH — КАК РАБОТАЕТ
 * ═══════════════════════════════════════
 *  Keep-alive раз в 1 с шлёт PING|<монотонные мс>; пир сразу на сетевом потоке отвечает PONG|<те же мс>.
 *  Обе строки надёжные → rtt включает очередь надёжного канала обеих сторон. Голый PING тоже принимается.
 *  Хост отвечает только отправителю (SendLineToPeer), клиент — хосту.
 *  Каждые 100 мс: очередь отправки Steam по всем пирам (max) → OutgoingBacklogBytes;
 *  IsOutgoingCongested с гистерезисом: включается от 24 КБ, гаснет ниже 6 КБ. TCP — всегда 0.
 *  RunHealthTick зовёт цикл keep-alive; он же сбрасывает [NetHealth] раз в 5 с.
 *  MOBAUDIT хоста ложится в _pendingMobAudit (только последний), MobSync забирает TryConsumeMobAudit.
 * ═══════════════════════════════════════
*/
