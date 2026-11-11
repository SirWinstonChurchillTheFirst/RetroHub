using RetroHub.Core.Library;
using RetroHub.Core.Metadata;

using Xunit;

namespace RetroHub.Core.Tests.Metadata;

public sealed class LibretroNamingTests
{
    [Theory]
    [InlineData("Mario & Luigi: Superstar Saga", "Mario _ Luigi_ Superstar Saga")]
    [InlineData("What? <Really> \"yes\"", "What_ _Really_ _yes_")]
    [InlineData(@"a*b/c\d|e`f", "a_b_c_d_e_f")]
    [InlineData("Pokemon - Emerald Version (USA, Europe)", "Pokemon - Emerald Version (USA, Europe)")]
    [InlineData("Ys I & II (Japan)", "Ys I _ II (Japan)")]
    [InlineData("", "")]
    public void Sanitize_follows_libretros_rule(string input, string expected) =>
        Assert.Equal(expected, LibretroThumbnails.Sanitize(input));

    [Fact]
    public void Repo_name_and_urls()
    {
        Assert.Equal("Nintendo_-_Game_Boy_Advance", LibretroThumbnails.RepoName("Nintendo - Game Boy Advance"));
        Assert.Equal("https://api.github.com/repos/libretro-thumbnails/Nintendo_-_Game_Boy_Advance/git/trees/master?recursive=1",
            LibretroThumbnails.TreeUrl("Nintendo - Game Boy Advance").AbsoluteUri);
        var url = LibretroThumbnails.BoxartUrl("Nintendo - Game Boy Advance", "Mario _ Luigi - Superstar Saga (Europe) (En,Fr,De,Es,It)");
        Assert.Equal("thumbnails.libretro.com", url.Host);
        Assert.Equal("https", url.Scheme);
        var segments = url.AbsolutePath.Split('/').Select(Uri.UnescapeDataString).ToArray();
        Assert.Equal(["", "Nintendo - Game Boy Advance", "Named_Boxarts", "Mario _ Luigi - Superstar Saga (Europe) (En,Fr,De,Es,It).png"], segments);
        // names with '#' or '%' must not break the URL
        var odd = LibretroThumbnails.BoxartUrl("Sega - Mega Drive - Genesis", "Game #1 (100%)");
        Assert.Equal("Game #1 (100%).png", Uri.UnescapeDataString(odd.AbsolutePath.Split('/').Last()));
        Assert.Equal("", odd.Fragment);
    }

    [Fact]
    public void Region_preference_order()
    {
        var order = new[] { "Europe", "Germany", "USA", "World", "Japan", "Korea", null }
            .Select(r => LibretroThumbnails.RegionRank(r)).ToArray();
        Assert.Equal([0, 0, 1, 2, 3, 4, 5], order);
        Assert.Equal(0, LibretroThumbnails.RegionRank("USA, Europe"));     // best part counts
        Assert.Equal(-1, LibretroThumbnails.RegionRank("Japan", "Japan")); // the ROM's own region first
        Assert.Equal(-1, LibretroThumbnails.RegionRank("USA, Europe", "USA"));
        Assert.Equal(3, LibretroThumbnails.RegionRank("Japan", "USA"));
    }

    private static Game G(string file, string? region = null)
    {
        var parsed = TitleParser.Parse(file);
        return new Game { SystemId = "gba", Path = "games/gba/" + file, Title = parsed.Title, Region = region ?? parsed.Region };
    }

    private static readonly ThumbnailIndex Index = new(
    [
        "Advance Wars (USA)",
        "Advance Wars (USA) (Rev 1)",
        "Advance Wars (USA) (Virtual Console)",
        "Advance Wars (Europe) (En,Fr,De,Es) (Virtual Console)",
        "Advance Wars (Europe) (En,Fr,De,Es)",
        "Advance Wars 2 - Black Hole Rising (USA)",
        "Advance Wars 2 - Black Hole Rising (Europe) (En,Fr,De,Es,It)",
        "Mario _ Luigi - Superstar Saga (Europe) (En,Fr,De,Es,It)",
        "Mario _ Luigi - Superstar Saga (USA)",
        "Pokemon - Emerald Version (USA, Europe)",
        "Kuru Kuru Kururin (Europe)",
        "Kuru Kuru Kururin (Europe) (Beta)",
        "Densetsu no Stafy (Japan)",
        "Densetsu no Stafy (Japan) (Demo)",
        "Legend of Zelda, The - The Minish Cap (Europe) (En,Fr,De,Es,It)",
        "Some Prototype (USA) (Proto)",
        "Banjo-Kazooie - Grunty's Revenge (USA)",
    ]);

