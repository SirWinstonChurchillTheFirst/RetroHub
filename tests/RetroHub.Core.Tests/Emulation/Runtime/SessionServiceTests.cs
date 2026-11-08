using System.Diagnostics;
using RetroHub.Core.Catalog;
using RetroHub.Core.Emulation;
using Xunit;

namespace RetroHub.Core.Tests.Emulation.Runtime;

[Collection(RuntimeCollection.Name)]
public sealed class SessionServiceTests
{
    private static readonly TimeSpan EndTimeout = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task PingSession_EndsOnce_RecordsPlaytimeAndExitCode()
    {
        using var root = new TempRoot();
        var repo = new FakeRepository();
        using var svc = Runtime.Service(repo, root);
        int started = 0, ended = 0;
        svc.SessionStarted += _ => Interlocked.Increment(ref started);
        svc.SessionEnded += _ => Interlocked.Increment(ref ended);

        var session = (GameSession)svc.Start(Runtime.Plan(Runtime.Cmd, ["/c", "ping -n 3 127.0.0.1 >nul"]));
        var endedTask = Runtime.EndedTask(session);
        Assert.Equal(SessionState.Running, session.State);
        Assert.Same(session, svc.Current);
        Assert.True(session.TracksProcessTree);
        Assert.True(File.Exists(svc.SessionFile));

        await Runtime.WithTimeout(endedTask, EndTimeout, "Ended");
        await Runtime.WaitUntil(() => Volatile.Read(ref ended) == 1, TimeSpan.FromSeconds(2));

        Assert.Equal(SessionState.Exited, session.State);
        Assert.Equal(0, session.ExitCode);
        Assert.Null(svc.Current);
        Assert.Equal(1, started);
        Assert.Equal(1, ended);
        Assert.False(File.Exists(svc.SessionFile));
        var rec = Assert.Single(repo.SessionsSnapshot());
        Assert.Equal(42, rec.GameId);
        Assert.InRange(rec.Seconds, 2, 3);
        Assert.Equal(0, rec.ExitCode);
        Assert.Equal(session.StartedUtc, rec.StartUtc);
        Assert.InRange((rec.EndUtc - rec.StartUtc).TotalSeconds, 1.5, 3.5);
        // a late subscriber still gets Ended (immediately)
        int late = 0;
        session.Ended += _ => late++;
        Assert.Equal(1, late);
    }

    [Fact]
    public async Task JobKeepsSessionAliveWhileChildRuns()
    {
        using var root = new TempRoot();
        var repo = new FakeRepository();
        using var svc = Runtime.Service(repo, root);
        // cmd starts ping in the background and exits at once; ping keeps running ~3 s
        var session = (GameSession)svc.Start(Runtime.Plan(Runtime.Cmd, ["/c", "start", "", "/b", "ping", "-n", "4", "127.0.0.1", ">nul"]));
        var endedTask = Runtime.EndedTask(session);
        Assert.True(session.TracksProcessTree);

        Assert.True(await Runtime.WaitUntil(() => session.Process!.HasExited, TimeSpan.FromSeconds(10)), "cmd.exe did not exit");
        var pids = session.ProcessIds();
        Assert.Equal(SessionState.Running, session.State);
        Assert.NotEmpty(pids);
        Assert.DoesNotContain(session.ProcessId, pids);
        Assert.Contains(pids, p => ProcessName(p) == "PING");

        // still running a second later (the main process is long gone)
        await Task.Delay(1000, TestContext.Current.CancellationToken);
        Assert.Equal(SessionState.Running, session.State);
        Assert.False(endedTask.IsCompleted);

        await Runtime.WithTimeout(endedTask, EndTimeout, "Ended");
        Assert.True(session.Elapsed >= TimeSpan.FromSeconds(2.5), $"ended after {session.Elapsed}");
        var rec = Assert.Single(repo.SessionsSnapshot());
        Assert.InRange(rec.Seconds, 3, 4);
        Assert.Equal(0, rec.ExitCode); // exit code of the main process (cmd)
    }

