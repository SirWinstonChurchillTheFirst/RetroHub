namespace RetroHub.Input;

/// <summary>Tuning for <see cref="InputTranslator"/>. Defaults follow the console-launcher feel.</summary>
public sealed record InputTranslatorOptions
{
    /// <summary>Left stick deflection (0..1) that counts as a direction press.</summary>
    public float StickPress { get; init; } = 0.5f;
    /// <summary>A held stick direction is released only below this deflection (hysteresis).</summary>
    public float StickRelease { get; init; } = 0.35f;
    /// <summary>When both stick axes are active, the other axis must exceed the current dominant one by this factor
    /// before the direction switches (prevents Up/Right chatter around 45°).</summary>
    public float StickAxisSwitchRatio { get; init; } = 1.25f;
    /// <summary>Trigger value that counts as pressed (strictly greater).</summary>
    public float TriggerPress { get; init; } = 0.6f;
    /// <summary>A pressed trigger is released below this value (hysteresis).</summary>
    public float TriggerRelease { get; init; } = 0.4f;
    /// <summary>First direction repeat after this many ms.</summary>
    public int RepeatDelayMs { get; init; } = 400;
    /// <summary>Following repeats every this many ms.</summary>
    public int RepeatIntervalMs { get; init; } = 80;
    /// <summary>After a direction was held this long, repeat at <see cref="FastRepeatIntervalMs"/>.</summary>
    public int AccelerateAfterMs { get; init; } = 2000;
    public int FastRepeatIntervalMs { get; init; } = 50;
}

/// <summary>
/// Pure gamepad → <see cref="UiAction"/> translation (no I/O, injected timestamps, single-threaded use).
/// <list type="bullet">
/// <item>D-pad and left stick → Up/Down/Left/Right with key repeat (first after 400 ms, then every 80 ms,
/// 50 ms once held for 2 s). Stick: press at 0.5, release below 0.35; only the dominant axis counts.</item>
/// <item>All other inputs fire once on press: South=Accept, East=Back (swapped by <see cref="SwapAcceptBack"/>),
/// West=Favorite, North=Search, Start=Menu, Back=View, LB/RB=PageLeft/PageRight, triggers &gt; 0.6 =
/// TriggerLeft/TriggerRight, Guide=Home.</item>
/// <item>Inputs already held when a pad first appears (connect, or after <see cref="Reset"/>) are ignored until
/// released.</item>
/// <item>All pads are merged into one logical controller (held on any pad = held).</item>
/// </list>
/// </summary>
public sealed class InputTranslator
{
    // logical inputs, one bit each
    private const int LUp = 0, LDown = 1, LLeft = 2, LRight = 3;
    private const int LSouth = 4, LEast = 5, LWest = 6, LNorth = 7, LStart = 8, LSelect = 9;
    private const int LShoulderL = 10, LShoulderR = 11, LTriggerL = 12, LTriggerR = 13, LGuide = 14;
    private const int LogicalCount = 15;

    private enum Axis : byte { None, X, Y }

    private sealed class PadState
    {
        public bool StickUp, StickDown, StickLeft, StickRight, TriggerL, TriggerR;
        public Axis Dominant;
        public uint Blocked;
        public uint PrevEffective;
        public int SeenGeneration;
    }

    private struct Repeat { public long PressedAt; public long NextAt; }

    private readonly InputTranslatorOptions _o;
    private readonly Dictionary<int, PadState> _pads = new();
    private readonly Repeat[] _repeat = new Repeat[4];
    private uint _prevMerged;
    private int _generation;
    private volatile bool _swapAcceptBack;

    public InputTranslator(InputTranslatorOptions? options = null) => _o = options ?? new InputTranslatorOptions();

    public InputTranslatorOptions Options => _o;

    /// <summary>Nintendo-style layout: East confirms, South goes back. May be changed from any thread.</summary>
    public bool SwapAcceptBack { get => _swapAcceptBack; set => _swapAcceptBack = value; }

    /// <summary>The pad that most recently pressed something (null until the first press).</summary>
    public int? LastActivePadId { get; private set; }

    /// <summary>Forgets all pads: every input that is held at the next <see cref="Update"/> is ignored until released.
    /// Call after resuming input or re-initialising the backend.</summary>
    public void Reset()
    {
        _pads.Clear();
        _prevMerged = 0;
        Array.Clear(_repeat);
    }

    /// <summary>
    /// Feeds one poll of all currently connected pads (pads missing from <paramref name="pads"/> are treated as
    /// disconnected) and returns the actions to raise, in a stable order (directions first).
    /// </summary>
    /// <param name="timestampMs">Monotonic time in milliseconds (e.g. Stopwatch); only differences matter.</param>
    public IReadOnlyList<UiAction> Update(IReadOnlyList<PadInput> pads, long timestampMs)
    {
        int gen = ++_generation;
        uint merged = 0;
        int seen = 0;
        for (int i = 0; i < pads.Count; i++)
        {
            var (id, snap) = pads[i];
            bool isNew = !_pads.TryGetValue(id, out var st);
            if (isNew) { st = new PadState(); _pads[id] = st; }
            if (st!.SeenGeneration != gen) seen++;
            st.SeenGeneration = gen;

            uint raw = Evaluate(st, snap);
            if (isNew) st.Blocked = raw;     // held at connect/resume → ignore until released
            st.Blocked &= raw;               // released bits are unblocked
            uint effective = raw & ~st.Blocked;
            if ((effective & ~st.PrevEffective) != 0) LastActivePadId = id;
            st.PrevEffective = effective;
            merged |= effective;
        }
        if (_pads.Count != seen) RemoveStale(gen);

        List<UiAction>? output = null;
        uint pressed = merged & ~_prevMerged;

        // directions: press + repeat
        for (int d = LUp; d <= LRight; d++)
        {
            uint bit = 1u << d;
            if ((merged & bit) == 0) continue;
            ref var r = ref _repeat[d];
            if ((pressed & bit) != 0)
            {
                r.PressedAt = timestampMs;
                r.NextAt = timestampMs + _o.RepeatDelayMs;
                (output ??= new()).Add(DirectionAction(d));
            }
            else if (timestampMs >= r.NextAt)
            {
                (output ??= new()).Add(DirectionAction(d));
                int interval = timestampMs - r.PressedAt >= _o.AccelerateAfterMs ? _o.FastRepeatIntervalMs : _o.RepeatIntervalMs;
                r.NextAt += interval;
                if (r.NextAt <= timestampMs) r.NextAt = timestampMs + interval; // after a stall: no burst
            }
        }

        // everything else: press only
        for (int b = LSouth; b < LogicalCount; b++)
        {
            if ((pressed & (1u << b)) == 0) continue;
            (output ??= new()).Add(ButtonAction(b));
        }

        _prevMerged = merged;
        return output ?? (IReadOnlyList<UiAction>)Array.Empty<UiAction>();
    }

