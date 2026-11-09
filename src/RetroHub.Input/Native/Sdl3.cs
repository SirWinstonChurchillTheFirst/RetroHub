using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RetroHub.Input.Native;

/// <summary>
/// Minimal hand-written SDL 3.4 bindings (gamepad subsystem only, no video/window).
/// All SDL functions use SDLCALL (= cdecl on Windows). SDL3 <c>bool</c> is the C99 1-byte bool,
/// marshalled as <see cref="UnmanagedType.U1"/>. Returned <c>const char*</c> are owned by SDL and must not be freed.
/// </summary>
internal static unsafe partial class Sdl3
{
    public const string Lib = "SDL3";

    // SDL_InitFlags (SDL_init.h)
    public const uint SDL_INIT_JOYSTICK = 0x00000200;
    public const uint SDL_INIT_GAMEPAD = 0x00002000; // implies JOYSTICK (and EVENTS)
    public const uint SDL_INIT_EVENTS = 0x00004000;

    // Hints (SDL_hints.h) - names verified against the strings in the shipped SDL3.dll (3.4.20)
    public const string SDL_HINT_JOYSTICK_ALLOW_BACKGROUND_EVENTS = "SDL_JOYSTICK_ALLOW_BACKGROUND_EVENTS";
    public const string SDL_HINT_JOYSTICK_ENHANCED_REPORTS = "SDL_JOYSTICK_ENHANCED_REPORTS";
    /// <summary>Windows: handle joystick detection + raw input messages on SDL's own thread (we have no message pump).</summary>
    public const string SDL_HINT_JOYSTICK_THREAD = "SDL_JOYSTICK_THREAD";
    /// <summary>Do not install SIGINT/SIGTERM handlers (we are a library inside an Avalonia app / test host).</summary>
    public const string SDL_HINT_NO_SIGNAL_HANDLERS = "SDL_NO_SIGNAL_HANDLERS";

    // SDL_GamepadButton (SDL_gamepad.h)
    public const int SDL_GAMEPAD_BUTTON_SOUTH = 0;
    public const int SDL_GAMEPAD_BUTTON_EAST = 1;
    public const int SDL_GAMEPAD_BUTTON_WEST = 2;
    public const int SDL_GAMEPAD_BUTTON_NORTH = 3;
    public const int SDL_GAMEPAD_BUTTON_BACK = 4;
    public const int SDL_GAMEPAD_BUTTON_GUIDE = 5;
    public const int SDL_GAMEPAD_BUTTON_START = 6;
    public const int SDL_GAMEPAD_BUTTON_LEFT_STICK = 7;
    public const int SDL_GAMEPAD_BUTTON_RIGHT_STICK = 8;
    public const int SDL_GAMEPAD_BUTTON_LEFT_SHOULDER = 9;
    public const int SDL_GAMEPAD_BUTTON_RIGHT_SHOULDER = 10;
    public const int SDL_GAMEPAD_BUTTON_DPAD_UP = 11;
    public const int SDL_GAMEPAD_BUTTON_DPAD_DOWN = 12;
    public const int SDL_GAMEPAD_BUTTON_DPAD_LEFT = 13;
    public const int SDL_GAMEPAD_BUTTON_DPAD_RIGHT = 14;
    /// <summary>Buttons 0..14 are the ones we read (MISC1 and paddles are ignored).</summary>
    public const int ButtonsWeRead = 15;

    // SDL_GamepadAxis. Sticks: -32768..32767 (Y: negative = up). Triggers: 0..32767.
    public const int SDL_GAMEPAD_AXIS_LEFTX = 0;
    public const int SDL_GAMEPAD_AXIS_LEFTY = 1;
    public const int SDL_GAMEPAD_AXIS_RIGHTX = 2;
    public const int SDL_GAMEPAD_AXIS_RIGHTY = 3;
    public const int SDL_GAMEPAD_AXIS_LEFT_TRIGGER = 4;
    public const int SDL_GAMEPAD_AXIS_RIGHT_TRIGGER = 5;

    // SDL_GamepadType
    public const int SDL_GAMEPAD_TYPE_UNKNOWN = 0;
    public const int SDL_GAMEPAD_TYPE_STANDARD = 1;
    public const int SDL_GAMEPAD_TYPE_XBOX360 = 2;
    public const int SDL_GAMEPAD_TYPE_XBOXONE = 3;
    public const int SDL_GAMEPAD_TYPE_PS3 = 4;
    public const int SDL_GAMEPAD_TYPE_PS4 = 5;
    public const int SDL_GAMEPAD_TYPE_PS5 = 6;
    public const int SDL_GAMEPAD_TYPE_NINTENDO_SWITCH_PRO = 7;
    public const int SDL_GAMEPAD_TYPE_NINTENDO_SWITCH_JOYCON_LEFT = 8;
    public const int SDL_GAMEPAD_TYPE_NINTENDO_SWITCH_JOYCON_RIGHT = 9;
    public const int SDL_GAMEPAD_TYPE_NINTENDO_SWITCH_JOYCON_PAIR = 10;
    public const int SDL_GAMEPAD_TYPE_GAMECUBE = 11; // new in SDL 3.4

