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
    /// <summary>The width a small bookend takes (inside a front's compartment).</summary>
    public const double SlotWidth = 34;

    /// <summary>The width the heavy bookend at the end of a row takes (R3 QA: the 9 px stick read as a faint line).</summary>
    public const double RowSlotWidth = 44;

    private static readonly Color Metal = Color.Parse("#464A51");
    private static readonly Color MetalHeavy = Color.Parse("#585D66");

    static Bookend()
    {
        IsHitTestVisibleProperty.OverrideDefaultValue<Bookend>(false);
        FocusableProperty.OverrideDefaultValue<Bookend>(false);
        ClipToBoundsProperty.OverrideDefaultValue<Bookend>(false);
        WidthProperty.OverrideDefaultValue<Bookend>(RowSlotWidth);
    }

    public Bookend()
    {
        // drawn before the row it closes: its foot runs under the last sleeve
        ZIndex = -1;
    }

    public override void Render(DrawingContext ctx)
    {
        var r = new Rect(Bounds.Size);
        // the rendered bent steel (R3): the upright stands 6 px into the slot, its foot runs left under the row
        if (DrawRendered(ctx, this, new Point(6, r.Height), Math.Min(132, Math.Round(r.Height * 0.8)))) return;
        Draw(ctx, r, 140, 16);
    }

    /// <summary>
    /// The rendered bookend (sheet steel bent to an L, Blender) with the inside of its bend at <paramref name="foot"/> and its
    /// upright <paramref name="upright"/> px tall; false when the render is not available.
    /// </summary>
    public static bool DrawRendered(DrawingContext ctx, Visual visual, Point foot, double upright)
    {
        if (Rendered.MetaOf("bookend") is not { } m || upright <= 0) return false;
        var k = upright / 132.0;
        var fx = m.Value("footX");
        var fy = m.Value("footY");
        var front = new Rect(foot.X - (fx - m.Front.X) * k, foot.Y - (fy - m.Front.Y) * k, m.Front.Width * k, m.Front.Height * k);
        return Rendered.Draw(ctx, visual, "bookend", front);
    }

    /// <summary>The bookend standing on the bottom edge of <paramref name="area"/> (also inside a front's compartment).</summary>
    public static void Draw(DrawingContext ctx, Rect area, double maxUpright = 118, double thickness = 9)
    {
        var heavy = thickness >= 12;
        var metal = heavy ? MetalHeavy : Metal;
        var H = area.Height;
        if (H <= 0) return;
        using var _ = ctx.PushTransform(Matrix.CreateTranslation(area.X, area.Y));
        var up = Math.Min(maxUpright, Math.Round(H * 0.8));
        var upright = new Rect(2, H - up, thickness, up);
        var footH = heavy ? 4.0 : 3.0;
        var footW = heavy ? 40.0 : 30.0;
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
        ctx.DrawGeometry(Craft.Solid(metal), null, g);
        using (ctx.PushGeometryClip(g))
        {
            ctx.FillRectangle(Craft.Curvature, upright);
            Craft.Edges(ctx, upright, lit: 0.18, dark: 0.45, grime: 0.30, grimeHeight: 10, topLit: heavy ? 0.12 : 0.14, softRight: 3);
            // the heavy one: a lit top face (the plate's cut edge catches the lamp) and a worn patch where hands push it
            if (heavy)
            {
                ctx.FillRectangle(Craft.Hi(0.22), new Rect(upright.X + 2, upright.Y, upright.Width - 5, 2));
                ctx.FillRectangle(Craft.Hi(0.06), new Rect(upright.X + 3, upright.Y + Math.Round(up * 0.55), upright.Width - 7, Math.Round(up * 0.18)));
            }
            Craft.Scuffs(ctx, upright, Craft.Seed("bookend"), 1, 0.10);
        }
        var slot = new Rect(upright.X + Math.Round(thickness / 3), upright.Y + Math.Round(up * 0.22), heavy ? 4 : 3, Math.Round(up * 0.26));
        ctx.FillRectangle(Craft.Solid(Craft.Deep), slot);
        Craft.Recess(ctx, slot, 2, 0.6, 0.12);
        // the foot: a 3 px plate along the board top, its bend lit, its front edge dark
        var foot = new Rect(upright.X, H - footH, footW, footH);
        ctx.FillRectangle(Craft.Solid(ColorUtil.Mix(metal, Colors.Black, 0.18)), foot);
        ctx.FillRectangle(Craft.Hi(0.16), new Rect(foot.X + 1, foot.Y, foot.Width - 2, 1));
        ctx.FillRectangle(Craft.Hi(0.22), new Rect(upright.Right - 1, H - footH - 2, 3, 2));
        ctx.FillRectangle(Craft.Dark(0.45), new Rect(foot.Right - 1, foot.Y, 1, footH));
    }
}
