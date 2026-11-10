using Avalonia.Media;

namespace RetroHub.App.Theming;

/// <summary>Housing material of a system (DESIGN 2.4): decides the two flat tones of its painted wall.</summary>
public enum Housing { WarmGrey, CoolGrey, Black, Anthracite, Violet, White, Beige, Wood, Berry }

/// <summary>Category motif of a system (DESIGN 6.2). Categories, never products.</summary>
public enum Motif
{
    Cart8, CartWide, Cart16, CartGrip, Cart64, CartStack, Card,
    HandheldTall, HandheldTallDouble, HandheldNarrow, HandheldWide, HandheldBig, HandheldLong, HandheldCompact, HandheldBroad,
    Clamshell, ClamshellWide,
    Disc, DiscTrack, DiscBands, DiscHalftone, MiniDisc, Tray, Remote, Tablet, Dock,
    TwinLens, ControlPanel, Stick, StickGrip, Floppy, KeyBlock, Tower,
}

/// <summary>Era texture of a wall (DESIGN 4.5); file name = <see cref="Textures.FileName"/>.</summary>
public enum Texture { None, Grain, Scan, Lcd, Bayer, Dither16, Grooves, Poly, Halftone, ShadowMask, Perforated, Wood, Grid,
    /// <summary>Material textures of the drawn objects (DESIGN 4.6), never a wall's era texture: card fibres, board grain, dust.</summary>
    Card, BoardGrain, Dust }

/// <summary>The resolved look of one system: wall tones, colour band, light, text on colour, motif, texture.</summary>
public sealed class SystemLook
{
    internal SystemLook(string id, Housing housing, Motif motif, Texture texture, double textureOpacity, Color color, Color second,
        Texture overlay = Texture.None, double overlayOpacity = 0)
    {
        Id = id;
        Overlay = overlay;
        OverlayOpacity = overlayOpacity;
        Housing = housing;
        Motif = motif;
        Texture = texture;
        TextureOpacity = textureOpacity;
        (Wall, WallHi) = SystemPalette.HousingTones(housing);
        Color = SystemPalette.FitTextOnColor(color);
        Second = second;
        Light = SystemPalette.LightFor(Color, WallHi);
        TextOnColor = ColorUtil.Contrast(SystemPalette.Paper, Color) >= ColorUtil.Contrast(SystemPalette.Ink, Color) ? SystemPalette.Paper : SystemPalette.Ink;
        WallBrush = Freeze(Wall);
        WallHiBrush = Freeze(WallHi);
        ColorBrush = Freeze(Color);
        SecondBrush = Freeze(Second);
        LightBrush = Freeze(Light);
        TextOnColorBrush = Freeze(TextOnColor);
        // "Flächen über 120×120 px bekommen nie die volle Farbe": the ticker/mirror use 70 %, motif ghosts 18 %
        Color70Brush = Freeze(ColorUtil.WithAlpha(Color, 0xB3));
        Color18Brush = Freeze(ColorUtil.WithAlpha(Color, 0x2E));
        // R2: on the near-black housing an empty case (#18181B on #0F0F11, 1.1 : 1) and a front's compartment were
        // black holes – the plastic and the compartment's back wall are lifted to read as objects (≥ 1.3 : 1)
        (CaseLo, CaseHi) = housing == Housing.Black ? (Color.Parse("#1C1C20"), Color.Parse("#27272C")) : (Wall, WallHi);
        Recess = housing == Housing.Black ? Color.Parse("#1D1C20") : ColorUtil.Mix(WallHi, Colors.Black, 0.22);
        CaseLoBrush = Freeze(CaseLo);
        CaseHiBrush = Freeze(CaseHi);
    }

    /// <summary>The plastic of an empty case (placeholder): the wall tones, lifted on the black housing.</summary>
    public Color CaseHi { get; }
    public Color CaseLo { get; }
    public IImmutableSolidColorBrush CaseHiBrush { get; }
    public IImmutableSolidColorBrush CaseLoBrush { get; }

