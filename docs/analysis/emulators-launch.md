# emulators-launch

## Summary
MonHub can run a game in two ways. In the default mode the emulator's own window is pulled into MonHub as a child window. In the other mode, switched on with HubConfig.GamesInOwnWindow, the emulator opens in its own window as usual. The entry point is Launcher.StartGame (Launcher.cs 50-89). It picks the emulator: 3DS goes to Azahar after Azahar3ds.Prepare; an encrypted DS ROM goes to DeSmuME when no BIOS is installed; .gb/.gbc/.gba go to mGBA; anything else is opened with ShellExecute. It then calls Launcher.Play (95-150). Play decides `inside = !GamesInOwnWindow && main != null && GameScreen.Possible`. It allows only one embedded game at a time, warns when the same emulator exe is already running (two copies would overwrite each other's saves), writes the DS/3DS screen arrangement into the emulator's config (ScreenLayout.Apply) and works out the picture's aspect ratio. Finally it starts the emulator with Os.Start(exe, "\"rom\"", exeDir, quiet: inside) and passes the Process to MainWindow.Embed, which calls GameScreen.Play. With quiet=true on Windows the emulator is started through raw CreateProcess with STARTF_USESHOWWINDOW + SW_SHOWMINNOACTIVE, so its first window comes up minimized and without focus and never flashes on the desktop. On Linux the same call sets QT_QPA_PLATFORM=xcb, points XDG_CONFIG_HOME/XDG_DATA_HOME at the emulator folder for portability, and wraps the start in /bin/sh so stderr goes to start.log. A DispatcherTimer in GameScreen (every 150 ms) then does the rest: it finds the emulator's top-level window by PID with EnumWindows and takes it in with SetParent. The target is a GameHost: a NativeControlHost that creates its own Win32 "STATIC" child window (WS_CHILD|WS_VISIBLE|WS_CLIPCHILDREN|SS_BLACKRECT). Each tick also sizes the host to the picture's aspect ratio plus the emulator's measured menu/status bars, and keeps the child at the host's pixel size. While the emulator loads, the host is hidden with an empty SetWindowRgn so MonHub's loading screen shows through. It is unmasked once the emulator has answered SendMessageTimeout(WM_NULL) 5 ticks in a row, more than 1.5 s after the take (Azahar also needs " | " in its title), or after 30 s at the latest. Qt emulators (melonDS, mGBA, Azahar) are handled on a dedicated worker thread, so a busy emulator cannot freeze the UI. They also keep their frame, which is pushed outside the clipped host. DeSmuME, a plain Win32 app, is handled on the UI thread with its frame styles stripped. Keyboard focus is handed over with AttachThreadInput+SetFocus, and only while MonHub's window is active. Exit is detected through Process.Exited, with polling via HasExited/GetProcessById as a fallback. GameScreen then raises Ended, and MainWindow.GameLeft restores the menu, starts a queued restart or game switch, or closes MonHub, and catches up on emulator config writes that were skipped. Quitting posts WM_CLOSE; if the emulator is still running after 8 s, MonHub offers a hard kill (Process.Kill), and asks again every 15 s. "Own window" and closing MonHub both hand the window back to the desktop: the original styles are restored, SetParent(game, 0) is called, and AdjustWindowRectEx puts the window where the host was. Fullscreen is MonHub's own WindowState.FullScreen; the emulator's fullscreen is never used. In fullscreen the bar auto-hides by polling GetCursorPos. Portable configs are written by HubSetup.EnsureAll at every start, with absolute paths each time, so a moved folder repairs itself. A config is never written while its emulator is running, because the emulators rewrite their config on exit; a _pending flag retries on window activation. Playtime is not measured anywhere: what MonHub shows is read from Pokémon save files (SaveInspector.PlayTime). GameScreen._started already exists and could be used to time sessions. 'ScreenLayout' only arranges the DS/3DS screens inside the picture and has nothing to do with multiple monitors. The only multi-monitor code is WindowFit.PrimaryScreen, used to build melonDS's first-start Qt window geometry. Compat.cs is not a compatibility checker: it is a WPF-to-Avalonia shim (Visibility, MessageBox enums, MainWindow accessor, SystemParametersInfo). The real compatibility checks are Rom3ds.Read (Azahar.cs) and RomCheck.HasEncryptedSecureArea (GameLibrary.cs 680-705).

