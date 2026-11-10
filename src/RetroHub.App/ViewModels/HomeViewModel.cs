using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetroHub.App.Input;
using RetroHub.App.Localization;
using RetroHub.App.Theming;
using RetroHub.Core.Catalog;
using RetroHub.Core.Library;
using RetroHub.Core.Metadata;

namespace RetroHub.App.ViewModels;

/// <summary>Where the focus is on Start: it decides the footer's location sentence and the hints (DESIGN 7.0).</summary>
public enum HomeFocus
{
    /// <summary>The primary action ("Spielen", "Öffnen", "Ordner hinzufügen").</summary>
    Primary,
    Details,
    Star,
    Imports,
    /// <summary>The box on the stage.</summary>
    StageBox,
    /// <summary>A game box on a shelf.</summary>
    ShelfGame,
    /// <summary>A system spine on the "Konsolen" shelf.</summary>
    ShelfSystem,
    /// <summary>The "Alle anzeigen" spine at the end of a shelf.</summary>
    ShelfAction,
}

/// <summary>
/// Start ("Der Fernseher im Regal", DESIGN 7.1): the stage (TV + box + text) and the shelves below it. The stage shows
/// the game to continue until a shelf item has been focused for <see cref="Motion.Dwell"/> (the view calls
/// <see cref="Present(GameItemViewModel, string, int, int)"/>). When the focus comes back to the stage and rests there for
/// the dwell, the view calls <see cref="ReturnToContinue"/>: Start's main action is always "Spielen" for the game to
/// continue (M2 QA); a browsed game is played from its box. The footer follows the focus (<see cref="SetFocus"/>).
/// </summary>
public sealed partial class HomeViewModel : PageViewModel
{
    private int _version;
    private int _presentVersion;
    private int _screenVersion;
    private CancellationTokenSource? _screenCts;

    /// <summary>Most items per shelf (DESIGN 7.1).</summary>
    public const int RecentMax = 11, FavoritesMax = 16, AddedMax = 12;

    public HomeViewModel(UiContext ctx) : base(ctx)
    {
        Title = Loc.T("nav.home");
        ctx.LibraryChanged += () => { if (IsLoaded) _ = LoadAsync(); };
    }

    public override PageKey Key => PageKey.Home;

    /// <summary>The game to continue (last played; without any play the newest game), null in an empty library.</summary>
    [ObservableProperty] public partial GameItemViewModel? Continue { get; private set; }
    [ObservableProperty] public partial bool HasContinue { get; private set; }
    [ObservableProperty] public partial bool IsLibraryEmpty { get; private set; }
    [ObservableProperty] public partial bool IsLoaded { get; private set; }

    /// <summary>What the stage shows (subject, texts, primary action).</summary>
    [ObservableProperty] public partial StageState? Stage { get; private set; }
    /// <summary>The TV picture of the stage subject: snap → title screen (null = "Kein Signal").</summary>
    [ObservableProperty] public partial string? StageScreen { get; private set; }

    [ObservableProperty] public partial IReadOnlyList<GameItemViewModel> RecentlyPlayed { get; private set; } = [];
    [ObservableProperty] public partial IReadOnlyList<GameItemViewModel> Favorites { get; private set; } = [];
    [ObservableProperty] public partial IReadOnlyList<GameItemViewModel> RecentlyAdded { get; private set; } = [];
    [ObservableProperty] public partial IReadOnlyList<SystemItemViewModel> Platforms { get; private set; } = [];

    public bool HasRecentlyPlayed => RecentlyPlayed.Count > 0;
    public bool HasFavorites => Favorites.Count > 0;
    public bool HasRecentlyAdded => RecentlyAdded.Count > 0;
    public bool HasPlatforms => Platforms.Count > 0;

    partial void OnRecentlyPlayedChanged(IReadOnlyList<GameItemViewModel> value) => OnPropertyChanged(nameof(HasRecentlyPlayed));
    partial void OnFavoritesChanged(IReadOnlyList<GameItemViewModel> value) => OnPropertyChanged(nameof(HasFavorites));
    partial void OnRecentlyAddedChanged(IReadOnlyList<GameItemViewModel> value) => OnPropertyChanged(nameof(HasRecentlyAdded));
    partial void OnPlatformsChanged(IReadOnlyList<SystemItemViewModel> value) => OnPropertyChanged(nameof(HasPlatforms));

