using Microsoft.Data.Sqlite;
using RetroHub.Core.Data;
using RetroHub.Core.Library;
using Xunit;
using static RetroHub.Core.Tests.Data.TempDatabase;

namespace RetroHub.Core.Tests.Data;

/// <summary>Regression tests added during review (failure paths, recovery, fallback concurrency).</summary>
public sealed class SqliteGameRepositoryRobustnessTests : IDisposable
{
    private readonly TempDatabase _db = new();

    public void Dispose() => _db.Dispose();

    [Fact]
    public void UpsertMany_SkipsConstraintViolations_AndCommitsTheRest()
    {
        using var repo = _db.Open();
        var a = repo.Upsert(NewGame("games/nes/a.nes", "A"));
        var b = repo.Upsert(NewGame("games/nes/b.nes", "B"));
        var events = 0;
        repo.Changed += () => events++;

        var moveOntoA = repo.Get(b)!;
        moveOntoA.Path = "GAMES/NES/A.NES";            // path already used by another game
        var badFolder = NewGame("games/nes/c.nes", "C", setup: g => g.FolderId = 4711); // unknown folder (FK)
        var good1 = NewGame("games/nes/d.nes", "D");
        var good2 = NewGame("games/nes/e.nes", "E");

        repo.UpsertMany([good1, moveOntoA, badFolder, good2]);

        Assert.Equal(1, events);
        Assert.True(good1.Id > 0);
        Assert.True(good2.Id > 0);
        Assert.Equal("D", repo.Get(good1.Id)!.Title);
        Assert.Equal("E", repo.Get(good2.Id)!.Title);
        Assert.Equal("games/nes/b.nes", repo.Get(b)!.Path); // unchanged
        Assert.Equal("A", repo.Get(a)!.Title);
        Assert.Null(repo.GetByPath("games/nes/c.nes"));
        Assert.Equal(4, repo.Query(new GameQuery()).Count);
    }

    [Fact]
    public void DamagedSqliteFile_IsPreservedAndReplaced()
    {
        using (var repo = _db.Open())
        {
            repo.UpsertMany(Enumerable.Range(0, 3000).Select(i => NewGame($"games/nes/g{i}.nes", $"Game {i}", setup: g => g.Description = new string('x', 200))));
        }
        SqliteConnection.ClearAllPools();
        var bytes = File.ReadAllBytes(_db.File);
        Assert.True(bytes.Length > 64 * 1024);
        // Keep the header page intact, smash the b-tree pages in the middle.
        new Random(7).NextBytes(bytes.AsSpan(8192, bytes.Length / 2));
        File.WriteAllBytes(_db.File, bytes);

        using var again = _db.Open();
        Assert.False(again.IsTemporary);
        Assert.NotNull(again.PreservedCorruptFile);
        Assert.Equal(bytes, File.ReadAllBytes(again.PreservedCorruptFile));
        Assert.Empty(again.Query(new GameQuery()));
    }

