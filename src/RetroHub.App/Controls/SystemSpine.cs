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
/// A system on a shelf (DESIGN 7.1 "Fächer-Brett", 7.4): a SPINE (72 × 200: wall-light tone with the era texture, 8 px band
/// in the system's colour, the code turned −90° in Rücken 22 paper, the count at the foot in Zahl 15) that turns its
/// FRONT forward when it gets the focus (200 × 200: the system's wall with band, texture and the motif standing at 72 %,
/// the name and the count). Only the width of the visible part animates (Glide 220 ms; 90 ms linear while a direction
/// is held; immediate when motion is reduced/off); the front is always laid out at its full width and clipped, so its
/// text never re-wraps, and it fades in over the last 40 % of the growth. Focus: the chamfered ring in the system's
/// light and a lift of 1.03 (no cone: the wall is the light surface). DataContext = <see cref="SystemItemViewModel"/>.
/// Drawn in one Render pass; text layouts are built on change only.
/// </summary>
public sealed class SystemSpine : Button, Avalonia.Rendering.ICustomHitTest
{
    public static readonly StyledProperty<double> SpineWidthProperty = AvaloniaProperty.Register<SystemSpine, double>(nameof(SpineWidth), 72);
    public static readonly StyledProperty<double> FrontWidthProperty = AvaloniaProperty.Register<SystemSpine, double>(nameof(FrontWidth), 200);

    private static readonly IImmutableSolidColorBrush Paper = new ImmutableSolidColorBrush(SystemPalette.Paper);
    private static readonly IImmutableSolidColorBrush Text2 = new ImmutableSolidColorBrush(Color.Parse("#C9BFAF"));
    private static readonly IImmutableSolidColorBrush LightEdge = new ImmutableSolidColorBrush(Color.Parse("#17FFF3E0"));

    private SystemItemViewModel? _system;
    private FormattedText? _code, _count, _name, _sub, _frontCount;
    private (string?, double, double) _textKey;
    private bool _open;
    private double _ringOpacity;

    static SystemSpine()
    {
        AffectsRender<SystemSpine>(BoundsProperty);
        AffectsMeasure<SystemSpine>(SpineWidthProperty, FrontWidthProperty);
        FocusAdornerProperty.OverrideDefaultValue<SystemSpine>(null);
        ClipToBoundsProperty.OverrideDefaultValue<SystemSpine>(false);
    }

