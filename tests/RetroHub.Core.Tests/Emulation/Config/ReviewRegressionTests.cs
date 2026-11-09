using RetroHub.Core.Catalog;
using RetroHub.Core.Emulation;
using RetroHub.Core.Emulation.ConfigFiles;
using RetroHub.Core.Emulation.Portable;
using Xunit;

namespace RetroHub.Core.Tests.Emulation.Config;

/// <summary>Regression tests for defects found in the review of the emulation module.</summary>
public sealed class ReviewRegressionTests : IDisposable
{
    private readonly TempRoot _t = new();
    public void Dispose() => _t.Dispose();

    /// <summary>
    /// A shortened copy of a qt-config.ini written by Azahar 2126 on this machine: the "Paths" group lives inside [UI]
    /// (keys "Paths\romsPath", "Paths\screenshotPath"); there is no [Paths] section. The recipe used to write
    /// "[Paths] romsPath", which Azahar never reads.
    /// </summary>
    private const string AzaharWritten =
        "[Data%20Storage]\r\nuse_virtual_sd\\default=true\r\nuse_virtual_sd=true\r\nuse_custom_storage\\default=true\r\nuse_custom_storage=false\r\n" +
        "nand_directory\\default=false\r\nnand_directory=C:\\\\Users\\\\x\\\\AppData\\\\Roaming/Azahar/nand/\r\n" +
        "sdmc_directory\\default=false\r\nsdmc_directory=C:\\\\Users\\\\x\\\\AppData\\\\Roaming/Azahar/sdmc/\r\n\r\n" +
        "[Miscellaneous]\r\ncheck_for_update_on_start\\default=true\r\ncheck_for_update_on_start=true\r\n\r\n" +
        "[UI]\r\nPaths\\screenshotPath\\default=true\r\nPaths\\screenshotPath=\r\nPaths\\romsPath=\r\nPaths\\symbolsPath=\r\n" +
        "Paths\\gamedirs\\1\\path=INSTALLED\r\nPaths\\gamedirs\\size=2\r\nconfirmClose\\default=true\r\nconfirmClose=true\r\n\r\n" +
        "[Core]\r\ncpu_clock_percentage\\default=true\r\ncpu_clock_percentage=100\r\n";

    [Fact]
    public void Azahar_rom_and_screenshot_paths_go_into_the_UI_group_azahar_reads()
    {
        var exe = _t.Touch("emulators/azahar/azahar-windows-msvc-2126.1.2/azahar.exe");
        var file = _t.Touch("emulators/azahar/azahar-windows-msvc-2126.1.2/user/config/qt-config.ini", AzaharWritten);
        var install = Make.Install(new EmulatorDef { Id = "azahar", Name = "Azahar", Portable = "azahar" }, exe);

        Assert.Equal(PortableApplyResult.Applied, PortableSetups.Apply(install, Make.Plan("3ds", LaunchMode.Embedded), _t.Paths));

        var ini = IniFile.Load(file);
        string Q(params string[] parts) => "\"" + Path.Combine([_t.Root, .. parts]).Replace('\\', '/') + "/\"";
        Assert.Equal(Q("games", "3ds"), ini.Get("UI", @"Paths\romsPath"));
        Assert.Equal(Q("screenshots", "3ds"), ini.Get("UI", @"Paths\screenshotPath"));
        Assert.Equal("false", ini.Get("UI", @"Paths\screenshotPath\default"));
        Assert.Null(ini.Get("UI", @"Paths\romsPath\default")); // Azahar has no default twin for romsPath
        Assert.False(ini.HasSection("Paths"));
        Assert.Equal("false", ini.Get("UI", "confirmClose"));
        Assert.Equal("INSTALLED", ini.Get("UI", @"Paths\gamedirs\1\path"));

        // the existing lines were replaced in place: the file still has exactly its 4 sections, CRLF only
        Assert.Equal(["Data%20Storage", "Miscellaneous", "UI", "Core"], ini.Sections());
        var text = File.ReadAllText(file);
        Assert.DoesNotContain("\n", text.Replace("\r\n", ""));
        // every line of the original that RetroHub does not manage is still there, unchanged
        foreach (var line in AzaharWritten.Split("\r\n"))
        {
            if (line.Length == 0 || line.StartsWith("use_custom_storage", StringComparison.Ordinal) || line.StartsWith("nand_directory=", StringComparison.Ordinal) ||
                line.StartsWith("sdmc_directory=", StringComparison.Ordinal) || line.StartsWith("check_for_update_on_start", StringComparison.Ordinal) ||
                line.StartsWith(@"Paths\screenshotPath", StringComparison.Ordinal) || line.StartsWith(@"Paths\romsPath", StringComparison.Ordinal) ||
                line.StartsWith("confirmClose", StringComparison.Ordinal)) continue;
            Assert.Contains(line + "\r\n", text);
        }
    }

