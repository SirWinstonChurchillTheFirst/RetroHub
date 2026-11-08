# library-import

## Summary
MonHub has no game database. The library is rebuilt from the file system on every refresh. GameLibrary.Load() (GameLibrary.cs:96-134) recursively lists two folders, "Randomisierte ROMs" (runs) and "ROMs", keeping files whose extension is in HubPaths.RomExtensions. For each ROM it builds an immutable GameEntry (GameLibrary.cs:15-82) holding the path, a GameSystem enum (DS/GBA/GB/N3DS/Other, worked out from the extension in SystemOf, lines 528-535), the file time, the game code and title read from the ROM header, and up to four SaveFile records, matched by base file name against each emulator's save folder. Settings that do persist (favourites, fangames, theme and so on) live in one JSON file, System\Einstellungen\MonHub.json (class HubConfig, written with System.Text.Json through SafeFile). Favourites are stored by bare ROM file name in a List<string>.

ROM detection reads headers by extension: RandoApp.RomInfo (Shared/RomInfo.cs) handles NDS, GBA and GB/GBC, and Rom3ds.Read (Azahar.cs:8-52) parses 3DS NCSD/NCCH headers. Neither checks magic bytes or checksums. Header results are kept in static dictionaries keyed by (path, mtime).

Duplicates are found by name only. There is no content hashing anywhere (the only SHA1 calls hash folder or instance names). The library drops a second ROM with the same base name (names.Add(name), GameLibrary.cs:110). The importer treats the same name plus the same byte length as one file (Importer.cs:314) and marks a target as "already in MonHub" by name and size (Importer.cs:316-320).

SafeFile (Shared/SafeFile.cs) writes atomically: it writes to a unique "<path>.<guid>.monhub-tmp" file, then calls File.Move(tmp, path, overwrite:true). It does not flush to disk. The importer copies to "<target>.monhub-tmp", restores the source's last-write time, then moves the file without overwriting (Importer.cs:425-465).

Cover and icon art comes from two places, and nothing is downloaded. GameLook (GameLook.cs) maps the first 3 letters of the game code, or keywords in the GB header title, to a bundled animated Pokémon sprite key plus an accent colour. IconHelper (IconHelper.cs) pulls a program's own icon from its exe with PrivateExtractIcons / ExtractAssociatedIcon; this is used for fangame tiles and emulator chips.

Lists are Avalonia ListBoxes (they virtualise by default because their panel is a VirtualizingStackPanel). There is no explicit virtualisation or async image loading. The fangame grid is an ItemsControl with a WrapPanel, which does not virtualise, and it builds icons synchronously in the view model's constructor. Pages load data with GameData.LoadAsync() (GameData.cs), which runs on Task.Run and shares one load between pages while it is in flight. Each page skips its rebuild when GameData.Signature(), a string over ROM path, ROM mtime and newest-save path/mtime plus the current minute, has not changed. A load-id counter throws away stale results.

There is no launcher-side playtime tracking. "Last played" is the last-write time of the newest save file (GameEntry.NewestSave). Play time is read out of the Pokémon save itself by SaveInspector (SaveSummary.Hours/Minutes). Search splits the text into words, and every word must appear in the file name or the version label (case-insensitive). Filters are radio-button chips (All, Favourites, With save, Runs, Originals). Favourites are sorted first with a stable OrderByDescending.

The import pipeline finds emulators on the PC in two ways: it reads registry caches of programs Windows has seen started (MuiCache, Compatibility Assistant Store), and it walks common folders with a depth limit. It then reads each emulator's INI/TOML settings to find its ROM and save folders, scans those folders up to 3 levels deep, and copies ROMs and saves into MonHub without overwriting anything. A .dsv save is converted to a raw .sav when the save goes to melonDS. A raw 512 KB .sav is converted to .dsv when DeSmuME is the main emulator.

