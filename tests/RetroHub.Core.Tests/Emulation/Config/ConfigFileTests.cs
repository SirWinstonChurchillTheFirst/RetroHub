using System.Text;
using RetroHub.Core.Emulation.ConfigFiles;
using Xunit;

namespace RetroHub.Core.Tests.Emulation.Config;

public sealed class IniFileTests
{
    private const string Sample =
        "; DeSmuME style\r\n" +
        "[3D]\r\n" +
        "Renderer=2\r\n" +
        "\r\n" +
        "[Video]\r\n" +
        "Window Rotate=0\r\n" +
        "# a comment inside\r\n" +
        "Window Force Ratio=1\r\n" +
        "\r\n" +
        "[PathSettings]\r\n" +
        "Roms=D:\\old\\roms\r\n" +
        "\r\n" +
        "[General]\r\n" +
        "Recent Rom 0=D:\\x.nds\r\n";

    [Fact]
    public void Untouched_document_round_trips_exactly()
    {
        var ini = IniFile.Parse(Sample);
        Assert.Equal(Sample, ini.ToString());
        Assert.False(ini.IsDirty);
    }

    [Fact]
    public void Set_existing_key_changes_only_that_line()
    {
        var ini = IniFile.Parse(Sample);
        Assert.True(ini.Set("PathSettings", "Roms", @"E:\RH\games\nds"));
        Assert.Equal(Sample.Replace(@"Roms=D:\old\roms", @"Roms=E:\RH\games\nds"), ini.ToString());
        Assert.False(ini.Set("PathSettings", "Roms", @"E:\RH\games\nds")); // same value: no change
    }

    [Fact]
    public void Section_and_key_are_case_insensitive_and_keep_their_spelling()
    {
        var ini = IniFile.Parse(Sample);
        Assert.Equal("1", ini.Get("video", "window force ratio"));
        ini.Set("VIDEO", "WINDOW FORCE RATIO", "0");
        Assert.Contains("Window Force Ratio=0\r\n", ini.ToString());
    }

    [Fact]
    public void New_key_goes_after_the_last_key_of_its_section()
    {
        var ini = IniFile.Parse(Sample);
        ini.Set("PathSettings", "Battery", @"E:\RH\saves\nds");
        Assert.Contains("[PathSettings]\r\nRoms=D:\\old\\roms\r\nBattery=E:\\RH\\saves\\nds\r\n\r\n[General]", ini.ToString());
    }

    [Fact]
    public void New_section_is_appended_with_the_files_separator_style()
    {
        var ini = IniFile.Parse(Sample);
        ini.Set("Display", "ScreenGap", "0");
        Assert.EndsWith("Recent Rom 0=D:\\x.nds\r\n\r\n[Display]\r\nScreenGap=0\r\n", ini.ToString());
        Assert.StartsWith(Sample, ini.ToString());
    }

    [Fact]
    public void Remove_deletes_only_that_key()
    {
        var ini = IniFile.Parse(Sample);
        Assert.True(ini.Remove("Video", "Window Rotate"));
        Assert.Equal(Sample.Replace("Window Rotate=0\r\n", ""), ini.ToString());
        Assert.False(ini.Remove("Video", "Window Rotate"));
        Assert.False(ini.Remove("Nope", "x"));
    }

    [Fact]
    public void Spaced_style_is_kept_and_used_for_new_keys()
    {
        const string text = "[Main]\nSettingsVersion = 3\nFoo = bar\n";
        var ini = IniFile.Parse(text);
        ini.Set("Main", "Foo", "baz");
        ini.Set("Main", "New", "1");
        ini.Set("BIOS", "SearchDirectory", @"C:\bios");
        Assert.Equal("[Main]\nSettingsVersion = 3\nFoo = baz\nNew = 1\n\n[BIOS]\nSearchDirectory = C:\\bios\n", ini.ToString());
        Assert.Equal("3", ini.Get("Main", "SettingsVersion"));
    }

    [Fact]
    public void Qt_keys_with_backslashes_and_percent_sections()
    {
        const string text = "[Data%20Storage]\nuse_custom_storage\\default=true\nuse_custom_storage=false\n\n[UI]\nconfirmClose=true\n";
        var ini = IniFile.Parse(text);
        Assert.Equal("true", ini.Get("Data%20Storage", @"use_custom_storage\default"));
        ini.Set("Data%20Storage", @"use_custom_storage\default", "false");
        ini.Set("Data%20Storage", "use_custom_storage", "true");
        ini.Set("Data%20Storage", @"sdmc_directory\default", "false");
        Assert.Equal("[Data%20Storage]\nuse_custom_storage\\default=false\nuse_custom_storage=true\nsdmc_directory\\default=false\n\n[UI]\nconfirmClose=true\n",
            ini.ToString());
    }

