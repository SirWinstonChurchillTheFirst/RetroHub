I built the catalog loader plus 30 built-in systems and 13 emulators, and committed it as `12e1b66` on `feat/catalog`. All 35 catalog tests pass, the built-in files load with zero warnings, and nothing outside my folders was touched.

**Watch out:** inside any `RetroHub.Core.*` namespace, the plain name `Catalog` means the namespace, not my class. Code there has to write `Catalog.Catalog.Load(paths)`, use an alias like `using CatalogImpl = RetroHub.Core.Catalog.Catalog;`, or work through `ICatalog`. My tests use the alias.

**Files created** (all in `D:\RetroHub-wt\catalog`)
- `src\RetroHub.Core\Catalog\Catalog.cs`, `CatalogBuilder.cs` (internal: merging and checking), `CatalogJson.cs` (internal: JSON reading), `CatalogLoadException.cs`
- `src\RetroHub.Core\Resources\systems\`: `nintendo.json`, `sony.json`, `sega.json`, `other.json` (Atari, NEC, SNK, Bandai, MSX), `arcade-pc.json`
- `src\RetroHub.Core\Resources\emulators\`: `retroarch.json`, `handhelds.json` (mGBA, melonDS, DeSmuME, Azahar), `consoles.json` (Dolphin, PCSX2, DuckStation, PPSSPP, Flycast, Redream, MAME), `native.json`
- `tests\RetroHub.Core.Tests\Catalog\`: `CatalogTestEnv.cs`, `BuiltInCatalogTests.cs`, `PluginCatalogTests.cs`, `FromDefinitionsTests.cs`

**Public API**
- `public sealed class Catalog : ICatalog` (namespace `RetroHub.Core.Catalog`), immutable and thread-safe after loading:
  - `static Catalog Load(AppPaths paths)`: reads `paths.Resources`, then `paths.Plugins`.
  - `static Catalog LoadFrom(string resourcesDir, string? pluginsDir)`: same, with explicit folders.
  - `static Catalog FromDefinitions(IEnumerable<SystemDef>, IEnumerable<EmulatorDef>)`: same checks; duplicate ids become warnings and the later one wins.
  - `static JsonSerializerOptions JsonOptions`
  - The `ICatalog` members (`Systems`, `Emulators`, `GetSystem`, `GetEmulator`, `SystemForFolder`, `SystemsForExtension`, `LoadWarnings`), all case-insensitive. `SystemsForExtension` accepts ".ISO", "iso" or "*.iso".
  - Extra helpers: `EmulatorsForSystem(string systemId)`, `EmulatorRefsFor(string systemId)`, `IsPlaylistExtension(string systemId, string ext)`.
- `public sealed class CatalogLoadException : Exception` with a `Path` property and a German message. It is thrown when a built-in folder is missing or empty, or a built-in file is broken: bad JSON, a missing required field, an unknown property, or a duplicate id.

**How loading behaves**
- Files are read in name order. A file may hold a single object instead of an array. `$schema`/`$comment` keys are ignored.
- A plugin with the same id replaces the built-in entry completely; fields are not merged.
- Plugin problems never throw. Each becomes a German warning, written to the log, and only the bad entry or value is dropped:
  - unknown properties, bad enum values, unsafe ids, folders, exe names or BIOS paths (`..`, absolute paths), non-http(s) links, unknown placeholders
- Unknown emulator references in a system are dropped with a warning. A RetroArch core not listed for that system is kept but warned about.
- If two systems claim the same folder name, a plugin wins over a built-in, otherwise the first one keeps it.
- RetroArch's system list is filled in automatically from its core list.

**Data decisions you may want to revisit**
- **gb/gbc/gba:** standalone mGBA comes first, because embedding it is proven in MonHub.
- **ps1:** order is DuckStation, then RetroArch PCSX ReARMed (it runs without a BIOS and is in your test setup), then SwanStation and Beetle PSX.
- **Merged systems:**
  - Neo Geo is part of arcade (folder alias `neogeo`).
  - PC Engine CD is part of `pcengine`, so CD games get the HuCard thumbnail name.
  - NGP covers Color, WonderSwan covers Color.
- **Extensions:** `.bin` is not listed for disc systems (only Mega Drive and Atari 2600 use it), and `.cia` is not listed for 3DS because Azahar installs it rather than launching it.
- **BIOS:**
  - I included MD5s only where I'm certain: gba_bios, scph5500/5501/5502/1001, dc_boot, sega_101, mpr-17933, syscard3, bios7, bios9, lynxboot. The rest are presence checks only.
  - `Required` is false everywhere, because every case is either optional or "one of several files" (PS1, Saturn, Sega CD region BIOSes), which the format can't express.
- **Bar heights** come from MonHub's `KnownBars`: mGBA 21, melonDS 25, DeSmuME 29, Azahar 33 top / 29 bottom.

**Contract changes / packages:** none.

**Tests run**
- `dotnet test` as given fails here before running anything: the .NET 10 SDK rejects the old VSTest mode, and the repo has no `global.json` opting into the new runner (not one of my files).
- What I actually ran:
  - the built test exe with `-namespace RetroHub.Core.Tests.Catalog`: 35 total, 0 failed (one failure on the first run was a mistake in my test data, now fixed)
  - the whole test exe: 35 total, 0 failed
  - `dotnet test ... -p:IsTestingPlatformApplication=false`: 35 passed
- `dotnet build RetroHub.Core` is clean. The full solution build fails only on RetroHub.App having no `Main` yet, and it fails the same way without my changes.

**Not verified / limitations**
- **For whoever writes the launch planner:**
  - It must fill in `{retroarchOverride}`.
  - It must replace placeholders inside a token: MAME uses `"{romDir};{bios}"`.
  - It should start MAME with the MAME folder as working directory.
- **Command-line flags I could not confirm:** Azahar `-f` (it comes from Citra), Flycast `-config window:fullscreen=yes`, and MAME's "`-window` then `-nowindow`, last one wins" trick for fullscreen. They are marked as unconfirmed in each emulator's Notes.
- **DeSmuME and Redream:** neither has a fullscreen flag. DeSmuME offers Embedded and Windowed only. For Redream, fullscreen depends on its own `redream.cfg`.
- **RetroArch embedding:** it lists Embedded as a mode as you asked, even though embedding is untested (the default is Fullscreen). The contract says Embedded should only be offered once verified.
- **No BIOS entries** for PS2 (any filename works) or MSX (blueMSX needs whole folders), because the format can't describe them.
- **No portable recipe:** PPSSPP, Flycast and Redream have none.
- None of the emulators were actually started; all of this is tested at the data level only.

I found and fixed 6 real defects in the catalog module (plus one stale comment), and committed them as `110cb60` on `feat/catalog`. All 43 tests pass: the implementer's 35 plus 8 new regression tests.

**Confirmed defects** (line numbers are from the implementer's commit `12e1b66`)

1. **Undefined enum numbers were accepted** (`src/RetroHub.Core/Catalog/CatalogBuilder.cs:140-210, 213-302`, especially `:269-276`). The JSON converter also takes plain numbers, so a plugin with `"kind": 7`, `"category": 42` or `"modes": [9]` loaded those meaningless values. A failing test showed `numemu: kind 7` getting through.
   - An unknown `Kind` now drops the entry with a warning, because guessing it would start the program the wrong way.
   - An unknown `Category` becomes `Console`, with a warning.
   - Unknown `Modes` entries are removed, and an unknown `DefaultMode` falls back to the first valid mode (or `Fullscreen`), both with a warning.
   - This also applies to `FromDefinitions`.
2. **Ids ending in "." were allowed** (`CatalogBuilder.cs:400`). Windows drops the trailing dot (`Path.GetFullPath(@"D:\x\saves\gba.")` gives `D:\x\saves\gba`, checked here). A plugin system `gba.` would therefore share the games, saves and savestates folders of the built-in `gba`. The id rule now forbids a trailing dot (`^[a-z0-9](?:[a-z0-9._-]{0,62}[a-z0-9_-])?$`) and the German warning text says so.
3. **Misused `{fullscreen}` went unnoticed** (`CatalogBuilder.cs:262-267`). The contract only expands `{fullscreen}` when it is a whole argument. Something like `--mode={fullscreen}` loaded silently and would reach the emulator unreplaced, and `{fullscreen}` inside `FullscreenArgs` would refer to itself. The first now gives a warning; the second is removed with a warning.
4. **Bar heights were not checked** (`CatalogBuilder.cs:277-282`). `barTop: -5` came through as -5. Bar heights are now kept between 0 and 200 px with a warning, and an empty `ReadyTitleContains` becomes null.
5. **The public lists could be changed by callers** (`Catalog.cs:38-47`). `Systems`, `Emulators` and `LoadWarnings` were plain `List<T>`, so a caller could cast them and change the catalog that is shared between threads. They are now read-only.
6. **Two accent colours were nearly invisible on the dark theme** (`Resources/systems/sony.json:80`, `sega.json:50`). PSP `#2B2B2B` had a contrast of only 1.38 against the background `#0B0A1A`, and Game Gear `#1A1A1A` was about 1.1. They are now PSP `#8C9BB5` and Game Gear `#2D9CDB`. A test requires a contrast of at least 2.0 for every system.
7. The comment in `other.json:2` pointed to `arcade.json`; the file is `arcade-pc.json`.

