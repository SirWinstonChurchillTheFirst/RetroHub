using System.Collections.Concurrent;
using System.Diagnostics;
using RetroHub.Input;
using Xunit;

namespace RetroHub.Core.Tests.Input;

/// <summary>"Physical" fake pads shared by all backend instances (a backend is re-created on every Resume).</summary>
internal sealed class FakePadWorld
{
    internal sealed class Device(string name, PadStyle style)
    {
        public string Name { get; } = name;
        public PadStyle Style { get; } = style;
        private PadSnapshot _state;
        private readonly object _lock = new();
        public PadSnapshot State { get { lock (_lock) return _state; } set { lock (_lock) _state = value; } }
    }

    private readonly object _lock = new();
    private readonly List<Device> _devices = new();
    private int _nextId = 100; // like SDL: instance ids are never reused, also not across re-initialisation
    public int BackendsCreated;

    public Device Plug(string name, PadStyle style = PadStyle.Xbox)
    {
        var d = new Device(name, style);
        lock (_lock) _devices.Add(d);
        return d;
    }

    public void Unplug(Device d) { lock (_lock) _devices.Remove(d); }

    public IPadBackend? CreateBackend()
    {
        Interlocked.Increment(ref BackendsCreated);
        return new Backend(this);
    }

    private sealed class Backend(FakePadWorld world) : IPadBackend
    {
        private readonly Dictionary<Device, GamepadInfo> _open = new();
        public string Name => "Fake";

        public void Poll(long nowMs, List<PadInput> pads, List<GamepadInfo> added, List<GamepadInfo> removed)
        {
            List<Device> present;
            lock (world._lock) present = world._devices.ToList();
            foreach (var d in _open.Keys.Where(d => !present.Contains(d)).ToList())
            {
                removed.Add(_open[d]);
                _open.Remove(d);
            }
            foreach (var d in present)
            {
                if (!_open.TryGetValue(d, out var info))
                {
                    info = new GamepadInfo(Interlocked.Increment(ref world._nextId), d.Name, d.Style);
                    _open[d] = info;
                    added.Add(info);
                }
                pads.Add(new PadInput(info.InstanceId, d.State));
            }
        }

        public void Rumble(int? padId, float low, float high, int durationMs) { }
        public void Dispose() => _open.Clear();
    }
}

