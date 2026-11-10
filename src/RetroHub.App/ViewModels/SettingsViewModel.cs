using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using RetroHub.App.Input;
using RetroHub.App.Localization;
using RetroHub.App.Services;
using RetroHub.App.Theming;
using RetroHub.Core.Config;
using RetroHub.Core.Logging;

namespace RetroHub.App.ViewModels;

public sealed record PathEntry(string Label, string Path, string Display);

/// <summary>
/// Einstellungen as a service menu (DESIGN 7.6): one continuous list of <see cref="ServiceSection"/>s whose rows are
/// <see cref="ServiceSetting"/>s (◀ Wert ▶ or "Öffnen ›"). Every value is applied and saved the moment it changes;
/// shoulder left/right jump between the sections (<see cref="SectionRequested"/>).
/// </summary>
public sealed partial class SettingsViewModel : PageViewModel
{
    private bool _loading;

    /// <summary>"Zurück zu RetroHub": off, then half seconds up to 3 s (the old slider's steps).</summary>
    public static readonly double[] ExitComboSteps = [0, 0.5, 1, 1.5, 2, 2.5, 3];

    public SettingsViewModel(UiContext ctx) : base(ctx)
    {
        Title = Loc.T("nav.settings");
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
        Sections = BuildSections();
    }

    public override PageKey Key => PageKey.Settings;

    public IReadOnlyList<PathEntry> Paths { get; }
    public IReadOnlyList<ServiceSection> Sections { get; }

    // the rows the menu re-reads when the page is shown again (settings may have changed elsewhere, e.g. F11)
    private ServiceSetting _accent = null!, _motion = null!, _osd = null!, _hints = null!, _fullscreen = null!, _controller = null!,
        _exitCombo = null!, _scan = null!, _watch = null!, _hashes = null!, _online = null!, _language = null!;

    [ObservableProperty] public partial string ControllerStatus { get; private set; } = "";

    public bool IsDemo => Ctx.Services.IsDemo;

    /// <summary>Shoulder left/right: the view focuses the first row of the previous/next section.</summary>
    public event Action<int>? SectionRequested;

    public override void OnNavigatedTo(object? argument, bool isBack)
    {
        Title = Loc.T("nav.settings");
        ReadSettings();
    }

    public override bool HandleAction(NavAction action)
    {
        if (action is NavAction.PageLeft or NavAction.PageRight)
        {
            SectionRequested?.Invoke(action == NavAction.PageLeft ? -1 : +1);
            return true;
        }
        return false;
    }

    public override IReadOnlyList<HintItem> Hints =>
    [
        new(NavAction.Accept, Loc.T("hint.change")),
        new(NavAction.PageRight, Loc.T("hint.section")),
        new(NavAction.Back, Loc.T("hint.back")),
    ];

    private IReadOnlyList<ServiceSection> BuildSections()
    {
        var s = Ctx.Services.Settings.Current;
        var ui = Ctx.Services.Ui.Current;
        string on = Loc.T("settings.on"), off = Loc.T("settings.off");

        var accents = ThemeManager.Presets.Select(p => new ServiceValue(p.Hex, Loc.T(p.NameKey), new ImmutableSolidColorBrush(Color.Parse(p.Hex)))).ToList();
        _accent = ServiceSetting.Choice(Loc.T("settings.accent"), Loc.T("settings.accent.hint"), accents, AccentIndex(accents, s.Accent), v =>
        {
            var hex = (string)v.Value!;
            var stored = string.Equals(hex, ThemeManager.DefaultAccent, StringComparison.OrdinalIgnoreCase) ? null : hex;
            ThemeManager.ApplyAccent(stored);
            Save(x => x.Accent = stored);
        });

        var motions = new (MotionLevel Level, string Key)[]
        {
            (MotionLevel.System, "settings.motion.system"), (MotionLevel.Full, "settings.motion.full"),
            (MotionLevel.Reduced, "settings.motion.reduced"), (MotionLevel.Off, "settings.motion.off"),
        };
        var motionValues = motions.Select(m => new ServiceValue(m.Level, Loc.T(m.Key))).ToList();
        var motionIndex = Math.Max(0, Array.FindIndex(motions, m => m.Level == s.Motion));
        _motion = ServiceSetting.Choice(Loc.T("settings.motion"), Loc.T(motions[motionIndex].Key + ".hint"), motionValues, motionIndex, v =>
        {
            var level = (MotionLevel)v.Value!;
            _motion.SetHint(Loc.T(motions.First(m => m.Level == level).Key + ".hint"));
            Motion.Apply(level);
            Save(x => x.Motion = level);
        });
        _osd = ServiceSetting.Switch(Loc.T("settings.osd"), Loc.T("settings.osd.hint"), ui.ShowOsd, v => UpdateUi(x => x.ShowOsd = v), on, off);
        _hints = ServiceSetting.Switch(Loc.T("settings.hints"), Loc.T("settings.hints.hint"), ui.ShowHints, v => UpdateUi(x => x.ShowHints = v), on, off);
        _fullscreen = ServiceSetting.Switch(Loc.T("settings.fullscreen"), Loc.T("settings.fullscreen.hint"), s.StartFullscreen, v => Save(x => x.StartFullscreen = v), on, off);

        _controller = ServiceSetting.Switch(Loc.T("settings.controller.enabled"), Loc.T("settings.controller.enabled.hint"), s.ControllerEnabled,
            v => Save(x => x.ControllerEnabled = v), on, off);
        var combo = ExitComboSteps.Select(sec => new ServiceValue(sec, ExitComboText(sec))).ToList();
        _exitCombo = ServiceSetting.Choice(Loc.T("settings.exitCombo"), Loc.T("settings.exitCombo.hint"), combo, ExitComboIndex(s.ExitComboSeconds),
            v => Save(x => x.ExitComboSeconds = (double)v.Value!), wraps: false);

        _scan = ServiceSetting.Switch(Loc.T("settings.scanOnStartup"), Loc.T("settings.scanOnStartup.hint"), s.ScanOnStartup, v => Save(x => x.ScanOnStartup = v), on, off);
        _watch = ServiceSetting.Switch(Loc.T("settings.watchFolders"), Loc.T("settings.watchFolders.hint"), s.WatchFolders, v => Save(x => x.WatchFolders = v), on, off);
        _hashes = ServiceSetting.Switch(Loc.T("settings.hashes"), Loc.T("settings.hashes.hint"), s.ComputeHashes, v => Save(x => x.ComputeHashes = v), on, off);
        _online = ServiceSetting.Switch(Loc.T("settings.onlineCovers"), Loc.T("settings.onlineCovers.hint"), s.OnlineCovers, v => Save(x => x.OnlineCovers = v), on, off);

        var languages = Loc.Available.Select(l => new ServiceValue(l.Code, l.Name)).ToList();
        _language = ServiceSetting.Choice(Loc.T("settings.language"), Loc.T("settings.language.hint"), languages,
            Math.Max(0, languages.FindIndex(l => Equals(l.Value, s.Language))), v => ChangeLanguage((string)v.Value!));

        var folders = Paths.Select(path => ServiceSetting.Action(path.Label, path.Display, Loc.T("settings.open"), () => OpenPath(path))).ToList();

        ControllerStatus = ControllerText();
        return
        [
            new(Loc.T("settings.appearance"), [_accent, _motion, _osd, _hints, _fullscreen]),
            new(Loc.T("settings.controls"), [_controller, _exitCombo], ControllerStatus),
            new(Loc.T("settings.library"), [_scan, _watch, _hashes, _online]),
            new(Loc.T("settings.language"), [_language]),
            new(Loc.T("settings.paths"), folders, Loc.T("settings.paths.hint")),
        ];
    }