    [Theory]
    [InlineData("Advance Wars (USA).gba", "Advance Wars (USA)")]                                 // exact No-Intro name
    [InlineData("Advance Wars (USA) (Rev 1).gba", "Advance Wars (USA) (Rev 1)")]                 // exact wins over "better" tags
    [InlineData("Advance Wars.gba", "Advance Wars (Europe) (En,Fr,De,Es)")]                      // no region: Europe first, no VC
    [InlineData("Advance Wars (U).gba", "Advance Wars (USA)")]                                   // GoodTools (U) → own region USA
    [InlineData("Advance Wars (Japan).gba", "Advance Wars (Europe) (En,Fr,De,Es)")]              // no Japan entry: preference order
    [InlineData("advance_wars_2_black_hole_rising.gba", "Advance Wars 2 - Black Hole Rising (Europe) (En,Fr,De,Es,It)")]
    [InlineData("Mario & Luigi - Superstar Saga (E).gba", "Mario _ Luigi - Superstar Saga (Europe) (En,Fr,De,Es,It)")]
    [InlineData("Mario & Luigi - Superstar Saga (USA).gba", "Mario _ Luigi - Superstar Saga (USA)")]
    [InlineData("Pokemon Emerald.gba", "Pokemon - Emerald Version (USA, Europe)")]               // loose: order/"Version"
    [InlineData("Pokémon - Emerald Version.gba", "Pokemon - Emerald Version (USA, Europe)")]     // diacritics
    [InlineData("Kuru Kuru Kururin.gba", "Kuru Kuru Kururin (Europe)")]                          // Beta avoided
    [InlineData("Densetsu no Stafy (Japan).gba", "Densetsu no Stafy (Japan)")]                   // Demo avoided, own region
    [InlineData("Legend of Zelda, The - The Minish Cap (E).gba", "Legend of Zelda, The - The Minish Cap (Europe) (En,Fr,De,Es,It)")]
    [InlineData("The Legend of Zelda - The Minish Cap.gba", "Legend of Zelda, The - The Minish Cap (Europe) (En,Fr,De,Es,It)")]
    [InlineData("Some Prototype (USA).gba", "Some Prototype (USA) (Proto)")]                    // only a proto exists → take it
    [InlineData("Banjo-Kazooie - Grunty's Revenge.gba", "Banjo-Kazooie - Grunty's Revenge (USA)")]
    public void Fuzzy_match_against_an_index(string file, string expected) => Assert.Equal(expected, Index.Match(G(file)));

    [Theory]
    [InlineData("Advance Wars 3.gba")]          // a sequel is not the original
    [InlineData("Advance.gba")]
    [InlineData("240p Test Suite (Homebrew).gba")]
    [InlineData("Pokemon Ruby.gba")]
    public void No_false_positives(string file) => Assert.Null(Index.Match(G(file)));

    [Fact]
    public void Header_title_is_used_when_the_file_name_is_meaningless()
    {
        var g = new Game { SystemId = "gba", Path = "games/gba/0001.gba", Title = "Advance Wars", Region = "USA" };
        Assert.Equal("Advance Wars (USA)", Index.Match(g));
    }

    [Fact]
    public void Direct_candidates_without_index()
    {
        Assert.Equal(["Golden Sun (USA, Europe)"], LibretroThumbnails.DirectCandidates(G("Golden Sun (USA, Europe).gba")));
        Assert.Equal(["Golden Sun (E)", "Golden Sun (Europe)"], LibretroThumbnails.DirectCandidates(G("Golden Sun (E).gba")));
        Assert.Equal(["Mario _ Luigi", "Mario _ Luigi (Europe)", "Mario _ Luigi (USA)", "Mario _ Luigi (World)"],
            LibretroThumbnails.DirectCandidates(G("Mario & Luigi.gba")));
    }

    // ───────────── language tags of personal collections ("(Deutsch)", "(Englisch, NTSC)") pick the matching box ─────────────

