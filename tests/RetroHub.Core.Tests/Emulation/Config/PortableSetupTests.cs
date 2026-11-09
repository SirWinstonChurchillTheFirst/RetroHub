using System.Text;
using RetroHub.Core.Catalog;
using RetroHub.Core.Emulation;
using RetroHub.Core.Emulation.ConfigFiles;
using RetroHub.Core.Emulation.Portable;
using Xunit;

namespace RetroHub.Core.Tests.Emulation.Config;

public sealed class PortableSetupTests : IDisposable
{
    private readonly TempRoot _t = new();

    public void Dispose() => _t.Dispose();

    private (EmulatorInstall Install, string Exe) Emu(string id, string relExe, string? portable = null)
    {
        var exe = _t.Touch(relExe);
        var def = new EmulatorDef { Id = id, Name = id, Exe = [Path.GetFileName(relExe)], Portable = portable ?? id };
        return (Make.Install(def, exe), exe);
    }

    private string P(params string[] parts) => Path.Combine([_t.Root, .. parts]);

    /// <summary>Runs the recipe twice: second run must change no file (content and timestamp).</summary>
    private void RunTwiceIdempotent(EmulatorInstall install, LaunchPlan plan)
    {
        Assert.Equal(PortableApplyResult.Applied, PortableSetups.Apply(install, plan, _t.Paths));
        var first = State();
        Thread.Sleep(30);
        Assert.Equal(PortableApplyResult.Applied, PortableSetups.Apply(install, plan, _t.Paths));
        Assert.Equal(first, State());
        Assert.True(_t.NothingOutsideRoot(), "a recipe wrote next to the RetroHub root");
        Assert.DoesNotContain(_t.Files(), f => f.EndsWith(".retrohub-tmp", StringComparison.Ordinal));
    }

    private List<string> State() => _t.Files()
        .Select(f => $"{f}|{File.GetLastWriteTimeUtc(Path.Combine(_t.Root, f)).Ticks}|{Convert.ToHexString(File.ReadAllBytes(Path.Combine(_t.Root, f)))}")
        .ToList();

    [Fact]
    public void All_recipes_are_registered_once()
    {
        var ids = PortableSetups.All.Select(s => s.Id).ToList();
        Assert.Equal(["retroarch", "mgba", "melonds", "desmume", "azahar", "dolphin", "pcsx2", "duckstation", "ppsspp", "cemu", "eden"], ids);
        Assert.Same(PortableSetups.All[1], PortableSetups.Find("MGBA"));
        Assert.Null(PortableSetups.Find("nope"));
        Assert.All(PortableSetups.All, s => Assert.False(string.IsNullOrWhiteSpace(PortableSetups.SummaryFor(s.Id))));
    }

    [Fact]
    public void RetroArch_writes_only_the_override_file()
    {
        var (install, exe) = Emu("retroarch", "emulators/retroarch/RetroArch-Win64/retroarch.exe");
        var userCfg = _t.Touch("emulators/retroarch/RetroArch-Win64/retroarch.cfg", "video_fullscreen = \"false\"\nsavefile_directory = \"default\"\n");
        var userBytes = File.ReadAllBytes(userCfg);

        RunTwiceIdempotent(install, Make.Plan("gba", LaunchMode.Fullscreen));

        Assert.Equal(userBytes, File.ReadAllBytes(userCfg));
        var file = P("data", "config", "retroarch", "retrohub.cfg");
        var cfg = RetroArchConfig.Load(file);
        Assert.Equal(P("bios"), cfg.Get("system_directory"));
        // straight into the system's folder – also for games in external folders (no per-game-folder subfolder)
        Assert.Equal(P("saves", "gba"), cfg.Get("savefile_directory"));
        Assert.Equal(P("savestates", "gba"), cfg.Get("savestate_directory"));
        Assert.Equal(P("screenshots", "gba"), cfg.Get("screenshot_directory"));
        Assert.Equal("false", cfg.Get("sort_savefiles_by_content_enable"));
        Assert.Equal("false", cfg.Get("sort_savestates_by_content_enable"));
        Assert.Equal("false", cfg.Get("sort_screenshots_by_content_enable"));
        Assert.Equal("false", cfg.Get("sort_savefiles_enable"));
        Assert.Equal("false", cfg.Get("savefiles_in_content_dir"));
        Assert.Equal("true", cfg.Get("video_fullscreen"));
        Assert.Equal("true", cfg.Get("video_windowed_fullscreen"));
        Assert.Equal("false", cfg.Get("quit_press_twice"));
        Assert.Equal("false", cfg.Get("config_save_on_exit"));
        Assert.Null(cfg.Get("pause_nonactive"));
        foreach (var d in new[] { "bios", "saves", "savestates", "screenshots" }) Assert.True(Directory.Exists(P(d)));
        Assert.True(Directory.Exists(P("saves", "gba")));

        // embedded/windowed: fullscreen off, pause_nonactive off only when embedded
        PortableSetups.Apply(install, Make.Plan("gba", LaunchMode.Embedded), _t.Paths);
        cfg = RetroArchConfig.Load(file);
        Assert.Equal("false", cfg.Get("video_fullscreen"));
        Assert.Equal("false", cfg.Get("pause_nonactive"));
        PortableSetups.Apply(install, Make.Plan("gba", LaunchMode.Windowed), _t.Paths);
        cfg = RetroArchConfig.Load(file);
        Assert.Equal("false", cfg.Get("video_fullscreen"));
        Assert.Null(cfg.Get("pause_nonactive"));
        Assert.Equal(userBytes, File.ReadAllBytes(userCfg));
        _ = exe;
    }

