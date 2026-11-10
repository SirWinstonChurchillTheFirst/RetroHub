using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using RetroHub.App.Controls;
using RetroHub.App.Input;
using RetroHub.App.Services;
using RetroHub.App.Theming;
using RetroHub.App.ViewModels;
using RetroHub.App.Views.Pages;
using RetroHub.Core.Logging;

namespace RetroHub.App.Views;

/// <summary>One row of the right-click list: icon resource key, label, action; <paramref name="Lit"/> = filled in the favourite colour.</summary>
internal sealed record ContextEntry(string Icon, string Label, Action Run, bool Lit = false);

/// <summary>Implemented by page views: where the focus goes when the page is shown (keyboard/controller).</summary>
public interface IPageView
{
    bool FocusDefault();
}

/// <summary>
/// The shell window: own title bar over the kept OS frame (Avalonia 12: ExtendClientAreaToDecorationsHint +
/// WindowDecorations.BorderOnly, roles via WindowDecorationProperties.ElementRole so Windows still provides snap,
/// resize and – on the maximize button – the snap-layouts flyout), header with channels, keep-alive page host, footer, toasts,
/// launch and dialog overlays, and the central <see cref="NavAction"/> router for keyboard and controller.
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private WindowState _beforeFullscreen = WindowState.Normal;
    private bool _pointerMode;

    // parameterless constructor for the XAML previewer only
    public MainWindow() : this(null!) { }

    public MainWindow(MainViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
        if (vm == null) return;
        DataContext = vm;

        // drag area = the bare strip behind the header + the word mark; never an ancestor of a button. Avalonia 12.1.5's
        // Win32 WindowImpl answers WM_NCHITTEST with the nearest ElementRole above the hit visual (TitleBar → HTCAPTION,
        // User → HTCLIENT; none → Win32Properties.NonClientHitTestResult, default Client). The old role on the whole
        // TopBar grid made every channel and tool button HTCAPTION: Windows started a window move instead of a click
        // (R3 owner test – keyboard worked, the mouse did not). Every header button also says User explicitly, so no
        // later role on a container can swallow it again (MouseReachabilityTests pins this).
        WindowDecorationProperties.SetElementRole(DragBar, WindowDecorationsElementRole.TitleBar);
        WindowDecorationProperties.SetElementRole(BrandBar, WindowDecorationsElementRole.TitleBar);
        TopBar.Styles.Add(new Avalonia.Styling.Style(x => Avalonia.Styling.Selectors.OfType<Button>(x))
        {
            Setters = { new Avalonia.Styling.Setter(WindowDecorationProperties.ElementRoleProperty, WindowDecorationsElementRole.User) },
        });
        WindowDecorationProperties.SetElementRole(DemoChip, WindowDecorationsElementRole.User);
        WindowDecorationProperties.SetElementRole(MinimizeButton, WindowDecorationsElementRole.MinimizeButton);
        WindowDecorationProperties.SetElementRole(MaximizeButton, WindowDecorationsElementRole.MaximizeButton);
        WindowDecorationProperties.SetElementRole(CloseButton, WindowDecorationsElementRole.CloseButton);
        MinimizeButton.Click += (_, _) => WindowState = WindowState.Minimized;
        MaximizeButton.Click += (_, _) => ToggleMaximize();
        CloseButton.Click += (_, _) => Close();
        // on Win32 a double click into the caption is the OS's own (it never reaches Avalonia); this covers the rest
        DragBar.DoubleTapped += (_, e) => { if (e.Source == DragBar) ToggleMaximize(); };

        // auto-repeat detection (DESIGN 8.4): ".nav-repeat" on the root while a direction is held
        NavRepeatTracker.Current = _repeat;
        _repeat.Changed += OnRepeatChanged;

        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, (_, e) => _repeat.OnKeyUp(e.Key), RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerPressedEvent, (_, _) => _pointerMode = true, RoutingStrategies.Tunnel, handledEventsToo: true);
        // the mouse's back button is Back everywhere (dialogs, a page's open fold or sheet, the back stack) – not while a
        // game runs inside RetroHub (the emulator keeps its buttons)
        AddHandler(PointerPressedEvent, OnMouseBackButton, RoutingStrategies.Tunnel, handledEventsToo: true);
        // footer hints are mouse targets (R2): the same action as their key/button
        HintBar.AddHandler(Button.ClickEvent, OnHintClick, RoutingStrategies.Bubble);
        // the right-click list: a click beside it closes it (and does nothing else); a page change closes it too
        ContextLayer.PointerPressed += (_, e) =>
        {
            if (e.Source != ContextLayer) return;
            e.Handled = true;
            HideContext(restoreFocus: true);
        };
        vm.PageShown += (_, _) => HideContext(restoreFocus: false);
        DialogScrim.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(DialogScrim).Properties.IsLeftButtonPressed) return;
            e.Handled = true;
            _vm.Dialogs.Current?.Cancel();
        };
        // scroll bars show only while scrolling (Styles.axaml: ScrollViewer.scrolling)
        AddHandler(ScrollViewer.ScrollChangedEvent, OnAnyScroll, RoutingStrategies.Bubble, handledEventsToo: true);
        SizeChanged += (_, e) =>
        {
            vm.WindowWidth = e.NewSize.Width;
            ApplyScale(e.NewSize);
        };
        Activated += (_, _) => { Motion.Refresh(); CaptionButtons.Opacity = 1; };
        Deactivated += (_, _) => CaptionButtons.Opacity = 0.55;

        Motion.Changed += ApplyMotionClass;
        ApplyMotionClass();
        ApplyScale(new Size(Width, Height));
        Pages.LayoutUpdated += (_, _) => UpdateStage();
        vm.PageShown += OnPageShown;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.CurrentPage)) WatchPage(vm.CurrentPage);
            else if (e.PropertyName == nameof(MainViewModel.ShowOsd))
            {
                Osd.Enabled = vm.ShowOsd;
                if (!vm.ShowOsd) Osd.HideNow();
            }
        };
        Osd.Enabled = vm.ShowOsd;
        vm.Dialogs.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(DialogService.Current)) return;
            OnOverlayChanged();
            UpdateGameObscured();
            Dispatcher.UIThread.Post(FocusDialog, DispatcherPriority.Loaded);
        };
        vm.Launch.PropertyChanged += (_, e) => OnLaunchChanged(e.PropertyName);
        vm.Launch.Returned += OnGameReturned;
        // after a session: the TV's OSD top right in the system's light, no notification card (QA 2)
        vm.Launch.SessionReturned += notice =>
            Osd.ShowNotice(notice.Total, notice.Session, SystemPalette.Get(notice.Game.SystemId, notice.Game.Accent).Light);
        GameScreen.FullscreenRequested += () => SetFullscreen(WindowState != WindowState.FullScreen);
        // the power-on (DESIGN 9.3): the flight dims the launch overlay and opens its start surface
        Flight.Scrim = LaunchScrim;
        Flight.Reveal = StartPanel;
        Flight.LandingSlot = StartBoxSlot;
        Flight.LandingImage = StartBoxImage;
        Flight.LandingArt = StartBoxArt;
        // toasts lie above the embedded game: the game screen cuts a hole for them into its native host (airspace)
        GameScreen.SetHoleSource(ToastDock);
        ToastHost.ContainerPrepared += (_, e) => SlideInToast(e.Container);
        ChannelList.SizeChanged += (_, _) => UpdateChannelMark(animate: false);
        ToastDock.SizeChanged += (_, _) => Dispatcher.UIThread.Post(GameScreen.UpdateHoles, DispatcherPriority.Render);
        Closing += (_, _) =>
        {
            // a game inside RetroHub would go down with the window: it goes back to its own window and runs on
            // (the session service keeps session.json, the next start re-attaches it)
            if (vm.Launch.IsEmbedded) GameScreen.ReleaseForClose();
        };
        vm.Ctx.Services.Input.ActionRaised += (a, isRepeat) => Dispatcher.UIThread.Post(() =>
        {
            if (!vm.Ctx.Services.Settings.Current.ControllerEnabled) return; // setting "Mit Controller bedienen"
            // SDL delivers pad events in the background too: only steer RetroHub while it is the active window
            // (otherwise the pad would navigate RetroHub while the user plays or works in another program).
            if (!IsActive) return;
            _pointerMode = false;
            vm.KeyboardGlyphs = false;
            if (a is NavAction.Up or NavAction.Down or NavAction.Left or NavAction.Right) _repeat.OnPadDirection(isRepeat);
            Execute(a);
        });
        vm.Ctx.Services.Input.DirectionReleased += () => Dispatcher.UIThread.Post(_repeat.OnPadReleased);

        var openedOnce = false;
        Opened += (_, _) =>
        {
            // raised again when the window is shown after Hide() (fullscreen hand-off): start-up work only once
            if (openedOnce) return;
            openedOnce = true;
            if (vm.Ctx.Services.Settings.Current.StartFullscreen) SetFullscreen(true);
            if (vm.CurrentPage == null) vm.Navigator.Navigate(PageKey.Home);
            // a game still running from before a crash/close (re-attached by the session service): show it
            Dispatcher.UIThread.Post(() =>
            {
                if (vm.Ctx.Services.Sessions.Current is { } running) vm.Launch.AttachRecovered(running);
            }, DispatcherPriority.Background);
        };
        PropertyChanged += (_, e) =>
        {
            if (e.Property == WindowStateProperty) UpdateWindowState();
        };
        // the shell must not wait for Opened in headless tests (no Opened without a platform window): navigate now
        if (vm.CurrentPage == null) vm.Navigator.Navigate(PageKey.Home);
    }

    private readonly Dictionary<ScrollViewer, DispatcherTimer> _scrollTimers = new();

    private void OnAnyScroll(object? sender, ScrollChangedEventArgs e)
    {
        if (e.Source is not ScrollViewer sv || e.OffsetDelta == default) return;
        sv.Classes.Set("scrolling", true);
        if (!_scrollTimers.TryGetValue(sv, out var timer))
        {
            timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                sv.Classes.Set("scrolling", false);
                _scrollTimers.Remove(sv);
            };
            _scrollTimers[sv] = timer;
        }
        timer.Stop();
        timer.Start();
    }

    public MainViewModel ViewModel => _vm;
    internal PageHost PageHost => Pages;

    /// <summary>Brings the window to the front (second start of RetroHub).</summary>
    public void BringToFront()
    {
        // a fullscreen emulator owns the screen right now: RetroHub comes back when it exits
        if (_hiddenForGame) return;
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Show();
        Activate();
        Topmost = true;
        Topmost = false;
    }

    // ================= running game (launch coordinator) =================

    private bool _hiddenForGame;
    private bool _focusPlayAfterReturn;

    internal GameScreen GameScreenControl => GameScreen;

    private void OnLaunchChanged(string? property)
    {
        var launch = _vm.Launch;
        switch (property)
        {
            case nameof(LaunchCoordinator.Phase):
                OnPowerPhase(launch.Phase);
                OnOverlayChanged();
                UpdateGameObscured();
                if (launch.Phase == LaunchPhase.Running) Dispatcher.UIThread.Post(FocusRunningGame, DispatcherPriority.Loaded);
                break;
            case nameof(LaunchCoordinator.Presentation):
                // e.g. "Eigenes Fenster": the running overlay replaces the game screen - its button gets the focus
                if (launch.IsRunning) Dispatcher.UIThread.Post(FocusRunningGame, DispatcherPriority.Loaded);
                break;
            case nameof(LaunchCoordinator.IsShellHidden):
                if (launch.IsShellHidden) HideForGame();
                else ShowAfterGame();
                break;
        }
    }

    private LaunchPhase _powerPhase = LaunchPhase.Idle;
    private bool _gameShown;

    /// <summary>
    /// The staged moment of DESIGN 9.3: Starting → power-on from the box the game was started from (the OSD shows the
    /// system code for 300 ms while the line grows); Running → the transition's own parts go (the start surface was
    /// already open); back to Idle after a session that was shown → power-off; a start that failed → no switch-off.
    /// </summary>
    private void OnPowerPhase(LaunchPhase phase)
    {
        var before = _powerPhase;
        _powerPhase = phase;
        // the game screen (masked, still loading) must not show through the flight and the opening line
        GameScreenPanel.Opacity = phase == LaunchPhase.Starting ? 0 : 1;
        var game = _vm.Launch.Game;
        var light = game != null ? SystemPalette.Get(game.SystemId, game.Accent).Light : ThemeManager.CurrentAccent;
        switch (phase)
        {
            case LaunchPhase.Starting when before == LaunchPhase.Idle:
                _gameShown = false;
                _powerLight = light;
                // the staged moment is the system's: the segment light in its Licht, the dark segments in its Wand hell
                // (Cyan stays the app's own progress, DESIGN 2.2)
                if (game != null)
                {
                    var look = SystemPalette.Get(game.SystemId, game.Accent);
                    StartSegments.Lit = look.LightBrush;
                    StartSegments.Unlit = look.WallHiBrush;
                }
                Flight.PowerOn(FindLaunchBox(game), light, Motion.Current);
                if (game != null)
                {
                    var code = game.SystemShort;
                    var delay = Motion.Current == EffectiveMotion.Full ? TimeSpan.FromMilliseconds(FlightLayer.FlyEnd) : TimeSpan.Zero;
                    DispatcherTimer.RunOnce(() =>
                    {
                        if (_vm.Launch.IsStarting) Osd.Show(null, code, light, TimeSpan.FromMilliseconds(300), nameSize: 40);
                    }, delay <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(1) : delay);
                }
                break;
            case LaunchPhase.Running:
                _gameShown = true;
                Flight.EndPowerOn();
                break;
            case LaunchPhase.Idle:
                if (_gameShown && !_hiddenForGame) Flight.PowerOff(_powerLight, Motion.Current);
                else if (_gameShown) Flight.PowerOff(_powerLight, EffectiveMotion.Off);
                else Flight.Cancel();
                _gameShown = false;
                break;
        }
    }

    private Color _powerLight = Color.Parse("#2FD3F5");

    /// <summary>
    /// The box the game was started from: a visible cover of that game on the current page – the one inside the focused
    /// element first (the stage box, the pulled box of the detail), else the largest one.
    /// </summary>
    private CoverArt? FindLaunchBox(GameItemViewModel? game)
    {
        if (game == null || Pages.Current is not { } page) return null;
        var arts = page.GetVisualDescendants().OfType<CoverArt>()
            .Where(a => a.IsEffectivelyVisible && a.Opacity > 0 && a.DataContext is GameItemViewModel g && g.Id == game.Id && a.ArtRect.Width > 4)
            .ToList();
        if (arts.Count == 0) return null;
        var focused = FocusManager?.GetFocusedElement() as Visual;
        return arts.FirstOrDefault(a => focused != null && (focused.IsVisualAncestorOf(a) || a == focused))
               ?? arts.OrderByDescending(a => a.ArtRect.Width * a.ArtRect.Height).First();
    }

    /// <summary>Design previews: one frame of the power-on (or the switch-off) on the current page.</summary>
    internal FlightLayer FlightLayerControl => Flight;

    private void FocusRunningGame()
    {
        var launch = _vm.Launch;
        if (!launch.IsRunning || _vm.Dialogs.Current != null) return;
        if (launch.ShowRunningOverlay) StopButton.Focus(NavigationMethod.Directional);
        else if (launch.ShowGameScreen)
        {
            // nothing destructive under the keyboard focus (Enter must not quit the game); the keys go to the game when ready
            GameScreen.Focus(NavigationMethod.Directional);
            GameScreen.FocusGameSoon();
        }
    }

    /// <summary>The start transition or a dialog lies above the embedded game: the game is masked meanwhile (airspace).</summary>
    private void UpdateGameObscured() => GameScreen.SetObscured(_vm.Dialogs.Current != null || _vm.Launch.IsStarting);

    /// <summary>Fullscreen hand-off: the emulator owns the screen; RetroHub hides (no rendering, no animations).</summary>
    private void HideForGame()
    {
        if (_hiddenForGame) return;
        _hiddenForGame = true;
        // RetroHub is the foreground window right now, so it may hand the foreground to the emulator
        var emu = _vm.Launch.HandoffWindow;
        if (emu != 0 && OperatingSystem.IsWindows()) GameScreenNative.SetForegroundWindow(emu);
        // the game belongs on the monitor RetroHub is on (emulators like Dolphin open where they like)
        var own = TryGetPlatformHandle()?.Handle ?? 0;
        if (emu != 0 && RetroHub.Core.Emulation.Windows.MonitorPlacement.MonitorOf(own) is { } monitor)
            _ = KeepGameOnMonitorAsync(emu, monitor);
        Log.Info("Fullscreen hand-off: hiding the RetroHub window");
        Hide();
        TestHooks.Report("shell-hidden");
    }

    /// <summary>
    /// For the first seconds of a fullscreen game: if the emulator's window sits on another monitor than RetroHub's
    /// (or switches there while it enters fullscreen), it is moved over. Stops early when the game ends.
    /// </summary>
    private async Task KeepGameOnMonitorAsync(nint emu, RetroHub.Core.Emulation.Windows.MonitorArea monitor)
    {
        var moves = 0;
        foreach (var delay in new[] { 0, 400, 800, 1500, 2500, 4000 })
        {
            if (delay > 0) await Task.Delay(delay);
            if (!_hiddenForGame || _vm.Launch.HandoffWindow != emu) return;
            if (RetroHub.Core.Emulation.Windows.MonitorPlacement.MoveTo(emu, monitor))
            {
                moves++;
                TestHooks.Report("moved-to-monitor", ("window", emu.ToString("X")), ("monitor", monitor.Bounds.ToString()));
                if (moves >= 3)
                {
                    Log.Warn($"Emulator window {emu:X} keeps returning to another monitor – leaving it there");
                    return;
                }
            }
        }
    }

    /// <summary>Back from a fullscreen game (or the hard-kill question needs the window): shown, activated, in front.</summary>
    private void ShowAfterGame()
    {
        if (!_hiddenForGame) return;
        _hiddenForGame = false;
        Log.Info("Showing the RetroHub window again");
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        var own = TryGetPlatformHandle()?.Handle ?? 0;
        if (own != 0) RetroHub.Core.Emulation.Windows.Foreground.BringToFront(own);
        Motion.Refresh();
        if (TestHooks.Enabled)
            Dispatcher.UIThread.Post(() => TestHooks.Report("shell-shown", ("visible", IsVisible), ("active", IsActive),
                ("foreground", OperatingSystem.IsWindows() && GameScreenNative.GetForegroundWindow() == own)), DispatcherPriority.Background);
    }

    private void OnGameReturned(GameItemViewModel? item, Core.Emulation.IGameSession session)
    {
        ShowAfterGame();
        // back at the game detail: the controller is on "Spielen" again
        _focusPlayAfterReturn = true;
    }

    // ================= overlays (dialog, launch) =================

    /// <summary>The shell element that had the focus before a modal overlay opened (restored when it closes).</summary>
    private IInputElement? _focusBeforeOverlay;
    private bool _overlayOpen;

    private void OnOverlayChanged()
    {
        var open = _vm.Dialogs.Current != null || _vm.Launch.IsActive;
        if (open == _overlayOpen) return;
        _overlayOpen = open;
        if (open)
        {
            var focused = FocusManager?.GetFocusedElement();
            _focusBeforeOverlay = focused is Visual v && IsInside(v, Shell) ? focused : null;
            return;
        }
        var restore = _focusBeforeOverlay;
        _focusBeforeOverlay = null;
        Dispatcher.UIThread.Post(() =>
        {
            if (_overlayOpen) return;
            var method = _pointerMode ? NavigationMethod.Pointer : NavigationMethod.Directional;
            if (_focusPlayAfterReturn)
            {
                _focusPlayAfterReturn = false;
                if (Pages.Current is GameDetailPage detail && detail.FocusDefault()) return;
            }
            if (restore is Visual rv && IsInside(rv, Shell) && restore.IsEffectivelyVisible && restore.IsEffectivelyEnabled && restore.Focus(method)) return;
            // the element is gone (e.g. the game ended on another page): keep the controller usable
            if (FocusManager?.GetFocusedElement() is not Visual f || !IsInside(f, Shell) || f is InputElement { IsEffectivelyVisible: false })
                if (Pages.Current != null) FocusPage(Pages.Current, preferRemembered: true);
        }, DispatcherPriority.Loaded);
    }

    private void ToggleMaximize() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    public void SetFullscreen(bool on)
    {
        if (on == (WindowState == WindowState.FullScreen)) return;
        if (on)
        {
            _beforeFullscreen = WindowState == WindowState.Minimized ? WindowState.Normal : WindowState;
            WindowState = WindowState.FullScreen;
        }
        else WindowState = _beforeFullscreen;
    }

    private void UpdateWindowState()
    {
        var full = WindowState == WindowState.FullScreen;
        _vm.IsFullscreen = full;
        CaptionButtons.IsVisible = !full;
        GameScreen.SetFullscreen(full);
        if (this.TryFindResource(WindowState == WindowState.Maximized ? "Glyph.Restore" : "Glyph.Maximize", out var glyph))
            MaximizeButton.Tag = glyph;
    }

    private void ApplyMotionClass()
    {
        foreach (var m in new[] { EffectiveMotion.Full, EffectiveMotion.Reduced, EffectiveMotion.Off })
            Root.Classes.Set(Motion.ClassName(m), m == Motion.Current);
        ThemeManager.ApplyBoardLightTransition();
        Backdrop.ApplyTransitions();
    }

    // ================= UI scale (DESIGN 7.0) =================

    private readonly NavRepeatTracker _repeat = new();
    private double _scale = -1;

    /// <summary>
    /// s = clamp(min(W/1920, H/1080), 0.80, 2.0), quantised to 1/120 (so 1600×900 → 0.833 = 1920×1080 logical,
    /// 2560×1440 → 1.333). Applied to the shell and the dialogs only; room light, game screen and OSD stay unscaled.
    /// </summary>
    public static double ComputeScale(Size client)
    {
        if (client.Width <= 0 || client.Height <= 0 || double.IsNaN(client.Width) || double.IsNaN(client.Height)) return 1;
        var s = Math.Clamp(Math.Min(client.Width / 1920, client.Height / 1080), 0.80, 2.0);
        return Math.Round(s * 120) / 120;
    }

    /// <summary>The current UI scale (tests, diagnostics).</summary>
    public double UiScale => _scale <= 0 ? 1 : _scale;

    private void ApplyScale(Size client)
    {
        var s = ComputeScale(client);
        if (Math.Abs(s - _scale) < 0.0001) return;
        _scale = s;
        ScaleHost.LayoutTransform = new ScaleTransform(s, s);
        StartScale.LayoutTransform = new ScaleTransform(s, s);
        Flight.Scale = s;
        DialogScaleHost.LayoutTransform = new ScaleTransform(s, s);
        Osd.Scale = s;
        ToastDock.LayoutTransform = new ScaleTransform(s, s);
        var logical = client.Width / s;
        // the footer message covers the location sentence's half of the footer, never the hints on the right
        ToastHost.Width = Math.Max(480, Math.Round(logical * 0.5));
        _vm.LogicalWidth = logical;
        var margin = logical >= 1600 ? 96 : 56;
        Osd.PageMargin = margin;
        // the caption buttons are not scaled: keep 170 REAL pixels free for them
        TopBar.Margin = new Thickness(margin == 96 ? 48 : 32, 0, 170 / s, 0);
        if (Application.Current is { } app && (_margin != margin))
        {
            _margin = margin;
            app.Resources["RhPageInset"] = new Thickness(margin, 24, margin, 48);
            app.Resources["RhGridInset"] = new Thickness(margin, 48, margin, 64);
            app.Resources["RhPageInsetX"] = new Thickness(margin, 0, margin, 0);
            app.Resources["RhShelfInset"] = new Thickness(margin, 40, margin, 64);
            app.Resources["RhListInset"] = new Thickness(margin, 16, 0, 48);
        }
    }

    private double _margin;

    private void OnRepeatChanged(bool active)
    {
        Root.Classes.Set("nav-repeat", active);
        if (!active) Backdrop.OnRepeatEnded();
    }

    // ================= room light =================

    private PageViewModel? _watched;

    private void WatchPage(PageViewModel? page)
    {
        if (_watched != null) _watched.PropertyChanged -= OnWatchedPageChanged;
        _watched = page;
        if (page == null) { ApplyHeaderTones(null); return; }
        page.PropertyChanged += OnWatchedPageChanged;
        Backdrop.Show(page.Backdrop);
        ApplyHeaderTones(page);
    }

    private void OnWatchedPageChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is not PageViewModel p) return;
        if (e.PropertyName == nameof(PageViewModel.Backdrop)) Backdrop.Show(p.Backdrop);
        else if (e.PropertyName is nameof(PageViewModel.HeaderGround) or nameof(PageViewModel.HeaderDim)) ApplyHeaderTones(p);
    }

    /// <summary>The page's ground and overlay dim under the header's content (transparent = the room shows through).</summary>
    private void ApplyHeaderTones(PageViewModel? page)
    {
        HeaderGround.Background = page == null || page.HeaderGround.A == 0 ? null : new ImmutableSolidColorBrush(page.HeaderGround);
        HeaderDim.Background = page == null || page.HeaderDim.A == 0 ? null : new ImmutableSolidColorBrush(page.HeaderDim);
    }

    /// <summary>The stage board of the current page (named "StageBoard") ends the TV light; its top in window DIPs.</summary>
    private void UpdateStage()
    {
        if (Pages.Current?.FindControl<Control>("StageBoard") is { IsEffectivelyVisible: true } board
            && board.TranslatePoint(new Point(0, 0), this) is { } p)
            Backdrop.SetStageY(p.Y);
        else Backdrop.SetStageY(double.NaN);
    }

    /// <summary>The channel display after a switch by keyboard/controller.</summary>
    private void ShowChannel(NavItemViewModel? channel)
    {
        if (channel?.Channel == null) return;
        Osd.Show(channel.Channel, channel.Label, ThemeManager.CurrentAccent);
    }

    // ================= pages =================

    private Control CreatePageView(PageKey key, PageViewModel vm) => key switch
    {
        PageKey.Home => new HomePage { DataContext = vm },
        PageKey.Library => new LibraryPage { DataContext = vm },
        PageKey.Systems => new SystemsPage { DataContext = vm },
        PageKey.System => new SystemPage { DataContext = vm },
        PageKey.GameDetail => new GameDetailPage { DataContext = vm },
        PageKey.Emulators => new EmulatorsPage { DataContext = vm },
        PageKey.Imports => new ImportsPage { DataContext = vm },
        PageKey.Settings => new SettingsPage { DataContext = vm },
        _ => new TextBlock { Text = key.ToString() },
    };

    private void OnPageShown(NavigationEntry entry, PageViewModel page)
    {
        var focused = FocusManager?.GetFocusedElement();
        if (Pages.Current != null) Pages.RememberFocus(Pages.Current, focused);
        // going back from a game's detail: the stage box is copied before its page goes (it flies back into its slot)
        var back = entry.IsBack ? TakeStageBox() : null;
        var previous = Pages.Current;
        var view = Pages.GetOrCreate(entry.Key, () => CreatePageView(entry.Key, page));
        Pages.Show(view, entry.IsBack, ChannelDirection(previous, view));
        UpdateChannelMark(animate: true);
        var focusInHeader = focused is Visual fv && IsInside(fv, TopBar);
        var pulled = entry.Key == PageKey.GameDetail && !entry.IsBack ? _pullSnap : null;
        _pullSnap = null;
        Dispatcher.UIThread.Post(() =>
        {
            UpdateStage();
            // keep the focus in the header while browsing the channels with the controller; otherwise focus the page
            if (!(focusInHeader && !entry.IsBack && Navigator.IsTopLevel(entry.Key))) FocusPage(view, entry.IsBack);
            if (pulled != null) FlyToStage(view, pulled);
            else if (back != null) FlyBack(view, back.Value.Snap, back.Value.GameId);
        }, DispatcherPriority.Loaded);
    }

    // ================= channel direction and the sliding channel mark (R2 animation) =================

    /// <summary>The header's order (channels, then the tools): a page right of the one left enters from the right.</summary>
    private static readonly PageKey[] HeaderOrder = [PageKey.Home, PageKey.Library, PageKey.Systems, PageKey.Emulators, PageKey.Imports, PageKey.Settings];

    private int ChannelDirection(Control? from, Control to)
    {
        if (from == null || KeyOf(from) is not { } a || KeyOf(to) is not { } b) return 0;
        int i = Array.IndexOf(HeaderOrder, a), j = Array.IndexOf(HeaderOrder, b);
        return i < 0 || j < 0 || i == j ? 0 : Math.Sign(j - i);
    }

    private PageKey? KeyOf(Control view)
    {
        foreach (var (key, page) in Pages.Pages)
            if (page == view && key is PageKey k) return k;
        return null;
    }

    private Tween? _markTween;
    private double _markX = double.NaN;

    /// <summary>
    /// The 12 × 3 mark under the chosen channel's digit slides there (Glide 220 ms CubicEaseOut, stretching a little on
    /// the way); reduced motion and off: it jumps. No channel chosen (a tool's page): no mark.
    /// </summary>
    private void UpdateChannelMark(bool animate)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var selected = ChannelList.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.Classes.Contains("selected") && b.IsEffectivelyVisible);
            if (selected == null || selected.Bounds.Height <= 0 || selected.TranslatePoint(new Point(8, selected.Bounds.Height - 7), MarkCanvas) is not { } p)
            {
                ChannelMark.IsVisible = false;
                _markX = double.NaN;
                return;
            }
            if (ChannelMark.RenderTransform is not TransformGroup { Children: [ScaleTransform stretch, TranslateTransform shift] }) return;
            ChannelMark.IsVisible = true;
            Canvas.SetTop(ChannelMark, Math.Round(p.Y));
            var from = _markX;
            var to = Math.Round(p.X);
            if (Math.Abs(from - to) < 0.5 && _markTween?.IsRunning == true) return;
            _markX = to;
            _markTween?.Stop();
            if (!animate || double.IsNaN(from) || Math.Abs(from - to) < 0.5 || Motion.Glide == TimeSpan.Zero)
            {
                shift.X = to;
                stretch.ScaleX = 1;
                return;
            }
            var start = shift.X;
            _markTween = Tween.Run(ChannelMark, Motion.Glide, k =>
            {
                shift.X = Ease.Lerp(start, to, Ease.CubicOut(k));
                stretch.ScaleX = 1 + 0.8 * Ease.Bump(k);
            });
        }, DispatcherPriority.Loaded);
    }

    // ================= the box between shelf and stage (R2 animation, DESIGN 9.2) =================

    private PullLayer.Snap? _pullSnap;
    private long _pulledId;

    /// <summary>A box was chosen (click, Accept): its copy is taken now, it flies once the detail page stands.</summary>
    internal void PullOut(GameTile tile, GameItemViewModel game)
    {
        Pull.Stop();
        _pullSnap?.Bitmap.Dispose();
        _pullSnap = null;
        _pulledId = game.Id;
        if (!Motion.AllowsMovement || tile.Art is not { } art || _vm.Launch.IsActive) return;
        // a box on the detail page itself ("Daneben im Regal") flies too; the stage box is not a shelf
        if (tile.Name == "DetailBox") return;
        _pullSnap = Pull.Take(art);
    }

    private void FlyToStage(Control view, PullLayer.Snap snap)
    {
        if (view.FindControl<GameTile>("DetailBox") is not { } box || box.DataContext is not GameItemViewModel shown || shown.Id != _pulledId)
        {
            snap.Bitmap.Dispose();
            return;
        }
        Pull.Fly(snap, () => box.IsEffectivelyVisible ? Pull.RectOf(box.Art) : null, PullLayer.OutDuration, box);
        // the text column follows the box onto the stage: 80 ms later, 16 px from the right, with the page's fade
        if (view.FindControl<Control>("TextColumn") is { } text) EnterFromRight(text, 80, 16);
    }

    /// <summary>The detail page's stage box as it stands, with its game (null when the current page is no detail).</summary>
    private (PullLayer.Snap Snap, long GameId)? TakeStageBox()
    {
        if (!Motion.AllowsMovement || Pages.Current is not GameDetailPage detail) return null;
        if (detail.FindControl<GameTile>("DetailBox") is not { Art: { } art, DataContext: GameItemViewModel game }) return null;
        return Pull.Take(art) is { } snap ? (snap, game.Id) : null;
    }

    private void FlyBack(Control view, PullLayer.Snap snap, long gameId)
    {
        // the box of that game in the page we came back to, on screen (the remembered focus, else any realised one)
        var tile = (Pages.RecallFocus(view) as GameTile) is { DataContext: GameItemViewModel g } t0 && g.Id == gameId ? t0
            : view.GetVisualDescendants().OfType<GameTile>().FirstOrDefault(t => t.DataContext is GameItemViewModel x && x.Id == gameId
                && t.Name != "DetailBox" && t.IsEffectivelyVisible && t.Art != null && IsOnScreen(t));
        if (tile == null)
        {
            snap.Bitmap.Dispose();
            return;
        }
        Pull.Fly(snap, () => tile.DataContext is GameItemViewModel x && x.Id == gameId && tile.IsEffectivelyVisible ? Pull.RectOf(tile.Art) : null,
            PullLayer.BackDuration, tile, () => PullLayer.Settle(tile.Art!));
    }

    private bool IsOnScreen(Control c) =>
        c.TranslatePoint(default, this) is { } p && c.TranslatePoint(new Point(c.Bounds.Width, c.Bounds.Height), this) is { } q
        && q.Y > 0 && p.Y < Bounds.Height && q.X > 0 && p.X < Bounds.Width;

    /// <summary>
    /// A part entering after the page (Scene, CubicEaseOut), <paramref name="delayMs"/> later and from <paramref name="dx"/>
    /// px to the right; only under full motion.
    /// </summary>
    private static void EnterFromRight(Control c, double delayMs, double dx)
    {
        if (!Motion.AllowsMovement) return;
        var t = c.RenderTransform as TranslateTransform;
        if (c.RenderTransform != null && t == null) return;
        t ??= new TranslateTransform();
        c.RenderTransform = t;
        var total = delayMs + Motion.Scene.TotalMilliseconds;
        Tween.Run(c, TimeSpan.FromMilliseconds(total), p =>
        {
            var k = Math.Clamp((p * total - delayMs) / Motion.Scene.TotalMilliseconds, 0, 1);
            var e = Ease.CubicOut(k);
            t.X = dx * (1 - e);
            c.Opacity = e;
        });
    }

    private void FocusPage(Control view, bool preferRemembered)
    {
        var method = _pointerMode ? NavigationMethod.Pointer : NavigationMethod.Directional;
        if (preferRemembered && Pages.RecallFocus(view) is { } remembered && remembered.Focus(method)) return;
        if (view is IPageView pv && pv.FocusDefault()) return;
        if (Avalonia.Input.FocusManager.FindFirstFocusableElement(view) is { } first) first.Focus(method);
    }

    internal Control? CurrentPageView => Pages.Current;

    // ================= input routing =================

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        _pointerMode = false;
        _vm.KeyboardGlyphs = true;
        _repeat.OnKeyDown(e.Key);
        if (e.Key == Key.F11)
        {
            SetFullscreen(WindowState != WindowState.FullScreen);
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.Alt)
        {
            SetFullscreen(WindowState != WindowState.FullScreen);
            e.Handled = true;
            return;
        }
        var focused = FocusManager?.GetFocusedElement();
        var inText = focused is TextBox;
        if (inText && e.Key == Key.Escape)
        {
            // leave the text field (keeps its text); a second Escape is Back
            if (Pages.Current != null) FocusPage(Pages.Current, preferRemembered: false);
            e.Handled = true;
            return;
        }
        // keys 1/2/3 switch the channel directly (not while typing, not inside overlays)
        if (!inText && e.KeyModifiers == KeyModifiers.None && _vm.Dialogs.Current == null && !_vm.Launch.IsActive
            && e.Key is Key.D1 or Key.D2 or Key.D3 or Key.NumPad1 or Key.NumPad2 or Key.NumPad3)
        {
            var digit = e.Key is Key.D1 or Key.NumPad1 ? "1" : e.Key is Key.D2 or Key.NumPad2 ? "2" : "3";
            ShowChannel(_vm.SelectChannel(digit));
            e.Handled = true;
            return;
        }
        var action = KeyboardMap.Map(e.Key, e.KeyModifiers, inText);
        if (action == null) return;
        if (Execute(action.Value)) e.Handled = true;
    }

    // ================= right-click list (R2) =================

    private Control? _contextReturn;

    /// <summary>True while the right-click list is open.</summary>
    internal bool IsContextOpen => ContextLayer.IsVisible;

    /// <summary>
    /// Opens the right-click list at <paramref name="at"/> (a point in <paramref name="anchor"/>), kept inside the shell. The
    /// first row takes the focus (visible, so the controller and the keyboard can use the list too).
    /// </summary>
    internal void ShowContext(Control anchor, Point at, IReadOnlyList<ContextEntry> entries, int focusIndex = 0)
    {
        if (entries.Count == 0) return;
        _contextReturn = FocusManager?.GetFocusedElement() as Control;
        ContextRows.Children.Clear();
        foreach (var entry in entries)
        {
            var icon = new RhIcon { Width = 22, Height = 22, Filled = entry.Lit };
            if (this.TryFindResource(entry.Icon, out var geo) && geo is Geometry g) icon.Data = g;
            if (entry.Lit && this.TryFindResource("RhFavoriteBrush", out var lit) && lit is IBrush b) icon.Foreground = b;
            var row = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    Spacing = 14,
                    Children = { icon, new TextBlock { Text = entry.Label, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center } },
                },
            };
            if (this.TryFindResource("RhSheetRow", out var theme) && theme is Avalonia.Styling.ControlTheme ct) row.Theme = ct;
            var run = entry.Run;
            row.Click += (_, e) =>
            {
                e.Handled = true;
                HideContext(restoreFocus: true);
                try { run(); } catch (Exception ex) { Log.Error("Context action failed", ex); }
            };
            ContextRows.Children.Add(row);
        }
        ContextLayer.IsVisible = true;
        var p = anchor.TranslatePoint(at, Shell) ?? new Point(Shell.Bounds.Width / 2, Shell.Bounds.Height / 2);
        ContextPanel.Measure(Size.Infinity);
        var size = ContextPanel.DesiredSize;
        var x = Math.Clamp(p.X, 8, Math.Max(8, Shell.Bounds.Width - size.Width - 8));
        var y = Math.Clamp(p.Y, 8, Math.Max(8, Shell.Bounds.Height - 56 - size.Height - 8));
        ContextPanel.Margin = new Thickness(Math.Round(x), Math.Round(y), 0, 0);
        // R2 animation: the list unfolds from the pointer (rows 20 ms apart)
        Anim.Unfold(ContextPanel, ContextRows.Children.ToList(), fromTop: true);
        var first = Math.Clamp(focusIndex, 0, ContextRows.Children.Count - 1);
        Dispatcher.UIThread.Post(() => (ContextRows.Children.ElementAtOrDefault(first) as Control)?.Focus(NavigationMethod.Directional), DispatcherPriority.Loaded);
    }

    internal void HideContext(bool restoreFocus)
    {
        if (!ContextLayer.IsVisible) return;
        ContextLayer.IsVisible = false;
        ContextRows.Children.Clear();
        var back = _contextReturn;
        _contextReturn = null;
        if (restoreFocus && back is { IsEffectivelyVisible: true }) back.Focus(_pointerMode ? NavigationMethod.Pointer : NavigationMethod.Directional);
    }

    private void OnMouseBackButton(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.PointerUpdateKind != PointerUpdateKind.XButton1Pressed) return;
        _pointerMode = true;
        e.Handled = true;
        MouseBack();
    }

    /// <summary>
    /// Back for the mouse (back button, footer hint "Zurück"): the dialog cancels, a page's own fold/sheet/overlay closes,
    /// else one step back in the history. Unlike the controller's Back it never moves the focus into the header.
    /// </summary>
    internal void MouseBack()
    {
        if (_vm.Launch.IsActive) return;
        if (_vm.Dialogs.Current is { } dialog) { dialog.Cancel(); return; }
        if (IsContextOpen) { HideContext(restoreFocus: true); return; }
        if (_vm.CurrentPage?.HandleAction(NavAction.Back) == true) return;
        if (_vm.Navigator.GoBack()) return;
        // a top-level page without history: back to Start (the controller's Back would go to the header)
        if (_vm.Navigator.Current != PageKey.Home) _vm.Navigator.Navigate(PageKey.Home);
    }

    private void OnHintClick(object? sender, RoutedEventArgs e)
    {
        if (e.Source is not Button { DataContext: HintItem { IsClickable: true } hint }) return;
        e.Handled = true;
        _pointerMode = true;
        if (hint.Action == NavAction.Back) MouseBack();
        else Execute(hint.Action);
    }

    private void OnLocationParentClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        _pointerMode = true;
        _vm.GoToSection();
    }

    /// <summary>Executes a UI action (keyboard or controller). Returns true when it was consumed.</summary>
    public bool Execute(NavAction action)
    {
        try { return ExecuteCore(action); }
        catch (Exception ex)
        {
            Log.Error($"NavAction {action} failed", ex);
            return true;
        }
    }

    private bool ExecuteCore(NavAction action)
    {
        var fm = FocusManager;
        var focused = fm?.GetFocusedElement();

        // dialogs are modal: everything stays inside them
        if (_vm.Dialogs.Current is { } dialog)
        {
            switch (action)
            {
                case NavAction.Back: dialog.Cancel(); return true;
                case NavAction.Accept: return Invoke(focused);
                case NavAction.Left or NavAction.Right or NavAction.Up or NavAction.Down:
                    if (focused is not Visual v || !IsInside(v, DialogPanel)) { FocusDialog(); return true; }
                    fm?.FindNextElement(ToDirection(action), new FindNextElementOptions { SearchRoot = DialogPanel })?.Focus(NavigationMethod.Directional);
                    return true;
                default: return true;
            }
        }
        // the right-click list is modal while open: up/down inside it, Accept runs a row, Back closes it
        if (IsContextOpen)
        {
            switch (action)
            {
                case NavAction.Back: HideContext(restoreFocus: true); return true;
                case NavAction.Accept: return Invoke(focused);
                case NavAction.Up or NavAction.Down:
                    var rows = ContextRows.Children.OfType<Control>().ToList();
                    var at = rows.IndexOf(focused as Control ?? null!);
                    var next = at < 0 ? 0 : Math.Clamp(at + (action == NavAction.Down ? 1 : -1), 0, rows.Count - 1);
                    if (rows.Count > 0) rows[next].Focus(NavigationMethod.Directional);
                    return true;
                default: return true;
            }
        }
        if (_vm.Launch.IsActive)
        {
            if (action == NavAction.Accept) return Invoke(focused);
            // the game screen's bar: left/right between its buttons
            if (action is NavAction.Left or NavAction.Right && focused is Visual lv && IsInside(lv, LaunchOverlay))
                fm?.FindNextElement(ToDirection(action), new FindNextElementOptions { SearchRoot = LaunchOverlay })?.Focus(NavigationMethod.Directional);
            return true; // the running game owns the controller; only the overlay's buttons are reachable
        }

        var page = _vm.CurrentPage;
        switch (action)
        {
            case NavAction.Up or NavAction.Down or NavAction.Left or NavAction.Right:
                return MoveFocus(action, focused);
            case NavAction.Accept:
                return Invoke(focused);
            case NavAction.Back:
                // a page's own overlay closes first (the detail's "Mehr", "Dateiinfo", cover …)
                if (page?.HandleAction(action) == true) return true;
                if (_vm.Navigator.GoBack()) return true;
                if (focused is Visual fv && IsInside(fv, TopBar))
                {
                    // Back in the header: to the remembered focus of the page (Start when on a tool page)
                    if (_vm.Navigator.Current != PageKey.Home && !Navigator.IsTopLevel(_vm.Navigator.Current)) _vm.Navigator.Navigate(PageKey.Home);
                    else if (Pages.Current != null) FocusPage(Pages.Current, true);
                    return true;
                }
                FocusHeader();
                return true;
            case NavAction.Menu:
                // Menu focuses the active channel; again: back to the page
                if (focused is Visual mv && IsInside(mv, TopBar)) { if (Pages.Current != null) FocusPage(Pages.Current, true); }
                else FocusHeader();
                return true;
            case NavAction.Favorite:
                if ((focused as Control)?.DataContext is GameItemViewModel item && focused is GameTile)
                {
                    _ = _vm.Ctx.ToggleFavoriteAsync(item);
                    return true;
                }
                return page?.HandleAction(action) ?? false;
            case NavAction.Search:
                if (page?.Key == PageKey.Library && page.HandleAction(action)) return true;
                _vm.Navigator.Navigate(PageKey.Library, LibraryRequest.Search);
                return true;
            case NavAction.View:
                return page?.HandleAction(action) ?? false;
            case NavAction.PageLeft or NavAction.PageRight:
                // shoulders are page-local first (library: shelf, system: neighbour …); otherwise they switch the channel
                if (page?.HandleAction(action) == true) return true;
                ShowChannel(_vm.CycleSection(action == NavAction.PageLeft ? -1 : +1));
                return true;
        }
        return false;
    }

    private bool MoveFocus(NavAction action, IInputElement? focused)
    {
        var fm = FocusManager;
        if (fm == null) return false;
        _pointerMode = false;
        if (focused is not Visual v || !IsInside(v, Shell))
        {
            if (Pages.Current != null) FocusPage(Pages.Current, true);
            return true;
        }
        // pages with their own directional logic (Start: shelves with anchors and remembered positions)
        if (Pages.Current is PageBase pb && v is Control fc && IsInside(v, pb) && pb.MoveFocus(action, fc)) return true;
        // the cover grid navigates by item/row itself (rows may be virtualized away)
        var grid = v.FindAncestorOfType<CoverGrid>(includeSelf: true);
        if (grid != null && grid.Move(action)) return true;
        // FindNextElement + Focus(Directional) instead of TryMoveFocus: only a directional focus shows the focus ring
        var next = fm.FindNextElement(ToDirection(action), new FindNextElementOptions { SearchRoot = Shell });
        var moved = next != null && next.Focus(NavigationMethod.Directional);
        if (!moved)
        {
            // Up from the top of the page reaches the header; Down from the header goes back into the page
            if (action == NavAction.Up && !IsInside(v, TopBar)) FocusHeader();
            else if (action == NavAction.Down && IsInside(v, TopBar) && Pages.Current != null) FocusPage(Pages.Current, true);
        }
        return true;
    }

    private static NavigationDirection ToDirection(NavAction a) => a switch
    {
        NavAction.Up => NavigationDirection.Up,
        NavAction.Down => NavigationDirection.Down,
        NavAction.Left => NavigationDirection.Left,
        _ => NavigationDirection.Right,
    };

    /// <summary>Activates the focused element like a click (buttons, toggles, combo boxes).</summary>
    private static bool Invoke(IInputElement? focused)
    {
        if (focused is not Control control || !control.IsEffectivelyEnabled) return false;
        var peer = ControlAutomationPeer.CreatePeerForElement(control);
        if (peer is IInvokeProvider invoke) { invoke.Invoke(); return true; }
        if (peer is IToggleProvider toggle) { toggle.Toggle(); return true; }
        return false;
    }

    /// <summary>Focuses the active channel (or the active tool icon) in the header.</summary>
    /// <summary>
    /// R2: a new message slides up out of the footer (24 px, Step 140 ms, CubicEaseOut) and fades in; reduced motion:
    /// it only fades; off: it is simply there.
    /// </summary>
    private static void SlideInToast(Control container)
    {
        container.Transitions = null;
        if (!Motion.Enabled)
        {
            container.Opacity = 1;
            container.RenderTransform = null;
            return;
        }
        var move = Motion.AllowsMovement;
        container.Opacity = 0;
        container.RenderTransform = move ? Avalonia.Media.Transformation.TransformOperations.Parse("translateY(24px)") : null;
        Dispatcher.UIThread.Post(() =>
        {
            container.Transitions =
            [
                new Avalonia.Animation.DoubleTransition { Property = OpacityProperty, Duration = Motion.StepDuration, Easing = new Avalonia.Animation.Easings.CubicEaseOut() },
                new Avalonia.Animation.TransformOperationsTransition { Property = RenderTransformProperty, Duration = Motion.StepDuration, Easing = new Avalonia.Animation.Easings.CubicEaseOut() },
            ];
            container.Opacity = 1;
            if (move) container.RenderTransform = Avalonia.Media.Transformation.TransformOperations.Parse("translateY(0px)");
            // R2 animation: the message's icon ticks once as the slip arrives
            foreach (var icon in container.GetVisualDescendants().OfType<RhIcon>().Where(i => i.IsVisible)) icon.Pop();
        }, DispatcherPriority.Render);
    }

    private void FocusHeader()
    {
        var buttons = TopBar.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("navitem") && b.IsEffectivelyVisible).ToList();
        var selected = buttons.FirstOrDefault(b => b.Classes.Contains("selected")) ?? buttons.FirstOrDefault();
        selected?.Focus(NavigationMethod.Directional);
    }

    private void FocusDialog()
    {
        if (_vm.Dialogs.Current == null) return;
        var buttons = DialogButtons.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible).ToList();
        (buttons.FirstOrDefault() as Control)?.Focus(NavigationMethod.Directional);
    }

    private static bool IsInside(Visual v, Visual root)
    {
        for (Visual? x = v; x != null; x = x.GetVisualParent()) if (x == root) return true;
        return false;
    }

    protected override void OnClosed(EventArgs e)
    {
        Motion.Changed -= ApplyMotionClass;
        base.OnClosed(e);
    }
}
