using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetroHub.App.Input;
using RetroHub.App.Localization;
using RetroHub.App.Theming;
using RetroHub.Core.Catalog;
using RetroHub.Core.Library;

namespace RetroHub.App.ViewModels;

/// <summary>Navigation arguments for the library page.</summary>
public sealed record LibraryRequest(bool Favorites = false, bool FocusSearch = false, string? SystemId = null, GameSort? Sort = null)
{
    public static readonly LibraryRequest FavoritesOnly = new(Favorites: true);
    public static readonly LibraryRequest Search = new(FocusSearch: true);
    /// <summary>The "Alle" spine of the shelf "Zuletzt gespielt": the whole library by last play (not saved as the user's sort).</summary>
    public static readonly LibraryRequest RecentlyPlayed = new(Sort: GameSort.LastPlayed);
    /// <summary>The "Alle" spine of "Neu in der Sammlung": by date added.</summary>
    public static readonly LibraryRequest RecentlyAdded = new(Sort: GameSort.Added);
}

/// <summary>A ticker entry ("Alle 66", "GBA 7"): the code, the count, the underline in the system's colour.</summary>
public sealed partial class FilterChip : ObservableObject
{
    public string? SystemId { get; init; }
    public required string Label { get; init; }
    /// <summary>The system's "Farbe" for the 3 px underline (null for "Alle": Bernstein, DESIGN 2.2 (4)).</summary>
    public IBrush? MarkBrush { get; init; }
    public bool IsAll => SystemId == null;
    [ObservableProperty] public partial int Count { get; set; }
    [ObservableProperty] public partial bool IsSelected { get; set; }
}

