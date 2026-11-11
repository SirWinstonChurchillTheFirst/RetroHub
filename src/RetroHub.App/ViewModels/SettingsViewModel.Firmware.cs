using RetroHub.App.Localization;
using RetroHub.App.Services;
using RetroHub.Core.Emulation.Firmware;
using RetroHub.Core.Logging;

namespace RetroHub.App.ViewModels;

/// <summary>
/// "Firmware &amp; Keys" (owner: "für jede Konsole, die Firmware braucht, ein Import"): one service-menu row per console
/// with its state as the description ("Eden · prod.keys da, Firmware fehlt") and "Importieren ›"; consoles that need
/// nothing say so on a fixed row ("Nicht nötig"). Three rows on top take files, a folder, or – for the controller,
/// whose confirm cannot drive Windows' file dialog – whatever the player put into bios/import. The work is done by
/// <see cref="FirmwareImporter"/> (Core) off the UI thread; the result comes as one toast. RetroHub never downloads or
/// ships any of these files; key contents are never shown.
/// </summary>
public sealed partial class SettingsViewModel
{
    /// <summary>Set by the view: file picker (title) → chosen paths (empty = cancelled).</summary>
    public Func<string, Task<IReadOnlyList<string>>>? PickFiles { get; set; }
    /// <summary>Set by the view: folder picker (title) → chosen folder (null = cancelled).</summary>
    public Func<string, Task<string?>>? PickFolder { get; set; }

    /// <summary>The demo never changes files; tests switch this on to import into their scratch root.</summary>
    internal bool AllowDemoImport { get; set; }

    private readonly Dictionary<string, ServiceSetting> _firmwareRows = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<FirmwareConsole> _firmwareConsoles = [];
    private bool _importing;

    /// <summary>True while an import runs (a second one is refused).</summary>
    public bool IsImporting => _importing;

    /// <summary>The last import's result (tests, toast).</summary>
    internal FirmwareImportResult? LastImport { get; private set; }

    /// <summary>bios/import: the drop folder for the controller flow.</summary>
    public string ImportInbox => Path.Combine(Ctx.Services.Paths.Bios, "import");

    private ServiceSection BuildFirmwareSection()
    {
        try { _firmwareConsoles = FirmwareCatalog.Build(Ctx.Services.Catalog.Systems); }
        catch (Exception ex)
        {
            Log.Warn("Firmware consoles unavailable", ex);
            _firmwareConsoles = [];
        }
        var rows = new List<ServiceSetting>
        {
            ServiceSetting.Action(Loc.T("firmware.files"), Loc.T("firmware.files.hint"), Loc.T("firmware.choose"),
                () => _ = PickAndImportAsync(null, folder: false)),
            ServiceSetting.Action(Loc.T("firmware.folder"), Loc.T("firmware.folder.hint"), Loc.T("firmware.choose"),
                () => _ = PickAndImportAsync(null, folder: true)),
            ServiceSetting.Action(Loc.T("firmware.inbox"), Loc.T("firmware.inbox.hint"), Loc.T("firmware.take"),
                () => _ = ImportInboxAsync()),
        };
        _firmwareRows.Clear();
        foreach (var console in _firmwareConsoles)
        {
            ServiceSetting row;
            if (console.Kind == FirmwareConsoleKind.NoneNeeded)
                row = ServiceSetting.Choice(console.Name, NoneHint(console), [new ServiceValue(null, Loc.T("firmware.notNeeded"))], 0, _ => { });
            else
                row = ServiceSetting.Action(console.Name, console.Emulators, Loc.T("firmware.import"),
                    () => _ = PickAndImportAsync(console, folder: console.Kind == FirmwareConsoleKind.Assets));
            _firmwareRows[console.Id] = row;
            rows.Add(row);
            // the Switch firmware is a folder of .nca files: its own row with the folder picker
            if (console.Kind == FirmwareConsoleKind.Switch)
                rows.Add(ServiceSetting.Action(Loc.T("firmware.switchFolder", console.Name), Loc.T("firmware.switchFolder.hint"),
                    Loc.T("firmware.choose"), () => _ = PickAndImportAsync(console, folder: true)));
        }
        _ = RefreshFirmwareAsync();
        return new ServiceSection(Loc.T("firmware.section"), rows, Loc.T("firmware.section.note"));
    }

    private static string NoneHint(FirmwareConsole c) =>
        c.SystemIds.Contains("3ds") ? Loc.T("firmware.none.3ds") : Loc.T("firmware.none.dolphin", c.Emulators);

    private FirmwareImporter CreateImporter() =>
        new(FirmwareLocations.From(Ctx.Services.Paths, Ctx.Services.Emulators), _firmwareConsoles);