## Reusable
- **SafeFile atomic writer** (Shared/SafeFile.cs:11-41): Reuse almost as-is (namespace RandoApp -> RetroHub; temp suffix '.retrohub-tmp'). Improve it: write through a FileStream and call Flush(flushToDisk:true) before the move. Optionally use File.Replace when the target exists, to keep a .bak. At startup, sweep leftover '*.retrohub-tmp' files from a killed process. Use it for library.json/db exports, config, playtime records and favourites.
- **RecycleBin.Delete (SHFileOperation P/Invoke)** (GameLibrary.cs:642-680): Reuse as-is for 'remove game / delete saves'. Keep the struct and flags: FO_DELETE=3, FOF_ALLOWUNDO|FOF_NOCONFIRMATION|FOF_SILENT|FOF_NOERRORUI|FOF_WANTNUKEWARNING; pFrom must be a double-null-terminated list. It reports success by checking that the files are gone afterwards. Os.Trash is the Linux branch; drop it if RetroHub is Windows-only.
- **Header-cache pattern keyed by file version** (GameLibrary.cs:436-449 (HeaderCache), Azahar.cs:15-50 (Rom3ds cache), IconHelper.cs:20-33): Take the idea: a dictionary keyed by (path, mtime), or better (path, size, mtime), behind a lock. For RetroHub, persist it in the library DB (path, size, mtime, systemId, header fields, crc32/sha1) so the next start skips unchanged files. In-memory caches are fine for icons.
- **ROM header parsing for NDS/GBA/GB(C)** (Shared/RomInfo.cs:42-78): Adapt it into a pluggable IRomSniffer per system. Offsets: NDS title @0x00 len 12, game code @0x0C len 4, unit code @0x12, ROM version @0x1E. GBA title @0xA0 len 12, code @0xAC len 4. GB title @0x134 len 11. It reads only the first 0x150 bytes. Add magic checks: GB Nintendo logo @0x104, GBA fixed value 0x96 @0xB2, iNES 'NES\x1A', Genesis 'SEGA' @0x100, N64 byte-order magic 0x80371240, ISO9660 'CD001' @0x8001, and so on. The current code decides by extension alone.
- **3DS NCSD/NCCH header reader** (Azahar.cs:8-52 (Rom3ds.Read)): Reuse as-is for 3DS. NCSD magic @0x100; partition 0 offset = u32 @0x120 * 0x200; in the NCCH header, product code @0x150 (16 ASCII), title ID u64 @0x118, NoCrypto = flags byte @0x18F & 0x04; encrypted when that bit is clear.
- **DS encrypted secure area check** (GameLibrary.cs:683-709 (RomCheck.HasEncryptedSecureArea)): Reuse as-is if RetroHub supports DS through melonDS (it decides whether a BIOS or another core is needed). Logic: ARM9 rom offset u32 @0x20 == 0x4000, then read 8 bytes @0x4000: E7FFDEFFE7FFDEFF, 0 or 'encryObj' means decrypted.
- **Emulator discovery on the PC** (Importer.cs:56-176 (Kinds table, FindEmulators, ExesWindowsRemembers, FindExes)): Adapt for RetroHub's 'import existing emulators' feature. Turn EmulatorKind(Label, Platform, ExePrefixes, ConfigFiles, AppDataDirs) into data-driven definitions (RetroArch, Dolphin, PCSX2, DuckStation, PPSSPP, Ryujinx, Cemu...). Keep the registry trick: HKCU MuiCache and 'AppCompatFlags\Compatibility Assistant\Store' value names, cut at '.exe', skipping %TEMP% (exes run straight from a zip). Keep the bounded depth-first walk with the per-folder depth memo (Dictionary<string,int> searched) and the CancellationToken checks.
- **Safe folder enumeration helpers** (Importer.cs:53-54 (SkipDirs), 248-271 (ToFolder), 469-525 (IsSameOrInside, IsSkipped, IsBroadFolder, SafeDirs skipping ReparsePoint|System), GameLibrary.cs:376-390 (EnumerationOptions IgnoreInaccessible)): Reuse as-is for the ROM-folder scanner. ToFolder rejects UNC/network drives (a sleeping NAS makes the scan hang), whole drives, Temp and relative paths that are not '.\'. Skipping reparse points avoids junction loops.
- **Downloads KnownFolder P/Invoke** (Importer.cs:497-506): Reuse as-is: [DllImport("shell32.dll", ExactSpelling = true)] static extern int SHGetKnownFolderPath(ref Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr ppszPath); with FOLDERID_Downloads 374DE290-123F-4565-9164-39C4925E467B, then Marshal.FreeCoTaskMem.
- **Copy pipeline (never overwrite, temp then move, keep mtime, report progress)** (Importer.cs:425-465 (Import), Result record line 423): Adapt: in RetroHub, make copying into the portable Roms folder optional; referencing ROMs where they are should be the default, like EmulationStation and Playnite. Keep the 'copy to tmp -> SetLastWriteTime -> Move without overwrite' step and the Result(Copied, Skipped, Problems) reporting. Use a unique tmp name (see pitfalls).
- **Reading folders out of emulator INI/TOML configs** (Importer.cs:211-245 (FolderKey/SaveKey regex, ReadConfigFolders handling multi-line TOML lists), GameLibrary.cs:346-367 (RecentRoms), 410-434 (Setting(section,key))): Reuse the Setting() INI/TOML section reader as-is for emulator config edits. Take the idea of key-regex folder harvesting for importing an existing setup.
- **Import dialog UX** (ImportWindow.axaml + .cs): Adapt the layout: 'From where?' checklist of found sources plus an 'Add folder' button; a 'Found' table with checkbox, type, file, size and status columns; summary counts; a primary button labelled 'Import N'; progress bar with the current file. Rescans are cancelled with a CTS. Closing is blocked while a copy runs. A new list instance is assigned to ItemsSource each time (line 55) because the control only notices a different list.
- **Shared load + change-signature + stale-result guard** (GameData.cs:18-38; LibraryPage.axaml.cs:41-68 (_loadId, _shown); StartPage.axaml.cs:70-92): Adapt the pattern: one in-flight Task shared by every page, a signature to skip identical rebuilds, a load id to drop out-of-order results. Back it with a persistent library and incremental updates (FileSystemWatcher or a rescan diff) instead of a full disk rescan.
- **GameCard view model with accent brushes** (GameViews.cs:28-84): Reuse the idea for platform or cover accent colours. The immutable brushes (ImmutableLinearGradientBrush, ImmutableSolidColorBrush) can be built off the UI thread. Text colour = black when (0.299R+0.587G+0.114B)/255 > 0.62, else white. Leave out the Pokémon parts (Team, TrainerLine, Summary).
- **Exe icon extraction** (IconHelper.cs:11-62): Adapt for PC/native games and emulator chips. P/Invokes: user32 PrivateExtractIcons(string file, int index, int cx, int cy, IntPtr[] icons, uint[] ids, uint count, uint flags); user32 DestroyIcon; shell32 ExtractAssociatedIcon(IntPtr, StringBuilder, out ushort). Move it off the UI thread and return an Avalonia Bitmap. To drop System.Drawing.Common, use IShellItemImageFactory or SHGetFileInfo instead.
- **Library page structure (search, filter chips, favourites first, detail pane, empty state, responsive)** (Pages/LibraryPage.axaml:13-139, LibraryPage.axaml.cs:88-123, 370-379): Adapt. Search: split on spaces, every word must match (Contains, OrdinalIgnoreCase) on name or version. Filter chips: RadioButton Theme=Hub.Chip in one GroupName, labels with counts ('All  12'). Favourites first with a stable OrderByDescending(IsFavorite). The selection is kept by ROM path. Arrange() moves the detail pane below the list when narrower than 760px. For controller/TV mode, swap the ListBox for a virtualising cover grid.
- **Continue / Recently played start page** (Pages/StartPage.axaml:34-182, StartPage.axaml.cs:94-128, GameLibrary.Continue (GameLibrary.cs:140-145)): Take the idea: a hero 'Continue' card for the most recent game and a 'Recently played' list (Take(4)) that launches straight into its emulator. Base it on RetroHub's own LastPlayed field instead of save-file mtimes.
- **Add-program flow (PC games / standalone exes)** (AddFangameWindow.axaml(.cs), Pages/FangamesPage.axaml(.cs), HubConfig.Fangame): Reuse the idea as RetroHub's 'Add PC game/shortcut'. Live icon preview, validation that the file exists and a name is set, name guessed from the folder when the file is 'Game.exe' or 3 characters or shorter, a 'file not found' tile status, a context menu (Start, Open location, Remove from library). Store it in the library DB, not in the config file.
- **Orphan-save tidy-up** (GameLibrary.OrphanSaves (GameLibrary.cs:277-321), OrphanSave class (553-590), OrphanWindow.axaml(.cs)): Take the idea: a per-emulator table of save folders, extension regexes and a savestate flag; saves grouped by game name; multi-select delete to the Recycle Bin. The tables are DS/GBA-specific; drive them from the emulator definitions.
- **Blank-save detection and handling of files held by an emulator** (GameLibrary.cs:476-512 (IsBlank)): Take the idea for 'has this game really been played': a file filled with one byte value (0xFF = erased flash) is no save. Open with FileShare.ReadWrite|Delete. If the emulator holds the file open, compare LastWrite against Creation (within 10 s). Results are not cached while the file is under 10 s old.
- **Relative time text** (GameLibrary.cs:628-639 (TimeText.Ago)): Reuse the logic (just now / N min ago / N h ago / yesterday / date) with RetroHub's localisation.
- **Portable root detection** (HubPaths.cs:13-25): Reuse as-is: the root is two levels above the exe when the exe sits in System\App, otherwise the exe folder (development). Good fit for RetroHub's portable layout.
- **Config load with a backup of a broken file** (HubConfig.cs:80-106): Reuse the pattern: on a deserialise failure, copy the file to '<file>.kaputt' (rename to .corrupt) before starting fresh, so the next Save does not wipe the user's data; write with SafeFile.
- **Bilingual inline string markup extension** (Txt.cs:12-92): Idea only: XAML {local:L 'de','en'} via MarkupExtension.ProvideValue. For RetroHub, prefer key-based resources (resx or JSON) if more than two languages are planned.