## Reusable
- **Win32GameWindow embedding engine (SetParent child embedding, style surgery, frame push-out, worker thread, coalesced Place, focus, release)** (GameWindow.cs 113-470): Reuse almost as-is (rename namespace). Swap the hard-coded IsQt(class name starts with "Qt") switch for a per-emulator EmbedProfile, e.g. { KeepFrame, OffUiThread, NudgeOnMove, PollResponsive, ReadyTitleContains, KnownBars }, so RetroArch, Dolphin, PCSX2, DuckStation, PPSSPP, Cemu and others can be tuned separately. Keep SWP_NOCOPYBITS, the newest-wins Place coalescing and the 3 s Release wait.
- **GameHost : NativeControlHost with its own STATIC HWND** (GameScreen.axaml.cs 504-547): Reuse as-is. WS_CLIPCHILDREN|SS_BLACKRECT gives a black, clipping parent, and the Destroying event releases the child before the host is destroyed. Keep that hook: without it the emulator window dies with the host.
- **GameScreen embedded-session state machine (Play -> Tick find/take -> WaitUntilReady -> Fit -> Gone/Release/QuitGame/OfferForceQuit)** (GameScreen.axaml.cs 97-501): Adapt. Drop the Pokémon sprite loading screen (ShowLoading/GameData/GameLook) and the bilingual Txt.L strings. Add session playtime: _started is already set in Play, so compute DateTime.UtcNow - _started in Gone()/Release() and persist it per game. Add a controller hotkey (e.g. Guide or Select+Start) that returns to the hub and keep the mouse-at-top-edge bar as a secondary option. Keep the 60 s 'no window found -> leave it as its own window' fallback and the 64x64 minimum host size before taking the window.
- **Aspect-fit host sizing with measured emulator chrome** (GameScreen.axaml.cs 277-339 (Fit, KnownBars, SizeHost) + GameWindow.Chrome 66-78): Adapt. Chrome() finds the child window that covers at least 40 % of the area as the picture, otherwise full-width bars at the top or bottom edge. A value is only accepted when two samples a tick apart agree. Move the KnownBars table into per-emulator config. Better still for RetroHub: configure emulators to hide their menu and status bars (or launch them with fullscreen/no-UI flags) so the chrome is 0.
- **Loading mask via empty window region + responsiveness probe** (GameWindow.cs 159-197 (Work loop, Responsive, Mask), GameScreen.axaml.cs 422-466): Reuse. It hides the emulator's startup flicker and black frames behind an Avalonia loading screen while the window keeps its full size; some emulators crash when squeezed while setting up their renderer. The ready rule is 5 consecutive answers to SendMessageTimeout WM_NULL, more than 1.5 s since the take, or a 30 s cap. Call Settled() afterwards, because DeSmuME stops drawing while it is pinged constantly.
- **Minimized/no-activate process start via CreateProcess** (GameWindow.cs 203-221 (StartMinimized), Os.cs 58-66): Reuse as-is for every embedded launch. Consider also adding a Job Object (JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE) so child processes are tracked and killed together.
- **Overlay layout: GameScreen in a Canvas spanning the window, positioned over PageHost or over Root in fullscreen** (MainWindow.axaml 118-126, MainWindow.axaml.cs 179-201, 244-259): Adapt for a console-style shell: hub fullscreen via WindowState.FullScreen, game overlay on top, and LayoutUpdated -> PlaceGame to keep it aligned.
- **Exit detection + return-to-hub flow** (GameScreen.axaml.cs 118-129, 186-191, 239-257; MainWindow.axaml.cs 54-58, 94-115): Reuse the pattern: Process.EnableRaisingEvents + Exited posted to the UI thread, plus polling as a fallback (HasExited, then GetProcessById(pid)). GameLeft handles the restart queue, close-after-quit and the pending config catch-up.
- **Graceful quit then force-kill** (GameScreen.axaml.cs 468-501): Reuse: PostMessage WM_CLOSE, then after 8 s offer a kill and repeat every 15 s. Use Process.Kill(entireProcessTree: true) instead of Kill(). Replace MessageBox.Choose with controller-navigable dialogs.
- **Single-instance guard by exact exe path** (Launcher.cs 176-204 (IsRunning/IsProgramRunning), HubSetup.cs 257-315 (cached process-name list)): Reuse as-is. Comparing MainModule.FileName means only RetroHub's portable copy of an emulator counts as running.
- **Portable config writers: line-based TOML/INI editors + atomic writes + skip-while-running/_pending retry** (HubSetup.cs 14-59, 317-377; Shared/SafeFile.cs): Reuse SetToml/GetToml/SetIni/RemoveIni and SafeFile as-is. Keep the EnsureAll-at-startup plus EnsurePending-on-Activated pattern, because emulators rewrite their config on exit. Rewriting absolute paths at every start keeps the folder portable.
- **Qt QSettings helpers** (Azahar.cs 315-323 (Set with key\default=false twin, Quote with forward slashes and trailing slash), HubSetup.cs 119-140 (MaximizedQtGeometry)): Reuse for any Qt emulator (Azahar/Citra forks; similar QSettings conventions elsewhere). MaximizedQtGeometry builds a QWidget::saveGeometry v3.0 blob, so a first start opens maximized instead of at 1x.
- **Per-emulator portable setup recipes** (HubSetup.cs 62-227, Azahar.cs 145-241): Take the recipes and generalize them into per-emulator provider classes. melonDS: toml next to the exe, SaveFilePath/SavestatePath/LastROMFolder/BIOS paths. DeSmuME: desmume.ini [PathSettings] Roms/Battery/States. mGBA: empty portable.ini marker + config.ini [ports.qt] savegamePath/savestatePath/lastDirectory. Azahar: a 'user' folder next to azahar.exe makes it portable; Data%20Storage use_custom_storage/nand_directory/sdmc_directory, check_for_update_on_start=false, UI confirmClose=false while embedded, graphics_api from VulkanAvailable().
- **Vulkan availability detection** (Azahar.cs 247-303): Reuse as-is for picking a renderer per emulator. Windows: vulkan-1.dll plus HKLM Khronos\Vulkan\Drivers or VulkanDriverName on display adapters. Linux: ICD json plus a real DRM driver.
- **ROM header checks** (Azahar.cs 8-52 (Rom3ds NCSD/NCCH), GameLibrary.cs 680-705 (RomCheck.HasEncryptedSecureArea), ScreenLayout.cs 93-109 (GB CGB/SGB flags)): Reuse as-is for platform detection and compatibility hints. The mtime-keyed cache pattern is worth copying for other header parsers (NES iNES, SNES, N64 byte order, etc.).
- **BIOS importer** (Bios.cs): Adapt into a per-system BIOS table (system, file name, allowed sizes, ideally MD5/SHA1) with import from loose files and ZIPs into a portable System\BIOS\<system> folder. Keep the 'rank' idea for preferring the right variant.
- **Aspect ratios / Auto layout choice** (ScreenLayout.cs): Adapt: Ratio() and Resolve() (fill factor = min(r/area, area/r)) generalize into a per-system aspect table. Apply() is the pattern for writing a layout into an emulator's config before launch.
- **Unified controls editor + writing bindings into emulator configs** (Pages/EmulatorsPage.axaml.cs 86-229, Controls.cs): Take the idea: tunnel KeyDown capture on the window, XInput polling at 40 ms, and the 'already held at start doesn't count' guard. The concrete mappings (Qt key codes, SDL button numbering differences between Windows and Linux) are useful for melonDS, mGBA and Azahar only.
- **Focus handoff** (GameWindow.cs 328-337, GameScreen.axaml.cs 139-143, MainWindow.axaml.cs 440-443): Reuse: AttachThreadInput(mine, theirs) + SetFocus + detach, only when the hub window IsActive, re-run on Window.Activated, and Post(FocusGame) after every bar button click.

