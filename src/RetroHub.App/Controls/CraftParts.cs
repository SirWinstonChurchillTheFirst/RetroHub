using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using RetroHub.App.Theming;

namespace RetroHub.App.Controls;

/// <summary>
/// An engraved groove (DESIGN 4.6): 1 px dark with 1 px light directly below – a line cut into the material of the
/// workbench instead of a hairline drawn on it. Replaces the 1 px "Kante" separators of the rows. 2 px high.
/// </summary>
public sealed class Groove : Control
{
    static Groove()
    {
        HeightProperty.OverrideDefaultValue<Groove>(2);
        IsHitTestVisibleProperty.OverrideDefaultValue<Groove>(false);
    }

    public override void Render(DrawingContext ctx)
    {
        var w = Bounds.Width;
        if (w <= 0) return;
        Craft.Groove(ctx, 0, 0, w, 0.50, 0.05);
    }
}

/// <summary>
/// The glass of a display window (the focused value of a service row, DESIGN 7.6): a 3 px inner shadow at the top, a
/// 2 px one at the left, 1 px light at the bottom edge, and a faint diagonal reflection – the window is set INTO the
/// workbench. Drawn over the window's ground, under its text.
/// </summary>
public sealed class WindowGlass : Control
{
    private static readonly IBrush Reflection = new ImmutableLinearGradientBrush(
        [
            new ImmutableGradientStop(0, Craft.A(Craft.Light, 0)),
            new ImmutableGradientStop(0.42, Craft.A(Craft.Light, 0)),
            new ImmutableGradientStop(0.48, Craft.A(Craft.Light, 0.035)),
            new ImmutableGradientStop(0.62, Craft.A(Craft.Light, 0.02)),
            new ImmutableGradientStop(0.64, Craft.A(Craft.Light, 0)),
        ],
        startPoint: new RelativePoint(0, 0, RelativeUnit.Relative), endPoint: new RelativePoint(1, 1, RelativeUnit.Relative));

    static WindowGlass()
    {
        IsHitTestVisibleProperty.OverrideDefaultValue<WindowGlass>(false);
    }

    public override void Render(DrawingContext ctx)
    {
        var r = new Rect(Bounds.Size);
        if (r.Width <= 0 || r.Height <= 0) return;
        Craft.Recess(ctx, r, 4, 0.60, 0.07);
        ctx.FillRectangle(Reflection, r);
    }
}

/// <summary>
/// A small key cap under an arrow (the ◀ ▶ of a service row): a chamfered plate with a 2 px darker depth edge at its
/// foot and a lit top edge; <see cref="Pressed"/> moves the face down onto its edge (1 px left).
/// </summary>
public sealed class KeyCap : Control
{
    public static readonly StyledProperty<bool> PressedProperty = AvaloniaProperty.Register<KeyCap, bool>(nameof(Pressed));
    private static readonly Color Face = Color.Parse("#36312A");

    static KeyCap()
    {
        AffectsRender<KeyCap>(PressedProperty);
        IsHitTestVisibleProperty.OverrideDefaultValue<KeyCap>(false);
    }

    public bool Pressed { get => GetValue(PressedProperty); set => SetValue(PressedProperty, value); }

    public override void Render(DrawingContext ctx)
    {
        var r = new Rect(Bounds.Size);
        if (r.Width <= 4 || r.Height <= 4) return;
        const double depth = 3;
        var d = Pressed ? 1 : depth;
        ctx.DrawGeometry(Craft.Solid(ColorUtil.Mix(Face, Colors.Black, 0.45)), null, Chamfer.Octagon(new Rect(0, depth - d + 1, r.Width, r.Height - depth + d - 1), 3));
        var face = new Rect(0, depth - d, r.Width, r.Height - depth);
        var outline = Chamfer.Octagon(face, 3);
        ctx.DrawGeometry(Craft.Solid(Face), null, outline);
        using (ctx.PushGeometryClip(outline))
        {
            ctx.FillRectangle(Craft.Hi(0.14), new Rect(face.X, face.Y, face.Width, 1));
            ctx.FillRectangle(Craft.Fade(Colors.Black, 0, 0.25, Craft.Toward.Down), new Rect(face.X, face.Bottom - 6, face.Width, 6));
        }
    }
}

