using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetroHub.App.Input;
using RetroHub.App.Localization;
using RetroHub.App.Services;
using RetroHub.App.Theming;
using RetroHub.Core.Catalog;
using RetroHub.Core.Emulation;
using RetroHub.Core.Library;

namespace RetroHub.App.ViewModels;

/// <summary>A system as a mini spine on the emulator rows (code + colour band).</summary>
public sealed record SystemSpine(string Code, IBrush Color);

/// <summary>One candidate of a system ("RetroArch (mgba) – Core fehlt"), shown in the opened row.</summary>
public sealed record EmulatorChoiceLine(string Name, string State, bool IsUsable, bool IsChosen);

/// <summary>
/// The details of one emulator install (path, start modes, cores, folder, website) – shown in an opened row of a
/// system, and for the programs no system of the library uses.
/// </summary>
public sealed class EmulatorItemViewModel
{
    public EmulatorItemViewModel(EmulatorInstall install, AppServices services)
    {
        Install = install;
        var def = install.Def;
        IsInstalled = services.IsInstalled(install);
        Name = def.Name;
        Version = install.Version;
        VersionText = install.Version is { } v ? Loc.T("emulators.version", v) : IsInstalled ? Loc.T("emulators.versionUnknown") : "";
        // "Gefunden in" / "Erwartet in" in the reading font, only the path itself in Cascadia Mono
        PathPrefix = install.ExePath != null && IsInstalled ? Loc.T("emulators.foundPrefix") : Loc.T("emulators.expectedPrefix");
        PathOnly = install.ExePath != null && IsInstalled ? services.Paths.ToPortable(install.ExePath) : "emulators/" + def.Id + "/";
        if (install.UserConfigured) PathOnly += " (" + Loc.T("emulators.userPath") + ")";
        Spines = def.Systems.Select(id =>
        {
            var sys = services.Catalog.GetSystem(id);
            var look = SystemPalette.Get(id, sys != null ? ColorUtil.Parse(sys.Accent, Color.Parse("#948A7A")) : null);
            return new SystemSpine(sys is { } d ? (string.IsNullOrEmpty(d.ShortName) ? d.Name : d.ShortName) : id.ToUpperInvariant(), look.ColorBrush);
        }).ToList();
        ModesSentence = Loc.T("emulators.modesSentence", JoinOr(def.Modes.Select(Fmt.ModePhrase).ToList()));
        SystemsText = string.Join(", ", def.Systems.Select(id => services.Catalog.GetSystem(id) is { } s ? (string.IsNullOrEmpty(s.ShortName) ? s.Name : s.ShortName) : id));
        CoresText = def.Kind == EmulatorKind.RetroArch
            ? (install.InstalledCores.Count > 0 ? Loc.T("emulators.cores", string.Join(", ", install.InstalledCores)) : Loc.T("emulators.noCores"))
            : "";
        HasCores = CoresText.Length > 0;
        Folder = install.ExePath != null ? Path.GetDirectoryName(install.ExePath) : Path.Combine(services.Paths.Emulators, def.Id);
        DownloadUrl = def.DownloadUrl ?? def.Website;
        // "Installiert, Version 0.10.3. Für GB, GBC, GBA." – one sentence for a program without a system of its own
        StateSentence = (IsInstalled
                ? (install.Version is { } ver ? Loc.T("emulators.state.installedVersion", ver) : Loc.T("emulators.state.installed"))
                : Loc.T("emulators.state.missing"))
            + " " + Loc.T("emulators.state.for", SystemsText);
    }

    public EmulatorInstall Install { get; }
    public string Name { get; }
    public string? Version { get; }
    public bool IsInstalled { get; }
    public bool IsMissing => !IsInstalled;
    public string VersionText { get; }
    public string PathPrefix { get; }
    public string PathOnly { get; }
    /// <summary>Mini spines (colour band + code) of the systems this emulator plays.</summary>
    public IReadOnlyList<SystemSpine> Spines { get; }
    /// <summary>"Startet eingebettet in RetroHub, im Vollbild oder im Fenster."</summary>
    public string ModesSentence { get; }
    public string SystemsText { get; }
    public string CoresText { get; }
    public bool HasCores { get; }
    public string? Folder { get; }
    public string? DownloadUrl { get; }
    public bool HasDownload => !string.IsNullOrEmpty(DownloadUrl);
    public string StateSentence { get; }

