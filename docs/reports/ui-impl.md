I built the UI foundation in `D:\RetroHub-wt\ui` (branch `feat/ui`) and committed it as `51da8ee`. It builds and all 35 headless UI tests pass. The app starts for real without exceptions, using the demo services. One problem affects everyone: `dotnet test tests/RetroHub.Core.Tests` fails on the .NET 10 SDK before running any test, so the Core tests can't run as instructed (details under "Tests").

## Verification (what I actually ran)

**Tests:** `dotnet test tests/RetroHub.App.Tests` passed 35 of 35 (namespace `RetroHub.App.Tests`):
- **Cover grid** (`CoverGridTests`):
  - Row chunking: 100 items at 600 px gives 5 columns and 20 rows; 1003 items leave a short last row.
  - Virtualization: only a few of 201 rows exist at a time.
  - Rebuild: a width change that keeps 5 columns causes no rebuild; going to 3 columns rebuilds and the focused item keeps focus.
  - Directional moves stop correctly at edges and the short last row.
  - The arrow keys scroll virtualized rows into view; Home and End work.
  - A focus request waits until the data has loaded.
- **Localization** (`LocalizationTests`): every key used in XAML and code exists in `de.json`; both files are valid; English placeholders match German; fallback is English → German → key.
- **Navigation** (`NavigationTests`):
  - Pages stay alive, and Back (Escape) returns to the same page instance.
  - Sidebar section highlighting, plus the focus ring when arrowing from the Play button into the shelves and left into the sidebar.
  - The library grid gets focus, and Accept opens the game.
  - The Favorite action, LB/RB cycling through sections, and Search.
  - A blocked launch shows the problems dialog.
  - A full launch → running → session end returns to the game detail and increases the play count.
  - The sidebar collapses below 1280 px.
- **Theming** (`ThemingTests`): accent swap, motion levels, placeholder initials.

**Core tests:** `dotnet test tests/RetroHub.Core.Tests` fails before running anything, with *"Testing with VSTest target is no longer supported by Microsoft.Testing.Platform on .NET 10 SDK"*. This is a repo-level setting, not my code. I worked around it in my own project only, with `<IsTestingPlatformApplication>false</IsTestingPlatformApplication>`. Fix for the others: the same property in each test project, or a root `global.json` that opts in to Microsoft.Testing.Platform.

**Real start:** I ran the built exe (`src/RetroHub.App/bin/Debug/net10.0/win-x64/RetroHub.App.exe`, built from `src/RetroHub.App`) for 7 seconds, closed it with exit code 0, and checked `data/logs/retrohub-20261109.log`: the log has no errors and shows the expected warning "Core services not available … using the demo services". Starting a second instance exits with code 0 and logs "already running … bringing it to the front".

**Real window:** a screenshot of the real window confirmed the custom title bar works: our own caption buttons, no OS buttons.

**Incident:** a second real-window capture also sent three arrow keys through `SendKeys`. The RetroHub window was not in front, so the keys went to whatever window was in the foreground (a browser). I deleted that capture and stopped using `SendKeys`; keyboard routing is covered by the headless tests instead.

