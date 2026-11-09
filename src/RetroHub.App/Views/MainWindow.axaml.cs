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

        WindowDecorationProperties.SetElementRole(TopBar, WindowDecorationsElementRole.TitleBar);
        WindowDecorationProperties.SetElementRole(BrandBar, WindowDecorationsElementRole.TitleBar);
        WindowDecorationProperties.SetElementRole(MinimizeButton, WindowDecorationsElementRole.MinimizeButton);
        WindowDecorationProperties.SetElementRole(MaximizeButton, WindowDecorationsElementRole.MaximizeButton);
        WindowDecorationProperties.SetElementRole(CloseButton, WindowDecorationsElementRole.CloseButton);
        MinimizeButton.Click += (_, _) => WindowState = WindowState.Minimized;
        MaximizeButton.Click += (_, _) => ToggleMaximize();
        CloseButton.Click += (_, _) => Close();
        TopBar.DoubleTapped += (_, e) => { if (e.Source == TopBar) ToggleMaximize(); };

        // auto-repeat detection (DESIGN 8.4): ".nav-repeat" on the root while a direction is held
        NavRepeatTracker.Current = _repeat;
        _repeat.Changed += OnRepeatChanged;

        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, (_, e) => _repeat.OnKeyUp(e.Key), RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerPressedEvent, (_, _) => _pointerMode = true, RoutingStrategies.Tunnel, handledEventsToo: true);
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
        };
        vm.Dialogs.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(DialogService.Current)) return;
            OnOverlayChanged();
            UpdateGameObscured();
            Dispatcher.UIThread.Post(FocusDialog, DispatcherPriority.Loaded);
        };
        vm.Launch.PropertyChanged += (_, e) => OnLaunchChanged(e.PropertyName);
        vm.Launch.Returned += OnGameReturned;
        GameScreen.FullscreenRequested += () => SetFullscreen(WindowState != WindowState.FullScreen);
        // toasts lie above the embedded game: the game screen cuts a hole for them into its native host (airspace)
        GameScreen.SetHoleSource(ToastHost);
        ToastHost.SizeChanged += (_, _) => Dispatcher.UIThread.Post(GameScreen.UpdateHoles, DispatcherPriority.Render);
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
        DialogScaleHost.LayoutTransform = new ScaleTransform(s, s);
        Osd.Scale = s;
        var logical = client.Width / s;
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
        if (page == null) return;
        page.PropertyChanged += OnWatchedPageChanged;
        Backdrop.Show(page.Backdrop);
    }

    private void OnWatchedPageChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PageViewModel.Backdrop) && sender is PageViewModel p) Backdrop.Show(p.Backdrop);
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
        var view = Pages.GetOrCreate(entry.Key, () => CreatePageView(entry.Key, page));
        Pages.Show(view, entry.IsBack);
        var focusInHeader = focused is Visual fv && IsInside(fv, TopBar);
        Dispatcher.UIThread.Post(() =>
        {
            UpdateStage();
            // keep the focus in the header while browsing the channels with the controller; otherwise focus the page
            if (focusInHeader && !entry.IsBack && Navigator.IsTopLevel(entry.Key)) return;
            FocusPage(view, entry.IsBack);
        }, DispatcherPriority.Loaded);
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
        if (focused is ComboBox { IsDropDownOpen: true }) return;
        if (focused is Slider && e.Key is Key.Left or Key.Right) return;
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
        if (focused is ComboBox combo) { combo.IsDropDownOpen = !combo.IsDropDownOpen; return true; }
        var peer = ControlAutomationPeer.CreatePeerForElement(control);
        if (peer is IInvokeProvider invoke) { invoke.Invoke(); return true; }
        if (peer is IToggleProvider toggle) { toggle.Toggle(); return true; }
        return false;
    }

    /// <summary>Focuses the active channel (or the active tool icon) in the header.</summary>
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