    [Fact]
    public void Top_level_keys_before_the_first_section()
    {
        var ini = IniFile.Parse("a=1\n[S]\nb=2\n");
        Assert.Equal("1", ini.Get(null, "a"));
        Assert.Null(ini.Get(null, "b"));
        ini.Set(null, "c", "3");
        Assert.Equal("a=1\nc=3\n[S]\nb=2\n", ini.ToString());
        var empty = IniFile.Parse("[S]\nb=2\n");
        empty.Set("", "top", "x");
        Assert.Equal("top=x\n[S]\nb=2\n", empty.ToString());
    }

    [Fact]
    public void Duplicate_keys_collapse_on_set()
    {
        var ini = IniFile.Parse("[S]\nk=1\nother=x\nk=2\n");
        Assert.Equal("1", ini.Get("S", "k"));
        ini.Set("S", "k", "3");
        Assert.Equal("[S]\nk=3\nother=x\n", ini.ToString());
    }

    [Fact]
    public void Values_with_equals_signs_and_comment_like_text_are_kept()
    {
        var ini = IniFile.Parse("[S]\nexpr=a=b ; not a comment\n");
        Assert.Equal("a=b ; not a comment", ini.Get("S", "expr"));
    }

    [Fact]
    public void Lists_sections_and_keys()
    {
        var ini = IniFile.Parse(Sample);
        Assert.Equal(["3D", "Video", "PathSettings", "General"], ini.Sections());
        Assert.Equal(["Window Rotate", "Window Force Ratio"], ini.Keys("Video"));
        Assert.True(ini.HasSection("general"));
    }

    [Fact]
    public void File_without_final_newline_keeps_that_style()
    {
        var ini = IniFile.Parse("[S]\nk=1");
        ini.Set("S", "j", "2");
        Assert.Equal("[S]\nk=1\nj=2", ini.ToString());
    }

    [Fact]
    public void Invalid_keys_are_rejected()
    {
        var ini = IniFile.Parse("");
        Assert.Throws<ArgumentException>(() => ini.Set("S", "a=b", "1"));
        Assert.Throws<ArgumentException>(() => ini.Set("S", " ", "1"));
        Assert.Throws<ArgumentException>(() => ini.Set("S]x", "a", "1"));
        // a line break in a value cannot inject a new key
        ini.Set("S", "a", "1\r\nevil=1");
        Assert.Null(ini.Get("S", "evil"));
    }

    [Fact]
    public void Saves_bom_lf_and_encoding_unchanged()
    {
        using var t = new TempRoot();
        var withBom = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("[S]\nPfad=Spielstände\n")).ToArray();
        var path = t.Touch("a.ini", withBom);
        var ini = IniFile.Load(path);
        Assert.Equal("Spielstände", ini.Get("S", "Pfad"));
        ini.Save(path);
        Assert.Equal(withBom, File.ReadAllBytes(path));
        ini.Set("S", "x", "ö");
        ini.Save(path);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("[S]\nPfad=Spielstände\nx=ö\n")), File.ReadAllBytes(path));
    }

    [Fact]
    public void Latin1_files_round_trip_every_byte()
    {
        using var t = new TempRoot();
        // "Spielstände" in Windows-1252/Latin-1 (invalid as UTF-8), plus a byte 0x80
        var bytes = Encoding.Latin1.GetBytes("[PathSettings]\r\nBattery=D:\\Spielstände\x80\r\n");
        var path = t.Touch("desmume.ini", bytes);
        var ini = IniFile.Load(path);
        Assert.Equal(Encoding.Latin1.CodePage, ini.Encoding.CodePage);
        ini.Save(path);
        Assert.Equal(bytes, File.ReadAllBytes(path));
        ini.Set("PathSettings", "States", @"D:\Zustände");
        ini.Save(path);
        Assert.Equal(Encoding.Latin1.GetBytes("[PathSettings]\r\nBattery=D:\\Spielstände\x80\r\nStates=D:\\Zustände\r\n"), File.ReadAllBytes(path));
    }

    [Fact]
    public void Ascii_file_uses_the_requested_fallback_encoding()
    {
        using var t = new TempRoot();
        var path = t.Touch("desmume.ini", "[S]\r\na=1\r\n");
        var ini = IniFile.Load(path, Encoding.Latin1);
        ini.Set("S", "p", "Ä");
        ini.Save(path);
        Assert.Equal(Encoding.Latin1.GetBytes("[S]\r\na=1\r\np=Ä\r\n"), File.ReadAllBytes(path));
        // default: UTF-8 without BOM
        var other = IniFile.Load(t.Touch("b.ini", "[S]\n"));
        other.Set("S", "p", "Ä");
        Assert.Equal(Encoding.UTF8.GetBytes("[S]\np=Ä\n"), other.ToBytes());
    }

    [Fact]
    public void Missing_file_is_new_and_saved_with_crlf_utf8()
    {
        using var t = new TempRoot();
        var path = Path.Combine(t.Root, "sub", "new.ini");
        var ini = IniFile.Load(path);
        Assert.True(ini.IsNew);
        ini.Set("ports.qt", "savegamePath", "C:/x");
        Assert.True(ini.SaveIfChanged(path));
        Assert.Equal("[ports.qt]\r\nsavegamePath=C:/x\r\n", File.ReadAllText(path));
        Assert.False(IniFile.Load(path).SaveIfChanged(path));
        Assert.False(File.Exists(path + ".retrohub-tmp"));
    }
}

