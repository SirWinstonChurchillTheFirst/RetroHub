using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace RetroHub.App.Controls;

/// <summary>
/// Vector icon drawn from a <see cref="Geometry"/> authored on a 24×24 grid ("Gefaste Linie"; no icon fonts).
/// Scales to its size; colour follows the inherited Foreground (TextElement.Foreground) unless set.
/// </summary>
public sealed class RhIcon : Control
{
    public static readonly StyledProperty<Geometry?> DataProperty =
        AvaloniaProperty.Register<RhIcon, Geometry?>(nameof(Data));

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<RhIcon>();

    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<RhIcon, double>(nameof(StrokeThickness), 2.0);

    /// <summary>Fill the geometry instead of stroking it (play triangle, filled star).</summary>
    public static readonly StyledProperty<bool> FilledProperty =
        AvaloniaProperty.Register<RhIcon, bool>(nameof(Filled));

    static RhIcon()
    {
        AffectsRender<RhIcon>(DataProperty, ForegroundProperty, StrokeThicknessProperty, FilledProperty);
        WidthProperty.OverrideDefaultValue<RhIcon>(20);
        HeightProperty.OverrideDefaultValue<RhIcon>(20);
        IsHitTestVisibleProperty.OverrideDefaultValue<RhIcon>(false);
    }

    public Geometry? Data { get => GetValue(DataProperty); set => SetValue(DataProperty, value); }
    public IBrush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    public double StrokeThickness { get => GetValue(StrokeThicknessProperty); set => SetValue(StrokeThicknessProperty, value); }
    public bool Filled { get => GetValue(FilledProperty); set => SetValue(FilledProperty, value); }

    public override void Render(DrawingContext context)
    {
        var data = Data;
        var brush = Foreground;
        if (data == null || brush == null || Bounds.Width <= 0 || Bounds.Height <= 0) return;
        var scale = Math.Min(Bounds.Width, Bounds.Height) / 24.0;
        var dx = (Bounds.Width - 24 * scale) / 2;
        var dy = (Bounds.Height - 24 * scale) / 2;
        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(dx, dy)))
        {
            // "Gefaste Linie": square caps, mitre joins (DESIGN 5.1)
            if (Filled)
                context.DrawGeometry(brush, new Pen(brush, 1.0, lineJoin: PenLineJoin.Miter), data);
            else
                context.DrawGeometry(null, new Pen(brush, StrokeThickness, lineCap: PenLineCap.Square, lineJoin: PenLineJoin.Miter), data);
        }
    }
}