## Pitfalls
- No persistent library at all. Every Refresh rescans both ROM folders recursively, opens every ROM header (header cache only in memory) and stats every save. That will not scale to thousands of ROMs or to network/USB drives. RetroHub needs a persistent DB (SQLite or LiteDB, or a SafeFile-written JSON) with incremental scans keyed by path, size and mtime.
- Identity is the bare file name. GameLibrary.Load drops any second ROM with the same base name (GameLibrary.cs:105-110: names.Add(name)), so 'Tetris.gb' and 'Tetris.nes' cannot both appear. Favourites are stored by Path.GetFileName(rom) (HubConfig.cs:110-116), so they collide across folders and are lost on rename. Give games a stable id (a GUID, or a content hash plus path).
- No content hashing. Import dedup is '{f.Name}|{f.Length}' (Importer.cs:314), and 'already in MonHub' means the same name and same size (Importer.cs:319). Use CRC32 or SHA1 (also needed for No-Intro/Redump DAT matching and scraping), computed in the background and cached.
- System detection uses the extension only (SystemOf, GameLibrary.cs:528-535). RomInfo has no magic or checksum checks, so a renamed or corrupt file gets the wrong system.
- Extension lists disagree. HubPaths.RomExtensions and RomInfo.Extensions include .cia, Importer.RomExt does not, and SystemOf maps .cia to Other (RomInfo also lists .cxi but not .cci). Keep one system/extension registry.
- There is no playtime or last-played tracking in the launcher. 'Last played' is the newest save file's mtime, and playtime comes from Pokémon save data (SaveInspector, SaveSummary.PlayTimeSpan). RetroHub must time sessions itself (process start to exit, written with SafeFile) and store LastPlayed, PlayCount and TotalPlaytime per game.
- SafeFile does not flush to disk before the rename (File.WriteAllText then File.Move). After a power loss the new file can be zero-length on some filesystems. Leftover '*.monhub-tmp' files are never swept. Importer uses a fixed tmp name, target + ".monhub-tmp" (Importer.cs:438), not a unique one, so two imports of the same target could collide.
- GameData.Signature includes DateTime.Now to the minute (GameData.cs:37), so every page rebuilds every GameCard and resets ItemsSource at least once a minute just to refresh the 'N min ago' texts. Scroll and focus state can jump. Use observable view models with a timer-updated property instead.
- Search re-filters and assigns a new ItemsSource list on every keystroke, with no debounce (LibraryPage.axaml.cs:115-118). PERFORMANCE.md itself recommends debouncing input that triggers work.
- No explicit virtualisation or lazy image loading. The game list relies on ListBox's default VirtualizingStackPanel. FangamesPage uses ItemsControl + WrapPanel (not virtualised) and calls IconHelper.Get synchronously in the FangameView constructor on the UI thread. A RetroHub cover grid needs a virtualising wrap/grid layout (ItemsRepeater with UniformGridLayout, or a third-party VirtualizingWrapPanel) and async thumbnail decoding (Bitmap.DecodeToWidth) with an LRU cache.
- The static caches grow without limit and are never evicted: GameLibrary.HeaderCache, BlankCache, IconHelper.Cache, Rom3ds.Cache, Sprites.Cache (Sprites.Cache also has no lock).
- Some lazy work runs on the UI thread: GameEntry.Summary (save parsing) and NeedsDeSmuME (a ROM file read) are computed lazily. GameData pre-reads Summary off-thread, but NeedsDeSmuME is first touched in LibraryPage.UpdateChoice on the UI thread.
- IconHelper depends on System.Drawing.Common (Icon.FromHandle, ToBitmap, Save as PNG), which is Windows-only. The ExtractAssociatedIcon fallback gives only 32x32.
- Heavy Pokémon coupling, do not copy: GameLook.ByCode/ByTitle tables (Pokémon game codes mapped to Pokémon sprite keys), SaveInspector/SaveSummary/TeamSlot/TrainerView, StarterTrios, SaveFormat (DeSmuME .dsv footer, 512 KB DS flash size), IsRun/'Randomisierte ROMs', the hardcoded emulator constants (MelonDS/DeSmuME/MGBA/Azahar) and per-emulator SaveFile properties on GameEntry (MelonDSSave, DeSmuMESave...). In RetroHub, model saves and emulators as data (a dictionary per emulator id).
- GameSystem is a closed enum (DS, GBA, GB, N3DS, Other) used in switch statements across files. RetroHub needs a data-driven platform registry (id, name, extensions, header sniffer, default emulator/core).
- UI code uses WPF-compat shims (Compat.cs: Visibility enum extension, MessageBox enums, global using FrameworkElement=Control, BitmapSource=IImage, Brush=IBrush). These are port leftovers; write idiomatic Avalonia/MVVM (IsVisible bindings, commands) instead of copying them.
- Pages are code-behind with x:Name lookups and direct ItemsSource assignment, not MVVM. Controller navigation will need proper focus management (XYFocus or a custom grid navigator). The current ListBox rows plus the star Button inside each row are mouse-oriented.
- Txt.L(de, en) inline strings are spread across every file. Fine for 2 languages, unmaintainable for more. Language is read from the JSON file at type initialisation and fixed per run (switching restarts the app).
- Importer heuristics are tuned to Pokémon DS/GBA: LooksLikeDsSave (256 KB or more and a power of two means DS), and the .sav kind is decided by a ROM with the same name or by the owning emulator's platform. Generalise per platform.
- Registry discovery (MuiCache / Compatibility Assistant Store) only finds exes the user has started, and only those whose file name starts with a known prefix. Keep it as a hint source next to the bounded folder walk, not as the only source.