    /// <summary>Maps an SDL_GamepadType to the glyph style used by the UI.</summary>
    public static PadStyle ToPadStyle(int sdlGamepadType) => sdlGamepadType switch
    {
        SDL_GAMEPAD_TYPE_XBOX360 or SDL_GAMEPAD_TYPE_XBOXONE => PadStyle.Xbox,
        SDL_GAMEPAD_TYPE_PS3 or SDL_GAMEPAD_TYPE_PS4 or SDL_GAMEPAD_TYPE_PS5 => PadStyle.PlayStation,
        SDL_GAMEPAD_TYPE_NINTENDO_SWITCH_PRO or SDL_GAMEPAD_TYPE_NINTENDO_SWITCH_JOYCON_LEFT
            or SDL_GAMEPAD_TYPE_NINTENDO_SWITCH_JOYCON_RIGHT or SDL_GAMEPAD_TYPE_NINTENDO_SWITCH_JOYCON_PAIR
            or SDL_GAMEPAD_TYPE_GAMECUBE => PadStyle.Nintendo,
        _ => PadStyle.Generic,
    };

    // ---- init / hints / errors -------------------------------------------------------------------------------

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool SDL_SetHint(string name, string value);

    [LibraryImport(Lib)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool SDL_Init(uint flags);

    [LibraryImport(Lib)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void SDL_QuitSubSystem(uint flags);

    /// <summary>Returns the subset of <paramref name="flags"/> that is initialised (0 → all initialised flags).</summary>
    [LibraryImport(Lib)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial uint SDL_WasInit(uint flags);

    /// <summary>SDL_VERSIONNUM: major*1000000 + minor*1000 + micro (e.g. 3004020).</summary>
    [LibraryImport(Lib)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int SDL_GetVersion();

    [LibraryImport(Lib)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial byte* SDL_GetError();

    [LibraryImport(Lib)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void SDL_free(void* mem);

    [LibraryImport(Lib)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void SDL_FlushEvents(uint minType, uint maxType);

    // ---- gamepads ---------------------------------------------------------------------------------------------

    [LibraryImport(Lib)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void SDL_UpdateGamepads();

    /// <summary>Returns a 0-terminated SDL_JoystickID array (free with <see cref="SDL_free"/>), or null on error.</summary>
    [LibraryImport(Lib)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial uint* SDL_GetGamepads(int* count);

    [LibraryImport(Lib)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial nint SDL_OpenGamepad(uint instanceId);

    [LibraryImport(Lib)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void SDL_CloseGamepad(nint gamepad);

    [LibraryImport(Lib)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool SDL_GamepadConnected(nint gamepad);

    [LibraryImport(Lib)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool SDL_GetGamepadButton(nint gamepad, int button);

    [LibraryImport(Lib)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial short SDL_GetGamepadAxis(nint gamepad, int axis);

    [LibraryImport(Lib)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int SDL_GetGamepadType(nint gamepad);

    [LibraryImport(Lib)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial byte* SDL_GetGamepadName(nint gamepad);

    [LibraryImport(Lib)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial uint SDL_GetGamepadID(nint gamepad);

    [LibraryImport(Lib)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool SDL_RumbleGamepad(nint gamepad, ushort lowFrequency, ushort highFrequency, uint durationMs);

    /// <summary>Disabling gamepad/joystick events: we poll state ourselves via <see cref="SDL_UpdateGamepads"/>,
    /// so nothing piles up in SDL's event queue (we never call SDL_PollEvent).</summary>
    [LibraryImport(Lib)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void SDL_SetGamepadEventsEnabled([MarshalAs(UnmanagedType.U1)] bool enabled);

    [LibraryImport(Lib)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void SDL_SetJoystickEventsEnabled([MarshalAs(UnmanagedType.U1)] bool enabled);

    // diagnostics: used by the tests to verify our enum constants against the loaded DLL
    [LibraryImport(Lib)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial byte* SDL_GetGamepadStringForType(int type);

    [LibraryImport(Lib)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial byte* SDL_GetGamepadStringForButton(int button);

    [LibraryImport(Lib)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial byte* SDL_GetGamepadStringForAxis(int axis);

    // ---- managed helpers --------------------------------------------------------------------------------------

    public static string GetError() => Utf8(SDL_GetError());
    public static string GetGamepadName(nint gamepad) => Utf8(SDL_GetGamepadName(gamepad));
    public static string GetStringForType(int type) => Utf8(SDL_GetGamepadStringForType(type));
    public static string GetStringForButton(int button) => Utf8(SDL_GetGamepadStringForButton(button));
    public static string GetStringForAxis(int axis) => Utf8(SDL_GetGamepadStringForAxis(axis));

    /// <summary>Currently attached gamepads (instance ids). Frees SDL's array. Empty on error.</summary>
    public static uint[] GetGamepads()
    {
        int count = 0;
        uint* ids = SDL_GetGamepads(&count);
        if (ids == null) return [];
        try
        {
            var result = new uint[Math.Max(0, count)];
            for (int i = 0; i < result.Length; i++) result[i] = ids[i];
            return result;
        }
        finally { SDL_free(ids); }
    }

    private static string Utf8(byte* p) => p == null ? "" : Marshal.PtrToStringUTF8((nint)p) ?? "";
}