    [Fact]
    public async Task Kill_KillsWholeTree()
    {
        using var root = new TempRoot();
        var repo = new FakeRepository();
        using var svc = Runtime.Service(repo, root);
        var session = (GameSession)svc.Start(Runtime.Plan(Runtime.Cmd, ["/c", "start", "", "/b", "ping", "-n", "60", "127.0.0.1", ">nul"]));
        var endedTask = Runtime.EndedTask(session);
        IReadOnlyList<int> pids = [];
        Assert.True(await Runtime.WaitUntil(() => (pids = session.ProcessIds()).Any(p => ProcessName(p) == "PING"), TimeSpan.FromSeconds(10)));
        await Task.Delay(1200, TestContext.Current.CancellationToken); // so the session is long enough to be recorded

        session.Kill();
        await Runtime.WithTimeout(endedTask, TimeSpan.FromSeconds(5), "Ended after Kill");

        Assert.True(session.WasKilled);
        foreach (var pid in pids) Assert.False(Runtime.IsAlive(pid), $"pid {pid} survived Kill");
        var rec = Assert.Single(repo.SessionsSnapshot());
        Assert.Null(rec.ExitCode);
    }

    [Fact]
    public async Task RequestClose_WithoutWindows_ReturnsFalseWithinTimeout()
    {
        using var root = new TempRoot();
        var repo = new FakeRepository();
        using var svc = Runtime.Service(repo, root);
        var session = svc.Start(Runtime.Plan(Runtime.Cmd, ["/c", "ping -n 60 127.0.0.1 >nul"]));
        var endedTask = Runtime.EndedTask(session);
        try
        {
            Assert.Empty(session.FindWindows());
            var sw = Stopwatch.StartNew();
            bool closed = await session.RequestCloseAsync(TimeSpan.FromSeconds(1.5));
            sw.Stop();
            Assert.False(closed);
            Assert.InRange(sw.Elapsed.TotalSeconds, 1.3, 4);
            Assert.Equal(SessionState.Running, session.State);
        }
        finally
        {
            session.Kill();
            await Runtime.WithTimeout(endedTask, TimeSpan.FromSeconds(5), "Ended after Kill");
        }
        Assert.True(await session.RequestCloseAsync(TimeSpan.FromSeconds(1))); // already ended → true at once
    }

    [Fact]
    public void Start_Throws_WhenPlanCannotLaunch()
    {
        using var root = new TempRoot();
        using var svc = Runtime.Service(new FakeRepository(), root);
        var noFile = Runtime.Plan(Runtime.Cmd, []) with { FileName = null };
        Assert.Throws<InvalidOperationException>(() => svc.Start(noFile));
        var blocked = Runtime.Plan(Runtime.Cmd, [], problems: [new LaunchProblem(LaunchProblemKind.BiosMissing, "BIOS fehlt.", Blocking: true)]);
        var ex = Assert.Throws<InvalidOperationException>(() => svc.Start(blocked));
        Assert.Contains("BIOS fehlt.", ex.Message);
        Assert.Null(svc.Current);
    }

    [Fact]
    public async Task Start_Throws_WhileASessionRuns()
    {
        using var root = new TempRoot();
        using var svc = Runtime.Service(new FakeRepository(), root);
        var session = svc.Start(Runtime.Plan(Runtime.Cmd, ["/c", "ping -n 30 127.0.0.1 >nul"]));
        var endedTask = Runtime.EndedTask(session);
        try
        {
            var ex = Assert.Throws<InvalidOperationException>(() => svc.Start(Runtime.Plan(Runtime.Cmd, ["/c", "exit"])));
            Assert.Contains("läuft bereits", ex.Message);
        }
        finally
        {
            session.Kill();
            await Runtime.WithTimeout(endedTask, TimeSpan.FromSeconds(5), "Ended after Kill");
        }
    }

