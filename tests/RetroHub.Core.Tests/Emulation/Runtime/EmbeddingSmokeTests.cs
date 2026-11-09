using System.Diagnostics;
using RetroHub.Core.Catalog;
using RetroHub.Core.Emulation;
using RetroHub.Core.Emulation.Windows;
using Xunit;

namespace RetroHub.Core.Tests.Emulation.Runtime;

/// <summary>
/// Real-window embedding tests. They open windows on the desktop, so they are marked Interactive
/// (filter with --filter "Category!=Interactive" to skip them).
/// </summary>
[Collection(RuntimeCollection.Name)]
public sealed class EmbeddingSmokeTests(ITestOutputHelper output)
{
    private const string MgbaFolder = @"D:\RetroHub\emulators\mgba\mGBA-0.10.5-win64";
    private const string Rom = @"D:\RetroHub\games\gba\240p Test Suite (Homebrew).gba";

    /// <summary>Embeds a window of our own process (no emulator needed): take, place, measure, release.</summary>
    [Fact]
    [Trait("Category", "Interactive")]
    public void OwnWindow_TakePlaceRelease()
    {
        using var hostWin = new TestWindowThread("RetroHub embed host", 800, 600);
        using var gameWin = new TestWindowThread("Fake emulator", 640, 480, withHost: false);
        using var embedder = new WindowEmbedder(new EmbedProfile { KeepFrame = false, OffUiThread = true, BarTop = 20 });
        var game = gameWin.Top;

        Assert.True(embedder.Take(game, hostWin.Host, 400, 300));
        Assert.Equal(hostWin.Host, TestWin32.GetParent(game));
        Assert.True(embedder.IsAttached);
        Assert.NotEqual(0, TestWin32.Style(game) & Native.WS_CHILD);
        Assert.Equal(0, TestWin32.Style(game) & Native.WS_CAPTION); // frame stripped (KeepFrame false)
        TestWin32.GetClientRect(game, out var rc);
        Assert.Equal((400, 300), (rc.Width, rc.Height));

        embedder.Place(10, 20, 500, 350);
        Assert.True(SpinUntil(() => TestWin32.GetClientRect(game, out var r) && r.Width == 500 && r.Height == 350, 3000));

        Assert.Equal("Fake emulator", embedder.Title);
        var bars = embedder.MeasureBars(); // no child windows → profile values (scaled by DPI)
        output.WriteLine($"bars {bars}");
        Assert.True(bars.Top >= 20);
        Assert.True(SpinUntil(embedder.IsResponsive, 3000));

        embedder.SetMasked(true);
        embedder.SetMasked(false);
        embedder.NudgeMoved();
        embedder.Focus();

        embedder.Release(toDesktop: true);
        Assert.False(embedder.IsAttached);
        Assert.Equal(IntPtr.Zero, TestWin32.GetParent(game));
        Assert.Equal(0, TestWin32.Style(game) & Native.WS_CHILD);
        Assert.NotEqual(0, TestWin32.Style(game) & Native.WS_CAPTION); // styles restored
    }

