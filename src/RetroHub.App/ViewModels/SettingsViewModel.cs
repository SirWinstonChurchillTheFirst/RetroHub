using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetroHub.App.Input;
using RetroHub.App.Localization;
using RetroHub.App.Services;
using RetroHub.App.Theming;
using RetroHub.Core.Config;
using RetroHub.Core.Logging;

namespace RetroHub.App.ViewModels;

public sealed partial class AccentOption : ObservableObject
{
    public required string? Hex { get; init; }
    public required string Name { get; init; }
    public required IBrush Swatch { get; init; }
    [ObservableProperty] public partial bool IsSelected { get; set; }
}

public sealed partial class ChoiceOption : ObservableObject
{
    public required object Value { get; init; }
    public required string Label { get; init; }
    public string? Hint { get; init; }
    [ObservableProperty] public partial bool IsSelected { get; set; }
    public override string ToString() => Label;
}

public sealed record PathEntry(string Label, string Path, string Display);

/// <summary>Settings: appearance, motion, language, fullscreen, controller, library scanning, folder overview.</summary>
public sealed partial class SettingsViewModel : PageViewModel
{
    private bool _loading;

    public SettingsViewModel(UiContext ctx) : base(ctx)
    {
        Title = Loc.T("nav.settings");
        Accents = ThemeManager.Presets.Select(p => new AccentOption
        {
            Hex = p.Hex,
            Name = Loc.T(p.NameKey),
            Swatch = new ImmutableSolidColorBrush(Color.Parse(p.Hex)),
        }).ToList();
        MotionOptions =
        [
            new() { Value = MotionLevel.System, Label = Loc.T("settings.motion.system"), Hint = Loc.T("settings.motion.system.hint") },
            new() { Value = MotionLevel.Full, Label = Loc.T("settings.motion.full"), Hint = Loc.T("settings.motion.full.hint") },
            new() { Value = MotionLevel.Reduced, Label = Loc.T("settings.motion.reduced"), Hint = Loc.T("settings.motion.reduced.hint") },
            new() { Value = MotionLevel.Off, Label = Loc.T("settings.motion.off"), Hint = Loc.T("settings.motion.off.hint") },
        ];
        GlyphOptions =
        [
            new() { Value = GlyphStyle.Auto, Label = Loc.T("settings.glyphs.auto") },
            new() { Value = GlyphStyle.Xbox, Label = "Xbox" },
            new() { Value = GlyphStyle.PlayStation, Label = "PlayStation" },
        ];
        SidebarOptions =
        [
            new() { Value = SidebarMode.Auto, Label = Loc.T("settings.sidebar.auto") },
            new() { Value = SidebarMode.Expanded, Label = Loc.T("settings.sidebar.expanded") },
            new() { Value = SidebarMode.Collapsed, Label = Loc.T("settings.sidebar.collapsed") },
        ];
        Languages = Loc.Available.Select(l => new ChoiceOption { Value = l.Code, Label = l.Name }).ToList();
        var p = ctx.Services.Paths;
        Paths =
        [
            new(Loc.T("settings.paths.root"), p.Root, p.Root),
            new(Loc.T("settings.paths.games"), p.Games, "games/"),
            new(Loc.T("settings.paths.emulators"), p.Emulators, "emulators/"),
            new(Loc.T("settings.paths.bios"), p.Bios, "bios/"),
            new(Loc.T("settings.paths.saves"), p.Saves, "saves/"),
            new(Loc.T("settings.paths.savestates"), p.SaveStates, "savestates/"),
            new(Loc.T("settings.paths.screenshots"), p.Screenshots, "screenshots/"),
            new(Loc.T("settings.paths.artwork"), p.Artwork, "artwork/"),
            new(Loc.T("settings.paths.data"), p.Data, "data/"),
            new(Loc.T("settings.paths.logs"), p.Logs, "data/logs/"),
        ];
        ReadSettings();
    }

    public override PageKey Key => PageKey.Settings;

    public IReadOnlyList<AccentOption> Accents { get; }
    public IReadOnlyList<ChoiceOption> MotionOptions { get; }
    public IReadOnlyList<ChoiceOption> GlyphOptions { get; }
    public IReadOnlyList<ChoiceOption> SidebarOptions { get; }
    public IReadOnlyList<ChoiceOption> Languages { get; }
    public IReadOnlyList<PathEntry> Paths { get; }

    [ObservableProperty] public partial ChoiceOption? SelectedLanguage { get; set; }
    [ObservableProperty] public partial bool StartFullscreen { get; set; }
    [ObservableProperty] public partial bool ControllerEnabled { get; set; }
    [ObservableProperty] public partial double ExitComboSeconds { get; set; }
    [ObservableProperty] public partial bool ScanOnStartup { get; set; }
    [ObservableProperty] public partial bool WatchFolders { get; set; }
    [ObservableProperty] public partial bool ComputeHashes { get; set; }
    [ObservableProperty] public partial bool OnlineCovers { get; set; }
    [ObservableProperty] public partial bool ShowHints { get; set; }
    [ObservableProperty] public partial string MotionHint { get; private set; } = "";
    [ObservableProperty] public partial string ControllerStatus { get; private set; } = "";