    [Fact]
    public void Start_Throws_WhenSameEmulatorExeIsAlreadyRunning()
    {
        using var root = new TempRoot();
        using var svc = Runtime.Service(new FakeRepository(), root);
        using var ping = Process.Start(new ProcessStartInfo(Runtime.Ping, "-n 30 127.0.0.1") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!;
        try
        {
            var emu = new EmulatorInstall { Def = new EmulatorDef { Id = "pingemu", Name = "PingEmu" }, ExePath = Runtime.Ping };
            var ex = Assert.Throws<InvalidOperationException>(() => svc.Start(Runtime.Plan(Runtime.Ping, ["-n", "1", "127.0.0.1"], emu)));
            Assert.Contains("PingEmu läuft bereits", ex.Message);
            Assert.Null(svc.Current);
            Assert.True(SessionService.IsProgramRunning(Runtime.Ping));
        }
        finally
        {
            ping.Kill();
        }
        Assert.False(SessionService.IsProgramRunning(@"C:\nowhere\nothing.exe"));
    }

    [Fact]
    public async Task PortableRecipe_Runs_AndItsFailureIsNotFatal()
    {
        using var root = new TempRoot();
        var repo = new FakeRepository();
        var good = new FakePortableSetup("goodrecipe");
        var bad = new FakePortableSetup("badrecipe", fail: true);
        var other = new FakePortableSetup("unrelated");
        using var svc = Runtime.Service(repo, root, good, bad, other);
        // ExePath is a (non-running) path of its own, so the already-running guard does not trip over other cmd.exe
        var fakeExe = Path.Combine(root.Dir, "emu.exe");

        var emuGood = new EmulatorInstall { Def = new EmulatorDef { Id = "e1", Name = "E1", Portable = "goodrecipe" }, ExePath = fakeExe };
        var s1 = svc.Start(Runtime.Plan(Runtime.Cmd, ["/c", "exit 0"], emuGood));
        await Runtime.WithTimeout(Runtime.EndedTask(s1), EndTimeout, "Ended");
        Assert.Equal(1, good.Calls);

        var emuBad = new EmulatorInstall { Def = new EmulatorDef { Id = "e2", Name = "E2", Portable = "BADRECIPE" }, ExePath = fakeExe };
        var s2 = svc.Start(Runtime.Plan(Runtime.Cmd, ["/c", "exit 5"], emuBad));
        await Runtime.WithTimeout(Runtime.EndedTask(s2), EndTimeout, "Ended");
        Assert.Equal(1, bad.Calls);
        Assert.Equal(SessionState.Exited, s2.State);
        Assert.Equal(5, s2.ExitCode);
        Assert.Equal(0, other.Calls);
    }

    [Fact]
    public async Task EnvironmentOverridesReachTheProcess()
    {
        using var root = new TempRoot();
        using var svc = Runtime.Service(new FakeRepository(), root);
        // (cmd.exe has its own quote rules, so the test value avoids quotes and spaces)
        string[] args = ["/c", "if %RH_RUNTIME_TEST%==hello (exit 7) else (exit 3)"];

        var with = svc.Start(Runtime.Plan(Runtime.Cmd, args, env: new Dictionary<string, string> { ["RH_RUNTIME_TEST"] = "hello" }));
        await Runtime.WithTimeout(Runtime.EndedTask(with), EndTimeout, "Ended");
        Assert.Equal(7, with.ExitCode);

        var without = svc.Start(Runtime.Plan(Runtime.Cmd, args));
        await Runtime.WithTimeout(Runtime.EndedTask(without), EndTimeout, "Ended");
        Assert.Equal(3, without.ExitCode);
    }

    [Fact]
    public async Task ArgumentsWithSpacesAndQuotesArriveIntact()
    {
        using var root = new TempRoot();
        using var svc = Runtime.Service(new FakeRepository(), root);
        // findstr exits 0 only if it finds the text in the file – both are single argv entries with spaces
        var file = Path.Combine(root.Dir, "a file with spaces.txt");
        File.WriteAllText(file, "needle in a haystack");
        var s = svc.Start(Runtime.Plan(Path.Combine(Environment.SystemDirectory, "findstr.exe"), ["/c:needle in a", file]));
        await Runtime.WithTimeout(Runtime.EndedTask(s), EndTimeout, "Ended");
        Assert.Equal(0, s.ExitCode);
    }

    [Fact]
    public async Task ShellExecutePlan_IsDetached_RecordsLaunchOnly()
    {
        using var root = new TempRoot();
        var repo = new FakeRepository();
        using var svc = Runtime.Service(repo, root);
        int started = 0, ended = 0;
        svc.SessionStarted += _ => started++;
        svc.SessionEnded += _ => ended++;

        var s = (GameSession)svc.Start(Runtime.Plan(Runtime.Cmd, ["/c", "exit 0"], shell: true));
        Assert.True(s.Detached);
        Assert.Equal(SessionState.Exited, s.State);
        Assert.Equal(TimeSpan.Zero, s.Elapsed);
        Assert.Null(svc.Current);
        Assert.Equal(1, started);
        Assert.Equal(1, ended);
        var rec = Assert.Single(repo.SessionsSnapshot());
        Assert.Equal(0, rec.Seconds);
        Assert.Null(rec.ExitCode);
        Assert.False(File.Exists(svc.SessionFile));
        Assert.True(await s.RequestCloseAsync(TimeSpan.FromSeconds(1)));
        s.Process?.WaitForExit(5000);
    }

    [Fact]
    public void MissingProgram_GivesFailedSession_NoEvents()
    {
        using var root = new TempRoot();
        var repo = new FakeRepository();
        using var svc = Runtime.Service(repo, root);
        int events = 0;
        svc.SessionStarted += _ => events++;
        svc.SessionEnded += _ => events++;
        var s = svc.Start(Runtime.Plan(@"C:\does\not\exist\emu.exe", ["x"]));
        Assert.Equal(SessionState.Failed, s.State);
        Assert.Contains("nicht gefunden", s.Error);
        Assert.Null(svc.Current);
        Assert.Equal(0, events);
        Assert.Empty(repo.SessionsSnapshot());
        Assert.False(File.Exists(svc.SessionFile));
    }

    [Fact]
    public async Task ThrowingHandlers_DoNotBreakTracking()
    {
        using var root = new TempRoot();
        var repo = new FakeRepository();
        using var svc = Runtime.Service(repo, root);
        var serviceEnded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        svc.SessionStarted += _ => throw new InvalidOperationException("boom (started)");
        svc.SessionEnded += _ => throw new InvalidOperationException("boom (service ended)");
        svc.SessionEnded += _ => serviceEnded.TrySetResult();
        var s = svc.Start(Runtime.Plan(Runtime.Cmd, ["/c", "ping -n 2 127.0.0.1 >nul"]));
        s.Ended += _ => throw new InvalidOperationException("boom (ended)");
        var endedTask = Runtime.EndedTask(s);
        await Runtime.WithTimeout(endedTask, EndTimeout, "Ended");
        await Runtime.WithTimeout(serviceEnded.Task.ContinueWith(_ => 0), EndTimeout, "SessionEnded");
        Assert.Single(repo.SessionsSnapshot());
        Assert.Null(svc.Current);
    }

    [Fact]
    public async Task Heartbeat_RefreshesSessionFile()
    {
        using var root = new TempRoot();
        using var svc = Runtime.Service(new FakeRepository(), root);
        svc.HeartbeatInterval = TimeSpan.FromMilliseconds(300);
        var s = (GameSession)svc.Start(Runtime.Plan(Runtime.Cmd, ["/c", "ping -n 3 127.0.0.1 >nul"]));
        var endedTask = Runtime.EndedTask(s);
        await Task.Delay(1200, TestContext.Current.CancellationToken);
        var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(svc.SessionFile)).RootElement;
        Assert.Equal(42, json.GetProperty("gameId").GetInt64());
        Assert.Equal(s.ProcessId, json.GetProperty("pid").GetInt32());
        Assert.Equal(Runtime.Cmd, json.GetProperty("exePath").GetString(), StringComparer.OrdinalIgnoreCase);
        var start = json.GetProperty("startUtc").GetDateTime();
        var beat = json.GetProperty("heartbeatUtc").GetDateTime();
        Assert.True(beat - start >= TimeSpan.FromMilliseconds(500), $"heartbeat {beat:O} vs start {start:O}");
        await Runtime.WithTimeout(endedTask, EndTimeout, "Ended");
        Assert.False(File.Exists(svc.SessionFile));
    }

