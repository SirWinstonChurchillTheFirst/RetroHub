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

    /// <summary>Legacy (v0 accent gradients, hue −34°, a bit lighter); unused by the "Fernsehlicht" design.</summary>
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

    /// <summary>WCAG relative luminance (alpha ignored).</summary>
    public static double Luminance(Color c)
    {
        static double F(byte v)
        {
            var x = v / 255.0;
            return x <= 0.03928 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * F(c.R) + 0.7152 * F(c.G) + 0.0722 * F(c.B);
    }

    /// <summary>WCAG contrast ratio (1 … 21).</summary>
    public static double Contrast(Color a, Color b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    /// <summary>CIE L*a*b* (D65) of an sRGB colour.</summary>
    public static (double L, double A, double B) ToLab(Color c)
    {
        static double F(byte v)
        {
            var x = v / 255.0;
            return x <= 0.04045 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4);
        }
        double r = F(c.R), g = F(c.G), b = F(c.B);
        var x = (0.4124 * r + 0.3576 * g + 0.1805 * b) / 0.95047;
        var y = 0.2126 * r + 0.7152 * g + 0.0722 * b;
        var z = (0.0193 * r + 0.1192 * g + 0.9505 * b) / 1.08883;
        static double G(double t) => t > 216.0 / 24389 ? Math.Cbrt(t) : (24389.0 / 27 * t + 16) / 116;
        double fx = G(x), fy = G(y), fz = G(z);
        return (116 * fy - 16, 500 * (fx - fy), 200 * (fy - fz));
    }

    /// <summary>CIEDE2000 colour difference.</summary>
    public static double DeltaE2000(Color c1, Color c2)
    {
        static double Rad(double d) => d * Math.PI / 180;
        static double Deg(double r) => r * 180 / Math.PI;
        var (l1, a1, b1) = ToLab(c1);
        var (l2, a2, b2) = ToLab(c2);
        double cA = Math.Sqrt(a1 * a1 + b1 * b1), cB = Math.Sqrt(a2 * a2 + b2 * b2), cb = (cA + cB) / 2;
        var g = 0.5 * (1 - Math.Sqrt(Math.Pow(cb, 7) / (Math.Pow(cb, 7) + Math.Pow(25, 7))));
        double a1p = (1 + g) * a1, a2p = (1 + g) * a2;
        double c1p = Math.Sqrt(a1p * a1p + b1 * b1), c2p = Math.Sqrt(a2p * a2p + b2 * b2);
        var h1p = (Deg(Math.Atan2(b1, a1p)) % 360 + 360) % 360;
        var h2p = (Deg(Math.Atan2(b2, a2p)) % 360 + 360) % 360;
        double dLp = l2 - l1, dCp = c2p - c1p, dh = h2p - h1p;
        if (c1p * c2p == 0) dh = 0;
        else if (dh > 180) dh -= 360;
        else if (dh < -180) dh += 360;
        var dHp = 2 * Math.Sqrt(c1p * c2p) * Math.Sin(Rad(dh / 2));
        double lbp = (l1 + l2) / 2, cbp = (c1p + c2p) / 2, hbp;
        if (c1p * c2p == 0) hbp = h1p + h2p;
        else if (Math.Abs(h1p - h2p) <= 180) hbp = (h1p + h2p) / 2;
        else hbp = h1p + h2p < 360 ? (h1p + h2p + 360) / 2 : (h1p + h2p - 360) / 2;
        var t = 1 - 0.17 * Math.Cos(Rad(hbp - 30)) + 0.24 * Math.Cos(Rad(2 * hbp)) + 0.32 * Math.Cos(Rad(3 * hbp + 6)) - 0.20 * Math.Cos(Rad(4 * hbp - 63));
        var dth = 30 * Math.Exp(-Math.Pow((hbp - 275) / 25, 2));
        var rc = 2 * Math.Sqrt(Math.Pow(cbp, 7) / (Math.Pow(cbp, 7) + Math.Pow(25, 7)));
        var sl = 1 + 0.015 * Math.Pow(lbp - 50, 2) / Math.Sqrt(20 + Math.Pow(lbp - 50, 2));
        var sc = 1 + 0.045 * cbp;
        var sh = 1 + 0.015 * cbp * t;
        var rt = -Math.Sin(Rad(2 * dth)) * rc;
        return Math.Sqrt(Math.Pow(dLp / sl, 2) + Math.Pow(dCp / sc, 2) + Math.Pow(dHp / sh, 2) + rt * (dCp / sc) * (dHp / sh));
    }

    public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    private static byte ToByte(double v) => (byte)Math.Clamp(Math.Round(v * 255), 0, 255);
}