    /// <summary>Re-reads every console's state (off the UI thread) and writes it into the row descriptions.</summary>
    public async Task RefreshFirmwareAsync()
    {
        if (_firmwareConsoles.Count == 0) return;
        var statuses = await UiContext.Background(() => CreateImporter().Status(), "Firmware status").ConfigureAwait(true);
        if (statuses == null) return;
        foreach (var s in statuses)
            if (_firmwareRows.TryGetValue(s.Console.Id, out var row) && s.Console.Kind != FirmwareConsoleKind.NoneNeeded)
                row.SetHint(StatusText(s));
    }

    /// <summary>"Eden · prod.keys da, title.keys fehlt, Firmware fehlt." – names and counts only.</summary>
    internal static string StatusText(FirmwareConsoleStatus s)
    {
        var c = s.Console;
        string Join(IEnumerable<string> parts) => string.Join(Loc.T("firmware.sep"), parts);
        string body;
        switch (c.Kind)
        {
            case FirmwareConsoleKind.Switch:
                body = Join(s.Items.Select(i => i.State switch
                {
                    FirmwareItemState.Present when i.Count > 0 => Loc.T("firmware.state.presentCount", i.Name, i.Count),
                    FirmwareItemState.Present => Loc.T("firmware.state.present", i.Name),
                    FirmwareItemState.Unknown => Loc.T("firmware.state.unknown", i.Name),
                    _ => Loc.T("firmware.state.missing", i.Name),
                })) + ".";
                break;
            case FirmwareConsoleKind.WiiU:
                var keys = s.Items.FirstOrDefault()?.Count ?? 0;
                body = keys > 0 ? Loc.T("firmware.wiiu.keys", keys) : Loc.T("firmware.wiiu.none");
                break;
            case FirmwareConsoleKind.Ps2:
                body = s.Items.FirstOrDefault()?.State switch
                {
                    FirmwareItemState.Present => Loc.T("firmware.ps2.present"),
                    FirmwareItemState.Unknown => Loc.T("firmware.ps2.unknown"),
                    _ => Loc.T("firmware.ps2.missing"),
                };
                break;
            case FirmwareConsoleKind.Assets when s.Items.Any(i => i.State != FirmwareItemState.Present):
                body = Loc.T("firmware.assets.missing", string.Join(", ", s.Items.Where(i => i.State != FirmwareItemState.Present).Select(i => i.Name)));
                break;
            default:
                body = ListText(s);
                break;
        }
        // the state first (a narrow or cut line keeps what matters), then for which programs
        return c.Emulators.Length > 0 ? $"{body} {Loc.T("firmware.for", c.Emulators)}" : body;
    }

    /// <summary>Catalog BIOS and data folders: what is there, what is missing (named while short), what is unknown.</summary>
    private static string ListText(FirmwareConsoleStatus s)
    {
        var present = s.Items.Where(i => i.State == FirmwareItemState.Present).Select(i => i.Name).ToList();
        var missing = s.Items.Where(i => i.State == FirmwareItemState.Missing).Select(i => i.Name).ToList();
        var unknown = s.Items.Where(i => i.State == FirmwareItemState.Unknown).Select(i => i.Name).ToList();
        string Names(List<string> n) => n.Count <= 2 ? string.Join(", ", n) : Loc.T("firmware.files.count", n.Count);
        var parts = new List<string>();
        if (missing.Count == 0 && unknown.Count == 0) return Loc.T("firmware.list.all", Names(present));
        if (present.Count > 0) parts.Add(Loc.T("firmware.list.present", Names(present)));
        if (unknown.Count > 0) parts.Add(Loc.T("firmware.list.unknown", Names(unknown)));
        if (missing.Count > 0)
        {
            var need = s.Console.Need switch
            {
                FirmwareNeed.Optional => Loc.T("firmware.need.optional"),
                FirmwareNeed.Sometimes => Loc.T("firmware.need.sometimes"),
                _ => "",
            };
            parts.Add(Loc.T("firmware.list.missing", Names(missing)) + need);
        }
        return string.Join(" ", parts);
    }

    private bool CanImport()
    {
        if (_importing)
        {
            Ctx.Toasts.Show(Loc.T("firmware.busy"), ToastKind.Info);
            return false;
        }
        if (IsDemo && !AllowDemoImport)
        {
            Ctx.Toasts.Show(Loc.T("firmware.demo"), ToastKind.Info);
            return false;
        }
        return true;
    }

