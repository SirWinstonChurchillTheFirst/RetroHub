using System.Security.Cryptography;
using RetroHub.Core.Catalog;
using RetroHub.Core.Emulation;
using RetroHub.Core.Library;
using Xunit;

namespace RetroHub.Core.Tests.Emulation.Config;

public sealed class LaunchPlannerTests : IDisposable
{
    private readonly TempRoot _t = new();
    private readonly FakeCatalog _catalog = new();
    private readonly FakeRegistry _registry = new();

    private static readonly EmulatorDef Mgba = new()
    {
        Id = "mgba", Name = "mGBA", Exe = ["mGBA.exe"], Systems = ["gba", "gb"],
        Args = ["{fullscreen}", "{rom}"], FullscreenArgs = ["-f"],
        DefaultMode = LaunchMode.Embedded, Modes = [LaunchMode.Embedded, LaunchMode.Fullscreen, LaunchMode.Windowed],
        Embed = new EmbedProfile { KeepFrame = true }, Portable = "mgba",
        DownloadUrl = "https://mgba.io/downloads.html",
    };

    private static readonly EmulatorDef RetroArch = new()
    {
        Id = "retroarch", Name = "RetroArch", Kind = EmulatorKind.RetroArch, Exe = ["retroarch.exe"],
        Systems = ["gba", "snes", "psx"],
        Args = ["-L", "{core}", "{fullscreen}", "--appendconfig", "{retroarchOverride}", "{rom}"], FullscreenArgs = ["-f"],
        Cores = new() { ["gba"] = ["mgba", "vba_next"], ["snes"] = ["snes9x"], ["psx"] = ["pcsx_rearmed"] },
        Portable = "retroarch",
    };

    private static readonly EmulatorDef Dolphin = new()
    {
        Id = "dolphin", Name = "Dolphin", Exe = ["Dolphin.exe"], Systems = ["gc"],
        Args = ["-b", "-e", "{rom}"], Modes = [LaunchMode.Fullscreen, LaunchMode.Windowed], DefaultMode = LaunchMode.Fullscreen,
    };

    public LaunchPlannerTests()
    {
        _catalog
            .Add(new SystemDef { Id = "gba", Name = "Game Boy Advance", Emulators = ["mgba", "retroarch/mgba"], AspectRatio = 1.5 })
            .Add(new SystemDef { Id = "snes", Name = "Super Nintendo", Emulators = ["retroarch"] })
            .Add(new SystemDef { Id = "gc", Name = "GameCube", Emulators = ["dolphin"] })
            .Add(new SystemDef { Id = "empty", Name = "Leer" })
            .Add(new SystemDef { Id = "pc", Name = "PC", Category = SystemCategory.Pc })
            .Add(Mgba).Add(RetroArch).Add(Dolphin);
    }

    public void Dispose() => _t.Dispose();

    private LaunchPlanner Planner() => new(_catalog, _registry, _t.Paths, _t.Settings);

    private string InstallMgba() => Install(Mgba, "emulators/mgba/mGBA.exe");

    private string Install(EmulatorDef def, string rel)
    {
        var exe = _t.Touch(rel);
        _registry.Set(Make.Install(def, exe));
        return exe;
    }

    private string InstallRetroArch(params string[] cores)
    {
        var exe = _t.Touch("emulators/retroarch/retroarch.exe");
        foreach (var c in cores) _t.Touch($"emulators/retroarch/cores/{c}_libretro.dll");
        _registry.Set(Make.Install(RetroArch, exe, cores));
        return exe;
    }

    private Game Rom(string system, string name)
    {
        _t.Touch($"games/{system}/{name}");
        return Make.Game(system, $"games/{system}/{name}");
    }

    [Fact]
    public void First_installed_choice_wins()
    {
        var exe = InstallMgba();
        InstallRetroArch("mgba");
        var plan = Planner().Plan(Rom("gba", "a.gba"));
        Assert.True(plan.CanLaunch, string.Join("; ", plan.Problems.Select(p => p.Message)));
        Assert.Equal("mgba", plan.Emulator!.Def.Id);
        Assert.Equal(exe, plan.FileName);
        Assert.Equal(Path.GetDirectoryName(exe), plan.WorkingDirectory);
        Assert.Equal(LaunchMode.Embedded, plan.Mode);
        Assert.NotNull(plan.Embed);
        Assert.True(plan.Embed!.KeepFrame);
        Assert.Equal(1.5, plan.AspectRatio);
        Assert.Equal([Path.Combine(_t.Root, "games", "gba", "a.gba")], plan.Arguments);
        Assert.Null(plan.Core);
    }

