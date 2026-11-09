using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using RetroHub.Core.Logging;

namespace RetroHub.App.Services;

/// <summary>
/// One RetroHub per portable root (two would write the same database/configs). A named mutex keyed by a hash of the
/// root path; a second start signals a named event instead, and the running instance brings its window to the front.
/// </summary>
public sealed partial class SingleInstance : IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _show;
    private readonly bool _owned;
    private Thread? _listener;
    private volatile bool _disposed;

    private SingleInstance(Mutex mutex, EventWaitHandle show, bool owned)
    {
        _mutex = mutex;
        _show = show;
        _owned = owned;
    }

    public bool IsFirst => _owned;

    public static string IdFor(string root)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToLowerInvariant()));
        return "RetroHub-" + Convert.ToHexString(hash)[..16];
    }

    /// <summary>Acquires the instance lock for <paramref name="root"/>. Check <see cref="IsFirst"/>.</summary>
    public static SingleInstance Acquire(string root)
    {
        var id = IdFor(root);
        var mutex = new Mutex(initiallyOwned: true, id + "-instance", out var createdNew);
        if (!createdNew)
        {
            // an abandoned mutex (crashed instance) can still be taken over
            try { createdNew = mutex.WaitOne(0); }
            catch (AbandonedMutexException) { createdNew = true; }
        }
        var show = new EventWaitHandle(false, EventResetMode.AutoReset, id + "-show");
        return new SingleInstance(mutex, show, createdNew);
    }

    /// <summary>Second instance: asks the first one to show its window.</summary>
    public void SignalFirstInstance()
    {
        // Windows only lets the process that currently owns the foreground (this one, just started by the user) hand
        // the foreground over; without this the running instance's Activate() would merely flash in the taskbar.
        if (OperatingSystem.IsWindows())
        {
            try { AllowSetForegroundWindow(ASFW_ANY); }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
        }
        try { _show.Set(); } catch (Exception ex) { Log.Warn("Signalling the running instance failed", ex); }
    }

    /// <summary>A command line handed over by a second start (e.g. <c>--launch</c> from a desktop shortcut).</summary>
    public sealed record ForwardedRequest(string[] Args, string? WorkingDirectory);

    /// <summary>
    /// Second instance: leaves its command line for the running instance as a small JSON file in
    /// <paramref name="folder"/> (data/cache/requests) – call before <see cref="SignalFirstInstance"/>.
    /// </summary>
    public static bool PostRequest(string folder, IReadOnlyList<string> args, string? workingDirectory)
    {
        try
        {
            Directory.CreateDirectory(folder);
            var name = $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.json";
            var tmp = Path.Combine(folder, name + ".tmp");
            File.WriteAllText(tmp, System.Text.Json.JsonSerializer.Serialize(new ForwardedRequest(args.ToArray(), workingDirectory)));
            File.Move(tmp, Path.Combine(folder, name));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("Forwarding the command line to the running instance failed", ex);
            return false;
        }
    }

    /// <summary>
    /// A request older than this is dropped instead of carried out: it was left behind (the running instance was
    /// closing or crashed when it was posted), and a game must not start out of the blue on some later second start.
    /// </summary>
    public static readonly TimeSpan MaxRequestAge = TimeSpan.FromMinutes(2);

    /// <summary>First instance: the requests left by second starts, oldest first; the files are deleted. Never throws.</summary>
    public static IReadOnlyList<ForwardedRequest> TakeRequests(string folder, DateTime? nowUtc = null)
    {
        var list = new List<ForwardedRequest>();
        var now = nowUtc ?? DateTime.UtcNow;
        try
        {
            if (!Directory.Exists(folder)) return list;
            foreach (var file in Directory.GetFiles(folder, "*.json").OrderBy(f => f, StringComparer.Ordinal))
            {
                try
                {
                    var age = now - File.GetLastWriteTimeUtc(file);
                    var request = System.Text.Json.JsonSerializer.Deserialize<ForwardedRequest>(File.ReadAllText(file));
                    if (age > MaxRequestAge) Log.Warn($"Forwarded request {Path.GetFileName(file)} is {age.TotalMinutes:0} min old – dropped");
                    else if (request?.Args != null) list.Add(request);
                }
                catch (Exception ex) when (ex is System.Text.Json.JsonException or IOException)
                {
                    Log.Warn($"Forwarded request {Path.GetFileName(file)} unreadable – skipped", ex);
                }
                try { File.Delete(file); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("Reading forwarded requests failed", ex);
        }
        return list;
    }

    private const int ASFW_ANY = -1;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AllowSetForegroundWindow(int processId);

    /// <summary>First instance: calls <paramref name="onShowRequested"/> (on a background thread) whenever another start signals.</summary>
    public void Listen(Action onShowRequested)
    {
        if (!_owned || _listener != null) return;
        _listener = new Thread(() =>
        {
            while (!_disposed)
            {
                try
                {
                    if (_show.WaitOne(500) && !_disposed) onShowRequested();
                }
                catch (ObjectDisposedException) { return; }
                catch (Exception ex) { Log.Warn("Single-instance listener", ex); }
            }
        }) { IsBackground = true, Name = "RetroHub single instance" };
        _listener.Start();
    }

    public void Dispose()
    {
        _disposed = true;
        _listener?.Join(TimeSpan.FromSeconds(1));
        if (_owned)
        {
            try { _mutex.ReleaseMutex(); } catch (ApplicationException) { }
        }
        _mutex.Dispose();
        _show.Dispose();
    }
}
