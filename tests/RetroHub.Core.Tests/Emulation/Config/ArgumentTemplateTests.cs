using RetroHub.Core.Catalog;
using RetroHub.Core.Emulation;
using Xunit;

namespace RetroHub.Core.Tests.Emulation.Config;

public sealed class ArgumentTemplateTests
{
    private static ArgumentContext Ctx(bool fullscreen = false, string[]? fs = null) => new()
    {
        Rom = @"D:\RH\games\gba\Pokémon Emerald (DE).gba",
        RomDir = @"D:\RH\games\gba",
        RomName = "Pokémon Emerald (DE)",
        Core = @"D:\RH\emulators\retroarch\cores\mgba_libretro.dll",
        EmuDir = @"D:\RH\emulators\retroarch",
        Root = @"D:\RH",
        Bios = @"D:\RH\bios",
        Saves = @"D:\RH\saves",
        SaveStates = @"D:\RH\savestates",
        Screenshots = @"D:\RH\screenshots",
        System = "gba",
        Config = @"D:\RH\data\config",
        RetroArchOverride = @"D:\RH\data\config\retroarch\retrohub.cfg",
        Fullscreen = fullscreen,
        FullscreenArgs = fs ?? ["-f"],
    };

    [Theory]
    [InlineData("{rom}", @"D:\RH\games\gba\Pokémon Emerald (DE).gba")]
    [InlineData("{romDir}", @"D:\RH\games\gba")]
    [InlineData("{romName}", "Pokémon Emerald (DE)")]
    [InlineData("{core}", @"D:\RH\emulators\retroarch\cores\mgba_libretro.dll")]
    [InlineData("{emuDir}", @"D:\RH\emulators\retroarch")]
    [InlineData("{root}", @"D:\RH")]
    [InlineData("{bios}", @"D:\RH\bios")]
    [InlineData("{saves}", @"D:\RH\saves")]
    [InlineData("{savestates}", @"D:\RH\savestates")]
    [InlineData("{screenshots}", @"D:\RH\screenshots")]
    [InlineData("{system}", "gba")]
    [InlineData("{config}", @"D:\RH\data\config")]
    [InlineData("{retroarchOverride}", @"D:\RH\data\config\retroarch\retrohub.cfg")]
    [InlineData("{ROM}", @"D:\RH\games\gba\Pokémon Emerald (DE).gba")] // names are case-insensitive
    [InlineData("-L{core}", @"-LD:\RH\emulators\retroarch\cores\mgba_libretro.dll")]
    [InlineData("--savedir={saves}/{system}", @"--savedir=D:\RH\saves/gba")]
    [InlineData("{unknown}", "{unknown}")]
    [InlineData("x{unknown}y{system}", "x{unknown}ygba")]
    [InlineData("{{system}", "{gba")]
    [InlineData("{system", "{system")]
    [InlineData("plain", "plain")]
    [InlineData("-batch", "-batch")]
    public void Expands_single_tokens(string token, string expected)
    {
        var result = ArgumentTemplate.Expand([token], Ctx());
        Assert.Equal([expected], result);
    }

    [Fact]
    public void One_token_is_one_argv_entry_even_with_spaces()
    {
        var result = ArgumentTemplate.Expand(["-L", "{core}", "{rom}"], Ctx());
        Assert.Equal(3, result.Count);
        Assert.Equal(@"D:\RH\games\gba\Pokémon Emerald (DE).gba", result[2]);
    }

    [Fact]
    public void Fullscreen_token_expands_only_in_fullscreen()
    {
        string[] tpl = ["{fullscreen}", "{rom}"];
        Assert.Equal(["-f", Ctx().Rom!], ArgumentTemplate.Expand(tpl, Ctx(fullscreen: true)));
        Assert.Equal([Ctx().Rom!], ArgumentTemplate.Expand(tpl, Ctx(fullscreen: false)));
    }

    [Fact]
    public void Fullscreen_args_are_each_their_own_entry_and_expanded()
    {
        var ctx = Ctx(fullscreen: true, fs: ["-C", "Dolphin.Display.Fullscreen=True", "--dir={bios}", "{fullscreen}"]);
        var result = ArgumentTemplate.Expand(["-b", "{fullscreen}", "-e", "{rom}"], ctx);
        Assert.Equal(["-b", "-C", "Dolphin.Display.Fullscreen=True", @"--dir=D:\RH\bios", "-e", ctx.Rom!], result);
    }

    [Fact]
    public void Fullscreen_must_be_the_whole_token()
    {
        // "{fullscreen}" inside a token is not a known placeholder: left as-is
        var result = ArgumentTemplate.Expand(["x{fullscreen}"], Ctx(fullscreen: true));
        Assert.Equal(["x{fullscreen}"], result);
    }

    [Fact]
    public void Placeholder_without_value_drops_the_token()
    {
        var ctx = Ctx() with { Core = null };
        Assert.Equal(["-L", "a"], ArgumentTemplate.Expand(["-L", "{core}", "a"], ctx));
        // an explicitly empty token stays
        Assert.Equal(["", "a"], ArgumentTemplate.Expand(["", "a"], ctx));
    }

    [Fact]
    public void Context_for_portable_layout()
    {
        var paths = new AppPaths(@"D:\RH", @"D:\RH\app");
        var ctx = ArgumentContext.For(paths, "snes", @"D:\RH\games\snes\a b.sfc", @"D:\RH\emulators\retroarch\retroarch.exe",
            @"D:\RH\emulators\retroarch\cores\snes9x_libretro.dll", LaunchMode.Fullscreen, ["-f"]);
        Assert.Equal(@"D:\RH\games\snes", ctx.RomDir);
        Assert.Equal("a b", ctx.RomName);
        Assert.Equal(@"D:\RH\emulators\retroarch", ctx.EmuDir);
        Assert.Equal(@"D:\RH\data\config", ctx.Config);
        Assert.Equal(@"D:\RH\data\config\retroarch\retrohub.cfg", ctx.RetroArchOverride);
        Assert.True(ctx.Fullscreen);
        Assert.False(ArgumentContext.For(paths, "snes", null, null, null, LaunchMode.Embedded, null).Fullscreen);
    }

    [Theory]
    [InlineData("", new string[0])]
    [InlineData("   ", new string[0])]
    [InlineData("-a -b", new[] { "-a", "-b" })]
    [InlineData("  -a    -b  ", new[] { "-a", "-b" })]
    [InlineData("\"C:\\Program Files\\x\" -w", new[] { @"C:\Program Files\x", "-w" })]
    [InlineData("a\"b c\"d", new[] { "ab cd" })]
    [InlineData("\"\"", new[] { "" })]
    [InlineData("a \"\" b", new[] { "a", "", "b" })]
    [InlineData("\\\"a", new[] { "\"a" })]                 // \" → literal quote
    [InlineData("\\\\\"a b\"", new[] { "\\a b" })]        // \\" → backslash + quote toggles
    [InlineData("C:\\dir\\ x", new[] { "C:\\dir\\", "x" })] // backslashes not before a quote are literal
    [InlineData("\"a\"\"b\"", new[] { "a\"b" })]           // "" inside quotes → literal quote
    [InlineData("-res=1920x1080\t-fs", new[] { "-res=1920x1080", "-fs" })]
    public void Splits_command_lines_like_windows(string input, string[] expected)
    {
        Assert.Equal(expected, ArgumentTemplate.SplitCommandLine(input));
    }
}
