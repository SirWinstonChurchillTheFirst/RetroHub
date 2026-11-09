using System.Diagnostics;
using RetroHub.Core.Catalog;
using RetroHub.Core.Config;
using RetroHub.Core.Emulation;
using Xunit;

namespace RetroHub.Core.Tests.Emulation.Config;

public sealed class EmulatorRegistryTests : IDisposable
{
    private readonly TempRoot _t = new();
    private readonly FakeCatalog _catalog = new();

    public void Dispose() => _t.Dispose();

    private static EmulatorDef Def(string id, params string[] exe) => new() { Id = id, Name = id, Exe = exe };

    [Fact]
    public void Finds_exe_below_its_own_folder_with_version_from_folder_name()
    {
        _catalog.Add(Def("mgba", "mGBA.exe"));
        var exe = _t.Touch("emulators/mgba/mGBA-0.10.5-win64/mGBA.exe");
        _t.Touch("emulators/mgba/mGBA-0.10.5-win64/mgba-sdl.exe");
        var reg = new EmulatorRegistry(_catalog, _t.Paths, _t.Settings);
        var e = reg.Get("MGBA")!;
        Assert.Equal(exe, e.ExePath);
        Assert.True(e.IsInstalled);
        Assert.False(e.UserConfigured);
        Assert.Equal("0.10.5", e.Version);
    }

    [Fact]
    public void Wildcards_prefer_x64_then_shallow_then_short_and_skip_side_tools()
    {
        _catalog.Add(Def("azahar", "azahar*.exe"))
                .Add(Def("desmume", "DeSmuME*.exe"))
                .Add(Def("tool", "x*.exe"));
        var azahar = _t.Touch("emulators/azahar/azahar-windows-msvc-2126.1.2/azahar.exe");
        _t.Touch("emulators/azahar/azahar-windows-msvc-2126.1.2/azahar-room.exe");
        _t.Touch("emulators/desmume/DeSmuME_0.9.13_x86.exe");
        var desmume = _t.Touch("emulators/desmume/DeSmuME_0.9.13_x64.exe");
        _t.Touch("emulators/tool/xunins000.exe");
        var reg = new EmulatorRegistry(_catalog, _t.Paths, _t.Settings);
        Assert.Equal(azahar, reg.Get("azahar")!.ExePath);
        Assert.Equal("2126.1.2", reg.Get("azahar")!.Version);
        Assert.Equal(desmume, reg.Get("desmume")!.ExePath);
        Assert.Equal("0.9.13", reg.Get("desmume")!.Version);
        Assert.Null(reg.Get("tool")!.ExePath); // uninstaller is never chosen by a wildcard
    }

    [Fact]
    public void First_pattern_with_a_match_wins()
    {
        _catalog.Add(Def("pcsx2", "pcsx2-qt.exe", "pcsx2*.exe"));
        _t.Touch("emulators/pcsx2/pcsx2-qtx64-avx2.exe");
        var qt = _t.Touch("emulators/pcsx2/sub/pcsx2-qt.exe");
        var reg = new EmulatorRegistry(_catalog, _t.Paths, _t.Settings);
        Assert.Equal(qt, reg.Get("pcsx2")!.ExePath);
    }

    [Fact]
    public void Depth_is_limited_to_three_levels()
    {
        _catalog.Add(Def("deep", "deep.exe")).Add(Def("ok", "ok.exe"));
        _t.Touch("emulators/deep/a/b/c/d/deep.exe");
        var ok = _t.Touch("emulators/ok/a/b/c/ok.exe");
        var reg = new EmulatorRegistry(_catalog, _t.Paths, _t.Settings);
        Assert.Null(reg.Get("deep")!.ExePath);
        Assert.Equal(ok, reg.Get("ok")!.ExePath);
    }