    internal static string JoinOr(IReadOnlyList<string> parts) =>
        parts.Count <= 1 ? string.Join("", parts) : string.Join(", ", parts.Take(parts.Count - 1)) + " " + Loc.T("common.or") + " " + parts[^1];
}

/// <summary>
/// A row of Emulatoren (DESIGN 7.6 "nach eigenen Systemen"): one system of the library, the emulator it plays with
/// ("spielt mit mGBA 0.10.3") or the honest gap ("Kein Emulator gefunden. Einrichten ›"); confirm folds it open
/// (path, start modes, cores, the other candidates, folder and website). The fold for "Weitere erkannte Programme"
/// is a row of the same kind without a system.
/// </summary>
public sealed partial class EmulatorRowViewModel : ObservableObject
{
    /// <summary>A system row.</summary>
    public EmulatorRowViewModel(SystemDef system, int games, EmulatorItemViewModel? emulator, bool ready, string? core, IReadOnlyList<EmulatorChoiceLine> choices)
    {
        System = system;
        var look = SystemPalette.Get(system.Id, ColorUtil.Parse(system.Accent, Color.Parse("#948A7A")));
        Band = look.ColorBrush;
        Code = string.IsNullOrEmpty(system.ShortName) ? system.Id.ToUpperInvariant() : system.ShortName;
        Title = system.Name;
        GamesText = Loc.T(games == 1 ? "emulators.games.one" : "emulators.games", games);
        Emulator = emulator;
        Ready = ready && emulator is { IsInstalled: true };
        Choices = choices;
        PlaysWith = Ready ? emulator!.Name + (core is { Length: > 0 } c ? $" ({c})" : "") : "";
        PlaysVersion = Ready && emulator!.Version is { } v ? v : "";
    }

    /// <summary>The fold "Weitere erkannte Programme": the programs no system of the library uses.</summary>
    public EmulatorRowViewModel(IReadOnlyList<EmulatorItemViewModel> others)
    {
        Others = others;
        Code = "";
        Title = Loc.T("emulators.others");
        Band = null;
        GamesText = Loc.T("emulators.others.count", others.Count(o => o.IsInstalled), others.Count(o => !o.IsInstalled));
        Choices = [];
        PlaysWith = "";
        PlaysVersion = "";
    }

    public SystemDef? System { get; }
    public bool IsSystem => System != null;
    public bool IsOthers => Others != null;
    public IBrush? Band { get; }
    public string Code { get; }
    public string Title { get; }
    /// <summary>"12 Spiele" (Text 3 beside the name).</summary>
    public string GamesText { get; }
    public EmulatorItemViewModel? Emulator { get; }
    public bool HasEmulator => Emulator != null;
    /// <summary>The system can start its games (an installed program, with its core for RetroArch).</summary>
    public bool Ready { get; }
    public bool NotReady => IsSystem && !Ready;
    public string PlaysWith { get; }
    public string PlaysVersion { get; }
    public bool HasPlaysVersion => PlaysVersion.Length > 0;
    public IReadOnlyList<EmulatorChoiceLine> Choices { get; }
    /// <summary>More than one candidate, or the one there is cannot start: the opened row lists them with their state.</summary>
    public bool HasChoices => Choices.Count > 1 || (NotReady && Choices.Count > 0);
    /// <summary>
    /// What is in the way, as one sentence: no program at all ("Kein Emulator gefunden.") or the program without its
    /// core ("RetroArch ist da, der Core fehlt.").
    /// </summary>
    public string MissingText => NotReady && Choices.FirstOrDefault(c => c.State == Loc.T("emulators.choice.noCore")) is { } c
        ? Loc.T("emulators.coreMissing", c.Name.Split(' ')[0])
        : Loc.T("emulators.noneFound");
    public IReadOnlyList<EmulatorItemViewModel>? Others { get; }

    [ObservableProperty] public partial bool IsOpen { get; set; }
}

