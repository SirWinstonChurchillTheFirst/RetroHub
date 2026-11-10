using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using RetroHub.App.Theming;
using RetroHub.App.ViewModels;

namespace RetroHub.App.Controls;

/// <summary>
/// The Konsolen wall's position rail (DESIGN 7.4.1, M5 QA – replaces the text ticker, which listed the codes of the
/// spines right above it a second time): a 2 px line across the width with one 12 × 4 tick per system in its band colour;
/// the current system's tick is 24 × 6 with its code above it in paper (code role 17). Not focusable – the wall is the
/// control; a click on a tick raises <see cref="TickClicked"/> (the page turns that front forward), a drag scrubs along
/// the ticks. Hover (R2): the tick under the pointer grows to the current size with its code in Text 2 above it, and the
/// tooltip names the system. Drawn in one pass; text layouts are built when the current/hovered system changes.
/// </summary>
public sealed class PositionRail : Control, Avalonia.Rendering.ICustomHitTest
{
    /// <summary>The whole strip takes the pointer (the ticks are small).</summary>
    public bool HitTest(Point point) => new Rect(Bounds.Size).Contains(point);

    public static readonly StyledProperty<IReadOnlyList<SystemItemViewModel>?> SystemsProperty =
        AvaloniaProperty.Register<PositionRail, IReadOnlyList<SystemItemViewModel>?>(nameof(Systems));
    public static readonly StyledProperty<SystemItemViewModel?> CurrentProperty =
        AvaloniaProperty.Register<PositionRail, SystemItemViewModel?>(nameof(Current));

    private static readonly IImmutableSolidColorBrush Line = new ImmutableSolidColorBrush(Color.Parse("#3A342B"));
    private static readonly IImmutableSolidColorBrush Paper = new ImmutableSolidColorBrush(SystemPalette.Paper);

    /// <summary>Distance of the line from the bottom edge.</summary>
    private const double LineFromBottom = 12;
    private const double Tick = 12, TickHeight = 4, TickCurrent = 24, TickCurrentHeight = 6;

    private FormattedText? _code, _hoverCode;
    private int _hover = -1, _scrubbed = -1;
    private bool _dragging;
    private static readonly IImmutableSolidColorBrush Text2 = new ImmutableSolidColorBrush(Color.Parse("#C9BFAF"));

    static PositionRail()
    {
        AffectsRender<PositionRail>(SystemsProperty, CurrentProperty, BoundsProperty);
        FocusableProperty.OverrideDefaultValue<PositionRail>(false);
    }

    public PositionRail()
    {
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.Antialias);
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    public IReadOnlyList<SystemItemViewModel>? Systems { get => GetValue(SystemsProperty); set => SetValue(SystemsProperty, value); }
    public SystemItemViewModel? Current { get => GetValue(CurrentProperty); set => SetValue(CurrentProperty, value); }

    /// <summary>A tick was clicked: the index of its system in <see cref="Systems"/>.</summary>
    public event Action<int>? TickClicked;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == CurrentProperty) _code = null;
        if (change.Property == SystemsProperty) SetHover(-1);
    }

    /// <summary>Centre x of tick <paramref name="i"/> of <paramref name="n"/> (evenly over the width, ends inset by half a current tick).</summary>
    private double TickX(int i, int n)
    {
        var w = Bounds.Width;
        if (n <= 1) return TickCurrent / 2;
        return Math.Round(TickCurrent / 2 + i * (w - TickCurrent) / (n - 1));
    }

    public override void Render(DrawingContext ctx)
    {
        var systems = Systems;
        var w = Bounds.Width;
        if (systems == null || systems.Count == 0 || w <= 0) return;
        var lineY = Math.Round(Bounds.Height - LineFromBottom);
        ctx.FillRectangle(Line, new Rect(0, lineY - 1, w, 2));
        var n = systems.Count;
        var current = -1;
        for (var i = 0; i < n; i++)
        {
            if (ReferenceEquals(systems[i], Current)) { current = i; continue; }
            var x = TickX(i, n);
            ctx.FillRectangle(systems[i].Look.ColorBrush, new Rect(x - Tick / 2, lineY - TickHeight / 2, Tick, TickHeight));
        }
        if (_hover >= 0 && _hover < n && _hover != current)
        {
            var h = systems[_hover];
            var hx = TickX(_hover, n);
            ctx.FillRectangle(h.Look.ColorBrush, new Rect(hx - TickCurrent / 2, lineY - TickCurrentHeight / 2, TickCurrent, TickCurrentHeight));
            _hoverCode ??= new FormattedText(h.ShortName, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, FontRoles.SpineFace, 17, Text2);
            var tx0 = Math.Clamp(Math.Round(hx - _hoverCode.Width / 2), 0, Math.Max(0, w - _hoverCode.Width));
            ctx.DrawText(_hoverCode, new Point(tx0, Math.Round(lineY - TickCurrentHeight / 2 - 6 - _hoverCode.Height)));
        }
        if (current < 0) return;
        var c = systems[current];
        var cx = TickX(current, n);
        ctx.FillRectangle(c.Look.ColorBrush, new Rect(cx - TickCurrent / 2, lineY - TickCurrentHeight / 2, TickCurrent, TickCurrentHeight));
        _code ??= new FormattedText(c.ShortName, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, FontRoles.SpineFace, 17, Paper);
        var tx = Math.Clamp(Math.Round(cx - _code.Width / 2), 0, Math.Max(0, w - _code.Width));
        ctx.DrawText(_code, new Point(tx, Math.Round(lineY - TickCurrentHeight / 2 - 6 - _code.Height)));
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
