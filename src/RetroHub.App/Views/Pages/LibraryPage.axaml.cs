using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using RetroHub.App.Controls;
using RetroHub.App.ViewModels;

namespace RetroHub.App.Views.Pages;

public partial class LibraryPage : PageBase
{
    /// <summary>Below this logical width the tools are icons only (DESIGN 7.2.1 "≈1100 px").</summary>
    public const double CompactBelowWidth = 1500;

    private LibraryViewModel? _vm;
    private bool _searchRequested;
    private bool _searchOpen;

    public LibraryPage()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_vm != null) _vm.FocusSearchRequested -= FocusSearch;
            _vm = DataContext as LibraryViewModel;
            if (_vm != null)
            {
                _vm.FocusSearchRequested += FocusSearch;
                // The focus state must be read BEFORE the switch: hiding the old view (a binding on the same
                // PropertyChanged) drops the keyboard focus, so afterwards nothing would be focused at all.
                _vm.PropertyChanging += (_, e) =>
                {
                    if (e.PropertyName != nameof(LibraryViewModel.IsListView)) return;
                    var from = _vm.IsListView ? Rows : Covers;
                    _focusToTransfer = from.IsKeyboardFocusWithin ? Math.Max(0, from.SelectedIndex) : -1;
                };
                _vm.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(LibraryViewModel.SearchText)) { if (!string.IsNullOrEmpty(_vm.SearchText)) SetSearchOpen(true); return; }
                    if (e.PropertyName == nameof(LibraryViewModel.SelectedSystemId)) { Dispatcher.UIThread.Post(BringActiveChipIntoView, DispatcherPriority.Loaded); return; }
                    if (e.PropertyName != nameof(LibraryViewModel.IsListView)) return;
                    var index = _focusToTransfer;
                    _focusToTransfer = -1;
                    if (index < 0) return;
                    var to = _vm.IsListView ? Rows : Covers;
                    Dispatcher.UIThread.Post(() => to.FocusWhenReady(index), DispatcherPriority.Loaded);
                };
                BuildSortMenu();
            }
        };
        // the footer says where the focus is ("Alle Spiele, 12 von 66")
        Covers.PropertyChanged += (_, e) => { if (e.Property == CoverGrid.SelectedIndexProperty && Covers.IsVisible && _vm != null) _vm.FocusIndex = Covers.SelectedIndex; };
        Rows.PropertyChanged += (_, e) => { if (e.Property == CoverGrid.SelectedIndexProperty && Rows.IsVisible && _vm != null) _vm.FocusIndex = Rows.SelectedIndex; };
        SearchToggle.Click += (_, _) => FocusSearchNow();
        Search.LostFocus += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            if (!Search.IsKeyboardFocusWithin && string.IsNullOrEmpty(_vm?.SearchText)) SetSearchOpen(false);
        }, DispatcherPriority.Background);
        Search.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Escape && string.IsNullOrEmpty(_vm?.SearchText)) { SetSearchOpen(false); FocusVisible(SearchToggle); e.Handled = true; }
        }, RoutingStrategies.Tunnel);
        TickerScroll.ScrollChanged += (_, _) => UpdateFades();
        TickerScroll.SizeChanged += (_, _) => UpdateFades();
        SizeChanged += (_, e) => Tools.Classes.Set("compact", e.NewSize.Width < CompactBelowWidth);
        SearchPanel.RenderTransform = new TranslateTransform(24, 0);
        SearchPanel.Opacity = 0;
    }

    private int _focusToTransfer = -1;

    private void BuildSortMenu()
    {
        if (_vm == null) return;
        var menu = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedRight };
        foreach (var option in _vm.SortOptions)
        {
            var item = new MenuItem { Header = option.Label };
            item.Click += (_, _) => _vm.SelectedSort = option;
            menu.Items.Add(item);
        }
        SortButton.Flyout = menu;
    }

    /// <summary>The search slides out over the ticker (180 ms) and back when it is left empty.</summary>
    private void SetSearchOpen(bool open)
    {
        if (_searchOpen == open) return;
        _searchOpen = open;
        if (open) SearchPanel.IsVisible = true;
        SearchPanel.Transitions = Theming.Motion.Current == Theming.EffectiveMotion.Off ? null :
        [
            new Avalonia.Animation.DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(180) },
            new Avalonia.Animation.TransformOperationsTransition { Property = RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(180),
                Easing = new Avalonia.Animation.Easings.CubicEaseOut() },
        ];
        SearchPanel.Opacity = open ? 1 : 0;
        SearchPanel.RenderTransform = Avalonia.Media.Transformation.TransformOperations.Parse(open ? "translateX(0px)" : "translateX(24px)");
        if (!open) DispatcherTimer.RunOnce(() => { if (!_searchOpen) SearchPanel.IsVisible = false; }, TimeSpan.FromMilliseconds(200));
    }

    private void FocusSearchNow()
    {
        SetSearchOpen(true);
        Dispatcher.UIThread.Post(() =>
        {
            Search.Focus(NavigationMethod.Directional);
            Search.SelectAll();
        }, DispatcherPriority.Loaded);
    }

    private void FocusSearch()
    {
        _searchRequested = true; // wins over the default focus the shell applies after navigation
        SetSearchOpen(true);
        Dispatcher.UIThread.Post(() =>
        {
            if (!_searchRequested) return;
            Search.Focus(NavigationMethod.Directional);
            Search.SelectAll();
        }, DispatcherPriority.Loaded);
    }

    private void UpdateFades()
    {
        var extent = TickerScroll.Extent.Width;
        var viewport = TickerScroll.Viewport.Width;
        var x = TickerScroll.Offset.X;
        FadeLeft.IsVisible = x > 1;
        FadeRight.IsVisible = extent - viewport - x > 1;
    }

    /// <summary>The active ticker entry stays visible (DESIGN 7.2.1).</summary>
    private void BringActiveChipIntoView()
    {
        if (_vm == null) return;
        var index = _vm.Chips.ToList().FindIndex(c => c.IsSelected);
        if (index >= 0 && Ticker.ContainerFromIndex(index) is Control c) c.BringIntoView();
    }

    public override bool FocusDefault()
    {
        var grid = _vm?.IsListView == true ? Rows : Covers;
        if (_searchRequested)
        {
            _searchRequested = false;
            grid.CancelPendingFocus();
            SetSearchOpen(true);
            return FocusVisible(Search);
        }
        if (_vm is { IsLoaded: true } && grid.ItemCount == 0)
        {
            SetSearchOpen(true);
            return FocusVisible(Search);
        }
        return grid.FocusWhenReady(Math.Max(0, grid.SelectedIndex));
    }
}