/// <summary>Emulatoren: the library's systems and how each one plays (DESIGN 7.6), then the other programs.</summary>
public sealed partial class EmulatorsViewModel : PageViewModel
{
    public EmulatorsViewModel(UiContext ctx) : base(ctx)
    {
        Title = Loc.T("nav.emulators");
        ctx.EmulatorsChanged += Load;
        ctx.LibraryChanged += () => { if (ctx.Navigator.Current == PageKey.Emulators) Load(); };
    }

    public override PageKey Key => PageKey.Emulators;

    [ObservableProperty] public partial IReadOnlyList<EmulatorRowViewModel> Rows { get; private set; } = [];
    /// <summary>The fold "Weitere erkannte Programme" (null when every program serves a system of the library).</summary>
    [ObservableProperty] public partial EmulatorRowViewModel? OthersRow { get; private set; }
    [ObservableProperty] public partial string SummaryText { get; private set; } = "";
    [ObservableProperty] public partial bool IsRefreshing { get; private set; }
    /// <summary>The library has no game yet: the page says so instead of an empty list.</summary>
    [ObservableProperty] public partial bool NoSystems { get; private set; }

    /// <summary>Only this system (from "Emulator einrichten" on the detail page); null = all.</summary>
    [ObservableProperty] public partial string? SystemFilter { get; private set; }
    [ObservableProperty] public partial string FilterText { get; private set; } = "";
    public bool IsFiltered => SystemFilter != null;
    partial void OnSystemFilterChanged(string? value) => OnPropertyChanged(nameof(IsFiltered));

    public override void OnNavigatedTo(object? argument, bool isBack)
    {
        Title = Loc.T("nav.emulators");
        if (!isBack) SystemFilter = argument as string;
        Load();
    }

    public override IReadOnlyList<HintItem> Hints =>
    [
        new(NavAction.Accept, Loc.T("hint.open")),
        new(NavAction.Back, Loc.T("hint.back")),
    ];

