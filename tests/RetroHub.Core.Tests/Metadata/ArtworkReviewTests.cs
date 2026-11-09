using System.Net;
using RetroHub.Core.Library;
using RetroHub.Core.Metadata;

using Xunit;

namespace RetroHub.Core.Tests.Metadata;

/// <summary>Regression tests for defects found in the review of the artwork module.</summary>
public sealed class ArtworkReviewTests : IDisposable
{
    private readonly ArtworkTestEnv _env = new(online: true);

    public void Dispose() => _env.Dispose();

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---- PC games: the exe name ("Game.exe", "Launcher.exe") is shared by many games ----

    [Fact]
    public async Task Pc_games_with_the_same_exe_name_never_share_a_cover()
    {
        _env.Settings.Current.OnlineCovers = false;
        var alpha = _env.AddGame("games/pc/Alpha/Game.exe", "pc", g => g.Title = "Alpha");
        var beta = _env.AddGame("games/pc/Beta/Game.exe", "pc", g => g.Title = "Beta");
        var picked = _env.Touch("picked/alpha-cover.png", ArtworkTestEnv.Png(7));
        var svc = _env.Service();

        var stored = svc.SetUserCover(alpha.Id, picked);
        var r = await svc.ResolveAsync(null, null, Ct);

        Assert.Equal("artwork/covers/pc/Alpha.png", stored);
        Assert.Null(_env.Reload(beta).CoverPath); // before the fix: Beta got "artwork/covers/pc/Game.png" (Alpha's cover)
        Assert.Equal(0, r.LocalFound);
    }

    [Fact]
    public async Task Pc_game_cover_in_the_shared_folder_is_found_by_title()
    {
        _env.Settings.Current.OnlineCovers = false;
        var g = _env.AddGame("games/pc/Hades/Launcher.exe", "pc", x => x.Title = "Hades");
        _env.Touch("artwork/covers/pc/Hades.jpg", "img");
        _env.Touch("artwork/covers/pc/Launcher.png", "someone else's");

        await _env.Service().ResolveAsync(null, null, Ct);

        Assert.Equal("artwork/covers/pc/Hades.jpg", _env.Reload(g).CoverPath);
    }

    [Fact]
    public async Task Pc_game_image_named_like_the_exe_next_to_it_still_counts()
    {
        _env.Settings.Current.OnlineCovers = false;
        var g = _env.AddGame("games/pc/Hades/Launcher.exe", "pc", x => x.Title = "Hades");
        _env.Touch("games/pc/Hades/Launcher.png", "img");

        await _env.Service().ResolveAsync(null, null, Ct);

        Assert.Equal("games/pc/Hades/Launcher.png", _env.Reload(g).CoverPath);
    }

    // ---- "Cover aus dem Internet laden" switched off while a run is going ----

    [Fact]
    public async Task Switching_online_covers_off_during_a_run_stops_all_further_downloads()
    {
        var names = Enumerable.Range(1, 12).Select(i => $"Game {i} (USA)").ToList();
        var json = ArtworkTestEnv.TreeJson(names);
        _env.Http.Handler = req =>
        {
            if (req.RequestUri!.Host == "api.github.com")
            {
                _env.Settings.Current.OnlineCovers = false; // the user flips the switch while the index loads
                return FakeHttp.Text(json);
            }
            return FakeHttp.Bytes(ArtworkTestEnv.Png());
        };
        foreach (var n in names) _env.AddGame($"games/gba/{n}.gba");

        var r = await _env.Service().ResolveAsync(null, null, Ct);

        Assert.Equal(0, _env.Http.ThumbnailCount);
        Assert.Equal(0, r.Downloaded);
        Assert.Equal(12, r.NotFound);
        // not remembered as "not found": the games are looked up again once the user switches it back on
        Assert.False(File.Exists(_env.Full("data/metadata/artwork-misses.json")));
    }

    // ---- GitHub truncates very large trees (> 100 000 entries / 7 MB) ----

    [Fact]
    public async Task Truncated_index_without_a_match_falls_back_to_direct_names()
    {
        var json = ArtworkTestEnv.TreeJson(["Advance Wars (USA)"]).Replace("\"truncated\":false", "\"truncated\":true");
        _env.Http.Handler = req =>
        {
            if (req.RequestUri!.Host == "api.github.com") return FakeHttp.Text(json);
            return FakeHttp.BoxartName(req.RequestUri) == "Golden Sun (USA, Europe)"
                ? FakeHttp.Bytes(ArtworkTestEnv.Png()) : new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        var g = _env.AddGame("games/gba/Golden Sun (USA, Europe).gba");

        var r = await _env.Service().ResolveAsync(null, null, Ct);

        Assert.Equal(1, r.Downloaded);
        Assert.Equal("artwork/covers/gba/Golden Sun (USA, Europe).png", _env.Reload(g).CoverPath);
    }

    [Fact]
    public async Task Complete_index_without_a_match_sends_no_thumbnail_request()
    {
        var json = ArtworkTestEnv.TreeJson(["Advance Wars (USA)"]);
        _env.Http.Handler = req => req.RequestUri!.Host == "api.github.com" ? FakeHttp.Text(json) : FakeHttp.Bytes(ArtworkTestEnv.Png());
        _env.AddGame("games/gba/Golden Sun (USA, Europe).gba");

        var r = await _env.Service().ResolveAsync(null, null, Ct);

        Assert.Equal(0, _env.Http.ThumbnailCount);
        Assert.Equal(1, r.NotFound);
    }
}