    [Fact]
    public void Falls_back_to_exact_names_anywhere_below_emulators()
    {
        _catalog.Add(Def("ppsspp", "PPSSPPWindows64.exe")).Add(Def("wild", "wild*.exe"));
        var exe = _t.Touch("emulators/PPSSPP v1.18/PPSSPPWindows64.exe");
        _t.Touch("emulators/elsewhere/wildthing.exe");
        var reg = new EmulatorRegistry(_catalog, _t.Paths, _t.Settings);
        Assert.Equal(exe, reg.Get("ppsspp")!.ExePath);
        Assert.Equal("1.18", reg.Get("ppsspp")!.Version);
        Assert.Null(reg.Get("wild")!.ExePath); // wildcards only inside emulators/<id>/
    }

    [Fact]
    public void User_path_wins_when_it_exists_and_is_stored_portably()
    {
        _catalog.Add(Def("mgba", "mGBA.exe"));
        _t.Touch("emulators/mgba/mGBA.exe");
        var custom = _t.Touch("tools/my mgba/mGBA.exe");
        var reg = new EmulatorRegistry(_catalog, _t.Paths, _t.Settings);
        int changed = 0;
        reg.Changed += () => changed++;
        reg.SetUserPath("mgba", custom);
        Assert.Equal(custom, reg.Get("mgba")!.ExePath);
        Assert.True(reg.Get("mgba")!.UserConfigured);
        Assert.Equal("tools/my mgba/mGBA.exe", _t.Settings.Current.EmulatorPaths["mgba"]);
        Assert.Equal(1, changed);
        // persisted
        Assert.Equal("tools/my mgba/mGBA.exe", new SettingsStore(_t.Paths.SettingsFile).Load().EmulatorPaths["mgba"]);

        // a configured path that no longer exists falls back to detection
        File.Delete(custom);
        reg.Refresh();
        Assert.Equal(Path.Combine(_t.Root, "emulators", "mgba", "mGBA.exe"), reg.Get("mgba")!.ExePath);
        Assert.False(reg.Get("mgba")!.UserConfigured);

        reg.SetUserPath("mgba", null);
        Assert.False(_t.Settings.Current.EmulatorPaths.ContainsKey("mgba"));
    }

    [Fact]
    public void Retroarch_lists_installed_cores()
    {
        _catalog.Add(new EmulatorDef { Id = "retroarch", Name = "RetroArch", Kind = EmulatorKind.RetroArch, Exe = ["retroarch.exe"] });
        _t.Touch("emulators/retroarch/RetroArch-Win64/retroarch.exe");
        _t.Touch("emulators/retroarch/RetroArch-Win64/cores/snes9x_libretro.dll");
        _t.Touch("emulators/retroarch/RetroArch-Win64/cores/mgba_libretro.dll");
        _t.Touch("emulators/retroarch/RetroArch-Win64/cores/readme.txt");
        var reg = new EmulatorRegistry(_catalog, _t.Paths, _t.Settings);
        Assert.Equal(["mgba", "snes9x"], reg.Get("retroarch")!.InstalledCores);
    }

    [Fact]
    public void Missing_folders_unsafe_ids_and_native_defs_do_not_crash()
    {
        _catalog.Add(Def("..", "x.exe")).Add(Def("a/b", "x.exe")).Add(Def("none", "none.exe"))
                .Add(new EmulatorDef { Id = "native", Name = "PC", Kind = EmulatorKind.Native })
                .Add(Def("trav", "../../evil.exe"));
        _t.Touch("evil.exe");
        var reg = new EmulatorRegistry(_catalog, _t.Paths, _t.Settings);
        Assert.Equal(5, reg.All.Count);
        Assert.All(reg.All, e => Assert.Null(e.ExePath));
        Assert.Null(reg.Get("unknown"));
    }

