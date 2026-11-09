using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using RetroHub.Core.Logging;

namespace RetroHub.App.Theming;

/// <summary>A selectable accent preset (settings page swatches).</summary>
public sealed record AccentPreset(string Id, string NameKey, string Hex);

/// <summary>
/// Runtime theme tokens. Every accent-dependent resource is replaced in one go in Application.Resources
/// (never per frame – see MonHub PERFORMANCE.md); all consumers use DynamicResource.
/// </summary>
public static class ThemeManager
{
    public const string DefaultAccent = "#7B61FF";

    public static IReadOnlyList<AccentPreset> Presets { get; } =
    [
        new("nova", "accent.nova", "#7B61FF"),
        new("ocean", "accent.ocean", "#2F9BFF"),
        new("aurora", "accent.aurora", "#1FC7A2"),
        new("sunset", "accent.sunset", "#FF7A45"),
        new("rose", "accent.rose", "#F2558F"),
        new("gold", "accent.gold", "#E8B030"),
    ];

    public static Color CurrentAccent { get; private set; } = Color.Parse(DefaultAccent);

    public static event Action? AccentChanged;

    /// <summary>Applies an accent (#RRGGBB; null/invalid = Nova default).</summary>
    public static void ApplyAccent(string? hex)
    {
        var app = Application.Current;
        if (app == null) return;
        var a = ColorUtil.Parse(hex, Color.Parse(DefaultAccent));
        a = Color.FromRgb(a.R, a.G, a.B);
        var b = hex == null || string.Equals(hex, DefaultAccent, StringComparison.OrdinalIgnoreCase)
            ? Color.Parse("#4C8DFF") // Nova's own partner colour
            : ColorUtil.GradientPartner(a);
        CurrentAccent = a;
        var r = app.Resources;

        r["RhAccentAColor"] = a;
        r["RhAccentBColor"] = b;
        r["RhAccentSoftColor"] = ColorUtil.WithAlpha(a, 0x2E);
        r["RhAccentBrush"] = new SolidColorBrush(a);
        r["RhAccentBBrush"] = new SolidColorBrush(b);
        r["RhAccentSoftBrush"] = new SolidColorBrush(ColorUtil.WithAlpha(a, 0x2E));
        r["RhAccentFaintBrush"] = new SolidColorBrush(ColorUtil.WithAlpha(a, 0x18));
        r["RhAccentTextBrush"] = new SolidColorBrush(ColorUtil.Mix(a, Colors.White, 0.35));
        r["RhAccentGradientBrush"] = Gradient(a, b, new RelativePoint(0, 0.5, RelativeUnit.Relative), new RelativePoint(1, 0.5, RelativeUnit.Relative));
        r["RhAccentGradientDiagBrush"] = Gradient(a, b, new RelativePoint(0, 0, RelativeUnit.Relative), new RelativePoint(1, 1, RelativeUnit.Relative));
        r["RhFocusRingBrush"] = Gradient(ColorUtil.Mix(a, Colors.White, 0.15), ColorUtil.Mix(b, Colors.White, 0.15),
            new RelativePoint(0, 0, RelativeUnit.Relative), new RelativePoint(1, 1, RelativeUnit.Relative));
        r["RhFocusGlow"] = BoxShadows.Parse($"0 0 0 1 {Hex(ColorUtil.WithAlpha(a, 0x55))}, 0 14 36 -4 {Hex(ColorUtil.WithAlpha(a, 0x80))}");
        r["RhButtonGlow"] = BoxShadows.Parse($"0 8 24 -6 {Hex(ColorUtil.WithAlpha(a, 0xA0))}");
        r["RhAmbientBrushA"] = Radial(ColorUtil.WithAlpha(a, 0x3A), new RelativePoint(0.12, 0.0, RelativeUnit.Relative), 0.75);
        r["RhAmbientBrushB"] = Radial(ColorUtil.WithAlpha(b, 0x26), new RelativePoint(0.95, 1.0, RelativeUnit.Relative), 0.7);

        // Fluent controls (ToggleSwitch, Slider, ProgressBar, ComboBox selection …) follow the accent too.
        r["SystemAccentColor"] = a;
        r["SystemAccentColorLight1"] = ColorUtil.Shift(a, dLight: 0.08);
        r["SystemAccentColorLight2"] = ColorUtil.Shift(a, dLight: 0.16);
        r["SystemAccentColorLight3"] = ColorUtil.Shift(a, dLight: 0.24);
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

    private static string Hex(Color c) => $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
}
