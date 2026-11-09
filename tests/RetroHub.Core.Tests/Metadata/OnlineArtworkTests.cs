using System.Net;
using RetroHub.Core.Metadata;

using Xunit;

namespace RetroHub.Core.Tests.Metadata;

public sealed class OnlineArtworkTests : IDisposable
{
    private readonly ArtworkTestEnv _env = new(online: true);

    private static readonly string[] GbaIndex =
    [
        "Advance Wars (USA)",
        "Advance Wars (USA) (Rev 1)",
        "Advance Wars (USA) (Virtual Console)",
        "Advance Wars (Europe) (En,Fr,De,Es)",
        "Advance Wars 2 - Black Hole Rising (USA)",
        "Mario _ Luigi - Superstar Saga (Europe) (En,Fr,De,Es,It)",
        "Pokemon - Emerald Version (USA, Europe)",
    ];

    public void Dispose() => _env.Dispose();

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>GitHub returns <paramref name="index"/>; thumbnails.libretro.com serves a PNG for every name in it.</summary>
    private void ServeIndex(IEnumerable<string> index)
    {
        var names = index.ToHashSet();
        var json = ArtworkTestEnv.TreeJson(names);
        _env.Http.Handler = req =>
        {
            if (req.RequestUri!.Host == "api.github.com") return FakeHttp.Text(json);
            var name = FakeHttp.BoxartName(req.RequestUri);
            return name != null && names.Contains(name) ? FakeHttp.Bytes(ArtworkTestEnv.Png()) : new HttpResponseMessage(HttpStatusCode.NotFound);
        };
    }

    [Fact]
    public async Task Online_off_sends_no_http_request_at_all()
    {
        using var env = new ArtworkTestEnv(online: false);
        env.Http.Handler = _ => throw new InvalidOperationException("no request expected");
        var g = env.AddGame("games/gba/Advance Wars (USA).gba");

        var r = await env.Service().ResolveAsync(null, null, Ct);

        Assert.Equal(0, env.Http.Count);
        Assert.Equal(new ArtworkReport(0, 0, 1, 0), r with { ExtrasFound = 0, Checked = 0, Duration = default, SkippedRecentMiss = 0 });
        Assert.Null(env.Reload(g).CoverPath);
        Assert.False(Directory.Exists(env.Full("data/metadata/libretro-thumbnails")));
    }

