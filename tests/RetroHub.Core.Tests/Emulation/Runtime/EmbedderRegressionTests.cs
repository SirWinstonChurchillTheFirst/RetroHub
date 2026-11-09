using System.Diagnostics;
using RetroHub.Core.Catalog;
using RetroHub.Core.Emulation.Windows;
using Xunit;

namespace RetroHub.Core.Tests.Emulation.Runtime;

/// <summary>
/// Regression tests for WindowEmbedder state handling, with windows of this process standing in for an emulator
/// (each on its own thread, so a "busy emulator" is simulated by making that thread sleep). They open windows on the
/// desktop, hence Interactive.
/// </summary>
[Collection(RuntimeCollection.Name)]
public sealed class EmbedderRegressionTests
{
    private static readonly EmbedProfile Frameless = new() { KeepFrame = false, OffUiThread = true };

    /// <summary>Taking the same window a second time must not overwrite the saved original styles with the child ones.</summary>
    [Fact]
    [Trait("Category", "Interactive")]
    public void TakingTheSameWindowTwice_KeepsTheOriginalStylesForRelease()
    {
        using var hostWin = new TestWindowThread("RetroHub host A", 800, 600);
        using var hostWin2 = new TestWindowThread("RetroHub host B", 800, 600);
        using var gameWin = new TestWindowThread("Fake emulator", 640, 480, withHost: false);
        using var embedder = new WindowEmbedder(Frameless);
        var game = gameWin.Top;
        long original = TestWin32.Style(game);
        Assert.NotEqual(0, original & Native.WS_CAPTION);

        Assert.True(embedder.Take(game, hostWin.Host, 400, 300));
        Assert.True(embedder.IsAttached);
        // again into the same host (e.g. the UI re-runs its take), then into another host
        Assert.True(embedder.Take(game, hostWin.Host, 420, 310));
        Assert.True(embedder.Take(game, hostWin2.Host, 430, 320));
        Assert.Equal(hostWin2.Host, TestWin32.GetParent(game));

        // the old host goes away (the UI rebuilt its view): the game stays in the new host
        hostWin.Invoke(() => { WindowEmbedder.DestroyHostWindow(hostWin.Host); return 0; });
        Assert.Equal(hostWin2.Host, TestWin32.GetParent(game));
        Assert.True(embedder.IsAttached);

        embedder.Release(toDesktop: true);
        Assert.Equal(IntPtr.Zero, TestWin32.GetParent(game));
        long restored = TestWin32.Style(game);
        Assert.Equal(0, restored & Native.WS_CHILD);
        Assert.NotEqual(0, restored & Native.WS_CAPTION);
        Assert.NotEqual(0, restored & Native.WS_THICKFRAME);
    }

    /// <summary>
    /// A busy emulator: Take returns after 300 ms while the take still runs on the worker. A Release (or Dispose) right
    /// then must still give the window back once the take has finished – it must never stay stuck inside the host.
    /// </summary>
    [Fact]
    [Trait("Category", "Interactive")]
    public void ReleaseWhileTakeIsStillRunning_GivesTheWindowBack()
    {
        using var hostWin = new TestWindowThread("RetroHub host", 800, 600);
        using var gameWin = new TestWindowThread("Busy emulator", 640, 480, withHost: false);
        using var embedder = new WindowEmbedder(Frameless);
        var game = gameWin.Top;

        gameWin.Post(() => Thread.Sleep(1500)); // the "emulator" does not answer for 1.5 s
        Thread.Sleep(100);
        var sw = Stopwatch.StartNew();
        Assert.True(embedder.Take(game, hostWin.Host, 400, 300)); // still running → true after ~300 ms
        Assert.InRange(sw.ElapsedMilliseconds, 250, 1200);

        embedder.Release(toDesktop: false);

        // wait until the "emulator" answers again and the worker has run everything it had queued
        Thread.Sleep(Math.Max(0, 2500 - (int)sw.ElapsedMilliseconds));
        Assert.True(SpinUntil(() => TestWin32.GetParent(game) == IntPtr.Zero && (TestWin32.Style(game) & Native.WS_CHILD) == 0, 3000),
            $"window still has parent {TestWin32.GetParent(game):X}, style {TestWin32.Style(game):X}");
        Thread.Sleep(300); // nothing may put it back afterwards
        Assert.Equal(IntPtr.Zero, TestWin32.GetParent(game));
        Assert.False(embedder.IsAttached);
        Assert.NotEqual(0, TestWin32.Style(game) & Native.WS_CAPTION);
    }

    /// <summary>Same race through Dispose (the game screen closes while the emulator is still busy).</summary>
    [Fact]
    [Trait("Category", "Interactive")]
    public void DisposeWhileTakeIsStillRunning_GivesTheWindowBack()
    {
        using var hostWin = new TestWindowThread("RetroHub host", 800, 600);
        using var gameWin = new TestWindowThread("Busy emulator", 640, 480, withHost: false);
        var game = gameWin.Top;
        var embedder = new WindowEmbedder(Frameless);

        gameWin.Post(() => Thread.Sleep(1500));
        Thread.Sleep(100);
        var sw = Stopwatch.StartNew();
        Assert.True(embedder.Take(game, hostWin.Host, 400, 300));
        embedder.Dispose();

        Thread.Sleep(Math.Max(0, 2500 - (int)sw.ElapsedMilliseconds));
        Assert.True(SpinUntil(() => TestWin32.GetParent(game) == IntPtr.Zero && (TestWin32.Style(game) & Native.WS_CHILD) == 0, 5000),
            $"window still has parent {TestWin32.GetParent(game):X}");
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
}
