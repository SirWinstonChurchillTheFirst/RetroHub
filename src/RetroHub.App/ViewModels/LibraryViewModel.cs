using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetroHub.App.Input;
using RetroHub.App.Localization;
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

/// <summary>A filter chip ("Alle", "GBA 7").</summary>
public sealed partial class FilterChip : ObservableObject
{
    public string? SystemId { get; init; }
    public required string Label { get; init; }
    [ObservableProperty] public partial int Count { get; set; }
    [ObservableProperty] public partial bool IsSelected { get; set; }
}

public sealed record SortOption(GameSort Sort, string Label)
{
    public override string ToString() => Label;
}

/// <summary>The whole library: cover grid or list, search, system chips, sort, favourites.</summary>
public sealed partial class LibraryViewModel : PageViewModel
{
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

    /// <summary>Raised when the page should put the keyboard focus into the search box.</summary>
    public event Action? FocusSearchRequested;

    public bool IsGridView => !IsListView;

    public override IReadOnlyList<HintItem> Hints =>
    [
        new(NavAction.Accept, Loc.T("hint.open")),
        new(NavAction.Favorite, Loc.T("hint.favorite")),
        new(NavAction.Search, Loc.T("hint.search")),
        new(NavAction.View, IsListView ? Loc.T("hint.gridView") : Loc.T("hint.listView")),
        new(NavAction.PageLeft, Loc.T("hint.section")),
    ];

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
        if (action == NavAction.View) { IsListView = !IsListView; return true; }
        if (action == NavAction.Search) { FocusSearchRequested?.Invoke(); return true; }
        return false;
    }

    public async Task LoadAsync()
    {
        var version = ++_version;
        var query = new GameQuery
        {
            SystemId = SelectedSystemId,
            Text = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim(),
            Favorite = FavoritesOnly ? true : null,
            Sort = SelectedSort.Sort,
        };
        var repo = Ctx.Services.Games;
        var result = await UiContext.Background(() => (Games: repo.Query(query), Stats: repo.Stats()), "Loading the library");
        if (version != _version || result.Games == null) return;

        Items = Ctx.Items.GetAll(result.Games);
        UpdateChips(result.Stats);
        var total = result.Stats.GameCount;
        CountText = Items.Count == total ? Loc.T("library.count", total) : Loc.T("library.countFiltered", Items.Count, total);
        _total = total;
        UpdateLocation(); // the collection count lives in the footer's location sentence (DESIGN 7.0)
        IsEmpty = Items.Count == 0;
        EmptyText = total == 0 ? Loc.T("library.emptyLibrary") : Loc.T("library.noMatches");
        IsLoaded = true;
    }

    private void UpdateChips(LibraryStats stats)
    {
        var wanted = new List<(string? Id, string Label, int Count)> { (null, Loc.T("library.all"), stats.GameCount) };
        foreach (var sys in Ctx.Services.Catalog.Systems.OrderBy(s => s.SortOrder))
        {
            var n = stats.GamesPerSystem.GetValueOrDefault(sys.Id);
            if (n > 0) wanted.Add((sys.Id, string.IsNullOrEmpty(sys.ShortName) ? sys.Name : sys.ShortName, n));
        }
        var same = Chips.Count == wanted.Count && Chips.Select(c => c.SystemId).SequenceEqual(wanted.Select(w => w.Id));
        if (!same)
        {
            Chips.Clear();
            foreach (var w in wanted) Chips.Add(new FilterChip { SystemId = w.Id, Label = w.Label, Count = w.Count });
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

    partial void OnFocusIndexChanged(int value) => UpdateLocation();

    /// <summary>"Alle Spiele, 12 von 66" / "Game Boy Advance, 3 von 7" / "Favoriten, 1 von 11" (DESIGN 7.0).</summary>
    private void UpdateLocation()
    {
        var n = Items.Count;
        if (n == 0)
        {
            Location = Loc.T("library.location.none");
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
    [RelayCommand] private void ClearSearch() => SearchText = "";
}