    [Fact]
    public void RetroArch_override_keeps_extra_user_lines()
    {
        var (install, _) = Emu("retroarch", "emulators/retroarch/retroarch.exe");
        var file = _t.Touch("data/config/retroarch/retrohub.cfg", "# mine\nvideo_monitor_index = \"2\"\n");
        PortableSetups.Apply(install, Make.Plan("snes", LaunchMode.Fullscreen), _t.Paths);
        var cfg = RetroArchConfig.Load(file);
        Assert.Equal("2", cfg.Get("video_monitor_index"));
        Assert.Contains("# mine", File.ReadAllText(file));
    }

    [Fact]
    public void Mgba_portable_marker_and_paths()
    {
        var (install, _) = Emu("mgba", "emulators/mgba/mGBA-0.10.5-win64/mGBA.exe");
        var dir = P("emulators", "mgba", "mGBA-0.10.5-win64");
        File.WriteAllText(Path.Combine(dir, "config.ini"), "[ports.qt]\nsavegamePath=C:\\old\naudioSync=1\n\n[gba.input.QT_K]\nkeyA=88\n");

        RunTwiceIdempotent(install, Make.Plan("gba", LaunchMode.Embedded));

        Assert.True(File.Exists(Path.Combine(dir, "portable.ini")));
        var ini = IniFile.Load(Path.Combine(dir, "config.ini"));
        Assert.Equal(P("saves", "gba"), ini.Get("ports.qt", "savegamePath"));
        Assert.Equal(P("savestates", "gba"), ini.Get("ports.qt", "savestatePath"));
        Assert.Equal(P("screenshots", "gba"), ini.Get("ports.qt", "screenshotPath"));
        Assert.Equal(P("games", "gba"), ini.Get("ports.qt", "lastDirectory"));
        Assert.Equal("0", ini.Get("ports.qt", "pauseOnFocusLost"));
        Assert.Equal("1", ini.Get("ports.qt", "audioSync"));
        Assert.Equal("88", ini.Get("gba.input.QT_K", "keyA"));
        Assert.True(Directory.Exists(P("saves", "gba")));

        // a GB game switches the folders; not embedded → pauseOnFocusLost untouched
        PortableSetups.Apply(install, Make.Plan("gb", LaunchMode.Fullscreen), _t.Paths);
        ini = IniFile.Load(Path.Combine(dir, "config.ini"));
        Assert.Equal(P("saves", "gb"), ini.Get("ports.qt", "savegamePath"));
        Assert.Equal("0", ini.Get("ports.qt", "pauseOnFocusLost"));
    }

    [Fact]
    public void Mgba_existing_portable_ini_is_not_rewritten()
    {
        var (install, _) = Emu("mgba", "emulators/mgba/mGBA.exe");
        var marker = _t.Touch("emulators/mgba/portable.ini", "keep me");
        PortableSetups.Apply(install, Make.Plan("gba", LaunchMode.Fullscreen), _t.Paths);
        Assert.Equal("keep me", File.ReadAllText(marker));
    }

