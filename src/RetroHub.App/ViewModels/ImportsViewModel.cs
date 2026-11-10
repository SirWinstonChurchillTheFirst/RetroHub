using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetroHub.App.Input;
using RetroHub.App.Localization;
using RetroHub.App.Services;
using RetroHub.Core;
using RetroHub.Core.Library;
using RetroHub.Core.Logging;
using RetroHub.Core.Metadata;

namespace RetroHub.App.ViewModels;

/// <summary>
/// A library folder as a service-menu row (DESIGN 7.6 Importe): the path is the label, one meta line says how it is
/// read, the value is its state "◀ Aktiv ▶" / "◀ Pausiert ▶" (left/right switch it at once); confirm opens its
/// "Mehr" list. The built-in games/ folder is always active (no arrows).
/// </summary>
public sealed partial class FolderItemViewModel : ObservableObject
{
    public FolderItemViewModel(LibraryFolder folder, AppServices services, Action<FolderItemViewModel, bool> setEnabled, Action<FolderItemViewModel> openMenu)
    {
        Folder = folder;
        PathText = folder.IsBuiltIn ? folder.Path + "/" : folder.Path;
        FullPath = services.Paths.FromPortable(folder.Path);
        SystemText = folder.SystemId == null
            ? Loc.T("imports.detectAuto")
            : Loc.T("imports.fixedSystem", services.Catalog.GetSystem(folder.SystemId)?.Name ?? folder.SystemId);
        ScanText = folder.LastScanUtc == null ? Loc.T("imports.neverScanned") : Loc.T("imports.lastScan", Fmt.LastPlayed(folder.LastScanUtc));
        OptionsText = folder.Recursive ? Loc.T("imports.recursive") : Loc.T("imports.topLevelOnly");
        // "Durchsucht vor 2 Tagen, mit Unterordnern, System automatisch erkennen." – one line, no chips; the time first,
        // so a narrow row (≈ 1100) cuts the least important part
        var hint = $"{Fmt.SentenceStart(ScanText)}, {OptionsText}, {(folder.SystemId == null ? SystemText : Loc.T("imports.fixedSystem.inSentence", services.Catalog.GetSystem(folder.SystemId)?.Name ?? folder.SystemId))}.";
        Row = folder.IsBuiltIn
            ? ServiceSetting.Choice(PathText, hint, [new ServiceValue(true, Loc.T("imports.alwaysActive"))], 0, _ => { })
            : ServiceSetting.Choice(PathText, hint,
                [new ServiceValue(true, Loc.T("imports.active")), new ServiceValue(false, Loc.T("imports.inactive"))],
                folder.Enabled ? 0 : 1, v => setEnabled(this, (bool)v.Value!));
        Row.ActivateOverride = () => openMenu(this);
    }

    public LibraryFolder Folder { get; }
    public ServiceSetting Row { get; }
    public string PathText { get; }
    public string FullPath { get; }
    public string SystemText { get; }
    public string ScanText { get; }
    public string OptionsText { get; }
    public bool IsBuiltIn => Folder.IsBuiltIn;
    public bool CanRemove => !Folder.IsBuiltIn;
    public bool IsEnabled => Folder.Enabled;
    /// <summary>"Pausieren" / "Aktivieren" in the Mehr list.</summary>
    public string ToggleText => Folder.Enabled ? Loc.T("imports.pause") : Loc.T("imports.resume");
    /// <summary>Its "Mehr" list is folded open under the row (the row keeps its focus look meanwhile).</summary>
    [ObservableProperty] public partial bool IsMenuOpen { get; set; }
}

/// <summary>
/// The delivery note of the last scan (DESIGN 7.6 "Lieferschein", replaces the result toast): lines in Etikett 18 with
/// the numbers in the Zahl role on one right-aligned axis. It stands until the next scan begins.
/// </summary>
public sealed record DeliveryLine(int Count, string Text)
{
    public string CountText => Count.ToString(Loc.Culture);
    public bool IsZero => Count == 0;
}

/// <summary>One group of scan issues (duplicates / unknown / errors).</summary>
/// <summary>Navigation arguments for the imports page.</summary>
public enum ImportsRequest
{
    /// <summary>Open the folder picker on arrival (empty Start: "Ordner hinzufügen").</summary>
    AddFolder,
}

public sealed record IssueGroup(string Title, IReadOnlyList<ScanIssue> Items, int Total, bool IsError)
{
    public bool HasMore => Total > Items.Count;
    public string MoreText => Loc.T("imports.moreIssues", Total - Items.Count);
}

