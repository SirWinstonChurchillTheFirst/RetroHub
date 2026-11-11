using System.Net;
using System.Text.Json;
using RetroHub.Core.Metadata;

using Xunit;

namespace RetroHub.Core.Tests.Metadata;

/// <summary>
/// 0.1.1 field report: the GameTDB database (built on demand by the server, first byte after 40+ s) was cut off by a
/// 60 s limit, every Switch game counted as an error, re-runs failed instantly for 15 minutes; misses recorded by
/// 0.1.1 blocked improved lookups for a week. Fake HTTP only (recorded small fixtures, no network).
/// </summary>
public sealed class CoverRecoveryTests : IDisposable
{
    private readonly ArtworkTestEnv _env = new(online: true);

    public void Dispose() => _env.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Answers www.gametdb.com: the first <paramref name="hangs"/> requests never answer (until cancelled), then the sample ZIP.</summary>
    private sealed class SlowGameTdb(int hangs, Func<HttpRequestMessage, HttpResponseMessage>? other = null) : HttpMessageHandler
    {
        private int _seen;
        public int DatabaseRequests => Volatile.Read(ref _seen);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.Host == "www.gametdb.com")
            {
                if (Interlocked.Increment(ref _seen) <= hangs) await Task.Delay(Timeout.Infinite, ct);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(GameTdbTests.SampleZip()) };
            }
            return other?.Invoke(request) ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }

    private static byte[] Jpeg() => [0xFF, 0xD8, 0xFF, 0xE0, 0, 16, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0];

    [Fact]
    public async Task A_database_that_times_out_once_is_fetched_by_the_retry()
    {
        var handler = new SlowGameTdb(hangs: 1);
        using var http = new HttpClient(handler);
        var store = new GameTdbIndexStore(Path.Combine(_env.Root, "gametdb"), http, timeout: TimeSpan.FromMilliseconds(300), retryDelay: TimeSpan.Zero);

        var index = await store.GetAsync("switch", Ct);

        Assert.NotNull(index);
        Assert.Equal(16, index.Count);
        Assert.Equal(2, handler.DatabaseRequests);
        Assert.True(File.Exists(store.CacheFileFor("switch")));
    }

    [Fact]
    public async Task A_failed_database_is_asked_again_after_a_short_cooldown_not_a_quarter_hour()
    {
        var now = new DateTime(2026, 11, 11, 20, 0, 0, DateTimeKind.Utc);
        var clock = now;
        var handler = new SlowGameTdb(hangs: 2); // both attempts of the first load time out
        using var http = new HttpClient(handler);
        var store = new GameTdbIndexStore(Path.Combine(_env.Root, "gametdb"), http, () => clock, TimeSpan.FromMilliseconds(200), TimeSpan.Zero);

        Assert.Null(await store.GetAsync("switch", Ct));
        Assert.Equal(2, handler.DatabaseRequests);

        clock = now.AddSeconds(30); // within the cool-down: the failure is shared, nothing is sent
        Assert.Null(await store.GetAsync("switch", Ct));
        Assert.Equal(2, handler.DatabaseRequests);

        clock = now + GameTdbIndexStore.RetryAfter + TimeSpan.FromSeconds(1);
        Assert.True(GameTdbIndexStore.RetryAfter <= TimeSpan.FromMinutes(5));
        var index = await store.GetAsync("switch", Ct);
        Assert.NotNull(index);
        Assert.Equal(3, handler.DatabaseRequests);
    }

    [Fact]
    public async Task A_database_timeout_counts_as_error_is_never_a_miss_and_the_next_run_recovers()
    {
        var handler = new SlowGameTdb(hangs: 2, other: req => req.RequestUri!.Host == "art.gametdb.com" ? FakeHttp.Bytes(Jpeg()) : new HttpResponseMessage(HttpStatusCode.NotFound));
        var games = new[]
        {
            _env.AddGame("games/switch/Example Island/Example Island.nsp", "switch"),
            _env.AddGame("games/switch/Other Example/Other Example.xci", "switch"),
        };
        ArtworkService Service() => new(_env.Paths, _env.Repo, _env.Catalog, _env.Settings, new HttpClient(handler))
        { GameTdbTimeout = TimeSpan.FromMilliseconds(200), GameTdbRetryDelay = TimeSpan.Zero };

        using (var first = Service())
        {
            var r = await first.ResolveAsync(null, null, Ct);
            Assert.Equal(2, r.Errors);
            Assert.Equal(0, first.Misses.Count);
            foreach (var g in games) Assert.False(first.Misses.IsRecentMiss(ArtworkService.GameTdbMissKey("switch", ArtworkNames.Stem(g))));
        }

        // the next run (here: after a restart) asks again; the sample database does not know these titles → real misses now
        using var second = Service();
        var again = await second.ResolveAsync(null, null, Ct);
        Assert.Equal(0, again.Errors);
        Assert.Equal(2, again.NotFound);
        Assert.Equal(3, handler.DatabaseRequests);
    }

    [Fact]
    public async Task Libretro_games_do_not_wait_for_a_slow_gametdb_database()
    {
        var release = new TaskCompletionSource();
        var json = ArtworkTestEnv.TreeJson(["Example Racer (USA)"]);
        var handler = new GatedGameTdb(release.Task, req =>
        {
            if (req.RequestUri!.Host == "api.github.com") return FakeHttp.Text(json);
            if (req.RequestUri.Host == "thumbnails.libretro.com") return FakeHttp.Bytes(ArtworkTestEnv.Png());
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        // more Switch games than workers: before, all four workers sat waiting for the database
        for (var i = 1; i <= 6; i++) _env.AddGame($"games/switch/Example {i}/Example {i}.nsp", "switch");
        var gba = _env.AddGame("games/gba/Example Racer (USA).gba");
        using var svc = new ArtworkService(_env.Paths, _env.Repo, _env.Catalog, _env.Settings, new HttpClient(handler))
        { GameTdbTimeout = TimeSpan.FromSeconds(30), GameTdbRetryDelay = TimeSpan.Zero };

        var run = svc.ResolveAsync(null, null, Ct);
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while ((_env.Reload(gba).CoverPath == null || handler.DatabaseRequests == 0) && DateTime.UtcNow < deadline) await Task.Delay(20, Ct);
            Assert.NotNull(_env.Reload(gba).CoverPath); // downloaded while the database was still loading
            Assert.Equal(1, handler.DatabaseRequests);   // the database was asked once, early
        }
        finally { release.TrySetResult(); }
        var r = await run;
        Assert.Equal(1, r.Downloaded);
        Assert.Equal(0, r.Errors);
    }

    /// <summary>www.gametdb.com answers only once <paramref name="gate"/> completes.</summary>
    private sealed class GatedGameTdb(Task gate, Func<HttpRequestMessage, HttpResponseMessage> other) : HttpMessageHandler
    {
        private int _seen;
        public int DatabaseRequests => Volatile.Read(ref _seen);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.Host != "www.gametdb.com") return other(request);
            Interlocked.Increment(ref _seen);
            await gate.WaitAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(GameTdbTests.SampleZip()) };
        }
    }

    [Fact]
    public async Task Misses_left_by_0_1_1_do_not_block_the_lookup_after_the_update()
    {
        var json = ArtworkTestEnv.TreeJson(["Example Quest, The - Hidden Kingdom (Germany)", "Example Quest, The - Hidden Kingdom (Europe)"]);
        _env.Http.Handler = req => req.RequestUri!.Host == "api.github.com" ? FakeHttp.Text(json)
            : FakeHttp.BoxartName(req.RequestUri) is { } n && n.Contains("Hidden Kingdom") ? FakeHttp.Bytes(ArtworkTestEnv.Png())
            : new HttpResponseMessage(HttpStatusCode.NotFound);
        var g = _env.AddGame("games/snes/The Example Quest - Hidden Kingdom (Deutsch).sfc", "snes");
        // 0.1.1 wrote a flat { key: time of recording } file; this miss is an hour old
        Directory.CreateDirectory(_env.Paths.Metadata);
        File.WriteAllText(Path.Combine(_env.Paths.Metadata, "artwork-misses.json"),
            JsonSerializer.Serialize(new Dictionary<string, string>
            {
                [ArtworkMissCache.KeyFor("snes", ArtworkNames.Stem(g))] = DateTime.UtcNow.AddHours(-1).ToString("yyyy-MM-ddTHH:mm:ssZ"),
            }));

        var r = await _env.Service().ResolveAsync(null, null, Ct);

        Assert.Equal(0, r.SkippedRecentMiss);
        Assert.Equal(1, r.Downloaded);
        Assert.Equal("Example Quest, The - Hidden Kingdom (Germany)",
            _env.Http.Requests.Select(q => FakeHttp.BoxartName(q.Url)).OfType<string>().Single());
    }

    [Fact]
    public async Task A_guess_made_without_the_index_is_remembered_for_one_day_only()
    {
        _env.Http.Handler = req => req.RequestUri!.Host == "api.github.com"
            ? new HttpResponseMessage(HttpStatusCode.Forbidden) // rate limit
            : new HttpResponseMessage(HttpStatusCode.NotFound);
        var g = _env.AddGame("games/gba/Example Racer.gba");

        var r = await _env.Service().ResolveAsync(null, null, Ct);

        Assert.Equal(1, r.NotFound);
        using var doc = JsonDocument.Parse(File.ReadAllText(_env.Full("data/metadata/artwork-misses.json")));
        var until = DateTime.Parse(doc.RootElement.GetProperty("misses").GetProperty(ArtworkMissCache.KeyFor("gba", ArtworkNames.Stem(g))).GetString()!,
            System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal);
        Assert.InRange(until - DateTime.UtcNow, TimeSpan.FromHours(23), ArtworkService.UnindexedMissTtl + TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Satellaview_games_get_their_box_from_the_satellaview_set()
    {
        var sat = ArtworkTestEnv.TreeJson(["BS Example Broadcast - Dai-1-wa (Japan) (SoundLink)"]);
        var snes = ArtworkTestEnv.TreeJson(["Example Quest, The - Hidden Kingdom (Europe)"]);
        var asked = new List<string>();
        _env.Http.Handler = req =>
        {
            var u = req.RequestUri!;
            if (u.Host == "api.github.com") return FakeHttp.Text(u.AbsolutePath.Contains("Satellaview") ? sat : snes);
            lock (asked) asked.Add(Uri.UnescapeDataString(u.AbsolutePath));
            return FakeHttp.Bytes(ArtworkTestEnv.Png());
        };
        var bs = _env.AddGame("games/snes/BS Example Broadcast - Dai-1-wa (Japan) (SoundLink).bs", "snes");
        var plain = _env.AddGame("games/snes/The Example Quest - Hidden Kingdom.sfc", "snes");

        var r = await _env.Service().ResolveAsync(null, null, Ct);

        Assert.Equal(2, r.Downloaded);
        Assert.Contains("/Nintendo - Satellaview/Named_Boxarts/BS Example Broadcast - Dai-1-wa (Japan) (SoundLink).png", asked);
        Assert.Contains("/Nintendo - Super Nintendo Entertainment System/Named_Boxarts/Example Quest, The - Hidden Kingdom (Europe).png", asked);
        Assert.Equal("artwork/covers/snes/BS Example Broadcast - Dai-1-wa (Japan) (SoundLink).png", _env.Reload(bs).CoverPath);
        Assert.NotNull(_env.Reload(plain).CoverPath);
        // one listing per set, cached apart
        Assert.True(File.Exists(_env.Full("data/metadata/libretro-thumbnails/snes-satellaview.json")));
        Assert.True(File.Exists(_env.Full("data/metadata/libretro-thumbnails/snes.json")));
    }
}
