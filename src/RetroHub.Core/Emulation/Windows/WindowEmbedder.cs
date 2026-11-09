using System.Collections.Concurrent;
using System.Diagnostics;
using RetroHub.Core.Catalog;
using RetroHub.Core.Logging;

namespace RetroHub.Core.Emulation.Windows;

/// <summary>
/// UI-agnostic Win32 embedding engine: hangs an emulator's top-level window into a host window of RetroHub
/// (SetParent child window) and gives it back. Port of MonHub's Win32GameWindow, tuned per emulator through an
/// <see cref="EmbedProfile"/> instead of the hard-wired "class name starts with Qt" check.
/// <para>
/// Cross-process window calls (SetWindowPos, SetFocus, SetParent …) wait until the other program answers. With
/// <see cref="EmbedProfile.OffUiThread"/> they run on a dedicated worker thread, so a busy or hung emulator only blocks
/// that thread; the caller never waits more than ~300 ms (except <see cref="Release"/>, ≤ 3 s). Without it (plain Win32
/// programs such as DeSmuME, which never draw when taken in from another thread) they run on the caller's thread.
/// </para>
/// Typical use from the UI: CreateHostWindow (NativeControlHost) → SetMasked(true) → Take → Place on every layout
/// change → poll IsResponsive until ready → Settled + SetMasked(false) + NudgeMoved + Focus → … → Release / Dispose
/// → DestroyHostWindow.
/// </summary>
public sealed class WindowEmbedder : IDisposable
{
    private static readonly ConcurrentDictionary<nint, WindowEmbedder> ByHost = new();

    private readonly BlockingCollection<Action> _work = new();
    private readonly Thread? _worker;
    private readonly object _gate = new();

    private nint _game, _host;
    // the taken window's original styles (for Release) and which window they belong to; only touched on the
    // worker thread (or the caller's thread without a worker)
    private (long Style, long ExStyle)? _own;
    private nint _ownWindow;
    private (int L, int T, int R, int B) _frame;
    private (int X, int Y, int W, int H)? _placeWanted;
    private bool _placeQueued;
    private volatile bool _attached;
    private volatile bool _watching;
    private volatile bool _answers = true;
    private long _busySince; // Stopwatch timestamp while an action runs on the worker, 0 = idle
    private volatile bool _disposed;

    public EmbedProfile Profile { get; }

    public WindowEmbedder(EmbedProfile profile)
    {
        Profile = profile ?? throw new ArgumentNullException(nameof(profile));
        // The worker also asks the emulator whether it answers (WM_NULL probe) – a hung one only blocks this thread.
        if (profile.OffUiThread)
        {
            _worker = new Thread(Work) { IsBackground = true, Name = "RetroHub window embedder" };
            _worker.Start();
        }
    }

    // ---- host window ------------------------------------------------------------------------------------------

    /// <summary>
    /// Creates RetroHub's place for the game: a black STATIC child window (WS_CHILD|WS_VISIBLE|WS_CLIPCHILDREN|SS_BLACKRECT)
    /// inside <paramref name="parentHwnd"/>. Call on the thread that owns the parent (Avalonia: in
    /// NativeControlHost.CreateNativeControlCore). Returns 0 on failure.
    /// </summary>
    public static nint CreateHostWindow(nint parentHwnd)
    {
        if (!OperatingSystem.IsWindows() || parentHwnd == 0) return 0;
        var h = Native.CreateWindowEx(0, "STATIC", "",
            (uint)(Native.WS_CHILD | Native.WS_VISIBLE | Native.WS_CLIPCHILDREN) | Native.SS_BLACKRECT,
            0, 0, 100, 100, parentHwnd, 0, 0, 0);
        if (h == 0) Log.Warn($"CreateHostWindow failed (error {System.Runtime.InteropServices.Marshal.GetLastPInvokeError()})");
        return h;
    }

