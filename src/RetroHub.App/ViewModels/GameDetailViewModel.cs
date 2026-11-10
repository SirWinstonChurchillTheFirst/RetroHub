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

/// <summary>One entry of the detail's choice list (emulator or start mode): label, a note ("nicht installiert"), the current one checked.</summary>
public sealed record PickOption(object Value, string Label, string? Note, bool IsCurrent)
{
    public bool HasNote => !string.IsNullOrEmpty(Note);
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

/// <summary>
/// One place of "Gespielt" (DESIGN 7.3, M4 QA): a rubber stamp of a session (date + duration), the entry stamp (the
/// day the game came into the collection) or an empty place waiting for the next stamp (a dashed baseline).
/// <paramref name="Index"/> tilts the stamps alternately (-1.5 / +1 degrees), like stamps pressed by hand.
/// </summary>
public sealed record CardPlace(string Date, string Note, CardPlaceKind Kind, int Index = 0)
{
    public bool IsStamp => Kind != CardPlaceKind.Empty;
    public bool IsEmpty => Kind == CardPlaceKind.Empty;
    public bool IsEntry => Kind == CardPlaceKind.Entry;
    public bool IsSession => Kind == CardPlaceKind.Session;
    /// <summary>The static tilt of the stamp in degrees (a RenderTransform, no per-frame cost).</summary>
    public double Angle => Kind == CardPlaceKind.Empty ? 0 : Index % 2 == 0 ? -1.5 : 1;
    public Avalonia.Media.ITransform Tilt => new Avalonia.Media.RotateTransform(Angle);
}

public enum CardPlaceKind { Session, Entry, Empty }

/// <summary>An other version of the game on the shelf ("Englisch (NTSC)"), offered under "Auch im Regal".</summary>
public sealed record VariantOption(GameItemViewModel Game, string Label);

/// <summary>One line of the file info overlay ("Ordner", the path in Cascadia Mono).</summary>
public sealed record InfoLine(string Label, string Value, bool Mono = false);

/// <summary>The overlays of the detail page (DESIGN 7.3).</summary>
public enum DetailSheet { None, More, FileInfo, Description, Cover, Screens, Variants, Choice }

/// <summary>What the choice list of the detail page offers.</summary>
public enum DetailChoice { Emulator, Mode }

/// <summary>Where the focus is on the detail page: the footer names the action of the focused element.</summary>
public enum DetailFocus { Play, Solve, Favorite, More, Box, System, Row, File, Variants, Screen, ReadMore, Neighbour, Sheet }

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

    // ===================== Rückseite: screens, Ausleihkarte (DESIGN 7.3) =====================

    /// <summary>Title screen and snap on disk (or fetched with online covers); null = that screen drops out.</summary>
    [ObservableProperty] public partial string? TitleScreen { get; private set; }
    [ObservableProperty] public partial string? SnapScreen { get; private set; }
    [ObservableProperty] public partial bool HasScreens { get; private set; }
    [ObservableProperty] public partial bool HasTitleScreen { get; private set; }
    [ObservableProperty] public partial bool HasSnapScreen { get; private set; }
    /// <summary>"Über das Spiel" has something to show: a description or a screen. Otherwise the column drops out.</summary>
    [ObservableProperty] public partial bool HasAbout { get; private set; }
    /// <summary>A home console makes TV pictures (near-4:3 screens fill the glass); a handheld's are letterboxed.</summary>
    [ObservableProperty] public partial bool IsTvSystem { get; private set; } = true;

    /// <summary>The places of the Ausleihkarte: always <see cref="CardSize"/> (stamps, the entry stamp, empty places).</summary>
    [ObservableProperty] public partial IReadOnlyList<CardPlace> CardPlaces { get; private set; } = [];
    /// <summary>"Die letzten 5 von 41 Sitzungen." when the card cannot show them all; else empty.</summary>
    [ObservableProperty] public partial string CardSentence { get; private set; } = "";

