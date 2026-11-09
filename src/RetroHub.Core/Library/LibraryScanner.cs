using System.Collections.Concurrent;
using System.Diagnostics;
using RetroHub.Core.Catalog;
using RetroHub.Core.Library.Sniffers;
using RetroHub.Core.Logging;

namespace RetroHub.Core.Library;

/// <summary>Phase names reported in <see cref="ScanProgress.Phase"/> (German, shown in the UI as is).</summary>
public static class ScanPhases
{
    public const string Preparing = "Vorbereiten";
    public const string Enumerating = "Dateien suchen";
    public const string Checking = "Dateien prüfen";
    public const string Finishing = "Abschließen";
}

/// <summary>
/// Walks the library folders, detects systems, and updates the repository. See <see cref="ILibraryScanner"/>.
/// <para>
/// Threading: every scan runs on the thread pool (never on the caller's thread). Scans never overlap. When
/// <see cref="ScanAsync"/> is called while a scan is running:
/// <list type="bullet">
/// <item>if the running scan has not read the folder list yet and its options cover the request (same or more
/// folders, at least as thorough), the caller gets the running scan's result (later, a new folder or a folder already
/// walked past could be missed, so the request is not joined to it);</item>
/// <item>else if a queued (not yet started) scan covers the request, the caller shares that one;</item>
/// <item>otherwise a new scan is queued and starts as soon as the running one has finished.</item>
/// </list>
/// A shared scan cancelled by its original caller is restarted for the remaining caller. Cancellation throws
/// <see cref="OperationCanceledException"/> (work done so far is persisted, nothing is marked Missing).
/// Any other failure is logged and returned as <see cref="ScanReport.Errors"/>, never thrown.
/// </para>
/// </summary>
public sealed class LibraryScanner : ILibraryScanner
{
    /// <summary>CRC32 is only computed for files up to this size (cartridge ROMs only, not disc images/playlists/PC).</summary>
    public const long MaxHashBytes = 64L * 1024 * 1024;
    /// <summary>Portable path of the built-in library folder.</summary>
    public const string BuiltInFolderPath = "games";

    private const int BatchSize = 200;
    private const int MaxHeaderBytes = 1 << 20;
    private const int MaxDepth = 32;
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(100);

