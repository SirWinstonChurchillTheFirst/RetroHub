using System.Collections;
using System.Windows.Input;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using RetroHub.App.Input;
using RetroHub.App.Theming;

namespace RetroHub.App.Controls;

/// <summary>
/// A shelf board with boxes ("Brett", DESIGN 7.1): the boxes stand on a 24 px board, a paper label
/// (<see cref="Header"/>) hangs at the left of the board front instead of a heading; the action ("Alle anzeigen") is a
/// spine standing at the end of the board, never a text link. Focus visuals come from one <see cref="FocusLayer"/>
/// that moves with the row.
/// <para>Horizontal anchor: the row glides (TranslateTransform, Glide 220 ms; 90 ms linear while a direction is held)
/// so that the focused box stands with its left edge at <see cref="AnchorX"/> (from the board's start on). The offset
/// is computed from the TARGET widths of the items (a system spine that is about to open counts with its front width),
/// so the row never jerks twice. The board and the label stay. Small item counts (≤ 16): no virtualization.</para>
/// </summary>
public class Shelf : TemplatedControl
{
    public static readonly StyledProperty<string?> HeaderProperty = AvaloniaProperty.Register<Shelf, string?>(nameof(Header));
    public static readonly StyledProperty<string?> SubHeaderProperty = AvaloniaProperty.Register<Shelf, string?>(nameof(SubHeader));
    public static readonly StyledProperty<IEnumerable?> ItemsSourceProperty = AvaloniaProperty.Register<Shelf, IEnumerable?>(nameof(ItemsSource));
    public static readonly StyledProperty<IDataTemplate?> ItemTemplateProperty = AvaloniaProperty.Register<Shelf, IDataTemplate?>(nameof(ItemTemplate));
    public static readonly StyledProperty<string?> ActionLabelProperty = AvaloniaProperty.Register<Shelf, string?>(nameof(ActionLabel));
    public static readonly StyledProperty<ICommand?> ActionCommandProperty = AvaloniaProperty.Register<Shelf, ICommand?>(nameof(ActionCommand));
    public static readonly StyledProperty<double> ItemSpacingProperty = AvaloniaProperty.Register<Shelf, double>(nameof(ItemSpacing), 24);
    /// <summary>Inset of the boxes inside the board (the board itself runs over the full width).</summary>
    public static readonly StyledProperty<Thickness> ItemsMarginProperty = AvaloniaProperty.Register<Shelf, Thickness>(nameof(ItemsMargin));
    public static readonly StyledProperty<Thickness> LabelMarginProperty = AvaloniaProperty.Register<Shelf, Thickness>(nameof(LabelMargin));
    /// <summary>Height of the boxes on this board (the "Alle anzeigen" spine matches it).</summary>
    public static readonly StyledProperty<double> ItemHeightProperty = AvaloniaProperty.Register<Shelf, double>(nameof(ItemHeight), 200);
    /// <summary>Free space above the boxes that the light cone may use (the gap to the board above).</summary>
    public static readonly StyledProperty<double> TopGapProperty = AvaloniaProperty.Register<Shelf, double>(nameof(TopGap), 40);
    /// <summary>Where the focused box's left edge stands, measured from the board's first box (DESIGN 7.1: x 240 − margin 96).</summary>
    public static readonly StyledProperty<double> AnchorXProperty = AvaloniaProperty.Register<Shelf, double>(nameof(AnchorX), 144);
    /// <summary>The plain (vertical) mouse wheel scrolls this row sideways (a row with nothing to scroll vertically).</summary>
    public static readonly StyledProperty<bool> WheelScrollsRowProperty = AvaloniaProperty.Register<Shelf, bool>(nameof(WheelScrollsRow));
    /// <summary>The board stands in front of this system's wall (Konsolen): its front takes the wall's tone (ShelfBoard.WallLook).</summary>
    public static readonly StyledProperty<SystemLook?> BoardLookProperty = AvaloniaProperty.Register<Shelf, SystemLook?>(nameof(BoardLook));
    /// <summary>A drawn bookend closes the row (the "Noch leer" edge: the row ends on purpose, not cut off).</summary>
    public static readonly StyledProperty<bool> BookendProperty = AvaloniaProperty.Register<Shelf, bool>(nameof(Bookend));

    /// <summary>The whole shelf: boxes, the 24 px board and 4 px below it.</summary>
    public const double BoardHeight = 24, BelowBoard = 4;

    static Shelf()
    {
        ClipToBoundsProperty.OverrideDefaultValue<Shelf>(false);
    }

