using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using RetroHub.App.Input;

namespace RetroHub.App.Controls;

/// <summary>A focusable cover tile (template in Themes/Controls.axaml; DataContext = GameItemViewModel).</summary>
public class GameTile : Button
{
    /// <summary>Show title + meta below the cover.</summary>
    public static readonly StyledProperty<bool> ShowCaptionProperty = AvaloniaProperty.Register<GameTile, bool>(nameof(ShowCaption), true);

    public bool ShowCaption { get => GetValue(ShowCaptionProperty); set => SetValue(ShowCaptionProperty, value); }

    protected override Type StyleKeyOverride => typeof(GameTile);
}

/// <summary>A platform card button (template in Themes/Controls.axaml; DataContext = SystemItemViewModel).</summary>
public class SystemCard : Button
{
    protected override Type StyleKeyOverride => typeof(SystemCard);
}

/// <summary>Dashboard statistic: icon, big value, label.</summary>
public class StatTile : TemplatedControl
{
    public static readonly StyledProperty<string?> ValueProperty = AvaloniaProperty.Register<StatTile, string?>(nameof(Value));
    public static readonly StyledProperty<string?> LabelProperty = AvaloniaProperty.Register<StatTile, string?>(nameof(Label));
    public static readonly StyledProperty<Geometry?> IconProperty = AvaloniaProperty.Register<StatTile, Geometry?>(nameof(Icon));

    public string? Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public string? Label { get => GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public Geometry? Icon { get => GetValue(IconProperty); set => SetValue(IconProperty, value); }
}

/// <summary>
/// Controller button glyph for the hint bar: Xbox letters (A/B/X/Y in their colours) or PlayStation symbols
/// (cross/circle/square/triangle), pills for Menu/View/LB/RB.
/// </summary>
public sealed class ButtonGlyph : Control
{
    public static readonly StyledProperty<NavAction> ActionProperty = AvaloniaProperty.Register<ButtonGlyph, NavAction>(nameof(Action));
    public static readonly StyledProperty<GlyphStyle> GlyphsProperty = AvaloniaProperty.Register<ButtonGlyph, GlyphStyle>(nameof(Glyphs), GlyphStyle.Xbox);

    static ButtonGlyph()
    {
        AffectsRender<ButtonGlyph>(ActionProperty, GlyphsProperty);
        AffectsMeasure<ButtonGlyph>(ActionProperty, GlyphsProperty);
    }

    public NavAction Action { get => GetValue(ActionProperty); set => SetValue(ActionProperty, value); }
    public GlyphStyle Glyphs { get => GetValue(GlyphsProperty); set => SetValue(GlyphsProperty, value); }

    private bool IsPill => Action is NavAction.Menu or NavAction.View or NavAction.PageLeft or NavAction.PageRight;

    protected override Size MeasureOverride(Size availableSize) => IsPill ? new Size(Action is NavAction.PageLeft or NavAction.PageRight ? 30 : 28, 22) : new Size(22, 22);

    public override void Render(DrawingContext ctx)
    {
        var b = new Rect(Bounds.Size);
        var ps = Glyphs == GlyphStyle.PlayStation;
        var face = new SolidColorBrush(Color.FromArgb(0x1F, 255, 255, 255));
        var edge = new Pen(new SolidColorBrush(Color.FromArgb(0x33, 255, 255, 255)), 1);
        var family = GetValue(TextElement.FontFamilyProperty);
        if (IsPill)
        {
            ctx.DrawRectangle(face, edge, new RoundedRect(b.Deflate(0.5), 7));
            var label = Action switch
            {
                NavAction.PageLeft => ps ? "L1" : "LB",
                NavAction.PageRight => ps ? "R1" : "RB",
                _ => null,
            };
            if (label != null) DrawCentered(ctx, label, family, 10, Brushes.White, b);
            else DrawGeometry(ctx, Action == NavAction.Menu ? "Glyph.Menu" : "Glyph.View", Brushes.White, b, 1.6);
            return;
        }
        var color = Colorize(Action, ps);
        ctx.DrawEllipse(face, edge, b.Center, b.Width / 2 - 0.5, b.Height / 2 - 0.5);
        if (ps)
        {
            var key = Action switch
            {
                NavAction.Accept => "Glyph.PsCross",
                NavAction.Back => "Glyph.PsCircle",
                NavAction.Favorite => "Glyph.PsSquare",
                _ => "Glyph.PsTriangle",
            };
            DrawGeometry(ctx, key, new SolidColorBrush(color), b, 1.9);
        }
        else
        {
            var letter = Action switch { NavAction.Accept => "A", NavAction.Back => "B", NavAction.Favorite => "X", _ => "Y" };
            DrawCentered(ctx, letter, family, 11.5, new SolidColorBrush(color), b);
        }
    }

    private static Color Colorize(NavAction a, bool ps) => (a, ps) switch
    {
        (NavAction.Accept, false) => Color.Parse("#5BD07A"),
        (NavAction.Back, false) => Color.Parse("#FF6B6B"),
        (NavAction.Favorite, false) => Color.Parse("#5AA2FF"),
        (_, false) => Color.Parse("#FFD15C"),
        (NavAction.Accept, true) => Color.Parse("#8FB4FF"),
        (NavAction.Back, true) => Color.Parse("#FF7A8A"),
        (NavAction.Favorite, true) => Color.Parse("#F59BE3"),
        _ => Color.Parse("#4FE0BE"),
    };

    private static void DrawCentered(DrawingContext ctx, string text, FontFamily family, double size, IBrush brush, Rect b)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(family, FontStyle.Normal, FontWeight.Bold), size, brush);
        ctx.DrawText(ft, new Point(b.X + (b.Width - ft.Width) / 2, b.Y + (b.Height - ft.Height) / 2 + 0.5));
    }

    private void DrawGeometry(DrawingContext ctx, string key, IBrush brush, Rect b, double thickness)
    {
        if (!this.TryFindResource(key, out var res) || res is not Geometry g) return;
        var scale = Math.Min(b.Width, b.Height) / 24.0;
        using (ctx.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(b.X + (b.Width - 24 * scale) / 2, b.Y + (b.Height - 24 * scale) / 2)))
            ctx.DrawGeometry(null, new Pen(brush, thickness / scale * 0.9, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), g);
    }
}
