using System.Text;
using RetroHub.Core.Catalog;
using RetroHub.Core.Emulation;
using RetroHub.Core.Emulation.Portable;
using Xunit;

namespace RetroHub.Core.Tests.Emulation.Config;

/// <summary>Cemu recipe: disc keys (.key next to .wux/.wud) are added to portable\keys.txt.</summary>
public sealed class CemuDiscKeyTests : IDisposable
{
    private readonly TempRoot _t = new();
    private readonly EmulatorInstall _cemu;
    private readonly string _keysTxt;

    // made-up test keys (not real disc keys)
    private static readonly byte[] KeyA = Convert.FromHexString("00112233445566778899AABBCCDDEEFF");
    private static readonly byte[] KeyB = Convert.FromHexString("0F1E2D3C4B5A69788796A5B4C3D2E1F0");

    // Cemu 2.6's keys.txt template as found on this machine (CRLF, with Cemu's public example key)
    private const string Template =
        "# this file contains keys needed for decryption of disc file system data (WUD/WUX)\r\n" +
        "# 1 key per line, any text after a '#' character is considered a comment\r\n" +
        "# the emulator will automatically pick the right key\r\n" +
        "541b9889519b27d363cd21604b97c67a # example key (can be deleted)\r\n";

    public CemuDiscKeyTests()
    {
        var exe = _t.Touch("emulators/cemu/Cemu_2.6/Cemu.exe");
        var def = new EmulatorDef { Id = "cemu", Name = "Cemu", Exe = ["Cemu.exe"], Portable = "cemu" };
        _cemu = Make.Install(def, exe);
        _keysTxt = Path.Combine(_t.Root, "emulators", "cemu", "Cemu_2.6", "portable", "keys.txt");
    }

    public void Dispose() => _t.Dispose();

    private PortableApplyResult Launch(string gamePath) =>
        PortableSetups.Apply(_cemu, Make.Plan("wiiu", LaunchMode.Fullscreen, gamePath), _t.Paths);

    private static string Hex(byte[] k) => Convert.ToHexString(k).ToLowerInvariant();

    [Fact]
    public void Binary_key_next_to_the_wux_is_added_and_the_source_stays_unchanged()
    {
        var wux = _t.Touch("games/wiiu/Zelda WW HD/Zelda WW HD.wux", "disc");
        var key = _t.Touch("games/wiiu/Zelda WW HD/Zelda WW HD.key", KeyA);
        var old = new DateTime(2024, 3, 1, 12, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(key, old);
        File.SetLastWriteTimeUtc(wux, old);

        Assert.Equal(PortableApplyResult.Applied, Launch(wux));

        Assert.Equal($"{Hex(KeyA)} # Zelda WW HD.wux {CemuSetup.ImportedComment}\r\n", File.ReadAllText(_keysTxt));
        Assert.Equal(KeyA, File.ReadAllBytes(key));
        Assert.Equal(old, File.GetLastWriteTimeUtc(key));
        Assert.Equal("disc", File.ReadAllText(wux));
        Assert.Equal(old, File.GetLastWriteTimeUtc(wux));
        Assert.Equal(2, Directory.GetFiles(Path.GetDirectoryName(wux)!).Length);
        Assert.True(CemuSetup.HasDiscKeys(_keysTxt));
        Assert.DoesNotContain(_t.Files(), f => f.EndsWith(".retrohub-tmp", StringComparison.Ordinal));
    }

    [Fact]
    public void Hex_text_key_with_whitespace_and_case_insensitive_name_is_accepted()
    {
        var wud = _t.Touch("games/wiiu/Game.WUD", "disc");
        _t.Touch("games/wiiu/game.KEY", "  0f1e2d3c4b5a6978\r\n8796A5B4C3D2E1F0 \n");
        Launch(wud);
        Assert.Equal($"{Hex(KeyB)} # Game.WUD {CemuSetup.ImportedComment}\r\n", File.ReadAllText(_keysTxt));
    }

    [Fact]
    public void Existing_lines_and_line_endings_are_preserved_and_a_key_is_added_only_once()
    {
        var wux = _t.Touch("games/wiiu/A.wux", "disc");
        _t.Touch("games/wiiu/A.key", KeyA);
        var keysTxt = _t.Touch("emulators/cemu/Cemu_2.6/portable/keys.txt", Template);
        var before = File.ReadAllBytes(keysTxt);

        Launch(wux);
        var after1 = File.ReadAllBytes(keysTxt);
        Assert.Equal(before, after1[..before.Length]);
        Assert.Equal(Template + $"{Hex(KeyA)} # A.wux {CemuSetup.ImportedComment}\r\n", Encoding.UTF8.GetString(after1));

        // second launch: nothing changes (content and timestamp)
        var stamp = File.GetLastWriteTimeUtc(keysTxt);
        Thread.Sleep(30);
        Launch(wux);
        Assert.Equal(after1, File.ReadAllBytes(keysTxt));
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(keysTxt));
    }

