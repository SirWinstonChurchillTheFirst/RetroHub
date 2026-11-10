using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace RetroHub.App.Controls;

/// <summary>Chamfered shapes of "Fernsehlicht" (DESIGN 4.1): 45° cuts at all four corners, cached per size.</summary>
public static class Chamfer
{
    private static readonly Dictionary<(int, int, int, int, int), StreamGeometry> Cache = new();

    /// <summary>An octagon (rectangle with 45° cut corners) of <paramref name="rect"/>; cached per 0.5-px bucket.</summary>
    public static Geometry Octagon(Rect rect, double chamfer)
    {
        static int Q(double v) => (int)Math.Round(v * 2);
        var key = (Q(rect.X), Q(rect.Y), Q(rect.Width), Q(rect.Height), Q(chamfer));
        lock (Cache)
        {
            if (Cache.TryGetValue(key, out var hit)) return hit;
            if (Cache.Count > 512) Cache.Clear();
            var c = Math.Max(0, Math.Min(chamfer, Math.Min(rect.Width, rect.Height) / 2));
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(new Point(rect.Left + c, rect.Top), true);
                ctx.LineTo(new Point(rect.Right - c, rect.Top));
                ctx.LineTo(new Point(rect.Right, rect.Top + c));
                ctx.LineTo(new Point(rect.Right, rect.Bottom - c));
                ctx.LineTo(new Point(rect.Right - c, rect.Bottom));
                ctx.LineTo(new Point(rect.Left + c, rect.Bottom));
                ctx.LineTo(new Point(rect.Left, rect.Bottom - c));
                ctx.LineTo(new Point(rect.Left, rect.Top + c));
                ctx.EndFigure(true);
            }
            Cache[key] = g;
            return g;
        }
    }

    private static readonly IImmutableSolidColorBrush RoomDeep = new ImmutableSolidColorBrush(Color.Parse("#0A0908"));

    /// <summary>
    /// The chamfered focus ring around <paramref name="target"/> (DESIGN 8.1): inside a 3 px gap in room-deep (keeps
    /// the ring readable against bright covers), outside 3 px in <paramref name="light"/>, outer chamfer 6.
    /// </summary>
    public static void DrawRing(DrawingContext ctx, Rect target, IBrush light, double gap = 3, double ring = 3, double chamfer = 6, double opacity = 1)
    {
        if (target.Width <= 0 || target.Height <= 0) return;
        using var _ = ctx.PushOpacity(opacity);
        var gapRect = target.Inflate(gap / 2);
        ctx.DrawGeometry(null, new ImmutablePen(RoomDeep, gap, lineJoin: PenLineJoin.Miter), Octagon(gapRect, Math.Max(0, chamfer - ring - gap / 2)));
        var ringRect = target.Inflate(gap + ring / 2);
        ctx.DrawGeometry(null, new Pen(light, ring, lineJoin: PenLineJoin.Miter), Octagon(ringRect, Math.Max(0, chamfer - ring / 2)));
    }

    /// <summary>
    /// The focus ring of a box STANDING on a board (M3 QA): gap and ring run up the sides and over the top like
    /// <see cref="DrawRing"/>, but their legs end flat on the board top (<paramref name="target"/>.Bottom) – the ring
    /// never pierces the board front, so the box stands on the shelf instead of sinking into it. The lit board piece
    /// (drawn by the caller) closes the frame at the bottom.
    /// </summary>
    public static void DrawStandingRing(DrawingContext ctx, Rect target, IBrush light, double gap = 3, double ring = 3, double chamfer = 6)
    {
        if (target.Width <= 0 || target.Height <= 0) return;
        static Geometry Open(double l, double t, double r, double b, double c)
        {
            c = Math.Max(0, Math.Min(c, Math.Min(r - l, b - t) / 2));
            var g = new StreamGeometry();
            using var ctx = g.Open();
            ctx.BeginFigure(new Point(l, b), false);
            ctx.LineTo(new Point(l, t + c));
            ctx.LineTo(new Point(l + c, t));
            ctx.LineTo(new Point(r - c, t));
            ctx.LineTo(new Point(r, t + c));
            ctx.LineTo(new Point(r, b));
            ctx.EndFigure(false);
            return g;
        }
        var b = target.Bottom;
        var gi = gap / 2;
        ctx.DrawGeometry(null, new ImmutablePen(RoomDeep, gap, lineCap: PenLineCap.Flat, lineJoin: PenLineJoin.Miter),
            Open(target.X - gi, target.Y - gi, target.Right + gi, b, Math.Max(0, chamfer - ring - gi)));
        var ri = gap + ring / 2;
        ctx.DrawGeometry(null, new Pen(light, ring, lineCap: PenLineCap.Flat, lineJoin: PenLineJoin.Miter),
            Open(target.X - ri, target.Y - ri, target.Right + ri, b, Math.Max(0, chamfer - ring / 2)));
    }
}

