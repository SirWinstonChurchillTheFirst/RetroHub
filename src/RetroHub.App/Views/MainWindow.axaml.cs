using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using RetroHub.App.Controls;
using RetroHub.App.Input;
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
/// resize and – on the maximize button – the snap-layouts flyout), sidebar, keep-alive page host, hint bar, toasts,
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

        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        AddHandler(PointerPressedEvent, (_, _) => _pointerMode = true, RoutingStrategies.Tunnel, handledEventsToo: true);
        SizeChanged += (_, e) => vm.WindowWidth = e.NewSize.Width;
        Activated += (_, _) => { Motion.Refresh(); CaptionButtons.Opacity = 1; };
        Deactivated += (_, _) => CaptionButtons.Opacity = 0.55;

        Motion.Changed += ApplyMotionClass;
        ApplyMotionClass();
        vm.PageShown += OnPageShown;
        vm.Dialogs.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DialogService.Current)) Dispatcher.UIThread.Post(FocusDialog, DispatcherPriority.Loaded);
        };
        vm.Launch.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LaunchCoordinator.Phase) && vm.Launch.IsRunning)
                Dispatcher.UIThread.Post(() => StopButton.Focus(NavigationMethod.Directional), DispatcherPriority.Loaded);
        };
        vm.Ctx.Services.Input.ActionRaised += a => Dispatcher.UIThread.Post(() =>
        {
            if (!vm.Ctx.Services.Settings.Current.ControllerEnabled) return; // setting "Mit Controller bedienen"
            _pointerMode = false;
            Execute(a);
        });

        Opened += (_, _) =>
        {
            if (vm.Ctx.Services.Settings.Current.StartFullscreen) SetFullscreen(true);
            if (vm.CurrentPage == null) vm.Navigator.Navigate(PageKey.Home);
        };
        PropertyChanged += (_, e) =>
        {
            if (e.Property == WindowStateProperty) UpdateWindowState();
        };
        // the shell must not wait for Opened in headless tests (no Opened without a platform window): navigate now
        if (vm.CurrentPage == null) vm.Navigator.Navigate(PageKey.Home);
    }

    public MainViewModel ViewModel => _vm;
    internal PageHost PageHost => Pages;

    /// <summary>Brings the window to the front (second start of RetroHub).</summary>
    public void BringToFront()
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Show();
        Activate();
        Topmost = true;
        Topmost = false;
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
        if (this.TryFindResource(WindowState == WindowState.Maximized ? "Glyph.Restore" : "Glyph.Maximize", out var glyph))
            MaximizeButton.Tag = glyph;
    }

    private void ApplyMotionClass()
    {
        foreach (var m in new[] { EffectiveMotion.Full, EffectiveMotion.Reduced, EffectiveMotion.Off })
            Root.Classes.Set(Motion.ClassName(m), m == Motion.Current);
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
        var focusInSidebar = focused is Visual fv && IsInside(fv, Sidebar);
        Dispatcher.UIThread.Post(() =>
        {
            // keep the focus in the sidebar while browsing it with the controller; otherwise focus the page
            if (focusInSidebar && !entry.IsBack && Navigator.IsTopLevel(entry.Key)) return;
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
            return true; // the running game owns the controller; only "Stop" is reachable
        }

        var page = _vm.CurrentPage;
        switch (action)
        {
            case NavAction.Up or NavAction.Down or NavAction.Left or NavAction.Right:
                return MoveFocus(action, focused);
            case NavAction.Accept:
                return Invoke(focused);
            case NavAction.Back:
                if (_vm.Navigator.GoBack()) return true;
                if (focused is Visual fv && IsInside(fv, Sidebar))
                {
                    if (_vm.Navigator.Current != PageKey.Home) _vm.Navigator.Navigate(PageKey.Home);
                    return true;
                }
                FocusSidebar();
                return true;
            case NavAction.Menu:
                if (focused is Visual mv && IsInside(mv, Sidebar)) { if (Pages.Current != null) FocusPage(Pages.Current, true); }
                else FocusSidebar();
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
            case NavAction.PageLeft:
                _vm.CycleSection(-1);
                return true;
            case NavAction.PageRight:
                _vm.CycleSection(+1);
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
        // the cover grid navigates by item/row itself (rows may be virtualized away)
        var grid = v.FindAncestorOfType<CoverGrid>(includeSelf: true);
        if (grid != null && grid.Move(action)) return true;
        // FindNextElement + Focus(Directional) instead of TryMoveFocus: only a directional focus shows the focus ring
        var next = fm.FindNextElement(ToDirection(action), new FindNextElementOptions { SearchRoot = Shell });
        var moved = next != null && next.Focus(NavigationMethod.Directional);
        if (!moved && action == NavAction.Left && !IsInside(v, Sidebar)) FocusSidebar();
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

    private void FocusSidebar()
    {
        var selected = NavList.GetRealizedContainers()
            .Select(c => c.GetVisualDescendants().OfType<Button>().FirstOrDefault())
            .FirstOrDefault(b => b?.Classes.Contains("selected") == true);
        (selected ?? (Control)CollapseButton).Focus(NavigationMethod.Directional);
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