/// <summary>
/// An indicator lamp (DESIGN 7.6: the "Bereit" segments, the scan's segment bar): a chamfered lens in its colour with
/// a glint of glass at its top left, a darker foot and a 1 px dark rim – a lamp set into the panel, not a flat bar.
/// </summary>
public sealed class Lamp : Control
{
    public static readonly StyledProperty<IBrush?> FillProperty = AvaloniaProperty.Register<Lamp, IBrush?>(nameof(Fill));

    static Lamp()
    {
        AffectsRender<Lamp>(FillProperty);
        IsHitTestVisibleProperty.OverrideDefaultValue<Lamp>(false);
    }

    public IBrush? Fill { get => GetValue(FillProperty); set => SetValue(FillProperty, value); }

    public override void Render(DrawingContext ctx) => Draw(ctx, new Rect(Bounds.Size), Fill);

    /// <summary>Draws one lamp into <paramref name="r"/>.</summary>
    public static void Draw(DrawingContext ctx, Rect r, IBrush? fill)
    {
        if (r.Width <= 2 || r.Height <= 2 || fill == null) return;
        var outline = Chamfer.Octagon(r, 2);
        ctx.DrawGeometry(Craft.Dark(0.55), null, Chamfer.Octagon(r.Inflate(1), 2.5));
        ctx.DrawGeometry(fill, null, outline);
        using (ctx.PushGeometryClip(outline))
        {
            ctx.FillRectangle(Craft.Fade(Colors.Black, 0, 0.30, Craft.Toward.Down), new Rect(r.X, r.Y + r.Height * 0.45, r.Width, r.Height * 0.55));
            ctx.FillRectangle(Craft.Hi(0.22), new Rect(r.X + 2, r.Y + 1, Math.Max(1, r.Width * 0.4), 1));
            ctx.FillRectangle(Craft.Hi(0.10), new Rect(r.X + 2, r.Y + 2, 2, 1));
        }
    }
}

/// <summary>
/// The paper of a slip (the Lieferschein, DESIGN 7.6.2): fibres, a slight curl at its right edge and the curl's shadow
/// under its bottom edge (drawn below the bounds). Lies under the slip's text.
/// </summary>
public sealed class PaperSlip : Control
{
    static PaperSlip()
    {
        IsHitTestVisibleProperty.OverrideDefaultValue<PaperSlip>(false);
        ClipToBoundsProperty.OverrideDefaultValue<PaperSlip>(false);
    }

    public override void Render(DrawingContext ctx)
    {
        var r = new Rect(Bounds.Size);
        if (r.Width <= 0 || r.Height <= 0) return;
        TextureFill.Draw(ctx, this, r, Texture.Card, 0.35);
        ctx.FillRectangle(Craft.Fade(Colors.Black, 0, 0.20, Craft.Toward.Right), new Rect(r.Right - 12, r.Y, 12, r.Height));
        ctx.FillRectangle(Craft.Hi(0.05), new Rect(r.X, r.Y, 1, r.Height));
        ctx.FillRectangle(Craft.Fade(Colors.Black, 0.45, 0, Craft.Toward.Down), new Rect(r.X + 6, r.Bottom, r.Width - 4, 10));
    }
}

/// <summary>
/// A note of cut paper (R2): an irregular, seeded outline with its thickness, a soft shadow, card fibres and one or
/// two strips of tape at the top; the content (ink text, keys) sits on it. Used where a page explains itself on the
/// shelf ("Weitere Spiele kommen über Importe ins Fach.") instead of floating a sentence in the room. Tilt it with a
/// RenderTransform; the paper itself is drawn upright so content and paper turn together.
/// </summary>
public sealed class PaperNote : Decorator
{
    public static readonly StyledProperty<string?> SeedTextProperty = AvaloniaProperty.Register<PaperNote, string?>(nameof(SeedText));
    public static readonly StyledProperty<Color> PaperProperty = AvaloniaProperty.Register<PaperNote, Color>(nameof(Paper), Craft.LabelPaper);
    public static readonly StyledProperty<bool> TapeProperty = AvaloniaProperty.Register<PaperNote, bool>(nameof(Tape), true);
    /// <summary>An index card: ruled lines every <see cref="RuleStep"/> from <see cref="RuleTop"/> on, a red head line at it.</summary>
    public static readonly StyledProperty<double> RuleStepProperty = AvaloniaProperty.Register<PaperNote, double>(nameof(RuleStep));
    public static readonly StyledProperty<double> RuleTopProperty = AvaloniaProperty.Register<PaperNote, double>(nameof(RuleTop), 56);