    /// <summary>
    /// Hints of the focused element (M2 QA): confirm says what it does there ("Spielen", "Öffnen", "Details", "Ordner
    /// hinzufügen"); "Favorit" only where F does something (a game on the stage or on a shelf); the empty library has
    /// only "Ordner hinzufügen" and "Menü".
    /// </summary>
    public override IReadOnlyList<HintItem> Hints
    {
        get
        {
            var accept = _focus switch
            {
                HomeFocus.Primary => Stage?.PrimaryLabel ?? Loc.T("hint.open"),
                HomeFocus.Details => Loc.T("home.details"),
                HomeFocus.Star => Stage?.Game?.IsFavorite == true ? Loc.T("hint.unfavorite") : Loc.T("hint.favorite"),
                HomeFocus.Imports => Loc.T("home.openImports"),
                _ => Loc.T("hint.open"),
            };
            if (IsLibraryEmpty || Stage?.IsEmpty == true) return [new(NavAction.Accept, accept), new(NavAction.Menu, Loc.T("hint.menu"))];
            var list = new List<HintItem> { new(NavAction.Accept, accept) };
            if (CanFavorite) list.Add(new(NavAction.Favorite, Loc.T("hint.favorite")));
            list.Add(new(NavAction.Search, Loc.T("hint.search")));
            list.Add(new(NavAction.Menu, Loc.T("hint.menu")));
            return list;
        }
    }

    private HomeFocus _focus = HomeFocus.Primary;
    private (string Shelf, int Index, int Count, string? Action) _shelfPosition;

    /// <summary>The focused element can be a favourite: a game box, or the stage's actions while a game is on the stage.</summary>
    private bool CanFavorite => _focus switch
    {
        HomeFocus.ShelfGame or HomeFocus.StageBox => true,
        HomeFocus.Primary or HomeFocus.Details or HomeFocus.Star => Stage?.IsGame == true,
        _ => false,
    };

    /// <summary>The focus moved (the view reports it): location sentence and hints follow at once, not after the dwell.</summary>
    public void SetFocus(HomeFocus focus, string? shelf = null, int index = 0, int count = 0, string? action = null)
    {
        _focus = focus;
        _shelfPosition = (shelf ?? "", index, count, action);
        UpdateLocation();
        RaiseHintsChanged();
    }

    /// <summary>The focus rests on the stage again (dwell): back to the game to continue (DESIGN 7.1).</summary>
    public void ReturnToContinue()
    {
        if (Stage is { Kind: StageKind.Game or StageKind.System }) PresentContinue();
    }

    /// <summary>"Start › Weiterspielen" (actions), "Start › Bühne, Titel" (stage box), "Start › Brett, n von m" (shelf).</summary>
    private void UpdateLocation()
    {
        if (IsLibraryEmpty || Stage?.IsEmpty == true) { Location = null; return; }
        Location = _focus switch
        {
            HomeFocus.StageBox => Loc.T("home.location.stage", Stage?.Game?.Title ?? ""),
            HomeFocus.ShelfGame or HomeFocus.ShelfSystem => Loc.T("home.location.shelf", _shelfPosition.Shelf, _shelfPosition.Index + 1, _shelfPosition.Count),
            HomeFocus.ShelfAction => Loc.T("home.location.shelfAll", _shelfPosition.Shelf, _shelfPosition.Action ?? ""),
            _ => Loc.T("home.location"),
        };
    }

    /// <summary>F on the stage's actions toggles the favourite of the game on the stage (the boxes handle it themselves).</summary>
    public override bool HandleAction(NavAction action)
    {
        if (action != NavAction.Favorite || !CanFavorite || Stage is not { IsGame: true, Game: { } g }) return false;
        _ = Ctx.ToggleFavoriteAsync(g);
        return true;
    }

    public override void OnNavigatedTo(object? argument, bool isBack)
    {
        Title = Loc.T("nav.home");
        // a fresh visit shows the game to continue again; coming back keeps what was on the stage
        if (!isBack && Stage is { Kind: StageKind.Game or StageKind.System }) _resetStage = true;
        _ = LoadAsync();
    }

    private bool _resetStage;

    private sealed record Snapshot(Game? Last, IReadOnlyList<Game> Recent, IReadOnlyList<Game> Favs, IReadOnlyList<Game> Added, LibraryStats Stats);

