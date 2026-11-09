using RetroHub.Core.Metadata;

using Xunit;

namespace RetroHub.Core.Tests.Metadata;

public sealed class LocalArtworkTests : IDisposable
{
    private readonly ArtworkTestEnv _env = new(online: false);

    public void Dispose() => _env.Dispose();

    private async Task<ArtworkReport> Resolve() =>
        await _env.Service().ResolveAsync(null, null, TestContext.Current.CancellationToken);

    [Fact]
    public async Task Finds_cover_by_rom_file_name_in_artwork_covers()
    {
        var g = _env.AddGame("games/gba/Advance Wars (USA).gba");
        _env.Touch("artwork/covers/gba/Advance Wars (USA).png", "img");

        var r = await Resolve();

        Assert.Equal(1, r.LocalFound);
        Assert.Equal(0, r.NotFound);
        Assert.Equal("artwork/covers/gba/Advance Wars (USA).png", _env.Reload(g).CoverPath);
    }

    [Fact]
    public async Task Matching_is_case_insensitive_and_accepts_every_system_folder_and_extension()
    {
        var g = _env.AddGame("games/gba/Advance Wars (USA).gba");
        _env.Touch("artwork/covers/GameBoyAdvance/ADVANCE WARS (usa).JPEG", "img");

        await Resolve();

        // the folder is matched case-insensitively (Windows paths); the stored casing may follow the catalog
        Assert.Equal("artwork/covers/GameBoyAdvance/ADVANCE WARS (usa).JPEG", _env.Reload(g).CoverPath, ignoreCase: true);
        Assert.True(File.Exists(_env.Full(_env.Reload(g).CoverPath!)));
    }

    [Theory]
    [InlineData("Advance Wars.webp")]          // cleaned title
    [InlineData("Advance Wars (Europe).jpg")]  // other region of the same title (normalised title match)
    [InlineData("advance-wars.png")]           // normalised title
    public async Task Accepts_cleaned_title_and_normalised_names(string file)
    {
        var g = _env.AddGame("games/gba/Advance Wars (USA).gba");
        _env.Touch("artwork/covers/gba/" + file, "img");

        await Resolve();

        Assert.Equal("artwork/covers/gba/" + file, _env.Reload(g).CoverPath);
    }

    [Fact]
    public async Task Png_wins_over_jpg_with_the_same_name()
    {
        var g = _env.AddGame("games/gba/Golden Sun (Europe).gba");
        _env.Touch("artwork/covers/gba/Golden Sun (Europe).jpg", "jpg");
        _env.Touch("artwork/covers/gba/Golden Sun (Europe).png", "png");

        await Resolve();

        Assert.EndsWith(".png", _env.Reload(g).CoverPath);
    }

    [Fact]
    public async Task Finds_cover_next_to_the_rom()
    {
        var g = _env.AddGame("games/gba/Homebrew Game.gba");
        _env.Touch("games/gba/Homebrew Game.png", "img");

        await Resolve();

        Assert.Equal("games/gba/Homebrew Game.png", _env.Reload(g).CoverPath);
    }

    [Fact]
    public async Task Pc_game_folder_cover_or_folder_jpg_but_not_a_shared_system_folder()
    {
        var own = _env.AddGame("games/pc/My Game/game.exe", "pc");
        _env.Touch("games/pc/My Game/folder.jpg", "img");
        var loose = _env.AddGame("games/pc/loose.exe", "pc");
        _env.Touch("games/pc/cover.png", "img"); // in the shared games/pc folder: must not become everybody's cover

        var r = await Resolve();

        Assert.Equal("games/pc/My Game/folder.jpg", _env.Reload(own).CoverPath);
        Assert.Null(_env.Reload(loose).CoverPath);
        Assert.Equal(1, r.LocalFound);
        Assert.Equal(1, r.NotFound);
    }

    [Fact]
    public async Task Rom_folder_cover_png_is_not_used_for_roms()
    {
        var g = _env.AddGame("games/gba/Some Game.gba");
        _env.Touch("games/gba/cover.png", "img");

        await Resolve();

        Assert.Null(_env.Reload(g).CoverPath);
    }

    [Fact]
    public async Task Es_de_and_batocera_layouts()
    {
        var esde = _env.AddGame("games/gba/Game One.gba");
        _env.Touch("games/gba/media/covers/Game One.png", "img");
        var bato = _env.AddGame("games/gba/Game Two.gba");
        _env.Touch("games/gba/downloaded_images/Game Two-image.png", "img");
        var bato2 = _env.AddGame("games/gba/Game Three.gba");
        _env.Touch("games/gba/images/Game Three-image.jpg", "img");

        await Resolve();

        Assert.Equal("games/gba/media/covers/Game One.png", _env.Reload(esde).CoverPath);
        Assert.Equal("games/gba/downloaded_images/Game Two-image.png", _env.Reload(bato).CoverPath);
        Assert.Equal("games/gba/images/Game Three-image.jpg", _env.Reload(bato2).CoverPath);
    }