    private static readonly EnumerationOptions DirOptions = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
        ReturnSpecialDirectories = false,
    };

    private readonly ICatalog _catalog;
    private readonly IGameRepository _repo;
    private readonly AppPaths _paths;
    private readonly IReadOnlyList<IRomSniffer> _sniffers;
    private readonly Dictionary<string, IRomSniffer> _snifferById = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _protectedDirs;

    private readonly object _sync = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ScanJob? _running, _pending;
    private int _active;

    /// <summary>CRC32 of files that are not stored as games (duplicates), so rescans do not hash them again.</summary>
    private readonly ConcurrentDictionary<string, (long Size, DateTime Time, uint Crc)> _crcCache = new(StringComparer.OrdinalIgnoreCase);

    public LibraryScanner(ICatalog catalog, IGameRepository repo, AppPaths paths, IEnumerable<IRomSniffer>? sniffers = null)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _repo = repo ?? throw new ArgumentNullException(nameof(repo));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _sniffers = (sniffers ?? RomSniffers.All).ToList();
        foreach (var s in _sniffers) _snifferById.TryAdd(s.Id, s);
        _protectedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in new[] { paths.Data, paths.App, paths.Emulators, paths.Bios, paths.Saves, paths.SaveStates,
                     paths.Screenshots, paths.Artwork, paths.Themes, paths.Plugins, paths.Backups })
            _protectedDirs.Add(Trim(d));
    }

    public IReadOnlyList<IRomSniffer> Sniffers => _sniffers;

    public bool IsScanning
    {
        get { lock (_sync) return _active > 0; }
    }

    // ───────────────────────────── scheduling ─────────────────────────────

    private sealed class ScanJob(ScanOptions options)
    {
        public ScanOptions Options { get; } = options;
        public Task<ScanReport> Task { get; set; } = null!;
        /// <summary>Set (under the scheduler lock) right before the first folder is listed; from then on nobody joins.</summary>
        public bool EnumerationStarted;
    }

    public Task<ScanReport> ScanAsync(ScanOptions options, IProgress<ScanProgress>? progress, CancellationToken ct)
    {
        options ??= new ScanOptions();
        if (ct.IsCancellationRequested) return Task.FromCanceled<ScanReport>(ct);
        lock (_sync)
        {
            if (_running is { EnumerationStarted: false } r && Covers(r.Options, options))
                return JoinAsync(r, options, progress, ct);
            if (_pending is { } p && Covers(p.Options, options))
                return JoinAsync(p, options, progress, ct);
            var job = new ScanJob(options);
            _active++;
            _pending = job;
            job.Task = Task.Run(() => RunJobAsync(job, progress, ct), CancellationToken.None);
            return job.Task;
        }
    }

    private async Task<ScanReport> JoinAsync(ScanJob job, ScanOptions options, IProgress<ScanProgress>? progress, CancellationToken ct)
    {
        try
        {
            return await job.Task.WaitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // The shared scan was cancelled by its owner; this caller still wants a result.
            return await ScanAsync(options, progress, ct).ConfigureAwait(false);
        }
    }

    private static bool Covers(ScanOptions running, ScanOptions wanted)
    {
        var folders = running.FolderIds == null ||
                      (wanted.FolderIds != null && wanted.FolderIds.All(id => running.FolderIds.Contains(id)));
        return folders && (running.Full || !wanted.Full) && (running.ComputeHashes || !wanted.ComputeHashes);
    }

    private async Task<ScanReport> RunJobAsync(ScanJob job, IProgress<ScanProgress>? progress, CancellationToken ct)
    {
        try
        {
            await _gate.WaitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            lock (_sync)
            {
                if (_pending == job) _pending = null;
                _active--;
            }
            throw;
        }
        lock (_sync)
        {
            if (_pending == job) _pending = null;
            _running = job;
        }
        try
        {
            return ScanCore(job, progress, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            Log.Info("Library scan cancelled");
            throw;
        }
        catch (Exception ex)
        {
            Log.Error("Library scan failed", ex);
            return new ScanReport { Errors = [new ScanIssue("", $"Die Suche ist fehlgeschlagen: {ex.Message}")] };
        }
        finally
        {
            lock (_sync)
            {
                _running = null;
                _active--;
            }
            _gate.Release();
        }
    }

    // ───────────────────────────── scan state ─────────────────────────────

    private enum EntryKind { File, PcFile, PcFolder }

    private sealed record Entry(EntryKind Kind, string FullPath, SystemDef? FolderSystem);

    private sealed record FolderPlan(LibraryFolder Folder, string Root, List<Entry> Entries, bool Complete);

    private readonly record struct Stat(long Size, DateTime TimeUtc);

    /// <summary>Everything one scan run keeps in memory.</summary>
    private sealed class RunState
    {
        public required ScanOptions Options { get; init; }
        public required CancellationToken Ct { get; init; }
        public required Throttle Progress { get; init; }
        public int Added, Updated, Unchanged, Missing;
        public readonly List<ScanIssue> Duplicates = [], Unknown = [], Errors = [];

        public readonly Dictionary<string, Game> ByPath = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<(long, string), List<Game>> BySizeCrc = new();
        public readonly Dictionary<(long, string), List<Game>> BySizeName = new();
        /// <summary>Games of kind Executable/Shortcut (PC games), for "same game folder" matching.</summary>
        public readonly List<Game> PcGames = [];
        public readonly List<Game> All = [];
        public readonly HashSet<Game> Seen = new(ReferenceEqualityComparer.Instance);
        public readonly Dictionary<string, bool> ExistsCache = new(StringComparer.OrdinalIgnoreCase);

        public readonly List<Game> PendingWrites = [];
        public readonly HashSet<Game> PendingSet = new(ReferenceEqualityComparer.Instance);

        public int FilesSeen, FilesTotal;
    }

    private sealed class Throttle(IProgress<ScanProgress>? progress)
    {
        private readonly Stopwatch _sw = Stopwatch.StartNew();
        private TimeSpan _last = TimeSpan.MinValue;

        public void Report(string phase, int seen, int total, string? current, bool force = false)
        {
            if (progress == null) return;
            var now = _sw.Elapsed;
            if (!force && now - _last < ProgressInterval) return;
            _last = now;
            try { progress.Report(new ScanProgress(phase, seen, total, current)); }
            catch (Exception ex) { Log.Warn("Scan progress listener failed", ex); }
        }
    }

    // ───────────────────────────── scan ─────────────────────────────

    private ScanReport ScanCore(ScanJob job, IProgress<ScanProgress>? progress, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var st = new RunState { Options = job.Options, Ct = ct, Progress = new Throttle(progress) };
        st.Progress.Report(ScanPhases.Preparing, 0, 0, null, force: true);
        ct.ThrowIfCancellationRequested();

        EnsureBuiltInFolder();
        // From here on the folder list and the files are read: a later request may need something this run misses.
        lock (_sync) job.EnumerationStarted = true;
        var allFolders = _repo.Folders();
        var folders = allFolders
            .Where(f => f.Enabled && (job.Options.FolderIds == null || job.Options.FolderIds.Contains(f.Id)))
            .ToList();

        foreach (var g in _repo.Query(new GameQuery { IncludeHidden = true, IncludeMissing = true }))
            Index(st, g);

        // Phase 1: enumerate every folder first (gives a total for the progress bar).
        var plans = new List<FolderPlan>();
        var allRoots = allFolders.Where(f => f.Enabled).Select(f => Trim(_paths.FromPortable(f.Path))).ToList();
        foreach (var folder in folders)
        {
            ct.ThrowIfCancellationRequested();
            var root = Trim(_paths.FromPortable(folder.Path));
            if (!Directory.Exists(root))
            {
                if (folder.IsBuiltIn) Log.Warn($"Built-in games folder does not exist: {root}");
                else st.Errors.Add(new ScanIssue(folder.Path, "Ordner nicht gefunden oder nicht erreichbar"));
                plans.Add(new FolderPlan(folder, root, [], Complete: true));
                continue;
            }
            var otherRoots = new HashSet<string>(allRoots.Where(r => !r.Equals(root, StringComparison.OrdinalIgnoreCase)),
                StringComparer.OrdinalIgnoreCase);
            var entries = new List<Entry>();
            var complete = Walk(st, folder, root, otherRoots, entries);
            plans.Add(new FolderPlan(folder, root, entries, complete));
        }
        st.FilesTotal = plans.Sum(p => p.Entries.Count);
        st.FilesSeen = 0; // the counter restarts for the checking phase

        // Phase 2: check files.
        try
        {
            foreach (var plan in plans) ProcessFolder(st, plan);
        }
        finally
        {
            // Persist what is done even when cancelled — the next (incremental) scan continues from there.
            Flush(st);
        }

        // Phase 3: games whose files are gone → Missing (only after a complete pass, so moves are recognised first).
        st.Progress.Report(ScanPhases.Finishing, st.FilesSeen, st.FilesTotal, null, force: true);
        foreach (var plan in plans)
        {
            if (!plan.Complete) continue; // unreadable subtree: do not declare its games missing
            foreach (var g in st.All)
            {
                if (g.FolderId != plan.Folder.Id || st.Seen.Contains(g)) continue;
                if (GameFileExists(st, g)) continue;
                st.Missing++;
                if (g.Status != GameStatus.Missing)
                {
                    g.Status = GameStatus.Missing;
                    Queue(st, g);
                }
            }
        }
        Flush(st);

        UpdateLastScan(plans);

        var report = new ScanReport
        {
            Added = st.Added,
            Updated = st.Updated,
            Unchanged = st.Unchanged,
            Missing = st.Missing,
            Duplicates = st.Duplicates,
            Unknown = st.Unknown,
            Errors = st.Errors,
            Duration = sw.Elapsed,
        };
        st.Progress.Report(ScanPhases.Finishing, st.FilesSeen, st.FilesTotal, null, force: true);
        Log.Info($"Library scan: {report.Added} added, {report.Updated} updated, {report.Unchanged} unchanged, " +
                 $"{report.Missing} missing, {report.Duplicates.Count} duplicates, {report.Unknown.Count} unknown, " +
                 $"{report.Errors.Count} errors in {report.Duration.TotalSeconds:0.0} s");
        return report;
    }

    /// <summary>
    /// Sets LastScanUtc on the scanned folders. The folders are read again first: the user may have changed a
    /// folder's settings (or removed it) while the scan ran, and the scan must neither revert nor resurrect it.
    /// </summary>
    private void UpdateLastScan(List<FolderPlan> plans)
    {
        var now = DateTime.UtcNow;
        IReadOnlyList<LibraryFolder> current;
        try { current = _repo.Folders(); }
        catch (Exception ex)
        {
            Log.Error("Could not re-read library folders after the scan", ex);
            return;
        }
        foreach (var plan in plans)
        {
            var f = current.FirstOrDefault(x => x.Id == plan.Folder.Id);
            if (f == null) continue; // removed during the scan
            try
            {
                f.LastScanUtc = now;
                _repo.UpsertFolder(f);
            }
            catch (Exception ex)
            {
                Log.Error($"Could not update library folder {f.Path}", ex);
            }
        }
    }

    /// <summary>Makes sure the built-in games/ folder is registered (IsBuiltIn, path "games"). Returns it.</summary>
    public LibraryFolder EnsureBuiltInFolder()
    {
        var folders = _repo.Folders();
        var builtIn = folders.FirstOrDefault(f => f.IsBuiltIn);
        if (builtIn != null) return builtIn;
        var byPath = folders.FirstOrDefault(f => string.Equals(f.Path.TrimEnd('/', '\\'), BuiltInFolderPath, StringComparison.OrdinalIgnoreCase));
        if (byPath != null)
        {
            byPath.IsBuiltIn = true;
            _repo.UpsertFolder(byPath);
            return byPath;
        }
        var created = new LibraryFolder { Path = BuiltInFolderPath, IsBuiltIn = true, Recursive = true, Enabled = true };
        created.Id = _repo.UpsertFolder(created);
        Log.Info("Registered built-in library folder games/");
        return created;
    }

    private void Index(RunState st, Game g)
    {
        st.All.Add(g);
        st.ByPath[g.Path] = g;
        AddIndexes(st, g);
    }

    private void AddIndexes(RunState st, Game g)
    {
        if (g.Crc32 != null) Add(st.BySizeCrc, (g.Size, g.Crc32), g);
        Add(st.BySizeName, (g.Size, Path.GetFileName(g.Path).ToLowerInvariant()), g);
        if (g.Kind is GameKind.Executable or GameKind.Shortcut) st.PcGames.Add(g);

        static void Add<TKey>(Dictionary<TKey, List<Game>> d, TKey key, Game g) where TKey : notnull
        {
            if (!d.TryGetValue(key, out var list)) d[key] = list = [];
            list.Add(g);
        }
    }

    private void RemoveIndexes(RunState st, Game g)
    {
        if (g.Crc32 != null && st.BySizeCrc.TryGetValue((g.Size, g.Crc32), out var a)) a.Remove(g);
        if (st.BySizeName.TryGetValue((g.Size, Path.GetFileName(g.Path).ToLowerInvariant()), out var b)) b.Remove(g);
        st.PcGames.Remove(g);
    }

    // ───────────────────────────── enumeration ─────────────────────────────

    /// <summary>Collects the entries of one library folder. Returns false when part of it could not be read.</summary>
    private bool Walk(RunState st, LibraryFolder folder, string root, HashSet<string> otherRoots, List<Entry> entries)
    {
        SystemDef? fixedSys = null;
        if (!string.IsNullOrWhiteSpace(folder.SystemId))
        {
            fixedSys = _catalog.GetSystem(folder.SystemId);
            if (fixedSys == null)
                st.Errors.Add(new ScanIssue(folder.Path, $"Unbekanntes System „{folder.SystemId}“ für diesen Ordner – Erkennung erfolgt automatisch"));
        }
        var rootName = Path.GetFileName(root);
        var rootSys = fixedSys ?? (rootName.Length > 0 ? _catalog.SystemForFolder(rootName) : null);
        var complete = true;
        WalkDir(new DirectoryInfo(root), rootSys, rootSys != null, 0);
        return complete;

        void WalkDir(DirectoryInfo dir, SystemDef? sys, bool isSystemRoot, int depth)
        {
            st.Ct.ThrowIfCancellationRequested();
            if (depth > MaxDepth) return;
            List<FileSystemInfo> items;
            try
            {
                items = dir.EnumerateFileSystemInfos("*", DirOptions).ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                complete = false;
                st.Errors.Add(new ScanIssue(_paths.ToPortable(dir.FullName), $"Ordner nicht lesbar: {ex.Message}"));
                return;
            }
            items.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name));
            var pcRoot = sys is { FolderGames: true } && isSystemRoot;

            foreach (var item in items)
            {
                if (item is DirectoryInfo d)
                {
                    if (ScanRules.IsSkippedFolder(d.Name) || IsLink(d)) continue;
                    var full = Trim(d.FullName);
                    if (otherRoots.Contains(full) || _protectedDirs.Contains(full)) continue;
                    if (pcRoot)
                    {
                        entries.Add(new Entry(EntryKind.PcFolder, full, sys));
                        continue;
                    }
                    if (sys is { FolderGames: true } || !folder.Recursive) continue;
                    if (fixedSys != null) { WalkDir(d, fixedSys, false, depth + 1); continue; }
                    var mapped = _catalog.SystemForFolder(d.Name);
                    if (mapped != null) WalkDir(d, mapped, true, depth + 1);
                    else WalkDir(d, sys, false, depth + 1);
                }
                else if (item is FileInfo f)
                {
                    if (ScanRules.IsTempFile(f.Name)) continue;
                    entries.Add(new Entry(pcRoot ? EntryKind.PcFile : EntryKind.File, f.FullName, sys));
                    st.FilesSeen++;
                    st.Progress.Report(ScanPhases.Enumerating, st.FilesSeen, 0, f.FullName);
                }
            }
        }
    }

    private static bool IsLink(DirectoryInfo d)
    {
        try
        {
            // Junctions and symlinks have a LinkTarget; OneDrive/cloud placeholders are reparse points without one.
            return (d.Attributes & FileAttributes.ReparsePoint) != 0 && d.LinkTarget != null;
        }
        catch (Exception) { return true; } // cannot tell: do not follow
    }

    // ───────────────────────────── processing ─────────────────────────────

    private void ProcessFolder(RunState st, FolderPlan plan)
    {
        // Multi-file descriptors first: everything they reference is part of that game, not a game of its own.
        var hidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var expansions = new Dictionary<string, (IReadOnlyList<string> Files, bool AnyRequiredMissing)>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in plan.Entries)
        {
            if (e.Kind != EntryKind.File || !ScanRules.IsPlaylistExtension(Path.GetExtension(e.FullPath))) continue;
            st.Ct.ThrowIfCancellationRequested();
            var x = PlaylistParser.Expand(e.FullPath);
            expansions[e.FullPath] = x;
            foreach (var f in x.Files) hidden.Add(f);
        }

        foreach (var e in plan.Entries)
        {
            st.Ct.ThrowIfCancellationRequested();
            st.FilesSeen++;
            st.Progress.Report(ScanPhases.Checking, Math.Min(st.FilesSeen, st.FilesTotal), st.FilesTotal, e.FullPath);
            if (hidden.Contains(e.FullPath)) continue;
            try
            {
                switch (e.Kind)
                {
                    case EntryKind.File:
                        expansions.TryGetValue(e.FullPath, out var x);
                        ProcessFile(st, plan.Folder, e, x.Files == null ? null : x);
                        break;
                    case EntryKind.PcFile:
                        ProcessPcFile(st, plan.Folder, e);
                        break;
                    case EntryKind.PcFolder:
                        ProcessPcFolder(st, plan.Folder, e);
                        break;
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                st.Errors.Add(new ScanIssue(_paths.ToPortable(e.FullPath), $"Datei nicht lesbar: {ex.Message}"));
            }
            catch (Exception ex)
            {
                Log.Error($"Scanner failed on {e.FullPath}", ex);
                st.Errors.Add(new ScanIssue(_paths.ToPortable(e.FullPath), $"Fehler bei der Prüfung: {ex.Message}"));
            }
            if (st.PendingWrites.Count >= BatchSize) Flush(st);
        }
    }

    private void ProcessFile(RunState st, LibraryFolder folder, Entry e,
        (IReadOnlyList<string> Files, bool AnyRequiredMissing)? playlist)
    {
        var path = e.FullPath;
        var portable = _paths.ToPortable(path);
        var ext = Path.GetExtension(path).ToLowerInvariant();
        var stat = StatOf(path, playlist);
        st.ByPath.TryGetValue(portable, out var existing);

        if (existing != null && TryUnchanged(st, folder, existing, stat, () => QuickBroken(path, ext, playlist, existing)))
            return;

        // System from folder / extension (cheap).
        SystemDef? sys = null;
        List<SystemDef>? candidates = null;
        if (e.FolderSystem != null)
        {
            if (!Accepts(e.FolderSystem, ext))
            {
                if (existing == null) ReportForeign(st, portable, ext, e.FolderSystem);
                return;
            }
            sys = e.FolderSystem;
        }
        else
        {
            if (ext.Length == 0 || ScanRules.IsIgnorableExtension(ext))
            {
                if (ext.Length == 0 && existing == null) st.Unknown.Add(new ScanIssue(portable, "Datei ohne Dateiendung"));
                return;
            }
            var all = _catalog.SystemsForExtension(ext);
            candidates = all.Where(s => !s.FolderGames).ToList();
            if (candidates.Count == 0)
            {
                if (existing == null)
                {
                    var folderOnly = all.FirstOrDefault();
                    st.Unknown.Add(new ScanIssue(portable, folderOnly != null
                        ? $"„{ext}“-Dateien werden nur im Ordner für {folderOnly.Name} erkannt"
                        : $"Unbekannter Dateityp „{ext}“"));
                }
                return;
            }
            if (candidates.Count == 1) sys = candidates[0];
        }

        var kind = KindOf(ext, sys ?? candidates![0]);
        var sniffTarget = playlist == null ? path : FirstDataFile(playlist.Value.Files);
        var broken = playlist?.AnyRequiredMissing ?? false;
        var sniffs = new SniffSession(this, sniffTarget, HeaderSizeFor(sys != null ? [sys] : candidates!));
        if (kind != GameKind.Playlist && stat.Size == 0) broken = true;
        if (sniffTarget != null && !sniffs.Readable) broken = true;

        if (sys == null)
        {
            sys = ResolveAmbiguous(candidates!, ext, sniffs);
            if (sys == null && existing != null) sys = _catalog.GetSystem(existing.SystemId);
            if (sys == null)
            {
                st.Unknown.Add(new ScanIssue(portable,
                    $"System nicht eindeutig erkennbar (möglich: {string.Join(", ", candidates!.Select(c => c.Name))})"));
                return;
            }
            kind = KindOf(ext, sys);
        }

        var sniff = sniffs.ForSystem(sys);
        if (sniff?.Broken == true) broken = true;

        string? crc = null;
        if (st.Options.ComputeHashes && kind == GameKind.Rom && !ScanRules.IsDiscImageExtension(ext) &&
            stat.Size is > 0 and <= MaxHashBytes && sniffs.Readable)
            crc = ComputeCrc(path, stat, useCache: !st.Options.Full, st.Ct);

        var status = broken ? GameStatus.Broken : GameStatus.Ok;
        if (existing != null)
        {
            // Changed file (or Full scan / Broken re-check): refresh what the file says, keep what the user/metadata set.
            Refresh(st, existing, folder, sys, kind, status, stat, crc, sniff);
            return;
        }

        var name = Path.GetFileName(path);
        if (TryMoved(st, folder, portable, name, sys, kind, status, stat, crc, sniff)) return;
        if (crc != null && FindDuplicate(st, stat.Size, crc, portable) is { } dup)
        {
            st.Duplicates.Add(new ScanIssue(portable, $"Gleicher Inhalt wie „{dup.Path}“ – nicht hinzugefügt"));
            return;
        }

        var parsed = TitleParser.Parse(name);
        AddNew(st, folder, portable, sys, kind, status, stat, crc, sniff, parsed);
    }

    private void ProcessPcFile(RunState st, LibraryFolder folder, Entry e)
    {
        var sys = e.FolderSystem!;
        var path = e.FullPath;
        var portable = _paths.ToPortable(path);
        var ext = Path.GetExtension(path).ToLowerInvariant();
        st.ByPath.TryGetValue(portable, out var existing);
        if (!Accepts(sys, ext))
        {
            if (existing == null) ReportForeign(st, portable, ext, sys);
            return;
        }
        var stat = StatOf(path, null);
        if (existing != null && TryUnchanged(st, folder, existing, stat, () => stat.Size == 0)) return;
        var kind = KindOf(ext, sys);
        var status = stat.Size == 0 ? GameStatus.Broken : GameStatus.Ok;
        if (existing != null)
        {
            Refresh(st, existing, folder, sys, kind, status, stat, null, null);
            return;
        }
        var name = Path.GetFileName(path);
        if (TryMoved(st, folder, portable, name, sys, kind, status, stat, null, null)) return;
        AddNew(st, folder, portable, sys, kind, status, stat, null, null, TitleParser.Parse(name));
    }

    private void ProcessPcFolder(RunState st, LibraryFolder folder, Entry e)
    {
        var sys = e.FolderSystem!;
        var exe = PcGameDetector.FindMainExecutable(e.FullPath);
        var folderPortable = _paths.ToPortable(e.FullPath);
        if (exe == null)
        {
            // Keep a known game if its exe still exists (heuristic may fail after an update added helper exes).
            var known = PcGamesIn(st, e.FullPath).FirstOrDefault(g => GameFileExists(st, g));
            if (known != null) { st.Seen.Add(known); st.Unchanged++; return; }
            st.Unknown.Add(new ScanIssue(folderPortable, "Kein Spielprogramm (.exe) im Ordner gefunden"));
            return;
        }
        var portable = _paths.ToPortable(exe);
        var ext = Path.GetExtension(exe).ToLowerInvariant();
        var stat = StatOf(exe, null);
        var kind = ext is ".lnk" or ".url" ? GameKind.Shortcut : GameKind.Executable;
        var status = stat.Size == 0 ? GameStatus.Broken : GameStatus.Ok;

        // A game whose stored exe still exists inside this folder keeps it (user may have picked another exe).
        if (!st.ByPath.TryGetValue(portable, out var existing))
            existing = PcGamesIn(st, e.FullPath).FirstOrDefault(g => GameFileExists(st, g));
        if (existing != null)
        {
            var existingStat = existing.Path.Equals(portable, StringComparison.OrdinalIgnoreCase)
                ? stat : StatOf(_paths.FromPortable(existing.Path), null);
            if (TryUnchanged(st, folder, existing, existingStat, () => existingStat.Size == 0)) return;
            Refresh(st, existing, folder, sys, existing.Kind, existingStat.Size == 0 ? GameStatus.Broken : GameStatus.Ok,
                existingStat, null, null);
            return;
        }

        // Exe renamed/replaced by an update: same game folder, old exe gone → keep the game, point it at the new exe.
        var moved = PcGamesIn(st, e.FullPath).FirstOrDefault(g => !st.Seen.Contains(g) && g.Id != 0 && !GameFileExists(st, g));
        if (moved != null)
        {
            Relocate(st, moved, folder, portable, sys, kind, status, stat, null, null);
            return;
        }
        if (TryMoved(st, folder, portable, Path.GetFileName(exe), sys, kind, status, stat, null, null)) return;
        AddNew(st, folder, portable, sys, kind, status, stat, null, null, TitleParser.Parse(Path.GetFileName(e.FullPath), isFileName: false));
    }

    /// <summary>Known PC games whose exe/shortcut lies inside <paramref name="gameFolder"/> (any depth).</summary>
    private List<Game> PcGamesIn(RunState st, string gameFolder) =>
        st.PcGames.Where(g => AppPaths.IsInside(_paths.FromPortable(g.Path), gameFolder)).ToList();

    /// <summary>Unchanged size+mtime (and not a full scan): count it, restore Missing → Ok/Broken, fix the folder id.</summary>
    private bool TryUnchanged(RunState st, LibraryFolder folder, Game existing, Stat stat, Func<bool> brokenCheck)
    {
        if (st.Options.Full || existing.Size != stat.Size || !SameTime(existing.FileTimeUtc, stat.TimeUtc)) return false;
        // Broken may have been transient (file locked by another program, network hiccup): check it again.
        // Refresh() counts it as unchanged when the result is the same, so this costs no writes.
        if (existing.Status == GameStatus.Broken) return false;
        st.Seen.Add(existing);
        var changed = false;
        if (existing.Status == GameStatus.Missing)
        {
            existing.Status = brokenCheck() ? GameStatus.Broken : GameStatus.Ok;
            changed = true;
            Log.Info($"Game file found again: {existing.Path}");
        }
        if (existing.FolderId != folder.Id)
        {
            existing.FolderId = folder.Id;
            changed = true;
        }
        if (changed)
        {
            Queue(st, existing);
            st.Updated++;
        }
        else st.Unchanged++;
        return true;
    }

    /// <summary>Header-only re-check for a restored (previously missing) file; no CRC.</summary>
    private bool QuickBroken(string path, string ext, (IReadOnlyList<string> Files, bool AnyRequiredMissing)? playlist, Game existing)
    {
        if (playlist?.AnyRequiredMissing == true) return true;
        var target = playlist == null ? path : FirstDataFile(playlist.Value.Files);
        if (target == null) return false;
        var sys = _catalog.GetSystem(existing.SystemId);
        var s = new SniffSession(this, target, sys == null ? 0 : HeaderSizeFor([sys]));
        if (!s.Readable) return true;
        if (playlist == null && SafeLength(path) == 0) return true;
        return sys != null && s.ForSystem(sys)?.Broken == true;
    }

    private bool TryMoved(RunState st, LibraryFolder folder, string portable, string fileName,
        SystemDef sys, GameKind kind, GameStatus status, Stat stat, string? crc, SniffResult? sniff)
    {
        IEnumerable<Game> candidates = [];
        if (crc != null && st.BySizeCrc.TryGetValue((stat.Size, crc), out var byCrc))
            candidates = byCrc;
        if (st.BySizeName.TryGetValue((stat.Size, fileName.ToLowerInvariant()), out var byName))
            candidates = candidates.Concat(byName.Where(g => g.SystemId == sys.Id && (crc == null || g.Crc32 == null || g.Crc32 == crc)));
        var moved = candidates.FirstOrDefault(g => g.Id != 0 && !st.Seen.Contains(g) && g.Kind == kind && !GameFileExists(st, g));
        if (moved == null) return false;
        Relocate(st, moved, folder, portable, sys, kind, status, stat, crc, sniff);
        return true;
    }

    /// <summary>
    /// Re-applies what the file says to a known game at the same path. Counts it as Updated (and writes it) only when a
    /// scanner-owned field actually changed, otherwise as Unchanged. Keeps the duplicate/rename indexes in sync.
    /// </summary>
    private void Refresh(RunState st, Game existing, LibraryFolder folder, SystemDef sys, GameKind kind, GameStatus status,
        Stat stat, string? crc, SniffResult? sniff)
    {
        var before = ScannerFields.Of(existing);
        RemoveIndexes(st, existing);
        ApplyFile(existing, folder, sys, kind, status, stat, crc, sniff);
        AddIndexes(st, existing);
        st.Seen.Add(existing);
        if (ScannerFields.Of(existing) == before)
        {
            st.Unchanged++;
            return;
        }
        Queue(st, existing);
        st.Updated++;
    }

    /// <summary>The fields the scanner owns on a <see cref="Game"/> (everything else belongs to the user/metadata).</summary>
    private readonly record struct ScannerFields(string Path, string SystemId, long? FolderId, GameKind Kind, GameStatus Status,
        long Size, DateTime FileTimeUtc, string? Crc32, string? Serial, string? Region)
    {
        public static ScannerFields Of(Game g) =>
            new(g.Path, g.SystemId, g.FolderId, g.Kind, g.Status, g.Size, g.FileTimeUtc, g.Crc32, g.Serial, g.Region);

        /// <summary>Copies the scanner's fields onto the current database row, keeping that row's user/metadata fields.</summary>
        public static void CopyTo(Game from, Game to)
        {
            to.Path = from.Path;
            to.SystemId = from.SystemId;
            to.FolderId = from.FolderId;
            to.Kind = from.Kind;
            to.Status = from.Status;
            to.Size = from.Size;
            to.FileTimeUtc = from.FileTimeUtc;
            to.Crc32 = from.Crc32;
            if (from.Serial != null) to.Serial = from.Serial;
            to.Region ??= from.Region;
        }
    }

    private void Relocate(RunState st, Game moved, LibraryFolder folder, string portable, SystemDef sys, GameKind kind,
        GameStatus status, Stat stat, string? crc, SniffResult? sniff)
    {
        Log.Info($"Game moved/renamed: {moved.Path} → {portable} (id {moved.Id})");
        RemoveIndexes(st, moved);
        st.ByPath.Remove(moved.Path);
        moved.Path = portable;
        ApplyFile(moved, folder, sys, kind, status, stat, crc ?? moved.Crc32, sniff);
        st.ByPath[portable] = moved;
        AddIndexes(st, moved);
        st.Seen.Add(moved);
        Queue(st, moved);
        st.Updated++;
    }

    private Game? FindDuplicate(RunState st, long size, string crc, string portable)
    {
        if (!st.BySizeCrc.TryGetValue((size, crc), out var list)) return null;
        return list.FirstOrDefault(g => !g.Path.Equals(portable, StringComparison.OrdinalIgnoreCase) && GameFileExists(st, g));
    }

    private static void ApplyFile(Game g, LibraryFolder folder, SystemDef sys, GameKind kind, GameStatus status, Stat stat,
        string? crc, SniffResult? sniff)
    {
        g.SystemId = sys.Id;
        g.FolderId = folder.Id;
        g.Kind = kind;
        g.Status = status;
        // Without a new CRC (hashing off), the stored one stays valid as long as the file is unchanged.
        var sameContent = g.Size == stat.Size && SameTime(g.FileTimeUtc, stat.TimeUtc);
        g.Crc32 = crc ?? (sameContent ? g.Crc32 : null);
        g.Size = stat.Size;
        g.FileTimeUtc = stat.TimeUtc;
        if (!string.IsNullOrWhiteSpace(sniff?.Serial)) g.Serial = sniff.Serial;
        g.Region ??= sniff?.Region;
    }

    private void AddNew(RunState st, LibraryFolder folder, string portable, SystemDef sys, GameKind kind, GameStatus status,
        Stat stat, string? crc, SniffResult? sniff, ParsedTitle parsed)
    {
        var title = parsed.Title;
        var sort = parsed.SortTitle;
        if (TitleParser.IsMeaningless(title) && TitleParser.CleanHeaderTitle(sniff?.Title) is { } header && !TitleParser.IsMeaningless(header))
        {
            title = header;
            sort = TitleParser.SortTitleOf(header);
        }
        var g = new Game
        {
            SystemId = sys.Id,
            Path = portable,
            Title = title,
            SortTitle = sort,
            Kind = kind,
            Status = status,
            Size = stat.Size,
            FileTimeUtc = stat.TimeUtc,
            Crc32 = crc,
            Serial = string.IsNullOrWhiteSpace(sniff?.Serial) ? null : sniff.Serial,
            Region = parsed.Region ?? sniff?.Region,
            AddedUtc = DateTime.UtcNow,
            FolderId = folder.Id,
        };
        Index(st, g);
        st.Seen.Add(g);
        Queue(st, g);
        st.Added++;
    }

    private void ReportForeign(RunState st, string portable, string ext, SystemDef sys)
    {
        if (ext.Length == 0)
        {
            st.Unknown.Add(new ScanIssue(portable, "Datei ohne Dateiendung"));
            return;
        }
        if (ScanRules.IsIgnorableExtension(ext)) return;
        st.Unknown.Add(new ScanIssue(portable, $"Dateityp „{ext}“ passt nicht zum System {sys.Name}"));
    }

    private static void Queue(RunState st, Game g)
    {
        if (st.PendingSet.Add(g)) st.PendingWrites.Add(g);
    }

    private void Flush(RunState st)
    {
        if (st.PendingWrites.Count == 0) return;
        try
        {
            _repo.UpsertMany(MergeWithCurrent(st.PendingWrites));
        }
        catch (Exception ex)
        {
            Log.Error($"Saving {st.PendingWrites.Count} scanned games failed", ex);
            st.Errors.Add(new ScanIssue("", $"Speichern in der Datenbank fehlgeschlagen: {ex.Message}"));
        }
        st.PendingWrites.Clear();
        st.PendingSet.Clear();
    }

    /// <summary>
    /// The scan works on a snapshot taken when it started; meanwhile the user may have played a game, toggled a
    /// favourite, edited metadata or removed the game. So for known games only the scanner's own fields are copied
    /// onto the row as it is now, and games removed during the scan are not written back.
    /// </summary>
    private List<Game> MergeWithCurrent(List<Game> pending)
    {
        var result = new List<Game>(pending.Count);
        foreach (var g in pending)
        {
            if (g.Id == 0)
            {
                result.Add(g);
                continue;
            }
            Game? current;
            try { current = _repo.Get(g.Id); }
            catch (Exception ex)
            {
                Log.Warn($"Could not re-read game {g.Id} before saving; writing the scanned state", ex);
                result.Add(g);
                continue;
            }
            if (current == null)
            {
                Log.Info($"Game {g.Id} ({g.Path}) was removed during the scan; not saved again");
                continue;
            }
            ScannerFields.CopyTo(g, current);
            result.Add(current);
        }
        return result;
    }

    private bool GameFileExists(RunState st, Game g)
    {
        var full = _paths.FromPortable(g.Path);
        if (st.ExistsCache.TryGetValue(full, out var known)) return known;
        bool exists;
        try { exists = File.Exists(full); } catch (Exception) { exists = false; }
        st.ExistsCache[full] = exists;
        return exists;
    }

    // ───────────────────────────── detection ─────────────────────────────

    /// <summary>
    /// Detects the system of one file (drag &amp; drop / manual add). <paramref name="folderHint"/> may be a system id
    /// or a folder name ("gba", "genesis"); without it the parent folders are used (up to the library folder root,
    /// else three levels). Null = unknown, ambiguous or the extension does not fit. Never throws.
    /// </summary>
    public string? DetectSystem(string fullPath, string? folderHint = null)
    {
        try
        {
            var full = Path.GetFullPath(fullPath);
            if (Directory.Exists(full))
            {
                // A PC game folder: its parent (or the hint) must be a folder-games system.
                var hinted = HintSystem(folderHint) ?? FolderSystemOf(Path.GetDirectoryName(full));
                return hinted is { FolderGames: true } ? hinted.Id : null;
            }
            var ext = Path.GetExtension(full).ToLowerInvariant();
            if (ext.Length == 0) return null;
            var sys = HintSystem(folderHint) ?? FolderSystemOf(Path.GetDirectoryName(full));
            if (sys != null) return Accepts(sys, ext) ? sys.Id : null;
            if (ScanRules.IsIgnorableExtension(ext)) return null;
            var candidates = _catalog.SystemsForExtension(ext).Where(s => !s.FolderGames).ToList();
            if (candidates.Count == 0) return null;
            if (candidates.Count == 1) return candidates[0].Id;
            var target = full;
            if (ScanRules.IsPlaylistExtension(ext)) target = FirstDataFile(PlaylistParser.Expand(full).Files) ?? full;
            return ResolveAmbiguous(candidates, ext, new SniffSession(this, target, HeaderSizeFor(candidates)))?.Id;
        }
        catch (Exception ex)
        {
            Log.Warn($"DetectSystem failed for {fullPath}", ex);
            return null;
        }
    }

    private SystemDef? HintSystem(string? hint)
    {
        if (string.IsNullOrWhiteSpace(hint)) return null;
        var h = hint.Trim();
        return _catalog.GetSystem(h) ?? _catalog.SystemForFolder(h) ?? _catalog.SystemForFolder(Path.GetFileName(h.TrimEnd('\\', '/')));
    }

    /// <summary>Nearest ancestor folder name that maps to a system, stopping at the enclosing library folder root.</summary>
    private SystemDef? FolderSystemOf(string? dir)
    {
        if (dir == null) return null;
        string? libraryRoot = null;
        LibraryFolder? libraryFolder = null;
        try
        {
            foreach (var f in _repo.Folders())
            {
                var r = Trim(_paths.FromPortable(f.Path));
                if ((AppPaths.IsInside(dir, r) || string.Equals(Trim(dir), r, StringComparison.OrdinalIgnoreCase)) &&
                    (libraryRoot == null || r.Length > libraryRoot.Length))
                {
                    libraryRoot = r;
                    libraryFolder = f;
                }
            }
        }
        catch (Exception ex) { Log.Warn("Could not read library folders", ex); }

        if (libraryFolder?.SystemId is { } fixedId && _catalog.GetSystem(fixedId) is { } fixedSys) return fixedSys;
        var d = new DirectoryInfo(dir);
        for (var level = 0; d != null; level++, d = d.Parent)
        {
            if (libraryRoot == null && level >= 3) break;
            var s = _catalog.SystemForFolder(d.Name);
            if (s != null) return s;
            if (libraryRoot != null && string.Equals(Trim(d.FullName), libraryRoot, StringComparison.OrdinalIgnoreCase)) break;
        }
        return null;
    }

    /// <summary>
    /// Resolves an extension shared by several systems with their sniffers. A sniffer hit whose reported system id
    /// is a candidate counts for that candidate ("strong"); otherwise for the candidate that lists the sniffer.
    /// Ties are broken by a candidate whose id equals the extension (".gb" → "gb").
    /// </summary>
    private SystemDef? ResolveAmbiguous(IReadOnlyList<SystemDef> candidates, string ext, SniffSession sniffs)
    {
        if (!sniffs.Readable) return null;
        var strong = new List<SystemDef>();
        var weak = new List<SystemDef>();
        foreach (var c in candidates)
        {
            foreach (var sid in c.Sniffers)
            {
                var r = sniffs.Run(sid);
                if (r == null) continue;
                var named = candidates.FirstOrDefault(x => x.Id.Equals(r.SystemId, StringComparison.OrdinalIgnoreCase));
                if (named != null) { if (!strong.Contains(named)) strong.Add(named); }
                else if (!weak.Contains(c)) weak.Add(c);
            }
        }
        return Pick(strong) ?? (strong.Count == 0 ? Pick(weak) : null);

        SystemDef? Pick(List<SystemDef> list)
        {
            if (list.Count == 1) return list[0];
            if (list.Count == 0) return null;
            var byExt = list.Where(s => s.Id.Equals(ext.TrimStart('.'), StringComparison.OrdinalIgnoreCase)).ToList();
            return byExt.Count == 1 ? byExt[0] : null;
        }
    }

    /// <summary>Reads a file header once and runs sniffers on it (results cached per sniffer id).</summary>
    private sealed class SniffSession
    {
        private readonly LibraryScanner _owner;
        private readonly string? _path;
        private readonly byte[] _header = [];
        private readonly Dictionary<string, SniffResult?> _results = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>False when the file could not be opened/read (→ Broken).</summary>
        public bool Readable { get; }

        public SniffSession(LibraryScanner owner, string? path, int headerBytes)
        {
            _owner = owner;
            _path = path;
            if (path == null) { Readable = true; return; }
            try
            {
                var want = Math.Clamp(headerBytes, 0, MaxHeaderBytes);
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1);
                var n = (int)Math.Min(want, fs.Length);
                _header = new byte[n];
                fs.ReadExactly(_header);
                Readable = true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                Log.Warn($"Cannot read {path}: {ex.Message}");
                Readable = false;
            }
        }

        public SniffResult? Run(string snifferId)
        {
            if (_path == null || !Readable) return null;
            if (_results.TryGetValue(snifferId, out var cached)) return cached;
            SniffResult? r = null;
            if (_owner._snifferById.TryGetValue(snifferId, out var sniffer))
            {
                try
                {
                    var n = Math.Min(Math.Max(sniffer.HeaderBytes, 0), _header.Length);
                    r = sniffer.Sniff(_path, _header.AsSpan(0, n));
                }
                catch (Exception ex)
                {
                    Log.Warn($"Sniffer {snifferId} failed on {_path}", ex);
                }
            }
            _results[snifferId] = r;
            return r;
        }

        /// <summary>First sniffer result of the system's sniffers (header data: serial, region, title, broken).</summary>
        public SniffResult? ForSystem(SystemDef sys)
        {
            SniffResult? first = null;
            foreach (var sid in sys.Sniffers)
            {
                var r = Run(sid);
                if (r == null) continue;
                if (r.SystemId.Equals(sys.Id, StringComparison.OrdinalIgnoreCase)) return r;
                first ??= r;
            }
            return first;
        }
    }

    // ───────────────────────────── helpers ─────────────────────────────

    /// <summary>Header bytes needed by the sniffers of these systems (the file is opened even for 0, as a readability check).</summary>
    private int HeaderSizeFor(IEnumerable<SystemDef> systems)
    {
        var max = 0;
        foreach (var s in systems)
            foreach (var id in s.Sniffers)
                if (_snifferById.TryGetValue(id, out var sn)) max = Math.Max(max, sn.HeaderBytes);
        return max;
    }

    private static bool Accepts(SystemDef sys, string ext) =>
        ext.Length > 0 && sys.Extensions.Any(x => x.Equals(ext, StringComparison.OrdinalIgnoreCase));

    private static GameKind KindOf(string ext, SystemDef sys)
    {
        if (ScanRules.IsPlaylistExtension(ext)) return GameKind.Playlist;
        if (sys.FolderGames) return ext is ".lnk" or ".url" ? GameKind.Shortcut : GameKind.Executable;
        return GameKind.Rom;
    }

    /// <summary>First existing non-descriptor file of a playlist (the data track the sniffers look at).</summary>
    private static string? FirstDataFile(IReadOnlyList<string> files)
    {
        foreach (var f in files)
            if (!ScanRules.IsPlaylistExtension(Path.GetExtension(f)) && File.Exists(f)) return f;
        return null;
    }

    /// <summary>Size and mtime; for descriptors the sum/max over the descriptor and every referenced file.</summary>
    private static Stat StatOf(string path, (IReadOnlyList<string> Files, bool AnyRequiredMissing)? playlist)
    {
        var fi = ScanRules.FollowLink(new FileInfo(path));
        var size = fi.Length;
        var time = fi.LastWriteTimeUtc;
        if (playlist != null)
        {
            foreach (var f in playlist.Value.Files)
            {
                try
                {
                    var r = ScanRules.FollowLink(new FileInfo(f));
                    if (!r.Exists) continue;
                    size += r.Length;
                    if (r.LastWriteTimeUtc > time) time = r.LastWriteTimeUtc;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
        return new Stat(size, time);
    }

    private static long SafeLength(string path)
    {
        try { return ScanRules.FollowLink(new FileInfo(path)).Length; } catch (Exception) { return -1; }
    }

    private string? ComputeCrc(string path, Stat stat, bool useCache, CancellationToken ct)
    {
        if (useCache && _crcCache.TryGetValue(path, out var c) && c.Size == stat.Size && c.Time == stat.TimeUtc)
            return Crc32.ToHex(c.Crc);
        try
        {
            var crc = Crc32.ComputeFile(path, ct);
            if (_crcCache.Count > 100_000) _crcCache.Clear();
            _crcCache[path] = (stat.Size, stat.TimeUtc, crc);
            return Crc32.ToHex(crc);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"CRC32 failed for {path}: {ex.Message}");
            return null;
        }
    }

    /// <summary>Same file time within one second (databases may round; FAT has 2 s, but stays consistent per file).</summary>
    private static bool SameTime(DateTime a, DateTime b) => Math.Abs(a.Ticks - b.Ticks) < TimeSpan.TicksPerSecond;

    /// <summary>Full path without a trailing separator (except for a drive root, where "D:" would mean "current dir on D").</summary>
    private static string Trim(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full);
        if (root != null && string.Equals(full.TrimEnd('\\', '/'), root.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
            return root;
        return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
