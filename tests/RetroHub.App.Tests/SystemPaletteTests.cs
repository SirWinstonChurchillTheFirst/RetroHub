using Avalonia.Media;
using RetroHub.App.Theming;
using Xunit;

namespace RetroHub.App.Tests;

/// <summary>The rules of DESIGN 2.4 for the per-system colours (housing walls, colour band, light).</summary>
public class SystemPaletteTests
{
    private static readonly Color Room = Color.Parse("#0B0C0E");
    private static readonly Color Paper = Color.Parse("#F5F7FA");

    public static TheoryData<string> Systems()
    {
        var data = new TheoryData<string>();
        foreach (var id in SystemPalette.KnownIds.OrderBy(i => i)) data.Add(id);
        return data;
    }

    [Fact]
    public void Every_catalog_system_has_a_hand_set_entry()
    {
        string[] catalog = ["3ds", "arcade", "atari2600", "atari7800", "dreamcast", "gamecube", "gamegear", "gb", "gba", "gbc", "lynx", "mastersystem",
            "megadrive", "msx", "n64", "nds", "nes", "ngp", "pc", "pcengine", "ps1", "ps2", "psp", "saturn", "sega32x", "segacd", "snes", "switch",
            "virtualboy", "wii", "wiiu", "wonderswan"];
        Assert.All(catalog, id => Assert.True(SystemPalette.IsKnown(id), id));
    }

    /// <summary>
    /// M1 QA: the demo catalog and folder/sniffer ids ("gc", "psx", "md") fell back to the custom look (a lavender
    /// cart for the GameCube). Every id the demo catalog or a sniffer uses must resolve to a hand-set entry.
    /// </summary>
    [Theory]
    [InlineData("gc", "gamecube")]
    [InlineData("ngc", "gamecube")]
    [InlineData("psx", "ps1")]
    [InlineData("md", "megadrive")]
    [InlineData("genesis", "megadrive")]
    [InlineData("sms", "mastersystem")]
    [InlineData("pce", "pcengine")]
    [InlineData("tg16", "pcengine")]
    public void Alias_ids_resolve_to_the_catalog_entry(string alias, string id)
    {
        Assert.True(SystemPalette.IsKnown(alias), alias);
        Assert.Same(SystemPalette.Get(id), SystemPalette.Get(alias));
        Assert.Equal(SystemPalette.BoxAspect(id), SystemPalette.BoxAspect(alias));
    }

    [Fact]
    public void Every_demo_system_has_a_hand_set_entry()
    {
        var catalog = new RetroHub.App.Services.Demo.DemoCatalog();
        Assert.NotEmpty(catalog.Systems);
        Assert.All(catalog.Systems, s => Assert.True(SystemPalette.IsKnown(s.Id), $"demo system '{s.Id}' falls back to the custom look"));
    }

    [Theory]
    [MemberData(nameof(Systems))]
    public void Light_reads_on_the_upper_wall_and_on_the_room(string id)
    {
        var look = SystemPalette.Get(id);
        Assert.True(ColorUtil.Contrast(look.Light, look.WallHi) >= 4.5, $"{id}: light/wall-hi {ColorUtil.Contrast(look.Light, look.WallHi):0.0}");
        Assert.True(ColorUtil.Contrast(look.Light, Room) >= 7.0, $"{id}: light/room {ColorUtil.Contrast(look.Light, Room):0.0}");
    }

    [Theory]
    [MemberData(nameof(Systems))]
    public void Text_on_the_colour_band_reaches_4_5(string id)
    {
        var look = SystemPalette.Get(id);
        Assert.True(ColorUtil.Contrast(look.TextOnColor, look.Color) >= 4.5, $"{id}: {ColorUtil.Contrast(look.TextOnColor, look.Color):0.0}");
    }

    [Theory]
    [MemberData(nameof(Systems))]
    public void Paper_reads_on_both_wall_tones(string id)
    {
        var look = SystemPalette.Get(id);
        Assert.True(ColorUtil.Contrast(Paper, look.Wall) >= 7.0, id); // DESIGN 2.4 table: 9.0 … 16.3
        Assert.True(ColorUtil.Contrast(Paper, look.WallHi) >= 7.0, id);
    }

