using System.ComponentModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using RetroHub.App.Localization;
using RetroHub.App.Services;
using RetroHub.App.ViewModels;
using RetroHub.Core.Catalog;
using RetroHub.Core.Emulation;
using RetroHub.Core.Emulation.Windows;
using RetroHub.Core.Logging;

namespace RetroHub.App.Controls;

/// <summary>
/// The embedded game (LaunchMode.Embedded): takes the emulator window of <see cref="LaunchCoordinator.EmbeddedSession"/>
/// into <see cref="GameScreenHost"/> with the Core <see cref="WindowEmbedder"/> – the state machine proven in MonHub's
/// GameScreen:
/// <list type="number">
/// <item>every 150 ms, while there is no window: <c>session.FindWindows()</c>; the first one is taken once the host is
/// at least 64×64 px (an emulator squeezed to nothing while it sets up its picture can crash). No window after 60 s →
/// the game is left to the desktop and the session continues windowed.</item>
/// <item>while loading the host is masked (empty window region) and the loading view behind it shows; ready when
/// <see cref="EmbedReadiness"/> says so → Settled, unmask, fit, nudge (profile), keys to the game.</item>
/// <item>fit: the host is aspect-fitted to the picture (+ the emulator's measured bars, taken when two samples agree)
/// in the available area; the window is placed in physical pixels on every size/position change (coalesced). A window
/// the emulator re-created is found and taken again.</item>
/// </list>
/// The bar above the host (outside the native window's area – airspace) has Vollbild / Eigenes Fenster / Beenden.
/// <see cref="SetObscured"/> masks the game while an Avalonia overlay (dialog, start transition) must be seen above it.
/// </summary>
public partial class GameScreen : UserControl
{
    /// <summary>Tick of the state machine (as MonHub).</summary>
    public static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(150);
    /// <summary>Take attempts that fail (SetParent refused) before the game is left in its own window.</summary>
    public const int MaxTakeFailures = 3;

    private readonly DispatcherTimer _tick;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly BarSampler _bars = new();
    private readonly BarAutoHide _autoHide = new();
    private LaunchCoordinator? _launch;
    private IGameSession? _session;
    private WindowEmbedder? _embedder;
    private EmbedProfile _profile = new();
    private EmbedReadiness _readiness = new(null);
    private double? _aspect;
    private TimeSpan _attachedAt, _takenAt;
    private bool _everTaken, _masked, _obscured, _fullscreen, _fitQueued;
    private int _takeFailures;
    private PixelPoint _onScreen;
    private (int W, int H) _placed;
    private string _loadingNote = "";
    private int _fits;
    /// <summary>The game (or RetroHub around it) had the foreground at the last tick: RetroHub takes it back when the game ends.</summary>
    private bool _hadForeground;
    private Window? _window;

