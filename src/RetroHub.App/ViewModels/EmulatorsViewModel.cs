using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetroHub.App.Input;
using RetroHub.App.Localization;
using RetroHub.App.Services;
using RetroHub.Core.Catalog;
using RetroHub.Core.Emulation;

namespace RetroHub.App.ViewModels;

/// <summary>One emulator row: install state, version, path, systems, modes.</summary>
public sealed class EmulatorItemViewModel
{
    public EmulatorItemViewModel(EmulatorInstall install, AppServices services)
    {
        Install = install;
        var def = install.Def;
        IsInstalled = services.IsInstalled(install);
        Name = def.Name;
        Initial = def.Name.Length > 0 ? def.Name[..1].ToUpperInvariant() : "?";
        KindText = def.Kind switch
        {
            EmulatorKind.RetroArch => Loc.T("emulators.kind.retroarch"),
            EmulatorKind.Native => Loc.T("emulators.kind.native"),
            _ => Loc.T("emulators.kind.standalone"),
        };
        StatusText = IsInstalled ? Loc.T("emulators.installed") : Loc.T("emulators.missing");
        VersionText = install.Version is { } v ? Loc.T("emulators.version", v) : IsInstalled ? Loc.T("emulators.versionUnknown") : "";
        PathText = install.ExePath != null ? services.Paths.ToPortable(install.ExePath) : Loc.T("emulators.expectedIn", "emulators/" + def.Id + "/");
        if (install.UserConfigured) PathText += " · " + Loc.T("emulators.userPath");
        Systems = def.Systems.Select(id => services.Catalog.GetSystem(id) is { } s ? (string.IsNullOrEmpty(s.ShortName) ? s.Name : s.ShortName) : id).ToList();
        ModesText = string.Join(" · ", def.Modes.Select(Fmt.Mode));
        CoresText = def.Kind == EmulatorKind.RetroArch
            ? (install.InstalledCores.Count > 0 ? Loc.T("emulators.cores", string.Join(", ", install.InstalledCores)) : Loc.T("emulators.noCores"))
            : "";
        HasCores = CoresText.Length > 0;
        Folder = install.ExePath != null ? Path.GetDirectoryName(install.ExePath) : Path.Combine(services.Paths.Emulators, def.Id);
        DownloadUrl = def.DownloadUrl ?? def.Website;
        Notes = def.Notes;
        SupportsEmbedding = def.Modes.Contains(LaunchMode.Embedded);
    }

    public EmulatorInstall Install { get; }
    public string Name { get; }
    public string Initial { get; }
    public string KindText { get; }
    public bool IsInstalled { get; }
    public bool IsMissing => !IsInstalled;
    public string StatusText { get; }
    public string VersionText { get; }
    public string PathText { get; }
    public IReadOnlyList<string> Systems { get; }
    public string ModesText { get; }
    public string CoresText { get; }
    public bool HasCores { get; }
    public string? Folder { get; }
    public string? DownloadUrl { get; }
    public bool HasDownload => !string.IsNullOrEmpty(DownloadUrl);
    public string? Notes { get; }
    public bool SupportsEmbedding { get; }
}

/// <summary>All emulators from <see cref="IEmulatorRegistry.All"/>: installed / missing.</summary>
public sealed partial class EmulatorsViewModel : PageViewModel
{
    public EmulatorsViewModel(UiContext ctx) : base(ctx)
    {
        Title = Loc.T("nav.emulators");
        ctx.EmulatorsChanged += Load;
    }

    public override PageKey Key => PageKey.Emulators;

    [ObservableProperty] public partial IReadOnlyList<EmulatorItemViewModel> Items { get; private set; } = [];
    [ObservableProperty] public partial string SummaryText { get; private set; } = "";
    [ObservableProperty] public partial bool IsRefreshing { get; private set; }

    public override void OnNavigatedTo(object? argument, bool isBack)
    {
        Title = Loc.T("nav.emulators");
        Load();
    }

    private void Load()
    {
        var all = Ctx.Services.Emulators.All.Select(i => new EmulatorItemViewModel(i, Ctx.Services))
            .OrderByDescending(i => i.IsInstalled).ThenBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        Items = all;
        SummaryText = Loc.T("emulators.summary", all.Count(i => i.IsInstalled), all.Count);
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

    [RelayCommand] private void OpenEmulatorsFolder() => OpenFolderPath(Ctx.Services.Paths.Emulators);

    private void OpenFolderPath(string path)
    {
        if (!ShellOpen.Folder(path, createIfInside: Ctx.Services.Paths.Root))
            Ctx.Toasts.Show(Loc.T("common.folderMissing", path), ToastKind.Warning);
    }
}
