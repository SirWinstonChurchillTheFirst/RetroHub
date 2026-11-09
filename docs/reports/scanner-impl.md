# RetroHub scanner module: final report (worktree D:\RetroHub-wt\scanner, branch feat/scanner, commit 54012de)

The scanner, all 15 header sniffers, the title parser, CRC32 and the folder watcher are built and tested. All 190 tests pass, including a scan of the real homebrew ROMs. The required command `dotnet test "<WT>/tests/RetroHub.Core.Tests" -nologo` does not run at all in this worktree. That is a test-project configuration problem, not a failing test (details under Tests).

## Files created
Source, all under `D:\RetroHub-wt\scanner\src\RetroHub.Core\Library\`:
- `LibraryScanner.cs`
- `FolderWatcher.cs`
- `TitleParser.cs`
- `Crc32.cs`
- `PlaylistParser.cs`
- `PcGameDetector.cs`
- `ScanRules.cs`
- `Sniffers\RomSniffers.cs`
- `Sniffers\CartridgeSniffers.cs`
- `Sniffers\DiscSniffers.cs`
- `Sniffers\IsoImage.cs`
- `Sniffers\SnifferUtil.cs`

Tests, all under `D:\RetroHub-wt\scanner\tests\RetroHub.Core.Tests\Library\`:
- `Fakes.cs` (FakeCatalog, InMemoryGameRepository, SyncProgress, TempRoot)
- `RomImages.cs` (builds the test ROM and disc images)
- `SnifferTests.cs`
- `TitleAndCrcTests.cs`
- `LibraryScannerTests.cs`
- `FolderWatcherTests.cs`
- `RealRomScanTests.cs`

## Public API (namespace RetroHub.Core.Library)

**`LibraryScanner : ILibraryScanner`**
- Constructor: `LibraryScanner(ICatalog catalog, IGameRepository repo, AppPaths paths, IEnumerable<IRomSniffer>? sniffers = null)`. The default sniffer list is `RomSniffers.All`.
- `Task<ScanReport> ScanAsync(ScanOptions, IProgress<ScanProgress>?, CancellationToken)`
- `string? DetectSystem(string fullPath, string? folderHint = null)`. The hint can be a system id or a folder name. It also works on a directory, for PC game folders. It never throws.
- `bool IsScanning`
- `LibraryFolder EnsureBuiltInFolder()`
- `IReadOnlyList<IRomSniffer> Sniffers`
- Constants: `MaxHashBytes` (64 MB) and `BuiltInFolderPath` ("games").

**What happens when a scan is already running** (also documented in the code):
- If the running scan is still listing files and its options cover the new request, the second caller gets the same result.
- Otherwise, if a queued scan covers the request, the caller shares that one.
- Otherwise a new scan is queued and runs after the current one.
- Cancelling throws `OperationCanceledException`. Work done so far is saved, and nothing is marked Missing.
- Any other failure is returned in `ScanReport.Errors`, never thrown.

**Progress phases:** `ScanPhases.Preparing`, `Enumerating`, `Checking`, `Finishing`. The phase strings are German and meant to be shown as-is.

**`FolderWatcher : IDisposable`**
- Constructor: `FolderWatcher(AppPaths paths, TimeSpan? debounce = null /*2 s*/, TimeSpan? retryInterval = null /*10 s*/)`.
- `void SetFolders(IEnumerable<LibraryFolder>)` replaces the watched set. Disabled folders are ignored.
- `event Action<IReadOnlyList<long>>? FoldersChanged` fires on a thread-pool thread.
- `IReadOnlyList<long> ActiveFolderIds`

**Sniffers (namespace `RetroHub.Core.Library.Sniffers`):**
- `RomSniffers.All` and `RomSniffers.ById(id)`.
- Ids: `nes`, `snes`, `n64`, `gb`, `gbc`, `gba`, `nds`, `3ds`, `megadrive`, `sms`, `iso9660-ps1`, `iso9660-ps2`, `iso9660-psp`, `nintendo-disc-gc`, `nintendo-disc-wii`.
- The system id a sniffer reports is only a hint, e.g. `psx`, `ps2`, `psp`, `gc`, `wii`, `gamecube`… wait — `gamegear` for Game Gear carts. When a sniffer matches, the scanner assigns the catalog system that lists that sniffer. So the catalog's own ids do not have to match these hints.

**Helpers other modules can use:**
- `TitleParser.Parse(name, isFileName = true)` returns `ParsedTitle(Title, SortTitle, Region)`. Also `SortTitleOf`, `IsMeaningless`, `CleanHeaderTitle`.
- `Crc32`: `Compute(span)`, `Compute(stream, ct)`, `ComputeFile(path, ct)`, `Append`, `ToHex`.
- `PlaylistParser.References(path)` and `PlaylistParser.Expand(path)`.
- `PcGameDetector.FindMainExecutable(dir)` and `IsExcludedExe(name)`.
- `ScanRules`: `IsIgnorableExtension`, `IsTempFile`, `IsSkippedFolder`, `IsPlaylistExtension`, `IsDiscImageExtension`.

## Contract changes and packages
None. No contract file was touched and no NuGet package was added.

## Tests run
- **Builds:** `dotnet build` of `src/RetroHub.Core` and `tests/RetroHub.Core.Tests` both succeed with 0 errors and 0 warnings.
- **`dotnet test` fails before running anything**, with: *"Testing with VSTest target is no longer supported by Microsoft.Testing.Platform on .NET 10 SDK…"*. This comes from the test project setup (xunit.v3 4.0.2 with VSTest on SDK 10.0.301), not from my files, and it will affect every module. It needs a `global.json` opting in to Microsoft.Testing.Platform, or a csproj change. Both files are outside my ownership, so I left them alone.
- **How I ran the tests instead:** I ran the built xUnit v3 runner directly: `tests\RetroHub.Core.Tests\bin\Debug\net10.0\RetroHub.Core.Tests.exe`.
  - Filtered to `-namespace RetroHub.Core.Tests.Library`: 188 tests, 0 failures, three runs in a row.
  - Whole test project (190 tests, all of them mine): 190 passed, 0 failed, 0 skipped, about 6.5 s.
- **What the tests cover:**
  - Folder detection, including alias folders (genesis, gamecube, n3ds) and nested subfolders.
  - External folders, stored with absolute paths, and folders with a fixed system.
  - `.iso` and `.rvz` files resolved by sniffers: PS2 (SYSTEM.CNF BOOT2), PS1, PSP, GameCube and Wii, while a plain data ISO is reported as Unknown.
  - Unknown files reported, ignorable files skipped.
  - Skipped folders (special names, Hidden attribute, dot-folders). A real junction loop created with `mklink /J` is not followed.
  - cue/bin, m3u→cue→bin and gdi: referenced files are hidden. A missing track makes the game Broken, and it goes back to Ok when the track returns.
  - PC: main-exe choice, deeper exe, .url/.lnk shortcuts, and keeping the game id when an update renames the exe.
  - Incremental scans, Full scans, rename and move keeping the id/playtime/favourite, and the name+size fallback when hashing is off.
  - Duplicates, Missing marking and restore, and a missing external drive.
  - Cancellation at a fixed point (results saved in batches, then resumed incrementally), a token already cancelled before the call, progress throttling, and running off the caller's thread.
  - Concurrent calls sharing or queueing scans, and repository failures reported instead of thrown.
  - `DetectSystem`.
  - Every sniffer recognises a valid image and rejects random bytes, a 12-byte file, an empty file, a zero-filled file and a missing file.
  - Title parsing cases and CRC check values.
  - Watcher debounce, temp/ignorable files, finished download renames, disabled folders, a folder that appears later, and dispose.
- **CRC32 speed:** 369 MB/s measured on a Debug build. The test only fails below 150 MB/s.
- **Real homebrew ROMs:** I copied `D:\RetroHub\games` to a temp folder (the originals were not touched) and scanned it with a fake catalog containing nes/gb/gbc/gba. Result: 4 added, 0 unknown, 0 errors, 50 ms.

| System | File | Title | Size (bytes) | CRC32 | Status |
|---|---|---|---|---|---|
| gb | 240p Test Suite (Homebrew).gb | 240p Test Suite | 32768 | 781E2947 | Ok |
| gba | 240p Test Suite (Homebrew).gba | 240p Test Suite | 61104 | 87C789D9 | Ok |
| gbc | uCity (Homebrew).gbc | uCity | 131072 | E044D080 | Ok |
| nes | 240p Test Suite (Homebrew).nes | 240p Test Suite | 65552 | 72FD0452 | Ok |

  Each detection was cross-checked against its header sniffer, and a second scan reported all 4 as unchanged.

## Known limitations
- **CRC32 is of the whole file.** iNES and SNES copier headers are not stripped, so CRCs of headered dumps won't match No-Intro databases. Disc images, playlists and PC games are never hashed. `.bin` is hashed because it is also a Mega Drive cartridge extension.
- **Not detected without a system folder:** `.chd`, `.cso`, `.pbp`, `.cia`, `.zip`/`.7z` contents, Saturn, Sega CD and Dreamcast discs. If their extension is shared between systems, they are reported as Unknown.
- **PC games** are only recognised inside a folder for a folder-games system (e.g. `pc`). A stray `.exe` anywhere else is reported as Unknown on purpose.
- **A game already in the database whose file later becomes a hidden track** (say a `.cue` is added next to an existing `.bin`) is left untouched: not marked Missing, not removed.
- **Sniffers mark a file Broken only for truncated dumps** (NES, GB, NDS, N64, a 3DS partition beyond the end of the file). Bad checksums do not count. Encrypted 3DS images are not flagged.
- **Not tested:** watcher recovery after a buffer overflow or a watcher error. The code re-creates the watcher and reports the folder as changed, but no test covers it.
- **Timestamps** are compared within 1 second.
- **Rename detection** only matches games whose old file is missing at scan time.

I found and fixed 10 real defects in the scanner module; one was found while fixing another. All 209 tests pass, and the fixes are committed as `489aea4` on feat/scanner. The implementer's sniffer offsets and magic numbers all check out; the defects are in the scanner's handling of changes during a scan, scan scheduling, CRC handling, symlinks and title cleanup.

## Defects (line numbers refer to the reviewed commit 54012de)
1. **Playtime, favourites and edits lost during a scan** – `LibraryScanner.cs:586-590, 826`. The scan saved whole game records from the copy it read at the start, so anything changed in the database while it ran was overwritten. A game removed during the scan was written again. In the test fake, that also made the whole batch fail. **Fix:** before saving, each known game is re-read and only the scanner's own fields are copied onto it; removed games are skipped.
2. **Folder settings reverted, removed folders brought back** – `LibraryScanner.cs:303-315`. Setting LastScanUtc saved the folder object read at scan start. **Fix:** the folders are read again, only LastScanUtc is set, and removed folders are left alone.
3. **New file missed when a scan is requested during a running scan** – `LibraryScanner.cs:104, 269`. A second request could join a running scan until it finished listing files, even though it might already have passed that folder. **Fix:** requests only join until the running scan reads its folder list; later ones are queued. The doc comment is updated.
4. **Full scans did not re-hash files** (found while fixing 6) – `LibraryScanner.cs:1084`. The CRC cache, keyed by size and modification time, answered even on a Full scan. **Fix:** Full scans skip the cache.
5. **False duplicate reports** – `LibraryScanner.cs:587`. When a known file changed, the lookup tables for duplicate and rename detection kept its old CRC. A copy of the old content was then reported as a duplicate of the changed file. **Fix:** the tables are updated along with the game.
6. **CRC wiped** – `LibraryScanner.cs:768`. A Full scan with hashing off set every stored CRC to null. **Fix:** the CRC is kept while size and modification time are unchanged.
7. **Broken stuck for good** – `LibraryScanner.cs:688`. A file that could not be read once (for example locked by another program) stayed Broken, because its size and time never change. **Fix:** Broken games are checked again on every scan. Known games now count as Updated, and are saved, only when their scanned data actually changed. That also stops Full scans from rewriting every game.
8. **Symlinked ROMs marked Broken** – `LibraryScanner.cs:1057`, `Sniffers/SnifferUtil.cs:73`, `PcGameDetector.cs:97`. Windows reports a symlink's own size, which is 0. **Fix:** new helper `ScanRules.FollowLink`, used for sizes, times and the truncation checks.
9. **Titles lost dots** – `TitleParser.cs:119`. "Super Mario Bros." became "Super Mario Bros" and ".hack" became "hack". **Fix:** dots are no longer trimmed.
10. **Watcher recovery was untested.** I made `FolderWatcher.OnError` internal and added two tests: a simulated buffer overflow, and a watched folder deleted then recreated. Both pass; the code itself was fine.

Two existing tests expected a Full scan to count every unchanged file as Updated. I changed them to the new behaviour, and one now also checks that a Full scan catches a content change with the same size and time. New regression tests are in `D:\RetroHub-wt\scanner\tests\RetroHub.Core.Tests\Library\ReviewRegressionTests.cs`. Each one failed before its fix: I ran the first probes and saw 9 failures, then the scheduling test separately.

No contract files or NuGet packages were changed.

## Still missing or limited
- **Test command:** `dotnet test "<WT>/tests/RetroHub.Core.Tests" -nologo` still fails before running anything, with "Testing with VSTest target is no longer supported…". The cause is the test project setup, which neither of us owns. Adding `-p:IsTestingPlatformApplication=false` makes it work. The proper fix is a `global.json` or csproj change by whoever owns those files.
- **Old disc games:** if `.cue` games are already in the library and an `.m3u` is added later, the old entries stay visible. They are neither hidden nor marked Missing.
- **Encrypted 3DS images** are not flagged.
- **The real `SqliteGameRepository` is not in this worktree.** All repository behaviour, including fix 1, is only tested against the in-memory fake.
- The other limitations the implementer listed are unchanged.

## Commands run
- `dotnet build src/RetroHub.Core` and `dotnet build tests/RetroHub.Core.Tests` (`-nologo -v q`): 0 errors, 0 warnings.
- `dotnet test tests/RetroHub.Core.Tests -nologo`: fails with the error above.
- `dotnet test tests/RetroHub.Core.Tests -nologo -p:IsTestingPlatformApplication=false`: 209 passed, 0 failed, 0 skipped.
- `tests\RetroHub.Core.Tests\bin\Debug\net10.0\RetroHub.Core.Tests.exe` (whole project): 209 passed, 0 failed. With `-namespace RetroHub.Core.Tests.Library`: 209 passed, three runs in a row.
- **Real homebrew scan** (copies of `D:\RetroHub\games`; the originals were only read): 4 added, 0 unknown, 0 errors, 58 ms. Results match the implementer's table:

| System | File | CRC32 | Status |
|---|---|---|---|
| gb | 240p Test Suite (Homebrew).gb | 781E2947 | Ok |
| gba | 240p Test Suite (Homebrew).gba | 87C789D9 | Ok |
| gbc | uCity (Homebrew).gbc | E044D080 | Ok |
| nes | 240p Test Suite (Homebrew).nes | 72FD0452 | Ok |

  A second scan reported all 4 as unchanged.

Changed files are all under `D:\RetroHub-wt\scanner\src\RetroHub.Core\Library\` and `D:\RetroHub-wt\scanner\tests\RetroHub.Core.Tests\Library\`.

