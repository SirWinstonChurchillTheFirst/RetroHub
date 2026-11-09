using Avalonia;
using Avalonia.Media;

namespace RetroHub.App.Theming;

/// <summary>
/// Category motifs (DESIGN 6.2) on a 120×120 grid, standing on y = 120. Each motif is a short list of flat parts
/// painted in the system's colours: the body (Zweitfarbe, lifted when it would vanish on a dark wall), the colour
/// parts (Farbe), cut-outs in the wall tone (labels, screens, grooves) and a few light lines. Categories, never
/// products. Geometries are parsed once and shared; final drawings and the outsider test follow in M5.
/// </summary>
public static class MotifCatalog
{
    /// <summary>How a part is painted.</summary>
    public enum Paint
    {
        /// <summary>"Farbe".</summary>
        Color,
        /// <summary>"Zweitfarbe" – lifted toward paper when it has too little contrast to the wall (black housings).</summary>
        Body,
        /// <summary>Cut-out in the ground tone (screens, label insets, grooves, slots).</summary>
        Cut,
        /// <summary>"Licht" (one groove of the tower, the lines of the twin lens).</summary>
        Light,
    }

    /// <summary>One flat part; <paramref name="Stroke"/> &gt; 0 = drawn as a line of that width instead of filled.</summary>
    public sealed record Part(Geometry Geometry, Paint Paint, double Stroke = 0);

    public sealed record Shape(IReadOnlyList<Part> Parts);

    private static readonly Dictionary<Motif, Shape> Cache = new();

