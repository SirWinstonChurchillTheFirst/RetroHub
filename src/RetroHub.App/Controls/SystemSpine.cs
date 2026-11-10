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
/// A system on a shelf (DESIGN 7.1 "Fächer-Brett", 7.4 "Fächerwand"): a SPINE (wall-light tone with the era texture, 8 px
/// band in the system's colour, the code turned −90° in the spine role, the count at the foot; on the Konsolen wall
/// also the system's motif standing at the foot, <see cref="ShowMotif"/>) that turns its FRONT forward when it gets the
/// focus (the system's wall with band, texture and the motif standing at 72 %, the name, maker and year, the count).
/// The front may be taller than the spine (<see cref="SpineHeight"/>; Konsolen: spine 460, front 600): both stand on the
/// same board, the drawn height grows with the width. Only the width of the visible part animates (Glide 220 ms; 90 ms
/// linear while a direction is held; immediate when motion is reduced/off); the front is always laid out at its full
/// size and clipped, so its text never re-wraps, and it fades in over the last 40 % of the growth. Focus: the chamfered
/// standing ring in the system's light and a lift of 1.03 (no cone: the wall is the light surface).
/// DataContext = <see cref="SystemItemViewModel"/>. Drawn in one Render pass; text layouts are built on change only.
/// </summary>
public sealed class SystemSpine : Button, Avalonia.Rendering.ICustomHitTest
{
    public static readonly StyledProperty<double> SpineWidthProperty = AvaloniaProperty.Register<SystemSpine, double>(nameof(SpineWidth), 72);
    public static readonly StyledProperty<double> FrontWidthProperty = AvaloniaProperty.Register<SystemSpine, double>(nameof(FrontWidth), 200);
    /// <summary>Height of the closed spine (NaN = the control's height; the open front always takes the full height).</summary>
    public static readonly StyledProperty<double> SpineHeightProperty = AvaloniaProperty.Register<SystemSpine, double>(nameof(SpineHeight), double.NaN);
    /// <summary>The motif stands at the foot of the spine (Konsolen: the wall reads without text, DESIGN 7.4).</summary>
    public static readonly StyledProperty<bool> ShowMotifProperty = AvaloniaProperty.Register<SystemSpine, bool>(nameof(ShowMotif));
    /// <summary>
    /// False: the spine never turns a front (the thin spines of systems without games, "Noch leer"): focus is ring and
    /// lift only, and the spine is drawn quieter (status: empty).
    /// </summary>
    public static readonly StyledProperty<bool> OpensProperty = AvaloniaProperty.Register<SystemSpine, bool>(nameof(Opens), true);

    private static readonly IImmutableSolidColorBrush Paper = new ImmutableSolidColorBrush(SystemPalette.Paper);
    private static readonly IImmutableSolidColorBrush Text2 = new ImmutableSolidColorBrush(Color.Parse("#C9BFAF"));
    private static readonly IImmutableSolidColorBrush LightEdge = new ImmutableSolidColorBrush(Color.Parse("#17FFF3E0"));

    private SystemItemViewModel? _system;
    private FormattedText? _code, _count, _name, _sub, _frontCount;
    private (string?, double, double, int) _textKey;
    private bool _open;
    private double _ringOpacity;

    static SystemSpine()
    {
        AffectsRender<SystemSpine>(BoundsProperty, ShowMotifProperty, SpineHeightProperty);
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
        FocusAdorner = null; // a local value: the theme's 2 px focus rectangle never shows (the chamfered ring is the focus)
    }

    // no Button theme: everything is drawn in Render (a theme's background would cover it)
    protected override Type StyleKeyOverride => typeof(SystemSpine);

    /// <summary>Only the drawn object takes the pointer (not the free space above a short spine or beside an open front).</summary>
    public bool HitTest(Point point) => ObjectRect.Contains(point);

    public double SpineWidth { get => GetValue(SpineWidthProperty); set => SetValue(SpineWidthProperty, value); }
    public double FrontWidth { get => GetValue(FrontWidthProperty); set => SetValue(FrontWidthProperty, value); }
    public double SpineHeight { get => GetValue(SpineHeightProperty); set => SetValue(SpineHeightProperty, value); }
    public bool ShowMotif { get => GetValue(ShowMotifProperty); set => SetValue(ShowMotifProperty, value); }
    public bool Opens { get => GetValue(OpensProperty); set => SetValue(OpensProperty, value); }

    /// <summary>Free space on each side of an open front (M2 QA): ring (3 + 3) and lift 1.03 never touch the neighbours.</summary>
    public const double FrontGap = 16;

    /// <summary>Width of the item while its front is turned forward: the front plus <see cref="FrontGap"/> on both sides.</summary>
    public double OpenWidth => FrontWidth + 2 * FrontGap;

