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

    private static readonly IImmutableSolidColorBrush LightEdge = new ImmutableSolidColorBrush(Color.Parse("#17FFF3E0"));

    static SystemWall()
    {
        AffectsRender<SystemWall>(SystemIdProperty, AccentProperty, MotifShareProperty, MotifInsetProperty, BandHeightProperty);
        IsHitTestVisibleProperty.OverrideDefaultValue<SystemWall>(false);
        ClipToBoundsProperty.OverrideDefaultValue<SystemWall>(true);
    }

    public string? SystemId { get => GetValue(SystemIdProperty); set => SetValue(SystemIdProperty, value); }
    public Color Accent { get => GetValue(AccentProperty); set => SetValue(AccentProperty, value); }
    public double MotifShare { get => GetValue(MotifShareProperty); set => SetValue(MotifShareProperty, value); }
    public double MotifInset { get => GetValue(MotifInsetProperty); set => SetValue(MotifInsetProperty, value); }
    public double BandHeight { get => GetValue(BandHeightProperty); set => SetValue(BandHeightProperty, value); }

    public override void Render(DrawingContext context)
    {
        var r = new Rect(Bounds.Size);
        if (r.Width <= 0 || r.Height <= 0) return;
        var look = SystemPalette.Get(SystemId, Accent);
        Draw(context, this, r, look, BandHeight, MotifShare, MotifInset);
    }

    /// <summary>Draws a wall into <paramref name="r"/> (shared by placeholders and banners).</summary>
    public static void Draw(DrawingContext context, Visual visual, Rect r, SystemLook look, double band, double motifShare, double motifInset)
    {
        var split = Math.Round(r.Height * 0.38);
        context.FillRectangle(look.WallHiBrush, new Rect(r.X, r.Y, r.Width, split));
        context.FillRectangle(look.WallBrush, new Rect(r.X, r.Y + split, r.Width, r.Height - split));
        TextureFill.Draw(context, visual, r, look.Texture, look.TextureOpacity);
        context.FillRectangle(LightEdge, new Rect(r.X, r.Y + split, r.Width, 1));
        if (motifShare > 0)
        {
            // never wider than 46 % of the wall, so the name above and maker/count bottom left stay free
            var size = Math.Round(Math.Min(r.Height * motifShare, r.Width * 0.46));
            var box = new Rect(r.Right - motifInset - size, r.Bottom - size, size, size);
            MotifCatalog.Draw(context, look, box);
        }
        if (band > 0) context.FillRectangle(look.ColorBrush, new Rect(r.X, r.Y, r.Width, band));
    }
}