    private void Load()
    {
        var s = Ctx.Services;
        var items = s.Emulators.All.ToDictionary(i => i.Def.Id, i => new EmulatorItemViewModel(i, s), StringComparer.OrdinalIgnoreCase);
        LibraryStats stats;
        try { stats = s.Games.Stats(); }
        catch { stats = new LibraryStats(0, 0, 0, 0, new Dictionary<string, int>(), new Dictionary<string, long>()); }
        var open = Rows.Where(r => r.IsOpen && r.System != null).Select(r => r.System!.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rows = new List<EmulatorRowViewModel>();
        foreach (var system in s.Catalog.Systems.OrderBy(x => x.SortOrder))
        {
            var games = stats.GamesPerSystem.GetValueOrDefault(system.Id);
            if (games <= 0 && !string.Equals(system.Id, SystemFilter, StringComparison.OrdinalIgnoreCase)) continue;
            var choices = s.Launcher.ChoicesFor(system.Id);
            if (choices.Count == 0) continue; // PC/native systems need no emulator
            var (chosen, core) = Choose(system.Id, choices);
            var lines = choices.Select(c =>
            {
                var installed = s.IsInstalled(c.Install);
                var coreOk = c.Ref.Core == null || c.Install?.InstalledCores.Contains(c.Ref.Core) == true;
                var name = SystemViewModel.EmulatorLabel(c.Ref, c.Install?.Def.Name ?? s.Catalog.GetEmulator(c.Ref.EmulatorId)?.Name ?? c.Ref.EmulatorId);
                var state = !installed ? Loc.T("emulators.choice.missing") : !coreOk ? Loc.T("emulators.choice.noCore") : Loc.T("emulators.choice.ready");
                return new EmulatorChoiceLine(name, state, installed && coreOk, chosen != null && c.Install == chosen.Install && c.Ref.Core == core);
            }).ToList();
            foreach (var c in choices) used.Add(c.Ref.EmulatorId);
            var emulator = chosen != null && items.TryGetValue(chosen.Install!.Def.Id, out var it) ? it
                : choices[0].Install != null && items.TryGetValue(choices[0].Install!.Def.Id, out var first) ? first : null;
            var row = new EmulatorRowViewModel(system, games, emulator, chosen != null, core, lines);
            row.IsOpen = open.Contains(system.Id) || string.Equals(system.Id, SystemFilter, StringComparison.OrdinalIgnoreCase);
            rows.Add(row);
        }
        var system1 = SystemFilter != null ? s.Catalog.GetSystem(SystemFilter) : null;
        Rows = system1 != null ? rows.Where(r => string.Equals(r.System!.Id, system1.Id, StringComparison.OrdinalIgnoreCase)).ToList() : rows;
        NoSystems = Rows.Count == 0;
        // the programs no system of the library plays with: installed ones first
        var others = items.Values.Where(i => !used.Contains(i.Install.Def.Id) && i.Install.Def.Kind != EmulatorKind.Native)
            .OrderByDescending(i => i.IsInstalled).ThenBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        var othersOpen = OthersRow?.IsOpen == true;
        OthersRow = system1 == null && others.Count > 0 ? new EmulatorRowViewModel(others) { IsOpen = othersOpen } : null;
        FilterText = system1 != null ? Loc.T("emulators.filtered", system1.Name) : "";
        Location = system1?.Name;
        var ready = rows.Count(r => r.Ready);
        SummaryText = rows.Count == 0 ? Loc.T("emulators.summary.none")
            : ready == rows.Count ? Loc.T("emulators.summary.allReady", rows.Count)
            : Loc.T("emulators.summary.some", ready, rows.Count);
    }

    /// <summary>
    /// What a system plays with when a game has no choice of its own – the planner's order: the system's setting, else
    /// the first installed candidate (RetroArch only with its core). Null when nothing is usable.
    /// </summary>
    private (ChosenEmulator? Chosen, string? Core) Choose(string systemId, IReadOnlyList<(EmulatorRef Ref, EmulatorInstall? Install)> choices)
    {
        var s = Ctx.Services;
        if (s.Settings.Current.SystemEmulator?.TryGetValue(systemId, out var wanted) == true && !string.IsNullOrWhiteSpace(wanted))
        {
            var r = EmulatorRef.Parse(wanted);
            var match = choices.FirstOrDefault(c => string.Equals(c.Ref.EmulatorId, r.EmulatorId, StringComparison.OrdinalIgnoreCase)
                                                    && (r.Core == null || string.Equals(c.Ref.Core, r.Core, StringComparison.OrdinalIgnoreCase)));
            if (match.Install != null && s.IsInstalled(match.Install)) return (new ChosenEmulator(match.Install), match.Ref.Core);
        }
        foreach (var (r, install) in choices)
            if (s.IsInstalled(install) && (r.Core == null || install!.InstalledCores.Contains(r.Core)))
                return (new ChosenEmulator(install!), r.Core);
        return (null, null);
    }

    private sealed record ChosenEmulator(EmulatorInstall Install);

    [RelayCommand]
    private void Toggle(EmulatorRowViewModel? row)
    {
        if (row != null) row.IsOpen = !row.IsOpen;
    }

    [RelayCommand]
    private void ShowAll()
    {
        SystemFilter = null;
        Load();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsRefreshing = true;
        var registry = Ctx.Services.Emulators;
        await UiContext.Background(() => { registry.Refresh(); return true; }, "Refreshing emulators");
        Load();
        IsRefreshing = false;
        Ctx.Toasts.Show(Loc.T("emulators.refreshed"), ToastKind.Success, seconds: 2.5);
    }

    [RelayCommand]
    private void OpenFolder(EmulatorItemViewModel? item)
    {
        if (item?.Folder == null) return;
        if (!ShellOpen.Folder(item.Folder, createIfInside: Ctx.Services.Paths.Emulators))
            Ctx.Toasts.Show(Loc.T("common.folderMissing", item.Folder), ToastKind.Warning);
    }

    [RelayCommand]
    private void OpenDownload(EmulatorItemViewModel? item)
    {
        if (item?.DownloadUrl != null && !ShellOpen.Url(item.DownloadUrl))
            Ctx.Toasts.Show(Loc.T("common.openFailed"), ToastKind.Error);
    }

    [RelayCommand] private void OpenEmulatorsFolder()
    {
        if (!ShellOpen.Folder(Ctx.Services.Paths.Emulators, createIfInside: Ctx.Services.Paths.Root))
            Ctx.Toasts.Show(Loc.T("common.folderMissing", Ctx.Services.Paths.Emulators), ToastKind.Warning);
    }
}