    public async Task LoadAsync()
    {
        var version = ++_version;
        var repo = Ctx.Services.Games;
        var snap = await UiContext.Background(() =>
        {
            // region/version variants of one game stand on a shelf once (the newest by the shelf's order); the library
            // and the system page stay complete
            var recent = Distinct(repo.Query(new GameQuery { Sort = GameSort.LastPlayed, Limit = RecentMax * 3 + 4 }).Where(g => g.LastPlayedUtc != null), RecentMax + 1);
            var favs = Distinct(repo.Query(new GameQuery { Favorite = true, Sort = GameSort.LastPlayed, Limit = FavoritesMax * 3 }), FavoritesMax);
            var added = Distinct(repo.Query(new GameQuery { Sort = GameSort.Added, Limit = AddedMax * 4 }), AddedMax);
            return new Snapshot(recent.FirstOrDefault() ?? added.FirstOrDefault(), recent, favs, added, repo.Stats());
        }, "Loading the dashboard");
        if (snap == null || version != _version) return;

        var items = Ctx.Items;
        Continue = snap.Last != null ? items.Get(snap.Last) : null;
        HasContinue = Continue != null;
        IsLibraryEmpty = snap.Stats.GameCount == 0;
        // Lists are only replaced when their content changed: a new list rebuilds the shelf, which drops the keyboard /
        // controller focus of its tiles (refreshes happen often, e.g. while a scan runs). The items are shared
        // instances (GameItemCache), so unchanged shelves still show updated favourites/play times.
        var played = snap.Last?.LastPlayedUtc != null;
        RecentlyPlayed = Keep(RecentlyPlayed, items.GetAll(snap.Recent.Skip(played ? 1 : 0).Take(RecentMax)));
        Favorites = Keep(Favorites, items.GetAll(snap.Favs));
        RecentlyAdded = Keep(RecentlyAdded, items.GetAll(snap.Added));

        var stats = snap.Stats;
        var platforms = Ctx.Services.Catalog.Systems
            .Where(s => stats.GamesPerSystem.GetValueOrDefault(s.Id) > 0)
            .OrderByDescending(s => stats.PlaySecondsPerSystem.GetValueOrDefault(s.Id))
            .ThenBy(s => s.SortOrder)
            .ToList();
        Platforms = SystemItemViewModel.Reconcile(Platforms, platforms, stats);

        // the stage: keep a browsed subject (a refresh must not yank it away), else the game to continue
        if (_resetStage || Stage == null || Stage.Kind is StageKind.Continue or StageKind.Empty
            || (Stage.Game != null && !ReferenceEquals(Ctx.Items.TryGet(Stage.Game.Id), Stage.Game)))
        {
            _resetStage = false;
            PresentContinue();
        }
        else if (Stage.Game != null) RefreshMeta();
        IsLoaded = true;
    }

    // ===================== stage =====================

    /// <summary>Shows the game to continue (or the empty library) on the stage.</summary>
    public void PresentContinue()
    {
        ++_presentVersion;
        if (Continue is not { } game)
        {
            SetStage(new StageState(StageKind.Empty, "", Loc.T("home.empty.title"), Loc.T("home.empty.text"), null, null, null, default, null, true,
                Loc.T("home.addFolder")), null);
            return;
        }
        var label = game.HasPlayed ? $"{Loc.T("home.continue")}: {game.LastPlayedText}" : Loc.T("home.shelf.added");
        SetStage(GameStage(StageKind.Continue, game, label), game);
    }

    partial void OnStageChanged(StageState? value)
    {
        UpdateLocation();
        RaiseHintsChanged();
    }

    partial void OnIsLibraryEmptyChanged(bool value)
    {
        UpdateLocation();
        RaiseHintsChanged();
    }

    /// <summary>A game focused on a shelf (after the dwell): the stage shows it under the shelf's name.</summary>
    public void Present(GameItemViewModel game, string shelf, int index, int count)
    {
        ++_presentVersion;
        SetStage(GameStage(StageKind.Game, game, shelf), game);
    }

    /// <summary>A system spine focused: "Kein Signal" of that system, its last played game as the box, "Öffnen".</summary>
    public void Present(SystemItemViewModel system, string shelf, int index, int count)
    {
        var version = ++_presentVersion;
        var repo = Ctx.Services.Games;
        _ = PresentSystemAsync(system, version, repo);
    }