    public static Shape Get(Motif motif)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(motif, out var s)) return s;
            s = new Shape(Create(motif));
            Cache[motif] = s;
            return s;
        }
    }

    private static Geometry P(string data) => Geometry.Parse(data);
    private static Part C(string d) => new(P(d), Paint.Color);
    private static Part B(string d) => new(P(d), Paint.Body);
    private static Part X(string d) => new(P(d), Paint.Cut);

    private static Geometry Disc(double cx, double cy, double r, double hole)
    {
        var g = new GeometryGroup { FillRule = FillRule.EvenOdd };
        g.Children.Add(new EllipseGeometry(new Rect(cx - r, cy - r, 2 * r, 2 * r)));
        if (hole > 0) g.Children.Add(new EllipseGeometry(new Rect(cx - hole, cy - hole, 2 * hole, 2 * hole)));
        return g;
    }

    /// <summary>Concentric hard bands (even-odd: r0 filled to r1, r2 filled to r3, …).</summary>
    private static Geometry Rings(double cx, double cy, params double[] radii)
    {
        var g = new GeometryGroup { FillRule = FillRule.EvenOdd };
        foreach (var r in radii) g.Children.Add(new EllipseGeometry(new Rect(cx - r, cy - r, 2 * r, 2 * r)));
        return g;
    }

    private static Geometry Dot(double cx, double cy, double r) => new EllipseGeometry(new Rect(cx - r, cy - r, 2 * r, 2 * r));

    private static Geometry Group(params Geometry[] parts)
    {
        var g = new GeometryGroup { FillRule = FillRule.NonZero };
        foreach (var p in parts) g.Children.Add(p);
        return g;
    }

    /// <summary>A d-pad cross centred at (cx, cy), arm length a, thickness t.</summary>
    private static string Cross(double cx, double cy, double a, double t) => FormattableString.Invariant(
        $"M{cx - t / 2},{cy - a} H{cx + t / 2} V{cy - t / 2} H{cx + a} V{cy + t / 2} H{cx + t / 2} V{cy + a} H{cx - t / 2} V{cy + t / 2} H{cx - a} V{cy - t / 2} H{cx - t / 2} Z");

    private static Part[] Create(Motif motif) => motif switch
    {
        // module: grooves on top, a label inset in colour
        Motif.Cart8 =>
        [
            B("M14,10 H106 V120 H14 Z"),
            X("M24,17 H96 V19 H24 Z M24,24 H96 V26 H24 Z M24,31 H96 V33 H24 Z"),
            C("M24,46 H96 V112 H24 Z"),
        ],
        Motif.Cart16 =>
        [
            B("M18,22 Q18,8 32,8 H88 Q102,8 102,22 V120 H18 Z"),
            X("M30,16 H90 V18 H30 Z M30,22 H90 V24 H30 Z"),
            C("M30,34 H90 V100 H30 Z"),
            X("M18,107 H102 V109 H18 Z"),
        ],
        Motif.Cart64 =>
        [
            B("M14,30 L26,10 H94 L106,30 V120 H14 Z"),
            X("M32,20 H88 V22 H32 Z"),
            C("M28,38 H92 V104 H28 Z"),
        ],
        // two modules: the upper one at 80 % width, 8 px joint
        Motif.CartStack =>
        [
            B("M30,20 Q30,8 42,8 H78 Q90,8 90,20 V42 H30 Z M18,62 Q18,50 30,50 H90 Q102,50 102,62 V120 H18 Z"),
            C("M40,16 H80 V36 H40 Z M32,64 H88 V108 H32 Z"),
        ],
        Motif.Card =>
        [
            B("M20,60 H100 V120 H20 Z"),
            C("M30,68 H90 V100 H30 Z"),
            X("M30,108 H34 V116 H30 Z M40,108 H44 V116 H40 Z M50,108 H54 V116 H50 Z M60,108 H64 V116 H60 Z M70,108 H74 V116 H70 Z M80,108 H84 V116 H80 Z"),
        ],
        Motif.HandheldTall or Motif.HandheldTallDouble =>
        [
            C("M30,4 H90 L98,12 V120 H22 V12 Z"),
            X("M34,16 H86 V62 H34 Z"),
            X(Cross(42, 86, 10, 6)),
            new(Group(Dot(74, 92, 4.5), Dot(86, 82, 4.5)), Paint.Cut),
        ],
        Motif.HandheldWide =>
        [
            C("M14,40 H106 L114,48 V112 L106,120 H14 L6,112 V48 Z"),
            X("M38,52 H82 V100 H38 Z"),
            X(Cross(22, 76, 8, 5)),
            new(Group(Dot(96, 70, 4), Dot(96, 84, 4)), Paint.Cut),
        ],
        Motif.HandheldBroad =>
        [
            C("M12,64 H108 L116,72 V112 L108,120 H12 L4,112 V72 Z"),
            X("M34,72 H86 V112 H34 Z"),
            X(Cross(19, 92, 7, 4)),
            new(Group(Dot(101, 86, 3.5), Dot(101, 98, 3.5)), Paint.Cut),
        ],
        // clamshell: two halves, a hinge in the body tone, two screens cut out
        Motif.Clamshell =>
        [
            C("M18,10 H102 V56 H18 Z M18,64 H102 V120 H18 Z"),
            B("M14,57 H106 V63 H14 Z"),
            X("M30,18 H90 V50 H30 Z M38,72 H82 V112 H38 Z"),
        ],
        Motif.Disc => [new(Disc(60, 68, 52, 9), Paint.Color)],
        Motif.DiscTrack =>
        [
            new(Disc(60, 68, 52, 9), Paint.Color),
            new(P("M60,28 A40,40 0 0 1 98,55 M60,38 A30,30 0 0 1 88,58"), Paint.Cut, 2),
        ],
        Motif.DiscBands =>
        [
            new(Disc(60, 68, 52, 9), Paint.Color),
            new(Rings(60, 68, 44, 40, 29, 26), Paint.Body),
        ],
        Motif.DiscHalftone =>
        [
            new(Disc(60, 68, 52, 9), Paint.Color),
            new(Group(Dot(78, 50, 1.2), Dot(84, 64, 1.8), Dot(80, 80, 2.2), Dot(92, 52, 2.4), Dot(96, 70, 2.8), Dot(92, 88, 3)), Paint.Body),
        ],
        // small disc with a 6 px ring in the second colour inside it (no outline square: it read as a focus ring)
        Motif.MiniDisc =>
        [
            new(Disc(60, 82, 38, 13), Paint.Color),
            new(Rings(60, 82, 31, 25), Paint.Body),
        ],
        // disc on a drawer: the drawer front stands clearly in front of the disc
        Motif.Tray =>
        [
            new(Disc(60, 62, 40, 7), Paint.Color),
            B("M8,86 H112 V120 H8 Z"),
            X("M44,100 H76 V105 H44 Z"),
        ],
        // slot console: a thin upright slab with one horizontal slot line on a low foot; no disc, no lit slot
        Motif.Stand =>
        [
            B("M46,10 H74 V112 H46 Z"),
            X("M51,30 H69 V33 H51 Z"),
            C("M34,112 H86 V120 H34 Z"),
        ],
        // tablet with grips on a stand: plate, cut screen, two dots outside the screen, foot on y 120
        Motif.Tablet =>
        [
            B("M52,100 H68 V112 H52 Z M34,112 H86 V120 H34 Z"),
            C("M10,30 H110 V100 H10 Z"),
            X("M30,38 H90 V92 H30 Z"),
            new(Group(Dot(20, 54, 4.5), Dot(100, 54, 4.5)), Paint.Body),
        ],
        // a screen plate standing in a flat block (a generic "device in a stand"): no U-shaped dock arms, no side parts
        Motif.Dock =>
        [
            B("M20,24 H100 V92 H20 Z"),
            X("M27,31 H93 V86 H27 Z"),
            C("M12,84 H108 V120 H12 Z"),
            X("M48,99 H72 V103 H48 Z"),
        ],
        Motif.TwinLens =>
        [
            new(P("M14,40 H106 V84 H14 Z M60,84 V112 M36,113 H84"), Paint.Light, 2),
            new(Group(Dot(40, 62, 13), Dot(80, 62, 13)), Paint.Light, 2),
            B("M36,112 H84 V120 H36 Z"),
        ],
        Motif.ControlPanel =>
        [
            C("M8,72 H112 L104,120 H16 Z"),
            B("M37,44 H43 V72 H37 Z"),
            new(Dot(40, 38, 9), Paint.Color),
            new(Group(Dot(66, 84, 5), Dot(80, 84, 5), Dot(94, 84, 5), Dot(66, 100, 5), Dot(80, 100, 5), Dot(94, 100, 5)), Paint.Cut),
        ],
        Motif.Stick =>
        [
            B("M24,92 H96 V120 H24 Z"),
            B("M57,40 H63 V92 H57 Z"),
            new(Group(Dot(60, 36, 10), Dot(36, 102, 6)), Paint.Color),
        ],
        Motif.Floppy =>
        [
            C("M14,8 H96 L110,22 V120 H14 Z"),
            X("M36,8 H84 V42 H36 Z"),
            new(P("M66,14 H76 V36 H66 Z"), Paint.Color),
            B("M28,64 H96 V120 H28 Z"),
        ],
        Motif.KeyBlock => [B("M4,52 H116 V120 H4 Z"), new(KeyBlock(), Paint.Color)],
        // tower: body lifted from the black wall, seven grooves, one in light
        Motif.Tower =>
        [
            B("M30,6 H90 V120 H30 Z"),
            X("M38,20 H82 V22 H38 Z M38,34 H82 V36 H38 Z M38,48 H82 V50 H38 Z M38,76 H82 V78 H38 Z M38,90 H82 V92 H38 Z M38,104 H82 V106 H38 Z"),
            new(P("M38,62 H82 V64 H38 Z"), Paint.Light),
        ],
        _ => [B("M14,10 H106 V120 H14 Z")],
    };

    private static Geometry KeyBlock()
    {
        var g = new GeometryGroup();
        for (var row = 0; row < 3; row++)
            for (var col = 0; col < 5; col++)
            {
                double x = 10 + col * 21, y = 60 + row * 20;
                g.Children.Add(P(FormattableString.Invariant($"M{x + 3},{y} H{x + 11} L{x + 14},{y + 3} V{y + 11} L{x + 11},{y + 14} H{x + 3} L{x},{y + 11} V{y + 3} Z")));
            }
        return g;
    }

    private static readonly Dictionary<uint, IImmutableSolidColorBrush> BodyBrushes = new();

    /// <summary>
    /// The body tone of <paramref name="look"/>: the "Zweitfarbe", lifted toward paper when it would vanish on the wall
    /// (contrast &lt; 1.6, e.g. the near-black housings of Master System, Mega Drive, PS2).
    /// </summary>
    public static IBrush BodyBrush(SystemLook look)
    {
        if (ColorUtil.Contrast(look.Second, look.Wall) >= 1.6) return look.SecondBrush;
        var key = look.WallHi.ToUInt32();
        lock (BodyBrushes)
        {
            if (!BodyBrushes.TryGetValue(key, out var b))
                BodyBrushes[key] = b = new ImmutableSolidColorBrush(ColorUtil.Mix(look.WallHi, SystemPalette.Paper, 0.16));
            return b;
        }
    }

    private static readonly Dictionary<Motif, Geometry> Silhouettes = new();

    /// <summary>
    /// The motif's outline as ONE geometry: the union of all painted parts (lines widened) minus the cut-outs, in the
    /// 120 grid. Built once per motif. Drawn with a single fill, it never shows overlapping translucent layers.
    /// </summary>
    public static Geometry Silhouette(Motif motif)
    {
        lock (Silhouettes)
        {
            if (Silhouettes.TryGetValue(motif, out var g)) return g;
            Geometry? solid = null, cuts = null;
            foreach (var part in Get(motif).Parts)
            {
                var geo = part.Stroke > 0
                    ? part.Geometry.GetWidenedGeometry(new Pen(Brushes.Black, part.Stroke, lineCap: PenLineCap.Square, lineJoin: PenLineJoin.Miter))
                    : part.Geometry;
                if (part.Paint == Paint.Cut) cuts = cuts == null ? geo : new CombinedGeometry(GeometryCombineMode.Union, cuts, geo);
                else solid = solid == null ? geo : new CombinedGeometry(GeometryCombineMode.Union, solid, geo);
            }
            g = solid == null ? new RectangleGeometry(default) : cuts == null ? solid : new CombinedGeometry(GeometryCombineMode.Exclude, solid, cuts);
            Silhouettes[motif] = g;
            return g;
        }
    }

    /// <summary>
    /// The placeholder motif (DESIGN 10.2): the <see cref="Silhouette"/> filled once in the system's colour at
    /// <paramref name="opacity"/>, standing on the bottom edge of <paramref name="box"/>, right-aligned.
    /// </summary>
    public static void DrawSilhouette(DrawingContext ctx, SystemLook look, Rect box, double opacity)
    {
        var scale = Math.Min(box.Width, box.Height) / 120.0;
        using (ctx.PushOpacity(opacity))
        using (ctx.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(box.Right - 120 * scale, box.Bottom - 120 * scale)))
            ctx.DrawGeometry(look.ColorBrush, null, Silhouette(look.Motif));
    }

    /// <summary>
    /// Draws the motif of <paramref name="look"/> standing on the bottom edge of <paramref name="box"/> (a square area).
    /// <paramref name="silhouette"/> = one colour (placeholders): every part in "Farbe", cut-outs in
    /// <paramref name="cutout"/> (the ground tone), composited at <paramref name="opacity"/> as one layer.
    /// </summary>
    public static void Draw(DrawingContext ctx, SystemLook look, Rect box, double opacity = 1, bool silhouette = false,
        IBrush? cutout = null, bool alignRight = false)
    {
        var shape = Get(look.Motif);
        var scale = Math.Min(box.Width, box.Height) / 120.0;
        var x = alignRight ? box.Right - 120 * scale : box.X + (box.Width - 120 * scale) / 2;
        var ground = cutout ?? look.WallBrush;
        var body = silhouette ? look.ColorBrush : BodyBrush(look);
        using (ctx.PushOpacity(opacity))
        using (ctx.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(x, box.Bottom - 120 * scale)))
        {
            foreach (var part in shape.Parts)
            {
                var brush = part.Paint switch
                {
                    Paint.Color => look.ColorBrush,
                    Paint.Body => body,
                    Paint.Light => silhouette ? look.ColorBrush : look.LightBrush,
                    _ => ground,
                };
                if (part.Stroke > 0)
                    ctx.DrawGeometry(null, new Pen(brush, part.Stroke, lineCap: PenLineCap.Square, lineJoin: PenLineJoin.Miter), part.Geometry);
                else ctx.DrawGeometry(brush, null, part.Geometry);
            }
        }
    }

    /// <summary>The motif as an outline only (every part stroked in the system's light): the "Kein Bild" ghost.</summary>
    public static void DrawOutline(DrawingContext ctx, SystemLook look, Rect box, double opacity, double thickness)
    {
        var shape = Get(look.Motif);
        var scale = Math.Min(box.Width, box.Height) / 120.0;
        var pen = new Pen(look.LightBrush, thickness / scale, lineJoin: PenLineJoin.Miter);
        using (ctx.PushOpacity(opacity))
        using (ctx.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(box.X + (box.Width - 120 * scale) / 2, box.Bottom - 120 * scale)))
            foreach (var part in shape.Parts)
                ctx.DrawGeometry(null, pen, part.Geometry);
    }
}
