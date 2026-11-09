using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetroHub.App.Localization;
using RetroHub.App.Services;
using RetroHub.Core;
using RetroHub.Core.Library;
using RetroHub.Core.Logging;
using RetroHub.Core.Metadata;

namespace RetroHub.App.ViewModels;

public sealed class FolderItemViewModel(LibraryFolder folder, AppServices services)
{
    public LibraryFolder Folder { get; } = folder;
    public string PathText { get; } = folder.IsBuiltIn ? folder.Path + "/" : folder.Path;
    public string FullPath { get; } = services.Paths.FromPortable(folder.Path);
    public string SystemText { get; } = folder.SystemId == null
        ? Loc.T("imports.detectAuto")
        : Loc.T("imports.fixedSystem", services.Catalog.GetSystem(folder.SystemId)?.Name ?? folder.SystemId);
    public string ScanText { get; } = folder.LastScanUtc == null ? Loc.T("imports.neverScanned") : Loc.T("imports.lastScan", Fmt.LastPlayed(folder.LastScanUtc));
    public string OptionsText { get; } = folder.Recursive ? Loc.T("imports.recursive") : Loc.T("imports.topLevelOnly");
    public bool IsBuiltIn => Folder.IsBuiltIn;
    public bool CanRemove => !Folder.IsBuiltIn;
    public bool IsEnabled => Folder.Enabled;
    public string BadgeText { get; } = folder.IsBuiltIn ? Loc.T("imports.builtIn") : folder.Enabled ? Loc.T("imports.active") : Loc.T("imports.inactive");
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
            Folders = Ctx.Services.Games.Folders().OrderByDescending(f => f.IsBuiltIn).ThenBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
                .Select(f => new FolderItemViewModel(f, Ctx.Services)).ToList();
        }
        catch (Exception ex)
        {
            Log.Error("Loading library folders failed", ex);
        }
    }

    private bool CanScan() => !IsScanning;

    partial void OnIsScanningChanged(bool value)
    {
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
        IsScanning = true;
        IsIndeterminate = true;
        Progress = 0;
        ProgressText = Loc.T("imports.preparing");
        CurrentFile = "";
        _scanCts = new CancellationTokenSource();
        var progress = new Progress<ScanProgress>(p =>
        {
            IsIndeterminate = p.FilesTotal <= 0;
            Progress = p.FilesTotal > 0 ? 100.0 * p.FilesSeen / p.FilesTotal : 0;
            ProgressText = p.FilesTotal > 0 ? Loc.T("imports.progress", PhaseText(p.Phase), p.FilesSeen, p.FilesTotal) : PhaseText(p.Phase);
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
            if (!quiet || report.Added > 0 || report.Errors.Count > 0)
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

    private void ShowReport(ScanReport r)
    {
        Report = r;
        HasReport = true;
        ReportTitle = Loc.T("imports.reportTitle", Fmt.DateTimeText(DateTime.UtcNow), r.Duration.TotalSeconds.ToString("0.0", Loc.Culture));
        var groups = new List<IssueGroup>();
        if (r.Duplicates.Count > 0) groups.Add(new IssueGroup(Loc.T("imports.duplicates"), r.Duplicates.Take(50).ToList(), r.Duplicates.Count, false));
        if (r.Unknown.Count > 0) groups.Add(new IssueGroup(Loc.T("imports.unknown"), r.Unknown.Take(50).ToList(), r.Unknown.Count, false));
        if (r.Errors.Count > 0) groups.Add(new IssueGroup(Loc.T("imports.errors"), r.Errors.Take(50).ToList(), r.Errors.Count, true));
        Issues = groups;
        ReportClean = groups.Count == 0;
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

    [RelayCommand]
    private void ToggleFolder(FolderItemViewModel? item)
    {
        if (item == null || item.IsBuiltIn) return;
        try
        {
            item.Folder.Enabled = !item.Folder.Enabled;
            Ctx.Services.Games.UpsertFolder(item.Folder);
            LoadFolders();
        }
        catch (Exception ex) { Log.Error("Updating a library folder failed", ex); }
    }

    [RelayCommand]
    private void OpenFolder(FolderItemViewModel? item)
    {
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