    [Fact]
    public void Skips_uninstalled_emulators_and_uses_retroarch_with_its_core()
    {
        var exe = InstallRetroArch("mgba");
        var plan = Planner().Plan(Rom("gba", "a.gba"));
        Assert.True(plan.CanLaunch, string.Join("; ", plan.Problems.Select(p => p.Message)));
        Assert.Equal("retroarch", plan.Emulator!.Def.Id);
        Assert.Equal("mgba", plan.Core);
        Assert.Equal(LaunchMode.Fullscreen, plan.Mode);
        Assert.Null(plan.Embed);
        var core = Path.Combine(Path.GetDirectoryName(exe)!, "cores", "mgba_libretro.dll");
        Assert.Equal(["-L", core, "-f", "--appendconfig", Path.Combine(_t.Paths.Config, "retroarch", "retrohub.cfg"),
            Path.Combine(_t.Root, "games", "gba", "a.gba")], plan.Arguments);
    }

    [Fact]
    public void Retroarch_entry_counts_as_installed_only_with_its_core()
    {
        // system order: retroarch first, but its core is missing → mGBA (second) is chosen
        _catalog.SystemList.RemoveAll(s => s.Id == "gba");
        _catalog.Add(new SystemDef { Id = "gba", Name = "GBA", Emulators = ["retroarch/mgba", "mgba"] });
        InstallRetroArch("snes9x");
        InstallMgba();
        var plan = Planner().Plan(Rom("gba", "a.gba"));
        Assert.Equal("mgba", plan.Emulator!.Def.Id);
        Assert.True(plan.CanLaunch);
    }

    [Fact]
    public void Nothing_installed_gives_blocking_emulator_missing_with_folder_hint()
    {
        var plan = Planner().Plan(Rom("gba", "a.gba"));
        Assert.False(plan.CanLaunch);
        Assert.Null(plan.FileName);
        Assert.Equal("mgba", plan.Emulator!.Def.Id);
        var p = Assert.Single(plan.Problems);
        Assert.Equal(LaunchProblemKind.EmulatorMissing, p.Kind);
        Assert.True(p.Blocking);
        Assert.Contains("mGBA", p.Message);
        Assert.Contains("emulators/mgba/", p.Message);
        Assert.Contains("mGBA.exe", p.Message);
        Assert.Contains("https://mgba.io/downloads.html", p.Message);
    }

    [Fact]
    public void Missing_core_is_blocking_and_names_dll_and_cores_folder()
    {
        var exe = InstallRetroArch();
        var plan = Planner().Plan(Rom("snes", "a.sfc"));
        Assert.False(plan.CanLaunch);
        var p = Assert.Single(plan.Problems);
        Assert.Equal(LaunchProblemKind.CoreMissing, p.Kind);
        Assert.True(p.Blocking);
        Assert.Contains("snes9x_libretro.dll", p.Message);
        Assert.Contains(Path.Combine(Path.GetDirectoryName(exe)!, "cores"), p.Message);
        Assert.Equal("snes9x", plan.Core);
    }

    [Fact]
    public void Retroarch_without_core_in_ref_picks_the_first_installed_mapped_core()
    {
        _catalog.SystemList.RemoveAll(s => s.Id == "gba");
        _catalog.Add(new SystemDef { Id = "gba", Name = "GBA", Emulators = ["retroarch"] });
        InstallRetroArch("vba_next");
        var plan = Planner().Plan(Rom("gba", "a.gba"));
        Assert.True(plan.CanLaunch);
        Assert.Equal("vba_next", plan.Core);
    }

    [Fact]
    public void Override_precedence_argument_game_settings()
    {
        InstallMgba();
        InstallRetroArch("mgba", "vba_next");
        var game = Rom("gba", "a.gba");
        _t.Settings.Current.SystemEmulator["gba"] = "retroarch/vba_next";
        Assert.Equal(("retroarch", "vba_next"), Pick(Planner().Plan(game)));

        game.EmulatorOverride = "retroarch/mgba";
        Assert.Equal(("retroarch", "mgba"), Pick(Planner().Plan(game)));

        Assert.Equal(("mgba", null), Pick(Planner().Plan(game, emulatorOverride: "mgba")));

        // unknown ids are skipped, the next level applies
        Assert.Equal(("retroarch", "mgba"), Pick(Planner().Plan(game, emulatorOverride: "doesnotexist")));
        game.EmulatorOverride = "gone/plugin";
        Assert.Equal(("retroarch", "vba_next"), Pick(Planner().Plan(game)));

        static (string, string?) Pick(LaunchPlan p) => (p.Emulator!.Def.Id, p.Core);
    }

    [Fact]
    public void Explicit_override_to_uninstalled_emulator_is_honoured_and_blocking()
    {
        InstallRetroArch("mgba");
        var plan = Planner().Plan(Rom("gba", "a.gba"), emulatorOverride: "mgba");
        Assert.Equal("mgba", plan.Emulator!.Def.Id);
        Assert.Contains(plan.Problems, p => p.Kind == LaunchProblemKind.EmulatorMissing && p.Blocking);
    }