## Pitfalls
- Cross-process SetParent joins the two input queues, and SetWindowPos/SetFocus on another process's window block until that process answers. MonHub works around this by running Qt emulators' window calls on a dedicated thread (GameWindow.cs 126-175). DeSmuME has to be handled on the UI thread: 'taken in from another thread, DeSmuME never draws its picture'. A hung non-Qt emulator can therefore still freeze the UI.
- Stripping the frame (WS_CAPTION/WS_THICKFRAME) confuses Qt 6 apps (melonDS, Azahar): they keep calculating with the old frame and leave a strip empty. Qt windows keep their frame, which is pushed outside the host's clip rect (PlaceNow offsets by the measured frame). Non-Qt windows lose the frame instead, because a pushed-out frame shifts DeSmuME's picture.
- Without SWP_NOCOPYBITS on resize, Windows copies the old frame into the new size and a ghost image appears at the edge (GameWindow.cs 119-121).
- A child window moved together with its parent gets no WM_MOVE. DeSmuME (DirectDraw) then keeps drawing at the old screen position, hence the +1px-and-back 'Moved' nudge whenever the host's screen position changes or after unmasking (GameWindow.cs 373-384).
- DeSmuME's 'Window Force Ratio' moves its own window and throws it off-screen when embedded, so it is set to 0 inside MonHub (HubSetup.SetDeSmuMERatio). DeSmuME has no 'big+small' layout either.
- Polling WM_NULL constantly makes DeSmuME stop drawing; polling stops after ready (Settled). Azahar answers WM_NULL long before the game runs, so readiness also checks for ' | ' in the window title.
- Emulators squeezed to near zero size while initializing their renderer can crash (Azahar, mGBA). The host keeps its full size and is only made invisible with an empty region; the window is only taken once the host is at least 64x64.
- Stale comment: MaskWhileLoading's doc says DeSmuME must be shown right away, but the property returns true for every emulator. DeSmuME works only because of the Moved nudge after unmasking.
- Airspace: the emulator HWND covers all Avalonia content in that area. MonHub refuses to run its tour while a game is embedded. Any in-game overlay RetroHub wants (quick menu, notifications, controller hints) must be a separate top-level window or popup above it, or MonHub's approach of a bar outside the game area.
- Window discovery (Find) takes the first visible, unowned window of the PID larger than 100x80. Splash screens or launcher windows can be picked up. GameWindow.Family() collects child processes only on Linux; on Windows an exe that hands over to a child process (launcher stubs, RetroArch cores are fine, but some emulators re-exec) is never found and falls back to 'own window' after 60 s. Process.Exited also only tracks the direct process.
- There is no Job Object: Process.Kill() kills only the main process, and orphaned child processes survive. Use Kill(entireProcessTree:true) or a kill-on-close job.
- No process-based playtime tracking exists. Playtime in the UI comes from parsing Pokémon save files (SaveInspector), which is useless for a general launcher.
- Multi-monitor is not handled: only the primary screen (WindowFit.PrimaryScreen) is used for melonDS's first geometry, there is no monitor choice for fullscreen, and nothing handles the emulator child at mixed DPI when the window moves between monitors with different scaling. app.manifest declares no dpiAware setting (Avalonia sets per-monitor awareness at runtime). Fit() converts DIPs to pixels with TopLevel.RenderScaling. Mixed-DPI behaviour of the cross-process child was not verified; test it.
- KnownBars contains hard-coded pixel heights of each emulator's menu/status bars at 100 % (GameScreen.axaml.cs 298-309), which breaks with emulator updates. Prefer configuring emulators without menu bars or with fullscreen/no-UI command-line flags.
- Emulators rewrite their config on exit, so any write while one is running is lost. MonHub skips the write and sets _pending, and also must never write melonDS EnableCheats while melonDS runs.
- Two copies of the same emulator overwrite each other's saves. All launch paths check IsProgramRunning; keep this.
- Azahar can't exceed Windows MAX_PATH (260): a game whose extdata can't be created hangs on load, hence SdCardOf's path budget (SdCardDepth=140) and the 'install into a shorter folder' error.
- Azahar settings need a 'key\default=false' twin or the value is ignored. Azahar also ignores storage folders that don't exist (and saves its default back), so they are created first.
- Pokémon-specific coupling not to copy: Rare Candy/60 FPS cheat codes (DeSmuME.cs RareCandyCodes, Azahar.cs 100-136), CarryCheatToDeSmuME, .sav/.dsv save conversion, Pikachu loading sprite, German folder names (Spielstände, Emulatoren, Einstellungen), bilingual Txt.L strings everywhere.
- UI flows are mouse/keyboard modal dialogs (MessageBox.Choose with an 'enter' default). They need controller-navigable replacements for a console-style launcher.
- The Linux /bin/sh exec wrapper puts {args} into the command line unescaped (Os.cs 95): fragile with quotes in ROM paths. Windows also builds the command line by hand ('"' + rom + '"'), and a ROM path containing quotes would break it.
- GetWindowLongPtr/SetWindowLongPtr are imported directly from user32, which works only in 64-bit processes (there is no such export in 32-bit user32). Fine for x64 .NET 10, but don't build x86.
- Linux embedding requires X11/XWayland (QT_QPA_PLATFORM=xcb); native Wayland windows can't be reparented.

