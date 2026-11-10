using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace RetroHub.App.Controls;

/// <summary>
/// The torn-off top of a paper slip (the Lieferschein, DESIGN 7.6.2): a row of teeth <see cref="ToothHeight"/> high and
/// twice as wide, cut out of the slip in the room's tone – the perforation the slip was torn from. One geometry per width.
/// </summary>
public sealed class Sawtooth : Control
{
    public static readonly StyledProperty<IBrush?> FillProperty = AvaloniaProperty.Register<Sawtooth, IBrush?>(nameof(Fill));
    /// <summary>8 px (QA 2: 6 px teeth on Regal against Raum were barely visible, the slip read as a grey panel).</summary>
    public const double ToothHeight = 8;

    private StreamGeometry? _geometry;
    private double _width = -1;

    static Sawtooth()
    {
        AffectsRender<Sawtooth>(FillProperty);
        HeightProperty.OverrideDefaultValue<Sawtooth>(ToothHeight);
        IsHitTestVisibleProperty.OverrideDefaultValue<Sawtooth>(false);
    }

    public IBrush? Fill { get => GetValue(FillProperty); set => SetValue(FillProperty, value); }

    public override void Render(DrawingContext ctx)
    {
        var w = Bounds.Width;
        if (w <= 0 || Fill == null) return;
        if (_geometry == null || Math.Abs(_width - w) > 0.5)
        {
            _width = w;
            _geometry = new StreamGeometry();
            using var g = _geometry.Open();
            var step = ToothHeight * 2;
            // the room's tone above the teeth: a strip with V-notches pointing down into the slip
            g.BeginFigure(new Point(0, 0), true);
            for (var x = 0.0; x < w; x += step)
            {
                g.LineTo(new Point(Math.Min(w, x + step / 2), ToothHeight));
                g.LineTo(new Point(Math.Min(w, x + step), 0));
            }
            g.EndFigure(true);
        }
        ctx.DrawGeometry(Fill, null, _geometry);
    }
}

/// <summary>
/// A dotted leader of the delivery note (DESIGN 7.6.2): 2 px dots with 4 px gaps in Kante from the word to its number,
/// on the text's baseline area (the bottom of the control).
/// </summary>
public sealed class DotLeader : Control
{
    private static readonly IImmutableSolidColorBrush Dot = new ImmutableSolidColorBrush(Color.Parse("#3A342B"));

    static DotLeader()
    {
        HeightProperty.OverrideDefaultValue<DotLeader>(2);
        IsHitTestVisibleProperty.OverrideDefaultValue<DotLeader>(false);
    }

    public override void Render(DrawingContext ctx)
    {
        var w = Bounds.Width;
        for (var x = 0.0; x + 2 <= w; x += 6) ctx.FillRectangle(Dot, new Rect(Math.Round(x), 0, 2, 2));
    }
}