    [Fact]
    public void Key_already_present_with_other_case_comment_or_spacing_is_not_added()
    {
        var wux = _t.Touch("games/wiiu/A.wux", "disc");
        _t.Touch("games/wiiu/A.key", KeyA);
        const string mine = "# my keys\n  00112233445566778899aabbccddeeff   # Zelda (eigene Disc)\nffffffffffffffffffffffffffffffff\n";
        var keysTxt = _t.Touch("emulators/cemu/Cemu_2.6/portable/keys.txt", mine);
        Launch(wux);
        Assert.Equal(mine, File.ReadAllText(keysTxt));
    }

    [Fact]
    public void Lf_file_without_final_newline_gets_an_lf_line()
    {
        var wux = _t.Touch("games/wiiu/A.wux", "disc");
        _t.Touch("games/wiiu/A.key", KeyB);
        var keysTxt = _t.Touch("emulators/cemu/Cemu_2.6/portable/keys.txt", "# mine\nffffffffffffffffffffffffffffffff");
        Launch(wux);
        Assert.Equal($"# mine\nffffffffffffffffffffffffffffffff\n{Hex(KeyB)} # A.wux {CemuSetup.ImportedComment}\n", File.ReadAllText(keysTxt));
    }

    [Fact]
    public void Two_games_add_two_keys()
    {
        var a = _t.Touch("games/wiiu/A/A.wux", "disc");
        _t.Touch("games/wiiu/A/A.key", KeyA);
        var b = _t.Touch("games/wiiu/B/B.wux", "disc");
        _t.Touch("games/wiiu/B/B.key", KeyB);
        Launch(a);
        Launch(b);
        Launch(a);
        var keys = CemuSetup.ParseKeys(File.ReadAllText(_keysTxt)).Select(k => k.Key).ToList();
        Assert.Equal([Convert.ToHexString(KeyA), Convert.ToHexString(KeyB)], keys);
    }

    [Theory]
    [InlineData(15)]
    [InlineData(17)]
    [InlineData(32)] // 32 bytes, but not hex text
    [InlineData(0)]
    [InlineData(4096)]
    public void Wrong_size_or_content_is_ignored(int size)
    {
        var wux = _t.Touch("games/wiiu/A.wux", "disc");
        var bytes = Enumerable.Range(0, size).Select(i => (byte)(i * 7 + 1)).ToArray();
        var key = _t.Touch("games/wiiu/A.key", bytes);
        Assert.Equal(PortableApplyResult.Applied, Launch(wux));
        Assert.False(File.Exists(_keysTxt));
        Assert.Equal(bytes, File.ReadAllBytes(key));
    }

    [Fact]
    public void Hex_text_with_wrong_length_is_ignored()
    {
        var wux = _t.Touch("games/wiiu/A.wux", "disc");
        _t.Touch("games/wiiu/A.key", "00112233445566778899AABBCCDDEEFF00");
        Launch(wux);
        Assert.False(File.Exists(_keysTxt));
    }

    [Fact]
    public void No_key_changes_nothing()
    {
        var wux = _t.Touch("games/wiiu/A.wux", "disc");
        var keysTxt = _t.Touch("emulators/cemu/Cemu_2.6/portable/keys.txt", Template);
        var before = _t.Files();
        Assert.Equal(PortableApplyResult.Applied, Launch(wux));
        Assert.Equal(Template, File.ReadAllText(keysTxt));
        // the only new file is Cemu's settings.xml with the update check switched off (OneFolderTests)
        Assert.Equal(before.Append("emulators/cemu/Cemu_2.6/portable/settings.xml").Order(StringComparer.OrdinalIgnoreCase), _t.Files());
        Assert.False(CemuSetup.HasDiscKeys(keysTxt)); // only Cemu's example line
    }

    [Fact]
    public void Only_key_file_in_the_folder_is_used_but_not_when_there_are_several()
    {
        var wux = _t.Touch("games/wiiu/A/Game (EUR).wux", "disc");
        _t.Touch("games/wiiu/A/disc.key", KeyA);
        Launch(wux);
        Assert.Single(CemuSetup.ParseKeys(File.ReadAllText(_keysTxt)));

        var other = _t.Touch("games/wiiu/B/Game.wux", "disc");
        _t.Touch("games/wiiu/B/x.key", KeyB);
        _t.Touch("games/wiiu/B/y.key", KeyB);
        Assert.Null(CemuSetup.FindKeyFile(other));
        Launch(other);
        Assert.Single(CemuSetup.ParseKeys(File.ReadAllText(_keysTxt)));
    }

    [Fact]
    public void Other_files_and_rpx_games_are_not_touched()
    {
        var rpx = _t.Touch("games/wiiu/Game/code/game.rpx", "rpx");
        _t.Touch("games/wiiu/Game/code/game.key", KeyA);
        Launch(rpx);
        Assert.False(File.Exists(_keysTxt));
        Assert.True(Directory.Exists(Path.GetDirectoryName(_keysTxt)));
    }