## Key details
EMBEDDING ALGORITHM, step by step (Windows)
0. Launcher.Play (Launcher.cs 95-150):
   - inside = !HubConfig.Current.GamesInOwnWindow && main != null && GameScreen.Possible (Possible = GameWindow.Create() != null).
   - Writes the screen arrangement and computes ratio = ScreenLayout.Ratio(...) or GameBoyRatio(rom).
   - process = Os.Start(exe, $"\"{rom}\"", Path.GetDirectoryName(exe), quiet: inside); then main.Embed(process, rom, emulator, ratio).
1. Os.Start, quiet path on Windows: Win32GameWindow.StartMinimized
   - STARTUPINFO { dwFlags = 0x1 STARTF_USESHOWWINDOW, wShowWindow = 7 SW_SHOWMINNOACTIVE }
   - CreateProcess(file, "\"file\" args", inherit=false, flags=0, cwd=workingDir)
   - CloseHandle on both handles, then Process.GetProcessById(pi.dwProcessId).
2. GameScreen.Play (97-129):
   - Resets state; Host.IsVisible = _shown (the host keeps its full size).
   - process.EnableRaisingEvents = true; Exited -> Dispatcher.UIThread.Post(Gone).
   - Starts _tick, a 150 ms DispatcherTimer.
3. Tick (193-234), part 1:
   - if !Alive() -> Gone.
   - if !_ready -> WaitUntilReady (applies the mask on first call).
   - if _game == 0 || !Exists(_game): window = Native.Find(GameWindow.Family(_pid)).
   - Take only when Native.Size(Host.Box) >= 64x64: Take(window, Host.Box); _takenAt = now; Fit(); FocusGame().
   - If no window after 60 s -> Release(toDesktop:false).
   - A recreated window (renderer change) is re-found the same way.
4. Find (223-239):
   - EnumWindows; GetWindowThreadProcessId(h) in pids && IsWindowVisible(h) && GetWindow(h, 4 GW_OWNER) == 0
   - && (IsIconic(h) || window rect > 100x80).
