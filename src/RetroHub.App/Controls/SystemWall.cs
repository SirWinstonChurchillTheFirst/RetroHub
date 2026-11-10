using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using RetroHub.App.Theming;

namespace RetroHub.App.Controls;

/// <summary>
/// Object "Wand" (DESIGN 1.2, 2.4): a painted wall in the housing material of a system – upper 38 % "Wand hell",
/// lower 62 % "Wand", a 1 px light edge between, an 8 px colour band on top, the era texture and (optionally) the
/// standing motif on the right. Flat tones only (no gradients), so wall changes interpolate.
/// </summary>
public sealed class SystemWall : Control
{
    public static readonly StyledProperty<string?> SystemIdProperty =
        AvaloniaProperty.Register<SystemWall, string?>(nameof(SystemId));
    public static readonly StyledProperty<Color> AccentProperty =
        AvaloniaProperty.Register<SystemWall, Color>(nameof(Accent), Color.Parse("#A0A7B2"));
    /// <summary>Motif height as a share of the wall height (0 = no motif; DESIGN 6.4: 72 % on walls).</summary>
    public static readonly StyledProperty<double> MotifShareProperty =
        AvaloniaProperty.Register<SystemWall, double>(nameof(MotifShare), 0.72);
    public static readonly StyledProperty<double> MotifInsetProperty =
        AvaloniaProperty.Register<SystemWall, double>(nameof(MotifInset), 24);
    public static readonly StyledProperty<double> BandHeightProperty =
        AvaloniaProperty.Register<SystemWall, double>(nameof(BandHeight), 8);
    /// <summary>
    /// Texture strength of this wall (NaN = the system's own). The library's banner raises it to 9 % so the wall reads
    /// as material at 1080 instead of a plain page header (M3 QA).
    /// </summary>
    public static readonly StyledProperty<double> TextureOpacityProperty =
        AvaloniaProperty.Register<SystemWall, double>(nameof(TextureOpacity), double.NaN);
    /// <summary>
    /// Height of the upper "Wand hell" zone in px (NaN = 38 % of the wall). A banner with a title fixes it, so the tone
    /// edge never runs through the glyphs of a two-line name (M5 QA): the light zone holds only the neighbour row.
    /// </summary>
    public static readonly StyledProperty<double> SplitHeightProperty =
        AvaloniaProperty.Register<SystemWall, double>(nameof(SplitHeight), double.NaN);

    /// <summary>
    /// A banner wall (Systemseite, R3 QA: a flat grey bar over a flat band with a hairline read as "coded"): the two tones
    /// meet at a wooden picture rail (lit top face, front with grain, its shadow on the wall below), the tone edge runs
    /// uneven, the colour band is printed 24 px in from the left with seeded wear, and the wall darkens toward the board.
    /// </summary>
    public static readonly StyledProperty<bool> RailProperty =
        AvaloniaProperty.Register<SystemWall, bool>(nameof(Rail));

    private static readonly IImmutableSolidColorBrush LightEdge = new ImmutableSolidColorBrush(Color.Parse("#17FFFFFF"));
    private static readonly Color RailWood = Color.Parse("#363A41");

    static SystemWall()
    {
        AffectsRender<SystemWall>(SystemIdProperty, AccentProperty, MotifShareProperty, MotifInsetProperty, BandHeightProperty, TextureOpacityProperty,
            SplitHeightProperty, RailProperty);
        IsHitTestVisibleProperty.OverrideDefaultValue<SystemWall>(false);
        ClipToBoundsProperty.OverrideDefaultValue<SystemWall>(true);
    }

    public string? SystemId { get => GetValue(SystemIdProperty); set => SetValue(SystemIdProperty, value); }
    public Color Accent { get => GetValue(AccentProperty); set => SetValue(AccentProperty, value); }
    public double MotifShare { get => GetValue(MotifShareProperty); set => SetValue(MotifShareProperty, value); }
    public double MotifInset { get => GetValue(MotifInsetProperty); set => SetValue(MotifInsetProperty, value); }
    public double BandHeight { get => GetValue(BandHeightProperty); set => SetValue(BandHeightProperty, value); }
    public double TextureOpacity { get => GetValue(TextureOpacityProperty); set => SetValue(TextureOpacityProperty, value); }
    public double SplitHeight { get => GetValue(SplitHeightProperty); set => SetValue(SplitHeightProperty, value); }
    public bool Rail { get => GetValue(RailProperty); set => SetValue(RailProperty, value); }

    public override void Render(DrawingContext context)
    {
        var r = new Rect(Bounds.Size);
        if (r.Width <= 0 || r.Height <= 0) return;
        var look = SystemPalette.Get(SystemId, Accent);
        if (Rail) DrawRailWall(context, r, look);
        else Draw(context, this, r, look, BandHeight, MotifShare, MotifInset, TextureOpacity, SplitHeight);
    }