    [Fact]
    public void Mode_precedence_and_fallback()
    {
        InstallMgba();
        Install(Dolphin, "emulators/dolphin/Dolphin.exe");
        var gba = Rom("gba", "a.gba");
        var planner = Planner();
        Assert.Equal(LaunchMode.Embedded, planner.Plan(gba).Mode);                       // def default

        _t.Settings.Current.EmulatorMode["mgba"] = LaunchMode.Windowed;
        Assert.Equal(LaunchMode.Windowed, planner.Plan(gba).Mode);                       // settings

        gba.LaunchModeOverride = LaunchMode.Fullscreen;
        var full = planner.Plan(gba);
        Assert.Equal(LaunchMode.Fullscreen, full.Mode);                                  // game override
        Assert.Equal(["-f", Path.Combine(_t.Root, "games", "gba", "a.gba")], full.Arguments);
        Assert.Null(full.Embed);

        Assert.Equal(LaunchMode.Embedded, planner.Plan(gba, modeOverride: LaunchMode.Embedded).Mode); // argument

        // Dolphin does not support Embedded → first of its modes
        var gc = Rom("gc", "a.rvz");
        var plan = planner.Plan(gc, modeOverride: LaunchMode.Embedded);
        Assert.Equal(LaunchMode.Fullscreen, plan.Mode);
        Assert.Null(plan.Embed);
    }

    [Fact]
    public void Extra_args_from_settings_are_appended_and_expanded()
    {
        InstallMgba();
        _t.Settings.Current.EmulatorExtraArgs["mgba"] = ["--log-level", "{system}"];
        var plan = Planner().Plan(Rom("gba", "a.gba"), modeOverride: LaunchMode.Windowed);
        Assert.Equal([Path.Combine(_t.Root, "games", "gba", "a.gba"), "--log-level", "gba"], plan.Arguments);
    }

    [Fact]
    public void Missing_game_file_is_blocking()
    {
        InstallMgba();
        var plan = Planner().Plan(Make.Game("gba", "games/gba/nope.gba"));
        Assert.False(plan.CanLaunch);
        Assert.Contains(plan.Problems, p => p.Kind == LaunchProblemKind.FileMissing && p.Blocking && p.Message.Contains("nope.gba"));
    }

    [Fact]
    public void System_without_emulators_or_unknown_system()
    {
        var plan = Planner().Plan(Rom("empty", "x.bin"));
        Assert.Contains(plan.Problems, p => p.Kind == LaunchProblemKind.NoEmulatorForSystem && p.Blocking);
        Assert.False(plan.CanLaunch);

        var unknown = Planner().Plan(Make.Game("nosuchsystem", "games/x/y.bin"));
        Assert.Equal("nosuchsystem", unknown.System.Id);
        Assert.Contains(unknown.Problems, p => p.Kind == LaunchProblemKind.NoEmulatorForSystem && p.Blocking);
    }

    [Fact]
    public void Emulators_listing_the_system_are_used_when_the_system_lists_none()
    {
        _catalog.Add(new SystemDef { Id = "gb", Name = "Game Boy" }); // mGBA lists "gb" in Systems
        InstallMgba();
        var plan = Planner().Plan(Rom("gb", "a.gb"));
        Assert.Equal("mgba", plan.Emulator!.Def.Id);
        Assert.True(plan.CanLaunch);
    }

    [Fact]
    public void Choices_in_preference_order_with_install_state()
    {
        InstallRetroArch("mgba");
        var choices = Planner().ChoicesFor("gba");
        Assert.Equal(["mgba", "retroarch/mgba", "retroarch/vba_next"], choices.Select(c => c.Ref.ToString()));
        Assert.False(choices[0].Install!.IsInstalled);
        Assert.True(choices[1].Install!.IsInstalled);
        Assert.Empty(Planner().ChoicesFor("nosuchsystem"));
    }

