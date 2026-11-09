using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetroHub.App.Input;
using RetroHub.App.Localization;
using RetroHub.Core.Catalog;
using RetroHub.Core.Emulation;
using RetroHub.Core.Library;
using RetroHub.Core.Logging;

namespace RetroHub.App.ViewModels;

/// <summary>One emulator choice of the detail page ("mGBA", "RetroArch (mgba)").</summary>
public sealed record EmulatorOption(string? Ref, string Label, string Status, bool IsInstalled, bool IsDefault)
{
    public override string ToString() => Label;
}

public sealed record ModeOption(LaunchMode? Mode, string Label, string Hint)
{
    public override string ToString() => Label;
}

/// <summary>A launch problem as shown on the detail page.</summary>
public sealed record ProblemItem(string Text, bool IsBlocking);

/// <summary>A label/value pair in the metadata list.</summary>
public sealed record FactItem(string Label, string Value)
{
    /// <summary>Label in upper case (overline style).</summary>
    public string Caption => Label.ToUpper(Loc.Culture);
}

/// <summary>Game detail: cover, metadata, playtime, emulator + mode choice, problems from the planner, play.</summary>
public sealed partial class GameDetailViewModel : PageViewModel
{
    private int _version;
    private bool _loading;

    public GameDetailViewModel(UiContext ctx) : base(ctx)
    {
        ctx.LibraryChanged += () => { if (Game != null && ctx.Navigator.Current == PageKey.GameDetail) _ = LoadAsync(Game.Id); };
        ctx.EmulatorsChanged += () => { if (Game != null) _ = LoadAsync(Game.Id); };
    }

    public override PageKey Key => PageKey.GameDetail;

    [ObservableProperty] public partial GameItemViewModel? Game { get; private set; }
    [ObservableProperty] public partial SystemItemViewModel? System { get; private set; }
    [ObservableProperty] public partial string Description { get; private set; } = "";
    [ObservableProperty] public partial bool HasDescription { get; private set; }
    [ObservableProperty] public partial IReadOnlyList<FactItem> Facts { get; private set; } = [];
    [ObservableProperty] public partial string PlayTime { get; private set; } = "";
    [ObservableProperty] public partial string LastPlayed { get; private set; } = "";
    [ObservableProperty] public partial string PlayCount { get; private set; } = "";
    [ObservableProperty] public partial string Added { get; private set; } = "";
    [ObservableProperty] public partial string FilePath { get; private set; } = "";
    [ObservableProperty] public partial string FileInfo { get; private set; } = "";

    [ObservableProperty] public partial IReadOnlyList<EmulatorOption> Emulators { get; private set; } = [];
    [ObservableProperty] public partial EmulatorOption? SelectedEmulator { get; set; }
    [ObservableProperty] public partial IReadOnlyList<ModeOption> Modes { get; private set; } = [];
    [ObservableProperty] public partial ModeOption? SelectedMode { get; set; }
    [ObservableProperty] public partial IReadOnlyList<ProblemItem> Problems { get; private set; } = [];
    [ObservableProperty] public partial bool CanPlay { get; private set; }
    [ObservableProperty] public partial string PlanSummary { get; private set; } = "";
    [ObservableProperty] public partial bool HasProblems { get; private set; }

    public override IReadOnlyList<HintItem> Hints =>
    [
        new(NavAction.Accept, Loc.T("hint.select")),
        new(NavAction.Back, Loc.T("hint.back")),
        new(NavAction.Favorite, Game?.IsFavorite == true ? Loc.T("hint.unfavorite") : Loc.T("hint.favorite")),
        new(NavAction.Menu, Loc.T("hint.menu")),
    ];

    public override void OnNavigatedTo(object? argument, bool isBack)
    {
        long? id = argument switch
        {
            GameItemViewModel g => g.Id,
            Game g => g.Id,
            long l => l,
            _ => Game?.Id,
        };
        if (argument is GameItemViewModel item) Game = item; // show immediately, details follow
        if (id != null) _ = LoadAsync(id.Value);
    }