    [Fact]
    public void MelonDs_toml_paths_and_bios()
    {
        var (install, _) = Emu("melonds", "emulators/melonds/melonDS.exe");
        var toml = _t.Touch("emulators/melonds/melonDS.toml",
            "LastBIOSFolder = \"\"\nPauseLostFocus = true\n\n[DS]\nFirmwarePath = \"\"\nBIOS7Path = \"\"\nBIOS9Path = \"\"\n\n" +
            "[Instance0]\nEnableCheats = false\nSaveFilePath = \"\"\n\n[Instance0.Keyboard]\nA = 88\n\n[Emu]\nExternalBIOSEnable = false\n");

        // without BIOS files: BIOS settings untouched
        PortableSetups.Apply(install, Make.Plan("nds", LaunchMode.Embedded), _t.Paths);
        var t = TomlFile.Load(toml);
        Assert.Equal(P("games", "nds").Replace('\\', '/'), t.Get(null, "LastROMFolder"));
        Assert.Equal("false", t.Get(null, "PauseLostFocus"));
        Assert.Equal(P("saves", "nds").Replace('\\', '/'), t.Get("Instance0", "SaveFilePath"));
        Assert.Equal(P("savestates", "nds").Replace('\\', '/'), t.Get("Instance0", "SavestatePath"));
        Assert.Equal("", t.Get("DS", "BIOS7Path"));
        Assert.Equal("false", t.Get("Emu", "ExternalBIOSEnable"));
        Assert.Equal("88", t.Get("Instance0.Keyboard", "A"));
        Assert.Null(t.Get("Instance0.Keyboard", "SaveFilePath"));
        // top-level key was inserted before the first table
        Assert.True(t.RawLines.ToList().FindIndex(l => l.StartsWith("LastROMFolder")) < t.RawLines.ToList().IndexOf("[DS]"));

        // with all three BIOS files (in a subfolder)
        _t.Touch("bios/nds/bios7.bin", "7");
        _t.Touch("bios/nds/bios9.bin", "9");
        _t.Touch("bios/nds/firmware.bin", "f");
        RunTwiceIdempotent(install, Make.Plan("nds", LaunchMode.Embedded));
        t = TomlFile.Load(toml);
        Assert.Equal(P("bios", "nds", "bios7.bin").Replace('\\', '/'), t.Get("DS", "BIOS7Path"));
        Assert.Equal(P("bios", "nds", "bios9.bin").Replace('\\', '/'), t.Get("DS", "BIOS9Path"));
        Assert.Equal(P("bios", "nds", "firmware.bin").Replace('\\', '/'), t.Get("DS", "FirmwarePath"));
        Assert.Equal("true", t.Get("Emu", "ExternalBIOSEnable"));
    }

    [Fact]
    public void MelonDs_creates_a_config_when_missing()
    {
        var (install, _) = Emu("melonds", "emulators/melonds/melonDS.exe");
        RunTwiceIdempotent(install, Make.Plan("nds", LaunchMode.Fullscreen));
        var t = TomlFile.Load(P("emulators", "melonds", "melonDS.toml"));
        Assert.Equal(P("saves", "nds").Replace('\\', '/'), t.Get("Instance0", "SaveFilePath"));
        Assert.Equal("false", t.Get(null, "PauseLostFocus"));
    }

    [Fact]
    public void Desmume_paths_and_ratio_only_when_embedded()
    {
        var (install, _) = Emu("desmume", "emulators/desmume/DeSmuME_0.9.13_x64.exe");
        var ini = _t.Touch("emulators/desmume/desmume.ini", "[3D]\r\nRenderer=2\r\n[Video]\r\nWindow Force Ratio=1\r\n[PathSettings]\r\nRoms=D:\\x\r\n");

        PortableSetups.Apply(install, Make.Plan("nds", LaunchMode.Fullscreen), _t.Paths);
        var d = IniFile.Load(ini);
        Assert.Equal("1", d.Get("Video", "Window Force Ratio"));
        RunTwiceIdempotent(install, Make.Plan("nds", LaunchMode.Embedded));
        d = IniFile.Load(ini);
        Assert.Equal(P("games", "nds"), d.Get("PathSettings", "Roms"));
        Assert.Equal(P("saves", "nds"), d.Get("PathSettings", "Battery"));
        Assert.Equal(P("savestates", "nds"), d.Get("PathSettings", "States"));
        Assert.Equal(P("screenshots", "nds"), d.Get("PathSettings", "Screenshots"));
        Assert.Equal("0", d.Get("Video", "Window Force Ratio"));
        Assert.Equal("2", d.Get("3D", "Renderer"));
        Assert.Contains("\r\n", File.ReadAllText(ini));
        Assert.DoesNotContain("\n[", File.ReadAllText(ini).Replace("\r\n[", ""));
    }

