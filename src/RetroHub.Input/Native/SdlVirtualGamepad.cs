using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RetroHub.Input.Native;

/// <summary>
/// Diagnostics/testing: an SDL virtual gamepad (SDL_AttachVirtualJoystick) whose state is pushed from SDL's own
/// joystick update (the descriptor's <c>Update</c> callback), i.e. exactly like a physical device: the state only
/// becomes visible after an <c>SDL_UpdateGamepads</c> that ran while the device was open. Lets the SDL backend
/// (hot-plug, button/axis reading, type → glyph style, held-at-connect guard) be exercised without hardware.
/// Requires an initialised SDL joystick subsystem. Not used by the launcher itself.
/// </summary>
internal sealed unsafe partial class SdlVirtualGamepad : IDisposable
{
    private const ushort SDL_JOYSTICK_TYPE_GAMEPAD = 1;
    private const int NAxes = 6;

    /// <summary>SDL_VirtualJoystickDesc (SDL_joystick.h, SDL 3.2+; 136 bytes on 64-bit, 84 on 32-bit).</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct VirtualJoystickDesc
    {
        public uint Version;          // = sizeof(desc) (SDL_INIT_INTERFACE)
        public ushort Type;
        public ushort Padding;
        public ushort VendorId;
        public ushort ProductId;
        public ushort NAxes;
        public ushort NButtons;
        public ushort NBalls;
        public ushort NHats;
        public ushort NTouchpads;
        public ushort NSensors;
        public ushort Padding2a, Padding2b;
        public uint ButtonMask;
        public uint AxisMask;
        public nint Name;
        public nint Touchpads;
        public nint Sensors;
        public nint UserData;
        public nint Update;
        public nint SetPlayerIndex;
        public nint Rumble;
        public nint RumbleTriggers;
        public nint SetLed;
        public nint SendEffect;
        public nint SetSensorsEnabled;
        public nint Cleanup;
    }

    private static readonly ConcurrentDictionary<nint, SdlVirtualGamepad> Live = new();
    private static long _nextKey;

    private readonly nint _key;
    private readonly nint _name;
    private volatile uint _buttons;
    private volatile int _leftX, _leftY, _leftTrigger, _rightTrigger;
    private bool _disposed;

    /// <summary>The SDL joystick instance id of the virtual device.</summary>
    public uint InstanceId { get; private set; }

    private SdlVirtualGamepad(nint key, nint name) { _key = key; _name = name; }

    internal static int DescriptorSize => sizeof(VirtualJoystickDesc);

    /// <summary>Attaches a virtual gamepad (buttons 0..14, axes 0..5 map 1:1 to SDL gamepad buttons/axes).
    /// The vendor/product ids decide the reported SDL_GamepadType (e.g. 0x054C/0x0CE6 = PS5).</summary>
    public static SdlVirtualGamepad Attach(string name, ushort vendorId, ushort productId, PadButtons held = PadButtons.None)
    {
        var key = (nint)Interlocked.Increment(ref _nextKey);
        var pad = new SdlVirtualGamepad(key, Marshal.StringToCoTaskMemUTF8(name));
        pad.Set(held); // released triggers sit at the bottom of the joystick axis range
        Live[key] = pad;
        var desc = new VirtualJoystickDesc
        {
            Version = (uint)sizeof(VirtualJoystickDesc),
            Type = SDL_JOYSTICK_TYPE_GAMEPAD,
            VendorId = vendorId,
            ProductId = productId,
            NAxes = NAxes,
            NButtons = Sdl3.ButtonsWeRead,
            ButtonMask = (1u << Sdl3.ButtonsWeRead) - 1,
            AxisMask = (1u << NAxes) - 1,
            Name = pad._name,
            UserData = key,
            Update = (nint)(delegate* unmanaged[Cdecl]<nint, void>)&OnUpdate,
        };
        uint id = SDL_AttachVirtualJoystick(&desc);
        if (id == 0)
        {
            var err = Sdl3.GetError();
            pad.Dispose();
            throw new InvalidOperationException($"SDL_AttachVirtualJoystick failed: {err}");
        }
        pad.InstanceId = id;
        return pad;
    }

    /// <summary>Sets the state the device reports on SDL's next update (stick/trigger -1..1 / 0..1).</summary>
    public void Set(PadButtons buttons, float leftX = 0, float leftY = 0, float leftTrigger = 0, float rightTrigger = 0)
    {
        _leftX = ToAxis(leftX);
        _leftY = ToAxis(leftY);
        _leftTrigger = ToAxis(leftTrigger * 2 - 1); // virtual trigger axes span the full joystick range
        _rightTrigger = ToAxis(rightTrigger * 2 - 1);
        _buttons = (uint)buttons;
    }

    private static int ToAxis(float v) => (int)Math.Round(Math.Clamp(v, -1f, 1f) * 32767);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnUpdate(nint userData)
    {
        try
        {
            if (!Live.TryGetValue(userData, out var pad) || pad.InstanceId == 0) return;
            nint joystick = SDL_GetJoystickFromID(pad.InstanceId);
            if (joystick == 0) return;
            uint b = pad._buttons;
            for (int i = 0; i < Sdl3.ButtonsWeRead; i++) SDL_SetJoystickVirtualButton(joystick, i, (b & (1u << i)) != 0);
            SDL_SetJoystickVirtualAxis(joystick, Sdl3.SDL_GAMEPAD_AXIS_LEFTX, (short)pad._leftX);
            SDL_SetJoystickVirtualAxis(joystick, Sdl3.SDL_GAMEPAD_AXIS_LEFTY, (short)pad._leftY);
            SDL_SetJoystickVirtualAxis(joystick, Sdl3.SDL_GAMEPAD_AXIS_LEFT_TRIGGER, (short)pad._leftTrigger);
            SDL_SetJoystickVirtualAxis(joystick, Sdl3.SDL_GAMEPAD_AXIS_RIGHT_TRIGGER, (short)pad._rightTrigger);
        }
        catch
        {
            // never let an exception cross the native boundary
        }
    }

    /// <summary>Detaches the device (a hot-unplug for whoever has it open).</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (InstanceId != 0 && Sdl3.SDL_WasInit(Sdl3.SDL_INIT_JOYSTICK) != 0) SDL_DetachVirtualJoystick(InstanceId);
        Live.TryRemove(_key, out _);
        Marshal.FreeCoTaskMem(_name);
    }

    [LibraryImport(Sdl3.Lib)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial uint SDL_AttachVirtualJoystick(VirtualJoystickDesc* desc);

    [LibraryImport(Sdl3.Lib)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool SDL_DetachVirtualJoystick(uint instanceId);

    [LibraryImport(Sdl3.Lib)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial nint SDL_GetJoystickFromID(uint instanceId);

    [LibraryImport(Sdl3.Lib)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool SDL_SetJoystickVirtualButton(nint joystick, int button, [MarshalAs(UnmanagedType.U1)] bool down);

    [LibraryImport(Sdl3.Lib)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool SDL_SetJoystickVirtualAxis(nint joystick, int axis, short value);
}