    public string? Header { get => GetValue(HeaderProperty); set => SetValue(HeaderProperty, value); }
    public string? SubHeader { get => GetValue(SubHeaderProperty); set => SetValue(SubHeaderProperty, value); }
    public IEnumerable? ItemsSource { get => GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }
    public IDataTemplate? ItemTemplate { get => GetValue(ItemTemplateProperty); set => SetValue(ItemTemplateProperty, value); }
    public string? ActionLabel { get => GetValue(ActionLabelProperty); set => SetValue(ActionLabelProperty, value); }
    public ICommand? ActionCommand { get => GetValue(ActionCommandProperty); set => SetValue(ActionCommandProperty, value); }
    public double ItemSpacing { get => GetValue(ItemSpacingProperty); set => SetValue(ItemSpacingProperty, value); }
    public Thickness ItemsMargin { get => GetValue(ItemsMarginProperty); set => SetValue(ItemsMarginProperty, value); }
    public Thickness LabelMargin { get => GetValue(LabelMarginProperty); set => SetValue(LabelMarginProperty, value); }
    public double ItemHeight { get => GetValue(ItemHeightProperty); set => SetValue(ItemHeightProperty, value); }
    public double TopGap { get => GetValue(TopGapProperty); set => SetValue(TopGapProperty, value); }
    public double AnchorX { get => GetValue(AnchorXProperty); set => SetValue(AnchorXProperty, value); }
    public SystemLook? BoardLook { get => GetValue(BoardLookProperty); set => SetValue(BoardLookProperty, value); }
    public bool WheelScrollsRow { get => GetValue(WheelScrollsRowProperty); set => SetValue(WheelScrollsRowProperty, value); }
    public bool Bookend { get => GetValue(BookendProperty); set => SetValue(BookendProperty, value); }

    /// <summary>Width the row needs after its last item (the bookend and its spacing).</summary>
    private double EndExtra => Bookend ? ItemSpacing + Controls.Bookend.RowSlotWidth : 0;

    /// <summary>The focus visuals of this shelf (null until the template is applied).</summary>
    public FocusLayer? FocusVisuals { get; private set; }

    /// <summary>The index of the last focused item on this board (restored when the focus comes back, DESIGN 8.4).</summary>
    public int RememberedIndex { get; private set; }

    /// <summary>Raised when an item of this shelf got the focus: (shelf, item control, index, count).</summary>
    public event Action<Shelf, Control, int, int>? ItemFocused;

    /// <summary>Raised when the mouse pointer came onto an item of this shelf: (shelf, item control, index, count).</summary>
    public event Action<Shelf, Control, int, int>? ItemHovered;

    private Control? _hovered;