    [Fact]
    public async Task Downloads_the_matched_boxart_with_a_polite_user_agent()
    {
        ServeIndex(GbaIndex);
        var g = _env.AddGame("games/gba/Advance Wars (USA).gba");

        var r = await _env.Service().ResolveAsync(null, null, Ct);

        Assert.Equal(1, r.Downloaded);
        Assert.Equal(0, r.NotFound);
        Assert.Equal("artwork/covers/gba/Advance Wars (USA).png", _env.Reload(g).CoverPath);
        Assert.Equal(ArtworkTestEnv.Png(), File.ReadAllBytes(_env.Full("artwork/covers/gba/Advance Wars (USA).png")));
        Assert.All(_env.Http.Requests, q => Assert.Equal("RetroHub/0.1", q.UserAgent));
        var github = Assert.Single(_env.Http.Requests, q => q.Url.Host == "api.github.com");
        Assert.Equal("/repos/libretro-thumbnails/Nintendo_-_Game_Boy_Advance/git/trees/master", github.Url.AbsolutePath);
        Assert.Equal("?recursive=1", github.Url.Query);
        var thumb = Assert.Single(_env.Http.Requests, q => q.Url.Host == "thumbnails.libretro.com");
        Assert.Equal("Advance Wars (USA)", FakeHttp.BoxartName(thumb.Url));
        Assert.StartsWith("/Nintendo%20-%20Game%20Boy%20Advance/Named_Boxarts/", thumb.Url.AbsolutePath);
        // index cached on disk
        Assert.True(File.Exists(_env.Full("data/metadata/libretro-thumbnails/gba.json")));
        // no temp files left behind
        Assert.Single(Directory.GetFiles(_env.Full("artwork/covers/gba"), "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Fuzzy_match_downloads_under_the_rom_name()
    {
        ServeIndex(GbaIndex);
        var mario = _env.AddGame("games/gba/Mario & Luigi - Superstar Saga (E).gba");
        var poke = _env.AddGame("games/gba/pokemon emerald.gba");

        var r = await _env.Service().ResolveAsync(null, null, Ct);

        Assert.Equal(2, r.Downloaded);
        Assert.Equal("artwork/covers/gba/Mario & Luigi - Superstar Saga (E).png", _env.Reload(mario).CoverPath);
        Assert.Equal("artwork/covers/gba/pokemon emerald.png", _env.Reload(poke).CoverPath);
        var asked = _env.Http.Requests.Select(q => FakeHttp.BoxartName(q.Url)).OfType<string>().Order().ToList();
        Assert.Equal(["Mario _ Luigi - Superstar Saga (Europe) (En,Fr,De,Es,It)", "Pokemon - Emerald Version (USA, Europe)"], asked);
    }

    [Fact]
    public async Task Index_is_fetched_once_and_reused_from_disk()
    {
        ServeIndex(GbaIndex);
        _env.AddGame("games/gba/Advance Wars (USA).gba");
        _env.AddGame("games/gba/Advance Wars 2 - Black Hole Rising (USA).gba");

        await _env.Service().ResolveAsync(null, null, Ct);
        Assert.Equal(1, _env.Http.GitHubCount);

        _env.AddGame("games/gba/Pokemon - Emerald Version (USA, Europe).gba");
        var r = await _env.Service().ResolveAsync(null, null, Ct); // a new service: only the disk cache is shared

        Assert.Equal(1, r.Downloaded);
        Assert.Equal(1, _env.Http.GitHubCount);
        Assert.Equal(3, _env.Http.ThumbnailCount);
    }

    [Fact]
    public async Task Unknown_game_is_remembered_as_miss_and_not_requested_again()
    {
        ServeIndex(GbaIndex);
        var g = _env.AddGame("games/gba/240p Test Suite (Homebrew).gba");

        var r1 = await _env.Service().ResolveAsync(null, null, Ct);
        var requestsAfterFirst = _env.Http.Count;
        var r2 = await _env.Service().ResolveAsync(null, null, Ct);

        Assert.Equal(1, r1.NotFound);
        Assert.Equal(0, _env.Http.ThumbnailCount); // the index already says "not there": no 404 needed
        Assert.Equal(requestsAfterFirst, _env.Http.Count); // second run: zero requests (index on disk + miss cache)
        Assert.Equal(1, r2.SkippedRecentMiss);
        Assert.Null(_env.Reload(g).CoverPath);
        Assert.Contains("gba|240p test suite (homebrew)", File.ReadAllText(_env.Full("data/metadata/artwork-misses.json")));
    }

    [Fact]
    public async Task Without_index_direct_names_are_tried_and_404_is_remembered()
    {
        // GitHub fails (rate limit) → direct candidates on thumbnails.libretro.com, all 404
        _env.Http.Handler = req => req.RequestUri!.Host == "api.github.com"
            ? new HttpResponseMessage(HttpStatusCode.Forbidden)
            : new HttpResponseMessage(HttpStatusCode.NotFound);
        _env.AddGame("games/gba/Some Game.gba");

        var r = await _env.Service().ResolveAsync(null, null, Ct);

        Assert.Equal(1, r.NotFound);
        Assert.Equal(0, r.Errors);
        var names = _env.Http.Requests.Select(q => FakeHttp.BoxartName(q.Url)).OfType<string>().ToList();
        Assert.Equal(["Some Game", "Some Game (Europe)", "Some Game (USA)", "Some Game (World)"], names);

        var before = _env.Http.Count;
        await _env.Service().ResolveAsync(null, null, Ct);
        Assert.Equal(before, _env.Http.Count); // the remembered 404 is checked first: no request at all
    }

    [Fact]
    public async Task Direct_mode_finds_a_no_intro_named_rom()
    {
        _env.Http.Handler = req =>
        {
            if (req.RequestUri!.Host == "api.github.com") throw new HttpRequestException("offline (test)");
            return FakeHttp.BoxartName(req.RequestUri) == "Golden Sun (USA, Europe)"
                ? FakeHttp.Bytes(ArtworkTestEnv.Png()) : new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        var g = _env.AddGame("games/gba/Golden Sun (USA, Europe).gba");

        var r = await _env.Service().ResolveAsync(null, null, Ct);

        Assert.Equal(1, r.Downloaded);
        Assert.Equal("artwork/covers/gba/Golden Sun (USA, Europe).png", _env.Reload(g).CoverPath);
    }

    [Fact]
    public async Task Network_failures_are_counted_never_thrown_and_not_cached_as_miss()
    {
        _env.Http.Handler = _ => throw new HttpRequestException("no route to host (test)");
        var g = _env.AddGame("games/gba/Advance Wars (USA).gba");

        var r = await _env.Service().ResolveAsync(null, null, Ct);

        Assert.Equal(1, r.Errors);
        Assert.Equal(1, r.NotFound);
        Assert.Null(_env.Reload(g).CoverPath);
        Assert.False(File.Exists(_env.Full("data/metadata/artwork-misses.json")) &&
                     File.ReadAllText(_env.Full("data/metadata/artwork-misses.json")).Contains("advance wars"));
    }

    [Fact]
    public async Task Download_never_overwrites_an_existing_file()
    {
        var target = _env.Touch("artwork/covers/gba/Advance Wars (USA).png", "user file");
        _env.Http.Handler = _ => FakeHttp.Bytes(ArtworkTestEnv.Png());
        var svc = _env.Service();

        var r = await svc.DownloadAsync(LibretroThumbnails.BoxartUrl(ArtworkTestEnv.GbaLibretro, "Advance Wars (USA)"), target, Ct);

        Assert.Equal(ArtworkService.DownloadResult.AlreadyExists, r);
        Assert.Equal("user file", File.ReadAllText(target));
        Assert.Equal(0, _env.Http.Count);
    }

    [Fact]
    public async Task Interrupted_download_leaves_neither_target_nor_temp_file()
    {
        _env.Http.Handler = _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new BrokenStream(ArtworkTestEnv.Png())) };
        var target = _env.Full("artwork/covers/gba/X.png");

        var r = await _env.Service().DownloadAsync(new Uri("https://thumbnails.libretro.com/x/Named_Boxarts/X.png"), target, Ct);

        Assert.Equal(ArtworkService.DownloadResult.Error, r);
        Assert.False(File.Exists(target));
        Assert.Empty(Directory.GetFiles(_env.Full("artwork/covers/gba")));
    }

    [Fact]
    public async Task Non_image_responses_are_discarded()
    {
        _env.Http.Handler = _ => FakeHttp.Text("<html>captive portal</html>");
        var target = _env.Full("artwork/covers/gba/X.png");

        var r = await _env.Service().DownloadAsync(new Uri("https://thumbnails.libretro.com/x/Named_Boxarts/X.png"), target, Ct);

        Assert.Equal(ArtworkService.DownloadResult.Error, r);
        Assert.False(File.Exists(target));
        Assert.Empty(Directory.GetFiles(_env.Full("artwork/covers/gba")));
    }

    [Fact]
    public async Task At_most_four_downloads_run_in_parallel()
    {
        var names = Enumerable.Range(1, 12).Select(i => $"Game {i} (USA)").ToList();
        var json = ArtworkTestEnv.TreeJson(names);
        int running = 0, peak = 0;
        _env.Http.Handler = req =>
        {
            if (req.RequestUri!.Host == "api.github.com") return FakeHttp.Text(json);
            var now = Interlocked.Increment(ref running);
            InterlockedMax(ref peak, now);
            Thread.Sleep(40);
            Interlocked.Decrement(ref running);
            return FakeHttp.Bytes(ArtworkTestEnv.Png());
        };
        foreach (var n in names) _env.AddGame($"games/gba/{n}.gba");

        var r = await _env.Service().ResolveAsync(null, null, Ct);

        Assert.Equal(12, r.Downloaded);
        Assert.InRange(peak, 1, ArtworkService.MaxParallelDownloads);
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int cur;
        while (value > (cur = Volatile.Read(ref target)) && Interlocked.CompareExchange(ref target, value, cur) != cur) { }
    }

    [Fact]
    public async Task Local_cover_wins_and_no_request_is_sent()
    {
        ServeIndex(GbaIndex);
        var g = _env.AddGame("games/gba/Advance Wars (USA).gba");
        _env.Touch("artwork/covers/gba/Advance Wars.jpg", "mine");

        var r = await _env.Service().ResolveAsync(null, null, Ct);

        Assert.Equal(1, r.LocalFound);
        Assert.Equal(0, _env.Http.Count);
        Assert.Equal("artwork/covers/gba/Advance Wars.jpg", _env.Reload(g).CoverPath);
    }

    [Fact]
    public async Task Systems_without_libretro_name_are_not_looked_up_online()
    {
        ServeIndex(GbaIndex);
        _env.AddGame("games/pc/Game/game.exe", "pc");

        var r = await _env.Service().ResolveAsync(null, null, Ct);

        Assert.Equal(1, r.NotFound);
        Assert.Equal(0, _env.Http.Count);
    }
}
