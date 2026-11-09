using System.Globalization;
using RetroHub.Core.Library;

namespace RetroHub.App.Services.Demo;

/// <summary>In-memory <see cref="IGameRepository"/> with the same query semantics the SQLite repository promises.</summary>
public sealed class DemoGameRepository : IGameRepository
{
    private readonly object _gate = new();
    private readonly List<Game> _games;
    private readonly List<LibraryFolder> _folders;
    private readonly List<PlaySession> _sessions = [];
    private readonly Dictionary<string, string> _state = new(StringComparer.Ordinal);
    private long _nextId;
    private long _nextFolderId;
    private long _nextSessionId = 1;

    public event Action? Changed;

    public DemoGameRepository(IEnumerable<Game> games, IEnumerable<LibraryFolder> folders)
    {
        _games = games.Select(Clone).ToList();
        _folders = folders.ToList();
        _nextId = _games.Count == 0 ? 1 : _games.Max(g => g.Id) + 1;
        _nextFolderId = _folders.Count == 0 ? 1 : _folders.Max(f => f.Id) + 1;
    }

    public IReadOnlyList<Game> Query(GameQuery query)
    {
        lock (_gate)
        {
            IEnumerable<Game> q = _games;
            if (!query.IncludeHidden) q = q.Where(g => !g.Hidden);
            if (!query.IncludeMissing) q = q.Where(g => g.Status != GameStatus.Missing);
            if (query.SystemId != null) q = q.Where(g => string.Equals(g.SystemId, query.SystemId, StringComparison.OrdinalIgnoreCase));
            if (query.Favorite is { } fav) q = q.Where(g => g.Favorite == fav);
            if (!string.IsNullOrWhiteSpace(query.Text))
            {
                var text = query.Text.Trim();
                q = q.Where(g => Matches(g.Title, text) || Matches(g.SortTitle, text) || Matches(g.Developer, text) || Matches(g.Genre, text));
            }
            q = query.Sort switch
            {
                GameSort.LastPlayed => q.OrderByDescending(g => g.LastPlayedUtc ?? DateTime.MinValue).ThenBy(SortKey, StringComparer.CurrentCultureIgnoreCase),
                GameSort.Added => q.OrderByDescending(g => g.AddedUtc).ThenBy(SortKey, StringComparer.CurrentCultureIgnoreCase),
                GameSort.PlayTime => q.OrderByDescending(g => g.PlaySeconds).ThenBy(SortKey, StringComparer.CurrentCultureIgnoreCase),
                GameSort.System => q.OrderBy(g => g.SystemId, StringComparer.OrdinalIgnoreCase).ThenBy(SortKey, StringComparer.CurrentCultureIgnoreCase),
                GameSort.Year => q.OrderBy(g => g.Year ?? int.MaxValue).ThenBy(SortKey, StringComparer.CurrentCultureIgnoreCase),
                _ => q.OrderBy(SortKey, StringComparer.CurrentCultureIgnoreCase),
            };
            if (query.Limit is { } limit) q = q.Take(limit);
            return q.Select(Clone).ToList();
        }
    }

    private static string SortKey(Game g) => g.SortTitle ?? g.Title;

