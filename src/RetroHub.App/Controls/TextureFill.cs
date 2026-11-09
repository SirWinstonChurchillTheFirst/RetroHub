using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using RetroHub.App.Theming;

namespace RetroHub.App.Controls;

/// <summary>
/// Fills its bounds with an era texture in whole DEVICE pixels (DESIGN 4.5), so the pattern has no moiré at
/// UiScale 0.8 or 1.33: the control undoes the total scale to the top level, snaps the origin to a device pixel and
/// draws the shared brush scaled by k = max(1, round(UiScale × RenderScaling)), nearest-neighbour.
/// </summary>
public sealed class TextureFill : Control
{
    public static readonly StyledProperty<Texture> TextureProperty =
        AvaloniaProperty.Register<TextureFill, Texture>(nameof(Texture));

    /// <summary>Pattern opacity (the PNGs carry their own alpha; DESIGN 4.5 lists 4–10 %).</summary>
    public static readonly StyledProperty<double> StrengthProperty =
        AvaloniaProperty.Register<TextureFill, double>(nameof(Strength), 0.07);

    static TextureFill()
    {
        AffectsRender<TextureFill>(TextureProperty, StrengthProperty);
        IsHitTestVisibleProperty.OverrideDefaultValue<TextureFill>(false);
    }

    public TextureFill()
    {
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.None);
    }

    public Texture Texture { get => GetValue(TextureProperty); set => SetValue(TextureProperty, value); }
    public double Strength { get => GetValue(StrengthProperty); set => SetValue(StrengthProperty, value); }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (TopLevel.GetTopLevel(this) is { } top) top.ScalingChanged += OnScalingChanged;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is { } top) top.ScalingChanged -= OnScalingChanged;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnScalingChanged(object? sender, EventArgs e) => InvalidateVisual();

    public override void Render(DrawingContext context) =>
        Draw(context, this, new Rect(Bounds.Size), Texture, Strength);

    /// <summary>Draws <paramref name="texture"/> into <paramref name="rect"/> of <paramref name="visual"/> in device pixels.</summary>
    public static void Draw(DrawingContext context, Visual visual, Rect rect, Texture texture, double strength)
    {
        if (rect.Width <= 0 || rect.Height <= 0 || strength <= 0) return;
        if (TextureBrushes.Get(texture) is not { } tex) return;
        var top = TopLevel.GetTopLevel(visual);
        var rs = top?.RenderScaling ?? 1.0;
        var m = top != null ? visual.TransformToVisual(top) ?? Matrix.Identity : Matrix.Identity;
        var ui = Math.Max(0.01, Math.Sqrt(m.M11 * m.M11 + m.M12 * m.M12));
        var total = ui * rs;                         // DIP of this control → device pixels
        var k = Math.Max(1, Math.Round(ui * rs));    // texture pixel → device pixels
        // device position of the rect's origin, snapped to whole device pixels
        var ox = (m.M31 + rect.X * ui) * rs;
        var oy = (m.M32 + rect.Y * ui) * rs;
        var dx = Math.Round(ox) - ox;
        var dy = Math.Round(oy) - oy;
        using (context.PushClip(rect))
        using (context.PushOpacity(strength))
        using (context.PushTransform(Matrix.CreateScale(1 / total, 1 / total) * Matrix.CreateTranslation(rect.X, rect.Y)))
        using (context.PushTransform(Matrix.CreateScale(k, k) * Matrix.CreateTranslation(dx, dy)))
        {
            var w = Math.Ceiling(rect.Width * total / k) + 1;
            var h = Math.Ceiling(rect.Height * total / k) + 1;
            context.FillRectangle(tex.Brush, new Rect(0, 0, w, h));
        }
    }
}
