# RetroHub: technical research report (Windows, .NET 10, Avalonia)

## Cross-cutting findings that change the plan

- **Avalonia 12 is stable now (12.0 shipped around April 2026, 12.0.x patches since).** Its breaking changes: .NET 8+ only, SkiaSharp 3, compiled bindings on by default, a new clipboard API, and new window decorations. **ItemsRepeater is no longer supported as of v12**, and its separate repo is being archived. Staying on 11.3 to share code with MonHub is reasonable, but don't build core UI on ItemsRepeater. Plan a move to 12 within about 6 months.
- **Overall architecture recommendation:**
  - **Phase 1:** launch external emulators borderless-fullscreen (topic 2B).
  - **Phase 2 (optional):** your own libretro "player" as a separate exe built on SDL3, launched like any other emulator. Don't host cores inside the Avalonia process.
  - **One native dependency for everything:** SDL3 covers gamepad, audio and the player's video.

---

## 1. Hosting libretro cores in .NET

### API surface (from libretro.h)

**Core exports to bind:**
- Lifecycle: `retro_api_version`, `retro_get_system_info`, `retro_get_system_av_info`, `retro_init`, `retro_deinit`, `retro_load_game`, `retro_unload_game`, `retro_run`, `retro_reset`
- Callback setters (call them before `retro_init`; `retro_set_environment` must come first): `retro_set_environment`, `retro_set_video_refresh`, `retro_set_audio_sample`, `retro_set_audio_sample_batch`, `retro_set_input_poll`, `retro_set_input_state`
- Save states: `retro_serialize_size`, `retro_serialize`, `retro_unserialize`
- SRAM: `retro_get_memory_data` / `retro_get_memory_size` with `RETRO_MEMORY_SAVE_RAM` = 0. Flush the .srm on unload and periodically.

**Loading the DLL:**
- Use `NativeLibrary.Load` + `GetExport`, called through `delegate* unmanaged[Cdecl]<...>` function pointers.
- Implement callbacks as `[UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]` static methods.
- Don't use marshalled delegates (GC lifetime bugs).
- Callbacks carry no userdata pointer, so the host is effectively one static instance per process.

**Marshalling pitfalls:**
- C `bool` is 1 byte. Use `byte` in blittable structs, or `[assembly: DisableRuntimeMarshalling]`.
- `retro_system_info` contains `need_fullpath` and `block_extract`.
- The environment callback returns 1-byte bool.
- Strings you return (system/save dir) must stay valid. Allocate once with `Marshal.StringToCoTaskMemUTF8` and keep them.
- If `need_fullpath == false`, load the ROM into unmanaged memory and keep it until `retro_unload_game`.
- Wrap every callback in try/catch. An exception crossing the native boundary kills the process.
- Threading: every core call and callback must happen on one dedicated emulation thread. Callbacks fire synchronously inside `retro_run`.

**Logging (variadic):**
- `GET_LOG_INTERFACE` (27) expects a variadic `printf`-style callback, which C# can't declare.
- MVP: return false, and cores fall back to stderr.
- Advanced (win-x64 only): declare a callback with fixed `nint` slots and forward them to msvcrt `_snprintf`.

### Minimum environment commands (return false for everything else)

| Command | ID |
|---|---|
| GET_CAN_DUPE | 3 |
| SET_MESSAGE | 6 |
| SHUTDOWN | 7 |
| SET_PERFORMANCE_LEVEL | 8 |
| GET_SYSTEM_DIRECTORY | 9 |
| SET_PIXEL_FORMAT | 10 |
| SET_INPUT_DESCRIPTORS | 11 |
| GET_VARIABLE | 15 |
| SET_VARIABLES | 16 |
| GET_VARIABLE_UPDATE | 17 |
| SET_SUPPORT_NO_GAME | 18 |
| GET_SAVE_DIRECTORY | 31 |
| SET_SYSTEM_AV_INFO | 32 |
| SET_GEOMETRY | 37 |
| GET_LANGUAGE | 39 |
| GET_INPUT_BITMASKS | 51 (with the EXPERIMENTAL flag 0x10000 masked off) |
| GET_CORE_OPTIONS_VERSION | 52: return 0 or false so cores fall back to the simple v0 `SET_VARIABLES` options |

Also mask `RETRO_ENVIRONMENT_EXPERIMENTAL` before the switch.

### Video (software cores)

- Pixel formats: 0RGB1555 (the default), XRGB8888 or RGB565. Respect `pitch`.
- `data == NULL` means a duplicated frame (with CAN_DUPE). `(void*)-1` means a hardware frame.
- Convert to BGRA32.
- **In Avalonia:**
  - `WriteableBitmap(PixelSize, Vector(96,96), PixelFormat.Bgra8888, AlphaFormat.Opaque)`, then `using var fb = wb.Lock()`, copy rows, then `image.InvalidateVisual()` on the UI thread.
  - Set `RenderOptions.BitmapInterpolationMode="None"` for nearest-neighbour scaling.
  - Throughput is trivial at GBA/SNES sizes.
  - The weak point is **frame pacing**: Avalonia's compositor tick isn't synced to the core's FPS (GBA 59.73, SNES NTSC 60.10), so expect occasional judder.
  - I'm not certain how tightly Avalonia's Win32 render loop follows vsync; assume it doesn't.
