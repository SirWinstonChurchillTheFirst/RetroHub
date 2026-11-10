using Avalonia;
using Avalonia.Media;
using RetroHub.App.Theming;

namespace RetroHub.App.Controls;

/// <summary>
/// The drawing kit of the handmade objects (DESIGN 4.6 "Gezeichnet, nicht gesetzt"): every object is built from the same
/// parts – material (card fibre, board grain), edges (lit left/top, dark right/bottom, grime at the foot), depth (side
/// and top faces, cast and contact shadows), paper labels with a torn-by-hand outline and a slight tilt, and wear
/// (scuffs, nicks, a dented corner). All variation is SEEDED from a stable hash of an id (FNV-1a – never
/// <see cref="string.GetHashCode()"/>, which changes per process and would break the screenshot tests), so an object
/// looks the same on every start. Brushes are shared (one per colour/alpha), geometries are cached per key; nothing here
/// allocates per frame once an object has been drawn.
/// </summary>
public static class Craft
{
    // ===================== seeded variation =====================

    /// <summary>A stable 32-bit hash of <paramref name="text"/> (FNV-1a over the UTF-16 code units).</summary>
    public static uint Seed(string? text)
    {
        var h = 2166136261u;
        if (text != null)
            foreach (var c in text)
            {
                h ^= c;
                h *= 16777619u;
            }
        return h;
    }

    /// <summary>A well-mixed 32-bit value of (<paramref name="seed"/>, <paramref name="salt"/>) (murmur3 finaliser).</summary>
    public static uint Mix(uint seed, int salt)
    {
        var h = seed ^ unchecked((uint)salt * 0x9E3779B9u);
        h ^= h >> 16;
        h *= 0x85EBCA6Bu;
        h ^= h >> 13;
        h *= 0xC2B2AE35u;
        h ^= h >> 16;
        return h;
    }

    /// <summary>A seeded value in [0, 1).</summary>
    public static double Rand(uint seed, int salt) => Mix(seed, salt) / 4294967296.0;

    /// <summary>A seeded value in [<paramref name="a"/>, <paramref name="b"/>).</summary>
    public static double Range(uint seed, int salt, double a, double b) => a + (b - a) * Rand(seed, salt);

    /// <summary>A seeded whole number in [<paramref name="a"/>, <paramref name="b"/>].</summary>
    public static int Pick(uint seed, int salt, int a, int b) => a + (int)(Mix(seed, salt) % (uint)(b - a + 1));

    // ===================== colours =====================

    /// <summary>Label paper (a step warmer and darker than the UI paper: a printed sticker, not a page).</summary>
    public static readonly Color LabelPaper = Color.Parse("#E9DFCC");
    /// <summary>Printing ink on paper.</summary>
    public static readonly Color LabelInk = Color.Parse("#1A1714");
    /// <summary>The light of an edge (#FFF3E0, the warm paper white – never pure white).</summary>
    public static readonly Color Light = Color.Parse("#FFF3E0");
    /// <summary>Room-deep: the darkest tone of the room (slots, gaps, grooves).</summary>
    public static readonly Color Deep = Color.Parse("#0A0908");

    private static readonly Dictionary<uint, IImmutableSolidColorBrush> Solids = new();

    /// <summary>One shared brush per colour.</summary>
    public static IImmutableSolidColorBrush Solid(Color c)
    {
        var key = c.ToUInt32();
        lock (Solids)
        {
            if (Solids.TryGetValue(key, out var b)) return b;
            if (Solids.Count > 2048) Solids.Clear();
            return Solids[key] = new ImmutableSolidColorBrush(c);
        }
    }

    /// <summary><paramref name="c"/> at <paramref name="alpha"/> (0..1).</summary>
    public static Color A(Color c, double alpha) => Color.FromArgb((byte)Math.Clamp(Math.Round(alpha * 255), 0, 255), c.R, c.G, c.B);

    /// <summary>The edge light at <paramref name="alpha"/>.</summary>
    public static IImmutableSolidColorBrush Hi(double alpha) => Solid(A(Light, alpha));

    /// <summary>Black at <paramref name="alpha"/>.</summary>
    public static IImmutableSolidColorBrush Dark(double alpha) => Solid(A(Colors.Black, alpha));

