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

    static PaperNote()
    {
        AffectsRender<PaperNote>(SeedTextProperty, PaperProperty, TapeProperty);
        ClipToBoundsProperty.OverrideDefaultValue<PaperNote>(false);
    }

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
