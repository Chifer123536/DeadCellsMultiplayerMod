using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace DeadCellsMultiplayerMod.Tools;

/// <summary>
/// Lightweight structured game trace for multiplayer debugging.
///
/// Records lifecycle events (level changes, hero init/death, mob spawn/death/attack,
/// network connect/disconnect, run launch phases) into a bounded in-memory ring buffer
/// and optionally to a file (env DCCM_GAME_TRACE=1, path override DCCM_GAME_TRACE_FILE).
///
/// Design constraints:
/// - event-driven only: nothing here runs per frame, so the steady-state cost is zero;
/// - no exceptions escape: every entry point is try/catch-wrapped, a broken trace can
///   never take down game code;
/// - thread-safe: called from both the game thread and network threads.
/// </summary>
public static class GameTrace
{
    public readonly record struct TraceEvent(
        double TimeSec,
        string Category,
        string Message);

    private const int RingCapacity = 20000;
    private static readonly ConcurrentQueue<TraceEvent> s_ring = new();
    private static readonly long s_startTicks = Stopwatch.GetTimestamp();
    private static readonly bool s_fileEnabled;
    private static readonly string? s_filePath;
    private static readonly object s_fileSync = new();

    static GameTrace()
    {
        try
        {
            s_fileEnabled = Environment.GetEnvironmentVariable("DCCM_GAME_TRACE") == "1";
            s_filePath = Environment.GetEnvironmentVariable("DCCM_GAME_TRACE_FILE");
            if (s_fileEnabled && string.IsNullOrWhiteSpace(s_filePath))
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "DeadCellsMultiplayerMod");
                Directory.CreateDirectory(dir);
                s_filePath = Path.Combine(
                    dir,
                    $"gametrace_{DateTime.Now:yyyyMMdd_HHmmss}_{Environment.ProcessId}.log");
            }
        }
        catch
        {
            s_fileEnabled = false;
            s_filePath = null;
        }
    }

    public static bool FileEnabled => s_fileEnabled;

    public static void Event(string category, string message)
    {
        try
        {
            var timeSec = Stopwatch.GetElapsedTime(s_startTicks).TotalSeconds;
            var evt = new TraceEvent(timeSec, category, message);

            s_ring.Enqueue(evt);
            while (s_ring.Count > RingCapacity && s_ring.TryDequeue(out _)) { }

            if (s_fileEnabled && s_filePath != null)
                AppendToFile(evt);
        }
        catch
        {
            // Trace must never break game code.
        }
    }

    /// <summary>
    /// Serilog-style overload: {Named} placeholders are replaced positionally by args.
    /// (string.Format would throw on named placeholders and the events would vanish.)
    /// </summary>
    public static void Event(string category, string format, params object?[] args)
    {
        try
        {
            Event(category, FormatMessage(format, args));
        }
        catch
        {
        }
    }

    private static string FormatMessage(string format, object?[]? args)
    {
        if (args == null || args.Length == 0)
            return format;

        var sb = new StringBuilder(format.Length + 64);
        var argIndex = 0;
        for (var i = 0; i < format.Length; i++)
        {
            var ch = format[i];
            if (ch == '{')
            {
                var end = format.IndexOf('}', i);
                if (end > i)
                {
                    if (argIndex < args.Length)
                    {
                        sb.Append(FormatArg(args[argIndex++]));
                    }
                    else
                    {
                        sb.Append(format, i, end - i + 1);
                    }
                    i = end;
                    continue;
                }
            }
            sb.Append(ch);
        }

        return sb.ToString();
    }

    private static string FormatArg(object? arg)
    {
        return arg switch
        {
            null => "null",
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => arg.ToString() ?? "null",
        };
    }

    /// <summary>Returns up to <paramref name="count"/> most recent events, oldest first.</summary>
    public static List<TraceEvent> Recent(int count = 500, string? categoryFilter = null, double? sinceSec = null)
    {
        var snapshot = s_ring.ToArray();
        var result = new List<TraceEvent>(Math.Min(count, snapshot.Length));
        var start = Math.Max(0, snapshot.Length - count * 4); // overscan for filters

        for (var i = start; i < snapshot.Length; i++)
        {
            var evt = snapshot[i];
            if (categoryFilter != null &&
                !evt.Category.Contains(categoryFilter, StringComparison.OrdinalIgnoreCase))
                continue;
            if (sinceSec.HasValue && evt.TimeSec < sinceSec.Value)
                continue;
            result.Add(evt);
        }

        if (result.Count > count)
            result.RemoveRange(0, result.Count - count);

        return result;
    }

    public static string Render(List<TraceEvent> events)
    {
        var sb = new StringBuilder(events.Count * 96);
        foreach (var evt in events)
            sb.Append(evt.TimeSec.ToString("F3", CultureInfo.InvariantCulture))
              .Append("s [").Append(evt.Category).Append("] ")
              .AppendLine(evt.Message);
        return sb.ToString();
    }

    private static void AppendToFile(TraceEvent evt)
    {
        lock (s_fileSync)
        {
            try
            {
                File.AppendAllText(
                    s_filePath!,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"{evt.TimeSec:F3}s [{evt.Category}] {evt.Message}\n"));
            }
            catch
            {
            }
        }
    }
}