    static PaperNote()
    {
        AffectsRender<PaperNote>(SeedTextProperty, PaperProperty, TapeProperty, RuleStepProperty, RuleTopProperty);
        ClipToBoundsProperty.OverrideDefaultValue<PaperNote>(false);
    }

    public double RuleStep { get => GetValue(RuleStepProperty); set => SetValue(RuleStepProperty, value); }
    public double RuleTop { get => GetValue(RuleTopProperty); set => SetValue(RuleTopProperty, value); }

    private static readonly IImmutableSolidColorBrush RuleBrush = new ImmutableSolidColorBrush(Color.FromArgb(0x48, 0x6E, 0x86, 0xA3));
    private static readonly IImmutableSolidColorBrush HeadRuleBrush = new ImmutableSolidColorBrush(Color.FromArgb(0x90, 0xB8, 0x4A, 0x3C));

    /// <summary>What seeds the outline and the tape (the same note always looks the same).</summary>
    public string? SeedText { get => GetValue(SeedTextProperty); set => SetValue(SeedTextProperty, value); }
    public Color Paper { get => GetValue(PaperProperty); set => SetValue(PaperProperty, value); }
    public bool Tape { get => GetValue(TapeProperty); set => SetValue(TapeProperty, value); }

    private static readonly IImmutableSolidColorBrush TapeBrush = new ImmutableSolidColorBrush(Craft.A(Color.Parse("#F2EAD8"), 0.42));

    public override void Render(DrawingContext ctx)
    {
        var r = new Rect(Bounds.Size);
        if (r.Width <= 0 || r.Height <= 0) return;
        var seed = Craft.Seed(SeedText ?? Name ?? "note");
        Craft.DrawPaper(ctx, this, r, seed, Paper, 0, jitter: 0.9, shadow: 0.45, fibres: 0.40);
        if (RuleStep > 4)
        {
            ctx.FillRectangle(HeadRuleBrush, new Rect(r.X + 3, Math.Round(r.Y + RuleTop), r.Width - 6, 1.5));
            for (var y = r.Y + RuleTop + RuleStep; y < r.Bottom - 8; y += RuleStep)
                ctx.FillRectangle(RuleBrush, new Rect(r.X + 3, Math.Round(y), r.Width - 6, 1));
        }
        if (!Tape) return;
        var tw = Math.Clamp(Math.Round(r.Width * 0.12), 22, 40);
        var th = Math.Round(tw * 0.36);
        var corners = Craft.Pick(seed, 51, 1, 2);
        var first = Craft.Pick(seed, 52, 0, 1);
        for (var i = 0; i < corners; i++)
        {
            var right = (first + i) % 2 == 1;
            var cx = right ? r.Right - tw * 0.3 : r.X + tw * 0.3;
            var angle = (right ? 1 : -1) * Craft.Range(seed, 53 + i, 7, 12) * Math.PI / 180;
            using (ctx.PushTransform(Matrix.CreateTranslation(-tw / 2, -th / 2) * Matrix.CreateRotation(angle) * Matrix.CreateTranslation(cx, r.Y + 1)))
            {
                ctx.FillRectangle(TapeBrush, new Rect(0, 0, tw, th));
                ctx.FillRectangle(Craft.Hi(0.10), new Rect(0, 0, tw, 1));
            }
        }
    }
}

/// <summary>
/// The value plate of the focused service row (R2, replaces the black 320 × 40 display box that read as a stock spin
/// box): a split-flap card set into the bench – an upper and a lower leaf in warm grey (the upper one lit), the gap
/// between them, a hinge pin at each side in the light colour, a recessed frame and the card's shadow on the bench.
/// The text lies on it; <see cref="FlapLeaf"/> above the text draws the split across the figures and the flip.
/// </summary>
public sealed class FlapPlate : Control
{
    public static readonly StyledProperty<IBrush?> PinProperty = AvaloniaProperty.Register<FlapPlate, IBrush?>(nameof(Pin));

    internal static readonly Color Upper = Color.Parse("#302B25");
    internal static readonly Color Lower = Color.Parse("#28241E");

    static FlapPlate()
    {
        AffectsRender<FlapPlate>(PinProperty);
        IsHitTestVisibleProperty.OverrideDefaultValue<FlapPlate>(false);
    }

