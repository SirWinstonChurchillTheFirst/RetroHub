using System.Diagnostics;
using Microsoft.Data.Sqlite;
using RetroHub.Core.Catalog;
using RetroHub.Core.Data;
using RetroHub.Core.Library;
using Xunit;
using static RetroHub.Core.Tests.Data.TempDatabase;

namespace RetroHub.Core.Tests.Data;

public sealed class SqliteGameRepositoryTests : IDisposable
{
    private readonly TempDatabase _db = new();

    public void Dispose() => _db.Dispose();

    // ───────────── schema ─────────────

    [Fact]
    public void Migrations_FromEmpty_CreateSchemaAndUseWal()
    {
        using (var repo = _db.Open())
        {
            Assert.False(repo.IsTemporary);
            Assert.Null(repo.PreservedCorruptFile);
            Assert.Equal(Migrations.Latest, repo.SchemaVersion);
            Assert.Equal("wal", repo.JournalMode);
            repo.Upsert(NewGame("games/nes/a.nes", "A"));
        }

        Assert.Equal((long)Migrations.Latest, _db.Scalar("PRAGMA user_version;"));
        foreach (var table in new[] { "games", "folders", "sessions", "state" })
            Assert.Equal(1L, _db.Scalar($"SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = '{table}';"));
        foreach (var index in new[] { "ix_games_system", "ix_games_favorite", "ix_games_last_played", "ix_games_added", "ix_games_title_norm", "ix_games_crc_size" })
            Assert.Equal(1L, _db.Scalar($"SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = '{index}';"));

        // Re-opening runs no migration again and keeps the data.
        using var again = _db.Open();
        Assert.Equal(Migrations.Latest, again.SchemaVersion);
        Assert.NotNull(again.GetByPath("games/nes/a.nes"));
    }

    [Theory]
    [InlineData(@"\\server\share\retrohub.db", true)]
    [InlineData(@"//server/share/retrohub.db", true)]
    [InlineData(@"\\?\UNC\server\share\retrohub.db", true)]
    [InlineData(@"C:\RetroHub\data\database\retrohub.db", false)]
    [InlineData(@"\\?\C:\RetroHub\retrohub.db", false)]
    [InlineData("", false)]
    public void IsNetworkPath_DetectsUncPaths(string path, bool expected) =>
        Assert.Equal(expected, SqliteGameRepository.IsNetworkPath(path));

    [Fact]
    public void UncPath_UsesDeleteJournalInsteadOfWal()
    {
        var full = Path.GetFullPath(_db.File);
        var share = $@"\\localhost\{full[0]}$\";
        bool reachable;
        try { reachable = full[1] == ':' && Directory.Exists(share); } catch (Exception) { reachable = false; }
        if (!reachable) Assert.Skip($"Administrative share {share} not reachable on this machine");

        var unc = share + full[3..];
        Directory.CreateDirectory(Path.GetDirectoryName(unc)!);
        using var repo = new SqliteGameRepository(unc);
        Assert.False(repo.IsTemporary);
        Assert.Equal("delete", repo.JournalMode);
        var id = repo.Upsert(NewGame("games/nes/a.nes", "A"));
        Assert.Equal("A", repo.Get(id)!.Title);
    }

    // ───────────── upsert / get ─────────────