    private static readonly ThumbnailIndex RegionalIndex = new(
    [
        "Example Quest, The - Hidden Kingdom (Europe)",
        "Example Quest, The - Hidden Kingdom (Germany)",
        "Example Quest, The - Hidden Kingdom (France)",
        "Example Quest, The - Hidden Kingdom (Canada) (Fr)",
        "Example Quest, The - Hidden Kingdom (USA)",
        "Example Quest, The - Hidden Kingdom (USA, Europe) (Switch Online)",
        "Reikai Monogatari (Japan)",
        "Reikai Monogatari (Japan) (Rev 1)",
        "Reikai Monogatari (Japan) (Rev 2) (Virtual Console)",
    ]);

    [Theory]
    [InlineData("The Example Quest - Hidden Kingdom (Deutsch).sfc", "Example Quest, The - Hidden Kingdom (Germany)")]
    [InlineData("The Example Quest - Hidden Kingdom (Englisch).sfc", "Example Quest, The - Hidden Kingdom (Europe)")]
    [InlineData("The Example Quest - Hidden Kingdom (Englisch, NTSC).sfc", "Example Quest, The - Hidden Kingdom (USA)")]
    [InlineData("The Example Quest - Hidden Kingdom (Französisch).sfc", "Example Quest, The - Hidden Kingdom (France)")]
    [InlineData("The Example Quest - Hidden Kingdom (French).sfc", "Example Quest, The - Hidden Kingdom (France)")]
    [InlineData("The Example Quest - Hidden Kingdom (NTSC).sfc", "Example Quest, The - Hidden Kingdom (USA)")]
    [InlineData("The Example Quest - Hidden Kingdom (PAL).sfc", "Example Quest, The - Hidden Kingdom (Europe)")]
    [InlineData("The Example Quest - Hidden Kingdom (Spanisch).sfc", "Example Quest, The - Hidden Kingdom (Europe)")]   // no Spanish box: Europe
    [InlineData("The Example Quest - Hidden Kingdom (Chinesisch).sfc", "Example Quest, The - Hidden Kingdom (Europe)")] // no Chinese box: general order
    [InlineData("The Example Quest - Hidden Kingdom.sfc", "Example Quest, The - Hidden Kingdom (Europe)")]
    [InlineData("The Example Quest - Hidden Kingdom (Germany).sfc", "Example Quest, The - Hidden Kingdom (Germany)")]
    [InlineData("Reikai Monogatari (Japanisch).sfc", "Reikai Monogatari (Japan)")]                                     // fewest tags, no VC
    public void Language_tags_prefer_the_matching_region(string file, string expected)
    {
        var parsed = TitleParser.Parse(file);
        var g = new Game { SystemId = "snes", Path = "games/snes/" + file, Title = parsed.Title, Region = parsed.Region };
        Assert.Equal(expected, RegionalIndex.Match(g));
    }

    [Fact]
    public void Preferred_regions_of_language_and_tv_tags()
    {
        Assert.Equal(["Germany", "Europe", "Austria", "Switzerland"], LibretroThumbnails.PreferredRegions("Deutsch"));
        Assert.Equal("USA", LibretroThumbnails.PreferredRegions("Englisch, NTSC")[0]);
        Assert.Equal("Europe", LibretroThumbnails.PreferredRegions("Englisch")[0]);
        Assert.Equal("Germany", LibretroThumbnails.PreferredRegions("Deutsch, PAL")[0]);
        Assert.Equal(["Japan"], LibretroThumbnails.PreferredRegions("Japanisch"));
        Assert.Equal(["USA", "Europe"], LibretroThumbnails.PreferredRegions("USA, Europe"));
        Assert.Empty(LibretroThumbnails.PreferredRegions(null));
        Assert.True(LibretroThumbnails.IsLanguageTag("Englisch, NTSC"));
        Assert.False(LibretroThumbnails.IsLanguageTag("USA, Europe"));
        // without an index: the regions the tag stands for are asked for, never "(Deutsch)" alone
        Assert.Equal(["Example Quest (Deutsch)", "Example Quest (Germany)", "Example Quest (Europe)"],
            LibretroThumbnails.DirectCandidates(G("Example Quest (Deutsch).gba")));
    }

