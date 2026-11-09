using System.Runtime.InteropServices;
using RetroHub.Core.Logging;

namespace RetroHub.Input.Native;

/// <summary>XINPUT_GAMEPAD (plus the reserved DWORD XInputGetStateEx may write).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct XInputGamepad
{
    public ushort Buttons;
    public byte LeftTrigger;
    public byte RightTrigger;
    public short ThumbLX;
    public short ThumbLY;
    public short ThumbRX;
    public short ThumbRY;
}

[StructLayout(LayoutKind.Sequential)]
internal struct XInputState
{
    public uint PacketNumber;
    public XInputGamepad Gamepad;
    /// <summary>XINPUT_STATE_EX has an extra reserved DWORD; keep room so ordinal 100 never writes past us.</summary>
    public uint Reserved0;
    public uint Reserved1;
}

/// <summary>XINPUT_VIBRATION: left = low-frequency motor, right = high-frequency motor.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct XInputVibration
{
    public ushort LeftMotorSpeed;
    public ushort RightMotorSpeed;
}

/// <summary>
/// XInput loaded dynamically: xinput1_4.dll (Windows 8+), else xinput9_1_0.dll (Vista/7 compatible, no Guide).
/// The undocumented ordinal 100 (XInputGetStateEx) is used when present, because it reports the Guide button.
/// Thread-safe; never throws.
/// </summary>
internal static unsafe partial class XInput
{
    public const ushort DPAD_UP = 0x0001, DPAD_DOWN = 0x0002, DPAD_LEFT = 0x0004, DPAD_RIGHT = 0x0008;
    public const ushort START = 0x0010, BACK = 0x0020, LEFT_THUMB = 0x0040, RIGHT_THUMB = 0x0080;
    public const ushort LEFT_SHOULDER = 0x0100, RIGHT_SHOULDER = 0x0200, GUIDE = 0x0400;
    public const ushort A = 0x1000, B = 0x2000, X = 0x4000, Y = 0x8000;
    public const int MaxUsers = 4;
    public const uint ERROR_SUCCESS = 0;
    public const uint ERROR_DEVICE_NOT_CONNECTED = 1167;

    private static readonly object InitLock = new();
    private static bool _initialised;
    private static delegate* unmanaged[Stdcall]<uint, XInputState*, uint> _getState;
    private static delegate* unmanaged[Stdcall]<uint, XInputState*, uint> _getStateEx;
    private static delegate* unmanaged[Stdcall]<uint, XInputVibration*, uint> _setState;

    /// <summary>Name of the loaded DLL, or null when XInput is unavailable.</summary>
    public static string? LoadedLibrary { get; private set; }
    public static bool IsAvailable { get { EnsureLoaded(); return _getState != null; } }
    /// <summary>True when XInputGetStateEx (ordinal 100) is available, i.e. the Guide button can be read.</summary>
    public static bool HasGuide { get { EnsureLoaded(); return _getStateEx != null; } }

    [LibraryImport("kernel32.dll", EntryPoint = "GetProcAddress")]
    private static partial nint GetProcAddressOrdinal(nint module, nint ordinal);