    [Fact]
    public void Same_housing_and_motif_need_distinct_colours()
    {
        var looks = SystemPalette.KnownIds.Select(id => SystemPalette.Get(id)).ToList();
        var pairs = 0;
        foreach (var a in looks)
            foreach (var b in looks)
            {
                if (string.CompareOrdinal(a.Id, b.Id) >= 0 || a.Housing != b.Housing) continue;
                var de = ColorUtil.DeltaE2000(a.Color, b.Color);
                if (a.Motif == b.Motif)
                {
                    pairs++;
                    Assert.True(de >= 15, $"{a.Id}/{b.Id}: same housing + motif, ΔE {de:0.0}");
                }
                // same housing and (nearly) the same colour: the motifs must differ
                if (de < 15) Assert.True(a.Motif != b.Motif, $"{a.Id}/{b.Id}: same housing, ΔE {de:0.0}, same motif");
            }
        Assert.Equal(0, pairs); // M5: no two catalog systems share a motif any more (see below)
    }

    /// <summary>
    /// M5 acceptance (DESIGN 6.5): every catalog system is told apart without text – each has its OWN motif, so even
    /// two systems with the same housing and a similar colour never look alike.
    /// </summary>
    [Fact]
    public void Every_catalog_system_has_its_own_motif()
    {
        var byMotif = SystemPalette.KnownIds.GroupBy(id => SystemPalette.Get(id).Motif).Where(g => g.Count() > 1).ToList();
        Assert.True(byMotif.Count == 0, string.Join("; ", byMotif.Select(g => $"{g.Key}: {string.Join(", ", g)}")));
    }

    [Fact]
    public void Lynx_and_game_gear_are_far_apart() =>
        Assert.InRange(ColorUtil.DeltaE2000(SystemPalette.Get("lynx").Color, SystemPalette.Get("gamegear").Color), 45, 57); // DESIGN: ΔE 51

    [Theory]
    [InlineData("nes", "#EE8481")]
    [InlineData("gb", "#DAF47C")]
    [InlineData("gba", "#9D91E8")] // v2: brighter paper keeps the colour undarkened
    [InlineData("wii", "#7CD4F3")]
    [InlineData("ps2", "#899DE6")]
    [InlineData("psp", "#A6B2C9")]
    [InlineData("atari2600", "#E5B58A")]
    public void Light_formula_matches_the_design_table(string id, string expected)
    {
        var light = SystemPalette.Get(id).Light;
        var e = Color.Parse(expected);
        Assert.True(Math.Abs(light.R - e.R) <= 3 && Math.Abs(light.G - e.G) <= 3 && Math.Abs(light.B - e.B) <= 3,
            $"{id}: {ColorUtil.ToHex(light)} vs {expected}");
    }

    [Theory]
    [InlineData("#FFFF00")]
    [InlineData("#000080")]
    [InlineData("#808080")]
    [InlineData("#FF00FF")]
    public void Unknown_systems_resolve_through_the_accent(string accent)
    {
        var look = SystemPalette.Get("my-homebrew-console", Color.Parse(accent));
        Assert.Equal(Housing.Anthracite, look.Housing);
        Assert.Equal(Motif.Cart8, look.Motif);
        Assert.Equal(Texture.Bayer, look.Texture);
        Assert.True(ColorUtil.Contrast(look.TextOnColor, look.Color) >= 4.5, accent);
        Assert.True(ColorUtil.Contrast(look.Light, look.WallHi) >= 4.5, accent);
    }

    [Fact]
    public void Brushes_are_shared_instances()
    {
        Assert.Same(SystemPalette.Get("gba").WallBrush, SystemPalette.Get("gba").WallBrush);
        Assert.Same(SystemPalette.Get("GBA"), SystemPalette.Get("gba"));
    }

    [Fact]
    public void Walls_have_flat_tones_without_alpha()
    {
        foreach (var id in SystemPalette.KnownIds)
        {
            var look = SystemPalette.Get(id);
            Assert.Equal(255, look.Wall.A);
            Assert.Equal(255, look.WallHi.A);
            Assert.Equal(255, look.Color.A);
        }
    }

    [Fact]
    public void Contrast_and_delta_e_reference_values()
    {
        Assert.Equal(21.0, ColorUtil.Contrast(Colors.Black, Colors.White), 2);
        Assert.Equal(1.0, ColorUtil.Contrast(Room, Room), 3);
        Assert.Equal(0.0, ColorUtil.DeltaE2000(Paper, Paper), 6);
        // DESIGN 2.1 (v2): paper on the room 18.3 : 1, Cyan 11.0 : 1
        Assert.InRange(ColorUtil.Contrast(Paper, Room), 18.1, 18.5);
        Assert.InRange(ColorUtil.Contrast(Color.Parse("#2FD3F5"), Room), 10.8, 11.2);
    }
}
