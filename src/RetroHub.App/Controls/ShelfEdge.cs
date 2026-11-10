using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using RetroHub.App.Theming;

namespace RetroHub.App.Controls;

/// <summary>
/// A "Winkel" (R2): a small chamfered metal plate screwed to the board front at the right end of a shelf, one pointing
/// back, one on. Mouse only (never focusable): a click glides the row one view on, so the boxes beyond the window edge
/// and the "Alle anzeigen" spine are always reachable. Drawn like the board: a plate a shade lighter than the front, a
/// 1 px light edge on top and a dark one below, two screw dots, the chevron cut in. Hover lights the chevron (paper);
/// a press pushes the plate 1 px in. A plate without boxes beyond it stays, quieter (no hover, no hand).
/// </summary>
public sealed class ShelfEdge : Control
{
    public static readonly StyledProperty<int> DirectionProperty = AvaloniaProperty.Register<ShelfEdge, int>(nameof(Direction), 1);

    private static readonly IImmutableSolidColorBrush Plate = new ImmutableSolidColorBrush(Color.Parse("#3A342B"));
    private static readonly IImmutableSolidColorBrush PlateHot = new ImmutableSolidColorBrush(Color.Parse("#474036"));
    private static readonly IImmutableSolidColorBrush TopEdge = new ImmutableSolidColorBrush(Color.Parse("#2EFFF3E0"));
    private static readonly IImmutableSolidColorBrush BottomEdge = new ImmutableSolidColorBrush(Color.Parse("#80000000"));
    private static readonly IImmutableSolidColorBrush Screw = new ImmutableSolidColorBrush(Color.Parse("#1F1C17"));
    private static readonly IImmutableSolidColorBrush Text3 = new ImmutableSolidColorBrush(Color.Parse("#8F8678"));
    private static readonly IImmutableSolidColorBrush Dim = new ImmutableSolidColorBrush(Color.Parse("#5A5246"));
    private static readonly IImmutableSolidColorBrush PaperBrush = new ImmutableSolidColorBrush(SystemPalette.Paper);

    private bool _live = true, _pressed;

    static ShelfEdge()
    {
        AffectsRender<ShelfEdge>(DirectionProperty, IsPointerOverProperty);
        FocusableProperty.OverrideDefaultValue<ShelfEdge>(false);
    }

    public ShelfEdge()
    {
        Width = 30;
        Height = 20;
        Opacity = 0;
        IsHitTestVisible = false;
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    /// <summary>−1 = back towards the row's start, +1 = on towards its end.</summary>
    public int Direction { get => GetValue(DirectionProperty); set => SetValue(DirectionProperty, value); }

    /// <summary>There are boxes beyond this plate's side: it reacts (hover, hand, click).</summary>
    public bool IsLive
    {
        get => _live;
        set
        {
            if (_live == value) return;
            _live = value;
            Cursor = value ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
            InvalidateVisual();
        }
    }

    /// <summary>Raised on a left click while live.</summary>
    public event Action? Clicked;

    /// <summary>Fades the plate in/out (Step 140 ms; at once when motion is off) and makes it hit-testable while shown.</summary>
    public void Show(bool show)
    {
        IsHitTestVisible = show;
        var to = show ? 1.0 : 0.0;
        if (Math.Abs(Opacity - to) < 0.01) return;
        Transitions = Motion.Enabled ? [new DoubleTransition { Property = OpacityProperty, Duration = Motion.StepDuration }] : null;
        Opacity = to;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        e.Handled = true;
        if (!_live) return;
        _pressed = true;
        e.Pointer.Capture(this);
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_pressed) return;
        _pressed = false;
        e.Pointer.Capture(null);
        e.Handled = true;
        InvalidateVisual();
        if (new Rect(Bounds.Size).Contains(e.GetPosition(this))) Clicked?.Invoke();
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _pressed = false;
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        var hot = _live && IsPointerOver;
        var dy = _pressed ? 1 : 0;
        var r = new Rect(0, dy, w, h - 1);
        ctx.DrawGeometry(hot ? PlateHot : Plate, null, Chamfer.Octagon(r, 3));
        // light from above: a lit top edge, a dark lower edge (the plate stands 1 px off the board)
        ctx.FillRectangle(TopEdge, new Rect(3, dy, w - 6, 1));
        if (!_pressed) ctx.FillRectangle(BottomEdge, new Rect(3, h - 1, w - 6, 1));
        // two screws
        ctx.FillRectangle(Screw, new Rect(3, dy + h / 2 - 1.5, 2, 2));
        ctx.FillRectangle(Screw, new Rect(w - 5, dy + h / 2 - 1.5, 2, 2));
        // the chevron, cut in: 6 × 10, 2 px stroke
        var cx = w / 2 + (Direction > 0 ? 1 : -1);
        var cy = dy + (h - 1) / 2;
        var pen = new Pen(!_live ? Dim : hot ? PaperBrush : Text3, 2, lineCap: PenLineCap.Square, lineJoin: PenLineJoin.Miter);
        var sx = Direction > 0 ? -3 : 3;
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(new Point(cx + sx, cy - 5), false);
            g.LineTo(new Point(cx - sx, cy));
            g.LineTo(new Point(cx + sx, cy + 5));
            g.EndFigure(false);
        }
        ctx.DrawGeometry(null, pen, geo);
    }
}