    [Fact]
    public async Task Backgrounds_and_logos_are_found_locally()
    {
        var g = _env.AddGame("games/gba/Advance Wars (USA).gba");
        _env.Touch("artwork/backgrounds/gba/Advance Wars (USA).jpg", "img");
        _env.Touch("artwork/logos/gba/Advance Wars.png", "img");

        var r = await Resolve();

        var after = _env.Reload(g);
        Assert.Equal("artwork/backgrounds/gba/Advance Wars (USA).jpg", after.BackgroundPath);
        Assert.Equal("artwork/logos/gba/Advance Wars.png", after.LogoPath);
        Assert.Null(after.CoverPath);
        Assert.Equal(2, r.ExtrasFound);
    }

    [Fact]
    public async Task A_vanished_cover_is_replaced_or_cleared()
    {
        var replaced = _env.AddGame("games/gba/A.gba", setup: g => g.CoverPath = "artwork/covers/gba/gone.png");
        _env.Touch("artwork/covers/gba/A.png", "img");
        var cleared = _env.AddGame("games/gba/B.gba", setup: g => g.CoverPath = "artwork/covers/gba/gone-too.png");

        await Resolve();

        Assert.Equal("artwork/covers/gba/A.png", _env.Reload(replaced).CoverPath);
        Assert.Null(_env.Reload(cleared).CoverPath);
    }

    [Fact]
    public async Task Existing_cover_and_user_removed_cover_are_left_alone()
    {
        _env.Touch("artwork/covers/gba/mine.png", "img");
        var kept = _env.AddGame("games/gba/A.gba", setup: g => g.CoverPath = "artwork/covers/gba/mine.png");
        _env.Touch("artwork/covers/gba/A.png", "img");
        var removed = _env.AddGame("games/gba/B.gba", setup: g => g.CoverPath = "");
        _env.Touch("artwork/covers/gba/B.png", "img");

        var r = await Resolve();

        Assert.Equal("artwork/covers/gba/mine.png", _env.Reload(kept).CoverPath);
        Assert.Equal("", _env.Reload(removed).CoverPath);
        Assert.Equal(0, r.LocalFound);
    }

    [Fact]
    public async Task Resolve_preserves_user_data_changed_meanwhile()
    {
        var g = _env.AddGame("games/gba/A.gba");
        _env.Touch("artwork/covers/gba/A.png", "img");
        var stale = _env.Repo.Query(new RetroHub.Core.Library.GameQuery()).ToList(); // list taken before the change
        _env.Repo.SetFavorite(g.Id, true);

        await _env.Service().ResolveAsync(stale, null, TestContext.Current.CancellationToken);

        var after = _env.Reload(g);
        Assert.True(after.Favorite);
        Assert.Equal("artwork/covers/gba/A.png", after.CoverPath);
    }

    [Fact]
    public void Choose_cover_copies_into_artwork_and_never_overwrites()
    {
        var g = _env.AddGame("games/gba/Advance Wars (USA).gba");
        var svc = _env.Service();
        var pick1 = _env.Touch("Downloads/a.png", "first");
        var pick2 = _env.Touch("Downloads/b.png", "second");

        var stored1 = svc.SetUserCover(g.Id, pick1);
        var stored2 = svc.SetUserCover(g.Id, pick2);
        var stored3 = svc.SetUserCover(g.Id, pick1); // same content as the existing file → reused, no third copy

        Assert.Equal("artwork/covers/gba/Advance Wars (USA).png", stored1);
        Assert.Equal("artwork/covers/gba/Advance Wars (USA) (2).png", stored2);
        Assert.Equal(stored1, stored3);
        Assert.Equal("first", File.ReadAllText(_env.Full(stored1)));
        Assert.Equal("second", File.ReadAllText(_env.Full(stored2)));
        Assert.True(File.Exists(pick1) && File.Exists(pick2)); // source files untouched
        Assert.Equal(stored3, _env.Reload(g).CoverPath);
    }

    [Fact]
    public void Choose_cover_inside_artwork_references_the_file_and_remove_keeps_it()
    {
        var g = _env.AddGame("games/gba/X.gba");
        var existing = _env.Touch("artwork/covers/gba/custom art.webp", "img");
        var svc = _env.Service();

        Assert.Equal("artwork/covers/gba/custom art.webp", svc.SetUserCover(g.Id, existing));
        svc.ClearCover(g.Id);

        Assert.Equal("", _env.Reload(g).CoverPath);
        Assert.True(File.Exists(existing));
        Assert.Single(Directory.GetFiles(_env.Full("artwork/covers/gba")));
    }

    [Fact]
    public void Choose_cover_rejects_non_images()
    {
        var g = _env.AddGame("games/gba/X.gba");
        var txt = _env.Touch("Downloads/notes.txt", "x");
        Assert.Throws<ArgumentException>(() => _env.Service().SetUserCover(g.Id, txt));
        Assert.Null(_env.Reload(g).CoverPath);
    }
}
