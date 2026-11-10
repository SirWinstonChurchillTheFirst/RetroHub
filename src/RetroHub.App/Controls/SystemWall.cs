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
        AvaloniaProperty.Register<SystemWall, Color>(nameof(Accent), Color.Parse("#948A7A"));
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

    private static readonly IImmutableSolidColorBrush LightEdge = new ImmutableSolidColorBrush(Color.Parse("#17FFF3E0"));

    static SystemWall()
    {
        AffectsRender<SystemWall>(SystemIdProperty, AccentProperty, MotifShareProperty, MotifInsetProperty, BandHeightProperty, TextureOpacityProperty,
            SplitHeightProperty);
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

    public override void Render(DrawingContext context)
    {
        var r = new Rect(Bounds.Size);
        if (r.Width <= 0 || r.Height <= 0) return;
        var look = SystemPalette.Get(SystemId, Accent);
        Draw(context, this, r, look, BandHeight, MotifShare, MotifInset, TextureOpacity, SplitHeight);
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
            MotifCatalog.Draw(context, look, box, relief: relief);
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
    public static readonly StyledProperty<Color> AccentProperty = AvaloniaProperty.Register<MotifView, Color>(nameof(Accent), Color.Parse("#948A7A"));
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
        // at hero size the motif gets its relief (edge light, foot, material; M5 QA)
        MotifCatalog.Draw(context, look, r, alignRight: true, relief: true);
    }
}
