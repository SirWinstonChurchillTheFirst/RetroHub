using RetroHub.Core.Logging;
using RetroHub.Input.Native;

namespace RetroHub.Input;

/// <summary>A polling gamepad backend. Used only from the GamepadService input thread.</summary>
internal interface IPadBackend : IDisposable
{
    /// <summary>"SDL3" or "XInput".</summary>
    string Name { get; }

    /// <summary>Updates the device list and reads all pads. <paramref name="pads"/> receives the state of every
    /// connected pad; <paramref name="added"/>/<paramref name="removed"/> receive hot-plug changes.</summary>
    void Poll(long nowMs, List<PadInput> pads, List<GamepadInfo> added, List<GamepadInfo> removed);

    /// <summary>Rumbles one pad (or all when <paramref name="padId"/> is null). Intensities 0..1.</summary>
    void Rumble(int? padId, float low, float high, int durationMs);
}

/// <summary>SDL3 gamepad backend (SDL_INIT_GAMEPAD only, no video, events disabled, state polled).</summary>
internal sealed class SdlBackend : IPadBackend
{
    private const int EnumerateIntervalMs = 1000;
    private const int OpenRetryMs = 5000;
    private const int MinVersion = 3_002_000; // SDL 3.2.0 (first stable SDL3)

    private sealed class Pad
    {
        public required nint Handle;
        public required GamepadInfo Info;
    }

    private readonly Dictionary<uint, Pad> _open = new();
    private readonly Dictionary<uint, long> _openFailedUntil = new();
    private long _nextEnumerate;
    private long _nextFlush;
    private bool _disposed;

    public string Name => "SDL3";
    public int Version { get; }

    private SdlBackend(int version) => Version = version;

    /// <summary>Loads SDL3.dll and initialises the gamepad subsystem; null (and a log entry) when not possible.</summary>
    public static SdlBackend? TryCreate()
    {
        NativeLibraryResolver.Install();
        int version;
        try
        {
            version = Sdl3.SDL_GetVersion();
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            Log.Warn($"Input: SDL3.dll not usable ({ex.GetType().Name}: {ex.Message})");
            return null;
        }

        try
        {
            if (version < MinVersion)
            {
                Log.Warn($"Input: SDL {FormatVersion(version)} is too old (need 3.2+)");
                return null;
            }
            // We gate input ourselves (Suspend while a game runs) and have no SDL window, so allow background events.
            Sdl3.SDL_SetHint(Sdl3.SDL_HINT_JOYSTICK_ALLOW_BACKGROUND_EVENTS, "1");
            Sdl3.SDL_SetHint(Sdl3.SDL_HINT_JOYSTICK_ENHANCED_REPORTS, "auto");
            // No message pump on our thread: let SDL handle device notifications/raw input on its own thread.
            Sdl3.SDL_SetHint(Sdl3.SDL_HINT_JOYSTICK_THREAD, "1");
            Sdl3.SDL_SetHint(Sdl3.SDL_HINT_NO_SIGNAL_HANDLERS, "1");
            if (!Sdl3.SDL_Init(Sdl3.SDL_INIT_GAMEPAD))
            {
                Log.Warn($"Input: SDL_Init(GAMEPAD) failed: {Sdl3.GetError()}");
                return null;
            }
            Sdl3.SDL_SetJoystickEventsEnabled(false);
            Sdl3.SDL_SetGamepadEventsEnabled(false);
            Log.Info($"Input: SDL {FormatVersion(version)} gamepad subsystem initialised");
            return new SdlBackend(version);
        }
        catch (Exception ex)
        {
            Log.Error("Input: SDL3 initialisation failed", ex);
            try { Sdl3.SDL_QuitSubSystem(Sdl3.SDL_INIT_GAMEPAD); } catch { }
            return null;
        }
    }

    public static string FormatVersion(int v) => $"{v / 1_000_000}.{v / 1000 % 1000}.{v % 1000}";

