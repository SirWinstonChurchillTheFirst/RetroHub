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
            // the room's tone above the teeth: a strip with notches pointing down into the slip, torn by hand (M7: the
            // regular zigzag read as a CSS trick) – pitch 12–20 px and depth 4–9 px per tooth, seeded by the width
            var seed = Craft.Seed("tear" + (int)w);
            g.BeginFigure(new Point(0, 0), true);
            var x = 0.0;
            for (var i = 0; x < w; i++)
            {
                var step = Craft.Range(seed, 3 * i, 12, 20);
                var depth = Craft.Range(seed, 3 * i + 1, 4, ToothHeight + 1);
                var peak = Craft.Range(seed, 3 * i + 2, 0.35, 0.65);
                g.LineTo(new Point(Math.Min(w, x + step * peak), depth));
                g.LineTo(new Point(Math.Min(w, x + step), Craft.Range(seed, 3 * i + 7, 0, 1.5)));
                x += step;
            }
            g.LineTo(new Point(w, 0));
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
        // printed dots (thermal print): each a little lighter or darker
        var i = 0;
        for (var x = 0.0; x + 2 <= w; x += 6, i++)
            using (ctx.PushOpacity(0.65 + 0.35 * Craft.Rand(77u, i)))
                ctx.FillRectangle(Dot, new Rect(Math.Round(x), 0, 2, 2));
    }
}