## Key details
STORAGE
- No DB. The library is rebuilt from disk on every refresh (GameLibrary.Load, GameLibrary.cs:96-134). Settings: System\Einstellungen\MonHub.json, written through System.Text.Json and SafeFile (HubConfig.cs:76-106).
- Favourites: `public List<string> Favorites` (ROM file names, no folder). `IsFavorite(rom) => Favorites.Contains(Path.GetFileName(rom), OrdinalIgnoreCase)`. `ToggleFavorite` saves immediately.
- Fangames: `class Fangame { string Name; string Path; }` stored in HubConfig.Fangames.
- Folder layout (HubPaths.cs): Root\ROMs, Root\Randomisierte ROMs, Root\Spielstände\{melonDS,DeSmuME,mGBA,Azahar,GBA und GB}, Root\Fangames, Root\System\{App,Emulatoren,Einstellungen,BIOS}. Root = AppDir.Parent.Parent when the parent folder is named "System".
- `RomExtensions = [".nds", ".gba", ".gbc", ".gb", ".3ds", ".cci", ".cxi", ".cia"]`.

MODEL
`public class GameEntry { required string Rom; required bool IsRun; required GameSystem System; required DateTime RomTime; SaveFile? MelonDSSave/DeSmuMESave/MGBASave/AzaharSave; ulong TitleId; string GameCode; string HeaderTitle; Name => GetFileNameWithoutExtension(Rom); NewestSave => new[]{...}.Where(s=>s!=null).MaxBy(s=>s.Time); }`
`public record SaveFile(string Path, string Emulator, DateTime Time);`
Sort order (Load): `OrderByDescending(NewestSave?.Time).ThenByDescending(IsRun ? RomTime : Min).ThenBy(Name, OrdinalIgnoreCase)`.

