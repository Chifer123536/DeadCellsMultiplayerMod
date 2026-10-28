using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading;
using Serilog;

namespace DeadCellsMultiplayerMod.Network;

internal enum NetLineKind
{
    Pos,
    Anim,
    Hp,
    Atk,
    MobState,
    MobMove,
    MobEvent,
    MobDie,
    MobReg,
    MobHit,
    MobAtk,
    MobDraw,
    MobAudit,
    Exit,
    Level,
    Inter,
    Ping,
    Pong,
    Other
}

internal static class NetHealth
{
    public const int KindCount = (int)NetLineKind.Other + 1;
    public const double WindowSeconds = 5.0;
    public const double IdleWindowSeconds = 30.0;
    public const int CongestionOnBytes = 24 * 1024;
    public const int CongestionOffBytes = 6 * 1024;
    const double RttWarnMs = 800.0;
    const int QueueWarnBytes = 64 * 1024;
    const double DispatchWarnMs = 400.0;
    const int TopKinds = 7;

    static readonly string[] KindNames =
    {
        "pos", "anim", "hp", "atk", "mstate", "mmove", "mevent", "mdie", "mreg", "mhit", "matk", "mdraw", "maudit",
        "exit", "level", "inter", "ping", "pong", "other"
    };

    static readonly long[] s_sentLines = new long[KindCount];
    static readonly long[] s_sentBytes = new long[KindCount];
    static readonly long[] s_recvLines = new long[KindCount];
    static readonly long[] s_recvBytes = new long[KindCount];
    static long s_reliableBytes;
    static long s_unreliableBytes;
    static long s_sendErrors;
    static long s_budgetDrops;
    static string? s_lastSendError;
    static long s_rttCount;
    static long s_rttSumUs;
    static long s_rttMaxUs;
    static long s_lastRttUs;
    static long s_queueSamples;
    static long s_queueSumBytes;
    static long s_queueMaxBytes;
    static long s_queueMaxPackets;
    static long s_congestedSamples;
    static int s_relay = -1;
    static int s_loggedRelay = -1;
    static long s_dispatchCount;
    static long s_dispatchSumUs;
    static long s_dispatchMaxUs;
    static long s_backlogMax;
    static long s_windowStartTicks;
    static long s_lastIdleLogTicks;

    public static int LastRttMs => (int)(Interlocked.Read(ref s_lastRttUs) / 1000L);

    public static string KindName(NetLineKind kind) => KindNames[(int)kind];

    public static NetLineKind Classify(string? line)
    {
        if (string.IsNullOrEmpty(line))
            return NetLineKind.Other;

        var c = line[0];
        if (c >= '0' && c <= '9')
            return NetLineKind.Pos;

        switch (c)
        {
            case 'M':
                if (!line.StartsWith("MOB", StringComparison.Ordinal))
                    return NetLineKind.Other;
                if (line.StartsWith("MOBSTATE", StringComparison.Ordinal)) return NetLineKind.MobState;
                if (line.StartsWith("MOBMOVE|", StringComparison.Ordinal)) return NetLineKind.MobMove;
                if (line.StartsWith("MOBEVENT|", StringComparison.Ordinal)) return NetLineKind.MobEvent;
                if (line.StartsWith("MOBDIE|", StringComparison.Ordinal)) return NetLineKind.MobDie;
                if (line.StartsWith("MOBREG|", StringComparison.Ordinal)) return NetLineKind.MobReg;
                if (line.StartsWith("MOBHIT|", StringComparison.Ordinal)) return NetLineKind.MobHit;
                if (line.StartsWith("MOBATK", StringComparison.Ordinal)) return NetLineKind.MobAtk;
                if (line.StartsWith("MOBDRAW|", StringComparison.Ordinal)) return NetLineKind.MobDraw;
                if (line.StartsWith("MOBAUDIT|", StringComparison.Ordinal)) return NetLineKind.MobAudit;
                return NetLineKind.Other;
            case 'A':
                if (line.StartsWith("ANIM|", StringComparison.Ordinal)) return NetLineKind.Anim;
                if (line.StartsWith("ATK|", StringComparison.Ordinal)) return NetLineKind.Atk;
                return NetLineKind.Other;
            case 'H':
                if (line.StartsWith("HP|", StringComparison.Ordinal)) return NetLineKind.Hp;
                if (line.StartsWith("HEADANIM|", StringComparison.Ordinal)) return NetLineKind.Anim;
                if (line.StartsWith("HXSYNC|", StringComparison.Ordinal)) return NetLineKind.Level;
                return NetLineKind.Other;
            case 'P':
                if (line.StartsWith("PING", StringComparison.Ordinal)) return NetLineKind.Ping;
                if (line.StartsWith("PONG", StringComparison.Ordinal)) return NetLineKind.Pong;
                return NetLineKind.Other;
            case 'E':
                return line.StartsWith("EXIT", StringComparison.Ordinal) ? NetLineKind.Exit : NetLineKind.Other;
            case 'I':
                return line.StartsWith("INTER", StringComparison.Ordinal) ? NetLineKind.Inter : NetLineKind.Other;
            case 'L':
                return line.StartsWith("LSEED", StringComparison.Ordinal) ||
                       line.StartsWith("LGRAPH", StringComparison.Ordinal) ||
                       line.StartsWith("LDESC|", StringComparison.Ordinal) ||
                       line.StartsWith("LEVEL|", StringComparison.Ordinal)
                    ? NetLineKind.Level
                    : NetLineKind.Other;
            case 'S':
                return line.StartsWith("SEED|", StringComparison.Ordinal) ||
                       line.StartsWith("SUB|", StringComparison.Ordinal)
                    ? NetLineKind.Level
                    : NetLineKind.Other;
            case 'G':
                return line.StartsWith("GEN|", StringComparison.Ordinal) ? NetLineKind.Level : NetLineKind.Other;
            case 'C':
                return line.StartsWith("CGDATA|", StringComparison.Ordinal) ? NetLineKind.Level : NetLineKind.Other;
            case 'Z':
                return line.StartsWith("ZROOM|", StringComparison.Ordinal) ? NetLineKind.Level : NetLineKind.Other;
            default:
                return NetLineKind.Other;
        }
    }