    [Fact]
    public async Task EmbeddedMode_StartsMinimizedWithoutActivation()
    {
        // a console program in embedded mode must still start and end normally (SW_SHOWMINNOACTIVE path)
        using var root = new TempRoot();
        using var svc = Runtime.Service(new FakeRepository(), root);
        var s = svc.Start(Runtime.Plan(Runtime.Cmd, ["/c", "exit 0"], mode: LaunchMode.Embedded));
        await Runtime.WithTimeout(Runtime.EndedTask(s), EndTimeout, "Ended");
        Assert.Equal(0, s.ExitCode);
    }

    [Fact]
    public async Task InstantExit_StartedAlwaysBeforeEnded()
    {
        using var root = new TempRoot();
        using var svc = Runtime.Service(new FakeRepository(), root);
        for (int i = 0; i < 5; i++)
        {
            var order = new System.Collections.Concurrent.ConcurrentQueue<string>();
            var done = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnStarted(IGameSession _) { order.Enqueue("started"); Thread.Sleep(300); } // slow handler: the process is long gone
            void OnEnded(IGameSession _) { order.Enqueue("ended"); done.TrySetResult(0); }
            svc.SessionStarted += OnStarted;
            svc.SessionEnded += OnEnded;
            svc.Start(Runtime.Plan(Runtime.Cmd, ["/c", "exit 0"]));
            await Runtime.WithTimeout(done.Task, EndTimeout, "SessionEnded");
            svc.SessionStarted -= OnStarted;
            svc.SessionEnded -= OnEnded;
            Assert.Equal(["started", "ended"], order.ToArray());
            Assert.Null(svc.Current);
        }
    }

    private static string ProcessName(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return p.ProcessName.ToUpperInvariant();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return "";
        }
    }
}
