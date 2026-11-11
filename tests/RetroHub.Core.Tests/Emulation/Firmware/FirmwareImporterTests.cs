using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using RetroHub.Core.Catalog;
using RetroHub.Core.Emulation;
using RetroHub.Core.Emulation.Firmware;
using RetroHub.Core.Emulation.Portable;
using RetroHub.Core.Tests.Emulation.Config;
using Xunit;
using CatalogImpl = RetroHub.Core.Catalog.Catalog;

namespace RetroHub.Core.Tests.Emulation.Firmware;

/// <summary>
/// "Firmware &amp; Keys importieren" with FAKE files only: right names, sizes and structure, made-up bytes (zeros,
/// counters, "00112233…" keys). No real key, BIOS or firmware is used or created.
/// </summary>
public sealed class FirmwareImporterTests : IDisposable
{
    private readonly TempRoot _t = new();
    private readonly string _in;

    public FirmwareImporterTests()
    {
        _in = Path.Combine(_t.Base, "picked");
        Directory.CreateDirectory(_in);
    }

    public void Dispose() => _t.Dispose();

    // ---------- fake data ----------

    private static string Hex(int seed, int length = 32) =>
        string.Concat(Enumerable.Range(0, length).Select(i => "0123456789abcdef"[(seed + i) % 16]));

    private static string FakeProdKeys() =>
        $"header_key = {Hex(1, 64)}\nmaster_key_00 = {Hex(2)}\nkey_area_key_application_00 = {Hex(3)}\ntitlekek_00 = {Hex(4)}\n";

    private static string FakeTitleKeys() => $"{Hex(5)} = {Hex(6)}\n{Hex(7)} = {Hex(8)}\n";

    private static byte[] Bytes(int length, byte seed) => Enumerable.Range(0, length).Select(i => (byte)(seed + i * 7)).ToArray();

    private static string Md5(byte[] b) => Convert.ToHexStringLower(MD5.HashData(b));

    /// <summary>A PS2-BIOS-shaped file: 4 MiB with the ROMDIR entries RESET + ROMDIR at 0x2700.</summary>
    private static byte[] FakePs2Bios()
    {
        var b = new byte[4 * 1024 * 1024];
        Encoding.ASCII.GetBytes("RESET").CopyTo(b, 0x2700);
        Encoding.ASCII.GetBytes("ROMDIR").CopyTo(b, 0x2710);
        return b;
    }