    private ShelfTrack? _track;
    private Panel? _row;
    private ShelfEdge? _edgeLeft, _edgeRight;
    private readonly TranslateTransform _offset = new();
    private double _target;

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _track = e.NameScope.Find<ShelfTrack>("PART_Track");
        _row = e.NameScope.Find<Panel>("PART_Row");
        if (_track == null) return;
        _track.RenderTransform = _offset;
        FocusVisuals = new FocusLayer(_track, _track) { BoardFront = BoardHeight, ConeHeadroom = TopGap, ConeOutsideBounds = true };
        _track.Children.Insert(0, FocusVisuals.Back);
        // contact shadows of every box on the board (10 px #000 55 % → 0, on the box's covered width)
        var contacts = new ContactLayer(this);
        _track.Children.Insert(1, contacts);
        if (_row != null) _row.LayoutUpdated += (_, _) => contacts.InvalidateVisual();
        _track.Children.Add(FocusVisuals.Front);
        _track.AddHandler(GotFocusEvent, OnItemGotFocus, RoutingStrategies.Bubble, handledEventsToo: true);
        _track.AddHandler(PointerPressedEvent, OnTrackPointerPressed, RoutingStrategies.Tunnel);
        _track.AddHandler(PointerMovedEvent, OnTrackPointerMoved, RoutingStrategies.Bubble, handledEventsToo: true);
        _edgeLeft = e.NameScope.Find<ShelfEdge>("PART_EdgeLeft");
        _edgeRight = e.NameScope.Find<ShelfEdge>("PART_EdgeRight");
        if (_edgeLeft != null) _edgeLeft.Clicked += () => PageBy(-1);
        if (_edgeRight != null) _edgeRight.Clicked += () => PageBy(+1);
        if (_row != null) _row.SizeChanged += (_, _) => UpdateEdges();
        UpdateEdges();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TopGapProperty && FocusVisuals != null) FocusVisuals.ConeHeadroom = TopGap;
        if (change.Property == ItemsSourceProperty) { RememberedIndex = 0; SetOffset(0, animate: false); }
        if (change.Property == BoundsProperty || change.Property == ItemHeightProperty) Avalonia.Threading.Dispatcher.UIThread.Post(Reanchor);
    }

    /// <summary>The focusable items of the row in order: boxes / system spines, then the "Alle" spine.</summary>
    public IReadOnlyList<Control> Items()
    {
        if (_row == null) return [];
        return _row.GetVisualDescendants().OfType<Button>()
            .Where(b => b.Focusable && b.IsEffectivelyVisible && b.FindAncestorOfType<Button>() == null)
            .Cast<Control>().ToList();
    }

    private void OnItemGotFocus(object? sender, FocusChangedEventArgs e)
    {
        if (e.Source is not Visual v) return;
        var items = Items();
        var index = -1;
        for (var i = 0; i < items.Count; i++)
            if (items[i] == v || v.FindAncestorOfType<Button>(includeSelf: true) == items[i]) { index = i; break; }
        if (index < 0) return;
        RememberedIndex = index;
        // a mouse press focuses on PRESS: gliding now would carry the box away under the pointer and the release would
        // miss it (no click). The row stays where it is; the next key/controller move anchors it.
        if (e.NavigationMethod != NavigationMethod.Pointer) Anchor(items, index);
        ItemFocused?.Invoke(this, items[index], index, items.Count);
    }

    /// <summary>
    /// A press on a closed system spine turns its front forward first (directional focus, the row glides to the anchor)
    /// instead of starting a click: closing the open front would move the pressed spine away. The open front then
    /// opens its page on the next click.
    /// </summary>
    private void OnTrackPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if ((e.Source as Visual)?.FindAncestorOfType<SystemSpine>(includeSelf: true) is not { Opens: true, IsOpen: false } spine) return;
        var items = Items();
        var index = IndexOf(items, spine);
        if (index < 0) return;
        e.Handled = true;
        FocusItem(index);
    }

    private void OnTrackPointerMoved(object? sender, PointerEventArgs e)
    {
        var items = Items();
        var index = e.Source is Control c ? IndexOf(items, c) : -1;
        var item = index >= 0 ? items[index] : null;
        if (item == _hovered) return;
        _hovered = item;
        if (item != null) ItemHovered?.Invoke(this, item, index, items.Count);
    }

    // ===== mouse: the wheel and the two "Winkel" plates at the board's right end move the row sideways =====

    /// <summary>Total width of the row and the visible width (current item widths).</summary>
    private (double Total, double View) RowExtent()
    {
        var items = Items();
        double total = 0;
        for (var i = 0; i < items.Count; i++) total += TargetWidth(items[i], false) + (i < items.Count - 1 ? ItemSpacing : 0);
        total += EndExtra;
        return (total, Bounds.Width - ItemsMargin.Left - ItemsMargin.Right);
    }

    /// <summary>Moves the row by <paramref name="dx"/> px (negative = towards its end), clamped. False when it cannot move.</summary>
    public bool ScrollRow(double dx)
    {
        var (total, view) = RowExtent();
        var min = Math.Min(0, view - total);
        var to = Math.Round(Math.Clamp(_target + dx, min, 0));
        if (Math.Abs(to - _target) < 0.5) return false;
        SetOffset(to, animate: true);
        return true;
    }

    /// <summary>One view width (less one box) towards the end (+1) or the start (−1).</summary>
    public bool PageBy(int direction)
    {
        var (_, view) = RowExtent();
        return ScrollRow(-direction * Math.Max(ItemHeight, view - ItemHeight));
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (e.Handled) return;
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var d = Math.Abs(e.Delta.X) > Math.Abs(e.Delta.Y) ? e.Delta.X : shift || WheelScrollsRow ? e.Delta.Y : 0;
        if (Math.Abs(d) < 0.01) return; // plain vertical wheel: the shelf viewport switches shelves
        var items = Items();
        var step = items.Count == 0 ? ItemHeight : 2 * items.Average(i => TargetWidth(i, false));
        ScrollRow(Math.Sign(d) * step);
        e.Handled = true;
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        UpdateEdges();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hovered = null;
        UpdateEdges();
    }

    /// <summary>The plates show while the pointer is over an overflowing shelf; each is live only towards hidden boxes.</summary>
    private void UpdateEdges()
    {
        if (_edgeLeft == null || _edgeRight == null) return;
        var (total, view) = RowExtent();
        var overflow = total > view + 1;
        var show = overflow && IsPointerOver;
        _edgeLeft.Show(show);
        _edgeRight.Show(show);
        _edgeLeft.IsLive = _target < -0.5;
        _edgeRight.IsLive = _target > view - total + 0.5;
    }

    /// <summary>Focuses item <paramref name="index"/> (clamped) with visible (directional) focus. False when the shelf is empty.</summary>
    public bool FocusItem(int index)
    {
        var items = Items();
        if (items.Count == 0) return false;
        index = Math.Clamp(index, 0, items.Count - 1);
        return items[index].Focus(NavigationMethod.Directional);
    }

    /// <summary>Left/right inside the row; false at the ends (the caller gives the boundary feedback).</summary>
    public bool MoveHorizontal(int direction, Control? from)
    {
        var items = Items();
        var at = from == null ? RememberedIndex : IndexOf(items, from);
        if (at < 0) at = RememberedIndex;
        var next = at + direction;
        if (next < 0 || next >= items.Count) return false;
        return items[next].Focus(NavigationMethod.Directional);
    }

    private static int IndexOf(IReadOnlyList<Control> items, Control c)
    {
        for (var i = 0; i < items.Count; i++)
            if (items[i] == c || c.FindAncestorOfType<Button>(includeSelf: true) == items[i]) return i;
        return -1;
    }

    private void Reanchor()
    {
        var items = Items();
        if (items.Count > 0) Anchor(items, Math.Clamp(RememberedIndex, 0, items.Count - 1), animate: false);
    }

    /// <summary>Glides the row so that item <paramref name="index"/> stands at the anchor (target widths, see class remarks).</summary>
    private void Anchor(IReadOnlyList<Control> items, int index, bool animate = true)
    {
        if (_row == null) return;
        double left = 0, total = 0;
        for (var i = 0; i < items.Count; i++)
        {
            var w = TargetWidth(items[i], i == index);
            if (i < index) left += w + ItemSpacing;
            total += w + (i < items.Count - 1 ? ItemSpacing : 0);
        }
        total += EndExtra;
        var view = Bounds.Width - ItemsMargin.Left - ItemsMargin.Right;
        var min = Math.Min(0, view - total);
        var off = Math.Clamp(AnchorX - left, min, 0);
        SetOffset(Math.Round(off), animate);
    }

    private static double TargetWidth(Control item, bool focused) => item switch
    {
        SystemSpine s => focused && s.Opens ? s.OpenWidth : s.ClosedWidth,
        _ => item.Bounds.Width > 0 ? item.Bounds.Width : item.DesiredSize.Width,
    };

    private void SetOffset(double x, bool animate)
    {
        if (Math.Abs(x - _target) < 0.5 && Math.Abs(_offset.X - x) < 0.5) return;
        _target = x;
        var repeat = NavRepeatTracker.Current?.IsActive == true;
        var d = !animate || Motion.Current != EffectiveMotion.Full ? TimeSpan.Zero : repeat ? Motion.GlideRepeatDuration : Motion.GlideDuration;
        _offset.Transitions = d == TimeSpan.Zero ? null :
            [new DoubleTransition { Property = TranslateTransform.XProperty, Duration = d, Easing = repeat ? new LinearEasing() : new CubicEaseOut() }];
        _offset.X = x;
        UpdateEdges();
    }

    /// <summary>The row's current target offset (tests).</summary>
    public double Offset => _target;

    private sealed class ContactLayer(Shelf owner) : Control
    {
        static ContactLayer() { IsHitTestVisibleProperty.OverrideDefaultValue<ContactLayer>(false); }

        public override void Render(DrawingContext context)
        {
            var y = owner.ItemHeight + 1;
            foreach (var item in owner.Items())
            {
                Rect span;
                if (item is GameTile { Art: { } art } tile)
                {
                    var r = art.ArtRect;
                    if (r.Width <= 0 || art.TranslatePoint(r.TopLeft, this) is not { } p) continue;
                    span = new Rect(p.X, y, r.Width, 10);
                    _ = tile;
                }
                else if (item is SystemSpine spine && spine.ObjectRect is { Width: > 0 } o && spine.TranslatePoint(o.TopLeft, this) is { } sp)
                    span = new Rect(sp.X, y, o.Width, 10);
                else if (item.TranslatePoint(new Point(0, 0), this) is { } q) span = new Rect(q.X, y, item.Bounds.Width, 10);
                else continue;
                context.FillRectangle(ShelfBoard.ContactBrush, span);
            }
        }
    }
}