    /// <summary>The front is (or is becoming) visible.</summary>
    public bool IsOpen => _open;

    /// <summary>A big front (Konsolen): poster name in 44/36, maker and year in the reading role, the count in Zahl 40.</summary>
    private bool LargeFront => FrontWidth >= 360;

    /// <summary>Share of the way from spine (0) to front (1), from the current (animating) width.</summary>
    private double Openness => Math.Clamp((Bounds.Width - SpineWidth) / Math.Max(1, OpenWidth - SpineWidth), 0, 1);

    /// <summary>The drawn object in local coordinates: the spine, or the front standing FrontGap in from both sides.</summary>
    public Rect ObjectRect
    {
        get
        {
            var r = new Rect(Bounds.Size);
            if (r.Width <= 0 || r.Height <= 0) return default;
            var t = Openness;
            var spineH = double.IsNaN(SpineHeight) ? r.Height : Math.Min(SpineHeight, r.Height);
            var h = Math.Round(spineH + (r.Height - spineH) * t);
            var pad = Math.Round(FrontGap * t);
            return new Rect(pad, r.Height - h, Math.Max(1, r.Width - 2 * pad), h);
        }
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_system != null) _system.PropertyChanged -= OnSystemChanged;
        _system = DataContext as SystemItemViewModel;
        if (_system != null) _system.PropertyChanged += OnSystemChanged;
        _textKey = default;
        InvalidateVisual();
    }

    private void OnSystemChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SystemItemViewModel.GameCount) or nameof(SystemItemViewModel.CountText))
        {
            _textKey = default;
            InvalidateVisual();
        }
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
            var open = visible && Opens;
            _ringOpacity = visible ? 1 : IsPointerOver ? 0.4 : 0;
            if (open != _open)
            {
                _open = open;
                SetWidth(animate: true);
            }
            // the ring stands above the neighbouring spines (they would cover it: later siblings draw on top)
            var z = _ringOpacity > 0 ? 1 : 0;
            ZIndex = z;
            if (Avalonia.VisualTree.VisualExtensions.GetVisualParent(this) is Avalonia.Controls.Presenters.ContentPresenter container) container.ZIndex = z;
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
        var target = _open ? OpenWidth : SpineWidth;
        var repeat = NavRepeatTracker.Current?.IsActive == true;
        var d = !animate || Motion.Current != EffectiveMotion.Full ? TimeSpan.Zero : repeat ? Motion.GlideRepeatDuration : Motion.GlideDuration;
        Transitions = d == TimeSpan.Zero ? null :
            [new DoubleTransition { Property = WidthProperty, Duration = d, Easing = repeat ? new LinearEasing() : new CubicEaseOut() }];
        Width = target;
    }

    private void EnsureText()
    {
        var key = (_system?.Id, FrontWidth, SpineWidth, _system?.GameCount ?? -1);
        if (_textKey == key && _code != null) return;
        _textKey = key;
        if (_system is not { } s) return;
        var spineSize = Math.Clamp(Math.Round(SpineWidth * 0.30), 15, 28);
        _code = new FormattedText(s.ShortName, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, FontRoles.SpineFace, spineSize, Paper);
        var countSize = SpineWidth >= 96 ? Math.Min(28, Math.Round(SpineWidth * 0.22)) : 15;
        _count = new FormattedText(s.GameCount.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            FontRoles.NumberFace, countSize, SpineWidth >= 96 ? Paper : Text2);
        // the front: the name in the poster role, measured (largest size whose words all fit, at most 3 lines; else the
        // code), the count bottom left beside the motif. A big front (Konsolen) adds maker and year under the name.
        var inset = FrontInset;
        _name = FitName(s, FrontWidth - 2 * inset);
        _sub = LargeFront && !string.IsNullOrEmpty(s.SubTitle)
            ? new FormattedText(s.SubTitle, Localization.Loc.Culture, FlowDirection.LeftToRight, FontRoles.ReadFace, FrontWidth >= 480 ? 18 : 16, Text2)
            { MaxTextWidth = FrontWidth - 2 * inset }
            : null;
        _frontCount = new FormattedText(s.CountText, Localization.Loc.Culture, FlowDirection.LeftToRight, FontRoles.NumberFace,
            LargeFront ? (FrontWidth >= 480 ? 40 : 32) : 15, Paper);
    }

    private double FrontInset => LargeFront ? (FrontWidth >= 480 ? 40 : 32) : Math.Round(FrontWidth * 0.08);

    /// <summary>
    /// The system's name at the largest size (big fronts 44/36/30 or 36/30/26, small fronts 22/19/16 scaled with the
    /// front) that breaks only between words into ≤ 3 lines; else the code.
    /// </summary>
    private FormattedText FitName(SystemItemViewModel s, double maxWidth)
    {
        var k = FrontWidth / 200;
        double[] sizes = LargeFront
            ? FrontWidth >= 480 ? [44, 36, 30] : [36, 30, 26]
            : new[] { 22, 19, 16 }.Select(v => Math.Max(15, Math.Round(v * k))).ToArray();
        foreach (var size in sizes)
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
        var t = Openness; // 0 = spine, 1 = front
        var area = ObjectRect;
        using (ctx.PushClip(area))
        {
            if (t < 1) DrawSpine(ctx, area, look, (1 - Math.Clamp(t / 0.5, 0, 1)) * (Opens ? 1 : _ringOpacity > 0.5 ? 0.9 : 0.62));
            if (t > 0)
            {
                var front = new Rect(area.X, 0, FrontWidth, r.Height);
                using (ctx.PushOpacity(Math.Clamp(t / 0.5, 0, 1)))
                    SystemWall.Draw(ctx, this, front, look, band: 8, motifShare: 0.72, motifInset: Math.Round(FrontWidth * 0.06));
                using (ctx.PushOpacity(Math.Clamp((t - 0.6) / 0.4, 0, 1)))
                using (ctx.PushTransform(Matrix.CreateTranslation(area.X, 0)))
                    DrawFrontText(ctx, new Rect(0, 0, FrontWidth, r.Height));
            }
        }
        // the chamfered ring in the system's light: 3 px room-deep gap + 3 px light, chamfer 6; it stands on the board
        // like the ring of a box (open at the foot, M3 QA)
        if (_ringOpacity > 0)
            using (ctx.PushOpacity(_ringOpacity))
                Chamfer.DrawStandingRing(ctx, area, look.LightBrush);
    }

    private void DrawSpine(DrawingContext ctx, Rect area, SystemLook look, double opacity)
    {
        using (ctx.PushOpacity(opacity))
        using (ctx.PushTransform(Matrix.CreateTranslation(area.X, area.Y)))
        {
            var w = SpineWidth;
            var r = new Rect(0, 0, area.Width, area.Height);
            // the Konsolen spine is a narrow slice of its wall (38 % Wand hell over 62 % Wand, the light edge between);
            // the small spines of Start stay one tone
            var split = ShowMotif ? Math.Round(r.Height * 0.38) : r.Height;
            ctx.FillRectangle(look.WallHiBrush, new Rect(0, 0, r.Width, split));
            if (split < r.Height) ctx.FillRectangle(look.WallBrush, new Rect(0, split, r.Width, r.Height - split));
            SystemWall.DrawTextures(ctx, this, r, look);
            if (split < r.Height) ctx.FillRectangle(LightEdge, new Rect(0, split, r.Width, 1));
            ctx.FillRectangle(look.ColorBrush, new Rect(0, 0, r.Width, 8));
            ctx.FillRectangle(LightEdge, new Rect(0, 8, r.Width, 1));
            // from the foot up: the count, (the motif standing above it), the code turned −90° reading bottom to top
            var gap = w >= 96 ? 16 : 12;
            var foot = r.Height - gap;
            if (_count is { } count && _system?.GameCount > 0)
            {
                ctx.DrawText(count, new Point(Math.Round((w - count.Width) / 2), Math.Round(foot - count.Height)));
                foot -= count.Height + gap;
            }
            if (ShowMotif)
            {
                var size = Math.Round(w - 2 * Math.Max(12, Math.Round(w * 0.14)));
                MotifCatalog.Draw(ctx, look, new Rect(Math.Round((w - size) / 2), Math.Round(foot - size), size, size), cutout: look.WallBrush);
                foot -= size + gap + 4;
            }
            if (_code is { } code)
            {
                var x = Math.Round((w - code.Height) / 2);
                // on the Konsolen spine the code reads upward and ends under the band (a label at the head of the spine);
                // only a code longer than the light zone runs on below it
                var start = ShowMotif ? Math.Min(foot, 8 + gap + code.Width) : foot;
                using (ctx.PushClip(new Rect(0, 12, w, Math.Max(0, foot - 12))))
                using (ctx.PushTransform(Matrix.CreateRotation(-Math.PI / 2) * Matrix.CreateTranslation(x, Math.Round(start))))
                    ctx.DrawText(code, new Point(0, 0));
            }
        }
    }

    private void DrawFrontText(DrawingContext ctx, Rect front)
    {
        var inset = FrontInset;
        var y = 8 + Math.Round(LargeFront ? inset * 0.8 : inset * 0.9);
        if (_name is { } name)
        {
            ctx.DrawText(name, new Point(inset, y));
            y += name.Height + 8;
        }
        if (_sub is { } sub) ctx.DrawText(sub, new Point(inset, Math.Round(y)));
        if (_frontCount is { } count)
            ctx.DrawText(count, new Point(inset, Math.Round(front.Bottom - inset - count.Height + (LargeFront ? 6 : 0))));
    }
}