    private async Task PickAndImportAsync(FirmwareConsole? console, bool folder)
    {
        if (!CanImport()) return;
        var title = console == null ? Loc.T("firmware.pickTitle") : Loc.T("firmware.pickTitleFor", console.Name);
        IReadOnlyList<string> picked;
        try
        {
            if (folder)
            {
                var f = PickFolder == null ? null : await PickFolder(title).ConfigureAwait(true);
                picked = f == null ? [] : [f];
            }
            else picked = PickFiles == null ? [] : await PickFiles(title).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Warn("Firmware picker failed", ex);
            return;
        }
        if (picked.Count > 0) await ImportAsync(picked).ConfigureAwait(true);
    }

    private async Task ImportInboxAsync()
    {
        if (!CanImport()) return;
        var inbox = ImportInbox;
        try { Directory.CreateDirectory(inbox); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log.Warn($"Cannot create {inbox}", ex); }
        if (!Directory.Exists(inbox) || !Directory.EnumerateFileSystemEntries(inbox).Any())
        {
            Ctx.Toasts.Show(Loc.T("firmware.inbox.empty"), ToastKind.Info, Loc.T("firmware.inbox.emptyDetail"));
            ShellOpen.Folder(inbox, createIfInside: Ctx.Services.Paths.Root);
            return;
        }
        await ImportAsync([inbox]).ConfigureAwait(true);
    }

    /// <summary>Imports the paths off the UI thread, refreshes the states and shows one result toast.</summary>
    public async Task ImportAsync(IReadOnlyList<string> paths)
    {
        if (!CanImport()) return;
        _importing = true;
        OnPropertyChanged(nameof(IsImporting));
        try
        {
            Ctx.Toasts.Show(Loc.T("firmware.running"), ToastKind.Info, seconds: 2);
            var importer = CreateImporter();
            var result = await UiContext.Background(() => importer.Import(paths), "Firmware import").ConfigureAwait(true)
                         ?? FirmwareImportResult.Empty;
            LastImport = result;
            ShowResult(result);
            await RefreshFirmwareAsync().ConfigureAwait(true);
        }
        finally
        {
            _importing = false;
            OnPropertyChanged(nameof(IsImporting));
        }
    }

    private void ShowResult(FirmwareImportResult r)
    {
        var failed = r.Items.Where(i => i.Outcome == ImportOutcome.Failed).ToList();
        var done = r.Items.Where(i => i.Outcome != ImportOutcome.Failed).ToList();
        var lines = new List<string>();
        if (done.Count > 0) lines.Add(string.Join(", ", done.Select(ItemText)));
        if (r.Skipped.Count > 0)
        {
            var names = r.Skipped.Select(s => s.Reason == SkipReason.UnsupportedArchive ? Loc.T("firmware.skip.archive", s.Name) : s.Name).Distinct().ToList();
            var shown = string.Join(", ", names.Take(3));
            lines.Add(names.Count > 3 ? Loc.T("firmware.skipped.more", shown, names.Count - 3) : Loc.T("firmware.skipped", shown));
        }
        if (r.Truncated) lines.Add(Loc.T("firmware.truncated", FirmwareImporter.MaxFiles));
        var detail = string.Join("\n", lines);
        if (failed.Count > 0)
            Ctx.Toasts.Show(Loc.T("firmware.result.failed"), ToastKind.Error, detail, seconds: 8);
        else if (r.Changed)
            Ctx.Toasts.Show(Loc.T("firmware.result.done"), ToastKind.Success, detail, seconds: 7);
        else if (done.Count > 0)
            Ctx.Toasts.Show(Loc.T("firmware.result.same"), ToastKind.Info, detail, seconds: 6);
        else
            Ctx.Toasts.Show(Loc.T("firmware.result.nothing"), ToastKind.Warning, detail.Length > 0 ? detail : Loc.T("firmware.result.nothingDetail"), seconds: 8);
    }

    /// <summary>"prod.keys", "Firmware (231 Dateien)", "scph5501.bin (unbekannte Version)", "3 neue Keys".</summary>
    private static string ItemText(ImportedItem i)
    {
        var name = i.Name == "Firmware" ? Loc.T("firmware.item.firmware", i.Count)
            : i.ConsoleId == "wiiu" && i.Count > 0 ? Loc.T("firmware.item.keys", i.Count)
            : i.Name;
        return i.Outcome switch
        {
            ImportOutcome.AlreadyPresent => Loc.T("firmware.outcome.same", name),
            ImportOutcome.KeptExisting => Loc.T("firmware.outcome.kept", name),
            ImportOutcome.ImportedUnknownVersion => Loc.T("firmware.outcome.unknown", name),
            ImportOutcome.Replaced => Loc.T("firmware.outcome.replaced", name),
            _ => name,
        };
    }
}
