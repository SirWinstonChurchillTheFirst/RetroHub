using System.Diagnostics;
using RetroHub.Core.Library;
using Xunit;

namespace RetroHub.Core.Tests.Library;

public sealed class LibraryScannerTests : IDisposable
{
    private readonly TempRoot _tmp = new();
    private readonly FakeCatalog _catalog = FakeCatalog.Default();
    private readonly InMemoryGameRepository _repo = new();
    private readonly LibraryScanner _scanner;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public LibraryScannerTests() => _scanner = new LibraryScanner(_catalog, _repo, _tmp.Paths);

    public void Dispose() => _tmp.Dispose();

    private Task<ScanReport> Scan(ScanOptions? options = null) => _scanner.ScanAsync(options ?? new ScanOptions(), null, Ct);

    private Game G(string portable) => _repo.GetByPath(portable) ?? throw new Xunit.Sdk.XunitException(
        $"No game at {portable}. Games: {string.Join(", ", _repo.All().Select(g => g.Path))}");

    private static void Touch(string path, int secondsLater = 10) =>
        File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddSeconds(secondsLater));

    // ───────────────────────── system detection ─────────────────────────

    [Fact]
    public async Task Detects_systems_from_folders_aliases_and_nested_subfolders()
    {
        _tmp.Write("games/nes/Super Test (USA).nes", RomImages.Nes());
        _tmp.Write("games/genesis/Sonic Test (Europe).md", RomImages.MegaDrive());
        _tmp.Write("games/gba/Hacks/Deep/Nested Hack.gba", RomImages.Gba());
        _tmp.Write("games/gamecube/Melee Test (USA).iso", RomImages.NintendoDisc(wii: false));
        _tmp.Write("games/n3ds/Test 3DS (Japan).3ds", RomImages.N3dsCci("CTR-P-ATEJ"));
        _tmp.Write("games/collection/snes/Mario Test.sfc", RomImages.Snes("MARIO TEST"));

        var r = await Scan();

        Assert.Empty(r.Unknown);
        Assert.Empty(r.Errors);
        Assert.Equal(6, r.Added);
        var nes = G("games/nes/Super Test (USA).nes");
        Assert.Equal("nes", nes.SystemId);
        Assert.Equal("Super Test", nes.Title);
        Assert.Equal("USA", nes.Region);
        Assert.Equal(GameKind.Rom, nes.Kind);
        Assert.Equal(GameStatus.Ok, nes.Status);
        Assert.NotNull(nes.Crc32);
        Assert.Equal(8, nes.Crc32!.Length);
        Assert.Equal("megadrive", G("games/genesis/Sonic Test (Europe).md").SystemId);
        Assert.Equal("gba", G("games/gba/Hacks/Deep/Nested Hack.gba").SystemId);
        var gc = G("games/gamecube/Melee Test (USA).iso");
        Assert.Equal("gc", gc.SystemId);
        Assert.Equal("GTEE01", gc.Serial);
        var n3 = G("games/n3ds/Test 3DS (Japan).3ds");
        Assert.Equal("3ds", n3.SystemId);
        Assert.Equal("CTR-P-ATEJ", n3.Serial);
        Assert.Equal("snes", G("games/collection/snes/Mario Test.sfc").SystemId);
        Assert.All(_repo.All(), g => Assert.NotNull(g.FolderId));
    }

    [Fact]
    public async Task Unique_extension_detects_system_in_external_folder_without_system_names()
    {
        var ext = _tmp.External("MyRoms");
        _tmp.WriteExternal("MyRoms/Zelda Test (USA).sfc", RomImages.Snes("ZELDA TEST"));
        _tmp.WriteExternal("MyRoms/misc/Advance Test.gba", RomImages.Gba());
        _tmp.WriteExternal("MyRoms/misc/more/Dual Test.nds", RomImages.Nds());
        var id = _repo.UpsertFolder(new LibraryFolder { Path = _tmp.Paths.ToPortable(ext) });

        var r = await Scan();

        Assert.Equal(3, r.Added);
        var snes = _repo.All().Single(g => g.SystemId == "snes");
        Assert.True(Path.IsPathRooted(snes.Path)); // outside the portable root → absolute
        Assert.Equal(Path.Combine(ext, "Zelda Test (USA).sfc"), snes.Path);
        Assert.Equal(id, snes.FolderId);
        Assert.Contains(_repo.All(), g => g.SystemId == "gba");
        Assert.Contains(_repo.All(), g => g.SystemId == "nds");
    }

    [Fact]
    public async Task Fixed_folder_system_wins_and_foreign_files_are_reported()
    {
        var ext = _tmp.External("Genesis Dumps");
        _tmp.WriteExternal("Genesis Dumps/Game One.bin", RomImages.MegaDrive("GAME ONE"));
        _tmp.WriteExternal("Genesis Dumps/nes/Inside.bin", RomImages.MegaDrive("INSIDE", seed: 99)); // "nes" subfolder ignored: fixed system
        _tmp.WriteExternal("Genesis Dumps/Wrong.gba", RomImages.Gba());
        _repo.UpsertFolder(new LibraryFolder { Path = ext, SystemId = "megadrive" });

        var r = await Scan();

        Assert.Equal(2, r.Added);
        Assert.All(_repo.All(), g => Assert.Equal("megadrive", g.SystemId));
        var issue = Assert.Single(r.Unknown);
        Assert.EndsWith("Wrong.gba", issue.Path);
        Assert.Contains("passt nicht", issue.Reason);
    }

    [Fact]
    public async Task Ambiguous_iso_and_rvz_are_resolved_by_sniffers()
    {
        _tmp.Write("games/discs/PS2 Test (USA).iso", IsoBuilder.Ps2().Build());
        _tmp.Write("games/discs/PS1 Test (Europe).iso", IsoBuilder.Ps1("SLES_012.34").Build());
        _tmp.Write("games/discs/PSP Test.iso", IsoBuilder.Psp().Build());
        _tmp.Write("games/discs/GC Test.iso", RomImages.NintendoDisc(wii: false, "GZLE"));
        _tmp.Write("games/discs/Wii Test.iso", RomImages.NintendoDisc(wii: true, "RMCP"));
        _tmp.Write("games/discs/Wii Packed.rvz", RomImages.Rvz(true, "RTEE"));
        _tmp.Write("games/discs/GC Packed.rvz", RomImages.Rvz(false, "GTEE"));
        _tmp.Write("games/discs/Data Disc.iso", new IsoBuilder { SystemId = "WIN32" }.File("README.TXT", "hi").Build());

        var r = await Scan();

        var ps2 = G("games/discs/PS2 Test (USA).iso");
        Assert.Equal("ps2", ps2.SystemId);
        Assert.Equal("SLUS-20312", ps2.Serial);
        Assert.Equal("USA", ps2.Region);
        Assert.Null(ps2.Crc32); // disc images are never hashed
        Assert.Equal("psx", G("games/discs/PS1 Test (Europe).iso").SystemId);
        Assert.Equal("psp", G("games/discs/PSP Test.iso").SystemId);
        Assert.Equal("gc", G("games/discs/GC Test.iso").SystemId);
        Assert.Equal("wii", G("games/discs/Wii Test.iso").SystemId);
        Assert.Equal("wii", G("games/discs/Wii Packed.rvz").SystemId);
        Assert.Equal("gc", G("games/discs/GC Packed.rvz").SystemId);
        var unknown = Assert.Single(r.Unknown);
        Assert.Equal("games/discs/Data Disc.iso", unknown.Path);
        Assert.Contains("nicht eindeutig", unknown.Reason);
    }

    [Fact]
    public async Task Unknown_files_are_reported_and_ignorable_files_skipped()
    {
        _tmp.Write("games/nes/Real Game.nes", RomImages.Nes());
        _tmp.Write("games/nes/readme.txt", "hello");
        _tmp.Write("games/nes/cover.png", new byte[100]);
        _tmp.Write("games/nes/Real Game.srm", new byte[8192]);
        _tmp.Write("games/nes/Real Game.state1", new byte[100]);
        _tmp.Write("games/nes/Real Game.state.auto", new byte[100]);
        _tmp.Write("games/nes/Real Game.ips", new byte[100]);
        _tmp.Write("games/nes/download.nes.part", new byte[100]);
        _tmp.Write("games/nes/Misplaced.gba", RomImages.Gba());
        _tmp.Write("games/misc/weird.xyz", new byte[10]);
        _tmp.Write("games/misc/Bundle.zip", new byte[100]);
        _tmp.Write("games/misc/Tool.exe", new byte[100]);
        _tmp.Write("games/misc/NOEXT", new byte[100]);
        _tmp.Write("games/misc/info.nfo", "x");

        var r = await Scan();

        Assert.Equal(1, r.Added);
        var unknown = r.Unknown.Select(u => u.Path).Order().ToList();
        Assert.Equal(["games/misc/Bundle.zip", "games/misc/NOEXT", "games/misc/Tool.exe", "games/misc/weird.xyz", "games/nes/Misplaced.gba"], unknown);
        Assert.Contains("PC", r.Unknown.Single(u => u.Path.EndsWith("Tool.exe")).Reason);
        Assert.Contains("nicht eindeutig", r.Unknown.Single(u => u.Path.EndsWith(".zip")).Reason);
    }

    [Fact]
    public async Task Skips_special_and_hidden_folders()
    {
        _tmp.Write("games/nes/Visible.nes", RomImages.Nes(seed: 1));
        _tmp.Write("games/nes/media/InMedia.nes", RomImages.Nes(seed: 2));
        _tmp.Write("games/nes/saves/InSaves.nes", RomImages.Nes(seed: 3));
        _tmp.Write("games/nes/downloaded_media/X.nes", RomImages.Nes(seed: 4));
        _tmp.Write("games/nes/.git/InGit.nes", RomImages.Nes(seed: 5));
        _tmp.Write("games/bios/bios.nes", RomImages.Nes(seed: 6));
        _tmp.Write("games/nes/hid/Hidden.nes", RomImages.Nes(seed: 7));
        File.SetAttributes(_tmp.Full("games/nes/hid"), FileAttributes.Directory | FileAttributes.Hidden);
        var hiddenFile = _tmp.Write("games/nes/HiddenFile.nes", RomImages.Nes(seed: 8));
        File.SetAttributes(hiddenFile, FileAttributes.Hidden);

        var r = await Scan();

        Assert.Equal(1, r.Added);
        Assert.Equal("games/nes/Visible.nes", Assert.Single(_repo.All()).Path);
        Assert.Empty(r.Unknown);
    }

    [Fact]
    public async Task Junctions_are_not_followed()
    {
        _tmp.Write("games/nes/Real.nes", RomImages.Nes());
        // A junction pointing back up the tree would loop forever if followed. mklink /J needs no admin rights.
        var link = _tmp.Full("games/nes/loop");
        var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{_tmp.Paths.Games}\"")
        {
            CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        using (var p = System.Diagnostics.Process.Start(psi)!)
        {
            await p.WaitForExitAsync(Ct);
            if (p.ExitCode != 0 || !Directory.Exists(link)) Assert.Skip("could not create a junction on this machine");
        }
        try
        {
            var r = await Scan();
            Assert.Equal(1, r.Added);
            Assert.Equal("games/nes/Real.nes", Assert.Single(_repo.All()).Path);
        }
        finally
        {
            Directory.Delete(link); // removes the junction only, never the target
        }
    }

    [Fact]
    public async Task Nested_library_folder_is_scanned_on_its_own_not_twice()
    {
        _tmp.Write("games/nes/A.nes", RomImages.Nes(seed: 1));
        _tmp.Write("games/extra/B.gba", RomImages.Gba());
        var builtIn = _scanner.EnsureBuiltInFolder();
        var extraId = _repo.UpsertFolder(new LibraryFolder { Path = "games/extra" });

        var r = await Scan();

        Assert.Equal(2, r.Added);
        Assert.Equal(builtIn.Id, G("games/nes/A.nes").FolderId);
        Assert.Equal(extraId, G("games/extra/B.gba").FolderId);
        Assert.Equal(2, (await Scan()).Unchanged);
    }

    // ───────────────────────── multi-file ─────────────────────────

    [Fact]
    public async Task Cue_bin_m3u_and_gdi_hide_their_parts_and_missing_tracks_mark_broken()
    {
        var ps1Raw = IsoBuilder.Ps1().Build(2352, 2);
        _tmp.Write("games/psx/Game A (USA) (Track 1).bin", ps1Raw);
        _tmp.Write("games/psx/Game A (USA) (Track 2).bin", new byte[2352 * 4]);
        _tmp.Write("games/psx/Game A (USA).cue",
            "FILE \"Game A (USA) (Track 1).bin\" BINARY\n  TRACK 01 MODE2/2352\n    INDEX 01 00:00:00\n" +
            "FILE \"Game A (USA) (Track 2).bin\" BINARY\n  TRACK 02 AUDIO\n    INDEX 01 00:00:00\n");
        _tmp.Write("games/psx/Broken Game (USA).cue", "FILE \"Broken Game (USA).bin\" BINARY\n  TRACK 01 MODE2/2352\n");

        _tmp.Write("games/psx/FF Test (USA) (Disc 1).bin", IsoBuilder.Ps1("SLUS_006.70").Build(2352, 2));
        _tmp.Write("games/psx/FF Test (USA) (Disc 1).cue", "FILE \"FF Test (USA) (Disc 1).bin\" BINARY\n");
        _tmp.Write("games/psx/FF Test (USA) (Disc 2).bin", IsoBuilder.Ps1("SLUS_007.70").Build(2352, 2));
        _tmp.Write("games/psx/FF Test (USA) (Disc 2).cue", "FILE \"FF Test (USA) (Disc 2).bin\" BINARY\n");
        _tmp.Write("games/psx/FF Test (USA).m3u", "FF Test (USA) (Disc 1).cue\nFF Test (USA) (Disc 2).cue\n");

        _tmp.Write("games/dreamcast/Shen Test/track01.bin", new byte[2352]);
        _tmp.Write("games/dreamcast/Shen Test/track02.raw", new byte[2352]);
        _tmp.Write("games/dreamcast/Shen Test/track03.bin", new byte[2352]);
        _tmp.Write("games/dreamcast/Shen Test/Shen Test.gdi", "3\n1 0 4 2352 track01.bin 0\n2 600 0 2352 track02.raw 0\n3 45000 4 2352 track03.bin 0\n");

        // Ambiguous .cue (psx + ps2) outside a system folder: resolved by sniffing the data track.
        _tmp.Write("games/misc/Loose Disc (Japan).bin", IsoBuilder.Ps1("SLPS_000.01").Build(2352, 2));
        _tmp.Write("games/misc/Loose Disc (Japan).cue", "FILE \"Loose Disc (Japan).bin\" BINARY\n");

        var r = await Scan();

        Assert.Empty(r.Unknown);
        var paths = _repo.All().Select(g => g.Path).Order().ToList();
        Assert.Equal([
            "games/dreamcast/Shen Test/Shen Test.gdi",
            "games/misc/Loose Disc (Japan).cue",
            "games/psx/Broken Game (USA).cue",
            "games/psx/FF Test (USA).m3u",
            "games/psx/Game A (USA).cue",
        ], paths);
        Assert.All(_repo.All(), g => Assert.Equal(GameKind.Playlist, g.Kind));
        Assert.All(_repo.All(), g => Assert.Null(g.Crc32));

        var a = G("games/psx/Game A (USA).cue");
        Assert.Equal(GameStatus.Ok, a.Status);
        Assert.Equal("Game A", a.Title);
        Assert.Equal("SLUS-00770", a.Serial);
        Assert.Equal(new FileInfo(_tmp.Full("games/psx/Game A (USA).cue")).Length + ps1Raw.Length + 2352 * 4, a.Size);

        Assert.Equal(GameStatus.Broken, G("games/psx/Broken Game (USA).cue").Status);
        var ff = G("games/psx/FF Test (USA).m3u");
        Assert.Equal("FF Test", ff.Title);
        Assert.Equal("SLUS-00670", ff.Serial); // first disc
        Assert.Equal("dreamcast", G("games/dreamcast/Shen Test/Shen Test.gdi").SystemId);
        var loose = G("games/misc/Loose Disc (Japan).cue");
        Assert.Equal("psx", loose.SystemId);
        Assert.Equal("Japan", loose.Region);
    }

    [Fact]
    public async Task Cue_becomes_broken_when_a_track_disappears_and_ok_when_it_returns()
    {
        _tmp.Write("games/psx/G (USA) (Track 1).bin", IsoBuilder.Ps1().Build(2352, 2));
        _tmp.Write("games/psx/G (USA) (Track 2).bin", new byte[2352]);
        _tmp.Write("games/psx/G (USA).cue", "FILE \"G (USA) (Track 1).bin\" BINARY\nFILE \"G (USA) (Track 2).bin\" BINARY\n");
        await Scan();
        Assert.Equal(GameStatus.Ok, G("games/psx/G (USA).cue").Status);

        File.Move(_tmp.Full("games/psx/G (USA) (Track 2).bin"), _tmp.Full("track2.bak"));
        var r = await Scan();
        Assert.Equal(1, r.Updated);
        Assert.Equal(GameStatus.Broken, G("games/psx/G (USA).cue").Status);

        File.Move(_tmp.Full("track2.bak"), _tmp.Full("games/psx/G (USA) (Track 2).bin"));
        await Scan();
        Assert.Equal(GameStatus.Ok, G("games/psx/G (USA).cue").Status);
    }

    // ───────────────────────── PC ─────────────────────────

    [Fact]
    public async Task Pc_folder_games_pick_the_main_exe()
    {
        _tmp.Write("games/pc/Celeste/Celeste.exe", new byte[1000]);
        _tmp.Write("games/pc/Celeste/unins000.exe", new byte[90_000]);
        _tmp.Write("games/pc/Celeste/UnityCrashHandler64.exe", new byte[50_000]);
        _tmp.Write("games/pc/Celeste/Content/data.bin", new byte[10]);

        _tmp.Write("games/pc/Big Game/launcher.exe", new byte[10]);
        _tmp.Write("games/pc/Big Game/engine.exe", new byte[5000]);
        _tmp.Write("games/pc/Big Game/setup.exe", new byte[90_000]);
        _tmp.Write("games/pc/Big Game/vcredist_x64.exe", new byte[90_000]);
        _tmp.Write("games/pc/Big Game/DXSETUP.exe", new byte[90_000]);

        _tmp.Write("games/pc/Deep Game/bin/deep.exe", new byte[100]);
        _tmp.Write("games/pc/Deep Game/readme.txt", "x");

        _tmp.Write("games/pc/Steam Thing/Steam Thing.url", "[InternetShortcut]\nURL=steam://rungameid/1\n");
        _tmp.Write("games/pc/Nothing Here/readme.txt", "x");
        _tmp.Write("games/pc/Direct Game.exe", new byte[100]);
        _tmp.Write("games/pc/Installed Game.lnk", new byte[100]);
        _tmp.Write("games/pc/notes.txt", "x");

        var r = await Scan();

        var celeste = G("games/pc/Celeste/Celeste.exe");
        Assert.Equal("pc", celeste.SystemId);
        Assert.Equal("Celeste", celeste.Title);
        Assert.Equal(GameKind.Executable, celeste.Kind);
        Assert.Null(celeste.Crc32);
        Assert.Equal("Big Game", G("games/pc/Big Game/engine.exe").Title);
        Assert.Equal("Deep Game", G("games/pc/Deep Game/bin/deep.exe").Title);
        Assert.Equal(GameKind.Shortcut, G("games/pc/Steam Thing/Steam Thing.url").Kind);
        Assert.Equal(GameKind.Executable, G("games/pc/Direct Game.exe").Kind);
        var lnk = G("games/pc/Installed Game.lnk");
        Assert.Equal(GameKind.Shortcut, lnk.Kind);
        Assert.Equal("Installed Game", lnk.Title);
        Assert.Equal(6, r.Added);
        var issue = Assert.Single(r.Unknown);
        Assert.Equal("games/pc/Nothing Here", issue.Path);
    }

    [Fact]
    public async Task Pc_game_keeps_its_id_when_an_update_renames_the_exe()
    {
        _tmp.Write("games/pc/Indie/indie_v1.exe", new byte[100]);
        await Scan();
        var before = G("games/pc/Indie/indie_v1.exe");
        _repo.SetFavorite(before.Id, true);

        File.Delete(_tmp.Full("games/pc/Indie/indie_v1.exe"));
        _tmp.Write("games/pc/Indie/indie_v2.exe", new byte[200]);
        var r = await Scan();

        var after = G("games/pc/Indie/indie_v2.exe");
        Assert.Equal(before.Id, after.Id);
        Assert.True(after.Favorite);
        Assert.Equal(0, r.Added);
        Assert.Equal(0, r.Missing);
    }

    // ───────────────────────── incremental / rename / duplicates / missing ─────────────────────────

    [Fact]
    public async Task Incremental_scan_skips_unchanged_and_rechecks_changed_files()
    {
        _tmp.Write("games/gba/A.gba", RomImages.Gba(seed: 1));
        var b = _tmp.Write("games/gba/B.gba", RomImages.Gba(seed: 2));
        _tmp.Write("games/nes/C.nes", RomImages.Nes());
        var first = await Scan();
        Assert.Equal(3, first.Added);
        var oldCrc = G("games/gba/B.gba").Crc32;

        var second = await Scan();
        Assert.Equal(0, second.Added);
        Assert.Equal(0, second.Updated);
        Assert.Equal(3, second.Unchanged);

        File.WriteAllBytes(b, RomImages.Gba(seed: 3));
        Touch(b);
        var third = await Scan();
        Assert.Equal(1, third.Updated);
        Assert.Equal(2, third.Unchanged);
        Assert.NotEqual(oldCrc, G("games/gba/B.gba").Crc32);

        // A Full scan re-checks every file, but only writes/counts games whose scanned data really changed.
        var calls = _repo.UpsertManyCalls;
        var full = await Scan(new ScanOptions { Full = true });
        Assert.Equal(0, full.Updated);
        Assert.Equal(3, full.Unchanged);
        Assert.Equal(calls, _repo.UpsertManyCalls);

        // Content replaced with the same size and an identical time stamp: only a Full scan notices.
        var t = File.GetLastWriteTimeUtc(b);
        File.WriteAllBytes(b, RomImages.Gba(seed: 4));
        File.SetLastWriteTimeUtc(b, t);
        Assert.Equal(3, (await Scan()).Unchanged);
        full = await Scan(new ScanOptions { Full = true });
        Assert.Equal(1, full.Updated);
        Assert.Equal(2, full.Unchanged);
    }

    [Fact]
    public async Task Rename_and_move_keep_the_game_id_playtime_and_favourite()
    {
        var a = _tmp.Write("games/gba/Old Name (USA).gba", RomImages.Gba("RENAMED", "BREE"));
        await Scan();
        var game = G("games/gba/Old Name (USA).gba");
        _repo.SetFavorite(game.Id, true);
        _repo.AddSession(new PlaySession { GameId = game.Id, StartUtc = DateTime.UtcNow.AddHours(-1), EndUtc = DateTime.UtcNow, Seconds = 3600 });

        File.Move(a, _tmp.Full("games/gba/New Name (USA).gba"));
        var r = await Scan();
        Assert.Equal(0, r.Added);
        Assert.Equal(1, r.Updated);
        Assert.Equal(0, r.Missing);
        var renamed = G("games/gba/New Name (USA).gba");
        Assert.Equal(game.Id, renamed.Id);
        Assert.True(renamed.Favorite);
        Assert.Equal(3600, renamed.PlaySeconds);
        Assert.Single(_repo.All());

        // Moved into another library folder.
        var ext = _tmp.External("Elsewhere");
        Directory.CreateDirectory(ext);
        var folderId = _repo.UpsertFolder(new LibraryFolder { Path = ext });
        File.Move(_tmp.Full("games/gba/New Name (USA).gba"), Path.Combine(ext, "Moved (USA).gba"));
        r = await Scan();
        Assert.Equal(0, r.Added);
        Assert.Equal(0, r.Missing);
        var moved = Assert.Single(_repo.All());
        Assert.Equal(game.Id, moved.Id);
        Assert.Equal(folderId, moved.FolderId);
        Assert.Equal(Path.Combine(ext, "Moved (USA).gba"), moved.Path);
        Assert.Equal(3600, moved.PlaySeconds);
    }

    [Fact]
    public async Task Rename_without_hashes_falls_back_to_name_and_size_when_moved_to_another_folder()
    {
        _tmp.Write("games/gba/sub1/Game.gba", RomImages.Gba());
        var opts = new ScanOptions { ComputeHashes = false };
        await Scan(opts);
        var id = G("games/gba/sub1/Game.gba").Id;
        Directory.CreateDirectory(_tmp.Full("games/gba/sub2"));
        File.Move(_tmp.Full("games/gba/sub1/Game.gba"), _tmp.Full("games/gba/sub2/Game.gba"));
        await Scan(opts);
        Assert.Equal(id, G("games/gba/sub2/Game.gba").Id);
        Assert.Null(G("games/gba/sub2/Game.gba").Crc32);
    }

    [Fact]
    public async Task Duplicates_are_reported_and_not_added()
    {
        var img = RomImages.Gba();
        _tmp.Write("games/gba/Original.gba", img);
        await Scan();
        _tmp.Write("games/gba/Copy of Original.gba", img);
        _tmp.Write("games/misc/Another Copy.gba", img);

        var r = await Scan();

        Assert.Equal(0, r.Added);
        Assert.Equal(2, r.Duplicates.Count);
        Assert.All(r.Duplicates, d => Assert.Contains("games/gba/Original.gba", d.Reason));
        Assert.Single(_repo.All());

        // Same scan: two identical new files → first added, second duplicate.
        _tmp.Write("games/nes/X1.nes", RomImages.Nes(seed: 50));
        _tmp.Write("games/nes/X2.nes", RomImages.Nes(seed: 50));
        r = await Scan();
        Assert.Equal(1, r.Added);
        Assert.Contains(r.Duplicates, d => d.Path == "games/nes/X2.nes");
    }

    [Fact]
    public async Task Missing_files_are_marked_and_restored_never_deleted()
    {
        var path = _tmp.Write("games/nes/Gone.nes", RomImages.Nes());
        _tmp.Write("games/nes/Stays.nes", RomImages.Nes(seed: 9));
        await Scan();
        var id = G("games/nes/Gone.nes").Id;
        _repo.SetFavorite(id, true);

        File.Move(path, _tmp.Full("away.nes"));
        var r = await Scan();
        Assert.Equal(1, r.Missing);
        Assert.Equal(GameStatus.Missing, _repo.Get(id)!.Status);
        Assert.Equal(2, _repo.All().Count);

        r = await Scan(); // still missing: counted, not rewritten
        Assert.Equal(1, r.Missing);

        File.Move(_tmp.Full("away.nes"), path);
        r = await Scan();
        Assert.Equal(0, r.Missing);
        Assert.Equal(1, r.Updated);
        var back = _repo.Get(id)!;
        Assert.Equal(GameStatus.Ok, back.Status);
        Assert.True(back.Favorite);
    }

    [Fact]
    public async Task Empty_and_truncated_files_are_broken_and_hashes_can_be_disabled()
    {
        _tmp.Write("games/gba/Empty.gba", []);
        _tmp.Write("games/nes/Cut.nes", RomImages.Nes(truncated: true));
        _tmp.Write("games/gba/Fine.gba", RomImages.Gba());

        await Scan(new ScanOptions { ComputeHashes = false });

        Assert.Equal(GameStatus.Broken, G("games/gba/Empty.gba").Status);
        Assert.Equal(GameStatus.Broken, G("games/nes/Cut.nes").Status);
        Assert.Equal(GameStatus.Ok, G("games/gba/Fine.gba").Status);
        Assert.All(_repo.All(), g => Assert.Null(g.Crc32));
    }

    [Fact]
    public async Task Header_title_is_used_only_for_meaningless_file_names()
    {
        _tmp.Write("games/gba/0001.gba", RomImages.Gba("HEADER TITLE", "BHTP"));
        _tmp.Write("games/gba/Proper Name (Europe).gba", RomImages.Gba("OTHER HEADER", "BOHP", seed: 77));

        await Scan();

        var coded = G("games/gba/0001.gba");
        Assert.Equal("HEADER TITLE", coded.Title);
        Assert.Equal("BHTP", coded.Serial);
        Assert.Equal("Europe", coded.Region); // from the game code
        Assert.Equal("Proper Name", G("games/gba/Proper Name (Europe).gba").Title);
    }

    // ───────────────────────── folders / threading ─────────────────────────

    [Fact]
    public async Task Registers_built_in_folder_and_sets_last_scan_time()
    {
        Assert.Empty(_repo.Folders());
        var r = await Scan();
        var f = Assert.Single(_repo.Folders());
        Assert.True(f.IsBuiltIn);
        Assert.Equal("games", f.Path);
        Assert.NotNull(f.LastScanUtc);
        Assert.Empty(r.Errors);

        _scanner.EnsureBuiltInFolder();
        Assert.Single(_repo.Folders());
    }

    [Fact]
    public async Task Missing_external_folder_is_an_error_and_its_games_become_missing()
    {
        var ext = _tmp.External("USB");
        _tmp.WriteExternal("USB/Game.gba", RomImages.Gba());
        _repo.UpsertFolder(new LibraryFolder { Path = ext });
        await Scan();
        Directory.Move(ext, _tmp.External("USB-unplugged"));

        var r = await Scan();

        Assert.Contains(r.Errors, e => e.Path == ext);
        Assert.Equal(1, r.Missing);
        Assert.Equal(GameStatus.Missing, Assert.Single(_repo.All()).Status);
    }

    [Fact]
    public async Task Only_requested_and_enabled_folders_are_scanned()
    {
        var ext = _tmp.External("Other");
        _tmp.WriteExternal("Other/X.gba", RomImages.Gba());
        _tmp.Write("games/nes/Y.nes", RomImages.Nes());
        var otherId = _repo.UpsertFolder(new LibraryFolder { Path = ext });
        var builtIn = _scanner.EnsureBuiltInFolder();

        var r = await Scan(new ScanOptions { FolderIds = [otherId] });
        Assert.Equal(1, r.Added);
        Assert.Equal("gba", Assert.Single(_repo.All()).SystemId);

        _repo.UpsertFolder(new LibraryFolder { Id = otherId, Path = ext, Enabled = false });
        File.Delete(Path.Combine(ext, "X.gba"));
        r = await Scan();
        Assert.Equal(1, r.Added); // only the built-in folder
        Assert.Equal(0, r.Missing); // disabled folder untouched
        Assert.Equal(GameStatus.Ok, _repo.All().Single(g => g.SystemId == "gba").Status);
        Assert.Equal(builtIn.Id, _repo.All().Single(g => g.SystemId == "nes").FolderId);
    }

    [Fact]
    public async Task Scan_is_cancellable_and_persists_progress_in_batches()
    {
        for (var i = 0; i < 600; i++) _tmp.Write($"games/nes/Game {i:D4}.nes", RomImages.Nes(prgBanks: 1, chrBanks: 0, seed: i));
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        // Deterministic cancel point: the NES sniffer runs once per file; cancel while checking file 450.
        var counting = new CountingSniffer(new RetroHub.Core.Library.Sniffers.NesSniffer(), 450, cts.Cancel);
        var scanner = new LibraryScanner(_catalog, _repo, _tmp.Paths, [counting]);

        var task = scanner.ScanAsync(new ScanOptions(), null, cts.Token);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);

        Assert.False(scanner.IsScanning);
        var stored = _repo.All().Count;
        Assert.InRange(stored, 400, 450); // batches written before the cancel (and the partial batch) were kept
        Assert.True(_repo.UpsertManyCalls >= 2);
        Assert.All(_repo.All(), g => Assert.NotEqual(GameStatus.Missing, g.Status));

        // The next scan continues incrementally.
        var r = await scanner.ScanAsync(new ScanOptions(), null, Ct);
        Assert.Equal(600 - stored, r.Added);
        Assert.Equal(stored, r.Unchanged);
    }

    private sealed class CountingSniffer(IRomSniffer inner, int cancelAt, Action cancel) : IRomSniffer
    {
        private int _calls;
        public string Id => inner.Id;
        public int HeaderBytes => inner.HeaderBytes;

        public SniffResult? Sniff(string path, ReadOnlySpan<byte> header)
        {
            if (Interlocked.Increment(ref _calls) == cancelAt) cancel();
            return inner.Sniff(path, header);
        }
    }

    [Fact]
    public async Task Pre_cancelled_token_returns_cancelled_task()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _scanner.ScanAsync(new ScanOptions(), null, cts.Token));
        Assert.False(_scanner.IsScanning);
    }

    [Fact]
    public async Task Progress_is_throttled()
    {
        for (var i = 0; i < 400; i++) _tmp.Write($"games/gba/G{i}.gba", RomImages.Gba(seed: i));
        var reports = new List<ScanProgress>();
        var sw = Stopwatch.StartNew();
        await _scanner.ScanAsync(new ScanOptions(), new SyncProgress<ScanProgress>(p => { lock (reports) reports.Add(p); }), Ct);
        sw.Stop();
        Assert.NotEmpty(reports);
        Assert.True(reports.Count <= sw.ElapsedMilliseconds / 100 + 8, $"{reports.Count} reports in {sw.ElapsedMilliseconds} ms");
        Assert.Equal(ScanPhases.Finishing, reports[^1].Phase);
    }

    [Fact]
    public async Task Runs_off_the_calling_thread_and_concurrent_calls_share_the_running_scan()
    {
        _tmp.Write("games/nes/A.nes", RomImages.Nes());
        var repo = new GatedRepository();
        var scanner = new LibraryScanner(_catalog, repo, _tmp.Paths);
        var callerThread = Environment.CurrentManagedThreadId;

        var first = scanner.ScanAsync(new ScanOptions(), null, Ct);
        Assert.True(repo.Entered.Wait(TimeSpan.FromSeconds(10), Ct)); // scan is now blocked inside Folders()
        Assert.NotEqual(callerThread, repo.ThreadId);
        Assert.True(scanner.IsScanning);
        var second = scanner.ScanAsync(new ScanOptions { FolderIds = [1] }, null, Ct); // covered by the running full scan
        repo.Release.Set();

        var r1 = await first;
        var r2 = await second;
        Assert.Same(r1, r2);
        Assert.Equal(1, r1.Added);
        Assert.False(scanner.IsScanning);

        // A request the running scan does not cover (Full) is queued and runs afterwards.
        repo.Reset();
        var third = scanner.ScanAsync(new ScanOptions(), null, Ct);
        Assert.True(repo.Entered.Wait(TimeSpan.FromSeconds(10), Ct));
        var fourth = scanner.ScanAsync(new ScanOptions { Full = true }, null, Ct);
        repo.Release.Set();
        var r3 = await third;
        var r4 = await fourth;
        Assert.NotSame(r3, r4);
        Assert.Equal(1, r3.Unchanged);
        Assert.Equal(1, r4.Unchanged); // re-checked by the Full scan, nothing changed
    }

    private sealed class GatedRepository : InMemoryGameRepository
    {
        public ManualResetEventSlim Entered { get; private set; } = new();
        public ManualResetEventSlim Release { get; private set; } = new();
        public int ThreadId { get; private set; }
        private int _blockOnce = 1;

        public void Reset()
        {
            Entered = new ManualResetEventSlim();
            Release = new ManualResetEventSlim();
            _blockOnce = 1;
        }

        public override IReadOnlyList<LibraryFolder> Folders()
        {
            if (Interlocked.Exchange(ref _blockOnce, 0) == 1)
            {
                ThreadId = Environment.CurrentManagedThreadId;
                Entered.Set();
                Release.Wait(TimeSpan.FromSeconds(30));
            }
            return base.Folders();
        }
    }

    [Fact]
    public async Task Repository_failures_are_reported_not_thrown()
    {
        _tmp.Write("games/nes/A.nes", RomImages.Nes());
        var scanner = new LibraryScanner(_catalog, new FailingRepository(), _tmp.Paths);
        var r = await scanner.ScanAsync(new ScanOptions(), null, Ct);
        Assert.NotEmpty(r.Errors);
    }

    private sealed class FailingRepository : InMemoryGameRepository
    {
        public override IReadOnlyList<Game> Query(GameQuery query) => throw new IOException("database is locked");
    }

    // ───────────────────────── DetectSystem ─────────────────────────

    [Fact]
    public void DetectSystem_for_single_files()
    {
        var gba = _tmp.Write("games/gba/x.gba", RomImages.Gba());
        var md = _tmp.Write("drop/sonic.md", RomImages.MegaDrive());
        var iso = _tmp.Write("drop/disc.iso", IsoBuilder.Ps2().Build());
        var data = _tmp.Write("drop/data.iso", new IsoBuilder { SystemId = "WIN32" }.Build());
        var txt = _tmp.Write("drop/readme.txt", "x");
        var exe = _tmp.Write("drop/tool.exe", new byte[10]);
        var cgb = _tmp.Write("drop/colour.gb", RomImages.Gb(cgb: 0xC0));
        _tmp.Write("games/pc/Some Game/game.exe", new byte[10]);

        Assert.Equal("gba", _scanner.DetectSystem(gba));
        Assert.Equal("megadrive", _scanner.DetectSystem(md));
        Assert.Equal("megadrive", _scanner.DetectSystem(md, "genesis"));
        Assert.Null(_scanner.DetectSystem(md, "gba")); // hint says gba, extension does not fit
        Assert.Equal("ps2", _scanner.DetectSystem(iso));
        Assert.Null(_scanner.DetectSystem(data));
        Assert.Null(_scanner.DetectSystem(txt));
        Assert.Null(_scanner.DetectSystem(exe));
        Assert.Equal("pc", _scanner.DetectSystem(exe, "pc"));
        Assert.Equal("gb", _scanner.DetectSystem(cgb)); // unique extension wins (order rule 3)
        Assert.Equal("pc", _scanner.DetectSystem(_tmp.Full("games/pc/Some Game")));
        Assert.Null(_scanner.DetectSystem(_tmp.Full("nope/missing.xyz")));
    }
}