    public SystemSpine()
    {
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.Antialias);
        Cursor = new Cursor(StandardCursorType.Hand);
        RenderTransformOrigin = new RelativePoint(0.5, 1, RelativeUnit.Relative);
        RenderTransform = new ScaleTransform(1, 1);
        Width = SpineWidth;
    }

    // no Button theme: everything is drawn in Render (a theme's background would cover it)
    protected override Type StyleKeyOverride => typeof(SystemSpine);

    public bool HitTest(Point point) => new Rect(Bounds.Size).Contains(point);

    public double SpineWidth { get => GetValue(SpineWidthProperty); set => SetValue(SpineWidthProperty, value); }
    public double FrontWidth { get => GetValue(FrontWidthProperty); set => SetValue(FrontWidthProperty, value); }

    /// <summary>The front is (or is becoming) visible.</summary>
    public bool IsOpen => _open;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        _system = DataContext as SystemItemViewModel;
        _textKey = default;
        InvalidateVisual();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SpineWidthProperty || change.Property == FrontWidthProperty)
        {
            SetWidth(animate: false);
            _textKey = default;
        }
        else if (change.Property == IsFocusedProperty || change.Property == IsPointerOverProperty) UpdateState();
    }

    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        UpdateState();
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        UpdateState();
    }

    /// <summary>Keyboard/controller focus opens the front (a mouse click navigates right away; hover shows the ring at 40 %).</summary>
    private void UpdateState()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            var visible = IsFocused && Classes.Contains(":focus-visible");
            var open = visible;
            _ringOpacity = visible ? 1 : IsPointerOver ? 0.4 : 0;
            if (open != _open)
            {
                _open = open;
                SetWidth(animate: true);
            }
            var lift = visible && Motion.AllowsMovement ? 1.03 : 1.0;
            if (RenderTransform is ScaleTransform st)
            {
                st.Transitions = Motion.Current == EffectiveMotion.Full && NavRepeatTracker.Current?.IsActive != true
                    ? [new DoubleTransition { Property = ScaleTransform.ScaleXProperty, Duration = Motion.StepDuration, Easing = new CubicEaseOut() },
                       new DoubleTransition { Property = ScaleTransform.ScaleYProperty, Duration = Motion.StepDuration, Easing = new CubicEaseOut() }]
                    : null;
                st.ScaleX = lift;
                st.ScaleY = lift;
            }
            InvalidateVisual();
        }, Avalonia.Threading.DispatcherPriority.Loaded);
    }

    private void SetWidth(bool animate)
    {
        var target = _open ? FrontWidth : SpineWidth;
        var repeat = NavRepeatTracker.Current?.IsActive == true;
        var d = !animate || Motion.Current != EffectiveMotion.Full ? TimeSpan.Zero : repeat ? Motion.GlideRepeatDuration : Motion.GlideDuration;
        Transitions = d == TimeSpan.Zero ? null :
            [new DoubleTransition { Property = WidthProperty, Duration = d, Easing = repeat ? new LinearEasing() : new CubicEaseOut() }];
        Width = target;
    }

    private void EnsureText()
    {
        var key = (_system?.Id, FrontWidth, Bounds.Height);
        if (_textKey == key && _code != null) return;
        _textKey = key;
        if (_system is not { } s) return;
        var h = Math.Max(1, Bounds.Height);
        var spineSize = Math.Clamp(Math.Round(SpineWidth * 0.30), 15, 22);
        _code = new FormattedText(s.ShortName, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, FontRoles.SpineFace, spineSize, Paper);
        _count = new FormattedText(s.GameCount.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            FontRoles.NumberFace, 15, Text2);
        // the front: the name in the poster role, measured (largest size whose words all fit, at most 3 lines; else the
        // code), the count bottom left beside the motif. Maker and year stand in the stage sentence above.
        var inset = Math.Round(FrontWidth * 0.08);
        _name = FitName(s, FrontWidth - 2 * inset);
        _sub = null;
        _frontCount = new FormattedText(s.CountText, Localization.Loc.Culture, FlowDirection.LeftToRight, FontRoles.NumberFace, 15, Paper);
        _ = h;
    }

    /// <summary>The system's name at the largest of 22/19/16 px (scaled with the front) that breaks only between words into ≤ 3 lines.</summary>
    private FormattedText FitName(SystemItemViewModel s, double maxWidth)
    {
        var k = FrontWidth / 200;
        foreach (var size in new[] { 22, 19, 16 }.Select(v => Math.Max(15, Math.Round(v * k))))
        {
            var words = s.PosterName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var fits = words.All(w => new FormattedText(w, Localization.Loc.Culture, FlowDirection.LeftToRight, FontRoles.PosterFace, size, Paper).Width <= maxWidth);
            if (!fits) continue;
            var t = new FormattedText(s.PosterName, Localization.Loc.Culture, FlowDirection.LeftToRight, FontRoles.PosterFace, size, Paper)
            {
                MaxTextWidth = maxWidth,
                LineHeight = Math.Round(size * 1.1),
            };
            if (t.Height <= 3 * Math.Round(size * 1.1) + 1) return t;
        }
        return new FormattedText(s.ShortName, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, FontRoles.SpineFace, Math.Round(28 * k), Paper);
    }

    public override void Render(DrawingContext ctx)
    {
        var r = new Rect(Bounds.Size);
        if (r.Width <= 0 || r.Height <= 0 || _system is not { } s) return;
        EnsureText();
        var look = s.Look;
        var span = Math.Max(1, FrontWidth - SpineWidth);
        var t = Math.Clamp((r.Width - SpineWidth) / span, 0, 1); // 0 = spine, 1 = front
        using (ctx.PushClip(r))
        {
            if (t < 1) DrawSpine(ctx, r, look, 1 - Math.Clamp((t - 0.0) / 0.5, 0, 1));
            if (t > 0)
            {
                var front = new Rect(0, 0, FrontWidth, r.Height);
                using (ctx.PushOpacity(Math.Clamp(t / 0.5, 0, 1)))
                    SystemWall.Draw(ctx, this, front, look, band: 8, motifShare: 0.72, motifInset: Math.Round(FrontWidth * 0.06));
                using (ctx.PushOpacity(Math.Clamp((t - 0.6) / 0.4, 0, 1)))
                    DrawFrontText(ctx, front);
            }
        }
        if (_ringOpacity > 0) Chamfer.DrawRing(ctx, r, look.LightBrush, opacity: _ringOpacity);
    }

    private void DrawSpine(DrawingContext ctx, Rect r, SystemLook look, double opacity)
    {
        using (ctx.PushOpacity(opacity))
        {
            ctx.FillRectangle(look.WallHiBrush, r);
            TextureFill.Draw(ctx, this, r, look.Texture, look.TextureOpacity);
            ctx.FillRectangle(look.ColorBrush, new Rect(0, 0, r.Width, 8));
            ctx.FillRectangle(LightEdge, new Rect(0, 8, r.Width, 1));
            // the count at the foot, the code turned −90° above it, reading bottom to top
            var foot = r.Height - 12;
            if (_count is { } count)
            {
                ctx.DrawText(count, new Point(Math.Round((SpineWidth - count.Width) / 2), Math.Round(foot - count.Height)));
                foot -= count.Height + 12;
            }
            if (_code is { } code)
            {
                var x = Math.Round((SpineWidth - code.Height) / 2);
                using (ctx.PushTransform(Matrix.CreateRotation(-Math.PI / 2) * Matrix.CreateTranslation(x, Math.Round(foot))))
                    ctx.DrawText(code, new Point(0, 0));
            }
        }
    }

    private void DrawFrontText(DrawingContext ctx, Rect front)
    {
        var inset = Math.Round(FrontWidth * 0.08);
        if (_name is { } name) ctx.DrawText(name, new Point(inset, 8 + Math.Round(inset * 0.9)));
        var y = front.Bottom - inset;
        if (_frontCount is { } count)
        {
            y -= count.Height;
            ctx.DrawText(count, new Point(inset, Math.Round(y)));
        }
        if (_sub is { } sub)
        {
            y -= sub.Height;
            ctx.DrawText(sub, new Point(inset, Math.Round(y)));
        }
    }
}
