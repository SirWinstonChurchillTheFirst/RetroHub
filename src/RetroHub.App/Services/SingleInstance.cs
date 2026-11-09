using System.Security.Cryptography;
using System.Text;
using RetroHub.Core.Logging;

namespace RetroHub.App.Services;

/// <summary>
/// One RetroHub per portable root (two would write the same database/configs). A named mutex keyed by a hash of the
/// root path; a second start signals a named event instead, and the running instance brings its window to the front.
/// </summary>
public sealed class SingleInstance : IDisposable
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
        try { _show.Set(); } catch (Exception ex) { Log.Warn("Signalling the running instance failed", ex); }
    }

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