    public void Poll(long nowMs, List<PadInput> pads, List<GamepadInfo> added, List<GamepadInfo> removed)
    {
        if (_disposed) return;
        Sdl3.SDL_UpdateGamepads(); // also runs SDL's device detection

        bool enumerate = nowMs >= _nextEnumerate;
        foreach (var p in _open.Values)
            if (!Sdl3.SDL_GamepadConnected(p.Handle)) { enumerate = true; break; }
        if (enumerate)
        {
            Reconcile(nowMs, added, removed);
            _nextEnumerate = nowMs + EnumerateIntervalMs;
        }
        if (nowMs >= _nextFlush)
        {
            // events are disabled; flush anything else that might still have been queued (keeps memory flat)
            Sdl3.SDL_FlushEvents(0, 0xFFFF);
            _nextFlush = nowMs + EnumerateIntervalMs;
        }

        foreach (var (id, p) in _open)
            pads.Add(new PadInput(unchecked((int)id), ReadState(p.Handle)));
    }

    private void Reconcile(long nowMs, List<GamepadInfo> added, List<GamepadInfo> removed)
    {
        var ids = Sdl3.GetGamepads();
        var present = new HashSet<uint>(ids);

        List<uint>? gone = null;
        foreach (var (id, p) in _open)
            if (!present.Contains(id) || !Sdl3.SDL_GamepadConnected(p.Handle)) (gone ??= new()).Add(id);
        if (gone != null)
            foreach (var id in gone)
            {
                var p = _open[id];
                _open.Remove(id);
                Sdl3.SDL_CloseGamepad(p.Handle);
                removed.Add(p.Info);
            }

        foreach (var id in ids)
        {
            if (_open.ContainsKey(id)) continue;
            if (_openFailedUntil.TryGetValue(id, out var until) && nowMs < until) continue;
            nint h = Sdl3.SDL_OpenGamepad(id);
            if (h == 0)
            {
                Log.Warn($"Input: SDL_OpenGamepad({id}) failed: {Sdl3.GetError()}");
                _openFailedUntil[id] = nowMs + OpenRetryMs;
                continue;
            }
            _openFailedUntil.Remove(id);
            var name = Sdl3.GetGamepadName(h);
            if (string.IsNullOrWhiteSpace(name)) name = "Controller";
            int type = Sdl3.SDL_GetGamepadType(h);
            var info = new GamepadInfo(unchecked((int)Sdl3.SDL_GetGamepadID(h)), name, Sdl3.ToPadStyle(type));
            _open[id] = new Pad { Handle = h, Info = info };
            added.Add(info);
        }
        if (_openFailedUntil.Count > 0)
            foreach (var id in _openFailedUntil.Keys.ToList())
                if (!present.Contains(id)) _openFailedUntil.Remove(id);
    }

    private static PadSnapshot ReadState(nint h)
    {
        PadButtons b = PadButtons.None;
        for (int i = 0; i < Sdl3.ButtonsWeRead; i++)
            if (Sdl3.SDL_GetGamepadButton(h, i)) b |= (PadButtons)(1u << i);
        return new PadSnapshot(b,
            Stick(Sdl3.SDL_GetGamepadAxis(h, Sdl3.SDL_GAMEPAD_AXIS_LEFTX)),
            Stick(Sdl3.SDL_GetGamepadAxis(h, Sdl3.SDL_GAMEPAD_AXIS_LEFTY)),
            Trigger(Sdl3.SDL_GetGamepadAxis(h, Sdl3.SDL_GAMEPAD_AXIS_LEFT_TRIGGER)),
            Trigger(Sdl3.SDL_GetGamepadAxis(h, Sdl3.SDL_GAMEPAD_AXIS_RIGHT_TRIGGER)));
    }

    private static float Stick(short v) => Math.Clamp(v / 32767f, -1f, 1f);
    private static float Trigger(short v) => Math.Clamp(v / 32767f, 0f, 1f);