    /// <summary>
    /// mGBA (Qt) into a Win32 host: a COPY of the emulator folder and the ROM in a temp folder (mGBA has portable.ini,
    /// so it would write its config next to the exe – D:\RetroHub stays untouched).
    /// </summary>
    [Fact]
    [Trait("Category", "Interactive")]
    public async Task Mgba_IsEmbedded_Responsive_Released_AndClosed()
    {
        Assert.SkipUnless(File.Exists(Path.Combine(MgbaFolder, "mGBA.exe")) && File.Exists(Rom), "mGBA or the test ROM is not installed");
        var temp = Path.Combine(Path.GetTempPath(), "RetroHub-embedtest-" + Guid.NewGuid().ToString("N")[..8]);
        var emuDir = Path.Combine(temp, "mgba");
        CopyDirectory(MgbaFolder, emuDir);
        var romCopy = Path.Combine(temp, "rom", Path.GetFileName(Rom));
        Directory.CreateDirectory(Path.GetDirectoryName(romCopy)!);
        File.Copy(Rom, romCopy);
        var exe = Path.Combine(emuDir, "mGBA.exe");
        output.WriteLine($"temp copy: {temp}");

        var profile = new EmbedProfile { KeepFrame = true, OffUiThread = true, MaskWhileLoading = true };
        var repo = new FakeRepository();
        using var root = new TempRoot();
        var svc = new SessionService(repo, root.Paths, []);
        var plan = Runtime.Plan(exe, [romCopy], new EmulatorInstall { Def = new EmulatorDef { Id = "mgba", Name = "mGBA", Embed = profile }, ExePath = exe },
            LaunchMode.Embedded, workingDir: emuDir) with { Embed = profile };

        GameSession? session = null;
        try
        {
            using var win = new TestWindowThread("RetroHub embed test (mGBA)", 960, 720);
            output.WriteLine($"host {win.Host:X} {win.HostWidth}x{win.HostHeight}");
            WindowEmbedder.SetMasked(win.Host, true); // loading mask before the take, as the app does

            session = (GameSession)svc.Start(plan);
            Assert.Equal(SessionState.Running, session.State);
            output.WriteLine($"started pid {session.ProcessId}, job {session.TracksProcessTree}");

            var sw = Stopwatch.StartNew();
            nint game = 0;
            while (sw.Elapsed < TimeSpan.FromSeconds(30) && game == 0)
            {
                var found = session.FindWindows();
                if (found.Count > 0) game = found[0];
                else await Task.Delay(100, TestContext.Current.CancellationToken);
            }
            Assert.True(game != 0, "no mGBA window within 30 s");
            output.WriteLine($"window {game:X} after {sw.ElapsedMilliseconds} ms: class '{TestWin32.ClassName(game)}', title '{Native.GetTitle(game)}', iconic {Native.IsIconic(game)}");

            using var embedder = new WindowEmbedder(profile);
            var takeWatch = Stopwatch.StartNew();
            bool took = embedder.Take(game, win.Host, win.HostWidth, win.HostHeight);
            output.WriteLine($"Take returned {took} after {takeWatch.ElapsedMilliseconds} ms");
            Assert.True(took);
            Assert.True(await Runtime.WaitUntil(() => TestWin32.GetParent(game) == win.Host, TimeSpan.FromSeconds(5)), "GetParent(game) != host");
            Assert.True(embedder.IsAttached);

            // readiness as in MonHub: 5 answers in a row, > 1.5 s after the take, 30 s cap
            int answers = 0;
            var readyWatch = Stopwatch.StartNew();
            while (readyWatch.Elapsed < TimeSpan.FromSeconds(30) && !(answers >= 5 && readyWatch.Elapsed > TimeSpan.FromSeconds(1.5)))
            {
                answers = embedder.IsResponsive() ? answers + 1 : 0;
                await Task.Delay(150, TestContext.Current.CancellationToken);
            }
            output.WriteLine($"responsive: {answers} answers in a row after {readyWatch.ElapsedMilliseconds} ms");
            Assert.True(answers >= 5, "mGBA never became responsive");
            embedder.Settled();
            embedder.SetMasked(false);
            embedder.NudgeMoved();

            int w = win.HostWidth - 160, h = win.HostHeight - 120;
            embedder.Place(0, 0, w, h);
            bool placed = await Runtime.WaitUntil(() => TestWin32.GetClientRect(game, out var r) && r.Width == w && r.Height == h, TimeSpan.FromSeconds(5));
            TestWin32.GetClientRect(game, out var client);
            output.WriteLine($"placed {w}x{h}: client now {client.Width}x{client.Height} ({placed})");
            Assert.True(placed);

            await Task.Delay(1500, TestContext.Current.CancellationToken); // let it run the ROM a moment
            output.WriteLine($"title '{embedder.Title}', bars {embedder.MeasureBars()}");

            // look at the screen: is the emulator's picture drawn inside the host? (needs the test window in front)
            Foreground.BringToFront(win.Top);
            await Task.Delay(500, TestContext.Current.CancellationToken);
            output.WriteLine($"foreground is test window: {Native.GetForegroundWindow() == win.Top}");
            var shot = win.Invoke(() => ScreenCapture.Capture(win.Host));
            var png = Path.Combine(Path.GetTempPath(), "RetroHub-embed-capture.png");
            ScreenCapture.SavePng(png, shot.W, shot.H, shot.Bgra);
            output.WriteLine($"host capture {shot.W}x{shot.H}: {ScreenCapture.NonBlackFraction(shot.Bgra):P1} non-black pixels → {png}");
            embedder.Focus();

            embedder.Release(toDesktop: true);
            Assert.Equal(IntPtr.Zero, TestWin32.GetParent(game));
            Assert.Equal(0, TestWin32.Style(game) & Native.WS_CHILD);
            output.WriteLine("released to the desktop");

            var closeWatch = Stopwatch.StartNew();
            bool closed = await session.RequestCloseAsync(TimeSpan.FromSeconds(15));
            output.WriteLine($"RequestCloseAsync → {closed} after {closeWatch.ElapsedMilliseconds} ms");
            Assert.True(closed);
            Assert.Equal(SessionState.Exited, session.State);
            Assert.True(session.Process!.HasExited);
            output.WriteLine($"exit code {session.ExitCode}, elapsed {session.Elapsed}, recorded {repo.SessionsSnapshot().Length} session(s)");
        }
        finally
        {
            if (session is { State: SessionState.Running or SessionState.Starting })
            {
                output.WriteLine("cleanup: killing mGBA");
                session.Kill();
                await Runtime.WaitUntil(() => session.State == SessionState.Exited, TimeSpan.FromSeconds(5));
            }
            svc.Dispose();
            for (int i = 0; i < 10 && Directory.Exists(temp); i++)
            {
                try { Directory.Delete(temp, recursive: true); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Thread.Sleep(500); }
            }
        }
    }

