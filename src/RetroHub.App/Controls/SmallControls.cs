using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.VisualTree;
using RetroHub.App.Input;
using RetroHub.App.Theming;

namespace RetroHub.App.Controls;

/// <summary>
/// A focusable game box (template in Themes/Controls.axaml; DataContext = GameItemViewModel). The template names its
/// cover <c>PART_Art</c>; <see cref="Art"/> exposes it to the shelf board (contact shadows) and the
/// <see cref="FocusLayer"/> (ring, cone).
/// </summary>
public class GameTile : Button
{
    /// <summary>Show title + label line below the board.</summary>
    public static readonly StyledProperty<bool> ShowCaptionProperty = AvaloniaProperty.Register<GameTile, bool>(nameof(ShowCaption), true);

    public bool ShowCaption { get => GetValue(ShowCaptionProperty); set => SetValue(ShowCaptionProperty, value); }

    /// <summary>
    /// Box format height / width of the slot (0 = off). With a fixed <see cref="Layoutable.Height"/> and no width the
    /// tile is as wide as the system's box (Start shelves, DESIGN 7.1: height 200, width from the format), so covers
    /// and spine placeholders of a board share one top line per format and nothing is shrunk into a portrait slot.
    /// </summary>
    public static readonly StyledProperty<double> SlotAspectProperty = AvaloniaProperty.Register<GameTile, double>(nameof(SlotAspect));

    public double SlotAspect { get => GetValue(SlotAspectProperty); set => SetValue(SlotAspectProperty, value); }

    static GameTile() => AffectsMeasure<GameTile>(SlotAspectProperty);

    protected override Size MeasureOverride(Size availableSize)
    {
        if (SlotAspect > 0 && !double.IsNaN(Height) && double.IsNaN(Width))
        {
            var size = new Size(Math.Round(Height / SlotAspect), Height);
            base.MeasureOverride(size);
            return size;
        }
        return base.MeasureOverride(availableSize);
    }

    protected override Type StyleKeyOverride => typeof(GameTile);

    /// <summary>The cover of this tile (null until the template is applied, or for templates without one).</summary>
    public CoverArt? Art { get; private set; }

    /// <summary>Raised when the cover's <see cref="CoverArt.ArtRect"/> changed (the row redraws its contact shadows).</summary>
    public event EventHandler? ArtChanged;

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        if (Art != null) Art.ArtRectChanged -= OnArtRectChanged;
        Art = e.NameScope.Find<CoverArt>("PART_Art");
        if (Art != null) Art.ArtRectChanged += OnArtRectChanged;
    }

    private void OnArtRectChanged(object? sender, EventArgs e) => ArtChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// The cover's covered rectangle in this tile's coordinates, from layout bounds (ignores the lift transform);
    /// null without a cover.
    /// </summary>
    public Rect? ArtRectInTile()
    {
        if (Art is not { } art) return null;
        var r = art.ArtRect;
        double x = r.X, y = r.Y;
        for (Visual? v = art; v != null && v != this; v = v.GetVisualParent())
        {
            x += v.Bounds.X;
            y += v.Bounds.Y;
        }
        return new Rect(x, y, r.Width, r.Height);
    }

    /// <summary>Bottom of the cover slot in tile coordinates (= top edge of the board under the box).</summary>
    public double? CoverBottomInTile()
    {
        if (Art is not { } art) return null;
        double y = art.Bounds.Height;
        for (Visual? v = art; v != null && v != this; v = v.GetVisualParent()) y += v.Bounds.Y;
        return y;
    }
}

/// <summary>A system wall button (template in Themes/Controls.axaml; DataContext = SystemItemViewModel).</summary>
public class SystemCard : Button
{
    protected override Type StyleKeyOverride => typeof(SystemCard);
}

/// <summary>Legacy dashboard statistic (removed from the pages; deleted in M6).</summary>
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
/// Controller/keyboard glyph of the footer (DESIGN 5.3), brand-free and monochrome. Pad: the face buttons are a
/// diamond of four dots with the meant POSITION filled (south = confirm, east = back, west = favourite, north = search;
/// <see cref="SwapAcceptBack"/> swaps south/east), shoulders are paper plates with a wedge, menu/view small plates.
/// Keyboard (last input was the keyboard): a paper key cap with the key name. The meaning is always the text next to it.
/// </summary>
public sealed class ButtonGlyph : Control
{
    public static readonly StyledProperty<NavAction> ActionProperty = AvaloniaProperty.Register<ButtonGlyph, NavAction>(nameof(Action));
    /// <summary>Show keyboard key caps instead of pad glyphs.</summary>
    public static readonly StyledProperty<bool> KeyboardProperty = AvaloniaProperty.Register<ButtonGlyph, bool>(nameof(Keyboard));
    public static readonly StyledProperty<bool> SwapAcceptBackProperty = AvaloniaProperty.Register<ButtonGlyph, bool>(nameof(SwapAcceptBack));
    /// <summary>Legacy (v0 brand glyphs); ignored – the glyphs are position-based now.</summary>
    public static readonly StyledProperty<GlyphStyle> GlyphsProperty = AvaloniaProperty.Register<ButtonGlyph, GlyphStyle>(nameof(Glyphs), GlyphStyle.Auto);

    private static readonly IImmutableSolidColorBrush Paper = new ImmutableSolidColorBrush(Color.Parse("#F4ECDF"));
    private static readonly IImmutableSolidColorBrush Ink = new ImmutableSolidColorBrush(Color.Parse("#14120F"));
    private static readonly IImmutableSolidColorBrush Shelf = new ImmutableSolidColorBrush(Color.Parse("#2A2620"));
    private static readonly IImmutableSolidColorBrush Text3 = new ImmutableSolidColorBrush(Color.Parse("#948A7A"));

