using Avalonia.Controls;
using Avalonia.Platform.Storage;
using RetroHub.App.Localization;
using RetroHub.App.ViewModels;

namespace RetroHub.App.Views.Pages;

public partial class GameDetailPage : PageBase
{
    private GameDetailViewModel? _vm;

    public GameDetailPage()
    {
        InitializeComponent();
        StageBoard.Casters = [DetailBox];
        Stage.LayoutUpdated += (_, _) => StageBoard.InvalidateVisual();
        DataContextChanged += (_, _) =>
        {
            if (_vm != null) _vm.PropertyChanged -= OnVmChanged;
            _vm = DataContext as GameDetailViewModel;
            if (_vm == null) return;
            _vm.PickImage = PickImageAsync;
            _vm.PropertyChanged += OnVmChanged;
            ApplyBackLayout();
        };
    }

    private void OnVmChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GameDetailViewModel.HasDescription)) ApplyBackLayout();
    }

    /// <summary>Without a description the "Über das Spiel" column drops out and "Start" moves to the page margin (DESIGN 7.3).</summary>
    private void ApplyBackLayout()
    {
        var alone = _vm?.HasDescription != true;
        Grid.SetColumn(StartColumn, alone ? 0 : 2);
        Grid.SetColumnSpan(StartColumn, alone ? 3 : 1);
        StartColumn.MaxWidth = alone ? 980 : double.PositiveInfinity;
        StartColumn.HorizontalAlignment = alone ? Avalonia.Layout.HorizontalAlignment.Left : Avalonia.Layout.HorizontalAlignment.Stretch;
        StartColumn.Width = alone ? 980 : double.NaN;
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

    /// <summary>First focus: "Spielen", or – when the game cannot start – the solution button.</summary>
    public override bool FocusDefault() => FocusVisible(PlayButton) || FocusVisible(SolveButton) || FocusVisible(EmulatorBox);
}
