using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetroHub.App.Input;
using RetroHub.App.Localization;
using RetroHub.App.Theming;
using RetroHub.Core.Library;

namespace RetroHub.App.ViewModels;

/// <summary>Dashboard: "Weiterspielen" hero, stats, shelves (recently played, favourites, recently added, platforms).</summary>
public sealed partial class HomeViewModel : PageViewModel
{
    private int _version;

    public HomeViewModel(UiContext ctx) : base(ctx)
    {
        Title = Loc.T("nav.home");
        ctx.LibraryChanged += () => { if (IsLoaded) _ = LoadAsync(); };
    }

    public override PageKey Key => PageKey.Home;

    [ObservableProperty] public partial GameItemViewModel? Continue { get; private set; }
    [ObservableProperty] public partial bool HasContinue { get; private set; }
    [ObservableProperty] public partial bool IsLibraryEmpty { get; private set; }
    [ObservableProperty] public partial bool IsLoaded { get; private set; }
    [ObservableProperty] public partial string ContinueMeta { get; private set; } = "";
    [ObservableProperty] public partial string Greeting { get; private set; } = "";

    [ObservableProperty] public partial string StatGames { get; private set; } = "–";
    [ObservableProperty] public partial string StatPlatforms { get; private set; } = "–";
    [ObservableProperty] public partial string StatPlayTime { get; private set; } = "–";
    [ObservableProperty] public partial string StatFavorites { get; private set; } = "–";

    [ObservableProperty] public partial IReadOnlyList<GameItemViewModel> RecentlyPlayed { get; private set; } = [];
    [ObservableProperty] public partial IReadOnlyList<GameItemViewModel> Favorites { get; private set; } = [];
    [ObservableProperty] public partial IReadOnlyList<GameItemViewModel> RecentlyAdded { get; private set; } = [];
    [ObservableProperty] public partial IReadOnlyList<SystemItemViewModel> Platforms { get; private set; } = [];

    public bool HasRecentlyPlayed => RecentlyPlayed.Count > 0;
    public bool HasFavorites => Favorites.Count > 0;
    public bool HasRecentlyAdded => RecentlyAdded.Count > 0;

    partial void OnRecentlyPlayedChanged(IReadOnlyList<GameItemViewModel> value) => OnPropertyChanged(nameof(HasRecentlyPlayed));
    partial void OnFavoritesChanged(IReadOnlyList<GameItemViewModel> value) => OnPropertyChanged(nameof(HasFavorites));
    partial void OnRecentlyAddedChanged(IReadOnlyList<GameItemViewModel> value) => OnPropertyChanged(nameof(HasRecentlyAdded));

    public override IReadOnlyList<HintItem> Hints =>
    [
        new(NavAction.Accept, Loc.T("hint.open")),
        new(NavAction.Favorite, Loc.T("hint.favorite")),
        new(NavAction.Search, Loc.T("hint.search")),
        new(NavAction.Menu, Loc.T("hint.menu")),
    ];

    public override void OnNavigatedTo(object? argument, bool isBack)
    {
        Title = Loc.T("nav.home");
        _ = LoadAsync();
    }

    private sealed record Snapshot(Game? Last, IReadOnlyList<Game> Recent, IReadOnlyList<Game> Favs, IReadOnlyList<Game> Added, LibraryStats Stats);