**Checked and found correct, left unchanged:** every BIOS MD5 the implementer entered, all libretro thumbnail folder names, aspect ratios, years, RetroArch core names, the mGBA, melonDS, DeSmuME and Azahar bar heights (they match MonHub's `KnownBars`), the Dolphin, PCSX2, DuckStation and PPSSPP command-line flags, folder aliases, and how invalid plugin data (paths, links) is dropped. A plugin file saved as UTF-8 with BOM also loads fine; I added a test so it stays that way.

**Suspected but not changed:**
- **Windows device names** (`CON`, `NUL`) as folder or BIOS names: on this Windows 11 machine they are created as normal folders and files, so I saw no defect here. Windows 10 might behave differently; that is untested.
- **`.cue` for PS2:** I'm not sure PCSX2 loads `.cue`, so I did not add it.

**Still open:**
- **RetroArch is offered as "Embedded"** because the brief asked for it. That contradicts the contract and the architecture doc, which say Embedded only after it has been tested. The default stays Fullscreen. Someone should decide which rule wins.
- **Unconfirmed command-line flags**, still marked as such in each emulator's notes: MAME's "`-window` then `-nowindow`" trick, Azahar `-f`, and Flycast's `-config window:fullscreen=yes`. None of the emulators were actually started.
- **A system can end up with no folders** if plugins take all of its folder names. That gives a warning per folder, but code that reads `Folders[0]` would crash on such a system.
- **`dotnet test` without extra options fails** with "Testing with VSTest target is no longer supported … on .NET 10 SDK". The repo needs a `global.json` that switches to the new test runner, and that file is outside this module.

**Commands run:**
- `dotnet build "D:\RetroHub-wt\catalog\tests\RetroHub.Core.Tests" -nologo -v q`: 0 warnings, 0 errors.
- `dotnet build "D:\RetroHub-wt\catalog\src\RetroHub.Core" -nologo -v q`: 0 errors.
- `RetroHub.Core.Tests.exe -namespace RetroHub.Core.Tests.Catalog`, before my fixes: 35/35 passed. The first run of the new regression tests then had 7 of 7 failing (one of those tests was later removed, see the Windows device names point above).
- After the fixes, the full test exe: 43 total, 0 failed.
- `dotnet test "D:\RetroHub-wt\catalog\tests\RetroHub.Core.Tests" -nologo -p:IsTestingPlatformApplication=false`: 43 passed, 0 failed.

No contract files or NuGet packages were changed.

Files changed:
- D:\RetroHub-wt\catalog\src\RetroHub.Core\Catalog\CatalogBuilder.cs
- D:\RetroHub-wt\catalog\src\RetroHub.Core\Catalog\Catalog.cs
- D:\RetroHub-wt\catalog\src\RetroHub.Core\Resources\systems\sony.json
- D:\RetroHub-wt\catalog\src\RetroHub.Core\Resources\systems\sega.json
- D:\RetroHub-wt\catalog\src\RetroHub.Core\Resources\systems\other.json
- D:\RetroHub-wt\catalog\tests\RetroHub.Core.Tests\Catalog\ReviewRegressionTests.cs (new)