public sealed class TomlFileTests
{
    private const string Melon =
        "LastBIOSFolder = \"\"\n" +
        "PauseLostFocus = true\n" +
        "LastROMFolder = \"D:/Pokemon/Rom\"\n" +
        "\n" +
        "[DS]\n" +
        "FirmwarePath = \"\"\n" +
        "BIOS7Path = \"\" # comment kept\n" +
        "\n" +
        "[Instance0]\n" +
        "EnableCheats = false\n" +
        "SaveFilePath = \"\"\n" +
        "\n" +
        "[Instance0.Window0]\n" +
        "Geometry = \"AdnQywADAAA=\"\n" +
        "\n" +
        "[Emu]\n" +
        "ExternalBIOSEnable = false\n";

    [Fact]
    public void Untouched_document_round_trips_exactly()
    {
        Assert.Equal(Melon, TomlFile.Parse(Melon).ToString());
    }

    [Fact]
    public void Get_unquotes_and_strips_comments()
    {
        var t = TomlFile.Parse(Melon);
        Assert.Equal("D:/Pokemon/Rom", t.Get(null, "LastROMFolder"));
        Assert.Equal("", t.Get("DS", "BIOS7Path"));
        Assert.Equal("\"\"", t.GetRaw("DS", "BIOS7Path"));
        Assert.Equal("false", t.Get("Instance0", "EnableCheats"));
        Assert.Null(t.Get("Instance0", "Geometry")); // belongs to [Instance0.Window0]
        Assert.Equal("AdnQywADAAA=", t.Get("Instance0.Window0", "Geometry"));
        Assert.Null(t.Get("Nope", "x"));
    }

    [Fact]
    public void Set_replaces_in_the_right_table_only()
    {
        var t = TomlFile.Parse(Melon);
        t.Set("Instance0", "SaveFilePath", TomlFile.QuotePath(@"E:\RH\saves\nds"));
        t.SetBool(null, "PauseLostFocus", false);
        t.SetBool("Emu", "ExternalBIOSEnable", true);
        t.Set("DS", "BIOS7Path", TomlFile.QuotePath(@"E:\RH\bios\bios7.bin"));
        var expected = Melon
            .Replace("SaveFilePath = \"\"", "SaveFilePath = \"E:/RH/saves/nds\"")
            .Replace("PauseLostFocus = true", "PauseLostFocus = false")
            .Replace("ExternalBIOSEnable = false", "ExternalBIOSEnable = true")
            .Replace("BIOS7Path = \"\" # comment kept", "BIOS7Path = \"E:/RH/bios/bios7.bin\" # comment kept");
        Assert.Equal(expected, t.ToString());
    }

    [Fact]
    public void New_keys_and_tables()
    {
        var t = TomlFile.Parse(Melon);
        t.Set("Instance0", "SavestatePath", "\"E:/s\"");
        t.Set(null, "NewTop", "1");
        t.Set("Audio", "Volume", "256");
        var s = t.ToString();
        Assert.Contains("SaveFilePath = \"\"\nSavestatePath = \"E:/s\"\n\n[Instance0.Window0]", s);
        // top-level keys must stay before the first table
        Assert.StartsWith("LastBIOSFolder = \"\"\nPauseLostFocus = true\nLastROMFolder = \"D:/Pokemon/Rom\"\nNewTop = 1\n\n[DS]", s);
        Assert.EndsWith("ExternalBIOSEnable = false\n\n[Audio]\nVolume = 256\n", s);
    }

    [Fact]
    public void Empty_document_gets_top_level_key_then_tables()
    {
        var t = TomlFile.Parse("");
        t.Set("Instance0", "SaveFilePath", "\"x\"");
        t.Set(null, "LastROMFolder", "\"y\"");
        Assert.Equal("LastROMFolder = \"y\"\r\n[Instance0]\r\nSaveFilePath = \"x\"\r\n", t.ToString());
        var reparsed = TomlFile.Parse(t.ToString());
        Assert.Equal("y", reparsed.Get(null, "LastROMFolder"));
        Assert.Equal("x", reparsed.Get("Instance0", "SaveFilePath"));
    }