- **Recommended instead:** a separate `RetroHub.Player.exe` with an SDL3 window, `SDL_Renderer` with vsync, and a streaming texture (`SDL_UpdateTexture`). Benefits:
  - Correct vsync and presentation
  - Crash isolation: core segfaults don't kill the hub
  - Clean unload of cores with sloppy global state (many don't survive `FreeLibrary` + reload)
  - The hub treats it like any other emulator
- Keep the WriteableBitmap path only for in-hub live previews, if you want them at all.

### Hardware-rendered cores (out of scope for MVP)

- They need `SET_HW_RENDER` (14) with `retro_hw_render_callback`: `context_type`, `context_reset`, `get_current_framebuffer` (your FBO), `get_proc_address`.
- Avalonia on Windows renders through ANGLE (GLES over D3D11), so `OpenGlControlBase` gives you **GLES**. Cores that want desktop GL core profiles (mupen64plus-next, flycast, ppsspp-core) won't run there.
- You'd need your own WGL context (Silk.NET.OpenGL / SDL3 GL window) plus a texture-sharing or child-window scheme.
- Vulkan needs `retro_hw_render_interface_vulkan` and is harder still.
- Verdict: for 3D systems, launch standalone emulators or RetroArch.

### Audio

- `audio_sample_batch(int16* stereo interleaved, frames)` arrives at `av_info.timing.sample_rate` (e.g. snes9x around 32 kHz). You must resample to the device rate, and ideally use **dynamic rate control** (±0.5%) so audio pacing and video vsync don't drift (otherwise you get crackle or drops).

| Option | Packages | Notes |
|---|---|---|
| **SDL3 audio (recommended)** | same SDL3 binding as for input | `SDL_OpenAudioDeviceStream` + `SDL_PutAudioStreamData`. SDL resamples internally. `SDL_SetAudioStreamFrequencyRatio` gives you DRC almost for free. Audio-driven pacing: block the emulation thread while `SDL_GetAudioStreamQueued` is above target. |
| OpenAL Soft | `Silk.NET.OpenAL` 2.22/2.23 + `Silk.NET.OpenAL.Soft.Native` (2.23 bundles OpenAL Soft 1.24.3) | Accepts any buffer frequency (resamples internally). Queue/unqueue buffers. Portable. |
| NAudio | `NAudio` 2.2.1 | `WasapiOut` + `BufferedWaveProvider` + `WdlResamplingSampleProvider`. Windows-only. DRC is manual. Fine, but it's an extra dependency if you already ship SDL3. |

### Input

- `input_state(port, device, index, id)` returns int16.
- `RETRO_DEVICE_JOYPAD` (1) ids: B0, Y1, SELECT2, START3, UP4, DOWN5, LEFT6, RIGHT7, A8, X9, L10, R11, L2 12, R2 13, L3 14, R3 15. `id 256` = bitmask (when GET_INPUT_BITMASKS is supported).
- `RETRO_DEVICE_ANALOG` = 5.
- Sample SDL gamepad state inside `input_poll`.

### Reference projects

- **SK.Libretro** (C#, Unity-oriented; last push Oct 2024). The best C# reference for marshalling and env handling.
- **RetroUnity** (C#).
- **BizHawk** (C#, has a libretro host).
- **nanoarch** (minimal C frontend, about 600 LOC). The clearest reference for the call sequence.
- The canonical `libretro.h` in libretro-common.
- RetroArch's `runloop.c` / `retroarch.c` for edge cases.

### Licensing risk (important)

- snes9x and Genesis Plus GX are **non-commercial** licences.
- mGBA is MPL-2.0. pcsx_rearmed, fceumm and gambatte are GPLv2; Mesen is GPLv3.
- Bundling GPL cores and loading them in-process from a closed-source app is legally grey.
- Safest: don't bundle cores. Let the user download them from `buildbot.libretro.com/nightly/windows/x86_64/latest/<core>_libretro.dll.zip`, or point at an existing RetroArch `cores\` folder.

### MVP scope (2D software cores, separate SDL3 player exe)

- Included: loader, env subset, video with aspect/integer scale, SDL audio with DRC, gamepad mapping, SRAM, save-state slots, exit combo, minimal OSD.
- Size: roughly 2–3.5k LOC C#.
- Time: about **3–5 weeks** for one experienced dev, plus 2–3 weeks hardening across about 8 cores (mgba, snes9x/bsnes, genesis_plus_gx/picodrive, mesen/fceumm, gambatte/sameboy, beetle-pce-fast, pcsx_rearmed in software mode).
- Excluded: shaders, rewind, runahead, netplay, RetroAchievements (rcheevos), HW cores. Each is weeks.
- **Honest alternative:** just launch RetroArch with `-L`. You get the same coverage plus all of those features. Your own player only pays off for a seamless, branded UX.

---

## 2. Embedding external emulator windows vs. launching fullscreen

### 2A. Embedding: technique and why it's fragile

**Technique:**
1. Subclass `NativeControlHost` and override `CreateNativeControlCore(IPlatformHandle parent)`. Create your own container: `CreateWindowEx(WS_CHILD|WS_CLIPCHILDREN|WS_VISIBLE)` and return `new PlatformHandle(hwnd, "HWND")`.
2. Find the emulator's top-level window: `EnumWindows` + `GetWindowThreadProcessId` == pid, visible, has a caption.
3. Call `SetParent(emuHwnd, container)`.
4. Change styles: `SetWindowLongPtr(GWL_STYLE, (style & ~(WS_POPUP|WS_CAPTION|WS_THICKFRAME|WS_SYSMENU|WS_MINIMIZEBOX|WS_MAXIMIZEBOX)) | WS_CHILD)`, also strip `WS_EX_APPWINDOW` from the extended style.
5. `SetWindowPos(..., SWP_FRAMECHANGED|SWP_NOZORDER)`, and resize it in step with the host's Bounds.

**Known problems:**
- **Cross-process parent/child attaches the input queues** (implicit `AttachThreadInput`). If either process stops pumping messages (shader compile, debugger, hang), **both UIs freeze**. Raymond Chen calls this "technically legal, practically a nightmare".
- **Emulators recreate windows:**
  - Dolphin creates a new render window per boot (the "Render to Main Window" option is internal to Dolphin, not an external HWND).
  - PCSX2 Qt and DuckStation Qt create a separate display widget/window when a game starts and recreate it on fullscreen toggle or renderer change.
  - RetroArch recreates its window when the video driver re-initialises.
  - Result: you have to re-hook continuously with timing races.
- **Swapchains:**
  - Exclusive fullscreen is impossible on a child window.
  - Flip-model DXGI (`FLIP_DISCARD`) on a child HWND works on Win10+, but loses independent flip/MPO (more latency, worse frame pacing). Vulkan surfaces survive but are subject to the recreation issue above.
- **Airspace:** native child HWNDs always draw over Avalonia content. No Avalonia overlays on the game; you'd need a separate topmost layered window.
- **Lifetime:** `NativeControlHost` destroys its native control when it leaves the visual tree (e.g. a tab switch). Destroying the parent HWND sends WM_DESTROY to the cross-process child, so the emulator crashes or quits.
- **Focus and input:**
  - The foreground window is the hub's top-level, so emulators that gate input on focus see themselves as in the background: SDL's `ALLOW_BACKGROUND_EVENTS` defaults to 0, Dolphin's "Background Input" is off by default, and PCSX2/DuckStation have pause-on-focus-loss options.
  - Keyboard needs explicit `SetFocus` across the attached threads.
- **DPI:** the hub is PerMonitorV2. Mixed-DPI hosting (`SetThreadDpiHostingBehavior`) is per-process only, so a cross-process child with different awareness gets bitmap-scaled or sized wrongly on mixed-DPI setups.

**Native "render into this HWND" support:**

| Emulator | Support |
|---|---|
| mpv | Yes, `--wid=<HWND>` |
| MAME (Windows OSD) | `-attach_window <title/handle>` exists, I'm fairly but not fully sure; check `mame -showusage` |
| libretro | Effectively yes, by being the host yourself |
| RetroArch | No documented Windows window-handle flag that I know of (uncertain) |
| Dolphin Qt | No |
| PCSX2 Qt | No |
| DuckStation | No |
| PPSSPP | No |
| Cemu | No |
| xemu | No |
| Azahar | No |
| melonDS | No |
| Flycast | No |
| Redream | No |

**Verdict: don't embed.** Only mpv-style players and your own libretro host embed cleanly.

### 2B. Recommended: launch borderless-fullscreen and hand off

1. **Before launch:** show a full-window transition overlay in the hub (fade plus box art). Pause hub animations, image loading and polling.
2. **Process:**
   - `ProcessStartInfo` with `UseShellExecute=false`, `WorkingDirectory` = emulator dir (required for MAME, xemu, Xenia), and arguments via **`ArgumentList`** (correct quoting).
   - Put the process in a **Job Object**: `CreateJobObject` + `AssignProcessToJobObject` + `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`. Track it through `QueryInformationJobObject(JobObjectBasicProcessIdList)`, so launchers that spawn a child and exit are still tracked and nothing is orphaned.
   - Alternatively `JOBOBJECT_ASSOCIATE_COMPLETION_PORT` gives a notification when the job becomes empty.
3. **Window detection:**
   - Poll `EnumWindows` for a visible window from any job pid whose rect covers the monitor (or just its first visible window), with a 10–20 s timeout.
   - Then hide the hub. Hiding is cleaner than minimising for a big-picture app; a minimised window gives a taskbar flash on restore.
4. **Monitor:**
   - Get the hub's screen with `Screens.ScreenFromWindow`.
   - Only a few emulators accept a monitor on the command line (RetroArch: `video_monitor_index` via `--appendconfig`). Most fullscreen on the monitor the window opens on, or the primary.
   - MVP: support the primary monitor; on other monitors, move the window with `SetWindowPos` before fullscreen. That's racy, so treat it as best effort.
5. **While running:**
   - Lower the hub's priority (`PriorityClass = BelowNormal`).
   - Stop the gamepad UI (see topic 3), but optionally keep an **exit combo** monitor (e.g. hold Select+Start for 2 s). It sends `WM_CLOSE` to the emulator's main window, then `Process.Kill(entireProcessTree)` / job termination after a timeout.
6. **On exit:**
   - Wait on `WaitForExitAsync` or the job-empty notification, then show and activate the hub.
   - `SetForegroundWindow` can be refused by Windows' foreground-lock rules. Gamepad input doesn't count as user input for those rules.
   - Workarounds: `AttachThreadInput` to the current foreground thread, or a synthetic Alt tap (`SendInput VK_MENU`) right before `SetForegroundWindow`. Both are widely used hacks and work in practice.
7. Disable "confirm on exit" in emulators you configure:
   - Dolphin: `-C Dolphin.Interface.ConfirmStop=False`
   - DuckStation/PCSX2: "Confirm Shutdown" off, or batch mode won't close cleanly.

---

## 3. Gamepad input for the launcher UI

| Option | DS4/DualSense | Switch Pro / 8BitDo | Background behaviour | Distribution | Portability |
|---|---|---|---|---|---|
| XInput P/Invoke (`xinput1_4.dll`: `XInputGetState`, `XInputSetState`) | **No** (needs DS4Windows or Steam Input) | No | Works regardless of focus; `XInputEnable(false)` mutes it | In the OS | Windows only |
| Windows.Gaming.Input (TFM `net10.0-windows10.0.19041.0`, CsWinRT) | Only as `RawGameController` with **no standard mapping** | Partly | Focus-gated delivery for desktop apps is reported, and the gamepad list fills in asynchronously (uncertain details) | In the OS | Windows only |
| **SDL3 (recommended)** | **Yes**, HIDAPI drivers incl. gyro, lightbar, rumble | Yes, plus a built-in mapping DB (gamecontrollerdb) | Gating is controllable by hint; with no SDL windows, events aren't focus-gated | Ship `SDL3.dll` (about 2–3 MB, zlib licence) | Win, Linux, macOS |
| SDL2 (`Silk.NET.SDL` 2.22/2.23) | Yes | Yes | As SDL3 | Native via Silk.NET | Cross-platform, but SDL2 is maintenance-only |

**Recommendation: SDL3.**

**Bindings:**
- `ppy.SDL3-CS` (osu! team, internally driven, lightly maintained; check that the package ID is current), or
- `SDL3-CS` 3.4.x (edwardgushchin; targets SDL 3.4.14, .NET 7–10). Native DLLs come in **`SDL3-CS.Windows`** (`SDL3-CS.Native` is deprecated).
- **Most robust:** write your own ~25 `[LibraryImport("SDL3")]` declarations and ship the official `SDL3.dll` from libsdl-org releases. This avoids binding churn; you only need:
  - `SDL_SetHint`, `SDL_Init(SDL_INIT_GAMEPAD | SDL_INIT_AUDIO)`
  - `SDL_PollEvent` (`SDL_EVENT_GAMEPAD_ADDED` / `_REMOVED` / `_BUTTON_DOWN` / `_AXIS_MOTION`)
  - `SDL_OpenGamepad`, `SDL_CloseGamepad`, `SDL_GetGamepadButton`, `SDL_GetGamepadAxis`, `SDL_GetGamepadType` (for glyphs: Xbox / PS / Nintendo)
  - `SDL_RumbleGamepad`, `SDL_QuitSubSystem`

**Threading:**
- Use one dedicated input thread that calls `SDL_Init` and polls at about 120 Hz.
- Translate to UI actions with key repeat (initial 400 ms, then 80 ms; analog deadzone about 0.5), then `Dispatcher.UIThread.Post`.

**Background behaviour (requirement: the hub must not react while a game runs):**
- Gate in your own code. Act only if no game session is active **and** `GetForegroundWindow()` belongs to the hub process (`Window.IsActive`).
- Set `SDL_HINT_JOYSTICK_ALLOW_BACKGROUND_EVENTS=1` so SDL never silently drops events and you're the only one gating.
- **During a game, call `SDL_QuitSubSystem(SDL_INIT_GAMEPAD)`**, or at least close all gamepads, and re-init on return. Reasons:
  - SDL's HIDAPI driver can switch DS4/DualSense into enhanced-report mode and send lightbar/rumble output reports. That can interfere with emulators reading the pad differently (DirectInput or raw HID), especially over Bluetooth.
  - SDL3 has `SDL_HINT_JOYSTICK_ENHANCED_REPORTS`; set it to `"auto"`/`"0"`. I'm not certain it fully avoids the interference.
  - It removes the CPU wakeups.
- **Exit combo while a game runs:** use XInput polling at 10–20 Hz (Xbox pads), which doesn't touch HID. For PS pads, rely on the emulator's own exit hotkey (RetroArch `input_exit_emulator_btn`, Dolphin/PCSX2/DuckStation hotkeys).
- Steam Input caveat: if Steam is running with Steam Input on, the pads show up as virtual XInput/SDL devices. That's fine, but you may see duplicates. SDL handles this better than XInput.

---

## 4. Emulator command lines (Windows)

Use `ArgumentList`. "Verified" means checked against ES-DE's `es_systems.xml` for Windows or official docs. Everything else is from memory: check it with `--help` per version.

| Emulator (exe) | Launch, fullscreen | Portable mode | Version detection |
|---|---|---|---|
| **RetroArch** (`retroarch.exe`) | `-L cores\mgba_libretro.dll -f "rom"` (verified `-L`). Per-launch overrides: `--appendconfig "override.cfg"` with `video_fullscreen = "true"`, `video_windowed_fullscreen = "true"` (borderless), `video_monitor_index = "N"`. Also `--config <cfg>`, `--verbose`, `--log-file <path>` | The Windows build always uses `retroarch.cfg` in the exe dir (both zip and installer write there) | File version resource present; `--version` exists, but it's a GUI-subsystem exe, so redirect stdout/stderr |
| **Dolphin** (`Dolphin.exe`) | `-b -e "game.rvz"` (verified). `-b` = exit when emulation stops. Fullscreen: `-C Dolphin.Display.Fullscreen=True` (the `-C <System>.<Section>.<Key>=<Value>` syntax is verified; the exact key matches `[Display] Fullscreen` in Dolphin.ini, so likely correct). Also `-C Dolphin.Interface.ConfirmStop=False` | `portable.txt` next to exe creates `.\User\`. `-u <dir>` overrides it for one session. Env `DOLPHIN_EMU_USERPATH` | Version resource; `DolphinTool.exe` (shipped) can read game IDs and metadata for your scraper |
| **PCSX2** 2.x (`pcsx2-qt.exe`; older nightlies `pcsx2-qtx64-avx2.exe`) | `-fullscreen -batch -- "game.iso"` (official docs). `-nogui` hides the main window and implies batch. `-bigpicture` | `-portable` flag, or an empty `portable.ini` / `portable.txt` next to exe (I'm unsure which filename is accepted; test both) | Version resource probably present (uncertain). Fall back to exe SHA-256 or the folder name |
| **DuckStation** (`duckstation-qt-x64-ReleaseLTCG.exe`) | `-batch -fullscreen -- "game.cue"` (ES-DE uses `-batch`). `-nogui` likely exists in recent builds. `-bigpicture` | `portable.txt` next to exe (likely; from the README). `-portable` also seen | Rolling builds; version resource unreliable, so hash or `-version` |
| **PPSSPP** (`PPSSPPWindows64.exe`) | `--fullscreen "game.iso"`. Optionally `--escape-exit` (Esc quits) and `--pause-menu-exit` (from memory) | The zip build is portable by default (`memstick\` next to exe). `installed.txt` switches to Documents\PPSSPP | Version resource present |
| **melonDS** (`melonDS.exe`) | `-f "game.nds"` (verified via ES-DE). `-b always\|never\|auto` boot mode | Unsure about 1.x: it either keeps its config next to the exe or uses %APPDATA% with a portable trigger; test per version | Version resource uncertain |
| **mGBA** (`mGBA.exe`) | `-f "game.gba"` (verified). `-C key=value` override (likely) | `portable.ini` next to exe | Version resource present |
| **Azahar** (`azahar.exe`) | ES-DE launches it with just `"game.3ds"`. Fullscreen: `-f` / `--fullscreen`, inherited from citra-qt (**unverified for Azahar**; check `--help`) | `user\` folder next to exe (Citra convention, likely) | Version resource uncertain |
| **Cemu** (`Cemu.exe`) | `-f -g "game.wua"` (verified). `-mlc <path>` | `portable\` folder next to exe (Cemu 2.x) | Version resource present |
| **xemu** (`xemu.exe`) | `-full-screen -dvd_path "game.iso"` (official docs). `-config_path <xemu.toml>`. Extra args go to QEMU, e.g. `-machine xbox,short-animation=on`. Set WorkingDirectory | The docs don't cover portable mode. `xemu.toml` next to exe is likely; otherwise always pass `-config_path` | Version resource uncertain; parse the window title or log |
| **Flycast** (`flycast.exe`) | `"game.chd"` (verified). Fullscreen: `-config window:fullscreen=yes` (from memory, unverified) | `emu.cfg` in exe dir (uncertain) | Uncertain |
| **Redream** (`redream.exe`) | `"game.gdi"` (verified). Fullscreen via `redream.cfg` (`mode=...`); no CLI flag known | Config in exe dir by design (likely) | Uncertain |
| **MAME** (`mame.exe`) | `<shortname> -rompath "C:\roms\arcade" -skip_gameinfo` (ES-DE: `-rompath %GAMEDIR%\;...`). Fullscreen by default; `-window` for windowed. **Set WorkingDirectory to the MAME dir** (relative paths) | Inherently portable: `mame.ini` in cwd; `mame -createconfig` | `mame -version` (stdout) or the version resource |
| Extras: RPCS3 | `rpcs3.exe --no-gui "EBOOT.BIN"` (verified) | `config\` next to exe by default | |
| Extras: Xenia | `xenia_canary.exe "game.iso"` with WorkingDirectory = emu dir | | |

**Version detection in general:**
- `FileVersionInfo.GetVersionInfo(path)`: read `ProductName` / `OriginalFilename` (identity) and `ProductVersion` / `FileVersion`.
- Many nightlies leave these empty or generic. Fallbacks, in order: `--version`/`-version` with redirected output (expect nothing from GUI exes), the exe SHA-256 against a known-hash table, then folder-name heuristics.
- Store emulator identity in a profile (exe-name patterns), not just the version. Flags change rarely, but exe names do (PCSX2, DuckStation).

---

## 5. SQLite (portable app)

**Recommendation:** `Microsoft.Data.Sqlite` 10.0.x (matches .NET 10). It pulls in `SQLitePCLRaw.bundle_e_sqlite3` 2.1.11+, whose `e_sqlite3.dll` includes **FTS5, JSON1 and R*Tree**. Pair it with `Dapper` 2.1.x, or hand-written readers. EF Core 10 Sqlite also works but adds startup cost and has trimming caveats. I couldn't confirm whether a SQLitePCLRaw 3.0 exists or is used by 10.0.

**sqlite-net-pcl** (1.9.172, `bundle_green`):
- Works, but its reflection ORM gives trim/AOT warnings.
- No FTS5 abstractions (raw SQL still works).
- Less control over the connection and pragmas.
- Not recommended.

**Startup pragmas** (run once per connection):
- `PRAGMA journal_mode=WAL;` (persistent), `PRAGMA synchronous=NORMAL;`, `PRAGMA foreign_keys=ON;`, `PRAGMA busy_timeout=5000;`
- Optionally `PRAGMA mmap_size=268435456;`
- Run `PRAGMA optimize;` on close.

**WAL caveats:**
- It needs shared memory (the `-shm` file), so it **fails on network or UNC paths**. Detect a UNC/network drive (`DriveInfo.DriveType == Network`) and fall back to `journal_mode=DELETE`.
- FAT32/exFAT USB sticks are OK, with a power-loss caveat.
- `-wal` and `-shm` sit next to the DB, so the portable folder must be writable.

**Connection pooling:** Microsoft.Data.Sqlite (6.0+) keeps file handles open. Call `SqliteConnection.ClearAllPools()` before moving, deleting or backing up the DB (or use the backup API `connection.BackupDatabase(dest)`).

**FTS5 design:**
```sql
CREATE VIRTUAL TABLE game_fts USING fts5(title, alt_titles, developer,
  content='games', content_rowid='id',
  tokenize='unicode61 remove_diacritics 2', prefix='2 3');
```
- Add AFTER INSERT/UPDATE/DELETE triggers on `games` to keep the index in sync, or rebuild with `INSERT INTO game_fts(game_fts) VALUES('rebuild')` after bulk scans.
- Query: `WHERE game_fts MATCH '"zeld"*' ORDER BY bm25(game_fts)`.
- For substring/typo-tolerant search, add a second table with `tokenize='trigram'` (SQLite 3.34+; the bundled version is newer).
- Sanitise user input into quoted FTS tokens to avoid FTS syntax errors.

**Publishing:**
- With `-r win-x64 --self-contained`, `e_sqlite3.dll` lands next to the exe. That's ideal for a portable folder.
- With `PublishSingleFile`, natives are not embedded unless `IncludeNativeLibrariesForSelfExtract=true`, and then they extract to %TEMP% at runtime (bad for "portable"). Prefer single-file plus side-by-side native DLLs (e_sqlite3, SDL3, av_libglesv2/libSkiaSharp/libHarfBuzzSharp for Avalonia).
- Ship win-x64 only (and win-arm64 if wanted). Set `SatelliteResourceLanguages=en;de` to slim the output.

---

## 6. Avalonia 11.3 for a big-picture UI

### Large cover grids

- **Built-in virtualizing panels in 11.3:** `VirtualizingStackPanel` (the default for ListBox) and `VirtualizingCarouselPanel`.
- **There's no virtualizing WrapPanel.** A plain `WrapPanel` realises every item: 5k covers means 5k containers, which is slow to open and heavy on memory.
- **ItemsRepeater + UniformGridLayout** (separate package `Avalonia.Controls.ItemsRepeater` in 11.1+; check the exact version) does virtualize. But it has no selection or focus management, BringIntoView with recycled elements is clunky, and it's **unsupported in Avalonia 12**. Avoid it.
- **Recommended: "row chunking".**
  - Use `ListBox` or `ItemsControl` with `VirtualizingStackPanel`, where each item is a *row* of N games.
  - N = floor(viewport width / tile width); rebuild the rows on resize (debounced). Each row is a plain horizontal `ItemsControl`.
  - Works the same on 11.3 and 12, virtualizes vertically, keeps neighbouring rows realised for directional navigation, and `ScrollIntoView(rowIndex)` is simple.
  - Disable ListBox's own arrow-key selection handling (handle KeyDown) so it doesn't fight XY focus.
- Alternatives: write a custom `VirtualizingPanel` subclass (feasible, about 500+ LOC), or try the community `Nova.Avalonia.UI` `VirtualizingVariableSizeWrapPanel` (I haven't verified its quality or version support).

### Image loading

- `Bitmap.DecodeToWidth(Stream, int width, BitmapInterpolationMode.HighQuality)` (and `DecodeToHeight`) are static and safe to call from a background thread (`Task.Run`); assign the result on the UI thread.
- Load pipeline:
  - Pre-generate a **disk thumbnail cache** (e.g. 2× tile width, about 400 px, JPEG/WebP) during library scans. Avalonia 11.3 uses SkiaSharp 2.88 and 12 uses SkiaSharp 3; either works through Avalonia or `SKBitmap`.
  - Keep an in-memory LRU of decoded bitmaps keyed by (path, width) with a byte budget.
  - Cancel loads when a container is recycled: use the `DataContextChanged` / `ContainerPrepared` / `ContainerClearing` events, check a CancellationToken plus an item-identity check before assigning.
  - Don't `Dispose()` bitmaps that might still be displayed; use ref counting, or let the GC handle evicted ones.
- `AsyncImageLoader.Avalonia` (AdvancedImage / `ImageLoader.Source`) is an option for URLs and caching, but it doesn't do decode-to-width well, so write your own loader (about 200 LOC).

### Animations

- Tile focus "pop": `Transitions` on `RenderTransform` (`TransformOperationsTransition`) plus a selector on `:focus-within` / `:focus`. Simple, and fine for one element at a time.
- **Composition API** (render thread; stays smooth while the UI thread is busy):
  - `var v = ElementComposition.GetElementVisual(ctrl); var c = v.Compositor;`
  - `c.CreateVector3KeyFrameAnimation()` for `"Scale"` / `"Offset"`, with `CenterPoint` set.
  - `c.CreateImplicitAnimationCollection()` with an `"Offset"` entry gives animated reflow on resize.
  - `CreateExpressionAnimation` and `CompositionCustomVisualHandler` for custom render-thread drawing (animated backgrounds).
- Avoid live `BlurEffect` over large areas (expensive). Use pre-blurred background art, crossfaded with opacity transitions.

### Acrylic / Mica

- `TransparencyLevelHint="Mica, AcrylicBlur, Blur, None"` (`IReadOnlyList<WindowTransparencyLevel>` in 11.x), plus `Background="Transparent"`. Check `ActualTransparencyLevel` at runtime.
- Mica needs Windows 11 and Avalonia's WinUI composition mode (the default on Win10+; `Win32PlatformOptions.CompositionMode`).
- `ExperimentalAcrylicBorder` + `ExperimentalAcrylicMaterial` give in-app acrylic.
- In a fullscreen big-picture window, Mica/acrylic only shows wallpaper and adds cost. Use it for the windowed/desktop mode only, and use art-based backgrounds in fullscreen (`WindowState.FullScreen`).

### XY focus navigation for controllers

- Available since 11.1 and present in 11.3: `XYFocus.NavigationModes` (flags: Disabled, Keyboard, Gamepad, Remote; the default is Gamepad|Remote), `XYFocus.Up/Down/Left/Right` explicit targets, `XYFocus.*NavigationStrategy` (Auto, Projection, NavigationDirectionDistance, RectilinearDistance; Auto falls back to Projection).
- **Avalonia has no gamepad input device**, and the docs say there's no cross-platform gamepad API. "Focus engagement" isn't implemented either.
- Integration:
  1. Set `XYFocus.NavigationModes="Keyboard,Gamepad"` on the root.
  2. Translate SDL D-pad/stick input into arrow-key navigation: raise `InputElement.KeyDownEvent` with `Key.Up/Down/...` on the focused element, or drive focus explicitly via `KeyboardNavigationHandler.GetNext(current, NavigationDirection.X)?.Focus(NavigationMethod.Directional)`.
  3. Map A/B/Start/shoulders to Enter, Back, a menu, and page jumps or letter-skip.
  4. I'm not fully sure synthetic KeyDown triggers XY navigation in every container. Prototype it early, and keep a fallback custom spatial navigator (projection over visible focusables, about 150 LOC).
- **Virtualization interplay:** XY navigation only sees *realised* elements. At the edge of the realised range, catch "no candidate", scroll the next row into view, wait one layout pass, then focus. Row chunking keeps this simple. I don't know whether 11.3 exposes a cache-length/buffer property on `VirtualizingStackPanel`.
- Show focus visuals for `NavigationMethod.Directional` with a strong, animated focus ring.

### Other Avalonia notes

- Hide the window during a game: `Hide()` stops rendering it. Also stop DispatcherTimers and composition animations.
- Win32 options: keep the default ANGLE/D3D11 rendering and add `Wgl` / `Software` as fallbacks.

---

## Uncertainties to verify early (all quick checks)

1. The Azahar fullscreen flag (`azahar.exe --help`) and its exe name.
2. Which PCSX2 portable filename is accepted (`portable.ini` vs `portable.txt`).
3. melonDS 1.x portable behaviour.
4. Flycast and Redream fullscreen/portable options.
5. MAME `-attach_window`.
6. Current package IDs and versions: `ppy.SDL3-CS` vs `SDL3-CS` + `SDL3-CS.Windows`, `Avalonia.Controls.ItemsRepeater` 11.x, NAudio.
7. Whether synthetic KeyDown drives XYFocus in 11.3.
8. Whether `SDL_HINT_JOYSTICK_ENHANCED_REPORTS` stops DS4/DualSense interference with other emulators.
9. Whether to target Avalonia 12 instead of 11.3 for a new app.

## Sources

- [Avalonia focus / XYFocus docs](https://docs.avaloniaui.net/docs/concepts/input/focus)
- [XYFocusNavigationStrategy](https://docs.avaloniaui.net/api/avalonia/input/xyfocusnavigationstrategy)
- [Avalonia 12 breaking changes](https://docs.avaloniaui.net/docs/avalonia12-breaking-changes)
- [Avalonia 12.0.4 release](https://newreleases.io/project/github/AvaloniaUI/Avalonia/release/12.0.4)
- [ItemsRepeater docs (unsupported in v12)](https://docs.avaloniaui.net/controls/data-display/collections/itemsrepeater)
- [ItemsControl docs](https://docs.avaloniaui.net/docs/reference/controls/itemscontrol)
- [Avalonia performance](https://docs.avaloniaui.net/docs/app-development/performance)
- [Nova.Avalonia.UI VirtualizingVariableSizeWrapPanel](https://jsuarezruiz.github.io/Nova.Avalonia.UI/api/Nova.Avalonia.UI.Controls.VirtualizingVariableSizeWrapPanel.html)
- [Avalonia native interop](https://docs.avaloniaui.net/docs/app-development/native-interop)
- [RoyalApps NativeControlHost notes](https://github.com/royalapplications/royalapps-community-avalonia/blob/main/README.md)
- [MS Q&A on SetParent of external apps](https://learn.microsoft.com/en-us/answers/a/12736602)
- [SDL3-CS (ppy)](https://github.com/ppy/SDL3-CS)
- [SDL3-CS (edwardgushchin)](https://github.com/edwardgushchin/SDL3-CS)
- [SDL3-CS NuGet](https://packages.nuget.org/packages/SDL3-CS/)
- [SDL3-CS.Native (deprecated)](https://www.nuget.org/packages/sdl3-cs.native)
- [Silk.NET.OpenAL](https://feed.nuget.org/packages/Silk.NET.OpenAL/)
- [SK.Libretro](https://awesome.ecosyste.ms/projects/github.com%2Fskurdt%2Fsk.libretro)
- [RetroUnity](https://github.com/Mode8fx/RetroUnity)
- [Microsoft.Data.Sqlite custom SQLite versions](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/custom-versions)
- [Microsoft.Data.Sqlite 10.0 RC dependencies](https://nuget-repo.joestr.at/packages/microsoft.data.sqlite)
- [ES-DE Windows es_systems.xml](https://gitlab.com/es-de/emulationstation-de/-/raw/master/resources/systems/windows/es_systems.xml)
- [PCSX2 CLI docs](https://pcsx2.net/docs/advanced/cli)
- [xemu CLI docs](https://xemu.app/docs/cli)
- [Dolphin CLI configuration](https://www.mintlify.com/dolphin-emu/dolphin/cli/configuration)
- [Dolphin global user directory guide](https://de.dolphin-emu.org/docs/guides/controlling-global-user-directory)
- [DuckStation LaunchBox thread](https://forums.launchbox-app.com/topic/92492-duckstation-prevent-gui-from-showing-when-booting-game)
- [Azahar LaunchBox plugin](https://feedback.launchbox-app.com/help/articles/9527070-azahar-launchbox-integration-plugin)