using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetroHub.App.Input;
using RetroHub.App.Localization;
using RetroHub.App.Services;
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

/// <summary>
/// A launch problem as shown on the detail page: one sentence (the planner's first sentence); a download link in the
/// message moves behind a "Website" action instead of standing in the text.
/// </summary>
public sealed record ProblemItem(string Text, bool IsBlocking, string? Url = null)
{
    public bool HasUrl => !string.IsNullOrEmpty(Url);

    private static readonly global::System.Text.RegularExpressions.Regex UrlPattern = new(@"https?://\S+", global::System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>"RetroArch ist nicht installiert. Entpacke … Download: https://…" → ("RetroArch ist nicht installiert.", url).</summary>
    public static ProblemItem From(string message, bool blocking)
    {
        var url = UrlPattern.Match(message) is { Success: true } m ? m.Value.TrimEnd('.', ')', ',') : null;
        var text = message.Replace("\r", "").Split('\n')[0].Trim();
        var end = text.IndexOf(". ", StringComparison.Ordinal);
        if (end > 0) text = text[..(end + 1)];
        if (url != null) text = text.Replace(url, "").Replace("Download:", "").Trim();
        return new ProblemItem(text, blocking, url);
    }
}

/// <summary>A label/value pair in the metadata list.</summary>
public sealed record FactItem(string Label, string Value)
{
    /// <summary>Label in upper case (overline style).</summary>
    public string Caption => Label; // no uppercase overlines (DESIGN 3.2)
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
    [ObservableProperty] public partial string FileName { get; private set; } = "";

    [ObservableProperty] public partial IReadOnlyList<EmulatorOption> Emulators { get; private set; } = [];
    [ObservableProperty] public partial EmulatorOption? SelectedEmulator { get; set; }
    [ObservableProperty] public partial IReadOnlyList<ModeOption> Modes { get; private set; } = [];
    [ObservableProperty] public partial ModeOption? SelectedMode { get; set; }
    [ObservableProperty] public partial IReadOnlyList<ProblemItem> Problems { get; private set; } = [];
    [ObservableProperty] public partial bool CanPlay { get; private set; }
    [ObservableProperty] public partial string PlanSummary { get; private set; } = "";
    [ObservableProperty] public partial bool HasProblems { get; private set; }
    /// <summary>The game has a cover reference ("Cover entfernen" is offered).</summary>
    [ObservableProperty] public partial bool HasCover { get; private set; }
    /// <summary>Covers can be chosen (real services; the demo has no artwork service).</summary>
    public bool CanEditCover => Ctx.Services.Artwork != null;

    /// <summary>Set by the view: shows the image picker and returns the chosen file (null = cancelled).</summary>
    public Func<Task<string?>>? PickImage { get; set; }

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

    /// <summary>"Rollenspiel von Game Freak, 2004. 1 Spieler. Europa." – meta as a sentence, missing parts drop out (DESIGN 3.2).</summary>
    [ObservableProperty] public partial string MetaSentence { get; private set; } = "";
    /// <summary>"46 Std. in 41 Sitzungen gespielt, zuletzt heute." / "Noch nie gespielt. Seit … in der Sammlung."</summary>
    [ObservableProperty] public partial string PlaySentence { get; private set; } = "";