    [Fact]
    public void Toml_set_replaces_a_multiline_value_completely()
    {
        var t = TomlFile.Parse("[T]\nlist = [\n  1,\n  2\n]\nk = 1\n");
        Assert.True(t.Set("T", "list", "[3]"));
        Assert.Equal("[T]\nlist = [3]\nk = 1\n", t.ToString());

        var s = TomlFile.Parse("a = \"\"\"\nline\n[not a table]\n\"\"\"\nb = 2\n");
        s.SetString(null, "a", "x");
        Assert.Equal("a = \"x\"\nb = 2\n", s.ToString());
    }

    [Fact]
    public void Toml_remove_takes_the_continuation_lines_with_it()
    {
        var t = TomlFile.Parse("[T]\nlist = [\n  1,\n  2\n]\nk = 1\n");
        Assert.True(t.Remove("T", "list"));
        Assert.Equal("[T]\nk = 1\n", t.ToString());
        // the result is still parseable: k belongs to [T]
        Assert.Equal("1", TomlFile.Parse(t.ToString()).Get("T", "k"));
    }

    [Fact]
    public void Toml_new_key_goes_after_a_multiline_last_value_with_blank_lines_inside()
    {
        var t = TomlFile.Parse("[T]\ns = \"\"\"\n\nx\n\"\"\"\n\n[U]\n");
        t.Set("T", "k", "1");
        Assert.Equal("[T]\ns = \"\"\"\n\nx\n\"\"\"\nk = 1\n\n[U]\n", t.ToString());
    }

    [Fact]
    public void Null_dictionaries_in_a_hand_edited_settings_json_do_not_hide_every_emulator()
    {
        Directory.CreateDirectory(_t.Paths.Config);
        File.WriteAllText(_t.Paths.SettingsFile,
            "{\"systemEmulator\": null, \"emulatorMode\": null, \"emulatorPaths\": null, \"emulatorExtraArgs\": null}");
        _t.Settings.Load();
        Assert.Null(_t.Settings.Current.EmulatorPaths); // the premise: System.Text.Json really sets null

        var def = new EmulatorDef { Id = "mgba", Name = "mGBA", Exe = ["mGBA.exe"], Args = ["{rom}"] };
        var exe = _t.Touch("emulators/mgba/mGBA.exe");
        _t.Touch("games/gba/a.gba");
        var catalog = new FakeCatalog().Add(def).Add(new SystemDef { Id = "gba", Name = "GBA", Emulators = ["mgba"] });

        var reg = new EmulatorRegistry(catalog, _t.Paths, _t.Settings);
        Assert.Equal(exe, reg.Get("mgba")!.ExePath);

        var plan = new LaunchPlanner(catalog, reg, _t.Paths, _t.Settings).Plan(Make.Game("gba", "games/gba/a.gba"));
        Assert.True(plan.CanLaunch, string.Join("; ", plan.Problems.Select(p => p.Message)));
        Assert.Equal(exe, plan.FileName);

        // setting a user path repairs the null dictionary instead of failing silently
        var custom = _t.Touch("tools/mGBA.exe");
        reg.SetUserPath("mgba", custom);
        Assert.Equal(custom, reg.Get("mgba")!.ExePath);
        Assert.Equal("tools/mGBA.exe", _t.Settings.Current.EmulatorPaths!["mgba"]);
    }
}