    /// <summary>
    /// Destroys a host window. A game window still inside it is given back to the desktop first – destroying a
    /// cross-process child would destroy the emulator's window (the emulator would crash or quit).
    /// </summary>
    public static void DestroyHostWindow(nint hostWindow)
    {
        if (!OperatingSystem.IsWindows() || hostWindow == 0) return;
        try
        {
            if (ByHost.TryGetValue(hostWindow, out var embedder)) embedder.Release(toDesktop: true);
            // anything foreign still inside (taken by someone else, or Release timed out): minimal rescue
            int me = Environment.ProcessId;
            for (var child = Native.GetWindow(hostWindow, Native.GW_CHILD); child != 0;)
            {
                var next = Native.GetWindow(child, Native.GW_HWNDNEXT);
                if (Native.ProcessIdOf(child) != me)
                {
                    long style = Native.GetStyle(child);
                    Native.SetParent(child, 0);
                    Native.SetWindowLongPtr(child, Native.GWL_STYLE, (nint)((style & ~Native.WS_CHILD) | Native.WS_OVERLAPPEDWINDOW));
                    Native.SetWindowPos(child, 0, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_FRAMECHANGED | Native.SWP_NOACTIVATE);
                    Native.ShowWindow(child, Native.SW_SHOWMINNOACTIVE);
                    Log.Warn($"Foreign window {child:X} rescued from host {hostWindow:X} before destroying it");
                }
                child = next;
            }
        }
        catch (Exception ex)
        {
            Log.Warn("DestroyHostWindow: releasing the game window failed", ex);
        }
        if (!Native.DestroyWindow(hostWindow))
            Log.Warn($"DestroyWindow({hostWindow:X}) failed (error {System.Runtime.InteropServices.Marshal.GetLastPInvokeError()})");
    }

    // ---- state ------------------------------------------------------------------------------------------------

    /// <summary>The taken game window (0 when none).</summary>
    public nint GameWindow => _game;
    /// <summary>The host the game window was taken into (0 when none).</summary>
    public nint HostWindow => _host;
    /// <summary>Whether the game window still exists (an emulator may build a new one, e.g. after a renderer change).</summary>
    public bool Exists => _game != 0 && Native.IsWindow(_game);
    /// <summary>The game window currently is a child of the host (Take finished successfully and no Release since).</summary>
    public bool IsAttached => _attached && Exists && Native.GetParent(_game) == _host;
    /// <summary>The window title as kept by the system (never blocks on the emulator); "" when none.</summary>
    public string Title => Native.GetTitle(_game);

    /// <summary>
    /// Game windows currently taken by any embedder. They are child windows then, so EnumWindows (top-level only) does
    /// not list them – GameSession.RequestCloseAsync sends WM_CLOSE to these as well.
    /// </summary>
    internal static IReadOnlyList<nint> EmbeddedWindows()
    {
        var list = new List<nint>();
        foreach (var e in ByHost.Values)
        {
            var g = e._game;
            if (g != 0 && !list.Contains(g)) list.Add(g);
        }
        return list;
    }

    // ---- take -------------------------------------------------------------------------------------------------

    /// <summary>
    /// Takes the game window into the host at width x height pixels (≤ 0: the host's client size). The frame is
    /// stripped, or with <see cref="EmbedProfile.KeepFrame"/> kept and pushed outside the clipped host (Qt 6 apps
    /// break when the frame is removed). Returns false when the window/host is invalid or SetParent failed; true when
    /// taken – or, for a busy emulator with OffUiThread, still in progress after 300 ms (check <see cref="IsAttached"/>).
    /// </summary>
    public bool Take(nint gameWindow, nint hostWindow, int width, int height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!OperatingSystem.IsWindows() || gameWindow == 0 || hostWindow == 0 || !Native.IsWindow(gameWindow) || !Native.IsWindow(hostWindow))
            return false;

        // an emulator that built a new window: let the old one go first (if it still exists)
        var previous = _game;
        if (previous != 0 && previous != gameWindow) Release(toDesktop: true);

        if (width <= 0 || height <= 0)
        {
            Native.GetClientRect(hostWindow, out var rc);
            (width, height) = (rc.Width, rc.Height);
        }

        nint oldHost;
        lock (_gate)
        {
            oldHost = _host;
            _game = gameWindow;
            _host = hostWindow;
            _placeWanted = null;
        }
        // moved to another host: destroying the old one must not touch the game any more
        if (oldHost != 0 && oldHost != hostWindow) ByHost.TryRemove(new KeyValuePair<nint, WindowEmbedder>(oldHost, this));
        ByHost[hostWindow] = this;
        // Only off-thread emulators (the slow Qt starters) are asked whether they answer; the others count as ready
        // at once (DeSmuME stops drawing when pinged all the time). See MonHub GameWindow.Take.
        _watching = Profile.OffUiThread;
        _answers = !Profile.OffUiThread;

