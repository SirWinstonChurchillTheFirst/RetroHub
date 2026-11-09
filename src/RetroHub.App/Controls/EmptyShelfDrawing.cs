using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace RetroHub.App.Controls;

/// <summary>
/// The empty library's line drawing (DESIGN 6.4 point 7, 7.1): "leeres Regal" – two bookends and one box leaning
/// against the right one, 1.5 px lines in paper at 60 %, standing DIRECTLY on the bottom edge (= the stage board's top;
/// M2 QA: no board of its own floating above the real one). No fill, no colour.
/// </summary>
public sealed class EmptyShelfDrawing : Control
{
    private static readonly IPen Line = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromArgb(0x99, 0xF4, 0xEC, 0xDF)), 1.5,
        lineCap: PenLineCap.Flat, lineJoin: PenLineJoin.Miter);

    /// <summary>
    /// Scale the FIGURES (not the 120 grid) to the control's height: the leaning box is then as tall as the control and
    /// stands at the scale of real boxes beside the outlined places (library, M3 QA: the 200 px grid drew 80 px figures).
    /// Width needed: ≈ 1.9 × height.
    /// </summary>
    public static readonly StyledProperty<bool> TightProperty = AvaloniaProperty.Register<EmptyShelfDrawing, bool>(nameof(Tight));

    public bool Tight { get => GetValue(TightProperty); set => SetValue(TightProperty, value); }

    static EmptyShelfDrawing()
    {
        AffectsRender<EmptyShelfDrawing>(BoundsProperty, TightProperty);
        IsHitTestVisibleProperty.OverrideDefaultValue<EmptyShelfDrawing>(false);
    }

    public override void Render(DrawingContext ctx)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
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
/// the shelf reads as one waiting to be filled. Not focusable, no fill.
/// </summary>
public sealed class EmptySlot : Control
{
    private static readonly IPen Dash = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromArgb(0x1F, 0xF4, 0xEC, 0xDF)), 1,
        new ImmutableDashStyle([4, 4], 0), PenLineCap.Flat, PenLineJoin.Miter);

    static EmptySlot()
    {
        AffectsRender<EmptySlot>(BoundsProperty);
        IsHitTestVisibleProperty.OverrideDefaultValue<EmptySlot>(false);
        FocusableProperty.OverrideDefaultValue<EmptySlot>(false);
    }

    public override void Render(DrawingContext ctx)
    {
        var r = new Rect(Bounds.Size).Deflate(0.5);
        if (r.Width <= 0 || r.Height <= 0) return;
        ctx.DrawRectangle(null, Dash, r);
    }
}
