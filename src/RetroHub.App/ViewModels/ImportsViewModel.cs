using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetroHub.App.Localization;
using RetroHub.App.Services;
using RetroHub.Core;
using RetroHub.Core.Library;
using RetroHub.Core.Logging;

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
public sealed record IssueGroup(string Title, IReadOnlyList<ScanIssue> Items, int Total, bool IsError)
{
    public bool HasMore => Total > Items.Count;
    public string MoreText => Loc.T("imports.moreIssues", Total - Items.Count);
}

/// <summary>Library folders and scanning (progress + the last <see cref="ScanReport"/>).</summary>
public sealed partial class ImportsViewModel : PageViewModel
{
    private CancellationTokenSource? _scanCts;

    public ImportsViewModel(UiContext ctx) : base(ctx)
    {
        Title = Loc.T("nav.imports");
        ctx.LibraryChanged += () => { if (!IsScanning) LoadFolders(); };
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

    public override void OnNavigatedTo(object? argument, bool isBack)
    {
        Title = Loc.T("nav.imports");
        LoadFolders();
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

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task ScanAsync()
    {
        if (Ctx.Services.Scanner.IsScanning)
        {
            Ctx.Toasts.Show(Loc.T("imports.alreadyScanning"), ToastKind.Warning);
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
        try
        {
            var report = await Task.Run(() => scanner.ScanAsync(options, progress, ct), ct).ConfigureAwait(true);
            ShowReport(report);
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
    }

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