/// <summary>GamepadService event/threading logic against a fake backend (no SDL, no XInput).</summary>
public class GamepadServiceLogicTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    private sealed class Recorder
    {
        public readonly ConcurrentQueue<GamepadInfo> Connected = new();
        public readonly ConcurrentQueue<GamepadInfo> Disconnected = new();
        public readonly ConcurrentQueue<UiAction> Actions = new();

        public Recorder(GamepadService s)
        {
            s.Connected += Connected.Enqueue;
            s.Disconnected += Disconnected.Enqueue;
            s.ActionTriggered += Actions.Enqueue;
        }
    }

    [Fact]
    public void Pad_identity_is_stable_across_suspend_and_resume()
    {
        var world = new FakePadWorld();
        var dev = world.Plug("Pad A", PadStyle.PlayStation);
        using var s = new GamepadService(world.CreateBackend);
        var rec = new Recorder(s);

        s.Start();
        var announced = Assert.Single(rec.Connected);
        Assert.Equal([announced], s.Pads);

        s.Suspend();
        Assert.Empty(s.Pads);
        s.Resume(); // the backend re-opens the pad under a new internal id
        Assert.Equal(2, world.BackendsCreated);
        Assert.Equal([announced], s.Pads);  // the UI keeps seeing the pad it was told about
        Assert.Single(rec.Connected);       // ...and is not told again

        world.Unplug(dev);
        Assert.True(SpinWait.SpinUntil(() => !rec.Disconnected.IsEmpty, Wait));
        Assert.Equal(announced, Assert.Single(rec.Disconnected)); // the same identity it was given in Connected
        Assert.Empty(s.Pads);
    }

    [Fact]
    public void Pad_lost_during_back_to_back_games_is_still_reported()
    {
        var world = new FakePadWorld();
        world.Plug("Pad A");
        var b = world.Plug("Pad B");
        using var s = new GamepadService(world.CreateBackend);
        var rec = new Recorder(s);
        s.Start();
        Assert.Equal(2, rec.Connected.Count);
        var announcedB = rec.Connected.Single(p => p.Name == "Pad B");

        s.Suspend();          // game 1
        world.Unplug(b);      // pad B dies while the game runs
        s.Resume();
        s.Suspend();          // game 2 starts right away (within the resume grace period)
        s.Resume();

        Assert.True(SpinWait.SpinUntil(() => !rec.Disconnected.IsEmpty, Wait), "Disconnected for pad B was never raised");
        Assert.Equal(announcedB, Assert.Single(rec.Disconnected));
        Assert.Equal("Pad A", Assert.Single(s.Pads).Name);
        Assert.Equal(2, rec.Connected.Count);
    }

    [Fact]
    public void Start_from_a_handler_does_not_stall_a_concurrent_suspend()
    {
        var world = new FakePadWorld();
        using var s = new GamepadService(world.CreateBackend);
        s.Start();
        var inHandler = new ManualResetEventSlim();
        int calls = 0;
        s.Polled += () =>
        {
            if (Interlocked.Increment(ref calls) != 1) return;
            inHandler.Set();
            Thread.Sleep(300);   // meanwhile the UI thread enters Suspend() and waits for this thread
            s.Start();           // e.g. a handler that "makes sure" the service runs
        };
        Assert.True(inHandler.Wait(Wait, TestContext.Current.CancellationToken));
        Thread.Sleep(50);
        var sw = Stopwatch.StartNew();
        s.Suspend();
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(2), $"Suspend took {sw.Elapsed}");
        Assert.True(s.IsSuspended);
        Assert.True(s.IsRunning);
    }

    [Fact]
    public void Stop_then_start_from_a_handler_keeps_the_service_running()
    {
        var world = new FakePadWorld();
        using var s = new GamepadService(world.CreateBackend);
        s.Start();
        var done = new ManualResetEventSlim();
        int calls = 0;
        s.Polled += () =>
        {
            if (Interlocked.Increment(ref calls) != 1) return;
            s.Stop();
            s.Start(); // cancels the pending stop
            done.Set();
        };
        Assert.True(done.Wait(Wait, TestContext.Current.CancellationToken));
        Thread.Sleep(100);
        Assert.True(s.IsRunning);
        Assert.True(Volatile.Read(ref calls) > 3); // still polling
    }

    [Fact]
    public void Buttons_held_through_resume_do_not_fire_and_actions_flow_after_release()
    {
        var world = new FakePadWorld();
        var dev = world.Plug("Pad A", PadStyle.Nintendo);
        using var s = new GamepadService(world.CreateBackend);
        var rec = new Recorder(s);
        var styles = new ConcurrentQueue<PadStyle>();
        s.LastUsedStyleChanged += styles.Enqueue;
        s.Start();
        Assert.Equal(PadStyle.Nintendo, s.LastUsedStyle); // style of the first connected pad

        dev.State = new PadSnapshot(PadButtons.South);
        Assert.True(SpinWait.SpinUntil(() => rec.Actions.Contains(UiAction.Accept), Wait));
        dev.State = default;

        s.Suspend();
        dev.State = new PadSnapshot(PadButtons.Back | PadButtons.Start); // exit combo still held when the game ends
        s.Resume();
        Thread.Sleep(200);
        Assert.DoesNotContain(UiAction.View, rec.Actions);
        Assert.DoesNotContain(UiAction.Menu, rec.Actions);

        dev.State = default;
        Thread.Sleep(50);
        dev.State = new PadSnapshot(PadButtons.Start);
        Assert.True(SpinWait.SpinUntil(() => rec.Actions.Contains(UiAction.Menu), Wait));
        Assert.Equal([PadStyle.Nintendo], styles);
    }
}
