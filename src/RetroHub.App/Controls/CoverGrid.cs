using System.Collections;
using System.Collections.Specialized;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using RetroHub.App.Input;

namespace RetroHub.App.Controls;

/// <summary>One row of the chunked grid: the items of row <see cref="Index"/>.</summary>
public sealed class CoverRow(int index, IReadOnlyList<object> items)
{
    public int Index { get; } = index;
    public IReadOnlyList<object> Items { get; } = items;
}

public sealed class CoverGridItemEventArgs(object item, int index) : EventArgs
{
    public object Item { get; } = item;
    public int Index { get; } = index;
}

/// <summary>
/// Virtualized cover grid. Avalonia 12 has no virtualizing wrap panel (ItemsRepeater is gone), so the items are
/// chunked into rows of N tiles and the rows go into an <see cref="ItemsControl"/> with a
/// <see cref="VirtualizingStackPanel"/>: only visible rows (and their tiles) exist. N follows the viewport width;
/// rows are rebuilt only when N changes (a width change with the same N only resizes the tiles), and the focused
/// item keeps focus across a rebuild. Arrow keys / <see cref="NavAction"/> move by item/row and scroll the target
/// row into view (realizing it first when it was virtualized away).
/// </summary>
public sealed class CoverGrid : UserControl
{
    public static readonly StyledProperty<IEnumerable?> ItemsSourceProperty =
        AvaloniaProperty.Register<CoverGrid, IEnumerable?>(nameof(ItemsSource));
    public static readonly StyledProperty<IDataTemplate?> ItemTemplateProperty =
        AvaloniaProperty.Register<CoverGrid, IDataTemplate?>(nameof(ItemTemplate));
    public static readonly StyledProperty<double> ItemWidthProperty =
        AvaloniaProperty.Register<CoverGrid, double>(nameof(ItemWidth), 176);
    /// <summary>Height of the cover part relative to its width (portrait boxes ≈ 1.4).</summary>
    public static readonly StyledProperty<double> CoverAspectProperty =
        AvaloniaProperty.Register<CoverGrid, double>(nameof(CoverAspect), 1.4);
    /// <summary>Extra height per tile below the cover (caption).</summary>
    public static readonly StyledProperty<double> CaptionHeightProperty =
        AvaloniaProperty.Register<CoverGrid, double>(nameof(CaptionHeight), 52);
    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<CoverGrid, double>(nameof(Spacing), 24);
    public static readonly StyledProperty<double> RowSpacingProperty =
        AvaloniaProperty.Register<CoverGrid, double>(nameof(RowSpacing), 28);
    /// <summary>Tiles grow up to this factor of <see cref="ItemWidth"/> so rows fill the width exactly.</summary>
    public static readonly StyledProperty<double> MaxStretchProperty =
        AvaloniaProperty.Register<CoverGrid, double>(nameof(MaxStretch), 1.3);
    public static readonly StyledProperty<Thickness> ContentPaddingProperty =
        AvaloniaProperty.Register<CoverGrid, Thickness>(nameof(ContentPadding), new Thickness(40, 16, 40, 32));
    public static readonly DirectProperty<CoverGrid, int> SelectedIndexProperty =
        AvaloniaProperty.RegisterDirect<CoverGrid, int>(nameof(SelectedIndex), g => g.SelectedIndex, (g, v) => g.SelectedIndex = v);
    public static readonly StyledProperty<ICommand?> ItemInvokedCommandProperty =
        AvaloniaProperty.Register<CoverGrid, ICommand?>(nameof(ItemInvokedCommand));
    /// <summary>Optional header shown above the first row (scrolls with the grid).</summary>
    public static readonly StyledProperty<Control?> HeaderProperty =
        AvaloniaProperty.Register<CoverGrid, Control?>(nameof(Header));

    /// <summary>The header runs edge to edge over the content padding (the library's wall banner, DESIGN 7.2.1).</summary>
    /// <summary>Content after the last row (scrolls along; the system page's "Fachende").</summary>
    public static readonly StyledProperty<Control?> FooterProperty =
        AvaloniaProperty.Register<CoverGrid, Control?>(nameof(Footer));

    public static readonly StyledProperty<bool> HeaderBleedProperty =
        AvaloniaProperty.Register<CoverGrid, bool>(nameof(HeaderBleed));

    /// <summary>Placeholder format (height/width) handed to every tile; 0 = each system's own box format.</summary>
    public static readonly StyledProperty<double> PlaceholderAspectProperty =
        AvaloniaProperty.Register<CoverGrid, double>(nameof(PlaceholderAspect));

    /// <summary>Draw the 10 px shelf edge (board) and the contact shadows under every row of boxes.</summary>
    public static readonly StyledProperty<bool> ShowBoardProperty =
        AvaloniaProperty.Register<CoverGrid, bool>(nameof(ShowBoard), true);