5. Take -> TakeNow (249-275):
   - qt = GetClassName starts with "Qt"; _watched = qt ? game : 0.
   - Saves _own[game] = (style & ~WS_MINIMIZE, exStyle).
   - frame = qt ? 0 : WS_CAPTION|WS_THICKFRAME|WS_SYSMENU|WS_MINIMIZEBOX|WS_MAXIMIZEBOX.
   - SetWindowLongPtr(GWL_STYLE, style & ~(WS_POPUP|WS_MAXIMIZE|WS_MINIMIZE|frame) | WS_CHILD).
   - SetWindowLongPtr(GWL_EXSTYLE, ex & ~(WS_EX_APPWINDOW | (qt ? 0 : WS_EX_WINDOWEDGE|WS_EX_CLIENTEDGE|WS_EX_DLGMODALFRAME))).
   - SetParent(game, box).
   - if IsIconic -> ShowWindow(game, 4 SW_SHOWNOACTIVATE).
   - SetWindowPos(game, 0, 0, 0, w, h, SWP_NOZORDER|SWP_NOACTIVATE|SWP_FRAMECHANGED|SWP_SHOWWINDOW|SWP_NOCOPYBITS).
   - _frame[game] = Frame(game): outer rect minus ClientToScreen(client) on each side.
   - PlaceNow(0, 0, w, h).
6. PlaceNow (308-320): wx = x - f.L, wy = y - f.T, ww = w + f.L + f.R, wh = h + f.T + f.B, so the frame sits outside the clipped STATIC host. It skips the call if the window is already at that spot (ScreenToClient compare), then SetWindowPos(..., SWP_NOZORDER|SWP_NOACTIVATE|SWP_NOCOPYBITS). Place() coalesces requests: newest wins, only one queued under _gate.
7. Fit (277-295):
   - SizeHost() aspect-fits Host.Width/Height to ratio + chrome, with a 2 px hysteresis.
   - w, h = Round(Host.Bounds * RenderScaling); falls back to GetClientRect(box).
   - Native.Place(game, 0, 0, w, h).
   - If Host.PointToScreen changed -> Native.Moved (nudge +1px and back).
   - Fit is triggered by Host.SizeChanged, Host.LayoutUpdated, Window.PositionChanged, SizeChanged and every tick.
8. Readiness (432-466):
   - _answers++ while Responsive(), else reset to 0.
   - Responsive = worker thread's SendMessageTimeout(game, WM_NULL, 0, 0, 0x2 SMTO_ABORTIFHUNG, 300 ms) != 0, and not stuck more than 200 ms inside a queued action.
   - Ready when (_answers >= 5 && gameRuns && since > 1.5 s) || since > 30 s; gameRuns = Azahar title contains " | ".
   - Ready(): Settled (stop probing), Mask(box, false), Fit, Moved nudge, FocusGame.
9. Mask (190-197): for h = box up to (excluding) GA_ROOT: SetWindowRgn(h, hidden ? CreateRectRgn(0,0,0,0) : IntPtr.Zero, true).
10. Focus (330-337): AttachThreadInput(GetCurrentThreadId(), emulatorThread, true); SetFocus(game); detach. Only when TopLevel is Window { IsActive: true } && _ready && _shown.
11. Release (340-369):
   - foreground = GetForegroundWindow() == GetAncestor(box, 2 GA_ROOT); GetWindowRect(box).
   - On the worker: SetParent(game, 0); restore style & ~WS_MAXIMIZE and exStyle (fallback 0x14CF0000 = WS_OVERLAPPEDWINDOW|WS_VISIBLE).
   - AdjustWindowRectEx(ref rect, style, GetMenu(game) != 0, ex); SetWindowPos(frame rect, top clamped to where.T-40, SWP_NOZORDER|SWP_FRAMECHANGED|SWP_SHOWWINDOW).
   - DrawMenuBar; SetForegroundWindow(game) if MonHub was in front.
   - Waits up to 3 s on a ManualResetEventSlim.
12. Close: PostMessage(game, 0x0010 WM_CLOSE). After 8 s -> OfferForceQuit -> Process.Kill.
13. Exit: Exited event, or Tick's Alive() (HasExited; on InvalidOperationException/Win32Exception it retries with GetProcessById(_pid)). Then Gone -> Stop() -> Ended -> MainWindow.GameLeft.

