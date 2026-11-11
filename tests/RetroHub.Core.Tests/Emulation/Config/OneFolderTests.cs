using System.Security.Cryptography;
using RetroHub.Core.Catalog;
using RetroHub.Core.Emulation;
using RetroHub.Core.Emulation.ConfigFiles;
using RetroHub.Core.Emulation.Portable;
using Xunit;

namespace RetroHub.Core.Tests.Emulation.Config;

/// <summary>
/// "Alles in einem Ordner": every bundled emulator keeps its data below the RetroHub root, self-updaters are off, and
/// the player's own installs outside the root are never written to.
/// </summary>
public sealed class OneFolderTests : IDisposable
{
    private readonly TempRoot _t = new();
    public void Dispose() => _t.Dispose();

    private (EmulatorInstall Install, string Dir) Emu(string id, string relExe)
    {
        var exe = _t.Touch(relExe);
        return (Make.Install(new EmulatorDef { Id = id, Name = id, Exe = [Path.GetFileName(relExe)], Portable = id }, exe), Path.GetDirectoryName(exe)!);
    }

    [Theory]
    [InlineData("mgba", "emulators/mgba/mGBA.exe", "gba")]
    [InlineData("melonds", "emulators/melonds/melonDS.exe", "nds")]
    [InlineData("desmume", "emulators/desmume/DeSmuME_0.9.13_x64.exe", "nds")]
    [InlineData("azahar", "emulators/azahar/azahar.exe", "3ds")]
    [InlineData("dolphin", "emulators/dolphin/Dolphin.exe", "gamecube")]
    [InlineData("cemu", "emulators/cemu/Cemu.exe", "wiiu")]
    [InlineData("eden", "emulators/eden/eden.exe", "switch")]
    [InlineData("retroarch", "emulators/retroarch/retroarch.exe", "snes")]
    public void Fresh_bundled_emulators_write_only_inside_the_root(string id, string exe, string system)
    {
        var (install, _) = Emu(id, exe);
        Assert.Equal(PortableApplyResult.Applied, PortableSetups.Apply(install, Make.Plan(system, LaunchMode.Fullscreen), _t.Paths));
        Assert.True(_t.NothingOutsideRoot(), $"{id} wrote next to the RetroHub root");
        Assert.All(_t.Files(), f => Assert.True(
            f.StartsWith("emulators/" + id + "/", StringComparison.Ordinal) || f.StartsWith("data/", StringComparison.Ordinal),
            $"{id}: unexpected file {f}"));
    }

    [Fact]
    public void Dolphin_update_check_and_statistics_question_are_off()
    {
        var (install, dir) = Emu("dolphin", "emulators/dolphin/Dolphin.exe");
        var file = _t.Touch("emulators/dolphin/User/Config/Dolphin.ini", "[General]\nISOPath0 = D:/x\n[AutoUpdate]\nUpdateTrack = stable\n");
        PortableSetups.Apply(install, Make.Plan("gamecube", LaunchMode.Fullscreen), _t.Paths);
        var ini = IniFile.Load(file);
        Assert.Equal("", ini.Get("AutoUpdate", "UpdateTrack"));
        Assert.Equal("True", ini.Get("Analytics", "PermissionAsked"));
        Assert.Equal("False", ini.Get("Analytics", "Enabled"));
        Assert.Equal("D:/x", ini.Get("General", "ISOPath0"));
        Assert.Contains("UpdateTrack =", File.ReadAllText(file));
        Assert.True(File.Exists(Path.Combine(dir, "portable.txt")));
    }

    [Fact]
    public void Mgba_keeps_its_update_check_off_and_uses_an_own_gba_bios_only_when_it_is_a_known_dump()
    {
        var (install, dir) = Emu("mgba", "emulators/mgba/mGBA.exe");
        PortableSetups.Apply(install, Make.Plan("gba", LaunchMode.Embedded), _t.Paths);
        var ini = IniFile.Load(Path.Combine(dir, "config.ini"));
        Assert.Equal("0", ini.Get("ports.qt", "updateAutoCheck"));
        Assert.Null(ini.Get("ports.qt", "gba.bios"));

        // a fake file with the right name but an unknown MD5 is not switched on
        _t.Touch("bios/gba_bios.bin", new byte[16384]);
        PortableSetups.Apply(install, Make.Plan("gba", LaunchMode.Embedded), _t.Paths);
        Assert.Null(IniFile.Load(Path.Combine(dir, "config.ini")).Get("ports.qt", "useBios"));
        Assert.NotEqual("a860e8c0b6d573d191e4ec7db1b1e4f6", Convert.ToHexStringLower(MD5.HashData(new byte[16384])));
    }

    [Fact]
    public void Own_installs_outside_the_root_are_left_untouched()
    {
        foreach (var (id, name) in new[] { ("mgba", "mGBA.exe"), ("dolphin", "Dolphin.exe"), ("cemu", "Cemu.exe"), ("eden", "eden.exe"), ("azahar", "azahar.exe") })
        {
            var dir = Path.Combine(_t.Base, "own", id);
            Directory.CreateDirectory(dir);
            var exe = Path.Combine(dir, name);
            File.WriteAllText(exe, "");
            var install = Make.Install(new EmulatorDef { Id = id, Name = id, Portable = id }, exe);
            Assert.Equal(PortableApplyResult.SkippedExternal, PortableSetups.Apply(install, Make.Plan("gba", LaunchMode.Fullscreen), _t.Paths));
            Assert.Equal([name], Directory.EnumerateFileSystemEntries(dir).Select(Path.GetFileName).ToArray());
        }
        // RetroArch only writes RetroHub's own override – also for an own install
        var raDir = Path.Combine(_t.Base, "own", "retroarch");
        Directory.CreateDirectory(raDir);
        var ra = Path.Combine(raDir, "retroarch.exe");
        File.WriteAllText(ra, "");
        Assert.Equal(PortableApplyResult.Applied,
            PortableSetups.Apply(Make.Install(new EmulatorDef { Id = "retroarch", Name = "RetroArch", Portable = "retroarch" }, ra), Make.Plan("snes", LaunchMode.Fullscreen), _t.Paths));
        Assert.Equal(["retroarch.exe"], Directory.EnumerateFileSystemEntries(raDir).Select(Path.GetFileName).ToArray());
    }

    [Theory]
    [InlineData("2.6", "2.6")]
    [InlineData("v0.2.1\r\n", "0.2.1")]
    [InlineData("Azahar 2126.2 (installer)", "2126.2")]
    [InlineData("", null)]
    [InlineData("unknown", null)]
    public void Version_txt_names_the_version_of_exes_without_a_version_resource(string content, string? expected)
    {
        var exe = _t.Touch("emulators/cemu/Cemu.exe");
        _t.Touch("emulators/cemu/version.txt", content);
        Assert.Equal(expected, EmulatorRegistry.VersionFile(exe, _t.Paths.Emulators));
        // the installer's flat layout: the fake exe has no resource, so ReadVersion takes version.txt
        if (expected != null) Assert.Equal(expected, EmulatorRegistry.ReadVersion(exe, _t.Paths.Emulators));
    }

    [Fact]
    public void Version_txt_is_looked_for_up_to_the_emulator_folder_only()
    {
        _t.Touch("emulators/version.txt", "9.9");
        var exe = _t.Touch("emulators/eden/bin/eden.exe");
        Assert.Null(EmulatorRegistry.VersionFile(exe, _t.Paths.Emulators));
        _t.Touch("emulators/eden/version.txt", "0.2.1");
        Assert.Equal("0.2.1", EmulatorRegistry.VersionFile(exe, _t.Paths.Emulators));
    }
}