    private async Task PresentSystemAsync(SystemItemViewModel system, int version, IGameRepository repo)
    {
        var last = await UiContext.Background(() =>
            repo.Query(new GameQuery { SystemId = system.Id, Sort = GameSort.LastPlayed, Limit = 1 }).FirstOrDefault(), "Loading the system's last game");
        if (version != _presentVersion) return;
        var game = last != null ? Ctx.Items.Get(last) : null;
        var meta = BuildSystemMeta(system.Def, system.GameCount, game?.HasPlayed == true ? game.Title : null);
        SetStage(new StageState(StageKind.System, Loc.T("home.shelf.platforms"), system.Name, meta, game, system, system.Id, system.Accent, system.ShortName,
            IsTvSystem(system.Def), Loc.T("home.open")), null);
    }

    private StageState GameStage(StageKind kind, GameItemViewModel game, string label)
    {
        var sys = Ctx.Services.Catalog.GetSystem(game.SystemId);
        return new StageState(kind, label, game.Title, BuildStageMeta(game.SystemName, game.Game), game, null, game.SystemId, game.Accent, game.SystemShort,
            IsTvSystem(sys), Loc.T("detail.play"));
    }

    private void RefreshMeta()
    {
        if (Stage is { Game: { } g } s && s.IsGame) Stage = s with { Meta = BuildStageMeta(g.SystemName, g.Game) };
    }

    /// <summary>Systems whose picture was made for a TV (home consoles): near-4:3 snaps fill the frame.</summary>
    internal static bool IsTvSystem(SystemDef? sys) => sys == null || sys.Category is SystemCategory.Console or SystemCategory.Arcade;

    private void SetStage(StageState stage, GameItemViewModel? screenGame)
    {
        if (Stage != null && Stage.SameSubject(stage) && Stage == stage) return;
        var sameSubject = Stage?.SameSubject(stage) == true;
        Stage = stage;
        if (sameSubject) return;
        // the TV: what is on disk at once, the rest on demand (only with online covers); the room light follows
        _screenCts?.Cancel();
        var version = ++_screenVersion;
        StageScreen = null;
        UpdateBackdrop(stage, null);
        if (screenGame == null || Ctx.Services.Artwork is not { } artwork) return;
        var cts = _screenCts = new CancellationTokenSource();
        var game = screenGame.Game;
        var ui = Avalonia.Threading.Dispatcher.UIThread;
        _ = Task.Run(async () =>
        {
            var local = artwork.FindScreens(game);
            ui.Post(() => ApplyScreen(version, stage, local.Best));
            if (local.Snap != null || !Ctx.Services.Settings.Current.OnlineCovers) return;
            var fetched = await artwork.FetchScreensAsync(game, cts.Token).ConfigureAwait(false);
            if (fetched.Best != local.Best) ui.Post(() => ApplyScreen(version, stage, fetched.Best));
        }, cts.Token).ContinueWith(t => { if (t.IsFaulted) Core.Logging.Log.Warn("Stage screen lookup failed", t.Exception); }, TaskScheduler.Default);
    }

    private void ApplyScreen(int version, StageState stage, string? path)
    {
        if (version != _screenVersion) return;
        StageScreen = path;
        UpdateBackdrop(stage, path);
    }

    /// <summary>The room light (DESIGN 2.5): snap → title screen → cover → the system's wall; the glow sits at the TV.</summary>
    private void UpdateBackdrop(StageState stage, string? screen)
    {
        var image = screen ?? stage.Game?.CoverPath;
        if (stage.IsSystem) image = null; // a system on the stage: its wall
        var spec = stage.IsEmpty
            ? new BackdropSpec(BackdropKind.TvLight, StageShare: 0.55)
            : new BackdropSpec(BackdropKind.TvLight, image, stage.SystemId, stage.Accent, stage.Game?.Id ?? 0, StageShare: 0.55, GlowX: 0.80, GlowY: 0.33, GhostX: 0.62);
        if (Backdrop != spec) Backdrop = spec;
    }

    // ===================== sentences =====================

    /// <summary>"Game Boy Advance. 46 Std. in 41 Sitzungen gespielt, zuletzt heute." / "… Noch nie gespielt. Seit 25.09.2026 in der Sammlung."</summary>
    internal static string BuildStageMeta(string systemName, Game game) => $"{systemName}. {GameDetailViewModel.BuildPlaySentence(game)}";

