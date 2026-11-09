# RetroHub.Input: final report (branch feat/input, commit 5264416)

The gamepad module is done and all 70 Input tests pass on this machine. **No controller was detected**: SDL3 and XInput both reported 0 pads, so everything that needs a real button press is untested (details under "What was not tested").

One thing outside my files needs fixing: the required `dotnet test` command fails before running anything. The .NET 10 SDK no longer supports VSTest, and the repo has no `global.json` opting into Microsoft.Testing.Platform (error: "Testing with VSTest target is no longer supported by Microsoft.Testing.Platform on .NET 10 SDK and later"). Adding a root `global.json` with `{"test":{"runner":"Microsoft.Testing.Platform"}}` should fix it, but I didn't touch that file. I ran the xUnit v3 test executable directly instead.

## Files created (all in D:\RetroHub-wt\input)
- `src/RetroHub.Input/InputTypes.cs`: `UiAction`, `PadStyle`, `GamepadInfo`, `PadButtons`, `PadSnapshot`, `PadInput`
- `src/RetroHub.Input/InputTranslator.cs`: `InputTranslator` and `InputTranslatorOptions`
- `src/RetroHub.Input/ExitCombo.cs`: `ExitComboDetector` (pure logic) and `ExitComboWatcher`
- `src/RetroHub.Input/GamepadService.cs`: `GamepadService` and `GamepadBackendPreference`
- `src/RetroHub.Input/Backends.cs`: internal `SdlBackend` and `XInputBackend`
- `src/RetroHub.Input/Native/Sdl3.cs`: my own `[LibraryImport("SDL3")]` bindings (cdecl, C bool marshalled as 1 byte)
- `src/RetroHub.Input/Native/XInput.cs`: loads xinput1_4.dll, else xinput9_1_0.dll; uses ordinal 100 for the Guide button and `XInputSetState` for rumble
- `src/RetroHub.Input/Native/NativeLibraryResolver.cs`: fallback lookup for SDL3.dll if normal loading fails
- `src/RetroHub.Input/Native/PreciseSleeper.cs`: precise sleep timer so polling actually reaches ~120 Hz (without changing the system-wide timer)
- Modified `src/RetroHub.Input/RetroHub.Input.csproj`: added a ProjectReference to RetroHub.Core (needed for `Log`) and a comment on how SDL3.dll is delivered
- Tests in `tests/RetroHub.Core.Tests/Input/`: `InputTranslatorTests.cs`, `ExitComboDetectorTests.cs`, `Sdl3NativeTests.cs` (also holds the XInput tests), `GamepadServiceTests.cs`

## Public API
- `enum UiAction { Up, Down, Left, Right, Accept, Back, Menu, View, Search, Favorite, PageLeft, PageRight, TriggerLeft, TriggerRight, Home }`
- `enum PadStyle { Xbox, PlayStation, Nintendo, Generic }`
- `sealed record GamepadInfo(int InstanceId, string Name, PadStyle Style)`. `InstanceId` is the SDL id, or the XInput slot 0–3.
- `[Flags] enum PadButtons : uint`: bit n is SDL button n (South … DPadRight).
- `readonly record struct PadSnapshot(PadButtons Buttons, float LeftX=0, float LeftY=0, float LeftTrigger=0, float RightTrigger=0)`. Positive Y means down.
- `readonly record struct PadInput(int PadId, PadSnapshot State)`
- `enum GamepadBackendPreference { Auto, Sdl3Only, XInputOnly }`
- **`GamepadService : IDisposable`**
  - Constructors: `GamepadService()` and `GamepadService(GamepadBackendPreference preference, InputTranslatorOptions? options = null)`
  - Methods: `Start()`, `Stop()`, `Suspend()`, `Resume()`, `Rumble(float low, float high, int durationMs, bool allPads=false)`, `Dispose()`
  - Properties: `string Backend` ("SDL3", "XInput" or "None"; constants `BackendSdl3`, `BackendXInput`, `BackendNone`), `IReadOnlyList<GamepadInfo> Pads`, `PadStyle LastUsedStyle`, `bool SwapAcceptBack`, `bool IsRunning`, `bool IsSuspended`
  - Events: `Action<UiAction>? ActionTriggered`, `Action<GamepadInfo>? Connected`, `Action<GamepadInfo>? Disconnected`, `Action<PadStyle>? LastUsedStyleChanged`
  - **The UI must know:** all events fire on the "RetroHub input" thread, so handlers must hand off to the UI thread. Handler exceptions are caught and logged.
  - `Start()` and `Resume()` wait (at most 5 s; about 0.1–0.5 s in practice) until the backend is ready, so `Backend` and `Pads` are valid when they return.
  - `Suspend()` closes all pads and shuts down SDL's gamepad subsystem; the tests confirm SDL reports nothing initialised afterwards. Calling the control methods from inside a handler only queues the request and never deadlocks.
  - Suspend and Resume don't fire Connected/Disconnected for pads that are still there afterwards; a pad that disappears during a game gets a Disconnected event about 2 s after Resume.