    /// <summary>Direction of a fade: the colour is full at the named side's opposite and fades towards it.</summary>
    public enum Toward { Down, Up, Right, Left }

    private static readonly Dictionary<(uint, uint, Toward), IBrush> Fades = new();

    /// <summary>
    /// A linear fade from <paramref name="c"/> at <paramref name="from"/> to <paramref name="to"/> alpha, running
    /// <paramref name="toward"/> across whatever it fills (relative units: one brush serves every size).
    /// </summary>
    public static IBrush Fade(Color c, double from, double to, Toward toward)
    {
        var a = A(c, from);
        var b = A(c, to);
        var key = (a.ToUInt32(), b.ToUInt32(), toward);
        lock (Fades)
        {
            if (Fades.TryGetValue(key, out var hit)) return hit;
            var (s, e) = toward switch
            {
                Toward.Down => (new RelativePoint(0, 0, RelativeUnit.Relative), new RelativePoint(0, 1, RelativeUnit.Relative)),
                Toward.Up => (new RelativePoint(0, 1, RelativeUnit.Relative), new RelativePoint(0, 0, RelativeUnit.Relative)),
                Toward.Right => (new RelativePoint(0, 0, RelativeUnit.Relative), new RelativePoint(1, 0, RelativeUnit.Relative)),
                _ => (new RelativePoint(1, 0, RelativeUnit.Relative), new RelativePoint(0, 0, RelativeUnit.Relative)),
            };
            return Fades[key] = new ImmutableLinearGradientBrush([new ImmutableGradientStop(0, a), new ImmutableGradientStop(1, b)], startPoint: s, endPoint: e);
        }
    }

    /// <summary>
    /// The curvature of a printed box spine wrapping round its edges: a faint light at the left edge, nothing in the
    /// middle, darkening to 14 % black at the right edge (one shared brush, relative to what it fills).
    /// </summary>
    public static readonly IBrush Curvature = new ImmutableLinearGradientBrush(
        [
            new ImmutableGradientStop(0, A(Light, 0.06)),
            new ImmutableGradientStop(0.18, A(Light, 0.015)),
            new ImmutableGradientStop(0.55, A(Light, 0)),
            new ImmutableGradientStop(1, A(Colors.Black, 0.16)),
        ],
        startPoint: new RelativePoint(0, 0, RelativeUnit.Relative), endPoint: new RelativePoint(1, 0, RelativeUnit.Relative));

    // ===================== edges and shadows =====================

    /// <summary>
    /// The edges of a solid object in <paramref name="r"/>: a 1 px lit left edge and top, a dark right edge (1 px hard
    /// plus a soft fade inwards) and grime at the foot (a dark fade upwards).
    /// </summary>
    public static void Edges(DrawingContext ctx, Rect r, double lit = 0.10, double dark = 0.35, double grime = 0.22, double grimeHeight = 6,
        double topLit = 0.08, double softRight = 3)
    {
        if (r.Width <= 2 || r.Height <= 2) return;
        if (grime > 0 && grimeHeight > 0) ctx.FillRectangle(Fade(Colors.Black, 0, grime, Toward.Down), new Rect(r.X, r.Bottom - grimeHeight, r.Width, grimeHeight));
        if (softRight > 0) ctx.FillRectangle(Fade(Colors.Black, 0, dark * 0.6, Toward.Right), new Rect(r.Right - 1 - softRight, r.Y, softRight, r.Height));
        if (lit > 0) ctx.FillRectangle(Hi(lit), new Rect(r.X, r.Y, 1, r.Height));
        if (topLit > 0) ctx.FillRectangle(Hi(topLit), new Rect(r.X + 1, r.Y, r.Width - 2, 1));
        if (dark > 0) ctx.FillRectangle(Dark(dark), new Rect(r.Right - 1, r.Y, 1, r.Height));
    }

    private static readonly Dictionary<(int, int, int), StreamGeometry> Parallelograms = new();