    /// <summary>"Handheld von Nintendo, 2001. 7 Spiele, zuletzt Pokémon Smaragd-Edition." – parts without data fall away.</summary>
    internal static string BuildSystemMeta(SystemDef def, int games, string? lastGame)
    {
        var first = Fmt.Category(def.Category);
        if (!string.IsNullOrEmpty(def.Manufacturer)) first = Loc.T("detail.meta.by", first, def.Manufacturer);
        if (def.Year is { } y) first = $"{first}, {y}";
        var count = games == 1 ? Loc.T("systems.oneGame") : Loc.T("systems.games", games);
        var second = lastGame != null ? Loc.T("home.system.last", count, lastGame) : count + ".";
        return $"{first}. {second}";
    }

    /// <summary>"Game Boy Advance. 46 Std. gespielt, zuletzt heute." – every sentence starts upper case ("Unter 1 Min.").</summary>
    internal static string BuildContinueMeta(string systemName, string lastPlayed, string playTime) =>
        Loc.T("home.continueMeta", systemName, lastPlayed, Fmt.SentenceStart(playTime));

    /// <summary>
    /// The games in order, each (system, normalised title) only once – the first one wins (the newest by the query's
    /// sort): "Zelda (Germany)", "Zelda (Rev 1)" and "Zelda [!]" are one box on a Start shelf.
    /// </summary>
    internal static List<Game> Distinct(IEnumerable<Game> games, int max)
    {
        var seen = new HashSet<(string, string)>();
        var list = new List<Game>();
        foreach (var g in games)
        {
            if (!seen.Add((g.SystemId, NormalizeTitle(g.Title)))) continue;
            list.Add(g);
            if (list.Count >= max) break;
        }
        return list;
    }

    private static readonly System.Text.RegularExpressions.Regex Tags = new(@"\s*[\(\[][^\)\]]*[\)\]]", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>The title without region/version tags ("(Germany)", "(Rev 1)", "[!]"), whitespace folded, case-insensitive.</summary>
    internal static string NormalizeTitle(string? title) =>
        string.Join(' ', Tags.Replace(title ?? "", " ").Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();

    private static IReadOnlyList<GameItemViewModel> Keep(IReadOnlyList<GameItemViewModel> current, IReadOnlyList<GameItemViewModel> fresh) =>
        current.Count == fresh.Count && current.SequenceEqual(fresh) ? current : fresh;

    // ===================== commands =====================

    /// <summary>"Spielen" (a game on the stage) or "Öffnen" (a system), "Ordner hinzufügen" in an empty library.</summary>
    [RelayCommand]
    private Task StagePrimary()
    {
        switch (Stage)
        {
            case { IsGame: true, Game: { } g }: return Ctx.Launch.LaunchAsync(g);
            case { IsSystem: true, System: { } s }: Ctx.Navigator.Navigate(PageKey.System, s.Id); break;
            case { IsEmpty: true }: Ctx.Navigator.Navigate(PageKey.Imports, ImportsRequest.AddFolder); break;
        }
        return Task.CompletedTask;
    }

    /// <summary>"Details" of the stage game (also the box on the stage).</summary>
    [RelayCommand]
    private void StageDetails()
    {
        if (Stage?.Game is { } g) Ctx.Navigator.Navigate(PageKey.GameDetail, g);
    }

    [RelayCommand]
    private Task StageFavorite() => Stage is { IsGame: true, Game: { } g } ? Ctx.ToggleFavoriteAsync(g) : Task.CompletedTask;

    [RelayCommand]
    private Task PlayContinue() => Continue != null ? Ctx.Launch.LaunchAsync(Continue) : Task.CompletedTask;

    [RelayCommand]
    private void OpenContinue()
    {
        if (Continue != null) Ctx.Navigator.Navigate(PageKey.GameDetail, Continue);
    }

    [RelayCommand] private void ShowLibrary() => Ctx.Navigator.Navigate(PageKey.Library);
    [RelayCommand] private void ShowRecent() => Ctx.Navigator.Navigate(PageKey.Library, LibraryRequest.RecentlyPlayed);
    [RelayCommand] private void ShowAdded() => Ctx.Navigator.Navigate(PageKey.Library, LibraryRequest.RecentlyAdded);
    [RelayCommand] private void ShowFavorites() => Ctx.Navigator.Navigate(PageKey.Library, LibraryRequest.FavoritesOnly);
    [RelayCommand] private void ShowSystems() => Ctx.Navigator.Navigate(PageKey.Systems);
    [RelayCommand] private void ShowImports() => Ctx.Navigator.Navigate(PageKey.Imports);
}