    /// <summary>The back wall of a front's inner compartment.</summary>
    public Color Recess { get; }

    public string Id { get; }
    public Housing Housing { get; }
    public Motif Motif { get; }
    public Texture Texture { get; }
    public double TextureOpacity { get; }
    /// <summary>
    /// A second era texture laid over the first (DESIGN 6.3: the 16-bit modules carry dither AND the grooves of their
    /// housings); None for most systems.
    /// </summary>
    public Texture Overlay { get; }
    public double OverlayOpacity { get; }
    /// <summary>Lower 62 % of the wall.</summary>
    public Color Wall { get; }
    /// <summary>Upper 38 % of the wall.</summary>
    public Color WallHi { get; }
    /// <summary>"Farbe": colour band, spine band, label bar, ticker underline.</summary>
    public Color Color { get; }
    public Color Second { get; }
    /// <summary>"Licht": focus ring, light cone and board light in the system context.</summary>
    public Color Light { get; }
    /// <summary>Paper or ink, whichever reads better on <see cref="Color"/> (≥ 4.5 : 1).</summary>
    public Color TextOnColor { get; }

    // shared, immutable brush instances (never one per tile)
    public IImmutableSolidColorBrush WallBrush { get; }
    public IImmutableSolidColorBrush WallHiBrush { get; }
    public IImmutableSolidColorBrush ColorBrush { get; }
    public IImmutableSolidColorBrush SecondBrush { get; }
    public IImmutableSolidColorBrush LightBrush { get; }
    public IImmutableSolidColorBrush TextOnColorBrush { get; }
    public IImmutableSolidColorBrush Color70Brush { get; }
    public IImmutableSolidColorBrush Color18Brush { get; }

    private static IImmutableSolidColorBrush Freeze(Color c) => new ImmutableSolidColorBrush(c);
}

/// <summary>
/// Per-system colours of "Fernsehlicht" (DESIGN 2.4): walls come from the housing material, the colour band and the
/// light from the hand-picked system colour. <see cref="Get"/> never fails: unknown/custom systems resolve through
/// <see cref="From"/> (anthracite housing, the catalog accent as colour, Cart8, bayer-8).
/// Reference script: scratchpad/design/tools/palette2.py; rules checked by SystemPaletteTests.
/// </summary>
public static class SystemPalette
{
    public static readonly Color Room = Color.Parse("#14120F");
    public static readonly Color Paper = Color.Parse("#F4ECDF");
    public static readonly Color Ink = Color.Parse("#14120F");

    /// <summary>(Wand, Wand hell) per housing.</summary>
    public static (Color Wall, Color WallHi) HousingTones(Housing h) => h switch
    {
        Housing.WarmGrey => (Color.Parse("#211F1B"), Color.Parse("#322E28")),
        Housing.CoolGrey => (Color.Parse("#1D2027"), Color.Parse("#2D313B")),
        Housing.Black => (Color.Parse("#0F0F11"), Color.Parse("#18181B")),
        Housing.Anthracite => (Color.Parse("#1C1C20"), Color.Parse("#2A2A30")),
        Housing.Violet => (Color.Parse("#1B1626"), Color.Parse("#2B213B")),
        Housing.White => (Color.Parse("#2A2B2D"), Color.Parse("#3D3F43")),
        Housing.Beige => (Color.Parse("#2A2418"), Color.Parse("#3E3524")),
        Housing.Wood => (Color.Parse("#24170E"), Color.Parse("#382419")),
        _ => (Color.Parse("#20132A"), Color.Parse("#311D3F")), // Berry (transparent housing)
    };

    private sealed record Def(Housing Housing, Motif Motif, Texture Texture, double TextureOpacity, string Color, string Second,
        Texture Overlay = Texture.None, double OverlayOpacity = 0);