/// <summary>
/// The moving part of a <see cref="Shelf"/>: measures its children with unlimited width and arranges them at their full
/// width from x 0 (the row may be wider than the shelf; nothing is clipped here – the shelf viewport clips).
/// </summary>
public sealed class ShelfTrack : Panel
{
    protected override Size MeasureOverride(Size availableSize)
    {
        double w = 0, h = 0;
        foreach (var c in Children)
        {
            c.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            w = Math.Max(w, c.DesiredSize.Width);
            h = Math.Max(h, c.DesiredSize.Height);
        }
        return new Size(double.IsInfinity(availableSize.Width) ? w : availableSize.Width, double.IsInfinity(availableSize.Height) ? h : availableSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var w = finalSize.Width;
        foreach (var c in Children) w = Math.Max(w, c.DesiredSize.Width);
        foreach (var c in Children) c.Arrange(new Rect(0, 0, w, finalSize.Height));
        return finalSize;
    }
}

/// <summary>
/// The shelves area of Start (DESIGN 7.1): the shelves stand one below the other (its child panel), and the whole stack
/// glides vertically so that the ACTIVE shelf's boxes start at <see cref="AnchorY"/>; shelves above slide under the
/// stage (this area clips), the next one peeks in from below. Up/down/left/right between and inside the shelves are
/// handled here (<see cref="Move"/>), with the remembered index per shelf. The mouse wheel moves the active shelf.
/// </summary>
public sealed class ShelfViewport : Decorator
{
    public static readonly StyledProperty<double> AnchorYProperty = AvaloniaProperty.Register<ShelfViewport, double>(nameof(AnchorY), 40);

