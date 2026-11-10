using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetroHub.App.Input;
using RetroHub.App.Localization;
using RetroHub.Core.Catalog;
using RetroHub.Core.Library;

namespace RetroHub.App.ViewModels;

/// <summary>
/// Konsolen "Die Fächerwand" (DESIGN 7.4): the systems with games as spines on one stage board (the focused one turns its
/// front forward), the systems without games as thin spines on a low edge ("Noch leer"). The page ground is the wall of
/// the focused system, in shadow.
/// </summary>
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
    /// <summary>The system in focus (spine on the wall or on the "Noch leer" edge); null before the first focus.</summary>
    [ObservableProperty] public partial SystemItemViewModel? Current { get; private set; }
    /// <summary>"Fach 3 von 13" (numbers as Archivo runs in the view); empty while a system without games is focused.</summary>
    [ObservableProperty] public partial string PositionText { get; private set; } = "";
    [ObservableProperty] public partial bool IsLoaded { get; private set; }
    public bool HasEmptySystems => EmptySystems.Count > 0;
    public bool HasSystems => Systems.Count > 0;
    /// <summary>No game in the library at all: the wall shows outlined places instead of spines.</summary>
    public bool IsLibraryEmpty => IsLoaded && Systems.Count == 0;
    partial void OnEmptySystemsChanged(IReadOnlyList<SystemItemViewModel> value) => OnPropertyChanged(nameof(HasEmptySystems));
    partial void OnSystemsChanged(IReadOnlyList<SystemItemViewModel> value)
    {
        OnPropertyChanged(nameof(HasSystems));
        OnPropertyChanged(nameof(IsLibraryEmpty));
    }
    partial void OnIsLoadedChanged(bool value) => OnPropertyChanged(nameof(IsLibraryEmpty));

    /// <summary>
    /// The focus reached <paramref name="system"/>: its wall becomes the page ground (in shadow, so its lit front stands
    /// out), its code is underlined in the ticker, the footer names it.
    /// </summary>
    public void SetCurrent(SystemItemViewModel? system)
    {
        if (Current == system) return;
        if (Current != null) Current.IsCurrent = false;
        Current = system;
        if (system == null)
        {
            Backdrop = BackdropSpec.Room;
            PositionText = "";
            Location = Systems.Count == 0 ? Loc.T("systems.location.empty") : null;
            return;
        }
        system.IsCurrent = true;
        Backdrop = new BackdropSpec(BackdropKind.Wall, SystemId: system.Id, Accent: system.Accent, WallShade: WallShade);
        var index = IndexOf(Systems, system);
        PositionText = index >= 0 ? Loc.T("systems.position", index + 1, Systems.Count) : "";
        Location = index >= 0 ? system.Name : Loc.T("systems.location.unused", system.Name);
    }

    /// <summary>Boxes on the inner shelf of an open front (≈1100: the front shows two of them).</summary>
    public const int FeaturedCount = 3;

    /// <summary>Share of room-deep in the page ground (the room wall in shadow, DESIGN 7.4).</summary>
    public const double WallShade = 0.45;

    private static int IndexOf(IReadOnlyList<SystemItemViewModel> list, SystemItemViewModel s)
    {
        for (var i = 0; i < list.Count; i++) if (ReferenceEquals(list[i], s)) return i;
        return -1;
    }

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
        var ordered = Ctx.Services.Catalog.Systems.OrderBy(s => s.SortOrder).ToList();
        // the statistics and, per system with games, the three boxes for the front's inner shelf (last played first)
        var data = await UiContext.Background(() =>
        {
            var s = repo.Stats();
            var featured = new Dictionary<string, IReadOnlyList<Game>>(StringComparer.OrdinalIgnoreCase);
            foreach (var def in ordered)
                if (s.GamesPerSystem.GetValueOrDefault(def.Id) > 0)
                    featured[def.Id] = repo.Query(new GameQuery { SystemId = def.Id, Sort = GameSort.LastPlayed, Limit = FeaturedCount });
            return (Stats: s, Featured: featured);
        }, "Loading platform statistics");
        if (data.Stats == null || version != _version) return;
        var stats = data.Stats;
        // unchanged card lists are kept (only their counts update), so a refresh does not drop the card focus
        Systems = SystemItemViewModel.Reconcile(Systems, ordered.Where(s => stats.GamesPerSystem.GetValueOrDefault(s.Id) > 0).ToList(), stats);
        foreach (var system in Systems)
            system.Featured = data.Featured.TryGetValue(system.Id, out var games) ? Ctx.Items.GetAll(games) : [];
        EmptySystems = SystemItemViewModel.Reconcile(EmptySystems, ordered.Where(s => stats.GamesPerSystem.GetValueOrDefault(s.Id) == 0).ToList(), stats);
        SummaryText = Loc.T("systems.summary", Systems.Count, stats.GameCount);
        IsLoaded = true;
        // the focused system keeps its place after a refresh; a system that left the wall hands the focus to the view
        if (Current != null && IndexOf(Systems, Current) < 0 && IndexOf(EmptySystems, Current) < 0) SetCurrent(null);
        else if (Current != null) { var c = Current; Current = null; SetCurrent(c); }
        else Location = Systems.Count == 0 ? Loc.T("systems.location.empty") : Loc.T("systems.location", Systems.Count, stats.GameCount);
    }

    [RelayCommand] private void OpenImports() => Ctx.Navigator.Navigate(PageKey.Imports);

    [RelayCommand]
    private void Open(SystemItemViewModel? system)
    {
        if (system != null) Ctx.Navigator.Navigate(PageKey.System, system.Id);
    }
}