HEADER OFFSETS
- RomInfo (Shared/RomInfo.cs:42-75) reads the first 0x150 bytes. NDS: title 0x00/12, code 0x0C/4, unit 0x12, romVersion 0x1E. GBA: title 0xA0/12, code 0xAC/4. GB/GBC: title 0x134/11.
- Rom3ds.Read (Azahar.cs:18-51): "NCSD" @0x100 means partition0 = u32@0x120 * 0x200. "NCCH" @0x100: product code ASCII 0x150/16 ("CTR-P-EKJA", GameCode = [6..10]), TitleId u64@0x118, Encrypted = (head[0x18F] & 0x04) == 0.
- RomCheck.HasEncryptedSecureArea: u32@0x20 == 0x4000, and u64@0x4000 not in {0xE7FFDEFFE7FFDEFF, 0, "encryObj"}.
- Header cache: `Dictionary<(string Path, DateTime Time),(string Code,string Title)>` behind `lock`.

SAFE WRITE (Shared/SafeFile.cs:21-40)
```
var tmp = $"{path}.{Guid.NewGuid():N}.monhub-tmp";
try { write(tmp); File.Move(tmp, path, overwrite: true); }
finally { try { if (File.Exists(tmp)) File.Delete(tmp); } catch {} }
```
API: WriteAllText(path, text, Encoding? = UTF8 no BOM), WriteAllLines, WriteAllBytes.
Import copy (Importer.cs:438-452): `tmp = target + ".monhub-tmp"`; skip if target exists; File.Copy(src,tmp,true) or convert; `File.SetLastWriteTime(tmp, File.GetLastWriteTime(src))`; `File.Move(tmp, target)` (no overwrite).