    /// <summary>
    /// The shadow an object standing in <paramref name="obj"/> casts on the wall behind it (light from the front left):
    /// a parallelogram leaning back to the right by <paramref name="reach"/>, dark at the object's edge and fading out.
    /// </summary>
    public static void CastShadow(DrawingContext ctx, Rect obj, double reach = 10, double alpha = 0.38)
    {
        if (obj.Width <= 0 || obj.Height <= 0 || reach <= 0) return;
        var key = ((int)Math.Round(obj.Height), (int)Math.Round(reach), 0);
        StreamGeometry g;
        lock (Parallelograms)
        {
            if (!Parallelograms.TryGetValue(key, out g!))
            {
                if (Parallelograms.Count > 256) Parallelograms.Clear();
                g = new StreamGeometry();
                using (var s = g.Open())
                {
                    var h = Math.Round(obj.Height);
                    var rise = Math.Round(reach * 0.6);
                    s.BeginFigure(new Point(0, h), true);
                    s.LineTo(new Point(0, 0));
                    s.LineTo(new Point(reach, rise));
                    s.LineTo(new Point(reach, h));
                    s.EndFigure(true);
                }
                Parallelograms[key] = g;
            }
        }
        using (ctx.PushTransform(Matrix.CreateTranslation(obj.Right, obj.Y)))
            ctx.DrawGeometry(Fade(Colors.Black, alpha, 0, Toward.Right), null, g);
    }

    /// <summary>The 4 px contact shadow an object leaves on the board top right under its foot.</summary>
    public static void Contact(DrawingContext ctx, double x, double y, double width, double height = 4, double alpha = 0.55) =>
        ctx.FillRectangle(Fade(Colors.Black, alpha, 0, Toward.Down), new Rect(x - 1, y, width + 2, height));

    // ===================== wear =====================

    /// <summary>1–<paramref name="count"/> seeded scuffs: thin light strokes at random heights, 6–14 px long.</summary>
    public static void Scuffs(DrawingContext ctx, Rect r, uint seed, int count, double alpha = 0.06, int salt = 100)
    {
        if (r.Width < 8 || r.Height < 8) return;
        for (var i = 0; i < count; i++)
        {
            var len = Math.Min(r.Width - 4, Range(seed, salt + 4 * i, 6, 14));
            var x = r.X + 2 + Math.Floor(Rand(seed, salt + 4 * i + 1) * Math.Max(1, r.Width - len - 4));
            var y = r.Y + 6 + Math.Floor(Rand(seed, salt + 4 * i + 2) * Math.Max(1, r.Height - 12));
            var dy = Rand(seed, salt + 4 * i + 3) < 0.5 ? 0 : 1;
            ctx.FillRectangle(Hi(alpha), new Rect(x, y, Math.Round(len * 0.6), 1));
            ctx.FillRectangle(Hi(alpha * 0.7), new Rect(x + Math.Round(len * 0.6), y + dy, Math.Round(len * 0.4), 1));
        }
    }

    /// <summary>
    /// Seeded nicks on a board edge or plate: <paramref name="count"/> small dents 3–8 px wide (1 px dark, 1 px light
    /// below) along the top of <paramref name="r"/>.
    /// </summary>
    public static void Nicks(DrawingContext ctx, Rect r, uint seed, int count, int salt = 200)
    {
        if (r.Width < 20) return;
        for (var i = 0; i < count; i++)
        {
            var w = Math.Round(Range(seed, salt + 3 * i, 3, 8));
            var x = r.X + Math.Round(Rand(seed, salt + 3 * i + 1) * (r.Width - w));
            var y = r.Y + Math.Round(Rand(seed, salt + 3 * i + 2) * Math.Max(0, r.Height - 2));
            ctx.FillRectangle(Dark(0.35), new Rect(x, y, w, 1));
            ctx.FillRectangle(Hi(0.07), new Rect(x + 1, y + 1, Math.Max(1, w - 2), 1));
        }
    }

    // ===================== paper =====================

    private static readonly Dictionary<(int, int, uint, int), StreamGeometry> Papers = new();

