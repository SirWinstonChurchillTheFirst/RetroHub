using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace RetroHub.App.Controls;

/// <summary>
/// The empty library's line drawing (DESIGN 6.4 point 7, 7.1): "leeres Regal" – a board with two bookends and one box
/// leaning against the right one, 1.5 px lines in paper at 60 %, standing on the bottom edge. No fill, no colour.
/// </summary>
public sealed class EmptyShelfDrawing : Control
{
    private static readonly IPen Line = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromArgb(0x99, 0xF4, 0xEC, 0xDF)), 1.5,
        lineCap: PenLineCap.Square, lineJoin: PenLineJoin.Miter);

    static EmptyShelfDrawing()
    {
        AffectsRender<EmptyShelfDrawing>(BoundsProperty);
        IsHitTestVisibleProperty.OverrideDefaultValue<EmptyShelfDrawing>(false);
    }

    public override void Render(DrawingContext ctx)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        // a 120 × 120 drawing scaled to the width, standing on the bottom edge
        var s = Math.Min(w, h) / 120.0;
        var ox = (w - 120 * s) / 2;
        var oy = h - 120 * s;
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            void Poly(params double[] p)
            {
                c.BeginFigure(new Point(ox + p[0] * s, oy + p[1] * s), false);
                for (var i = 2; i < p.Length; i += 2) c.LineTo(new Point(ox + p[i] * s, oy + p[i + 1] * s));
                c.EndFigure(false);
            }
            Poly(2, 108, 118, 108);                 // the board's top edge
            Poly(2, 116, 118, 116);                 // its front
            Poly(14, 108, 14, 78, 20, 78, 20, 108); // left bookend
            Poly(100, 108, 100, 70, 106, 70, 106, 108); // right bookend
            Poly(84, 108, 69, 102.5, 85, 59.3, 100, 64.8, 84, 108); // one box leaning against it (20°)
        }
        ctx.DrawGeometry(null, Line, g);
    }
}