/// <summary>Library folders and scanning (progress + the last <see cref="ScanReport"/>).</summary>
public sealed partial class ImportsViewModel : PageViewModel
{
    private CancellationTokenSource? _scanCts;
    private CancellationTokenSource? _coverCts;
    private bool _coverRerunPending;

    public ImportsViewModel(UiContext ctx) : base(ctx)
    {
        Title = Loc.T("nav.imports");
        ctx.LibraryChanged += () => { if (!IsScanning) LoadFolders(); };
        ctx.Services.Settings.Saved += _ => Avalonia.Threading.Dispatcher.UIThread.Post(UpdateCoverHint);
        UpdateCoverHint();
    }

    public override PageKey Key => PageKey.Imports;

    [ObservableProperty] public partial IReadOnlyList<FolderItemViewModel> Folders { get; private set; } = [];
    [ObservableProperty] public partial bool IsScanning { get; private set; }
    [ObservableProperty] public partial double Progress { get; private set; }
    [ObservableProperty] public partial bool IsIndeterminate { get; private set; }
    [ObservableProperty] public partial string ProgressText { get; private set; } = "";
    [ObservableProperty] public partial string CurrentFile { get; private set; } = "";
    [ObservableProperty] public partial ScanReport? Report { get; private set; }
    [ObservableProperty] public partial bool HasReport { get; private set; }
    [ObservableProperty] public partial string ReportTitle { get; private set; } = "";
    [ObservableProperty] public partial IReadOnlyList<IssueGroup> Issues { get; private set; } = [];
    [ObservableProperty] public partial bool ReportClean { get; private set; }
    /// <summary>The lines of the delivery note ("412 Dateien geprüft", "12 neu" …).</summary>
    [ObservableProperty] public partial IReadOnlyList<DeliveryLine> Delivery { get; private set; } = [];
    [ObservableProperty] public partial int IssueCount { get; private set; }
    public bool HasIssues => IssueCount > 0;
    partial void OnIssueCountChanged(int value) => OnPropertyChanged(nameof(HasIssues));
    /// <summary>"3 Hinweise ›" folds the notes open under the delivery note.</summary>
    [ObservableProperty] public partial bool IssuesOpen { get; set; }
    [ObservableProperty] public partial string IssuesText { get; private set; } = "";
    /// <summary>"Dauer 2,1 s" at the foot of the delivery note.</summary>
    [ObservableProperty] public partial string ReportFoot { get; private set; } = "";
    /// <summary>When the folders were last read ("Zuletzt gescannt vor 3 Stunden.") or "Noch nie gescannt.".</summary>
    [ObservableProperty] public partial string LastScanText { get; private set; } = "";

    /// <summary>The folder whose "Mehr" list is open (null = none).</summary>
    [ObservableProperty] public partial FolderItemViewModel? MenuFolder { get; private set; }
    public bool IsMenuOpen => MenuFolder != null;
    partial void OnMenuFolderChanged(FolderItemViewModel? oldValue, FolderItemViewModel? newValue)
    {
        // the list folds open under its own row (M6 QA: a floating list over the dimmed page looked like another row's)
        if (oldValue != null) oldValue.IsMenuOpen = false;
        if (newValue != null) newValue.IsMenuOpen = true;
        OnPropertyChanged(nameof(IsMenuOpen));
        RaiseHintsChanged();
    }

    /// <summary>The scan column's label: "Letzter Scan", while a scan runs "Scan läuft".</summary>
    public string ScanLabel => IsScanning ? Loc.T("imports.scanRunning") : Loc.T("imports.lastReport");
    /// <summary>"Zuletzt gescannt heute." – while a scan runs "Gestartet um 19:53.".</summary>
    public string ScanSentence => IsScanning ? Loc.T("imports.startedAt", _scanStarted.ToLocalTime().ToString("t", Loc.Culture)) : LastScanText;
    partial void OnLastScanTextChanged(string value) => OnPropertyChanged(nameof(ScanSentence));
    private DateTime _scanStarted = DateTime.UtcNow;

    /// <summary>The games the last scan brought in, newest first (at most 12), as small spines under the delivery note.</summary>
    [ObservableProperty] public partial IReadOnlyList<GameItemViewModel> NewGames { get; private set; } = [];
    public bool HasNewGames => NewGames.Count > 0;
    partial void OnNewGamesChanged(IReadOnlyList<GameItemViewModel> value) => OnPropertyChanged(nameof(HasNewGames));