    private FormattedText? _key;

    static ButtonGlyph()
    {
        AffectsRender<ButtonGlyph>(ActionProperty, KeyboardProperty, SwapAcceptBackProperty);
        AffectsMeasure<ButtonGlyph>(ActionProperty, KeyboardProperty);
    }

    public NavAction Action { get => GetValue(ActionProperty); set => SetValue(ActionProperty, value); }
    public bool Keyboard { get => GetValue(KeyboardProperty); set => SetValue(KeyboardProperty, value); }
    public bool SwapAcceptBack { get => GetValue(SwapAcceptBackProperty); set => SetValue(SwapAcceptBackProperty, value); }
    public GlyphStyle Glyphs { get => GetValue(GlyphsProperty); set => SetValue(GlyphsProperty, value); }

    /// <summary>Key name for the keyboard caps (matches <see cref="KeyboardMap"/>).</summary>
    public static string KeyName(NavAction a) => a switch
    {
        NavAction.Accept => "Enter",
        NavAction.Back => "Esc",
        NavAction.Favorite => "F",
        NavAction.Search => "F3",
        NavAction.Menu => "F10",
        NavAction.View => "V",
        NavAction.PageLeft => "Strg+Bild auf",
        NavAction.PageRight => "Strg+Bild ab",
        NavAction.Up => "↑",
        NavAction.Down => "↓",
        NavAction.Left => "←",
        _ => "→",
    };

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ActionProperty || change.Property == KeyboardProperty) _key = null;
    }

    private FormattedText KeyText => _key ??= new FormattedText(KeyName(Action), CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
        FontRoles.LabelFace, 15, Ink);

    private bool IsShoulder => Action is NavAction.PageLeft or NavAction.PageRight;
    private bool IsSmallPlate => Action is NavAction.Menu or NavAction.View;

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Keyboard) return new Size(Math.Max(28, Math.Ceiling(KeyText.Width) + 16), 28);
        if (IsShoulder) return new Size(40, 28);
        if (IsSmallPlate) return new Size(28, 28);
        return new Size(28, 28);
    }

    public override void Render(DrawingContext ctx)
    {
        var b = new Rect(Bounds.Size);
        if (b.Width <= 0) return;
        if (Keyboard)
        {
            var cap = new Rect(0, (b.Height - 24) / 2, b.Width, 24);
            ctx.DrawGeometry(Paper, null, Chamfer.Octagon(cap, 4));
            var t = KeyText;
            ctx.DrawText(t, new Point(cap.X + (cap.Width - t.Width) / 2, cap.Y + (cap.Height - t.Height) / 2));
            return;
        }
        if (IsShoulder)
        {
            var plate = new Rect(0, (b.Height - 24) / 2, 40, 24);
            ctx.DrawGeometry(Paper, null, Chamfer.Octagon(plate, 4));
            // wedge ◤ (left) / ◥ (right), 10 px, ink
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                double x0 = plate.X + 15, y0 = plate.Y + 7;
                if (Action == NavAction.PageLeft)
                {
                    c.BeginFigure(new Point(x0, y0), true);
                    c.LineTo(new Point(x0 + 10, y0));
                    c.LineTo(new Point(x0, y0 + 10));
                }
                else
                {
                    c.BeginFigure(new Point(x0, y0), true);
                    c.LineTo(new Point(x0 + 10, y0));
                    c.LineTo(new Point(x0 + 10, y0 + 10));
                }
                c.EndFigure(true);
            }
            ctx.DrawGeometry(Ink, null, g);
            return;
        }
        if (IsSmallPlate)
        {
            var plate = new Rect(0, (b.Height - 20) / 2, 28, 20);
            ctx.DrawGeometry(Shelf, null, Chamfer.Octagon(plate, 4));
            var pen = new ImmutablePen(Paper, 1.5);
            if (Action == NavAction.Menu)
            {
                for (var i = 0; i < 3; i++)
                {
                    var y = plate.Y + 6 + i * 4 + 0.75;
                    ctx.DrawLine(pen, new Point(plate.X + 8, y), new Point(plate.Right - 8, y));
                }
            }
            else
            {
                ctx.DrawRectangle(null, pen, new Rect(plate.X + 7.75, plate.Y + 4.75, 7, 7));
                ctx.DrawRectangle(null, pen, new Rect(plate.X + 12.75, plate.Y + 8.75, 7, 7));
            }
            return;
        }
        // face diamond: Ø28 disc, four Ø6 dots 7 px from the centre; the meant position filled
        var center = new Point(b.Width / 2, b.Height / 2);
        ctx.DrawEllipse(Shelf, null, center, 14, 14);
        var meant = Position(Action, SwapAcceptBack);
        var ring = new ImmutablePen(Text3, 1.5);
        for (var p = 0; p < 4; p++)
        {
            var (dx, dy) = p switch { 0 => (0, 7), 1 => (7, 0), 2 => (-7, 0), _ => (0, -7) }; // S, E, W, N
            var at = new Point(center.X + dx, center.Y + dy);
            if (p == meant) ctx.DrawEllipse(Paper, null, at, 3.4, 3.4);
            else ctx.DrawEllipse(null, ring, at, 2.25, 2.25);
        }
    }

    /// <summary>0 = south, 1 = east, 2 = west, 3 = north.</summary>
    public static int Position(NavAction action, bool swapAcceptBack) => action switch
    {
        NavAction.Accept => swapAcceptBack ? 1 : 0,
        NavAction.Back => swapAcceptBack ? 0 : 1,
        NavAction.Favorite => 2,
        _ => 3,
    };
}