    private static int AccentIndex(IReadOnlyList<ServiceValue> accents, string? hex)
    {
        var want = hex ?? ThemeManager.DefaultAccent;
        var i = accents.ToList().FindIndex(a => string.Equals((string)a.Value!, want, StringComparison.OrdinalIgnoreCase));
        return Math.Max(0, i);
    }

    private static int ExitComboIndex(double seconds)
    {
        // the nearest step (older files may hold any value)
        var best = 0;
        for (var i = 1; i < ExitComboSteps.Length; i++)
            if (Math.Abs(ExitComboSteps[i] - seconds) < Math.Abs(ExitComboSteps[best] - seconds)) best = i;
        return best;
    }

    public static string ExitComboText(double seconds) =>
        seconds <= 0 ? Loc.T("settings.exitCombo.off") : Loc.T("settings.exitCombo.seconds", seconds.ToString("0.0", Loc.Culture));

    private string ControllerText()
    {
        var input = Ctx.Services.Input;
        return input.ControllerName is { } n ? Loc.T("settings.controller.connected", n)
            : input is NullInputSource ? Loc.T("settings.controller.notAvailable") : Loc.T("settings.controller.none");
    }

    /// <summary>The page is shown again: every row shows what is stored now (nothing is applied or saved).</summary>
    private void ReadSettings()
    {
        _loading = true;
        try
        {
            var s = Ctx.Services.Settings.Current;
            var ui = Ctx.Services.Ui.Current;
            _accent.Show(AccentIndex(_accent.Values, s.Accent));
            var motion = _motion.Values.ToList().FindIndex(v => Equals(v.Value, s.Motion));
            _motion.Show(Math.Max(0, motion));
            _osd.Show(ui.ShowOsd ? 0 : 1);
            _hints.Show(ui.ShowHints ? 0 : 1);
            _fullscreen.Show(s.StartFullscreen ? 0 : 1);
            _controller.Show(s.ControllerEnabled ? 0 : 1);
            _exitCombo.Show(ExitComboIndex(s.ExitComboSeconds));
            _scan.Show(s.ScanOnStartup ? 0 : 1);
            _watch.Show(s.WatchFolders ? 0 : 1);
            _hashes.Show(s.ComputeHashes ? 0 : 1);
            _online.Show(s.OnlineCovers ? 0 : 1);
            _language.Show(Math.Max(0, _language.Values.ToList().FindIndex(l => Equals(l.Value, s.Language))));
            ControllerStatus = ControllerText();
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

    private void UpdateUi(Action<UiPreferences> change)
    {
        if (_loading) return;
        Ctx.Services.Ui.Update(change);
    }

    private void ChangeLanguage(string code)
    {
        if (_loading || code == Loc.Language) return;
        Save(s => s.Language = code);
        Loc.Load(code, Ctx.Services.Paths.Resources);
        Ctx.Toasts.Show(Loc.T("settings.languageChanged"), ToastKind.Info, Loc.T("settings.languageChanged.detail"));
    }

    private void OpenPath(PathEntry entry)
    {
        if (!ShellOpen.Folder(entry.Path, createIfInside: Ctx.Services.Paths.Root))
            Ctx.Toasts.Show(Loc.T("common.folderMissing", entry.Path), ToastKind.Warning);
    }
}
