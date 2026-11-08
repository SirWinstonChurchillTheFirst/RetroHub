using RetroHub.Core.Emulation.Windows;
using Xunit;

namespace RetroHub.Core.Tests.Emulation.Runtime;

public sealed class CommandLineTests
{
    public static TheoryData<string[]> Matrix => new()
    {
        new string[] { },
        new[] { "a" },
        new[] { "" },
        new[] { "", "", "" },
        new[] { "a b" },
        new[] { "  leading and trailing  " },
        new[] { "tab\there" },
        new[] { "new\nline" },
        new[] { "quote\"inside" },
        new[] { "\"" },
        new[] { "\"\"" },
        new[] { "\"quoted\"" },
        new[] { "\\" },
        new[] { "\\\\" },
        new[] { "a\\" },
        new[] { "a\\\\" },
        new[] { "a b\\" },
        new[] { "a b\\\\" },
        new[] { "a\\\"b" },
        new[] { "a\\\\\"b" },
        new[] { "a\\\\\\\"b c" },
        new[] { "\\\\server\\share\\" },
        new[] { "\\\\server\\share\\with space\\" },
        new[] { "C:\\Program Files\\Emu\\" },
        new[] { "C:\\Program Files\\Emu\\game.iso" },
        new[] { "D:\\RetroHub\\games\\gba\\240p Test Suite (Homebrew).gba" },
        new[] { "Pokémon – Edition Ü (Ä) 日本語", "ß" },
        new[] { "-c", "Dolphin.Display.Fullscreen=True", "-e", "x y.rvz" },
        new[] { "--appendconfig=a.cfg|b c.cfg" },
        new[] { "%PATH%", "^&|<>", "a&b" },
        new[] { "trailing\\\\\\", "\\\"", "\\\\\"\\\\" },
    };

    [Theory]
    [MemberData(nameof(Matrix))]
    public void RoundTripsThroughCommandLineToArgvW(string[] args)
    {
        const string exe = @"C:\Program Files\Some Emu\emu.exe";
        var line = CommandLine.Build(exe, args);
        var parsed = TestWin32.ParseCommandLine(line);
        Assert.Equal(exe, parsed[0]);
        Assert.Equal(args, parsed.Skip(1).ToArray());
    }

    [Theory]
    [InlineData(new[] { "a" }, "a")]
    [InlineData(new[] { "" }, "\"\"")]
    [InlineData(new[] { "a b" }, "\"a b\"")]
    [InlineData(new[] { "a\\" }, "a\\")]
    [InlineData(new[] { "a b\\" }, "\"a b\\\\\"")]
    [InlineData(new[] { "a\"b" }, "\"a\\\"b\"")]
    [InlineData(new[] { "a\\\"b" }, "\"a\\\\\\\"b\"")]
    [InlineData(new[] { "x", "y z", "" }, "x \"y z\" \"\"")]
    public void QuotesExactlyLikePasteArguments(string[] args, string expected)
    {
        Assert.Equal(expected, CommandLine.JoinArguments(args));
    }

    [Fact]
    public void ProgramNameIsAlwaysQuoted()
    {
        Assert.Equal("\"C:\\emu.exe\" -x", CommandLine.Build("C:\\emu.exe", ["-x"]));
        Assert.Equal("\"C:\\emu.exe\"", CommandLine.Build("\"C:\\emu.exe\"", []));
    }

    [Fact]
    public void EnvironmentBlockMergesOverridesSortedAndTerminated()
    {
        var block = ProcessLauncher.BuildEnvironmentBlock(new Dictionary<string, string>
        {
            ["RH_TEST_ZZ"] = "last",
            ["RH_TEST_AA"] = "first",
            ["PATH"] = "C:\\only",
            ["BAD=KEY"] = "x",
        });
        Assert.EndsWith("\0\0", block);
        var entries = block.TrimEnd('\0').Split('\0');
        Assert.Contains("RH_TEST_AA=first", entries);
        Assert.Contains("RH_TEST_ZZ=last", entries);
        Assert.Single(entries, e => e.StartsWith("PATH=", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("PATH=C:\\only", entries, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(entries, e => e.StartsWith("BAD", StringComparison.Ordinal));
        var keys = entries.Select(e => e[..e.IndexOf('=', 1)]).ToList();
        Assert.Equal(keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList(), keys);
        // an empty value removes the variable
        var removed = ProcessLauncher.BuildEnvironmentBlock(new Dictionary<string, string> { ["PATH"] = "" });
        Assert.DoesNotContain(removed.Split('\0'), e => e.StartsWith("PATH=", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ResolvesExecutablesFromSystemFolders()
    {
        var cmd = ProcessLauncher.ResolveExecutable("cmd", null);
        Assert.NotNull(cmd);
        Assert.True(string.Equals(Path.Combine(Environment.SystemDirectory, "cmd.exe"), cmd, StringComparison.OrdinalIgnoreCase), cmd);
        Assert.Null(ProcessLauncher.ResolveExecutable(@"C:\does\not\exist\emu.exe", null));
        Assert.Null(ProcessLauncher.ResolveExecutable("definitely-not-a-program-4711", null));
    }
}
