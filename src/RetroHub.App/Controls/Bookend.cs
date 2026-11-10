using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using RetroHub.App.Theming;

namespace RetroHub.App.Controls;

/// <summary>
/// A bookend ("Buchstütze", DESIGN 7.4.2) closing a row of sleeves: an L of bent sheet metal standing on the board – a
/// 9 px upright with a punched slot, a foot running out to the right with a lit bend, its own cast shadow and a scuff.
/// The row then ends on purpose instead of being cut off. Not focusable, no hit test; drawn in one pass.
/// </summary>
public sealed class Bookend : Control
{
    /// <summary>The width the bookend takes in its row.</summary>
    public const double SlotWidth = 34;

    private static readonly Color Metal = Color.Parse("#4A453D");

    static Bookend()
    {
        IsHitTestVisibleProperty.OverrideDefaultValue<Bookend>(false);
        FocusableProperty.OverrideDefaultValue<Bookend>(false);
        ClipToBoundsProperty.OverrideDefaultValue<Bookend>(false);
        WidthProperty.OverrideDefaultValue<Bookend>(SlotWidth);
    }

    public override void Render(DrawingContext ctx) => Draw(ctx, new Rect(Bounds.Size));

    /// <summary>The bookend standing on the bottom edge of <paramref name="area"/> (also inside a front's compartment).</summary>
    public static void Draw(DrawingContext ctx, Rect area, double maxUpright = 118)
    {
        var H = area.Height;
        if (H <= 0) return;
        using var _ = ctx.PushTransform(Matrix.CreateTranslation(area.X, area.Y));
        var up = Math.Min(maxUpright, Math.Round(H * 0.8));
        var upright = new Rect(2, H - up, 9, up);
        const double footH = 3, footW = 30;
        Craft.CastShadow(ctx, upright, 12, 0.42);
        // the upright: a rounded top corner (outer), lit left edge, dark right edge, a punched slot
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(upright.X, upright.Bottom), true);
            c.LineTo(new Point(upright.X, upright.Y + 2));
            c.LineTo(new Point(upright.X + 2, upright.Y));
            c.LineTo(new Point(upright.Right - 3, upright.Y));
            c.LineTo(new Point(upright.Right, upright.Y + 3));
            c.LineTo(new Point(upright.Right, upright.Bottom));
            c.EndFigure(true);
        }
        ctx.DrawGeometry(Craft.Solid(Metal), null, g);
        using (ctx.PushGeometryClip(g))
        {
            ctx.FillRectangle(Craft.Curvature, upright);
            Craft.Edges(ctx, upright, lit: 0.18, dark: 0.45, grime: 0.30, grimeHeight: 10, topLit: 0.14, softRight: 3);
            Craft.Scuffs(ctx, upright, Craft.Seed("bookend"), 1, 0.10);
        }
        var slot = new Rect(upright.X + 3, upright.Y + Math.Round(up * 0.22), 3, Math.Round(up * 0.26));
        ctx.FillRectangle(Craft.Solid(Craft.Deep), slot);
        Craft.Recess(ctx, slot, 2, 0.6, 0.12);
        // the foot: a 3 px plate along the board top, its bend lit, its front edge dark
        var foot = new Rect(upright.X, H - footH, footW, footH);
        ctx.FillRectangle(Craft.Solid(ColorUtil.Mix(Metal, Colors.Black, 0.18)), foot);
        ctx.FillRectangle(Craft.Hi(0.16), new Rect(foot.X + 1, foot.Y, foot.Width - 2, 1));
        ctx.FillRectangle(Craft.Hi(0.22), new Rect(upright.Right - 1, H - footH - 2, 3, 2));
        ctx.FillRectangle(Craft.Dark(0.45), new Rect(foot.Right - 1, foot.Y, 1, footH));
    }
}