    [Fact]
    public void Upsert_RoundTripsAllFields()
    {
        using var repo = _db.Open();
        var fileTime = new DateTime(2023, 5, 6, 7, 8, 9, DateTimeKind.Utc).AddTicks(1234567);
        var g = NewGame("games/gba/Pokemon Emerald.gba", "Pokémon Smaragd", "gba", x =>
        {
            x.SortTitle = "Pokemon Smaragd";
            x.Kind = GameKind.Playlist;
            x.Status = GameStatus.Broken;
            x.Size = 16_777_216;
            x.FileTimeUtc = fileTime;
            x.Crc32 = "1F1C08FB";
            x.Serial = "BPEE";
            x.Region = "EU";
            x.AddedUtc = new DateTime(2024, 2, 3, 4, 5, 6, DateTimeKind.Utc);
            x.LastPlayedUtc = new DateTime(2024, 3, 4, 5, 6, 7, DateTimeKind.Utc);
            x.PlaySeconds = 3600;
            x.PlayCount = 3;
            x.Favorite = true;
            x.Hidden = true;
            x.EmulatorOverride = "retroarch/mgba";
            x.LaunchModeOverride = LaunchMode.Windowed;
            x.CoverPath = "artwork/covers/gba/x.png";
            x.BackgroundPath = "artwork/backgrounds/gba/x.png";
            x.LogoPath = "artwork/logos/gba/x.png";
            x.Developer = "Game Freak";
            x.Publisher = "Nintendo";
            x.Genre = "RPG";
            x.Year = 2004;
            x.Players = 1;
            x.Description = "Beschreibung mit Ümläuten";
            x.ExtraArgs = "--fast";
        });

        var id = repo.Upsert(g);
        Assert.True(id > 0);
        Assert.Equal(id, g.Id);

        var r = repo.Get(id)!;
        Assert.Equal("gba", r.SystemId);
        Assert.Equal("games/gba/Pokemon Emerald.gba", r.Path);
        Assert.Equal("Pokémon Smaragd", r.Title);
        Assert.Equal("Pokemon Smaragd", r.SortTitle);
        Assert.Equal(GameKind.Playlist, r.Kind);
        Assert.Equal(GameStatus.Broken, r.Status);
        Assert.Equal(16_777_216, r.Size);
        Assert.Equal(fileTime, r.FileTimeUtc); // exact to the tick (scanner change detection)
        Assert.Equal(DateTimeKind.Utc, r.FileTimeUtc.Kind);
        Assert.Equal("1F1C08FB", r.Crc32);
        Assert.Equal("BPEE", r.Serial);
        Assert.Equal("EU", r.Region);
        Assert.Equal(g.AddedUtc, r.AddedUtc);
        Assert.Equal(g.LastPlayedUtc, r.LastPlayedUtc);
        Assert.Equal(3600, r.PlaySeconds);
        Assert.Equal(3, r.PlayCount);
        Assert.True(r.Favorite);
        Assert.True(r.Hidden);
        Assert.Equal("retroarch/mgba", r.EmulatorOverride);
        Assert.Equal(LaunchMode.Windowed, r.LaunchModeOverride);
        Assert.Equal("artwork/covers/gba/x.png", r.CoverPath);
        Assert.Equal("artwork/backgrounds/gba/x.png", r.BackgroundPath);
        Assert.Equal("artwork/logos/gba/x.png", r.LogoPath);
        Assert.Equal("Game Freak", r.Developer);
        Assert.Equal("Nintendo", r.Publisher);
        Assert.Equal("RPG", r.Genre);
        Assert.Equal(2004, r.Year);
        Assert.Equal(1, r.Players);
        Assert.Equal("Beschreibung mit Ümläuten", r.Description);
        Assert.Equal("--fast", r.ExtraArgs);
        Assert.Null(r.FolderId);

        var byPath = repo.GetByPath("GAMES/GBA/pokemon emerald.GBA");
        Assert.NotNull(byPath);
        Assert.Equal(id, byPath.Id);
        Assert.Null(repo.Get(id + 1000));
        Assert.Null(repo.GetByPath("games/gba/none.gba"));
    }

    [Fact]
    public void Upsert_SetsAddedUtcWhenMissing()
    {
        using var repo = _db.Open();
        var before = DateTime.UtcNow.AddSeconds(-1);
        var id = repo.Upsert(NewGame("games/nes/a.nes", "A"));
        var added = repo.Get(id)!.AddedUtc;
        Assert.InRange(added, before, DateTime.UtcNow.AddSeconds(1));
    }

