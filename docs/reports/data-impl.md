# Report: SQLite library database (WT=`D:\RetroHub-wt\data`, branch `feat/data`)

The repository is built and committed as `796e259`. All 40 tests in the test project pass, run three times. Plain `dotnet test` does not work in this project, so I ran the tests through the xUnit v3 test executable instead (details under "Tests run").

## Files created (only my own folders)
- `D:\RetroHub-wt\data\src\RetroHub.Core\Data\SqliteGameRepository.cs`: the repository.
- `D:\RetroHub-wt\data\src\RetroHub.Core\Data\Migrations.cs`: the list of schema steps, keyed on `user_version`.
- `D:\RetroHub-wt\data\src\RetroHub.Core\Data\SearchText.cs`: the text normaliser for search and sorting.
- `D:\RetroHub-wt\data\tests\RetroHub.Core.Tests\Data\SqliteGameRepositoryTests.cs`
- `D:\RetroHub-wt\data\tests\RetroHub.Core.Tests\Data\SearchTextTests.cs`
- `D:\RetroHub-wt\data\tests\RetroHub.Core.Tests\Data\TempDatabase.cs`: test helper.

## Public API (namespace `RetroHub.Core.Data`)
- **`public sealed class SqliteGameRepository : IGameRepository`**, created with `new SqliteGameRepository(string databaseFile)`.
  - It implements every `IGameRepository` member: `Query`, `Get`, `GetByPath`, `Upsert`, `UpsertMany`, `Delete`, `SetFavorite`, `SetHidden`, `AddSession`, `Sessions`, `Stats`, `Folders`, `UpsertFolder`, `DeleteFolder`, `GetState`, `SetState`, `Changed`, `Dispose`.
  - **Extra members other modules may call:**
    - `long EnsureBuiltInFolder(string portableGamesPath)`: creates the built-in folder row once and returns its id. You specified `void`; returning `long` is still compatible when called as a plain statement.
    - `void Backup(string destinationFile)`: SQLite online backup into a temp file, which is then moved into place. Throws if the target is the database file itself.
    - `string DatabaseFile { get; }`
    - `bool IsTemporary { get; }`: true when the app is running on an in-memory fallback database.
    - `string? PreservedCorruptFile { get; }`: set when a broken database was copied aside at startup, so the UI can tell the user.
  - Internal, used only by tests: `JournalMode`, `SchemaVersion`, `IsNetworkPath`, `NormalizePath`, `ToDb`/`FromDb`.
- **`public static class SearchText`**, with `string Normalize(string?)`. It lower-cases, removes diacritics, turns ß into ss and æ/ø/œ/ł similarly, drops apostrophes and treats separators as spaces. The UI can use it for in-memory filtering.