    /// <summary>
    /// "Rollenspiel von Game Freak, 2004. Ein Spieler. Europa." (DESIGN 3.2): missing parts drop out with their
    /// punctuation; region tags become words ("EUR" → "Europa"); a language ("Englisch") is only mentioned beside genre,
    /// developer or year – never as the only fact. Nothing known: the system name ("Game Boy Advance."), or nothing when
    /// <paramref name="systemName"/> is null (the detail page names the system in its label already).
    /// </summary>
    internal static string BuildMetaSentence(Core.Library.Game g, string? systemName)
    {
        var first = !string.IsNullOrEmpty(g.Genre) && !string.IsNullOrEmpty(g.Developer) ? Loc.T("detail.meta.by", g.Genre!, g.Developer!)
            : !string.IsNullOrEmpty(g.Developer) ? Loc.T("detail.meta.byOnly", g.Developer!)
            : g.Genre ?? "";
        if (g.Year is { } y) first = first.Length > 0 ? $"{first}, {y}" : y.ToString();
        var parts = new List<string>();
        if (first.Length > 0) parts.Add(Fmt.SentenceStart(first) + ".");
        if (g.Players is { } p) parts.Add((p == 1 ? Loc.T("detail.onePlayerSentence") : Loc.T("detail.manyPlayers", p)) + ".");
        if (!string.IsNullOrWhiteSpace(g.Region))
        {
            if (Fmt.RegionWord(g.Region) is { } region) parts.Add(region + ".");
            else if (first.Length > 0) parts.Add(Fmt.SentenceStart(g.Region!.Trim().TrimEnd('.')) + ".");
        }
        return parts.Count > 0 ? string.Join(" ", parts) : systemName != null ? systemName + "." : "";
    }

    /// <summary>"46 Std. in 41 Sitzungen gespielt, zuletzt heute." – the play time starts the sentence ("Unter 1 Min. …").</summary>
    internal static string BuildPlaySentence(Core.Library.Game g) =>
        g.PlayCount == 0 || g.LastPlayedUtc == null
            ? Loc.T("detail.sentence.never", Fmt.DateInSentence(g.AddedUtc))
            : g.PlayCount == 1
                ? Loc.T("detail.sentence.once", Fmt.SentenceStart(Fmt.PlayTime(g.PlaySeconds)), Fmt.LastPlayed(g.LastPlayedUtc))
                : Loc.T("detail.sentence.played", Fmt.SentenceStart(Fmt.PlayTime(g.PlaySeconds)), g.PlayCount, Fmt.LastPlayed(g.LastPlayedUtc));

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
            Location = g.DisplayTitle;
            // TV light of the game above the stage board; the glow sits at the box (left)
            Backdrop = new BackdropSpec(BackdropKind.TvLight, Game.CoverPath, g.SystemId, Game.Accent, g.Id, StageShare: 0.63, GlowX: 0.16, GlowY: 0.38, GhostX: 0.82);
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
            MetaSentence = BuildMetaSentence(g, null); // the system label above the title already names the system
            PlaySentence = BuildPlaySentence(g);
            PlayTime = Fmt.PlayTime(g.PlaySeconds);
            LastPlayed = Fmt.LastPlayed(g.LastPlayedUtc);
            PlayCount = g.PlayCount == 0 ? Loc.T("detail.neverStarted") : g.PlayCount == 1 ? Loc.T("detail.startedOnce") : Loc.T("detail.startedTimes", g.PlayCount);
            Added = Fmt.Date(g.AddedUtc);
            FilePath = g.Path;
            HasCover = !string.IsNullOrEmpty(g.CoverPath);
            FileName = Path.GetFileName(g.Path);
            FileInfo = Fmt.Size(g.Size); // name and size only; path and CRC belong in the file info overlay

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

    /// <summary>The game's full path in a launcher message replaced by its file name.</summary>
    private string ShortenPath(string message)
    {
        if (string.IsNullOrEmpty(FileName)) return message;
        if (!string.IsNullOrEmpty(FilePath)) message = message.Replace(FilePath, FileName, StringComparison.OrdinalIgnoreCase);
        // a resolved spelling of the same file ("E:\…\name.sfc", "\\server\…\name.sfc")
        return global::System.Text.RegularExpressions.Regex.Replace(message,
            @"(?:[A-Za-z]:|\\\\)[^:\r\n]*?[\\/]" + global::System.Text.RegularExpressions.Regex.Escape(FileName), FileName.Replace("$", "$$"));
    }

