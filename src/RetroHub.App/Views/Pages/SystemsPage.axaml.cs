using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using RetroHub.App.Controls;
using RetroHub.App.Input;
using RetroHub.App.ViewModels;

namespace RetroHub.App.Views.Pages;

/// <summary>
/// Konsolen (DESIGN 7.4): the wall of spines on one board, the position rail, the "Noch leer" edge. Left/right walk along a
/// row (boundary feedback at the ends), down reaches the "Noch leer" edge, up goes back to the remembered spine (and
/// from the wall up into the header). The focused system becomes the page ground. The mouse (R2): the first click on a
/// spine turns its front forward, a click on the open front opens the system; a click or a drag on the rail and the wheel
/// over the wall or the rail step through the systems.
/// </summary>
public partial class SystemsPage : PageBase
{
    /// <summary>Below this page height the ≈1100 values apply (DESIGN 7.4: front 380 × 460, spines 96 × 360).</summary>
    public const double CompactBelowHeight = 860;

    private SystemsViewModel? _vm;
    private bool? _compact;

    public SystemsPage()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_vm != null) _vm.PropertyChanged -= OnVmChanged;
            _vm = DataContext as SystemsViewModel;
            if (_vm != null) _vm.PropertyChanged += OnVmChanged;
        };
        Wall.ItemFocused += (_, item, _, _) => OnSpineFocused(item);
        Unused.ItemFocused += (_, item, _, _) => OnSpineFocused(item);
        SizeChanged += (_, e) => ApplySize(e.NewSize);
        Stack.SizeChanged += (_, _) => PlaceStack();
        Rail.TickClicked += index => Wall.FocusItem(index);
        // nothing scrolls vertically here: the wheel over the wall or the rail turns the next/previous front forward
        Wall.AddHandler(PointerWheelChangedEvent, OnWallWheel, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        Rail.PointerWheelChanged += OnWallWheel;
    }

    private void OnWallWheel(object? sender, Avalonia.Input.PointerWheelEventArgs e)
    {
        var d = Math.Abs(e.Delta.X) > Math.Abs(e.Delta.Y) ? -e.Delta.X : -e.Delta.Y;
        if (Math.Abs(d) < 0.01 || Wall.Items().Count == 0) return;
        e.Handled = true;
        Wall.FocusItem(Math.Clamp(Wall.RememberedIndex + (d > 0 ? 1 : -1), 0, Wall.Items().Count - 1));
    }

    private void OnVmChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
    }

    private void OnSpineFocused(Control item)
    {
        if (_vm != null && item.DataContext is SystemItemViewModel system) _vm.SetCurrent(system);
    }

    /// <summary>1080 values or the ≈1100 values (class "compact"), from the page height.</summary>
    private void ApplySize(Size size)
    {
        var compact = size.Height < CompactBelowHeight;
        if (_compact == compact) return;
        _compact = compact;
        Root.Classes.Set("compact", compact);
        PlaceStack();
        var wallHeight = compact ? 460.0 : 600.0;
        EmptyPlaces.Height = wallHeight;
        foreach (var slot in EmptyPlaces.Children) slot.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom;
        EmptyText.Width = compact ? 380 : 520;
        Unused.ItemHeight = compact ? 116 : 148;
        // the rail and the "Noch leer" spines read as two bands, not one (M5 QA)
        UnusedShelf.Margin = new Thickness(0, compact ? 20 : 32, 0, 0);
        TickerRow.Height = compact ? 40 : 44;
    }

    /// <summary>Space the footer takes at the page's bottom (its bar; the fade above it may cover the label's last pixels).</summary>
    private const double FooterReserve = 56;

    /// <summary>
    /// The wall, the rail and the "Noch leer" edge stand in the middle of the page above the footer (top between 40 and
    /// 140; ≈1100: from 24, so the label of the "Noch leer" edge stays above the footer).
    /// </summary>
    private void PlaceStack()
    {
        var free = Bounds.Height - FooterReserve - Stack.Bounds.Height;
        if (Bounds.Height <= 0 || Stack.Bounds.Height <= 0) return;
        var top = Math.Round(Math.Clamp(free / 2, _compact == true ? 24 : 40, 140));
        if (Math.Abs(Stack.Margin.Top - top) > 0.5) Stack.Margin = new Thickness(0, top, 0, 0);
    }

    /// <summary>
    /// Left/right along the focused row (the wall or the "Noch leer" edge) with boundary feedback; down from the wall to
    /// the edge, up from the edge back to the remembered spine; up from the wall leaves the page (header).
    /// </summary>
    public override bool MoveFocus(NavAction action, Control focused)
    {
        var shelf = focused.FindAncestorOfType<Shelf>(includeSelf: true);
        if (focused == EmptyImports && action == NavAction.Down) return Unused.FocusItem(Unused.RememberedIndex);
        if (shelf == Unused && action == NavAction.Up && Wall.Items().Count == 0 && EmptyImports.IsEffectivelyVisible) return FocusVisible(EmptyImports);
        if (shelf != Wall && shelf != Unused) return false;
        switch (action)
        {
            case NavAction.Left or NavAction.Right:
                var dir = action == NavAction.Right ? 1 : -1;
                if (!shelf.MoveHorizontal(dir, focused)) ShelfViewport.Bump(focused, dir);
                return true;
            case NavAction.Down when shelf == Wall:
                if (Unused.IsEffectivelyVisible && Unused.FocusItem(Unused.RememberedIndex)) return true;
                ShelfViewport.Bump(focused, 0);
                return true;
            case NavAction.Down:
                return true; // the last row: nothing below
            case NavAction.Up when shelf == Unused && Wall.Items().Count > 0:
                return Wall.FocusItem(Wall.RememberedIndex);
        }
        return false;
    }

    public override bool FocusDefault()
    {
        if (Wall.Items().Count > 0) return Wall.FocusItem(Wall.RememberedIndex);
        if (_vm?.IsLibraryEmpty == true && FocusVisible(EmptyImports)) return true;
        if (Unused.Items().Count > 0) return Unused.FocusItem(Unused.RememberedIndex);
        // the items are built after the first layout: try again once they exist
        Dispatcher.UIThread.Post(() =>
        {
            if (!IsEffectivelyVisible || IsKeyboardFocusWithin) return;
            if (Wall.Items().Count > 0) Wall.FocusItem(Wall.RememberedIndex);
            else if (Unused.Items().Count > 0) Unused.FocusItem(Unused.RememberedIndex);
        }, DispatcherPriority.Loaded);
        return true;
    }
}
