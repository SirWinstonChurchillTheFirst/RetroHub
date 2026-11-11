using System.Globalization;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using RetroHub.App.Input;
using RetroHub.App.Theming;
using RetroHub.App.ViewModels;

namespace RetroHub.App.Controls;

/// <summary>
/// The Konsolen wall's position rail (DESIGN 7.4.1), drawn as a tuning dial ("Senderskala", M7 "gezeichnet" – the
/// hairline with coloured dashes read as a chart axis): a 16 px strip with chamfered ends and an engraved groove, a
/// printed scale (one major tick per system in its colour, two minor ticks between, each a hair off like printed ink),
/// and the slider knob riding in the groove with the needle in the current system's light; the current code stands on a
/// small paper flag above the needle. The needle GLIDES to the new system (Glide 220 ms; jumps while a direction is
/// held and when motion is reduced or off). Not focusable – the wall is the control; a click on the strip raises
/// <see cref="TickClicked"/> for the nearest system (the page turns that front forward), a drag scrubs along the
/// ticks. Hover (R2): the tick under the pointer stands taller, its code in Text 2 above it, and the tooltip names the
/// system. Drawn in one pass; text layouts are built when the current/hovered system changes.
/// </summary>
public sealed class PositionRail : Control, Avalonia.Rendering.ICustomHitTest
{
    /// <summary>The whole strip takes the pointer (the ticks are small).</summary>
    public bool HitTest(Point point) => new Rect(Bounds.Size).Contains(point);

    public static readonly StyledProperty<IReadOnlyList<SystemItemViewModel>?> SystemsProperty =
        AvaloniaProperty.Register<PositionRail, IReadOnlyList<SystemItemViewModel>?>(nameof(Systems));
    public static readonly StyledProperty<SystemItemViewModel?> CurrentProperty =
        AvaloniaProperty.Register<PositionRail, SystemItemViewModel?>(nameof(Current));
    /// <summary>Where the needle stands (x; animated when the front changes; NaN = not placed yet).</summary>
    public static readonly StyledProperty<double> NeedleProperty =
        AvaloniaProperty.Register<PositionRail, double>(nameof(Needle), double.NaN);

    private static readonly IImmutableSolidColorBrush Text2 = new ImmutableSolidColorBrush(Color.Parse("#C3C8D1"));
    private static readonly IImmutableSolidColorBrush Text3 = new ImmutableSolidColorBrush(Color.Parse("#979EA9"));
    private static readonly Color Strip = Color.Parse("#30333A");
    private static readonly Color Knob = Color.Parse("#575C66");

    /// <summary>The dial strip: 16 px, its bottom 4 px above the control's bottom; the groove runs through its middle.</summary>
    private const double StripHeight = 18, StripFromBottom = 3;
    /// <summary>Space kept free at both ends (the knob never leaves the strip).</summary>
    private const double EndInset = 12;

    private FormattedText? _code, _hoverCode;
    private int _hover = -1, _scrubbed = -1;
    private bool _dragging;

    static PositionRail()
    {
        AffectsRender<PositionRail>(SystemsProperty, CurrentProperty, BoundsProperty, NeedleProperty);
        FocusableProperty.OverrideDefaultValue<PositionRail>(false);
    }

    public PositionRail()
    {
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.Antialias);
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    public IReadOnlyList<SystemItemViewModel>? Systems { get => GetValue(SystemsProperty); set => SetValue(SystemsProperty, value); }
    public SystemItemViewModel? Current { get => GetValue(CurrentProperty); set => SetValue(CurrentProperty, value); }
    public double Needle { get => GetValue(NeedleProperty); set => SetValue(NeedleProperty, value); }