    private void ApplyPlan(LaunchPlan plan)
    {
        // the status line names the file, never its full path (the path belongs in the file info)
        Problems = plan.Problems.OrderByDescending(p => p.Blocking).Select(p => ProblemItem.From(ShortenPath(p.Message), p.Blocking)).ToList();
        HasProblems = Problems.Count > 0;
        CanPlay = plan.CanLaunch;
        // a sentence, no "A · B" chain: "Startet mit RetroArch (snes9x) im Vollbild."
        PlanSummary = plan.Emulator != null
            ? Loc.T("detail.planSummary", plan.Emulator.Def.Name + (plan.Core != null ? $" ({plan.Core})" : ""), Fmt.ModePhrase(plan.Mode))
            : Loc.T("detail.planNoEmulator");
        ShowSolution = !CanPlay && Game != null;
        SolutionText = Game?.IsMissing == true ? Loc.T("detail.solution.file") : Loc.T("detail.solution.emulator");
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

    /// <summary>"Spielen" is not possible: the primary plate becomes the solution button (DESIGN 7.3).</summary>
    [ObservableProperty] public partial bool ShowSolution { get; private set; }
    [ObservableProperty] public partial string SolutionText { get; private set; } = "";

    /// <summary>The way out: the emulators of this system (or the imports when the file is missing).</summary>
    [RelayCommand]
    private void Solve()
    {
        if (Game == null) return;
        if (Game.IsMissing) Ctx.Navigator.Navigate(PageKey.Imports);
        else Ctx.Navigator.Navigate(PageKey.Emulators, Game.SystemId);
    }

    [RelayCommand]
    private void OpenWebsite(ProblemItem? problem)
    {
        if (problem?.Url != null && !ShellOpen.Url(problem.Url)) Ctx.Toasts.Show(Loc.T("common.openFailed"), ToastKind.Error);
    }

    /// <summary>"Mehr › Ordner öffnen": the folder of the game file.</summary>
    [RelayCommand]
    private void OpenGameFolder()
    {
        var folder = string.IsNullOrEmpty(FilePath) ? null : Path.GetDirectoryName(FilePath);
        if (folder != null && !ShellOpen.Folder(folder)) Ctx.Toasts.Show(Loc.T("common.folderMissing", folder), ToastKind.Warning);
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

    /// <summary>"Cover wählen…": file picker → copied to artwork/covers/&lt;system&gt;/&lt;rom name&gt;.&lt;ext&gt; → CoverPath.</summary>
    [RelayCommand]
    private async Task ChooseCoverAsync()
    {
        var artwork = Ctx.Services.Artwork;
        var item = Game;
        if (artwork == null || item == null || PickImage == null) return;
        string? file;
        try { file = await PickImage(); }
        catch (Exception ex) { Log.Warn("Image picker failed", ex); return; }
        if (file == null) return;
        var stored = await UiContext.Background(() => artwork.SetUserCover(item.Id, file), "Setting the cover");
        if (stored == null)
        {
            Ctx.Toasts.Show(Loc.T("artwork.chooseFailed"), ToastKind.Error);
            return;
        }
        AsyncImageLoader.Shared.ResetFailures();
        HasCover = true;
        Ctx.Toasts.Show(Loc.T("artwork.chosen"), ToastKind.Success, stored);
        await LoadAsync(item.Id);
    }

    /// <summary>"Cover entfernen": only clears the reference; the image file is never deleted.</summary>
    [RelayCommand]
    private async Task RemoveCoverAsync()
    {
        var artwork = Ctx.Services.Artwork;
        var item = Game;
        if (artwork == null || item == null) return;
        var ok = await UiContext.Background(() => { artwork.ClearCover(item.Id); return true; }, "Removing the cover");
        if (!ok) return;
        HasCover = false;
        Ctx.Toasts.Show(Loc.T("artwork.removed"), ToastKind.Info, Loc.T("artwork.removedDetail"));
        await LoadAsync(item.Id);
    }
}
