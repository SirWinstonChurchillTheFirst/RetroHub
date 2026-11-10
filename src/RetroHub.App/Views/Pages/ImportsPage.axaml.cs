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
/// which hangs under the row and keeps the focus until it closes (Zurück, a choice, the row again, a click beside it).
/// </summary>
public partial class ImportsPage : PageBase
{
    private ImportsViewModel? _vm;
    private Control? _menuOpener;
    private bool _closeByPointer;

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
        // not for a mouse press (the row would scroll away under the pointer, the click would be lost)
        AddHandler(GotFocusEvent, (_, e) =>
        {
            if (_vm?.IsMenuOpen != true && e.NavigationMethod != NavigationMethod.Pointer) EmulatorsPage.KeepClear(e.Source as Control);
        }, RoutingStrategies.Bubble);
        // a click beside the open list closes it; the click still reaches what it hit (R2)
        AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (_vm?.IsMenuOpen != true || e.Source is not Visual v) return;
            var (row, buttons) = OpenMenu();
            var fold = row?.GetVisualParent()?.GetVisualChildren().OfType<FoldPanel>().FirstOrDefault();
            if ((row != null && (row == v || row.IsVisualAncestorOf(v))) || (fold != null && (fold == v || fold.IsVisualAncestorOf(v)))) return;
            _closeByPointer = true; // the focus goes where the click goes
            try { _vm.CloseMenuCommand.Execute(null); }
            finally { _closeByPointer = false; }
        }, RoutingStrategies.Tunnel);
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

    private bool _wasScanning;

    private void OnVmChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ImportsViewModel.IsScanning) && _vm != null)
        {
            // a scan that just ended prints its delivery note (R2 animation)
            if (_wasScanning && !_vm.IsScanning) Dispatcher.UIThread.Post(PrintNote, DispatcherPriority.Loaded);
            _wasScanning = _vm.IsScanning;
            return;
        }
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
            if (_closeByPointer) return;
            Dispatcher.UIThread.Post(() =>
            {
                if (back is { IsEffectivelyVisible: true }) FocusVisible(back);
                else FocusDefault();
            }, DispatcherPriority.Loaded);
        }
    }

    /// <summary>
    /// The delivery note is printed (R2 animation): it comes out line by line from the top – the paper is revealed
    /// downward while each line drops 4 px into place with its ink, 24 ms apart (at most 600 ms for the whole note) – and
    /// is torn off last: the torn edge appears and the slip drops 4 px and settles (Step). Reduced: it fades in (100 ms);
    /// off: it is there.
    /// </summary>
    private void PrintNote()
    {
        if (_vm?.HasReport != true || !DeliveryNote.IsEffectivelyVisible || !Theming.Motion.Enabled) return;
        if (!Theming.Motion.AllowsMovement)
        {
            Anim.FadeIn(DeliveryNote);
            return;
        }
        var lines = new List<Control>();
        foreach (var child in NoteBody.Children.Where(c => c.IsVisible))
            if (child == DeliveryLines && DeliveryLines.ItemsPanelRoot is Panel panel) lines.AddRange(panel.Children);
            else lines.Add(child);
        var height = DeliveryNote.Bounds.Height;
        var width = DeliveryNote.Bounds.Width;
        if (height <= 0 || lines.Count == 0) return;
        const double line = 120, stagger = 24, tear = 140;
        var feed = Math.Min(600 - tear, line + stagger * (lines.Count - 1));
        var gap = lines.Count > 1 ? (feed - line) / (lines.Count - 1) : 0;
        var shifts = lines.Select(l => { var t = new Avalonia.Media.TranslateTransform(0, -4); l.RenderTransform = t; l.Opacity = 0; return t; }).ToList();
        var drop = new Avalonia.Media.TranslateTransform();
        DeliveryNote.RenderTransform = drop;
        NoteTear.Opacity = 0;
        Tween.Run(DeliveryNote, TimeSpan.FromMilliseconds(feed + tear), p =>
        {
            var ms = p * (feed + tear);
            var shown = Math.Min(1, ms / feed);
            DeliveryNote.Clip = shown >= 1 ? null : new Avalonia.Media.RectangleGeometry(new Rect(0, 0, width, Math.Max(1, height * shown)));
            for (var i = 0; i < lines.Count; i++)
            {
                var k = Ease.CubicOut((ms - gap * i) / line);
                lines[i].Opacity = k;
                shifts[i].Y = -4 * (1 - k);
            }
            var torn = Math.Clamp((ms - feed) / tear, 0, 1);
            NoteTear.Opacity = torn > 0 ? 1 : 0;
            drop.Y = 4 * Ease.Bump(torn);
        }, () =>
        {
            DeliveryNote.Clip = null;
            DeliveryNote.RenderTransform = null;
            NoteTear.Opacity = 1;
            foreach (var l in lines)
            {
                l.ClearValue(Visual.RenderTransformProperty);
                l.ClearValue(Visual.OpacityProperty);
            }
        });
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
