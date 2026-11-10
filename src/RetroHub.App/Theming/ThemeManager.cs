using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using RetroHub.Core.Logging;

namespace RetroHub.App.Theming;

/// <summary>A selectable light-colour preset (settings page swatches).</summary>
public sealed record AccentPreset(string Id, string NameKey, string Hex);

/// <summary>
/// Runtime theme tokens ("Fernsehlicht"). The light colour (Bernstein by default) replaces every light-dependent
/// resource in one go in Application.Resources (never per frame – see MonHub PERFORMANCE.md); all consumers use
/// DynamicResource. Everything is a solid colour: no gradients, no glow.
/// <para>
/// <see cref="BoardLight"/> is the ONE shared SolidColorBrush on the top edge of every shelf board (DESIGN 2.5.6). It is
/// never replaced, only re-coloured (<see cref="SetBoardLight"/>), so a change costs one colour and animates through
/// its ColorTransition.
/// </para>
/// </summary>
public static class ThemeManager
{
    /// <summary>Standby-Bernstein, the only brand colour (DESIGN 2.2).</summary>
    public const string DefaultAccent = "#FFB547";

    public static IReadOnlyList<AccentPreset> Presets { get; } =
    [
        new("amber", "accent.amber", "#FFB547"),
        new("phosphor", "accent.phosphor", "#8FE3A0"),
        new("tube", "accent.tube", "#EDE3D0"),
        new("ice", "accent.ice", "#8CC4FF"),
        new("cherry", "accent.cherry", "#FF8A80"),
        new("neon", "accent.neon", "#FF8CC6"),
    ];

    public static Color CurrentAccent { get; private set; } = Color.Parse(DefaultAccent);

    public static event Action? AccentChanged;

    /// <summary>Neutral board light outside the TV-light and system contexts (= light edge #17FFF3E0).</summary>
    public static readonly Color NeutralBoardLight = Color.Parse("#17FFF3E0");

    private static SolidColorBrush? _boardLight;

    /// <summary>The shared board-light brush (created on first use, registered as RhBoardLightBrush).</summary>
    public static SolidColorBrush BoardLight
    {
        get
        {
            if (_boardLight != null) return _boardLight;
            _boardLight = new SolidColorBrush(NeutralBoardLight);
            ApplyBoardLightTransition();
            return _boardLight;
        }
    }

    /// <summary>Re-colours the shared board light (animated by its ColorTransition, 420 ms, hard when motion is off).</summary>
    public static void SetBoardLight(Color color)
    {
        if (BoardLight.Color == color) return;
        BoardLight.Color = color;
    }

    /// <summary>Board light for a context: TV light 35 % (Start/Detail), light colour 22 % (library/system), else neutral.</summary>
    public static Color BoardLightFor(Color? light, double share) =>
        light is { } l ? ColorUtil.WithAlpha(l, (byte)Math.Round(255 * share)) : NeutralBoardLight;

    /// <summary>Re-reads the motion level for the board-light transition.</summary>
    public static void ApplyBoardLightTransition()
    {
        if (_boardLight == null) return;
        var d = Motion.Light;
        _boardLight.Transitions = d == TimeSpan.Zero ? null :
            [new ColorTransition { Property = SolidColorBrush.ColorProperty, Duration = d, Easing = new SineEaseInOut() }];
    }

    /// <summary>
    /// Applies a light colour (#RRGGBB; null / unknown = Bernstein). Only the presets are accepted: values of the old
    /// "Nova" accent palette (violet, blue …) are not part of the warm palette and fall back to Bernstein.
    /// </summary>
    public static void ApplyAccent(string? hex)
    {
        var app = Application.Current;
        if (app == null) return;
        var a = Resolve(hex);
        CurrentAccent = a;
        var r = app.Resources;

        r["RhAccentBrush"] = new SolidColorBrush(a);
        r["RhAccentPressedBrush"] = new SolidColorBrush(ColorUtil.Mix(a, Colors.Black, 0.12));
        r["RhAccentSoftBrush"] = new SolidColorBrush(ColorUtil.WithAlpha(a, 0x33));
        r["RhAccentTextBrush"] = new SolidColorBrush(ColorUtil.Mix(a, Colors.White, 0.22));
        r["RhBoardLightBrush"] = BoardLight;

        // Fluent controls (selection, progress, slider) follow the light colour too.
        r["SystemAccentColor"] = a;
        r["SystemAccentColorLight1"] = ColorUtil.Shift(a, dLight: 0.06);
        r["SystemAccentColorLight2"] = ColorUtil.Shift(a, dLight: 0.12);
        r["SystemAccentColorLight3"] = ColorUtil.Shift(a, dLight: 0.18);
        r["SystemAccentColorDark1"] = ColorUtil.Shift(a, dLight: -0.08);
        r["SystemAccentColorDark2"] = ColorUtil.Shift(a, dLight: -0.16);
        r["SystemAccentColorDark3"] = ColorUtil.Shift(a, dLight: -0.24);
        foreach (var style in app.Styles)
        {
            if (style is FluentTheme fluent && fluent.Palettes.TryGetValue(ThemeVariant.Dark, out var palette))
            {
                palette.Accent = a;
            }
        }
        try { AccentChanged?.Invoke(); } catch (Exception ex) { Log.Warn("AccentChanged handler failed", ex); }
    }

    /// <summary>The light colour for a stored setting value (presets only; everything else = Bernstein).</summary>
    public static Color Resolve(string? hex)
    {
        var preset = Presets.FirstOrDefault(p => string.Equals(p.Hex, hex?.Trim(), StringComparison.OrdinalIgnoreCase));
        return Color.Parse(preset?.Hex ?? DefaultAccent);
    }

    /// <summary>Linear two-stop gradient (only for scrims and shadows – never as decoration).</summary>
    public static IBrush Gradient(Color a, Color b, RelativePoint start, RelativePoint end) => new LinearGradientBrush
    {
        StartPoint = start,
        EndPoint = end,
        GradientStops = { new GradientStop(a, 0), new GradientStop(b, 1) },
    };

    public static IBrush Radial(Color c, RelativePoint center, double radius) => new RadialGradientBrush
    {
        Center = center,
        GradientOrigin = center,
        RadiusX = new RelativeScalar(radius, RelativeUnit.Relative),
        RadiusY = new RelativeScalar(radius, RelativeUnit.Relative),
        GradientStops = { new GradientStop(c, 0), new GradientStop(ColorUtil.WithAlpha(c, 0), 1) },
    };
}