    /// <summary>
    /// Height of a compact page header that covers the viewport's top once the grid's header has scrolled out
    /// (Systemseite, M5 QA): rows ≥ 1 snap this much lower, so they start <see cref="RowSpacing"/> below it.
    /// </summary>
    public static readonly StyledProperty<double> StickyInsetProperty =
        AvaloniaProperty.Register<CoverGrid, double>(nameof(StickyInset));

    /// <summary>
    /// Every snapped position stays reachable at the end of the content (a spacer grows on demand): a short grid under
    /// a tall header never stops between two snaps with a sliver of the header left at the top (M5 QA).
    /// </summary>
    public static readonly StyledProperty<bool> SnapToEndProperty =
        AvaloniaProperty.Register<CoverGrid, bool>(nameof(SnapToEnd));

    /// <summary>
    /// A mouse click on a row that is not the selected one only selects it (the list's preview fills); a click on the
    /// selected row (or a double click) opens it. Keyboard/controller Accept always opens (the focused row is selected).
    /// </summary>
    public static readonly StyledProperty<bool> ClickSelectsFirstProperty =
        AvaloniaProperty.Register<CoverGrid, bool>(nameof(ClickSelectsFirst));

    public bool ClickSelectsFirst { get => GetValue(ClickSelectsFirstProperty); set => SetValue(ClickSelectsFirstProperty, value); }

    private int _pressIndex = -1;
    private bool _pressWasSelected;

    private readonly ScrollViewer _scroll;
    private readonly ItemsControl _rows;
    private readonly StackPanel _outer;
    private readonly Panel _content;
    private readonly ContentControl _headerHost;
    private readonly ContentControl _footerHost;
    private readonly Control _tail = new Control { IsHitTestVisible = false };
    private readonly FocusLayer _focus;
    private readonly Control _boards;
    private readonly Border _fade;
    private IReadOnlyList<object> _items = [];
    private Dictionary<object, int> _indexOf = new(ReferenceEqualityComparer.Instance);
    private INotifyCollectionChanged? _observed;
    private int _columns;
    private int _selectedIndex = -1;
    private int _pendingFocus = -1;
    private double _tileWidth;
    private double _tileHeight;

    /// <summary>Raised when an item is activated (click, Enter, gamepad A).</summary>
    public event EventHandler<CoverGridItemEventArgs>? ItemInvoked;

    /// <summary>Raised after the rows were (re)built (tests, diagnostics).</summary>
    public event Action? RowsRebuilt;

    /// <summary>Raised when the part of the list in view changed (scrolling, rebuild, resize): see <see cref="VisibleRange"/>.</summary>
    public event Action? ViewportChanged;

    /// <summary>Raised on every change of the scroll position (see <see cref="ScrollOffsetY"/>).</summary>
    public event Action? Scrolled;

    private (int, int) _lastRange = (-2, -2);

    private void RaiseViewportChanged()
    {
        var r = VisibleRange();
        if (r == _lastRange) return;
        _lastRange = r;
        try { ViewportChanged?.Invoke(); } catch (Exception ex) { RetroHub.Core.Logging.Log.Warn("ViewportChanged handler failed", ex); }
    }

    /// <summary>Row presenters constructed so far (tests: scrolling reuses them).</summary>
    internal int PresentersCreated { get; private set; }

    /// <summary>The rows: an items control whose containers are the row presenters themselves.</summary>
    private sealed class RowItems(CoverGrid grid) : ItemsControl
    {
        protected override Type StyleKeyOverride => typeof(ItemsControl);

        protected override bool NeedsContainerOverride(object? item, int index, out object? recycleKey)
        {
            recycleKey = typeof(CoverRowPresenter);
            return true;
        }

        protected override Control CreateContainerForItemOverride(object? item, int index, object? recycleKey)
        {
            grid.PresentersCreated++;
            return new CoverRowPresenter(grid);
        }

        protected override void PrepareContainerForItemOverride(Control container, object? item, int index) => container.DataContext = item;

        // keep the tiles bound while the row waits hidden in the recycle pool (cheaper than unbinding and rebinding)
        protected override void ClearContainerForItemOverride(Control container) { }
    }

    private int _boardSignature;