    private string In(string relative, byte[] content)
    {
        var p = Path.Combine(_in, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllBytes(p, content);
        return p;
    }

    private string In(string relative, string content) => In(relative, Encoding.UTF8.GetBytes(content));

    private static string NcaName(int i, bool meta = false) => $"{i:x32}{(meta ? ".cnmt" : "")}.nca";

    private string FakeFirmwareFolder(string folder, int count, byte seed = 1)
    {
        for (int i = 0; i < count; i++) In($"{folder}/{NcaName(i + 1, i % 5 == 0)}", Bytes(64 + i, seed));
        return Path.Combine(_in, folder);
    }

    // ---------- systems / importer ----------

    private static readonly byte[] Bios7 = Bytes(16384, 7);
    private static readonly byte[] Bios9 = Bytes(4096, 9);

    private static SystemDef Nds() => new()
    {
        Id = "nds", Name = "Nintendo DS", SortOrder = 140,
        Bios =
        [
            new BiosFile { File = "bios7.bin", Md5 = [Md5(Bios7)], Sizes = [16384] },
            new BiosFile { File = "bios9.bin", Md5 = [Md5(Bios9)], Sizes = [4096] },
            new BiosFile { File = "firmware.bin", Sizes = [131072, 262144, 524288] },
        ],
    };

    private static readonly byte[] Scph = Bytes(524288, 55);

    private static SystemDef Ps1() => new()
    {
        Id = "ps1", Name = "Sony PlayStation", SortOrder = 200,
        Bios = [new BiosFile { File = "scph5501.bin", Md5 = [Md5(Scph)], Sizes = [524288] }],
    };

    private static SystemDef Dc() => new()
    {
        Id = "dreamcast", Name = "Sega Dreamcast", SortOrder = 330,
        Bios = [new BiosFile { File = "dc/dc_flash.bin", Sizes = [131072] }],
    };

    private static SystemDef Plain(string id, int order = 500) => new() { Id = id, Name = id, SortOrder = order };

    private static IReadOnlyList<FirmwareConsole> Consoles() => FirmwareCatalog.Build(
    [
        Plain("switch", 180), Plain("wiiu", 170), Plain("3ds", 160), Plain("gamecube", 150), Plain("wii", 155), Plain("ps2", 210),
        Plain("psp", 220), Plain("msx", 420), Nds(), Ps1(), Dc(),
    ]);

    private string EdenDir => Path.Combine(_t.Root, "emulators", "eden");
    private string CemuDir => Path.Combine(_t.Root, "emulators", "cemu");

    private FirmwareImporter Importer(bool eden = true, bool cemu = true)
    {
        if (eden) _t.Touch("emulators/eden/eden.exe");
        if (cemu) _t.Touch("emulators/cemu/Cemu.exe");
        return new FirmwareImporter(FirmwareLocations.From(_t.Paths, null), Consoles());
    }

    private string P(params string[] parts) => Path.Combine([_t.Root, .. parts]);

    private static FirmwareItemStatus Item(FirmwareImporter imp, string console, string name) =>
        imp.Status().Single(s => s.Console.Id == console).Items.Single(i => i.Name == name);

    // ---------- catalog ----------

    [Fact]
    public void Builtin_catalog_lists_every_console_that_needs_or_skips_files()
    {
        using var env = new RetroHub.Core.Tests.Catalog.CatalogTestEnv();
        var catalog = CatalogImpl.Load(env.Paths);
        Assert.Empty(catalog.LoadWarnings);
        var list = FirmwareCatalog.Build(catalog.Systems);
        var byId = list.ToDictionary(c => c.Id);
        Assert.Equal(FirmwareConsoleKind.Switch, byId["switch"].Kind);
        Assert.Equal(FirmwareConsoleKind.WiiU, byId["wiiu"].Kind);
        Assert.Equal(FirmwareConsoleKind.NoneNeeded, byId["3ds"].Kind);
        Assert.Equal(FirmwareConsoleKind.NoneNeeded, byId["gc-wii"].Kind);
        Assert.Equal(["gamecube", "wii"], byId["gc-wii"].SystemIds);
        Assert.Equal(FirmwareConsoleKind.Ps2, byId["ps2"].Kind);
        Assert.Equal(FirmwareConsoleKind.Assets, byId["psp"].Kind);
        Assert.Equal(FirmwareConsoleKind.Assets, byId["msx"].Kind);
        foreach (var id in new[] { "nds", "gba", "nes", "ps1", "saturn", "segacd", "dreamcast", "pcengine", "lynx", "atari7800", "arcade" })
            Assert.Equal(FirmwareConsoleKind.CatalogBios, byId[id].Kind);
        // every catalog BIOS without an MD5 has its sizes (recognised by name + size, like MonHub's Bios.Files)
        Assert.All(list.SelectMany(c => c.Bios).Where(b => b.Md5.Length == 0 && !b.File.EndsWith(".zip", StringComparison.Ordinal)),
            b => Assert.NotEmpty(b.Sizes));
        Assert.Equal([16384L], byId["nds"].Bios.Single(b => b.File == "bios7.bin").Sizes);
        // sorted like the catalog
        Assert.Equal(list.OrderBy(c => c.SortOrder).Select(c => c.Id), list.Select(c => c.Id));
    }

    [Fact]
    public void Classify_tells_the_key_files_apart_by_content()
    {
        Assert.Equal(KeyFileKind.SwitchProdKeys, FirmwareFiles.Classify(Encoding.UTF8.GetBytes(FakeProdKeys())));
        Assert.Equal(KeyFileKind.SwitchTitleKeys, FirmwareFiles.Classify(Encoding.UTF8.GetBytes(FakeTitleKeys())));
        Assert.Equal(KeyFileKind.WiiUKeys, FirmwareFiles.Classify(Encoding.UTF8.GetBytes($"# Wii U\n{Hex(9)} # Game\n")));
        // Azahar's aes_keys.txt style and plain text are neither
        Assert.Equal(KeyFileKind.None, FirmwareFiles.Classify(Encoding.UTF8.GetBytes($"slot0x25KeyX={Hex(3)}\n")));
        Assert.Equal(KeyFileKind.None, FirmwareFiles.Classify(Encoding.UTF8.GetBytes("hello world\nreadme\n")));
        Assert.Equal(KeyFileKind.None, FirmwareFiles.Classify([0, 1, 2, 3]));
        Assert.True(FirmwareFiles.IsNcaName(NcaName(3)));
        Assert.True(FirmwareFiles.IsNcaName(NcaName(3, meta: true).ToUpperInvariant().Replace(".NCA", ".nca")));
        Assert.False(FirmwareFiles.IsNcaName("game.nca"));
        using var ps2 = new MemoryStream(FakePs2Bios());
        Assert.True(FirmwareFiles.IsPs2Bios(ps2, ps2.Length));
        using var zeros = new MemoryStream(new byte[4 * 1024 * 1024]);
        Assert.False(FirmwareFiles.IsPs2Bios(zeros, zeros.Length));
    }

    // ---------- Switch ----------

    [Fact]
    public void Switch_keys_under_any_name_go_to_bios_switch_and_into_eden()
    {
        var imp = Importer();
        Assert.Equal(FirmwareItemState.Missing, Item(imp, "switch", "prod.keys").State);
        var prod = In("my switch/keys.txt", FakeProdKeys());
        var title = In("my switch/title.keys", FakeTitleKeys());

        var r = imp.Import([prod, title]);

        Assert.Contains(r.Items, i => i is { ConsoleId: "switch", Name: "prod.keys", Outcome: ImportOutcome.Imported });
        Assert.Contains(r.Items, i => i is { ConsoleId: "switch", Name: "title.keys", Outcome: ImportOutcome.Imported });
        Assert.Equal(FakeProdKeys(), File.ReadAllText(P("bios", "switch", "prod.keys")));
        Assert.Equal(FakeProdKeys(), File.ReadAllText(Path.Combine(EdenDir, "user", "keys", "prod.keys")));
        Assert.Equal(FakeTitleKeys(), File.ReadAllText(Path.Combine(EdenDir, "user", "keys", "title.keys")));
        Assert.Equal(FirmwareItemState.Present, Item(imp, "switch", "prod.keys").State);
        Assert.Equal(FirmwareItemState.Present, Item(imp, "switch", "title.keys").State);
        Assert.True(File.Exists(prod), "the source stays");

        // again: nothing written; newer keys: the old copy is kept
        Assert.All(imp.Import([prod]).Items, i => Assert.Equal(ImportOutcome.AlreadyPresent, i.Outcome));
        var newer = In("new/prod.keys", FakeProdKeys() + $"master_key_01 = {Hex(10)}\n");
        Assert.Equal(ImportOutcome.Replaced, imp.Import([newer]).Items.Single().Outcome);
        Assert.Single(Directory.GetFiles(P("bios", "switch"), "prod.keys" + EdenSetup.BackupInfix + "*"));
        Assert.Single(Directory.GetFiles(Path.Combine(EdenDir, "user", "keys"), "prod.keys" + EdenSetup.BackupInfix + "*"));
        // nothing but the picked folder and the RetroHub root in the temp base
        Assert.Equal(["picked", "root"], Directory.EnumerateFileSystemEntries(_t.Base).Select(Path.GetFileName).Order().ToArray());
    }

    [Fact]
    public void A_broken_prod_keys_shows_as_unknown_file()
    {
        var imp = Importer();
        _t.Touch("bios/switch/prod.keys", "this is not a key file");
        Assert.Equal(FirmwareItemState.Unknown, Item(imp, "switch", "prod.keys").State);
    }

    [Fact]
    public void Switch_firmware_folder_goes_flat_into_edens_registered_and_an_old_one_is_kept()
    {
        var imp = Importer();
        var fw = FakeFirmwareFolder("Firmware 19.0.1", 40);
        var registered = EdenSetup.FirmwarePath(EdenDir);

        var r = imp.Import([fw]);

        var item = Assert.Single(r.Items);
        Assert.Equal(("switch", "Firmware", ImportOutcome.Imported, 40), (item.ConsoleId, item.Name, item.Outcome, item.Count));
        Assert.Equal(Path.Combine(EdenDir, "user", "nand", "system", "Contents", "registered"), registered);
        Assert.Equal(40, Directory.GetFiles(registered, "*.nca").Length);
        Assert.Equal(Bytes(64, 1), File.ReadAllBytes(Path.Combine(registered, NcaName(1, meta: true))));
        Assert.Equal(40, Item(imp, "switch", "Firmware").Count);
        Assert.Equal(FirmwareItemState.Present, Item(imp, "switch", "Firmware").State);

        Assert.Equal(ImportOutcome.AlreadyPresent, imp.Import([fw]).Items.Single().Outcome);

        var other = FakeFirmwareFolder("Firmware 20.0.0", 30, seed: 2);
        Assert.Equal(ImportOutcome.Replaced, imp.Import([other]).Items.Single().Outcome);
        Assert.Equal(30, Directory.GetFiles(registered).Length);
        var backup = Assert.Single(Directory.GetDirectories(Path.GetDirectoryName(registered)!, "registered" + EdenSetup.BackupInfix + "*"));
        Assert.Equal(40, Directory.GetFiles(backup).Length);
        Assert.False(Directory.Exists(registered + ".retrohub-new"));
    }

    [Fact]
    public void A_few_nca_files_are_no_firmware()
    {
        var imp = Importer();
        var game = In("game/" + NcaName(1), Bytes(100, 3));
        var r = imp.Import([game]);
        Assert.Empty(r.Items);
        Assert.Equal(SkipReason.LoneNca, Assert.Single(r.Skipped).Reason);
        Assert.False(Directory.Exists(EdenSetup.FirmwarePath(EdenDir)));
    }

    [Fact]
    public void Firmware_zip_without_eden_waits_in_bios_and_eden_setup_installs_it()
    {
        var imp = Importer(eden: false);
        var zip = Path.Combine(_in, "firmware.zip");
        using (var z = ZipFile.Open(zip, ZipArchiveMode.Create))
            for (int i = 0; i < 12; i++)
            {
                using var s = z.CreateEntry(NcaName(i + 1)).Open();
                s.Write(Bytes(50, (byte)i));
            }

        var r = imp.Import([zip]);

        Assert.Equal(ImportOutcome.Imported, r.Items.Single().Outcome);
        Assert.Empty(r.Skipped);
        var staged = EdenSetup.StagedFirmwarePath(_t.Paths.Bios);
        Assert.Equal(12, Directory.GetFiles(staged, "*.nca").Length);

        // Eden arrives later: its setup installs the waiting firmware once, never over an existing one
        var exe = _t.Touch("emulators/eden/eden.exe");
        var install = Make.Install(new EmulatorDef { Id = "eden", Name = "Eden", Portable = "eden" }, exe);
        Assert.Equal(PortableApplyResult.Applied, PortableSetups.Apply(install, Make.Plan("switch", LaunchMode.Fullscreen), _t.Paths));
        Assert.Equal(12, Directory.GetFiles(EdenSetup.FirmwarePath(EdenDir), "*.nca").Length);
        Assert.False(EdenSetup.InstallStagedFirmware(_t.Paths.Bios, EdenDir));
    }

    // ---------- Wii U ----------

    [Fact]
    public void Wii_u_keys_are_merged_without_duplicates_into_bios_and_cemu()
    {
        var imp = Importer();
        var cemuKeys = CemuSetup.KeysPath(CemuDir);
        _t.Touch("emulators/cemu/portable/keys.txt", $"# example\r\n{Hex(1).ToUpperInvariant()} # mine\r\n");
        var picked = In("wiiu/keys.txt", $"{Hex(1)} # same key\n{Hex(2)} # Game B\n{Hex(3)}\n");
        var disc = In("wiiu/Game C.key", Bytes(16, 40));

        var r = imp.Import([picked, disc]);

        var item = r.Items.Single();
        Assert.Equal(("wiiu", "keys.txt", ImportOutcome.Imported), (item.ConsoleId, item.Name, item.Outcome));
        var cemu = File.ReadAllText(cemuKeys);
        Assert.StartsWith($"# example\r\n{Hex(1).ToUpperInvariant()} # mine\r\n", cemu);
        Assert.Equal(4, CemuSetup.ParseKeys(cemu).Count());
        Assert.Contains("Game C.key", cemu);
        Assert.Equal(4, CemuSetup.ParseKeys(File.ReadAllText(CemuSetup.StagedKeysPath(_t.Paths.Bios))).Count());
        Assert.Equal(4, Item(imp, "wiiu", "keys.txt").Count);

        Assert.Equal(ImportOutcome.AlreadyPresent, imp.Import([picked, disc]).Items.Single().Outcome);
        Assert.Equal(cemu, File.ReadAllText(cemuKeys));
    }

    [Fact]
    public void Cemu_setup_merges_the_staged_keys_and_switches_off_the_update_check()
    {
        _t.Touch("bios/wiiu/keys.txt", $"{Hex(4)} # Game D\n");
        var exe = _t.Touch("emulators/cemu/Cemu.exe");
        var install = Make.Install(new EmulatorDef { Id = "cemu", Name = "Cemu", Portable = "cemu" }, exe);
        Assert.Equal(PortableApplyResult.Applied, PortableSetups.Apply(install, Make.Plan("wiiu", LaunchMode.Fullscreen), _t.Paths));
        Assert.Contains(Hex(4), File.ReadAllText(CemuSetup.KeysPath(CemuDir)));
        var settings = System.Xml.Linq.XDocument.Load(Path.Combine(CemuDir, "portable", "settings.xml"));
        Assert.Equal("content", settings.Root!.Name.LocalName);
        Assert.Equal("false", settings.Root.Element("check_update")!.Value);

        // an existing settings.xml keeps everything else
        var file = Path.Combine(CemuDir, "portable", "settings.xml");
        File.WriteAllText(file, "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<content>\n    <mlc_path>x</mlc_path>\n    <check_update>true</check_update>\n</content>\n");
        PortableSetups.Apply(install, Make.Plan("wiiu", LaunchMode.Fullscreen), _t.Paths);
        var text = File.ReadAllText(file);
        Assert.Contains("<mlc_path>x</mlc_path>", text);
        Assert.Contains("<check_update>false</check_update>", text);
        var before = File.ReadAllBytes(file);
        PortableSetups.Apply(install, Make.Plan("wiiu", LaunchMode.Fullscreen), _t.Paths);
        Assert.Equal(before, File.ReadAllBytes(file));
    }

    // ---------- catalog BIOS ----------

    [Fact]
    public void Ds_bios_is_recognised_by_md5_under_any_name_and_by_name_and_size()
    {
        var imp = Importer();
        var zip = Path.Combine(_in, "ds.zip");
        using (var z = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            void Add(string name, byte[] data) { using var s = z.CreateEntry(name).Open(); s.Write(data); }
            Add("DS/biosnds7.rom", Bios7);                 // DeSmuME's name, known MD5
            Add("DS/bios9.bin", Bios9);
            Add("DS/firmware.bin", Bytes(262144, 1));      // no MD5: name + size
            Add("DSi/firmware.bin", Bytes(131072, 2));     // DSi folder: only when nothing else
            Add("DSi/bios7.bin", Bytes(65536, 3));         // DSi size: wrong size
            Add("readme.txt", Encoding.UTF8.GetBytes("hello"));
        }

        var r = imp.Import([zip]);

        Assert.Equal(["bios7.bin", "bios9.bin", "firmware.bin"], r.Items.Select(i => i.Name).Order().ToArray());
        Assert.All(r.Items, i => Assert.Equal(ImportOutcome.Imported, i.Outcome));
        Assert.Equal(Bios7, File.ReadAllBytes(P("bios", "bios7.bin")));
        Assert.Equal(Bytes(262144, 1), File.ReadAllBytes(P("bios", "firmware.bin")));
        Assert.Contains(r.Skipped, s => s is { Name: "bios7.bin", Reason: SkipReason.WrongSize });
        Assert.Contains(r.Skipped, s => s is { Name: "readme.txt", Reason: SkipReason.Unknown });
        Assert.DoesNotContain(r.Skipped, s => s.Name == "ds.zip");
        Assert.All(imp.Status().Single(s => s.Console.Id == "nds").Items, i => Assert.Equal(FirmwareItemState.Present, i.State));
    }

    [Fact]
    public void An_unknown_dump_never_replaces_a_known_good_one()
    {
        var imp = Importer();
        var odd = In("odd/scph5501.bin", Bytes(524288, 99));
        var r1 = imp.Import([odd]);
        Assert.Equal(ImportOutcome.ImportedUnknownVersion, r1.Items.Single().Outcome);
        Assert.Equal(FirmwareItemState.Unknown, Item(imp, "ps1", "scph5501.bin").State);

        var good = In("good/SCPH-5501 (USA).BIN", Scph);
        Assert.Equal(ImportOutcome.Replaced, imp.Import([good]).Items.Single().Outcome);
        Assert.Equal(FirmwareItemState.Present, Item(imp, "ps1", "scph5501.bin").State);
        Assert.Single(Directory.GetFiles(P("bios"), "scph5501.bin" + EdenSetup.BackupInfix + "*"));

        Assert.Equal(ImportOutcome.KeptExisting, imp.Import([odd]).Items.Single().Outcome);
        Assert.Equal(Scph, File.ReadAllBytes(P("bios", "scph5501.bin")));
    }

    [Fact]
    public void A_catalog_subpath_is_kept_and_a_wrong_size_is_refused()
    {
        var imp = Importer();
        var r = imp.Import([In("x/dc_flash.bin", Bytes(131072, 4)), In("y/dc_boot.bin", Bytes(10, 1))]);
        Assert.Equal(ImportOutcome.Imported, r.Items.Single().Outcome);
        Assert.True(File.Exists(P("bios", "dc", "dc_flash.bin")));
        Assert.Contains(r.Skipped, s => s.Name == "dc_boot.bin");
    }

    // ---------- PS2, assets, rest ----------

    [Fact]
    public void Ps2_bios_is_recognised_by_its_romdir_and_kept_under_its_name()
    {
        var imp = Importer();
        Assert.Equal(FirmwareItemState.Missing, Item(imp, "ps2", "PS2-BIOS").State);
        var r = imp.Import([In("SCPH-70004_BIOS_V12_EUR_200.BIN", FakePs2Bios()), In("other.bin", new byte[4 * 1024 * 1024])]);
        Assert.Equal(("ps2", ImportOutcome.Imported), (r.Items.Single().ConsoleId, r.Items.Single().Outcome));
        Assert.True(File.Exists(P("bios", "pcsx2", "bios", "SCPH-70004_BIOS_V12_EUR_200.BIN")));
        Assert.Equal(1, Item(imp, "ps2", "PS2-BIOS").Count);
        Assert.Contains(r.Skipped, s => s is { Name: "other.bin", Reason: SkipReason.Unknown });
    }

    [Fact]
    public void Ppsspp_and_bluemsx_data_folders_are_copied_below_bios()
    {
        var imp = Importer();
        In("ppsspp-1.19/assets/ppge_atlas.zim", Bytes(300, 1));
        In("ppsspp-1.19/assets/flash0/font/jpn0.pgf", Bytes(200, 2));
        In("blueMSX/Machines/Shared Roms/MSX.rom", Bytes(32768, 3));
        In("blueMSX/Machines/MSX2/config.ini", "x");
        In("blueMSX/Databases/msxromdb.xml", "<x/>");

        var r = imp.Import([_in]);

        Assert.Contains(r.Items, i => i is { ConsoleId: "psp", Name: "PPSSPP/", Outcome: ImportOutcome.Imported, Count: 2 });
        Assert.Contains(r.Items, i => i is { ConsoleId: "msx", Name: "Machines/", Count: 2 });
        Assert.Contains(r.Items, i => i is { ConsoleId: "msx", Name: "Databases/" });
        Assert.True(File.Exists(P("bios", "PPSSPP", "flash0", "font", "jpn0.pgf")));
        Assert.True(File.Exists(P("bios", "Machines", "Shared Roms", "MSX.rom")));
        Assert.True(File.Exists(P("bios", "Databases", "msxromdb.xml")));
        Assert.All(imp.Status().Where(s => s.Console.Id is "psp" or "msx").SelectMany(s => s.Items), i => Assert.Equal(FirmwareItemState.Present, i.State));
        Assert.Empty(r.Skipped);
    }

    [Fact]
    public void Other_archives_and_unknown_files_are_reported_by_name_only()
    {
        var imp = Importer();
        var r = imp.Import([In("keys.7z", Bytes(10, 1)), In("notes.txt", "my notes")]);
        Assert.Empty(r.Items);
        Assert.Contains(r.Skipped, s => s is { Name: "keys.7z", Reason: SkipReason.UnsupportedArchive });
        Assert.Contains(r.Skipped, s => s is { Name: "notes.txt", Reason: SkipReason.Unknown });
        Assert.False(r.Changed);
    }

    [Fact]
    public void Emulators_outside_the_retrohub_folder_are_never_written_to()
    {
        var outside = Path.Combine(_t.Base, "MyEden", "eden.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(outside)!);
        File.WriteAllText(outside, "");
        var registry = new FakeRegistry().Set(Make.Install(new EmulatorDef { Id = "eden", Name = "Eden", Portable = "eden" }, outside));
        var loc = FirmwareLocations.From(_t.Paths, registry);
        Assert.Null(loc.EdenDir);
        var imp = new FirmwareImporter(loc, Consoles());
        imp.Import([In("prod.keys", FakeProdKeys())]);
        Assert.True(File.Exists(P("bios", "switch", "prod.keys")));
        Assert.False(Directory.Exists(Path.Combine(_t.Base, "MyEden", "user")));
    }

    [Fact]
    public void Dolphin_and_gamecube_need_nothing()
    {
        var imp = Importer();
        var gc = imp.Status().Single(s => s.Console.Id == "gc-wii");
        Assert.Equal(FirmwareNeed.None, gc.Console.Need);
        Assert.Empty(gc.Items);
        Assert.Empty(imp.Status().Single(s => s.Console.Id == "3ds").Items);
    }
}