P/INVOKE (GameWindow.cs 433-469; all [DllImport("user32.dll")] unless noted)
- kernel32, CharSet.Unicode, SetLastError: bool CreateProcess(string app, StringBuilder commandLine, IntPtr pa, IntPtr ta, bool inheritHandles, uint flags, IntPtr env, string currentDirectory, ref STARTUPINFO si, out PROCESS_INFORMATION pi)
- kernel32: bool CloseHandle(IntPtr); uint GetCurrentThreadId()
- bool IsIconic(IntPtr); bool ShowWindow(IntPtr, int)
- delegate bool EnumProc(IntPtr h, IntPtr l); bool EnumWindows(EnumProc, IntPtr)
- uint GetWindowThreadProcessId(IntPtr, out uint pid)
- bool IsWindowVisible(IntPtr); bool IsWindow(IntPtr); IntPtr GetWindow(IntPtr, uint cmd)
- IntPtr GetWindowLongPtr(IntPtr, int); IntPtr SetWindowLongPtr(IntPtr, int, IntPtr)
- IntPtr SetParent(IntPtr child, IntPtr parent); IntPtr GetParent(IntPtr)
- bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int hh, uint flags)
- bool GetClientRect(IntPtr, out RECT); bool GetWindowRect(IntPtr, out RECT)
- bool ScreenToClient(IntPtr, ref POINT); bool ClientToScreen(IntPtr, ref POINT)
- CharSet.Unicode: int GetClassName(IntPtr, StringBuilder, int)
- bool AdjustWindowRectEx(ref RECT, uint style, bool menu, uint ex); IntPtr GetMenu(IntPtr); bool DrawMenuBar(IntPtr)
- bool SetForegroundWindow(IntPtr); IntPtr GetForegroundWindow(); IntPtr GetAncestor(IntPtr, uint)
- bool AttachThreadInput(uint a, uint b, bool attach); IntPtr SetFocus(IntPtr)
- bool PostMessage(IntPtr, uint, IntPtr, IntPtr); bool GetCursorPos(out POINT)
- CharSet.Unicode: int GetWindowText(IntPtr, StringBuilder, int)
- IntPtr SendMessageTimeout(IntPtr h, uint msg, IntPtr w, IntPtr l, uint flags, uint timeout, out IntPtr result)
- int SetWindowRgn(IntPtr, IntPtr region, bool redraw); gdi32: IntPtr CreateRectRgn(int, int, int, int)
- GameHost (GameScreen.axaml.cs 542-546): IntPtr CreateWindowEx(int ex, string cls, string name, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param) called with "STATIC", style 0x40000000|0x10000000|0x02000000|0x4; bool DestroyWindow(IntPtr).

CONSTANTS
- WS_CHILD=0x40000000, WS_POPUP=0x80000000, WS_CAPTION=0x00C00000, WS_THICKFRAME=0x00040000, WS_SYSMENU=0x00080000, WS_MINIMIZEBOX=0x00020000, WS_MAXIMIZEBOX=0x00010000, WS_MAXIMIZE=0x01000000, WS_MINIMIZE=0x20000000
- WS_EX_APPWINDOW=0x40000, WS_EX_WINDOWEDGE=0x100, WS_EX_CLIENTEDGE=0x200, WS_EX_DLGMODALFRAME=0x1
- GWL_STYLE=-16, GWL_EXSTYLE=-20
- SWP_NOZORDER=0x4, SWP_NOACTIVATE=0x10, SWP_FRAMECHANGED=0x20, SWP_SHOWWINDOW=0x40, SWP_NOCOPYBITS=0x100, SWP_NOSIZE=0x1
- STARTUPINFO is Sequential, CharSet.Unicode: int cb; string lpReserved, lpDesktop, lpTitle; int dwX..dwFlags; short wShowWindow, cbReserved2; IntPtr lpReserved2, hStdInput, hStdOutput, hStdError.

LINUX (X11GameWindow 472-731)
- Find walks XQueryTree to depth 3: _NET_WM_PID in pids && has WM_STATE && no WM_TRANSIENT_FOR && map_state == IsViewable && larger than 100x80.
- Take: XUnmapWindow; XSync; Sleep 50; XReparentWindow(box, 0, 0); XMoveResizeWindow; XMapWindow.
- Focus: XSetInputFocus(RevertToParent), re-applied every tick unless a TextBox has focus.
- Release: reparent to root, then _NET_ACTIVE_WINDOW client message with source = 2.
- Close: WM_PROTOCOLS / WM_DELETE_WINDOW.
- Mask: XShapeCombineRectangles with 0 rects (libXext).

XAML / THEME TOKENS USED
- Button themes: Hub.Key, Hub.DangerKey, Hub.Ghost, Hub.PrimaryKey, Hub.Chip (RadioButton), Hub.RailMini
- Panels: Hub.Panel, Hub.Inset (ContentControl)
- Brushes and fonts: Hub.Surface, Hub.Line, Hub.Ink, Hub.ChipOn, Hub.ChipOnInk, Hub.Good, Hub.FontLabel, Hub.SizeTag
- Radii: Hub.RadiusInner, Hub.RadiusChip
- Flags and classes: Hub.NavCaps (bool), text classes heading/muted/label/title/body, Button.bar(.narrow) hides labels below 900 px
- Glyph StreamGeometries: Glyph.Screens/Menu/Full/FullExit/Own/Quit
- Markup extension {local:L 'de', 'en'}

