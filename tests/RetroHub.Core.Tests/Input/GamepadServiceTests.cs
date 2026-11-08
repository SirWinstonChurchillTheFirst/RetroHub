using System.Diagnostics;
using RetroHub.Input;
using RetroHub.Input.Native;
using Xunit;

namespace RetroHub.Core.Tests.Input;

/// <summary>Smoke tests against the real SDL3.dll / XInput on this machine (no controller required).</summary>
[Collection(Sdl3Collection.Name)]
public class GamepadServiceTests(ITestOutputHelper output)
{
    private void Report(GamepadService s)
    {
        output.WriteLine($"Backend: {s.Backend}, pads: {s.Pads.Count}, last style: {s.LastUsedStyle}");
        foreach (var p in s.Pads) output.WriteLine($"  pad {p.InstanceId}: {p.Name} ({p.Style})");
    }

    [Fact]
    public void Starts_with_sdl3_polls_and_disposes_cleanly()
    {
        var s = new GamepadService();
        try
        {
            s.Start();
            Assert.Equal(GamepadService.BackendSdl3, s.Backend);
            Assert.True(s.IsRunning);
            Assert.Equal(Sdl3.SDL_INIT_GAMEPAD, Sdl3.SDL_WasInit(Sdl3.SDL_INIT_GAMEPAD));
            Report(s);

            long before = s.PollCount;
            var sw = Stopwatch.StartNew();
            Thread.Sleep(1000);
            double hz = (s.PollCount - before) / sw.Elapsed.TotalSeconds;
            output.WriteLine($"Measured poll rate: {hz:0} Hz");
            Assert.InRange(hz, 40, 200); // target ~120 Hz; generous bounds for loaded CI machines
        }
        finally
        {
            s.Dispose();
        }
        Assert.False(s.IsRunning);
        Assert.Equal(GamepadService.BackendNone, s.Backend);
        Assert.Empty(s.Pads);
        Assert.Equal(0u, Sdl3.SDL_WasInit(Sdl3.SDL_INIT_GAMEPAD)); // subsystem released
        s.Dispose(); // idempotent
        Assert.Throws<ObjectDisposedException>(s.Start);
    }

    [Fact]
    public void Suspend_quits_the_sdl_gamepad_subsystem_and_resume_reinitialises()
    {
        using var s = new GamepadService();
        s.Start();
        Assert.Equal(GamepadService.BackendSdl3, s.Backend);

        s.Suspend();
        Assert.True(s.IsSuspended);
        Assert.Equal(GamepadService.BackendNone, s.Backend);
        Assert.Empty(s.Pads);
        Assert.Equal(0u, Sdl3.SDL_WasInit(Sdl3.SDL_INIT_GAMEPAD));
        Assert.Equal(0u, Sdl3.SDL_WasInit(Sdl3.SDL_INIT_JOYSTICK));
        s.Suspend(); // idempotent

        s.Resume();
        Assert.False(s.IsSuspended);
        Assert.Equal(GamepadService.BackendSdl3, s.Backend);
        Assert.Equal(Sdl3.SDL_INIT_GAMEPAD, Sdl3.SDL_WasInit(Sdl3.SDL_INIT_GAMEPAD));

        // a few fast cycles (as when games are started back to back)
        for (int i = 0; i < 3; i++) { s.Suspend(); s.Resume(); }
        Assert.Equal(GamepadService.BackendSdl3, s.Backend);
        Report(s);
    }

    [Fact]
    public void Suspend_before_start_starts_suspended()
    {
        using var s = new GamepadService();
        s.Suspend();
        s.Start();
        Assert.True(s.IsRunning);
        Assert.Equal(GamepadService.BackendNone, s.Backend);
        Assert.Equal(0u, Sdl3.SDL_WasInit(Sdl3.SDL_INIT_GAMEPAD));
        s.Resume();
        Assert.Equal(GamepadService.BackendSdl3, s.Backend);
    }