    private StreamGeometry? _upper;
    private (double, double, uint) _upperKey;

    /// <summary>The banner wall with its picture rail (see <see cref="RailProperty"/>).</summary>
    private void DrawRailWall(DrawingContext ctx, Rect r, SystemLook look)
    {
        var seed = Craft.Seed(SystemId);
        var split = double.IsNaN(SplitHeight) ? Math.Round(r.Height * 0.38) : Math.Clamp(Math.Round(SplitHeight), 0, r.Height);
        ctx.FillRectangle(look.WallBrush, r);
        // the upper tone with an uneven lower edge (hidden by the rail, but it shows in the 2 px above it)
        if (_upper == null || _upperKey != (r.Width, split, seed))
        {
            _upperKey = (r.Width, split, seed);
            _upper = new StreamGeometry();
            using var c = _upper.Open();
            c.BeginFigure(new Point(0, 0), true);
            c.LineTo(new Point(r.Width, 0));
            var phase = Craft.Range(seed, 31, 0, Math.PI * 2);
            for (var x = r.Width; x > -8; x -= 8)
            {
                var px = Math.Max(0, x);
                c.LineTo(new Point(px, split + 1.2 * Math.Sin(phase + px / 41.0) + 0.6 * Math.Sin(phase * 3 + px / 9.0)));
            }
            c.EndFigure(true);
        }
        ctx.DrawGeometry(look.WallHiBrush, null, _upper);
        DrawTextures(ctx, this, r, look, TextureOpacity);
        // the lamp above the banner: the wall is lit near the rail and falls off toward the board (no flat panel)
        ctx.FillRectangle(Craft.Fade(Craft.Light, 0.05, 0, Craft.Toward.Down), new Rect(0, split + 6, r.Width, Math.Round((r.Height - split) * 0.45)));
        ctx.FillRectangle(Craft.Fade(Colors.Black, 0, 0.30, Craft.Toward.Down), new Rect(0, r.Bottom - Math.Round((r.Height - split) * 0.5), r.Width, Math.Round((r.Height - split) * 0.5)));
        // the picture rail: 4 px lit top face, 7 px front with grain, a dark lower edge, its shadow on the wall
        var railTop = split - 4;
        var wood = ColorUtil.Mix(RailWood, look.Wall, 0.35);
        ctx.FillRectangle(Craft.Fade(Colors.Black, 0.40, 0, Craft.Toward.Down), new Rect(0, railTop + 11, r.Width, 12));
        ctx.FillRectangle(Craft.Solid(ColorUtil.Mix(wood, Craft.Light, 0.16)), new Rect(0, railTop, r.Width, 4));
        ctx.FillRectangle(Craft.Hi(0.18), new Rect(0, railTop, r.Width, 1));
        var front = new Rect(0, railTop + 4, r.Width, 7);
        ctx.FillRectangle(Craft.Solid(wood), front);
        TextureFill.Draw(ctx, this, front, Texture.BoardGrain, 0.5);
        ctx.FillRectangle(Craft.Fade(Craft.Light, 0.08, 0, Craft.Toward.Down), new Rect(0, front.Y, r.Width, 3));
        ctx.FillRectangle(Craft.Dark(0.55), new Rect(0, front.Bottom - 1, r.Width, 1));
        Craft.Nicks(ctx, front, seed, 5);
        // the colour band: printed 24 px in from the left, 8 px, with its print edge and seeded wear
        if (BandHeight > 0)
        {
            var band = new Rect(24, 10, Math.Max(0, r.Width - 48), BandHeight);
            ctx.FillRectangle(look.ColorBrush, band);
            ctx.FillRectangle(Craft.Solid(ColorUtil.Mix(look.Color, Colors.Black, 0.32)), new Rect(band.X, band.Bottom, band.Width, 1));
            ctx.FillRectangle(Craft.Hi(0.14), new Rect(band.X, band.Y, band.Width, 1));
            // worn print: a few long patches where the ink rubbed thin (soft, the band's full height)
            var wear = Craft.Pick(seed, 33, 2, 4);
            for (var i = 0; i < wear; i++)
            {
                var ww = Math.Round(Craft.Range(seed, 60 + i, 40, 140));
                var wx = band.X + Math.Round(Craft.Range(seed, 40 + i, 0, Math.Max(0, band.Width - ww)));
                ctx.FillRectangle(Craft.Fade(Craft.Light, 0, 0.10, Craft.Toward.Right), new Rect(wx, band.Y, Math.Round(ww / 2), BandHeight));
                ctx.FillRectangle(Craft.Fade(Craft.Light, 0.10, 0, Craft.Toward.Right), new Rect(wx + Math.Round(ww / 2), band.Y, ww - Math.Round(ww / 2), BandHeight));
            }
            // the band's ends are torn a little, not cut
            ctx.FillRectangle(look.WallHiBrush, new Rect(band.X, band.Y, 2, Math.Round(BandHeight * 0.5)));
            ctx.FillRectangle(look.WallHiBrush, new Rect(band.Right - 2, band.Y + Math.Round(BandHeight * 0.4), 2, BandHeight));
        }
    }

