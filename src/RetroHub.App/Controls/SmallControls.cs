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

    /// <summary>Show the favourite marker on the box (off on Start: no badges on shelves and stage, DESIGN 10.2).</summary>
    public static readonly StyledProperty<bool> ShowFavoriteProperty = AvaloniaProperty.Register<GameTile, bool>(nameof(ShowFavorite), true);

    public bool ShowFavorite { get => GetValue(ShowFavoriteProperty); set => SetValue(ShowFavoriteProperty, value); }

    /// <summary>
    /// Height/width of a spine placeholder on this tile (0 = the system's box format). The library sets it in a system
    /// compartment, where the slot format comes from the system's covers: placeholders then stand like their neighbours.
    /// </summary>
    public static readonly StyledProperty<double> PlaceholderFormatProperty = AvaloniaProperty.Register<GameTile, double>(nameof(PlaceholderFormat));

    public double PlaceholderFormat { get => GetValue(PlaceholderFormatProperty); set => SetValue(PlaceholderFormatProperty, value); }

    /// <summary>Show the sticker "Neu" when the game is new (library only; Start carries no badges).</summary>
    public static readonly StyledProperty<bool> ShowStickerProperty = AvaloniaProperty.Register<GameTile, bool>(nameof(ShowSticker));

    public bool ShowSticker { get => GetValue(ShowStickerProperty); set => SetValue(ShowStickerProperty, value); }

    /// <summary>
    /// Box format height / width of the slot (0 = off). With a fixed <see cref="Layoutable.Height"/> and no width the
    /// tile is as wide as the system's box (Start shelves, DESIGN 7.1: height 200, width from the format), so covers
    /// and spine placeholders of a board share one top line per format and nothing is shrunk into a portrait slot.
    /// </summary>
    public static readonly StyledProperty<double> SlotAspectProperty = AvaloniaProperty.Register<GameTile, double>(nameof(SlotAspect));

    public double SlotAspect { get => GetValue(SlotAspectProperty); set => SetValue(SlotAspectProperty, value); }

    /// <summary>
    /// With a fixed height: once the cover is loaded the tile takes the IMAGE's width (Start shelves, DESIGN 7.1:
    /// "Breite nach Bild"), so a portrait box in a square system slot leaves no gap. Until then the system's format.
    /// </summary>
    public static readonly StyledProperty<bool> FitToImageProperty = AvaloniaProperty.Register<GameTile, bool>(nameof(FitToImage));

    public bool FitToImage { get => GetValue(FitToImageProperty); set => SetValue(FitToImageProperty, value); }

    static GameTile() => AffectsMeasure<GameTile>(SlotAspectProperty, FitToImageProperty);

    protected override Size MeasureOverride(Size availableSize)
    {
        if (FitToImage && Art?.ImageAspect is { } ia && !double.IsNaN(Height) && double.IsNaN(Width))
        {
            var fit = new Size(Math.Round(Height * Math.Clamp(ia, 0.4, 2.5)), Height);
            base.MeasureOverride(fit);
            return fit;
        }
        if (SlotAspect > 0 && !double.IsNaN(Height) && double.IsNaN(Width))
        {
            var size = new Size(Math.Round(Height / SlotAspect), Height);
            base.MeasureOverride(size);
            return size;
        }
        return base.MeasureOverride(availableSize);
    }

    protected override Type StyleKeyOverride => typeof(GameTile);

    /// <summary>Tiles constructed so far (perf diagnostics: a scrolling grid recycles its tiles).</summary>
    internal static int Created;

    public GameTile() => Created++;

    /// <summary>The cover of this tile (null until the template is applied, or for templates without one).</summary>
    public CoverArt? Art { get; private set; }

    /// <summary>The generated spine beside the cover (only the detail stage's pulled box, DESIGN 7.3); else null.</summary>
    public BoxSpine? Spine { get; private set; }

    /// <summary>Raised when the cover's <see cref="CoverArt.ArtRect"/> changed (the row redraws its contact shadows).</summary>
    public event EventHandler? ArtChanged;

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        if (Art != null) Art.ArtRectChanged -= OnArtRectChanged;
        Art = e.NameScope.Find<CoverArt>("PART_Art");
        if (Art != null) Art.ArtRectChanged += OnArtRectChanged;
        Spine = e.NameScope.Find<BoxSpine>("PART_Spine");
        if (Spine != null) Spine.Art = Art;
        _liftTween?.Stop();
        _lifted = false;
        _liftNow = 1;
        _liftScale = null;
        if (e.NameScope.Find<Panel>("PART_CoverArea") is { } area)
        {
            area.RenderTransformOrigin = new RelativePoint(0.5, 1, RelativeUnit.Relative);
            area.RenderTransform = _liftScale = new ScaleTransform(1, 1);
        }
    }

    // ===== the focus lift (DESIGN 9.2 "Aufnehmen", R2 animation) =====
    // The box is picked up rather than scaled: 1.00 → 1.075 → 1.06 in 180 ms (CubicEaseOut to 60 %, then SineEaseInOut
    // settling back), origin bottom centre; the box it leaves drops back 1.06 → 1.00 in Step. Under a held direction the
    // lift jumps; reduced motion and off: no lift. Hover and press live one panel deeper (PART_Hover, theme styles), so
    // the two never fight over one transform.

    /// <summary>The settled lift of a focused box (= <see cref="FocusLayer.Lift"/>).</summary>
    public const double LiftScale = FocusLayer.Lift;
    /// <summary>Peak of the pick-up before it settles.</summary>
    public const double LiftPeak = 1.075;
    public static readonly TimeSpan LiftDuration = TimeSpan.FromMilliseconds(180);

    private ScaleTransform? _liftScale;
    private Tween? _liftTween;
    private double _liftNow = 1;
    private bool _lifted;

    /// <summary>The current scale of the cover area (tests).</summary>
    public double CurrentLift => _liftNow;

    protected override void OnGotFocus(Avalonia.Input.FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        PostLift();
    }

    protected override void OnLostFocus(Avalonia.Input.FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        PostLift();
    }

    // :focus-visible is set after GotFocus: decide once the input pass is done
    private void PostLift() => Avalonia.Threading.Dispatcher.UIThread.Post(UpdateLift, Avalonia.Threading.DispatcherPriority.Loaded);

    private void UpdateLift()
    {
        if (_liftScale == null) return;
        var want = IsKeyboardFocusWithin && Classes.Contains(":focus-visible") && Motion.AllowsMovement;
        if (want == _lifted) return;
        _lifted = want;
        _liftTween?.Stop();
        var target = want ? LiftScale : 1.0;
        if (NavRepeatTracker.Current?.IsActive == true || !Motion.AllowsMovement || !IsEffectivelyVisible)
        {
            SetLift(target);
            return;
        }
        var from = _liftNow;
        _liftTween = want
            ? Tween.Run(this, LiftDuration, p => SetLift(Settle(from, p)))
            : Tween.Run(this, Motion.Step, p => SetLift(Ease.Lerp(from, 1, Ease.CubicOut(p))));
    }

    /// <summary>The pick-up curve: up past the lift to <see cref="LiftPeak"/> at 60 %, then settling on it.</summary>
    public static double Settle(double from, double p) => p < 0.6
        ? Ease.Lerp(from, LiftPeak, Ease.CubicOut(p / 0.6))
        : Ease.Lerp(LiftPeak, LiftScale, Ease.SineInOut((p - 0.6) / 0.4));

    private void SetLift(double s)
    {
        _liftNow = s;
        if (_liftScale == null) return;
        _liftScale.ScaleX = s;
        _liftScale.ScaleY = s;
    }

    private void OnArtRectChanged(object? sender, EventArgs e)
    {
        if (FitToImage) InvalidateMeasure();
        ArtChanged?.Invoke(this, EventArgs.Empty);
    }

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

    private static readonly IImmutableSolidColorBrush Paper = new ImmutableSolidColorBrush(Color.Parse("#F4ECDF"));
    private static readonly IImmutableSolidColorBrush Ink = new ImmutableSolidColorBrush(Color.Parse("#14120F"));
    private static readonly IImmutableSolidColorBrush Shelf = new ImmutableSolidColorBrush(Color.Parse("#2A2620"));
    private static readonly IImmutableSolidColorBrush Text3 = new ImmutableSolidColorBrush(Color.Parse("#948A7A"));
    private static readonly IImmutableSolidColorBrush Text2 = new ImmutableSolidColorBrush(Color.Parse("#C9BFAF"));
    private static readonly IPen EdgePen = new ImmutablePen(new ImmutableSolidColorBrush(Color.Parse("#3A342B")), 1);

    /// <summary>
    /// Quiet plate (the ticker's shoulders, M3 QA): Regal hell with a 1 px edge and the wedge / key name in Text 2 –
    /// not a bright paper chip next to the ticker. <see cref="Hot"/> (hover, pressed) inverts it to paper and ink.
    /// </summary>
    public static readonly StyledProperty<bool> QuietProperty = AvaloniaProperty.Register<ButtonGlyph, bool>(nameof(Quiet));
    public static readonly StyledProperty<bool> HotProperty = AvaloniaProperty.Register<ButtonGlyph, bool>(nameof(Hot));

    private FormattedText? _key;

    static ButtonGlyph()
    {
        AffectsRender<ButtonGlyph>(ActionProperty, KeyboardProperty, SwapAcceptBackProperty, QuietProperty, HotProperty);
        AffectsMeasure<ButtonGlyph>(ActionProperty, KeyboardProperty, QuietProperty);
    }

    public bool Quiet { get => GetValue(QuietProperty); set => SetValue(QuietProperty, value); }
    public bool Hot { get => GetValue(HotProperty); set => SetValue(HotProperty, value); }

    public NavAction Action { get => GetValue(ActionProperty); set => SetValue(ActionProperty, value); }
    public bool Keyboard { get => GetValue(KeyboardProperty); set => SetValue(KeyboardProperty, value); }
    public bool SwapAcceptBack { get => GetValue(SwapAcceptBackProperty); set => SetValue(SwapAcceptBackProperty, value); }

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
        if (change.Property == ActionProperty || change.Property == KeyboardProperty || change.Property == QuietProperty || change.Property == HotProperty) _key = null;
    }

    private FormattedText KeyText => _key ??= new FormattedText(KeyName(Action), CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
        FontRoles.LabelFace, 15, Quiet && !Hot ? Text2 : Ink);

    private void DrawQuietPlate(DrawingContext ctx, Rect plate)
    {
        var geo = Chamfer.Octagon(plate.Deflate(0.5), 3.5);
        ctx.DrawGeometry(Hot ? Paper : Shelf, Hot ? null : EdgePen, geo);
    }

    private bool IsShoulder => Action is NavAction.PageLeft or NavAction.PageRight;
    private bool IsSmallPlate => Action is NavAction.Menu or NavAction.View;

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Keyboard) return new Size(Math.Max(28, Math.Ceiling(KeyText.Width) + 16), 28);
        if (IsShoulder && Quiet) return new Size(36, 28);
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
            if (Quiet) DrawQuietPlate(ctx, cap);
            else ctx.DrawGeometry(Paper, null, Chamfer.Octagon(cap, 4));
            var t = KeyText;
            ctx.DrawText(t, new Point(cap.X + (cap.Width - t.Width) / 2, cap.Y + (cap.Height - t.Height) / 2));
            return;
        }
        if (IsShoulder)
        {
            var plate = Quiet ? new Rect(0, (b.Height - 22) / 2, 36, 22) : new Rect(0, (b.Height - 24) / 2, 40, 24);
            if (Quiet) DrawQuietPlate(ctx, plate);
            else ctx.DrawGeometry(Paper, null, Chamfer.Octagon(plate, 4));
            // wedge ◤ (left) / ◥ (right), 10 px, ink (quiet: Text 2 at rest)
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                double x0 = plate.X + (plate.Width - 10) / 2, y0 = plate.Y + (plate.Height - 10) / 2;
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
            ctx.DrawGeometry(Quiet && !Hot ? Text2 : Ink, null, g);
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