PORTABLE CONFIG KEYS
- melonDS.toml: LastROMFolder; [Instance0] SaveFilePath, SavestatePath, EnableCheats; [DS] BIOS7Path/BIOS9Path/FirmwarePath; [Emu] ExternalBIOSEnable; [Instance0.Window0] Geometry (base64 QWidget::saveGeometry: magic 0x01D9D0CB, ver 3.0, frame rect, normal rect, screen#, maximized=2, fullscreen=0, screenWidth, rect), ScreenLayout 1/2, ScreenSizing 0/1, ScreenGap 0, ScreenRotation 0. Paths are quoted with forward slashes.
- desmume.ini: [PathSettings] Roms, Battery, States; [Video] LCDsLayout 0/1, Window Rotate 0, Window Force Ratio 0/1.
- mGBA: empty portable.ini next to exe; config.ini [ports.qt] savegamePath, savestatePath, lastDirectory, fastForwardHeldRatio, fastForwardRatio; [gba.input.QT_K]/[gba.input.SDLB]; qt.ini shortcutKey/shortcutButton/shortcutAxis.
- Azahar (portable via user\ next to azahar.exe): user\config\qt-config.ini keys, each with key\default=false: [Data%20Storage] use_custom_storage, nand_directory, sdmc_directory (per-game SD card Spielstände\Azahar\<game>); [Renderer] frame_limit 100, graphics_api 2 Vulkan / 1 OpenGL, resolution_factor 3, async_shader_compilation, use_disk_shader_cache; [Layout] layout_option 0/2/3, swap_screen, upright_screen; [UI] confirmClose; [Miscellaneous] check_for_update_on_start=false; [Paths] romsPath; [Controls] profile.
- Azahar cheats: user\cheats\<TitleId:X16>.txt.
- Installer: melonDS.toml copied only if absent (MonHub.iss line 98 onlyifdoesntexist); on Linux shipped as melonDS.default.toml and copied on first run.

BIOS
- Bios.Files = [("bios7.bin", [16K]), ("bios9.bin", [4K]), ("firmware.bin", [128K, 256K, 512K])], stored in HubPaths.Bios = System\BIOS.
- Import: matches by file name + size from loose files or ZIP entries; entries with 'dsi' in the path rank lower.
- Encrypted DS ROM check: ARM9 offset at header 0x20 == 0x4000 and the 8 bytes at 0x4000 are not E7FFDEFFE7FFDEFF, 0 or "encryObj".
- 3DS check: NCSD at 0x100 -> partition 0 offset at 0x120 * 0x200; NCCH magic; product code at 0x150; title id at 0x118; NoCrypto flag = bit 0x04 of byte 0x18F.

PATHS
- HubPaths.Root = parent of System when AppDir is System\App.
- Emulators in System\Emulatoren\<melonDS|DeSmuME|mGBA|Azahar>; saves in Spielstände\<emu>; settings in System\Einstellungen\MonHub.json (HubConfig: GamesInOwnWindow, DsScreens, ThreeDsScreens, Favorites, Controls, AzaharLook, AzaharSixtyFps).
- Os.FindProgram(folder, name): exact name.exe, else name[_ .]*.exe preferring 'x64'.

## Files
- MonHub\Launcher.cs: Game/emulator start orchestration: StartGame (emulator choice + per-emulator prep), Play (inside vs own window, single-instance guards, screen layout, Os.Start + Embed), IsProgramRunning (by exact exe path via MainModule.FileName), EmulatorsOpen guard before deleting saves, CarryCheatToDeSmuME
- MonHub\GameWindow.cs: Core embedding engine: abstract GameWindow (Find/Take/Place/Size/Focus/Release/Close/Moved/Title/Responsive/Settled/Mask/Exists/Cursor/Chrome/Family), Win32GameWindow (SetParent child embedding, worker thread, CreateProcess minimized start, WM_NULL responsiveness probe, SetWindowRgn mask), X11GameWindow (XReparentWindow, XShape mask, EWMH activate)
- MonHub\GameScreen.axaml.cs: Embedded-game state machine (150 ms DispatcherTimer Tick), loading screen/mask, aspect-ratio host sizing with emulator chrome measurement, focus policy, fullscreen auto-hide bar, quit/force-kill flow, exit detection; GameHost : NativeControlHost creating a Win32 STATIC child HWND
- MonHub\GameScreen.axaml: Bar over the embedded game (title, emulator chip, Menu/Screens/Fullscreen/Own window/Quit buttons using Hub.Key / Hub.DangerKey themes), Loading StackPanel, local:GameHost x:Name=Host in Grid row 1
- MonHub\MainWindow.axaml.cs: Host side: Embed (73-91), GameLeft (94-115: close-after-game, restart queue, EnsurePending), AskClose on window Closing (61-70,121-146), SwitchGame, ArrangementChanged restart prompt, PlaceGame (Canvas overlay over PageHost or whole Root in fullscreen, 179-201), SetFullScreen via WindowState.FullScreen (244-259), Window_Activated -> FocusGame + EnsurePending
- MonHub\MainWindow.axaml: Line 118 Grid x:Name=PageHost; lines 124-126 Canvas Grid.RowSpan=2 holding local:GameScreen x:Name=Game (the overlay layer)
- MonHub\Os.cs: Os.FindProgram (emulator exe discovery: exact name.exe, else name_/name /name. prefix preferring x64; Linux AppRun/AppImage), Os.Start (quiet Windows -> StartMinimized; ShellExecute; Linux env/XDG portable/QT_QPA_PLATFORM=xcb/LD_LIBRARY_PATH/start.log + early-crash dialog), LinuxPad joystick reader
- MonHub\Azahar.cs: Rom3ds NCSD/NCCH header parser (product code, title id, encrypted flag, cached by mtime); Azahar3ds: per-game SD card folder with MAX_PATH budget, Prepare (config + cheats before every start), Ensure (portable defaults), Qt-ini Set with \default=false twin, Quote, VulkanAvailable (Windows registry / Linux DRM driver)
- MonHub\Bios.cs: User-supplied DS BIOS import: spec table (name + allowed sizes), import from loose files or ZIPs, DSi-folder deprioritized, stored in System\BIOS; Installed/Missing/Remove
- MonHub\Compat.cs: NOT a compatibility checker: global usings + WPF->Avalonia shims (Visibility enum ext, MessageBox enums, WindowOf, MainWindow, SystemAnimations via SystemParametersInfo SPI_GETCLIENTAREAANIMATION)
- MonHub\Shell.cs: Navigation facade (Shell.Navigate), OpenFolder/OpenWith (explorer / xdg-open), ShowIcon, ForwardWheel
- MonHub\HubSetup.cs: Portable emulator config writer run at every start: EnsureAll/EnsurePending (skip while emulator runs, _pending retry), EnsureMelonDS/DeSmuME/MGBA/Azahar, ApplyBios(ToMelonDS), MaximizedQtGeometry (QWidget::saveGeometry 3.0 blob), SetDeSmuMERatio, IsRunning(prefix) with 1 s cached process list, SetToml/GetToml/SetIni/RemoveIni line-based editors
- MonHub\HubPaths.cs: Portable folder layout: Root = two levels above System\App; System\Emulatoren\<emu>, System\Einstellungen, System\BIOS, ROMs, Spielstände\<emu>; exe lookups; MelonDSConfigDir (next to exe on Windows, config/melonDS on Linux)
- MonHub\ScreenLayout.cs: DS/3DS screen arrangement (Auto/Stacked/SideBySide/BigAndSmall): aspect ratios, Auto resolve by fill factor of game area, Apply into melonDS.toml / desmume.ini / Azahar qt-config.ini, GameBoyRatio (GBA 3:2, GB 10:9, SGB 8:7 border)
- MonHub\Rando\MelonDS.cs: MelonDSInstall : IEmulator – reads [Instance0] SaveFilePath/CheatFilePath, writes .mch cheat file, EnsureCheatsEnabled (only while melonDS closed)
- MonHub\Rando\DeSmuME.cs: Pokémon Rare Candy AR codes (Pokémon-specific), IEmulator interface (Name/Folder/Exe/HasDataFor/WriteCheatFile), Emulators.TryOpen, DeSmuMEInstall ([PathSettings] Battery/Cheats from ini, .dct writer)
- MonHub\emulator-config\melonDS.toml: Shipped melonDS 1.x starter config (installed only if absent): JIT on, GL renderer ScaleFactor 4, ScreenLayout 2, DirectBoot, ExternalBIOSEnable=false, Qt key codes for keyboard, SDL joystick mapping, PauseLostFocus=false (important for embedding)
- MonHub\Pages\EmulatorsPage.axaml.cs: Emulators page: per-emulator status (save counts), Open (Launcher.OpenEmulator with duplicate-instance prompt), default emulator, own-window toggle, screen arrangement chips, unified key/controller binding editor (tunnel KeyDown capture, XInput polling 40 ms), BIOS import/remove
- MonHub\Pages\EmulatorsPage.axaml: Emulator cards using Hub.Panel/Hub.Inset/Hub.Key/Hub.Ghost/Hub.PrimaryKey/Hub.Chip themes and Hub.ChipOn/Hub.ChipOnInk/Hub.FontLabel/Hub.SizeTag/Hub.RadiusChip tokens
- MonHub\Controls.cs: ControlSettings: one binding set written into melonDS.toml ([Instance0.Keyboard]/[Instance0.Joystick]), mGBA config.ini (gba.input.QT_K / SDLB) + qt.ini, Azahar qt-config.ini profiles; XInputGetState (xinput1_4.dll) and joyGetPosEx (winmm) for pad detection
- MonHub\Shared\SafeFile.cs: Atomic write (temp file + File.Move overwrite) used for all emulator config writes
- MonHub\Shared\WindowFit.cs: Primary screen work area in logical px (only multi-monitor-related code; primary screen only)
- MonHub\GameLibrary.cs: EmulatorFor/MainEmulator/ExeFor (lines 147-179), emulator name constants (90-93), RecentRoms parsing of melonDS/DeSmuME configs (348-366), RomCheck.HasEncryptedSecureArea (680-705)
