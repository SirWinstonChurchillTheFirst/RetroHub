RUNTIME MODULE REPORT (worktree D:\RetroHub-wt\runtime, branch feat/runtime, commit f4308bc)

The session service and the window embedder are built and committed. All 64 tests in the test project pass, including the interactive mGBA embedding test. One thing to know first: `dotnet test` does not run on the .NET 10 SDK in this repo, so I ran the xUnit v3 test exe directly (details under "Tests").

## Files created (only within my ownership)
- D:\RetroHub-wt\runtime\src\RetroHub.Core\Emulation\SessionService.cs
- D:\RetroHub-wt\runtime\src\RetroHub.Core\Emulation\GameSession.cs
- D:\RetroHub-wt\runtime\src\RetroHub.Core\Emulation\Windows\Native.cs — internal `[LibraryImport]` P/Invokes (x64, GetWindowLongPtrW/SetWindowLongPtrW) and `SafeJobHandle`
- D:\RetroHub-wt\runtime\src\RetroHub.Core\Emulation\Windows\JobObject.cs (internal)
- D:\RetroHub-wt\runtime\src\RetroHub.Core\Emulation\Windows\ProcessLauncher.cs (internal: CreateProcessW, env block, exe resolution, already-running check)
- D:\RetroHub-wt\runtime\src\RetroHub.Core\Emulation\Windows\CommandLine.cs
- D:\RetroHub-wt\runtime\src\RetroHub.Core\Emulation\Windows\WindowFinder.cs
- D:\RetroHub-wt\runtime\src\RetroHub.Core\Emulation\Windows\WindowEmbedder.cs
- D:\RetroHub-wt\runtime\src\RetroHub.Core\Emulation\Windows\Foreground.cs
- Tests in D:\RetroHub-wt\runtime\tests\RetroHub.Core.Tests\Emulation\Runtime\: CommandLineTests.cs, SessionServiceTests.cs, RecoveryTests.cs, EmbeddingSmokeTests.cs, RuntimeTestSupport.cs (fake repo, fake recipes, Win32 test window with its own message loop, screen capture to PNG)

## Public API other modules will call

**`RetroHub.Core.Emulation.SessionService : ISessionService, IDisposable`**
- Constructor: `SessionService(IGameRepository repo, AppPaths paths, IEnumerable<IPortableSetup> setups)`.
- Members:
  - `IGameSession? Current`
  - `GameSession? CurrentSession`
  - `string SessionFile` (data/cache/session.json)
  - `IGameSession Start(LaunchPlan plan)`
  - `void RecoverInterruptedSession()`
  - `static bool IsProgramRunning(string exePath)`
  - events `SessionStarted`, `SessionEnded`
  - `Dispose()`
- `Start` throws `InvalidOperationException` (German message) in three cases: `!CanLaunch`, a session is already running, or `plan.Emulator.ExePath` is already running. The already-running check compares the exact MainModule path, falling back to QueryFullProcessImageName.
- A failed process start does not throw. It returns a session with `State == Failed` and a German `Error`; no events are raised and `Current` stays null.
- `SessionStarted` is always raised before `SessionEnded`, outside the internal lock.
- `Dispose` does not kill the game and keeps session.json, so the next start can recover it.

**`RetroHub.Core.Emulation.GameSession : IGameSession`**
- Everything from the interface, plus:
  - `int ProcessId`
  - `string? ExePath`
  - `string? EmulatorId`
  - `DateTime? EndedUtc`
  - `bool TracksProcessTree`
  - `bool Detached`
  - `bool Recovered`
  - `bool WasKilled`
  - `IReadOnlyList<int> ProcessIds()`
- `Ended` fires exactly once. A handler added after the session ended is called immediately, and handler exceptions are logged.

**`RetroHub.Core.Emulation.Windows.WindowEmbedder : IDisposable`**
- Constructor: `WindowEmbedder(EmbedProfile profile)`.
- Host window helpers: `static nint CreateHostWindow(nint parentHwnd)`, `static void DestroyHostWindow(nint)`.
  - `DestroyHostWindow` first releases any game window still inside the host, so the emulator isn't destroyed with it.