    public GameScreen()
    {
        InitializeComponent();
        _tick = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TickInterval };
        _tick.Tick += (_, _) => SafeTick();
        Host.Created += _ => Dispatcher.UIThread.Post(() => { ApplyMask(force: true); QueueFit(); });
        Area.SizeChanged += (_, _) => QueueFit();
        // not LayoutUpdated: it is raised after every layout pass anywhere in the window and kept the dispatcher busy
        Host.PropertyChanged += (_, e) => { if (e.Property == BoundsProperty) QueueFit(); };
        SizeChanged += (_, e) =>
        {
            foreach (var b in new[] { FullscreenButton, OwnWindowButton, QuitButton }) b.Classes.Set("narrow", e.NewSize.Width < 1040);
            QueueFit();
        };
        FullscreenButton.Click += (_, _) =>
        {
            FullscreenRequested?.Invoke();
            FocusGameSoon();
        };
        OwnWindowButton.Click += (_, _) => ReleaseToDesktop();
        DataContextChanged += (_, _) => SetCoordinator(DataContext as LaunchCoordinator);
        AttachedToVisualTree += (_, _) =>
        {
            _window = TopLevel.GetTopLevel(this) as Window;
            if (_window == null) return;
            _window.Activated += OnWindowActivated;
            _window.PositionChanged += OnWindowMoved;
        };
        DetachedFromVisualTree += (_, _) =>
        {
            if (_window != null)
            {
                _window.Activated -= OnWindowActivated;
                _window.PositionChanged -= OnWindowMoved;
            }
            _window = null;
            Detach(keepGame: true);
        };
    }

    /// <summary>The bar's "Vollbild" button (the window toggles RetroHub's own full screen, F11).</summary>
    public event Action? FullscreenRequested;

    /// <summary>A game window is inside the host right now.</summary>
    public bool HasGame => _embedder?.IsAttached == true;
    /// <summary>The embedded game finished loading (loading view gone, keys go to it).</summary>
    public bool IsReady => _readiness.IsReady && _session != null;
    /// <summary>The emulator's window (0 when none).</summary>
    public nint GameWindow => _embedder?.GameWindow ?? 0;
    /// <summary>For tests and logs: what the screen measured.</summary>
    public string Measured => $"host {Host.Bounds.Size} bars {_bars.Current} aspect {_aspect:0.###} placed {_placed} fits {_fits}";

    // ================= coordinator =================

    private void SetCoordinator(LaunchCoordinator? launch)
    {
        if (launch == _launch) return;
        if (_launch != null) _launch.PropertyChanged -= OnLaunchChanged;
        _launch = launch;
        if (_launch != null) _launch.PropertyChanged += OnLaunchChanged;
        Sync();
    }

    private void OnLaunchChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LaunchCoordinator.EmbeddedSession)) Sync();
    }

    private void Sync()
    {
        var wanted = _launch?.EmbeddedSession;
        if (ReferenceEquals(wanted, _session)) return;
        Detach(keepGame: false);
        if (wanted != null) Attach(wanted);
    }

    // ================= attach / detach =================

    private void Attach(IGameSession session)
    {
        var plan = session.Plan;
        _session = session;
        _profile = plan.Embed ?? new EmbedProfile();
        _embedder = new WindowEmbedder(_profile);
        _aspect = plan.AspectRatio;
        _readiness = new EmbedReadiness(_profile.ReadyTitleContains);
        _bars.Reset();
        _everTaken = false;
        _takeFailures = 0;
        _attachedAt = _clock.Elapsed;
        _onScreen = default;
        _placed = default;
        var emulator = plan.Emulator?.Def.Name;
        LoadingTitle.Text = Loc.T("gamescreen.loading.title", plan.Game.DisplayTitle);
        _loadingNote = string.IsNullOrEmpty(emulator) ? Loc.T("gamescreen.loading.generic") : Loc.T("gamescreen.loading.note", emulator);
        LoadingNote.Text = _loadingNote + " …";
        LoadingView.IsVisible = true;
        LoadingBar.IsIndeterminate = true;
        Log.Info($"Game screen: embedding '{plan.Game.DisplayTitle}' ({plan.Emulator?.Def.Id}), profile keepFrame={_profile.KeepFrame} offUiThread={_profile.OffUiThread} " +
                 $"mask={_profile.MaskWhileLoading} readyTitle='{_profile.ReadyTitleContains}' aspect={_aspect}");
        TestHooks.Report("embed-attached", ("emulator", plan.Emulator?.Def.Id), ("host", Host.Hwnd.ToString("X")));
        ApplyMask(force: true);
        QueueFit();
        _tick.Start();
    }

    /// <param name="keepGame">The screen goes away while the game runs on (window closing): the game is given back to the desktop.</param>
    private void Detach(bool keepGame)
    {
        _tick.Stop();
        var e = _embedder;
        _embedder = null;
        _session = null;
        if (e != null)
        {
            try
            {
                // a window still inside must never be destroyed with the host
                if (e.GameWindow != 0 && e.Exists) e.Release(toDesktop: keepGame);
                e.Dispose();
            }
            catch (Exception ex) { Log.Warn("Game screen: releasing the game window failed", ex); }
        }
        // the game had the foreground (e.g. RetroArch takes it for its embedded window): when it ends, the keys and the
        // foreground come back to RetroHub instead of whatever window is next
        if (_hadForeground && !keepGame && _window is { } w)
        {
            _hadForeground = false;
            Dispatcher.UIThread.Post(() => { if (_session == null) w.Activate(); }, DispatcherPriority.Background);
        }
        LoadingView.IsVisible = false;
        LoadingBar.IsIndeterminate = false; // no animation while nothing is shown
        if (_masked && Host.Hwnd != 0) WindowEmbedder.SetMasked(Host.Hwnd, false);
        else if (_holes != null && Host.Hwnd != 0) GameScreenNative.SetHoles(Host.Hwnd, []);
        _masked = false;
        _holes = null;
        Host.Width = double.NaN;
        Host.Height = double.NaN;
        Bar.IsVisible = true;
    }

    /// <summary>The main window is closing while a game is embedded: the game goes back to its own window and runs on.</summary>
    public void ReleaseForClose()
    {
        if (_embedder is not { } e || e.GameWindow == 0) return;
        Log.Info("RetroHub closes while a game is embedded – the game runs on in its own window");
        TestHooks.Report("embed-release-close");
        try { e.Release(toDesktop: true); }
        catch (Exception ex) { Log.Warn("Release on close failed", ex); }
    }

    /// <summary>"Eigenes Fenster": the game leaves RetroHub and runs on in the emulator's own window.</summary>
    public void ReleaseToDesktop()
    {
        if (_embedder is not { } e || _launch == null) return;
        Log.Info("Game screen: 'Eigenes Fenster' – releasing the game window to the desktop");
        var game = e.GameWindow;
        if (game != 0) e.Release(toDesktop: true);
        else RestoreSessionWindows();
        // the user just pressed the button, so RetroHub may hand over the foreground: the released window must not end
        // up behind RetroHub (the embedder only activates it when RetroHub itself was the foreground window, not when
        // the embedded window had taken the foreground – RetroArch does)
        if (game != 0 && OperatingSystem.IsWindows()) GameScreenNative.SetForegroundWindow(game);
        // the game left RetroHub on purpose: RetroHub must not take the foreground back from it when the screen detaches
        _hadForeground = false;
        _launch.EmbedReleased(EmbedRelease.User);
    }

    /// <summary>No window to take (or taking failed): whatever comes up stays the emulator's own.</summary>
    private void FallBack(EmbedRelease reason)
    {
        Log.Warn($"Game screen: giving up embedding ({reason}) after {(_clock.Elapsed - _attachedAt).TotalSeconds:0.0} s");
        if (_embedder is { GameWindow: not 0 } e) e.Release(toDesktop: true);
        RestoreSessionWindows();
        _hadForeground = false; // the game runs on in its own window: no foreground grab when the screen detaches
        _launch?.EmbedReleased(reason);
    }

    /// <summary>Embedded starts are minimized without activation: windows that exist are shown normally again.</summary>
    private void RestoreSessionWindows()
    {
        if (_session == null || !OperatingSystem.IsWindows()) return;
        foreach (var w in _session.FindWindows())
            if (GameScreenNative.IsIconic(w)) GameScreenNative.ShowWindowAsync(w, GameScreenNative.SW_RESTORE);
    }

    // ================= the state machine =================

    private void SafeTick()
    {
        try { Tick(); }
        catch (Exception ex) { Log.Error("Game screen tick failed", ex); }
    }

    private void Tick()
    {
        if (_session is not { } session || _embedder is not { } e) return;
        if (session.State is SessionState.Exited or SessionState.Failed) return; // the coordinator tears down
        var now = _clock.Elapsed;

        if (!e.Exists)
        {
            // the first window – or a new one (some emulators build their window again, e.g. after a renderer change)
            var host = Host.Hwnd;
            var (pw, ph) = HostPixels();
            var windows = host != 0 ? session.FindWindows() : [];
            switch (WindowSearch.Decide(now - _attachedAt, _everTaken, windows.Count > 0, pw, ph))
            {
                case WindowSearchStep.Take:
                    TakeWindow(e, windows[0], host, pw, ph, now);
                    break;
                case WindowSearchStep.GiveUp:
                    TestHooks.Report("embed-no-window", ("seconds", Math.Round((now - _attachedAt).TotalSeconds, 1)));
                    FallBack(EmbedRelease.NoWindow);
                    return;
            }
            UpdateLoadingNote(now);
            return;
        }

        if (!_readiness.IsReady)
        {
            if (_readiness.Update(now, e.IsResponsive(), e.Title)) OnReady(now);
            else UpdateLoadingNote(now);
        }
        // the emulator's bars, taken only when two samples agree (see BarSampler)
        if (e.IsAttached && _bars.Add(e.MeasureBars())) QueueFit();
        if (OperatingSystem.IsWindows())
        {
            var fg = GameScreenNative.GetForegroundWindow();
            _hadForeground = fg != 0 && (fg == e.GameWindow || GameScreenNative.GetAncestor(fg, GameScreenNative.GA_ROOT) == OwnHandle);
        }
        Fit();
        if (_fullscreen) AutoHideBar(now);
    }

    private void TakeWindow(WindowEmbedder e, nint window, nint host, int pw, int ph, TimeSpan now)
    {
        bool first = !_everTaken;
        var sw = Stopwatch.StartNew();
        bool ok = e.Take(window, host, pw, ph);
        if (!ok)
        {
            e.Release(toDesktop: false); // forget it (the window was left alone)
            _takeFailures++;
            Log.Warn($"Game screen: taking window {window:X} failed ({_takeFailures}/{MaxTakeFailures})");
            if (_takeFailures >= MaxTakeFailures)
            {
                TestHooks.Report("embed-take-failed");
                FallBack(EmbedRelease.Failed);
            }
            return;
        }
        _everTaken = true;
        _takeFailures = 0;
        _takenAt = now;
        Log.Info($"Game screen: took window {window:X} '{e.Title}' after {(now - _attachedAt).TotalMilliseconds:0} ms ({sw.ElapsedMilliseconds} ms), host {pw}x{ph} px");
        TestHooks.Report(first ? "embed-taken" : "embed-retaken", ("window", window.ToString("X")), ("title", e.Title),
            ("sinceAttachMs", (long)(now - _attachedAt).TotalMilliseconds), ("takeMs", sw.ElapsedMilliseconds), ("hostPx", $"{pw}x{ph}"));
        if (_readiness.IsReady)
        {
            // re-created window of a game that was already running: no loading screen again
            e.Settled();
            ApplyMask();
            if (_profile.NudgeOnMove) e.NudgeMoved();
            FocusGame();
        }
        else _readiness.Taken(now);
        QueueFit();
    }

    private void OnReady(TimeSpan now)
    {
        var e = _embedder!;
        e.Settled();
        Log.Info($"Game screen: ready ({_readiness.Reason}) {(now - _takenAt).TotalMilliseconds:0} ms after the take, title '{e.Title}'");
        TestHooks.Report("embed-ready", ("reason", _readiness.Reason), ("sinceTakeMs", (long)(now - _takenAt).TotalMilliseconds),
            ("sinceAttachMs", (long)(now - _attachedAt).TotalMilliseconds), ("title", e.Title));
        LoadingView.IsVisible = false;
        LoadingBar.IsIndeterminate = false;
        ApplyMask();
        Fit();
        // shown again: DeSmuME only draws once it is told its window moved
        if (_profile.NudgeOnMove) e.NudgeMoved();
        FocusGame();
        if (TestHooks.Enabled) DispatcherTimer.RunOnce(ReportPicture, TimeSpan.FromSeconds(2));
    }

    private void UpdateLoadingNote(TimeSpan now)
    {
        var dots = new string('.', 1 + (int)(now.Ticks / TimeSpan.FromMilliseconds(400).Ticks % 3));
        LoadingNote.Text = _loadingNote + " " + dots;
    }

    // ================= mask / focus =================

    /// <summary>An Avalonia overlay (dialog, start transition) must be visible above the game area: the game is masked meanwhile.</summary>
    public void SetObscured(bool obscured)
    {
        if (_obscured == obscured) return;
        _obscured = obscured;
        ApplyMask();
        if (obscured) TakeKeyboardBack();
        else FocusGameSoon();
    }

    /// <summary>
    /// A dialog now lies above the (masked) game: the Win32 keyboard focus may still be on the emulator's window, so
    /// Enter/arrows would go to the invisible game instead of the dialog. RetroHub's own window takes it back (own
    /// window, input queues of a cross-process child are attached, so SetFocus works; never blocks).
    /// </summary>
    private void TakeKeyboardBack()
    {
        if (!OperatingSystem.IsWindows() || _embedder is not { GameWindow: not 0 } e || OwnHandle is var own && own == 0) return;
        // only when the keys really are with the game (RetroHub in front); anything else is left alone
        var focus = GameScreenNative.KeyboardFocus();
        if (focus == 0 || focus == own || !IsInsideGame(focus, e.GameWindow)) return;
        GameScreenNative.SetFocus(own);
        TestHooks.Report("keyboard-back", ("ok", GameScreenNative.KeyboardFocus() == own));
    }

    private static bool IsInsideGame(nint window, nint game)
    {
        for (var w = window; w != 0; w = GameScreenNative.GetParent(w))
            if (w == game) return true;
        return false;
    }

    private void ApplyMask(bool force = false)
    {
        var host = Host.Hwnd;
        if (host == 0) return;
        bool want = _session != null && (_obscured || (_profile.MaskWhileLoading && !_readiness.IsReady));
        if (want != _masked || force)
        {
            WindowEmbedder.SetMasked(host, want);
            _masked = want;
            _holes = null; // the region was replaced
        }
        UpdateHoles();
    }

    // ================= holes for toasts (airspace) =================

    private Control? _holeSource;
    /// <summary>What was cut out of the host last (null: no region of ours on it).</summary>
    private (PixelHole Hole, PixelPoint Origin)? _holes;

    /// <summary>
    /// Avalonia content that has to stay visible above the game (the toast stack): where it overlaps the host, a hole is
    /// cut into the host (only there – the game stays visible around it). Kept up to date on every fit.
    /// </summary>
    public void SetHoleSource(Control? source)
    {
        _holeSource = source;
        UpdateHoles();
    }

    /// <summary>Re-checks the holes now (e.g. a toast appeared or went away).</summary>
    public void UpdateHoles()
    {
        var host = Host.Hwnd;
        if (host == 0 || _masked || !OperatingSystem.IsWindows()) return;
        (PixelHole, PixelPoint)? wanted = null;
        if (_session != null && _holeSource is { IsEffectivelyVisible: true } src && TopLevel.GetTopLevel(Host) != null
            && src.TranslatePoint(default, Host) is { } at
            && GameScreenCutout.Compute(at.X, at.Y, src.Bounds.Width, src.Bounds.Height, Host.Bounds.Width, Host.Bounds.Height, Scale) is { } hole)
            wanted = (hole, Host.PointToScreen(default));
        if (wanted == _holes) return;
        _holes = wanted;
        if (wanted is { } w)
        {
            var (h, o) = w;
            GameScreenNative.SetHoles(host, [new GameScreenNative.RECT(o.X + h.Left, o.Y + h.Top, o.X + h.Right, o.Y + h.Bottom)]);
            TestHooks.Report("embed-hole", ("px", $"{h.Left},{h.Top}-{h.Right},{h.Bottom}"));
        }
        else
        {
            GameScreenNative.SetHoles(host, []);
            TestHooks.Report("embed-hole", ("px", null));
        }
    }

    /// <summary>Keys to the game – only while RetroHub is the active window and nothing of RetroHub's lies above it.</summary>
    public void FocusGame()
    {
        if (_embedder is { IsAttached: true } e && _readiness.IsReady && !_obscured && _window is { IsActive: true })
            e.Focus();
    }

    public void FocusGameSoon() => Dispatcher.UIThread.Post(FocusGame, DispatcherPriority.Background);

    private void OnWindowActivated(object? sender, EventArgs e) => FocusGameSoon();

    private void OnWindowMoved(object? sender, PixelPointEventArgs e) => QueueFit();

    // ================= fit =================

    private void QueueFit()
    {
        if (_fitQueued || _session == null) return;
        _fitQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _fitQueued = false;
            try { Fit(); }
            catch (Exception ex) { Log.Warn("Game screen fit failed", ex); }
        }, DispatcherPriority.Render);
    }

    private double Scale => TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;

    private nint OwnHandle => _window?.TryGetPlatformHandle()?.Handle ?? 0;

    /// <summary>The host's size right now in physical pixels (what Avalonia gives it; the native window follows a moment later).</summary>
    private (int W, int H) HostPixels()
    {
        var b = Host.Bounds;
        if (b.Width > 0 && b.Height > 0) return GameScreenFit.ToPixels(b.Width, b.Height, Scale);
        return GameScreenFit.ToPixels(double.IsNaN(Host.Width) ? 0 : Host.Width, double.IsNaN(Host.Height) ? 0 : Host.Height, Scale);
    }

    private void Fit()
    {
        if (_session == null) return;
        _fits++;
        var scale = Scale;
        var area = Area.Bounds.Size;
        var bars = _bars.Current ?? ((int)Math.Round(_profile.BarTop * scale), (int)Math.Round(_profile.BarBottom * scale));
        var fit = GameScreenFit.Compute(area.Width, area.Height, scale, _aspect, bars.Item1, bars.Item2);
        if (fit.IsEmpty) return;
        if (GameScreenFit.Differs(Host.Width, fit.Width)) Host.Width = fit.Width;
        if (GameScreenFit.Differs(Host.Height, fit.Height)) Host.Height = fit.Height;
        if (_embedder is not { IsAttached: true } e) return;
        var (pw, ph) = HostPixels();
        if (pw <= 0 || ph <= 0) (pw, ph) = (fit.PixelWidth, fit.PixelHeight);
        e.Place(0, 0, pw, ph);
        if (_placed != (pw, ph))
        {
            _placed = (pw, ph);
            TestHooks.Report("embed-placed", ("px", $"{pw}x{ph}"), ("bars", $"{bars.Item1},{bars.Item2}"), ("scale", scale));
        }
        // the place moved on the screen (window dragged, full screen): a child moved with its parent gets no WM_MOVE
        if (_window != null && TopLevel.GetTopLevel(Host) != null)
        {
            var onScreen = Host.PointToScreen(default);
            if (onScreen != _onScreen)
            {
                bool moved = _onScreen != default;
                _onScreen = onScreen;
                if (moved && _profile.NudgeOnMove && _readiness.IsReady) e.NudgeMoved();
            }
        }
        UpdateHoles();
    }

    // ================= full screen =================

    /// <summary>RetroHub's window went into/out of full screen: black around the picture, the bar only at the top edge.</summary>
    public void SetFullscreen(bool on)
    {
        if (_fullscreen == on) return;
        _fullscreen = on;
        FullscreenText.Text = Loc.T(on ? "gamescreen.fullscreenExit" : "gamescreen.fullscreen");
        Root.Background = on ? Brushes.Black : Brushes.Transparent;
        Area.Margin = on ? default : new Thickness(24, 20, 24, 24);
        Bar.Padding = on ? new Thickness(28, 0, 28, 0) : new Thickness(28, 0, 152, 0);
        Bar.IsVisible = true;
        if (on) _autoHide.Start(_clock.Elapsed);
        QueueFit();
    }

    private void AutoHideBar(TimeSpan now)
    {
        if (TopLevel.GetTopLevel(this) == null || GameScreenNative.Cursor() is not { } cursor) return;
        var top = this.PointToScreen(default).Y;
        int? barBottom = Bar.IsVisible ? top + (int)Math.Round(Bar.Bounds.Height * Scale) : null;
        bool show = _autoHide.ShouldShow(now, cursor.Y, top, barBottom);
        if (Bar.IsVisible == show) return;
        Bar.IsVisible = show;
        QueueFit();
    }

    // ================= test report =================

    /// <summary>Dev/test only: is a picture drawn inside the host, is the window really a child, who has the foreground.</summary>
    private void ReportPicture()
    {
        if (_embedder is not { } e || Host.Hwnd == 0 || !OperatingSystem.IsWindows()) return;
        GameScreenNative.GetWindowRect(Host.Hwnd, out var r);
        var fg = GameScreenNative.GetForegroundWindow();
        var own = OwnHandle;
        var focus = GameScreenNative.KeyboardFocus();
        var printed = GameScreenNative.SampleWindow(own, r, printWindow: true);
        var screen = GameScreenNative.SampleWindow(own, r, printWindow: false);
        TestHooks.Report("embed-picture", ("hostRect", $"{r.Left},{r.Top},{r.Right - r.Left}x{r.Bottom - r.Top}"),
            ("printWindowNonBlack", Math.Round(printed.NonBlack, 3)), ("printWindowColors", printed.Colors),
            ("screenNonBlack", Math.Round(screen.NonBlack, 3)), ("screenColors", screen.Colors),
            ("parentIsHost", GameScreenNative.GetParent(e.GameWindow) == Host.Hwnd),
            ("retroHubForeground", fg == own), ("foregroundIsGame", fg == e.GameWindow),
            ("keyboardFocusOnGame", focus != 0 && (focus == e.GameWindow || GameScreenNative.GetParent(focus) == e.GameWindow)),
            ("measured", Measured));
    }
}
