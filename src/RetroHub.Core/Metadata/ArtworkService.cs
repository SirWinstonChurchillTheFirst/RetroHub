using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using RetroHub.Core.Catalog;
using RetroHub.Core.Config;
using RetroHub.Core.Library;
using RetroHub.Core.Logging;

namespace RetroHub.Core.Metadata;

/// <summary>
/// Finds covers (and local backgrounds/logos) for library games.
/// <list type="bullet">
/// <item><b>Local first</b> (<see cref="LocalArtworkFinder"/>): artwork/covers/&lt;system&gt;/, next to the ROM, ES-DE/Batocera folders.</item>
/// <item><b>Online</b> only when <see cref="AppSettings.OnlineCovers"/> is on (default off): libretro-thumbnails boxarts.
/// The system's file index comes from the GitHub API (cached 30 days, <see cref="ThumbnailIndexStore"/>) and is
/// matched by name/normalised title + region preference (<see cref="ThumbnailIndex"/>); without an index, a few
/// direct names are tried. Downloads land in artwork/covers/&lt;system&gt;/&lt;rom name&gt;.png (temp file + move, an existing
/// file is never overwritten), max 4 in parallel, User-Agent "RetroHub/0.1", 404s remembered for 7 days
/// (<see cref="ArtworkMissCache"/>). Network failures are logged and counted, never thrown.</item>
/// <item><b>GameTDB</b> for systems libretro has no set for (<see cref="SystemDef.GameTdb"/>, Nintendo Switch): the
/// platform's database (<see cref="GameTdbIndexStore"/>, monthly, ETag) is matched by title (<see cref="GameTdbIndex"/>)
/// and the coverM front is fetched in German, else English, else US (<see cref="GameTdb.CoverLanguages"/>), saved as
/// artwork/covers/&lt;system&gt;/&lt;rom name&gt;.jpg, at most 2 downloads from art.gametdb.com at a time. Same opt-in, miss
/// cache and rules as libretro.</item>
/// <item><b>Thumbnails</b>: Core has no image library, so downscaled copies for the UI are produced by the App's
/// AsyncImageLoader (disk cache in data/cache/thumbs). This service only stores paths.</item>
/// </list>
/// <see cref="Game.CoverPath"/> conventions: null = not looked up / not found (looked up again next run);
/// "" = removed by the user ("Cover entfernen"), never filled automatically again; otherwise a portable path.
/// </summary>
public sealed class ArtworkService : IDisposable
{
    public const string UserAgent = "RetroHub/0.1";
    public const int MaxParallelDownloads = 4;
    /// <summary>Parallel cover downloads from art.gametdb.com (a small volunteer host: politer than libretro's CDN).</summary>
    public const int MaxParallelGameTdbDownloads = 2;
    private const long MaxImageBytes = 16 * 1024 * 1024;

    private readonly AppPaths _paths;
    private readonly IGameRepository _repo;
    private readonly ICatalog _catalog;
    private readonly SettingsStore _settings;
    private readonly HttpClient? _injectedHttp;
    private HttpClient? _ownHttp;
    private ThumbnailIndexStore? _indexes;
    private GameTdbIndexStore? _gameTdb;
    private readonly SemaphoreSlim _gameTdbDownloads = new(MaxParallelGameTdbDownloads, MaxParallelGameTdbDownloads);
    private readonly ArtworkMissCache _misses;
    private readonly SemaphoreSlim _runGate = new(1, 1);
    private readonly object _httpGate = new();
    private int _httpRequests;

    public ArtworkService(AppPaths paths, IGameRepository repo, ICatalog catalog, SettingsStore settings, HttpClient? http = null)
    {
        _paths = paths;
        _repo = repo;
        _catalog = catalog;
        _settings = settings;
        _injectedHttp = http;
        _misses = new ArtworkMissCache(Path.Combine(paths.Metadata, "artwork-misses.json"));
    }

    /// <summary>Timeout of a single boxart download.</summary>
    public TimeSpan DownloadTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Folder of the cached GitHub indexes (data/metadata/libretro-thumbnails).</summary>
    public string IndexCacheFolder => Path.Combine(_paths.Metadata, "libretro-thumbnails");

