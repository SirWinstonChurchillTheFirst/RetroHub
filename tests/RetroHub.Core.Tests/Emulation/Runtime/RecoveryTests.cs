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
    public async Task ReattachedSession_Kill_AlsoKillsChildrenStartedBeforeTheRestart()
    {
        using var root = new TempRoot();
        var repo = new FakeRepository();
        using var svc = Runtime.Service(repo, root);
        // cmd waits for its child ping: the ping was started "before RetroHub restarted", so it is not in the new job
        using var cmd = Process.Start(new ProcessStartInfo(Runtime.Cmd, "/c \"ping -n 60 127.0.0.1 >nul\"") { UseShellExecute = false, CreateNoWindow = true })!;
        int pingPid = 0;
        try
        {
            Assert.True(await Runtime.WaitUntil(() => (pingPid = ChildPing(cmd.Id)) != 0, TimeSpan.FromSeconds(10)), "ping child did not start");
            var start = cmd.StartTime.ToUniversalTime();
            WriteSessionFile(svc, new { gameId = 10, pid = cmd.Id, exePath = Runtime.Cmd, startUtc = start, heartbeatUtc = start });
            svc.RecoverInterruptedSession();
            var current = Assert.IsType<GameSession>(svc.Current);
            Assert.True(current.Recovered);
            var ended = Runtime.EndedTask(current);

            current.Kill();
            await Runtime.WithTimeout(ended, TimeSpan.FromSeconds(10), "Ended after Kill");
            Assert.True(await Runtime.WaitUntil(() => !Runtime.IsAlive(pingPid), TimeSpan.FromSeconds(5)), $"child ping {pingPid} survived Kill");
        }
        finally
        {
            if (!cmd.HasExited) cmd.Kill(entireProcessTree: true);
            if (pingPid != 0 && Runtime.IsAlive(pingPid)) Process.GetProcessById(pingPid).Kill();
        }
    }

    private static int ChildPing(int parentPid)
    {
        foreach (var p in Process.GetProcessesByName("PING"))
        {
            using (p)
            {
                if (ParentOf(p.Id) == parentPid) return p.Id;
            }
        }
        return 0;
    }

    private static int ParentOf(int pid)
    {
        // WMI-free: NtQueryInformationProcess would need more interop; the toolhelp snapshot is enough here
        var snap = CreateToolhelp32Snapshot(0x2 /* TH32CS_SNAPPROCESS */, 0);
        try
        {
            var e = new PROCESSENTRY32W { dwSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<PROCESSENTRY32W>() };
            for (bool ok = Process32FirstW(snap, ref e); ok; ok = Process32NextW(snap, ref e))
                if (e.th32ProcessID == pid) return (int)e.th32ParentProcessID;
            return 0;
        }
        finally
        {
            CloseHandle(snap);
        }
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private struct PROCESSENTRY32W
    {
        public uint dwSize, cntUsage, th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID, cntThreads, th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile;
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] private static extern bool Process32FirstW(IntPtr snap, ref PROCESSENTRY32W e);
    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] private static extern bool Process32NextW(IntPtr snap, ref PROCESSENTRY32W e);
    [System.Runtime.InteropServices.DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);

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
