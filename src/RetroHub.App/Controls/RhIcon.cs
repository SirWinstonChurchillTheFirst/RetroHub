using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace RetroHub.App.Controls;

/// <summary>
/// Vector icon drawn from a <see cref="Geometry"/> authored on a 24×24 grid ("Gefaste Linie"; no icon fonts).
/// Scales to its size; colour follows the inherited Foreground (TextElement.Foreground) unless set.
/// </summary>
public sealed class RhIcon : Control
{
    public static readonly StyledProperty<Geometry?> DataProperty =
        AvaloniaProperty.Register<RhIcon, Geometry?>(nameof(Data));

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<RhIcon>();

    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<RhIcon, double>(nameof(StrokeThickness), 2.0);

    /// <summary>Fill the geometry instead of stroking it (play triangle, filled star).</summary>
    public static readonly StyledProperty<bool> FilledProperty =
        AvaloniaProperty.Register<RhIcon, bool>(nameof(Filled));

    /// <summary>
    /// The favourite stamp (R2 animation): when this icon comes into view by a toggle of the SAME subject (not because the
    /// page shows another game), a filled icon is stamped – 1 → 1.28 → 1 in 200 ms (back-out) while its fill grows from
    /// the centre (Step) – and an outline icon gives way (1 → 0.85 → 1 in 140 ms). Reduced: the colour changes in Blink
    /// (the icon fades in); off: at once.
    /// </summary>
    public static readonly StyledProperty<bool> StampProperty =
        AvaloniaProperty.Register<RhIcon, bool>(nameof(Stamp));

    public bool Stamp { get => GetValue(StampProperty); set => SetValue(StampProperty, value); }

    private object? _stampContext;
    private bool _stampKnown;
    private double _scale = 1, _fill = 1;
    private Tween? _stamp;

    /// <summary>The stamp's scale shown now (tests).</summary>
    public double StampScale => _scale;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (!Stamp || change.Property != IsVisibleProperty) return;
        var same = _stampKnown && ReferenceEquals(DataContext, _stampContext);
        _stampContext = DataContext;
        _stampKnown = true;
        if (change.GetNewValue<bool>() && same) PlayStamp();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        _stampContext = DataContext;
        _stampKnown = true;
    }

    private void PlayStamp()
    {
        _stamp?.Stop();
        if (!Theming.Motion.Enabled) return;
        if (!Theming.Motion.AllowsMovement)
        {
            _stamp = Tween.Run(this, Theming.Motion.Blink, p => Opacity = p, () => Opacity = 1);
            return;
        }
        if (Filled)
            _stamp = Tween.Run(this, TimeSpan.FromMilliseconds(200), p =>
            {
                // up to 1.28 at 40 %, back with a little overshoot below the rest size
                _scale = p < 0.4 ? Ease.Lerp(1, 1.28, Ease.CubicOut(p / 0.4)) : Ease.Lerp(1.28, 1, Ease.BackOut((p - 0.4) / 0.6, 1.2));
                _fill = Ease.CubicOut(Math.Min(1, p * 200 / Theming.Motion.StepDuration.TotalMilliseconds));
                InvalidateVisual();
            }, () => { _scale = 1; _fill = 1; InvalidateVisual(); });
        else
            _stamp = Tween.Run(this, Theming.Motion.StepDuration, p =>
            {
                _scale = 1 - 0.15 * Ease.Bump(p);
                InvalidateVisual();
            }, () => { _scale = 1; InvalidateVisual(); });
    }

    /// <summary>The icon ticks once (a message arriving, R2 animation): 0.8 → 1.1 → 1 in 160 ms; full motion only.</summary>
    public void Pop()
    {
        if (!Theming.Motion.AllowsMovement) return;
        _stamp?.Stop();
        _stamp = Tween.Run(this, TimeSpan.FromMilliseconds(160), p =>
        {
            _scale = p < 0.55 ? Ease.Lerp(0.8, 1.1, Ease.CubicOut(p / 0.55)) : Ease.Lerp(1.1, 1, Ease.SineInOut((p - 0.55) / 0.45));
            InvalidateVisual();
        }, () => { _scale = 1; InvalidateVisual(); });
    }

    static RhIcon()
    {
        AffectsRender<RhIcon>(DataProperty, ForegroundProperty, StrokeThicknessProperty, FilledProperty);
        WidthProperty.OverrideDefaultValue<RhIcon>(20);
        HeightProperty.OverrideDefaultValue<RhIcon>(20);
        IsHitTestVisibleProperty.OverrideDefaultValue<RhIcon>(false);
    }

    public Geometry? Data { get => GetValue(DataProperty); set => SetValue(DataProperty, value); }
    public IBrush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    public double StrokeThickness { get => GetValue(StrokeThicknessProperty); set => SetValue(StrokeThicknessProperty, value); }
    public bool Filled { get => GetValue(FilledProperty); set => SetValue(FilledProperty, value); }

    public override void Render(DrawingContext context)
    {
        var data = Data;
        var brush = Foreground;
        if (data == null || brush == null || Bounds.Width <= 0 || Bounds.Height <= 0) return;
        var scale = Math.Min(Bounds.Width, Bounds.Height) / 24.0;
        var dx = (Bounds.Width - 24 * scale) / 2;
        var dy = (Bounds.Height - 24 * scale) / 2;
        // the stamp scales the icon about its centre
        var stamp = Math.Abs(_scale - 1) > 0.001
            ? Matrix.CreateTranslation(-12, -12) * Matrix.CreateScale(_scale, _scale) * Matrix.CreateTranslation(12, 12)
            : Matrix.Identity;
        using (context.PushTransform(stamp * Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(dx, dy)))
        {
            // "Gefaste Linie": square caps, mitre joins (DESIGN 5.1)
            if (Filled && _fill < 0.999)
            {
                // being stamped: the outline at once, the fill growing from the centre
                context.DrawGeometry(null, new Pen(brush, StrokeThickness, lineCap: PenLineCap.Square, lineJoin: PenLineJoin.Miter), data);
                using (context.PushTransform(Matrix.CreateTranslation(-12, -12) * Matrix.CreateScale(_fill, _fill) * Matrix.CreateTranslation(12, 12)))
                    context.DrawGeometry(brush, new Pen(brush, 1.0, lineJoin: PenLineJoin.Miter), data);
            }
            else if (Filled)
                context.DrawGeometry(brush, new Pen(brush, 1.0, lineJoin: PenLineJoin.Miter), data);
            else
                context.DrawGeometry(null, new Pen(brush, StrokeThickness, lineCap: PenLineCap.Square, lineJoin: PenLineJoin.Miter), data);
        }
    }
}
