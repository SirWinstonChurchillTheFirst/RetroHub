using System.Diagnostics;
using RetroHub.Core.Logging;
using RetroHub.Input.Native;

namespace RetroHub.Input;

/// <summary>
/// Pure exit-combo logic (no I/O, injected timestamps, single-threaded use):
/// fires once when Back+Start are held together on the same pad for <see cref="HoldSeconds"/>, or when Guide is
/// pressed. Each hold/press fires only once; buttons already held when a pad first appears (or after
/// <see cref="Reset"/>) are ignored until released.
/// </summary>
public sealed class ExitComboDetector
{
    private const PadButtons Relevant = PadButtons.Back | PadButtons.Start | PadButtons.Guide;
    private const PadButtons Combo = PadButtons.Back | PadButtons.Start;

    private sealed class PadState
    {
        public PadButtons Blocked;
        public long? ComboSince;
        public bool ComboFired;
        public bool GuideWasDown;
        public int SeenGeneration;
    }

    private readonly Dictionary<int, PadState> _pads = new();
    private int _generation;
    private double _holdSeconds;

    public ExitComboDetector(double holdSeconds = 1.5) => HoldSeconds = holdSeconds;

    /// <summary>How long Back+Start must be held (clamped to 0.1..10 s).</summary>
    public double HoldSeconds
    {
        get => Volatile.Read(ref _holdSeconds);
        set => Volatile.Write(ref _holdSeconds, double.IsFinite(value) ? Math.Clamp(value, 0.1, 10) : 1.5);
    }

    public void Reset() => _pads.Clear();

    /// <summary>Feeds one poll of all connected pads; returns true when the combo fired in this poll.</summary>
    public bool Update(IReadOnlyList<PadInput> pads, long timestampMs)
    {
        int gen = ++_generation;
        long holdMs = (long)Math.Round(HoldSeconds * 1000);
        bool fired = false;
        int seen = 0;
        for (int i = 0; i < pads.Count; i++)
        {
            var (id, snap) = pads[i];
            var buttons = snap.Buttons & Relevant;
            if (!_pads.TryGetValue(id, out var st))
            {
                st = new PadState { Blocked = buttons };
                _pads[id] = st;
            }
            if (st.SeenGeneration != gen) seen++;
            st.SeenGeneration = gen;
            st.Blocked &= buttons;
            var eff = buttons & ~st.Blocked;

            if ((eff & Combo) == Combo)
            {
                st.ComboSince ??= timestampMs;
                if (!st.ComboFired && timestampMs - st.ComboSince.Value >= holdMs)
                {
                    st.ComboFired = true;
                    fired = true;
                }
            }
            else
            {
                st.ComboSince = null;
                st.ComboFired = false;
            }

            bool guide = (eff & PadButtons.Guide) != 0;
            if (guide && !st.GuideWasDown) fired = true;
            st.GuideWasDown = guide;
        }
        if (_pads.Count != seen)
        {
            List<int>? stale = null;
            foreach (var (id, st) in _pads) if (st.SeenGeneration != gen) (stale ??= new()).Add(id);
            if (stale != null) foreach (var id in stale) _pads.Remove(id);
        }
        return fired;
    }
}

/// <summary>
/// Watches for the exit combo while a game runs: polls <b>XInput only</b> (never SDL, so the emulator keeps
/// exclusive HID access) at ~15 Hz on its own background thread. Raises <see cref="Triggered"/> on that thread
/// when Back+Start are held for <see cref="HoldSeconds"/> or Guide is pressed (Guide only where
/// XInputGetStateEx exists). PlayStation/Nintendo pads without an XInput driver are not seen — rely on the
/// emulator's own exit hotkey for them. Never throws from the polling thread.
/// </summary>
public sealed class ExitComboWatcher : IDisposable
{
    private const int PollIntervalMs = 66;          // ~15 Hz
    private const int EmptySlotProbeMs = 1000;       // XInputGetState on an empty slot is slow; probe rarely

    private readonly object _gate = new();
    private readonly ExitComboDetector _detector;
    private Thread? _thread;
    private ManualResetEventSlim? _stop;
    private bool _disposed;

    public ExitComboWatcher(double holdSeconds = 1.5) => _detector = new ExitComboDetector(holdSeconds);

    /// <summary>Raised on the watcher thread, once per hold/press. Consumers marshal to the UI thread.</summary>
    public event Action? Triggered;

    /// <summary>Back+Start hold time in seconds (default 1.5, clamped 0.1..10). May be changed while running.</summary>
    public double HoldSeconds { get => _detector.HoldSeconds; set => _detector.HoldSeconds = value; }

    /// <summary>True when an XInput DLL could be loaded.</summary>
    public bool IsAvailable => XInput.IsAvailable;

    /// <summary>True when the Guide button can be read (XInputGetStateEx, ordinal 100).</summary>
    public bool GuideSupported => XInput.HasGuide;

    public bool IsRunning { get { lock (_gate) return _thread != null; } }

    public void Start()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_thread != null) return;
            if (!XInput.IsAvailable)
            {
                Log.Warn("Input: exit combo not available (no XInput)");
                return;
            }
            _stop = new ManualResetEventSlim(false);
            var stop = _stop;
            _thread = new Thread(() => Run(stop)) { IsBackground = true, Name = "RetroHub exit combo" };
            _thread.Start();
            Log.Info($"Input: exit combo watcher started (hold {HoldSeconds:0.##} s, Guide {(GuideSupported ? "on" : "unavailable")})");
        }
    }

    public void Stop()
    {
        Thread? t;
        ManualResetEventSlim? stop;
        lock (_gate)
        {
            t = _thread; stop = _stop;
            _thread = null; _stop = null;
        }
        if (t == null) return;
        stop!.Set();
        // Stop() may be called from a Triggered handler (i.e. on the watcher thread itself): no join, and the
        // event stays alive so the loop can observe it and exit.
        if (t != Thread.CurrentThread)
        {
            if (t.Join(TimeSpan.FromSeconds(2))) stop.Dispose();
            else Log.Warn("Input: exit combo thread did not stop in time");
        }
        Log.Info("Input: exit combo watcher stopped");
    }

    public void Dispose()
    {
        Stop();
        lock (_gate) _disposed = true;
    }

    private void Run(ManualResetEventSlim stop)
    {
        var clock = Stopwatch.StartNew();
        var connected = new bool[XInput.MaxUsers];
        var nextProbe = new long[XInput.MaxUsers];
        var pads = new List<PadInput>(XInput.MaxUsers);
        _detector.Reset();
        try
        {
            while (!stop.IsSet)
            {
                try
                {
                    long now = clock.ElapsedMilliseconds;
                    pads.Clear();
                    for (int i = 0; i < XInput.MaxUsers; i++)
                    {
                        if (!connected[i] && now < nextProbe[i]) continue;
                        connected[i] = XInput.TryGetState(i, out var s);
                        if (connected[i]) pads.Add(new PadInput(i, XInput.ToSnapshot(s.Gamepad)));
                        else nextProbe[i] = now + EmptySlotProbeMs;
                    }
                    if (_detector.Update(pads, now))
                    {
                        Log.Info("Input: exit combo triggered");
                        try { Triggered?.Invoke(); }
                        catch (Exception ex) { Log.Error("Input: exit combo handler failed", ex); }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("Input: exit combo poll failed", ex);
                }
                stop.Wait(PollIntervalMs);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Input: exit combo thread crashed", ex);
        }
    }
}
