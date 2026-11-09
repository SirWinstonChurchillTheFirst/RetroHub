using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;
using RetroHub.Core.Catalog;
using RetroHub.Core.IO;
using RetroHub.Core.Library;
using RetroHub.Core.Logging;

namespace RetroHub.Core.Data;

/// <summary>
/// SQLite implementation of <see cref="IGameRepository"/> (data/database/retrohub.db).
/// <list type="bullet">
/// <item>Schema via <see cref="Migrations"/> keyed on PRAGMA user_version.</item>
/// <item>WAL journal (DELETE on network/UNC drives), synchronous=NORMAL, foreign_keys=ON, busy_timeout=5000.</item>
/// <item>Connection per operation (pooled). Reads run concurrently; writes are serialised by a lock and run in one transaction each.</item>
/// <item>A file that is not a valid SQLite database is copied aside (SafeFile.PreserveCorrupt) and replaced by a fresh one.
///   If the file cannot be used at all (locked, read-only, preserve failed), the repository runs on a temporary
///   in-memory database instead of crashing (<see cref="IsTemporary"/>).</item>
/// <item>Play statistics (PlaySeconds/PlayCount/LastPlayedUtc) are owned by <see cref="AddSession"/>; Upsert never lowers them.</item>
/// </list>
/// </summary>
public sealed class SqliteGameRepository : IGameRepository
{
    private const string DateFormat = "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'";
    private const int SqliteConstraint = 19, SqliteCorrupt = 11, SqliteNotADb = 26;
    private const int MaxSearchTokens = 8;

    private readonly object _writeLock = new();
    private string _connectionString = "";
    private SqliteConnection? _keepAlive; // only for the in-memory fallback
    private volatile bool _disposed;

    /// <summary>Full path of the database file this repository was asked to open.</summary>
    public string DatabaseFile { get; }

    /// <summary>True when the file could not be used and the library lives in a temporary in-memory database (lost on exit).</summary>
    public bool IsTemporary { get; private set; }

    /// <summary>Where a corrupt database file was preserved during startup (null = nothing was corrupt).</summary>
    public string? PreservedCorruptFile { get; private set; }

    /// <summary>Effective journal mode ("wal", "delete", "memory").</summary>
    internal string JournalMode { get; private set; } = "";

    internal int SchemaVersion { get; private set; }

    public event Action? Changed;

    public SqliteGameRepository(string databaseFile)
    {
        if (string.IsNullOrWhiteSpace(databaseFile)) throw new ArgumentException("Database file path is empty.", nameof(databaseFile));
        DatabaseFile = Path.GetFullPath(databaseFile);
        Open();
    }

    // ───────────────────────────── startup / recovery ─────────────────────────────

    private void Open()
    {
        var fileCs = new SqliteConnectionStringBuilder
        {
            DataSource = DatabaseFile,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = true,
        }.ToString();

        bool corrupt;
        try
        {
            var dir = Path.GetDirectoryName(DatabaseFile);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            // Checked before SQLite touches the file: on a failed open SQLite may delete/replay the -wal next to it,
            // so a foreign file must be preserved together with its side files first.
            if (HasForeignHeader(DatabaseFile)) throw new CorruptDatabaseException("file does not start with the SQLite header");
            InitializeAt(fileCs, isFile: true);
            _connectionString = fileCs;
            return;
        }
        catch (Exception ex) when (IsCorruption(ex))
        {
            Log.Error($"Library database '{DatabaseFile}' is damaged or not a SQLite database", ex);
            corrupt = true;
        }
        catch (Exception ex)
        {
            Log.Error($"Library database '{DatabaseFile}' cannot be opened; using a temporary in-memory library", ex);
            corrupt = false;
        }

        if (corrupt && TryRecover(fileCs)) return;
        ClearPool(fileCs);
        OpenInMemoryFallback();
    }