    /// <summary>Folder of the cached GameTDB databases (data/metadata/gametdb).</summary>
    public string GameTdbCacheFolder => Path.Combine(_paths.Metadata, "gametdb");

    /// <summary>HTTP requests sent by this service so far (thumbnail downloads + GitHub index; diagnostics/tests).</summary>
    public int HttpRequests => Volatile.Read(ref _httpRequests) + (_indexes?.Requests ?? 0) + (_gameTdb?.Requests ?? 0);

    /// <summary>True while <see cref="ResolveAsync"/> runs.</summary>
    public bool IsRunning => _runGate.CurrentCount == 0;

    internal ArtworkMissCache Misses => _misses;

    /// <summary>The polite headers every request carries: a product User-Agent, no personal data.</summary>
    internal static void AddPoliteHeaders(HttpRequestMessage req)
    {
        req.Headers.UserAgent.Clear();
        req.Headers.UserAgent.Add(new ProductInfoHeaderValue("RetroHub", "0.1"));
    }

    private HttpClient Http
    {
        get
        {
            if (_injectedHttp != null) return _injectedHttp;
            lock (_httpGate)
            {
                return _ownHttp ??= new HttpClient(new SocketsHttpHandler
                {
                    AutomaticDecompression = DecompressionMethods.All,
                    PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                    MaxConnectionsPerServer = MaxParallelDownloads,
                    ConnectTimeout = TimeSpan.FromSeconds(15),
                })
                { Timeout = TimeSpan.FromSeconds(120) };
            }
        }
    }

    private ThumbnailIndexStore Indexes
    {
        get
        {
            lock (_httpGate) return _indexes ??= new ThumbnailIndexStore(IndexCacheFolder, Http);
        }
    }

    private GameTdbIndexStore GameTdbIndexes
    {
        get
        {
            lock (_httpGate) return _gameTdb ??= new GameTdbIndexStore(GameTdbCacheFolder, Http);
        }
    }