    public IBrush? Pin { get => GetValue(PinProperty); set => SetValue(PinProperty, value); }

    public override void Render(DrawingContext ctx)
    {
        var r = new Rect(Bounds.Size);
        if (r.Width < 20 || r.Height < 12) return;
        // the slot in the bench the card stands in: a dark recess 2 px around it
        Craft.Recess(ctx, r, 3, 0.55, 0.06);
        var card = r.Deflate(new Thickness(3, 2, 3, 2));
        var split = Math.Round(card.Y + card.Height / 2);
        var outline = new RoundedRect(card, 3);
        using (ctx.PushClip(outline))
        {
            ctx.FillRectangle(Craft.Solid(Upper), new Rect(card.X, card.Y, card.Width, split - card.Y));
            ctx.FillRectangle(Craft.Solid(Lower), new Rect(card.X, split, card.Width, card.Bottom - split));
            // light from above on the upper leaf, the lower one a shade under it
            ctx.FillRectangle(Craft.Fade(Craft.Light, 0.07, 0, Craft.Toward.Down), new Rect(card.X, card.Y, card.Width, split - card.Y));
            ctx.FillRectangle(Craft.Fade(Colors.Black, 0, 0.22, Craft.Toward.Down), new Rect(card.X, split, card.Width, card.Bottom - split));
            ctx.FillRectangle(Craft.Hi(0.12), new Rect(card.X, card.Y, card.Width, 1));
        }
        // hinge pins in the light colour, half in the gap
        var pin = Pin ?? Craft.Hi(0.3);
        ctx.FillRectangle(pin, new Rect(card.X - 2, split - 2, 4, 4));
        ctx.FillRectangle(pin, new Rect(card.Right - 2, split - 2, 4, 4));
    }
}

/// <summary>
/// Above the value text of a <see cref="FlapPlate"/>: the gap between the two leaves running across the figures (as on a
/// real split-flap card), and on <see cref="Flip"/> one leaf falling – the upper half folds down to the hinge, then the
/// lower half lands (180 ms, shaded as it turns). Only when motion is full; otherwise the value just changes.
/// </summary>
public sealed class FlapLeaf : Control
{
    private DateTime _started;
    private double _t = 1;

    /// <summary>Duration of one flip.</summary>
    public static readonly TimeSpan FlipDuration = TimeSpan.FromMilliseconds(180);

    static FlapLeaf()
    {
        IsHitTestVisibleProperty.OverrideDefaultValue<FlapLeaf>(false);
    }

    /// <summary>True while a flip is drawn (tests).</summary>
    public bool IsFlipping => _t < 1;

    public void Flip()
    {
        if (!Theming.Motion.AllowsMovement || !IsEffectivelyVisible || TopLevel.GetTopLevel(this) is not { } top) return;
        var running = _t < 1;
        _started = DateTime.UtcNow;
        _t = 0;
        InvalidateVisual();
        if (!running) top.RequestAnimationFrame(Frame);
    }

    /// <summary>Driven by the render loop (frames, not a timer): the flip never hangs half-way when frames stop.</summary>
    private void Frame(TimeSpan _)
    {
        _t = Math.Min(1, (DateTime.UtcNow - _started).TotalMilliseconds / FlipDuration.TotalMilliseconds);
        InvalidateVisual();
        if (_t < 1 && TopLevel.GetTopLevel(this) is { } top) top.RequestAnimationFrame(Frame);
        else _t = 1;
    }

    public override void Render(DrawingContext ctx)
    {
        var r = new Rect(Bounds.Size);
        if (r.Width < 20 || r.Height < 12) return;
        var card = r.Deflate(new Thickness(3, 2, 3, 2));
        var split = Math.Round(card.Y + card.Height / 2);
        if (_t < 1)
        {
            var half = split - card.Y;
            if (_t < 0.5)
            {
                // the upper leaf folds toward the viewer and down: its projection shrinks to the hinge, darkening
                var k = _t / 0.5;
                var h = Math.Max(1, Math.Round(half * (1 - k)));
                var leaf = new Rect(card.X, split - h, card.Width, h);
                ctx.FillRectangle(Craft.Solid(FlapPlate.Upper), leaf);
                ctx.FillRectangle(Craft.Dark(0.10 + 0.35 * k), leaf);
                ctx.FillRectangle(Craft.Hi(0.10), new Rect(leaf.X, leaf.Y, leaf.Width, 1));
            }
            else
            {
                // and lands below it, lighting up as it lies flat again
                var k = (_t - 0.5) / 0.5;
                var h = Math.Max(1, Math.Round((card.Bottom - split) * k));
                var leaf = new Rect(card.X, split, card.Width, h);
                ctx.FillRectangle(Craft.Solid(FlapPlate.Lower), leaf);
                ctx.FillRectangle(Craft.Dark(0.45 * (1 - k)), leaf);
                ctx.FillRectangle(Craft.Dark(0.5), new Rect(leaf.X, leaf.Bottom - 1, leaf.Width, 1));
            }
        }
        // the gap between the leaves, across the figures: dark, with the lower leaf's lit lip under it
        ctx.FillRectangle(Craft.Solid(Craft.Deep), new Rect(card.X, split - 1, card.Width, 1));
        ctx.FillRectangle(Craft.Hi(0.06), new Rect(card.X, split, card.Width, 1));
    }
}