    /// <summary>
    /// The board layer is redrawn only when the realised rows changed (realised, recycled, resized, moved by the
    /// header) – not on every layout pass. Before (M3 perf), each focus move re-recorded all shelf edges and the
    /// compositor repainted the whole viewport: ≈ 140 ms per move in the headless CPU raster instead of a few.
    /// </summary>
    private void InvalidateBoardsIfMoved()
    {
        var hash = new HashCode();
        foreach (var container in _rows.GetRealizedContainers())
        {
            hash.Add(container.Bounds.Y);
            hash.Add(container.Bounds.Width);
            hash.Add((container.DataContext as CoverRow)?.Index ?? -1);
        }
        hash.Add(_tileWidth);
        hash.Add(_tileHeight);
        hash.Add(_headerHost.Bounds.Height);
        var signature = hash.ToHashCode();
        if (signature == _boardSignature) return;
        _boardSignature = signature;
        _boards.InvalidateVisual();
    }

    /// <summary>Distance from the top of the scroll content to the first row (padding + header).</summary>
    private double RowsTop => _content.Margin.Top + _headerHost.Bounds.Height;

    private double RowPitch => _tileHeight + RowSpacing;

    /// <summary>
    /// First and last item index whose tile is (at least half) in the viewport, from the scroll offset and the regular
    /// row pitch alone (no realized containers needed); (−1, −1) while empty or unmeasured.
    /// </summary>
    public (int First, int Last) VisibleRange()
    {
        if (_items.Count == 0 || _columns <= 0 || RowPitch <= 0 || _scroll.Viewport.Height <= 0) return (-1, -1);
        var top = _scroll.Offset.Y - RowsTop;
        var bottom = top + _scroll.Viewport.Height;
        var half = _tileHeight / 2;
        var firstRow = Math.Max(0, (int)Math.Floor((top + half) / RowPitch));
        var lastRow = Math.Min(RowCount - 1, (int)Math.Floor((bottom - half) / RowPitch));
        if (lastRow < firstRow) lastRow = firstRow;
        return (Math.Min(firstRow * _columns, _items.Count - 1), Math.Min((lastRow + 1) * _columns - 1, _items.Count - 1));
    }

    /// <summary>Scrolls so the row of <paramref name="index"/> is the top row in view (shelf mirror); the focus stays.</summary>
    public void ScrollToIndex(int index)
    {
        if (_columns <= 0 || index < 0 || index >= _items.Count) return;
        ScrollToSnapped(SnapOffset(index / _columns));
    }

    /// <summary>
    /// Space kept above the top row in view when the grid snaps (M3 QA): the row spacing itself – the lifted box
    /// (+6 % of its height) and its ring fit into it, and the row above ends exactly at the viewport's top edge.
    /// </summary>
    private double SnapHeadroom => Math.Max(0, RowSpacing) + Math.Max(0, StickyInset);

    /// <summary>The scroll offset that puts row <paramref name="row"/> at the top with <see cref="SnapHeadroom"/> above it (row 0: 0).</summary>
    private double SnapOffset(int row) => row <= 0 ? 0 : Math.Max(0, RowsTop + row * RowPitch - SnapHeadroom);

    /// <summary>The scroll position (the page shows its compact header once the grid's header has scrolled out).</summary>
    public double ScrollOffsetY => _scroll.Offset.Y;

    /// <summary>Height of the header above the first row (0 without one).</summary>
    public double HeaderHeight => _headerHost.Bounds.Height;

    /// <summary>
    /// Fade colour of the viewport's top edge (null = none): a 12 px overlay in the page ground, 100 % → 0, fixed to the
    /// viewport and drawn between the rows and the focus ring. Whatever is scrolled freely under the fixed header fades
    /// out there instead of being sliced (M3 QA). No OpacityMask.
    /// </summary>
    public static readonly StyledProperty<Color?> TopFadeColorProperty =
        AvaloniaProperty.Register<CoverGrid, Color?>(nameof(TopFadeColor));

    public Color? TopFadeColor { get => GetValue(TopFadeColorProperty); set => SetValue(TopFadeColorProperty, value); }

    public const double TopFadeHeight = 12;

    private void UpdateTopFade()
    {
        if (TopFadeColor is not { } c)
        {
            _fade.IsVisible = false;
            return;
        }
        _fade.IsVisible = true;
        _fade.Background = new ImmutableLinearGradientBrush(
            [new ImmutableGradientStop(0, c), new ImmutableGradientStop(1, Color.FromArgb(0, c.R, c.G, c.B))],
            startPoint: new RelativePoint(0, 0, RelativeUnit.Relative), endPoint: new RelativePoint(0, 1, RelativeUnit.Relative));
        PlaceTopFade();
    }

    private void PlaceTopFade()
    {
        if (!_fade.IsVisible) return;
        var y = _scroll.Offset.Y;
        // at the very top nothing is scrolled away (and a compartment's banner keeps its colour band)
        _fade.Opacity = Math.Clamp(y / TopFadeHeight, 0, 1);
        _fade.RenderTransform = new TranslateTransform(0, y - _content.Margin.Top);
    }