/// <summary>
/// Systemseite "Das Fach" (DESIGN 7.5): the system's wall as a banner standing on a stage board (name, ONE sentence,
/// emulator line, Systeminfo; the neighbours as text buttons with shoulder glyphs, the motif right), below it the grid
/// in the system's light on its wall. The shoulders switch to the neighbouring system in place (the back stack keeps
/// one entry); Systeminfo is an overlay (folders, file types, BIOS).
/// </summary>
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
    /// <summary>"Handheld von Nintendo, 2001. 7 Spiele, 46 Stunden gespielt, zuletzt heute." (numbers as Archivo runs in the view).</summary>
    [ObservableProperty] public partial string SentenceText { get; private set; } = "";
    [ObservableProperty] public partial string FoldersText { get; private set; } = "";
    [ObservableProperty] public partial string ExtensionsText { get; private set; } = "";
    [ObservableProperty] public partial bool IsEmpty { get; private set; }
    /// <summary>One to six games: the compartment ends in the "Fachende" drawing with the way to more (DESIGN 7.5).</summary>
    [ObservableProperty] public partial bool IsFewGames { get; private set; }
    [ObservableProperty] public partial bool IsLoaded { get; private set; }
    /// <summary>The neighbouring systems with games (catalog order, wrapping); null when there is none.</summary>
    [ObservableProperty] public partial SystemItemViewModel? Previous { get; private set; }
    [ObservableProperty] public partial SystemItemViewModel? Next { get; private set; }
    public bool HasNeighbours => Previous != null && Next != null;
    partial void OnPreviousChanged(SystemItemViewModel? value) => OnPropertyChanged(nameof(HasNeighbours));
    partial void OnNextChanged(SystemItemViewModel? value) => OnPropertyChanged(nameof(HasNeighbours));
    /// <summary>The Systeminfo overlay is open (folders, file types, BIOS).</summary>
    [ObservableProperty] public partial bool IsInfoOpen { get; private set; }
    [ObservableProperty] public partial IReadOnlyList<InfoLine> InfoLines { get; private set; } = [];
    /// <summary>"Lege Spieldateien in games/gba/ oder …" (the empty compartment).</summary>
    [ObservableProperty] public partial string EmptyText { get; private set; } = "";
    /// <summary>
    /// Slot format of the grid (height / width): the median of the stored cover sizes of this system (≥ 8, else its
    /// start value, DESIGN 11.4), set before the games so the grid is built once in its final format.
    /// </summary>
    [ObservableProperty] public partial double SlotAspect { get; private set; } = 1.25;

    /// <summary>Raised when the shoulders switched the system in place: −1 = to the previous, +1 = to the next.</summary>
    public event Action<int>? SystemSwitched;

    partial void OnIsInfoOpenChanged(bool value)
    {
        HeaderDim = value ? GameDetailViewModel.SheetDim : default;
        RaiseHintsChanged();
    }

    public override IReadOnlyList<HintItem> Hints => IsInfoOpen
        ? [new(NavAction.Back, Loc.T("hint.close"))]
        :
        [
            new(NavAction.Accept, Loc.T("hint.open")),
            new(NavAction.PageLeft, Loc.T("hint.system")),
            new(NavAction.Favorite, Loc.T("hint.favorite")),
            new(NavAction.Back, Loc.T("hint.back")),
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
        IsInfoOpen = false;
        if (id != null) _ = LoadAsync(id);
    }

    public override void OnNavigatedFrom()
    {
        base.OnNavigatedFrom();
        IsInfoOpen = false;
    }

    public override bool HandleAction(NavAction action)
    {
        if (IsInfoOpen)
        {
            if (action == NavAction.Back) IsInfoOpen = false;
            return action is NavAction.Back or NavAction.PageLeft or NavAction.PageRight or NavAction.Favorite or NavAction.View;
        }
        if (action is NavAction.PageLeft or NavAction.PageRight)
        {
            Switch(action == NavAction.PageLeft ? -1 : +1);
            return true; // also without neighbours: the shoulders never leave the compartment for another channel
        }
        return false;
    }

    /// <summary>The neighbouring system in place (wall, motif and grid change; Back still returns to where we came from).</summary>
    public void Switch(int direction)
    {
        var target = direction < 0 ? Previous : Next;
        if (target == null || target.Id == System?.Id) return;
        Ctx.Navigator.ReplaceArgument(target.Id);
        _ = LoadAsync(target.Id, direction);
    }

    [RelayCommand] private void PreviousSystem() => Switch(-1);
    [RelayCommand] private void NextSystem() => Switch(+1);
    [RelayCommand] private void ShowInfo() => IsInfoOpen = true;
    [RelayCommand] private void CloseInfo() => IsInfoOpen = false;
    [RelayCommand] private void OpenEmulators() => Ctx.Navigator.Navigate(PageKey.Emulators);
    [RelayCommand] private void OpenImports() => Ctx.Navigator.Navigate(PageKey.Imports);

    public async Task LoadAsync(string systemId, int direction = 0)
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

        var games = data.stats.GamesPerSystem.GetValueOrDefault(systemId);
        var seconds = data.stats.PlaySecondsPerSystem.GetValueOrDefault(systemId);
        var changed = System?.Id != systemId;
        // the slot format first, so the grid is built once in its final format (DESIGN 11.4)
        SlotAspect = LibraryViewModel.SlotAspectFor(systemId, data.games);
        if (changed)
        {
            System = new SystemItemViewModel(def, games, seconds);
            Backdrop = new BackdropSpec(BackdropKind.Wall, SystemId: systemId, Accent: System.Accent);
        }
        else System!.Update(games, seconds);

        // the neighbours: systems with games in catalog order, wrapping around
        var ordered = services.Catalog.Systems.OrderBy(s => s.SortOrder).Where(s => data.stats.GamesPerSystem.GetValueOrDefault(s.Id) > 0).ToList();
        var at = ordered.FindIndex(s => s.Id == systemId);
        if (at >= 0 && ordered.Count > 1)
        {
            SystemItemViewModel Item(SystemDef d) => new(d, data.stats.GamesPerSystem.GetValueOrDefault(d.Id), data.stats.PlaySecondsPerSystem.GetValueOrDefault(d.Id));
            var prev = ordered[(at - 1 + ordered.Count) % ordered.Count];
            var next = ordered[(at + 1) % ordered.Count];
            Previous = Previous?.Id == prev.Id ? Previous : Item(prev);
            Next = Next?.Id == next.Id ? Next : Item(next);
        }
        else
        {
            Previous = null;
            Next = null;
        }

        Title = def.Name;
        Location = def.Name;
        Games = Ctx.Items.GetAll(data.games);
        IsEmpty = Games.Count == 0;
        IsFewGames = Games.Count is > 0 and <= 6;
        var installed = data.choices.FirstOrDefault(c => services.IsInstalled(c.Install));
        EmulatorReady = installed.Install != null;
        EmulatorText = installed.Install != null
            ? Loc.T("system.emulatorReady", EmulatorLabel(installed.Ref, installed.Install.Def.Name))
            : Loc.T("system.noEmulator");
        // "Handheld von Nintendo, 2001" – a sentence part, no "A · B · C" chain (DESIGN 3.2)
        FactsText = FirstPart(def);
        SentenceText = BuildSentence(def, games, seconds, data.games.Count == 0 ? null : data.games.Max(g => g.LastPlayedUtc));
        FoldersText = string.Join(", ", def.Folders.Select(f => "games/" + f));
        ExtensionsText = string.Join(" ", def.Extensions);
        EmptyText = Loc.T("system.emptyText", def.Folders.Length > 0 ? "games/" + def.Folders[0] + "/" : "games/");
        InfoLines = BuildInfo(def, data.choices.Select(c => c.Install?.Def.Name ?? c.Ref.EmulatorId).Distinct().ToList());
        IsLoaded = true;
        if (changed && direction != 0) SystemSwitched?.Invoke(direction);
    }

    /// <summary>
    /// "Handheld von Nintendo, 2001. 7 Spiele, 46 Stunden gespielt, zuletzt heute." – the banner sentence of the
    /// library plus when the system was last played; parts without data fall away.
    /// </summary>
    internal static string BuildSentence(SystemDef def, int games, long playSeconds, DateTime? lastPlayedUtc, DateTime? nowUtc = null)
    {
        if (games == 0) return $"{FirstPart(def)}. {Loc.T("system.sentence.empty")}";
        var sentence = LibraryViewModel.BuildBannerSentence(def, games, playSeconds);
        // "noch nicht gespielt" (under a minute in all) never gets a "zuletzt" behind it
        if (lastPlayedUtc is not { } last || playSeconds < 60) return sentence;
        var now = nowUtc ?? DateTime.UtcNow;
        // whole words inside a sentence ("vor einer Woche", "vor 5 Minuten"), never the abbreviations of the labels
        // ("vor 1 Wo."): an abbreviation's period and the sentence's period collided (M5 QA: "zuletzt vor 1 Wo")
        var when = (now.ToLocalTime().Date - last.ToLocalTime().Date).Days < 30
            ? Fmt.LastPlayedInSentence(last, now)
            : Loc.T("system.sentence.on", Fmt.DateInSentence(last, now));
        var tail = Loc.T("system.sentence.last", when);
        // a part that already ends with a period gives up exactly ONE (the sentence's own)
        if (when.EndsWith('.') && tail.EndsWith("..", StringComparison.Ordinal)) tail = tail[..^1];
        return sentence.TrimEnd('.') + tail;
    }

    private static string FirstPart(SystemDef def)
    {
        var first = Fmt.Category(def.Category);
        if (!string.IsNullOrEmpty(def.Manufacturer)) first = Loc.T("detail.meta.by", first, def.Manufacturer);
        return def.Year is { } y ? $"{first}, {y}" : first;
    }

    /// <summary>The Systeminfo lines: folders, file types, emulators, BIOS files (each "nötig" or "optional").</summary>
    private static IReadOnlyList<InfoLine> BuildInfo(SystemDef def, IReadOnlyList<string> emulators)
    {
        var lines = new List<InfoLine>
        {
            new(Loc.T("system.info.folders"), string.Join(", ", def.Folders.Select(f => "games/" + f + "/")), Mono: true),
            new(Loc.T("system.info.extensions"), string.Join("  ", def.Extensions), Mono: true),
        };
        if (emulators.Count > 0) lines.Add(new(Loc.T("system.info.emulators"), string.Join(", ", emulators)));
        foreach (var bios in def.Bios)
            lines.Add(new(Loc.T(bios.Required ? "system.info.biosRequired" : "system.info.biosOptional"), bios.File, Mono: true));
        return lines;
    }

    public static string EmulatorLabel(EmulatorRef r, string name) => r.Core == null ? name : $"{name} ({r.Core})";

    [RelayCommand] private void Back() => Ctx.Navigator.GoBack();
}