DUPLICATES
- Library: `HashSet<string> names (OrdinalIgnoreCase)`, first base name wins; Randomized is scanned before Roms.
- Import: `seenRoms.Add($"{f.Name}|{f.Length}")`; target exists with the same length gives "already in MonHub", a different length gives "name already taken". Both are deselected.

P/INVOKE
- `[DllImport("shell32.dll", CharSet=Unicode)] static extern int SHFileOperation(ref SHFILEOPSTRUCT op);` struct {IntPtr hwnd; uint wFunc; string pFrom; string? pTo; ushort fFlags; [MarshalAs(Bool)] bool fAnyOperationsAborted; IntPtr hNameMappings; string? lpszProgressTitle}. FO_DELETE=3; flags 0x40|0x10|0x4|0x400|0x4000. pFrom = string.Join('\0', files) + "\0\0".
- `[DllImport("user32.dll", CharSet=Unicode)] static extern uint PrivateExtractIcons(string file, int index, int cx, int cy, IntPtr[] icons, uint[] ids, uint count, uint flags);` + `DestroyIcon(IntPtr)` + `shell32 ExtractAssociatedIcon(IntPtr instance, StringBuilder path, out ushort index)`. Converted with System.Drawing.Icon.FromHandle -> ToBitmap -> PNG MemoryStream -> `new Avalonia.Media.Imaging.Bitmap(png)`. Cache key (path.ToLowerInvariant(), size, mtimeUtc). Default size 128.
- `[DllImport("shell32.dll", ExactSpelling=true)] static extern int SHGetKnownFolderPath(ref Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr ppszPath);` FOLDERID_Downloads = 374DE290-123F-4565-9164-39C4925E467B.
- Registry discovery keys (HKCU): `Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\MuiCache` and `Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Compatibility Assistant\Store`. Each value name is cut at ".exe" (MuiCache entries look like "C:\...\melonDS.exe.FriendlyAppName").