- Embedding:
  - `bool Take(nint gameWindow, nint hostWindow, int width, int height)` — if a busy emulator hasn't finished after 300 ms it returns true, and the caller should check `IsAttached`.
  - `void Place(int x, int y, int w, int h)`
  - `void Release(bool toDesktop)` — false means detach and minimize without activation.
  - `Dispose()`
- Masking: `void SetMasked(bool hidden)` and `static void SetMasked(nint host, bool hidden)` (the static one lets you mask before `Take`).
- Readiness: `bool IsResponsive()`, `void Settled()`.
- Focus and position: `void Focus()`, `void NudgeMoved()`.
- Measurement: `(int Top, int Bottom) MeasureBars()`.
- State: `string Title`, `bool Exists`, `bool IsAttached`, `nint GameWindow`, `nint HostWindow`, `EmbedProfile Profile`.

**Static helpers**
- `Foreground.BringToFront(nint hwnd)`
- `WindowFinder.FindMainWindows(IReadOnlyCollection<int>)`, `WindowFinder.FindTopLevelWindows(pids, visibleOnly)`
- `CommandLine.Build(fileName, args)`, `CommandLine.JoinArguments(args)`, `CommandLine.AppendArgument(StringBuilder, string)`

**Behaviour decisions to document for other modules**
- **Empty environment value:** in `plan.Environment`, an empty value removes that variable from the child's environment.
- **Leaving an enclosing job:** if RetroHub itself runs inside a job, the game is started with CREATE_BREAKAWAY_FROM_JOB when allowed, otherwise nested.
- **Re-attached sessions:** a new Job Object only follows processes started after re-attach. The `Plan` is rebuilt from the game in the repo; the `SystemDef` is a placeholder; the launch mode comes from session.json.
- **Exit codes:** a killed session records `ExitCode = null` in the `PlaySession`.

## Contract changes / packages
- Contract files: none changed.
- NuGet packages: none added.

## Tests run (real results)
- **`dotnet test` does not work in this repo.** On the .NET 10 SDK it fails with "Testing with VSTest target is no longer supported by Microsoft.Testing.Platform…". Fixing it needs a repo-level global.json opting into the new test runner, which isn't my file. I ran the xUnit v3 exe directly: `tests\RetroHub.Core.Tests\bin\Debug\net10.0\RetroHub.Core.Tests.exe`.
- **Non-interactive** (`-namespace RetroHub.Core.Tests.Emulation.Runtime -trait- "Category=Interactive"`): 61/61 passed, ~19 s, run twice. A later regression test for the event order also passes. Covered:
  - quoting matrix (31 cases) round-tripped through CommandLineToArgvW
  - cmd `ping -n 3` session: Ended once, 2–3 s recorded, ExitCode 0, session.json deleted
  - children tracked by the job: the session stays Running after cmd exits and ends only after ping, ~3 s
  - `Kill` kills the whole tree: every pid in the job is dead afterwards
  - `RequestCloseAsync` on a windowless process returns false in about 1.5 s
  - already-running guard, portable recipe called (and a throwing one is not fatal), environment overrides (exit code 7 vs 3)
  - shell start (detached), missing exe (Failed), throwing handlers, heartbeat
  - recovery: dead pid recorded with heartbeat as end, pid reused by another exe not re-attached, live process re-attached and recorded, corrupt file
- **Interactive** (`-trait "Category=Interactive"`), run three times, all passed. Last run, mGBA test:
  - window found after 544 ms (class `Qt51516QWindowIcon`, minimized)
  - `Take` returned true after 73 ms, and `GetParent(game) == host`
  - responsive (9 answers in a row) after 1.56 s
  - `Place` to 784x561 matched the client size exactly
  - `MeasureBars` gave (21, 0), the same 21 px menu bar MonHub hard-coded for mGBA
  - a screen capture of the host showed the 240p Test Suite title screen drawn inside it (93 % non-black pixels; saved as %USERPROFILE%\AppData\Local\Temp\RetroHub-embed-capture.png)
  - `Release` left the window with no parent and no WS_CHILD
  - `RequestCloseAsync` returned true after 289 ms, exit code 0, one session recorded
  - the own-window take/place/release test also passed
  - mGBA's title reads "(BIOS)": the ROM's header title bytes (0xA0–0xAB) are all zero, and the title showed ~62 frames/s while running.