    /// <summary>
    /// The outline of a cut paper label of <paramref name="size"/> (origin top left): a polygon with a vertex at every
    /// corner and 1–2 more along each edge, each jittered by up to <paramref name="jitter"/> px – never a crisp
    /// rectangle. Cached per (size, seed, jitter).
    /// </summary>
    public static Geometry PaperOutline(Size size, uint seed, double jitter = 0.7)
    {
        var key = ((int)Math.Round(size.Width * 2), (int)Math.Round(size.Height * 2), seed, (int)Math.Round(jitter * 10));
        lock (Papers)
        {
            if (Papers.TryGetValue(key, out var hit)) return hit;
            if (Papers.Count > 1024) Papers.Clear();
            var w = size.Width;
            var h = size.Height;
            var pts = new List<Point>(12);
            var salt = 0;
            double J() => (Rand(seed, salt++) * 2 - 1) * jitter;
            // corners, with 1 extra vertex on short edges and 2 on long ones
            void Edge(Point a, Point b, bool horizontal)
            {
                var len = horizontal ? Math.Abs(b.X - a.X) : Math.Abs(b.Y - a.Y);
                var extra = len > 60 ? 2 : 1;
                pts.Add(new Point(a.X + J(), a.Y + J()));
                for (var i = 1; i <= extra; i++)
                {
                    var t = (i + (Rand(seed, salt++) - 0.5) * 0.4) / (extra + 1);
                    var p = new Point(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
                    pts.Add(horizontal ? new Point(p.X, p.Y + J()) : new Point(p.X + J(), p.Y));
                }
            }
            Edge(new Point(0, 0), new Point(w, 0), true);
            Edge(new Point(w, 0), new Point(w, h), false);
            Edge(new Point(w, h), new Point(0, h), true);
            Edge(new Point(0, h), new Point(0, 0), false);
            var g = new StreamGeometry();
            using (var s = g.Open())
            {
                s.BeginFigure(pts[0], true);
                for (var i = 1; i < pts.Count; i++) s.LineTo(pts[i]);
                s.EndFigure(true);
            }
            Papers[key] = g;
            return g;
        }
    }

    /// <summary>
    /// A paper label in <paramref name="r"/> tilted by <paramref name="degrees"/> about its centre: a soft shadow
    /// (offset 1, 2), a darker bottom edge (its thickness), the paper itself and its fibres. Draw the content inside
    /// <see cref="PushLabel"/> so it tilts with the paper.
    /// </summary>
    public static void DrawPaper(DrawingContext ctx, Visual visual, Rect r, uint seed, Color paper, double degrees = 0, double jitter = 0.7,
        double shadow = 0.30, double fibres = 0.05)
    {
        if (r.Width <= 0 || r.Height <= 0) return;
        var outline = PaperOutline(r.Size, seed, jitter);
        using (PushLabel(ctx, r, degrees))
        {
            if (shadow > 0)
                using (ctx.PushTransform(Matrix.CreateTranslation(1, 2)))
                    ctx.DrawGeometry(Dark(shadow), null, outline);
            using (ctx.PushTransform(Matrix.CreateTranslation(0, 1)))
                ctx.DrawGeometry(Solid(ColorUtil.Mix(paper, Colors.Black, 0.22)), null, outline);
            ctx.DrawGeometry(Solid(paper), null, outline);
            if (fibres > 0)
                using (ctx.PushGeometryClip(outline))
                    TextureFill.Draw(ctx, visual, new Rect(r.Size), Texture.Card, fibres);
        }
    }

    /// <summary>Moves the origin to the top left of <paramref name="r"/>, tilted by <paramref name="degrees"/> about its centre.</summary>
    public static DrawingContext.PushedState PushLabel(DrawingContext ctx, Rect r, double degrees)
    {
        var m = Matrix.CreateTranslation(r.X, r.Y);
        if (Math.Abs(degrees) > 0.01)
            m = Matrix.CreateTranslation(-r.Width / 2, -r.Height / 2) * Matrix.CreateRotation(degrees * Math.PI / 180)
                * Matrix.CreateTranslation(r.X + r.Width / 2, r.Y + r.Height / 2);
        return ctx.PushTransform(m);
    }

    /// <summary>
    /// Printed text: the ink a hair off register – a faint second pass 0.5 px to the right under the main pass, so type
    /// on a label reads as printed on paper, not set on a screen.
    /// </summary>
    public static void Print(DrawingContext ctx, FormattedText text, Point at, double ghost = 0.25)
    {
        if (ghost > 0)
            using (ctx.PushOpacity(ghost))
                ctx.DrawText(text, new Point(at.X + 0.5, at.Y));
        ctx.DrawText(text, at);
    }

    /// <summary>
    /// An engraved groove across <paramref name="r"/> (1 px dark, 1 px light directly below): a line cut into the material
    /// instead of a hairline drawn on it.
    /// </summary>
    public static void Groove(DrawingContext ctx, double x, double y, double width, double dark = 0.45, double light = 0.05)
    {
        if (width <= 0) return;
        ctx.FillRectangle(Dark(dark), new Rect(x, y, width, 1));
        ctx.FillRectangle(Hi(light), new Rect(x, y + 1, width, 1));
    }

    /// <summary>
    /// A recessed window in <paramref name="r"/> (a display, a slot cut into a front): a 3 px inner shadow at its top
    /// and 2 px at its left, 1 px light at its bottom edge.
    /// </summary>
    public static void Recess(DrawingContext ctx, Rect r, double depth = 3, double alpha = 0.50, double light = 0.06)
    {
        if (r.Width <= 0 || r.Height <= 0) return;
        ctx.FillRectangle(Fade(Colors.Black, alpha, 0, Toward.Down), new Rect(r.X, r.Y, r.Width, depth));
        ctx.FillRectangle(Fade(Colors.Black, alpha * 0.7, 0, Toward.Right), new Rect(r.X, r.Y, Math.Max(1, depth - 1), r.Height));
        if (light > 0) ctx.FillRectangle(Hi(light), new Rect(r.X, r.Bottom - 1, r.Width, 1));
    }
}

/// <summary>
/// A plain standing box face for templates (the "Alle" spine at the end of a shelf): the fill with the spine's curvature,
/// the colour band PRINTED inset at the top, lit and dark edges, and the side and top faces outside its bounds – the same
/// drawn object as the system spines beside it (M7 "gezeichnet").
/// </summary>
public sealed class BoxFace : Avalonia.Controls.Control
{
    public static readonly StyledProperty<IBrush?> FillProperty = AvaloniaProperty.Register<BoxFace, IBrush?>(nameof(Fill));
    public static readonly StyledProperty<IBrush?> BandProperty = AvaloniaProperty.Register<BoxFace, IBrush?>(nameof(Band));

