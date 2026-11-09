using System.Net;
using RetroHub.Core.Metadata;

using Xunit;

namespace RetroHub.Core.Tests.Metadata;

/// <summary>Snaps and title screens (DESIGN 7.1/10.1): local lookup and the on-demand libretro fetch for one game.</summary>
public sealed class ScreenArtworkTests : IDisposable
{
    private readonly ArtworkTestEnv _env = new(online: true);

    private static readonly string[] GbaIndex =
    [
        "Advance Wars (USA)",
        "Advance Wars (Europe) (En,Fr,De,Es)",
        "Pokemon - Emerald Version (USA, Europe)",
    ];

    public void Dispose() => _env.Dispose();

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>GitHub returns the boxart index; thumbnails.libretro.com serves the folders listed in <paramref name="serve"/>.</summary>
    private void Serve(params string[] serve)
    {
        var json = ArtworkTestEnv.TreeJson(GbaIndex);
        var names = GbaIndex.ToHashSet();
        _env.Http.Handler = req =>
        {
            var url = req.RequestUri!;
            if (url.Host == "api.github.com") return FakeHttp.Text(json);
            var folder = Uri.UnescapeDataString(url.AbsolutePath).Split('/')[2];
            var name = FakeHttp.BoxartName(url);
            return serve.Contains(folder) && name != null && names.Contains(name)
                ? FakeHttp.Bytes(ArtworkTestEnv.Png(folder == "Named_Snaps" ? (byte)2 : (byte)3))
                : new HttpResponseMessage(HttpStatusCode.NotFound);
        };
    }

    [Theory]
    [InlineData(ArtworkKind.Cover, "/Nintendo%20-%20Game%20Boy%20Advance/Named_Boxarts/Advance%20Wars%20%28USA%29.png")]
    [InlineData(ArtworkKind.Snap, "/Nintendo%20-%20Game%20Boy%20Advance/Named_Snaps/Advance%20Wars%20%28USA%29.png")]
    [InlineData(ArtworkKind.Title, "/Nintendo%20-%20Game%20Boy%20Advance/Named_Titles/Advance%20Wars%20%28USA%29.png")]
    public void Thumbnail_urls_use_the_folder_of_the_kind(ArtworkKind kind, string path)
    {
        var url = LibretroThumbnails.ThumbnailUrl(ArtworkTestEnv.GbaLibretro, kind, "Advance Wars (USA)");
        Assert.Equal("thumbnails.libretro.com", url.Host);
        Assert.Equal(path, url.AbsolutePath);
        Assert.Throws<ArgumentOutOfRangeException>(() => LibretroThumbnails.FolderFor(ArtworkKind.Logo));
    }

    [Fact]
    public void Local_snaps_and_titles_are_found_in_the_artwork_folders_and_es_de_media()
    {
        var aw = _env.AddGame("games/gba/Advance Wars (USA).gba");
        var poke = _env.AddGame("games/gba/Pokemon Emerald (E).gba");
        _env.Touch("artwork/snaps/gba/Advance Wars (USA).png", ArtworkTestEnv.Png());
        _env.Touch("artwork/titles/gameboyadvance/advance wars.jpg", ArtworkTestEnv.Png());
        _env.Touch("games/gba/media/titlescreens/Pokemon Emerald (E).png", ArtworkTestEnv.Png());
        _env.Touch("games/gba/media/covers/Pokemon Emerald (E).png", ArtworkTestEnv.Png()); // a cover is never a screen

        var service = _env.Service();
        var a = service.FindScreens(aw);
        var p = service.FindScreens(poke);

        Assert.Equal(_env.Full("artwork/snaps/gba/Advance Wars (USA).png"), a.Snap);
        Assert.Equal(_env.Full("artwork/titles/gameboyadvance/advance wars.jpg"), a.Title);
        Assert.Equal(a.Snap, a.Best);
        Assert.Null(p.Snap);
        Assert.Equal(_env.Full("games/gba/media/titlescreens/Pokemon Emerald (E).png"), p.Title);
        Assert.Equal(p.Title, p.Best); // no snap: the title screen
        Assert.Equal(0, _env.Http.Count);
    }