    public override bool HandleAction(NavAction action)
    {
        if (action == NavAction.Favorite && Game != null)
        {
            _ = ToggleFavoriteAsync();
            return true;
        }
        return false;
    }

    public async Task LoadAsync(long gameId)
    {
        var version = ++_version;
        var s = Ctx.Services;
        var data = await UiContext.Background(() =>
        {
            var game = s.Games.Get(gameId);
            if (game == null) return default;
            var choices = s.Launcher.ChoicesFor(game.SystemId);
            var plan = s.Launcher.Plan(game);
            var stats = s.Games.Stats();
            return (Game: game, Choices: choices, Plan: plan, Stats: stats);
        }, "Loading the game");
        if (version != _version) return;
        if (data.Game == null)
        {
            Ctx.Toasts.Show(Loc.T("detail.notFound"), ToastKind.Warning);
            return;
        }
        _loading = true;
        try
        {
            var g = data.Game;
            Game = Ctx.Items.Get(g);
            var def = s.Catalog.GetSystem(g.SystemId);
            System = def == null ? null : new SystemItemViewModel(def, data.Stats.GamesPerSystem.GetValueOrDefault(def.Id), data.Stats.PlaySecondsPerSystem.GetValueOrDefault(def.Id));
            Title = g.DisplayTitle;
            Ambient = System?.AmbientBrush;
            Description = string.IsNullOrWhiteSpace(g.Description) ? Loc.T("detail.noDescription") : g.Description!;
            HasDescription = !string.IsNullOrWhiteSpace(g.Description);
            var facts = new List<FactItem>();
            if (g.Year != null) facts.Add(new FactItem(Loc.T("detail.year"), g.Year.Value.ToString()));
            if (!string.IsNullOrEmpty(g.Developer)) facts.Add(new FactItem(Loc.T("detail.developer"), g.Developer!));
            if (!string.IsNullOrEmpty(g.Publisher) && g.Publisher != g.Developer) facts.Add(new FactItem(Loc.T("detail.publisher"), g.Publisher!));
            if (!string.IsNullOrEmpty(g.Genre)) facts.Add(new FactItem(Loc.T("detail.genre"), g.Genre!));
            if (g.Players != null) facts.Add(new FactItem(Loc.T("detail.players"), g.Players == 1 ? Loc.T("detail.onePlayer") : Loc.T("detail.manyPlayers", g.Players)));
            if (!string.IsNullOrEmpty(g.Region)) facts.Add(new FactItem(Loc.T("detail.region"), g.Region!));
            Facts = facts;
            PlayTime = Fmt.PlayTime(g.PlaySeconds);
            LastPlayed = Fmt.LastPlayed(g.LastPlayedUtc);
            PlayCount = g.PlayCount == 0 ? Loc.T("detail.neverStarted") : g.PlayCount == 1 ? Loc.T("detail.startedOnce") : Loc.T("detail.startedTimes", g.PlayCount);
            Added = Fmt.Date(g.AddedUtc);
            FilePath = g.Path;
            FileInfo = string.Join(" · ", new[] { Fmt.Size(g.Size), g.Crc32 != null ? "CRC32 " + g.Crc32 : null, g.Serial }.Where(x => !string.IsNullOrEmpty(x)));

            // emulator choices: "Automatisch" (system default order) + every configured choice with its install state
            var options = new List<EmulatorOption>
            {
                new(null, Loc.T("detail.emulatorAuto"), data.Plan.Emulator != null ? data.Plan.Emulator.Def.Name : Loc.T("detail.none"), true, true),
            };
            foreach (var (r, install) in data.Choices)
            {
                var installed = s.IsInstalled(install) && (r.Core == null || install!.InstalledCores.Contains(r.Core));
                var name = install?.Def.Name ?? s.Catalog.GetEmulator(r.EmulatorId)?.Name ?? r.EmulatorId;
                options.Add(new EmulatorOption(r.ToString(), SystemViewModel.EmulatorLabel(r, name),
                    installed ? (install?.Version is { } v ? Loc.T("detail.installedVersion", v) : Loc.T("detail.installed")) : Loc.T("detail.notInstalled"),
                    installed, false));
            }
            Emulators = options;
            SelectedEmulator = options.FirstOrDefault(o => o.Ref != null && string.Equals(o.Ref, g.EmulatorOverride, StringComparison.OrdinalIgnoreCase)) ?? options[0];
            BuildModes(data.Plan, g.LaunchModeOverride);
            ApplyPlan(data.Plan);
        }
        finally
        {
            _loading = false;
        }
        RaiseHintsChanged();
    }

