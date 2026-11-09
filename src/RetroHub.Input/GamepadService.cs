using System.Collections.Concurrent;
using System.Diagnostics;
using RetroHub.Core.Logging;
using RetroHub.Input.Native;

namespace RetroHub.Input;

/// <summary>Which backend <see cref="GamepadService"/> may use.</summary>
public enum GamepadBackendPreference
{
    /// <summary>SDL3 when SDL3.dll loads and SDL_Init succeeds, else XInput.</summary>
    Auto,
    /// <summary>SDL3 only (no fallback).</summary>
    Sdl3Only,
    /// <summary>XInput only (never loads SDL).</summary>
    XInputOnly,
}

/// <summary>
/// Gamepad input for the launcher UI. Runs one dedicated background thread ("RetroHub input") that polls at
/// ~120 Hz and owns all SDL calls. Backend: SDL3 (all common pads incl. DualShock/DualSense/Switch) or the XInput
/// fallback (Xbox-compatible pads).
/// <para><b>Threading:</b> <see cref="ActionTriggered"/>, <see cref="Connected"/>, <see cref="Disconnected"/> and
/// <see cref="LastUsedStyleChanged"/> are raised <b>on the input thread</b>; consumers must marshal to the UI thread
/// (e.g. <c>Dispatcher.UIThread.Post</c>). Handler exceptions are caught and logged. All public methods are
/// thread-safe; <see cref="Start"/>/<see cref="Resume"/> block briefly (≤ 5 s, normally &lt; 0.5 s) until the
/// backend is initialised so <see cref="Backend"/> and <see cref="Pads"/> are valid afterwards.</para>
/// <para><b>While a game runs</b> call <see cref="Suspend"/>: all pads are closed and SDL's gamepad subsystem is
/// shut down so its HIDAPI driver cannot interfere with the emulator. <see cref="Resume"/> re-initialises; buttons
/// held at that moment are ignored until released.</para>
/// </summary>
public sealed class GamepadService : IDisposable
{
    public const string BackendSdl3 = "SDL3";
    public const string BackendXInput = "XInput";
    public const string BackendNone = "None";