    [Theory]
    [InlineData("games/snes/BS Example Broadcast - Dai-1-wa (Japan) (SoundLink).bs", true)]
    [InlineData("games/snes/BS Example Broadcast (Japan).sfc", true)]
    [InlineData("games/snes/Example Quest (Europe).sfc", false)]
    [InlineData("games/snes/BSX Example.sfc", false)]
    public void Satellaview_games_also_search_the_satellaview_set(string path, bool satellaview)
    {
        var parsed = TitleParser.Parse(Path.GetFileName(path));
        var g = new Game { SystemId = "snes", Path = path, Title = parsed.Title };
        var sets = LibretroThumbnails.SetsFor("snes", LibretroThumbnails.SnesSet, g).Select(s => s.LibretroName).ToArray();
        Assert.Equal(satellaview ? [LibretroThumbnails.SatellaviewSet, LibretroThumbnails.SnesSet] : [LibretroThumbnails.SnesSet], sets);
        // other systems never do
        Assert.Single(LibretroThumbnails.SetsFor("gba", ArtworkTestEnv.GbaLibretro, g));
    }

    [Fact]
    public void Miss_cache_expires_after_seven_days()
    {
        var dir = Directory.CreateTempSubdirectory("retrohub-miss-").FullName;
        try
        {
            var now = new DateTime(2026, 11, 9, 12, 0, 0, DateTimeKind.Utc);
            var file = Path.Combine(dir, "artwork-misses.json");
            var cache = new ArtworkMissCache(file, () => now);
            var key = ArtworkMissCache.KeyFor("gba", "Some Game");
            cache.AddMiss(key);
            cache.Save();

            Assert.True(new ArtworkMissCache(file, () => now.AddDays(6.9)).IsRecentMiss(key));
            Assert.False(new ArtworkMissCache(file, () => now.AddDays(7.1)).IsRecentMiss(key));
            Assert.Equal("gba|some game", key);

            // expired entries are dropped when the file is written again
            var later = new ArtworkMissCache(file, () => now.AddDays(8));
            later.AddMiss(ArtworkMissCache.KeyFor("gba", "Other"));
            later.Save();
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("some game", text);
            Assert.Contains("gba|other", text);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Broken_miss_cache_file_is_ignored()
    {
        var dir = Directory.CreateTempSubdirectory("retrohub-miss-").FullName;
        try
        {
            var file = Path.Combine(dir, "artwork-misses.json");
            File.WriteAllText(file, "{ not json");
            var cache = new ArtworkMissCache(file);
            Assert.False(cache.IsRecentMiss("gba|x"));
            cache.AddMiss("gba|x");
            cache.Save();
            Assert.True(new ArtworkMissCache(file).IsRecentMiss("gba|x"));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Misses_of_the_old_flat_format_are_discarded_once()
    {
        var dir = Directory.CreateTempSubdirectory("retrohub-miss-").FullName;
        try
        {
            var file = Path.Combine(dir, "artwork-misses.json");
            var recorded = DateTime.UtcNow.AddHours(-1).ToString("yyyy-MM-ddTHH:mm:ssZ");
            File.WriteAllText(file, $$"""{ "snes|example quest (deutsch)": "{{recorded}}" }"""); // 0.1.1: key → time of recording
            var cache = new ArtworkMissCache(file);
            Assert.False(cache.IsRecentMiss("snes|example quest (deutsch)"));
            cache.Save(); // rewritten in the new format even without a new miss
            var text = File.ReadAllText(file);
            Assert.Contains($"\"version\": {ArtworkMissCache.FormatVersion}", text);
            Assert.DoesNotContain("example quest", text);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void A_short_miss_expires_early_and_a_long_one_is_capped()
    {
        var dir = Directory.CreateTempSubdirectory("retrohub-miss-").FullName;
        try
        {
            var now = new DateTime(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc);
            var file = Path.Combine(dir, "artwork-misses.json");
            var cache = new ArtworkMissCache(file, () => now);
            cache.AddMiss("gba|short", TimeSpan.FromDays(1));
            cache.AddMiss("gba|long", TimeSpan.FromDays(365));
            cache.Save();
            Assert.True(new ArtworkMissCache(file, () => now.AddHours(23)).IsRecentMiss("gba|short"));
            Assert.False(new ArtworkMissCache(file, () => now.AddHours(25)).IsRecentMiss("gba|short"));
            Assert.True(new ArtworkMissCache(file, () => now.AddDays(6.9)).IsRecentMiss("gba|long"));
            Assert.False(new ArtworkMissCache(file, () => now.AddDays(7.1)).IsRecentMiss("gba|long"));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Image_signature_check()
    {
        Assert.True(ArtworkService.LooksLikeImage(ArtworkTestEnv.Png()));
        Assert.True(ArtworkService.LooksLikeImage(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }));
        Assert.False(ArtworkService.LooksLikeImage("<html>"u8));
        Assert.False(ArtworkService.LooksLikeImage([]));
    }
}
