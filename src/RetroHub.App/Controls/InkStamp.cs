using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using RetroHub.App.Theming;

namespace RetroHub.App.Controls;

/// <summary>
/// The favourite stamp on the pulled box (R3 QA: the stamp's motion only showed in a 22 px star on a button): a 64 px
/// rubber stamp in red ink at 85 % on the lower right of the box's picture – a star inside a double ring whose outer ring
/// is worn (broken by seeded gaps), turned −9°. When the SAME game becomes a favourite it lands: 1.3 → 1.0 from its
/// centre with a 2° rotation settle in 220 ms (full motion; reduced: it fades in over Blink; off: at once). Removing the
/// favourite lifts it at once. Lies over the <see cref="CoverArt"/> sibling it reads its picture rect from (same parent,
/// same margin), so it moves with the box's lift.
/// </summary>
public sealed class InkStamp : Control
{
    public static readonly StyledProperty<bool> OnProperty = AvaloniaProperty.Register<InkStamp, bool>(nameof(On));

    /// <summary>The ink.</summary>
    public static readonly Color Ink = Color.Parse("#B8392C");

    /// <summary>Diameter of the stamp.</summary>
    public const double Size = 64;

    /// <summary>How long the landing takes.</summary>
    public static readonly TimeSpan LandDuration = TimeSpan.FromMilliseconds(220);

    private const double RestAngle = -9;

    private static readonly IImmutableSolidColorBrush InkBrush = new ImmutableSolidColorBrush(Ink);
    private static readonly IPen Outer = new ImmutablePen(InkBrush, 3.2, new ImmutableDashStyle([12, 0.35, 5, 0.25, 18, 0.45, 8, 0.3], 0));
    private static readonly IPen Inner = new ImmutablePen(InkBrush, 1.4);
    private static readonly Geometry Star = BuildStar(15, 6.4);

    private object? _context;
    private bool _known;
    private double _t = 1;
    private double _fade = 1;
    private Tween? _tween;

    static InkStamp()
    {
        AffectsRender<InkStamp>(OnProperty);
        IsHitTestVisibleProperty.OverrideDefaultValue<InkStamp>(false);
        ClipToBoundsProperty.OverrideDefaultValue<InkStamp>(false);
    }

    public bool On { get => GetValue(OnProperty); set => SetValue(OnProperty, value); }

    /// <summary>The landing's progress (1 = at rest; tests).</summary>
    public double Progress => _t;

    private static Geometry BuildStar(double outer, double inner)
    {
        var g = new StreamGeometry();
        using var c = g.Open();
        for (var i = 0; i < 10; i++)
        {
            var r = i % 2 == 0 ? outer : inner;
            var a = -Math.PI / 2 + i * Math.PI / 5;
            var p = new Point(r * Math.Cos(a), r * Math.Sin(a));
            if (i == 0) c.BeginFigure(p, true);
            else c.LineTo(p);
        }
        c.EndFigure(true);
        return g;
    }

    private CoverArt? _art;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _art = this.GetVisualParent() is Panel panel ? panel.Children.OfType<CoverArt>().FirstOrDefault() : null;
        if (_art != null) _art.ArtRectChanged += OnArtChanged;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_art != null) _art.ArtRectChanged -= OnArtChanged;
        _art = null;
    }

    private void OnArtChanged(object? sender, EventArgs e) => InvalidateVisual();

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        _context = DataContext;
        _known = true;
        // another game in the box: its stamp (if any) is simply there – whichever of the context and the On binding
        // updates first, nothing lands for the next 60 ms
        _quietUntil = DateTime.UtcNow.AddMilliseconds(60);
        _tween?.Stop();
        _t = 1;
        _fade = 1;
        InvalidateVisual();
    }

    private DateTime _quietUntil;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != OnProperty) return;
        var same = _known && ReferenceEquals(DataContext, _context) && DateTime.UtcNow >= _quietUntil;
        _context = DataContext;
        _known = true;
        _tween?.Stop();
        _t = 1;
        _fade = 1;
        if (!change.GetNewValue<bool>() || !same || !Motion.Enabled || TopLevel.GetTopLevel(this) == null) return;
        if (!Motion.AllowsMovement)
        {
            _fade = 0;
            _tween = Tween.Run(this, Motion.Blink, p => { _fade = p; InvalidateVisual(); }, () => { _fade = 1; InvalidateVisual(); });
            return;
        }
        _t = 0;
        _tween = Tween.Run(this, LandDuration, p => { _t = p; InvalidateVisual(); }, () => { _t = 1; InvalidateVisual(); });
    }

    public override void Render(DrawingContext ctx)
    {
        if (!On) return;
        var art = this.GetVisualParent() is Panel panel ? panel.Children.OfType<CoverArt>().FirstOrDefault() : null;
        var rect = art is { ArtRect.Width: > 0 } ? art.ArtRect : new Rect(Bounds.Size);
        if (rect.Width < 120 || rect.Height < 120) return;
        var center = new Point(rect.Right - Size / 2 - 14, rect.Bottom - Size / 2 - 16);
        // the landing: down from 1.3 in the first 70 %, the hand's twist settling 2° past rest and back
        var k = Ease.CubicOut(Math.Min(1, _t / 0.7));
        var scale = 1.3 - 0.3 * k;
        var angle = RestAngle + 2 * (1 - _t) * Math.Cos(Math.PI * _t);
        var opacity = 0.85 * _fade * Math.Min(1, 0.35 + _t / 0.3);
        var m = Matrix.CreateRotation(angle * Math.PI / 180) * Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(center.X, center.Y);
        using (ctx.PushOpacity(opacity))
        using (ctx.PushTransform(m))
        {
            var r = Size / 2 - 2;
            ctx.DrawEllipse(null, Outer, new Point(0, 0), r, r);
            ctx.DrawEllipse(null, Inner, new Point(0, 0), r - 6, r - 6);
            ctx.DrawGeometry(InkBrush, null, Star);
        }
    }
}
