using System.Globalization;
using Avalonia;
using Avalonia.Media;
using RetroHub.App.Theming;
using static RetroHub.App.Controls.Rendered;

namespace RetroHub.App.Controls;

/// <summary>
/// The Konsolen wall from rendered objects (R3, owner feedback "zu sehr selber gecoded"): the spine ("Fach"), the open
/// front with its compartment and the "Noch leer" sleeves are Blender renders (<see cref="Rendered"/>), tinted per system
/// through their light maps – the band in the system's colour, the body in its housing tone. The app prints only what
/// changes: the code on the paper label, the count on the metal tag, name/maker/count on the front; the motif is the
/// rendered relief plaque of the system's <see cref="Motif"/>. Without the sprites the drawn shapes stay (fallback).
/// </summary>
public sealed partial class SystemSpine
{
    /// <summary>"l" (Konsolen 128 × 460) or "s" (≈1100: 96 × 360) when the wall spine is drawn from the renders, else null.</summary>
    private string? SpineSprite => ShowMotif && Opens && Available ? (SpineWidth >= 112 ? "l" : "s") : null;

    /// <summary>The big front is drawn from the renders ("front-l" 520 × 600, "front-s" 380 × 460).</summary>
    private string? FrontSprite => HasShelf && Available ? (FrontWidth >= 480 ? "front-l" : "front-s") : null;

    /// <summary>
    /// The housing tone the body of a rendered spine/front takes (theme v2: neutral greys, no warm grey, beige or wood;
    /// violet and berry keep a trace of their colour). The renders multiply it by their light, so it reads as plastic.
    /// </summary>
    internal static Color HousingBody(SystemLook look) => look.Housing switch
    {
        Housing.Black => Color.Parse("#1F2125"),
        Housing.Anthracite => Color.Parse("#2B2E33"),
        Housing.CoolGrey => Color.Parse("#45494F"),
        Housing.WarmGrey or Housing.Wood => Color.Parse("#3C3F45"),
        Housing.White => Color.Parse("#A9ADB3"),
        Housing.Beige => Color.Parse("#8A8E94"),
        _ => ColorUtil.Mix(Color.Parse("#2B2E33"), look.Color, 0.32),
    };

    /// <summary>A light body (white/beige housings): the front's text is printed in ink instead of paper.</summary>
    private static bool LightBody(SystemLook look) => ColorUtil.Contrast(SystemPalette.Paper, HousingBody(look)) < 3.5;

    /// <summary>The motif plaque's tints (Farbe, Zweitfarbe lifted on dark bodies, the lifted colour, Licht).</summary>
    internal static Tint[] MotifTints(SystemLook look) =>
    [
        new("body", ((ISolidColorBrush)MotifCatalog.BodyBrush(look)).Color),
        new("color", look.Color),
        new("lift", MotifCatalog.LiftColor(look)),
        new("light", look.Light),
    ];

    /// <summary>The rendered relief plaque of the system's motif into <paramref name="box"/>; false when not available.</summary>
    internal static bool DrawMotifPlaque(DrawingContext ctx, Visual visual, SystemLook look, Rect box) =>
        box.Width > 0 && Draw(ctx, visual, "motif-" + look.Motif, box, true, MotifTints(look));

    private FormattedText? _labelCode, _tagCount;
    private (string?, string?, int) _labelKey;

    /// <summary>The code for the paper label (ink, stepped down until it fits the label) and the number for the tag.</summary>
    private void EnsureSpriteText(Meta m, double labelRoom)
    {
        var key = (_system?.Id, SpineSprite, _system?.GameCount ?? -1);
        if (_labelKey == key && _labelCode != null) return;
        _labelKey = key;
        if (_system is not { } s) return;
        var size = SpineSprite == "l" ? 28.0 : 22.0;
        var ink = Craft.Solid(Craft.LabelInk);
        while (true)
        {
            _labelCode = new FormattedText(s.ShortName, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, FontRoles.SpineFace, size, ink);
            if ((_labelCode.Width <= labelRoom && _labelCode.Height <= m.Rect("label").Width - 4) || size <= 14) break;
            size--;
        }
        _tagCount = new FormattedText(s.GameCount.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            FontRoles.NumberFace, SpineSprite == "l" ? 21 : 17, TagInk);
    }