    static BoxFace()
    {
        AffectsRender<BoxFace>(FillProperty, BandProperty);
        IsHitTestVisibleProperty.OverrideDefaultValue<BoxFace>(false);
        ClipToBoundsProperty.OverrideDefaultValue<BoxFace>(false);
    }

    public IBrush? Fill { get => GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public IBrush? Band { get => GetValue(BandProperty); set => SetValue(BandProperty, value); }

    public override void Render(DrawingContext ctx)
    {
        var r = new Rect(Bounds.Size);
        if (r.Width <= 0 || r.Height <= 0) return;
        var tone = (Fill as ISolidColorBrush)?.Color ?? Color.Parse("#2E2A23");
        const double side = 4, top = 3;
        var sideG = new StreamGeometry();
        using (var c = sideG.Open())
        {
            c.BeginFigure(new Point(r.Right, 0), true);
            c.LineTo(new Point(r.Right + side, -top));
            c.LineTo(new Point(r.Right + side, r.Bottom - top));
            c.LineTo(new Point(r.Right, r.Bottom));
            c.EndFigure(true);
        }
        ctx.DrawGeometry(Craft.Solid(ColorUtil.Mix(tone, Colors.Black, 0.45)), null, sideG);
        var topG = new StreamGeometry();
        using (var c = topG.Open())
        {
            c.BeginFigure(new Point(0, 0), true);
            c.LineTo(new Point(side, -top));
            c.LineTo(new Point(r.Right + side, -top));
            c.LineTo(new Point(r.Right, 0));
            c.EndFigure(true);
        }
        ctx.DrawGeometry(Craft.Solid(ColorUtil.Mix(tone, Craft.Light, 0.12)), null, topG);
        ctx.FillRectangle(Fill ?? Craft.Solid(tone), r);
        ctx.FillRectangle(Craft.Curvature, r);
        if (Band is { } band)
        {
            var b = new Rect(5, 5, Math.Max(0, r.Width - 10), 8);
            ctx.FillRectangle(band, b);
            ctx.FillRectangle(Craft.Dark(0.30), new Rect(b.X, b.Bottom, b.Width, 1));
            ctx.FillRectangle(Craft.Hi(0.16), new Rect(b.X, b.Y, b.Width, 1));
        }
        Craft.Edges(ctx, r, lit: 0.09, dark: 0.32, grime: 0.20, grimeHeight: 10, topLit: 0.10, softRight: 4);
    }
}