    private bool TryRecover(string fileCs)
    {
        ClearPool(fileCs);
        string? preserved;
        try { preserved = SafeFile.PreserveCorrupt(DatabaseFile); }
        catch (Exception ex) // PreserveCorrupt only catches IOException (e.g. UnauthorizedAccessException escapes)
        {
            Log.Error($"Preserving the damaged database '{DatabaseFile}' failed", ex);
            preserved = null;
        }
        if (preserved == null)
        {
            Log.Error($"Could not preserve the damaged database '{DatabaseFile}'; leaving it untouched and using a temporary in-memory library");
            return false;
        }
        // The WAL / rollback journal may hold the newest pages: they are deleted below, so they must be copied first.
        // If a copy fails, nothing is deleted (the user's data must never be lost).
        foreach (var suffix in new[] { "-wal", "-journal" })
        {
            var side = DatabaseFile + suffix;
            try { if (File.Exists(side)) File.Copy(side, preserved + suffix, overwrite: true); }
            catch (Exception ex)
            {
                Log.Error($"Could not preserve '{side}'; leaving the damaged database untouched and using a temporary in-memory library", ex);
                PreservedCorruptFile = preserved;
                return false;
            }
        }
        try
        {
            // Side files first: a stale -wal next to a fresh database file would be replayed into it.
            foreach (var suffix in new[] { "-wal", "-shm", "-journal", "" })
            {
                var f = DatabaseFile + suffix;
                if (File.Exists(f)) File.Delete(f);
            }
            InitializeAt(fileCs, isFile: true);
            _connectionString = fileCs;
            PreservedCorruptFile = preserved;
            Log.Warn($"Damaged library database preserved as '{preserved}'; a new empty database was created");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"Could not recreate the library database '{DatabaseFile}'", ex);
            PreservedCorruptFile = preserved;
            return false;
        }
    }

    private void OpenInMemoryFallback()
    {
        var cs = new SqliteConnectionStringBuilder
        {
            DataSource = "retrohub-fallback-" + Guid.NewGuid().ToString("N"),
            Mode = SqliteOpenMode.Memory,
            Cache = SqliteCacheMode.Shared,
            Pooling = true,
        }.ToString();
        _keepAlive = new SqliteConnection(cs);
        _keepAlive.Open();
        InitializeAt(cs, isFile: false);
        _connectionString = cs;
        IsTemporary = true;
    }

    private void InitializeAt(string cs, bool isFile)
    {
        using var c = new SqliteConnection(cs);
        c.Open();
        Exec(c, null, "PRAGMA busy_timeout = 5000;");

        // A non-SQLite file fails here with SQLITE_NOTADB; a damaged one returns something other than "ok".
        var check = Scalar(c, null, "PRAGMA quick_check(1);") as string;
        if (!string.Equals(check, "ok", StringComparison.OrdinalIgnoreCase))
            throw new CorruptDatabaseException($"quick_check: {check}");

        if (isFile)
        {
            var desired = IsNetworkPath(DatabaseFile) ? "delete" : "wal";
            JournalMode = (Scalar(c, null, $"PRAGMA journal_mode = {desired};") as string ?? "").ToLowerInvariant();
            if (JournalMode != desired) Log.Warn($"Database journal mode is '{JournalMode}' (wanted '{desired}')");
        }
        else JournalMode = (Scalar(c, null, "PRAGMA journal_mode;") as string ?? "").ToLowerInvariant();

        Exec(c, null, "PRAGMA foreign_keys = ON; PRAGMA synchronous = NORMAL;");
        SchemaVersion = Migrations.Migrate(c);
    }

    private static bool IsCorruption(Exception ex) => ex switch
    {
        CorruptDatabaseException => true,
        SqliteException se => (se.SqliteErrorCode & 0xFF) is SqliteCorrupt or SqliteNotADb,
        _ => false,
    };

    private sealed class CorruptDatabaseException(string message) : Exception(message);

    private static ReadOnlySpan<byte> SqliteMagic => "SQLite format 3\0"u8;

    /// <summary>True when the file exists, is not empty and does not start with the SQLite magic (an empty file is a valid new database).</summary>
    private static bool HasForeignHeader(string file)
    {
        try
        {
            if (!File.Exists(file)) return false;
            using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (fs.Length == 0) return false;
            Span<byte> head = stackalloc byte[16];
            var n = fs.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
            return n < head.Length || !head.SequenceEqual(SqliteMagic);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false; // unreadable: let SQLite report it (falls back to the in-memory library without deleting anything)
        }
    }

    /// <summary>
    /// True for UNC paths (\\server\share, \\?\UNC\...) and mapped network drives.
    /// WAL needs shared memory, which does not work reliably on network file systems.
    /// </summary>
    internal static bool IsNetworkPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        var p = path.Replace('/', '\\');
        if (p.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) return true;
        if (p.StartsWith(@"\\?\", StringComparison.Ordinal) || p.StartsWith(@"\\.\", StringComparison.Ordinal)) p = p[4..];
        else if (p.StartsWith(@"\\", StringComparison.Ordinal)) return true;
        try
        {
            var root = Path.GetPathRoot(p);
            if (string.IsNullOrEmpty(root) || !OperatingSystem.IsWindows()) return false;
            return new DriveInfo(root).DriveType == DriveType.Network;
        }
        catch (Exception) { return false; }
    }

    // ───────────────────────────── connections / helpers ─────────────────────────────

    private SqliteConnection Connect()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var c = new SqliteConnection(_connectionString);
        try
        {
            c.Open();
            Exec(c, null, "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000; PRAGMA synchronous = NORMAL;");
            return c;
        }
        catch
        {
            c.Dispose();
            throw;
        }
    }

    /// <summary>Runs <paramref name="work"/> in one write transaction under the write lock; raises Changed after commit when it reports a change.</summary>
    private T Write<T>(Func<SqliteConnection, SqliteTransaction, (T Result, bool Changed)> work)
    {
        (T Result, bool Changed) r;
        lock (_writeLock)
        {
            using var c = Connect();
            using var tx = c.BeginTransaction(deferred: false);
            r = work(c, tx);
            tx.Commit();
        }
        if (r.Changed) RaiseChanged();
        return r.Result;
    }

    private void RaiseChanged()
    {
        var handlers = Changed;
        if (handlers == null) return;
        foreach (var d in handlers.GetInvocationList())
        {
            try { ((Action)d)(); }
            catch (Exception ex) { Log.Warn("Library Changed listener failed", ex); }
        }
    }

    private static SqliteCommand Cmd(SqliteConnection c, SqliteTransaction? tx, string sql)
    {
        var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        return cmd;
    }

    private static int Exec(SqliteConnection c, SqliteTransaction? tx, string sql, params (string Name, object? Value)[] args)
    {
        using var cmd = Cmd(c, tx, sql);
        foreach (var (n, v) in args) cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
        return cmd.ExecuteNonQuery();
    }

    private static object? Scalar(SqliteConnection c, SqliteTransaction? tx, string sql, params (string Name, object? Value)[] args)
    {
        using var cmd = Cmd(c, tx, sql);
        foreach (var (n, v) in args) cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
        var r = cmd.ExecuteScalar();
        return r is DBNull ? null : r;
    }

    /// <summary>The UTC instant a value is stored as: Local is converted, Unspecified is taken as UTC.</summary>
    private static DateTime AsUtc(DateTime dt) => dt.Kind switch
    {
        DateTimeKind.Local => dt.ToUniversalTime(),
        DateTimeKind.Unspecified => DateTime.SpecifyKind(dt, DateTimeKind.Utc),
        _ => dt,
    };

    internal static string ToDb(DateTime dt) => AsUtc(dt).ToString(DateFormat, CultureInfo.InvariantCulture);

    private static object ToDb(DateTime? dt) => dt.HasValue ? ToDb(dt.Value) : DBNull.Value;

    internal static DateTime FromDb(string s)
    {
        if (DateTime.TryParseExact(s, DateFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt))
            return dt;
        return DateTime.TryParse(s, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out dt) ? dt : default;
    }

    private static object Db(string? s) => s == null ? DBNull.Value : s;
    private static object Db(long? v) => v.HasValue ? v.Value : DBNull.Value;

    /// <summary>
    /// Canonical portable path: relative paths use '/', absolute paths (drive or UNC) use '\'.
    /// Matches AppPaths.ToPortable so "games\nes\a.nes" and "games/nes/a.nes" are the same row.
    /// </summary>
    internal static string NormalizePath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var p = path.Trim();
        if (p.Length == 0) throw new ArgumentException("Path is empty.", nameof(path));
        var absolute = (p.Length >= 2 && p[1] == ':') || p.StartsWith(@"\\", StringComparison.Ordinal) || p.StartsWith("//", StringComparison.Ordinal);
        return absolute ? p.Replace('/', '\\') : p.Replace('\\', '/');
    }

    /// <summary>
    /// Case-insensitive identity of a canonical path (games.path_key). Uses full Unicode upper-casing like Windows file
    /// name comparison; SQLite's NOCASE only folds ASCII ("É" ≠ "é").
    /// </summary>
    internal static string PathKey(string normalizedPath) => normalizedPath.ToUpperInvariant();

    /// <summary>Canonical folder path: like <see cref="NormalizePath"/>, without trailing separators ("E:\Roms\" → "E:\Roms"; "E:\" stays).</summary>
    internal static string NormalizeFolderPath(string path)
    {
        var p = NormalizePath(path);
        var trimmed = p.TrimEnd('\\', '/');
        if (trimmed.Length == 0 || trimmed.Length == p.Length) return p;
        if (trimmed.Length == 2 && trimmed[1] == ':') return trimmed + @"\"; // drive root; "E:" alone would be drive-relative
        return trimmed;
    }

    private static string EscapeLike(string s) =>
        s.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");

    // ───────────────────────────── game columns ─────────────────────────────

    private static readonly string[] Columns =
    [
        "system_id", "path", "path_key", "title", "title_norm", "sort_title", "sort_key", "search_norm", "kind", "status", "size",
        "file_time_utc", "crc32", "serial", "region", "added_utc", "last_played_utc", "play_seconds", "play_count",
        "favorite", "hidden", "emulator_override", "launch_mode_override", "cover_path", "background_path", "logo_path",
        "developer", "publisher", "genre", "year", "players", "description", "extra_args", "folder_id",
    ];

    private const string SelectColumns =
        "id, system_id, path, title, sort_title, kind, status, size, file_time_utc, crc32, serial, region, added_utc, " +
        "last_played_utc, play_seconds, play_count, favorite, hidden, emulator_override, launch_mode_override, cover_path, " +
        "background_path, logo_path, developer, publisher, genre, year, players, description, extra_args, folder_id";

    private static readonly string InsertSql =
        $"INSERT INTO games ({string.Join(", ", Columns)}) VALUES ({string.Join(", ", Columns.Select((_, i) => "@p" + i))}) RETURNING id;";

    private static readonly string UpdateSql =
        $"UPDATE games SET {string.Join(", ", Columns.Select((col, i) => $"{col} = @p{i}"))} WHERE id = @id;";

    private static Game ReadGame(SqliteDataReader r)
    {
        string? S(int i) => r.IsDBNull(i) ? null : r.GetString(i);
        int? I(int i) => r.IsDBNull(i) ? null : r.GetInt32(i);
        return new Game
        {
            Id = r.GetInt64(0),
            SystemId = r.GetString(1),
            Path = r.GetString(2),
            Title = r.GetString(3),
            SortTitle = S(4),
            Kind = (GameKind)r.GetInt32(5),
            Status = (GameStatus)r.GetInt32(6),
            Size = r.GetInt64(7),
            FileTimeUtc = FromDb(r.GetString(8)),
            Crc32 = S(9),
            Serial = S(10),
            Region = S(11),
            AddedUtc = FromDb(r.GetString(12)),
            LastPlayedUtc = r.IsDBNull(13) ? null : FromDb(r.GetString(13)),
            PlaySeconds = r.GetInt64(14),
            PlayCount = r.GetInt32(15),
            Favorite = r.GetInt64(16) != 0,
            Hidden = r.GetInt64(17) != 0,
            EmulatorOverride = S(18),
            LaunchModeOverride = r.IsDBNull(19) ? null : (LaunchMode)r.GetInt32(19),
            CoverPath = S(20),
            BackgroundPath = S(21),
            LogoPath = S(22),
            Developer = S(23),
            Publisher = S(24),
            Genre = S(25),
            Year = I(26),
            Players = I(27),
            Description = S(28),
            ExtraArgs = S(29),
            FolderId = r.IsDBNull(30) ? null : r.GetInt64(30),
        };
    }

    private static object?[] GameValues(Game g, string path)
    {
        var title = g.Title ?? "";
        var sortKey = SearchText.Normalize(string.IsNullOrWhiteSpace(g.SortTitle) ? title : g.SortTitle);
        // \u001F separates fields so a search token never spans two fields (Normalize never emits it).
        var search = string.Join('\u001F',
            SearchText.Normalize(title), SearchText.Normalize(g.SortTitle), SearchText.Normalize(g.Developer), SearchText.Normalize(g.Genre));
        return
        [
            g.SystemId, path, PathKey(path), title, SearchText.Normalize(title), Db(g.SortTitle), sortKey, search, (int)g.Kind, (int)g.Status, g.Size,
            ToDb(g.FileTimeUtc), Db(g.Crc32), Db(g.Serial), Db(g.Region), ToDb(g.AddedUtc), ToDb(g.LastPlayedUtc), Math.Max(0, g.PlaySeconds),
            Math.Max(0, g.PlayCount), g.Favorite ? 1 : 0, g.Hidden ? 1 : 0, Db(g.EmulatorOverride),
            g.LaunchModeOverride.HasValue ? (int)g.LaunchModeOverride.Value : DBNull.Value, Db(g.CoverPath), Db(g.BackgroundPath),
            Db(g.LogoPath), Db(g.Developer), Db(g.Publisher), Db(g.Genre), Db(g.Year), Db(g.Players), Db(g.Description),
            Db(g.ExtraArgs), Db(g.FolderId),
        ];
    }

    /// <summary>Prepared commands for one write transaction (reused across rows by UpsertMany).</summary>
    private sealed class GameWriter : IDisposable
    {
        private readonly SqliteCommand _byId, _byPath, _insert, _update;

        public GameWriter(SqliteConnection c, SqliteTransaction tx)
        {
            _byId = Cmd(c, tx, $"SELECT {SelectColumns} FROM games WHERE id = @id;");
            _byId.Parameters.Add("@id", SqliteType.Integer);
            _byPath = Cmd(c, tx, $"SELECT {SelectColumns} FROM games WHERE path_key = @key;");
            _byPath.Parameters.Add("@key", SqliteType.Text);
            _insert = Cmd(c, tx, InsertSql);
            _update = Cmd(c, tx, UpdateSql);
            for (var i = 0; i < Columns.Length; i++)
            {
                _insert.Parameters.Add(new SqliteParameter("@p" + i, null));
                _update.Parameters.Add(new SqliteParameter("@p" + i, null));
            }
            _update.Parameters.Add("@id", SqliteType.Integer);
        }

        private static Game? ReadOne(SqliteCommand cmd)
        {
            using var r = cmd.ExecuteReader();
            return r.Read() ? ReadGame(r) : null;
        }

        public long Upsert(Game game)
        {
            ArgumentNullException.ThrowIfNull(game);
            if (string.IsNullOrWhiteSpace(game.SystemId)) throw new ArgumentException("Game.SystemId is empty.", nameof(game));
            var path = NormalizePath(game.Path);

            Game? existing = null;
            var matchedById = false;
            if (game.Id > 0)
            {
                _byId.Parameters["@id"].Value = game.Id;
                existing = ReadOne(_byId);
                matchedById = existing != null;
            }
            if (existing == null)
            {
                _byPath.Parameters["@key"].Value = PathKey(path);
                existing = ReadOne(_byPath);
            }

            if (existing == null)
            {
                var row = game.AddedUtc == default ? Clone(game, addedUtc: DateTime.UtcNow) : game;
                Bind(_insert, GameValues(row, path));
                var id = Convert.ToInt64(_insert.ExecuteScalar(), CultureInfo.InvariantCulture);
                game.Id = id;
                return id;
            }

            var merged = matchedById ? MergeById(existing, game) : MergeByPath(existing, game);
            Bind(_update, GameValues(merged, path));
            _update.Parameters["@id"].Value = existing.Id;
            _update.ExecuteNonQuery();
            game.Id = existing.Id;
            return existing.Id;
        }

        private static void Bind(SqliteCommand cmd, object?[] values)
        {
            for (var i = 0; i < values.Length; i++) cmd.Parameters[i].Value = values[i] ?? DBNull.Value;
        }

        public void Dispose()
        {
            _byId.Dispose(); _byPath.Dispose(); _insert.Dispose(); _update.Dispose();
        }
    }

    /// <summary>Update of a known row: caller's values win, but play statistics never go down and AddedUtc is kept if not given.</summary>
    private static Game MergeById(Game old, Game g)
    {
        var m = Clone(g, addedUtc: g.AddedUtc == default ? old.AddedUtc : g.AddedUtc);
        KeepPlayStats(old, m);
        return m;
    }

    /// <summary>
    /// A fresh object (Id == 0) for a path already in the library (e.g. a re-scan or import): file/scan fields come from
    /// the new object; user data survives (playtime, favourite, hidden, added date) and null metadata does not erase
    /// existing metadata/overrides.
    /// </summary>
    private static Game MergeByPath(Game old, Game g)
    {
        var m = Clone(g, addedUtc: old.AddedUtc);
        m.Favorite = old.Favorite || g.Favorite;
        m.Hidden = old.Hidden || g.Hidden;
        m.SortTitle = g.SortTitle ?? old.SortTitle;
        m.Crc32 = g.Crc32 ?? old.Crc32;
        m.Serial = g.Serial ?? old.Serial;
        m.Region = g.Region ?? old.Region;
        m.EmulatorOverride = g.EmulatorOverride ?? old.EmulatorOverride;
        m.LaunchModeOverride = g.LaunchModeOverride ?? old.LaunchModeOverride;
        m.CoverPath = g.CoverPath ?? old.CoverPath;
        m.BackgroundPath = g.BackgroundPath ?? old.BackgroundPath;
        m.LogoPath = g.LogoPath ?? old.LogoPath;
        m.Developer = g.Developer ?? old.Developer;
        m.Publisher = g.Publisher ?? old.Publisher;
        m.Genre = g.Genre ?? old.Genre;
        m.Year = g.Year ?? old.Year;
        m.Players = g.Players ?? old.Players;
        m.Description = g.Description ?? old.Description;
        m.ExtraArgs = g.ExtraArgs ?? old.ExtraArgs;
        m.FolderId = g.FolderId ?? old.FolderId;
        KeepPlayStats(old, m);
        return m;
    }

    private static void KeepPlayStats(Game old, Game m)
    {
        m.PlaySeconds = Math.Max(old.PlaySeconds, m.PlaySeconds);
        m.PlayCount = Math.Max(old.PlayCount, m.PlayCount);
        // Compare the way the values are stored (ToDb treats Unspecified as UTC; ToUniversalTime would treat it as local).
        if (old.LastPlayedUtc.HasValue && (!m.LastPlayedUtc.HasValue || AsUtc(m.LastPlayedUtc.Value) < AsUtc(old.LastPlayedUtc.Value)))
            m.LastPlayedUtc = old.LastPlayedUtc;
    }

    private static Game Clone(Game g, DateTime addedUtc) => new()
    {
        Id = g.Id, SystemId = g.SystemId, Path = g.Path, Title = g.Title ?? "", SortTitle = g.SortTitle, Kind = g.Kind,
        Status = g.Status, Size = g.Size, FileTimeUtc = g.FileTimeUtc, Crc32 = g.Crc32, Serial = g.Serial, Region = g.Region,
        AddedUtc = addedUtc, LastPlayedUtc = g.LastPlayedUtc, PlaySeconds = g.PlaySeconds, PlayCount = g.PlayCount,
        Favorite = g.Favorite, Hidden = g.Hidden, EmulatorOverride = g.EmulatorOverride, LaunchModeOverride = g.LaunchModeOverride,
        CoverPath = g.CoverPath, BackgroundPath = g.BackgroundPath, LogoPath = g.LogoPath, Developer = g.Developer,
        Publisher = g.Publisher, Genre = g.Genre, Year = g.Year, Players = g.Players, Description = g.Description,
        ExtraArgs = g.ExtraArgs, FolderId = g.FolderId,
    };

    // ───────────────────────────── games: reads ─────────────────────────────

    public IReadOnlyList<Game> Query(GameQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        var sql = new StringBuilder($"SELECT {SelectColumns} FROM games");
        var where = new List<string>();
        using var c = Connect();
        using var cmd = c.CreateCommand();

        if (!string.IsNullOrWhiteSpace(query.SystemId))
        {
            where.Add("system_id = @system");
            cmd.Parameters.AddWithValue("@system", query.SystemId);
        }
        if (query.Favorite.HasValue)
        {
            where.Add("favorite = @fav");
            cmd.Parameters.AddWithValue("@fav", query.Favorite.Value ? 1 : 0);
        }
        if (!query.IncludeHidden) where.Add("hidden = 0");
        if (!query.IncludeMissing) where.Add($"status <> {(int)GameStatus.Missing}");

        var tokens = SearchText.Tokens(SearchText.Normalize(query.Text));
        for (var i = 0; i < tokens.Length && i < MaxSearchTokens; i++)
        {
            where.Add($"search_norm LIKE @t{i} ESCAPE '\\'");
            cmd.Parameters.AddWithValue("@t" + i, "%" + EscapeLike(tokens[i]) + "%");
        }

        if (where.Count > 0) sql.Append(" WHERE ").AppendJoin(" AND ", where);
        sql.Append(" ORDER BY ").Append(query.Sort switch
        {
            GameSort.LastPlayed => "last_played_utc DESC NULLS LAST, sort_key, id",
            GameSort.Added => "added_utc DESC, id DESC",
            GameSort.PlayTime => "play_seconds DESC, sort_key, id",
            GameSort.System => "system_id COLLATE NOCASE, sort_key, COALESCE(sort_title, title) COLLATE NOCASE, id",
            GameSort.Year => "year NULLS LAST, sort_key, id",
            _ => "sort_key, COALESCE(sort_title, title) COLLATE NOCASE, id",
        });
        if (query.Limit is > 0)
        {
            sql.Append(" LIMIT @limit");
            cmd.Parameters.AddWithValue("@limit", query.Limit.Value);
        }
        cmd.CommandText = sql.ToString();

        var list = new List<Game>(query.Limit is > 0 and < 10_000 ? query.Limit.Value : 256);
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(ReadGame(r));
        return list;
    }

    public Game? Get(long id)
    {
        using var c = Connect();
        using var cmd = Cmd(c, null, $"SELECT {SelectColumns} FROM games WHERE id = @id;");
        cmd.Parameters.AddWithValue("@id", id);
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadGame(r) : null;
    }

    public Game? GetByPath(string portablePath)
    {
        if (string.IsNullOrWhiteSpace(portablePath)) return null;
        using var c = Connect();
        using var cmd = Cmd(c, null, $"SELECT {SelectColumns} FROM games WHERE path_key = @key;");
        cmd.Parameters.AddWithValue("@key", PathKey(NormalizePath(portablePath)));
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadGame(r) : null;
    }

    // ───────────────────────────── games: writes ─────────────────────────────

    /// <summary>
    /// Insert or update. Matching: by Id when Id &gt; 0 and the row exists, otherwise by path (case-insensitive) — so the
    /// same file never gets two rows. Sets <c>game.Id</c>. Play statistics never decrease (see class remarks).
    /// Throws when the update would violate a constraint (path already used by another game, unknown FolderId).
    /// </summary>
    public long Upsert(Game game)
    {
        ArgumentNullException.ThrowIfNull(game);
        return Write((c, tx) =>
        {
            using var w = new GameWriter(c, tx);
            return (w.Upsert(game), true);
        });
    }

    /// <summary>
    /// Upserts all games in a single transaction (same rules as <see cref="Upsert"/>; sets each game's Id).
    /// A row that violates a constraint is skipped and logged instead of rolling back the whole batch.
    /// </summary>
    public void UpsertMany(IEnumerable<Game> games)
    {
        ArgumentNullException.ThrowIfNull(games);
        var list = games as IReadOnlyCollection<Game> ?? games.ToList();
        if (list.Count == 0) return;
        Write((c, tx) =>
        {
            using var w = new GameWriter(c, tx);
            var written = 0;
            foreach (var g in list)
            {
                if (g == null) continue;
                try { w.Upsert(g); written++; }
                catch (SqliteException ex) when ((ex.SqliteErrorCode & 0xFF) == SqliteConstraint)
                {
                    Log.Warn($"Skipped game '{g.Path}' while saving the library (constraint violation)", ex);
                }
                catch (ArgumentException ex)
                {
                    Log.Warn("Skipped invalid game while saving the library", ex);
                }
            }
            return (written, written > 0);
        });
    }

    public void Delete(long id) =>
        Write((c, tx) => (0, Exec(c, tx, "DELETE FROM games WHERE id = @id;", ("@id", id)) > 0));

    public void SetFavorite(long id, bool favorite) =>
        Write((c, tx) => (0, Exec(c, tx, "UPDATE games SET favorite = @v WHERE id = @id AND favorite <> @v;",
            ("@id", id), ("@v", favorite ? 1 : 0)) > 0));

    public void SetHidden(long id, bool hidden) =>
        Write((c, tx) => (0, Exec(c, tx, "UPDATE games SET hidden = @v WHERE id = @id AND hidden <> @v;",
            ("@id", id), ("@v", hidden ? 1 : 0)) > 0));

    // ───────────────────────────── sessions ─────────────────────────────

    /// <summary>
    /// Stores a finished session and adds it to the game's totals (seconds, count, last played = max) in one transaction.
    /// Sets <c>session.Id</c>. A session for a game that no longer exists is logged and dropped (no exception).
    /// </summary>
    public void AddSession(PlaySession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var seconds = Math.Max(0, session.Seconds);
        var end = session.EndUtc == default ? session.StartUtc : session.EndUtc;
        var start = session.StartUtc == default ? end : session.StartUtc;
        var endText = ToDb(end);

        Write((c, tx) =>
        {
            if (Scalar(c, tx, "SELECT 1 FROM games WHERE id = @id;", ("@id", session.GameId)) == null)
            {
                Log.Warn($"Play session for unknown game id {session.GameId} dropped ({seconds} s)");
                return (0, false);
            }
            var id = Convert.ToInt64(Scalar(c, tx,
                "INSERT INTO sessions (game_id, emulator_id, start_utc, end_utc, seconds, exit_code) " +
                "VALUES (@g, @e, @s, @end, @sec, @x) RETURNING id;",
                ("@g", session.GameId), ("@e", session.EmulatorId), ("@s", ToDb(start)), ("@end", endText),
                ("@sec", seconds), ("@x", session.ExitCode)), CultureInfo.InvariantCulture);
            Exec(c, tx,
                "UPDATE games SET play_seconds = play_seconds + @sec, play_count = play_count + 1, " +
                "last_played_utc = CASE WHEN last_played_utc IS NULL OR last_played_utc < @end THEN @end ELSE last_played_utc END " +
                "WHERE id = @g;",
                ("@sec", seconds), ("@end", endText), ("@g", session.GameId));
            session.Id = id;
            return (id, true);
        });
    }

    public IReadOnlyList<PlaySession> Sessions(long gameId, int limit = 50)
    {
        using var c = Connect();
        using var cmd = Cmd(c, null,
            "SELECT id, game_id, emulator_id, start_utc, end_utc, seconds, exit_code FROM sessions " +
            "WHERE game_id = @g ORDER BY start_utc DESC, id DESC LIMIT @n;");
        cmd.Parameters.AddWithValue("@g", gameId);
        cmd.Parameters.AddWithValue("@n", limit > 0 ? limit : -1);
        var list = new List<PlaySession>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new PlaySession
            {
                Id = r.GetInt64(0),
                GameId = r.GetInt64(1),
                EmulatorId = r.IsDBNull(2) ? null : r.GetString(2),
                StartUtc = FromDb(r.GetString(3)),
                EndUtc = FromDb(r.GetString(4)),
                Seconds = r.GetInt64(5),
                ExitCode = r.IsDBNull(6) ? null : r.GetInt32(6),
            });
        return list;
    }

    // ───────────────────────────── stats ─────────────────────────────

    /// <summary>
    /// Counts (games, systems, favourites, games per system) exclude hidden games; play time totals include every game,
    /// because the time was actually played.
    /// </summary>
    public LibraryStats Stats()
    {
        using var c = Connect();
        using var cmd = Cmd(c, null,
            "SELECT system_id, SUM(hidden = 0), SUM(hidden = 0 AND favorite = 1), SUM(play_seconds) FROM games GROUP BY system_id;");
        var perSystem = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var playPerSystem = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        int games = 0, favorites = 0;
        long total = 0;
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var system = r.GetString(0);
            var visible = r.GetInt32(1);
            var fav = r.GetInt32(2);
            var seconds = r.GetInt64(3);
            games += visible;
            favorites += fav;
            total += seconds;
            if (visible > 0) perSystem[system] = perSystem.GetValueOrDefault(system) + visible;
            if (seconds > 0) playPerSystem[system] = playPerSystem.GetValueOrDefault(system) + seconds;
        }
        return new LibraryStats(games, perSystem.Count, favorites, total, perSystem, playPerSystem);
    }

    // ───────────────────────────── folders ─────────────────────────────

    public IReadOnlyList<LibraryFolder> Folders()
    {
        using var c = Connect();
        using var cmd = Cmd(c, null,
            "SELECT id, path, system_id, recursive, is_builtin, enabled, last_scan_utc FROM folders ORDER BY is_builtin DESC, path COLLATE NOCASE, id;");
        var list = new List<LibraryFolder>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new LibraryFolder
            {
                Id = r.GetInt64(0),
                Path = r.GetString(1),
                SystemId = r.IsDBNull(2) ? null : r.GetString(2),
                Recursive = r.GetInt64(3) != 0,
                IsBuiltIn = r.GetInt64(4) != 0,
                Enabled = r.GetInt64(5) != 0,
                LastScanUtc = r.IsDBNull(6) ? null : FromDb(r.GetString(6)),
            });
        return list;
    }

    /// <summary>
    /// Insert or update a folder: by Id when it exists, otherwise by path (case-insensitive; no duplicate rows).
    /// Sets <c>folder.Id</c>. The built-in flag can only be set through <see cref="EnsureBuiltInFolder"/> and is never removed here.
    /// </summary>
    public long UpsertFolder(LibraryFolder folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        var path = NormalizeFolderPath(folder.Path);
        return Write((c, tx) =>
        {
            long? id = null;
            if (folder.Id > 0) id = Scalar(c, tx, "SELECT id FROM folders WHERE id = @id;", ("@id", folder.Id)) as long?;
            id ??= Scalar(c, tx, "SELECT id FROM folders WHERE path = @p;", ("@p", path)) as long?;
            (string, object?)[] args =
            [
                ("@p", path), ("@s", folder.SystemId), ("@r", folder.Recursive ? 1 : 0), ("@e", folder.Enabled ? 1 : 0),
                ("@t", folder.LastScanUtc is { } scanned ? ToDb(scanned) : null),
            ];
            if (id is { } existing)
            {
                Exec(c, tx, "UPDATE folders SET path = @p, system_id = @s, recursive = @r, enabled = @e, " +
                            "last_scan_utc = COALESCE(@t, last_scan_utc) WHERE id = @id;", [.. args, ("@id", existing)]);
            }
            else
            {
                existing = Convert.ToInt64(Scalar(c, tx,
                    "INSERT INTO folders (path, system_id, recursive, is_builtin, enabled, last_scan_utc) VALUES (@p, @s, @r, 0, @e, @t) RETURNING id;",
                    args), CultureInfo.InvariantCulture);
            }
            folder.Id = existing;
            folder.IsBuiltIn = Convert.ToInt64(Scalar(c, tx, "SELECT is_builtin FROM folders WHERE id = @id;", ("@id", existing))) != 0;
            return (existing, true);
        });
    }

    /// <summary>
    /// Removes a folder entry. Its games stay in the library (folder_id becomes NULL); files are never touched.
    /// The built-in games folder cannot be deleted (logged, ignored).
    /// </summary>
    public void DeleteFolder(long id) =>
        Write((c, tx) =>
        {
            var builtIn = Scalar(c, tx, "SELECT is_builtin FROM folders WHERE id = @id;", ("@id", id));
            if (builtIn == null) return (0, false);
            if (Convert.ToInt64(builtIn) != 0)
            {
                Log.Warn($"Refused to delete the built-in games folder (id {id})");
                return (0, false);
            }
            Exec(c, tx, "UPDATE games SET folder_id = NULL WHERE folder_id = @id;", ("@id", id));
            Exec(c, tx, "DELETE FROM folders WHERE id = @id;", ("@id", id));
            return (0, true);
        });

    /// <summary>
    /// Makes sure the built-in games folder row exists exactly once (IsBuiltIn, recursive, enabled, auto-detect system).
    /// If it exists with a different path, the path is updated; an existing user folder with the same path becomes the built-in one.
    /// Returns its id.
    /// </summary>
    public long EnsureBuiltInFolder(string portableGamesPath)
    {
        var path = NormalizeFolderPath(portableGamesPath);
        long resultId = 0;
        Write((c, tx) =>
        {
            var builtIn = Scalar(c, tx, "SELECT id FROM folders WHERE is_builtin = 1;") as long?;
            var samePath = Scalar(c, tx, "SELECT id FROM folders WHERE path = @p;", ("@p", path)) as long?;
            if (builtIn is { } b)
            {
                resultId = b;
                if (samePath == b)
                {
                    // Same path, maybe different spelling (case/separators): keep the canonical one.
                    var n = Exec(c, tx, "UPDATE folders SET path = @p WHERE id = @id AND path IS NOT @p COLLATE BINARY;", ("@p", path), ("@id", b));
                    return (0, n > 0);
                }
                if (samePath is { } other)
                {
                    // Another (user) row already uses the new path: move its games over and drop the duplicate.
                    Exec(c, tx, "UPDATE games SET folder_id = @b WHERE folder_id = @o;", ("@b", b), ("@o", other));
                    Exec(c, tx, "DELETE FROM folders WHERE id = @o;", ("@o", other));
                }
                Exec(c, tx, "UPDATE folders SET path = @p WHERE id = @id;", ("@p", path), ("@id", b));
                return (0, true);
            }
            if (samePath is { } s)
            {
                Exec(c, tx, "UPDATE folders SET is_builtin = 1, enabled = 1 WHERE id = @id;", ("@id", s));
                resultId = s;
                return (0, true);
            }
            resultId = Convert.ToInt64(Scalar(c, tx,
                "INSERT INTO folders (path, system_id, recursive, is_builtin, enabled) VALUES (@p, NULL, 1, 1, 1) RETURNING id;",
                ("@p", path)), CultureInfo.InvariantCulture);
            return (0, true);
        });
        return resultId;
    }

    // ───────────────────────────── state ─────────────────────────────

    public string? GetState(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        using var c = Connect();
        return Scalar(c, null, "SELECT value FROM state WHERE key = @k;", ("@k", key)) as string;
    }

    /// <summary>Sets a value; null removes the key. Changed is raised only when the stored value actually changes.</summary>
    public void SetState(string key, string? value)
    {
        ArgumentNullException.ThrowIfNull(key);
        Write((c, tx) =>
        {
            var n = value == null
                ? Exec(c, tx, "DELETE FROM state WHERE key = @k;", ("@k", key))
                : Exec(c, tx, "INSERT INTO state (key, value) VALUES (@k, @v) ON CONFLICT(key) DO UPDATE SET value = excluded.value " +
                              "WHERE value IS NOT excluded.value;", ("@k", key), ("@v", value));
            return (0, n > 0);
        });
    }

    // ───────────────────────────── backup / dispose ─────────────────────────────

    /// <summary>
    /// Writes a consistent copy of the database to <paramref name="destinationFile"/> using the SQLite online backup API.
    /// The copy is written to a temp file first and then moved into place, so an existing backup is never half-overwritten.
    /// </summary>
    public void Backup(string destinationFile)
    {
        if (string.IsNullOrWhiteSpace(destinationFile)) throw new ArgumentException("Backup path is empty.", nameof(destinationFile));
        var dest = Path.GetFullPath(destinationFile);
        if (string.Equals(dest, DatabaseFile, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Backup target must differ from the database file.", nameof(destinationFile));
        var dir = Path.GetDirectoryName(dest);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmp = dest + SafeFile.TempSuffix;
        try
        {
            if (File.Exists(tmp)) File.Delete(tmp);
            var destCs = new SqliteConnectionStringBuilder { DataSource = tmp, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false }.ToString();
            lock (_writeLock)
            {
                using var src = Connect();
                using var target = new SqliteConnection(destCs);
                target.Open();
                src.BackupDatabase(target);
                // The copy must be a self-contained single file.
                Exec(target, null, "PRAGMA journal_mode = DELETE;");
            }
            File.Move(tmp, dest, overwrite: true);
            Log.Info($"Library database backed up to '{dest}'");
        }
        catch
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        lock (_writeLock)
        {
            if (_disposed) return;
            try
            {
                using var c = Connect();
                Exec(c, null, "PRAGMA optimize;");
            }
            catch (Exception ex) { Log.Debug($"PRAGMA optimize failed: {ex.Message}"); }
            _disposed = true;
            ClearPool(_connectionString);
            _keepAlive?.Dispose();
            _keepAlive = null;
        }
    }

    private static void ClearPool(string cs)
    {
        if (string.IsNullOrEmpty(cs)) return;
        try
        {
            using var c = new SqliteConnection(cs);
            SqliteConnection.ClearPool(c);
        }
        catch (Exception ex) { Log.Debug($"ClearPool failed: {ex.Message}"); }
    }
}