DISCOVERY
- `sealed record EmulatorKind(string Label, EmuPlatform Platform, string[] ExePrefixes, string[] ConfigFiles, string[] AppDataDirs)`. Kinds: DeSmuME, melonDS (melonDS.toml/.ini, AppData melonDS), VisualBoyAdvance (prefixes VBA-, VBA_, VBA., VBAM), mGBA (config.ini, qt.ini).
- Walk roots and depths: Downloads/Desktop/Documents/OneDrive* depth 5, profile 4, LocalAppData\Programs, ProgramFiles(x86) and fixed drive roots depth 3.
- Config folder keys: `FolderKey = @"rom|recent|mru|last|batt|save|file|^\d+$"`, `SaveKey = @"batt|savegame|savefile"`. Multi-line TOML `[ "...", ]` lists are joined.
- `record ImportSource(string Label, string Folder, List<string> ExtraFolders) { List<string> SaveFolders; EmuPlatform Platform; }`. `class ImportItem { ImportKind Kind; string SourcePath; long Size; bool Selected = true; string? Note; }`. `record Result(int Copied, int Skipped, List<string> Problems)`. Scan depth 3.

ASYNC LOADING (GameData.cs:18-30)
```
lock (Gate) { if (_running is { IsCompleted: false } running) return running;
  return _running = Task.Run(() => { var games = GameLibrary.Load(); foreach (var g in games) _ = g.Summary; return games; }); }
```
Signature = `DateTime.Now.ToString("yyyyMMddHHmm") + "|" + join(g => $"{g.Rom}*{g.RomTime.Ticks}*{g.NewestSave?.Path}*{g.NewestSave?.Time.Ticks}")`. LibraryPage appends favourites and the orphan count.

SEARCH / FILTER (LibraryPage.axaml.cs:88-113)
`words = Text.Split(' ', RemoveEmptyEntries); visible = cards.Where(MatchesFilter && words.All(w => Name.Contains(w, OIC) || Look.Version.Contains(w, OIC))).OrderByDescending(c => c.IsFavorite)`. Filters are RadioButtons with GroupName="LibFilter": FilterAll, FilterFavorites, FilterSaved, FilterRuns, FilterOriginals.

LAST PLAYED / PLAYTIME
Continue = `games.Where(g => g.NewestSave != null).MaxBy(g => g.NewestSave!.Time)`. Recent = cards with HasSave except the Continue game, Take(4). Total playtime = sum of SaveSummary.PlayTimeSpan parsed from Pokémon saves. There is no session tracking.

XAML TOKENS USED IN THESE FILES
- Control themes: Hub.Card and Hub.Row (ListBoxItem), Hub.PlainItem, Hub.Chip (RadioButton), Hub.Search (TextBox), Hub.Tile (Button), Hub.Key, Hub.PrimaryKey, Hub.Ghost, Hub.DangerKey, Hub.Panel/Hub.Inset (ContentControl), Hub.DialogWindow (StaticResource).
- Brushes: Hub.Ink, Hub.InkMuted, Hub.Accent, Hub.Danger, Hub.Good, Hub.Surface, Hub.SurfaceAlt, Hub.SurfaceHover, Hub.SurfaceSelected, Hub.Line, Hub.InsetLine, Hub.Selection, Hub.Shadow, Hub.BadgeOn, Hub.Dialog.
- Metrics: Hub.RadiusCard/RadiusInner/RadiusSlot/RadiusChip, Hub.CardMargin, Hub.CardBorder, Hub.SelectionBorder, Hub.ShadowOffset, Hub.ShadowVisible, Hub.CursorText, Hub.FontLabel, Hub.SizeTag.
- Text classes: label, title, heading, muted, body, value, divider.
- Hub.Card (Themes/Base.axaml:482) draws a hard offset shadow Border (no DropShadowEffect, per PERFORMANCE.md) and a selection border plus a 'Cursor' TextBlock shown when :selected. That is a console-style selection cursor RetroHub can reuse.