        if (_worker == null) return TakeNow(gameWindow, hostWindow, width, height);

        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!Enqueue(() => done.TrySetResult(TakeNow(gameWindow, hostWindow, width, height)))) return false;
        if (done.Task.Wait(300)) return done.Task.Result;
        Log.Info($"Take of {gameWindow:X} still running after 300 ms (emulator busy)");
        return true;
    }

    private bool TakeNow(nint game, nint host, int w, int h)
    {
        if (!Native.IsWindow(game) || !Native.IsWindow(host)) return false;
        lock (_gate)
        {
            if (_game != game || _host != host) return false; // released (or replaced) while this was queued
        }
        long style = Native.GetStyle(game), ex = Native.GetExStyle(game);
        // Taken again while still ours (same window, already a child: the UI re-ran Take, or moved it to a new host):
        // keep the styles saved by the first take – the current ones are the stripped child styles.
        bool retake = _own != null && _ownWindow == game && (style & Native.WS_CHILD) != 0;
        if (!retake)
        {
            _own = (style & ~Native.WS_MINIMIZE, ex); // given back as a normal window, not a minimized one
            _ownWindow = game;
        }

        long frame = Profile.KeepFrame ? 0 : Native.WS_CAPTION | Native.WS_THICKFRAME | Native.WS_SYSMENU | Native.WS_MINIMIZEBOX | Native.WS_MAXIMIZEBOX;
        long exFrame = Profile.KeepFrame ? 0 : Native.WS_EX_WINDOWEDGE | Native.WS_EX_CLIENTEDGE | Native.WS_EX_DLGMODALFRAME;
        Native.SetWindowLongPtr(game, Native.GWL_STYLE, (nint)((style & ~(Native.WS_POPUP | Native.WS_MAXIMIZE | Native.WS_MINIMIZE | frame)) | Native.WS_CHILD));
        Native.SetWindowLongPtr(game, Native.GWL_EXSTYLE, (nint)(ex & ~(Native.WS_EX_APPWINDOW | exFrame)));
        Native.SetParent(game, host);
        if (Native.GetParent(game) != host)
        {
            int err = System.Runtime.InteropServices.Marshal.GetLastPInvokeError();
            Log.Warn($"SetParent({game:X}, {host:X}) failed (error {err}) – restoring the window");
            Native.SetWindowLongPtr(game, Native.GWL_STYLE, (nint)style);
            Native.SetWindowLongPtr(game, Native.GWL_EXSTYLE, (nint)ex);
            Native.SetWindowPos(game, 0, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_FRAMECHANGED);
            // a failed re-take leaves the window where it was (still ours, original styles still saved)
            if (!retake)
            {
                _own = null;
                _ownWindow = 0;
            }
            return false;
        }
        // it was started minimized (SW_SHOWMINNOACTIVE): back to its normal state, now inside RetroHub
        if (Native.IsIconic(game)) Native.ShowWindow(game, Native.SW_SHOWNOACTIVATE);
        // SWP_NOCOPYBITS: on a resize Windows would copy the old picture into the new size – the last frame
        // showed a second time at the edge until the emulator drew again
        Native.SetWindowPos(game, 0, 0, 0, Math.Max(1, w), Math.Max(1, h),
            Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_FRAMECHANGED | Native.SWP_SHOWWINDOW | Native.SWP_NOCOPYBITS);
        _frame = MeasureFrame(game);
        lock (_gate)
        {
            // released while this ran (busy emulator): the queued Release gives the window back right after
            if (_game == game) _attached = true;
        }
        PlaceNow(game, 0, 0, w, h);
        Log.Info($"Took window {game:X} ('{Native.GetTitle(game)}') into host {host:X}: {w}x{h}, frame {_frame}, keepFrame {Profile.KeepFrame}");
        return true;
    }

    /// <summary>How far the window reaches beyond its client area on each side.</summary>
    private static (int L, int T, int R, int B) MeasureFrame(nint game)
    {
        if (!Native.GetWindowRect(game, out var outer) || !Native.GetClientRect(game, out var inner)) return default;
        var origin = new Native.POINT();
        Native.ClientToScreen(game, ref origin);
        return (origin.X - outer.Left, origin.Y - outer.Top, outer.Right - (origin.X + inner.Right), outer.Bottom - (origin.Y + inner.Bottom));
    }

    // ---- place ------------------------------------------------------------------------------------------------

    /// <summary>
    /// Puts the game's client area exactly onto this rectangle of the host (its frame, if kept, beyond it). Requests
    /// are coalesced – only the newest counts while the emulator is busy – and skipped when nothing changed.
    /// </summary>
    public void Place(int x, int y, int w, int h)
    {
        if (_disposed || _game == 0 || w <= 0 || h <= 0) return;
        lock (_gate)
        {
            _placeWanted = (x, y, w, h);
            if (_placeQueued) return;
            _placeQueued = true;
        }
        Do(() =>
        {
            (int X, int Y, int W, int H)? wanted;
            nint game;
            lock (_gate)
            {
                wanted = _placeWanted;
                _placeQueued = false;
                game = _game;
            }
            if (wanted is { } p && game != 0 && _attached) PlaceNow(game, p.X, p.Y, p.W, p.H);
        });
    }

    private void PlaceNow(nint game, int x, int y, int width, int height)
    {
        var f = _frame;
        int wx = x - f.L, wy = y - f.T, ww = width + f.L + f.R, wh = height + f.T + f.B;
        if (Native.GetWindowRect(game, out var r) && Native.GetParent(game) is var parent && parent != 0)
        {
            // already there: no move (a move makes some emulators redraw)
            var p = new Native.POINT { X = r.Left, Y = r.Top };
            Native.ScreenToClient(parent, ref p);
            if (p.X == wx && p.Y == wy && r.Width == ww && r.Height == wh) return;
        }
        Native.SetWindowPos(game, 0, wx, wy, ww, wh, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_NOCOPYBITS);
    }

    // ---- mask / readiness -------------------------------------------------------------------------------------

    /// <summary>
    /// Makes the host (and the game inside) invisible with an empty window region, keeping its full size – the
    /// emulator has its full room while it starts and RetroHub's loading screen below shows meanwhile. Applied to the
    /// host and its parents up to (not including) the top-level window. Own-process windows only: never blocks.
    /// </summary>
    public void SetMasked(bool hidden) => SetMasked(_host, hidden);

    /// <summary>Same as <see cref="SetMasked(bool)"/> for a host that has no game window yet (mask before Take).</summary>
    public static void SetMasked(nint host, bool hidden)
    {
        if (host == 0 || !OperatingSystem.IsWindows()) return;
        var root = Native.GetAncestor(host, Native.GA_ROOT);
        for (var h = host; h != 0 && h != root; h = Native.GetParent(h))
        {
            nint region = hidden ? Native.CreateRectRgn(0, 0, 0, 0) : 0;
            // on success the system owns the region
            if (Native.SetWindowRgn(h, region, true) == 0 && region != 0) Native.DeleteObject(region);
        }
    }

    /// <summary>
    /// Whether the emulator answers right now. Off-thread profiles: the worker's last WM_NULL probe
    /// (SendMessageTimeout, SMTO_ABORTIFHUNG, 300 ms) until <see cref="Settled"/>, and false while a window call is
    /// stuck for more than 200 ms. Other profiles (and after Settled): the system's hung-window state. Never blocks.
    /// </summary>
    public bool IsResponsive()
    {
        var game = _game;
        if (game == 0 || !Native.IsWindow(game)) return false;
        long since = Interlocked.Read(ref _busySince);
        if (since != 0 && Stopwatch.GetElapsedTime(since) > TimeSpan.FromMilliseconds(200)) return false;
        if (_watching) return _answers;
        return !Native.IsHungAppWindow(game);
    }

    /// <summary>The game is up: stop probing it (DeSmuME stops drawing when asked all the time).</summary>
    public void Settled() => _watching = false;

    // ---- focus / move -----------------------------------------------------------------------------------------

    /// <summary>Keys go to the game: the emulator's thread joins the caller's input queue for SetFocus, then leaves.</summary>
    public void Focus()
    {
        var game = _game;
        if (game == 0 || _disposed) return;
        Do(() =>
        {
            uint theirs = Native.GetWindowThreadProcessId(game, out _), mine = Native.GetCurrentThreadId();
            if (theirs == 0) return;
            bool joined = theirs != mine && Native.AttachThreadInput(mine, theirs, true);
            Native.SetFocus(game);
            if (joined) Native.AttachThreadInput(mine, theirs, false);
        });
    }

    /// <summary>
    /// The host moved on the screen: a child moved along with its parent gets no WM_MOVE – one step aside and back
    /// gives it two (DirectDraw emulators like DeSmuME otherwise keep drawing at the old spot).
    /// </summary>
    public void NudgeMoved()
    {
        var game = _game;
        if (game == 0 || _disposed) return;
        Do(() =>
        {
            if (!Native.GetWindowRect(game, out var r) || Native.GetParent(game) is var parent && parent == 0) return;
            var p = new Native.POINT { X = r.Left, Y = r.Top };
            Native.ScreenToClient(parent, ref p);
            const uint keep = Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_NOSIZE;
            Native.SetWindowPos(game, 0, p.X + 1, p.Y, 0, 0, keep);
            Native.SetWindowPos(game, 0, p.X, p.Y, 0, 0, keep);
        });
    }

    /// <summary>
    /// How much of the game's client area is not picture (menu bar/toolbar at the top, status bar at the bottom), in
    /// pixels: around the picture's own child window if there is one, else bars along the top/bottom edge. Falls back to
    /// the profile's BarTop/BarBottom (scaled to the host's DPI) while nothing can be measured. Never blocks.
    /// </summary>
    public (int Top, int Bottom) MeasureBars()
    {
        var game = _game;
        if (game != 0 && Native.IsWindow(game))
        {
            var children = Children(game, out int w, out int h);
            if (w > 0 && h > 0)
            {
                var picture = children.Where(c => (long)c.W * c.H >= (long)w * h * 2 / 5).OrderByDescending(c => (long)c.W * c.H).FirstOrDefault();
                if (picture.W > 0)
                    return (Math.Max(0, picture.Y), Math.Max(0, h - picture.Y - picture.H));
                if (children.Count > 0)
                {
                    var bars = children.Where(c => c.W >= w * 4 / 5 && c.H < h / 4).ToList();
                    int top = bars.Where(c => c.Y <= 2).Select(c => c.Y + c.H).DefaultIfEmpty(0).Max();
                    int bottom = bars.Where(c => c.Y + c.H >= h - 2 && c.Y > h / 2).Select(c => h - c.Y).DefaultIfEmpty(0).Max();
                    if (top > 0 || bottom > 0) return (top, bottom);
                }
            }
        }
        double scale = 1;
        var host = _host;
        if (host != 0)
        {
            uint dpi = Native.GetDpiForWindow(host);
            if (dpi > 0) scale = dpi / 96.0;
        }
        return ((int)Math.Round(Profile.BarTop * scale), (int)Math.Round(Profile.BarBottom * scale));
    }

    private static List<(int X, int Y, int W, int H)> Children(nint game, out int width, out int height)
    {
        var list = new List<(int, int, int, int)>();
        Native.GetClientRect(game, out var client);
        width = client.Width;
        height = client.Height;
        int guard = 0;
        for (var child = Native.GetWindow(game, Native.GW_CHILD); child != 0 && guard++ < 512; child = Native.GetWindow(child, Native.GW_HWNDNEXT))
        {
            if (!Native.IsWindowVisible(child) || !Native.GetWindowRect(child, out var r)) continue;
            var p = new Native.POINT { X = r.Left, Y = r.Top };
            Native.ScreenToClient(game, ref p);
            list.Add((p.X, p.Y, r.Width, r.Height));
        }
        return list;
    }

    // ---- release ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Gives the window back: original styles, SetParent(game, 0), placed (AdjustWindowRectEx) where the host was.
    /// <paramref name="toDesktop"/> true: shown as the emulator's own window (activated if RetroHub was in front);
    /// false (game is ending / RetroHub closes the game screen): detached and minimized without activation, so it never
    /// disappears with the host but does not pop up either. Waits ≤ 3 s for a busy emulator.
    /// </summary>
    public void Release(bool toDesktop)
    {
        nint game, host;
        lock (_gate)
        {
            game = _game;
            host = _host;
            _game = 0;
            _placeWanted = null;
            _attached = false;
        }
        _watching = false;
        if (host != 0) ByHost.TryRemove(new KeyValuePair<nint, WindowEmbedder>(host, this));
        if (game == 0) return;
        if (!Native.IsWindow(game))
        {
            Do(() => ForgetOwn(game)); // the window is gone (emulator closed): nothing to give back
            return;
        }

        bool foreground = host != 0 && Native.GetForegroundWindow() == Native.GetAncestor(host, Native.GA_ROOT);
        if (host == 0 || !Native.GetWindowRect(host, out var where)) Native.GetWindowRect(game, out where);

        // Decided where the window calls run (after any Take still running there for a busy emulator): the window is
        // given back if it is inside the host by then – a take that was dropped or failed left it alone.
        void Run()
        {
            var own = ForgetOwn(game);
            if (Native.IsWindow(game) && Native.GetParent(game) == host) ReleaseNow(game, own, where, foreground, toDesktop);
        }

        if (_worker == null || _work.IsAddingCompleted)
        {
            Run();
            return;
        }
        // not disposed: after a timeout the worker still sets it when the emulator finally answers
        var done = new ManualResetEventSlim();
        bool queued = Enqueue(() =>
        {
            try { Run(); }
            finally { done.Set(); }
        });
        if (!queued) Run();
        else if (!done.Wait(TimeSpan.FromSeconds(3))) Log.Warn($"Release of {game:X} did not finish within 3 s (emulator busy)");
    }

    /// <summary>The saved original styles of this window (null when none) – forgotten from now on.</summary>
    private (long Style, long ExStyle)? ForgetOwn(nint game)
    {
        var own = _ownWindow == game ? _own : null;
        if (_ownWindow == game)
        {
            _own = null;
            _ownWindow = 0;
        }
        return own;
    }

    private static void ReleaseNow(nint game, (long Style, long ExStyle)? own, Native.RECT where, bool foreground, bool toDesktop)
    {
        try
        {
            var (style, ex) = own ?? (Native.WS_OVERLAPPEDWINDOW | Native.WS_VISIBLE, 0L);
            Native.SetParent(game, 0);
            Native.SetWindowLongPtr(game, Native.GWL_STYLE, (nint)(style & ~Native.WS_MAXIMIZE));
            Native.SetWindowLongPtr(game, Native.GWL_EXSTYLE, (nint)ex);
            // where the game was, with the frame around it
            var frame = where;
            Native.AdjustWindowRectEx(ref frame, (uint)style, Native.GetMenu(game) != 0, (uint)ex);
            Native.SetWindowPos(game, 0, frame.Left, Math.Max(frame.Top, where.Top - 40), frame.Width, frame.Height,
                Native.SWP_NOZORDER | Native.SWP_FRAMECHANGED | Native.SWP_SHOWWINDOW | (toDesktop ? 0 : Native.SWP_NOACTIVATE));
            Native.DrawMenuBar(game);
            if (!toDesktop) Native.ShowWindow(game, Native.SW_SHOWMINNOACTIVE);
            // RetroHub was in front (the button was just clicked): the keys go along to the game's window – never otherwise
            else if (foreground) Native.SetForegroundWindow(game);
            Log.Info($"Released window {game:X} ({(toDesktop ? "to the desktop" : "minimized")})");
        }
        catch (Exception e)
        {
            Log.Warn($"Release of {game:X} failed", e);
        }
    }

    // ---- worker -----------------------------------------------------------------------------------------------

    private void Do(Action action)
    {
        if (_worker == null)
        {
            try { action(); }
            catch (Exception ex) { Log.Warn("Window action failed", ex); }
        }
        else Enqueue(action);
    }

    private bool Enqueue(Action action)
    {
        try
        {
            if (_work.IsAddingCompleted) return false;
            _work.Add(action);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false; // completed concurrently (disposing)
        }
    }

    private void Work()
    {
        while (true)
        {
            Action? action = null;
            try
            {
                if (_work.IsCompleted) return;
                _work.TryTake(out action, 250);
            }
            catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
            {
                return;
            }
            if (action != null)
            {
                Interlocked.Exchange(ref _busySince, Stopwatch.GetTimestamp());
                try { action(); }
                catch (Exception ex) { Log.Warn("Window action failed", ex); }
                Interlocked.Exchange(ref _busySince, 0);
            }
            // does the emulator answer? (asked here, so a busy one only makes this thread wait)
            var game = _game;
            if (_watching && game != 0)
                _answers = Native.SendMessageTimeout(game, Native.WM_NULL, 0, 0, Native.SMTO_ABORTIFHUNG, 300, out _) != 0;
        }
    }

    /// <summary>Releases the window to the desktop if still attached, then stops the worker thread.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        try
        {
            // also while a Take is still running for a busy emulator (not attached yet): Release waits for it
            if (_game != 0) Release(toDesktop: true);
        }
        catch (Exception ex)
        {
            Log.Warn("WindowEmbedder.Dispose: release failed", ex);
        }
        _disposed = true;
        foreach (var entry in ByHost)
            if (ReferenceEquals(entry.Value, this)) ByHost.TryRemove(entry);
        _work.CompleteAdding();
        if (_worker != null && Thread.CurrentThread != _worker) _worker.Join(TimeSpan.FromSeconds(1));
    }
}