    private static readonly IImmutableSolidColorBrush TagInk = new ImmutableSolidColorBrush(Color.Parse("#ECEEF0"));

    /// <summary>
    /// The closed wall spine from its render: band and body tinted, the code in ink on the paper label (reading upward,
    /// hanging under the label's top), the motif plaque in the debossed square, the count on the riveted metal tag.
    /// </summary>
    private bool DrawRenderedSpine(DrawingContext ctx, Rect area, SystemLook look, double opacity)
    {
        if (SpineSprite is not { } size || _system is not { } s) return false;
        var name = $"spine-{size}{Craft.Pick(Craft.Seed(s.Id), 41, 0, 2)}";
        if (MetaOf(name) is not { } m) return false;
        var front = new Rect(area.X, area.Y, SpineWidth, area.Height);
        var sx = front.Width / m.Front.Width;
        var sy = front.Height / m.Front.Height;
        Rect Map(Rect r) => new(front.X + (r.X - m.Front.X) * sx, front.Y + (r.Y - m.Front.Y) * sy, r.Width * sx, r.Height * sy);
        using (ctx.PushOpacity(opacity))
        {
            if (!Draw(ctx, this, name, front, true, new Tint("body", HousingBody(look)), new Tint("band", look.Color))) return false;
            var label = Map(m.Rect("label"));
            var pad = Math.Round(label.Width * 0.2);
            EnsureSpriteText(m, label.Height - 2 * pad);
            if (_labelCode is { } code)
                using (ctx.PushClip(label))
                using (ctx.PushTransform(Matrix.CreateRotation(-Math.PI / 2) *
                    Matrix.CreateTranslation(Math.Round(label.X + (label.Width - code.Height) / 2), Math.Round(label.Y + pad + code.Width))))
                    Craft.Print(ctx, code, new Point(0, 0), 0.2);
            var motif = Map(m.Rect("motif"));
            var inset = Math.Round(motif.Width * 0.065);
            var box = motif.Deflate(inset);
            if (!DrawMotifPlaque(ctx, this, look, box)) MotifCatalog.Draw(ctx, look, box, relief: true);
            if (_tagCount is { } count && s.GameCount > 0)
            {
                var tag = size == "l" ? "tag-l" : "tag-s";
                var th = MetaOf(tag)?.Front.Height ?? 28;
                var tw = Math.Max(size == "l" ? 48 : 40, Math.Round(count.Width + (size == "l" ? 30 : 24)));
                var cx = front.X + (m.Value("tagCenterX") - m.Front.X) * sx;
                var cy = front.Y + (m.Value("tagCenterY") - m.Front.Y) * sy;
                var plate = new Rect(Math.Round(cx - tw / 2), Math.Round(cy - th / 2), tw, th);
                if (DrawStrip(ctx, this, tag, plate))
                    ctx.DrawText(count, new Point(Math.Round(plate.X + (tw - count.Width) / 2), Math.Round(plate.Y + (th - count.Height) / 2)));
            }
        }
        return true;
    }

    /// <summary>The open front's render (band, body, the compartment with its inner board) into <paramref name="front"/>.</summary>
    private bool DrawRenderedFrontBody(DrawingContext ctx, Rect front, SystemLook look) =>
        FrontSprite is { } name && Draw(ctx, this, name, front, true, new Tint("body", HousingBody(look)), new Tint("band", look.Color));

    /// <summary>
    /// The compartment of the rendered front in front coordinates (top of its opening, top of the inner board), or null
    /// when the front is drawn.
    /// </summary>
    private (double RecessTop, double BoardY)? FrontCompartment(double height)
    {
        if (FrontSprite is not { } name || MetaOf(name) is not { } m) return null;
        var k = height / m.Front.Height;
        return (Math.Round((m.Rect("recess").Y - m.Front.Y) * k), Math.Round((m.Rect("innerBoard").Y - m.Front.Y) * k));
    }