    [Fact]
    public async Task Start_stop_start_and_concurrent_calls_are_safe()
    {
        using var s = new GamepadService();
        s.Start();
        s.Start(); // no-op
        s.Stop();
        Assert.False(s.IsRunning);
        Assert.Equal(0u, Sdl3.SDL_WasInit(Sdl3.SDL_INIT_GAMEPAD));
        s.Start();
        Assert.Equal(GamepadService.BackendSdl3, s.Backend);

        var tasks = Enumerable.Range(0, 8).Select(i => Task.Run(() =>
        {
            for (int k = 0; k < 5; k++)
            {
                if ((i + k) % 2 == 0) s.Suspend(); else s.Resume();
                s.Rumble(0.2f, 0.2f, 10);
                _ = s.Pads.Count;
            }
        }, TestContext.Current.CancellationToken)).ToArray();
        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        s.Resume();
        Assert.Equal(GamepadService.BackendSdl3, s.Backend);
        s.SwapAcceptBack = true;
        Assert.True(s.SwapAcceptBack);
    }

    [Fact]
    public void Calls_from_the_input_thread_do_not_deadlock()
    {
        using var s = new GamepadService();
        s.Start();
        var done = new ManualResetEventSlim();
        int calls = 0;
        int inputThread = -1;
        s.Polled += () =>
        {
            if (Interlocked.Increment(ref calls) != 1) return;
            inputThread = Environment.CurrentManagedThreadId;
            // what an event handler might do on the input thread: none of this may block on the thread itself
            s.Suspend();
            s.Resume();
            s.Rumble(0.1f, 0.1f, 10);
            s.Stop();
            done.Set();
        };
        var sw = Stopwatch.StartNew();
        Assert.True(done.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(2), $"handler took {sw.Elapsed}");
        Assert.NotEqual(Environment.CurrentManagedThreadId, inputThread);
        Assert.True(SpinWait.SpinUntil(() => !s.IsRunning, TimeSpan.FromSeconds(5)));
        Assert.True(SpinWait.SpinUntil(() => Sdl3.SDL_WasInit(Sdl3.SDL_INIT_GAMEPAD) == 0, TimeSpan.FromSeconds(5)));

        s.Start(); // restart after a handler-requested stop
        Assert.True(s.IsRunning);
        Assert.Equal(GamepadService.BackendSdl3, s.Backend);
    }

    [Fact]
    public void Handler_exceptions_are_contained()
    {
        using var s = new GamepadService();
        s.Start();
        int calls = 0;
        s.Polled += () => { Interlocked.Increment(ref calls); throw new InvalidOperationException("boom"); };
        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref calls) >= 5, TimeSpan.FromSeconds(5)));
        Assert.True(s.IsRunning);
        Assert.Equal(GamepadService.BackendSdl3, s.Backend);
    }

    [Fact]
    public void XInput_only_backend()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only");
        using var s = new GamepadService(GamepadBackendPreference.XInputOnly);
        s.Start();
        Assert.Equal(GamepadService.BackendXInput, s.Backend);
        Assert.Equal(0u, Sdl3.SDL_WasInit(Sdl3.SDL_INIT_GAMEPAD)); // never touched SDL
        Report(s);
        s.Suspend();
        Assert.Equal(GamepadService.BackendNone, s.Backend);
        s.Resume();
        Assert.Equal(GamepadService.BackendXInput, s.Backend);
    }

    [Fact]
    public void Exit_combo_watcher_starts_and_stops()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only");
        using var w = new ExitComboWatcher(holdSeconds: 2);
        Assert.True(w.IsAvailable);
        Assert.Equal(2, w.HoldSeconds);
        output.WriteLine($"XInput: {XInput.LoadedLibrary}, Guide supported: {w.GuideSupported}");
        w.Start();
        Assert.True(w.IsRunning);
        w.Start(); // no-op
        Thread.Sleep(200);
        Assert.Equal(0u, Sdl3.SDL_WasInit(Sdl3.SDL_INIT_GAMEPAD)); // never SDL
        w.Stop();
        Assert.False(w.IsRunning);
        w.Start();
        Assert.True(w.IsRunning);
        w.Dispose();
        Assert.False(w.IsRunning);
        Assert.Throws<ObjectDisposedException>(w.Start);
    }
}
