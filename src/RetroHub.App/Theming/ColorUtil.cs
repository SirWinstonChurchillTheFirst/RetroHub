using Avalonia.Media;

namespace RetroHub.App.Theming;

/// <summary>Small colour helpers (HSL conversion, mixing) for accent derivation and placeholder art.</summary>
public static class ColorUtil
{
    public static Color Parse(string? hex, Color fallback)
    {
        if (!string.IsNullOrWhiteSpace(hex) && Color.TryParse(hex.Trim(), out var c)) return c;
        return fallback;
    }

    public static (double H, double S, double L) ToHsl(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        double h = 0, s, l = (max + min) / 2;
        var d = max - min;
        if (d < 1e-9) s = 0;
        else
        {
            s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
            if (max == r) h = (g - b) / d + (g < b ? 6 : 0);
            else if (max == g) h = (b - r) / d + 2;
            else h = (r - g) / d + 4;
            h *= 60;
        }
        return (h, s, l);
    }

    public static Color FromHsl(double h, double s, double l, byte a = 255)
    {
        h = ((h % 360) + 360) % 360;
        s = Math.Clamp(s, 0, 1);
        l = Math.Clamp(l, 0, 1);
        double c = (1 - Math.Abs(2 * l - 1)) * s;
        double x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        double m = l - c / 2;
        (double r, double g, double b) = h switch
        {
            < 60 => (c, x, 0d),
            < 120 => (x, c, 0d),
            < 180 => (0d, c, x),
            < 240 => (0d, x, c),
            < 300 => (x, 0d, c),
            _ => (c, 0d, x),
        };
        return Color.FromArgb(a, ToByte(r + m), ToByte(g + m), ToByte(b + m));
    }

    /// <summary>The partner colour of the accent gradient (Nova: #7B61FF → #4C8DFF ≈ hue −34°, a bit lighter).</summary>
    public static Color GradientPartner(Color a)
    {
        var (h, s, l) = ToHsl(a);
        return FromHsl(h - 34, Math.Min(1, s * 1.02), Math.Clamp(l - 0.02, 0.35, 0.75), a.A);
    }

    public static Color WithAlpha(Color c, byte alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);

    public static Color Mix(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromArgb(
            ToByte((a.A + (b.A - a.A) * t) / 255.0),
            ToByte((a.R + (b.R - a.R) * t) / 255.0),
            ToByte((a.G + (b.G - a.G) * t) / 255.0),
            ToByte((a.B + (b.B - a.B) * t) / 255.0));
    }

    public static Color Shift(Color c, double dHue = 0, double dSat = 0, double dLight = 0)
    {
        var (h, s, l) = ToHsl(c);
        return FromHsl(h + dHue, s + dSat, l + dLight, c.A);
    }

    private static byte ToByte(double v) => (byte)Math.Clamp(Math.Round(v * 255), 0, 255);
}
