using System.Collections.Concurrent;
using RetroHub.Core.Library;
using Xunit;

namespace RetroHub.Core.Tests.Library;

public sealed class FolderWatcherTests : IDisposable
{
    private readonly TempRoot _tmp = new();
    private readonly BlockingCollection<IReadOnlyList<long>> _events = new();
    private readonly FolderWatcher _watcher;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public FolderWatcherTests()
    {
        _watcher = new FolderWatcher(_tmp.Paths, debounce: TimeSpan.FromMilliseconds(300), retryInterval: TimeSpan.FromMilliseconds(200));
        _watcher.FoldersChanged += ids => _events.Add(ids);
    }

    public void Dispose()
    {
        _watcher.Dispose();
        _tmp.Dispose();
    }

    private IReadOnlyList<long>? Next(int ms = 5000) => _events.TryTake(out var ids, ms, Ct) ? ids : null;

    private LibraryFolder Folder(long id, string path, bool enabled = true)
    {
        Directory.CreateDirectory(_tmp.Paths.FromPortable(path));
        return new LibraryFolder { Id = id, Path = path, Enabled = enabled };
    }

    [Fact]
    public void New_rom_raises_folders_changed_once_after_debounce()
    {
        _watcher.SetFolders([Folder(1, "games"), Folder(2, "games2")]);
        Assert.Equal([1L, 2L], _watcher.ActiveFolderIds.Order());

        _tmp.Write("games/gba/a.gba", RomImages.Gba(seed: 1));
        _tmp.Write("games/gba/b.gba", RomImages.Gba(seed: 2));
        _tmp.Write("games/nes/c.nes", RomImages.Nes());

        var ids = Next();
        Assert.Equal([1L], ids);
        Assert.Null(Next(800)); // the burst was debounced into one notification
    }

    [Fact]
    public void Temp_partial_and_ignorable_files_do_not_trigger()
    {
        // Folders exist beforehand: creating a folder is itself a (relevant) change.
        Directory.CreateDirectory(_tmp.Full("games/gba/media"));
        _watcher.SetFolders([Folder(1, "games")]);
        _tmp.Write("games/gba/download.gba.part", new byte[10]);
        _tmp.Write("games/gba/x.crdownload", new byte[10]);
        _tmp.Write("games/gba/y.tmp", new byte[10]);
        _tmp.Write("games/gba/settings.json.retrohub-tmp", new byte[10]);
        _tmp.Write("games/gba/game.sav", new byte[10]);
        _tmp.Write("games/gba/readme.txt", "x");
        _tmp.Write("games/gba/media/cover.gba", new byte[10]); // inside a skipped folder
        Assert.Null(Next(1200));
    }

    [Fact]
    public void Finished_download_rename_triggers()
    {
        Directory.CreateDirectory(_tmp.Full("games/gba"));
        _watcher.SetFolders([Folder(1, "games")]);
        var part = _tmp.Write("games/gba/game.gba.part", RomImages.Gba());
        Assert.Null(Next(800));
        File.Move(part, _tmp.Full("games/gba/game.gba"));
        Assert.Equal([1L], Next());
    }

    [Fact]
    public void Disabled_folders_are_not_watched_and_set_folders_replaces_the_set()
    {
        Directory.CreateDirectory(_tmp.Full("games/gba"));
        _watcher.SetFolders([Folder(1, "games", enabled: false), Folder(2, "games2")]);
        Assert.Equal([2L], _watcher.ActiveFolderIds);
        _tmp.Write("games/gba/a.gba", RomImages.Gba());
        Assert.Null(Next(1000));

        _watcher.SetFolders([Folder(1, "games")]);
        Assert.Equal([1L], _watcher.ActiveFolderIds);
        _tmp.Write("games/gba/b.gba", RomImages.Gba(seed: 3));
        Assert.Equal([1L], Next());
    }

    [Fact]
    public void Folder_that_appears_later_is_picked_up_by_the_retry()
    {
        var ext = _tmp.External("LateDrive");
        _watcher.SetFolders([new LibraryFolder { Id = 7, Path = ext }]);
        Assert.Empty(_watcher.ActiveFolderIds);

        Directory.CreateDirectory(ext);
        Assert.Equal([7L], Next()); // becoming available counts as a change (rescan it)
        Assert.Equal([7L], _watcher.ActiveFolderIds);

        File.WriteAllBytes(Path.Combine(ext, "x.nes"), RomImages.Nes());
        Assert.Equal([7L], Next());
    }

    [Fact]
    public void Dispose_stops_notifications_and_is_idempotent()
    {
        _watcher.SetFolders([Folder(1, "games")]);
        _watcher.Dispose();
        _tmp.Write("games/gba/a.gba", RomImages.Gba());
        Assert.Null(Next(800));
        _watcher.Dispose();
        _watcher.SetFolders([Folder(1, "games")]); // no-op after dispose, no throw
    }

    [Theory]
    [InlineData(@"gba\game.gba", true)]
    [InlineData(@"gba\game.gba.part", false)]
    [InlineData(@"gba\game.srm", false)]
    [InlineData(@"gba\saves\game.gba", false)]
    [InlineData(@"gba\.git\x.gba", false)]
    [InlineData(@"newfolder", true)]
    public void Relevance_is_judged_relative_to_the_root(string relative, bool relevant)
    {
        // The root itself lies below a folder named "media": that must not make everything irrelevant.
        var root = Path.Combine(_tmp.Base, "media", "roms");
        Assert.Equal(relevant, FolderWatcher.Relevant(root, Path.Combine(root, relative)));
    }
}