    [Fact]
    public void Upsert_SamePath_NeverCreatesDuplicate_AndKeepsUserData()
    {
        using var repo = _db.Open();
        var id = repo.Upsert(NewGame("games/snes/Super Mario World.sfc", "Super Mario World", "snes",
            g => { g.Developer = "Nintendo"; g.EmulatorOverride = "snes9x"; }));
        repo.SetFavorite(id, true);
        repo.AddSession(new PlaySession { GameId = id, StartUtc = DateTime.UtcNow.AddMinutes(-10), EndUtc = DateTime.UtcNow, Seconds = 600 });
        var addedBefore = repo.Get(id)!.AddedUtc;

        // A fresh scan result (Id == 0) with different case and backslashes is the same file.
        var rescan = NewGame(@"GAMES\SNES\super mario world.SFC", "Super Mario World (USA)", "snes", g => g.Size = 4096);
        var id2 = repo.Upsert(rescan);

        Assert.Equal(id, id2);
        Assert.Equal(id, rescan.Id);
        Assert.Equal(1L, (long)repo.Query(new GameQuery()).Count);
        var r = repo.Get(id)!;
        Assert.Equal("Super Mario World (USA)", r.Title);  // scan data updated
        Assert.Equal(4096, r.Size);
        Assert.True(r.Favorite);                           // user data kept
        Assert.Equal(600, r.PlaySeconds);
        Assert.Equal(1, r.PlayCount);
        Assert.NotNull(r.LastPlayedUtc);
        Assert.Equal(addedBefore, r.AddedUtc);
        Assert.Equal("Nintendo", r.Developer);             // null metadata does not erase
        Assert.Equal("snes9x", r.EmulatorOverride);
        Assert.Equal("GAMES/SNES/super mario world.SFC", r.Path); // canonical separators, newest spelling

        // Same within one batch.
        repo.UpsertMany([NewGame("games/nes/x.nes", "X"), NewGame("GAMES/nes/X.NES", "X2")]);
        Assert.Equal(2, repo.Query(new GameQuery()).Count);
        Assert.Equal("X2", repo.GetByPath("games/nes/x.nes")!.Title);
    }

    [Fact]
    public void Upsert_ById_UpdatesPathAndNeverLowersPlayStats()
    {
        using var repo = _db.Open();
        var id = repo.Upsert(NewGame("games/nes/old.nes", "Old"));
        var stale = repo.Get(id)!;
        repo.AddSession(new PlaySession { GameId = id, StartUtc = DateTime.UtcNow.AddMinutes(-1), EndUtc = DateTime.UtcNow, Seconds = 60 });

        stale.Path = "games/nes/new.nes";
        stale.Title = "New";
        Assert.Equal(id, repo.Upsert(stale));

        var r = repo.Get(id)!;
        Assert.Equal("games/nes/new.nes", r.Path);
        Assert.Equal("New", r.Title);
        Assert.Equal(60, r.PlaySeconds);
        Assert.Equal(1, r.PlayCount);
        Assert.NotNull(r.LastPlayedUtc);
        Assert.Null(repo.GetByPath("games/nes/old.nes"));

        // Changing the path onto another game's path is a caller error.
        var other = repo.Upsert(NewGame("games/nes/other.nes", "Other"));
        var g = repo.Get(other)!;
        g.Path = "games/nes/NEW.nes";
        Assert.Throws<SqliteException>(() => repo.Upsert(g));
        Assert.Equal(2, repo.Query(new GameQuery()).Count);
    }

    [Fact]
    public void Delete_RemovesRowAndSessionsOnly()
    {
        var file = Path.Combine(_db.Folder, "rom.nes");
        File.WriteAllBytes(file, [1, 2, 3]);
        using var repo = _db.Open();
        var id = repo.Upsert(NewGame(file, "Rom"));
        repo.AddSession(new PlaySession { GameId = id, StartUtc = DateTime.UtcNow, EndUtc = DateTime.UtcNow, Seconds = 5 });

        repo.Delete(id);

        Assert.Null(repo.Get(id));
        Assert.Empty(repo.Sessions(id));
        Assert.Equal(0L, _db.Scalar("SELECT COUNT(*) FROM sessions;"));
        Assert.True(File.Exists(file)); // the game file is never touched
    }

    // ───────────── query ─────────────