    [Fact]
    public void Bios_required_missing_is_blocking_mismatch_is_a_warning()
    {
        var good = new byte[] { 1, 2, 3, 4 };
        var goodMd5 = Convert.ToHexStringLower(MD5.HashData(good));
        _catalog.Add(new SystemDef
        {
            Id = "psx", Name = "PlayStation", Emulators = ["retroarch/pcsx_rearmed"],
            Bios =
            [
                new BiosFile { File = "scph5501.bin", Md5 = [goodMd5], Required = true, Description = "PS1 BIOS (USA)" },
                new BiosFile { File = "optional.bin" },
            ],
        });
        InstallRetroArch("pcsx_rearmed");
        var game = Rom("psx", "a.cue");

        var missing = Planner().Plan(game);
        var p = Assert.Single(missing.Problems);
        Assert.Equal(LaunchProblemKind.BiosMissing, p.Kind);
        Assert.True(p.Blocking);
        Assert.Contains("scph5501.bin", p.Message);
        Assert.Contains("bios/", p.Message);

        _t.Touch("bios/SCPH5501.BIN", [9, 9, 9]); // wrong dump, case-insensitive name
        var mismatch = Planner().Plan(game);
        Assert.True(mismatch.CanLaunch);
        var w = Assert.Single(mismatch.Problems);
        Assert.False(w.Blocking);
        Assert.Contains("MD5", w.Message);

        File.WriteAllBytes(Path.Combine(_t.Root, "bios", "SCPH5501.BIN"), good);
        File.SetLastWriteTimeUtc(Path.Combine(_t.Root, "bios", "SCPH5501.BIN"), DateTime.UtcNow.AddMinutes(1));
        Assert.Empty(Planner().Plan(game).Problems);
    }

    [Fact]
    public void Bios_in_subfolder_counts_but_retroarch_gets_a_hint()
    {
        _catalog.Add(new SystemDef
        {
            Id = "psx", Name = "PlayStation", Emulators = ["retroarch/pcsx_rearmed"],
            Bios = [new BiosFile { File = "scph5501.bin", Required = true }],
        });
        InstallRetroArch("pcsx_rearmed");
        _t.Touch("bios/psx/scph5501.bin", "x");
        var plan = Planner().Plan(Rom("psx", "a.cue"));
        Assert.True(plan.CanLaunch);
        var hint = Assert.Single(plan.Problems);
        Assert.False(hint.Blocking);
        Assert.Contains("Unterordner", hint.Message);
    }

    [Fact]
    public void Native_exe_uses_its_folder_and_splits_extra_args()
    {
        var exe = _t.Touch("games/pc/My Game/game.exe");
        var game = Make.Game("pc", "games/pc/My Game/game.exe", GameKind.Executable);
        game.ExtraArgs = "-windowed \"--profile=C:\\My Saves\" -x";
        var plan = Planner().Plan(game);
        Assert.True(plan.CanLaunch, string.Join("; ", plan.Problems.Select(p => p.Message)));
        Assert.Equal(exe, plan.FileName);
        Assert.Equal(Path.GetDirectoryName(exe), plan.WorkingDirectory);
        Assert.Equal(["-windowed", @"--profile=C:\My Saves", "-x"], plan.Arguments);
        Assert.False(plan.UseShellExecute);
        Assert.Equal(LaunchMode.Fullscreen, plan.Mode);
        Assert.Equal(LaunchMode.Windowed, Planner().Plan(game, modeOverride: LaunchMode.Windowed).Mode);
        Assert.NotEqual(LaunchMode.Embedded, Planner().Plan(game, modeOverride: LaunchMode.Embedded).Mode);
    }

    [Fact]
    public void Shortcuts_and_urls_use_shell_execute()
    {
        var lnk = _t.Touch("games/pc/Game.lnk");
        var plan = Planner().Plan(Make.Game("pc", "games/pc/Game.lnk", GameKind.Shortcut));
        Assert.True(plan.UseShellExecute);
        Assert.Equal(lnk, plan.FileName);
        Assert.True(plan.CanLaunch);

        var steam = Planner().Plan(Make.Game("pc", "steam://rungameid/70", GameKind.Shortcut));
        Assert.True(steam.UseShellExecute);
        Assert.Equal("steam://rungameid/70", steam.FileName);
        Assert.Null(steam.WorkingDirectory);
        Assert.Empty(steam.Arguments);
        Assert.True(steam.CanLaunch, string.Join("; ", steam.Problems.Select(p => p.Message)));

        // a URL is recognised by its scheme even when the scanner stored Kind=Executable
        Assert.True(Planner().Plan(Make.Game("pc", "com.epicgames.launcher://apps/x?action=launch", GameKind.Executable)).UseShellExecute);
    }

    [Fact]
    public void Missing_exe_for_native_game_is_blocking()
    {
        var plan = Planner().Plan(Make.Game("pc", "games/pc/gone/game.exe", GameKind.Executable));
        Assert.Contains(plan.Problems, p => p.Kind == LaunchProblemKind.FileMissing && p.Blocking);
        _t.Touch("games/pc/Folder Game/readme.txt");
        var folder = Planner().Plan(Make.Game("pc", "games/pc/Folder Game", GameKind.Executable));
        Assert.Contains(folder.Problems, p => p.Kind == LaunchProblemKind.FileMissing && p.Message.Contains(".exe"));
    }

    [Fact]
    public void Planner_never_throws_for_odd_input()
    {
        var plan = Planner().Plan(Make.Game("gba", "games/gba/\0bad.gba"));
        Assert.False(plan.CanLaunch);
        Assert.NotEmpty(plan.Problems);
    }
}
