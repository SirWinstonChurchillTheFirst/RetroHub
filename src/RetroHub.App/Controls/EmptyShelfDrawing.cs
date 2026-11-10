using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using RetroHub.App.Theming;

namespace RetroHub.App.Controls;

/// <summary>
/// The empty library's line drawing (DESIGN 6.4 point 7, 7.1): "leeres Regal" – two bookends and one box leaning
/// against the right one, 1.5 px lines in paper at 60 %, standing DIRECTLY on the bottom edge (= the stage board's top;
/// M2 QA: no board of its own floating above the real one). No fill, no colour.
/// </summary>
public sealed class EmptyShelfDrawing : Control
{
    private static readonly IPen Line = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromArgb(0x99, 0xF5, 0xF7, 0xFA)), 1.5,
        lineCap: PenLineCap.Flat, lineJoin: PenLineJoin.Miter);

    /// <summary>
    /// Scale the FIGURES (not the 120 grid) to the control's height: the leaning box is then as tall as the control and
    /// stands at the scale of real boxes beside the outlined places (library, M3 QA: the 200 px grid drew 80 px figures).
    /// Width needed: ≈ 1.9 × height.
    /// </summary>
    public static readonly StyledProperty<bool> TightProperty = AvaloniaProperty.Register<EmptyShelfDrawing, bool>(nameof(Tight));

    public bool Tight { get => GetValue(TightProperty); set => SetValue(TightProperty, value); }

    /// <summary>
    /// Only the one leaning box as a gesture (2 px in Text 3, scaled to the height, left-aligned) beside recessed places
    /// (M5 QA: bookends and dashed outlines read as an empty-state wireframe). Width needed: ≈ 0.7 × height.
    /// </summary>
    public static readonly StyledProperty<bool> GestureProperty = AvaloniaProperty.Register<EmptyShelfDrawing, bool>(nameof(Gesture));

    public bool Gesture { get => GetValue(GestureProperty); set => SetValue(GestureProperty, value); }

    private static readonly IPen GestureLine = new ImmutablePen(new ImmutableSolidColorBrush(Color.Parse("#A0A7B2")), 2,
        lineCap: PenLineCap.Flat, lineJoin: PenLineJoin.Miter);

    static EmptyShelfDrawing()
    {
        AffectsRender<EmptyShelfDrawing>(BoundsProperty, TightProperty, GestureProperty);
        IsHitTestVisibleProperty.OverrideDefaultValue<EmptyShelfDrawing>(false);
    }

    public override void Render(DrawingContext ctx)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        if (Gesture)
        {
            // the leaning box of the grid (x 69–100, y 71–120) at the control's height, its foot on the bottom edge
            var k = h / 49.5;
            var gx = -68 * k;
            var gy = h - 120 * k;
            var gfoot = 120 - 1 / k;
            var lean = new StreamGeometry();
            using (var c = lean.Open())
            {
                c.BeginFigure(new Point(gx + 84 * k, gy + gfoot * k), false);
                c.LineTo(new Point(gx + 69 * k, gy + 114.5 * k));
                c.LineTo(new Point(gx + 85 * k, gy + 71.3 * k));
                c.LineTo(new Point(gx + 100 * k, gy + 76.8 * k));
                c.EndFigure(true);
            }
            ctx.DrawGeometry(null, GestureLine, lean);
            return;
        }
        // a 120 × 120 drawing scaled to the width; its feet end on the bottom edge (open figures, no base line).
        // Tight: the figures (x 14–106, y 71–120 of the grid) fill the height, left-aligned
        var s = Tight ? h / 49.5 : Math.Min(w, h) / 120.0;
        var ox = Tight ? -13 * s : (w - 120 * s) / 2;
        var oy = h - 120 * s;
        var foot = 120 - 0.75 / s; // the stroke's lower half ends exactly on the board
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            void Poly(bool closed, params double[] p)
            {
                c.BeginFigure(new Point(ox + p[0] * s, oy + p[1] * s), false);
                for (var i = 2; i < p.Length; i += 2) c.LineTo(new Point(ox + p[i] * s, oy + p[i + 1] * s));
                c.EndFigure(closed);
            }
            Poly(false, 14, foot, 14, 90, 20, 90, 20, foot);       // left bookend
            Poly(false, 100, foot, 100, 82, 106, 82, 106, foot);   // right bookend
            Poly(true, 84, foot, 69, 114.5, 85, 71.3, 100, 76.8);  // one box leaning against it (20°)
        }
        ctx.DrawGeometry(null, Line, g);
    }
}

