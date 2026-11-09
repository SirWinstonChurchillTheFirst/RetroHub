using RetroHub.Core.Logging;

namespace RetroHub.Core.Library;

/// <summary>
/// Watches the enabled library folders and reports which ones changed, debounced (default 2 s after the last event),
/// so the app can run an incremental scan for just those folders. Ignores partial downloads/temp files and files that
/// are never games (saves, artwork …). A watcher that fails (buffer overflow, folder removed, network drop) is
/// re-created; a folder that does not exist yet is retried periodically. <see cref="FoldersChanged"/> is raised on a
/// thread-pool thread and never throws into the caller.
/// </summary>
public sealed class FolderWatcher : IDisposable
{
    private const int BufferBytes = 64 * 1024;

    private readonly AppPaths _paths;
    private readonly TimeSpan _debounce;
    private readonly TimeSpan _retry;
    private readonly object _gate = new();
    private readonly Dictionary<long, (LibraryFolder Folder, string Root, FileSystemWatcher? Watcher)> _watched = new();
    private readonly HashSet<long> _changed = new();
    private readonly Timer _debounceTimer;
    private readonly Timer _retryTimer;
    private bool _disposed;

    /// <summary>Raised with the ids of the library folders that changed since the last notification.</summary>
    public event Action<IReadOnlyList<long>>? FoldersChanged;

