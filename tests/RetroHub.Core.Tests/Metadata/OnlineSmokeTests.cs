using System.Diagnostics;
using RetroHub.Core.Library;
using RetroHub.Core.Metadata;

using Xunit;

namespace RetroHub.Core.Tests.Metadata;

/// <summary>
/// Real network: GitHub API + thumbnails.libretro.com. Opt-in only (they cost GitHub's unauthenticated rate limit of
/// 60 requests/hour): set RETROHUB_ONLINE_TESTS=1, e.g.
/// <c>$env:RETROHUB_ONLINE_TESTS=1; dotnet test --project tests/RetroHub.Core.Tests --filter-trait "Category=Online"</c>.
/// Everything is written to a temp folder and deleted afterwards.
/// </summary>
[Trait("Category", "Online")]
public sealed class OnlineSmokeTests
{
    private static bool Enabled => Environment.GetEnvironmentVariable("RETROHUB_ONLINE_TESTS") == "1";

    [Fact]
    public async Task Fetches_the_gba_index_and_downloads_one_real_boxart()
    {
        Assert.SkipUnless(Enabled, "set RETROHUB_ONLINE_TESTS=1 to run the online smoke test");
        var ct = TestContext.Current.CancellationToken;
        var output = new Report(TestContext.Current.TestOutputHelper);
        var temp = Directory.CreateTempSubdirectory("retrohub-online-").FullName;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
            var store = new ThumbnailIndexStore(Path.Combine(temp, "index"), http);

            var sw = Stopwatch.StartNew();
            var index = await store.GetAsync("gba", "Nintendo - Game Boy Advance", ct);
            var indexMs = sw.ElapsedMilliseconds;
            Assert.NotNull(index);
            var cacheFile = new FileInfo(store.CacheFileFor("gba"));
            output.WriteLine($"GBA index: {index.Count} boxarts in {indexMs} ms, cache file {cacheFile.Length / 1024} KB");
            Assert.True(index.Count > 3000);
            Assert.True(index.Contains("Advance Wars (USA)"));

            var game = new Game { SystemId = "gba", Path = "games/gba/Advance Wars (USA).gba", Title = "Advance Wars", Region = "USA" };
            Assert.Equal("Advance Wars (USA)", index.Match(game));

            // the download path of the service (temp root; online setting irrelevant for DownloadAsync)
            using var env = new ArtworkTestEnv(online: true);
            using var svc = new ArtworkService(env.Paths, env.Repo, env.Catalog, env.Settings, http);
            var target = Path.Combine(temp, "covers", "Advance Wars (USA).png");
            sw.Restart();
            var r = await svc.DownloadAsync(LibretroThumbnails.BoxartUrl("Nintendo - Game Boy Advance", "Advance Wars (USA)"), target, ct);
            var dlMs = sw.ElapsedMilliseconds;
            var size = File.Exists(target) ? new FileInfo(target).Length : -1;
            output.WriteLine($"Download 'Advance Wars (USA)': {r}, {size} bytes in {dlMs} ms");
            Assert.Equal(ArtworkService.DownloadResult.Saved, r);
            Assert.True(size > 10_000);
            var head = new byte[8];
            await using (var fs = File.OpenRead(target)) Assert.Equal(8, await fs.ReadAsync(head, ct));
            Assert.True(ArtworkService.LooksLikeImage(head));

            // the four local homebrew test ROMs (D:\RetroHub\games): are they in libretro-thumbnails?
            foreach (var (sys, lib, file) in new[]
                     {
                         ("gba", "Nintendo - Game Boy Advance", "240p Test Suite (Homebrew).gba"),
                         ("gb", "Nintendo - Game Boy", "240p Test Suite (Homebrew).gb"),
                         ("gbc", "Nintendo - Game Boy Color", "uCity (Homebrew).gbc"),
                         ("nes", "Nintendo - Nintendo Entertainment System", "240p Test Suite (Homebrew).nes"),
                     })
            {
                var idx = await store.GetAsync(sys, lib, ct);
                var parsed = TitleParser.Parse(file);
                var g = new Game { SystemId = sys, Path = $"games/{sys}/{file}", Title = parsed.Title, Region = parsed.Region };
                var match = idx?.Match(g);
                var similar = idx?.Names.Where(n => n.Contains(parsed.Title.Split(' ')[0], StringComparison.OrdinalIgnoreCase)
                                                     && n.Contains(parsed.Title.Split(' ').Last(), StringComparison.OrdinalIgnoreCase)).Take(5).ToList() ?? [];
                output.WriteLine($"{sys}/{file}: index {(idx == null ? "unavailable" : idx.Count + " boxarts")}, match: {match ?? "(none)"}; similar names: {string.Join(" | ", similar)}");
            }
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
            Assert.False(Directory.Exists(temp));
            output.WriteLine($"temp folder {temp} deleted: {!Directory.Exists(temp)}");
            output.Flush();
        }
    }

    /// <summary>
    /// GameTDB for Nintendo Switch: the real database (one ZIP, ~1.9 MB) is parsed and eight well-known game names
    /// match the right product codes; then the real pipeline (ArtworkService, online on) downloads two covers – one
    /// with a German box, one that falls back to English. Three requests to art.gametdb.com at most per game.
    /// </summary>
    [Fact]
    public async Task Fetches_the_switch_database_from_gametdb_and_downloads_real_covers()
    {
        Assert.SkipUnless(Enabled, "set RETROHUB_ONLINE_TESTS=1 to run the online smoke test");
        var ct = TestContext.Current.CancellationToken;
        var output = new Report(TestContext.Current.TestOutputHelper);
        using var env = new ArtworkTestEnv(online: true);
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
        try
        {
            var store = new GameTdbIndexStore(Path.Combine(env.Root, "gametdb"), http);
            var sw = Stopwatch.StartNew();
            var index = await store.GetAsync("switch", ct);
            Assert.NotNull(index);
            output.WriteLine($"SwitchTDB: {index.Count} games in {sw.ElapsedMilliseconds} ms, cache {new FileInfo(store.CacheFileFor("switch")).Length / 1024} KB");
            Assert.True(index.Count > 20_000);
            foreach (var (title, id) in new[]
                     {
                         ("The Legend of Zelda - Breath of the Wild", "AAAAA"), ("Hyrule Warriors - Zeit der Verheerung", "AXEAB"),
                         ("Hyrule Warriors - Definitive Edition", "AKUTB"), ("Cadence of Hyrule", "ASWBD"),
                         ("The Legend of Zelda - Link's Awakening", "AR3NA"), ("The Legend of Zelda - Echoes of Wisdom", "BDGEA"),
                         ("The Legend of Zelda - Skyward Sword HD", "AZ89A"), ("The Legend of Zelda - Tears of the Kingdom", "AXN7A"),
                     })
            {
                var match = index.Match(title);
                output.WriteLine($"  {title} -> {match ?? "(none)"}");
                Assert.Equal(id, match);
            }

            var aoc = env.AddGame("games/switch/Hyrule Warriors - Zeit der Verheerung/Hyrule Warriors - Zeit der Verheerung.nsp", "switch");
            var cadence = env.AddGame("games/switch/Cadence of Hyrule/Cadence of Hyrule.nsp", "switch");
            using var svc = new ArtworkService(env.Paths, env.Repo, env.Catalog, env.Settings, http);
            var r = await svc.ResolveAsync([aoc, cadence], null, ct);
            output.WriteLine($"Pipeline: {r.Downloaded} downloaded, {r.NotFound} not found, {r.Errors} errors, {svc.HttpRequests} requests");
            Assert.Equal(2, r.Downloaded);
            foreach (var g in new[] { aoc, cadence })
            {
                var cover = env.Reload(g).CoverPath!;
                var full = env.Full(cover);
                var size = ImageHeader.TryRead(full);
                output.WriteLine($"  {g.Title}: {cover}, {new FileInfo(full).Length} bytes, {size?.Width}x{size?.Height}");
                Assert.EndsWith(".jpg", cover);
                Assert.Equal(352, size?.Width);
            }
        }
        finally
        {
            output.Flush();
        }
    }

    /// <summary>Test output, also appended to the file named by RETROHUB_ONLINE_REPORT (MTP hides output of passing tests).</summary>
    private sealed class Report(ITestOutputHelper? helper)
    {
        private readonly List<string> _lines = new();
        public void WriteLine(string line)
        {
            helper?.WriteLine(line);
            _lines.Add(line);
        }
        public void Flush()
        {
            var file = Environment.GetEnvironmentVariable("RETROHUB_ONLINE_REPORT");
            if (!string.IsNullOrWhiteSpace(file)) File.AppendAllLines(file, _lines.Prepend($"--- {DateTime.Now:yyyy-MM-dd HH:mm:ss}"));
        }
    }
}