## Behaviour other modules should know
- **Dates** are stored as ISO-8601 UTC text with 100 ns precision, so `FileTimeUtc` comes back exactly to the tick. Unix ms would have cut ticks and made the scanner's size+mtime check see every file as changed. **Enums** are stored as int.
- **Paths are made canonical:** relative paths use `/`, absolute paths use `\`, and `path` is `UNIQUE COLLATE NOCASE`. So `games\nes\a.nes` and `GAMES/nes/A.NES` are the same row.
- **Upsert rules:**
  - It matches by Id when that row exists, otherwise by path, and sets `game.Id`.
  - When `Id == 0` and the path already exists, scan fields are updated, but favourite/hidden (OR-ed), added date and play statistics are kept, and null metadata or overrides do not erase stored values.
  - **Upsert never lowers PlaySeconds, PlayCount or LastPlayedUtc**; `AddSession` owns those. So a stale object from the UI cannot reset playtime.
  - A single `Upsert` throws `SqliteException` on a constraint violation (path taken by another game, unknown `FolderId`). `UpsertMany` logs and skips such rows instead of rolling back the whole batch.
- **Search:** the query is normalised and split into words, and every word must appear (as an escaped `LIKE`) in a `search_norm` column that combines title, sort title, developer and genre. A `title_norm` column with an index exists as you specified. A query made only of separators applies no text filter.
- **Title sort** uses a normalised `sort_key` (from `COALESCE(sort_title, title)`), then `COALESCE(sort_title, title) COLLATE NOCASE`, so "Ägypten" sorts under A. Year sorts ascending with nulls last; LastPlayed is DESC with nulls last; Added and PlayTime are DESC; System sorts by system id, then title.
- **`Stats()`:** game, system and favourite counts leave out hidden games; play-time totals include all games.
- **`AddSession`** for a game id that no longer exists is logged and dropped, with no exception. Negative seconds are clamped to 0.
- **`DeleteFolder`** sets the games' `folder_id` to NULL and keeps the games. Deleting the built-in folder is refused and logged.
- **`Changed`** fires after commit, outside the lock, only when something actually changed (including `SetState`). Listener exceptions are caught and logged.
- **Startup:** a non-SQLite or damaged file (`NOTADB`/`CORRUPT`, or `quick_check` not "ok") is copied aside with `SafeFile.PreserveCorrupt` (plus a copy of any `-wal`) and a new database is created. If the file cannot be used at all (locked, a directory, or the copy failed), nothing is deleted and the repository runs on a temporary in-memory database (`IsTemporary`).

## Contract changes / packages
- No contract files were changed.
- No packages were added; only `Microsoft.Data.Sqlite`, which was already referenced.

## Tests run (real results)
- **`dotnet test` fails before running anything:** "Testing with VSTest target is no longer supported by Microsoft.Testing.Platform on .NET 10 SDK". This needs a setting in the test project or a `global.json`, neither of which I own, so I did not change it.
- Instead I ran `dotnet build "D:/RetroHub-wt/data/tests/RetroHub.Core.Tests" -nologo -v q` (0 errors, 0 warnings), then the xUnit v3 runner `tests\RetroHub.Core.Tests\bin\Debug\net10.0\RetroHub.Core.Tests.exe`.
- **Whole project:** 40 total, 40 passed, 0 failed, 0 skipped (3.9 s).
- **Data namespace (`-namespace RetroHub.Core.Tests.Data`):** run three times, all passed every time. No temp folders were left behind.
- **What is covered:**
  - schema from an empty database, and reopening
  - full field round-trip
  - get by path, case-insensitive
  - no duplicate rows by path, with backslash and case variants
  - merge that keeps user data
  - update by id that does not lower play stats
  - delete that cascades sessions and leaves the file on disk
  - filters, all six sorts, and text search ("pokemon" and "POKÉMON" find "Pokémon", Ä/ä, developer, genre, `%` escaping)
  - favourite/hidden plus `Changed` counts, and a listener that throws
  - session totals and ordering
  - stats
  - folders and the built-in folder
  - state
  - concurrency: 8 writer threads and 4 reader threads, no errors, no duplicates
  - corrupt-file recovery: the preserved copy is byte-identical
  - fallback when the path is a directory
  - backup
  - dispose releases the file handles
  - UNC path test against `\\localhost\C$\...`: really ran (not skipped) and got journal mode `delete`; it skips by itself on machines without that share
- **10k performance:** the whole test (10k `UpsertMany` insert, a 10k re-scan merge, and two queries) took **1.33 s**. Each of the two batches is asserted to take under 3 s.

## Known limitations
- Text search uses `LIKE '%word%'`, which reads every row (no FTS5). That is fine for about 10k games; FTS5 could be added later as a new migration.
- "mueller" does not find "Müller"; "muller" does.
- On a network drive, an existing database left in WAL mode may fail to switch to DELETE. If that happens, the app falls back to the in-memory database and logs it; this case was not tested.
- If a future database has a higher schema version than this code knows, it is opened without migrating and a warning is logged.