    public CoverGrid()
    {
        Focusable = false;
        // the row presenter IS the container: the virtualizing panel recycles it in place (hidden, kept attached) and
        // only swaps its DataContext. With a ContentPresenter container the items control cleared the content on
        // recycling and every row scrolled into view built 8 new tiles (M3 perf: 2096 tiles in 118 moves).
        _rows = new RowItems(this)
        {
            ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel { Orientation = Orientation.Vertical, ClipToBounds = false }),
            ClipToBounds = false,
        };
        _headerHost = new ContentControl();
        _footerHost = new ContentControl();
        _outer = new StackPanel { Children = { _headerHost, _rows, _footerHost, _tail } };
        // one focus layer per grid, inside the scroll content (scrolls along): cone behind the rows, ring above them
        _content = new Panel();
        _focus = new FocusLayer(_content, _rows) { BoardFront = ShelfBoard.EdgeHeight };
        _boards = new BoardLayer(this);
        _fade = new Border
        {
            Height = TopFadeHeight, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false, IsVisible = false,
        };
        _content.Children.Add(_focus.Back);
        _content.Children.Add(_boards);
        _content.Children.Add(_outer);
        _content.Children.Add(_fade);
        _content.Children.Add(_focus.Front);
        _scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = _content,
        };
        // the grid brings the focused row into view itself, with the 40 px margin of DESIGN 8.4; the scroll viewer's own
        // bring-into-view on focus scrolled a few pixels on its own (a compartment banner then lost its colour band)
        ScrollViewer.SetBringIntoViewOnFocusChange(_scroll, false);
        Content = _scroll;
        _scroll.SizeChanged += (_, _) => UpdateLayoutMetrics();
        _scroll.ScrollChanged += (_, _) => { RaiseViewportChanged(); PlaceTopFade(); Scrolled?.Invoke(); };
        _rows.LayoutUpdated += (_, _) => InvalidateBoardsIfMoved();
        // tunnel: tiles (buttons) or the focus manager must not consume the arrows before the grid sees them
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        AddHandler(GotFocusEvent, OnTileGotFocus, RoutingStrategies.Bubble);
        AddHandler(Button.ClickEvent, OnTileClick, RoutingStrategies.Bubble);
        AddHandler(PointerPressedEvent, (_, e) =>
        {
            _pressIndex = e.GetCurrentPoint(this).Properties.IsLeftButtonPressed ? IndexOfTile(e.Source as Visual) : -1;
            // before the press moves the focus: was this row already the selected, focused one?
            _pressWasSelected = _pressIndex >= 0 && _pressIndex == SelectedIndex && IsKeyboardFocusWithin;
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() => _pressIndex = -1),
            RoutingStrategies.Bubble, handledEventsToo: true);
        ApplyPadding();
    }

    public IEnumerable? ItemsSource { get => GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }
    public IDataTemplate? ItemTemplate { get => GetValue(ItemTemplateProperty); set => SetValue(ItemTemplateProperty, value); }
    public double ItemWidth { get => GetValue(ItemWidthProperty); set => SetValue(ItemWidthProperty, value); }
    public double CoverAspect { get => GetValue(CoverAspectProperty); set => SetValue(CoverAspectProperty, value); }
    public double CaptionHeight { get => GetValue(CaptionHeightProperty); set => SetValue(CaptionHeightProperty, value); }
    public double Spacing { get => GetValue(SpacingProperty); set => SetValue(SpacingProperty, value); }
    public double RowSpacing { get => GetValue(RowSpacingProperty); set => SetValue(RowSpacingProperty, value); }
    public double MaxStretch { get => GetValue(MaxStretchProperty); set => SetValue(MaxStretchProperty, value); }
    public Thickness ContentPadding { get => GetValue(ContentPaddingProperty); set => SetValue(ContentPaddingProperty, value); }
    public ICommand? ItemInvokedCommand { get => GetValue(ItemInvokedCommandProperty); set => SetValue(ItemInvokedCommandProperty, value); }
    public Control? Header { get => GetValue(HeaderProperty); set => SetValue(HeaderProperty, value); }
    public Control? Footer { get => GetValue(FooterProperty); set => SetValue(FooterProperty, value); }
    public bool ShowBoard { get => GetValue(ShowBoardProperty); set => SetValue(ShowBoardProperty, value); }
    public bool HeaderBleed { get => GetValue(HeaderBleedProperty); set => SetValue(HeaderBleedProperty, value); }
    public double StickyInset { get => GetValue(StickyInsetProperty); set => SetValue(StickyInsetProperty, value); }
    public bool SnapToEnd { get => GetValue(SnapToEndProperty); set => SetValue(SnapToEndProperty, value); }
    public double PlaceholderAspect { get => GetValue(PlaceholderAspectProperty); set => SetValue(PlaceholderAspectProperty, value); }

    /// <summary>The grid's focus visuals (light colour: null = the user's light colour; a system's light in its context).</summary>
    public FocusLayer FocusVisuals => _focus;

    /// <summary>Index of the selected (last focused) item; -1 = none.</summary>
    public int SelectedIndex
    {
        get => _selectedIndex;
        set => SetAndRaise(SelectedIndexProperty, ref _selectedIndex, value < -1 || value >= _items.Count ? -1 : value);
    }

    public object? SelectedItem => _selectedIndex >= 0 && _selectedIndex < _items.Count ? _items[_selectedIndex] : null;

    /// <summary>Tiles per row (≥ 1 once measured).</summary>
    public int Columns => _columns;
    public int ItemCount => _items.Count;
    public int RowCount => _columns <= 0 ? 0 : (_items.Count + _columns - 1) / _columns;
    public double TileWidth => _tileWidth;
    public double TileHeight => _tileHeight;
    internal ScrollViewer ScrollViewer => _scroll;
    internal ItemsControl RowsControl => _rows;
    internal int RebuildCount { get; private set; }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ItemsSourceProperty) OnItemsSourceChanged();
        else if (change.Property == ItemWidthProperty || change.Property == SpacingProperty || change.Property == MaxStretchProperty
                 || change.Property == CoverAspectProperty || change.Property == CaptionHeightProperty || change.Property == RowSpacingProperty)
            UpdateLayoutMetrics(force: true);
        else if (change.Property == ContentPaddingProperty || change.Property == HeaderBleedProperty) { ApplyPadding(); UpdateLayoutMetrics(); PlaceTopFade(); }
        else if (change.Property == TopFadeColorProperty) UpdateTopFade();
        else if (change.Property == PlaceholderAspectProperty) foreach (var presenter in RealizedRows()) presenter.ApplySize();
        else if (change.Property == HeaderProperty) _headerHost.Content = Header;
        else if (change.Property == FooterProperty) _footerHost.Content = Footer;
        else if (change.Property == ItemTemplateProperty) Rebuild(keepFocus: false);
    }

    private void ApplyPadding()
    {
        var p = ContentPadding;
        _content.Margin = p;
        _fade.Margin = new Thickness(-p.Left, 0, -p.Right, 0);
        // a bleeding header covers the padding left, right and above; the padding's top stays as the gap below it
        _headerHost.Margin = HeaderBleed ? new Thickness(-p.Left, -p.Top, -p.Right, p.Top) : default;
    }

    private void OnItemsSourceChanged()
    {
        if (_observed != null) _observed.CollectionChanged -= OnCollectionChanged;
        _observed = ItemsSource as INotifyCollectionChanged;
        if (_observed != null) _observed.CollectionChanged += OnCollectionChanged;
        // a refreshed list (same games, new list instance) keeps the focused game focused and the scroll position
        var hadFocus = IsKeyboardFocusWithin;
        var selected = SelectedItem;
        var oldIndex = _selectedIndex;
        ReadItems();
        var index = selected != null && _indexOf.TryGetValue(selected, out var i) ? i : -1;
        if (index < 0 && hadFocus && oldIndex >= 0 && _items.Count > 0)
        {
            // the focused item left the list (e.g. un-favourited while "Favoriten" is on): stay at the same place
            index = Math.Min(oldIndex, _items.Count - 1);
        }
        if (index < 0) _scroll.Offset = default;
        SelectedIndex = index >= 0 ? index : hadFocus && _items.Count > 0 ? 0 : -1;
        Rebuild(keepFocus: hadFocus);
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        var selected = SelectedItem;
        ReadItems();
        if (selected != null && _indexOf.TryGetValue(selected, out var i)) SelectedIndex = i;
        Rebuild(keepFocus: true);
    }

    private void ReadItems()
    {
        _tail.Height = 0;
        _items = ItemsSource?.Cast<object>().ToList() ?? [];
        var map = new Dictionary<object, int>(_items.Count, ReferenceEqualityComparer.Instance);
        for (var i = 0; i < _items.Count; i++) map.TryAdd(_items[i], i);
        _indexOf = map;
        if (_selectedIndex >= _items.Count) SelectedIndex = _items.Count - 1;
    }

    /// <summary>Recomputes columns and tile size from the viewport; rebuilds rows only when the column count changes.</summary>
    private void UpdateLayoutMetrics(bool force = false)
    {
        var p = ContentPadding;
        var available = _scroll.Bounds.Width - p.Left - p.Right - 0.5; // slack against layout rounding
        if (available <= 0) return;
        var spacing = Spacing;
        var cols = Math.Max(1, (int)Math.Floor((available + spacing) / (ItemWidth + spacing)));
        var width = Math.Min(ItemWidth * MaxStretch, (available - (cols - 1) * spacing) / cols);
        width = Math.Floor(width);
        var height = Math.Round(width * CoverAspect + CaptionHeight);
        var sizeChanged = Math.Abs(width - _tileWidth) > 0.5 || Math.Abs(height - _tileHeight) > 0.5;
        _tileWidth = width;
        _tileHeight = height;
        if (cols != _columns || force)
        {
            _columns = cols;
            Rebuild(keepFocus: true);
        }
        else if (sizeChanged)
        {
            foreach (var presenter in RealizedRows()) presenter.ApplySize();
        }
    }

    private IEnumerable<CoverRowPresenter> RealizedRows() =>
        _rows.GetRealizedContainers().OfType<CoverRowPresenter>();

    /// <summary>Re-chunks the items into rows of <see cref="Columns"/>.</summary>
    private void Rebuild(bool keepFocus)
    {
        if (_columns <= 0) return;
        var hadFocus = keepFocus && IsKeyboardFocusWithin;
        var keep = _selectedIndex;
        var rows = new List<CoverRow>((_items.Count + _columns - 1) / _columns);
        for (var start = 0; start < _items.Count; start += _columns)
            rows.Add(new CoverRow(rows.Count, _items.Skip(start).Take(_columns).ToList()));
        _rows.ItemsSource = rows;
        RebuildCount++;
        try { RowsRebuilt?.Invoke(); } catch { }
        Dispatcher.UIThread.Post(RaiseViewportChanged, DispatcherPriority.Loaded);
        if (hadFocus && keep >= 0)
            Dispatcher.UIThread.Post(() => FocusIndex(keep), DispatcherPriority.Loaded);
        else if (_pendingFocus >= 0 && _items.Count > 0)
        {
            var pending = Math.Min(_pendingFocus, _items.Count - 1);
            _pendingFocus = -1;
            Dispatcher.UIThread.Post(() => FocusIndex(pending), DispatcherPriority.Loaded);
        }
    }

    /// <summary>Moves the focus in a direction. Returns false at the grid edge (the caller may move focus elsewhere).</summary>
    public bool Move(NavAction action)
    {
        if (_items.Count == 0 || _columns <= 0) return false;
        var current = _selectedIndex < 0 ? 0 : _selectedIndex;
        if (!IsKeyboardFocusWithin)
        {
            FocusIndex(current);
            return true;
        }
        var col = current % _columns;
        var row = current / _columns;
        var lastRow = (_items.Count - 1) / _columns;
        int target;
        switch (action)
        {
            case NavAction.Left:
                if (col == 0) return false;
                target = current - 1;
                break;
            case NavAction.Right:
                if (col == _columns - 1 || current + 1 >= _items.Count) return false;
                target = current + 1;
                break;
            case NavAction.Up:
                if (row == 0) return false;
                target = current - _columns;
                break;
            case NavAction.Down:
                if (row >= lastRow) return false;
                target = Math.Min(current + _columns, _items.Count - 1);
                break;
            case NavAction.PageLeft:
                target = Math.Max(0, current - _columns * VisibleRows());
                break;
            case NavAction.PageRight:
                target = Math.Min(_items.Count - 1, current + _columns * VisibleRows());
                break;
            default:
                return false;
        }
        FocusIndex(target);
        return true;
    }

    private int VisibleRows()
    {
        var h = _tileHeight + RowSpacing;
        return h <= 0 ? 1 : Math.Max(1, (int)(_scroll.Viewport.Height / h));
    }

    /// <summary>
    /// Focuses item <paramref name="index"/> now, or – while the grid is still empty / not measured (data loading) – as
    /// soon as its rows exist. Returns true when the focus was set or scheduled.
    /// </summary>
    public bool FocusWhenReady(int index)
    {
        if (_items.Count > 0 && _columns > 0) return FocusIndex(Math.Min(Math.Max(0, index), _items.Count - 1));
        _pendingFocus = Math.Max(0, index);
        return true;
    }

    /// <summary>Cancels a focus request made by <see cref="FocusWhenReady"/>.</summary>
    public void CancelPendingFocus() => _pendingFocus = -1;

    /// <summary>Focuses the tile of item <paramref name="index"/>, realizing and scrolling its row into view first.</summary>
    public bool FocusIndex(int index)
    {
        if (index < 0 || index >= _items.Count || _columns <= 0) return false;
        SelectedIndex = index;
        var rowIndex = index / _columns;
        var col = index % _columns;
        var tile = TileAt(rowIndex, col);
        if (tile == null)
        {
            _rows.ScrollIntoView(rowIndex);
            _rows.UpdateLayout();
            tile = TileAt(rowIndex, col);
        }
        if (tile == null) return false;
        tile.Focus(NavigationMethod.Directional);
        BringRowIntoView(tile);
        return true;
    }

    /// <summary>Space kept below the focused row (the footer's fade lies over the page's last 40 px, DESIGN 4.6 / 8.4).</summary>
    private const double BottomMargin = 40;

    /// <summary>
    /// Brings the focused row into view, SNAPPED to row boundaries (M3 QA): the top row in view always starts
    /// <see cref="SnapHeadroom"/> below the viewport's top edge – never a row cut through its boxes or captions under
    /// the fixed header. Only the bottom edge may show part of a row. Scrolling up puts the focused row at the top;
    /// scrolling down takes the smallest snapped offset that shows the focused row with its caption and
    /// <see cref="BottomMargin"/> below. Row 0 keeps the header (a compartment's banner) in view.
    /// </summary>
    private void BringRowIntoView(Control tile)
    {
        var p = tile.TranslatePoint(new Point(0, 0), _content);
        if (p == null || _columns <= 0) return;
        var index = IndexOfTile(tile);
        if (index < 0) return;
        var row = index / _columns;
        var top = p.Value.Y + _content.Margin.Top;            // the row's slot top in scroll coordinates
        var bottom = top + tile.Bounds.Height + BottomMargin;  // caption end + margin
        var viewport = _scroll.Viewport.Height;
        var offset = _scroll.Offset.Y;
        var headroom = row == 0 ? top : SnapHeadroom;
        if (top - headroom >= offset - 0.5 && bottom <= offset + viewport + 0.5) return; // in view as it is
        double target;
        if (top - headroom < offset) target = SnapOffset(row);
        else
        {
            // down: the first snapped offset at which the focused row fits; a row taller than the view goes to the top
            target = SnapOffset(row);
            for (var k = 0; k < row; k++)
            {
                var o = SnapOffset(k);
                if (bottom <= o + viewport) { target = o; break; }
            }
        }
        ScrollToSnapped(target);
    }

    /// <summary>
    /// Scrolls to <paramref name="target"/>; the scroll viewer clamps at the end of the content, so with
    /// <see cref="SnapToEnd"/> the spacer after the footer first grows until the snapped position is reachable.
    /// </summary>
    private void ScrollToSnapped(double target)
    {
        if (SnapToEnd && target > 0)
        {
            var max = _scroll.Extent.Height - _scroll.Viewport.Height;
            if (target > max + 0.5)
            {
                _tail.Height = Math.Ceiling(_tail.Bounds.Height + target - max);
                _scroll.UpdateLayout();
            }
        }
        _scroll.Offset = _scroll.Offset.WithY(target);
    }

    internal Control? TileAt(int rowIndex, int col)
    {
        var container = _rows.ContainerFromIndex(rowIndex);
        var presenter = container switch
        {
            CoverRowPresenter row => row,
            ContentPresenter cp => cp.Child as CoverRowPresenter,
            ContentControl cc => cc.Content as CoverRowPresenter ?? cc.Presenter?.Child as CoverRowPresenter,
            _ => null,
        };
        if (presenter == null) return null;
        return col < presenter.Children.Count && presenter.Children[col].IsVisible ? presenter.Children[col] as Control : null;
    }

    /// <summary>The tile control currently showing <paramref name="index"/> (null when virtualized away).</summary>
    public Control? TileForIndex(int index) => _columns <= 0 || index < 0 ? null : TileAt(index / _columns, index % _columns);

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || IndexOfTile(e.Source as Visual) < 0) return; // only keys aimed at a tile (not a header text box)
        var action = e.Key switch
        {
            Key.Left => NavAction.Left,
            Key.Right => NavAction.Right,
            Key.Up => NavAction.Up,
            Key.Down => NavAction.Down,
            Key.PageUp => NavAction.PageLeft,
            Key.PageDown => NavAction.PageRight,
            _ => (NavAction?)null,
        };
        if (e.Key == Key.Home && _items.Count > 0) { FocusIndex(0); e.Handled = true; return; }
        if (e.Key == Key.End && _items.Count > 0) { FocusIndex(_items.Count - 1); e.Handled = true; return; }
        if (action != null && Move(action.Value)) e.Handled = true;
    }

    private void OnTileGotFocus(object? sender, FocusChangedEventArgs e)
    {
        var idx = IndexOfTile(e.Source as Visual);
        if (idx < 0) return;
        SelectedIndex = idx;
        // focus from outside the grid (the ticker above, a header) lands on a tile: keep its row in view
        if (e.NavigationMethod == NavigationMethod.Directional && TileForIndex(idx) is { } tile) BringRowIntoView(tile);
    }

    /// <summary>Item index of the tile containing <paramref name="v"/>, or -1.</summary>
    private int IndexOfTile(Visual? v)
    {
        for (; v != null && v != this; v = v.GetVisualParent())
        {
            if (v is Control { Parent: CoverRowPresenter } tile && tile.DataContext is { } item && _indexOf.TryGetValue(item, out var idx))
                return idx;
        }
        return -1;
    }

    private void OnTileClick(object? sender, RoutedEventArgs e)
    {
        if (e.Source is not Control c) return;
        for (Visual? v = c; v != null && v != this; v = v.GetVisualParent())
        {
            if (v is Control { Parent: CoverRowPresenter } tile && tile.DataContext is { } item && _indexOf.TryGetValue(item, out var idx))
            {
                SelectedIndex = idx;
                if (ClickSelectsFirst && _pressIndex == idx && !_pressWasSelected)
                {
                    e.Handled = true; // the first click selects (the preview shows it); the page does not open it
                    return;
                }
                ItemInvoked?.Invoke(this, new CoverGridItemEventArgs(item, idx));
                if (ItemInvokedCommand?.CanExecute(item) == true) ItemInvokedCommand.Execute(item);
                return;
            }
        }
    }

    /// <summary>
    /// The shelf edges under the realized rows (DESIGN 4.2: 10 px over the full content width) and the contact
    /// shadow of every box on them (from the boxes' ArtRects). ONE layer per grid behind the rows, in the scroll
    /// content; static brushes; redrawn only when rows are realized/laid out or a cover changes its rectangle.
    /// </summary>
    private sealed class BoardLayer(CoverGrid grid) : Control
    {
        static BoardLayer() { IsHitTestVisibleProperty.OverrideDefaultValue<BoardLayer>(false); }

        public override void Render(DrawingContext context)
        {
            if (!grid.ShowBoard) return;
            var width = Bounds.Width;
            foreach (var row in grid.RealizedRows())
            {
                if (row.Children.Count == 0 || row.Children[0] is not GameTile first || first.CoverBottomInTile() is not { } bottom) continue;
                if (row.TranslatePoint(new Point(0, 0), this) is not { } origin) continue;
                var y = origin.Y + first.Bounds.Y + bottom;
                ShelfBoard.DrawEdge(context, new Rect(0, y, width, ShelfBoard.EdgeHeight), this);
                foreach (var child in row.Children)
                {
                    if (child is not GameTile { IsVisible: true } tile || tile.ArtRectInTile() is not { } art || art.Width <= 0) continue;
                    context.FillRectangle(ShelfBoard.ContactBrush, new Rect(origin.X + tile.Bounds.X + art.X, y, art.Width, ShelfBoard.EdgeHeight));
                }
            }
        }
    }

    /// <summary>
    /// Row view: a horizontal panel whose children are the item tiles. On recycling (DataContext = another row)
    /// existing tiles are reused and only get the new item as DataContext.
    /// </summary>
    internal sealed class CoverRowPresenter : StackPanel
    {
        private readonly CoverGrid _grid;

        public CoverRowPresenter(CoverGrid grid)
        {
            _grid = grid;
            Orientation = Orientation.Horizontal;
            ClipToBounds = false;
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            ApplySize(); // the tile size may have changed while it waited in the pool
        }

        private void OnTileArtChanged(object? sender, EventArgs e) => _grid._boards.InvalidateVisual();

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);
            Sync();
        }

        private void Sync()
        {
            if (DataContext is not CoverRow row) return;
            var template = _grid.ItemTemplate;
            // tiles beyond a short (last) row are hidden, not removed: the next full row reuses them
            for (var i = row.Items.Count; i < Children.Count; i++)
            {
                Children[i].IsVisible = false;
                Children[i].DataContext = null;
            }
            for (var i = 0; i < row.Items.Count; i++)
            {
                var item = row.Items[i];
                if (i < Children.Count)
                {
                    Children[i].DataContext = item;
                    Children[i].IsVisible = true;
                    continue;
                }
                var tile = template?.Build(item) ?? new TextBlock { Text = item.ToString() };
                tile.DataContext = item;
                if (tile is GameTile gt) gt.ArtChanged += OnTileArtChanged;
                Children.Add(tile);
            }
            ApplySize();
        }

        public void ApplySize()
        {
            Spacing = _grid.Spacing;
            Margin = new Thickness(0, 0, 0, _grid.RowSpacing);
            foreach (var child in Children)
            {
                child.Width = _grid._tileWidth;
                child.Height = _grid._tileHeight;
                if (child is GameTile tile) tile.PlaceholderFormat = _grid.PlaceholderAspect;
            }
        }
    }
}
