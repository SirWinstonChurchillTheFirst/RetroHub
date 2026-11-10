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
/// Konsolen (DESIGN 7.4): the wall of spines on one board, the ticker, the "Noch leer" edge. Left/right walk along a
/// row (boundary feedback at the ends), down reaches the "Noch leer" edge, up goes back to the remembered spine (and
/// from the wall up into the header). The focused system becomes the page ground; a ticker click turns its front.
/// </summary>
public partial class SystemsPage : PageBase
{
    /// <summary>Below this page height the ≈1100 values apply (DESIGN 7.4: front 380 × 460, spines 96 × 360).</summary>
    public const double CompactBelowHeight = 860;

    private static readonly Avalonia.Media.Color RoomDeep = Avalonia.Media.Color.Parse("#0A0908");

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
        TickerScroll.ScrollChanged += (_, _) => UpdateFades();
        TickerScroll.SizeChanged += (_, _) => UpdateFades();
    }

    private void OnVmChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_vm == null) return;
        if (e.PropertyName == nameof(SystemsViewModel.Backdrop)) UpdateFadeColours();
        else if (e.PropertyName == nameof(SystemsViewModel.Current)) Dispatcher.UIThread.Post(BringCurrentTickIntoView, DispatcherPriority.Loaded);
    }

    private void OnSpineFocused(Control item)
    {
        if (_vm != null && item.DataContext is SystemItemViewModel system) _vm.SetCurrent(system);
    }

    /// <summary>A ticker code turns that system's front forward (the ticker itself takes no focus).</summary>
    private void OnTickerClick(object? sender, RoutedEventArgs e)
    {
        if (_vm == null || (sender as Control)?.Tag is not SystemItemViewModel system) return;
        var index = _vm.Systems.ToList().IndexOf(system);
        if (index >= 0) Wall.FocusItem(index);
        e.Handled = true;
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
        Unused.ItemHeight = compact ? 104 : 132;
        UnusedShelf.Margin = new Thickness(0, compact ? 12 : 20, 0, 0);
        TickerRow.Height = compact ? 44 : 48;
    }

    /// <summary>The wall, the ticker and the "Noch leer" edge stand in the middle of the page (top between 40 and 140).</summary>
    private void PlaceStack()
    {
        var free = Bounds.Height - Stack.Bounds.Height;
        if (Bounds.Height <= 0 || Stack.Bounds.Height <= 0) return;
        var top = Math.Round(Math.Clamp(free / 2, 40, 140));
        if (Math.Abs(Stack.Margin.Top - top) > 0.5) Stack.Margin = new Thickness(0, top, 0, 0);
    }

    private void UpdateFadeColours()
    {
        var g = _vm?.Backdrop is { Kind: BackdropKind.Wall, SystemId: { } id } spec
            ? Theming.ColorUtil.Mix(Theming.SystemPalette.Get(id, spec.Accent).Wall, RoomDeep, spec.WallShade)
            : Theming.SystemPalette.Room;
        FadeLeft.Background = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            GradientStops = { new GradientStop(g, 0), new GradientStop(Avalonia.Media.Color.FromArgb(0, g.R, g.G, g.B), 1) },
        };
        FadeRight.Background = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Avalonia.Media.Color.FromArgb(0, g.R, g.G, g.B), 0), new GradientStop(g, 1) },
        };
    }

    private void UpdateFades()
    {
        var extent = TickerScroll.Extent.Width;
        var viewport = TickerScroll.Viewport.Width;
        var x = TickerScroll.Offset.X;
        FadeLeft.IsVisible = x > 1;
        FadeRight.IsVisible = extent - viewport - x > 1;
    }

    /// <summary>The current code stays fully visible inside the 48 px edge fades.</summary>
    private void BringCurrentTickIntoView()
    {
        if (_vm?.Current is not { } current) return;
        var index = _vm.Systems.ToList().IndexOf(current);
        if (index < 0 || Ticker.ContainerFromIndex(index) is not Control c || c.TranslatePoint(new Point(0, 0), Ticker) is not { } p) return;
        const double fade = 48;
        var view = TickerScroll.Viewport.Width;
        var x = TickerScroll.Offset.X;
        var left = p.X;
        var right = p.X + c.Bounds.Width;
        if (left <= 1) x = 0;
        else if (left - fade < x) x = left - fade;
        else if (right + fade > x + view) x = right + fade - view;
        TickerScroll.Offset = TickerScroll.Offset.WithX(Math.Max(0, x));
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