    private static void Seed(SqliteGameRepository repo)
    {
        var t0 = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        repo.UpsertMany(
        [
            NewGame("games/gba/emerald.gba", "Pokémon Smaragd", "gba", g => { g.Year = 2004; g.Developer = "Game Freak"; g.Genre = "Rollenspiel"; g.AddedUtc = t0.AddDays(1); g.Favorite = true; }),
            NewGame("games/snes/smw.sfc", "Super Mario World", "snes", g => { g.Year = 1990; g.Developer = "Nintendo EAD"; g.Genre = "Jump 'n' Run"; g.AddedUtc = t0.AddDays(2); }),
            NewGame("games/snes/zelda.sfc", "The Legend of Zelda: A Link to the Past", "snes", g => { g.SortTitle = "Legend of Zelda, The"; g.Year = 1991; g.AddedUtc = t0.AddDays(3); g.Favorite = true; }),
            NewGame("games/nes/agypten.nes", "Ägypten Abenteuer", "nes", g => { g.AddedUtc = t0.AddDays(4); }),
            NewGame("games/nes/hidden.nes", "Bomberman", "nes", g => { g.Hidden = true; g.Year = 1985; g.AddedUtc = t0.AddDays(5); }),
            NewGame("games/nes/missing.nes", "Castlevania", "nes", g => { g.Status = GameStatus.Missing; g.Year = 1986; g.AddedUtc = t0.AddDays(6); }),
            NewGame("games/nes/oj.nes", "100% Orange Juice", "nes", g => { g.AddedUtc = t0.AddDays(7); }),
            NewGame("games/nes/oj2.nes", "1000 Oranges", "nes", g => { g.AddedUtc = t0.AddDays(8); }),
        ]);
    }

    private static string[] Titles(IEnumerable<Game> games) => games.Select(g => g.Title).ToArray();

    [Fact]
    public void Query_Filters()
    {
        using var repo = _db.Open();
        Seed(repo);

        Assert.Equal(7, repo.Query(new GameQuery()).Count); // hidden excluded by default, missing included
        Assert.Equal(8, repo.Query(new GameQuery { IncludeHidden = true }).Count);
        Assert.Equal(6, repo.Query(new GameQuery { IncludeMissing = false }).Count);
        Assert.Equal(["Legend of Zelda", "Super Mario World"],
            Titles(repo.Query(new GameQuery { SystemId = "snes" })).Select(t => t.Contains("Zelda") ? "Legend of Zelda" : t).ToArray());
        Assert.Equal(["The Legend of Zelda: A Link to the Past", "Pokémon Smaragd"], Titles(repo.Query(new GameQuery { Favorite = true })));
        Assert.Equal(5, repo.Query(new GameQuery { Favorite = false }).Count);
        Assert.Equal(3, repo.Query(new GameQuery { Limit = 3 }).Count);
        Assert.Single(repo.Query(new GameQuery { SystemId = "nes", Favorite = false, IncludeMissing = false, Text = "agypten" }));
    }

    [Fact]
    public void Query_TextSearch_IsCaseAndDiacriticsInsensitive()
    {
        using var repo = _db.Open();
        Seed(repo);

        Assert.Equal(["Pokémon Smaragd"], Titles(repo.Query(new GameQuery { Text = "pokemon" })));
        Assert.Equal(["Pokémon Smaragd"], Titles(repo.Query(new GameQuery { Text = "POKÉMON" })));
        Assert.Equal(["Pokémon Smaragd"], Titles(repo.Query(new GameQuery { Text = "smaragd pok" })));      // all tokens, any order
        Assert.Equal(["Ägypten Abenteuer"], Titles(repo.Query(new GameQuery { Text = "ägypten" })));
        Assert.Equal(["Ägypten Abenteuer"], Titles(repo.Query(new GameQuery { Text = "agypten" })));
        Assert.Equal(["Pokémon Smaragd"], Titles(repo.Query(new GameQuery { Text = "game freak" })));     // developer
        Assert.Equal(["Pokémon Smaragd"], Titles(repo.Query(new GameQuery { Text = "rollenspiel" })));    // genre
        Assert.Equal(["Super Mario World"], Titles(repo.Query(new GameQuery { Text = "nintendo" })));
        Assert.Single(repo.Query(new GameQuery { Text = "zelda the" }));                                 // sort title / title
        Assert.Single(repo.Query(new GameQuery { Text = "link to" }));
        Assert.Empty(repo.Query(new GameQuery { Text = "metroid" }));
        Assert.Empty(repo.Query(new GameQuery { Text = "bomberman" }));                                  // hidden
        Assert.Single(repo.Query(new GameQuery { Text = "bomberman", IncludeHidden = true }));

        // % is a literal, not a wildcard.
        Assert.Equal(["100% Orange Juice"], Titles(repo.Query(new GameQuery { Text = "100%" })));
        Assert.Equal(2, repo.Query(new GameQuery { Text = "100" }).Count);
        Assert.Empty(repo.Query(new GameQuery { Text = "1%0" }));
        // Only separators → no text filter.
        Assert.Equal(7, repo.Query(new GameQuery { Text = " - : " }).Count);
    }