    // ---- covers (ArtworkService) ----
    [ObservableProperty] public partial bool IsFindingCovers { get; private set; }
    [ObservableProperty] public partial double CoverProgress { get; private set; }
    [ObservableProperty] public partial bool CoverIndeterminate { get; private set; }
    [ObservableProperty] public partial string CoverProgressText { get; private set; } = "";
    [ObservableProperty] public partial string CoverCurrent { get; private set; } = "";
    [ObservableProperty] public partial string CoverResultText { get; private set; } = "";
    [ObservableProperty] public partial bool HasCoverResult { get; private set; }
    [ObservableProperty] public partial string CoverOnlineText { get; private set; } = "";
    [ObservableProperty] public partial ArtworkReport? LastCoverReport { get; private set; }
    public bool CoversAvailable => Ctx.Services.Artwork != null;
    public bool CoversUnavailable => !CoversAvailable;

    public override IReadOnlyList<HintItem> Hints => IsMenuOpen
        ? [new(NavAction.Accept, Loc.T("hint.select")), new(NavAction.Back, Loc.T("hint.close"))]
        : [new(NavAction.Accept, Loc.T("hint.select")), new(NavAction.Back, Loc.T("hint.back"))];

    public override bool HandleAction(NavAction action)
    {
        if (!IsMenuOpen) return false;
        if (action == NavAction.Back) MenuFolder = null;
        // the list is modal: shoulders, favourite, search do nothing behind it
        return true;
    }

    public override void OnNavigatedFrom()
    {
        base.OnNavigatedFrom();
        MenuFolder = null;
    }

    public override void OnNavigatedTo(object? argument, bool isBack)
    {
        Title = Loc.T("nav.imports");
        LoadFolders();
        UpdateCoverHint();
        // "Ordner hinzufügen" on an empty Start: open the folder picker right away
        if (argument is ImportsRequest.AddFolder && !isBack)
            Avalonia.Threading.Dispatcher.UIThread.Post(() => _ = AddFolderAsync(), Avalonia.Threading.DispatcherPriority.Background);
    }

    private void LoadFolders()
    {
        try
        {
            var folders = Ctx.Services.Games.Folders();
            Folders = folders.OrderByDescending(f => f.IsBuiltIn).ThenBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
                .Select(f => new FolderItemViewModel(f, Ctx.Services, SetFolderEnabled, item => MenuFolder = item)).ToList();
            var last = folders.Where(f => f.Enabled).Max(f => f.LastScanUtc);
            var now = DateTime.UtcNow;
            LastScanText = last is not { } at ? Loc.T("imports.never")
                : Loc.T("imports.lastSentence", (now.ToLocalTime().Date - at.ToLocalTime().Date).Days < 30
                    ? Fmt.LastPlayedInSentence(at, now)
                    : Loc.T("system.sentence.on", Fmt.DateInSentence(at, now)));
        }
        catch (Exception ex)
        {
            Log.Error("Loading library folders failed", ex);
        }
    }

    private bool CanScan() => !IsScanning;

