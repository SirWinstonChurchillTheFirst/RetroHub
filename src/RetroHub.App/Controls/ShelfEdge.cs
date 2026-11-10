using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using RetroHub.App.Theming;

namespace RetroHub.App.Controls;

/// <summary>
/// A "Winkel" (R2, R3 QA: 30 × 20 at 1.6:1 was nearly invisible and a small target): a chamfered metal plate 44 × 30
/// screwed to the board front at the right end of a shelf, one pointing back, one on. Mouse only (never focusable): a
/// click glides the row one view on, so the boxes beyond the window edge and the "Alle anzeigen" spine are always
/// reachable. Drawn like the board: a plate lit 10 % above the front, a light edge on top and a dark one below, two screw
/// heads, the chevron cut in and embossed (lamp light at 75 % over a 1 px dark shadow). Hover lifts the plate 2 px with
/// the 40 % ring of the boxes; a press pushes it 1 px in. A plate without boxes beyond it stays, quieter.
/// </summary>
public sealed class ShelfEdge : Control
{
    public static readonly StyledProperty<int> DirectionProperty = AvaloniaProperty.Register<ShelfEdge, int>(nameof(Direction), 1);

    private static readonly IImmutableSolidColorBrush Plate = new ImmutableSolidColorBrush(ColorUtil.Mix(Color.Parse("#24272D"), Craft.Light, 0.10));
    private static readonly IImmutableSolidColorBrush PlateHot = new ImmutableSolidColorBrush(ColorUtil.Mix(Color.Parse("#24272D"), Craft.Light, 0.16));
    private static readonly IImmutableSolidColorBrush TopEdge = new ImmutableSolidColorBrush(Color.Parse("#40FFFFFF"));
    private static readonly IImmutableSolidColorBrush BottomEdge = new ImmutableSolidColorBrush(Color.Parse("#99000000"));
    private static readonly IImmutableSolidColorBrush Screw = new ImmutableSolidColorBrush(Color.Parse("#1B1D21"));
    private static readonly IImmutableSolidColorBrush ScrewLit = new ImmutableSolidColorBrush(Color.Parse("#33FFFFFF"));
    private static readonly IImmutableSolidColorBrush Chevron = new ImmutableSolidColorBrush(Craft.A(Craft.Light, 0.75));
    private static readonly IImmutableSolidColorBrush ChevronShadow = new ImmutableSolidColorBrush(Color.Parse("#B3000000"));
    private static readonly IImmutableSolidColorBrush Dim = new ImmutableSolidColorBrush(Color.Parse("#646A75"));
    private static readonly IImmutableSolidColorBrush PaperBrush = new ImmutableSolidColorBrush(SystemPalette.Paper);
    private static readonly IImmutableSolidColorBrush Ring = new ImmutableSolidColorBrush(SystemPalette.Paper);

    private bool _live = true, _pressed;

    static ShelfEdge()
    {
        AffectsRender<ShelfEdge>(DirectionProperty, IsPointerOverProperty);
        FocusableProperty.OverrideDefaultValue<ShelfEdge>(false);
        ClipToBoundsProperty.OverrideDefaultValue<ShelfEdge>(false);
    }

    public ShelfEdge()
    {
        Width = 44;
        Height = 30;
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
        // hover lifts the plate 2 px off the board (its shadow shows under it); a press pushes it 1 px in
        var dy = _pressed ? 1 : hot && Motion.AllowsMovement ? -2 : 0;
        var r = new Rect(0, dy, w, h - 1);
        if (dy < 0) ctx.DrawGeometry(Craft.Dark(0.45), null, Chamfer.Octagon(new Rect(1, 1, w - 1, h - 1), 4));
        ctx.DrawGeometry(hot ? PlateHot : Plate, null, Chamfer.Octagon(r, 4));
        using (ctx.PushGeometryClip(Chamfer.Octagon(r, 4)))
        {
            ctx.FillRectangle(Craft.Curvature, r);
            ctx.FillRectangle(Craft.Fade(Colors.Black, 0, 0.28, Craft.Toward.Down), new Rect(0, r.Bottom - 8, w, 8));
            Craft.Scuffs(ctx, r, Craft.Seed(Direction > 0 ? "winkel+" : "winkel-"), 1, 0.08);
        }
        // light from above: a lit top edge, a dark lower edge (the plate stands 1 px off the board)
        ctx.FillRectangle(TopEdge, new Rect(4, dy, w - 8, 1));
        if (!_pressed) ctx.FillRectangle(BottomEdge, new Rect(4, dy + h - 1, w - 8, 1));
        // two screw heads, lit on their upper rim
        foreach (var sx in new[] { 5.0, w - 8 })
        {
            ctx.FillRectangle(Screw, new Rect(sx, dy + h / 2 - 1.5, 3, 3));
            ctx.FillRectangle(ScrewLit, new Rect(sx, dy + h / 2 - 2.5, 3, 1));
        }
        // the chevron, cut in and embossed: 8 × 14, 2.5 px stroke, its dark copy 1 px down
        var cx = w / 2 + (Direction > 0 ? 1.5 : -1.5);
        var cy = dy + (h - 1) / 2;
        var sxv = Direction > 0 ? -4 : 4;
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(new Point(cx + sxv, cy - 7), false);
            g.LineTo(new Point(cx - sxv, cy));
            g.LineTo(new Point(cx + sxv, cy + 7));
            g.EndFigure(false);
        }
        using (ctx.PushTransform(Matrix.CreateTranslation(0, 1)))
            ctx.DrawGeometry(null, new Pen(ChevronShadow, 2.5, lineCap: PenLineCap.Square, lineJoin: PenLineJoin.Miter), geo);
        ctx.DrawGeometry(null, new Pen(!_live ? Dim : hot ? PaperBrush : Chevron, 2.5, lineCap: PenLineCap.Square, lineJoin: PenLineJoin.Miter), geo);
        // the 40 % ring of a hovered box
        if (hot)
            using (ctx.PushOpacity(0.4))
                Chamfer.DrawRing(ctx, r, Ring, gap: 2, ring: 2, chamfer: 6);
    }
}