    [Fact]
    public void Single_key_in_a_flat_folder_with_several_images_is_not_used_for_the_other_image()
    {
        var a = _t.Touch("games/wiiu/A.wux", "disc");
        var keyA = _t.Touch("games/wiiu/A.key", KeyA);
        var b = _t.Touch("games/wiiu/B.wux", "disc");

        Assert.Equal(keyA, CemuSetup.FindKeyFile(a));
        Assert.Null(CemuSetup.FindKeyFile(b));
        Launch(b);
        Assert.False(File.Exists(_keysTxt));

        var hint = Assert.Single(Planner().Plan(Make.Game("wiiu", "games/wiiu/B.wux")).Problems);
        Assert.Equal(CemuSetup.MissingDiscKeyMessage, hint.Message);
        Assert.Empty(Planner().Plan(Make.Game("wiiu", "games/wiiu/A.wux")).Problems);

        Launch(a);
        Assert.Equal($"{Hex(KeyA)} # A.wux {CemuSetup.ImportedComment}\r\n", File.ReadAllText(_keysTxt));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(15)]
    [InlineData(4096)]
    public void Launch_plan_hint_for_a_key_file_that_holds_no_usable_key(int size)
    {
        _t.Touch("games/wiiu/A.wux", "disc");
        _t.Touch("games/wiiu/A.key", Enumerable.Range(0, size).Select(i => (byte)(i * 7 + 1)).ToArray());
        _t.Touch("emulators/cemu/Cemu_2.6/portable/keys.txt", Template);

        var plan = Planner().Plan(Make.Game("wiiu", "games/wiiu/A.wux"));
        Assert.True(plan.CanLaunch);
        var hint = Assert.Single(plan.Problems);
        Assert.False(hint.Blocking);
        Assert.Equal(CemuSetup.InvalidDiscKeyMessage("A.key"), hint.Message);
        Assert.Contains("„A.key“", hint.Message);

        // hex text of the wrong length: same hint; a real key line in keys.txt → none
        _t.Touch("games/wiiu/A.key", "00112233445566778899AABBCCDDEEFF00");
        Assert.Equal(CemuSetup.InvalidDiscKeyMessage("A.key"), Assert.Single(Planner().Plan(Make.Game("wiiu", "games/wiiu/A.wux")).Problems).Message);
        File.AppendAllText(_keysTxt, "ffffffffffffffffffffffffffffffff # meine Disc\r\n");
        Assert.Empty(Planner().Plan(Make.Game("wiiu", "games/wiiu/A.wux")).Problems);
    }

    private LaunchPlanner Planner()
    {
        var def = new EmulatorDef
        {
            Id = "cemu", Name = "Cemu", Exe = ["Cemu.exe"], Portable = "cemu",
            Args = ["{fullscreen}", "-g", "{rom}"], FullscreenArgs = ["-f"],
            DefaultMode = LaunchMode.Fullscreen, Modes = [LaunchMode.Fullscreen, LaunchMode.Windowed],
        };
        var catalog = new FakeCatalog().Add(def).Add(new SystemDef { Id = "wiiu", Name = "Wii U", Emulators = ["cemu"] });
        var reg = new EmulatorRegistry(catalog, _t.Paths, _t.Settings);
        return new LaunchPlanner(catalog, reg, _t.Paths, _t.Settings);
    }

    [Fact]
    public void Launch_plan_hint_only_for_a_disc_image_without_key()
    {
        _t.Touch("games/wiiu/A.wux", "disc");
        _t.Touch("games/wiiu/B/B.wux", "disc");
        _t.Touch("games/wiiu/B/B.key", KeyB);
        _t.Touch("games/wiiu/C/code/c.rpx", "rpx");
        var planner = Planner();

        var plan = planner.Plan(Make.Game("wiiu", "games/wiiu/A.wux"));
        Assert.True(plan.CanLaunch, string.Join("; ", plan.Problems.Select(p => p.Message)));
        var hint = Assert.Single(plan.Problems);
        Assert.False(hint.Blocking);
        Assert.Equal(CemuSetup.MissingDiscKeyMessage, hint.Message);

        Assert.Empty(planner.Plan(Make.Game("wiiu", "games/wiiu/B/B.wux")).Problems);
        Assert.Empty(planner.Plan(Make.Game("wiiu", "games/wiiu/C/code/c.rpx")).Problems);

        // only Cemu's template example → still the hint; a real key line → none
        var keysTxt = _t.Touch("emulators/cemu/Cemu_2.6/portable/keys.txt", Template);
        Assert.Single(planner.Plan(Make.Game("wiiu", "games/wiiu/A.wux")).Problems);
        File.AppendAllText(keysTxt, "ffffffffffffffffffffffffffffffff # meine Disc\r\n");
        Assert.Empty(planner.Plan(Make.Game("wiiu", "games/wiiu/A.wux")).Problems);
    }
}