    [Fact]
    public void Keys_compare_exactly_like_monhub()
    {
        var t = TomlFile.Parse("[T]\nKey = 1\nKeyLonger = 2\n");
        t.Set("T", "Key", "3");
        Assert.Equal("[T]\nKey = 3\nKeyLonger = 2\n", t.ToString());
        Assert.Null(t.Get("T", "key"));
        Assert.Null(t.Get("t", "Key"));
    }

    [Fact]
    public void Multiline_arrays_and_strings_are_not_parsed_as_tables()
    {
        const string text = "list = [\n  \"a\",\n[not a table]\n]\ns = \"\"\"\n[also not]\nk = 1\n\"\"\"\n[Real]\nk = 2\n";
        var t = TomlFile.Parse(text);
        Assert.False(t.HasTable("not a table"));
        Assert.False(t.HasTable("also not"));
        Assert.Null(t.Get(null, "k"));
        Assert.Equal("2", t.Get("Real", "k"));
        t.Set(null, "after", "true");
        Assert.Equal("list = [\n  \"a\",\n[not a table]\n]\ns = \"\"\"\n[also not]\nk = 1\n\"\"\"\nafter = true\n[Real]\nk = 2\n", t.ToString());
    }

    [Fact]
    public void Quote_escapes_backslashes_and_quotes()
    {
        Assert.Equal("\"C:\\\\a \\\"b\\\"\"", TomlFile.Quote("C:\\a \"b\""));
        Assert.Equal("C:\\a \"b\"", TomlFile.Unquote(TomlFile.Quote("C:\\a \"b\"")));
        Assert.Equal("\"D:/RH/games/nds\"", TomlFile.QuotePath(@"D:\RH\games\nds"));
        Assert.Equal("literal\\n", TomlFile.Unquote("'literal\\n'"));
        Assert.Equal("é", TomlFile.Unquote("\"\\u00E9\""));
    }

    [Fact]
    public void Table_headers_with_spaces_and_remove()
    {
        var t = TomlFile.Parse("[ Instance0 ]\nSaveFilePath = \"a\"\nOther = 1\n");
        Assert.Equal("a", t.Get("Instance0", "SaveFilePath"));
        Assert.True(t.Remove("Instance0", "Other"));
        Assert.False(t.Remove("Instance0", "Other"));
        Assert.Equal("[ Instance0 ]\nSaveFilePath = \"a\"\n", t.ToString());
    }

    [Fact]
    public void Set_is_idempotent()
    {
        var t = TomlFile.Parse(Melon);
        Assert.True(t.Set("Instance0", "SaveFilePath", "\"x\""));
        Assert.False(t.Set("Instance0", "SaveFilePath", "\"x\""));
        Assert.False(t.Set(null, "LastROMFolder", "\"D:/Pokemon/Rom\""));
    }
}

public sealed class RetroArchConfigTests
{
    [Fact]
    public void Set_get_remove_preserving_comments()
    {
        const string text = "# my comment\nvideo_fullscreen = \"false\"\n\n# another\ninput_driver = \"dinput\"\n";
        var c = RetroArchConfig.Parse(text);
        Assert.Equal("false", c.Get("video_fullscreen"));
        Assert.Null(c.Get("missing"));
        Assert.True(c.Set("video_fullscreen", "true"));
        Assert.False(c.Set("video_fullscreen", "true"));
        c.Set("savefile_directory", @"D:\RH\saves");
        Assert.Equal("# my comment\nvideo_fullscreen = \"true\"\n\n# another\ninput_driver = \"dinput\"\nsavefile_directory = \"D:\\RH\\saves\"\n", c.ToString());
        Assert.True(c.Remove("input_driver"));
        Assert.DoesNotContain("input_driver", c.ToString());
        Assert.Equal(["video_fullscreen", "savefile_directory"], c.Keys());
    }

    [Fact]
    public void Unquoted_values_and_duplicates()
    {
        var c = RetroArchConfig.Parse("a = 1\na = \"2\"\n");
        Assert.Equal("1", c.Get("a"));
        c.Set("a", "3");
        Assert.Equal("a = \"3\"\n", c.ToString());
    }

    [Fact]
    public void Rejects_values_that_would_break_the_format()
    {
        var c = RetroArchConfig.Parse("");
        Assert.Throws<ArgumentException>(() => c.Set("a", "x\"y"));
        Assert.Throws<ArgumentException>(() => c.Set("a", "x\ny"));
        Assert.Throws<ArgumentException>(() => c.Set("a b", "x"));
    }

    [Fact]
    public void Comment_header_is_added_once()
    {
        var c = RetroArchConfig.Parse("a = \"1\"\n");
        Assert.True(c.EnsureComment("hello"));
        Assert.False(c.EnsureComment("hello"));
        Assert.Equal("# hello\na = \"1\"\n", c.ToString());
    }
}