/// <summary>
/// A display niche in the bench (R2, Emulatoren's side column stood empty under the Gerätefach): a compartment cut into
/// the bench showing the focused system – its wall with the colour band and era texture, its motif standing on a small
/// board, the compartment's inner shadow and lip. A new system slides its motif up 14 px and fades it in (Glide; no
/// movement when motion is reduced, nothing animated when it is off).
/// </summary>
public sealed class SystemNiche : Control
{
    public static readonly StyledProperty<SystemLook?> LookProperty = AvaloniaProperty.Register<SystemNiche, SystemLook?>(nameof(Look));

    private DateTime _changed = DateTime.MinValue;
    private double _t = 1;

    static SystemNiche()
    {
        AffectsRender<SystemNiche>(LookProperty);
        IsHitTestVisibleProperty.OverrideDefaultValue<SystemNiche>(false);
    }

    public SystemLook? Look { get => GetValue(LookProperty); set => SetValue(LookProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != LookProperty || change.OldValue == null || ReferenceEquals(change.OldValue, change.NewValue)) return;
        if (Theming.Motion.Glide == TimeSpan.Zero || TopLevel.GetTopLevel(this) is not { } top) { _t = 1; return; }
        var running = _t < 1;
        _changed = DateTime.UtcNow;
        _t = 0;
        if (!running) top.RequestAnimationFrame(Frame);
    }

    private void Frame(TimeSpan _)
    {
        _t = Math.Min(1, (DateTime.UtcNow - _changed).TotalMilliseconds / Theming.Motion.GlideDuration.TotalMilliseconds);
        InvalidateVisual();
        if (_t < 1 && TopLevel.GetTopLevel(this) is { } top) top.RequestAnimationFrame(Frame);
        else _t = 1;
    }

    public override void Render(DrawingContext ctx)
    {
        var r = new Rect(Bounds.Size);
        if (r.Width < 40 || r.Height < 40 || Look is not { } look) return;
        // the lip of the compartment, then the wall set back into it
        ctx.FillRectangle(Craft.Solid(Color.Parse("#1B1814")), r);
        var inner = r.Deflate(6);
        SystemWall.Draw(ctx, this, inner, look, band: 6, motifShare: 0, motifInset: 0);
        var boardY = Math.Round(inner.Bottom - 26);
        ShelfBoard.DrawOnWall(ctx, new Rect(inner.X, boardY, inner.Width, 12), look, this);
        var size = Math.Round(Math.Min(boardY - inner.Y - 30, inner.Width * 0.55));
        var ease = 1 - Math.Pow(1 - _t, 3);
        var box = new Rect(inner.X + (inner.Width - size) / 2, boardY - size + Math.Round(14 * (1 - ease)), size, size);
        using (ctx.PushOpacity(ease))
            MotifCatalog.Draw(ctx, look, box, relief: true);
        // the compartment's depth: shadow under the lip at the top and left, light on the bottom lip
        ctx.FillRectangle(Craft.Fade(Colors.Black, 0.55, 0, Craft.Toward.Down), new Rect(inner.X, inner.Y, inner.Width, 10));
        ctx.FillRectangle(Craft.Fade(Colors.Black, 0.40, 0, Craft.Toward.Right), new Rect(inner.X, inner.Y, 6, inner.Height));
        ctx.FillRectangle(Craft.Fade(Colors.Black, 0, 0.30, Craft.Toward.Right), new Rect(inner.Right - 5, inner.Y, 5, inner.Height));
        Craft.Edges(ctx, r, lit: 0.10, dark: 0.40, grime: 0.20, grimeHeight: 6, topLit: 0.12, softRight: 0);
        ctx.FillRectangle(Craft.Hi(0.09), new Rect(inner.X, inner.Bottom, inner.Width, 1));
    }
}