/// <summary>
/// A chamfered plate ("gefaste Platte", DESIGN 4.1): filled with <see cref="Background"/>, optional outline. Used as
/// the face of the primary button and of key caps. Hit-testing follows the bounds.
/// </summary>
public sealed class ChamferPlate : Control
{
    public static readonly StyledProperty<IBrush?> BackgroundProperty =
        AvaloniaProperty.Register<ChamferPlate, IBrush?>(nameof(Background));
    public static readonly StyledProperty<IBrush?> OutlineProperty =
        AvaloniaProperty.Register<ChamferPlate, IBrush?>(nameof(Outline));
    public static readonly StyledProperty<double> OutlineThicknessProperty =
        AvaloniaProperty.Register<ChamferPlate, double>(nameof(OutlineThickness), 2);
    public static readonly StyledProperty<double> ChamferProperty =
        AvaloniaProperty.Register<ChamferPlate, double>(nameof(Chamfer), 4);
    /// <summary>
    /// Key travel (M7): the plate stands on a darker edge this many px deep at its foot (3 on the primary button; 1 while
    /// pressed – the face goes down), with a lit top edge. 0 = a flat plate.
    /// </summary>
    public static readonly StyledProperty<double> DepthProperty =
        AvaloniaProperty.Register<ChamferPlate, double>(nameof(Depth));

    static ChamferPlate()
    {
        AffectsRender<ChamferPlate>(BackgroundProperty, OutlineProperty, OutlineThicknessProperty, ChamferProperty, DepthProperty);
    }

    public double Depth { get => GetValue(DepthProperty); set => SetValue(DepthProperty, value); }

    /// <summary>The most a plate with <see cref="Depth"/> stands up: its face always leaves this much at the foot.</summary>
    public const double MaxDepth = 3;

    public IBrush? Background { get => GetValue(BackgroundProperty); set => SetValue(BackgroundProperty, value); }
    public IBrush? Outline { get => GetValue(OutlineProperty); set => SetValue(OutlineProperty, value); }
    public double OutlineThickness { get => GetValue(OutlineThicknessProperty); set => SetValue(OutlineThicknessProperty, value); }
    public double Chamfer { get => GetValue(ChamferProperty); set => SetValue(ChamferProperty, value); }

    public override void Render(DrawingContext context)
    {
        var r = new Rect(Bounds.Size);
        if (r.Width <= 0 || r.Height <= 0) return;
        var outline = Outline;
        var t = outline != null ? OutlineThickness : 0;
        var depth = Math.Clamp(Depth, 0, MaxDepth);
        if (Depth > 0 && Background is ISolidColorBrush solid)
        {
            // the edge under the face, then the face pushed down by the travel already used
            var travel = MaxDepth - depth;
            context.DrawGeometry(Craft.Solid(Theming.ColorUtil.Mix(solid.Color, Colors.Black, 0.34)), null,
                Controls.Chamfer.Octagon(new Rect(0, travel + 1, r.Width, r.Height - travel - 1), Chamfer));
            r = new Rect(0, travel, r.Width, r.Height - MaxDepth);
        }
        var geo = Controls.Chamfer.Octagon(r.Deflate(t / 2), Math.Max(0, Chamfer - t / 2));
        context.DrawGeometry(Background, outline != null ? new Pen(outline, t, lineJoin: PenLineJoin.Miter) : null, geo);
        if (Depth > 0)
            using (context.PushGeometryClip(geo))
                context.FillRectangle(Craft.Hi(0.30), new Rect(r.X, r.Y, r.Width, 1));
    }
}

/// <summary>
/// The chamfered focus ring of a single object in a template (primary button, system card): drawn OUTSIDE its own
/// bounds (gap + ring), so the hosting template must not clip. For cover tiles in grids the <see cref="FocusLayer"/>
/// draws the ring instead (covers clip, z-order).
/// </summary>
public sealed class FocusRing : Control
{
    public static readonly StyledProperty<IBrush?> BrushProperty =
        AvaloniaProperty.Register<FocusRing, IBrush?>(nameof(Brush));
    public static readonly StyledProperty<double> GapProperty =
        AvaloniaProperty.Register<FocusRing, double>(nameof(Gap), 3);
    public static readonly StyledProperty<double> ThicknessProperty =
        AvaloniaProperty.Register<FocusRing, double>(nameof(Thickness), 3);
    public static readonly StyledProperty<double> ChamferProperty =
        AvaloniaProperty.Register<FocusRing, double>(nameof(Chamfer), 6);
    /// <summary>The object stands on a board: the ring is open at the bottom, its legs end on the board top (M3 QA).</summary>
    public static readonly StyledProperty<bool> StandingProperty =
        AvaloniaProperty.Register<FocusRing, bool>(nameof(Standing));

    static FocusRing()
    {
        AffectsRender<FocusRing>(BrushProperty, GapProperty, ThicknessProperty, ChamferProperty, StandingProperty);
        IsHitTestVisibleProperty.OverrideDefaultValue<FocusRing>(false);
        ClipToBoundsProperty.OverrideDefaultValue<FocusRing>(false);
    }

    public IBrush? Brush { get => GetValue(BrushProperty); set => SetValue(BrushProperty, value); }
    public double Gap { get => GetValue(GapProperty); set => SetValue(GapProperty, value); }
    public double Thickness { get => GetValue(ThicknessProperty); set => SetValue(ThicknessProperty, value); }
    public double Chamfer { get => GetValue(ChamferProperty); set => SetValue(ChamferProperty, value); }
    public bool Standing { get => GetValue(StandingProperty); set => SetValue(StandingProperty, value); }

    public override void Render(DrawingContext context)
    {
        if (Brush is not { } brush) return;
        if (Standing) Controls.Chamfer.DrawStandingRing(context, new Rect(Bounds.Size), brush, Gap, Thickness, Chamfer);
        else Controls.Chamfer.DrawRing(context, new Rect(Bounds.Size), brush, Gap, Thickness, Chamfer);
    }
}