    private static bool Matches(string? value, string text) =>
        value != null && CultureInfo.InvariantCulture.CompareInfo.IndexOf(value, text,
            CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;

    public Game? Get(long id) { lock (_gate) return _games.FirstOrDefault(g => g.Id == id) is { } g ? Clone(g) : null; }

    public Game? GetByPath(string portablePath)
    {
        lock (_gate) return _games.FirstOrDefault(g => string.Equals(g.Path, portablePath, StringComparison.OrdinalIgnoreCase)) is { } g ? Clone(g) : null;
    }

    public long Upsert(Game game)
    {
        long id;
        lock (_gate) id = UpsertLocked(game);
        RaiseChanged();
        return id;
    }

    public void UpsertMany(IEnumerable<Game> games)
    {
        lock (_gate) foreach (var g in games) UpsertLocked(g);
        RaiseChanged();
    }

    private long UpsertLocked(Game game)
    {
        if (game.Id == 0) game.Id = _nextId++;
        var i = _games.FindIndex(g => g.Id == game.Id);
        if (i >= 0) _games[i] = Clone(game); else _games.Add(Clone(game));
        return game.Id;
    }

    public void Delete(long id) { lock (_gate) _games.RemoveAll(g => g.Id == id); RaiseChanged(); }

    public void SetFavorite(long id, bool favorite) => Mutate(id, g => g.Favorite = favorite);
    public void SetHidden(long id, bool hidden) => Mutate(id, g => g.Hidden = hidden);

    public void AddSession(PlaySession session)
    {
        lock (_gate)
        {
            session.Id = _nextSessionId++;
            _sessions.Add(session);
            var g = _games.FirstOrDefault(x => x.Id == session.GameId);
            if (g != null)
            {
                g.PlaySeconds += Math.Max(0, session.Seconds);
                g.PlayCount++;
                g.LastPlayedUtc = session.EndUtc;
            }
        }
        RaiseChanged();
    }

    public IReadOnlyList<PlaySession> Sessions(long gameId, int limit = 50)
    {
        lock (_gate) return _sessions.Where(s => s.GameId == gameId).OrderByDescending(s => s.StartUtc).Take(limit).ToList();
    }

    public LibraryStats Stats()
    {
        lock (_gate)
        {
            var visible = _games.Where(g => !g.Hidden).ToList();
            var perSystem = visible.GroupBy(g => g.SystemId).ToDictionary(g => g.Key, g => g.Count());
            var playPerSystem = visible.GroupBy(g => g.SystemId).ToDictionary(g => g.Key, g => g.Sum(x => x.PlaySeconds));
            return new LibraryStats(visible.Count, perSystem.Count, visible.Count(g => g.Favorite), visible.Sum(g => g.PlaySeconds),
                perSystem, playPerSystem);
        }
    }

    public IReadOnlyList<LibraryFolder> Folders() { lock (_gate) return _folders.Select(CloneFolder).ToList(); }

    public long UpsertFolder(LibraryFolder folder)
    {
        lock (_gate)
        {
            if (folder.Id == 0) folder.Id = _nextFolderId++;
            var i = _folders.FindIndex(f => f.Id == folder.Id);
            if (i >= 0) _folders[i] = CloneFolder(folder); else _folders.Add(CloneFolder(folder));
        }
        RaiseChanged();
        return folder.Id;
    }

    public void DeleteFolder(long id) { lock (_gate) _folders.RemoveAll(f => f.Id == id && !f.IsBuiltIn); RaiseChanged(); }

    public string? GetState(string key) { lock (_gate) return _state.TryGetValue(key, out var v) ? v : null; }

    public void SetState(string key, string? value)
    {
        lock (_gate) { if (value == null) _state.Remove(key); else _state[key] = value; }
    }

    private void Mutate(long id, Action<Game> change)
    {
        lock (_gate)
        {
            var g = _games.FirstOrDefault(x => x.Id == id);
            if (g == null) return;
            change(g);
        }
        RaiseChanged();
    }

    private void RaiseChanged()
    {
        try { Changed?.Invoke(); } catch { /* listeners must not break the repository */ }
    }

    private static Game Clone(Game g) => new()
    {
        Id = g.Id, SystemId = g.SystemId, Path = g.Path, Title = g.Title, SortTitle = g.SortTitle, Kind = g.Kind, Status = g.Status,
        Size = g.Size, FileTimeUtc = g.FileTimeUtc, Crc32 = g.Crc32, Serial = g.Serial, Region = g.Region, AddedUtc = g.AddedUtc,
        LastPlayedUtc = g.LastPlayedUtc, PlaySeconds = g.PlaySeconds, PlayCount = g.PlayCount, Favorite = g.Favorite, Hidden = g.Hidden,
        EmulatorOverride = g.EmulatorOverride, LaunchModeOverride = g.LaunchModeOverride, CoverPath = g.CoverPath,
        BackgroundPath = g.BackgroundPath, LogoPath = g.LogoPath, Developer = g.Developer, Publisher = g.Publisher, Genre = g.Genre,
        Year = g.Year, Players = g.Players, Description = g.Description, ExtraArgs = g.ExtraArgs, FolderId = g.FolderId,
    };

    private static LibraryFolder CloneFolder(LibraryFolder f) => new()
    {
        Id = f.Id, Path = f.Path, SystemId = f.SystemId, Recursive = f.Recursive, IsBuiltIn = f.IsBuiltIn, Enabled = f.Enabled,
        LastScanUtc = f.LastScanUtc,
    };

    public void Dispose() { }
}
