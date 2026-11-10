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
        foreach (var k in new[] { "RhPaperBrush", "RhInkBrush", "RhLightEdgeBrush", "RhAccentBrush", "RhAccentTextBrush", "RhWarningBrush", "RhWarningSoftBrush", "RhDangerSoftBrush" })
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

    /// <summary>
    /// R3 QA: the detail page's TV light took a brown/olive cover's colour – (37,18,13) across the top 550 px. The page top
    /// (softened picture at 38 % under the scrims and the glow) must stay neutral for warm covers: R − B ≤ 8.
    /// </summary>
    [Theory]
    [InlineData("#7A6420")] // olive
    [InlineData("#6B3A1C")] // brown
    [InlineData("#E07020")] // orange
    [InlineData("#C8B070")] // beige/gold
    [InlineData("#B01818")] // red
    public Task A_warm_cover_leaves_the_detail_page_top_neutral(string hex) => UiTest.Run(() =>
    {
        var cover = Color.Parse(hex);
        var path = Path.Combine(Path.GetTempPath(), $"rh-wash-{hex.TrimStart('#')}-{Guid.NewGuid():N}.png");
        try
        {
            using (var bmp = new Avalonia.Media.Imaging.WriteableBitmap(new PixelSize(96, 128), new Vector(96, 96),
                       Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Premul))
            {
                using (var fb = bmp.Lock())
                {
                    var row = new byte[96 * 4];
                    for (var x = 0; x < 96; x++)
                    {
                        row[x * 4] = cover.B;
                        row[x * 4 + 1] = cover.G;
                        row[x * 4 + 2] = cover.R;
                        row[x * 4 + 3] = 255;
                    }
                    for (var y = 0; y < 128; y++) System.Runtime.InteropServices.Marshal.Copy(row, 0, fb.Address + y * fb.RowBytes, row.Length);
                }
                bmp.Save(path);
            }
            var soft = Controls.RoomBackdrop.Soften(path)!;
            Assert.True(ColorUtil.ToHsl(soft.Light).S <= Controls.RoomBackdrop.MaxLightSaturation + 0.01, $"light {ColorUtil.ToHex(soft.Light)}");
            var sw = soft.Bitmap.PixelSize.Width;
            var sh = soft.Bitmap.PixelSize.Height;
            var px = new byte[sw * sh * 4];
            var handle = System.Runtime.InteropServices.GCHandle.Alloc(px, System.Runtime.InteropServices.GCHandleType.Pinned);
            try { soft.Bitmap.CopyPixels(new PixelRect(0, 0, sw, sh), handle.AddrOfPinnedObject(), px.Length, sw * 4); }
            finally { handle.Free(); }
            // the detail page: 1920 × 1080, board at 560, glow at the cover (the page's GlowX/GlowY ≈ 0.2 / 0.35)
            var key = new Controls.RoomBackdrop.LightKey(1920, 1088, 560, soft.Light.ToUInt32(), 160, 200, 350, 1.0);
            var light = Controls.RoomBackdrop.LightPixels(key, out var lw, out var lh);
            var room = SystemPalette.Room;
            var worst = 0.0;
            foreach (var (u, v) in new[] { (0.1, 0.05), (0.2, 0.15), (0.5, 0.1), (0.8, 0.2), (0.3, 0.35), (0.6, 0.45) })
            {
                var sp = ((int)(v * sh) * sw + (int)(u * sw)) * 4;
                var lp = ((int)(v * lh) * lw + (int)(u * lw)) * 4;
                double Under(int c, byte roomC) => roomC * (1 - Controls.RoomBackdrop.ImageOpacity) + px[sp + c] * Controls.RoomBackdrop.ImageOpacity;
                var a = light[lp + 3] / 255.0;
                var r = light[lp + 2] + (1 - a) * Under(2, room.R);
                var b = light[lp] + (1 - a) * Under(0, room.B);
                worst = Math.Max(worst, r - b);
            }
            Assert.True(worst <= 8, $"{hex}: R − B up to {worst:0.0} at the page top");
        }
        finally { File.Delete(path); }
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
