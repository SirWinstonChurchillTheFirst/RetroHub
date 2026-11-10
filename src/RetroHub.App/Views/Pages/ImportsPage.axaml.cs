using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using RetroHub.App.Controls;
using RetroHub.App.Input;
using RetroHub.App.Localization;
using RetroHub.App.ViewModels;

namespace RetroHub.App.Views.Pages;

/// <summary>
/// Importe (DESIGN 7.6): left/right on a folder row switch it ◀ Aktiv ▶ / ◀ Pausiert ▶; confirm opens its "Mehr" list,
/// which hangs under the row and keeps the focus until it closes (Zurück, a choice, a click on the dim).
/// </summary>
public partial class ImportsPage : PageBase
{
    private ImportsViewModel? _vm;
    private Control? _menuOpener;

    public ImportsPage()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_vm != null) _vm.PropertyChanged -= OnVmChanged;
            _vm = DataContext as ImportsViewModel;
            if (_vm == null) return;
            _vm.PickFolder = PickFolderAsync;
            _vm.PropertyChanged += OnVmChanged;
        };
        MenuScrim.PointerPressed += (_, e) =>
        {
            e.Handled = true;
            _vm?.CloseMenuCommand.Execute(null);
        };
        MenuLayer.SizeChanged += (_, _) => PlaceMenu();
    }

    private void OnVmChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ImportsViewModel.MenuFolder)) return;
        if (_vm?.MenuFolder != null)
        {
            _menuOpener = FolderList.GetVisualDescendants().OfType<ServiceRow>()
                .FirstOrDefault(r => ReferenceEquals(r.DataContext, _vm.MenuFolder.Row));
            PlaceMenu();
            Dispatcher.UIThread.Post(FocusMenu, DispatcherPriority.Loaded);
        }
        else
        {
            var back = _menuOpener;
            _menuOpener = null;
            Dispatcher.UIThread.Post(() =>
            {
                if (back is { IsEffectivelyVisible: true }) FocusVisible(back);
                else FocusDefault();
            }, DispatcherPriority.Loaded);
        }
    }

    private void FocusMenu() =>
        MenuRows.Children.OfType<Button>().FirstOrDefault(b => b.IsEffectivelyVisible)?.Focus(NavigationMethod.Directional);

    /// <summary>The list hangs under its row, aligned with the value column; above it when there is no room below.</summary>
    private void PlaceMenu()
    {
        if (_menuOpener == null || _menuOpener.TranslatePoint(new Point(0, 0), MenuLayer) is not { } at) return;
        var layer = MenuLayer.Bounds.Size;
        var rows = MenuRows.Children.Count(c => c.IsVisible);
        var height = rows * 56 + 16 + 2;
        var x = Math.Clamp(at.X + _menuOpener.Bounds.Width - MenuPanel.Width, 16, Math.Max(16, layer.Width - MenuPanel.Width - 16));
        var below = at.Y + _menuOpener.Bounds.Height + 8;
        var y = below + height <= layer.Height - 64 ? below : Math.Max(16, at.Y - 8 - height);
        MenuPanel.Margin = new Thickness(x, y, 0, 0);
    }

    public override bool MoveFocus(NavAction action, Control focused)
    {
        if (_vm?.IsMenuOpen == true)
        {
            // the list is modal: the focus stays inside
            var buttons = MenuRows.Children.OfType<Button>().Where(b => b.IsEffectivelyVisible).ToList();
            var at = buttons.IndexOf(focused as Button ?? null!);
            if (at < 0) { FocusMenu(); return true; }
            var next = at + (action == NavAction.Down ? 1 : action == NavAction.Up ? -1 : 0);
            if (next >= 0 && next < buttons.Count) buttons[next].Focus(NavigationMethod.Directional);
            return true;
        }
        if (focused is ServiceRow row && action is NavAction.Left or NavAction.Right)
        {
            row.Step(action == NavAction.Left ? -1 : +1);
            return true;
        }
        return false;
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

    public override bool FocusDefault() => FocusVisible(ScanButton) || FocusVisible(FolderList.GetVisualDescendants().OfType<ServiceRow>().FirstOrDefault());
}
