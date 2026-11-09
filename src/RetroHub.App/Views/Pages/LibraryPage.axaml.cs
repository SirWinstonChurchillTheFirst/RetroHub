using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using Avalonia.VisualTree;
using RetroHub.App.Controls;
using RetroHub.App.Input;
using RetroHub.App.Theming;
using RetroHub.App.ViewModels;

namespace RetroHub.App.Views.Pages;

public partial class LibraryPage : PageBase
{
    /// <summary>Below this logical width the tools are icons only and the banner is 160 high (DESIGN 7.2.1 "≈1100 px").</summary>
    public const double CompactBelowWidth = 1500;

    private LibraryViewModel? _vm;
    private bool _searchRequested;
    private bool _searchOpen;
    private bool _compact;

    public LibraryPage()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_vm != null)
            {
                _vm.FocusSearchRequested -= FocusSearch;
                _vm.PropertyChanging -= OnVmChanging;
                _vm.PropertyChanged -= OnVmChanged;
            }
            _vm = DataContext as LibraryViewModel;
            if (_vm != null)
            {
                _vm.FocusSearchRequested += FocusSearch;
                _vm.PropertyChanging += OnVmChanging;
                _vm.PropertyChanged += OnVmChanged;
                BuildSortMenu();
                ApplyContext();
                Mirror.Items = _vm.Items;
            }
        };
        // the footer says where the focus is ("Alle Spiele, 12 von 66"); the mirror's mark follows it
        Covers.PropertyChanged += (_, e) => { if (e.Property == CoverGrid.SelectedIndexProperty && Covers.IsVisible && _vm != null) _vm.FocusIndex = Covers.SelectedIndex; };
        Rows.PropertyChanged += (_, e) => { if (e.Property == CoverGrid.SelectedIndexProperty && Rows.IsVisible && _vm != null) _vm.FocusIndex = Rows.SelectedIndex; };
        Covers.ViewportChanged += UpdateMirrorRange;
        Rows.ViewportChanged += UpdateMirrorRange;
        Mirror.Scrub += index => ActiveGrid.ScrollToIndex(index);
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
        SizeChanged += (_, e) => ApplyWidth(e.NewSize.Width);
        PreviewShelf.SizeChanged += (_, e) => SizePreview(e.NewSize);
        PreviewBoard.Casters = [PreviewArt];
        BannerBoard.Casters = [BannerMotif];
        SearchPanel.RenderTransform = new TranslateTransform(24, 0);
        SearchPanel.Opacity = 0;
    }

    private int _focusToTransfer = -1;

    private CoverGrid ActiveGrid => _vm?.IsListView == true ? Rows : Covers;

    private void OnVmChanging(object? sender, System.ComponentModel.PropertyChangingEventArgs e)
    {
        // The focus state must be read BEFORE the switch: hiding the old view (a binding on the same PropertyChanged)
        // drops the keyboard focus, so afterwards nothing would be focused at all.
        if (e.PropertyName != nameof(LibraryViewModel.IsListView) || _vm == null) return;
        var from = _vm.IsListView ? Rows : Covers;
        _focusToTransfer = from.IsKeyboardFocusWithin ? Math.Max(0, from.SelectedIndex) : -1;
    }

    private void OnVmChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_vm == null) return;
        switch (e.PropertyName)
        {
            case nameof(LibraryViewModel.SearchText):
                if (!string.IsNullOrEmpty(_vm.SearchText)) SetSearchOpen(true);
                return;
            case nameof(LibraryViewModel.SelectedSystemId):
                Dispatcher.UIThread.Post(BringActiveChipIntoView, DispatcherPriority.Loaded);
                return;
            case nameof(LibraryViewModel.Items):
                Mirror.Items = _vm.Items;
                return;
            case nameof(LibraryViewModel.FocusIndex):
                Mirror.FocusIndex = _vm.FocusIndex;
                return;
            case nameof(LibraryViewModel.ContextLight) or nameof(LibraryViewModel.Ground):
                ApplyContext();
                return;
            case nameof(LibraryViewModel.SlotAspect) or nameof(LibraryViewModel.IsSystemSelected):
                // in a compartment the spine placeholders take the slot format of the system's covers
                Covers.PlaceholderAspect = _vm.IsSystemSelected ? _vm.SlotAspect : 0;
                return;
            case nameof(LibraryViewModel.BannerTitle):
                AnimateBanner();
                return;
            case nameof(LibraryViewModel.IsListView):
                UpdateMirrorRange();
                var index = _focusToTransfer;
                _focusToTransfer = -1;
                if (index < 0) return;
                var to = _vm.IsListView ? Rows : Covers;
                Dispatcher.UIThread.Post(() => to.FocusWhenReady(index), DispatcherPriority.Loaded);
                return;
        }
    }

    /// <summary>The system context: the focus light and mirror mark in the system's light, fades in the page ground.</summary>
    private void ApplyContext()
    {
        if (_vm == null) return;
        Covers.FocusVisuals.Light = _vm.ContextLight;
        Covers.FocusVisuals.Refresh();
        Mirror.MarkColor = _vm.ContextLight;
        Mirror.Ground = _vm.Ground;
        var g = _vm.Ground;
        FadeLeft.Background = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            GradientStops = { new GradientStop(g, 0), new GradientStop(Color.FromArgb(0, g.R, g.G, g.B), 1) },
        };
        FadeRight.Background = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.FromArgb(0, g.R, g.G, g.B), 0), new GradientStop(g, 1) },
        };
        SearchPanel.Background = new SolidColorBrush(g);
        // what scrolls away under the fixed ticker and mirror fades out in the page ground instead of being sliced
        Covers.TopFadeColor = g;
        Rows.TopFadeColor = g;
    }

    private void UpdateMirrorRange()
    {
        var (first, last) = ActiveGrid.VisibleRange();
        Mirror.FirstVisible = first;
        Mirror.LastVisible = last;
    }

    /// <summary>Width-dependent values: icon-only tools, grid spacing 28/20, banner 200/160 (DESIGN 7.0, 7.2.1).</summary>
    private void ApplyWidth(double width)
    {
        _compact = width < CompactBelowWidth;
        Tools.Classes.Set("compact", _compact);
        Covers.Spacing = width >= 1600 ? 28 : 20;
        Banner.Height = _compact ? 160 : 200;
        // the motif stands on the banner's stage board at ≈ 72 % of the wall above it (M3 QA: 144 looked pushed aside)
        BannerMotif.Width = BannerMotif.Height = _compact ? 124 : 160;
        // the empty shelf's board at ≈ 46 % of the page (1080: y ≈ 510, 1100 px: y ≈ 415 in window pixels)
        var inset = this.TryFindResource("RhPageInsetX", out var r) && r is Thickness t ? t : new Thickness(96, 0, 96, 0);
        EmptyShelf.Margin = new Thickness(inset.Left, Math.Max(24, Math.Round(Bounds.Height * 0.46) - 200), inset.Right, 0);
        EmptySlotLast.IsVisible = !_compact; // 1100 px: four places, none cut at the edge
        BannerTitle.FontSize = _compact ? 44 : 56;
        BannerText.Margin = new Thickness(0, 0, _compact ? 200 : 280, _compact ? 40 : 48);
    }

    /// <summary>System change: the banner's text and motif slide 24 px in and fade (220 ms); the wall itself is set at once.</summary>
    private void AnimateBanner()
    {
        if (Motion.Current != EffectiveMotion.Full || !Banner.IsEffectivelyVisible) return;
        BannerContent.Transitions = null;
        BannerContent.Opacity = 0;
        BannerContent.RenderTransform = TransformOperations.Parse("translateX(24px)");
        Dispatcher.UIThread.Post(() =>
        {
            BannerContent.Transitions =
            [
                new DoubleTransition { Property = OpacityProperty, Duration = Motion.Glide, Easing = new CubicEaseOut() },
                new TransformOperationsTransition { Property = RenderTransformProperty, Duration = Motion.Glide, Easing = new CubicEaseOut() },
            ];
            BannerContent.Opacity = 1;
            BannerContent.RenderTransform = TransformOperations.Parse("translateX(0px)");
        }, DispatcherPriority.Render);
    }

    /// <summary>
    /// The preview box: at most 420 × 560, as tall as the column allows above the bottom-anchored text, standing on the
    /// shelf edge (M3 QA: at 1080 the 360 × 480 box left ≈ 200 px of empty column above it).
    /// </summary>
    private void SizePreview(Size size)
    {
        PreviewArt.Width = 420;
        PreviewArt.Height = Math.Max(120, Math.Min(560, size.Height - ShelfBoard.EdgeHeight));
    }

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
        SearchPanel.Transitions = Motion.Current == EffectiveMotion.Off ? null :
        [
            new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(180) },
            new TransformOperationsTransition { Property = RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(180), Easing = new CubicEaseOut() },
        ];
        SearchPanel.Opacity = open ? 1 : 0;
        SearchPanel.RenderTransform = TransformOperations.Parse(open ? "translateX(0px)" : "translateX(24px)");
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

    /// <summary>
    /// The active ticker entry stays visible (DESIGN 7.2.1), and fully inside the 48 px edge fades – an entry that
    /// stood under a fade looked cut (M3 QA).
    /// </summary>
    private void BringActiveChipIntoView()
    {
        if (ActiveChip() is not { } c || c.TranslatePoint(new Point(0, 0), Ticker) is not { } p) return;
        const double fade = 48;
        var view = TickerScroll.Viewport.Width;
        var x = TickerScroll.Offset.X;
        var left = p.X;
        var right = p.X + c.Bounds.Width;
        var first = left <= 1; // "Alle" may stand at the very start
        if (!first && left - fade < x) x = left - fade;
        else if (right + fade > x + view) x = right + fade - view;
        else if (first) x = 0;
        TickerScroll.Offset = TickerScroll.Offset.WithX(Math.Max(0, x));
    }

    private Control? ActiveChip()
    {
        if (_vm == null) return null;
        var index = _vm.Chips.ToList().FindIndex(c => c.IsSelected);
        return index >= 0 && Ticker.ContainerFromIndex(index) is Control container
            ? container.GetVisualDescendants().OfType<Button>().FirstOrDefault() ?? container
            : null;
    }

    /// <summary>
    /// Page-local moves: up from the first row reaches the active ticker entry (not whatever the spatial search finds),
    /// down from the ticker returns to the remembered box; left from the list's preview goes back to its row.
    /// </summary>
    public override bool MoveFocus(NavAction action, Control focused)
    {
        if (_vm == null) return false;
        var grid = ActiveGrid;
        if (action == NavAction.Up && focused.FindAncestorOfType<CoverGrid>(includeSelf: true) == grid
            && grid.SelectedIndex >= 0 && grid.SelectedIndex < Math.Max(1, grid.Columns))
        {
            if (ActiveChip() is { } chip) return FocusVisible(chip);
            return false;
        }
        if (action == NavAction.Down && focused.FindAncestorOfType<ItemsControl>(includeSelf: true) == Ticker && grid.ItemCount > 0)
            return grid.FocusWhenReady(Math.Max(0, grid.SelectedIndex));
        if (action == NavAction.Left && _vm.IsListView && focused == PreviewPlay)
            return Rows.FocusWhenReady(Math.Max(0, Rows.SelectedIndex));
        return false;
    }

    public override bool FocusDefault()
    {
        var grid = ActiveGrid;
        if (_searchRequested)
        {
            _searchRequested = false;
            grid.CancelPendingFocus();
            SetSearchOpen(true);
            return FocusVisible(Search);
        }
        if (_vm is { IsLoaded: true, IsLibraryEmpty: true }) return FocusVisible(EmptyAddFolder);
        if (_vm is { IsLoaded: true } && grid.ItemCount == 0)
        {
            SetSearchOpen(true);
            return FocusVisible(Search);
        }
        return grid.FocusWhenReady(Math.Max(0, grid.SelectedIndex));
    }
}
