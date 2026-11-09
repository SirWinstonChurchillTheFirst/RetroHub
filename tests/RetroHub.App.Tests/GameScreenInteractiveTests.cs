using System.Diagnostics;
using System.Text.Json;
using RetroHub.Core.Data;
using RetroHub.Core.Library;
using Xunit;

namespace RetroHub.App.Tests;

/// <summary>
/// Real desktop: starts the built RetroHub.App (its own process – the headless test platform cannot host a native
/// window) over a throw-away portable root with a copy of mGBA and the GBA homebrew ROM, launches the ROM from the
/// command line in Embedded mode and reads the dev-only test report (see docs/TESTS-SPIELSTART.md). Covers the CLI
/// add path (file outside the library → system detected → added), the real GameScreen state machine (window found,
/// taken, ready, a picture inside the host), "Spiel beenden" through the normal command, the return and the recorded
/// play time. Needs a desktop session and D:\RetroHub's test resources (or RETROHUB_TEST_RESOURCES).
/// </summary>
[Trait("Category", "Interactive")]
public class GameScreenInteractiveTests
{
    private static string Resources => Environment.GetEnvironmentVariable("RETROHUB_TEST_RESOURCES") is { Length: > 0 } r ? r : @"D:\RetroHub";

    private static string? AppExe()
    {
        // same configuration as this test build (bin\<Config>\net10.0\win-x64)
        var config = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)).Parent?.Parent?.Name ?? "Debug";
        var exe = Path.Combine(TestSupport.RepoRoot, "src", "RetroHub.App", "bin", config, "net10.0", "win-x64", "RetroHub.App.exe");
        return File.Exists(exe) ? exe : null;
    }

    private static void CopyDir(string from, string to)
    {
        foreach (var dir in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, dir)));
        Directory.CreateDirectory(to);
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(to, Path.GetRelativePath(from, file)));
    }

    [Fact]
    public void Mgba_runs_embedded_in_the_real_game_screen_and_returns()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Windows only");
        if (!RetroHub.App.Services.TestHooks.Allowed) Assert.Skip("test hooks only exist in DEBUG builds");
        var exe = AppExe() ?? throw new InvalidOperationException("RetroHub.App.exe not built");
        var mgba = Path.Combine(Resources, "emulators", "mgba", "mGBA-0.10.5-win64");
        var rom = Path.Combine(Resources, "games", "gba", "240p Test Suite (Homebrew).gba");
        if (!Directory.Exists(mgba) || !File.Exists(rom)) Assert.Skip($"test resources missing below {Resources}");

        var root = Path.Combine(Path.GetTempPath(), "retrohub-e2e-" + Guid.NewGuid().ToString("N")[..8]);
        Process? app = null;
        try
        {
            CopyDir(mgba, Path.Combine(root, "emulators", "mgba", "mGBA-0.10.5-win64"));
            // outside every library folder: the CLI has to detect the system and add the file itself
            var incoming = Path.Combine(root, "incoming");
            Directory.CreateDirectory(incoming);
            var romCopy = Path.Combine(incoming, Path.GetFileName(rom));
            File.Copy(rom, romCopy);
            var report = Path.Combine(root, "report.jsonl");

            var psi = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = root };
            psi.Environment["RETROHUB_ROOT"] = root;
            foreach (var a in new[] { "--launch", romCopy, "--launch-mode", "embedded", "--test-quit-after", "9", "--test-exit-after-return", "2", "--test-report", report })
                psi.ArgumentList.Add(a);
            app = Process.Start(psi)!;
            Assert.True(app.WaitForExit(90_000), "RetroHub did not finish within 90 s");
            Assert.Equal(0, app.ExitCode);

            var events = File.ReadAllLines(report).Select(l => JsonDocument.Parse(l).RootElement).ToList();
            JsonElement Event(string name) => events.FirstOrDefault(e => e.GetProperty("event").GetString() == name) is { ValueKind: JsonValueKind.Object } e
                ? e : throw new Xunit.Sdk.XunitException($"no '{name}' in the report:\n{string.Join("\n", events)}");
            TestContext.Current.SendDiagnosticMessage(string.Join("\n", events));

            Assert.True(Event("cli-resolved").GetProperty("added").GetBoolean());
            Assert.Equal("Embedded", Event("plan").GetProperty("mode").GetString());
            var taken = Event("embed-taken");
            Assert.True(taken.GetProperty("sinceAttachMs").GetInt64() < 30_000);
            Event("embed-ready");
            var picture = Event("embed-picture");
            Assert.True(picture.GetProperty("parentIsHost").GetBoolean(), "the emulator window is not a child of the host");
            Assert.True(picture.GetProperty("printWindowNonBlack").GetDouble() > 0.5, "no picture inside the host");
            Assert.True(picture.GetProperty("printWindowColors").GetInt32() >= 5, "the host shows a flat colour, not the game");
            Assert.Equal(0, Event("session-ended").GetProperty("exitCode").GetInt32());
            Assert.True(Event("close-result").GetProperty("closed").GetBoolean(), "mGBA did not close on WM_CLOSE");
            Assert.True(Event("test-exit").GetProperty("visible").GetBoolean(), "RetroHub was not visible after the game");

            // the play time is in the library
            using var repo = new SqliteGameRepository(Path.Combine(root, "data", "database", "retrohub.db"));
            var game = repo.Query(new GameQuery()).Single(g => g.SystemId == "gba");
            Assert.Equal(1, game.PlayCount);
            Assert.InRange(game.PlaySeconds, 5, 60);
            Assert.Equal("mgba", repo.Sessions(game.Id).Single().EmulatorId);
        }
        finally
        {
            if (app is { HasExited: false }) app.Kill(entireProcessTree: true);
            foreach (var p in Process.GetProcessesByName("mGBA"))
            {
                try { if (p.MainModule?.FileName.StartsWith(root, StringComparison.OrdinalIgnoreCase) == true) p.Kill(); }
                catch (Exception) { /* not ours / gone */ }
            }
            for (int i = 0; i < 10 && Directory.Exists(root); i++)
            {
                try { Directory.Delete(root, true); }
                catch (IOException) { Thread.Sleep(300); }
                catch (UnauthorizedAccessException) { Thread.Sleep(300); }
            }
        }
    }
}