    private readonly TranslateTransform _offset = new();
    private int _active;

    static ShelfViewport()
    {
        ClipToBoundsProperty.OverrideDefaultValue<ShelfViewport>(true);
        AffectsArrange<ShelfViewport>(AnchorYProperty);
    }

    public ShelfViewport()
    {
        AddHandler(GotFocusEvent, OnGotFocus, RoutingStrategies.Bubble, handledEventsToo: true);
        // after the click (the button raised it on release, bubbling before this): now the stack may glide
        AddHandler(PointerReleasedEvent, (_, _) => ApplyPointerActivation(), RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(PointerCaptureLostEvent, (_, _) => ApplyPointerActivation(), RoutingStrategies.Bubble, handledEventsToo: true);
        Background = Brushes.Transparent; // wheel input over the gaps
    }

    public IBrush? Background { get; set; }

    public double AnchorY { get => GetValue(AnchorYProperty); set => SetValue(AnchorYProperty, value); }

    /// <summary>The visible shelves in order.</summary>
    public IReadOnlyList<Shelf> Shelves => (Child as Panel)?.Children.OfType<Shelf>().Where(s => s.IsVisible).ToList() ?? [];

    /// <summary>Index of the shelf standing at the anchor.</summary>
    public int ActiveIndex => _active;

    /// <summary>Raised when the active shelf changed (the page shows its name).</summary>
    public event Action<Shelf?>? ActiveChanged;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ChildProperty && change.NewValue is Control c) c.RenderTransform = _offset;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        Child?.Measure(new Size(availableSize.Width, double.PositiveInfinity));
        return new Size(double.IsInfinity(availableSize.Width) ? Child?.DesiredSize.Width ?? 0 : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? Child?.DesiredSize.Height ?? 0 : availableSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Child?.Arrange(new Rect(0, 0, finalSize.Width, Child.DesiredSize.Height));
        Avalonia.Threading.Dispatcher.UIThread.Post(() => Glide(animate: false));
        return finalSize;
    }

    public override void Render(DrawingContext context)
    {
        if (Background != null) context.FillRectangle(Background, new Rect(Bounds.Size));
    }

    private int _pointerActive = -1;

    private void OnGotFocus(object? sender, FocusChangedEventArgs e)
    {
        if ((e.Source as Visual)?.FindAncestorOfType<Shelf>() is not { } shelf) return;
        var index = IndexOf(shelf);
        if (index < 0) return;
        // a mouse press on a box of another shelf (the one peeking in): the stack glides only after the release,
        // otherwise the box would move away under the pointer and the click would be lost
        if (e.NavigationMethod == NavigationMethod.Pointer && index != _active) { _pointerActive = index; return; }
        SetActive(index);
    }

