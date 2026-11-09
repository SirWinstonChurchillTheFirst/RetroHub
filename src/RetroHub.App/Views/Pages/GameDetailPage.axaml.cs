using Avalonia.Controls;
using Avalonia.Platform.Storage;
using RetroHub.App.Localization;
using RetroHub.App.ViewModels;

namespace RetroHub.App.Views.Pages;

public partial class GameDetailPage : PageBase
{
    public GameDetailPage()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is GameDetailViewModel vm) vm.PickImage = PickImageAsync;
        };
    }

    /// <summary>File picker for "Cover wählen…" (PNG/JPG/WebP).</summary>
    private async Task<string?> PickImageAsync()
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage == null || !storage.CanOpen) return null;
        var result = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Loc.T("artwork.pickTitle"),
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(Loc.T("artwork.pickFilter")) { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp"] },
            ],
        });
        return result.Count > 0 ? result[0].TryGetLocalPath() : null;
    }

    public override bool FocusDefault() => FocusVisible(PlayButton) || FocusVisible(EmulatorBox);
}