    [Fact]
    public void CorruptFile_ThatCannotBePreserved_DoesNotCrash_AndIsLeftUntouched()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_db.File)!);
        var garbage = "definitely not a sqlite database, just some text ......................................"u8.ToArray();
        File.WriteAllBytes(_db.File, garbage);
        // Directories with the exact names PreserveCorrupt would copy to make File.Copy throw UnauthorizedAccessException.
        var now = DateTime.Now;
        for (var s = -2; s <= 30; s++)
            Directory.CreateDirectory($"{_db.File}.corrupt-{now.AddSeconds(s):yyyyMMdd-HHmmss}");

        using var repo = _db.Open();

        Assert.True(repo.IsTemporary);
        Assert.Null(repo.PreservedCorruptFile);
        Assert.Equal(garbage, File.ReadAllBytes(_db.File));
        var id = repo.Upsert(NewGame("games/nes/a.nes", "A"));
        Assert.NotNull(repo.Get(id));
    }

    [Fact]
    public void CorruptFile_SideFilesArePreservedWithIdenticalContent()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_db.File)!);
        var garbage = "not a database, the user's file ......................................................"u8.ToArray();
        var wal = "pretend write-ahead log with the user's newest changes"u8.ToArray();
        var journal = "pretend rollback journal"u8.ToArray();
        File.WriteAllBytes(_db.File, garbage);
        File.WriteAllBytes(_db.File + "-wal", wal);
        File.WriteAllBytes(_db.File + "-journal", journal);

        using var repo = _db.Open();

        Assert.False(repo.IsTemporary);
        Assert.NotNull(repo.PreservedCorruptFile);
        Assert.Equal(garbage, File.ReadAllBytes(repo.PreservedCorruptFile));
        Assert.Equal(wal, File.ReadAllBytes(repo.PreservedCorruptFile + "-wal"));
        Assert.Equal(journal, File.ReadAllBytes(repo.PreservedCorruptFile + "-journal"));
        Assert.False(File.Exists(_db.File + "-journal"));
        Assert.Empty(repo.Query(new GameQuery()));
    }

    [Fact]
    public void EmptyFile_IsAValidNewDatabase()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_db.File)!);
        File.WriteAllBytes(_db.File, []);
        using var repo = _db.Open();
        Assert.False(repo.IsTemporary);
        Assert.Null(repo.PreservedCorruptFile);
        Assert.Equal(Migrations.Latest, repo.SchemaVersion);
    }

    [Fact]
    public void CorruptFile_WhoseWalCannotBePreserved_IsNotDeleted()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_db.File)!);
        var garbage = "not a database either, but long enough to look like one ............................."u8.ToArray();
        var wal = "pretend write-ahead log with the user's newest changes"u8.ToArray();
        File.WriteAllBytes(_db.File, garbage);
        File.WriteAllBytes(_db.File + "-wal", wal);
        // Block only the copy of the -wal (the main file can be preserved).
        var now = DateTime.Now;
        for (var s = -2; s <= 30; s++)
            Directory.CreateDirectory($"{_db.File}.corrupt-{now.AddSeconds(s):yyyyMMdd-HHmmss}-wal");

        using var repo = _db.Open();

        Assert.True(repo.IsTemporary);
        Assert.Equal(garbage, File.ReadAllBytes(_db.File));
        Assert.Equal(wal, File.ReadAllBytes(_db.File + "-wal"));
    }

    [Fact]
    public async Task InMemoryFallback_IsThreadSafe()
    {
        Directory.CreateDirectory(_db.File); // forces the fallback
        using var repo = _db.Open();
        Assert.True(repo.IsTemporary);
        var errors = 0;
        var writers = Enumerable.Range(0, 4).Select(w => Task.Run(() =>
        {
            for (var i = 0; i < 100; i++)
            {
                try { repo.Upsert(NewGame($"games/nes/w{w}-{i}.nes", $"Game {w}-{i}")); }
                catch (Exception) { Interlocked.Increment(ref errors); }
            }
        }));
        var readers = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < 100; i++)
            {
                try { repo.Query(new GameQuery { Text = "game", Limit = 20 }); repo.Stats(); }
                catch (Exception) { Interlocked.Increment(ref errors); }
            }
        }));
        await Task.WhenAll(writers.Concat(readers));
        Assert.Equal(0, errors);
        Assert.Equal(400, repo.Query(new GameQuery()).Count);
    }

    [Fact]
    public void UpsertById_UnspecifiedLastPlayed_IsTreatedAsUtcLikeEverywhereElse()
    {
        if (TimeZoneInfo.Local.GetUtcOffset(new DateTime(2025, 3, 1)) == TimeSpan.Zero)
            Assert.Skip("Needs a local time zone with a non-zero UTC offset");
        using var repo = _db.Open();
        var id = repo.Upsert(NewGame("games/nes/a.nes", "A"));
        var t = new DateTime(2025, 3, 1, 20, 0, 0, DateTimeKind.Utc);
        repo.AddSession(new PlaySession { GameId = id, StartUtc = t.AddMinutes(-5), EndUtc = t, Seconds = 300 });

        var g = repo.Get(id)!;
        var offset = TimeZoneInfo.Local.GetUtcOffset(t);
        // Newer than the stored value when read as UTC (which is how ToDb stores Unspecified values).
        var newer = DateTime.SpecifyKind(t + offset.Duration() - TimeSpan.FromMinutes(1), DateTimeKind.Unspecified);
        if (offset < TimeSpan.Zero) newer = DateTime.SpecifyKind(t + TimeSpan.FromMinutes(1), DateTimeKind.Unspecified);
        g.LastPlayedUtc = newer;
        repo.Upsert(g);
        Assert.Equal(DateTime.SpecifyKind(newer, DateTimeKind.Utc), repo.Get(id)!.LastPlayedUtc);

        // Older than the stored value (as UTC): must not lower it.
        g.LastPlayedUtc = DateTime.SpecifyKind(t.AddHours(-3), DateTimeKind.Unspecified);
        repo.Upsert(g);
        Assert.Equal(DateTime.SpecifyKind(newer, DateTimeKind.Utc), repo.Get(id)!.LastPlayedUtc);
    }
    [Fact]
    public async Task ParallelOpenOfANewFile_MigratesOnce_AndNoInstanceFallsBack()
    {
        // Two app instances (or the app and a tool) starting at the same moment on a fresh database.
        for (var round = 0; round < 10; round++)
        {
            var file = Path.Combine(_db.Folder, $"parallel-{round}.db");
            var repos = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => new SqliteGameRepository(file))));
            try
            {
                Assert.All(repos, r => Assert.False(r.IsTemporary));
                Assert.All(repos, r => Assert.Equal(Migrations.Latest, r.SchemaVersion));
            }
            finally { foreach (var r in repos) r.Dispose(); }
        }
    }

    [Fact]
    public void SamePath_WithNonAsciiCaseDifference_IsOneRow()
    {
        using var repo = _db.Open();
        var id = repo.Upsert(NewGame("games/gba/Pokémon Smaragd.gba", "Pokémon Smaragd", "gba"));
        Assert.Equal(id, repo.Upsert(NewGame("GAMES/GBA/POKÉMON SMARAGD.GBA", "Pokémon Smaragd", "gba")));
        Assert.Equal(id, repo.Upsert(NewGame(@"games\gba\pokémon smaragd.gba", "Pokémon Smaragd", "gba")));
        repo.UpsertMany([NewGame("games/nes/Ärger.nes", "Ärger"), NewGame("games/nes/ÄRGER.NES", "Ärger 2")]);

        Assert.Equal(2, repo.Query(new GameQuery()).Count);
        Assert.Equal(id, repo.GetByPath("games/gba/POKÉMON smaragd.gba")!.Id);
        Assert.Equal("Ärger 2", repo.GetByPath("GAMES/NES/ärger.nes")!.Title);
        // Different letters (not just case) stay different games.
        repo.Upsert(NewGame("games/nes/Arger.nes", "Arger"));
        Assert.Equal(3, repo.Query(new GameQuery()).Count);
    }

    [Theory]
    [InlineData(@"E:\Roms\", @"E:\Roms")]
    [InlineData("E:/Roms//", @"E:\Roms")]
    [InlineData(@"E:\", @"E:\")]
    [InlineData("E:", "E:")]
    [InlineData("games/", "games")]
    [InlineData(@"games\nes\", "games/nes")]
    [InlineData(@"\\server\share\roms\", @"\\server\share\roms")]
    public void NormalizeFolderPath_DropsTrailingSeparators(string input, string expected) =>
        Assert.Equal(expected, SqliteGameRepository.NormalizeFolderPath(input));

    [Fact]
    public void UpsertFolder_TrailingSeparator_IsSameFolder()
    {
        using var repo = _db.Open();
        var id = repo.UpsertFolder(new LibraryFolder { Path = @"E:\Roms" });
        Assert.Equal(id, repo.UpsertFolder(new LibraryFolder { Path = @"e:\roms\" }));
        var builtIn = repo.EnsureBuiltInFolder("games/");
        Assert.Equal(builtIn, repo.EnsureBuiltInFolder("games"));
        Assert.Equal(2, repo.Folders().Count);
    }
}
