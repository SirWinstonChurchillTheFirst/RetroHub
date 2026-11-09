using Avalonia.Controls;
using Avalonia.Platform.Storage;
using RetroHub.App.Localization;
using RetroHub.App.ViewModels;

namespace RetroHub.App.Views.Pages;

public partial class ImportsPage : PageBase
{
    public ImportsPage()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is ImportsViewModel vm) vm.PickFolder = PickFolderAsync;
        };
    }

    private async Task<string?> PickFolderAsync()
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage == null || !storage.CanPickFolder) return null;
        var result = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = Loc.T("imports.pickFolder"),
            AllowMultiple = false,
        });
        return result.Count > 0 ? result[0].TryGetLocalPath() : null;
    }

    public override bool FocusDefault() => FocusVisible(ScanButton);
}