    private void BuildModes(LaunchPlan plan, LaunchMode? current)
    {
        var def = plan.Emulator?.Def;
        var modes = def?.Modes ?? [LaunchMode.Fullscreen, LaunchMode.Windowed];
        var list = new List<ModeOption> { new(null, Loc.T("detail.modeAuto", Fmt.Mode(def?.DefaultMode ?? plan.Mode)), Loc.T("detail.modeAuto.hint")) };
        list.AddRange(modes.Select(m => new ModeOption(m, Fmt.Mode(m), Fmt.ModeHint(m))));
        Modes = list;
        SelectedMode = list.FirstOrDefault(m => m.Mode == current) ?? list[0];
    }

    private void ApplyPlan(LaunchPlan plan)
    {
        Problems = plan.Problems.OrderByDescending(p => p.Blocking).Select(p => new ProblemItem(p.Message, p.Blocking)).ToList();
        HasProblems = Problems.Count > 0;
        CanPlay = plan.CanLaunch;
        PlanSummary = plan.Emulator != null
            ? Loc.T("detail.planSummary", plan.Emulator.Def.Name + (plan.Core != null ? $" ({plan.Core})" : ""), Fmt.Mode(plan.Mode))
            : Loc.T("detail.planNoEmulator");
        PlayCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedEmulatorChanged(EmulatorOption? value)
    {
        if (_loading || Game == null) return;
        _ = SaveOverridesAndReplanAsync(rebuildModes: true);
    }

    partial void OnSelectedModeChanged(ModeOption? value)
    {
        if (_loading || Game == null) return;
        _ = SaveOverridesAndReplanAsync(rebuildModes: false);
    }

    /// <summary>Stores the per-game emulator/mode override and re-plans (problems update immediately).</summary>
    private async Task SaveOverridesAndReplanAsync(bool rebuildModes)
    {
        var item = Game;
        if (item == null) return;
        var emu = SelectedEmulator?.Ref;
        var mode = rebuildModes ? null : SelectedMode?.Mode;
        var s = Ctx.Services;
        var plan = await UiContext.Background(() =>
        {
            var game = s.Games.Get(item.Id);
            if (game == null) return null;
            game.EmulatorOverride = emu;
            game.LaunchModeOverride = mode;
            s.Games.Upsert(game);
            return s.Launcher.Plan(game);
        }, "Saving the emulator choice");
        if (plan == null || Game != item) return;
        _loading = true;
        try
        {
            if (rebuildModes) BuildModes(plan, null);
            ApplyPlan(plan);
        }
        finally { _loading = false; }
    }

    private bool CanPlayGame() => CanPlay && Game != null;

    [RelayCommand(CanExecute = nameof(CanPlayGame))]
    private Task Play() => Game == null ? Task.CompletedTask : Ctx.Launch.LaunchAsync(Game);

    [RelayCommand]
    private async Task ToggleFavoriteAsync()
    {
        if (Game == null) return;
        await Ctx.ToggleFavoriteAsync(Game);
        RaiseHintsChanged();
    }

    [RelayCommand]
    private void OpenSystem()
    {
        if (System != null) Ctx.Navigator.Navigate(PageKey.System, System.Id);
    }

    [RelayCommand] private void Back() => Ctx.Navigator.GoBack();
}