## Visual review
Screens are in `D:\RetroHub-wt\ui\artifacts\screens\`, rendered with Skia at 1600×900 plus `-narrow` variants at 1100×700. Files: home, library, library-list, systems, system-gba, detail, detail-problems, emulators, imports, settings, launch-starting, launch-running.

I looked at every PNG and fixed what I found over several passes:
- clipped focus ring on the first tile
- list columns not lining up
- empty accent swatches
- a colour band where the page tint stopped at the top bar
- toasts covering the toolbar
- the demo badge showing in the collapsed sidebar
- platform cards leaving a gap on narrow windows
- focus lost after the library finished loading
- the focus ring not appearing on arrow-key moves

## Contract changes and packages
- **Contracts:** no changes.
- **Packages:** none added to `Directory.Packages.props`. The test project uses `Avalonia.Headless` plus xunit.v3 4.0.2. I did not use `Avalonia.Headless.XUnit`: version 12.1.5 is built against xunit.v3 3.2 and fails test discovery with 4.0.2 (`MissingMethodException`). The tests run through a small helper (`UiTest.Run`) on the headless session instead.
- **Outside `src/RetroHub.App`:** I added the test project to `RetroHub.slnx` and `/artifacts/` to `.gitignore`.
- **Tooling:** I installed the `ilspycmd` decompiler into the scratchpad (not globally) to check the Avalonia 12 window-decoration API. That API is `WindowDecorations.BorderOnly` plus `ExtendClientAreaToDecorationsHint`, with `WindowDecorationProperties.ElementRole` set to `TitleBar`, `MinimizeButton`, `MaximizeButton` and `CloseButton`.

## Files (all new under `src/RetroHub.App/` unless noted)
- **Startup:** `Program.cs`, `App.axaml(.cs)`, `app.manifest` (per-monitor DPI), `RetroHub.App.csproj` (language files copied to `resources/lang` and also embedded).
- **Themes:** `Themes/Tokens.axaml`, `Icons.axaml` (vector icons, no icon font), `Controls.axaml`, `Styles.axaml`, `FluentOverrides.axaml`; `Assets/Fonts` (Poppins + `OFL.txt`).
- **Theming:** `Theming/ThemeManager.cs`, `Motion.cs`, `ColorUtil.cs`.
- **Localization:** `Localization/Loc.cs`, `TExtension.cs`; `Resources/lang/de.json` (full) and `en.json` (partial).
- **Input:** `Input/NavAction.cs`, `IInputSource.cs`, `KeyboardMap.cs`.
- **Services:** `Services/AppServices.cs`, `UiPreferences.cs`, `AsyncImageLoader.cs`, `SingleInstance.cs`, `ShellOpen.cs`; `Services/Demo/*` (catalog with 13 systems and 9 emulators, 66 games, repository, emulator registry, launch planner, scanner, sessions).
- **Controls:** `Controls/CoverGrid.cs`, `CoverArt.cs`, `SmallControls.cs` (`GameTile`, `SystemCard`, `StatTile`, `ButtonGlyph`, `GameScreenHost`), `Shelf.cs`, `PageHost.cs`, `RhIcon.cs`.
- **ViewModels:** `ViewModels/*` (Main, Home, Library, Systems/System, GameDetail, Emulators, Imports, Settings, `Navigator`, `LaunchCoordinator`, overlays, `GameItemViewModel`).
- **Views:** `Views/MainWindow.axaml(.cs)`, `Converters.cs`, `Views/Pages/*`.
- **Tests:** `tests/RetroHub.App.Tests/*`.

## Public API other modules will use
- **`AppServices`:** properties `Paths`, `Settings`, `Ui`, `Catalog`, `Games`, `Scanner`, `Emulators`, `Launcher`, `Sessions`, `Input`, `IsDemo`; methods `Create(paths, settings, ui, forceDemo)`, `CreateDemo(...)`, `IsInstalled(EmulatorInstall?)`.
- **Where the real services plug in:** implement `static partial void CreateReal(AppPaths, SettingsStore, UiPreferencesStore, ref AppServices? services)` in a new file `Services/AppServices.Real.cs`. While it is unimplemented, the app uses the demo services; `--demo` on the command line forces them.
- **Gamepad seam:** `IInputSource` with `ActionRaised`, `ControllerChanged`, `Controller`, `ControllerName`, `SetSuspended(bool)`. `NavAction` has the 12 actions you specified. `MainWindow.Execute(NavAction)` runs an action.
- **Localization:** `Loc.Load(lang, resourcesDir)`, `Loc.T(key, args)`, and `{l:T key}` in XAML (add `Upper=True` for capitals). XAML texts switch language live.
- **Theming:** `ThemeManager.ApplyAccent(hex)`, `Motion.Apply(level)` / `Motion.Current`, `App.InitializeUi(settings, resourcesDir)`.
- **Cover grid:** `ItemsSource`, `ItemTemplate`, `ItemWidth`, `CoverAspect`, `CaptionHeight`, `Spacing`, `RowSpacing`, `ContentPadding`, `Header`, `SelectedIndex`, `Columns`, `Move(NavAction)`, `FocusIndex`, `FocusWhenReady`, `ItemInvoked`.
- **Image loading:** `AsyncImageLoader.Shared.LoadAsync(path, width, ct)`.
- **Launching:** `UiContext.Launch.LaunchAsync(item, emu, mode)`.
- **UI-only preferences:** button glyph style, sidebar mode and hint bar are stored in `data/config/ui.json` (`UiPreferencesStore`), so `AppSettings` stays untouched.

## Known limitations
- **Embedded game screen:** `GameScreenHost` is only a placeholder that shows where the emulator window will go. Embedding comes later.
- **Fullscreen hand-off:** hiding RetroHub while a fullscreen emulator runs is not implemented. The launch overlay with "Spiel beenden" is shown for every mode.
- **No real controller yet:** input uses `NullInputSource`. Keyboard routing is complete; button glyphs follow the setting, or the reported controller type in Auto mode.
- **Cover images:** the decode-and-cache path has never been tested with real image files, because the demo library has no covers; every tile uses the generated placeholder art. There is no thumbnail cache on disk, only in memory.
- **English is partial:** about 100 keys; the rest falls back to German. After a language switch, texts built in code refresh when the page is reopened.
- **Not exercised in tests:** the folder picker ("Ordner hinzufügen") and Windows 11's snap-layout flyout on our maximize button.
- **Toasts:** only unhandled UI-thread exceptions show as toasts; `Log.Written` events are not shown.

I reviewed the whole UI module, confirmed 12 real defects with tests, fixed them all, and committed the fixes as `7a50ff2` on `feat/ui`. The full App test suite passes, 51 of 51, in each of 7 full runs. There are no contract changes and no new packages.

## Confirmed defects and fixes
Line numbers refer to the original commit `51da8ee`.

1. **Focus ring was invisible on every Button-based control except game tiles** (`Themes/Controls.axaml` :13, :190, :352, :393, :481). Avalonia 12 clips buttons to their bounds, and the ring sits 2–5 px outside. On the re-rendered screenshots only white corner arcs showed. A pixel test measured a brightness of 53 above the focused Play button.
   - Fix: the five themes now turn clipping off, and the round accent swatches in Settings get a round ring. Re-rendered and checked visually.
2. **Memory leak from localized text** (`Localization/TExtension.cs:153`). Every `{l:T}` binding hooked a static event and was never released. Each discarded control stayed alive forever, including its cover bitmap. Game tiles are rebuilt on every Home visit, and other lists are rebuilt on every reload.
   - Fix: the language subscriptions are now weak and owned by the binding. A test shows a removed control is collected and that text still follows a language switch after GC.
3. **Debounce timers fired once on their own** (`UiContext.cs:17`, `LibraryViewModel.cs:56`, `LaunchCoordinator.cs:26`, `MainViewModel.cs:46`). That `DispatcherTimer` constructor starts the timer, so about 150 ms after start a library-changed event fired with no change, and the library loaded twice. This made an existing navigation test fail depending on test order, because Home reloaded and the focused tile vanished.
   - Fix: the timers are now created stopped.
4. **Any library refresh dropped controller focus** on Home and Konsolen (`HomeViewModel.cs:195`, `SystemsViewModels.cs:299`). Every refresh rebuilt the lists. Fix: unchanged shelves and cards are kept and only their counts update.
5. **Wrong launch result messages** (`LaunchCoordinator.cs:169`). The real `SessionService` on main returns a failed session instead of throwing; the UI reported "wurde mit einem Fehler beendet". Shell-started games (.lnk, steam://) showed "Willkommen zurück" straight away.
   - Fix: a failed start now shows the "Start fehlgeschlagen" dialog with the error. A shell start shows "„…“ wurde gestartet", explaining that play time can't be recorded. Three new keys added to `de.json`/`en.json`.
6. **Overlays were not modal for the keyboard.** Tab moved focus behind the dialog or the launch overlay, and focus was lost after a game ended.
   - Fix: Tab now cycles inside the overlay, and the previous focus is restored when it closes.
7. **The grid/list toggle lost focus** (`LibraryPage.axaml.cs:136`). It checked for focus after the old view was already hidden. Fix: the focus is captured before the switch.
8. **The grid jumped back to item 0** when the focused item left the list, e.g. un-favouriting with the favourites filter on (`CoverGrid.cs:169`). Fix: it now stays at the same position.
9. **"Beim Start scannen" did nothing**; the setting was only saved. Fix: added a quiet start-up scan that only shows a toast when something is new or fails.
10. **Status-bar statistics were read on the UI thread** (`MainViewModel.cs:296`). Moved to a background thread.
11. **A second start could not bring the window to the front reliably.** It now grants foreground rights before signalling the running instance. I did not check visually that the window actually comes to the front.
12. **Repository setup issues:**
    - The test project opted out of the new test runner, which conflicts with main's `global.json`. I removed the opt-out and added a `global.json` identical to main's.
    - `.gitignore` had been fully rewritten with Windows line endings. It is now identical to main's, so merging produces no diff there.

The regression tests (16 cases in `tests/RetroHub.App.Tests/ReviewRegressionTests.cs` and `CoverGridTests.cs`) also cover real cover images for the first time: decoding, caching, missing and corrupt files, and display. All 16 pass. I checked that the tests for defects 1, 2, 3, 5, 6, 7 and 8 fail on the original code and pass on the fix. The tests for defects 4, 9 and the cover images were only run against the fixed code.

## What I ran
- **Build:** `dotnet build RetroHub.slnx` with 0 warnings and 0 errors.
- **App tests:** `dotnet test --project tests/RetroHub.App.Tests` gave 51/51 in each of 7 full runs, including the screenshot renders. Before my changes it was 35/35.
- **Core tests:** in this worktree there are no Core test files, so the command finds 0 tests and exits with code 5.
- **Passing `-nologo` to `dotnet test` breaks it** under main's test runner: the flag is passed to the test app, which then reports 0 tests. Leave it off.
- **Screenshots:** re-rendered and looked at home, library, library-list, detail, systems, settings, imports and the narrow variants.
- **Real app:** started the exe for 8 s and it exited with code 0. The log has no errors and shows "Start-up library scan". A second start exits with code 0 and logs "already running".

## Still missing (unchanged)
- `GameScreenHost` is still only a placeholder; fullscreen hand-off and the gamepad connection are not done.
- The real services are not plugged in yet (`CreateReal` is unimplemented).
- English is partial. Sort and motion option labels built in code don't change on a language switch.
- "Ordner überwachen" and "Cover aus dem Internet laden" are only saved; the core services have to act on them.
- In the headless tests, timers only fire when a test runs the real dispatcher loop. My debounce test does this.