    public void Rumble(int? padId, float low, float high, int durationMs)
    {
        if (_disposed) return;
        ushort lo = (ushort)Math.Round(Math.Clamp(low, 0f, 1f) * 65535);
        ushort hi = (ushort)Math.Round(Math.Clamp(high, 0f, 1f) * 65535);
        uint ms = (uint)Math.Clamp(durationMs, 0, 10_000);
        foreach (var (id, p) in _open)
            if (padId == null || unchecked((int)id) == padId)
                Sdl3.SDL_RumbleGamepad(p.Handle, lo, hi, ms); // false = pad has no rumble; ignore
    }

    /// <summary>Closes all pads and quits the gamepad subsystem (refcounted by SDL; also releases HIDAPI devices).</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            foreach (var p in _open.Values) Sdl3.SDL_CloseGamepad(p.Handle);
            _open.Clear();
            Sdl3.SDL_QuitSubSystem(Sdl3.SDL_INIT_GAMEPAD);
            Log.Info("Input: SDL gamepad subsystem shut down");
        }
        catch (Exception ex) { Log.Warn("Input: SDL shutdown failed", ex); }
    }
}

/// <summary>XInput fallback backend (Xbox-compatible pads only; Guide via XInputGetStateEx when available).</summary>
internal sealed class XInputBackend : IPadBackend
{
    private const int EmptySlotProbeMs = 1000; // XInputGetState on an empty slot is slow

    private readonly GamepadInfo?[] _slots = new GamepadInfo?[XInput.MaxUsers];
    private readonly long[] _nextProbe = new long[XInput.MaxUsers];
    private readonly long[] _rumbleUntil = new long[XInput.MaxUsers]; // 0 = not rumbling
    private long _now;
    private bool _disposed;

    public string Name => "XInput";

    public static XInputBackend? TryCreate()
    {
        if (!XInput.IsAvailable) return null;
        Log.Info($"Input: using XInput ({XInput.LoadedLibrary})");
        return new XInputBackend();
    }

    public void Poll(long nowMs, List<PadInput> pads, List<GamepadInfo> added, List<GamepadInfo> removed)
    {
        if (_disposed) return;
        _now = nowMs;
        for (int i = 0; i < XInput.MaxUsers; i++)
        {
            if (_rumbleUntil[i] != 0 && nowMs >= _rumbleUntil[i])
            {
                XInput.SetVibration(i, 0, 0);
                _rumbleUntil[i] = 0;
            }
            if (_slots[i] == null && nowMs < _nextProbe[i]) continue;
            if (XInput.TryGetState(i, out var s))
            {
                if (_slots[i] == null)
                {
                    _slots[i] = new GamepadInfo(i, $"Xbox-Controller {i + 1}", PadStyle.Xbox);
                    added.Add(_slots[i]!);
                }
                pads.Add(new PadInput(i, XInput.ToSnapshot(s.Gamepad)));
            }
            else
            {
                if (_slots[i] != null) { removed.Add(_slots[i]!); _slots[i] = null; _rumbleUntil[i] = 0; }
                _nextProbe[i] = nowMs + EmptySlotProbeMs;
            }
        }
    }

    /// <summary>XInputSetState, stopped again by <see cref="Poll"/> once the duration has elapsed.</summary>
    public void Rumble(int? padId, float low, float high, int durationMs)
    {
        if (_disposed) return;
        ushort lo = (ushort)Math.Round(Math.Clamp(low, 0f, 1f) * 65535);
        ushort hi = (ushort)Math.Round(Math.Clamp(high, 0f, 1f) * 65535);
        int ms = Math.Clamp(durationMs, 0, 10_000);
        for (int i = 0; i < XInput.MaxUsers; i++)
        {
            if (_slots[i] == null || (padId != null && padId != i)) continue;
            if (XInput.SetVibration(i, lo, hi))
                _rumbleUntil[i] = (lo == 0 && hi == 0) || ms == 0 ? 0 : Math.Max(1, _now + ms);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        for (int i = 0; i < XInput.MaxUsers; i++)
            if (_rumbleUntil[i] != 0) { XInput.SetVibration(i, 0, 0); _rumbleUntil[i] = 0; }
    }
}