    private static readonly TimeSpan PollInterval = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 120);
    private static readonly TimeSpan IdleInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan ApplyTimeout = TimeSpan.FromSeconds(5);
    private const long ResumeGraceMs = 2000;

    private readonly GamepadBackendPreference _preference;
    private readonly Func<IPadBackend?>? _backendFactory; // tests only
    private readonly InputTranslator _translator;
    private readonly object _gate = new();          // serialises Start/Stop/Suspend/Resume/Dispose
    private readonly object _sync = new();          // request/acknowledge handshake with the input thread
    private readonly ConcurrentQueue<(int? Pad, float Low, float High, int Ms)> _rumble = new();

    // request state (guarded by _sync)
    private bool _wantSuspended;
    private bool _stopRequested;
    private long _requestSeq;
    private long _appliedSeq;

    private Thread? _thread;
    private bool _disposed;
    private bool _sdlUnavailable; // input thread only: don't retry a missing/broken SDL3.dll on every Resume

    // published state
    private volatile string _backendName = BackendNone;
    private volatile GamepadInfo[] _pads = [];
    private volatile bool _suspended;
    private int _lastUsedStyle = (int)PadStyle.Xbox;
    private bool _styleFromInput;
    private long _pollCount;
    private volatile int _lastActivePad = -1; // for Rumble(); -1 = none yet

    public GamepadService() : this(GamepadBackendPreference.Auto) { }

    public GamepadService(GamepadBackendPreference preference, InputTranslatorOptions? options = null)
    {
        _preference = preference;
        _translator = new InputTranslator(options);
    }

    /// <summary>Tests: uses <paramref name="backendFactory"/> (called on the input thread at Start/Resume) instead of
    /// SDL3/XInput.</summary>
    internal GamepadService(Func<IPadBackend?> backendFactory, InputTranslatorOptions? options = null)
        : this(GamepadBackendPreference.Auto, options) => _backendFactory = backendFactory;

    /// <summary>A UI action (raised on the input thread).</summary>
    public event Action<UiAction>? ActionTriggered;
    /// <summary>A pad was plugged in (raised on the input thread; also for pads present at <see cref="Start"/>).
    /// Not raised again for the same pads after <see cref="Resume"/>.</summary>
    public event Action<GamepadInfo>? Connected;
    /// <summary>A pad was unplugged (raised on the input thread). Not raised by <see cref="Suspend"/>/<see cref="Stop"/>.</summary>
    public event Action<GamepadInfo>? Disconnected;
    /// <summary><see cref="LastUsedStyle"/> changed (raised on the input thread).</summary>
    public event Action<PadStyle>? LastUsedStyleChanged;

    /// <summary>"SDL3", "XInput" or "None" (not started, suspended, or no backend available).</summary>
    public string Backend => _backendName;

    /// <summary>Currently open pads (immutable snapshot). Empty while suspended or stopped.</summary>
    public IReadOnlyList<GamepadInfo> Pads => _pads;

    /// <summary>Glyph style of the pad that pressed something last (until then: of the first connected pad;
    /// default Xbox).</summary>
    public PadStyle LastUsedStyle => (PadStyle)Volatile.Read(ref _lastUsedStyle);

    /// <summary>Nintendo-style confirm/back: East = Accept, South = Back. May be changed at any time.</summary>
    public bool SwapAcceptBack { get => _translator.SwapAcceptBack; set => _translator.SwapAcceptBack = value; }

    public bool IsRunning
    {
        get
        {
            var t = _thread;
            if (t == null || !t.IsAlive) return false;
            lock (_sync) return !_stopRequested;
        }
    }
    public bool IsSuspended => _suspended;

    /// <summary>Number of completed polls (diagnostics/tests).</summary>
    internal long PollCount => Interlocked.Read(ref _pollCount);

    /// <summary>Test hook: raised on the input thread after every poll.</summary>
    internal event Action? Polled;

    /// <summary>Starts the input thread (no-op when already running) and waits until the backend is initialised.
    /// If <see cref="Suspend"/> was called before, the thread starts suspended.</summary>
    public void Start()
    {
        if (_thread == Thread.CurrentThread)
        {
            // from an event handler: the thread is obviously running; only cancel a pending Stop() request.
            // Taking _gate here could stall a UI-thread caller that holds it while waiting for this very thread.
            ObjectDisposedException.ThrowIf(_disposed, this);
            lock (_sync)
            {
                if (_stopRequested) { _stopRequested = false; _requestSeq++; }
            }
            return;
        }
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_thread != null)
            {
                bool stopping;
                lock (_sync) stopping = _stopRequested;
                if (!stopping && _thread.IsAlive) return; // already running
                // a stop requested from an event handler (or a crashed thread): finish it first
                if (_thread.IsAlive && !_thread.Join(TimeSpan.FromSeconds(3)))
                {
                    Log.Error("Input: previous input thread is still running; not starting a second one");
                    return;
                }
                _thread = null;
            }
            long seq;
            lock (_sync)
            {
                _stopRequested = false;
                seq = ++_requestSeq;
            }
            _thread = new Thread(Run) { IsBackground = true, Name = "RetroHub input" };
            _thread.Start();
            WaitApplied(seq);
            Log.Info($"Input: gamepad service started (backend {Backend}, {Pads.Count} pad(s))");
        }
    }

    /// <summary>Stops the input thread, closes all pads and shuts SDL's gamepad subsystem down. Blocks until the
    /// thread exited (≤ 3 s). When called from an event handler (input thread), it only requests the stop.</summary>
    public void Stop()
    {
        if (_thread == Thread.CurrentThread) { RequestStop(); return; } // never block the input thread on _gate
        lock (_gate) StopCore();
    }

    private void RequestStop()
    {
        lock (_sync)
        {
            _stopRequested = true;
            _requestSeq++;
        }
    }

    /// <summary>Closes all pads and quits SDL's gamepad subsystem (call before a game starts). Blocks until done.</summary>
    public void Suspend() => SetSuspended(true);

    /// <summary>Re-initialises the backend after <see cref="Suspend"/>. Inputs held at this moment are ignored until
    /// released. Blocks until the backend is initialised.</summary>
    public void Resume() => SetSuspended(false);

    /// <summary>Queues a rumble on the last used pad (or all pads). Intensities 0..1; silently ignored when the pad
    /// cannot rumble or the service is suspended.</summary>
    public void Rumble(float lowFrequency, float highFrequency, int durationMs, bool allPads = false)
    {
        if (_thread == null || _suspended) return;
        int last = _lastActivePad;
        int? pad = allPads || last < 0 ? null : last;
        if (_rumble.Count < 16) _rumble.Enqueue((pad, lowFrequency, highFrequency, durationMs));
    }

    /// <summary>Stops the service and joins the input thread (from an event handler: requests the stop only).</summary>
    public void Dispose()
    {
        if (_thread == Thread.CurrentThread)
        {
            RequestStop();
            _disposed = true;
            return;
        }
        lock (_gate)
        {
            if (_disposed) return;
            StopCore();
            _disposed = true;
        }
    }

    // ---- control -----------------------------------------------------------------------------------------------

    private void SetSuspended(bool suspended)
    {
        if (_thread == Thread.CurrentThread)
        {
            // from an event handler: just record the request (applied on the next loop iteration); taking _gate here
            // could stall against a UI-thread caller that is waiting for this very thread
            lock (_sync)
            {
                if (_wantSuspended == suspended) return;
                _wantSuspended = suspended;
                _requestSeq++;
            }
            _suspended = suspended;
            return;
        }
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            long seq;
            lock (_sync)
            {
                if (_wantSuspended == suspended) return;
                _wantSuspended = suspended;
                seq = ++_requestSeq;
            }
            _suspended = suspended;
            if (_thread == null) return; // applied on Start
            WaitApplied(seq);
            Log.Info(suspended ? "Input: gamepad service suspended" : $"Input: gamepad service resumed (backend {Backend})");
        }
    }

    private void StopCore()
    {
        var t = _thread;
        if (t == null) return;
        RequestStop();
        if (t.Join(TimeSpan.FromSeconds(3)))
        {
            _thread = null;
            Log.Info("Input: gamepad service stopped");
        }
        else
        {
            // keep the reference: Start() must not run a second thread against the same SDL state
            Log.Error("Input: input thread did not stop within 3 s");
        }
    }

    private void WaitApplied(long seq)
    {
        if (_thread == Thread.CurrentThread) return;
        var deadline = Stopwatch.GetTimestamp() + (long)(ApplyTimeout.TotalSeconds * Stopwatch.Frequency);
        lock (_sync)
        {
            while (_appliedSeq < seq)
            {
                var left = deadline - Stopwatch.GetTimestamp();
                if (left <= 0 || !(_thread?.IsAlive ?? false))
                {
                    if (left <= 0) Log.Warn("Input: input thread did not acknowledge in time");
                    return;
                }
                Monitor.Wait(_sync, TimeSpan.FromSeconds((double)left / Stopwatch.Frequency));
            }
        }
    }

    // ---- input thread ------------------------------------------------------------------------------------------

    private void Run()
    {
        IPadBackend? backend = null;
        var clock = Stopwatch.StartNew();
        var pads = new List<PadInput>(8);
        var added = new List<GamepadInfo>();
        var removed = new List<GamepadInfo>();
        // backend pad id -> the GamepadInfo published to consumers (Pads/Connected/Disconnected). After Resume the
        // backend re-opens a pad under a new id (SDL never reuses instance ids); the published info stays the same.
        var open = new Dictionary<int, GamepadInfo>();
        List<GamepadInfo>? resumeMemo = null;   // published pads open before Suspend (matched silently after Resume)
        long resumeMemoUntil = 0;
        bool everInitialised = false;
        bool initAttempted = false;
        int consecutiveErrors = 0;

        using var sleeper = new PreciseSleeper();
        try
        {
            while (true)
            {
                // 1) apply requests
                bool stop, wantSuspended;
                long seq;
                lock (_sync) { stop = _stopRequested; wantSuspended = _wantSuspended; seq = _requestSeq; }

                if ((stop || wantSuspended) && backend != null)
                {
                    // keep memo entries of an earlier Suspend that have not reappeared yet (back-to-back games),
                    // otherwise a pad lost during the first game would never get its Disconnected event
                    if (stop) resumeMemo = null;
                    else (resumeMemo ??= new()).AddRange(open.Values);
                    ShutdownBackend(ref backend, open);
                }
                if (stop || wantSuspended)
                {
                    _backendName = BackendNone;
                    initAttempted = false;
                }
                else if (!initAttempted)
                {
                    // one attempt per Start/Resume; without a backend the thread just idles
                    initAttempted = true;
                    backend = CreateBackend();
                    _backendName = backend?.Name ?? BackendNone;
                    _translator.Reset();
                    open.Clear();
                    if (everInitialised) resumeMemoUntil = clock.ElapsedMilliseconds + ResumeGraceMs;
                    else resumeMemo = null;
                    everInitialised = true;
                    if (backend == null) Log.Warn("Input: no gamepad backend available (keyboard/mouse only)");
                }

                if (backend != null) PollOnce(backend, clock.ElapsedMilliseconds, pads, added, removed, open,
                    resumeMemo, ref consecutiveErrors);
                if (resumeMemo != null && !wantSuspended && !stop && clock.ElapsedMilliseconds >= resumeMemoUntil)
                {
                    // pads that did not come back after Resume (also when no backend could be re-initialised)
                    foreach (var gone in resumeMemo)
                    {
                        Log.Info($"Input: controller gone while suspended: {gone.Name}");
                        Raise(Disconnected, gone);
                    }
                    resumeMemo = null;
                }

                lock (_sync)
                {
                    if (_appliedSeq < seq) { _appliedSeq = seq; Monitor.PulseAll(_sync); }
                }
                if (stop) break;

                sleeper.Sleep(backend != null ? PollInterval : IdleInterval);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Input: input thread crashed", ex);
            try { if (backend != null) ShutdownBackend(ref backend, open); } catch { }
        }
        finally
        {
            _backendName = BackendNone;
            _pads = [];
            lock (_sync) { _appliedSeq = _requestSeq; Monitor.PulseAll(_sync); }
        }
    }

    private IPadBackend? CreateBackend()
    {
        try
        {
            if (_backendFactory != null) return _backendFactory();
            if (_preference != GamepadBackendPreference.XInputOnly && !_sdlUnavailable)
            {
                var sdl = SdlBackend.TryCreate();
                if (sdl != null) return sdl;
                // a missing/broken DLL won't fix itself; an SDL_Init failure might (retry on next Resume)
                _sdlUnavailable = !IsSdlLoadable();
            }
            if (_preference != GamepadBackendPreference.Sdl3Only)
                return XInputBackend.TryCreate();
        }
        catch (Exception ex)
        {
            Log.Error("Input: creating the gamepad backend failed", ex);
        }
        return null;
    }

    private static bool IsSdlLoadable()
    {
        try { return Sdl3.SDL_GetVersion() > 0; }
        catch { return false; }
    }

    private void ShutdownBackend(ref IPadBackend? backend, Dictionary<int, GamepadInfo> open)
    {
        try { backend?.Dispose(); }
        catch (Exception ex) { Log.Warn("Input: backend shutdown failed", ex); }
        backend = null;
        open.Clear();
        _pads = [];
        _translator.Reset();
        while (_rumble.TryDequeue(out _)) { }
        _lastActivePad = -1; // backend ids change on re-open; until the next press, rumble targets all pads
    }

    private void PollOnce(IPadBackend backend, long now, List<PadInput> pads, List<GamepadInfo> added,
        List<GamepadInfo> removed, Dictionary<int, GamepadInfo> open, List<GamepadInfo>? resumeMemo,
        ref int consecutiveErrors)
    {
        pads.Clear(); added.Clear(); removed.Clear();
        try
        {
            while (_rumble.TryDequeue(out var r)) backend.Rumble(r.Pad, r.Low, r.High, r.Ms);
            backend.Poll(now, pads, added, removed);
            consecutiveErrors = 0;
        }
        catch (Exception ex)
        {
            if (consecutiveErrors++ % 600 == 0) Log.Error($"Input: {backend.Name} poll failed", ex); // ≤ 1 per 5 s
            return;
        }

        // hot-plug bookkeeping + events
        if (removed.Count > 0 || added.Count > 0)
        {
            var gone = new List<GamepadInfo>(removed.Count);
            foreach (var r in removed)
                if (open.Remove(r.InstanceId, out var published)) gone.Add(published);
            var fresh = new List<GamepadInfo>(added.Count);
            foreach (var a in added)
            {
                // same pad as before Suspend -> keep the identity the consumers already know, raise nothing
                var known = resumeMemo != null ? TakeMatch(resumeMemo, a, open) : null;
                open[a.InstanceId] = known ?? a;
                if (known == null) fresh.Add(a);
            }
            _pads = open.Values.ToArray();
            foreach (var r in gone)
            {
                Log.Info($"Input: controller disconnected: {r.Name} ({r.Style})");
                Raise(Disconnected, r);
            }
            foreach (var a in fresh)
            {
                Log.Info($"Input: controller connected: {a.Name} ({a.Style}, id {a.InstanceId}, {backend.Name})");
                if (!_styleFromInput) SetLastUsedStyle(a.Style, fromInput: false);
                Raise(Connected, a);
            }
        }

        var actions = _translator.Update(pads, now);
        Interlocked.Increment(ref _pollCount);
        if (Polled is { } polled)
        {
            try { polled(); } catch (Exception ex) { Log.Error("Input: poll hook failed", ex); }
        }
        if (actions.Count == 0) return;

        if (_translator.LastActivePadId is int id && open.TryGetValue(id, out var active))
        {
            _lastActivePad = id;
            SetLastUsedStyle(active.Style, fromInput: true);
        }
        foreach (var a in actions) Raise(ActionTriggered, a);
    }

    /// <summary>Finds (and removes) the pre-Suspend entry for a re-opened pad; null when it is a new pad.</summary>
    private static GamepadInfo? TakeMatch(List<GamepadInfo> memo, GamepadInfo pad, Dictionary<int, GamepadInfo> open)
    {
        int i = memo.FindIndex(m => m.Name == pad.Name && m.Style == pad.Style
                                    && !open.Values.Any(o => o.InstanceId == m.InstanceId));
        if (i < 0) return null;
        var known = memo[i];
        memo.RemoveAt(i);
        return known;
    }

    private void SetLastUsedStyle(PadStyle style, bool fromInput)
    {
        if (fromInput) _styleFromInput = true;
        if (Interlocked.Exchange(ref _lastUsedStyle, (int)style) != (int)style) Raise(LastUsedStyleChanged, style);
    }

    private static void Raise<T>(Action<T>? handler, T arg)
    {
        if (handler == null) return;
        foreach (var d in handler.GetInvocationList())
        {
            try { ((Action<T>)d)(arg); }
            catch (Exception ex) { Log.Error($"Input: event handler failed ({typeof(T).Name} {arg})", ex); }
        }
    }
}