    /// <summary>
    /// Fills missing artwork for <paramref name="games"/> (null = the whole library). Games with a cover whose file
    /// still exists are skipped; games whose cover the user removed ("") are skipped. Runs are serialised.
    /// </summary>
    public async Task<ArtworkReport> ResolveAsync(IEnumerable<Game>? games, IProgress<ArtworkProgress>? progress, CancellationToken ct)
    {
        await _runGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await ResolveCoreAsync(games, progress, ct).ConfigureAwait(false);
        }
        finally
        {
            _misses.Save();
            _runGate.Release();
        }
    }

    private async Task<ArtworkReport> ResolveCoreAsync(IEnumerable<Game>? games, IProgress<ArtworkProgress>? progress, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        SweepStaleTemps();
        InvalidateScreens(); // a scan may have brought new snaps/titles (artwork folders, ES-DE media)
        var list = (games ?? await Task.Run(() => _repo.Query(new GameQuery { IncludeHidden = true, IncludeMissing = true }), ct).ConfigureAwait(false))
            .Where(g => g != null).ToList();
        var finder = new LocalArtworkFinder(_paths, _catalog);
        var online = _settings.Current.OnlineCovers;

        int local = 0, extras = 0, checkedCount = 0, coversNeeded = 0;
        var needOnline = new List<Game>();
        var updates = new List<(long Id, string? Cover, string? Background, string? Logo, bool ClearCover)>();

        // ---------------- local (sequential: directory listings are cached, this is fast) ----------------
        var candidates = list.Where(g => NeedsCover(g) || Needs(g.BackgroundPath) || Needs(g.LogoPath)).ToList();
        for (var i = 0; i < candidates.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var g = candidates[i];
            checkedCount++;
            if (i % 25 == 0) progress?.Report(new ArtworkProgress("local", i, candidates.Count, g.DisplayTitle));
            string? cover = null, bg = null, logo = null;
            var clear = false;
            if (NeedsCover(g))
            {
                coversNeeded++;
                cover = finder.Find(g, ArtworkKind.Cover);
                if (cover != null) local++;
                else
                {
                    // a vanished cover file: forget it (the placeholder shows) unless the whole game is on a missing drive
                    clear = !string.IsNullOrEmpty(g.CoverPath) && g.Status != GameStatus.Missing;
                    if (online && g.Status != GameStatus.Missing) needOnline.Add(g);
                }
            }
            if (Needs(g.BackgroundPath) && (bg = finder.Find(g, ArtworkKind.Background)) != null) extras++;
            if (Needs(g.LogoPath) && (logo = finder.Find(g, ArtworkKind.Logo)) != null) extras++;
            if (cover != null || bg != null || logo != null || clear) updates.Add((g.Id, cover, bg, logo, clear));
        }
        progress?.Report(new ArtworkProgress("local", candidates.Count, candidates.Count, null));
        if (updates.Count > 0) await Task.Run(() => SaveLocal(updates), ct).ConfigureAwait(false);
        // covers stored before the size was recorded (schema 2): read their headers once
        await Task.Run(() => FillCoverSizes(list.Where(g => !string.IsNullOrEmpty(g.CoverPath) && g.CoverWidth == null), ct), ct).ConfigureAwait(false);

        // ---------------- online (opt-in) ----------------
        int downloaded = 0, errors = 0, skipped = 0, done = 0, notFound;
        if (online && needOnline.Count > 0)
        {
            await Parallel.ForEachAsync(needOnline, new ParallelOptions { MaxDegreeOfParallelism = MaxParallelDownloads, CancellationToken = ct }, async (g, token) =>
            {
                var r = await FetchOnlineAsync(g, finder, token).ConfigureAwait(false);
                switch (r)
                {
                    case OnlineResult.Downloaded: Interlocked.Increment(ref downloaded); break;
                    case OnlineResult.RecentMiss: Interlocked.Increment(ref skipped); break;
                    case OnlineResult.Error: Interlocked.Increment(ref errors); break;
                }
                var n = Interlocked.Increment(ref done);
                progress?.Report(new ArtworkProgress("online", n, needOnline.Count, g.DisplayTitle));
            }).ConfigureAwait(false);
        }
        notFound = coversNeeded - local - downloaded;

        var report = new ArtworkReport(local, downloaded, Math.Max(0, notFound), errors)
        {
            ExtrasFound = extras,
            Checked = checkedCount,
            SkippedRecentMiss = skipped,
            Duration = sw.Elapsed,
        };
        Log.Info($"Artwork: {checkedCount} games checked, {local} covers found locally, {downloaded} downloaded, {report.NotFound} without cover, " +
                 $"{errors} errors, {extras} backgrounds/logos ({(online ? "online on" : "online off")}, {sw.ElapsedMilliseconds} ms)");
        return report;
    }

    /// <summary>Cover missing: null, or a stored path whose file no longer exists. "" (removed by the user) is not missing.</summary>
    private bool NeedsCover(Game g) => g.CoverPath == null || (g.CoverPath.Length > 0 && !FileExists(g.CoverPath));

    private bool Needs(string? stored) => stored == null || (stored.Length > 0 && !FileExists(stored));

    private bool FileExists(string stored)
    {
        try { return File.Exists(_paths.FromPortable(stored)); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }

    private void SaveLocal(List<(long Id, string? Cover, string? Background, string? Logo, bool ClearCover)> updates)
    {
        var changed = new List<Game>();
        foreach (var u in updates)
        {
            // re-read: the game may have changed since the list was taken (playtime, favourite, a parallel scan)
            var fresh = _repo.Get(u.Id);
            if (fresh == null) continue;
            var dirty = false;
            if (u.Cover != null && NeedsCover(fresh)) { StoreCover(fresh, u.Cover); dirty = true; }
            else if (u.ClearCover && !string.IsNullOrEmpty(fresh.CoverPath) && !FileExists(fresh.CoverPath))
            {
                fresh.CoverPath = null;
                fresh.CoverWidth = fresh.CoverHeight = null;
                dirty = true;
            }
            if (u.Background != null && Needs(fresh.BackgroundPath)) { fresh.BackgroundPath = _paths.ToPortable(u.Background); dirty = true; }
            if (u.Logo != null && Needs(fresh.LogoPath)) { fresh.LogoPath = _paths.ToPortable(u.Logo); dirty = true; }
            if (dirty) changed.Add(fresh);
        }
        if (changed.Count > 0) _repo.UpsertMany(changed);
    }

    private enum OnlineResult { Downloaded, NotFound, RecentMiss, Error, Skipped }

    private async Task<OnlineResult> FetchOnlineAsync(Game g, LocalArtworkFinder finder, CancellationToken ct)
    {
        var sys = _catalog.GetSystem(g.SystemId);
        if (sys?.GameTdb is { Length: > 0 } platform) return await FetchGameTdbAsync(g, sys, platform, finder, ct).ConfigureAwait(false);
        if (sys?.LibretroName is not { Length: > 0 } libretroName) return OnlineResult.NotFound;
        if (g.Path.Contains("://", StringComparison.Ordinal)) return OnlineResult.NotFound;
        var stem = ArtworkNames.Stem(g);
        if (!AppPaths.IsSafeName(stem)) return OnlineResult.NotFound;
        var missKey = ArtworkMissCache.KeyFor(g.SystemId, stem);
        if (_misses.IsRecentMiss(missKey)) return OnlineResult.RecentMiss;
        // the user may switch "Cover aus dem Internet laden" off while a run is going: from then on no request at all
        if (!_settings.Current.OnlineCovers) return OnlineResult.Skipped;

        try
        {
            var index = await Indexes.GetAsync(g.SystemId, libretroName, ct).ConfigureAwait(false);
            IReadOnlyList<string> names;
            if (index != null)
            {
                var match = index.Match(g);
                if (match != null) names = [match];
                else if (index.Truncated) names = LibretroThumbnails.DirectCandidates(g); // incomplete listing: ask directly
                else
                {
                    Log.Debug($"No libretro boxart matches '{stem}' ({g.SystemId})");
                    _misses.AddMiss(missKey);
                    return OnlineResult.NotFound;
                }
            }
            else names = LibretroThumbnails.DirectCandidates(g);

            var target = Path.Combine(_paths.Covers, LocalArtworkFinder.SystemFolder(sys, g.SystemId), stem + ".png");
            var anyError = false;
            foreach (var name in names)
            {
                if (!_settings.Current.OnlineCovers) return OnlineResult.Skipped;
                var r = await DownloadAsync(LibretroThumbnails.BoxartUrl(libretroName, name), target, ct).ConfigureAwait(false);
                if (r == DownloadResult.NotFound) continue;
                if (r == DownloadResult.Error) { anyError = true; continue; }
                finder.Invalidate(Path.GetDirectoryName(target)!);
                SetCover(g.Id, target);
                if (r == DownloadResult.Saved) Log.Info($"Cover downloaded for '{g.DisplayTitle}': {name}");
                return OnlineResult.Downloaded;
            }
            if (anyError) return OnlineResult.Error;
            _misses.AddMiss(missKey);
            return OnlineResult.NotFound;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            Log.Warn($"Online cover lookup for '{g.DisplayTitle}' failed", ex);
            return OnlineResult.Error;
        }
    }

    /// <summary>
    /// A cover from GameTDB: database → title match → coverM in DE, EN, US (first that exists), saved as
    /// &lt;rom name&gt;.jpg. No match, or no language has the cover → a miss (7 days); an unreachable database is an error
    /// (not cached as a miss, so the next run asks again).
    /// </summary>
    private async Task<OnlineResult> FetchGameTdbAsync(Game g, SystemDef sys, string platform, LocalArtworkFinder finder, CancellationToken ct)
    {
        if (g.Path.Contains("://", StringComparison.Ordinal)) return OnlineResult.NotFound;
        var stem = ArtworkNames.Stem(g);
        if (!AppPaths.IsSafeName(stem)) return OnlineResult.NotFound;
        var missKey = ArtworkMissCache.KeyFor(g.SystemId, stem);
        if (_misses.IsRecentMiss(missKey)) return OnlineResult.RecentMiss;
        if (!_settings.Current.OnlineCovers) return OnlineResult.Skipped;

        try
        {
            var index = await GameTdbIndexes.GetAsync(platform, ct).ConfigureAwait(false);
            if (index == null) return OnlineResult.Error;
            var id = index.Match(g);
            if (id == null)
            {
                Log.Debug($"No GameTDB entry matches '{stem}' ({g.SystemId})");
                _misses.AddMiss(missKey);
                return OnlineResult.NotFound;
            }

            var dir = Path.Combine(_paths.Covers, LocalArtworkFinder.SystemFolder(sys, g.SystemId));
            var target = Path.Combine(dir, stem + ".jpg");
            var anyError = false;
            foreach (var lang in GameTdb.CoverLanguages)
            {
                if (!_settings.Current.OnlineCovers) return OnlineResult.Skipped;
                DownloadResult r;
                await _gameTdbDownloads.WaitAsync(ct).ConfigureAwait(false);
                try { r = await DownloadAsync(GameTdb.CoverUrl(platform, lang, id), target, ct).ConfigureAwait(false); }
                finally { _gameTdbDownloads.Release(); }
                if (r == DownloadResult.NotFound) continue;
                if (r == DownloadResult.Error) { anyError = true; continue; }
                finder.Invalidate(dir);
                SetCover(g.Id, target);
                if (r == DownloadResult.Saved) Log.Info($"Cover downloaded for '{g.DisplayTitle}': GameTDB {id}/{lang}");
                return OnlineResult.Downloaded;
            }
            if (anyError) return OnlineResult.Error;
            _misses.AddMiss(missKey);
            return OnlineResult.NotFound;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            Log.Warn($"GameTDB cover lookup for '{g.DisplayTitle}' failed", ex);
            return OnlineResult.Error;
        }
    }

    private void SetCover(long id, string fullPath)
    {
        var fresh = _repo.Get(id);
        if (fresh == null || !NeedsCover(fresh)) return;
        StoreCover(fresh, fullPath);
        _repo.Upsert(fresh);
    }

    /// <summary>Sets the cover reference and its pixel size from the file header (no decode; unknown → null).</summary>
    private void StoreCover(Game game, string fullPath)
    {
        game.CoverPath = _paths.ToPortable(fullPath);
        var size = ImageHeader.TryRead(fullPath);
        game.CoverWidth = size?.Width;
        game.CoverHeight = size?.Height;
    }

    /// <summary>
    /// Fills the cover pixel size of games whose cover exists but has no stored size (covers stored before schema 2,
    /// or by another tool). Header reads only; returns the number of games updated. Safe to call any time.
    /// </summary>
    public int FillCoverSizes(IEnumerable<Game>? games = null, CancellationToken ct = default)
    {
        var list = games ?? _repo.Query(new GameQuery { IncludeHidden = true, IncludeMissing = true });
        var changed = new List<Game>();
        foreach (var g in list)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(g.CoverPath) || g.CoverWidth is > 0 && g.CoverHeight is > 0) continue;
            string full;
            try { full = _paths.FromPortable(g.CoverPath); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { continue; }
            if (ImageHeader.TryRead(full) is not { } size) continue;
            var fresh = _repo.Get(g.Id);
            if (fresh == null || fresh.CoverPath != g.CoverPath) continue;
            fresh.CoverWidth = size.Width;
            fresh.CoverHeight = size.Height;
            changed.Add(fresh);
        }
        if (changed.Count > 0)
        {
            _repo.UpsertMany(changed);
            Log.Info($"Artwork: pixel size stored for {changed.Count} covers");
        }
        return changed.Count;
    }

    internal enum DownloadResult { Saved, AlreadyExists, NotFound, Error }

    /// <summary>
    /// Downloads one image to <paramref name="target"/>: never overwrites an existing file (returns AlreadyExists),
    /// writes to a temp file in the same folder and moves it into place, checks the PNG/JPEG signature and a size cap.
    /// 404/410 → NotFound; anything else that fails → Error (logged).
    /// </summary>
    internal async Task<DownloadResult> DownloadAsync(Uri url, string target, CancellationToken ct)
    {
        if (File.Exists(target)) return DownloadResult.AlreadyExists;
        var dir = Path.GetDirectoryName(Path.GetFullPath(target))!;
        string? tmp = null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(DownloadTimeout);
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            AddPoliteHeaders(req);
            Interlocked.Increment(ref _httpRequests);
            using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (resp.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone) return DownloadResult.NotFound;
            if (!resp.IsSuccessStatusCode)
            {
                Log.Warn($"Cover download {url} failed: HTTP {(int)resp.StatusCode}");
                return DownloadResult.Error;
            }
            if (resp.Content.Headers.ContentLength is > MaxImageBytes)
            {
                Log.Warn($"Cover download {url} skipped: {resp.Content.Headers.ContentLength} bytes");
                return DownloadResult.Error;
            }
            Directory.CreateDirectory(dir);
            tmp = Path.Combine(dir, $".{Path.GetFileName(target)}.{Guid.NewGuid():N}{IO.SafeFile.TempSuffix}");
            long total = 0;
            var head = new byte[8];
            var headLen = 0;
            await using (var src = await resp.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false))
            await using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true))
            {
                var buf = new byte[64 * 1024];
                int n;
                while ((n = await src.ReadAsync(buf, timeout.Token).ConfigureAwait(false)) > 0)
                {
                    if (headLen < head.Length)
                    {
                        var c = Math.Min(head.Length - headLen, n);
                        Array.Copy(buf, 0, head, headLen, c);
                        headLen += c;
                    }
                    total += n;
                    if (total > MaxImageBytes) throw new IOException($"image larger than {MaxImageBytes} bytes");
                    await fs.WriteAsync(buf.AsMemory(0, n), timeout.Token).ConfigureAwait(false);
                }
                await fs.FlushAsync(timeout.Token).ConfigureAwait(false);
            }
            if (!LooksLikeImage(head.AsSpan(0, headLen)))
            {
                Log.Warn($"Cover download {url} is not a PNG/JPEG image ({total} bytes) – discarded");
                return DownloadResult.Error;
            }
            try
            {
                File.Move(tmp, target, overwrite: false);
                tmp = null;
                return DownloadResult.Saved;
            }
            catch (IOException) when (File.Exists(target))
            {
                return DownloadResult.AlreadyExists; // someone (the user, a parallel run) put a file there meanwhile: keep theirs
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Cover download {url} failed: {ex.Message}");
            return DownloadResult.Error;
        }
        finally
        {
            if (tmp != null)
            {
                try { File.Delete(tmp); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _ = ex; }
            }
        }
    }

    /// <summary>Removes temp files of downloads/copies that a killed process left in artwork/covers (older than 10 minutes).</summary>
    private void SweepStaleTemps()
    {
        try
        {
            if (!Directory.Exists(_paths.Covers)) return;
            var options = new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 2, IgnoreInaccessible = true };
            foreach (var f in Directory.EnumerateFiles(_paths.Covers, "*" + IO.SafeFile.TempSuffix, options))
            {
                try { if (DateTime.UtcNow - File.GetLastWriteTimeUtc(f) > TimeSpan.FromMinutes(10)) File.Delete(f); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _ = ex; }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log.Debug($"Temp sweep in {_paths.Covers} failed: {ex.Message}"); }
    }

    internal static bool LooksLikeImage(ReadOnlySpan<byte> head) =>
        (head.Length >= 8 && head[0] == 0x89 && head[1] == 0x50 && head[2] == 0x4E && head[3] == 0x47) ||
        (head.Length >= 3 && head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF);

    // ───────────────────────────── screens: snaps and title screens (Start TV, DESIGN 7.1/10.1) ─────────────────────────────

    private LocalArtworkFinder? _screenFinder;
    private readonly object _screenGate = new();

    /// <summary>The finder for snaps/titles: directory listings are kept until <see cref="InvalidateScreens"/>.</summary>
    private LocalArtworkFinder ScreenFinder
    {
        get { lock (_screenGate) return _screenFinder ??= new LocalArtworkFinder(_paths, _catalog); }
    }

    /// <summary>Forgets the cached folder listings of snaps/titles (after a scan or when files were added by hand).</summary>
    public void InvalidateScreens()
    {
        lock (_screenGate) _screenFinder = null;
    }

    /// <summary>
    /// Full path of the snap or title screen of <paramref name="game"/> on disk, or null: artwork/snaps|titles/&lt;system
    /// folder&gt;/ (ROM name, title, normalised title) and ES-DE/Batocera media next to the ROM. No network, never throws.
    /// </summary>
    public string? FindScreen(Game game, ArtworkKind kind)
    {
        if (kind is not (ArtworkKind.Snap or ArtworkKind.Title)) throw new ArgumentOutOfRangeException(nameof(kind));
        return ScreenFinder.Find(game, kind);
    }

    /// <summary>Snap and title screen of <paramref name="game"/> that are on disk (no network).</summary>
    public ScreenArt FindScreens(Game game) => new(FindScreen(game, ArtworkKind.Snap), FindScreen(game, ArtworkKind.Title));

    /// <summary>
    /// The screen pictures of ONE game, fetched on demand (the Start stage asks for the game it shows): what is on disk
    /// first; the missing kinds only when <see cref="AppSettings.OnlineCovers"/> is on – from libretro-thumbnails
    /// Named_Snaps / Named_Titles under the name the system's boxart index matches (same naming scheme in all three
    /// folders), else the direct candidates. Saved as artwork/snaps|titles/&lt;system&gt;/&lt;rom name&gt;.png (temp file + move,
    /// never overwriting); 404s are remembered per kind for 7 days. Network failures are logged, never thrown.
    /// </summary>
    public async Task<ScreenArt> FetchScreensAsync(Game game, CancellationToken ct)
    {
        var local = FindScreens(game);
        if ((local.Snap != null && local.Title != null) || !_settings.Current.OnlineCovers || game.Status == GameStatus.Missing) return local;
        var sys = _catalog.GetSystem(game.SystemId);
        if (sys?.LibretroName is not { Length: > 0 } libretroName || game.Path.Contains("://", StringComparison.Ordinal)) return local;
        var stem = ArtworkNames.Stem(game);
        if (!AppPaths.IsSafeName(stem)) return local;
        var kinds = new List<ArtworkKind>(2);
        if (local.Snap == null && !_misses.IsRecentMiss(ScreenMissKey(game.SystemId, ArtworkKind.Snap, stem))) kinds.Add(ArtworkKind.Snap);
        if (local.Title == null && !_misses.IsRecentMiss(ScreenMissKey(game.SystemId, ArtworkKind.Title, stem))) kinds.Add(ArtworkKind.Title);
        if (kinds.Count == 0) return local;

        string? snap = local.Snap, title = local.Title;
        try
        {
            IReadOnlyList<string> names;
            var index = await Indexes.GetAsync(game.SystemId, libretroName, ct).ConfigureAwait(false);
            if (index?.Match(game) is { } match) names = [match];
            else names = LibretroThumbnails.DirectCandidates(game);

            foreach (var kind in kinds)
            {
                var target = Path.Combine(LocalArtworkFinder.KindRoot(_paths, kind), LocalArtworkFinder.SystemFolder(sys, game.SystemId), stem + ".png");
                var anyError = false;
                string? saved = null;
                foreach (var name in names)
                {
                    if (!_settings.Current.OnlineCovers) return new ScreenArt(snap, title);
                    var r = await DownloadAsync(LibretroThumbnails.ThumbnailUrl(libretroName, kind, name), target, ct).ConfigureAwait(false);
                    if (r == DownloadResult.NotFound) continue;
                    if (r == DownloadResult.Error) { anyError = true; continue; }
                    saved = target;
                    if (r == DownloadResult.Saved) Log.Info($"{kind} downloaded for '{game.DisplayTitle}': {name}");
                    break;
                }
                if (saved != null)
                {
                    if (kind == ArtworkKind.Snap) snap = saved; else title = saved;
                }
                else if (!anyError) _misses.AddMiss(ScreenMissKey(game.SystemId, kind, stem));
            }
            InvalidateScreens();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            Log.Warn($"Online screen lookup for '{game.DisplayTitle}' failed", ex);
        }
        finally
        {
            _misses.Save();
        }
        return new ScreenArt(snap, title);
    }

    /// <summary>Miss key of a snap/title (kept apart from the cover's key: a game may have a boxart but no snap).</summary>
    internal static string ScreenMissKey(string systemId, ArtworkKind kind, string stem) =>
        ArtworkMissCache.KeyFor($"{systemId}/{(kind == ArtworkKind.Snap ? "snap" : "title")}", stem);

    // ───────────────────────────── manual cover (game detail page) ─────────────────────────────

    /// <summary>
    /// "Cover wählen…": uses <paramref name="sourceFile"/> as the game's cover. A file already inside artwork/covers is
    /// referenced as is; anything else is copied to artwork/covers/&lt;system&gt;/&lt;rom name&gt;.&lt;ext&gt; — an existing,
    /// different file there is never overwritten ("&lt;rom name&gt; (2).png" is used instead). Returns the stored portable path.
    /// </summary>
    public string SetUserCover(long gameId, string sourceFile)
    {
        var game = _repo.Get(gameId) ?? throw new InvalidOperationException($"Game {gameId} not found");
        var src = Path.GetFullPath(sourceFile);
        if (!File.Exists(src)) throw new FileNotFoundException("Cover image not found", src);
        if (!ArtworkNames.IsImage(src)) throw new ArgumentException("Only .png, .jpg, .jpeg and .webp images can be used as cover", nameof(sourceFile));

        string final;
        if (AppPaths.IsInside(src, _paths.Covers)) final = src;
        else
        {
            var sys = _catalog.GetSystem(game.SystemId);
            var dir = Path.Combine(_paths.Covers, LocalArtworkFinder.SystemFolder(sys, game.SystemId));
            Directory.CreateDirectory(dir);
            var stem = ArtworkNames.Stem(game);
            if (!AppPaths.IsSafeName(stem)) stem = "game-" + gameId;
            var ext = Path.GetExtension(src).ToLowerInvariant();
            final = Path.Combine(dir, stem + ext);
            for (var i = 2; File.Exists(final) && !SameContent(final, src); i++)
                final = Path.Combine(dir, $"{stem} ({i}){ext}");
            if (!File.Exists(final))
            {
                var tmp = final + IO.SafeFile.TempSuffix;
                File.Copy(src, tmp, overwrite: true);
                try { File.Move(tmp, final, overwrite: false); }
                finally { if (File.Exists(tmp)) File.Delete(tmp); }
            }
        }
        var fresh = _repo.Get(gameId) ?? game;
        StoreCover(fresh, final);
        _repo.Upsert(fresh);
        Log.Info($"Cover for '{fresh.DisplayTitle}' set by the user: {fresh.CoverPath}");
        return fresh.CoverPath!;
    }

    /// <summary>"Cover entfernen": clears the reference (stored as "" so it is not filled again automatically). Never deletes a file.</summary>
    public void ClearCover(long gameId)
    {
        var game = _repo.Get(gameId);
        if (game == null) return;
        game.CoverPath = "";
        game.CoverWidth = game.CoverHeight = null;
        _repo.Upsert(game);
        Log.Info($"Cover for '{game.DisplayTitle}' removed by the user (file kept)");
    }

    private static bool SameContent(string a, string b)
    {
        try
        {
            var fa = new FileInfo(a);
            var fb = new FileInfo(b);
            if (fa.Length != fb.Length) return false;
            return File.ReadAllBytes(a).AsSpan().SequenceEqual(File.ReadAllBytes(b));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    public void Dispose()
    {
        lock (_httpGate)
        {
            _ownHttp?.Dispose();
            _ownHttp = null;
        }
    }
}
