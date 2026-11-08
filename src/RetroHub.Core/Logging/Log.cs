using System.Collections.Concurrent;
using System.Text;

namespace RetroHub.Core.Logging;

public enum LogLevel { Debug, Info, Warn, Error }

/// <summary>
/// Small thread-safe file logger: data/logs/retrohub-YYYYMMDD.log, background writer, keeps 14 days.
/// Never throws. Call <see cref="Init"/> once at startup, <see cref="Shutdown"/> on exit.
/// </summary>
public static class Log
{
    private static readonly BlockingCollection<string> Queue = new(new ConcurrentQueue<string>(), 10_000);
    private static string? _folder;
    private static Thread? _writer;
    public static LogLevel MinLevel { get; set; } = LogLevel.Info;

    /// <summary>Raised for every entry (any thread). The UI may show warnings/errors as toasts.</summary>
    public static event Action<LogLevel, string>? Written;

    public static void Init(string folder)
    {
        _folder = folder;
        Directory.CreateDirectory(folder);
        Cleanup(folder, TimeSpan.FromDays(14));
        _writer = new Thread(WriteLoop) { IsBackground = true, Name = "RetroHub log writer" };
        _writer.Start();
        Info($"RetroHub log started (pid {Environment.ProcessId})");
    }

    public static void Debug(string msg) => Write(LogLevel.Debug, msg);
    public static void Info(string msg) => Write(LogLevel.Info, msg);
    public static void Warn(string msg, Exception? ex = null) => Write(LogLevel.Warn, ex == null ? msg : $"{msg}: {ex}");
    public static void Error(string msg, Exception? ex = null) => Write(LogLevel.Error, ex == null ? msg : $"{msg}: {ex}");

    public static void Write(LogLevel level, string msg)
    {
        if (level < MinLevel) return;
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level.ToString().ToUpperInvariant(),-5}] [{Environment.CurrentManagedThreadId,3}] {msg}";
        try { Queue.TryAdd(line); } catch (InvalidOperationException) { }
        try { Written?.Invoke(level, msg); } catch { /* listeners must not break logging */ }
    }

    public static void Shutdown()
    {
        Queue.CompleteAdding();
        _writer?.Join(TimeSpan.FromSeconds(2));
    }

    private static void WriteLoop()
    {
        foreach (var first in Queue.GetConsumingEnumerable())
        {
            var sb = new StringBuilder(first).AppendLine();
            while (Queue.TryTake(out var more)) sb.AppendLine(more);
            try
            {
                if (_folder != null)
                    File.AppendAllText(Path.Combine(_folder, $"retrohub-{DateTime.Now:yyyyMMdd}.log"), sb.ToString());
            }
            catch { /* disk full / locked: drop */ }
        }
    }

    private static void Cleanup(string folder, TimeSpan keep)
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(folder, "retrohub-*.log"))
                if (DateTime.Now - File.GetLastWriteTime(f) > keep) File.Delete(f);
        }
        catch { }
    }
}
