using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetroHub.App.Input;
using RetroHub.App.Localization;
using RetroHub.Core.Catalog;
using RetroHub.Core.Library;

namespace RetroHub.App.ViewModels;

/// <summary>All platforms as cards with game counts.</summary>
public sealed partial class SystemsViewModel : PageViewModel
{
    private int _version;

    public SystemsViewModel(UiContext ctx) : base(ctx)
    {
        Title = Loc.T("nav.systems");
        ctx.LibraryChanged += () => { if (Systems.Count > 0) _ = LoadAsync(); };
    }

    public override PageKey Key => PageKey.Systems;

    [ObservableProperty] public partial IReadOnlyList<SystemItemViewModel> Systems { get; private set; } = [];
    [ObservableProperty] public partial IReadOnlyList<SystemItemViewModel> EmptySystems { get; private set; } = [];
    [ObservableProperty] public partial string SummaryText { get; private set; } = "";
    public bool HasEmptySystems => EmptySystems.Count > 0;
    partial void OnEmptySystemsChanged(IReadOnlyList<SystemItemViewModel> value) => OnPropertyChanged(nameof(HasEmptySystems));

    public override IReadOnlyList<HintItem> Hints =>
    [
        new(NavAction.Accept, Loc.T("hint.open")),
        new(NavAction.Back, Loc.T("hint.back")),
        new(NavAction.Search, Loc.T("hint.search")),
        new(NavAction.Menu, Loc.T("hint.menu")),
    ];

    public override void OnNavigatedTo(object? argument, bool isBack)
    {
        Title = Loc.T("nav.systems");
        _ = LoadAsync();
    }

    public async Task LoadAsync()
    {
        var version = ++_version;
        var repo = Ctx.Services.Games;
        var stats = await UiContext.Background(repo.Stats, "Loading platform statistics");
        if (stats == null || version != _version) return;
        // unchanged card lists are kept (only their counts update), so a refresh does not drop the card focus
        var ordered = Ctx.Services.Catalog.Systems.OrderBy(s => s.SortOrder).ToList();
        Systems = SystemItemViewModel.Reconcile(Systems, ordered.Where(s => stats.GamesPerSystem.GetValueOrDefault(s.Id) > 0).ToList(), stats);
        EmptySystems = SystemItemViewModel.Reconcile(EmptySystems, ordered.Where(s => stats.GamesPerSystem.GetValueOrDefault(s.Id) == 0).ToList(), stats);
        SummaryText = Loc.T("systems.summary", Systems.Count, stats.GameCount);
    }

    [RelayCommand]
    private void Open(SystemItemViewModel? system)
    {
        if (system != null) Ctx.Navigator.Navigate(PageKey.System, system.Id);
    }
}

/// <summary>One platform: header with its accent ambience, facts, preferred emulator, and its games.</summary>
public sealed partial class SystemViewModel : PageViewModel
{
    private int _version;

    public SystemViewModel(UiContext ctx) : base(ctx)
    {
        ctx.LibraryChanged += () => { if (System != null) _ = LoadAsync(System.Id); };
    }

    public override PageKey Key => PageKey.System;

    [ObservableProperty] public partial SystemItemViewModel? System { get; private set; }
    [ObservableProperty] public partial IReadOnlyList<GameItemViewModel> Games { get; private set; } = [];
    [ObservableProperty] public partial string EmulatorText { get; private set; } = "";
    [ObservableProperty] public partial bool EmulatorReady { get; private set; }
    [ObservableProperty] public partial string FactsText { get; private set; } = "";
    /// <summary>"Heimkonsole von Nintendo, 1990. 18 Spiele." (numbers become Archivo runs in the view).</summary>
    [ObservableProperty] public partial string SentenceText { get; private set; } = "";
    [ObservableProperty] public partial string FoldersText { get; private set; } = "";
    [ObservableProperty] public partial string ExtensionsText { get; private set; } = "";
    [ObservableProperty] public partial bool IsEmpty { get; private set; }

    public override IReadOnlyList<HintItem> Hints =>
    [
        new(NavAction.Accept, Loc.T("hint.open")),
        new(NavAction.Back, Loc.T("hint.back")),
        new(NavAction.Favorite, Loc.T("hint.favorite")),
        new(NavAction.Search, Loc.T("hint.search")),
    ];

    public override void OnNavigatedTo(object? argument, bool isBack)
    {
        var id = argument switch
        {
            string s => s,
            SystemItemViewModel vm => vm.Id,
            SystemDef d => d.Id,
            _ => System?.Id,
        };
        if (id != null) _ = LoadAsync(id);
    }

    public async Task LoadAsync(string systemId)
    {
        var version = ++_version;
        var services = Ctx.Services;
        var def = services.Catalog.GetSystem(systemId);
        if (def == null) return;
        var data = await UiContext.Background(() =>
        {
            var games = services.Games.Query(new GameQuery { SystemId = systemId, Sort = GameSort.Title });
            var stats = services.Games.Stats();
            var choices = services.Launcher.ChoicesFor(systemId);
            return (games, stats, choices);
        }, "Loading the platform");
        if (version != _version || data.games == null) return;

        if (System?.Id != systemId)
        {
            System = new SystemItemViewModel(def, data.stats.GamesPerSystem.GetValueOrDefault(systemId), data.stats.PlaySecondsPerSystem.GetValueOrDefault(systemId));
            Backdrop = new BackdropSpec(BackdropKind.Wall, SystemId: systemId, Accent: System.Accent);
        }
        else System.Update(data.stats.GamesPerSystem.GetValueOrDefault(systemId), data.stats.PlaySecondsPerSystem.GetValueOrDefault(systemId));

        Title = def.Name;
        Games = Ctx.Items.GetAll(data.games);
        IsEmpty = Games.Count == 0;
        var installed = data.choices.FirstOrDefault(c => services.IsInstalled(c.Install));
        EmulatorReady = installed.Install != null;
        EmulatorText = installed.Install != null
            ? Loc.T("system.emulatorReady", EmulatorLabel(installed.Ref, installed.Install.Def.Name))
            : data.choices.Count == 0 ? Loc.T("system.noEmulatorDefined") : Loc.T("system.emulatorMissing", string.Join(", ", data.choices.Select(c => c.Install?.Def.Name ?? c.Ref.EmulatorId).Distinct()));
        // "Handheld von Nintendo, 2001" – a sentence part, no "A · B · C" chain (DESIGN 3.2)
        var category = Fmt.Category(def.Category);
        var facts = !string.IsNullOrEmpty(def.Manufacturer) ? Loc.T("detail.meta.by", category, def.Manufacturer) : category;
        FactsText = def.Year is { } year ? $"{facts}, {year}" : facts;
        SentenceText = $"{FactsText}. {System.CountText}.";
        FoldersText = string.Join(", ", def.Folders.Select(f => "games/" + f));
        ExtensionsText = string.Join(" ", def.Extensions);
    }

    public static string EmulatorLabel(EmulatorRef r, string name) => r.Core == null ? name : $"{name} ({r.Core})";

    [RelayCommand] private void Back() => Ctx.Navigator.GoBack();
}