    [Theory]
    [InlineData("1.19.1+abc123", "1.19.1")]
    [InlineData(" 0.10.5 ", "0.10.5")]
    [InlineData("1, 2, 3, 4", "1.2.3.4")]
    [InlineData("0.10.5.0", "0.10.5")]
    [InlineData("mgba", null)]
    [InlineData("v2.1 (git)", "2.1")]
    [InlineData("1.1", "1.1")]
    [InlineData("0.0.0.0", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Cleans_version_strings(string? raw, string? expected) => Assert.Equal(expected, EmulatorRegistry.CleanVersion(raw));

    [Theory]
    [InlineData("mGBA-0.10.5-win64", "0.10.5")]
    [InlineData("azahar-windows-msvc-2126.1.2", "2126.1.2")]
    [InlineData("DeSmuME_0.9.13_x64", "0.9.13")]
    [InlineData("RetroArch-Win64", null)]
    [InlineData("melonds", null)]
    [InlineData("dolphin-v2503a", null)]
    [InlineData("pcsx2-v2.2.0-windows-x64-Qt", "2.2.0")]
    public void Version_from_folder_names(string name, string? expected) => Assert.Equal(expected, EmulatorRegistry.VersionFromName(name));

    /// <summary>
    /// The user's real layout under D:\RetroHub\emulators (READ-ONLY: nothing is written there; the settings file
    /// lives in a temp folder). Skipped on machines without it.
    /// </summary>
    [Fact]
    public void Detects_the_real_emulator_layout_read_only()
    {
        const string realRoot = @"D:\RetroHub";
        Assert.SkipUnless(Directory.Exists(Path.Combine(realRoot, "emulators")), @"D:\RetroHub\emulators not present on this machine");

        var catalog = new FakeCatalog()
            .Add(new EmulatorDef { Id = "retroarch", Name = "RetroArch", Kind = EmulatorKind.RetroArch, Exe = ["retroarch.exe"] })
            .Add(new EmulatorDef { Id = "mgba", Name = "mGBA", Exe = ["mGBA.exe"] })
            .Add(new EmulatorDef { Id = "melonds", Name = "melonDS", Exe = ["melonDS.exe"] })
            .Add(new EmulatorDef { Id = "desmume", Name = "DeSmuME", Exe = ["DeSmuME.exe", "DeSmuME*.exe"] })
            .Add(new EmulatorDef { Id = "azahar", Name = "Azahar", Exe = ["azahar.exe"] });
        var paths = new AppPaths(realRoot, Path.Combine(_t.Root, "app"));
        var settings = new SettingsStore(Path.Combine(_t.Root, "settings.json"));
        var before = Snapshot(Path.Combine(realRoot, "emulators"));

        var reg = new EmulatorRegistry(catalog, paths, settings);

        var output = TestContext.Current.TestOutputHelper;
        foreach (var e in reg.All)
            output?.WriteLine($"{e.Def.Id,-10} {e.Version ?? "(no version)",-14} {e.ExePath ?? "(not found)"}" +
                              (e.InstalledCores.Count > 0 ? $"  cores: {string.Join(", ", e.InstalledCores)}" : ""));

        var em = Path.Combine(realRoot, "emulators");
        Assert.Equal(Path.Combine(em, "retroarch", "RetroArch-Win64", "retroarch.exe"), reg.Get("retroarch")!.ExePath, ignoreCase: true);
        Assert.Equal(Path.Combine(em, "mgba", "mGBA-0.10.5-win64", "mGBA.exe"), reg.Get("mgba")!.ExePath, ignoreCase: true);
        Assert.Equal(Path.Combine(em, "melonds", "melonDS.exe"), reg.Get("melonds")!.ExePath, ignoreCase: true);
        Assert.Equal(Path.Combine(em, "desmume", "DeSmuME_0.9.13_x64.exe"), reg.Get("desmume")!.ExePath, ignoreCase: true);
        Assert.Equal(Path.Combine(em, "azahar", "azahar-windows-msvc-2126.1.2", "azahar.exe"), reg.Get("azahar")!.ExePath, ignoreCase: true);
        foreach (var core in new[] { "mgba", "snes9x", "fceumm", "gambatte", "genesis_plus_gx", "pcsx_rearmed", "mupen64plus_next" })
            Assert.Contains(core, reg.Get("retroarch")!.InstalledCores);
        // versions as found on 2026-11-09: mGBA FileVersion 0.10.5.0 (its ProductVersion is just "mgba"),
        // melonDS ProductVersion 1.1; DeSmuME/Azahar have no version resource → folder/exe name;
        // RetroArch has neither (folder "RetroArch-Win64") → null is correct
        Assert.Equal("0.10.5", reg.Get("mgba")!.Version);
        Assert.Equal("1.1", reg.Get("melonds")!.Version);
        Assert.Equal("0.9.13", reg.Get("desmume")!.Version);
        Assert.Equal("2126.1.2", reg.Get("azahar")!.Version);

        // read-only: nothing below D:\RetroHub\emulators changed
        Assert.Equal(before, Snapshot(em));
        Assert.False(File.Exists(Path.Combine(_t.Root, "settings.json")));
    }

    private static List<string> Snapshot(string dir) => Directory
        .EnumerateFiles(dir, "*", new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 4, IgnoreInaccessible = true })
        .Select(f => $"{f}|{new FileInfo(f).Length}|{File.GetLastWriteTimeUtc(f).Ticks}")
        .Order(StringComparer.OrdinalIgnoreCase).ToList();
}

public sealed class EmulatorProcessesTests
{
    [Fact]
    public void The_test_host_itself_is_running_and_a_fake_path_is_not()
    {
        var self = Process.GetCurrentProcess().MainModule!.FileName;
        Assert.True(EmulatorProcesses.IsRunning(self));
        Assert.True(EmulatorProcesses.IsRunning(self.ToUpperInvariant())); // case-insensitive
        var fake = Path.Combine(Path.GetDirectoryName(self)!, "definitely-not-running-" + Guid.NewGuid().ToString("N") + ".exe");
        Assert.False(EmulatorProcesses.IsRunning(fake));
        Assert.False(EmulatorProcesses.IsRunning(null));
        Assert.False(EmulatorProcesses.IsRunning(""));
        Assert.False(EmulatorProcesses.IsRunning("\0bad"));
    }

