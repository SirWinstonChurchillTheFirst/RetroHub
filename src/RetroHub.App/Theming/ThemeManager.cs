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
/// Runtime theme tokens ("Fernsehlicht" v2, "Schwarz + Weiß + Cyan"). The light colour (Cyan by default) replaces every light-dependent
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
    /// <summary>Standby-Cyan, the only brand colour (DESIGN 2.2, v2 – the owner's "Schwarz + Weiß + Cyan").</summary>
    public const string DefaultAccent = "#2FD3F5";

    /// <summary>The cool light colours (v2): no warm preset is left – Bernstein and the warm whites are gone.</summary>
    public static IReadOnlyList<AccentPreset> Presets { get; } =
    [
        new("cyan", "accent.cyan", "#2FD3F5"),
        new("white", "accent.white", "#F2F5F8"),
        new("signal", "accent.signal", "#FF808B"),
        new("green", "accent.green", "#3DDC84"),
    ];

    /// <summary>
    /// v1 light colours (stored as #RRGGBB in settings.json) → their v2 successor; null = the default (Cyan). Bernstein
    /// and every other value outside the v2 presets lands on Cyan, the near ones on their cool neighbour.
    /// </summary>
    public static string? MigrateStored(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        var h = hex.Trim().ToUpperInvariant();
        if (Presets.Any(p => string.Equals(p.Hex, h, StringComparison.OrdinalIgnoreCase)))
            return string.Equals(h, DefaultAccent, StringComparison.OrdinalIgnoreCase) ? null : h;
        return h switch
        {
            "#8FE3A0" => "#3DDC84",           // Phosphor → Grün
            "#EDE3D0" => "#F2F5F8",           // Röhrenweiß → Weiß
            "#FF8A80" or "#FF8CC6" => "#FF808B", // Kirsche, Neonrosa → Signalrot
            _ => null,                        // Bernstein (FFB547), Eisblau, Nova leftovers → Cyan
        };
    }

    public static Color CurrentAccent { get; private set; } = Color.Parse(DefaultAccent);

    public static event Action? AccentChanged;

    /// <summary>Neutral board light outside the TV-light and system contexts (= light edge #17FFFFFF).</summary>
    public static readonly Color NeutralBoardLight = Color.Parse("#17FFFFFF");

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
    /// Applies a light colour (#RRGGBB; null / unknown = Cyan). Only the presets are accepted: everything else falls
    /// back to Cyan (stored v1 values are migrated first, <see cref="MigrateStored"/>).
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

    /// <summary>The light colour for a stored setting value (presets only; everything else = Cyan).</summary>
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
