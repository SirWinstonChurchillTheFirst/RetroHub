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

    private readonly ScrollViewer _scroll;
    private readonly ItemsControl _rows;
    private readonly StackPanel _outer;
    private readonly ContentControl _headerHost;
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

    public CoverGrid()
    {
        Focusable = false;
        _rows = new ItemsControl
        {
            ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel { Orientation = Orientation.Vertical, ClipToBounds = false }),
            ClipToBounds = false,
            ItemTemplate = new FuncDataTemplate<CoverRow>((row, _) => new CoverRowPresenter(this), supportsRecycling: true),
        };
        _headerHost = new ContentControl();
        _outer = new StackPanel { Children = { _headerHost, _rows } };
        _scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = _outer,
        };
        Content = _scroll;
        _scroll.SizeChanged += (_, _) => UpdateLayoutMetrics();
        // tunnel: tiles (buttons) or the focus manager must not consume the arrows before the grid sees them
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        AddHandler(GotFocusEvent, OnTileGotFocus, RoutingStrategies.Bubble);
        AddHandler(Button.ClickEvent, OnTileClick, RoutingStrategies.Bubble);
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
        else if (change.Property == ContentPaddingProperty) { ApplyPadding(); UpdateLayoutMetrics(); }
        else if (change.Property == HeaderProperty) _headerHost.Content = Header;
        else if (change.Property == ItemTemplateProperty) Rebuild(keepFocus: false);
    }

    private void ApplyPadding()
    {
        var p = ContentPadding;
        _outer.Margin = p;
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
        _rows.GetRealizedContainers().Select(c => c is ContentPresenter cp ? cp.Child as CoverRowPresenter : (c as ContentControl)?.Content as CoverRowPresenter)
            .OfType<CoverRowPresenter>();

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

    /// <summary>Keeps a margin above/below the focused row so the scaled tile and its glow stay visible.</summary>
    private void BringRowIntoView(Control tile)
    {
        var p = tile.TranslatePoint(new Point(0, 0), _outer);
        if (p == null) return;
        var margin = 24;
        var top = p.Value.Y + _outer.Margin.Top - margin;
        var bottom = p.Value.Y + _outer.Margin.Top + tile.Bounds.Height + margin;
        var offset = _scroll.Offset;
        if (top < offset.Y) _scroll.Offset = offset.WithY(Math.Max(0, top));
        else if (bottom > offset.Y + _scroll.Viewport.Height) _scroll.Offset = offset.WithY(bottom - _scroll.Viewport.Height);
    }

    internal Control? TileAt(int rowIndex, int col)
    {
        var container = _rows.ContainerFromIndex(rowIndex);
        var presenter = container switch
        {
            ContentPresenter cp => cp.Child as CoverRowPresenter,
            ContentControl cc => cc.Content as CoverRowPresenter ?? cc.Presenter?.Child as CoverRowPresenter,
            _ => null,
        };
        if (presenter == null) return null;
        return col < presenter.Children.Count ? presenter.Children[col] as Control : null;
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
        if (idx >= 0) SelectedIndex = idx;
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
                ItemInvoked?.Invoke(this, new CoverGridItemEventArgs(item, idx));
                if (ItemInvokedCommand?.CanExecute(item) == true) ItemInvokedCommand.Execute(item);
                return;
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

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);
            Sync();
        }

        private void Sync()
        {
            if (DataContext is not CoverRow row) return;
            var template = _grid.ItemTemplate;
            while (Children.Count > row.Items.Count) Children.RemoveAt(Children.Count - 1);
            for (var i = 0; i < row.Items.Count; i++)
            {
                var item = row.Items[i];
                if (i < Children.Count)
                {
                    Children[i].DataContext = item;
                    continue;
                }
                var tile = template?.Build(item) ?? new TextBlock { Text = item.ToString() };
                tile.DataContext = item;
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
            }
        }
    }
}