- **`InputTranslator`**
  - Constructor: `InputTranslator(InputTranslatorOptions? options = null)`
  - `IReadOnlyList<UiAction> Update(IReadOnlyList<PadInput> pads, long timestampMs)`. A pad missing from the list counts as disconnected.
  - `Reset()`, `SwapAcceptBack`, `LastActivePadId`
  - Defaults:
    - Stick: counts as pressed from 0.5, released below 0.35. On a diagonal only the stronger axis counts; it switches only when the other is 1.25× stronger.
    - Triggers: pressed above 0.6, released below 0.4.
    - Key repeat: first after 400 ms, then every 80 ms, every 50 ms once held for 2 s.
- **`ExitComboDetector`** (pure logic): `ExitComboDetector(double holdSeconds = 1.5)`, `HoldSeconds` (limited to 0.1–10 s), `bool Update(IReadOnlyList<PadInput>, long timestampMs)`, `Reset()`. Back+Start must be on the same pad.
- **`ExitComboWatcher : IDisposable`**
  - Constructor: `ExitComboWatcher(double holdSeconds = 1.5)`
  - Members: `event Action? Triggered` (fires on the watcher's own thread), `HoldSeconds`, `IsAvailable`, `GuideSupported`, `IsRunning`, `Start()`, `Stop()`, `Dispose()`
  - Polls XInput only, at about 15 Hz, and never touches SDL.

## SDL3.dll delivery
The SDL3-CS.Windows 3.4.20 package ships `runtimes/win-{x64,x86,arm64}/native/SDL3.dll`.
- **Test project (no RuntimeIdentifier):** the DLL lands in `bin/Debug/net10.0/runtimes/win-x64/native/SDL3.dll` and .NET finds it through the deps.json. Verified.
- **App (win-x64):** I couldn't build the App on this branch because it has no `Main` yet. A scratch win-x64 console project referencing RetroHub.Input got `SDL3.dll` next to the exe in both build and publish output, and reported `Backend=SDL3`.
- `NativeLibraryResolver` also checks both locations in case normal loading fails.

## Contract changes and packages
- No contract files were changed.
- No packages were added.

## Tests run (real results)
- Command: `tests/RetroHub.Core.Tests/bin/Debug/net10.0/RetroHub.Core.Tests.exe -namespace RetroHub.Core.Tests.Input`
- Result: **Total 70, Failed 0, Skipped 0**, about 5.9 s, run 4 times with the same result.
- Running the whole test project also gave 70/70; this worktree only contains my tests.
- Build: 0 warnings, 0 errors.
- What they cover:
  - Translator: repeat timing at 1 ms and 8 ms polling, no burst of repeats after a stall, stick hysteresis, dominant axis, triggers, buttons held at connect or after Reset ignored until released, swap, multiple pads merged, disconnect/reconnect, NaN values.
  - Exit combo logic: hold time, fires once per hold, Guide, held-at-start guard, same-pad requirement, configurable hold time, pad removal.
  - SDL constants: every button, axis and gamepad-type value checked against the strings in the shipped DLL (SDL 3.4.20, including GameCube = 11), plus init flags and bool marshalling.
  - Smoke tests on this machine, no controller:
    - SDL3: `Backend == "SDL3"` and disposes cleanly; measured **116 Hz** polling.
    - Suspend/Resume cycles shut down and restore SDL's gamepad subsystem.
    - Calling the control methods from a handler on the input thread doesn't deadlock, and handler exceptions are contained.
    - Start/Stop/Start and concurrent calls are safe.
    - XInput-only mode reports `Backend == "XInput"` and never loads SDL.
    - The exit combo watcher starts and stops; xinput1_4.dll loaded and the Guide button is readable.

## What was not tested
These need a physical controller:
- SDL hot-plug events and real button/stick reading
- the style a real pad reports (Xbox / PlayStation / Nintendo)
- rumble
- the XInput exit combo firing
- whether SDL picks up raw-input pads without a window. I set the `SDL_JOYSTICK_THREAD=1` hint for this; it should work but is unverified.

## Known limitations
- The exit combo only sees XInput (Xbox-compatible) pads. PlayStation and Nintendo pads must use the emulator's own exit hotkey.
- The XInput fallback names pads "Xbox-Controller N" and always reports the Xbox style.
- `Start()`/`Resume()` block the calling thread briefly (normally under 0.5 s, at most 5 s).
- The XInput Guide button relies on the undocumented ordinal 100. Without it, only Back+Start works.

# RetroHub.Input review: fixes on branch feat/input, commit ac91972

I found and fixed four real defects, all in `GamepadService` and all reproduced with failing tests first. I also added tests that run the real SDL3 code path with SDL's own virtual gamepads, so hot-plug and button/stick reading are now tested without a controller. **No controller was detected on this machine** (SDL3 reported 0 pads). The required `dotnet test` command still fails before running anything; that needs a file outside my ownership (see the end).

## Confirmed defects (line numbers refer to the implementer's commit 5264416)

1. **A pad's identity changed after every game** (`GamepadService.cs:405-406, 415`).
   - **Problem:** SDL gives a pad a new id each time it is re-opened; I proved this with a virtual pad. After Suspend/Resume, `Pads` showed the new id and a later unplug raised `Disconnected` with an id the UI never got in `Connected`. A UI that tracks pads by the record it received could never remove the pad.
   - **Failing test:** `Pad_identity_is_stable_across_suspend_and_resume` expected id 101, got 102.
   - **Fix:** the service now keeps the record it announced in `Connected` and uses it in `Pads` and `Disconnected`, even after a Resume. The internal id is only used for input and rumble. I updated the documentation comment on `GamepadInfo` to say so.

2. **A pad lost during back-to-back games never got a Disconnected event** (`GamepadService.cs:300`).
   - **Problem:** a second Suspend within the 2-second grace period after Resume overwrote the list of pads still waiting to reappear.
   - **Failing test:** `Pad_lost_during_back_to_back_games_is_still_reported`.
   - **Fix:** the list is now added to instead of replaced. The "gone while suspended" check moved into the main loop, so it also runs if no backend could be re-initialised.

3. **`Start()` called from an event handler froze the UI thread for 5 s** (`GamepadService.cs:120`).
   - **Problem:** if the UI thread was inside Suspend/Resume at that moment, both threads waited on each other until the 5-second timeout.
   - **Failing test:** `Start_from_a_handler_does_not_stall_a_concurrent_suspend`, where Suspend took 5.001 s.

4. **`Stop()` followed by `Start()` in a handler left the service stopped** (`GamepadService.cs:134`).
   - **Failing test:** `Stop_then_start_from_a_handler_keeps_the_service_running`.
   - **Fix for 3 and 4:** when called from the input thread, `Start()` no longer takes the lock; it only cancels a pending stop request. I also removed an unreachable check in `SetSuspended`.

**Minor, not tested (needs a controller that rumbles):** after Suspend the remembered "last used pad" for rumble pointed to an id that no longer exists. It is now reset, so rumble goes to all pads until the next button press.

## Suspected, but not confirmed
- **Held buttons firing right after a pad connects:** I suspected SDL reports an all-zero state for a pad in the poll that opens it, which would make a held button fire. A test with a virtual pad showed the correct state in that very first poll, so I made no change. Real HIDAPI pads (PlayStation, Switch) might still deliver their first state later; that can only be checked with hardware.
- **Exit combo state shared between threads:** `ExitComboWatcher` could in theory have two threads using the same detector after a timed-out Stop. I found no realistic way to trigger it, so I left it.
- The bindings, constants, C bool marshalling, XInput handling (including the Guide button and Y inversion), the input translator and the exit-combo logic all look correct to me.

## Added
- **`src/RetroHub.Input/Native/SdlVirtualGamepad.cs`** (internal, used only by tests): creates SDL virtual gamepads with my own bindings for the virtual-joystick functions. The pad's state is fed in from SDL's own update, the same way a physical pad's would be.
- **`tests/RetroHub.Core.Tests/Input/SdlVirtualPadTests.cs`:**
  - full `GamepadService` run over SDL3 with a virtual DualSense: hot-plug detected, name, PlayStation style, a button held at connect does not fire, Accept/Down/TriggerRight fire, `LastUsedStyle` is PlayStation, unplug raises `Disconnected` with the same record
  - backend-level reading of buttons, stick, triggers and hot-unplug
  - SDL hands out new ids after re-initialisation
  - descriptor size matches SDL's (136 bytes)
- **`tests/RetroHub.Core.Tests/Input/GamepadServiceLogicTests.cs`:** a fake backend (through a new internal constructor) plus the five service tests above. One of them checks that Back+Start still held through a Resume does not fire.
- `Backends.cs`: one internal `OpenPads` property for the tests.
- No contract files changed, no packages added.

## Commands run (real results)
- `dotnet build src/RetroHub.Input -nologo -v q`: 0 errors. Building the test project: 0 warnings, 0 errors.
- New tests before the fixes: 4 of 5 logic tests failed (details above). After the fixes: 5/5 passed, and the virtual-pad tests passed 5/5.
- **Full test project via the test executable `RetroHub.Core.Tests.exe`, run 3 times: Total 80, Failed 0, Skipped 0** (about 11 s each). That is the 70 existing tests plus 10 new ones.
- `dotnet test tests/RetroHub.Core.Tests -nologo` fails with: "Testing with VSTest target is no longer supported by Microsoft.Testing.Platform on .NET 10 SDK and later". The fix is a root `global.json` with `{"test":{"runner":"Microsoft.Testing.Platform"}}`, which is outside my files and has not been added.
- **SDL3.dll for the App:** a scratch console project with `RuntimeIdentifier` win-x64 referencing RetroHub.Input got `SDL3.dll` next to the exe and reported `Backend=SDL3 Pads=0`. The App itself has no `Main` yet, so I could not build it.

## Still untested
These need a physical controller: real HIDAPI/XInput pads through SDL, whether SDL picks up raw-input pads without a window, the style a real pad reports, rumble, the XInput exit combo actually firing, and Guide via ordinal 100. As before, the exit combo only sees XInput (Xbox-compatible) pads.