- **Full unfiltered run of the whole test project:** 64/64 passed, 26 s.
- **Read-only rule:** mGBA in D:\RetroHub has a portable.ini, so running it there would write its config into D:\RetroHub. The test therefore copies the mGBA folder and the ROM to %TEMP% and runs the copy. Afterwards the D:\RetroHub mGBA folder is unchanged, and no mGBA or ping processes are left.

## Known limitations
- **Non-off-thread profiles:** with `OffUiThread = false`, window calls run on the caller's thread, so a hung emulator of that type can still block the UI (same as MonHub). For these profiles `IsResponsive` uses `IsHungAppWindow` instead of the WM_NULL check.
- **No DPI handling:** the embedder doesn't handle mixed DPI between RetroHub and the emulator; everything is in physical pixels.
- **cmd.exe quoting:** cmd.exe has its own quote rules and ignores the MSVCRT ones. This only matters for plans that launch cmd/.bat files.
- **Solution build:** fails only in `RetroHub.App` (CS5001, no `Main`), which is still an empty scaffold owned by the UI work. RetroHub.Core and the tests build with 0 warnings and 0 errors.

## Runtime module review (worktree `D:\RetroHub-wt\runtime`, branch `feat/runtime`, commit `b534735`)

I found and fixed five real defects; each one failed a new test before the fix and passes after it. The full test project passes (69/69, including the Interactive tests).

### Confirmed defects (file:line as in the implementer's commit `f4308bc`) and fixes

1. **An embedded emulator could never be closed politely** (`GameSession.cs:322`). `RequestCloseAsync` only sent WM_CLOSE to windows found by EnumWindows. An embedded window is a child of the host, so EnumWindows doesn't list it. The close request timed out and the UI's only option left was `Kill()`, so the emulator never got a chance to save.
   - Proof: new test `Mgba_RequestCloseWhileEmbedded_ClosesIt`. Before the fix, 0 windows were found while embedded and it returned False after 15013 ms. After the fix it returns True after about 800–930 ms, with exit code 0 and the session not killed.
   - Fix: a new `internal static WindowEmbedder.EmbeddedWindows()`. `RequestCloseAsync` now also sends WM_CLOSE to embedded windows that belong to the session's processes.

2. **Taking the same window a second time broke Release** (`WindowEmbedder.cs:172`). On the second `Take`, `_own` was overwritten with the already-stripped child styles. `Release` then left a frameless WS_CHILD window on the desktop.
   - Proof: `TakingTheSameWindowTwice_KeepsTheOriginalStylesForRelease`. Before the fix, `GetParent` returned the desktop window (65548) instead of 0.
   - Fix: the saved styles are now tied to the window they came from, and a re-take keeps the original ones.

3. **Release or Dispose during a still-running Take left the window stuck in the host** (`WindowEmbedder.cs:196`, `:402-404`, `:511`). With a busy emulator, `Take` returns after 300 ms while the take is still running on the worker. `Release` saw "not attached" and returned. The take then finished and parented the window anyway. Destroying the host afterwards would destroy the emulator's window.
   - Proof: `ReleaseWhileTakeIsStillRunning_GivesTheWindowBack` and `DisposeWhileTakeIsStillRunning_GivesTheWindowBack`, using a sleeping window thread. Both failed with "window still has parent …".
   - Fix: on the worker path, Release is now queued behind any running take and decides there whether the window is in the host. Take only marks itself attached if it wasn't released in the meantime. Dispose releases whenever a game window is set.