    /// <param name="debounce">Quiet time after the last event before <see cref="FoldersChanged"/> fires (default 2 s).</param>
    /// <param name="retryInterval">How often missing folders / failed watchers are retried (default 10 s).</param>
    public FolderWatcher(AppPaths paths, TimeSpan? debounce = null, TimeSpan? retryInterval = null)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _debounce = debounce ?? TimeSpan.FromSeconds(2);
        _retry = retryInterval ?? TimeSpan.FromSeconds(10);
        _debounceTimer = new Timer(_ => FireChanged(), null, Timeout.Infinite, Timeout.Infinite);
        _retryTimer = new Timer(_ => RetryMissing(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>Ids of the folders currently watched successfully.</summary>
    public IReadOnlyList<long> ActiveFolderIds
    {
        get { lock (_gate) return _watched.Where(w => w.Value.Watcher != null).Select(w => w.Key).ToList(); }
    }

    /// <summary>
    /// Sets the folders to watch (replaces the previous set). Disabled folders are ignored. Call again after the
    /// user adds/removes/toggles a library folder. Never throws.
    /// </summary>
    public void SetFolders(IEnumerable<LibraryFolder> folders)
    {
        lock (_gate)
        {
            if (_disposed) return;
            var wanted = new Dictionary<long, (LibraryFolder, string)>();
            foreach (var f in folders)
            {
                if (!f.Enabled) continue;
                try { wanted[f.Id] = (f, Path.GetFullPath(_paths.FromPortable(f.Path))); }
                catch (Exception ex) { Log.Warn($"Folder watcher: invalid folder path {f.Path}", ex); }
            }
            foreach (var id in _watched.Keys.ToList())
            {
                if (wanted.TryGetValue(id, out var w) && string.Equals(w.Item2, _watched[id].Root, StringComparison.OrdinalIgnoreCase))
                    continue;
                _watched[id].Watcher?.Dispose();
                _watched.Remove(id);
            }
            foreach (var (id, (folder, root)) in wanted)
            {
                if (_watched.TryGetValue(id, out var existing))
                {
                    _watched[id] = (folder, root, existing.Watcher);
                    continue;
                }
                _watched[id] = (folder, root, Create(id, root));
            }
            ScheduleRetryIfNeeded();
        }
    }

    private FileSystemWatcher? Create(long id, string root)
    {
        try
        {
            if (!Directory.Exists(root)) return null;
            var w = new FileSystemWatcher(root)
            {
                IncludeSubdirectories = true,
                InternalBufferSize = BufferBytes,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size | NotifyFilters.LastWrite,
            };
            w.Created += (_, e) => OnEvent(id, root, e.FullPath);
            w.Deleted += (_, e) => OnEvent(id, root, e.FullPath);
            w.Changed += (_, e) =>
            {
                // A folder's own "changed" (its timestamp moved because a child changed) carries no information:
                // the child's own event decides. Without this, writing "x.part" would trigger via its folder.
                if (!IsDirectory(e.FullPath)) OnEvent(id, root, e.FullPath);
            };
            w.Renamed += (_, e) =>
            {
                // A download finishing is "x.part" → "x.gba": either name being relevant counts.
                if (Relevant(root, e.FullPath) || Relevant(root, e.OldFullPath)) MarkChanged(id);
            };
            w.Error += (_, e) => OnError(id, e.GetException());
            w.EnableRaisingEvents = true;
            return w;
        }
        catch (Exception ex)
        {
            Log.Warn($"Folder watcher could not watch {root}", ex);
            return null;
        }
    }

    private static bool IsDirectory(string path)
    {
        try { return Directory.Exists(path); } catch (Exception) { return false; }
    }

    private void OnEvent(long id, string root, string fullPath)
    {
        try
        {
            if (Relevant(root, fullPath)) MarkChanged(id);
        }
        catch (Exception ex) { Log.Warn($"Folder watcher event failed for {fullPath}", ex); }
    }

    /// <summary>Temp/partial files and never-game files (saves, images, configs) do not trigger a rescan.</summary>
    internal static bool Relevant(string root, string fullPath)
    {
        if (ScanRules.IsTempFile(fullPath)) return false;
        var ext = Path.GetExtension(fullPath);
        if (ext.Length > 0 && ScanRules.IsIgnorableExtension(ext)) return false;
        // Changes inside saves/, media/, .git/ … below the library root are not library changes.
        var rel = Path.GetRelativePath(root, fullPath);
        var parts = rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        for (var i = 0; i < parts.Length - 1; i++)
            if (parts[i] != ".." && ScanRules.IsSkippedFolder(parts[i])) return false;
        return true;
    }

    private void MarkChanged(long id)
    {
        lock (_gate)
        {
            if (_disposed) return;
            _changed.Add(id);
            _debounceTimer.Change(_debounce, Timeout.InfiniteTimeSpan); // restart the quiet period
        }
    }

    /// <summary>A watcher failed (buffer overflow, folder gone, network drop): re-create it and report the folder as changed. Internal for tests.</summary>
    internal void OnError(long id, Exception ex)
    {
        Log.Warn($"Folder watcher error for folder {id}, re-creating", ex);
        lock (_gate)
        {
            if (_disposed || !_watched.TryGetValue(id, out var w)) return;
            try { w.Watcher?.Dispose(); } catch (Exception) { }
            _watched[id] = (w.Folder, w.Root, Create(id, w.Root));
            ScheduleRetryIfNeeded();
        }
        // Events may have been lost (buffer overflow): let the app rescan this folder.
        MarkChanged(id);
    }

    private void RetryMissing()
    {
        var recovered = new List<long>();
        lock (_gate)
        {
            if (_disposed) return;
            foreach (var (id, w) in _watched.ToList())
            {
                if (w.Watcher != null) continue;
                var created = Create(id, w.Root);
                if (created == null) continue;
                _watched[id] = (w.Folder, w.Root, created);
                recovered.Add(id);
            }
            ScheduleRetryIfNeeded();
        }
        foreach (var id in recovered)
        {
            Log.Info($"Folder watcher: folder {id} is available again");
            MarkChanged(id);
        }
    }

    private void ScheduleRetryIfNeeded()
    {
        var anyMissing = _watched.Values.Any(w => w.Watcher == null);
        _retryTimer.Change(anyMissing ? _retry : Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    private void FireChanged()
    {
        List<long> ids;
        lock (_gate)
        {
            if (_disposed || _changed.Count == 0) return;
            ids = _changed.Order().ToList();
            _changed.Clear();
        }
        try { FoldersChanged?.Invoke(ids); }
        catch (Exception ex) { Log.Error("FoldersChanged handler failed", ex); }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var w in _watched.Values)
            {
                try { w.Watcher?.Dispose(); } catch (Exception) { }
            }
            _watched.Clear();
            _changed.Clear();
        }
        _debounceTimer.Dispose();
        _retryTimer.Dispose();
    }
}