    public static void RecordSent(string? line, int bytes, bool reliable)
    {
        var kind = (int)Classify(line);
        var size = Math.Max(0, bytes);
        Interlocked.Increment(ref s_sentLines[kind]);
        Interlocked.Add(ref s_sentBytes[kind], size);
        if (reliable)
            Interlocked.Add(ref s_reliableBytes, size);
        else
            Interlocked.Add(ref s_unreliableBytes, size);
    }

    public static void RecordReceived(string? line)
    {
        var kind = (int)Classify(line);
        Interlocked.Increment(ref s_recvLines[kind]);
        Interlocked.Add(ref s_recvBytes[kind], line?.Length ?? 0);
    }

    public static void RecordSendError(string? error = null)
    {
        Interlocked.Increment(ref s_sendErrors);
        if (!string.IsNullOrWhiteSpace(error))
            Volatile.Write(ref s_lastSendError, error);
    }

    public static void RecordBudgetDrop()
    {
        Interlocked.Increment(ref s_budgetDrops);
    }

    public static void RecordRtt(double ms)
    {
        if (!double.IsFinite(ms) || ms < 0.0 || ms > 600_000.0)
            return;

        var us = (long)(ms * 1000.0);
        Interlocked.Increment(ref s_rttCount);
        Interlocked.Add(ref s_rttSumUs, us);
        Interlocked.Exchange(ref s_lastRttUs, us);
        UpdateMax(ref s_rttMaxUs, us);
    }

    public static void RecordSendQueue(int bytes, int packets, int relay, bool congested)
    {
        Interlocked.Increment(ref s_queueSamples);
        Interlocked.Add(ref s_queueSumBytes, Math.Max(0, bytes));
        UpdateMax(ref s_queueMaxBytes, Math.Max(0, bytes));
        UpdateMax(ref s_queueMaxPackets, Math.Max(0, packets));
        if (congested)
            Interlocked.Increment(ref s_congestedSamples);
        if (relay >= 0)
            Volatile.Write(ref s_relay, relay);
    }

    public static void RecordDispatchLag(double ms, int backlog)
    {
        if (!double.IsFinite(ms) || ms < 0.0)
            return;

        var us = (long)(Math.Min(ms, 600_000.0) * 1000.0);
        Interlocked.Increment(ref s_dispatchCount);
        Interlocked.Add(ref s_dispatchSumUs, us);
        UpdateMax(ref s_dispatchMaxUs, us);
        UpdateMax(ref s_backlogMax, Math.Max(0, backlog));
    }

