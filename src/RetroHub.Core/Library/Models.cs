namespace RetroHub.Core.Library;

public enum GameStatus
{
    /// <summary>File exists and system is known.</summary>
    Ok = 0,
    /// <summary>File was not found during the last scan (kept in the DB: playtime/favourite survive a moved drive).</summary>
    Missing = 1,
    /// <summary>File looks damaged (empty, unreadable, header check failed).</summary>
    Broken = 2,
}

public enum GameKind
{
    Rom = 0,
    /// <summary>Playlist/descriptor (.m3u, .cue, .gdi) that references other files.</summary>
    Playlist = 1,
    /// <summary>Windows executable (portable PC game).</summary>
    Executable = 2,
    /// <summary>Windows shortcut (.lnk) or URL (steam://...) to an installed PC game.</summary>
    Shortcut = 3,
}

/// <summary>One game in the library. Paths are portable (see AppPaths.ToPortable).</summary>
public sealed class Game
{
    public long Id { get; set; }
    public required string SystemId { get; set; }
    /// <summary>Portable path of the main file (or the exe / .lnk for PC games).</summary>
    public required string Path { get; set; }
    public required string Title { get; set; }
    public string? SortTitle { get; set; }
    public GameKind Kind { get; set; }
    public GameStatus Status { get; set; }
    public long Size { get; set; }
    /// <summary>Last write time (UTC) of the file at the last scan.</summary>
    public DateTime FileTimeUtc { get; set; }
    /// <summary>CRC32 of the ROM contents (uppercase hex, 8 chars), computed lazily for files &lt; 64 MB; null if not computed.</summary>
    public string? Crc32 { get; set; }
    /// <summary>Header data found by a sniffer (e.g. GBA game code "BPEE"), used for metadata matching.</summary>
    public string? Serial { get; set; }
    public string? Region { get; set; }
    public DateTime AddedUtc { get; set; }
    public DateTime? LastPlayedUtc { get; set; }
    public long PlaySeconds { get; set; }
    public int PlayCount { get; set; }
    public bool Favorite { get; set; }
    public bool Hidden { get; set; }
    /// <summary>Per-game override "emulator" or "emulator/core"; null = system default.</summary>
    public string? EmulatorOverride { get; set; }
    /// <summary>Per-game launch mode override; null = emulator default.</summary>
    public Catalog.LaunchMode? LaunchModeOverride { get; set; }
    /// <summary>Portable paths of artwork; null = not found yet.</summary>
    public string? CoverPath { get; set; }
    public string? BackgroundPath { get; set; }
    public string? LogoPath { get; set; }
    public string? Developer { get; set; }
    public string? Publisher { get; set; }
    public string? Genre { get; set; }
    public int? Year { get; set; }
    public int? Players { get; set; }
    public string? Description { get; set; }
    /// <summary>Extra launch arguments (PC games) appended after the configured ones.</summary>
    public string? ExtraArgs { get; set; }
    /// <summary>Id of the library folder (LibraryFolder.Id) the game was found in; null = added manually.</summary>
    public long? FolderId { get; set; }

    public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? System.IO.Path.GetFileNameWithoutExtension(Path) : Title;
}

/// <summary>A folder RetroHub scans. The built-in games/ folder is always present (IsBuiltIn).</summary>
public sealed class LibraryFolder
{
    public long Id { get; set; }
    /// <summary>Portable path.</summary>
    public required string Path { get; set; }
    /// <summary>Fixed system for every file in this folder; null = detect per subfolder/extension/header.</summary>
    public string? SystemId { get; set; }
    public bool Recursive { get; set; } = true;
    public bool IsBuiltIn { get; set; }
    public bool Enabled { get; set; } = true;
    public DateTime? LastScanUtc { get; set; }
}

public sealed class PlaySession
{
    public long Id { get; set; }
    public long GameId { get; set; }
    public string? EmulatorId { get; set; }
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
    public long Seconds { get; set; }
    /// <summary>Exit code of the emulator process; null when unknown (killed / detached).</summary>
    public int? ExitCode { get; set; }
}

public sealed record LibraryStats(int GameCount, int SystemCount, int FavoriteCount, long TotalPlaySeconds,
    IReadOnlyDictionary<string, int> GamesPerSystem, IReadOnlyDictionary<string, long> PlaySecondsPerSystem);

public sealed record GameQuery
{
    public string? SystemId { get; init; }
    /// <summary>Free text, matched against title/sort title/developer/genre (case- and diacritics-insensitive).</summary>
    public string? Text { get; init; }
    public bool? Favorite { get; init; }
    public bool IncludeHidden { get; init; }
    public bool IncludeMissing { get; init; } = true;
    public GameSort Sort { get; init; } = GameSort.Title;
    public int? Limit { get; init; }
}

public enum GameSort { Title, LastPlayed, Added, PlayTime, System, Year }

/// <summary>Persistent library storage (SQLite, data/database/retrohub.db). All methods are thread-safe.</summary>
public interface IGameRepository : IDisposable
{
    IReadOnlyList<Game> Query(GameQuery query);
    Game? Get(long id);
    Game? GetByPath(string portablePath);
    /// <summary>Insert (Id == 0) or update. Returns the id.</summary>
    long Upsert(Game game);
    void UpsertMany(IEnumerable<Game> games);
    /// <summary>Removes the DB entry only. Never touches the file.</summary>
    void Delete(long id);
    void SetFavorite(long id, bool favorite);
    void SetHidden(long id, bool hidden);
    /// <summary>Adds a finished session and updates PlaySeconds/PlayCount/LastPlayedUtc atomically.</summary>
    void AddSession(PlaySession session);
    IReadOnlyList<PlaySession> Sessions(long gameId, int limit = 50);
    LibraryStats Stats();

    IReadOnlyList<LibraryFolder> Folders();
    long UpsertFolder(LibraryFolder folder);
    void DeleteFolder(long id);

    /// <summary>Simple key/value store for small app state (e.g. last selected system).</summary>
    string? GetState(string key);
    void SetState(string key, string? value);

    /// <summary>Raised after any change (any thread) so the UI can refresh.</summary>
    event Action? Changed;
}