    [Fact]
    public void Query_Sorts()
    {
        using var repo = _db.Open();
        Seed(repo);
        var ids = repo.Query(new GameQuery { IncludeHidden = true }).ToDictionary(g => g.Title, g => g.Id);
        var now = new DateTime(2025, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        repo.AddSession(new PlaySession { GameId = ids["Super Mario World"], StartUtc = now.AddHours(-3), EndUtc = now.AddHours(-2), Seconds = 3600 });
        repo.AddSession(new PlaySession { GameId = ids["Castlevania"], StartUtc = now.AddHours(-1), EndUtc = now, Seconds = 100 });

        // Title: diacritics-insensitive, SortTitle wins ("Legend of Zelda, The" sorts under L, "Ägypten" under A).
        Assert.Equal(["100% Orange Juice", "1000 Oranges", "Ägypten Abenteuer", "Castlevania", "The Legend of Zelda: A Link to the Past", "Pokémon Smaragd", "Super Mario World"],
            Titles(repo.Query(new GameQuery { Sort = GameSort.Title })));

        var lastPlayed = Titles(repo.Query(new GameQuery { Sort = GameSort.LastPlayed }));
        Assert.Equal(["Castlevania", "Super Mario World"], lastPlayed[..2]);
        Assert.Equal(7, lastPlayed.Length); // never-played games follow (NULLS LAST)

        Assert.Equal(["1000 Oranges", "100% Orange Juice", "Castlevania"], Titles(repo.Query(new GameQuery { Sort = GameSort.Added, Limit = 3 })));

        Assert.Equal(["Super Mario World", "Castlevania"], Titles(repo.Query(new GameQuery { Sort = GameSort.PlayTime, Limit = 2 })));

        Assert.Equal(["gba", "nes", "nes", "nes", "nes", "snes", "snes"],
            repo.Query(new GameQuery { Sort = GameSort.System }).Select(g => g.SystemId).ToArray());
        Assert.Equal(["Legend", "Super"],
            repo.Query(new GameQuery { Sort = GameSort.System, SystemId = "snes" }).Select(g => g.Title.Contains("Zelda") ? "Legend" : "Super").ToArray());

        var byYear = repo.Query(new GameQuery { Sort = GameSort.Year });
        Assert.Equal([1986, 1990, 1991, 2004], byYear.Take(4).Select(g => g.Year!.Value).ToArray());
        Assert.All(byYear.Skip(4), g => Assert.Null(g.Year)); // NULLS LAST
    }

    // ───────────── favourites / hidden / events ─────────────

    [Fact]
    public void SetFavoriteAndHidden_RaiseChangedOnlyWhenSomethingChanges()
    {
        using var repo = _db.Open();
        var id = repo.Upsert(NewGame("games/nes/a.nes", "A"));
        var events = 0;
        repo.Changed += () => Interlocked.Increment(ref events);
        repo.Changed += () => throw new InvalidOperationException("listener bug"); // must be swallowed

        repo.SetFavorite(id, true);
        Assert.True(repo.Get(id)!.Favorite);
        repo.SetFavorite(id, true); // no-op
        repo.SetHidden(id, true);
        Assert.True(repo.Get(id)!.Hidden);
        repo.SetFavorite(id, false);
        Assert.False(repo.Get(id)!.Favorite);
        repo.SetFavorite(id + 99, true); // unknown id: no-op, no throw

        Assert.Equal(3, events);
        repo.Upsert(NewGame("games/nes/b.nes", "B"));
        Assert.Equal(4, events);
    }

    // ───────────── sessions / stats ─────────────

    [Fact]
    public void AddSession_UpdatesTotalsAtomically()
    {
        using var repo = _db.Open();
        var id = repo.Upsert(NewGame("games/nes/a.nes", "A"));
        var t = new DateTime(2025, 3, 1, 20, 0, 0, DateTimeKind.Utc);

        var s1 = new PlaySession { GameId = id, EmulatorId = "mesen", StartUtc = t, EndUtc = t.AddMinutes(30), Seconds = 1800, ExitCode = 0 };
        repo.AddSession(s1);
        // An older session added later must not move LastPlayedUtc backwards.
        repo.AddSession(new PlaySession { GameId = id, StartUtc = t.AddDays(-1), EndUtc = t.AddDays(-1).AddMinutes(5), Seconds = 300 });
        repo.AddSession(new PlaySession { GameId = id, StartUtc = t.AddHours(2), EndUtc = t.AddHours(2), Seconds = -5 }); // clamped to 0

        var g = repo.Get(id)!;
        Assert.Equal(2100, g.PlaySeconds);
        Assert.Equal(3, g.PlayCount);
        Assert.Equal(t.AddHours(2), g.LastPlayedUtc);
        Assert.True(s1.Id > 0);

        var sessions = repo.Sessions(id);
        Assert.Equal(3, sessions.Count);
        Assert.Equal(t.AddHours(2), sessions[0].StartUtc); // newest first
        Assert.Equal(t.AddDays(-1), sessions[2].StartUtc);
        var stored = sessions.Single(s => s.Id == s1.Id);
        Assert.Equal("mesen", stored.EmulatorId);
        Assert.Equal(0, stored.ExitCode);
        Assert.Equal(1800, stored.Seconds);
        Assert.Equal(t.AddMinutes(30), stored.EndUtc);
        Assert.Null(sessions[0].ExitCode);
        Assert.Equal(2, repo.Sessions(id, limit: 2).Count);

        // Unknown game: dropped without exception.
        repo.AddSession(new PlaySession { GameId = 987654, StartUtc = t, EndUtc = t, Seconds = 1 });
        Assert.Equal(3L, _db.Scalar("SELECT COUNT(*) FROM sessions;"));
    }

    [Fact]
    public void Stats_CountsVisibleGamesAndAllPlayTime()
    {
        using var repo = _db.Open();
        Seed(repo);
        var hidden = repo.GetByPath("games/nes/hidden.nes")!;
        var smw = repo.GetByPath("games/snes/smw.sfc")!;
        repo.AddSession(new PlaySession { GameId = hidden.Id, StartUtc = DateTime.UtcNow, EndUtc = DateTime.UtcNow, Seconds = 50 });
        repo.AddSession(new PlaySession { GameId = smw.Id, StartUtc = DateTime.UtcNow, EndUtc = DateTime.UtcNow, Seconds = 70 });

        var s = repo.Stats();
        Assert.Equal(7, s.GameCount);
        Assert.Equal(3, s.SystemCount);
        Assert.Equal(2, s.FavoriteCount);
        Assert.Equal(120, s.TotalPlaySeconds);
        Assert.Equal(4, s.GamesPerSystem["nes"]);
        Assert.Equal(2, s.GamesPerSystem["snes"]);
        Assert.Equal(1, s.GamesPerSystem["gba"]);
        Assert.Equal(50, s.PlaySecondsPerSystem["nes"]);
        Assert.Equal(70, s.PlaySecondsPerSystem["snes"]);
        Assert.False(s.PlaySecondsPerSystem.ContainsKey("gba"));

        using var empty = new SqliteGameRepository(Path.Combine(_db.Folder, "empty.db"));
        var e = empty.Stats();
        Assert.Equal(0, e.GameCount);
        Assert.Empty(e.GamesPerSystem);
    }

    // ───────────── folders ─────────────

    [Fact]
    public void Folders_UpsertDeleteAndBuiltIn()
    {
        using var repo = _db.Open();
        var builtIn = repo.EnsureBuiltInFolder("games");
        Assert.Equal(builtIn, repo.EnsureBuiltInFolder("GAMES"));
        Assert.Equal(builtIn, repo.EnsureBuiltInFolder("games")); // same folder, latest spelling kept

        var ext = new LibraryFolder { Path = @"E:\Roms\PS1", SystemId = "psx", Recursive = false };
        var extId = repo.UpsertFolder(ext);
        Assert.Equal(extId, ext.Id);
        Assert.False(ext.IsBuiltIn);

        // Same path again (other spelling) → same row, values updated.
        var dup = new LibraryFolder { Path = "e:/roms/ps1", SystemId = null, Enabled = false, LastScanUtc = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc) };
        Assert.Equal(extId, repo.UpsertFolder(dup));

        var folders = repo.Folders();
        Assert.Equal(2, folders.Count);
        Assert.True(folders[0].IsBuiltIn);
        Assert.Equal("games", folders[0].Path);
        Assert.True(folders[0].Recursive && folders[0].Enabled);
        var f = folders[1];
        Assert.Equal(@"e:\roms\ps1", f.Path);
        Assert.Null(f.SystemId);
        Assert.True(f.Recursive);
        Assert.False(f.Enabled);
        Assert.Equal(dup.LastScanUtc, f.LastScanUtc);

        var gameId = repo.Upsert(NewGame(@"E:\Roms\PS1\ff7.cue", "Final Fantasy VII", "psx", g => g.FolderId = extId));
        repo.DeleteFolder(extId);
        Assert.Single(repo.Folders());
        var game = repo.Get(gameId);
        Assert.NotNull(game);          // games stay
        Assert.Null(game.FolderId);    // only unlinked

        repo.DeleteFolder(builtIn);    // refused
        Assert.Single(repo.Folders());
        Assert.Equal(1L, _db.Scalar("SELECT COUNT(*) FROM folders WHERE is_builtin = 1;"));
    }