    // ===================== the "Noch leer" sleeves from their renders =====================

    /// <summary>"l" (row ≥ 130 px) or "s" (≈1100) when the sleeves are drawn from the renders, else null.</summary>
    private string? SleeveSprite => !Opens && Available ? (SleeveRowHeight >= 130 ? "l" : "s") : null;

    /// <summary>The lean of the rendered leaning sleeve (≈ 6.5°, top resting on its left neighbour).</summary>
    private const double RenderedLean = -0.113;

    /// <summary>The rendered sleeve's shape: one of four upright cases, the leaning one or the flat one (seeded).</summary>
    private SleeveShape? RenderedSleeve(uint seed, double H, int index, int count)
    {
        if (SleeveSprite is not { } size) return null;
        string name;
        var lean = 0.0;
        var flat = false;
        if (count >= 6 && index == count - 1)
        {
            name = $"sleeve-{size}flat";
            flat = true;
        }
        // one rendered lean looks alike wherever it stands: at most one in eight, never two side by side (odd places)
        else if (index > 0 && index % 2 == 1 && Craft.Rand(seed, 3) < 0.25)
        {
            name = $"sleeve-{size}lean";
            lean = RenderedLean;
        }
        else name = $"sleeve-{size}{Craft.Pick(seed, 1, 0, 3)}";
        if (MetaOf(name) is not { } m) return null;
        // the leaning one keeps 8 px at its left for its top resting on the neighbour's side face
        var x = flat ? 2 : lean != 0 ? 8 : 0;
        return new SleeveShape(new Rect(x, H - m.Front.Height, m.Front.Width, m.Front.Height), seed, lean, false, false, 0, flat) { Sprite = name };
    }

    private FormattedText? _sleeveCode;
    private (string?, string?) _sleeveKey;

    /// <summary>
    /// A rendered sleeve: its render (card body near-neutral, the colour tab on the label), the code printed in ink on
    /// the label. Quiet sleeves keep their label in the shade of the row; the pulled one straightens (the upright case
    /// fades in over the leaning one) and its label comes into the light.
    /// </summary>
    private bool DrawRenderedSleeve(DrawingContext ctx, SystemLook look, SleeveShape s)
    {
        if (s.Sprite is not { } name || MetaOf(name) is not { } m || _system is not { } sys) return false;
        var pull = Math.Clamp(Pull, 0, 1);
        var lift = SleeveLift;
        var f = s.Front.Translate(new Vector(0, -lift));
        var tints = new[] { new Tint("body", RenderedSleeveBody(look)), new Tint("band", look.Color) };
        if (pull > 0 && !s.Flat)
            Craft.Contact(ctx, f.X - f.Width * 0.05, SleeveRowHeight - 1, f.Width * 1.1 + SleeveSide, 6, 0.45 * pull);
        var key = (sys.Id, name);
        if (_sleeveKey != key || _sleeveCode == null)
        {
            _sleeveKey = key;
            var room = s.Flat ? m.Rect("label").Width - 16 : m.Rect("label").Height - m.Rect("tab").Height - 12;
            var across = s.Flat ? m.Rect("label").Height - 6 : (s.Lean != 0 ? m.Front.Width - 26 : m.Rect("label").Width) - 6;
            var size = SleeveSprite == "l" ? 16.0 : 14.0;
            while (true)
            {
                _sleeveCode = new FormattedText(sys.ShortName, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, FontRoles.SpineFace, size, Craft.Solid(Craft.LabelInk));
                if ((_sleeveCode.Width <= room && _sleeveCode.Height <= across) || size <= 11) break;
                size--;
            }
        }
        var code = _sleeveCode!;
        // the leaning sleeve straightens as it is pulled: the upright case of its size fades in over it
        var upright = s.Lean != 0 ? $"sleeve-{SleeveSprite}1" : null;
        var leanOpacity = upright != null ? 1 - pull : 1;
        if (leanOpacity > 0)
            using (ctx.PushOpacity(leanOpacity))
            {
                if (!Draw(ctx, this, name, f, true, tints)) return false;
                PrintSleeveLabel(ctx, m, f, code, s, pull);
            }
        if (upright != null && pull > 0 && MetaOf(upright) is { } um)
            using (ctx.PushOpacity(pull))
            {
                var uf = new Rect(f.Right - um.Front.Width - 4, f.Bottom - um.Front.Height, um.Front.Width, um.Front.Height);
                Draw(ctx, this, upright, uf, true, tints);
                PrintSleeveLabel(ctx, um, uf, code, s with { Lean = 0 }, pull);
            }
        return true;
    }