    [Fact]
    public void Desmume_writes_ansi_for_non_ascii_paths()
    {
        using var t = new TempRoot();
        var rootWithUmlaut = Path.Combine(t.Root, "Spielstände");
        var paths = new AppPaths(rootWithUmlaut, Path.Combine(rootWithUmlaut, "app"));
        var exe = Path.Combine(rootWithUmlaut, "emulators", "desmume", "DeSmuME.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(exe)!);
        File.WriteAllText(exe, "");
        var install = Make.Install(new EmulatorDef { Id = "desmume", Name = "DeSmuME", Portable = "desmume" }, exe);
        Assert.Equal(PortableApplyResult.Applied, PortableSetups.Apply(install, Make.Plan("nds", LaunchMode.Fullscreen), paths));
        var bytes = File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(exe)!, "desmume.ini"));
        Assert.Contains("Spielst\u00e4nde", Encoding.Latin1.GetString(bytes));
        Assert.DoesNotContain((byte)0xC3, bytes); // not UTF-8
    }

    [Fact]
    public void Azahar_portable_user_folder_and_qt_settings()
    {
        var (install, _) = Emu("azahar", "emulators/azahar/azahar-windows-msvc-2126.1.2/azahar.exe");
        var dir = P("emulators", "azahar", "azahar-windows-msvc-2126.1.2");
        RunTwiceIdempotent(install, Make.Plan("3ds", LaunchMode.Fullscreen));

        Assert.True(Directory.Exists(Path.Combine(dir, "user")));
        var ini = IniFile.Load(Path.Combine(dir, "user", "config", "qt-config.ini"));
        string Q(params string[] parts) => "\"" + P(parts).Replace('\\', '/') + "/\"";
        Assert.Equal("true", ini.Get("Data%20Storage", "use_custom_storage"));
        Assert.Equal("false", ini.Get("Data%20Storage", @"use_custom_storage\default"));
        Assert.Equal(Q("saves", "3ds", "nand"), ini.Get("Data%20Storage", "nand_directory"));
        Assert.Equal(Q("saves", "3ds", "sdmc"), ini.Get("Data%20Storage", "sdmc_directory"));
        Assert.Equal("false", ini.Get("Data%20Storage", @"sdmc_directory\default"));
        Assert.Equal("false", ini.Get("Miscellaneous", "check_for_update_on_start"));
        Assert.Equal("false", ini.Get("Miscellaneous", @"check_for_update_on_start\default"));
        Assert.Equal("false", ini.Get("UI", "confirmClose"));
        // Azahar keeps its "Paths" group inside [UI] (there is no [Paths] section in an Azahar-written file)
        Assert.Equal(Q("games", "3ds"), ini.Get("UI", @"Paths\romsPath"));
        Assert.Equal(Q("screenshots", "3ds"), ini.Get("UI", @"Paths\screenshotPath"));
        Assert.Equal("false", ini.Get("UI", @"Paths\screenshotPath\default"));
        Assert.False(ini.HasSection("Paths"));
        Assert.True(Directory.Exists(P("saves", "3ds", "nand")));
        Assert.True(Directory.Exists(P("saves", "3ds", "sdmc")));
    }

    [Fact]
    public void Azahar_keeps_unrelated_settings()
    {
        var (install, _) = Emu("azahar", "emulators/azahar/azahar.exe");
        var file = _t.Touch("emulators/azahar/user/config/qt-config.ini",
            "[Renderer]\nresolution_factor\\default=false\nresolution_factor=3\n\n[UI]\nconfirmClose\\default=true\nconfirmClose=true\ntheme=dark\n");
        PortableSetups.Apply(install, Make.Plan("3ds", LaunchMode.Embedded), _t.Paths);
        var ini = IniFile.Load(file);
        Assert.Equal("3", ini.Get("Renderer", "resolution_factor"));
        Assert.Equal("dark", ini.Get("UI", "theme"));
        Assert.Equal("false", ini.Get("UI", "confirmClose"));
        Assert.Equal("false", ini.Get("UI", @"confirmClose\default"));
    }

    [Fact]
    public void Dolphin_pcsx2_ppsspp_markers()
    {
        var (dolphin, _) = Emu("dolphin", "emulators/dolphin/Dolphin-x64/Dolphin.exe");
        RunTwiceIdempotent(dolphin, Make.Plan("gc", LaunchMode.Fullscreen));
        Assert.True(File.Exists(P("emulators", "dolphin", "Dolphin-x64", "portable.txt")));

        var (cemu, _) = Emu("cemu", "emulators/cemu/Cemu_2.6/Cemu.exe");
        RunTwiceIdempotent(cemu, Make.Plan("wiiu", LaunchMode.Fullscreen));
        Assert.True(Directory.Exists(P("emulators", "cemu", "Cemu_2.6", "portable")));

        var (pcsx2, _) = Emu("pcsx2", "emulators/pcsx2/pcsx2-qt.exe");
        RunTwiceIdempotent(pcsx2, Make.Plan("ps2", LaunchMode.Fullscreen));
        Assert.True(File.Exists(P("emulators", "pcsx2", "portable.ini")));

        var (pcsx2b, _) = Emu("pcsx2", "emulators/pcsx2b/pcsx2-qt.exe");
        _t.Touch("emulators/pcsx2b/portable.txt");
        PortableSetups.Apply(pcsx2b, Make.Plan("ps2", LaunchMode.Fullscreen), _t.Paths);
        Assert.False(File.Exists(P("emulators", "pcsx2b", "portable.ini")));

        var (ppsspp, _) = Emu("ppsspp", "emulators/ppsspp/PPSSPPWindows64.exe");
        var installed = _t.Touch("emulators/ppsspp/installed.txt");
        var before = _t.Files();
        Assert.Equal(PortableApplyResult.Applied, PortableSetups.Apply(ppsspp, Make.Plan("psp", LaunchMode.Fullscreen), _t.Paths));
        Assert.True(File.Exists(installed)); // never deleted
        Assert.Equal(before, _t.Files());
    }

    [Fact]
    public void Eden_creates_user_and_config_folders_without_keys()
    {
        var (eden, _) = Emu("eden", "emulators/eden/eden.exe");
        RunTwiceIdempotent(eden, Make.Plan("switch", LaunchMode.Fullscreen));
        Assert.True(Directory.Exists(P("emulators", "eden", "user")));
        // user\config keeps Eden's "migrate from yuzu" dialog away
        Assert.True(Directory.Exists(P("emulators", "eden", "user", "config")));
        Assert.False(Directory.Exists(P("emulators", "eden", "user", "keys")));
        Assert.Equal(["emulators/eden/eden.exe", "emulators/eden/user/config/qt-config.ini"], _t.Files());
        Assert.False(EdenSetup.HasProdKeys(_t.Paths.Bios, P("emulators", "eden")));

        // a new qt-config.ini holds only the [UI] keys RetroHub needs
        var ini = IniFile.Load(P("emulators", "eden", "user", "config", "qt-config.ini"));
        Assert.Equal("2", ini.Get("UI", "confirmStop"));
        Assert.Equal("false", ini.Get("UI", @"confirmStop\default"));
        Assert.Equal("false", ini.Get("UI", "check_for_updates"));
        Assert.Equal("false", ini.Get("UI", @"check_for_updates\default"));
        Assert.Equal(["UI"], ini.Sections());
    }

    [Fact]
    public void Eden_turns_off_stop_confirmation_and_update_check_keeping_other_settings()
    {
        var (eden, _) = Emu("eden", "emulators/eden/eden.exe");
        // excerpt of the qt-config.ini Eden v0.2.1 wrote on this machine (default confirmStop=0 = Ask_Always)
        const string head = "[Renderer]\nbackend\\default=true\nbackend=1\n\n[UI]\nshowConsole=false\n";
        const string tail = "pauseWhenInBackground\\default=true\npauseWhenInBackground=false\n";
        const string rest = "theme\\default=false\ntheme=colorful_dark\n\n[System]\nlanguage_index\\default=false\nlanguage_index=2\n";
        var file = _t.Touch("emulators/eden/user/config/qt-config.ini",
            head + "confirmStop\\default=true\nconfirmStop=0\n" + tail +
            "check_for_updates\\default=true\ncheck_for_updates=true\n" + rest);
        RunTwiceIdempotent(eden, Make.Plan("switch", LaunchMode.Fullscreen));

        Assert.Equal(
            head + "confirmStop\\default=false\nconfirmStop=2\n" + tail +
            "check_for_updates\\default=false\ncheck_for_updates=false\n" + rest,
            File.ReadAllText(file));
    }

    [Fact]
    public void Eden_copies_keys_from_bios_without_touching_the_source()
    {
        var (eden, _) = Emu("eden", "emulators/eden/eden.exe");
        var prod = _t.Touch("bios/switch/prod.keys", "header_key = 00\n");
        var title = _t.Touch("bios/title.keys", "0100 = 11\n"); // directly in bios/ works too
        File.SetLastWriteTimeUtc(prod, new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(title, new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var plan = Make.Plan("switch", LaunchMode.Fullscreen);

        RunTwiceIdempotent(eden, plan);
        var dstProd = P("emulators", "eden", "user", "keys", "prod.keys");
        var dstTitle = P("emulators", "eden", "user", "keys", "title.keys");
        Assert.Equal("header_key = 00\n", File.ReadAllText(dstProd));
        Assert.Equal("0100 = 11\n", File.ReadAllText(dstTitle));
        Assert.Equal("header_key = 00\n", File.ReadAllText(prod)); // source kept
        Assert.Equal("0100 = 11\n", File.ReadAllText(title));
        Assert.True(EdenSetup.HasProdKeys(_t.Paths.Bios, P("emulators", "eden")));

        // a newer, different copy in Eden's folder (installed through Eden's menu) is kept
        File.WriteAllText(dstProd, "header_key = 22\n");
        File.SetLastWriteTimeUtc(dstProd, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        PortableSetups.Apply(eden, plan, _t.Paths);
        Assert.Equal("header_key = 22\n", File.ReadAllText(dstProd));

        // a newer key file in bios/ replaces the older one in Eden's folder
        File.WriteAllText(prod, "header_key = 33\n");
        File.SetLastWriteTimeUtc(prod, new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));
        PortableSetups.Apply(eden, plan, _t.Paths);
        Assert.Equal("header_key = 33\n", File.ReadAllText(dstProd));
        // ... but the replaced copy is kept (the mtime rule can be wrong)
        var keysDir = P("emulators", "eden", "user", "keys");
        var backup = Assert.Single(Directory.GetFiles(keysDir, "prod.keys" + EdenSetup.BackupInfix + "*"));
        Assert.Equal("header_key = 22\n", File.ReadAllText(backup));

        // a second replacement never overwrites the first backup
        File.WriteAllText(prod, "header_key = 44\n");
        File.SetLastWriteTimeUtc(prod, new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc));
        PortableSetups.Apply(eden, plan, _t.Paths);
        Assert.Equal("header_key = 44\n", File.ReadAllText(dstProd));
        var backups = Directory.GetFiles(keysDir, "prod.keys" + EdenSetup.BackupInfix + "*");
        Assert.Equal(2, backups.Length);
        Assert.Equal("header_key = 22\n", File.ReadAllText(backup));
        Assert.Contains(backups, b => File.ReadAllText(b) == "header_key = 33\n");

        // an empty key file in bios/switch/ never overwrites anything
        File.WriteAllText(prod, "");
        File.SetLastWriteTimeUtc(prod, new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc));
        PortableSetups.Apply(eden, plan, _t.Paths);
        Assert.Equal("header_key = 44\n", File.ReadAllText(dstProd));
        Assert.Equal(2, Directory.GetFiles(keysDir, "prod.keys" + EdenSetup.BackupInfix + "*").Length);
        Assert.True(File.Exists(prod));
        Assert.DoesNotContain(_t.Files(), f => f.EndsWith(".retrohub-tmp", StringComparison.Ordinal));
    }

    [Fact]
    public void Eden_launch_plan_warns_about_missing_keys_without_blocking()
    {
        var def = new EmulatorDef
        {
            Id = "eden", Name = "Eden", Exe = ["eden.exe"], Portable = "eden",
            Args = ["{fullscreen}", "-g", "{rom}"], FullscreenArgs = ["-f"],
            DefaultMode = LaunchMode.Fullscreen, Modes = [LaunchMode.Fullscreen, LaunchMode.Windowed],
        };
        var exe = _t.Touch("emulators/eden/eden.exe");
        var rom = _t.Touch("games/switch/a.nsp");
        var catalog = new FakeCatalog().Add(def).Add(new SystemDef { Id = "switch", Name = "Nintendo Switch", Emulators = ["eden"] });
        var reg = new EmulatorRegistry(catalog, _t.Paths, _t.Settings);
        var planner = new LaunchPlanner(catalog, reg, _t.Paths, _t.Settings);

        var plan = planner.Plan(Make.Game("switch", "games/switch/a.nsp"));
        Assert.True(plan.CanLaunch, string.Join("; ", plan.Problems.Select(p => p.Message)));
        Assert.Equal(exe, plan.FileName);
        Assert.Equal(["-f", "-g", rom], plan.Arguments);
        var warning = Assert.Single(plan.Problems);
        Assert.False(warning.Blocking);
        Assert.Equal(LaunchProblemKind.BiosMissing, warning.Kind);
        Assert.Contains("bios/switch/", warning.Message);

        _t.Touch("bios/switch/prod.keys", "header_key = 00\n");
        Assert.Empty(planner.Plan(Make.Game("switch", "games/switch/a.nsp")).Problems);
    }

    [Fact]
    public void DuckStation_marker_and_settings_only_after_first_start()
    {
        var (install, _) = Emu("duckstation", "emulators/duckstation/duckstation-qt-x64-ReleaseLTCG.exe");
        PortableSetups.Apply(install, Make.Plan("psx", LaunchMode.Fullscreen), _t.Paths);
        Assert.True(File.Exists(P("emulators", "duckstation", "portable.txt")));
        Assert.False(File.Exists(P("emulators", "duckstation", "settings.ini")));

        var file = _t.Touch("emulators/duckstation/settings.ini",
            "[Main]\nSettingsVersion = 3\n\n[BIOS]\nSearchDirectory = bios\nPathNTSCU = scph1001.bin\n\n[MemoryCards]\nDirectory = memcards\n");
        RunTwiceIdempotent(install, Make.Plan("psx", LaunchMode.Fullscreen));
        var ini = IniFile.Load(file);
        Assert.Equal(P("bios"), ini.Get("BIOS", "SearchDirectory"));
        Assert.Equal("scph1001.bin", ini.Get("BIOS", "PathNTSCU"));
        Assert.Equal(P("saves", "psx"), ini.Get("MemoryCards", "Directory"));
        Assert.Equal(P("savestates", "psx"), ini.Get("Folders", "SaveStates"));
        Assert.Equal(P("screenshots", "psx"), ini.Get("Folders", "Screenshots"));
        Assert.Contains("SearchDirectory = " + P("bios"), File.ReadAllText(file)); // spaced style kept
    }

    [Fact]
    public void Running_emulator_is_skipped_and_nothing_is_written()
    {
        var (install, exe) = Emu("mgba", "emulators/mgba/mGBA.exe");
        var before = _t.Files();
        EmulatorProcesses.Override.Value = p => string.Equals(p, exe, StringComparison.OrdinalIgnoreCase);
        try
        {
            foreach (var setup in PortableSetups.All)
            {
                var i = install with { Def = install.Def with { Portable = setup.Id } };
                Assert.Equal(PortableApplyResult.SkippedRunning, PortableSetups.Apply(i, Make.Plan("gba", LaunchMode.Embedded), _t.Paths));
            }
        }
        finally { EmulatorProcesses.Override.Value = null; }
        Assert.Equal(before, _t.Files());
        Assert.False(Directory.Exists(P("saves")));
    }

    [Fact]
    public void Not_installed_unknown_recipe_and_io_errors_never_throw()
    {
        var def = new EmulatorDef { Id = "mgba", Name = "mGBA", Portable = "mgba" };
        Assert.Equal(PortableApplyResult.NotInstalled, PortableSetups.Apply(Make.Install(def, null), Make.Plan("gba", LaunchMode.Fullscreen), _t.Paths));
        Assert.Equal(PortableApplyResult.NotInstalled,
            PortableSetups.Apply(Make.Install(def, P("nope.exe")), Make.Plan("gba", LaunchMode.Fullscreen), _t.Paths));
        Assert.Equal(PortableApplyResult.NoRecipe,
            PortableSetups.Apply(Make.Install(def with { Portable = "zzz" }, null), Make.Plan("gba", LaunchMode.Fullscreen), _t.Paths));
        Assert.Equal(PortableApplyResult.NoRecipe,
            PortableSetups.Apply(Make.Install(def with { Portable = null }, null), Make.Plan("gba", LaunchMode.Fullscreen), _t.Paths));

        // config.ini locked exclusively → write fails → logged, Failed, no exception
        var (install, _) = Emu("mgba", "emulators/mgba/mGBA.exe");
        var cfg = _t.Touch("emulators/mgba/config.ini", "[ports.qt]\n");
        using (new FileStream(cfg, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Assert.Equal(PortableApplyResult.Failed, PortableSetups.Apply(install, Make.Plan("gba", LaunchMode.Fullscreen), _t.Paths));
        Assert.Equal("[ports.qt]\n", File.ReadAllText(cfg));
    }

    [Fact]
    public void Unsafe_system_id_falls_back_to_root_folders()
    {
        var (install, _) = Emu("mgba", "emulators/mgba/mGBA.exe");
        PortableSetups.Apply(install, Make.Plan("../../evil", LaunchMode.Fullscreen), _t.Paths);
        var ini = IniFile.Load(P("emulators", "mgba", "config.ini"));
        Assert.Equal(P("saves"), ini.Get("ports.qt", "savegamePath"));
        Assert.True(_t.NothingOutsideRoot());
    }

    [Fact]
    public void Moved_root_repairs_absolute_paths()
    {
        var (install, _) = Emu("mgba", "emulators/mgba/mGBA.exe");
        PortableSetups.Apply(install, Make.Plan("gba", LaunchMode.Fullscreen), _t.Paths);
        var moved = Path.Combine(_t.Base, "moved");
        Directory.Move(_t.Root, moved);
        try
        {
            var paths = new AppPaths(moved, Path.Combine(moved, "app"));
            var exe = Path.Combine(moved, "emulators", "mgba", "mGBA.exe");
            PortableSetups.Apply(install with { ExePath = exe }, Make.Plan("gba", LaunchMode.Fullscreen), paths);
            var ini = IniFile.Load(Path.Combine(moved, "emulators", "mgba", "config.ini"));
            Assert.Equal(Path.Combine(moved, "saves", "gba"), ini.Get("ports.qt", "savegamePath"));
        }
        finally { Directory.Move(moved, _t.Root); }
    }
}

public sealed class BiosCheckTests : IDisposable
{
    private readonly TempRoot _t = new();
    public void Dispose() => _t.Dispose();

    [Fact]
    public void Finds_files_case_insensitively_in_subfolders_and_rejects_traversal()
    {
        _t.Touch("bios/dc/DC_BOOT.BIN", "x");
        _t.Touch("secret.bin", "x");
        var system = new SystemDef
        {
            Id = "dc", Name = "Dreamcast",
            Bios =
            [
                new BiosFile { File = "dc/dc_boot.bin", Required = true },
                new BiosFile { File = "dc_boot.bin" },
                new BiosFile { File = "../secret.bin", Required = true },
                new BiosFile { File = "missing.bin" },
            ],
        };
        var s = BiosCheck.Check(system, _t.Paths.Bios);
        Assert.Equal(BiosState.Ok, s[0].State);
        Assert.Equal(Path.Combine(_t.Root, "bios", "dc", "DC_BOOT.BIN"), s[0].FoundPath);
        Assert.Equal(BiosState.Ok, s[1].State);
        Assert.Equal(BiosState.Missing, s[2].State);
        Assert.Equal(BiosState.Missing, s[3].State);
        var problems = BiosCheck.Problems(system, _t.Paths.Bios);
        var p = Assert.Single(problems); // only the required traversal one; optional missing is no problem
        Assert.True(p.Blocking);
    }

    [Fact]
    public void No_bios_folder_is_fine()
    {
        var system = new SystemDef { Id = "x", Name = "X", Bios = [new BiosFile { File = "a.bin" }] };
        Assert.Equal(BiosState.Missing, Assert.Single(BiosCheck.Check(system, Path.Combine(_t.Root, "nope"))).State);
        Assert.Empty(BiosCheck.Problems(system, Path.Combine(_t.Root, "nope")));
    }
}