    [Fact]
    public void Same_name_in_another_folder_does_not_count()
    {
        var self = Process.GetCurrentProcess().MainModule!.FileName;
        var other = Path.Combine(Path.GetTempPath(), "elsewhere", Path.GetFileName(self));
        Assert.False(EmulatorProcesses.IsRunning(other));
    }

    [Fact]
    public void Exact_copy_is_running_until_it_exits_even_with_an_open_handle()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only");
        var dir = Directory.CreateTempSubdirectory("retrohub-proc-").FullName;
        try
        {
            var exe = Path.Combine(dir, "fake-emulator.exe");
            File.Copy(Path.Combine(Environment.SystemDirectory, "cmd.exe"), exe);
            using var p = Process.Start(new ProcessStartInfo(exe, "/c ping -n 30 127.0.0.1 >nul") { UseShellExecute = false, CreateNoWindow = true })!;
            try
            {
                Thread.Sleep(200);
                EmulatorProcesses.Invalidate();
                Assert.True(EmulatorProcesses.IsRunning(exe));
                Assert.False(EmulatorProcesses.IsRunning(Path.Combine(dir, "other.exe")));
            }
            finally
            {
                p.Kill(entireProcessTree: true);
                p.WaitForExit(5000);
            }
            // `p` still holds its process handle here: an ended process must not count as running
            EmulatorProcesses.Invalidate();
            Assert.False(EmulatorProcesses.IsRunning(exe));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Process_list_is_cached_for_a_second()
    {
        EmulatorProcesses.Invalidate();
        var self = Process.GetCurrentProcess().MainModule!.FileName;
        var sw = Stopwatch.StartNew();
        EmulatorProcesses.IsRunning(self);
        var first = sw.Elapsed;
        sw.Restart();
        for (int i = 0; i < 100; i++) EmulatorProcesses.IsRunning(self);
        var hundred = sw.Elapsed;
        TestContext.Current.TestOutputHelper?.WriteLine($"first {first.TotalMilliseconds:F1} ms, 100 cached {hundred.TotalMilliseconds:F1} ms");
        Assert.True(hundred < TimeSpan.FromMilliseconds(200));
    }
}
