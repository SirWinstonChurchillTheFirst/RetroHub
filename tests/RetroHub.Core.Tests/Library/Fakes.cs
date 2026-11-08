using System.Reflection;
using RetroHub.Core.Catalog;
using RetroHub.Core.Library;

namespace RetroHub.Core.Tests.Library;

/// <summary>In-test catalog built from plain SystemDef records (the real JSON catalog lives in another module).</summary>
internal sealed class FakeCatalog(IEnumerable<SystemDef> systems, IEnumerable<EmulatorDef>? emulators = null) : ICatalog
{
    public IReadOnlyList<SystemDef> Systems { get; } = systems.ToList();
    public IReadOnlyList<EmulatorDef> Emulators { get; } = (emulators ?? []).ToList();
    public IReadOnlyList<string> LoadWarnings { get; } = [];

    public SystemDef? GetSystem(string id) => Systems.FirstOrDefault(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
    public EmulatorDef? GetEmulator(string id) => Emulators.FirstOrDefault(e => e.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public SystemDef? SystemForFolder(string folderName) =>
        Systems.FirstOrDefault(s => s.Folders.Any(f => f.Equals(folderName, StringComparison.OrdinalIgnoreCase)));

    public IReadOnlyList<SystemDef> SystemsForExtension(string extension) =>
        Systems.Where(s => s.Extensions.Any(e => e.Equals(extension, StringComparison.OrdinalIgnoreCase))).ToList();

    private static SystemDef Sys(string id, string name, string[] folders, string[] ext, string[] sniffers, bool folderGames = false) =>
        new() { Id = id, Name = name, Folders = folders, Extensions = ext, Sniffers = sniffers, FolderGames = folderGames };

    /// <summary>A catalog resembling the shipped one (ES-DE style folder names and extensions).</summary>
    public static FakeCatalog Default() => new(
    [
        Sys("nes", "Nintendo Entertainment System", ["nes", "famicom"], [".nes", ".zip"], ["nes"]),
        Sys("snes", "Super Nintendo", ["snes", "sfc"], [".sfc", ".smc", ".zip"], ["snes"]),
        Sys("n64", "Nintendo 64", ["n64"], [".z64", ".n64", ".v64"], ["n64"]),
        Sys("gb", "Game Boy", ["gb"], [".gb", ".zip"], ["gb"]),
        Sys("gbc", "Game Boy Color", ["gbc"], [".gbc", ".zip"], ["gbc"]),
        Sys("gba", "Game Boy Advance", ["gba"], [".gba", ".zip"], ["gba"]),
        Sys("nds", "Nintendo DS", ["nds"], [".nds"], ["nds"]),
        Sys("3ds", "Nintendo 3DS", ["3ds", "n3ds"], [".3ds", ".cci", ".cxi"], ["3ds"]),
        Sys("megadrive", "Sega Mega Drive", ["megadrive", "genesis", "md"], [".md", ".gen", ".smd", ".bin"], ["megadrive"]),
        Sys("sms", "Sega Master System", ["mastersystem", "sms"], [".sms"], ["sms"]),
        Sys("psx", "PlayStation", ["psx", "ps1"], [".cue", ".bin", ".chd", ".m3u", ".pbp", ".iso", ".img", ".ccd"], ["iso9660-ps1"]),
        Sys("ps2", "PlayStation 2", ["ps2"], [".iso", ".chd", ".cso", ".bin", ".cue", ".m3u"], ["iso9660-ps2"]),
        Sys("psp", "PlayStation Portable", ["psp"], [".iso", ".cso", ".pbp"], ["iso9660-psp"]),
        Sys("gc", "Nintendo GameCube", ["gc", "gamecube"], [".iso", ".gcm", ".rvz", ".gcz"], ["nintendo-disc-gc"]),
        Sys("wii", "Nintendo Wii", ["wii"], [".iso", ".wbfs", ".rvz", ".gcz"], ["nintendo-disc-wii"]),
        Sys("dreamcast", "Sega Dreamcast", ["dreamcast", "dc"], [".gdi", ".cdi", ".chd"], []),
        Sys("pc", "PC", ["pc", "windows"], [".exe", ".lnk", ".url", ".bat"], [], folderGames: true),
    ]);
}

/// <summary>
/// Thread-safe in-memory IGameRepository that behaves like a database: it stores copies (callers' objects are not
/// shared), assigns ids on insert (also writes them back, like the SQLite repo), and enforces unique paths.
/// </summary>
internal class InMemoryGameRepository : IGameRepository
{
    private readonly object _gate = new();
    private readonly Dictionary<long, Game> _games = new();
    private readonly Dictionary<long, LibraryFolder> _folders = new();
    private readonly Dictionary<string, string?> _state = new();
    private readonly List<PlaySession> _sessions = new();
    private long _nextGame = 1, _nextFolder = 1, _nextSession = 1;

    public event Action? Changed;
    public int UpsertManyCalls { get; private set; }

    public static Game Copy(Game g)
    {
        var c = new Game { SystemId = g.SystemId, Path = g.Path, Title = g.Title };
        foreach (var p in typeof(Game).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            if (p.CanWrite) p.SetValue(c, p.GetValue(g));
        return c;
    }

    private static LibraryFolder Copy(LibraryFolder f) => new()
    {
        Id = f.Id, Path = f.Path, SystemId = f.SystemId, Recursive = f.Recursive, IsBuiltIn = f.IsBuiltIn,
        Enabled = f.Enabled, LastScanUtc = f.LastScanUtc,
    };

    public IReadOnlyList<Game> All() { lock (_gate) return _games.Values.Select(Copy).OrderBy(g => g.Id).ToList(); }

    public virtual IReadOnlyList<Game> Query(GameQuery query)
    {
        lock (_gate)
        {
            IEnumerable<Game> q = _games.Values;
            if (query.SystemId != null) q = q.Where(g => g.SystemId == query.SystemId);
            if (!query.IncludeHidden) q = q.Where(g => !g.Hidden);
            if (!query.IncludeMissing) q = q.Where(g => g.Status != GameStatus.Missing);
            if (query.Favorite != null) q = q.Where(g => g.Favorite == query.Favorite);
            if (query.Text != null) q = q.Where(g => g.Title.Contains(query.Text, StringComparison.OrdinalIgnoreCase));
            q = q.OrderBy(g => g.SortTitle ?? g.Title, StringComparer.OrdinalIgnoreCase);
            if (query.Limit != null) q = q.Take(query.Limit.Value);
            return q.Select(Copy).ToList();
        }
    }

    public Game? Get(long id) { lock (_gate) return _games.TryGetValue(id, out var g) ? Copy(g) : null; }

    public Game? GetByPath(string portablePath)
    {
        lock (_gate)
            return _games.Values.Where(g => g.Path.Equals(portablePath, StringComparison.OrdinalIgnoreCase)).Select(Copy).FirstOrDefault();
    }

    public long Upsert(Game game)
    {
        long id;
        lock (_gate) id = UpsertLocked(game);
        Changed?.Invoke();
        return id;
    }

    private long UpsertLocked(Game game)
    {
        var clash = _games.Values.FirstOrDefault(g => g.Id != game.Id && g.Path.Equals(game.Path, StringComparison.OrdinalIgnoreCase));
        if (clash != null) throw new InvalidOperationException($"UNIQUE constraint failed: games.path ({game.Path})");
        if (game.Id == 0) game.Id = _nextGame++;
        else if (!_games.ContainsKey(game.Id)) throw new InvalidOperationException($"Game {game.Id} does not exist");
        _games[game.Id] = Copy(game);
        return game.Id;
    }

    public void UpsertMany(IEnumerable<Game> games)
    {
        lock (_gate)
        {
            UpsertManyCalls++;
            foreach (var g in games) UpsertLocked(g);
        }
        Changed?.Invoke();
    }

    public void Delete(long id) { lock (_gate) _games.Remove(id); Changed?.Invoke(); }
    public void SetFavorite(long id, bool favorite) { lock (_gate) if (_games.TryGetValue(id, out var g)) g.Favorite = favorite; Changed?.Invoke(); }
    public void SetHidden(long id, bool hidden) { lock (_gate) if (_games.TryGetValue(id, out var g)) g.Hidden = hidden; Changed?.Invoke(); }

    public void AddSession(PlaySession session)
    {
        lock (_gate)
        {
            session.Id = _nextSession++;
            _sessions.Add(session);
            if (_games.TryGetValue(session.GameId, out var g))
            {
                g.PlaySeconds += session.Seconds;
                g.PlayCount++;
                g.LastPlayedUtc = session.EndUtc;
            }
        }
        Changed?.Invoke();
    }

    public IReadOnlyList<PlaySession> Sessions(long gameId, int limit = 50)
    {
        lock (_gate) return _sessions.Where(s => s.GameId == gameId).OrderByDescending(s => s.StartUtc).Take(limit).ToList();
    }

    public LibraryStats Stats()
    {
        lock (_gate)
        {
            var games = _games.Values.ToList();
            return new LibraryStats(games.Count, games.Select(g => g.SystemId).Distinct().Count(), games.Count(g => g.Favorite),
                games.Sum(g => g.PlaySeconds),
                games.GroupBy(g => g.SystemId).ToDictionary(x => x.Key, x => x.Count()),
                games.GroupBy(g => g.SystemId).ToDictionary(x => x.Key, x => x.Sum(g => g.PlaySeconds)));
        }
    }

    public virtual IReadOnlyList<LibraryFolder> Folders() { lock (_gate) return _folders.Values.Select(Copy).OrderBy(f => f.Id).ToList(); }

    public long UpsertFolder(LibraryFolder folder)
    {
        lock (_gate)
        {
            if (folder.Id == 0) folder.Id = _nextFolder++;
            _folders[folder.Id] = Copy(folder);
            return folder.Id;
        }
    }

    public void DeleteFolder(long id) { lock (_gate) _folders.Remove(id); }
    public string? GetState(string key) { lock (_gate) return _state.GetValueOrDefault(key); }
    public void SetState(string key, string? value) { lock (_gate) _state[key] = value; }
    public void Dispose() { }
}

/// <summary>Synchronous IProgress (Progress&lt;T&gt; would post to a sync context and arrive late).</summary>
internal sealed class SyncProgress<T>(Action<T> action) : IProgress<T>
{
    public void Report(T value) => action(value);
}

/// <summary>A temporary portable root (RetroHub layout) that is deleted afterwards.</summary>
internal sealed class TempRoot : IDisposable
{
    /// <summary>Base folder: contains "root" (the portable RetroHub root) and room for external library folders.</summary>
    public string Base { get; }
    public string Root { get; }
    public AppPaths Paths { get; }

    public TempRoot()
    {
        Base = Path.Combine(Path.GetTempPath(), "retrohub-scan-tests", Guid.NewGuid().ToString("N")[..12]);
        Root = Path.Combine(Base, "root");
        Directory.CreateDirectory(Root);
        Paths = new AppPaths(Root, Path.Combine(Root, "app"));
        Directory.CreateDirectory(Paths.Games);
    }

    /// <summary>Writes a file below the root (relative path with '/'), creating folders. Returns the full path.</summary>
    public string Write(string relative, byte[] content)
    {
        var full = Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, content);
        return full;
    }

    public string Write(string relative, string text) => Write(relative, System.Text.Encoding.UTF8.GetBytes(text));

    public string Full(string relative) => Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>A folder outside the portable root (external library folder, stored with an absolute path).</summary>
    public string External(string relative) => Path.Combine(Base, relative.Replace('/', Path.DirectorySeparatorChar));

    public string WriteExternal(string relative, byte[] content)
    {
        var full = External(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, content);
        return full;
    }

    public void Dispose()
    {
        try
        {
            foreach (var f in Directory.EnumerateFileSystemEntries(Base, "*", SearchOption.AllDirectories))
                try { File.SetAttributes(f, FileAttributes.Normal); } catch { }
            Directory.Delete(Base, recursive: true);
        }
        catch { /* best effort */ }
    }
}