    public async Task LoadAsync()
    {
        var version = ++_version;
        var repo = Ctx.Services.Games;
        var snap = await UiContext.Background(() =>
        {
            var recent = repo.Query(new GameQuery { Sort = GameSort.LastPlayed, Limit = 16 }).Where(g => g.LastPlayedUtc != null).Take(12).ToList();
            var favs = repo.Query(new GameQuery { Favorite = true, Sort = GameSort.LastPlayed, Limit = 16 });
            var added = repo.Query(new GameQuery { Sort = GameSort.Added, Limit = 12 });
            return new Snapshot(recent.FirstOrDefault(), recent, favs, added, repo.Stats());
        }, "Loading the dashboard");
        if (snap == null || version != _version) return;

        var items = Ctx.Items;
        var previous = Continue;
        Continue = snap.Last != null ? items.Get(snap.Last) : null;
        HasContinue = Continue != null;
        // TV light of the game to continue, above the stage board; the glow sits at its box (right)
        if (Continue != previous || Backdrop.Kind != BackdropKind.TvLight)
            Backdrop = Continue != null
                ? new BackdropSpec(BackdropKind.TvLight, Continue.CoverPath, Continue.SystemId, Continue.Accent, Continue.Id, StageShare: 0.55, GlowX: 0.78, GlowY: 0.32)
                : new BackdropSpec(BackdropKind.TvLight, StageShare: 0.55);
        ContinueMeta = Continue == null ? "" : Loc.T("home.continueMeta", Continue.SystemName, Continue.LastPlayedText, Continue.PlayTimeText);
        // Lists are only replaced when their content changed: a new list rebuilds the shelf, which drops the keyboard /
        // controller focus of its tiles (refreshes happen often, e.g. while a scan runs). The items are shared
        // instances (GameItemCache), so unchanged shelves still show updated favourites/play times.
        RecentlyPlayed = Keep(RecentlyPlayed, items.GetAll(snap.Recent.Skip(snap.Last != null ? 1 : 0)));
        Favorites = Keep(Favorites, items.GetAll(snap.Favs));
        RecentlyAdded = Keep(RecentlyAdded, items.GetAll(snap.Added));
        Greeting = GreetingText(DateTime.Now.Hour); // no uppercase overlines (DESIGN 3.2)

        var stats = snap.Stats;
        StatGames = stats.GameCount.ToString("N0", Loc.Culture);
        StatPlatforms = stats.SystemCount.ToString("N0", Loc.Culture);
        StatPlayTime = Fmt.PlayTimeShort(stats.TotalPlaySeconds);
        StatFavorites = stats.FavoriteCount.ToString("N0", Loc.Culture);
        IsLibraryEmpty = stats.GameCount == 0;

        var platforms = Ctx.Services.Catalog.Systems
            .Where(s => stats.GamesPerSystem.GetValueOrDefault(s.Id) > 0)
            .OrderByDescending(s => stats.PlaySecondsPerSystem.GetValueOrDefault(s.Id))
            .ThenBy(s => s.SortOrder)
            .ToList();
        Platforms = SystemItemViewModel.Reconcile(Platforms, platforms, stats);
        IsLoaded = true;
    }

    private static IReadOnlyList<GameItemViewModel> Keep(IReadOnlyList<GameItemViewModel> current, IReadOnlyList<GameItemViewModel> fresh) =>
        current.Count == fresh.Count && current.SequenceEqual(fresh) ? current : fresh;

    private static string GreetingText(int hour) => hour switch
    {
        < 5 => Loc.T("home.greeting.night"),
        < 11 => Loc.T("home.greeting.morning"),
        < 18 => Loc.T("home.greeting.day"),
        _ => Loc.T("home.greeting.evening"),
    };

    [RelayCommand]
    private Task PlayContinue() => Continue != null ? Ctx.Launch.LaunchAsync(Continue) : Task.CompletedTask;

    [RelayCommand]
    private void OpenContinue()
    {
        if (Continue != null) Ctx.Navigator.Navigate(PageKey.GameDetail, Continue);
    }

    [RelayCommand] private void ShowLibrary() => Ctx.Navigator.Navigate(PageKey.Library);
    [RelayCommand] private void ShowFavorites() => Ctx.Navigator.Navigate(PageKey.Library, LibraryRequest.FavoritesOnly);
    [RelayCommand] private void ShowSystems() => Ctx.Navigator.Navigate(PageKey.Systems);
    [RelayCommand] private void ShowImports() => Ctx.Navigator.Navigate(PageKey.Imports);
}
