using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace RetroHub.App.Controls;

/// <summary>
/// The segment bar (DESIGN 7.6 Importe, 9.3 start overlay): 14 segments 24 × 10 with 4 px gaps, lit ones in the light
/// colour (Bernstein), the others Regal hell – like the level meter of a tape deck, never a rounded progress pill.
/// <see cref="IsIndeterminate"/>: three lit segments wander one step every 80 ms (one timer, only while visible).
/// Under motion "Aus" the wandering stops and the middle three stay lit.
/// </summary>
public sealed class SegmentBar : Control
{
    public const int Segments = 14;
    public const double SegmentWidth = 24, SegmentHeight = 10, Gap = 4;
    public static readonly TimeSpan StepInterval = TimeSpan.FromMilliseconds(80);

    public static readonly StyledProperty<double> ValueProperty = AvaloniaProperty.Register<SegmentBar, double>(nameof(Value));
    public static readonly StyledProperty<bool> IsIndeterminateProperty = AvaloniaProperty.Register<SegmentBar, bool>(nameof(IsIndeterminate));
    public static readonly StyledProperty<IBrush?> LitProperty = AvaloniaProperty.Register<SegmentBar, IBrush?>(nameof(Lit));
    public static readonly StyledProperty<IBrush?> UnlitProperty = AvaloniaProperty.Register<SegmentBar, IBrush?>(nameof(Unlit));

    private readonly DispatcherTimer _timer;
    private int _step;

    static SegmentBar()
    {
        AffectsRender<SegmentBar>(ValueProperty, IsIndeterminateProperty, LitProperty, UnlitProperty);
    }

    public SegmentBar()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = StepInterval };
        _timer.Tick += (_, _) =>
        {
            _step = (_step + 1) % (Segments + 3);
            InvalidateVisual();
        };
        Bind(LitProperty, this.GetResourceObservable("RhAccentBrush"));
        Bind(UnlitProperty, this.GetResourceObservable("RhSurfaceHiBrush"));
    }

    /// <summary>0–100.</summary>
    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public bool IsIndeterminate { get => GetValue(IsIndeterminateProperty); set => SetValue(IsIndeterminateProperty, value); }
    public IBrush? Lit { get => GetValue(LitProperty); set => SetValue(LitProperty, value); }
    public IBrush? Unlit { get => GetValue(UnlitProperty); set => SetValue(UnlitProperty, value); }

    /// <summary>Lit segments for a value: the bar rounds down, so 100 % only when it is done.</summary>
    public static int LitCount(double value) => (int)Math.Floor(Math.Clamp(value, 0, 100) / 100.0 * Segments + 1e-9);

    /// <summary>Is segment <paramref name="i"/> lit at wander step <paramref name="step"/> (three segments entering from the left)?</summary>
    public static bool WanderLit(int i, int step) => i <= step - 1 && i >= step - 3;

    protected override Size MeasureOverride(Size availableSize) => new(Segments * SegmentWidth + (Segments - 1) * Gap, SegmentHeight);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsIndeterminateProperty || change.Property == IsVisibleProperty) UpdateTimer();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        UpdateTimer();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _timer.Stop();
    }

    private void UpdateTimer()
    {
        var run = IsIndeterminate && IsEffectivelyVisible && TopLevel.GetTopLevel(this) != null && Theming.Motion.Enabled;
        if (run && !_timer.IsEnabled) _timer.Start();
        else if (!run) _timer.Stop();
    }

    public override void Render(DrawingContext context)
    {
        var lit = Lit ?? Brushes.Orange;
        var unlit = Unlit ?? Brushes.DimGray;
        var count = LitCount(Value);
        var still = !Theming.Motion.Enabled;
        for (var i = 0; i < Segments; i++)
        {
            bool on = IsIndeterminate
                ? (still ? i is >= Segments / 2 - 1 and <= Segments / 2 + 1 : WanderLit(i, _step))
                : i < count;
            Lamp.Draw(context, new Rect(i * (SegmentWidth + Gap), 0, SegmentWidth, SegmentHeight), on ? lit : unlit);
        }
    }
}