    public string ExitComboText => ExitComboSeconds <= 0 ? Loc.T("settings.exitCombo.off") : Loc.T("settings.exitCombo.seconds", ExitComboSeconds.ToString("0.0", Loc.Culture));
    public bool IsDemo => Ctx.Services.IsDemo;

    public override void OnNavigatedTo(object? argument, bool isBack)
    {
        Title = Loc.T("nav.settings");
        ReadSettings();
    }

    private void ReadSettings()
    {
        _loading = true;
        try
        {
            var s = Ctx.Services.Settings.Current;
            var ui = Ctx.Services.Ui.Current;
            foreach (var a in Accents) a.IsSelected = string.Equals(a.Hex, s.Accent ?? ThemeManager.DefaultAccent, StringComparison.OrdinalIgnoreCase);
            foreach (var m in MotionOptions) m.IsSelected = Equals(m.Value, s.Motion);
            foreach (var g in GlyphOptions) g.IsSelected = Equals(g.Value, ui.Glyphs);
            foreach (var o in SidebarOptions) o.IsSelected = Equals(o.Value, ui.Sidebar);
            SelectedLanguage = Languages.FirstOrDefault(l => Equals(l.Value, s.Language)) ?? Languages[0];
            StartFullscreen = s.StartFullscreen;
            ControllerEnabled = s.ControllerEnabled;
            ExitComboSeconds = s.ExitComboSeconds;
            ScanOnStartup = s.ScanOnStartup;
            WatchFolders = s.WatchFolders;
            ComputeHashes = s.ComputeHashes;
            OnlineCovers = s.OnlineCovers;
            ShowHints = ui.ShowHints;
            MotionHint = MotionOptions.First(m => m.IsSelected).Hint ?? "";
            var input = Ctx.Services.Input;
            ControllerStatus = input.ControllerName is { } n ? Loc.T("settings.controller.connected", n)
                : input is NullInputSource ? Loc.T("settings.controller.notAvailable") : Loc.T("settings.controller.none");
        }
        finally { _loading = false; }
    }

    private void Save(Action<AppSettings> change)
    {
        if (_loading) return;
        try { Ctx.Services.Settings.Update(change); }
        catch (Exception ex)
        {
            Log.Error("Saving settings failed", ex);
            Ctx.Toasts.Show(Loc.T("settings.saveFailed"), ToastKind.Error, ex.Message);
        }
    }

    [RelayCommand]
    private void SelectAccent(AccentOption? option)
    {
        if (option == null) return;
        foreach (var a in Accents) a.IsSelected = a == option;
        var hex = string.Equals(option.Hex, ThemeManager.DefaultAccent, StringComparison.OrdinalIgnoreCase) ? null : option.Hex;
        ThemeManager.ApplyAccent(hex);
        Save(s => s.Accent = hex);
    }

    [RelayCommand]
    private void SelectMotion(ChoiceOption? option)
    {
        if (option == null) return;
        foreach (var m in MotionOptions) m.IsSelected = m == option;
        MotionHint = option.Hint ?? "";
        var level = (MotionLevel)option.Value;
        Motion.Apply(level);
        Save(s => s.Motion = level);
    }

    [RelayCommand]
    private void SelectGlyphs(ChoiceOption? option)
    {
        if (option == null) return;
        foreach (var g in GlyphOptions) g.IsSelected = g == option;
        Ctx.Services.Ui.Update(p => p.Glyphs = (GlyphStyle)option.Value);
    }

    [RelayCommand]
    private void SelectSidebar(ChoiceOption? option)
    {
        if (option == null) return;
        foreach (var o in SidebarOptions) o.IsSelected = o == option;
        Ctx.Services.Ui.Update(p => p.Sidebar = (SidebarMode)option.Value);
    }

    partial void OnSelectedLanguageChanged(ChoiceOption? value)
    {
        if (_loading || value?.Value is not string code || code == Loc.Language) return;
        Save(s => s.Language = code);
        Loc.Load(code, Ctx.Services.Paths.Resources);
        Ctx.Toasts.Show(Loc.T("settings.languageChanged"), ToastKind.Info, Loc.T("settings.languageChanged.detail"));
    }

    partial void OnStartFullscreenChanged(bool value) => Save(s => s.StartFullscreen = value);
    partial void OnControllerEnabledChanged(bool value) => Save(s => s.ControllerEnabled = value);
    partial void OnExitComboSecondsChanged(double value)
    {
        OnPropertyChanged(nameof(ExitComboText));
        Save(s => s.ExitComboSeconds = Math.Round(value * 2) / 2);
    }
    partial void OnScanOnStartupChanged(bool value) => Save(s => s.ScanOnStartup = value);
    partial void OnWatchFoldersChanged(bool value) => Save(s => s.WatchFolders = value);
    partial void OnComputeHashesChanged(bool value) => Save(s => s.ComputeHashes = value);
    partial void OnOnlineCoversChanged(bool value) => Save(s => s.OnlineCovers = value);
    partial void OnShowHintsChanged(bool value)
    {
        if (!_loading) Ctx.Services.Ui.Update(p => p.ShowHints = value);
    }

    [RelayCommand]
    private void OpenPath(PathEntry? entry)
    {
        if (entry == null) return;
        if (!ShellOpen.Folder(entry.Path, createIfInside: Ctx.Services.Paths.Root))
            Ctx.Toasts.Show(Loc.T("common.folderMissing", entry.Path), ToastKind.Warning);
    }
}