    [Fact]
    public async Task Online_off_returns_what_is_on_disk_without_any_request()
    {
        using var env = new ArtworkTestEnv(online: false);
        env.Http.Handler = _ => throw new InvalidOperationException("no request expected");
        var g = env.AddGame("games/gba/Advance Wars (USA).gba");
        env.Touch("artwork/snaps/gba/Advance Wars (USA).png", ArtworkTestEnv.Png());

        var r = await env.Service().FetchScreensAsync(g, Ct);

        Assert.Equal(env.Full("artwork/snaps/gba/Advance Wars (USA).png"), r.Snap);
        Assert.Null(r.Title);
        Assert.Equal(0, env.Http.Count);
    }

    [Fact]
    public async Task Snap_and_title_are_downloaded_under_the_matched_name_and_the_rom_name()
    {
        Serve("Named_Snaps", "Named_Titles");
        var g = _env.AddGame("games/gba/pokemon emerald.gba");

        var r = await _env.Service().FetchScreensAsync(g, Ct);

        Assert.Equal(_env.Full("artwork/snaps/gba/pokemon emerald.png"), r.Snap);
        Assert.Equal(_env.Full("artwork/titles/gba/pokemon emerald.png"), r.Title);
        Assert.Equal(ArtworkTestEnv.Png(2), File.ReadAllBytes(r.Snap!));
        Assert.Equal(ArtworkTestEnv.Png(3), File.ReadAllBytes(r.Title!));
        var thumbs = _env.Http.Requests.Where(q => q.Url.Host == "thumbnails.libretro.com").ToList();
        Assert.Equal(2, thumbs.Count);
        Assert.All(thumbs, q => Assert.Equal("Pokemon - Emerald Version (USA, Europe)", FakeHttp.BoxartName(q.Url)));
        Assert.All(_env.Http.Requests, q => Assert.Equal("RetroHub/0.1", q.UserAgent));
        // the cover is not touched, and the game's database row is unchanged
        Assert.Null(_env.Reload(g).CoverPath);
        // found on disk now: a second call sends nothing
        var before = _env.Http.Count;
        var again = await _env.Service().FetchScreensAsync(g, Ct);
        Assert.Equal(r, again);
        Assert.Equal(before, _env.Http.Count);
    }

    [Fact]
    public async Task A_missing_title_screen_is_remembered_and_not_asked_again()
    {
        Serve("Named_Snaps"); // no title screens on the server
        var g = _env.AddGame("games/gba/Advance Wars (USA).gba");

        var r1 = await _env.Service().FetchScreensAsync(g, Ct);
        Assert.NotNull(r1.Snap);
        Assert.Null(r1.Title);
        var titleAsks = _env.Http.Requests.Count(q => q.Url.AbsolutePath.Contains("/Named_Titles/"));
        Assert.Equal(1, titleAsks);

        var r2 = await _env.Service().FetchScreensAsync(g, Ct); // new service: the miss is read from disk
        Assert.Equal(r1, r2);
        Assert.Equal(1, _env.Http.Requests.Count(q => q.Url.AbsolutePath.Contains("/Named_Titles/")));
        Assert.True(File.Exists(_env.Full("data/metadata/artwork-misses.json")));
    }

    [Fact]
    public async Task An_existing_file_is_never_overwritten()
    {
        Serve("Named_Snaps", "Named_Titles");
        var g = _env.AddGame("games/gba/Advance Wars (USA).gba");
        _env.Touch("artwork/titles/gba/Advance Wars (USA).png", ArtworkTestEnv.Png(9));

        var r = await _env.Service().FetchScreensAsync(g, Ct);

        Assert.Equal(ArtworkTestEnv.Png(9), File.ReadAllBytes(r.Title!));
        Assert.DoesNotContain(_env.Http.Requests, q => q.Url.AbsolutePath.Contains("/Named_Titles/"));
        Assert.NotNull(r.Snap);
    }
}