    /// <summary>
    /// The era texture of <paramref name="look"/> (and its overlay, e.g. the grooves of the 16-bit modules) into
    /// <paramref name="r"/>; <paramref name="strength"/> NaN = the system's own strength, else the overlay scales along.
    /// </summary>
    public static void DrawTextures(DrawingContext context, Visual visual, Rect r, SystemLook look, double strength = double.NaN)
    {
        var main = double.IsNaN(strength) ? look.TextureOpacity : strength;
        TextureFill.Draw(context, visual, r, look.Texture, main);
        if (look.Overlay != Texture.None && look.TextureOpacity > 0)
            TextureFill.Draw(context, visual, r, look.Overlay, look.OverlayOpacity * main / look.TextureOpacity);
    }

    /// <summary>Draws a wall into <paramref name="r"/> (shared by placeholders and banners).</summary>
    public static void Draw(DrawingContext context, Visual visual, Rect r, SystemLook look, double band, double motifShare, double motifInset,
        double textureOpacity = double.NaN, double splitHeight = double.NaN, bool relief = false)
    {
        var split = double.IsNaN(splitHeight) ? Math.Round(r.Height * 0.38) : Math.Clamp(Math.Round(splitHeight), 0, r.Height);
        context.FillRectangle(look.WallHiBrush, new Rect(r.X, r.Y, r.Width, split));
        context.FillRectangle(look.WallBrush, new Rect(r.X, r.Y + split, r.Width, r.Height - split));
        DrawTextures(context, visual, r, look, textureOpacity);
        context.FillRectangle(LightEdge, new Rect(r.X, r.Y + split, r.Width, 1));
        if (motifShare > 0)
        {
            // never wider than 46 % of the wall, so the name above and maker/count bottom left stay free
            var size = Math.Round(Math.Min(r.Height * motifShare, r.Width * 0.46));
            var box = new Rect(r.Right - motifInset - size, r.Bottom - size, size, size);
            if (!relief || !SystemSpine.DrawMotifPlaque(context, visual, look, box)) MotifCatalog.Draw(context, look, box, relief: relief);
        }
        if (band > 0) context.FillRectangle(look.ColorBrush, new Rect(r.X, r.Y, r.Width, band));
    }
}

/// <summary>
/// A system's motif standing alone (DESIGN 6.4.1: the wall banner of the library – motif right, standing on a stage
/// board). Square, standing on its bottom edge; the cut-outs take the wall tone behind it.
/// </summary>
public sealed class MotifView : Control
{
    public static readonly StyledProperty<string?> SystemIdProperty = AvaloniaProperty.Register<MotifView, string?>(nameof(SystemId));
    public static readonly StyledProperty<Color> AccentProperty = AvaloniaProperty.Register<MotifView, Color>(nameof(Accent), Color.Parse("#A0A7B2"));
    /// <summary>Set: the motif as ONE flat silhouette in this brush (no colours: the outsider-test sheet, DESIGN 6.5).</summary>
    public static readonly StyledProperty<IBrush?> SilhouetteBrushProperty = AvaloniaProperty.Register<MotifView, IBrush?>(nameof(SilhouetteBrush));

    static MotifView()
    {
        AffectsRender<MotifView>(SystemIdProperty, AccentProperty, SilhouetteBrushProperty);
        IsHitTestVisibleProperty.OverrideDefaultValue<MotifView>(false);
    }

    public string? SystemId { get => GetValue(SystemIdProperty); set => SetValue(SystemIdProperty, value); }
    public Color Accent { get => GetValue(AccentProperty); set => SetValue(AccentProperty, value); }
    public IBrush? SilhouetteBrush { get => GetValue(SilhouetteBrushProperty); set => SetValue(SilhouetteBrushProperty, value); }

    public override void Render(DrawingContext context)
    {
        var r = new Rect(Bounds.Size);
        if (r.Width <= 0 || r.Height <= 0 || string.IsNullOrEmpty(SystemId)) return;
        var look = SystemPalette.Get(SystemId, Accent);
        if (SilhouetteBrush is { } flat)
        {
            var scale = Math.Min(r.Width, r.Height) / 120.0;
            using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(r.Right - 120 * scale, r.Bottom - 120 * scale)))
                context.DrawGeometry(flat, null, MotifCatalog.Silhouette(look.Motif));
            return;
        }
        // at hero size the motif is the rendered relief plaque (R3), standing bottom right; else the drawn relief (M5 QA)
        var size = Math.Min(r.Width, r.Height);
        if (SystemSpine.DrawMotifPlaque(context, this, look, new Rect(r.Right - size, r.Bottom - size, size, size))) return;
        MotifCatalog.Draw(context, look, r, alignRight: true, relief: true);
    }
}
