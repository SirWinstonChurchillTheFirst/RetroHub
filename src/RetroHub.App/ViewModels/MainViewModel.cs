using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetroHub.App.Input;
using RetroHub.App.Localization;
using RetroHub.App.Services;
using RetroHub.Core.Logging;

namespace RetroHub.App.ViewModels;

/// <summary>A sidebar entry.</summary>
public sealed partial class NavItemViewModel : ObservableObject
{
    public required PageKey Key { get; init; }
    public required string LabelKey { get; init; }
    public required string IconKey { get; init; }
    [ObservableProperty] public partial string Label { get; set; } = "";
    [ObservableProperty] public partial bool IsSelected { get; set; }
}

/// <summary>Shell state: navigation, pages, hint bar, clock, sidebar, overlays.</summary>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly Dictionary<PageKey, PageViewModel> _pages = new();
    private readonly DispatcherTimer _clock;

    public MainViewModel(UiContext ctx)
    {
        Ctx = ctx;
        NavItems =
        [
            new NavItemViewModel { Key = PageKey.Home, LabelKey = "nav.home", IconKey = "Icon.Home" },
            new NavItemViewModel { Key = PageKey.Library, LabelKey = "nav.library", IconKey = "Icon.Library" },
            new NavItemViewModel { Key = PageKey.Systems, LabelKey = "nav.systems", IconKey = "Icon.Gamepad" },
            new NavItemViewModel { Key = PageKey.Emulators, LabelKey = "nav.emulators", IconKey = "Icon.Chip" },
            new NavItemViewModel { Key = PageKey.Imports, LabelKey = "nav.imports", IconKey = "Icon.Import" },
            new NavItemViewModel { Key = PageKey.Settings, LabelKey = "nav.settings", IconKey = "Icon.Settings" },
        ];
        UpdateTexts();
        ctx.Navigator.Navigated += OnNavigated;
        ctx.Services.Ui.Changed += _ => Dispatcher.UIThread.Post(UpdatePreferences);
        ctx.Services.Input.ControllerChanged += () => Dispatcher.UIThread.Post(UpdateController);
        ctx.LibraryChanged += UpdateStatus;
        Loc.LanguageChanged += OnLanguageChanged;
        _clock = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(10) };
        _clock.Tick += (_, _) => UpdateClock();
        _clock.Start();
        UpdateClock();
        UpdatePreferences();
        UpdateController();
        UpdateStatus();
    }

    public UiContext Ctx { get; }
    public ToastService Toasts => Ctx.Toasts;
    public DialogService Dialogs => Ctx.Dialogs;
    public LaunchCoordinator Launch => Ctx.Launch;
    public Navigator Navigator => Ctx.Navigator;
    public ObservableCollection<NavItemViewModel> NavItems { get; }
    public bool IsDemo => Ctx.Services.IsDemo;

    [ObservableProperty] public partial PageViewModel? CurrentPage { get; private set; }
    [ObservableProperty] public partial IReadOnlyList<HintItem> Hints { get; private set; } = [];
    [ObservableProperty] public partial string Clock { get; private set; } = "";
    [ObservableProperty] public partial string PageTitle { get; private set; } = "";
    [ObservableProperty] public partial bool CanGoBack { get; private set; }
    [ObservableProperty] public partial GlyphStyle Glyphs { get; private set; } = GlyphStyle.Xbox;
    [ObservableProperty] public partial bool ShowHints { get; private set; } = true;
    [ObservableProperty] public partial bool ControllerConnected { get; private set; }
    [ObservableProperty] public partial string ControllerName { get; private set; } = "";
    [ObservableProperty] public partial string StatusText { get; private set; } = "";
    [ObservableProperty] public partial string DemoText { get; private set; } = "";
    /// <summary>Window width, set by the view (drives the automatic sidebar collapse).</summary>
    [ObservableProperty] public partial double WindowWidth { get; set; } = 1600;
    [ObservableProperty] public partial bool IsSidebarCollapsed { get; private set; }
    [ObservableProperty] public partial bool IsFullscreen { get; set; }

    /// <summary>Below this window width the sidebar shows icons only.</summary>
    public const double CollapseBelow = 1280;

    partial void OnWindowWidthChanged(double value) => UpdateSidebar();

    /// <summary>Lazily created page view model (kept alive).</summary>
    public PageViewModel GetPage(PageKey key)
    {
        if (_pages.TryGetValue(key, out var page)) return page;
        page = key switch
        {
            PageKey.Home => new HomeViewModel(Ctx),
            PageKey.Library => new LibraryViewModel(Ctx),
            PageKey.Systems => new SystemsViewModel(Ctx),
            PageKey.System => new SystemViewModel(Ctx),
            PageKey.GameDetail => new GameDetailViewModel(Ctx),
            PageKey.Emulators => new EmulatorsViewModel(Ctx),
            PageKey.Imports => new ImportsViewModel(Ctx),
            PageKey.Settings => new SettingsViewModel(Ctx),
            _ => throw new ArgumentOutOfRangeException(nameof(key)),
        };
        page.PropertyChanged += (s, e) =>
        {
            if (s != CurrentPage) return;
            if (e.PropertyName == nameof(PageViewModel.Hints)) Hints = CurrentPage!.Hints;
            if (e.PropertyName == nameof(PageViewModel.Title)) PageTitle = CurrentPage!.Title;
        };
        _pages[key] = page;
        return page;
    }

    public IEnumerable<PageViewModel> CreatedPages => _pages.Values;

    /// <summary>Raised after the current page changed (the view swaps the visible page).</summary>
    public event Action<NavigationEntry, PageViewModel>? PageShown;

    private void OnNavigated(NavigationEntry e)
    {
        var previous = CurrentPage;
        var page = GetPage(e.Key);
        if (previous != null && previous != page)
        {
            try { previous.OnNavigatedFrom(); } catch (Exception ex) { Log.Error($"{previous.Key}.OnNavigatedFrom failed", ex); }
        }
        CurrentPage = page;
        try { page.OnNavigatedTo(e.Argument, e.IsBack); } catch (Exception ex) { Log.Error($"{page.Key}.OnNavigatedTo failed", ex); }
        var section = Ctx.Navigator.Section;
        foreach (var n in NavItems) n.IsSelected = n.Key == section;
        Hints = page.Hints;
        PageTitle = page.Title;
        CanGoBack = Ctx.Navigator.CanGoBack;
        PageShown?.Invoke(e, page);
    }

    /// <summary>
    /// Work that starts once the window is up: the library scan when "Beim Start scannen" is on. Runs in the
    /// background (the Imports page shows its progress); never blocks or throws into the UI.
    /// </summary>
    public Task? StartBackgroundTasks()
    {
        try
        {
            if (!Ctx.Services.Settings.Current.ScanOnStartup) return null;
            Log.Info("Start-up library scan");
            return ((ImportsViewModel)GetPage(PageKey.Imports)).RunScanAsync(quiet: true);
        }
        catch (Exception ex)
        {
            Log.Error("Starting the background tasks failed", ex);
            return null;
        }
    }

    [RelayCommand]
    private void Navigate(NavItemViewModel? item)
    {
        if (item != null) Ctx.Navigator.Navigate(item.Key);
    }

    [RelayCommand]
    private void Back() => Ctx.Navigator.GoBack();

    /// <summary>LB/RB: previous/next sidebar section (wraps).</summary>
    public void CycleSection(int delta)
    {
        var index = NavItems.ToList().FindIndex(n => n.Key == Ctx.Navigator.Section);
        if (index < 0) index = 0;
        var next = NavItems[(index + delta + NavItems.Count) % NavItems.Count];
        Ctx.Navigator.Navigate(next.Key);
    }

    [RelayCommand]
    private void ToggleSidebar()
    {
        var collapsed = !IsSidebarCollapsed;
        Ctx.Services.Ui.Update(p => p.Sidebar = collapsed ? SidebarMode.Collapsed : SidebarMode.Expanded);
    }

    private void UpdateSidebar()
    {
        IsSidebarCollapsed = Ctx.Services.Ui.Current.Sidebar switch
        {
            SidebarMode.Collapsed => true,
            SidebarMode.Expanded => false,
            _ => WindowWidth < CollapseBelow,
        };
    }

    private void UpdatePreferences()
    {
        var prefs = Ctx.Services.Ui.Current;
        ShowHints = prefs.ShowHints;
        UpdateGlyphs();
        UpdateSidebar();
    }

    private void UpdateGlyphs()
    {
        var pref = Ctx.Services.Ui.Current.Glyphs;
        Glyphs = pref != GlyphStyle.Auto ? pref
            : Ctx.Services.Input.Controller == ControllerFamily.PlayStation ? GlyphStyle.PlayStation : GlyphStyle.Xbox;
    }

    private void UpdateController()
    {
        var input = Ctx.Services.Input;
        ControllerConnected = input.Controller != ControllerFamily.None;
        ControllerName = input.ControllerName ?? Loc.T("shell.noController");
        UpdateGlyphs();
    }

    private void UpdateClock() => Clock = DateTime.Now.ToString("t", Loc.Culture);

    private int _statusVersion;

    /// <summary>Status line ("66 Spiele · 13 Plattformen"); the statistics query runs off the UI thread.</summary>
    private void UpdateStatus() => _ = UpdateStatusAsync();

    private async Task UpdateStatusAsync()
    {
        DemoText = IsDemo ? Loc.T("shell.demo") : "";
        var version = ++_statusVersion;
        var games = Ctx.Services.Games;
        var stats = await UiContext.Background(games.Stats, "Reading library stats");
        if (stats == null || version != _statusVersion) return;
        StatusText = Loc.T("shell.status", stats.GameCount, stats.SystemCount);
    }

    private void UpdateTexts()
    {
        foreach (var n in NavItems) n.Label = Loc.T(n.LabelKey);
    }

    private void OnLanguageChanged()
    {
        UpdateTexts();
        UpdateClock();
        UpdateStatus();
        UpdateController();
        if (CurrentPage != null)
        {
            try { CurrentPage.OnNavigatedTo(Ctx.Navigator.CurrentArgument, true); } catch (Exception ex) { Log.Error("Page refresh failed", ex); }
            Hints = CurrentPage.Hints;
            PageTitle = CurrentPage.Title;
        }
    }

    public void Dispose()
    {
        _clock.Stop();
        Loc.LanguageChanged -= OnLanguageChanged;
        Ctx.Navigator.Navigated -= OnNavigated;
    }
}
