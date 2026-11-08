using System.Diagnostics;
using System.Text.Json;
using RetroHub.Core.Emulation;
using Xunit;

namespace RetroHub.Core.Tests.Emulation.Runtime;

[Collection(RuntimeCollection.Name)]
public sealed class RecoveryTests
{
    private static void WriteSessionFile(SessionService svc, object data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(svc.SessionFile)!);
        File.WriteAllText(svc.SessionFile, JsonSerializer.Serialize(data));
    }

    [Fact]
    public void DeadProcess_IsRecordedUntilLastHeartbeat()
    {
        using var root = new TempRoot();
        var repo = new FakeRepository();
        using var svc = Runtime.Service(repo, root);
        var start = new DateTime(2026, 11, 8, 20, 0, 0, DateTimeKind.Utc);
        var beat = start.AddMinutes(17).AddSeconds(30);
        // 999999 is not a valid pid (pids are multiples of 4) – the process is certainly gone
        WriteSessionFile(svc, new { gameId = 7, emulatorId = "mgba", pid = 999999, exePath = @"C:\nowhere\mgba.exe", startUtc = start, heartbeatUtc = beat });

        int started = 0;
        svc.SessionStarted += _ => started++;
        svc.RecoverInterruptedSession();

        var rec = Assert.Single(repo.SessionsSnapshot());
        Assert.Equal(7, rec.GameId);
        Assert.Equal("mgba", rec.EmulatorId);
        Assert.Equal(start, rec.StartUtc);
        Assert.Equal(beat, rec.EndUtc);
        Assert.Equal(17 * 60 + 30, rec.Seconds);
        Assert.Null(rec.ExitCode);
        Assert.False(File.Exists(svc.SessionFile));
        Assert.Null(svc.Current);
        Assert.Equal(0, started);
    }

    [Fact]
    public void LivePidWithOtherExe_IsNotReattached()
    {
        // pid reuse: the pid runs, but it is a different program (this test host)
        using var root = new TempRoot();
        var repo = new FakeRepository();
        using var svc = Runtime.Service(repo, root);
        var start = DateTime.UtcNow.AddMinutes(-3);
        WriteSessionFile(svc, new { gameId = 8, pid = Environment.ProcessId, exePath = @"C:\nowhere\emu.exe", startUtc = start, heartbeatUtc = start.AddSeconds(90) });
        svc.RecoverInterruptedSession();
        Assert.Null(svc.Current);
        Assert.Equal(90, Assert.Single(repo.SessionsSnapshot()).Seconds);
    }

    [Fact]
    public async Task RunningProcess_IsReattached_AndRecordedWhenItEnds()
    {
        using var root = new TempRoot();
        var repo = new FakeRepository();
        using var svc = Runtime.Service(repo, root);
        using var ping = Process.Start(new ProcessStartInfo(Runtime.Ping, "-n 4 127.0.0.1") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!;
        try
        {
            var start = ping.StartTime.ToUniversalTime();
            WriteSessionFile(svc, new { gameId = 9, emulatorId = "pingemu", pid = ping.Id, exePath = Runtime.Ping, startUtc = start, heartbeatUtc = start, mode = "Embedded" });
            IGameSession? startedSession = null;
            svc.SessionStarted += s => startedSession = s;

            svc.RecoverInterruptedSession();

            var current = Assert.IsType<GameSession>(svc.Current);
            Assert.Same(current, startedSession);
            Assert.True(current.Recovered);
            Assert.Equal(ping.Id, current.ProcessId);
            Assert.Equal(start, current.StartedUtc);
            Assert.Equal(RetroHub.Core.Catalog.LaunchMode.Embedded, current.Plan.Mode);
            Assert.True(File.Exists(svc.SessionFile)); // re-attached sessions keep crash protection

            await Runtime.WithTimeout(Runtime.EndedTask(current), TimeSpan.FromSeconds(20), "Ended");
            var rec = Assert.Single(repo.SessionsSnapshot());
            Assert.Equal(9, rec.GameId);
            Assert.Equal("pingemu", rec.EmulatorId);
            Assert.Equal(start, rec.StartUtc);
            Assert.InRange(rec.Seconds, 2, 5);
            Assert.Equal(0, rec.ExitCode);
            Assert.Null(svc.Current);
            Assert.False(File.Exists(svc.SessionFile));
        }
        finally
        {
            if (!ping.HasExited) ping.Kill();
        }
    }

    [Fact]
    public void CorruptOrMissingFile_NeverThrows()
    {
        using var root = new TempRoot();
        var repo = new FakeRepository();
        using var svc = Runtime.Service(repo, root);
        svc.RecoverInterruptedSession(); // no file
        File.WriteAllText(svc.SessionFile, "{ not json");
        svc.RecoverInterruptedSession();
        Assert.False(File.Exists(svc.SessionFile));
        Assert.Empty(repo.SessionsSnapshot());
    }
}