public sealed record SortOption(GameSort Sort, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// The library "Das Regal" (DESIGN 7.2): the ticker (system filter; shoulders step through it), the shelf mirror, the
/// shelf grid or the list with a preview, search, sort, favourites. A selected system turns the page into that system's
/// compartment: a wall banner as the grid's header, the page ground and the board light become the system's wall and
/// light, and the slot format comes from the stored cover sizes (median) BEFORE the grid is built (DESIGN 11.4).
/// </summary>
public sealed partial class LibraryViewModel : PageViewModel
{
    /// <summary>Slot format (height/width) of the mixed library (DESIGN 7.2.1: 1 : 1.25).</summary>
    public const double MixedAspect = 1.25;

    /// <summary>Fewer stored cover sizes than this: the system's start value (DESIGN 11.4).</summary>
    public const int MinSizesForMedian = 8;

    /// <summary>Sticker "Neu": added within this many days and never started (DESIGN 7.2.1).</summary>
    public const int NewDays = 7;

    private readonly DispatcherTimer _searchDebounce;
    private int _version;
    private bool _suppressReload;

    public LibraryViewModel(UiContext ctx) : base(ctx)
    {
        Title = Loc.T("nav.library");
        SortOptions =
        [
            new(GameSort.Title, Loc.T("sort.title")),
            new(GameSort.LastPlayed, Loc.T("sort.lastPlayed")),
            new(GameSort.Added, Loc.T("sort.added")),
            new(GameSort.PlayTime, Loc.T("sort.playTime")),
            new(GameSort.System, Loc.T("sort.system")),
            new(GameSort.Year, Loc.T("sort.year")),
        ];
        var savedSort = Enum.TryParse<GameSort>(ctx.Services.Ui.Current.LibrarySort, out var s) ? s : GameSort.Title;
        _suppressReload = true;
        SelectedSort = SortOptions.First(o => o.Sort == savedSort);
        IsListView = string.Equals(ctx.Services.Settings.Current.LibraryView, "list", StringComparison.OrdinalIgnoreCase);
        _suppressReload = false;
        // not the (interval, priority, callback) constructor: that one starts the timer → a second load right away
        _searchDebounce = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(220) };
        _searchDebounce.Tick += (_, _) =>
        {
            _searchDebounce.Stop();
            _ = LoadAsync();
        };
        ctx.LibraryChanged += () => { if (IsLoaded) _ = LoadAsync(); };
        Backdrop = new BackdropSpec(BackdropKind.Room, AccentBoard: true);
    }

    public override PageKey Key => PageKey.Library;

    public IReadOnlyList<SortOption> SortOptions { get; }
    public ObservableCollection<FilterChip> Chips { get; } = [];

    [ObservableProperty] public partial IReadOnlyList<GameItemViewModel> Items { get; private set; } = [];
    [ObservableProperty] public partial string SearchText { get; set; } = "";
    [ObservableProperty] public partial SortOption SelectedSort { get; set; }
    [ObservableProperty] public partial bool FavoritesOnly { get; set; }
    [ObservableProperty] public partial bool IsListView { get; set; }
    [ObservableProperty] public partial string? SelectedSystemId { get; private set; }
    [ObservableProperty] public partial string CountText { get; private set; } = "";
    [ObservableProperty] public partial bool IsEmpty { get; private set; }
    [ObservableProperty] public partial bool IsLoaded { get; private set; }
    [ObservableProperty] public partial string EmptyText { get; private set; } = "";

    /// <summary>Slot format of the grid (height/width): 1.25 mixed, the system's cover median in a system context.</summary>
    [ObservableProperty] public partial double SlotAspect { get; private set; } = MixedAspect;

    // ----- the wall banner of a selected system (DESIGN 7.2.1 "Kopf") -----
    [ObservableProperty] public partial bool IsSystemSelected { get; private set; }
    [ObservableProperty] public partial string BannerTitle { get; private set; } = "";
    [ObservableProperty] public partial string BannerSentence { get; private set; } = "";
    [ObservableProperty] public partial Color BannerAccent { get; private set; }
    /// <summary>The selected system's look (wall, light); null for "Alle".</summary>
    [ObservableProperty] public partial SystemLook? SystemLook { get; private set; }
    /// <summary>The light of the context: the system's light, or null = the user's light colour (ring, cone, mirror mark).</summary>
    [ObservableProperty] public partial Color? ContextLight { get; private set; }
    /// <summary>The page ground: room, or the selected system's wall (flat tone).</summary>
    [ObservableProperty] public partial Color Ground { get; private set; } = SystemPalette.Room;

    // ----- the list view's preview (DESIGN 7.2.2) -----
    [ObservableProperty] public partial GameItemViewModel? PreviewItem { get; private set; }
    [ObservableProperty] public partial string PreviewMeta { get; private set; } = "";

    /// <summary>Raised when the page should put the keyboard focus into the search box.</summary>
    public event Action? FocusSearchRequested;

    public bool IsGridView => !IsListView;

    /// <summary>The whole library is empty (not just the filter): the page shows the empty shelf and "Ordner hinzufügen".</summary>
    [ObservableProperty] public partial bool IsLibraryEmpty { get; private set; }

    /// <summary>Games exist, but none matches search/filter.</summary>
    public bool IsNoMatch => IsEmpty && !IsLibraryEmpty;

    partial void OnIsLibraryEmptyChanged(bool value) { OnPropertyChanged(nameof(IsNoMatch)); RaiseHintsChanged(); }
    partial void OnIsEmptyChanged(bool value) { OnPropertyChanged(nameof(IsNoMatch)); RaiseHintsChanged(); }

    /// <summary>Hints name what works here (M2 QA): an empty library has one way out, a list without matches the search.</summary>
    public override IReadOnlyList<HintItem> Hints => IsLibraryEmpty
        ? [new(NavAction.Accept, Loc.T("home.addFolder")), new(NavAction.Menu, Loc.T("hint.menu"))]
        : IsEmpty
            ? [new(NavAction.Search, Loc.T("hint.search")), new(NavAction.PageLeft, Loc.T("hint.shelf")), new(NavAction.Menu, Loc.T("hint.menu"))]
            :
            [
                new(NavAction.Accept, Loc.T("hint.open")),
                new(NavAction.PageLeft, Loc.T("hint.shelf")),
                new(NavAction.View, IsListView ? Loc.T("hint.gridView") : Loc.T("hint.listView")),
                new(NavAction.Favorite, Loc.T("hint.favorite")),
                new(NavAction.Search, Loc.T("hint.search")),
            ];

    [RelayCommand] private void AddFolder() => Ctx.Navigator.Navigate(PageKey.Imports, ImportsRequest.AddFolder);
    [RelayCommand] private void OpenImports() => Ctx.Navigator.Navigate(PageKey.Imports);

    partial void OnSearchTextChanged(string value)
    {
        _searchDebounce.Stop();
        _searchDebounce.Start();
    }

    partial void OnSelectedSortChanged(SortOption value)
    {
        if (_suppressReload || value == null) return;
        Ctx.Services.Ui.Update(p => p.LibrarySort = value.Sort.ToString());
        _ = LoadAsync();
    }

    partial void OnFavoritesOnlyChanged(bool value) { if (!_suppressReload) _ = LoadAsync(); }

    partial void OnIsListViewChanged(bool value)
    {
        OnPropertyChanged(nameof(IsGridView));
        RaiseHintsChanged();
        if (_suppressReload) return;
        try { Ctx.Services.Settings.Update(s => s.LibraryView = value ? "list" : "grid"); }
        catch (Exception ex) { Core.Logging.Log.Warn("Saving the library view failed", ex); }
    }

    public override void OnNavigatedTo(object? argument, bool isBack)
    {
        Title = Loc.T("nav.library");
        if (argument is LibraryRequest req && !isBack)
        {
            _suppressReload = true;
            FavoritesOnly = req.Favorites;
            if (req.SystemId != null) SelectedSystemId = req.SystemId;
            if (req.Sort is { } sort && SortOptions.FirstOrDefault(o => o.Sort == sort) is { } option) SelectedSort = option;
            _suppressReload = false;
            if (req.FocusSearch) FocusSearchRequested?.Invoke();
        }
        _ = LoadAsync();
    }

    public override bool HandleAction(NavAction action)
    {
        switch (action)
        {
            case NavAction.View: IsListView = !IsListView; return true;
            case NavAction.Search: FocusSearchRequested?.Invoke(); return true;
            // the shoulders step through the ticker (DESIGN 7.0: page-local first – library = compartment)
            case NavAction.PageLeft: return StepChip(-1);
            case NavAction.PageRight: return StepChip(+1);
            default: return false;
        }
    }

    /// <summary>Selects the previous/next ticker entry (wrapping). False when there is nothing to step to.</summary>
    public bool StepChip(int delta)
    {
        if (Chips.Count < 2) return false;
        var index = Chips.ToList().FindIndex(c => c.IsSelected);
        if (index < 0) index = 0;
        var next = Chips[((index + delta) % Chips.Count + Chips.Count) % Chips.Count];
        SelectChip(next);
        return true;
    }

    public async Task LoadAsync()
    {
        var version = ++_version;
        var systemId = SelectedSystemId;
        var query = new GameQuery
        {
            SystemId = systemId,
            Text = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim(),
            Favorite = FavoritesOnly ? true : null,
            Sort = SelectedSort.Sort,
        };
        var repo = Ctx.Services.Games;
        var unfiltered = query.SystemId == null && query.Text == null && query.Favorite == null;
        var result = await UiContext.Background(() =>
        {
            var games = repo.Query(query);
            var stats = repo.Stats();
            // the sticker rule looks at the whole library; the system's cover sizes at all of its games
            var all = unfiltered ? games : repo.Query(new GameQuery());
            var sized = systemId == null ? null : (query.Text == null && query.Favorite == null ? games : repo.Query(new GameQuery { SystemId = systemId }));
            return (Games: games, Stats: stats, All: all, Sized: sized);
        }, "Loading the library");
        if (version != _version || result.Games == null) return;

        // the slot format and the system context are set BEFORE the items: the grid is built once, in its final format
        ApplySystemContext(systemId, result.Stats, result.Sized);
        var items = Ctx.Items.GetAll(result.Games);
        MarkVariants(items);
        MarkNew(Ctx.Items.GetAll(result.All), DateTime.UtcNow);
        Items = items;
        UpdateChips(result.Stats);
        var total = result.Stats.GameCount;
        CountText = Items.Count == total ? Loc.T("library.count", total) : Loc.T("library.countFiltered", Items.Count, total);
        _total = total;
        UpdateLocation(); // the collection count lives in the footer's location sentence (DESIGN 7.0)
        UpdatePreview();
        IsLibraryEmpty = total == 0;
        IsEmpty = Items.Count == 0;
        EmptyText = total == 0 ? Loc.T("library.emptyLibrary") : Loc.T("library.noMatches");
        IsLoaded = true;
    }

    /// <summary>
    /// The compartment of a system (DESIGN 7.2.1, 11.4): banner text, wall ground, system light, and the slot format
    /// from the median of the stored cover sizes (≥ <see cref="MinSizesForMedian"/>, else the system's start value).
    /// "Alle": the room, the user's light, 1 : 1.25.
    /// </summary>
    private void ApplySystemContext(string? systemId, LibraryStats stats, IReadOnlyList<Game>? sized)
    {
        var def = systemId != null ? Ctx.Services.Catalog.GetSystem(systemId) : null;
        if (systemId == null || def == null)
        {
            SlotAspect = MixedAspect;
            IsSystemSelected = false;
            SystemLook = null;
            ContextLight = null;
            Ground = SystemPalette.Room;
            Backdrop = new BackdropSpec(BackdropKind.Room, AccentBoard: true);
            return;
        }
        var accent = ColorUtil.Parse(def.Accent, Color.Parse("#948A7A"));
        var look = SystemPalette.Get(systemId, accent);
        SlotAspect = SlotAspectFor(systemId, sized ?? []);
        BannerTitle = def.Name;
        BannerAccent = accent;
        BannerSentence = BuildBannerSentence(def, stats.GamesPerSystem.GetValueOrDefault(systemId), stats.PlaySecondsPerSystem.GetValueOrDefault(systemId));
        SystemLook = look;
        ContextLight = look.Light;
        Ground = look.Wall;
        IsSystemSelected = true;
        var spec = new BackdropSpec(BackdropKind.Wall, SystemId: systemId, Accent: accent);
        if (Backdrop != spec) Backdrop = spec;
    }

    /// <summary>Median height/width of the stored cover sizes of a system; the start value with fewer than 8 (DESIGN 11.4).</summary>
    public static double SlotAspectFor(string systemId, IEnumerable<Game> games)
    {
        var ratios = games.Where(g => g.CoverWidth is > 0 && g.CoverHeight is > 0 && !string.IsNullOrEmpty(g.CoverPath))
            .Select(g => (double)g.CoverHeight!.Value / g.CoverWidth!.Value).OrderBy(r => r).ToList();
        if (ratios.Count < MinSizesForMedian) return SystemPalette.BoxAspect(systemId);
        var mid = ratios.Count / 2;
        var median = ratios.Count % 2 == 1 ? ratios[mid] : (ratios[mid - 1] + ratios[mid]) / 2;
        return Math.Round(Math.Clamp(median, 0.6, 1.8), 2);
    }

    /// <summary>"Handheld von Nintendo, 2001. 7 Spiele, 46 Stunden gespielt." – parts without data fall away.</summary>
    internal static string BuildBannerSentence(SystemDef def, int games, long playSeconds)
    {
        var first = Fmt.Category(def.Category);
        if (!string.IsNullOrEmpty(def.Manufacturer)) first = Loc.T("detail.meta.by", first, def.Manufacturer);
        if (def.Year is { } y) first = $"{first}, {y}";
        var count = games == 1 ? Loc.T("systems.oneGame") : Loc.T("systems.games", games);
        var hours = (int)(playSeconds / 3600);
        var minutes = (int)(playSeconds / 60);
        var played = hours >= 2 ? Loc.T("library.banner.hours", hours)
            : hours == 1 ? Loc.T("library.banner.hour")
            : minutes >= 1 ? Loc.T("library.banner.minutes", minutes)
            : Loc.T("library.banner.never");
        return $"{first}. {count}, {played}.";
    }

    /// <summary>
    /// Two games of one system with one title (after region/version tags) carry their region or language in the label
    /// line ("Englisch" / "Japan"), so namesakes – translations, BS chapters, test ROMs – are told apart.
    /// </summary>
    internal static void MarkVariants(IReadOnlyList<GameItemViewModel> items)
    {
        var groups = items.GroupBy(i => (i.SystemId, HomeViewModel.NormalizeTitle(i.Title))).Where(g => g.Count() > 1)
            .SelectMany(g => g).ToHashSet();
        foreach (var item in items)
        {
            var variant = groups.Contains(item) ? item.RegionText : "";
            if (variant.Length == 0 && groups.Contains(item)) variant = Loc.T("library.variant.original");
            if (item.Variant != variant) item.Variant = variant;
        }
    }

    /// <summary>
    /// The sticker "Neu" (DESIGN 7.2.1): added within 7 days and never started, not yet unpacked in this session.
    /// A freshly imported shelf is not news: when more than a quarter of the library (and more than 3 games) would
    /// carry a sticker, none does – a wall of stickers marks nothing.
    /// </summary>
    internal static void MarkNew(IReadOnlyList<GameItemViewModel> all, DateTime nowUtc)
    {
        var limit = nowUtc.AddDays(-NewDays);
        var candidates = all.Where(i => i.Game.AddedUtc > limit && i.Game.LastPlayedUtc == null && i.Game.PlayCount == 0).ToHashSet();
        var flood = candidates.Count > 3 && candidates.Count > all.Count / 4;
        foreach (var item in all)
        {
            var isNew = !flood && candidates.Contains(item) && !item.WasUnpacked;
            if (item.IsNew != isNew) item.IsNew = isNew;
        }
    }

    private void UpdateChips(LibraryStats stats)
    {
        var wanted = new List<(string? Id, string Label, int Count, IBrush? Mark)> { (null, Loc.T("library.all"), stats.GameCount, null) };
        foreach (var sys in Ctx.Services.Catalog.Systems.OrderBy(s => s.SortOrder))
        {
            var n = stats.GamesPerSystem.GetValueOrDefault(sys.Id);
            if (n <= 0) continue;
            var look = SystemPalette.Get(sys.Id, ColorUtil.Parse(sys.Accent, Color.Parse("#948A7A")));
            wanted.Add((sys.Id, string.IsNullOrEmpty(sys.ShortName) ? sys.Name : sys.ShortName, n, look.ColorBrush));
        }
        var same = Chips.Count == wanted.Count && Chips.Select(c => c.SystemId).SequenceEqual(wanted.Select(w => w.Id));
        if (!same)
        {
            Chips.Clear();
            foreach (var w in wanted) Chips.Add(new FilterChip { SystemId = w.Id, Label = w.Label, Count = w.Count, MarkBrush = w.Mark });
        }
        for (var i = 0; i < wanted.Count; i++)
        {
            Chips[i].Count = wanted[i].Count;
            Chips[i].IsSelected = Chips[i].SystemId == SelectedSystemId;
        }
    }

    [RelayCommand]
    private void SelectChip(FilterChip? chip)
    {
        SelectedSystemId = chip?.SystemId;
        foreach (var c in Chips) c.IsSelected = c.SystemId == SelectedSystemId;
        _ = LoadAsync();
    }

    private int _total;

    /// <summary>Index of the focused game in <see cref="Items"/> (set by the view; −1 = none yet).</summary>
    [ObservableProperty] public partial int FocusIndex { get; set; } = -1;

    partial void OnFocusIndexChanged(int value)
    {
        UpdateLocation();
        UpdatePreview();
    }

    /// <summary>The list's preview shows the focused game (the first one before anything is focused).</summary>
    private void UpdatePreview()
    {
        var item = Items.Count == 0 ? null : Items[Math.Clamp(FocusIndex, 0, Items.Count - 1)];
        if (item == PreviewItem) return;
        PreviewItem = item;
        PreviewMeta = item == null ? "" : BuildPreviewMeta(item);
    }

    /// <summary>"Action von Konami, 2003. Noch nie gespielt. …" – the detail's sentences, short.</summary>
    private static string BuildPreviewMeta(GameItemViewModel item)
    {
        var meta = GameDetailViewModel.BuildMetaSentence(item.Game, item.SystemName);
        var play = GameDetailViewModel.BuildPlaySentence(item.Game);
        return string.IsNullOrEmpty(meta) ? play : $"{meta} {play}";
    }

    /// <summary>"Alle Spiele, 12 von 66" / "Game Boy Advance, 3 von 7" / "Favoriten, 1 von 11" (DESIGN 7.0).</summary>
    private void UpdateLocation()
    {
        var n = Items.Count;
        if (n == 0)
        {
            Location = _total == 0 ? Loc.T("library.location.empty") : Loc.T("library.location.none");
            return;
        }
        var pos = Math.Clamp(FocusIndex + 1, 1, n);
        var system = SelectedSystemId != null ? Ctx.Services.Catalog.GetSystem(SelectedSystemId)?.Name : null;
        var scope = !string.IsNullOrWhiteSpace(SearchText) ? Loc.T("library.location.search", SearchText.Trim())
            : system ?? (FavoritesOnly ? Loc.T("library.location.favorites") : null);
        Location = scope == null && n == _total ? Loc.T("library.location.all", pos, n) : Loc.T("library.location.scope", scope ?? Loc.T("library.all"), pos, n);
    }

    [RelayCommand] private void ShowGrid() => IsListView = false;
    [RelayCommand] private void ShowList() => IsListView = true;
    [RelayCommand] private void ToggleView() => IsListView = !IsListView;
    [RelayCommand] private void ClearSearch() => SearchText = "";
    [RelayCommand] private void PreviousChip() => StepChip(-1);
    [RelayCommand] private void NextChip() => StepChip(+1);

    /// <summary>"Spielen" in the list's preview.</summary>
    [RelayCommand]
    private Task PlayPreview() => PreviewItem is { } item ? Ctx.Launch.LaunchAsync(item) : Task.CompletedTask;

    /// <summary>"Details" in the list's preview.</summary>
    [RelayCommand]
    private void OpenPreview()
    {
        if (PreviewItem is { } item) Ctx.Navigator.Navigate(PageKey.GameDetail, item);
    }
}
