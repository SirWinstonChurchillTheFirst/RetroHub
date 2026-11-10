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
        AddHandler(GotFocusEvent, (_, e) => { if (_vm?.IsMenuOpen != true) EmulatorsPage.KeepClear(e.Source as Control); }, RoutingStrategies.Bubble);
    }

    /// <summary>The owner row of the open list and the list's buttons (the fold right under the row).</summary>
    private (ServiceRow? Row, IReadOnlyList<Button> Buttons) OpenMenu()
    {
        if (_vm?.MenuFolder is not { } folder) return (null, []);
        var row = FolderList.GetVisualDescendants().OfType<ServiceRow>().FirstOrDefault(r => ReferenceEquals(r.DataContext, folder.Row));
        var fold = row?.GetVisualParent()?.GetVisualChildren().OfType<FoldPanel>().FirstOrDefault();
        var buttons = fold?.GetVisualDescendants().OfType<Button>().Where(b => b.IsVisible).ToList() ?? [];
        return (row, buttons);
    }

    private void OnVmChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ImportsViewModel.MenuFolder)) return;
        if (_vm?.MenuFolder != null)
        {
            _menuOpener = OpenMenu().Row;
            // the list folds open under its row: the row and its three lines come into view (260 px below the row)
            if (_menuOpener != null) _menuOpener.BringIntoView(new Rect(0, -48, _menuOpener.Bounds.Width, _menuOpener.Bounds.Height + 48 + 260));
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

    private void FocusMenu() => OpenMenu().Buttons.FirstOrDefault(b => b.IsEffectivelyVisible)?.Focus(NavigationMethod.Directional);

    public override bool MoveFocus(NavAction action, Control focused)
    {
        if (_vm?.IsMenuOpen == true)
        {
            // the list is modal: the focus stays inside it
            var buttons = OpenMenu().Buttons.Where(b => b.IsEffectivelyVisible).ToList();
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