4. **Moving the game to another host kept the old host registered** (`WindowEmbedder.cs:149`). Destroying the old host then pulled the game out of the new one.
   - Proof: an extra assertion in the test from item 2. Without the fix, the game's parent was 0 instead of the new host.
   - Fix: when the host changes, the old host's registration is removed.

5. **`Kill()` on a re-attached session left older child processes running** (`GameSession.cs:343`). Children started before RetroHub restarted are not in the new job.
   - Proof: `ReattachedSession_Kill_AlsoKillsChildrenStartedBeforeTheRestart`. Before the fix: "child ping 8248 survived Kill".
   - Fix: for recovered sessions, Kill first runs `Process.Kill(entireProcessTree: true)`, then terminates the job.

I checked the rest and found it correct: the struct layouts (STARTUPINFOW, INPUT, the job accounting info), the constants, the quoting rules, the environment block, the job without kill-on-close, the exit tracking and event order, the session.json handling and recovery, and the German messages.

### Contract and packages
- No contract files changed.
- No NuGet packages added.

### Still open (not fixed)
- **`dotnet test` doesn't work.** It fails with "Testing with VSTest target is no longer supported by Microsoft.Testing.Platform on .NET 10 SDK". The fix is a repo-level `global.json` opting into the new test runner, which isn't my file. I ran the xUnit v3 exe directly instead.
- **The host is gray, not black.** SS_BLACKRECT, which the brief specifies, paints in the system frame colour, and that is gray on Windows 11. You see it in the capture wherever the game doesn't fill the host. It's cosmetic.
- **`DestroyHostWindow` can still freeze the UI with a hung emulator.** If Release times out, the fallback that takes the window back out of the host runs on the UI thread. Against an emulator that is fully hung, those calls could block the UI.
- **The App project doesn't build.** `RetroHub.App` fails with CS5001 (no `Main`); that's the UI scaffold, outside this module.

### Commands run and real results
- `dotnet build src/RetroHub.Core` and `dotnet build tests/RetroHub.Core.Tests`: 0 warnings, 0 errors.
- `dotnet test tests/RetroHub.Core.Tests --no-build`: fails with the VSTest error above.
- Test exe at the start, before any changes, non-interactive runtime tests: 62/62 passed.
- New tests against the old code: all failed as described in items 1–5.
- Full unfiltered run on the final code, including Interactive: **69/69 passed** (about 35 s), run twice; the second run was after restoring the original LF line endings.
- Non-interactive runtime tests: 63/63 passed.
- Interactive tests: 6/6 passed. In the original mGBA test:
  - window found after 432 ms; Take returned True after 13 ms
  - responsive (9 answers in a row) after 1555 ms
  - `Place` gave exactly 784x561; `MeasureBars` gave (21, 0)
  - the host capture was 93 % non-black; I looked at it and the 240p Test Suite screen is drawn inside the host
  - `RequestCloseAsync` returned True after 204 ms, exit code 0, one session recorded
- Afterwards no ping or mGBA processes were left and the temp copies were deleted. Nothing under `D:\RetroHub` or `<MonHub-Ordner>` was changed; the mGBA tests run a copy in %TEMP%.

### Files
- `D:\RetroHub-wt\runtime\src\RetroHub.Core\Emulation\GameSession.cs`
- `D:\RetroHub-wt\runtime\src\RetroHub.Core\Emulation\Windows\WindowEmbedder.cs`
- `D:\RetroHub-wt\runtime\tests\RetroHub.Core.Tests\Emulation\Runtime\EmbedderRegressionTests.cs` (new)
- `D:\RetroHub-wt\runtime\tests\RetroHub.Core.Tests\Emulation\Runtime\EmbeddingSmokeTests.cs`
- `D:\RetroHub-wt\runtime\tests\RetroHub.Core.Tests\Emulation\Runtime\RecoveryTests.cs`
- `D:\RetroHub-wt\runtime\tests\RetroHub.Core.Tests\Emulation\Runtime\RuntimeTestSupport.cs` (added `TestWindowThread.Post`)
- Screen capture: `%USERPROFILE%\AppData\Local\Temp\RetroHub-embed-capture.png`

