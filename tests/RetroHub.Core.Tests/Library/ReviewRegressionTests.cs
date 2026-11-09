using RetroHub.Core.Library;
using Xunit;

namespace RetroHub.Core.Tests.Library;

/// <summary>Regression tests for defects found in the scanner review.</summary>
public sealed class ReviewRegressionTests : IDisposable
{
    private readonly TempRoot _tmp = new();
    private readonly FakeCatalog _catalog = FakeCatalog.Default();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _tmp.Dispose();

    private static Game G(InMemoryGameRepository repo, string portable) => repo.GetByPath(portable) ??
        throw new Xunit.Sdk.XunitException($"No game at {portable}. Games: {string.Join(", ", repo.All().Select(g => g.Path))}");

    private static void Touch(string path, int secondsLater = 10) =>
        File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddSeconds(secondsLater));

    // ───────────────────────── titles ─────────────────────────

    [Theory]
    [InlineData("Super Mario Bros. (World).nes", "Super Mario Bros.")]
    [InlineData("Super Mario Bros. 3 (USA).nes", "Super Mario Bros. 3")]
    [InlineData("Mario Bros. (World).nes", "Mario Bros.")]
    [InlineData("Ken Griffey Jr. Presents Major League Baseball (USA).sfc", "Ken Griffey Jr. Presents Major League Baseball")]
    [InlineData("Legend of Zelda, The - Ocarina of Time (USA).z64", "The Legend of Zelda - Ocarina of Time")]
    [InlineData("Game - (USA).gba", "Game")]
    [InlineData(".hack - Infection (USA).iso", ".hack - Infection")]
    [InlineData("Ms. Pac-Man (USA).nes", "Ms. Pac-Man")]
    public void Title_keeps_trailing_abbreviation_dot(string file, string title) =>
        Assert.Equal(title, TitleParser.Parse(file).Title);

    // ───────────────────────── duplicate / CRC index ─────────────────────────

    [Fact]
    public async Task Changed_file_updates_the_duplicate_index()
    {
        // A.gba is patched in place (new content, same size); B.gba is a fresh copy of A's *old* content.
        var repo = new InMemoryGameRepository();
        var scanner = new LibraryScanner(_catalog, repo, _tmp.Paths);
        var oldContent = RomImages.Gba(seed: 1);
        var a = _tmp.Write("games/gba/A.gba", oldContent);
        await scanner.ScanAsync(new ScanOptions(), null, Ct);

        File.WriteAllBytes(a, RomImages.Gba(seed: 2));
        Touch(a);
        _tmp.Write("games/gba/B.gba", oldContent);
        var r = await scanner.ScanAsync(new ScanOptions(), null, Ct);

        Assert.Empty(r.Duplicates); // B is not a copy of anything that exists now
        Assert.Equal(1, r.Added);
        Assert.Equal(1, r.Updated);
        Assert.Equal(2, repo.All().Count);

        // And the new content of A is recognised as a duplicate target.
        _tmp.Write("games/gba/C.gba", RomImages.Gba(seed: 2));
        r = await scanner.ScanAsync(new ScanOptions(), null, Ct);
        Assert.Contains(r.Duplicates, d => d.Path == "games/gba/C.gba" && d.Reason.Contains("games/gba/A.gba"));
    }

    [Fact]
    public async Task Full_scan_without_hashes_keeps_the_stored_crc_of_unchanged_files()
    {
        var repo = new InMemoryGameRepository();
        var scanner = new LibraryScanner(_catalog, repo, _tmp.Paths);
        _tmp.Write("games/gba/A.gba", RomImages.Gba());
        await scanner.ScanAsync(new ScanOptions(), null, Ct);
        var crc = G(repo, "games/gba/A.gba").Crc32;
        Assert.NotNull(crc);

        var r = await scanner.ScanAsync(new ScanOptions { Full = true, ComputeHashes = false }, null, Ct);

        Assert.Equal(crc, G(repo, "games/gba/A.gba").Crc32);
        Assert.Equal(1, r.Unchanged); // nothing the file says changed
        Assert.Equal(0, r.Updated);
    }

    // ───────────────────────── concurrent user changes ─────────────────────────

    /// <summary>Runs an action right after the scanner took its snapshot of the games (simulates the user/UI acting during a scan).</summary>
    private sealed class MutatingRepository : InMemoryGameRepository
    {
        public Action<InMemoryGameRepository>? AfterSnapshot;

        public override IReadOnlyList<Game> Query(GameQuery query)
        {
            var result = base.Query(query);
            var a = AfterSnapshot;
            AfterSnapshot = null;
            a?.Invoke(this);
            return result;
        }
    }

    [Fact]
    public async Task User_changes_made_during_a_scan_are_not_overwritten()
    {
        var repo = new MutatingRepository();
        var scanner = new LibraryScanner(_catalog, repo, _tmp.Paths);
        var a = _tmp.Write("games/gba/A.gba", RomImages.Gba(seed: 1));
        await scanner.ScanAsync(new ScanOptions(), null, Ct);
        var id = G(repo, "games/gba/A.gba").Id;

        // The file changes (so the scan writes the game) while the user plays it and marks it favourite.
        File.WriteAllBytes(a, RomImages.Gba(seed: 2));
        Touch(a);
        repo.AfterSnapshot = r =>
        {
            r.SetFavorite(id, true);
            r.AddSession(new PlaySession { GameId = id, StartUtc = DateTime.UtcNow.AddMinutes(-30), EndUtc = DateTime.UtcNow, Seconds = 1800 });
        };
        var report = await scanner.ScanAsync(new ScanOptions(), null, Ct);

        Assert.Equal(1, report.Updated);
        var g = repo.Get(id)!;
        Assert.True(g.Favorite);
        Assert.Equal(1800, g.PlaySeconds);
        Assert.Equal(1, g.PlayCount);
        Assert.Equal(Crc32.ToHex(Crc32.ComputeFile(a, Ct)), g.Crc32); // the scanner's own fields were written
    }

    [Fact]
    public async Task Game_deleted_during_a_scan_is_not_resurrected_and_does_not_break_the_batch()
    {
        var repo = new MutatingRepository();
        var scanner = new LibraryScanner(_catalog, repo, _tmp.Paths);
        var a = _tmp.Write("games/gba/A.gba", RomImages.Gba(seed: 1));
        var b = _tmp.Write("games/gba/B.gba", RomImages.Gba(seed: 2));
        await scanner.ScanAsync(new ScanOptions(), null, Ct);
        var idA = G(repo, "games/gba/A.gba").Id;

        File.WriteAllBytes(a, RomImages.Gba(seed: 3)); Touch(a);
        File.WriteAllBytes(b, RomImages.Gba(seed: 4)); Touch(b);
        repo.AfterSnapshot = r => r.Delete(idA); // user removes A from the library while the scan runs
        var report = await scanner.ScanAsync(new ScanOptions(), null, Ct);

        Assert.Empty(report.Errors);
        Assert.Null(repo.Get(idA));
        Assert.Equal(Crc32.ToHex(Crc32.ComputeFile(b, Ct)), G(repo, "games/gba/B.gba").Crc32);
    }

    /// <summary>Changes/deletes library folders right after the scanner read them.</summary>
    private sealed class FolderMutatingRepository : InMemoryGameRepository
    {
        public Action<InMemoryGameRepository>? AfterFolders;
        private int _calls;

        public override IReadOnlyList<LibraryFolder> Folders()
        {
            var result = base.Folders();
            // Call 1 = EnsureBuiltInFolder, call 2 = the scan's folder list.
            if (Interlocked.Increment(ref _calls) == 2) AfterFolders?.Invoke(this);
            return result;
        }
    }

    [Fact]
    public async Task Folder_settings_changed_during_a_scan_are_kept_and_deleted_folders_stay_deleted()
    {
        var repo = new FolderMutatingRepository();
        var scanner = new LibraryScanner(_catalog, repo, _tmp.Paths);
        var ext = _tmp.External("Ext");
        _tmp.WriteExternal("Ext/A.gba", RomImages.Gba());
        var ext2 = _tmp.External("Ext2");
        _tmp.WriteExternal("Ext2/B.nes", RomImages.Nes());
        var extId = repo.UpsertFolder(new LibraryFolder { Path = ext });
        var ext2Id = repo.UpsertFolder(new LibraryFolder { Path = ext2 });
        repo.AfterFolders = r =>
        {
            var f = r.Folders().Single(x => x.Id == extId);
            f.Enabled = false;
            f.SystemId = "gba";
            r.UpsertFolder(f);
            r.DeleteFolder(ext2Id);
        };

        await scanner.ScanAsync(new ScanOptions(), null, Ct);

        var after = repo.Folders();
        var changed = after.Single(x => x.Id == extId);
        Assert.False(changed.Enabled);
        Assert.Equal("gba", changed.SystemId);
        Assert.NotNull(changed.LastScanUtc);
        Assert.DoesNotContain(after, x => x.Id == ext2Id);
    }

    // ───────────────────────── scheduling ─────────────────────────

    /// <summary>Delegating catalog that blocks the scan thread when the walk reaches a given folder name.</summary>
    private sealed class BlockingCatalog(global::RetroHub.Core.Catalog.ICatalog inner, string blockOn) : global::RetroHub.Core.Catalog.ICatalog
    {
        public readonly ManualResetEventSlim Reached = new(), Release = new();
        private int _once = 1;
        public IReadOnlyList<global::RetroHub.Core.Catalog.SystemDef> Systems => inner.Systems;
        public IReadOnlyList<global::RetroHub.Core.Catalog.EmulatorDef> Emulators => inner.Emulators;
        public IReadOnlyList<string> LoadWarnings => inner.LoadWarnings;
        public global::RetroHub.Core.Catalog.SystemDef? GetSystem(string id) => inner.GetSystem(id);
        public global::RetroHub.Core.Catalog.EmulatorDef? GetEmulator(string id) => inner.GetEmulator(id);
        public IReadOnlyList<global::RetroHub.Core.Catalog.SystemDef> SystemsForExtension(string extension) => inner.SystemsForExtension(extension);

        public global::RetroHub.Core.Catalog.SystemDef? SystemForFolder(string folderName)
        {
            if (folderName.Equals(blockOn, StringComparison.OrdinalIgnoreCase) && Interlocked.Exchange(ref _once, 0) == 1)
            {
                Reached.Set();
                Release.Wait(TimeSpan.FromSeconds(30));
            }
            return inner.SystemForFolder(folderName);
        }
    }

    [Fact]
    public async Task Scan_requested_while_the_running_scan_is_past_that_folder_sees_the_new_file()
    {
        // The running scan has already listed games/gba and is walking games/zzz when the watcher reports a new
        // games/gba file and asks for a scan. That request must not be answered with the running scan's result.
        _tmp.Write("games/gba/Old.gba", RomImages.Gba(seed: 1));
        Directory.CreateDirectory(_tmp.Full("games/zzz"));
        var catalog = new BlockingCatalog(_catalog, "zzz");
        var repo = new InMemoryGameRepository();
        var scanner = new LibraryScanner(catalog, repo, _tmp.Paths);

        var first = scanner.ScanAsync(new ScanOptions(), null, Ct);
        Assert.True(catalog.Reached.Wait(TimeSpan.FromSeconds(10), Ct));
        _tmp.Write("games/gba/New.gba", RomImages.Gba(seed: 2));
        var builtIn = repo.Folders().Single(f => f.IsBuiltIn).Id;
        var second = scanner.ScanAsync(new ScanOptions { FolderIds = [builtIn] }, null, Ct);
        catalog.Release.Set();

        var r1 = await first;
        var r2 = await second;
        Assert.Equal(1, r1.Added);
        Assert.Equal(1, r2.Added);
        Assert.NotNull(repo.GetByPath("games/gba/New.gba"));
    }

    // ───────────────────────── file system details ─────────────────────────

    [Fact]
    public async Task Symlinked_rom_uses_the_target_size_and_is_not_broken()
    {
        var target = _tmp.WriteExternal("store/Real.nes", RomImages.Nes());
        var link = _tmp.Full("games/nes/Linked.nes");
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        try { File.CreateSymbolicLink(link, target); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Assert.Skip($"Cannot create symbolic links here: {ex.Message}");
        }
        var repo = new InMemoryGameRepository();
        var scanner = new LibraryScanner(_catalog, repo, _tmp.Paths);

        var r = await scanner.ScanAsync(new ScanOptions(), null, Ct);

        Assert.Equal(1, r.Added);
        var g = G(repo, "games/nes/Linked.nes");
        Assert.Equal(GameStatus.Ok, g.Status);
        Assert.Equal(new FileInfo(target).Length, g.Size);
        Assert.Equal(Crc32.ToHex(Crc32.ComputeFile(target, Ct)), g.Crc32);

        r = await scanner.ScanAsync(new ScanOptions(), null, Ct);
        Assert.Equal(1, r.Unchanged);
    }

    [Fact]
    public async Task File_that_was_temporarily_unreadable_is_rechecked_on_the_next_scan()
    {
        var path = _tmp.Write("games/gba/Locked.gba", RomImages.Gba());
        var repo = new InMemoryGameRepository();
        var scanner = new LibraryScanner(_catalog, repo, _tmp.Paths);

        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            await scanner.ScanAsync(new ScanOptions(), null, Ct);
        Assert.Equal(GameStatus.Broken, G(repo, "games/gba/Locked.gba").Status);

        var r = await scanner.ScanAsync(new ScanOptions(), null, Ct); // unchanged size + time, now readable
        var g = G(repo, "games/gba/Locked.gba");
        Assert.Equal(GameStatus.Ok, g.Status);
        Assert.NotNull(g.Crc32);
        Assert.Equal(1, r.Updated);

        r = await scanner.ScanAsync(new ScanOptions(), null, Ct);
        Assert.Equal(1, r.Unchanged);
    }

    [Fact]
    public async Task Genuinely_broken_file_is_not_counted_as_updated_on_every_scan()
    {
        _tmp.Write("games/nes/Cut.nes", RomImages.Nes(truncated: true));
        var repo = new InMemoryGameRepository();
        var scanner = new LibraryScanner(_catalog, repo, _tmp.Paths);
        await scanner.ScanAsync(new ScanOptions(), null, Ct);
        var calls = repo.UpsertManyCalls;

        var r = await scanner.ScanAsync(new ScanOptions(), null, Ct);

        Assert.Equal(1, r.Unchanged);
        Assert.Equal(0, r.Updated);
        Assert.Equal(calls, repo.UpsertManyCalls); // nothing written
        Assert.Equal(GameStatus.Broken, G(repo, "games/nes/Cut.nes").Status);
    }
}