    // ───────────── state ─────────────

    [Fact]
    public void State_SetGetOverwriteRemove()
    {
        using var repo = _db.Open();
        var events = 0;
        repo.Changed += () => events++;

        Assert.Null(repo.GetState("ui.lastSystem"));
        repo.SetState("ui.lastSystem", "snes");
        Assert.Equal("snes", repo.GetState("ui.lastSystem"));
        repo.SetState("ui.lastSystem", "snes"); // unchanged → no event
        repo.SetState("ui.lastSystem", "Größe/äöü");
        Assert.Equal("Größe/äöü", repo.GetState("ui.lastSystem"));
        repo.SetState("ui.lastSystem", null);
        Assert.Null(repo.GetState("ui.lastSystem"));
        repo.SetState("ui.lastSystem", null); // already gone → no event
        Assert.Equal(3, events);
    }

    // ───────────── robustness ─────────────

    [Fact]
    public async Task Concurrency_ParallelUpsertsAndReads()
    {
        using var repo = _db.Open();
        var errors = 0;
        var writers = Enumerable.Range(0, 8).Select(w => Task.Run(() =>
        {
            for (var i = 0; i < 150; i++)
            {
                try
                {
                    repo.Upsert(NewGame($"games/nes/w{w}-{i}.nes", $"Game {w}-{i}"));
                    // Every writer also hits the same shared paths: must never produce duplicates.
                    repo.Upsert(NewGame($"games/nes/shared-{i % 10}.nes", $"Shared {i % 10}"));
                }
                catch (Exception) { Interlocked.Increment(ref errors); }
            }
        }));
        var readers = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < 100; i++)
            {
                try
                {
                    repo.Query(new GameQuery { Text = "game", Limit = 50 });
                    repo.Stats();
                }
                catch (Exception) { Interlocked.Increment(ref errors); }
            }
        }));
        await Task.WhenAll(writers.Concat(readers));

        Assert.Equal(0, errors);
        Assert.Equal(8 * 150 + 10, repo.Query(new GameQuery()).Count);
        Assert.Equal(10, repo.Query(new GameQuery { Text = "shared" }).Count);
    }

    [Fact]
    public void CorruptFile_IsPreservedAndReplacedByFreshDatabase()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_db.File)!);
        var garbage = new byte[8192];
        new Random(42).NextBytes(garbage);
        "this is not a database"u8.CopyTo(garbage);
        File.WriteAllBytes(_db.File, garbage);

        using (var repo = _db.Open())
        {
            Assert.False(repo.IsTemporary);
            Assert.NotNull(repo.PreservedCorruptFile);
            Assert.True(File.Exists(repo.PreservedCorruptFile));
            Assert.Equal(garbage, File.ReadAllBytes(repo.PreservedCorruptFile));
            Assert.Equal(Migrations.Latest, repo.SchemaVersion);
            repo.Upsert(NewGame("games/nes/a.nes", "A"));
            Assert.Single(repo.Query(new GameQuery()));
        }

        // The new file is a valid database that opens cleanly next time.
        using var again = _db.Open();
        Assert.Null(again.PreservedCorruptFile);
        Assert.Single(again.Query(new GameQuery()));
    }

    [Fact]
    public void UnusablePath_FallsBackToTemporaryDatabase()
    {
        // A directory where the database file should be: cannot be opened as a file, nothing may be deleted.
        Directory.CreateDirectory(_db.File);
        using var repo = _db.Open();
        Assert.True(repo.IsTemporary);
        Assert.True(Directory.Exists(_db.File));
        var id = repo.Upsert(NewGame("games/nes/a.nes", "A"));
        Assert.NotNull(repo.Get(id));
    }

    [Fact]
    public void Backup_ProducesStandaloneCopy()
    {
        using var repo = _db.Open();
        Seed(repo);
        var dest = Path.Combine(_db.Folder, "backups", "retrohub-backup.db");
        repo.Backup(dest);
        repo.Backup(dest); // overwriting an existing backup works

        Assert.True(File.Exists(dest));
        Assert.False(File.Exists(dest + ".retrohub-tmp"));
        using var copy = new SqliteGameRepository(dest);
        Assert.Equal(8, copy.Query(new GameQuery { IncludeHidden = true }).Count);
        Assert.Throws<ArgumentException>(() => repo.Backup(_db.File));
    }

    [Fact]
    public void Dispose_ReleasesFileAndBlocksFurtherUse()
    {
        var repo = _db.Open();
        repo.Upsert(NewGame("games/nes/a.nes", "A"));
        repo.Dispose();
        repo.Dispose(); // idempotent
        Assert.Throws<ObjectDisposedException>(() => repo.Get(1));
        // File handles are released: the database can be moved.
        File.Move(_db.File, _db.File + ".moved");
        Assert.True(File.Exists(_db.File + ".moved"));
    }

    // ───────────── performance ─────────────

    [Fact]
    public void UpsertMany_10kGames_IsFast()
    {
        using var repo = _db.Open();
        var games = Enumerable.Range(0, 10_000)
            .Select(i => NewGame($"games/snes/Game {i:D5} (Europe).sfc", $"Spiel Nummer {i} – Äußerst gut", "snes", g =>
            {
                g.Crc32 = i.ToString("X8");
                g.Developer = "Entwickler " + (i % 50);
                g.Genre = "Genre " + (i % 12);
            }))
            .ToList();

        var sw = Stopwatch.StartNew();
        repo.UpsertMany(games);
        sw.Stop();
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(3), $"insert of 10k took {sw.ElapsedMilliseconds} ms");
        Assert.All(games, g => Assert.True(g.Id > 0));

        // Re-scan of the same 10k (Id == 0, merge by path) is just as fast and adds nothing.
        var rescan = games.Select(g => NewGame(g.Path, g.Title + " (rev 1)", "snes")).ToList();
        sw.Restart();
        repo.UpsertMany(rescan);
        sw.Stop();
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(3), $"re-scan of 10k took {sw.ElapsedMilliseconds} ms");
        Assert.Equal(10_000L, _db.Scalar("SELECT COUNT(*) FROM games;"));
        Assert.Equal(games.Select(g => g.Id), rescan.Select(g => g.Id));

        sw.Restart();
        var all = repo.Query(new GameQuery());
        var found = repo.Query(new GameQuery { Text = "ausserst 4242" });
        sw.Stop();
        Assert.Equal(10_000, all.Count);
        Assert.Single(found);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(2), $"query took {sw.ElapsedMilliseconds} ms");
    }
}