    partial void OnIsScanningChanged(bool value)
    {
        OnPropertyChanged(nameof(ScanLabel));
        OnPropertyChanged(nameof(ScanSentence));
        ScanCommand.NotifyCanExecuteChanged();
        CancelScanCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsFindingCoversChanged(bool value)
    {
        FindCoversCommand.NotifyCanExecuteChanged();
        CancelCoversCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanScan))]
    private Task ScanAsync() => RunScanAsync(quiet: false);

    /// <summary>
    /// The scan behind "Jetzt scannen" and the start-up scan (setting "Beim Start scannen"). A quiet scan only reports
    /// when it found something new or failed, so a routine start stays silent.
    /// </summary>
    public async Task RunScanAsync(bool quiet)
    {
        if (IsScanning) return;
        if (Ctx.Services.Scanner.IsScanning)
        {
            if (!quiet) Ctx.Toasts.Show(Loc.T("imports.alreadyScanning"), ToastKind.Warning);
            return;
        }
        _scanStarted = DateTime.UtcNow;
        IsScanning = true;
        IsIndeterminate = true;
        // the delivery note of the last scan stands until the next one begins
        HasReport = false;
        IssuesOpen = false;
        Progress = 0;
        ProgressText = Loc.T("imports.preparing");
        CurrentFile = "";
        _scanCts = new CancellationTokenSource();
        var progress = new Progress<ScanProgress>(p =>
        {
            IsIndeterminate = p.FilesTotal <= 0;
            Progress = p.FilesTotal > 0 ? 100.0 * p.FilesSeen / p.FilesTotal : 0;
            ProgressText = p.FilesTotal > 0 ? Loc.T(p.Phase switch { "hashes" => "imports.progress.hashes", "database" => "imports.progress.database", _ => "imports.progress.files" }, p.FilesSeen, p.FilesTotal) : PhaseText(p.Phase) + ".";
            CurrentFile = p.CurrentFile ?? "";
        });
        var options = new ScanOptions { ComputeHashes = Ctx.Services.Settings.Current.ComputeHashes };
        var scanner = Ctx.Services.Scanner;
        var ct = _scanCts.Token;
        var scanned = false;
        try
        {
            var report = await Task.Run(() => scanner.ScanAsync(options, progress, ct), ct).ConfigureAwait(true);
            ShowReport(report);
            scanned = true;
            // the delivery note replaces the result toast; a toast only reaches the user who is elsewhere (start-up scan)
            if (Ctx.Navigator.Current != PageKey.Imports && (!quiet || report.Added > 0 || report.Errors.Count > 0))
                Ctx.Toasts.Show(Loc.T("imports.done"), ToastKind.Success, Loc.T("imports.doneDetail", report.Added, report.Updated, report.Missing));
        }
        catch (OperationCanceledException)
        {
            Ctx.Toasts.Show(Loc.T("imports.cancelled"), ToastKind.Info);
        }
        catch (Exception ex)
        {
            Log.Error("Library scan failed", ex);
            Ctx.Toasts.Show(Loc.T("imports.failed"), ToastKind.Error, ex.Message);
        }
        finally
        {
            IsScanning = false;
            _scanCts?.Dispose();
            _scanCts = null;
            LoadFolders();
        }
        // every successful scan is followed by a quiet cover lookup (new games get their covers right away)
        // (not awaited: the scan button must not stay disabled while covers are looked up)
        if (scanned) _ = RunCoverSearchAsync(quiet: true);
    }

    private bool CanFindCovers() => !IsFindingCovers && CoversAvailable;

    [RelayCommand(CanExecute = nameof(CanFindCovers))]
    private Task FindCoversAsync() => RunCoverSearchAsync(quiet: false);

    [RelayCommand(CanExecute = nameof(IsFindingCovers))]
    private void CancelCovers() => _coverCts?.Cancel();

    /// <summary>
    /// "Cover suchen": artwork for every game without a cover (local folders first, then libretro-thumbnails when the
    /// setting allows it). The quiet variant (after a scan) shows no toasts, only the result line on this page.
    /// </summary>
    public async Task RunCoverSearchAsync(bool quiet)
    {
        var artwork = Ctx.Services.Artwork;
        if (artwork == null) return;
        if (IsFindingCovers || artwork.IsRunning)
        {
            // a scan finished while covers are being looked up: its new games may not be part of the running
            // pass (the library was read at its start) → one more quiet pass once the current one is done
            if (quiet) _coverRerunPending = true;
            else Ctx.Toasts.Show(Loc.T("artwork.busy"), ToastKind.Info);
            return;
        }
        _coverRerunPending = false;
        IsFindingCovers = true;
        CoverIndeterminate = true;
        CoverProgress = 0;
        CoverProgressText = Loc.T("artwork.preparing");
        CoverCurrent = "";
        _coverCts = new CancellationTokenSource();
        var ct = _coverCts.Token;
        var progress = new Progress<ArtworkProgress>(p =>
        {
            CoverIndeterminate = p.Total <= 0;
            CoverProgress = p.Total > 0 ? 100.0 * p.Done / p.Total : 0;
            CoverProgressText = Loc.T(p.Phase == "online" ? "artwork.progressOnline" : "artwork.progressLocal", p.Done, p.Total);
            CoverCurrent = p.CurrentTitle ?? "";
        });
        try
        {
            var report = await Task.Run(() => artwork.ResolveAsync(null, progress, ct), ct).ConfigureAwait(true);
            LastCoverReport = report;
            CoverResultText = Loc.T("artwork.lastResult", report.LocalFound, report.Downloaded, report.NotFound);
            HasCoverResult = true;
            if (report.LocalFound + report.Downloaded > 0) AsyncImageLoader.Shared.ResetFailures();
            if (!quiet)
                Ctx.Toasts.Show(Loc.T("artwork.done"), report.Errors > 0 ? ToastKind.Warning : ToastKind.Success,
                    Loc.T("artwork.doneDetail", report.LocalFound, report.Downloaded, report.NotFound)
                    + (report.Errors > 0 ? " " + Loc.T("artwork.errors", report.Errors) : ""));
        }
        catch (OperationCanceledException)
        {
            _coverRerunPending = false; // the user cancelled: do not start again on our own
            if (!quiet) Ctx.Toasts.Show(Loc.T("artwork.cancelled"), ToastKind.Info);
        }
        catch (Exception ex)
        {
            Log.Error("Cover search failed", ex);
            if (!quiet) Ctx.Toasts.Show(Loc.T("artwork.failed"), ToastKind.Error, ex.Message);
        }
        finally
        {
            IsFindingCovers = false;
            _coverCts?.Dispose();
            _coverCts = null;
        }
        if (_coverRerunPending)
        {
            _coverRerunPending = false;
            await RunCoverSearchAsync(quiet: true);
        }
    }

    private void UpdateCoverHint() =>
        CoverOnlineText = CoversAvailable
            ? Loc.T(Ctx.Services.Settings.Current.OnlineCovers ? "artwork.onlineOn" : "artwork.onlineOff")
            : Loc.T("artwork.unavailable");

    [RelayCommand(CanExecute = nameof(IsScanning))]
    private void CancelScan() => _scanCts?.Cancel();

    /// <summary>Entries the opened "Hinweise" show in all (QA 2).</summary>
    public const int MaxIssueLines = 3;

    private void ShowReport(ScanReport r)
    {
        Report = r;
        HasReport = true;
        ReportTitle = Loc.T("imports.reportTitle", Fmt.DateTimeText(DateTime.UtcNow), r.Duration.TotalSeconds.ToString("0.0", Loc.Culture));
        var groups = new List<IssueGroup>();
        // "1 doppelte Datei", "2 nicht zugeordnet", "1 Fehler" – the number leads, no "A · 1" chain (DESIGN 3.2)
        string Head(string key, int n) => Loc.T(n == 1 ? key + ".one" : key, n);
        // QA 2: the opened notes show at most three entries in all (errors first), each group closing with "+ n weitere",
        // so the slip never pushes the cover section under the footer
        var budget = MaxIssueLines;
        IssueGroup Group(string key, IEnumerable<ScanIssue> items, bool error)
        {
            var all = items.ToList();
            var shown = all.Take(budget).ToList();
            budget -= shown.Count;
            return new IssueGroup(Head(key, all.Count), shown, all.Count, error);
        }
        var errors = r.Errors.Count > 0 ? Group("imports.issues.errors", r.Errors, true) : null;
        if (r.Duplicates.Count > 0) groups.Add(Group("imports.issues.duplicates", r.Duplicates, false));
        if (r.Unknown.Count > 0) groups.Add(Group("imports.issues.unknown", r.Unknown, false));
        if (errors != null) groups.Add(errors);
        Issues = groups;
        ReportClean = groups.Count == 0;
        var files = r.Added + r.Updated + r.Unchanged + r.Duplicates.Count + r.Unknown.Count + r.Errors.Count;
        Delivery =
        [
            new(files, Loc.T(files == 1 ? "imports.note.file" : "imports.note.files")),
            new(r.Added, Loc.T("imports.note.added")),
            new(r.Updated, Loc.T("imports.note.updated")),
            new(r.Missing, Loc.T(r.Missing == 1 ? "imports.note.missingOne" : "imports.note.missing")),
        ];
        IssueCount = r.Duplicates.Count + r.Unknown.Count + r.Errors.Count;
        IssuesText = Loc.T(IssueCount == 1 ? "imports.note.issue" : "imports.note.issues");
        ReportTitle = Fmt.DateTimeText(DateTime.UtcNow);
        ReportFoot = Loc.T("imports.note.duration", r.Duration.TotalSeconds.ToString("0.0", Loc.Culture));
        NewGames = [];
        if (r.Added > 0)
        {
            try
            {
                var since = _scanStarted.AddSeconds(-1);
                NewGames = Ctx.Services.Games.Query(new GameQuery { Sort = GameSort.Added })
                    .Where(g => g.AddedUtc >= since).OrderByDescending(g => g.AddedUtc).Take(12).Select(Ctx.Items.Get).ToList();
            }
            catch (Exception ex) { Log.Warn("Reading the new games of the scan failed", ex); }
        }
    }

    private static string PhaseText(string phase) => phase switch
    {
        "folders" => Loc.T("imports.phase.folders"),
        "files" => Loc.T("imports.phase.files"),
        "hashes" => Loc.T("imports.phase.hashes"),
        "database" => Loc.T("imports.phase.database"),
        _ => phase,
    };

    /// <summary>Adds a folder chosen by the user (the view shows the folder picker).</summary>
    public void AddFolder(string fullPath)
    {
        try
        {
            if (!Directory.Exists(fullPath))
            {
                Ctx.Toasts.Show(Loc.T("common.folderMissing", fullPath), ToastKind.Warning);
                return;
            }
            var portable = Ctx.Services.Paths.ToPortable(fullPath);
            if (Ctx.Services.Games.Folders().Any(f => string.Equals(Ctx.Services.Paths.FromPortable(f.Path), Path.GetFullPath(fullPath), StringComparison.OrdinalIgnoreCase)))
            {
                Ctx.Toasts.Show(Loc.T("imports.folderExists"), ToastKind.Info);
                return;
            }
            var system = Ctx.Services.Catalog.SystemForFolder(Path.GetFileName(fullPath.TrimEnd('\\', '/')));
            Ctx.Services.Games.UpsertFolder(new LibraryFolder { Path = portable, SystemId = system?.Id, Recursive = true, Enabled = true });
            LoadFolders();
            Ctx.Toasts.Show(Loc.T("imports.folderAdded"), ToastKind.Success, Loc.T("imports.folderAddedDetail"));
        }
        catch (Exception ex)
        {
            Log.Error("Adding a library folder failed", ex);
            Ctx.Toasts.Show(Loc.T("imports.folderAddFailed"), ToastKind.Error);
        }
    }

    [RelayCommand]
    private async Task RemoveFolderAsync(FolderItemViewModel? item)
    {
        MenuFolder = null;
        if (item == null || item.IsBuiltIn) return;
        var ok = await Ctx.Dialogs.ConfirmAsync(Loc.T("imports.remove.title"), Loc.T("imports.remove.message", item.PathText), Loc.T("imports.remove.confirm"));
        if (!ok) return;
        try
        {
            Ctx.Services.Games.DeleteFolder(item.Folder.Id);
            LoadFolders();
        }
        catch (Exception ex)
        {
            Log.Error("Removing a library folder failed", ex);
            Ctx.Toasts.Show(Loc.T("imports.folderRemoveFailed"), ToastKind.Error);
        }
    }

    /// <summary>"◀ Aktiv ▶" / "◀ Pausiert ▶": stored at once; the row keeps its focus (no reload of the list).</summary>
    private void SetFolderEnabled(FolderItemViewModel item, bool enabled)
    {
        if (item.IsBuiltIn || item.Folder.Enabled == enabled) return;
        try
        {
            item.Folder.Enabled = enabled;
            Ctx.Services.Games.UpsertFolder(item.Folder);
        }
        catch (Exception ex)
        {
            Log.Error("Updating a library folder failed", ex);
            item.Folder.Enabled = !enabled;
            item.Row.Show(item.Folder.Enabled ? 0 : 1);
        }
    }

    /// <summary>"Pausieren" / "Aktivieren" in the Mehr list (the same as left/right on the row).</summary>
    [RelayCommand]
    private void ToggleFolder(FolderItemViewModel? item)
    {
        MenuFolder = null;
        if (item == null || item.IsBuiltIn) return;
        item.Row.Step(+1);
    }

    [RelayCommand] private void CloseMenu() => MenuFolder = null;

    [RelayCommand] private void ToggleIssues() => IssuesOpen = !IssuesOpen;

    [RelayCommand]
    private void OpenFolder(FolderItemViewModel? item)
    {
        MenuFolder = null;
        if (item != null && !ShellOpen.Folder(item.FullPath, createIfInside: Ctx.Services.Paths.Games))
            Ctx.Toasts.Show(Loc.T("common.folderMissing", item.FullPath), ToastKind.Warning);
    }

    /// <summary>Set by the view: shows the folder picker and returns the chosen path (null = cancelled).</summary>
    public Func<Task<string?>>? PickFolder { get; set; }

    [RelayCommand]
    private async Task AddFolderAsync()
    {
        if (PickFolder == null) return;
        string? path;
        try { path = await PickFolder(); }
        catch (Exception ex) { Log.Warn("Folder picker failed", ex); return; }
        if (path != null) AddFolder(path);
    }
}
