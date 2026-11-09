using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetroHub.App.Input;
using RetroHub.App.Localization;
using RetroHub.App.Services;
using RetroHub.Core.Logging;

namespace RetroHub.App.ViewModels;

/// <summary>A header entry: a channel ("1 Start") or a tool icon (Importe, Emulatoren, Einstellungen).</summary>
public sealed partial class NavItemViewModel : ObservableObject
{
    public required PageKey Key { get; init; }
    public required string LabelKey { get; init; }
    public required string IconKey { get; init; }
    /// <summary>Channel digit ("1"); null for tool icons.</summary>
    public string? Channel { get; init; }
    public bool IsChannel => Channel != null;
    [ObservableProperty] public partial string Label { get; set; } = "";
    [ObservableProperty] public partial bool IsSelected { get; set; }
}

/// <summary>Shell state: navigation, pages, header channels, footer (location + hints), clock, overlays.</summary>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly Dictionary<PageKey, PageViewModel> _pages = new();
    private readonly DispatcherTimer _clock;

    public MainViewModel(UiContext ctx)
    {
        Ctx = ctx;
        NavItems =
        [
            new NavItemViewModel { Key = PageKey.Home, LabelKey = "nav.home", IconKey = "Icon.Start", Channel = "1" },
            new NavItemViewModel { Key = PageKey.Library, LabelKey = "nav.library", IconKey = "Icon.Library", Channel = "2" },
            new NavItemViewModel { Key = PageKey.Systems, LabelKey = "nav.systems", IconKey = "Icon.Systems", Channel = "3" },
            new NavItemViewModel { Key = PageKey.Imports, LabelKey = "nav.imports", IconKey = "Icon.Imports" },
            new NavItemViewModel { Key = PageKey.Emulators, LabelKey = "nav.emulators", IconKey = "Icon.Emulators" },
            new NavItemViewModel { Key = PageKey.Settings, LabelKey = "nav.settings", IconKey = "Icon.Settings" },
        ];
        Channels = NavItems.Where(n => n.IsChannel).ToList();
        Tools = NavItems.Where(n => !n.IsChannel).ToList();
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
    /// <summary>All header entries (channels first, then the tools).</summary>
    public ObservableCollection<NavItemViewModel> NavItems { get; }
    /// <summary>"1 Start  2 Bibliothek  3 Konsolen".</summary>
    public IReadOnlyList<NavItemViewModel> Channels { get; }
    /// <summary>Header icons after the search: Importe, Emulatoren, Einstellungen.</summary>
    public IReadOnlyList<NavItemViewModel> Tools { get; }
    public bool IsDemo => Ctx.Services.IsDemo;

    [ObservableProperty] public partial PageViewModel? CurrentPage { get; private set; }
    [ObservableProperty] public partial IReadOnlyList<HintItem> Hints { get; private set; } = [];
    [ObservableProperty] public partial string Clock { get; private set; } = "";
    [ObservableProperty] public partial string PageTitle { get; private set; } = "";
    [ObservableProperty] public partial bool CanGoBack { get; private set; }
    /// <summary>Legacy (v0 brand glyphs); the footer glyphs are position-based now.</summary>
    [ObservableProperty] public partial GlyphStyle Glyphs { get; private set; } = GlyphStyle.Auto;
    /// <summary>The last input was the keyboard: the footer shows key caps instead of pad glyphs.</summary>
    [ObservableProperty] public partial bool KeyboardGlyphs { get; set; } = true;
    [ObservableProperty] public partial bool ShowHints { get; private set; } = true;
    [ObservableProperty] public partial bool ControllerConnected { get; private set; }
    [ObservableProperty] public partial string ControllerName { get; private set; } = "";
    [ObservableProperty] public partial string StatusText { get; private set; } = "";
    [ObservableProperty] public partial string DemoText { get; private set; } = "";
    /// <summary>Footer location sentence: the parent part (section, Text 3) …</summary>
    [ObservableProperty] public partial string LocationParent { get; private set; } = "";
    /// <summary>… and the page part (Text 2), e.g. "Bibliothek › Pokémon Smaragd-Edition".</summary>
    [ObservableProperty] public partial string LocationText { get; private set; } = "";
    public bool HasLocationText => !string.IsNullOrEmpty(LocationText);
    /// <summary>Window width, set by the view.</summary>
    [ObservableProperty] public partial double WindowWidth { get; set; } = 1600;
    /// <summary>Logical (scaled) width of the shell, set by the view.</summary>
    [ObservableProperty] public partial double LogicalWidth { get; set; } = 1920;
    /// <summary>Below 1500 px logical width the controller name is hidden (DESIGN 5.3).</summary>
    [ObservableProperty] public partial bool ShowControllerName { get; private set; } = true;
    /// <summary>Below 1300 px logical width the footer shows at most 3 hints (DESIGN 7.0).</summary>
    [ObservableProperty] public partial IReadOnlyList<HintItem> VisibleHints { get; private set; } = [];
    /// <summary>Legacy name: true when the header is in its compact form (narrow window).</summary>
    [ObservableProperty] public partial bool IsSidebarCollapsed { get; private set; }
    [ObservableProperty] public partial bool IsFullscreen { get; set; }

    /// <summary>Below this logical width the header is compact (no controller name).</summary>
    public const double CompactBelow = 1500;

    partial void OnLogicalWidthChanged(double value) => UpdateCompact();
    partial void OnHintsChanged(IReadOnlyList<HintItem> value) => UpdateCompact();
    partial void OnLocationTextChanged(string value) => OnPropertyChanged(nameof(HasLocationText));

    /// <summary>The "Demo-Modus" label in the header (hidden in the compact header).</summary>
    [ObservableProperty] public partial bool ShowDemoChip { get; private set; }

    private void UpdateCompact()
    {
        ShowControllerName = LogicalWidth >= CompactBelow;
        ShowDemoChip = IsDemo && ShowControllerName;
        IsSidebarCollapsed = !ShowControllerName;
        var max = LogicalWidth < 1300 ? 3 : 4;
        VisibleHints = Hints.Count <= max ? Hints : Hints.Take(max).ToList();
    }

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
            if (e.PropertyName is nameof(PageViewModel.Title) or nameof(PageViewModel.Location)) UpdateLocation();
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
        UpdateLocation();
        PageShown?.Invoke(e, page);
    }

    /// <summary>"Bibliothek › 66 Spiele" – parent in Text 3, page part in Text 2 (DESIGN 7.0).</summary>
    private void UpdateLocation()
    {
        var section = NavItems.FirstOrDefault(n => n.Key == Ctx.Navigator.Section)?.Label ?? "";
        var page = CurrentPage;
        var detail = page?.Location ?? page?.Title ?? "";
        if (string.Equals(detail, section, StringComparison.CurrentCulture)) detail = "";
        LocationParent = section;
        LocationText = detail;
    }

    /// <summary>
    /// Work that starts once the window is up: the library scan when "Beim Start scannen" is on. Runs in the
    /// background (the Imports page shows its progress); never blocks or throws into the UI.
    /// </summary>
    public Task? StartBackgroundTasks()
    {
        try
        {
            // "Ordner überwachen": new/changed files in a library folder → quiet incremental scan.
            void Hook()
            {
                var w = Ctx.Services.Watcher;
                if (w == null) return;
                w.FoldersChanged -= OnFoldersChanged;
                w.FoldersChanged += OnFoldersChanged;
            }
            Ctx.Services.WatcherChanged += () => Dispatcher.UIThread.Post(Hook);
            Hook();

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

    private void OnFoldersChanged(IReadOnlyList<long> folderIds) => Dispatcher.UIThread.Post(() =>
    {
        // A game is running: do not compete for the disk; the next change or the next start will pick it up.
        if (Ctx.Services.Sessions.Current != null) return;
        Log.Info($"Library folders changed ({folderIds.Count}) – quiet scan");
        _ = ((ImportsViewModel)GetPage(PageKey.Imports)).RunScanAsync(quiet: true);
    });

    [RelayCommand]
    private void Navigate(NavItemViewModel? item)
    {
        if (item != null) Ctx.Navigator.Navigate(item.Key);
    }

    [RelayCommand]
    private void Back() => Ctx.Navigator.GoBack();

    /// <summary>
    /// Channel switch (LB/RB when the page does not use the shoulders itself, keys 1/2/3): previous/next channel,
    /// wrapping. From a tool page (Importe …) the cycle starts at "Start". Returns the channel shown.
    /// </summary>
    public NavItemViewModel CycleSection(int delta)
    {
        var index = Channels.ToList().FindIndex(n => n.Key == Ctx.Navigator.Section);
        if (index < 0) index = delta > 0 ? -1 : 0;
        var next = Channels[((index + delta) % Channels.Count + Channels.Count) % Channels.Count];
        Ctx.Navigator.Navigate(next.Key);
        return next;
    }

    /// <summary>Keys 1/2/3: the channel with that digit (null when none).</summary>
    public NavItemViewModel? SelectChannel(string digit)
    {
        var channel = Channels.FirstOrDefault(c => c.Channel == digit);
        if (channel != null) Ctx.Navigator.Navigate(channel.Key);
        return channel;
    }

    [RelayCommand]
    private void Search() => Ctx.Navigator.Navigate(PageKey.Library, LibraryRequest.Search);

    private void UpdatePreferences()
    {
        var prefs = Ctx.Services.Ui.Current;
        ShowHints = prefs.ShowHints;
        UpdateCompact();
    }

    private void UpdateController()
    {
        var input = Ctx.Services.Input;
        ControllerConnected = input.Controller != ControllerFamily.None;
        ControllerName = input.ControllerName ?? Loc.T("shell.noController");
        if (ControllerConnected) KeyboardGlyphs = false;
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
        UpdateLocation();
    }

    public void Dispose()
    {
        _clock.Stop();
        Loc.LanguageChanged -= OnLanguageChanged;
        Ctx.Navigator.Navigated -= OnNavigated;
    }
}
