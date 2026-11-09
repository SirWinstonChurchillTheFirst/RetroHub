# Review of module "data" (WT=`D:\RetroHub-wt\data`, branch `feat/data`)

I found and fixed six real defects, confirmed a seventh failure path by testing, and added one hardening fix. Fixes are committed as `fba5d71` on top of the implementer's `796e259`. The full test project passes: 58 of 58. No contract files and no packages were changed. Most of the implementer's work held up under testing.

## Confirmed defects and fixes
1. **Startup race in the schema setup** (`Migrations.cs`, `Migrate`, about line 31).
   - **Problem:** `user_version` was read outside the transaction. When two instances open a new database at the same moment, one of them fails with "table folders already exists" and silently runs on the temporary in-memory library.
   - **Evidence:** reproduced with 4 parallel constructors; it failed in 4 of 7 runs.
   - **Fix:** `BEGIN IMMEDIATE` (`BeginTransaction(deferred: false)`) and re-read `user_version` inside the transaction. `GetUserVersion` now takes an optional transaction.
2. **Duplicate rows when only non-ASCII letters differ in case** (`Migrations.cs` V1 games table; `SqliteGameRepository.cs` `GameWriter._byPath` and `GetByPath`).
   - **Problem:** `COLLATE NOCASE` only folds ASCII, so `Pokémon.gba` and `POKÉMON.GBA` became two rows. Windows treats them as the same file, so this breaks the brief's "never create duplicate rows for the same path".
   - **Fix:** new column `games.path_key TEXT NOT NULL UNIQUE` (the path upper-cased with `ToUpperInvariant`), used for all path lookups. `path UNIQUE COLLATE NOCASE` is kept as the brief asks.
   - **Note:** I edited migration V1 in place, which is only safe because the module is not merged yet and no database with the old layout exists.
3. **Duplicate folder rows** (`UpsertFolder`, `EnsureBuiltInFolder`).
   - **Problem:** `E:\Roms` and `E:\Roms\` (or `games/` and `games`) became separate rows.
   - **Fix:** new `NormalizeFolderPath` strips trailing separators. `E:\` stays as it is, and so does a bare `E:` (which means the current folder on drive E).
4. **The `-wal` file could not be preserved** (`Open` / `TryRecover`, about lines 70–135).
   - **Problem:** when the main file is not a SQLite database, SQLite itself deletes the `-wal` next to it when the failed connection closes. So the "copy `-wal` aside" step never had a file to copy. I confirmed this with a direct test: `walExists=False` after the failed open.
   - **Fix:** a byte-level check of the 16-byte SQLite header now runs before SQLite touches the file. A foreign file goes straight to recovery. An empty file is still accepted as a new database.
5. **Possible data loss during recovery** (`TryRecover`).
   - **Problem:** a failed copy of `-wal`/`-journal` was only logged, and the files were then deleted anyway. The main file was also deleted before the side files.
   - **Fix:** if a side file cannot be copied, nothing is deleted and the app uses the in-memory library. Side files are now deleted before the main file.
6. **Inconsistent date handling in `KeepPlayStats`** (about line 504).
   - **Problem:** it compared with `ToUniversalTime()`, which treats an `Unspecified` date as local time. `ToDb` stores the same value as UTC. In this machine's UTC+1 zone, a newer "last played" value was dropped; in negative offsets an older one could lower the stored value.
   - **Fix:** a shared `AsUtc` helper used by both.
7. **Hardening (failure not reproduced):** `SafeFile.PreserveCorrupt` only catches `IOException`, and `TryRecover` runs outside the try block. An `UnauthorizedAccessException` would therefore have escaped from the constructor and crashed the app. The call is now wrapped in a try/catch. Also added: `ClearPool` before the in-memory fallback, so the app does not keep a handle on the unusable file.

The implementer's other claims held up when I tested them:
- skipping bad rows in `UpsertMany`, and committing the rest, works;
- a database damaged inside its pages is preserved and recreated;
- the in-memory fallback is thread-safe;
- a tiny non-SQLite file is detected.

## Regression tests
New file `D:\RetroHub-wt\data\tests\RetroHub.Core.Tests\Data\SqliteGameRepositoryRobustnessTests.cs`, with 11 tests (18 cases including the theory rows). I ran them against the original implementation in a scratch copy:
- Failed on the original code, as expected: Unspecified last-played date, non-ASCII case duplicates, folder trailing separator (plus the `NormalizeFolderPath` theory), side files preserved with identical content, and "-wal cannot be preserved, nothing deleted".
- The parallel-open test passed in that one run because the race is intermittent; it failed in my earlier probe runs.

## Still open
- **`dotnet test` does not run.** It fails with "Testing with VSTest target is no longer supported … on .NET 10 SDK" (I confirmed this). The fix needs a `global.json` or a setting in the test project, which this module does not own. I ran the xUnit v3 runner `tests\RetroHub.Core.Tests\bin\Debug\net10.0\RetroHub.Core.Tests.exe` instead.
- Folder paths are still matched with NOCASE only, so a folder with a non-ASCII case difference could still be stored twice. Folders come from a folder picker, so I left this alone.
- The implementer's known limitations still apply:
  - text search reads every row (`LIKE`, no FTS5);
  - "mueller" does not find "Müller";
  - an existing WAL database on a network drive that cannot switch to DELETE mode was not tested.
- The repository has no user-visible strings. The UI has to build the German message from `PreservedCorruptFile` and `IsTemporary`.

## Commands run (real results)
- `dotnet build "D:/RetroHub-wt/data/src/RetroHub.Core" -nologo -v q` and `dotnet build "D:/RetroHub-wt/data/tests/RetroHub.Core.Tests" -nologo -v q`: 0 warnings, 0 errors.
- `RetroHub.Core.Tests.exe -namespace RetroHub.Core.Tests.Data`: run 5 times after the fixes, all passed. The final count was 58 total, 0 failed.
- Full project `RetroHub.Core.Tests.exe`: 58 total, 58 passed, 0 failed, 0 skipped (3.06 s). The UNC test really ran against `\\localhost\C$`. No temp folders were left behind.

Files changed:
- `D:\RetroHub-wt\data\src\RetroHub.Core\Data\SqliteGameRepository.cs`
- `D:\RetroHub-wt\data\src\RetroHub.Core\Data\Migrations.cs`
- `D:\RetroHub-wt\data\tests\RetroHub.Core.Tests\Data\SqliteGameRepositoryRobustnessTests.cs` (new)