    public static void TryFlush(ILogger log, string role, int peers, Func<string>? motionDetail = null)
    {
        var now = Stopwatch.GetTimestamp();
        var start = Interlocked.Read(ref s_windowStartTicks);
        if (start == 0)
        {
            Interlocked.CompareExchange(ref s_windowStartTicks, now, 0);
            return;
        }

        var elapsedSeconds = (double)(now - start) / Stopwatch.Frequency;
        if (elapsedSeconds < WindowSeconds)
            return;
        if (Interlocked.CompareExchange(ref s_windowStartTicks, now, start) != start)
            return;

        var sentLines = Drain(s_sentLines);
        var sentBytes = Drain(s_sentBytes);
        var recvLines = Drain(s_recvLines);
        var recvBytes = Drain(s_recvBytes);
        var reliable = Interlocked.Exchange(ref s_reliableBytes, 0);
        var unreliable = Interlocked.Exchange(ref s_unreliableBytes, 0);
        var sendErrors = Interlocked.Exchange(ref s_sendErrors, 0);
        var budgetDrops = Interlocked.Exchange(ref s_budgetDrops, 0);
        var lastError = Interlocked.Exchange(ref s_lastSendError, null);
        var rttCount = Interlocked.Exchange(ref s_rttCount, 0);
        var rttSum = Interlocked.Exchange(ref s_rttSumUs, 0);
        var rttMax = Interlocked.Exchange(ref s_rttMaxUs, 0);
        var queueSamples = Interlocked.Exchange(ref s_queueSamples, 0);
        var queueSum = Interlocked.Exchange(ref s_queueSumBytes, 0);
        var queueMax = Interlocked.Exchange(ref s_queueMaxBytes, 0);
        var queueMaxPackets = Interlocked.Exchange(ref s_queueMaxPackets, 0);
        var congestedSamples = Interlocked.Exchange(ref s_congestedSamples, 0);
        var dispatchCount = Interlocked.Exchange(ref s_dispatchCount, 0);
        var dispatchSum = Interlocked.Exchange(ref s_dispatchSumUs, 0);
        var dispatchMax = Interlocked.Exchange(ref s_dispatchMaxUs, 0);
        var backlogMax = Interlocked.Exchange(ref s_backlogMax, 0);
        var relay = Volatile.Read(ref s_relay);

        if (peers <= 0 && Sum(sentLines) == 0 && Sum(recvLines) == 0)
            return;

        if (IsOnlyKeepAlive(sentLines) && IsOnlyKeepAlive(recvLines) && sendErrors == 0 && rttMax < RttWarnMs * 1000.0)
        {
            var lastIdle = Interlocked.Read(ref s_lastIdleLogTicks);
            if (lastIdle != 0 && (double)(now - lastIdle) / Stopwatch.Frequency < IdleWindowSeconds)
                return;
            Interlocked.Exchange(ref s_lastIdleLogTicks, now);
        }

        var sb = new StringBuilder(512);
        sb.Append("[NetHealth] ").Append(role).Append(" peers=").Append(peers);
        if (rttCount > 0)
            sb.Append(" rtt=").Append(Ms(rttSum / rttCount)).Append('/').Append(Ms(rttMax)).Append("ms");
        else
            sb.Append(" rtt=none");
        if (queueSamples > 0)
        {
            sb.Append(" steamQ=").Append(Kb(queueSum / queueSamples)).Append('/').Append(Kb(queueMax)).Append("KB")
              .Append(" pk=").Append(queueMaxPackets)
              .Append(" congested=").Append(congestedSamples * 100 / queueSamples).Append('%');
        }
        if (relay >= 0)
            sb.Append(" relay=").Append(relay == 1 ? "yes" : "no");
        if (dispatchCount > 0)
            sb.Append(" lag=").Append(Ms(dispatchSum / dispatchCount)).Append('/').Append(Ms(dispatchMax)).Append("ms backlog=").Append(backlogMax);
        var motion = motionDetail?.Invoke();
        if (!string.IsNullOrEmpty(motion))
            sb.Append(" | motion ").Append(motion);
        sb.Append(" | out ").Append(Rate(Sum(sentBytes), elapsedSeconds)).Append("KB/s ")
          .Append(PerSecond(Sum(sentLines), elapsedSeconds)).Append("/s rel=").Append(Rate(reliable, elapsedSeconds))
          .Append(" unrel=").Append(Rate(unreliable, elapsedSeconds)).Append(": ")
          .Append(FormatKinds(sentLines, sentBytes, elapsedSeconds, TopKinds));
        sb.Append(" | in ").Append(Rate(Sum(recvBytes), elapsedSeconds)).Append("KB/s ")
          .Append(PerSecond(Sum(recvLines), elapsedSeconds)).Append("/s: ")
          .Append(FormatKinds(recvLines, recvBytes, elapsedSeconds, TopKinds));
        if (sendErrors > 0 || budgetDrops > 0)
            sb.Append(" | err=").Append(sendErrors).Append(" drop=").Append(budgetDrops);
        log.Information(sb.ToString());

        if (relay >= 0 && relay != Volatile.Read(ref s_loggedRelay))
        {
            Volatile.Write(ref s_loggedRelay, relay);
            log.Information("[NetHealth] Steam route {Route}", relay == 1 ? "via Valve relay (higher ping)" : "direct P2P");
        }

        if (rttMax >= (long)(RttWarnMs * 1000.0))
            log.Warning("[NetHealth] WARN reliable round-trip {Max}ms (avg {Avg}ms): mob deaths, hits and events reach the peer this late", Ms(rttMax), rttCount > 0 ? Ms(rttSum / rttCount) : "?");
        if (queueMax >= QueueWarnBytes)
            log.Warning("[NetHealth] WARN Steam send queue peaked at {Queue}KB ({Packets} packets): sending faster than the link drains", Kb(queueMax), queueMaxPackets);
        if (dispatchMax >= (long)(DispatchWarnMs * 1000.0))
            log.Warning("[NetHealth] WARN game thread handled a network line {Max}ms after arrival (backlog {Backlog})", Ms(dispatchMax), backlogMax);
        if (sendErrors > 0)
            log.Warning("[NetHealth] WARN {Count} send failures, last: {Error}", sendErrors, lastError ?? "?");
        if (peers > 0 && Sum(recvLines) == 0)
            log.Warning("[NetHealth] WARN nothing received from the peer for {Seconds}s", (int)elapsedSeconds);
    }