    /// <summary>Places on the card: the last sessions (oldest left), the entry stamp while it still fits, empty places.</summary>
    public const int CardSize = 5;

    /// <summary>
    /// "Daneben im Regal" (M4): the boxes that stand left and right of this one on its system's shelf (title order), at
    /// most <see cref="NeighbourCount"/> each side; the view shows as many as fit around the gap the pulled box left.
    /// </summary>
    [ObservableProperty] public partial IReadOnlyList<GameItemViewModel> NeighboursBefore { get; private set; } = [];
    [ObservableProperty] public partial IReadOnlyList<GameItemViewModel> NeighboursAfter { get; private set; } = [];
    [ObservableProperty] public partial bool HasNeighbours { get; private set; }

    /// <summary>
    /// The same game in other versions on the shelf (same title without region/version tags): folded out of the
    /// neighbours like the Start shelves fold them, named in one row of "Start" ("Auch im Regal: Deutsch, Englisch
    /// (NTSC)") - three identical covers beside the gap read as a duplication bug (M4 QA).
    /// </summary>
    [ObservableProperty] public partial IReadOnlyList<VariantOption> Variants { get; private set; } = [];
    [ObservableProperty] public partial bool HasVariants { get; private set; }
    [ObservableProperty] public partial string VariantsText { get; private set; } = "";

    public const int NeighbourCount = 4;

    /// <summary>
    /// "Chinesisch, Deutsch, Englisch und 4 weitere" – the row counts instead of clipping ("Italienisch,…" read
    /// unfinished, QA 2): up to three names, then the rest as a number.
    /// </summary>
    public static string JoinCounted(IReadOnlyList<string> labels, int shown = 3)
    {
        if (labels.Count <= shown) return labels.Count switch
        {
            0 => "",
            1 => labels[0],
            _ => Loc.T("detail.variants.and", string.Join(", ", labels.Take(labels.Count - 1)), labels[^1]),
        };
        return Loc.T("detail.variants.more", string.Join(", ", labels.Take(shown)), labels.Count - shown);
    }

    // ===================== overlays =====================

    [ObservableProperty] public partial DetailSheet Sheet { get; private set; }
    /// <summary>The choice list's entries (the emulators or the start modes) and what it chooses.</summary>
    [ObservableProperty] public partial IReadOnlyList<PickOption> ChoiceItems { get; private set; } = [];
    [ObservableProperty] public partial DetailChoice Choice { get; private set; }
    public bool IsSheetOpen => Sheet != DetailSheet.None;
    [ObservableProperty] public partial IReadOnlyList<InfoLine> FileLines { get; private set; } = [];
    /// <summary>The game has a per-game emulator or mode choice ("Startoptionen zurücksetzen" is offered).</summary>
    [ObservableProperty] public partial bool HasOverrides { get; private set; }

    partial void OnSheetChanged(DetailSheet value)
    {
        OnPropertyChanged(nameof(IsSheetOpen));
        // the dim reaches under the header too (no seam at its lower edge; the header's content stays above it)
        HeaderDim = value switch
        {
            // the choice list, the file info and the opened description fold open in the page itself: nothing dims
            // (M6 QA, QA 2)
            DetailSheet.None or DetailSheet.Choice or DetailSheet.FileInfo or DetailSheet.Description => default,
            DetailSheet.Cover or DetailSheet.Screens => SheetDimDeep,
            _ => SheetDim,
        };
        RaiseHintsChanged();
    }

    /// <summary>Raum-Tief 72 % (the lists) and 96 % (the cover and the screens large: QA 2, nothing of the page reads through).</summary>
    public static readonly Avalonia.Media.Color SheetDim = Avalonia.Media.Color.FromArgb(0xB8, 0x0A, 0x09, 0x08),
        SheetDimDeep = Avalonia.Media.Color.FromArgb(0xF5, 0x0A, 0x09, 0x08);

    /// <summary>Shoulder left/right: the view jumps to the previous/next section (Bühne, Über das Spiel, Start).</summary>
    public event Action<int>? SectionRequested;