/// <summary>
/// The body of a panel that lies over the page (R2: right-click list, the detail's "Mehr", dialogs – they read as a
/// generic UI kit's floating cards): a plate of the workbench – chamfered corners, a grain, a lit top and left edge, a
/// dark right edge and foot, four rivets and the plate's shadow falling down-right onto the page. Placed behind the
/// panel's content with a negative margin equal to the panel's padding.
/// </summary>
public sealed class PanelPlate : Control
{
    internal static readonly Color Body = Color.Parse("#25211C");

    static PanelPlate()
    {
        IsHitTestVisibleProperty.OverrideDefaultValue<PanelPlate>(false);
        ClipToBoundsProperty.OverrideDefaultValue<PanelPlate>(false);
    }

    public override void Render(DrawingContext ctx)
    {
        var r = new Rect(Bounds.Size);
        if (r.Width < 24 || r.Height < 24) return;
        // its shadow on the page: offset down-right, soft
        using (ctx.PushOpacity(0.55))
        {
            ctx.DrawGeometry(Craft.Dark(0.35), null, Chamfer.Octagon(r.Translate(new Vector(6, 9)), 6));
            ctx.DrawGeometry(Craft.Dark(0.25), null, Chamfer.Octagon(r.Translate(new Vector(3, 4)).Inflate(2), 7));
        }
        var outline = Chamfer.Octagon(r, 6);
        ctx.DrawGeometry(Craft.Solid(Body), null, outline);
        using (ctx.PushGeometryClip(outline))
        {
            TextureFill.Draw(ctx, this, r, Texture.Grain, 0.55);
            ctx.FillRectangle(Craft.Fade(Craft.Light, 0.05, 0, Craft.Toward.Down), new Rect(r.X, r.Y, r.Width, Math.Min(40, r.Height)));
            Craft.Edges(ctx, r, lit: 0.12, dark: 0.45, grime: 0.25, grimeHeight: 8, topLit: 0.16, softRight: 3);
        }
        // four rivets set into the corners
        foreach (var (x, y) in new[] { (r.X + 10, r.Y + 10), (r.Right - 14, r.Y + 10), (r.X + 10, r.Bottom - 14), (r.Right - 14, r.Bottom - 14) })
        {
            ctx.DrawEllipse(Craft.Dark(0.55), null, new Rect(x, y + 0.5, 4, 4));
            ctx.DrawEllipse(Craft.Solid(Color.Parse("#4A443A")), null, new Rect(x, y, 4, 4));
            ctx.DrawEllipse(Craft.Hi(0.25), null, new Rect(x + 0.8, y + 0.6, 1.6, 1.4));
        }
    }
}

/// <summary>
/// The front edge of the footer (R2): the footer is the bench's front ledge, and the page slides UNDER it – a 6 px top
/// face (lit edge, grain, a darker underside line) instead of content fading out into a flat band. Rows cut at the
/// bottom of a list then read as going behind the ledge, not as half-drawn.
/// </summary>
public sealed class FooterLip : Control
{
    private static readonly IImmutableSolidColorBrush Face = new ImmutableSolidColorBrush(Color.Parse("#1F1B17"));

    static FooterLip()
    {
        HeightProperty.OverrideDefaultValue<FooterLip>(6);
        IsHitTestVisibleProperty.OverrideDefaultValue<FooterLip>(false);
    }

    public override void Render(DrawingContext ctx)
    {
        var r = new Rect(Bounds.Size);
        if (r.Width <= 0) return;
        ctx.FillRectangle(Face, r);
        TextureFill.Draw(ctx, this, r, Texture.BoardGrain, 0.5);
        ctx.FillRectangle(Craft.Hi(0.13), new Rect(r.X, r.Y, r.Width, 1));
        ctx.FillRectangle(Craft.Fade(Craft.Light, 0.04, 0, Craft.Toward.Down), new Rect(r.X, r.Y + 1, r.Width, r.Height - 2));
        ctx.FillRectangle(Craft.Dark(0.5), new Rect(r.X, r.Bottom - 1, r.Width, 1));
    }
}