    internal static string FormatKinds(long[] lines, long[] bytes, double seconds, int top)
    {
        var order = new int[KindCount];
        for (var i = 0; i < KindCount; i++)
            order[i] = i;
        Array.Sort(order, (a, b) => bytes[b].CompareTo(bytes[a]));

        var sb = new StringBuilder(160);
        var shown = 0;
        long restBytes = 0;
        long restLines = 0;
        for (var i = 0; i < KindCount; i++)
        {
            var k = order[i];
            if (lines[k] == 0)
                continue;
            if (shown >= top)
            {
                restBytes += bytes[k];
                restLines += lines[k];
                continue;
            }

            if (shown > 0)
                sb.Append(' ');
            sb.Append(KindNames[k]).Append(' ').Append(Rate(bytes[k], seconds)).Append('(').Append(PerSecond(lines[k], seconds)).Append(')');
            shown++;
        }

        if (restLines > 0)
            sb.Append(" rest ").Append(Rate(restBytes, seconds)).Append('(').Append(PerSecond(restLines, seconds)).Append(')');
        return shown == 0 ? "-" : sb.ToString();
    }

    static bool IsOnlyKeepAlive(long[] lines)
    {
        for (var i = 0; i < KindCount; i++)
        {
            if (lines[i] != 0 && i != (int)NetLineKind.Ping && i != (int)NetLineKind.Pong)
                return false;
        }
        return true;
    }

    static long[] Drain(long[] counters)
    {
        var copy = new long[counters.Length];
        for (var i = 0; i < counters.Length; i++)
            copy[i] = Interlocked.Exchange(ref counters[i], 0);
        return copy;
    }

    static long Sum(long[] values)
    {
        long total = 0;
        for (var i = 0; i < values.Length; i++)
            total += values[i];
        return total;
    }

    static void UpdateMax(ref long target, long value)
    {
        var current = Interlocked.Read(ref target);
        while (value > current)
        {
            var seen = Interlocked.CompareExchange(ref target, value, current);
            if (seen == current)
                return;
            current = seen;
        }
    }

    static string Ms(long us) => (us / 1000L).ToString(CultureInfo.InvariantCulture);

    static string Kb(long bytes) => (bytes / 1024.0).ToString("0.0", CultureInfo.InvariantCulture);

    static string Rate(long bytes, double seconds) =>
        (seconds > 0.0 ? bytes / 1024.0 / seconds : 0.0).ToString("0.0", CultureInfo.InvariantCulture);

    static string PerSecond(long count, double seconds) =>
        (seconds > 0.0 ? count / seconds : 0.0).ToString("0", CultureInfo.InvariantCulture);
}

/*
 * ═══════════════════════════════════════
 *  NET HEALTH — КАК РАБОТАЕТ
 * ═══════════════════════════════════════
 *  Раз в 5 с одна строка [NetHealth]: rtt=avg/max надёжного канала (PING|t → PONG|t),
 *  steamQ=avg/max КБ в очереди отправки Steam (GetP2PSessionState), congested=% времени очередь ≥ 24 КБ,
 *  relay, lag=avg/max мс от прихода строки до главного потока, motion — буфер позиций напарника
 *  (int — интервал пакетов, jit — джиттер, delay — задержка интерполяции), out/in КБ/с и строк/с по типам (топ-7).
 *  Байты исходящих — UTF-8 одной копии строки; входящих — длина строки.
 *  Только PING/PONG в окне → строка не чаще раза в 30 с.
 *  WARN: rtt ≥ 800 мс, очередь ≥ 64 КБ, lag ≥ 400 мс, ошибки отправки, тишина от пира целое окно.
 *  TryFlush зовёт цикл keep-alive NetNode (100 мс); счётчики Interlocked, писать можно из любого потока.
 * ═══════════════════════════════════════
*/