    private DetailFocus _focus = DetailFocus.Play;

    /// <summary>The view reports where the focus is (the footer follows at once).</summary>
    public void SetFocus(DetailFocus focus)
    {
        if (_focus == focus) return;
        _focus = focus;
        RaiseHintsChanged();
    }

    public override IReadOnlyList<HintItem> Hints
    {
        get
        {
            if (IsSheetOpen)
                return Sheet switch
                {
                    DetailSheet.More or DetailSheet.Variants or DetailSheet.Choice => [new(NavAction.Accept, Loc.T("hint.select")), new(NavAction.Back, Loc.T("hint.close"))],
                    DetailSheet.FileInfo => [new(NavAction.Accept, Loc.T("detail.openFolder")), new(NavAction.Back, Loc.T("hint.close"))],
                    DetailSheet.Description => [new(NavAction.Accept, Loc.T("detail.readLess")), new(NavAction.Back, Loc.T("hint.close"))],
                    _ => [new(NavAction.Back, Loc.T("hint.close"))],
                };
            var accept = _focus switch
            {
                DetailFocus.Play => Loc.T("detail.play"),
                DetailFocus.Solve => SolutionText,
                DetailFocus.Favorite => Game?.IsFavorite == true ? Loc.T("hint.unfavorite") : Loc.T("hint.favorite"),
                DetailFocus.More => Loc.T("detail.more"),
                DetailFocus.Box => Loc.T("hint.cover"),
                DetailFocus.System => Loc.T("hint.open"),
                DetailFocus.Row => Loc.T("hint.change"),
                DetailFocus.File => Loc.T("hint.fileInfo"),
                DetailFocus.Variants => Loc.T("hint.open"),
                DetailFocus.Screen => Loc.T("hint.view"),
                DetailFocus.ReadMore => Loc.T("hint.readMore"),
                DetailFocus.Neighbour => Loc.T("hint.open"),
                _ => Loc.T("hint.select"),
            };
            var list = new List<HintItem> { new(NavAction.Accept, accept) };
            // F toggles the favourite everywhere on this page; on the Favorit button Accept already says it
            if (_focus != DetailFocus.Favorite)
                list.Add(new(NavAction.Favorite, Game?.IsFavorite == true ? Loc.T("hint.unfavorite") : Loc.T("hint.favorite")));
            list.Add(new(NavAction.PageRight, Loc.T("hint.section")));
            list.Add(new(NavAction.Back, Loc.T("hint.back")));
            return list;
        }
    }

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
        if (action == NavAction.Back && IsSheetOpen)
        {
            Sheet = DetailSheet.None;
            return true;
        }
        // an overlay is modal: the shoulders and F do nothing behind it
        if (IsSheetOpen) return action is NavAction.PageLeft or NavAction.PageRight or NavAction.Favorite or NavAction.View;
        if (action == NavAction.Favorite && Game != null)
        {
            _ = ToggleFavoriteAsync();
            return true;
        }
        if (action is NavAction.PageLeft or NavAction.PageRight)
        {
            SectionRequested?.Invoke(action == NavAction.PageLeft ? -1 : +1);
            return true;
        }
        return false;
    }

    public override void OnNavigatedFrom()
    {
        base.OnNavigatedFrom();
        Sheet = DetailSheet.None;
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
            var sessions = s.Games.Sessions(gameId, CardSize);
            // the shelf the box was pulled from: the same system in title order, the box's neighbours left and right
            var shelf = s.Games.Query(new GameQuery { SystemId = game.SystemId, Sort = GameSort.Title });
            var around = ShelfAround(shelf, game, NeighbourCount);
            return (Game: game, Choices: choices, Plan: plan, Stats: stats, Sessions: sessions, Before: around.Before, After: around.After, Variants: around.Variants);
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
            Location = Loc.T("detail.location", Game.SystemName, g.DisplayTitle);
            IsTvSystem = def == null || def.Category is SystemCategory.Console or SystemCategory.Arcade;
            // TV light of the game above the stage board; the glow sits at the box (left)
            Backdrop = new BackdropSpec(BackdropKind.TvLight, Game.CoverPath, g.SystemId, Game.Accent, g.Id, StageShare: 0.63, GlowX: 0.16, GlowY: 0.38, GhostX: 0.88,
                GhostShare: 0.4, GhostAlpha: 0.04); // no picture: a small, faint motif standing on the board, no watermark (M4 QA)
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
            // only the region known ("Japan."): no orphan line above the play sentence – one line (QA 2)
            if (MetaSentence.Length > 0 && string.IsNullOrEmpty(g.Genre) && string.IsNullOrEmpty(g.Developer) && g.Year == null && g.Players == null)
            {
                PlaySentence = MetaSentence + " " + PlaySentence;
                MetaSentence = "";
            }
            PlayTime = Fmt.PlayTime(g.PlaySeconds);
            LastPlayed = Fmt.LastPlayed(g.LastPlayedUtc);
            PlayCount = g.PlayCount == 0 ? Loc.T("detail.neverStarted") : g.PlayCount == 1 ? Loc.T("detail.startedOnce") : Loc.T("detail.startedTimes", g.PlayCount);
            Added = Fmt.Date(g.AddedUtc);
            FilePath = g.Path;
            HasCover = !string.IsNullOrEmpty(g.CoverPath);
            FileName = Path.GetFileName(g.Path);
            FileInfo = Fmt.Size(g.Size); // name and size only; path and CRC belong in the file info overlay
            // the file info folds open right under the "Datei" row, which names the file already (QA 2)
            FileLines = BuildFileLines(g, SafeAbsolute(g.Path)).Skip(1).ToList();
            HasOverrides = g.EmulatorOverride != null || g.LaunchModeOverride != null;
            BuildCard(g, data.Sessions, DateTime.UtcNow);
            NeighboursBefore = data.Before.Select(Ctx.Items.Get).ToList();
            NeighboursAfter = data.After.Select(Ctx.Items.Get).ToList();
            HasNeighbours = NeighboursBefore.Count + NeighboursAfter.Count > 0;
            Variants = data.Variants.Select(v => new VariantOption(Ctx.Items.Get(v), VariantLabel(v))).OrderBy(v => v.Label, StringComparer.Create(Loc.Culture, true)).ToList();
            HasVariants = Variants.Count > 0;
            VariantsText = JoinCounted(Variants.Select(v => v.Label).ToList());
            if (_screensFor != g.Id) SetScreens(null, null);
            HasAbout = HasDescription || HasScreens;
            LoadScreens(g);

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
        var firstBlocking = plan.Problems.FirstOrDefault(p => p.Blocking);
        // a sentence, no "A · B" chain: "Startet mit RetroArch (snes9x) im Vollbild."
        PlanSummary = plan.Emulator != null
            ? Loc.T("detail.planSummary", Controls.Sentence.Name(plan.Emulator.Def.Name + (plan.Core != null ? $" ({plan.Core})" : "")), Fmt.ModePhrase(plan.Mode))
            : Loc.T("detail.planNoEmulator");
        ShowSolution = !CanPlay && Game != null;
        // the way out names the FIRST thing in the way: a missing file before a missing emulator
        _solveFile = Game?.IsMissing == true || firstBlocking?.Kind == LaunchProblemKind.FileMissing;
        SolutionText = _solveFile ? Loc.T("detail.solution.file") : Loc.T("detail.solution.emulator");
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
    private bool _solveFile;

    [RelayCommand]
    private void Solve()
    {
        if (Game == null) return;
        if (_solveFile) Ctx.Navigator.Navigate(PageKey.Imports);
        else Ctx.Navigator.Navigate(PageKey.Emulators, Game.SystemId);
    }

    // ===================== overlays (DESIGN 7.3) =====================

    [RelayCommand] private void ShowMore() => Sheet = DetailSheet.More;

    /// <summary>"Emulator ›": the choices as a list at the row, the current one checked (DESIGN 7.3, in-style picker).</summary>
    [RelayCommand]
    private void ShowEmulatorChoice()
    {
        if (Emulators.Count == 0) return;
        // the row again (a second click): the list closes
        if (Sheet == DetailSheet.Choice && Choice == DetailChoice.Emulator) { Sheet = DetailSheet.None; return; }
        Choice = DetailChoice.Emulator;
        ChoiceItems = Emulators.Select(e => new PickOption(e, e.Label, e.Status, ReferenceEquals(e, SelectedEmulator))).ToList();
        Sheet = DetailSheet.Choice;
    }

    /// <summary>"Startmodus ›": the modes of the chosen emulator.</summary>
    [RelayCommand]
    private void ShowModeChoice()
    {
        if (Modes.Count == 0) return;
        if (Sheet == DetailSheet.Choice && Choice == DetailChoice.Mode) { Sheet = DetailSheet.None; return; }
        Choice = DetailChoice.Mode;
        ChoiceItems = Modes.Select(m => new PickOption(m, m.Label, null, ReferenceEquals(m, SelectedMode))).ToList();
        Sheet = DetailSheet.Choice;
    }

    /// <summary>A choice taken: stored at once (the plan and its problems follow), the list closes.</summary>
    [RelayCommand]
    private void Pick(PickOption? option)
    {
        Sheet = DetailSheet.None;
        switch (option?.Value)
        {
            case EmulatorOption e when !ReferenceEquals(e, SelectedEmulator):
                SelectedEmulator = e;
                break;
            case ModeOption m when !ReferenceEquals(m, SelectedMode):
                SelectedMode = m;
                break;
        }
    }

    /// <summary>"Auch im Regal": one other version opens at once, several are offered in a list at the row.</summary>
    [RelayCommand]
    private void ShowVariants()
    {
        if (Variants.Count == 1) OpenVariant(Variants[0]);
        else if (Variants.Count > 1) Sheet = DetailSheet.Variants;
    }

    [RelayCommand]
    private void OpenVariant(VariantOption? variant)
    {
        if (variant == null) return;
        Sheet = DetailSheet.None;
        Ctx.Navigator.Navigate(PageKey.GameDetail, variant.Game);
    }
    /// <summary>The file info folds open under the "Datei" row; confirm on the row again folds it shut.</summary>
    [RelayCommand] private void ShowFileInfo() => Sheet = Sheet == DetailSheet.FileInfo ? DetailSheet.None : DetailSheet.FileInfo;
    /// <summary>"Weiterlesen" opens the text in place, "Weniger anzeigen" closes it.</summary>
    [RelayCommand] private void ShowDescription() { if (HasDescription) Sheet = Sheet == DetailSheet.Description ? DetailSheet.None : DetailSheet.Description; }
    [RelayCommand] private void ShowCover() { if (Game != null) Sheet = DetailSheet.Cover; }
    [RelayCommand] private void ShowScreens() { if (HasScreens) Sheet = DetailSheet.Screens; }
    [RelayCommand] private void CloseSheet() => Sheet = DetailSheet.None;

    /// <summary>"Startoptionen zurücksetzen": emulator and mode back to "Automatisch" (the system's defaults).</summary>
    [RelayCommand]
    private async Task ResetLaunchOptionsAsync()
    {
        Sheet = DetailSheet.None;
        var item = Game;
        if (item == null) return;
        var s = Ctx.Services;
        var ok = await UiContext.Background(() =>
        {
            var game = s.Games.Get(item.Id);
            if (game == null) return false;
            game.EmulatorOverride = null;
            game.LaunchModeOverride = null;
            s.Games.Upsert(game);
            return true;
        }, "Resetting the launch options");
        if (ok && Game == item) await LoadAsync(item.Id);
    }

    // ===================== screens =====================

    private long _screensFor;
    private CancellationTokenSource? _screenCts;

    private void SetScreens(string? title, string? snap)
    {
        TitleScreen = title;
        SnapScreen = snap;
        HasTitleScreen = title != null;
        HasSnapScreen = snap != null;
        HasScreens = title != null || snap != null;
        HasAbout = HasDescription || HasScreens;
    }

    /// <summary>
    /// Title screen and snap for the back (DESIGN 7.3/10.1): what is on disk at once, the missing kinds only with online
    /// covers (the Start stage does the same). What is missing drops out; the demo has no artwork service.
    /// </summary>
    private void LoadScreens(Core.Library.Game game)
    {
        if (Ctx.Services.Artwork is not { } artwork) return;
        _screenCts?.Cancel();
        var cts = _screenCts = new CancellationTokenSource();
        var id = game.Id;
        var online = Ctx.Services.Settings.Current.OnlineCovers;
        _ = Task.Run(async () =>
        {
            var local = artwork.FindScreens(game);
            Avalonia.Threading.Dispatcher.UIThread.Post(() => ApplyScreens(id, local.Title, local.Snap));
            if ((local.Snap != null && local.Title != null) || !online) return;
            var fetched = await artwork.FetchScreensAsync(game, cts.Token).ConfigureAwait(false);
            if (fetched.Snap != local.Snap || fetched.Title != local.Title)
                Avalonia.Threading.Dispatcher.UIThread.Post(() => ApplyScreens(id, fetched.Title, fetched.Snap));
        }, cts.Token).ContinueWith(t => { if (t.IsFaulted) Log.Warn("Detail screen lookup failed", t.Exception); }, TaskScheduler.Default);
    }

    private void ApplyScreens(long id, string? title, string? snap)
    {
        if (Game?.Id != id) return;
        _screensFor = id;
        SetScreens(title, snap);
    }

    // ===================== Ausleihkarte =====================

    /// <summary>
    /// "Gespielt" (DESIGN 7.3, M4 QA): <see cref="CardSize"/> places, filled from the left – the entry stamp ("Eingang",
    /// the day the game came into the collection) while the record still reaches back that far, then the sessions
    /// oldest to newest, then empty places (a dashed baseline each) waiting for the next stamps. A game never played
    /// shows only its entry stamp and the sentence "Noch kein Eintrag." – four empty boxes read as a loading skeleton.
    /// </summary>
    internal void BuildCard(Core.Library.Game g, IReadOnlyList<PlaySession> recent, DateTime nowUtc)
    {
        var places = new List<CardPlace>(CardSize);
        var total = Math.Max(g.PlayCount, recent.Count);
        var shown = recent.Take(CardSize).Reverse().ToList(); // oldest left
        // the entry stamp only while the card reaches back to the start (every session known and room left)
        if (recent.Count >= total && total < CardSize) places.Add(new CardPlace(StampDate(g.AddedUtc, nowUtc), Loc.T("detail.card.entry"), CardPlaceKind.Entry, places.Count));
        foreach (var session in shown.Skip(Math.Max(0, shown.Count - (CardSize - places.Count))))
            places.Add(new CardPlace(StampDate(session.StartUtc, nowUtc), StampDuration(session.Seconds), CardPlaceKind.Session, places.Count));
        var stamps = places.Count(p => p.IsSession);
        if (stamps > 0)
            while (places.Count < CardSize) places.Add(new CardPlace("", "", CardPlaceKind.Empty, places.Count));
        CardPlaces = places;
        CardSentence = stamps == 0 && total == 0 ? Loc.T("detail.card.none")
            : total > stamps && stamps > 0 ? Loc.T("detail.card.lastOf", stamps, total) : "";
    }

    /// <summary>
    /// The shelf around <paramref name="game"/> (title order), folded like the Start shelves: one box per title without
    /// region/version tags. Returns up to <paramref name="count"/> other titles on each side and the other versions of
    /// the game itself (they stand in the same spot of the shelf).
    /// </summary>
    internal static (List<Core.Library.Game> Before, List<Core.Library.Game> After, List<Core.Library.Game> Variants) ShelfAround(
        IReadOnlyList<Core.Library.Game> shelf, Core.Library.Game game, int count)
    {
        var own = HomeViewModel.NormalizeTitle(game.Title);
        var variants = shelf.Where(x => x.Id != game.Id && HomeViewModel.NormalizeTitle(x.Title) == own).ToList();
        var folded = new List<Core.Library.Game>();
        var seen = new HashSet<string>();
        var at = -1;
        foreach (var x in shelf)
        {
            var key = HomeViewModel.NormalizeTitle(x.Title);
            if (key == own)
            {
                if (at < 0) { at = folded.Count; folded.Add(game); seen.Add(key); }
                continue;
            }
            if (seen.Add(key)) folded.Add(x);
        }
        if (at < 0) return ([], [], variants);
        var before = folded.Skip(Math.Max(0, at - count)).Take(at - Math.Max(0, at - count)).ToList();
        var after = folded.Skip(at + 1).Take(count).ToList();
        return (before, after, variants);
    }

    /// <summary>"Deutsch", "Englisch (NTSC)": the region words of a version, the first one leading; else "Original".</summary>
    internal static string VariantLabel(Core.Library.Game g)
    {
        var words = Fmt.Region(g.Region).Split(", ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0) return Loc.T("library.variant.original");
        return words.Length == 1 ? words[0] : $"{words[0]} ({string.Join(", ", words.Skip(1))})";
    }

    /// <summary>"9. Nov." – with the year when it is not this year ("3. März 2024").</summary>
    internal static string StampDate(DateTime utc, DateTime nowUtc)
    {
        var local = utc.ToLocalTime();
        var now = nowUtc.ToLocalTime();
        return local.ToString(local.Year == now.Year ? "d. MMM" : "d. MMM yyyy", Loc.Culture);
    }

    /// <summary>"1:12 Std." from an hour on, else "12 Min."; a session under a minute is "unter 1 Min.".</summary>
    internal static string StampDuration(long seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        if (t.TotalHours >= 1) return Loc.T("detail.card.hours", $"{(int)t.TotalHours}:{t.Minutes:00}");
        if (t.TotalMinutes >= 1) return Loc.T("detail.card.minutes", (int)t.TotalMinutes);
        return Loc.T("detail.card.underMinute");
    }

    // ===================== file info =====================

    private string SafeAbsolute(string stored)
    {
        try { return Ctx.Services.Paths.FromPortable(stored); }
        catch (Exception) { return stored; }
    }

    /// <summary>The file info overlay: name, folder (Cascadia Mono), size, CRC32, header code, dates – what is known.</summary>
    internal static IReadOnlyList<InfoLine> BuildFileLines(Core.Library.Game g, string fullPath)
    {
        var lines = new List<InfoLine> { new(Loc.T("detail.file"), Path.GetFileName(fullPath)) };
        if (Path.GetDirectoryName(fullPath) is { Length: > 0 } folder) lines.Add(new InfoLine(Loc.T("detail.folder"), folder, true));
        if (g.Size > 0) lines.Add(new InfoLine(Loc.T("detail.size"), $"{Fmt.Size(g.Size)} ({g.Size.ToString("N0", Loc.Culture)} Bytes)"));
        if (!string.IsNullOrEmpty(g.Crc32)) lines.Add(new InfoLine("CRC32", g.Crc32!, true));
        if (!string.IsNullOrEmpty(g.Serial)) lines.Add(new InfoLine(Loc.T("detail.serial"), g.Serial!, true));
        lines.Add(new InfoLine(Loc.T("detail.added"), Fmt.DateTimeText(g.AddedUtc)));
        if (g.FileTimeUtc > DateTime.MinValue.AddYears(1)) lines.Add(new InfoLine(Loc.T("detail.fileTime"), Fmt.DateTimeText(g.FileTimeUtc)));
        return lines;
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
        Sheet = DetailSheet.None;
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
        Sheet = DetailSheet.None;
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
        Sheet = DetailSheet.None;
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