    // DESIGN 2.4 system table + 6.3 textures (opacity from 4.5)
    private static readonly Dictionary<string, Def> Defs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["nes"] = new(Housing.WarmGrey, Motif.Cart8, Texture.Bayer, 0.07, "#C51F1B", "#A8A39A"),
        ["snes"] = new(Housing.WarmGrey, Motif.Cart16, Texture.Dither16, 0.07, "#6652BE", "#BDB7AD", Texture.Grooves, 0.05),
        ["n64"] = new(Housing.Anthracite, Motif.Cart64, Texture.Poly, 0.06, "#12A05E", "#4A4A50"),
        ["gb"] = new(Housing.WarmGrey, Motif.HandheldTall, Texture.Lcd, 0.08, "#8BAC0F", "#306230"),
        ["gbc"] = new(Housing.Berry, Motif.HandheldTallDouble, Texture.Lcd, 0.08, "#8E3BD0", "#2FB8A6"),
        ["gba"] = new(Housing.Violet, Motif.HandheldWide, Texture.Lcd, 0.08, "#5A46D9", "#A9A2E8"),
        ["gamecube"] = new(Housing.Violet, Motif.MiniDisc, Texture.Halftone, 0.07, "#6450CC", "#F29A3B"),
        ["wii"] = new(Housing.White, Motif.Remote, Texture.Halftone, 0.04, "#3FC0EE", "#E6EEF2"),
        ["wiiu"] = new(Housing.White, Motif.Tablet, Texture.Halftone, 0.07, "#12A0CC", "#F0F0F0"),
        ["nds"] = new(Housing.CoolGrey, Motif.Clamshell, Texture.Lcd, 0.08, "#8FA3B8", "#3C4654"),
        ["3ds"] = new(Housing.Anthracite, Motif.ClamshellWide, Texture.Lcd, 0.08, "#C21D26", "#2B2B30"),
        ["switch"] = new(Housing.Anthracite, Motif.Dock, Texture.Perforated, 0.06, "#C41F2A", "#2E2E33"),
        // 6.3 lists scan-4x3, but scanlines are reserved for the screen object (QA 11.5) → bayer-8
        ["virtualboy"] = new(Housing.Black, Motif.TwinLens, Texture.Bayer, 0.07, "#C8121C", "#1A0505"),
        ["arcade"] = new(Housing.Black, Motif.ControlPanel, Texture.ShadowMask, 0.10, "#FF2E88", "#22E4FF"),
        ["pc"] = new(Housing.Beige, Motif.Floppy, Texture.Perforated, 0.06, "#C7B48A", "#3FBF6A"),
        ["atari2600"] = new(Housing.Wood, Motif.Stick, Texture.Wood, 0.10, "#C0702A", "#2A1A10"),
        ["atari7800"] = new(Housing.Black, Motif.StickGrip, Texture.Bayer, 0.07, "#C8243A", "#C0C0C0"),
        ["lynx"] = new(Housing.Black, Motif.HandheldLong, Texture.Lcd, 0.08, "#F2A21A", "#2B2B2B"),
        ["pcengine"] = new(Housing.White, Motif.Card, Texture.Dither16, 0.07, "#F05A28", "#E8E4DA"),
        ["ngp"] = new(Housing.Anthracite, Motif.HandheldCompact, Texture.Lcd, 0.08, "#2E5DB8", "#D9D4C8"),
        ["wonderswan"] = new(Housing.White, Motif.HandheldNarrow, Texture.Lcd, 0.08, "#10A8B5", "#F0E6C8"),
        ["msx"] = new(Housing.Beige, Motif.KeyBlock, Texture.Grid, 0.07, "#3368A3", "#D8D2C4"),
        ["mastersystem"] = new(Housing.Black, Motif.CartWide, Texture.Grid, 0.07, "#C42019", "#1A1A1A"),
        ["megadrive"] = new(Housing.Black, Motif.CartGrip, Texture.Dither16, 0.07, "#1B66B2", "#121212", Texture.Grooves, 0.05),
        ["gamegear"] = new(Housing.Black, Motif.HandheldBig, Texture.Grid, 0.07, "#2D9CDB", "#1A1A1A"),
        ["segacd"] = new(Housing.Black, Motif.Tray, Texture.Halftone, 0.07, "#4A86D0", "#1A1A1A"),
        ["sega32x"] = new(Housing.Black, Motif.CartStack, Texture.Dither16, 0.07, "#F2AE00", "#1A1A1A"),
        ["saturn"] = new(Housing.Anthracite, Motif.DiscBands, Texture.Poly, 0.06, "#5A5FB8", "#2A2A2E"),
        ["dreamcast"] = new(Housing.White, Motif.DiscHalftone, Texture.Halftone, 0.07, "#F26F21", "#EDEBE6"),
        ["ps1"] = new(Housing.CoolGrey, Motif.DiscTrack, Texture.Poly, 0.06, "#9AA0B4", "#3A3F4E"),
        ["ps2"] = new(Housing.Black, Motif.Tower, Texture.Grooves, 0.06, "#2C4FD0", "#08090F"),
        ["psp"] = new(Housing.Black, Motif.HandheldBroad, Texture.Perforated, 0.06, "#A9B4C8", "#0C0D10"),
    };

    private static readonly Dictionary<string, SystemLook> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<uint, SystemLook> CustomCache = new();
    private static readonly object Gate = new();

    /// <summary>Ids with a hand-set entry in the system table.</summary>
    public static IReadOnlyCollection<string> KnownIds => Defs.Keys;

    public static bool IsKnown(string? id) => Normalize(id) is { } n && Defs.ContainsKey(n);

    /// <summary>
    /// Folder / sniffer / demo ids that name a catalog system under another spelling ("gc", "psx", "md", "genesis" …):
    /// they resolve to the catalog id before the table, so they never fall back to the custom look.
    /// </summary>
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["gc"] = "gamecube", ["ngc"] = "gamecube",
        ["md"] = "megadrive", ["genesis"] = "megadrive",
        ["psx"] = "ps1", ["ps"] = "ps1", ["playstation"] = "ps1",
        ["sms"] = "mastersystem", ["markiii"] = "mastersystem",
        ["pce"] = "pcengine", ["tg16"] = "pcengine", ["turbografx16"] = "pcengine",
        ["famicom"] = "nes", ["sfc"] = "snes", ["superfamicom"] = "snes",
        ["ndsi"] = "nds", ["n3ds"] = "3ds", ["gg"] = "gamegear", ["32x"] = "sega32x", ["mcd"] = "segacd",
        ["vb"] = "virtualboy", ["ws"] = "wonderswan", ["dc"] = "dreamcast",
    };

    /// <summary>The catalog id for <paramref name="id"/> (aliases resolved); null stays null.</summary>
    public static string? Normalize(string? id) => id == null ? null : Aliases.TryGetValue(id, out var canonical) ? canonical : id;

    /// <summary>Box format start values, height / width (DESIGN 11.4); unknown systems 1.25.</summary>
    private static readonly Dictionary<string, double> BoxAspects = new(StringComparer.OrdinalIgnoreCase)
    {
        ["nes"] = 1.38, ["snes"] = 1.00, ["n64"] = 0.90, ["gb"] = 1.00, ["gbc"] = 1.00, ["gba"] = 1.00,
        ["nds"] = 0.90, ["3ds"] = 0.86, ["gamecube"] = 1.40, ["wii"] = 1.40, ["wiiu"] = 1.40, ["switch"] = 1.62,
        ["ps1"] = 1.00, ["ps2"] = 1.42, ["psp"] = 1.72, ["megadrive"] = 1.40, ["mastersystem"] = 1.40,
        ["saturn"] = 1.00, ["dreamcast"] = 1.00, ["gamegear"] = 1.25, ["arcade"] = 1.40, ["pc"] = 1.40,
    };

    /// <summary>
    /// Typical box format of system <paramref name="id"/> as height / width (DESIGN 11.4 start values): shelf slots on
    /// Start and spine placeholders take it, so a box without a cover has the size of its neighbours with one.
    /// </summary>
    public static double BoxAspect(string? id) => Normalize(id) is { } n && BoxAspects.TryGetValue(n, out var a) ? a : 1.25;

    /// <summary>The look of system <paramref name="id"/>; unknown ids fall back to <see cref="From"/>(<paramref name="accent"/>).</summary>
    public static SystemLook Get(string? id, Color? accent = null)
    {
        id = Normalize(id);
        lock (Gate)
        {
            if (id != null && Cache.TryGetValue(id, out var look)) return look;
            if (id != null && Defs.TryGetValue(id, out var d))
            {
                look = new SystemLook(id.ToLowerInvariant(), d.Housing, d.Motif, d.Texture, d.TextureOpacity, Color.Parse(d.Color), Color.Parse(d.Second),
                    d.Overlay, d.OverlayOpacity);
                Cache[id] = look;
                return look;
            }
        }
        return From(accent ?? Color.Parse("#948A7A"), id);
    }

    /// <summary>Unknown / user-defined systems: anthracite housing, the accent (fitted for contrast) as colour, Cart8, bayer-8.</summary>
    public static SystemLook From(Color accent, string? id = null)
    {
        var key = ((uint)accent.R << 16) | ((uint)accent.G << 8) | accent.B;
        lock (Gate)
        {
            if (CustomCache.TryGetValue(key, out var look)) return look;
            var second = HousingTones(Housing.Anthracite).WallHi;
            look = new SystemLook(id ?? "custom", Housing.Anthracite, Motif.Cart8, Texture.Bayer, 0.07, Color.FromRgb(accent.R, accent.G, accent.B), second);
            CustomCache[key] = look;
            return look;
        }
    }

    /// <summary>
    /// "Licht" = HSL(h, clamp(s, 0.25, 0.85), L from 0.72), L rising in 0.02 steps until light/wall-hi ≥ 4.5 and
    /// light/room ≥ 7.0 (or L reaches 0.92).
    /// </summary>
    public static Color LightFor(Color color, Color wallHi)
    {
        var (h, s, _) = ColorUtil.ToHsl(color);
        var ls = Math.Clamp(s, 0.25, 0.85);
        var l = 0.72;
        var c = ColorUtil.FromHsl(h, ls, l);
        while ((ColorUtil.Contrast(c, wallHi) < 4.5 || ColorUtil.Contrast(c, Room) < 7.0) && l < 0.92)
        {
            l += 0.02;
            c = ColorUtil.FromHsl(h, ls, l);
        }
        return c;
    }

    /// <summary>Text on the colour must reach 4.5 : 1 (paper or ink); otherwise the colour darkens in 4 % steps.</summary>
    public static Color FitTextOnColor(Color color)
    {
        var c = Color.FromRgb(color.R, color.G, color.B);
        for (var i = 0; i < 25; i++)
        {
            if (Math.Max(ColorUtil.Contrast(Paper, c), ColorUtil.Contrast(Ink, c)) >= 4.5) return c;
            c = ColorUtil.Mix(c, Colors.Black, 0.04);
        }
        return c;
    }
}

/// <summary>Texture file names (Assets/Textures, generated by build/tools/texturegen.py).</summary>
public static class Textures
{
    public static string? FileName(Texture t) => t switch
    {
        Texture.Grain => "korn-128.png",
        Texture.Scan => "scan-4x3.png",
        Texture.Lcd => "lcd-4.png",
        Texture.Bayer => "bayer-8.png",
        Texture.Dither16 => "dither16-8.png",
        Texture.Grooves => "rillen-1x6.png",
        Texture.Poly => "poly-48.png",
        Texture.Halftone => "halbton-12.png",
        Texture.ShadowMask => "maske-6.png",
        Texture.Perforated => "lochblech-10.png",
        Texture.Wood => "holz-256x32.png",
        Texture.Grid => "raster-16.png",
        Texture.Card => "pappe-64.png",
        Texture.BoardGrain => "brett-512x24.png",
        Texture.Dust => "staub-64.png",
        _ => null,
    };
}