    private void ApplyPointerActivation()
    {
        if (_pointerActive < 0) return;
        var index = _pointerActive;
        _pointerActive = -1;
        Avalonia.Threading.Dispatcher.UIThread.Post(() => SetActive(index));
    }

    private int IndexOf(Shelf shelf)
    {
        var list = Shelves;
        for (var i = 0; i < list.Count; i++) if (list[i] == shelf) return i;
        return -1;
    }

    /// <summary>Makes shelf <paramref name="index"/> the one at the anchor.</summary>
    public void SetActive(int index, bool animate = true)
    {
        var count = Shelves.Count;
        if (count == 0) return;
        index = Math.Clamp(index, 0, count - 1);
        var changed = index != _active;
        _active = index;
        Glide(animate);
        if (changed) ActiveChanged?.Invoke(Shelves[index]);
    }

    private double _target = double.NaN;

    private void Glide(bool animate)
    {
        var list = Shelves;
        if (list.Count == 0) return;
        _active = Math.Clamp(_active, 0, list.Count - 1);
        var shelf = list[_active];
        // the shelf's top in the child panel (layout position, not affected by the transform)
        var top = shelf.Bounds.Y;
        var y = Math.Round(AnchorY - top);
        if (Math.Abs(y - _target) < 0.5 && Math.Abs(_offset.Y - y) < 0.5) return;
        _target = y;
        var repeat = NavRepeatTracker.Current?.IsActive == true;
        var d = !animate || Motion.Current != EffectiveMotion.Full ? TimeSpan.Zero : repeat ? Motion.GlideRepeatDuration : Motion.GlideDuration;
        _offset.Transitions = d == TimeSpan.Zero ? null :
            [new DoubleTransition { Property = TranslateTransform.YProperty, Duration = d, Easing = repeat ? new LinearEasing() : new CubicEaseOut() }];
        _offset.Y = y;
    }

    /// <summary>
    /// Directional navigation inside the shelves: left/right along the row (consumed at the ends), down to the next
    /// shelf (consumed at the last), up to the previous shelf – false from the first shelf (the page goes to the stage).
    /// </summary>
    public bool Move(NavAction action, Control focused)
    {
        var list = Shelves;
        var shelf = focused.FindAncestorOfType<Shelf>(includeSelf: true);
        var at = shelf == null ? -1 : IndexOf(shelf);
        if (at < 0) return false;
        switch (action)
        {
            case NavAction.Left or NavAction.Right:
                if (!shelf!.MoveHorizontal(action == NavAction.Right ? 1 : -1, focused)) Bump(focused, action == NavAction.Right ? 1 : -1);
                return true;
            case NavAction.Down:
                if (at + 1 >= list.Count) { Bump(focused, 0); return true; }
                return FocusShelf(at + 1);
            case NavAction.Up:
                if (at == 0) return false;
                return FocusShelf(at - 1);
        }
        return false;
    }

    /// <summary>Focuses shelf <paramref name="index"/> at its remembered item.</summary>
    public bool FocusShelf(int index)
    {
        var list = Shelves;
        if (list.Count == 0) return false;
        index = Math.Clamp(index, 0, list.Count - 1);
        SetActive(index);
        return list[index].FocusItem(list[index].RememberedIndex);
    }

    /// <summary>Boundary feedback (DESIGN 8.4): the focused element shakes 0 → −6 → +4 → 0 px in 180 ms.</summary>
    internal static void Bump(Control c, int direction)
    {
        if (!Motion.AllowsMovement || direction == 0) return;
        var t = c.RenderTransform as TranslateTransform;
        if (c.RenderTransform != null && t == null) return; // the element animates its own transform (lift): no shake
        t ??= new TranslateTransform();
        c.RenderTransform = t;
        var anim = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(180),
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Avalonia.Styling.Setter(TranslateTransform.XProperty, 0d) } },
                new KeyFrame { Cue = new Cue(0.33), Setters = { new Avalonia.Styling.Setter(TranslateTransform.XProperty, -6d * direction) } },
                new KeyFrame { Cue = new Cue(0.66), Setters = { new Avalonia.Styling.Setter(TranslateTransform.XProperty, 4d * direction) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Avalonia.Styling.Setter(TranslateTransform.XProperty, 0d) } },
            },
        };
        _ = anim.RunAsync(t);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (e.Handled || Math.Abs(e.Delta.Y) < 0.01) return;
        SetActive(_active + (e.Delta.Y < 0 ? 1 : -1));
        e.Handled = true;
    }
}