    private static void EnsureLoaded()
    {
        if (Volatile.Read(ref _initialised)) return;
        lock (InitLock)
        {
            if (_initialised) return;
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    foreach (var name in new[] { "xinput1_4.dll", "xinput9_1_0.dll" })
                    {
                        if (!NativeLibrary.TryLoad(name, out var h)) continue;
                        if (!NativeLibrary.TryGetExport(h, "XInputGetState", out var fn)) continue;
                        _getState = (delegate* unmanaged[Stdcall]<uint, XInputState*, uint>)fn;
                        LoadedLibrary = name;
                        if (NativeLibrary.TryGetExport(h, "XInputSetState", out var set))
                            _setState = (delegate* unmanaged[Stdcall]<uint, XInputVibration*, uint>)set;
                        var ex = GetProcAddressOrdinal(h, 100);
                        if (ex != 0) _getStateEx = (delegate* unmanaged[Stdcall]<uint, XInputState*, uint>)ex;
                        break;
                    }
                }
                if (_getState == null) Log.Info("Input: XInput not available");
                else Log.Info($"Input: XInput loaded from {LoadedLibrary} (Guide button {(_getStateEx != null ? "available" : "not available")})");
            }
            catch (Exception ex)
            {
                Log.Warn("Input: loading XInput failed", ex);
                _getState = null;
                _getStateEx = null;
            }
            Volatile.Write(ref _initialised, true);
        }
    }

    /// <summary>
    /// Reads one user slot (0..3). Uses XInputGetStateEx when available (and allowed) so <see cref="GUIDE"/> is reported.
    /// Returns false when the slot is empty or XInput is unavailable.
    /// </summary>
    public static bool TryGetState(int userIndex, out XInputState state, bool useEx = true)
    {
        state = default;
        EnsureLoaded();
        if (_getState == null || (uint)userIndex >= MaxUsers) return false;
        try
        {
            XInputState s = default;
            uint rc;
            if (useEx && _getStateEx != null) rc = _getStateEx((uint)userIndex, &s);
            else rc = _getState((uint)userIndex, &s);
            if (rc != ERROR_SUCCESS) return false;
            if (!useEx || _getStateEx == null) s.Gamepad.Buttons &= unchecked((ushort)~GUIDE);
            state = s;
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn($"Input: XInputGetState({userIndex}) failed", ex);
            return false;
        }
    }

    /// <summary>Sets both rumble motors (0..65535); false when unavailable or the slot is empty.</summary>
    public static bool SetVibration(int userIndex, ushort low, ushort high)
    {
        EnsureLoaded();
        if (_setState == null || (uint)userIndex >= MaxUsers) return false;
        try
        {
            var v = new XInputVibration { LeftMotorSpeed = low, RightMotorSpeed = high };
            return _setState((uint)userIndex, &v) == ERROR_SUCCESS;
        }
        catch (Exception ex)
        {
            Log.Warn($"Input: XInputSetState({userIndex}) failed", ex);
            return false;
        }
    }

    /// <summary>Converts an XInput pad into our backend-neutral snapshot (Y axis: + = down, like SDL).</summary>
    public static PadSnapshot ToSnapshot(in XInputGamepad g)
    {
        PadButtons b = PadButtons.None;
        ushort w = g.Buttons;
        if ((w & A) != 0) b |= PadButtons.South;
        if ((w & B) != 0) b |= PadButtons.East;
        if ((w & X) != 0) b |= PadButtons.West;
        if ((w & Y) != 0) b |= PadButtons.North;
        if ((w & BACK) != 0) b |= PadButtons.Back;
        if ((w & GUIDE) != 0) b |= PadButtons.Guide;
        if ((w & START) != 0) b |= PadButtons.Start;
        if ((w & LEFT_THUMB) != 0) b |= PadButtons.LeftStick;
        if ((w & RIGHT_THUMB) != 0) b |= PadButtons.RightStick;
        if ((w & LEFT_SHOULDER) != 0) b |= PadButtons.LeftShoulder;
        if ((w & RIGHT_SHOULDER) != 0) b |= PadButtons.RightShoulder;
        if ((w & DPAD_UP) != 0) b |= PadButtons.DPadUp;
        if ((w & DPAD_DOWN) != 0) b |= PadButtons.DPadDown;
        if ((w & DPAD_LEFT) != 0) b |= PadButtons.DPadLeft;
        if ((w & DPAD_RIGHT) != 0) b |= PadButtons.DPadRight;
        return new PadSnapshot(b,
            Axis(g.ThumbLX), -Axis(g.ThumbLY),
            g.LeftTrigger / 255f, g.RightTrigger / 255f);
    }

    private static float Axis(short v) => Math.Clamp(v / 32767f, -1f, 1f);
}