## Files
- MonHub\GameLibrary.cs: Core library: GameEntry model (15-82), GameLibrary.Load scan (96-134), Continue (140-145), emulator choice (149-178), DS save transfer (192-219), FilesOf/DeleteGame (227-263), OrphanSaves (277-321), RecentRoms from emulator configs (349-367), INI/TOML Setting reader (410-434), header cache (436-449), IsBlank save detection (476-512), RomFiles/SystemOf (514-535), OrphanSave (553-590), SaveFormat DSV<->raw (593-626), TimeText.Ago (628-639), RecycleBin SHFileOperation (642-680), RomCheck DS secure area (683-709)
- MonHub\Importer.cs: Emulator discovery (registry MuiCache + bounded folder walk), config-folder extraction, ROM/save scan with name|size dedup, copy via temp file then move without overwrite, KnownFolder Downloads P/Invoke
- MonHub\ImportWindow.axaml: Import dialog: source checklist, found-files table (ListBox with a Grid of columns 36,160,*,80,180), progress bar and result line
- MonHub\ImportWindow.axaml.cs: Async emulator search, rescan cancelled with a CancellationTokenSource, Import with IProgress<string>, blocks closing while busy
- MonHub\GameData.cs: LoadAsync shares one in-flight load between pages; Signature string for change detection
- MonHub\GameLook.cs: Game code / GB title -> (version label, sprite key, accent colour) table; Region from game code letter; SpriteView animated Image control
- MonHub\GameViews.cs: GameCard view model: immutable accent brushes (safe to build off the UI thread), luminance-based text colour, favourite glyph, save time; TeamSlot
- MonHub\IconHelper.cs: Exe icon extraction (PrivateExtractIcons, falling back to ExtractAssociatedIcon) -> PNG -> Avalonia Bitmap, cached by (path, size, mtime)
- MonHub\Shared\RomInfo.cs: ROM header parsing for .nds/.gba/.gb/.gbc (title, game code, unit code, ROM version); file-type groups/extension filter
- MonHub\Shared\SafeFile.cs: Atomic write: unique temp file + File.Move(overwrite:true)
- MonHub\Azahar.cs: Rom3ds record + Read(): NCSD/NCCH header parse (product code, title ID, encrypted flag), cached by (path, mtime) (lines 8-52)
- MonHub\HubConfig.cs: JSON settings: Favorites (List<string> of file names), Fangames (Name/Path), Save via SafeFile, broken file backed up to .kaputt
- MonHub\HubPaths.cs: Portable root detection (System\App two levels below root), folder layout, RomExtensions
- MonHub\Pages\LibraryPage.axaml: Library UI: search box with overlay hint, filter chips in a WrapPanel, GameList ListBox (Hub.Card items, 68px rows), detail pane with emulator radio chips and a Play button
- MonHub\Pages\LibraryPage.axaml.cs: Reload with load id and signature, ShowGames multi-word search and favourites first, MatchesFilter, emulator preselection, delete game/saves to Recycle Bin, responsive Arrange()
- MonHub\Pages\StartPage.axaml: Continue hero panel, trainer card, 'Recently played' ListBox, onboarding empty state
- MonHub\Pages\StartPage.axaml.cs: Continue = game with the newest save; recent = next 4 games with a save; total playtime summed from save data
- MonHub\Pages\FangamesPage.axaml: Tile grid (ItemsControl + WrapPanel, Hub.Tile buttons 236x132) with context menu
- MonHub\Pages\FangamesPage.axaml.cs: FangameView (icon + missing-file status), add/remove persisted in HubConfig.Fangames
- MonHub\AddFangameWindow.axaml: Add-program dialog: path + name fields, live tile preview
- MonHub\AddFangameWindow.axaml.cs: File picker filters, guesses the name from the folder when the file is Game.exe or very short, live icon preview, validation
- MonHub\OrphanWindow.axaml: Multi-select list of saves without a game: coloured edge per place, tag chips, time/size
- MonHub\OrphanWindow.axaml.cs: Select-all, delete to Recycle Bin, show in folder
- MonHub\Txt.cs: Inline bilingual strings Txt.L(de,en) and XAML {local:L 'de','en'} markup extension; language read straight from the JSON settings file
- MonHub\Shared\Sprites.cs: Sprite art: avares:// PNG strips decoded with WriteableBitmap, cut into frames, unbounded cache (lines 16-90)
- MonHub\Compat.cs: WPF-to-Avalonia shims (Visibility enum/extension, MessageBox enums, global usings FrameworkElement=Control, BitmapSource=IImage, Brush=IBrush) that the pages above rely on