    /// <summary>
    /// The usual way a game ends in embedded mode: RequestCloseAsync while the window is still inside RetroHub. As a
    /// child window it is no longer a top-level window (EnumWindows does not list it), yet it must get WM_CLOSE so the
    /// emulator can save and quit by itself instead of being killed.
    /// </summary>
    [Fact]
    [Trait("Category", "Interactive")]
    public async Task Mgba_RequestCloseWhileEmbedded_ClosesIt()
    {
        Assert.SkipUnless(File.Exists(Path.Combine(MgbaFolder, "mGBA.exe")) && File.Exists(Rom), "mGBA or the test ROM is not installed");
        var temp = Path.Combine(Path.GetTempPath(), "RetroHub-embedtest-" + Guid.NewGuid().ToString("N")[..8]);
        var emuDir = Path.Combine(temp, "mgba");
        CopyDirectory(MgbaFolder, emuDir);
        var romCopy = Path.Combine(temp, "rom", Path.GetFileName(Rom));
        Directory.CreateDirectory(Path.GetDirectoryName(romCopy)!);
        File.Copy(Rom, romCopy);
        var exe = Path.Combine(emuDir, "mGBA.exe");

        var profile = new EmbedProfile { KeepFrame = true, OffUiThread = true, MaskWhileLoading = true };
        var repo = new FakeRepository();
        using var root = new TempRoot();
        var svc = new SessionService(repo, root.Paths, []);
        var plan = Runtime.Plan(exe, [romCopy], new EmulatorInstall { Def = new EmulatorDef { Id = "mgba", Name = "mGBA", Embed = profile }, ExePath = exe },
            LaunchMode.Embedded, workingDir: emuDir) with { Embed = profile };

        GameSession? session = null;
        try
        {
            using var win = new TestWindowThread("RetroHub embed test (mGBA close)", 960, 720);
            session = (GameSession)svc.Start(plan);
            var sw = Stopwatch.StartNew();
            nint game = 0;
            while (sw.Elapsed < TimeSpan.FromSeconds(30) && game == 0)
            {
                var found = session.FindWindows();
                if (found.Count > 0) game = found[0];
                else await Task.Delay(100, TestContext.Current.CancellationToken);
            }
            Assert.True(game != 0, "no mGBA window within 30 s");

            using var embedder = new WindowEmbedder(profile);
            Assert.True(embedder.Take(game, win.Host, win.HostWidth, win.HostHeight));
            Assert.True(await Runtime.WaitUntil(() => embedder.IsAttached, TimeSpan.FromSeconds(5)), "not attached");
            int answers = 0;
            var readyWatch = Stopwatch.StartNew();
            while (readyWatch.Elapsed < TimeSpan.FromSeconds(30) && !(answers >= 5 && readyWatch.Elapsed > TimeSpan.FromSeconds(1.5)))
            {
                answers = embedder.IsResponsive() ? answers + 1 : 0;
                await Task.Delay(150, TestContext.Current.CancellationToken);
            }
            Assert.True(answers >= 5, "mGBA never became responsive");
            embedder.Settled();
            output.WriteLine($"embedded {game:X}; top-level windows found while embedded: {session.FindWindows().Count}");

            var closeWatch = Stopwatch.StartNew();
            bool closed = await session.RequestCloseAsync(TimeSpan.FromSeconds(15));
            output.WriteLine($"RequestCloseAsync while embedded → {closed} after {closeWatch.ElapsedMilliseconds} ms");
            Assert.True(closed, "mGBA did not close on RequestCloseAsync while embedded");
            Assert.Equal(SessionState.Exited, session.State);
            Assert.False(session.WasKilled);
            Assert.Equal(0, session.ExitCode);
            Assert.False(embedder.Exists);
            embedder.Release(toDesktop: false); // window is gone: must be a harmless no-op
        }
        finally
        {
            if (session is { State: SessionState.Running or SessionState.Starting })
            {
                output.WriteLine("cleanup: killing mGBA");
                session.Kill();
                await Runtime.WaitUntil(() => session.State == SessionState.Exited, TimeSpan.FromSeconds(5));
            }
            svc.Dispose();
            for (int i = 0; i < 10 && Directory.Exists(temp); i++)
            {
                try { Directory.Delete(temp, recursive: true); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Thread.Sleep(500); }
            }
        }
    }

    private static bool SpinUntil(Func<bool> condition, int timeoutMs)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (condition()) return true;
            Thread.Sleep(30);
        }
        return condition();
    }

    private static void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var dir in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, dir)));
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(to, Path.GetRelativePath(from, file)));
    }
}