    /// <summary>A tick was clicked: the index of its system in <see cref="Systems"/>.</summary>
    public event Action<int>? TickClicked;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == CurrentProperty)
        {
            _code = null;
            _tabCode = null;
            PlaceNeedle(animate: true);
        }
        else if (change.Property == SystemsProperty)
        {
            SetHover(-1);
            PlaceNeedle(animate: false);
        }
        else if (change.Property == BoundsProperty) PlaceNeedle(animate: false);
    }

    /// <summary>The needle moves to the current system's tick: it glides (motion full, no direction held) or jumps.</summary>
    private void PlaceNeedle(bool animate)
    {
        var systems = Systems;
        if (systems == null || Bounds.Width <= 0) return;
        var index = -1;
        for (var i = 0; i < systems.Count; i++)
            if (ReferenceEquals(systems[i], Current)) { index = i; break; }
        if (index < 0) return;
        var x = TickX(index, systems.Count);
        _glide?.Stop();
        _swing?.Stop();
        _swingAngle = 0;
        if (!animate || double.IsNaN(Needle) || Math.Abs(Needle - x) < 0.5 || !Motion.Enabled)
        {
            Needle = x;
            _flagT = 1;
            InvalidateVisual();
            return;
        }
        if (Motion.Current == EffectiveMotion.Reduced)
        {
            // reduced: the needle jumps, the flag with the new code fades in (100 ms)
            Needle = x;
            _glide = Tween.Run(this, Motion.Scene, p => { _flagT = p; InvalidateVisual(); });
            return;
        }
        // full: the needle and knob slide (Glide; a held direction: GlideRepeat, linear), the flag fades over in Blink, and
        // the needle swings out on arrival (only when no direction is held)
        var repeat = NavRepeatTracker.Current?.IsActive == true;
        var from = Needle;
        var d = repeat ? Motion.GlideRepeatDuration : Motion.GlideDuration;
        var dir = Math.Sign(x - from);
        _flagT = 0;
        _glide = Tween.Run(this, d, p =>
        {
            Needle = Ease.Lerp(from, x, repeat ? p : Ease.CubicOut(p));
            _flagT = Math.Min(1, p * d.TotalMilliseconds / Motion.BlinkDuration.TotalMilliseconds);
        }, () =>
        {
            if (repeat || NavRepeatTracker.Current?.IsActive == true) return;
            _swing = Tween.Run(this, SwingDuration, p =>
            {
                _swingAngle = dir * SwingAngle(p);
                InvalidateVisual();
            }, () => { _swingAngle = 0; InvalidateVisual(); });
        });
    }

    private Tween? _glide, _swing;
    private double _swingAngle, _flagT = 1;

    /// <summary>The needle's swing on arrival: 0 → 4° → −2° → 0 over 200 ms about the knob's centre.</summary>
    public static readonly TimeSpan SwingDuration = TimeSpan.FromMilliseconds(200);

    /// <summary>The swing's angle in degrees at <paramref name="p"/> (0 … 1).</summary>
    public static double SwingAngle(double p) => p < 0.3
        ? 4 * Ease.SineInOut(p / 0.3)
        : p < 0.65 ? Ease.Lerp(4, -2, Ease.SineInOut((p - 0.3) / 0.35)) : Ease.Lerp(-2, 0, Ease.SineInOut((p - 0.65) / 0.35));

    /// <summary>The needle's swing angle shown now (tests).</summary>
    public double CurrentSwing => _swingAngle;

    /// <summary>Shows the arrival swing at <paramref name="p"/> (design previews: a still of the movement).</summary>
    internal void FreezeSwing(double p)
    {
        _glide?.Stop();
        _swing?.Stop();
        _flagT = 1;
        _swingAngle = SwingAngle(p);
        InvalidateVisual();
    }

    /// <summary>Centre x of tick <paramref name="i"/> of <paramref name="n"/> (evenly over the width, ends inset).</summary>
    private double TickX(int i, int n)
    {
        var w = Bounds.Width;
        if (n <= 1) return EndInset;
        return Math.Round(EndInset + i * (w - 2 * EndInset) / (n - 1));
    }

    public override void Render(DrawingContext ctx)
    {
        var systems = Systems;
        var w = Bounds.Width;
        if (systems == null || systems.Count == 0 || w <= 0) return;
        var n = systems.Count;
        var bottom = Math.Round(Bounds.Height - StripFromBottom);
        // R3 QA 3: the rendered steel running rail (24 px, C-profile) with one notch per Fach and an index tab (Reiter) as
        // the slider; the printed ticks, the needle and the paper flag are gone. Without the renders: the drawn dial.
        if (Rendered.Available && Rendered.MetaOf("rail") is { } rm && Rendered.MetaOf("reiter") is not null)
        {
            RenderRail(ctx, systems, w, rm);
            return;
        }
        var strip = new Rect(0, bottom - StripHeight, w, StripHeight);
        var groove = Math.Round(strip.Y + StripHeight / 2 + 2);
        var rendered = false;
        if (!rendered)
        {
            var outline = Chamfer.Octagon(strip, 3);
            // the strip: chamfered ends, grain, lit top edge, dark bottom edge, the groove cut along it
            ctx.DrawGeometry(Craft.Solid(Strip), null, outline);
            using (ctx.PushGeometryClip(outline))
            {
                TextureFill.Draw(ctx, this, strip, Texture.BoardGrain, 0.8);
                ctx.FillRectangle(Craft.Hi(0.10), new Rect(strip.X, strip.Y, w, 1));
                ctx.FillRectangle(Craft.Fade(Colors.Black, 0, 0.35, Craft.Toward.Down), new Rect(strip.X, strip.Bottom - 5, w, 5));
                ctx.FillRectangle(Craft.Dark(0.55), new Rect(strip.X, strip.Bottom - 1, w, 1));
            }
            Craft.Groove(ctx, 6, groove, w - 12, 0.60, 0.08);
        }
        // the printed scale: two minor ticks between neighbours, a major tick per system in its colour
        for (var i = 0; i < n - 1; i++)
        {
            var a = TickX(i, n);
            var b = TickX(i + 1, n);
            for (var k = 1; k <= 2; k++)
                ctx.FillRectangle(Text3, new Rect(Math.Round(a + (b - a) * k / 3), strip.Y + 2, 1, 5));
        }
        var current = -1;
        for (var i = 0; i < n; i++)
        {
            if (ReferenceEquals(systems[i], Current)) current = i;
            var seed = Craft.Seed(systems[i].Id);
            var x = TickX(i, n) + Math.Round(Craft.Range(seed, 20, -0.5, 0.5), 1);
            var len = 8 + Craft.Pick(seed, 21, 0, 1);
            var hovered = i == _hover && i != current;
            using (ctx.PushOpacity(hovered ? 1 : 0.85))
                ctx.FillRectangle(systems[i].Look.ColorBrush, new Rect(x - 1, strip.Y + 2, hovered ? 3 : 2, hovered ? len + 3 : len));
        }
        if (_hover >= 0 && _hover < n && _hover != current)
        {
            var h = systems[_hover];
            var hx = TickX(_hover, n);
            _hoverCode ??= new FormattedText(h.ShortName, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, FontRoles.SpineFace, 17, Text2);
            var tx0 = Math.Clamp(Math.Round(hx - _hoverCode.Width / 2), 0, Math.Max(0, w - _hoverCode.Width));
            ctx.DrawText(_hoverCode, new Point(tx0, Math.Round(strip.Y - 6 - _hoverCode.Height)));
        }
        if (current < 0 || double.IsNaN(Needle)) return;
        var c = systems[current];
        var nx = Math.Round(Needle);
        // the code on a paper flag above the strip; the needle runs from the flag down through the knob
        _code ??= new FormattedText(c.ShortName, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, FontRoles.SpineFace, 16,
            Craft.Solid(Craft.LabelInk));
        var fw = Math.Ceiling(_code.Width) + 14;
        var fh = Math.Ceiling(_code.Height) + 1;
        var flag = new Rect(Math.Clamp(Math.Round(nx - fw / 2), 0, Math.Max(0, w - fw)), Math.Round(strip.Y - 7 - fh), fw, fh);
        // the needle swings about the knob's centre when it arrives (R2 animation; flag and needle together)
        var swing = Math.Abs(_swingAngle) > 0.01
            ? Matrix.CreateTranslation(-nx, -groove) * Matrix.CreateRotation(_swingAngle * Math.PI / 180) * Matrix.CreateTranslation(nx, groove)
            : Matrix.Identity;
        using (ctx.PushTransform(swing))
        {
            ctx.FillRectangle(Craft.Dark(0.5), new Rect(nx + 1, flag.Bottom, 1, strip.Bottom - flag.Bottom));
            ctx.FillRectangle(c.Look.LightBrush, new Rect(nx - 1, flag.Bottom - 1, 2, strip.Bottom - flag.Bottom + 1));
        }
        // the rendered carriage riding in the groove, its anodised inlay in the system's colour
        if (rendered && Rendered.MetaOf("slider") is { } sm)
        {
            var sh = strip.Height + 12;
            var sw = Math.Round(sh * sm.Front.Width / sm.Front.Height);
            Rendered.Draw(ctx, this, "slider", new Rect(nx - sw / 2, strip.Y + strip.Height / 2 - sh / 2, sw, sh), true,
                new Rendered.Tint("band", c.Look.Color));
        }
        else DrawKnob(ctx, c, nx, groove);
        DrawFlag(ctx, c, flag, fh, swing);
    }

    /// <summary>Height of the rendered running rail (1080: 24 px; the control's height decides, ≈1100: 20).</summary>
    private double RailHeight => Bounds.Height >= 50 ? 24 : 20;

    private FormattedText? _tabCode;

    /// <summary>
    /// R3 QA 3 (the rail read as a UI slider): the rendered running rail along the whole width, a notch cut into its top lip
    /// for every Fach (the notch render set at each system's place), and the Reiter – a paper index tab in a steel clip
    /// riding on the top lip – at the current system, its colour only a 4 px printed stripe, the code printed in ink.
    /// </summary>
    private void RenderRail(DrawingContext ctx, IReadOnlyList<SystemItemViewModel> systems, double w, Rendered.Meta rm)
    {
        var n = systems.Count;
        var rh = RailHeight;
        var rail = new Rect(0, Math.Round(Bounds.Height - StripFromBottom - rh), w, rh);
        Rendered.DrawStrip(ctx, this, "rail", rail);
        var k = rh / rm.Front.Height;
        for (var i = 0; i < n; i++)
            Rendered.DrawMark(ctx, this, "rail-notch-mark", rail, rm, TickX(i, n), k);
        if (_hover >= 0 && _hover < n && !ReferenceEquals(systems[_hover], Current))
        {
            var h = systems[_hover];
            _hoverCode ??= new FormattedText(h.ShortName, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, FontRoles.SpineFace, 17, Text2);
            var hx = TickX(_hover, n);
            var tx0 = Math.Clamp(Math.Round(hx - _hoverCode.Width / 2), 0, Math.Max(0, w - _hoverCode.Width));
            ctx.DrawText(_hoverCode, new Point(tx0, Math.Round(rail.Y - 12 - _hoverCode.Height)));
        }
        var current = -1;
        for (var i = 0; i < n; i++) if (ReferenceEquals(systems[i], Current)) { current = i; break; }
        if (current < 0 || double.IsNaN(Needle) || Rendered.MetaOf("reiter") is not { } tm) return;
        var c = systems[current];
        var nx = Math.Round(Needle);
        var kt = rh / 24.0;
        _tabCode ??= new FormattedText(c.ShortName, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, FontRoles.SpineFace, Math.Round(15 * kt),
            Craft.Solid(Craft.LabelInk));
        var th = Math.Round(tm.Front.Height * kt);
        var tw = Math.Max(Math.Round(28 * kt), Math.Ceiling(_tabCode.Width) + Math.Round(16 * kt));
        // the clip hugs the rail's top lip: its foot 9 px (×k) under the rail's top edge
        var tab = new Rect(Math.Clamp(Math.Round(nx - tw / 2), 0, Math.Max(0, w - tw)), Math.Round(rail.Y + 9 * kt - th), tw, th);
        var swing = Math.Abs(_swingAngle) > 0.01
            ? Matrix.CreateTranslation(-nx, -rail.Y) * Matrix.CreateRotation(_swingAngle * Math.PI / 180) * Matrix.CreateTranslation(nx, rail.Y)
            : Matrix.Identity;
        using (ctx.PushTransform(swing))
        using (ctx.PushOpacity(Math.Clamp(0.35 + 0.65 * _flagT, 0, 1)))
        {
            if (!Rendered.DrawStrip(ctx, this, "reiter", tab, new Rendered.Tint("band", c.Look.Color))) return;
            var s = th / tm.Front.Height;
            var paper = tm.Rect("paper");
            var py = tab.Y + (paper.Y - tm.Front.Y) * s;
            var ph = paper.Height * s;
            Craft.Print(ctx, _tabCode, new Point(Math.Round(tab.X + (tw - _tabCode.Width) / 2), Math.Round(py + (ph - _tabCode.Height) / 2)), 0.15);
        }
    }

    private void DrawKnob(DrawingContext ctx, SystemItemViewModel c, double nx, double groove)
    {
        // the knob riding in the groove: chamfered, lit on top, darker at its foot, two grip lines, its shadow to the right
        var knob = new Rect(nx - 10, groove - 7, 20, 13);
        Craft.CastShadow(ctx, knob, 4, 0.5);
        var knobOutline = Chamfer.Octagon(knob, 3);
        ctx.DrawGeometry(Craft.Solid(Knob), null, knobOutline);
        using (ctx.PushGeometryClip(knobOutline))
        {
            ctx.FillRectangle(Craft.Hi(0.24), new Rect(knob.X, knob.Y, knob.Width, 1));
            ctx.FillRectangle(Craft.Fade(Colors.Black, 0, 0.40, Craft.Toward.Down), new Rect(knob.X, knob.Bottom - 4, knob.Width, 4));
            for (var g = -6; g <= 6; g += 3)
                if (Math.Abs(g) > 1) ctx.FillRectangle(Craft.Dark(0.40), new Rect(nx + g, knob.Y + 3, 1, 6));
        }
        ctx.FillRectangle(c.Look.LightBrush, new Rect(nx - 1, knob.Y + 1, 2, knob.Height - 2));
    }

    private void DrawFlag(DrawingContext ctx, SystemItemViewModel c, Rect flag, double fh, Matrix swing)
    {
        if (_code == null) return;
        var seed0 = Craft.Seed(c.Id);
        var tilt = Craft.Range(seed0, 22, -1.2, 1.2);
        // the flag with the new code comes up over the old place (Blink; reduced 100 ms)
        using (ctx.PushTransform(swing))
        using (ctx.PushOpacity(Math.Clamp(_flagT, 0, 1)))
        {
            Craft.DrawPaper(ctx, this, flag, seed0, Craft.LabelPaper, tilt, shadow: 0.45, fibres: 0.6);
            using (Craft.PushLabel(ctx, flag, tilt))
            {
                ctx.FillRectangle(c.Look.ColorBrush, new Rect(2, 2, 3, fh - 3));
                Craft.Print(ctx, _code, new Point(9, Math.Round((fh - _code.Height) / 2)));
            }
        }
    }

    /// <summary>Index of the tick nearest to <paramref name="x"/>, or −1 without systems.</summary>
    private int Nearest(double x)
    {
        var systems = Systems;
        if (systems == null || systems.Count == 0) return -1;
        var best = 0;
        for (var i = 1; i < systems.Count; i++)
            if (Math.Abs(TickX(i, systems.Count) - x) < Math.Abs(TickX(best, systems.Count) - x)) best = i;
        return best;
    }

    private void SetHover(int index)
    {
        if (_hover == index) return;
        _hover = index;
        _hoverCode = null;
        ToolTip.SetTip(this, index >= 0 && Systems is { } s && index < s.Count ? s[index].Name : null);
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var best = Nearest(e.GetPosition(this).X);
        if (best < 0) return;
        _dragging = true;
        _scrubbed = best;
        e.Pointer.Capture(this);
        TickClicked?.Invoke(best);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var at = Nearest(e.GetPosition(this).X);
        if (_dragging && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            // scrub: the front follows the pointer, one system at a time
            if (at >= 0 && at != _scrubbed) { _scrubbed = at; TickClicked?.Invoke(at); }
            SetHover(-1);
            return;
        }
        SetHover(at);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_dragging) return;
        _dragging = false;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _dragging = false;
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        SetHover(-1);
    }
}
