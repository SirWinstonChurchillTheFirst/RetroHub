using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using RetroHub.App.Theming;

namespace RetroHub.App.Controls;

/// <summary>
/// The channel display ("Kanalanzeige", DESIGN 7.0): on a channel switch the channel digit (Zahl 40) and the name
/// (Rücken 28) appear top right in the current light colour – HARD on, 1200 ms, HARD off (no fade, no movement; also
/// under reduced motion). Lies outside the UI scaling; sizes are multiplied by <see cref="Scale"/> here.
/// At game start it shows the system code for 300 ms (DESIGN 9.3).
/// </summary>
public sealed class OsdLayer : Panel
{
    private readonly StackPanel _box;
    private readonly TextBlock _digit;
    private readonly TextBlock _name;
    private int _version;

    public OsdLayer()
    {
        IsHitTestVisible = false;
        _digit = new TextBlock { FontFamily = FontRoles.Number, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Bottom };
        _digit.FontFeatures = [new FontFeature { Tag = "tnum" }];
        _name = new TextBlock { FontFamily = FontRoles.Spine, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Bottom };
        _box = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Children = { _digit, _name },
            IsVisible = false,
        };
        Children.Add(_box);
        Scale = 1;
    }

    private double _scale = 1;
    private double _nameSize = 28;

    /// <summary>UI scale of the shell (the layer itself is not scaled).</summary>
    public double Scale
    {
        get => _scale;
        set
        {
            _scale = value;
            _digit.FontSize = 40 * value;
            _name.FontSize = _nameSize * value;
            _box.Spacing = 12 * value;
            _box.Margin = new Thickness(0, 104 * value, PageMargin * value, 0);
        }
    }

    /// <summary>Logical page margin (96 / 56) the display is right-aligned to.</summary>
    public double PageMargin { get; set; } = 96;

    public bool Enabled { get; set; } = true;

    public bool IsShowing => _box.IsVisible;

    /// <summary>Shows "<paramref name="digit"/> <paramref name="name"/>" for <paramref name="duration"/> (default 1200 ms).</summary>
    public void Show(string? digit, string name, Color light, TimeSpan? duration = null, double nameSize = 28)
    {
        if (!Enabled) return;
        _nameSize = nameSize;
        var brush = new ImmutableSolidColorBrush(light);
        _digit.Text = digit;
        _digit.IsVisible = !string.IsNullOrEmpty(digit);
        _digit.Foreground = brush;
        _name.Text = name;
        _name.Foreground = brush;
        Scale = _scale;
        _box.IsVisible = true;
        var version = ++_version;
        _ = Task.Delay(duration ?? Motion.Osd).ContinueWith(_ => Dispatcher.UIThread.Post(() =>
        {
            if (version == _version) _box.IsVisible = false;
        }), TaskScheduler.Default);
    }

    public void HideNow()
    {
        _version++;
        _box.IsVisible = false;
    }
}