/// <summary>
/// An outlined, empty place for a box on the empty library's shelf (M2 QA): 1 px paper at 12 %, dashed 4/4, square –
/// the shelf reads as one waiting to be filled. Not focusable, no fill. <see cref="Aspect"/> (width / height) draws the
/// place in a box format inside the bounds (contain, standing on the bottom edge, centred): the gap a pulled box left
/// on "Daneben im Regal" has that box's format (M4 QA). <see cref="Baseline"/> draws only a dashed line at the bottom in
/// Kante (an empty place on "Gespielt": a line waiting for a stamp, not a box).
/// </summary>
public sealed class EmptySlot : Control
{
    private static readonly IPen Dash = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromArgb(0x1F, 0xF5, 0xF7, 0xFA)), 1,
        new ImmutableDashStyle([4, 4], 0), PenLineCap.Flat, PenLineJoin.Miter);
    private static readonly IPen BaselineDash = new ImmutablePen(new ImmutableSolidColorBrush(Color.Parse("#343840")), 1,
        new ImmutableDashStyle([4, 4], 0), PenLineCap.Flat, PenLineJoin.Miter);

    public static readonly StyledProperty<double> AspectProperty = AvaloniaProperty.Register<EmptySlot, double>(nameof(Aspect));
    /// <summary>
    /// A recessed empty compartment instead of a dashed outline (M5 QA): the ground darkened, a 2 px shadow inside at the
    /// top, 1 px Kante at the sides – an empty place standing on the real board, not a selection marquee.
    /// </summary>
    public static readonly StyledProperty<bool> RecessedProperty = AvaloniaProperty.Register<EmptySlot, bool>(nameof(Recessed));

    private static readonly IImmutableSolidColorBrush RecessFill = new ImmutableSolidColorBrush(Color.FromArgb(0x33, 0, 0, 0));
    private static readonly IImmutableSolidColorBrush RecessTop = new ImmutableSolidColorBrush(Color.FromArgb(0x66, 0, 0, 0));
    private static readonly IImmutableSolidColorBrush RecessSide = new ImmutableSolidColorBrush(Color.Parse("#343840"));

    public bool Recessed { get => GetValue(RecessedProperty); set => SetValue(RecessedProperty, value); }
    public static readonly StyledProperty<bool> BaselineProperty = AvaloniaProperty.Register<EmptySlot, bool>(nameof(Baseline));

    /// <summary>Width / height of the outlined place inside the bounds; 0 = the bounds themselves.</summary>
    public double Aspect { get => GetValue(AspectProperty); set => SetValue(AspectProperty, value); }

    public bool Baseline { get => GetValue(BaselineProperty); set => SetValue(BaselineProperty, value); }

    static EmptySlot()
    {
        AffectsRender<EmptySlot>(BoundsProperty, AspectProperty, BaselineProperty, RecessedProperty);
        IsHitTestVisibleProperty.OverrideDefaultValue<EmptySlot>(false);
        FocusableProperty.OverrideDefaultValue<EmptySlot>(false);
    }

    public override void Render(DrawingContext ctx)
    {
        var size = Bounds.Size;
        if (size.Width <= 0 || size.Height <= 0) return;
        if (Baseline)
        {
            // a groove waiting for its stamp (M7: no dashed line)
            Craft.Groove(ctx, 0, size.Height - 2, size.Width, 0.45, 0.06);
            return;
        }
        var outer = new Rect(size);
        var r = Aspect > 0 ? CoverArt.Compute(outer, null, false, Aspect) : outer;
        if (Recessed)
        {
            DrawGhost(ctx, r);
            return;
        }
        if (r.Width <= 4 || r.Height <= 4) return;
        // the place where a box stood (M7: the dashed outline read as a wireframe or a failed image): the wall behind it
        // a touch cleaner, dust settled all around it – thickest on the board at its foot – and a soft rim
        ctx.FillRectangle(Craft.Hi(0.025), r);
        TextureFill.Draw(ctx, this, new Rect(r.X - 6, r.Y - 5, r.Width + 12, 5), Texture.Dust, 0.30);
        TextureFill.Draw(ctx, this, new Rect(r.X - 6, r.Y, 6, r.Height), Texture.Dust, 0.30);
        TextureFill.Draw(ctx, this, new Rect(r.Right, r.Y, 6, r.Height), Texture.Dust, 0.30);
        TextureFill.Draw(ctx, this, new Rect(r.X - 8, r.Bottom - 4, r.Width + 16, 4), Texture.Dust, 0.55);
        ctx.FillRectangle(Craft.Hi(0.06), new Rect(r.X, r.Y, r.Width, 1));
        ctx.FillRectangle(Craft.Hi(0.05), new Rect(r.X, r.Y + 1, 1, r.Height - 1));
        ctx.FillRectangle(Craft.Hi(0.05), new Rect(r.Right - 1, r.Y + 1, 1, r.Height - 1));
    }

    /// <summary>
    /// A place on an empty board (R3 QA: the recessed places were flat dark rectangles of one width – "generated"): what
    /// is left where something stood – its footprint on the board edge, clean between two drifts of dust, and on about
    /// one place in three the wall kept cleaner where it stood, with the soft shadow of its rims. Seeded per place (its
    /// position in the row), so the places differ in width and height and none is a filled box.
    /// </summary>
    private void DrawGhost(DrawingContext ctx, Rect r)
    {
        var x = Bounds.X + (this.GetVisualParent() is Visual p ? p.Bounds.X : 0);
        var seed = Craft.Mix(Craft.Seed("leerer-platz"), (int)Math.Round(x));
        var fw = Math.Round(r.Width * Craft.Range(seed, 1, 0.45, 0.92));
        var fh = Math.Round(r.Height * Craft.Range(seed, 2, 0.62, 0.98));
        var fx = r.X + Math.Round((r.Width - fw) * Craft.Rand(seed, 3));
        var f = new Rect(fx, r.Bottom - fh, fw, fh);
        if (Craft.Rand(seed, 4) < 0.4)
        {
            // the wall stayed cleaner behind it; its rims left a soft shadow line
            ctx.FillRectangle(Craft.Hi(0.025), f);
            ctx.FillRectangle(Craft.Fade(Colors.Black, 0.20, 0, Craft.Toward.Right), new Rect(f.X, f.Y, 6, f.Height));
            ctx.FillRectangle(Craft.Fade(Colors.Black, 0, 0.14, Craft.Toward.Right), new Rect(f.Right - 6, f.Y, 6, f.Height));
            ctx.FillRectangle(Craft.Fade(Colors.Black, 0.22, 0, Craft.Toward.Down), new Rect(f.X, f.Y, f.Width, 6));
            TextureFill.Draw(ctx, this, new Rect(f.X, f.Y - 4, f.Width, 4), Texture.Dust, 0.35);
        }
        // the footprint: dust drifts at the foot of the wall to both sides of it, a clean, slightly lighter strip where it stood
        TextureFill.Draw(ctx, this, new Rect(r.X, r.Bottom - 6, Math.Max(0, f.X - r.X), 6), Texture.Dust, 0.6);
        TextureFill.Draw(ctx, this, new Rect(f.Right, r.Bottom - 6, Math.Max(0, r.Right - f.Right), 6), Texture.Dust, 0.6);
        ctx.FillRectangle(Craft.Hi(0.035), new Rect(f.X + 1, r.Bottom - 3, f.Width - 2, 3));
        ctx.FillRectangle(Craft.Dark(0.25), new Rect(f.X, r.Bottom - 3, 1, 3));
        ctx.FillRectangle(Craft.Dark(0.25), new Rect(f.Right - 1, r.Bottom - 3, 1, 3));
    }
}
