using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using RetroHub.App.ViewModels;

namespace RetroHub.App.Controls;

/// <summary>
/// A row of the service menu (DESIGN 7.6, theme <c>RhServiceRow</c> in Themes/Controls.axaml; DataContext =
/// <see cref="ServiceSetting"/>). It is a button, so confirm and a click go through the normal invoke path
/// (<see cref="ServiceSetting.Activate"/>: an action runs, a value moves on). Left/right are routed here by the page
/// (<see cref="Step"/>); the mouse clicks the two arrows (<c>PART_Previous</c>, <c>PART_Next</c>), which never start
/// the row's own click. There are no toggle switches, sliders or segment controls in RetroHub.
/// </summary>
public class ServiceRow : Button
{
    private Control? _previous, _next, _value;

    protected override Type StyleKeyOverride => typeof(ServiceRow);

    public ServiceSetting? Setting => DataContext as ServiceSetting;

    /// <summary>Left (-1) / right (+1) from the pad or keyboard: the value changes at once. True when it changed.</summary>
    public bool Step(int direction)
    {
        if (Setting is not { IsChoice: true } s) return false;
        var changed = s.Step(direction);
        if (!changed) Nudge(direction);
        return changed;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        Avalonia.Automation.AutomationProperties.SetName(this, Setting?.Label);
    }

    protected override void OnClick()
    {
        base.OnClick();
        Setting?.Activate();
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        if (_previous != null) _previous.PointerPressed -= OnPreviousPressed;
        if (_next != null) _next.PointerPressed -= OnNextPressed;
        _previous = e.NameScope.Find<Control>("PART_Previous");
        _next = e.NameScope.Find<Control>("PART_Next");
        _value = e.NameScope.Find<Control>("PART_Value");
        _columns = e.NameScope.Find<Grid>("PART_Columns");
        _hint = e.NameScope.Find<TextBlock>("PART_Hint");
        if (_previous != null) _previous.PointerPressed += OnPreviousPressed;
        if (_next != null) _next.PointerPressed += OnNextPressed;
        _narrow = null;
        ApplyWidth(Bounds.Width);
    }

    private Grid? _columns;
    private TextBlock? _hint;
    private bool? _narrow;

    /// <summary>
    /// Below this row width (Importe at ≈ 1100: a 787 px list beside the scan column) the value column narrows to 292
    /// (window 220) and the description wraps onto a second line (the row grows to ≈ 88) instead of being cut after
    /// ≈ 270 px (QA 2).
    /// </summary>
    public const double NarrowBelow = 900;

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (e.WidthChanged) ApplyWidth(e.NewSize.Width);
    }

    private void ApplyWidth(double width)
    {
        if (width <= 0 || _columns == null) return;
        var narrow = width < NarrowBelow;
        if (_narrow == narrow) return;
        _narrow = narrow;
        if (_columns.ColumnDefinitions.Count > 2) _columns.ColumnDefinitions[2].Width = new GridLength(narrow ? 292 : 392);
        if (_value is Grid g && g.ColumnDefinitions.Count > 1) g.ColumnDefinitions[1].Width = new GridLength(narrow ? 220 : 320);
        if (_hint != null)
        {
            _hint.TextWrapping = narrow ? Avalonia.Media.TextWrapping.Wrap : Avalonia.Media.TextWrapping.NoWrap;
            _hint.MaxLines = narrow ? 2 : 1;
        }
    }

    private void OnPreviousPressed(object? sender, PointerPressedEventArgs e) => OnArrow(e, -1);
    private void OnNextPressed(object? sender, PointerPressedEventArgs e) => OnArrow(e, +1);

    private void OnArrow(PointerPressedEventArgs e, int direction)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || Setting is not { IsChoice: true }) return;
        // the arrow is not the row: no pressed state and no click of the button (that would move on once more)
        e.Handled = true;
        Focus(NavigationMethod.Pointer);
        Step(direction);
        // the press shows on the arrow itself (R2): accent fill and pressed in for 90 ms
        if ((direction < 0 ? _previous : _next) is { } arrow)
        {
            arrow.Classes.Add("pressed");
            Avalonia.Threading.DispatcherTimer.RunOnce(() => arrow.Classes.Remove("pressed"), TimeSpan.FromMilliseconds(90));
        }
    }

    /// <summary>The end of an ordered row: a short push against it (DESIGN 8.4 Grenz-Feedback), as on the shelves.</summary>
    private void Nudge(int direction)
    {
        if (!Theming.Motion.AllowsMovement) return;
        var target = _value;
        if (target == null) return;
        var t = new Avalonia.Media.TranslateTransform();
        target.RenderTransform = t;
        var started = DateTime.UtcNow;
        var timer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) =>
        {
            var ms = (DateTime.UtcNow - started).TotalMilliseconds;
            // 0 → −6 → +4 → 0 in 180 ms (mirrored for the left end)
            double x = ms switch
            {
                < 60 => -6 * ms / 60,
                < 120 => -6 + 10 * (ms - 60) / 60,
                < 180 => 4 - 4 * (ms - 120) / 60,
                _ => 0,
            };
            t.X = direction > 0 ? -x : x;
            if (ms >= 180) timer.Stop();
        };
        timer.Start();
    }
}

