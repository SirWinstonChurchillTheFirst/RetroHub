using Avalonia;
using Avalonia.Media;
using RetroHub.App.Theming;
using Xunit;

namespace RetroHub.App.Tests;

/// <summary>
/// Theme v2 "Schwarz + Weiß + Cyan" (owner decision after R3, DESIGN 2.1/2.2 v2): neutral near-black, white text, Cyan as
/// the light colour; no warm brown, olive, beige or gold left in the neutrals, and every text and focus colour readable
/// on every ground, including each housing's wall.
/// </summary>
public class ThemeV2Tests
{
    private static readonly string[] Neutrals =
    [
        "RhRoomDeepColor", "RhBaseColor", "RhSurfaceColor", "RhSurfaceHiColor", "RhLineColor",
        "RhText1Color", "RhText2Color", "RhText3Color", "RhInkColor",
    ];

    private static Color Res(string key)
    {
        Assert.True(Application.Current!.TryGetResource(key, null, out var v), key);
        return v switch { Color c => c, ISolidColorBrush b => b.Color, _ => throw new InvalidOperationException(key) };
    }

    /// <summary>Warm = hue 15°–75° with some colour in it: the brown, olive, beige and gold the owner rejected.</summary>
    private static bool IsWarm(Color c)
    {
        var (h, s, l) = ColorUtil.ToHsl(c);
        return h is >= 15 and <= 75 && s >= 0.10 && l is > 0.04 and < 0.97;
    }

    private static IEnumerable<Color> Grounds()
    {
        foreach (var k in new[] { "RhRoomDeepColor", "RhBaseColor", "RhSurfaceColor", "RhSurfaceHiColor" }) yield return Res(k);
        foreach (var h in Enum.GetValues<Housing>())
        {
            var (wall, hi) = SystemPalette.HousingTones(h);
            yield return wall;
            yield return hi;
        }
    }

    [Fact]
    public Task The_owner_palette_is_the_token_base() => UiTest.Run(() =>
    {
        Assert.Equal(Color.Parse("#0B0C0E"), Res("RhBaseColor"));
        Assert.Equal(Color.Parse("#1A1C20"), Res("RhSurfaceColor"));
        Assert.Equal(Color.Parse("#FFFFFF"), Res("RhText1Color"));
        Assert.Equal("#2FD3F5", ThemeManager.DefaultAccent);
        ThemeManager.ApplyAccent(null);
        Assert.Equal(Color.Parse("#2FD3F5"), Res("RhAccentBrush"));
    });

    [Fact]
    public Task No_neutral_token_and_no_housing_wall_is_warm() => UiTest.Run(() =>
    {
        var warm = Neutrals.Where(k => IsWarm(Res(k))).ToList();
        foreach (var h in Enum.GetValues<Housing>())
        {
            var (wall, hi) = SystemPalette.HousingTones(h);
            if (IsWarm(wall) || IsWarm(hi)) warm.Add($"{h}: {ColorUtil.ToHex(wall)}/{ColorUtil.ToHex(hi)}");
        }
        foreach (var k in new[] { "RhPaperBrush", "RhInkBrush", "RhLightEdgeBrush", "RhAccentBrush", "RhAccentTextBrush", "RhWarningSoftBrush", "RhDangerSoftBrush" })
            if (IsWarm(Res(k))) warm.Add(k);
        Assert.True(warm.Count == 0, string.Join(", ", warm));
    });

    [Fact]
    public void Bernstein_is_no_light_colour_any_more_and_every_preset_is_cool_or_neutral()
    {
        Assert.DoesNotContain(ThemeManager.Presets, p => string.Equals(p.Hex, "#FFB547", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("cyan", ThemeManager.Presets[0].Id);
        Assert.All(ThemeManager.Presets, p => Assert.False(IsWarm(Color.Parse(p.Hex)), $"{p.Id} {p.Hex}"));
    }

    [Theory]
    [InlineData("#FFB547", null)]      // Bernstein → Cyan (stored as "default")
    [InlineData("#ffb547", null)]
    [InlineData("#8CC4FF", null)]      // Eisblau → Cyan
    [InlineData("#8FE3A0", "#3DDC84")] // Phosphor → Grün
    [InlineData("#EDE3D0", "#F2F5F8")] // Röhrenweiß → Weiß
    [InlineData("#FF8A80", "#FF808B")] // Kirsche → Signalrot
    [InlineData("#FF8CC6", "#FF808B")] // Neonrosa → Signalrot
    [InlineData("#2FD3F5", null)]      // Cyan is the default
    [InlineData("#3DDC84", "#3DDC84")] // v2 presets stay
    [InlineData(null, null)]
    [InlineData("", null)]
    public void A_stored_v1_light_colour_migrates_to_its_cool_successor(string? stored, string? expected)
    {
        Assert.Equal(expected, ThemeManager.MigrateStored(stored));
        // and what it resolves to is a v2 preset
        Assert.Contains(ThemeManager.Presets, p => Color.Parse(p.Hex) == ThemeManager.Resolve(ThemeManager.MigrateStored(stored)));
    }

    [Fact]
    public Task Text_reaches_4_5_to_1_on_every_ground_including_every_housing_wall() => UiTest.Run(() =>
    {
        var fails = new List<string>();
        foreach (var t in new[] { "RhText1Color", "RhText2Color", "RhText3Color" })
            foreach (var g in Grounds())
            {
                var c = ColorUtil.Contrast(Res(t), g);
                if (c < 4.5) fails.Add($"{t} on {ColorUtil.ToHex(g)}: {c:0.00}");
            }
        // ink on paper, labels and every light colour (the play button, inversions)
        foreach (var bg in ThemeManager.Presets.Select(p => Color.Parse(p.Hex)).Append(SystemPalette.Paper).Append(Res("RhPaperBrush")))
            if (ColorUtil.Contrast(Res("RhInkColor"), bg) < 4.5) fails.Add($"ink on {ColorUtil.ToHex(bg)}");
        Assert.True(fails.Count == 0, string.Join("\n", fails));
    });

    [Fact]
    public Task Every_light_colour_as_focus_ring_reaches_3_to_1_on_every_wall() => UiTest.Run(() =>
    {
        var fails = new List<string>();
        foreach (var p in ThemeManager.Presets)
            foreach (var g in Grounds())
            {
                var c = ColorUtil.Contrast(Color.Parse(p.Hex), g);
                if (c < 3.0) fails.Add($"{p.Id} on {ColorUtil.ToHex(g)}: {c:0.00}");
            }
        // in the system context the ring is the system's light on its own walls
        foreach (var id in SystemPalette.KnownIds)
        {
            var look = SystemPalette.Get(id);
            foreach (var g in new[] { look.Wall, look.WallHi })
                if (ColorUtil.Contrast(look.Light, g) < 3.0) fails.Add($"{id} light on {ColorUtil.ToHex(g)}");
        }
        Assert.True(fails.Count == 0, string.Join("\n", fails));
    });
}