    private void PrintSleeveLabel(DrawingContext ctx, Meta m, Rect f, FormattedText code, SleeveShape s, double pull)
    {
        var sx = f.Width / m.Front.Width;
        var sy = f.Height / m.Front.Height;
        Rect Map(Rect r) => new(f.X + (r.X - m.Front.X) * sx, f.Y + (r.Y - m.Front.Y) * sy, r.Width * sx, r.Height * sy);
        var label = Map(m.Rect("label"));
        var tab = Map(m.Rect("tab"));
        // a quiet sleeve's label stands in the shade of the row (R3 contact sheet: twenty bright labels were busy)
        var shade = 0.30 * (1 - pull);
        if (s.Flat)
        {
            var paper = new Rect(label.X, label.Y, Math.Max(0, tab.X - label.X), label.Height);
            if (shade > 0) ctx.FillRectangle(Craft.Dark(shade), paper);
            Craft.Print(ctx, code, new Point(Math.Round(paper.X + 10), Math.Round(paper.Y + (paper.Height - code.Height) / 2)), 0.2);
            return;
        }
        if (s.Lean != 0)
        {
            // the label turns with the sleeve: printed about the label's centre below the tab
            var c = new Point(label.Center.X + 2, label.Y + (tab.Height + label.Height) / 2);
            if (shade > 0 && MetaOf($"sleeve-{SleeveSprite}1") is { } um)
            {
                // the label's paper below the tab, turned with the sleeve
                var lw = um.Rect("label").Width * sx;
                var lh = (um.Rect("label").Height - um.Rect("tab").Height) * sy;
                using (ctx.PushTransform(Matrix.CreateRotation(s.Lean) * Matrix.CreateTranslation(c.X, c.Y)))
                    ctx.FillRectangle(Craft.Dark(shade), new Rect(-lw / 2, -lh / 2, lw, lh));
            }
            using (ctx.PushTransform(Matrix.CreateTranslation(-code.Width / 2, -code.Height / 2) * Matrix.CreateRotation(-Math.PI / 2 + s.Lean) *
                Matrix.CreateTranslation(c.X, c.Y)))
                Craft.Print(ctx, code, new Point(0, 0), 0.2);
            return;
        }
        var body = new Rect(label.X, tab.Bottom, label.Width, Math.Max(0, label.Bottom - tab.Bottom));
        if (shade > 0) ctx.FillRectangle(Craft.Dark(shade), body);
        using (ctx.PushClip(body))
        using (ctx.PushTransform(Matrix.CreateRotation(-Math.PI / 2) *
            Matrix.CreateTranslation(Math.Round(body.X + (body.Width - code.Height) / 2), Math.Round(body.Y + 6 + code.Width))))
            Craft.Print(ctx, code, new Point(0, 0), 0.2);
    }

    /// <summary>
    /// The card of a rendered sleeve: near-neutral grey with 16 % of a cool system colour; red, orange and yellow systems
    /// stay plain grey card (mixed into the card they turn brown/olive – theme v2), their colour lives in the tab.
    /// </summary>
    private static Color RenderedSleeveBody(SystemLook look)
    {
        var grey = Color.Parse("#2E3136");
        var hue = ColorUtil.ToHsl(look.Color).H;
        return hue is >= 345 or <= 75 ? grey : ColorUtil.Mix(grey, look.Color, 0.16);
    }
}