/// <summary>
/// A container that folds its child open and shut (DESIGN 7.6: an emulator row opens in 180 ms at every width): it
/// measures the child fully and reports <see cref="Openness"/> × its height, clipped. Only a double is animated
/// (<see cref="Transitions"/> set by the owner); nothing is re-templated while it moves.
/// </summary>
public sealed class FoldPanel : Decorator
{
    public static readonly StyledProperty<double> OpennessProperty =
        AvaloniaProperty.Register<FoldPanel, double>(nameof(Openness));

    public static readonly StyledProperty<bool> IsOpenProperty =
        AvaloniaProperty.Register<FoldPanel, bool>(nameof(IsOpen));

    /// <summary>Fold time (DESIGN 7.6: 180 ms); reduced/off motion: none (it jumps).</summary>
    public static readonly TimeSpan FoldDuration = TimeSpan.FromMilliseconds(180);

    static FoldPanel()
    {
        ClipToBoundsProperty.OverrideDefaultValue<FoldPanel>(true);
        AffectsMeasure<FoldPanel>(OpennessProperty);
    }

    public double Openness { get => GetValue(OpennessProperty); set => SetValue(OpennessProperty, value); }
    public bool IsOpen { get => GetValue(IsOpenProperty); set => SetValue(IsOpenProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsOpenProperty)
        {
            var open = change.GetNewValue<bool>();
            Transitions = Theming.Motion.AllowsMovement && IsAttachedToVisualTree()
                ? [new Avalonia.Animation.DoubleTransition { Property = OpennessProperty, Duration = FoldDuration, Easing = new Avalonia.Animation.Easings.CubicEaseOut() }]
                : null;
            Openness = open ? 1 : 0;
            // a shut fold keeps nothing focusable inside it
            if (Child != null) Child.IsVisible = open || Openness > 0;
        }
        else if (change.Property == OpennessProperty && Child != null)
        {
            Child.IsVisible = IsOpen || change.GetNewValue<double>() > 0.001;
        }
        else if (change.Property == ChildProperty && Child != null)
        {
            Child.IsVisible = IsOpen || Openness > 0.001;
        }
    }

    private bool IsAttachedToVisualTree() => TopLevel.GetTopLevel(this) != null;

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Child == null) return default;
        Child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
        var d = Child.DesiredSize;
        return new Size(d.Width, Math.Round(d.Height * Math.Clamp(Openness, 0, 1)));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Child == null) return finalSize;
        Child.Arrange(new Rect(0, 0, finalSize.Width, Math.Max(finalSize.Height, Child.DesiredSize.Height)));
        return finalSize;
    }
}
