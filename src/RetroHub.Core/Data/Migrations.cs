using Microsoft.Data.Sqlite;

namespace RetroHub.Core.Data;

/// <summary>One schema step. Applied inside a transaction; PRAGMA user_version is set to <see cref="Version"/> in the same transaction.</summary>
internal sealed record Migration(int Version, string Description, Action<SqliteConnection, SqliteTransaction> Apply);

/// <summary>
/// Ordered schema migrations keyed on PRAGMA user_version. Never edit a released migration — append a new one.
/// Note: PRAGMA foreign_keys cannot be changed inside a transaction; a future migration that rebuilds a table
/// referenced by a foreign key must use the "create new table, copy, drop, rename" pattern without relying on cascades.
/// </summary>
internal static class Migrations
{
    public static readonly IReadOnlyList<Migration> All =
    [
        new(1, "initial schema (games, folders, sessions, state)", (c, tx) => Exec(c, tx, V1)),
    ];

    public static int Latest => All[^1].Version;

    /// <summary>Applies all pending migrations. Returns the resulting schema version.</summary>
    public static int Migrate(SqliteConnection c)
    {
        var current = GetUserVersion(c);
        if (current > Latest)
        {
            Logging.Log.Warn($"Database schema version {current} is newer than this RetroHub version knows ({Latest}); opening without migration.");
            return current;
        }
        foreach (var m in All)
        {
            if (m.Version <= current) continue;
            using var tx = c.BeginTransaction();
            m.Apply(c, tx);
            Exec(c, tx, $"PRAGMA user_version = {m.Version};");
            tx.Commit();
            current = m.Version;
            Logging.Log.Info($"Database migrated to schema {m.Version}: {m.Description}");
        }
        return current;
    }

    public static int GetUserVersion(SqliteConnection c)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static void Exec(SqliteConnection c, SqliteTransaction tx, string sql)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    // Dates are ISO-8601 UTC text with 100 ns precision ("yyyy-MM-ddTHH:mm:ss.fffffffZ"): fixed width, so text
    // comparison/ORDER BY/MAX are chronological, and file times round-trip exactly (scanner change detection).
    // Enums are stored as INTEGER. Booleans as 0/1.
    private const string V1 = """
        CREATE TABLE folders (
            id             INTEGER PRIMARY KEY,
            path           TEXT    NOT NULL UNIQUE COLLATE NOCASE,
            system_id      TEXT,
            recursive      INTEGER NOT NULL DEFAULT 1,
            is_builtin     INTEGER NOT NULL DEFAULT 0,
            enabled        INTEGER NOT NULL DEFAULT 1,
            last_scan_utc  TEXT
        );
        CREATE UNIQUE INDEX ux_folders_builtin ON folders(is_builtin) WHERE is_builtin = 1;

        CREATE TABLE games (
            id                    INTEGER PRIMARY KEY,
            system_id             TEXT    NOT NULL,
            path                  TEXT    NOT NULL UNIQUE COLLATE NOCASE,
            title                 TEXT    NOT NULL,
            title_norm            TEXT    NOT NULL DEFAULT '',
            sort_title            TEXT,
            sort_key              TEXT    NOT NULL DEFAULT '',
            search_norm           TEXT    NOT NULL DEFAULT '',
            kind                  INTEGER NOT NULL DEFAULT 0,
            status                INTEGER NOT NULL DEFAULT 0,
            size                  INTEGER NOT NULL DEFAULT 0,
            file_time_utc         TEXT    NOT NULL,
            crc32                 TEXT,
            serial                TEXT,
            region                TEXT,
            added_utc             TEXT    NOT NULL,
            last_played_utc       TEXT,
            play_seconds          INTEGER NOT NULL DEFAULT 0,
            play_count            INTEGER NOT NULL DEFAULT 0,
            favorite              INTEGER NOT NULL DEFAULT 0,
            hidden                INTEGER NOT NULL DEFAULT 0,
            emulator_override     TEXT,
            launch_mode_override  INTEGER,
            cover_path            TEXT,
            background_path       TEXT,
            logo_path             TEXT,
            developer             TEXT,
            publisher             TEXT,
            genre                 TEXT,
            year                  INTEGER,
            players               INTEGER,
            description           TEXT,
            extra_args            TEXT,
            folder_id             INTEGER REFERENCES folders(id) ON DELETE SET NULL
        );
        CREATE INDEX ix_games_system      ON games(system_id);
        CREATE INDEX ix_games_favorite    ON games(favorite);
        CREATE INDEX ix_games_last_played ON games(last_played_utc);
        CREATE INDEX ix_games_added       ON games(added_utc);
        CREATE INDEX ix_games_title_norm  ON games(title_norm);
        CREATE INDEX ix_games_sort_key    ON games(sort_key);
        CREATE INDEX ix_games_crc_size    ON games(crc32, size);
        CREATE INDEX ix_games_folder      ON games(folder_id);

        CREATE TABLE sessions (
            id           INTEGER PRIMARY KEY,
            game_id      INTEGER NOT NULL REFERENCES games(id) ON DELETE CASCADE,
            emulator_id  TEXT,
            start_utc    TEXT    NOT NULL,
            end_utc      TEXT    NOT NULL,
            seconds      INTEGER NOT NULL DEFAULT 0,
            exit_code    INTEGER
        );
        CREATE INDEX ix_sessions_game ON sessions(game_id, start_utc);

        CREATE TABLE state (
            key    TEXT PRIMARY KEY,
            value  TEXT NOT NULL
        );
        """;
}
