# RetroHub emulation module: registry, launch planner, portable recipes, config editors

The module is built and committed on `feat/emuconfig` (commit `f23e738`, 24 files, all mine). All 142 tests pass. I ran them with the xUnit test executable, because the prescribed `dotnet test` command does not work in this repo (details under "Tests").

## Files created (all under `D:\RetroHub-wt\emuconfig`)
- **Emulation:** `src\RetroHub.Core\Emulation\` `EmulatorRegistry.cs`, `LaunchPlanner.cs`, `ArgumentTemplate.cs`, `BiosCheck.cs`, `EmulatorProcesses.cs`
- **Config editors:** `src\RetroHub.Core\Emulation\ConfigFiles\` `TextDocument.cs`, `IniFile.cs`, `TomlFile.cs`, `RetroArchConfig.cs`
- **Recipes:** `src\RetroHub.Core\Emulation\Portable\` `PortableSetupBase.cs`, `PortableSetups.cs`, `RetroArchSetup.cs`, `MgbaSetup.cs`, `MelonDsSetup.cs`, `DesmumeSetup.cs`, `AzaharSetup.cs`, `DuckStationSetup.cs`, `MarkerSetups.cs` (Dolphin, PCSX2, PPSSPP)
- **Tests:** `tests\RetroHub.Core.Tests\Emulation\Config\` `Fakes.cs` (FakeCatalog, FakeRegistry, TempRoot), `ArgumentTemplateTests.cs`, `ConfigFileTests.cs`, `LaunchPlannerTests.cs`, `EmulatorRegistryTests.cs`, `PortableSetupTests.cs`

## Public API (what other modules call)

**Registry and planner**
- `EmulatorRegistry(ICatalog catalog, AppPaths paths, SettingsStore settings) : IEmulatorRegistry`
  - Members: `All`, `Get(id)` (case-insensitive), `Refresh()`, `SetUserPath(id, exePath)`, `event Changed`.
  - `SetUserPath` stores the path portably and saves the settings.
  - The constructor scans once; nothing in it throws.
- `LaunchPlanner(ICatalog catalog, IEmulatorRegistry registry, AppPaths paths, SettingsStore settings) : ILaunchPlanner`
  - Members: `Plan(game, emulatorOverride?, modeOverride?)` and `ChoicesFor(systemId)`.
  - `Plan` never throws; every issue becomes a `LaunchProblem`.
  - `ChoicesFor` lists the system's emulators in order. A bare `retroarch` entry is expanded to one `retroarch/<core>` per mapped core. Other catalog emulators that list the system are appended. Native entries are left out.

**Argument expansion**
- `static ArgumentTemplate`
  - `Expand(IEnumerable<string> tokens, ArgumentContext ctx)`
  - `ExpandToken(token, ctx)`
  - `SplitCommandLine(string?)` (Windows quoting rules)
  - `const FullscreenToken = "{fullscreen}"`
- `sealed record ArgumentContext`: `static For(AppPaths, systemId, romPath, exePath, corePath, LaunchMode, fullscreenArgs)` and `static RetroArchOverridePath(AppPaths)`, which returns `data\config\retroarch\retrohub.cfg`.

**BIOS and processes**
- `static BiosCheck`
  - `Check(SystemDef, biosRoot) → IReadOnlyList<BiosStatus>`, where `BiosStatus(BiosFile File, BiosState State, string? FoundPath, string? Md5)`
  - `Problems(SystemDef, biosRoot)`
  - `ProblemsFor(SystemDef, statuses)`
  - `enum BiosState { Ok, Mismatch, Missing }`
- `static EmulatorProcesses`: `IsRunning(string? exePath)` and `Invalidate()`.

**Config editors**
- `IniFile.Load(path, Encoding? asciiEncoding = null)` / `Parse(text)`
  - `Get` / `Set` / `Remove(section, key)`, `Sections()`, `Keys(section)`, `HasSection`
- `TomlFile.Load` / `Parse`
  - `Get`, `GetRaw`, `Set(table, key, rawValue)`, `SetString`, `SetBool`, `Remove`, `HasTable`, `static Quote` / `QuotePath`
- `RetroArchConfig.Load` / `Parse`: `Get` / `Set` / `Remove(key)`, `Keys()`, `EnsureComment`
- Common base `TextDocument`: `Save(path)` (via SafeFile), `SaveIfChanged(path)`, `IsNew`, `IsDirty`, `Encoding`, `NewLine`, `ToBytes()`.

**Portable recipes**
- `static PortableSetups`
  - `All` (9 recipes: retroarch, mgba, melonds, desmume, azahar, dolphin, pcsx2, duckstation, ppsspp)
  - `Find(id)`
  - `Apply(EmulatorInstall, LaunchPlan, AppPaths) → PortableApplyResult { Applied, SkippedRunning, NotInstalled, Failed, NoRecipe }` — never throws. **The session service should call this one.**
  - `SummaryFor(id)` returns a German description for the Emulators page.
- `abstract PortableSetupBase : IPortableSetup` (`Run(...)`, `Summary`) and `PortableContext`.

## Contract changes and packages
- **Contract changes:** none.
- **Packages added:** none.

## Decisions and notes for other agents
- **Catalog agent:** the RetroArch definition must include `"--appendconfig", "{retroarchOverride}"` in its `Args`, otherwise the override file is never loaded.
- **RetroArch `config_save_on_exit = "false"`:** with `--appendconfig`, RetroArch would write RetroHub's absolute paths and the per-launch fullscreen flag back into the user's `retroarch.cfg` on exit. The trade-off is that changes made in the RetroArch menu during a RetroHub session are not saved automatically. This is documented in `RetroArchSetup.cs`.
- **More RetroArch keys:** I also set `*_in_content_dir = false` and `sort_savefiles_enable` / `sort_savestates_enable = false`, so RetroArch actually uses RetroHub's folders and doesn't add a per-core subfolder. `pause_nonactive = false` is only written for embedded mode; otherwise the key is removed and the user's own setting applies.
- **Per-system subfolders:** recipes write saves/savestates/screenshots to `saves/<system id>` and so on. For DuckStation that means `saves/psx`, not `ps1` as the task text said.
- **DuckStation:** `settings.ini` is only edited once DuckStation has run once and the file contains `[Main] SettingsVersion`. DuckStation resets a settings file without that key, so writing it earlier would be wasted.
- **PPSSPP:** an existing `installed.txt` is only logged, not deleted. Deleting it would make the user's existing saves in Documents seem to disappear.
- **Native PC games:** a native game is never embedded unless its definition provides an `Embed` profile.
- **AlreadyRunning:** the planner adds a non-blocking `AlreadyRunning` warning when the chosen emulator exe is already running.
- **Optional BIOS:** a missing optional BIOS file produces no problem at all. When RetroArch would miss a BIOS file because it sits in a subfolder of `bios/`, the planner adds a non-blocking hint.

## Tests
- **`dotnet test` fails in this repo:** "Testing with VSTest target is no longer supported by Microsoft.Testing.Platform on .NET 10 SDK". There is no `global.json` with the Microsoft.Testing.Platform opt-in, and that file isn't mine. I ran the xUnit v3 exe instead: `tests\RetroHub.Core.Tests\bin\Debug\net10.0\RetroHub.Core.Tests.exe`. **Whoever owns the repo setup needs to add the opt-in before `dotnet test` works.**
- **Results:**
  - Module namespace: Total 142, Failed 0, Skipped 0.
  - Whole test project: Total 142, Failed 0, Skipped 0. It currently contains only my tests.
  - `dotnet build src/RetroHub.Core`: 0 warnings, 0 errors.
  - `RetroHub.slnx` fails only with CS5001 in `RetroHub.App` (no Main method). The clean baseline has the same error.
- **Real layout test (ran, read-only):** it took a file snapshot before and after and confirmed nothing under `D:\RetroHub\emulators` changed. Detected:

| Emulator | Path | Version |
|---|---|---|
| retroarch | `RetroArch-Win64\retroarch.exe` | none (no version resource, no version in the folder name) |
| mgba | `mGBA-0.10.5-win64\mGBA.exe` | 0.10.5 |
| melonds | `melonDS.exe` | 1.1 |
| desmume | `DeSmuME_0.9.13_x64.exe` | 0.9.13 |
| azahar | `azahar-windows-msvc-2126.1.2\azahar.exe` | 2126.1.2 |

  - All 7 RetroArch cores were listed.
  - mGBA's ProductVersion is literally "mgba", so version cleaning now rejects strings without a number and falls back to FileVersion.
- **Performance fix found by testing:** the first process snapshot took about 800 ms because of the `MainModule` fallback on protected processes. On Windows I now use only `QueryFullProcessImageName`, and a snapshot takes about 12 ms. Cached calls take 0.2 ms per 100.
- **Mutation check:** I disabled the "ignore exited processes" branch and the test still passed. Windows evidently doesn't list exited processes in `GetProcesses()` even while their handle is open. So that branch is purely defensive and no test exercises it.

## Known limitations
- **Not verified against real emulator-written configs:** DuckStation keys (`[BIOS] SearchDirectory`, `[MemoryCards] Directory`, `[Folders] SaveStates/Screenshots`), Azahar `[Paths] screenshotPath`, mGBA `screenshotPath` / `pauseOnFocusLost`, and the RetroArch `sort_*` / `*_in_content_dir` keys. I only had DeSmuME and melonDS sample configs and MonHub's tested keys to check against.
- **No emulator was started:** the recipes are tested only against fake temp emulator folders.
- **Not redirected to RetroHub's folders:**
  - Dolphin and PCSX2 keep their data next to their exe; only the portable marker is created.
  - Azahar savestates stay in its own `user\states` folder.
  - mGBA and DeSmuME BIOS paths are not set.
- **DeSmuME encoding:** DeSmuME reads its ini as ANSI, so pure-ASCII or new files are written as Latin-1. Path characters outside Latin-1 cannot be represented; this is logged.
- **TOML editor is minimal:** dotted keys, inline tables and array-of-tables are never edited.

I found and fixed four defects in the emuconfig module and committed the fixes as `f2c0ff3` on `feat/emuconfig`. The whole test project passes: 147 tests, 0 failed, 0 skipped. No contract files or packages were changed.

## Defects found and fixed
Line numbers refer to the implementer's commit `f23e738`.

1. **Azahar ignored the ROM and screenshot paths** (`src/RetroHub.Core/Emulation/Portable/AzaharSetup.cs:49-50`). The recipe wrote `[Paths] romsPath` and `screenshotPath`, copying MonHub's mistake. The Azahar-written `%APPDATA%\Azahar\config\qt-config.ini` on this machine has no `[Paths]` section; Azahar keeps these keys in `[UI]` as `Paths\romsPath` and `Paths\screenshotPath`.
   - **Fix:** it now writes `[UI] Paths\romsPath` without a `\default` twin, since Azahar's own file has none for that key. It writes `[UI] Paths\screenshotPath` with its `\default=false` twin.
   - **Tests:** I updated the existing Azahar test and added a regression test built on a shortened copy of the real file. It checks the new keys, that no `[Paths]` section appears, CRLF only, and that unrelated lines are unchanged.

2. **TOML editor broke multi-line values** (`src/RetroHub.Core/Emulation/ConfigFiles/TomlFile.cs:195`, `:222`, `:202-204`). A failing test confirmed all three cases:
   - `Set` on a key whose value spans several lines (array or `"""` string) replaced only the first line. The rest stayed behind as invalid TOML, which would make melonDS throw its config away.
   - `Remove` had the same problem.
   - A new key could be inserted inside a multi-line string that contains a blank line.
   - **Fix:** lines inside a multi-line value are now tracked as belonging to it. `Set` and `Remove` replace or remove all of them, and new keys go after the whole value.

3. **One null entry in `settings.json` hid every emulator** (`EmulatorRegistry.cs:134` and `:69-70`, `LaunchPlanner.cs:175`, `:273`, `:288`). A hand-edited `settings.json` with `"emulatorPaths": null` (or another of the per-emulator dictionaries set to null) is read in as null. The registry then reported every emulator as not installed, and planning failed. Confirmed by test.
   - **Fix:** all reads treat a null dictionary as empty, and `SetUserPath` recreates it.

4. **`Plan()`'s error fallback could still throw** (`LaunchPlanner.cs:49`). If the catalog itself threw, the catch block called `_catalog.GetSystem` again unguarded. That call is now guarded.

I checked that the new regression tests fail on the original code: with my source changes stashed, 6 tests fail; with them, all pass.

## Checked and left as is
- **Other config keys:** mGBA, melonDS and DeSmuME keys match real configs on this machine (`%APPDATA%\mGBA\config.ini`, `<MonHub-Ordner>\Melonds\melonDS.toml`, `<MonHub-Ordner>\desmume-0.9.13-win64\desmume.ini`). I ran the recipes against temp copies of these and the Azahar file; only the intended lines changed and line endings were kept. That check was a one-off and is not in the committed tests.
- **No defects found** in the RetroArch override (the user's `retroarch.cfg` is never touched), skip-while-running, the 1 s process cache, the planner's choice and mode precedence, the problem messages, command-line splitting, or path-traversal guards.
- **Catalog:** the catalog branch's RetroArch definition already passes `--appendconfig {retroarchOverride}`.

## Still open (not changed)
- **Settings changed for embedded play stay changed.** After one embedded session, DeSmuME `Window Force Ratio` stays 0 and mGBA `pauseOnFocusLost` stays 0 in later windowed or fullscreen launches. MonHub sets the DeSmuME ratio back to 1 outside embedded mode; RetroHub leaves the user's value alone, as the implementer documented.
- **Unverified keys:** the DuckStation keys and the RetroArch `sort_*` / `*_in_content_dir` keys are still not checked against real files, because neither is installed here.
- **TOML scope:** dotted keys and inline tables are still not supported.
- **`dotnet test` fails in this repo** with "Testing with VSTest target is no longer supported by Microsoft.Testing.Platform on .NET 10 SDK". The repo needs a `global.json` opt-in, which is not a file I own.

## Commands run and results
- `dotnet build src/RetroHub.Core -nologo -v q`: 0 errors.
- `dotnet build tests/RetroHub.Core.Tests -nologo --no-incremental -v q`: 0 warnings, 0 errors.
- `dotnet test tests/RetroHub.Core.Tests -nologo`: fails with the VSTest error above.
- `tests\RetroHub.Core.Tests\bin\Debug\net10.0\RetroHub.Core.Tests.exe` (whole project): Total 147, Failed 0, Skipped 0.
- The same exe with `-namespace RetroHub.Core.Tests.Emulation.Config`: Total 147, Failed 0. This module's tests are currently the only ones in the project.
- The real-layout detection test against `D:\RetroHub\emulators` was among them, read-only, and found all 5 emulators with their versions.

## Files changed (all under `D:\RetroHub-wt\emuconfig`)
- `src\RetroHub.Core\Emulation\Portable\AzaharSetup.cs`
- `src\RetroHub.Core\Emulation\ConfigFiles\TomlFile.cs`
- `src\RetroHub.Core\Emulation\EmulatorRegistry.cs`
- `src\RetroHub.Core\Emulation\LaunchPlanner.cs`
- `tests\RetroHub.Core.Tests\Emulation\Config\PortableSetupTests.cs`
- `tests\RetroHub.Core.Tests\Emulation\Config\ReviewRegressionTests.cs` (new)

