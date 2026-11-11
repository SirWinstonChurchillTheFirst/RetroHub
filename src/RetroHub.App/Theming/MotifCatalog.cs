using Avalonia;
using Avalonia.Media;

namespace RetroHub.App.Theming;

/// <summary>
/// Category motifs (DESIGN 6.2) on a 120×120 grid, standing on y = 120. Each motif is a short list of flat parts
/// painted in the system's colours: the body (Zweitfarbe, lifted when it would vanish on a dark wall), the colour
/// parts (Farbe), cut-outs in the wall tone (labels, screens, grooves) and a few light lines. Categories, never
/// products (outsider test, DESIGN 6.5). Geometries are parsed once and shared; every catalog system has its own motif.
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
        /// <summary>"Licht" (the lines of the twin lens).</summary>
        Light,
        /// <summary>
        /// "Farbe" lifted to lightness ≥ 0.55 and ≥ 3.2 : 1 against the wall (same hue): a body that would vanish on a near-black wall in its own dark
        /// colour (PS2 tower, M5 QA: ≈ 1.3 : 1) reads with ≥ 3 : 1 without turning into paper.
        /// </summary>
        Lift,
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

    /// <summary>A 3 × 3 perforation of dots (speaker holes as a category, not a product's slot pattern).</summary>
    private static Geometry Perforation(double x0, double y0, double pitch, double r)
    {
        var g = new GeometryGroup { FillRule = FillRule.NonZero };
        for (var row = 0; row < 3; row++)
            for (var col = 0; col < 3; col++)
                g.Children.Add(Dot(x0 + col * pitch, y0 + row * pitch, r));
        return g;
    }

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
        // ---- modules (cartridges) ----
        // upright module with three grooves on top and a label inset in colour
        Motif.Cart8 =>
        [
            B("M14,10 H106 V120 H14 Z"),
            X("M24,17 H96 V19 H24 Z M24,24 H96 V26 H24 Z M24,31 H96 V33 H24 Z"),
            C("M24,46 H96 V112 H24 Z"),
        ],
        // module lying on its side: a raised grip on top, the label left, vertical ribs right
        Motif.CartWide =>
        [
            B("M24,40 H96 V48 H24 Z M6,48 H114 V120 H6 Z"),
            C("M16,60 H70 V110 H16 Z"),
            X("M82,60 H84 V110 H82 Z M90,60 H92 V110 H90 Z M98,60 H100 V110 H98 Z"),
        ],
        // module with rounded shoulders, label in the middle, one groove near the foot
        Motif.Cart16 =>
        [
            B("M18,22 Q18,8 32,8 H88 Q102,8 102,22 V120 H18 Z"),
            X("M30,16 H90 V18 H30 Z M30,22 H90 V24 H30 Z"),
            C("M30,34 H90 V100 H30 Z"),
            X("M18,107 H102 V109 H18 Z"),
        ],
        // module with a narrower ribbed grip stepped on top of the body
        Motif.CartGrip =>
        [
            B("M28,6 H92 V30 H28 Z M16,30 H104 V120 H16 Z"),
            X("M38,10 H40 V26 H38 Z M48,10 H50 V26 H48 Z M58,10 H60 V26 H58 Z M68,10 H70 V26 H68 Z M78,10 H80 V26 H78 Z"),
            C("M28,42 H92 V108 H28 Z"),
        ],
        // module with sloped shoulders
        Motif.Cart64 =>
        [
            B("M14,30 L26,10 H94 L106,30 V120 H14 Z"),
            X("M32,20 H88 V22 H32 Z"),
            C("M28,38 H92 V104 H28 Z"),
        ],
        // a module on a module: the upper one at 80 % width, 8 px joint
        Motif.CartStack =>
        [
            B("M30,20 Q30,8 42,8 H78 Q90,8 90,20 V42 H30 Z M18,62 Q18,50 30,50 H90 Q102,50 102,62 V120 H18 Z"),
            C("M40,16 H80 V36 H40 Z M32,64 H88 V108 H32 Z"),
        ],
        // a flat card with a row of contacts at its foot
        Motif.Card =>
        [
            B("M14,44 H106 V120 H14 Z"),
            C("M24,52 H96 V94 H24 Z"),
            X("M24,104 H28 V114 H24 Z M34,104 H38 V114 H34 Z M44,104 H48 V114 H44 Z M54,104 H58 V114 H54 Z M64,104 H68 V114 H64 Z M74,104 H78 V114 H74 Z M84,104 H88 V114 H84 Z M92,104 H96 V114 H92 Z"),
        ],

        // ---- handhelds ----
        // upright handheld: screen on top, a cross and two keys STACKED on the right, a 3 × 3 perforation at the foot
        // (M5 QA: the level pair beside slanted speaker lines still named the product; no rounded corner)
        Motif.HandheldTall =>
        [
            C("M30,4 H90 L98,12 V120 H22 V12 Z"),
            X("M34,16 H86 V62 H34 Z"),
            X(Cross(42, 86, 10, 6)),
            new(Group(Dot(80, 77, 4.5), Dot(80, 93, 4.5)), Paint.Cut),
            new(Perforation(54, 104, 6, 1.7), Paint.Cut),
        ],
        // the same upright handheld in two tones (R3 QA 3: the outline repeated behind it read as a double, offset
        // contour): the lower half of the shell in the body tone
        Motif.HandheldTallDouble =>
        [
            C("M30,4 H90 L98,12 V120 H22 V12 Z"),
            B("M22,70 H98 V120 H22 Z"),
            X("M34,16 H86 V62 H34 Z"),
            X(Cross(42, 86, 10, 6)),
            new(Group(Dot(80, 77, 4.5), Dot(80, 93, 4.5)), Paint.Cut),
            new(Perforation(54, 104, 6, 1.7), Paint.Cut),
        ],
        // a narrow upright handheld: small screen, a small cross, two keys above each other, speaker lines
        Motif.HandheldNarrow =>
        [
            C("M38,2 H82 L88,8 V120 H32 V8 Z"),
            X("M38,14 H82 V56 H38 Z"),
            X(Cross(47, 76, 7, 4)),
            new(Group(Dot(72, 70, 4), Dot(72, 84, 4)), Paint.Cut),
            X("M42,98 H78 V100 H42 Z M42,104 H78 V106 H42 Z M42,110 H78 V112 H42 Z"),
        ],
        // a landscape handheld: square shoulders with a 6 px chamfer only at the foot, screen in the middle, cross left,
        // two flat slot keys right (M5 QA: the chamfered octagon with round keys was the product's outline)
        Motif.HandheldWide =>
        [
            // the body in its colour lifted to L 0.55: violet on the violet wall was 2.8 : 1 (M5 QA contrast rule)
            new(P("M6,40 H114 V114 L108,120 H12 L6,114 Z"), Paint.Lift),
            X("M38,52 H82 V100 H38 Z"),
            X(Cross(22, 76, 8, 5)),
            X("M91,68 H101 V72 H91 Z M91,82 H101 V86 H91 Z"),
        ],
        // a big landscape handheld: tall body, a large screen, speaker slots under the keys
        Motif.HandheldBig =>
        [
            C("M10,26 H110 L116,32 V114 L110,120 H10 L4,114 V32 Z"),
            X("M32,36 H88 V92 H32 Z"),
            X(Cross(18, 60, 8, 5)),
            new(Group(Dot(102, 54, 4.5), Dot(102, 68, 4.5)), Paint.Cut),
            X("M95,88 H97 V106 H95 Z M101,88 H103 V106 H101 Z M107,88 H109 V106 H107 Z"),
        ],
        // a very wide handheld with grips hanging at both ends
        Motif.HandheldLong =>
        [
            C("M8,56 H112 L118,62 V106 L110,120 H88 L82,104 H38 L32,120 H10 L2,106 V62 Z"),
            X("M42,64 H78 V98 H42 Z"),
            X(Cross(19, 80, 7, 4)),
            new(Group(Dot(100, 74, 4), Dot(100, 88, 4)), Paint.Cut),
        ],
        // a small, almost square handheld: screen on top, a round knob and two keys below
        Motif.HandheldCompact =>
        [
            C("M26,44 H94 L102,52 V112 L94,120 H26 L18,112 V52 Z"),
            X("M34,52 H86 V88 H34 Z"),
            new(Dot(36, 104, 6), Paint.Body),
            new(Group(Dot(76, 104, 4), Dot(88, 100, 4)), Paint.Cut),
        ],
        // a broad, flat handheld whose screen fills most of the front
        Motif.HandheldBroad =>
        [
            C("M12,64 H108 L116,72 V112 L108,120 H12 L4,112 V72 Z"),
            X("M30,70 H90 V114 H30 Z"),
            X(Cross(17, 90, 6, 4)),
            new(Group(Dot(103, 84, 3.5), Dot(103, 97, 3.5)), Paint.Cut),
        ],
        // clamshell (R3 QA 3: one opened block, no hinge details): one upright block, two equal screens cut out
        Motif.Clamshell =>
        [
            C("M18,10 H102 V120 H18 Z"),
            X("M34,18 H86 V56 H34 Z M34,72 H86 V110 H34 Z"),
        ],
        // clamshell with two halves of the SAME width; the upper screen shows depth as a stair of three offset frames
        // (M5 QA: a wider top over a narrower bottom with a circle pad was the product; this is "a clamshell with depth")
        // R3 QA 3 (the stair of frames rendered as a tangle; the hinge was a product detail): one opened block, a wide
        // upper screen with a smaller panel set into it (depth), a smaller lower screen
        Motif.ClamshellWide =>
        [
            new(P("M12,8 H108 V120 H12 Z"), Paint.Lift), // red on anthracite was 2.8 : 1
            X("M22,16 H98 V56 H22 Z M36,72 H84 V110 H36 Z"),
            B("M40,26 H80 V46 H40 Z"),
        ],

        // ---- discs ----
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
        // disc on a drawer: the wide drawer front stands clearly in front of the disc
        Motif.Tray =>
        [
            new(Disc(60, 58, 46, 8), Paint.Color),
            B("M4,92 H116 V120 H4 Z"),
            X("M44,103 H76 V108 H44 Z"),
        ],
        // R3 QA (outsider test: a pointing remote with motion waves was too close to one product), "ein Gerät, das funkt".
        // R3 QA 2 (the upright slab with waves still read as one product standing on its foot): a flat box lying on the
        // board – a slot across its front, a lit strip at its right end – and two wave arcs rising above it
        Motif.Remote =>
        [
            B("M6,84 H114 V114 Q114,120 108,120 H12 Q6,120 6,114 Z"),
            X("M16,96 H74 V99 H16 Z"),
            C("M88,104 H104 V108 H88 Z"),
            new(P("M44,66 Q60,52 76,66"), Paint.Color, 3.5),
            new(P("M32,52 Q60,28 88,52"), Paint.Color, 3.5),
        ],
        // a second screen (R3 QA 3: the hairline frame and the slab with cross and keys were one product): a large
        // screen panel in the body tone and a small screen slab standing in front of it – two screens, no keys
        Motif.Tablet =>
        [
            B("M8,14 H112 V70 H8 Z"),
            X("M14,20 H106 V64 H14 Z"),
            C("M30,78 H90 V120 H30 Z"),
            X("M38,84 H82 V114 H38 Z"),
        ],
        // hybrid (R3 QA: a handheld on a coloured base was too close to one product): a blank screen in the light
        // colour between two separate side blocks with a gap on each side – a cross on the left, two keys on the right –
        // and no base, no colour pair
        // R3 QA 3 (outsider test): reduced to a rectangle with two narrow plain side strips – square, no keys, no cross
        Motif.Dock =>
        [
            B("M12,56 H22 V120 H12 Z"),
            B("M98,56 H108 V120 H98 Z"),
            B("M26,56 H94 V120 H26 Z"),
            new(P("M32,62 H88 V114 H32 Z"), Paint.Lift),
            X("M36,66 H84 V110 H36 Z"),
        ],

        // ---- other devices ----
        // stereo goggles (R3 QA 3: the only motif in outline): a filled visor with a nose notch and strap stubs, the two
        // lenses cut out of it
        Motif.TwinLens =>
        [
            new(P("M12,66 H108 V110 L100,118 H72 L64,104 H56 L48,118 H20 L12,110 Z M3,76 H12 V84 H3 Z M108,76 H117 V84 H108 Z"), Paint.Lift),
            new(Group(Dot(37, 88, 13), Dot(83, 88, 13)), Paint.Cut),
        ],
        Motif.ControlPanel =>
        [
            C("M8,72 H112 L104,120 H16 Z"),
            B("M37,44 H43 V72 H37 Z"),
            new(Dot(40, 38, 9), Paint.Color),
            new(Group(Dot(66, 84, 5), Dot(80, 84, 5), Dot(94, 84, 5), Dot(66, 100, 5), Dot(80, 100, 5), Dot(94, 100, 5)), Paint.Cut),
        ],
        // a joystick: low base, thin shaft with a ball, one key
        Motif.Stick =>
        [
            B("M24,92 H96 V120 H24 Z"),
            B("M57,40 H63 V92 H57 Z"),
            new(Group(Dot(60, 36, 10), Dot(36, 102, 6)), Paint.Color),
        ],
        // a grip stick: a thick handle without a ball on a base with two keys
        Motif.StickGrip =>
        [
            B("M18,98 H102 V120 H18 Z"),
            C("M54,30 H66 L70,34 V98 H50 V34 Z"),
            X("M54,40 H66 V42 H54 Z"),
            new(Group(Dot(32, 109, 5), Dot(88, 109, 5)), Paint.Color),
        ],
        Motif.Floppy =>
        [
            C("M14,8 H96 L110,22 V120 H14 Z"),
            X("M36,8 H84 V42 H36 Z"),
            new(P("M66,14 H76 V36 H66 Z"), Paint.Color),
            B("M28,64 H96 V120 H28 Z"),
        ],
        Motif.KeyBlock => [B("M4,52 H116 V120 H4 Z"), new(KeyBlock(), Paint.Color)],
        // standing slab (R2: the tower with five grooves read as a PC case): a slim slab in its own colour lifted to
        // L 0.55, its top and right faces in the body tone (depth), a vertical tray slit with two keys, two card slots at
        // the foot, short slanted ridges along the side – standing on an oval stand, which no PC case has
        // R3 QA 2 (an upright tower on a round stand was one product's silhouette): two flat boxes stacked on the board,
        // the upper one set back – a drawer slot in the lower one, a key and a lit dot at its right
        Motif.Tower =>
        [
            B("M18,64 H102 V88 H18 Z"),
            new(P("M6,88 H114 V120 H6 Z"), Paint.Lift),
            X("M16,100 H72 V104 H16 Z"),
            X("M28,74 H92 V76 H28 Z"),
            new(Group(Dot(92, 104, 3), Dot(103, 104, 3)), Paint.Cut),
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

    private static readonly Dictionary<uint, IImmutableSolidColorBrush> LiftBrushes = new();

    /// <summary>The colour of <paramref name="look"/> lifted to ≥ L 0.55 and ≥ 3.2 : 1 against its wall, same hue and saturation (<see cref="Paint.Lift"/>).</summary>
    public static Color LiftColor(SystemLook look)
    {
        // at least L 0.55, raised in 0.02 steps until it stands ≥ 3.2 : 1 against its wall (same hue and saturation)
        var (h, sat, l) = ColorUtil.ToHsl(look.Color);
        l = Math.Max(l, 0.55);
        var c = ColorUtil.FromHsl(h, sat, l);
        while (ColorUtil.Contrast(c, look.Wall) < 3.2 && l < 0.86)
        {
            l += 0.02;
            c = ColorUtil.FromHsl(h, sat, l);
        }
        return c;
    }

    private static IBrush LiftBrush(SystemLook look)
    {
        var key = look.Color.ToUInt32();
        lock (LiftBrushes)
        {
            if (!LiftBrushes.TryGetValue(key, out var b)) LiftBrushes[key] = b = new ImmutableSolidColorBrush(LiftColor(look));
            return b;
        }
    }

    /// <summary>
    /// The colours a motif shows against its wall (cut-outs excluded): the QA rule "at least one part ≥ 3 : 1 to the wall"
    /// is checked on these (DesignM5Tests).
    /// </summary>
    public static IEnumerable<(Paint Paint, Color Color)> PaintedColors(SystemLook look)
    {
        foreach (var part in Get(look.Motif).Parts)
        {
            var c = part.Paint switch
            {
                Paint.Color => look.Color,
                Paint.Body => ((ISolidColorBrush)BodyBrush(look)).Color,
                Paint.Light => look.Light,
                Paint.Lift => LiftColor(look),
                _ => (Color?)null,
            };
            if (c is { } color) yield return (part.Paint, color);
        }
    }

    /// <summary>A motif drawn at least this tall (DIP) gets the relief (edge light, lip, texture) when asked for.</summary>
    public const double ReliefMinSize = 160;

    private static readonly IImmutableSolidColorBrush ReliefLight = new ImmutableSolidColorBrush(Color.FromArgb(0x2E, 0xF5, 0xF7, 0xFA));
    private static readonly IImmutableSolidColorBrush ReliefLip = new ImmutableSolidColorBrush(Color.FromArgb(0x4D, 0, 0, 0));
    private static readonly Dictionary<(Motif, int), (Geometry Rim, Geometry Lip)> Reliefs = new();
    private static readonly Dictionary<(Texture, int), IBrush?> ReliefTextures = new();

    /// <summary>
    /// The relief of a motif at a size bucket (M5 QA: at hero size the flat single fills read as clip-art): the 2 px
    /// top/left rim of every part (the silhouette minus itself shifted down-right) and the 6 px foot of every part (the
    /// silhouette minus itself shifted up) – in the 120 grid, cached per motif and 20-px bucket.
    /// </summary>
    private static (Geometry Rim, Geometry Lip) Relief(Motif motif, double scale)
    {
        var bucket = (int)Math.Round(120 * scale / 20);
        lock (Reliefs)
        {
            if (Reliefs.TryGetValue((motif, bucket), out var r)) return r;
            var s = bucket * 20 / 120.0;
            var sil = Silhouette(motif);
            Geometry Shifted(double dx, double dy) => new GeometryGroup { Children = { sil }, Transform = new MatrixTransform(Matrix.CreateTranslation(dx, dy)) };
            r = (new CombinedGeometry(GeometryCombineMode.Exclude, sil, Shifted(2 / s, 2 / s)),
                 new CombinedGeometry(GeometryCombineMode.Exclude, sil, Shifted(0, -6 / s)));
            Reliefs[(motif, bucket)] = r;
            return r;
        }
    }

    /// <summary>The system's texture as a brush in the 120 grid at <paramref name="scale"/> (one tile = its pixel size on screen).</summary>
    private static IBrush? ReliefTexture(Texture texture, double scale)
    {
        var bucket = (int)Math.Round(scale * 100);
        lock (ReliefTextures)
        {
            if (ReliefTextures.TryGetValue((texture, bucket), out var b)) return b;
            b = null;
            if (TextureBrushes.Get(texture) is { } tex && tex.Brush.Source is { } source)
                b = new ImageBrush(source)
                {
                    TileMode = TileMode.Tile,
                    Stretch = Stretch.Fill,
                    AlignmentX = AlignmentX.Left,
                    AlignmentY = AlignmentY.Top,
                    Opacity = 0.08,
                    DestinationRect = new RelativeRect(0, 0, tex.Size.Width / (bucket / 100.0), tex.Size.Height / (bucket / 100.0), RelativeUnit.Absolute),
                }.ToImmutable();
            ReliefTextures[(texture, bucket)] = b;
            return b;
        }
    }

    private static readonly Dictionary<Motif, Geometry> Silhouettes = new();

    /// <summary>
    /// The motif's outline as ONE geometry: the union of all painted parts (lines widened) minus the cut-outs (in painting
    /// order), in the 120 grid. Built once per motif. Drawn with a single fill, it never shows overlapping translucent layers.
    /// </summary>
    public static Geometry Silhouette(Motif motif)
    {
        lock (Silhouettes)
        {
            if (Silhouettes.TryGetValue(motif, out var g)) return g;
            Geometry? solid = null;
            var seamPen = new Pen(Brushes.Black, 4, lineJoin: PenLineJoin.Miter);
            foreach (var part in Get(motif).Parts)
            {
                var geo = part.Stroke > 0
                    ? part.Geometry.GetWidenedGeometry(new Pen(Brushes.Black, part.Stroke, lineCap: PenLineCap.Square, lineJoin: PenLineJoin.Miter))
                    : part.Geometry;
                // cut-outs in painting order: a part drawn later (the depth frames in a screen, a shutter in a window) stays
                if (part.Paint == Paint.Cut) { if (solid != null) solid = new CombinedGeometry(GeometryCombineMode.Exclude, solid, geo); continue; }
                // a part laid over earlier ones keeps a 2 px seam around it (labels, keys, bands, the hinge): in one
                // colour the drawing stays readable instead of melting into a plain block (M5 outsider sheet)
                if (solid != null && part.Stroke <= 0)
                    solid = new CombinedGeometry(GeometryCombineMode.Exclude, solid, part.Geometry.GetWidenedGeometry(seamPen));
                solid = solid == null ? geo : new CombinedGeometry(GeometryCombineMode.Union, solid, geo);
            }
            g = solid ?? new RectangleGeometry(default);
            Silhouettes[motif] = g;
            return g;
        }
    }

    /// <summary>
    /// The placeholder motif (DESIGN 10.2): the <see cref="Silhouette"/> filled once in the system's colour at
    /// <paramref name="opacity"/>, standing on the bottom edge of <paramref name="box"/>, right-aligned.
    /// </summary>
    public static void DrawSilhouette(DrawingContext ctx, SystemLook look, Rect box, double opacity, IBrush? brush = null)
    {
        var scale = Math.Min(box.Width, box.Height) / 120.0;
        using (ctx.PushOpacity(opacity))
        using (ctx.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(box.Right - 120 * scale, box.Bottom - 120 * scale)))
            ctx.DrawGeometry(brush ?? look.ColorBrush, null, Silhouette(look.Motif));
    }

    /// <summary>
    /// Draws the motif of <paramref name="look"/> standing on the bottom edge of <paramref name="box"/> (a square area).
    /// <paramref name="silhouette"/> = one colour (placeholders): every part in "Farbe", cut-outs in
    /// <paramref name="cutout"/> (the ground tone), composited at <paramref name="opacity"/> as one layer.
    /// </summary>
    public static void Draw(DrawingContext ctx, SystemLook look, Rect box, double opacity = 1, bool silhouette = false,
        IBrush? cutout = null, bool alignRight = false, bool relief = false)
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
                    Paint.Lift => silhouette ? look.ColorBrush : LiftBrush(look),
                    _ => ground,
                };
                if (part.Stroke > 0)
                    ctx.DrawGeometry(null, new Pen(brush, part.Stroke, lineCap: PenLineCap.Square, lineJoin: PenLineJoin.Miter), part.Geometry);
                else ctx.DrawGeometry(brush, null, part.Geometry);
            }
            // the relief at hero size (≥ 160): material inside the fill, a thickness at the foot, light on the top/left
            // edges – static, three geometries per motif and size bucket (spine-size motifs stay flat)
            if (relief && !silhouette && 120 * scale >= ReliefMinSize)
            {
                var (rim, lip) = Relief(look.Motif, scale);
                if (ReliefTexture(look.Texture, scale) is { } tex) ctx.DrawGeometry(tex, null, Silhouette(look.Motif));
                ctx.DrawGeometry(ReliefLip, null, lip);
                ctx.DrawGeometry(ReliefLight, null, rim);
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