    private uint Evaluate(PadState st, in PadSnapshot s)
    {
        uint v = 0;
        var b = s.Buttons;
        if ((b & PadButtons.DPadUp) != 0) v |= 1u << LUp;
        if ((b & PadButtons.DPadDown) != 0) v |= 1u << LDown;
        if ((b & PadButtons.DPadLeft) != 0) v |= 1u << LLeft;
        if ((b & PadButtons.DPadRight) != 0) v |= 1u << LRight;

        // left stick with hysteresis per direction
        float x = Sanitize(s.LeftX), y = Sanitize(s.LeftY);
        st.StickRight = Hyst(st.StickRight, x);
        st.StickLeft = Hyst(st.StickLeft, -x);
        st.StickDown = Hyst(st.StickDown, y);
        st.StickUp = Hyst(st.StickUp, -y);
        bool xOn = st.StickLeft || st.StickRight, yOn = st.StickUp || st.StickDown;
        if (xOn && yOn)
        {
            float ax = MathF.Abs(x), ay = MathF.Abs(y);
            st.Dominant = st.Dominant switch
            {
                Axis.X => ay > ax * _o.StickAxisSwitchRatio ? Axis.Y : Axis.X,
                Axis.Y => ax > ay * _o.StickAxisSwitchRatio ? Axis.X : Axis.Y,
                _ => ax >= ay ? Axis.X : Axis.Y,
            };
        }
        else st.Dominant = xOn ? Axis.X : yOn ? Axis.Y : Axis.None;

        if (st.Dominant == Axis.X)
        {
            if (st.StickLeft) v |= 1u << LLeft;
            if (st.StickRight) v |= 1u << LRight;
        }
        else if (st.Dominant == Axis.Y)
        {
            if (st.StickUp) v |= 1u << LUp;
            if (st.StickDown) v |= 1u << LDown;
        }

        if ((b & PadButtons.South) != 0) v |= 1u << LSouth;
        if ((b & PadButtons.East) != 0) v |= 1u << LEast;
        if ((b & PadButtons.West) != 0) v |= 1u << LWest;
        if ((b & PadButtons.North) != 0) v |= 1u << LNorth;
        if ((b & PadButtons.Start) != 0) v |= 1u << LStart;
        if ((b & PadButtons.Back) != 0) v |= 1u << LSelect;
        if ((b & PadButtons.LeftShoulder) != 0) v |= 1u << LShoulderL;
        if ((b & PadButtons.RightShoulder) != 0) v |= 1u << LShoulderR;
        if ((b & PadButtons.Guide) != 0) v |= 1u << LGuide;

        float lt = Sanitize(s.LeftTrigger), rt = Sanitize(s.RightTrigger);
        st.TriggerL = st.TriggerL ? lt >= _o.TriggerRelease : lt > _o.TriggerPress;
        st.TriggerR = st.TriggerR ? rt >= _o.TriggerRelease : rt > _o.TriggerPress;
        if (st.TriggerL) v |= 1u << LTriggerL;
        if (st.TriggerR) v |= 1u << LTriggerR;
        return v;
    }

    private bool Hyst(bool on, float value) => on ? value >= _o.StickRelease : value >= _o.StickPress;

    private static float Sanitize(float v) => float.IsFinite(v) ? v : 0f;

    private void RemoveStale(int gen)
    {
        List<int>? stale = null;
        foreach (var (id, st) in _pads) if (st.SeenGeneration != gen) (stale ??= new()).Add(id);
        if (stale != null) foreach (var id in stale) _pads.Remove(id);
    }

    private static UiAction DirectionAction(int d) => d switch
    {
        LUp => UiAction.Up,
        LDown => UiAction.Down,
        LLeft => UiAction.Left,
        _ => UiAction.Right,
    };

    private UiAction ButtonAction(int b) => b switch
    {
        LSouth => _swapAcceptBack ? UiAction.Back : UiAction.Accept,
        LEast => _swapAcceptBack ? UiAction.Accept : UiAction.Back,
        LWest => UiAction.Favorite,
        LNorth => UiAction.Search,
        LStart => UiAction.Menu,
        LSelect => UiAction.View,
        LShoulderL => UiAction.PageLeft,
        LShoulderR => UiAction.PageRight,
        LTriggerL => UiAction.TriggerLeft,
        LTriggerR => UiAction.TriggerRight,
        _ => UiAction.Home,
    };
}
