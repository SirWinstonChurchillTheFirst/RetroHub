using RetroHub.Input;
using RetroHub.Input.Native;
using Xunit;

namespace RetroHub.Core.Tests.Input;

/// <summary>All tests that touch SDL run serially (SDL's subsystem state is process-wide).</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class Sdl3Collection
{
    public const string Name = "SDL3 (process-wide state)";
}

[Collection(Sdl3Collection.Name)]
public class Sdl3NativeTests
{
    [Fact]
    public void Sdl3_dll_is_delivered_to_the_output_folder()
    {
        // RID-less projects (like this test project) get runtimes/win-<arch>/native/SDL3.dll + a deps.json entry;
        // RID-specific ones (RetroHub.App, win-x64) get SDL3.dll flat next to the exe.
        var found = NativeLibraryResolver.Sdl3Candidates().Where(File.Exists).ToList();
        Assert.NotEmpty(found);
        Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, "runtimes", "win-x64", "native", "SDL3.dll"))
                    || File.Exists(Path.Combine(AppContext.BaseDirectory, "SDL3.dll")));
    }

    [Fact]
    public void Loads_sdl_3_4()
    {
        int v = Sdl3.SDL_GetVersion();
        Assert.True(v >= 3_004_000, $"SDL version {SdlBackend.FormatVersion(v)}");
    }

    [Fact]
    public void Button_constants_match_the_loaded_sdl()
    {
        Assert.Equal("a", Sdl3.GetStringForButton(Sdl3.SDL_GAMEPAD_BUTTON_SOUTH));
        Assert.Equal("b", Sdl3.GetStringForButton(Sdl3.SDL_GAMEPAD_BUTTON_EAST));
        Assert.Equal("x", Sdl3.GetStringForButton(Sdl3.SDL_GAMEPAD_BUTTON_WEST));
        Assert.Equal("y", Sdl3.GetStringForButton(Sdl3.SDL_GAMEPAD_BUTTON_NORTH));
        Assert.Equal("back", Sdl3.GetStringForButton(Sdl3.SDL_GAMEPAD_BUTTON_BACK));
        Assert.Equal("guide", Sdl3.GetStringForButton(Sdl3.SDL_GAMEPAD_BUTTON_GUIDE));
        Assert.Equal("start", Sdl3.GetStringForButton(Sdl3.SDL_GAMEPAD_BUTTON_START));
        Assert.Equal("leftstick", Sdl3.GetStringForButton(Sdl3.SDL_GAMEPAD_BUTTON_LEFT_STICK));
        Assert.Equal("rightstick", Sdl3.GetStringForButton(Sdl3.SDL_GAMEPAD_BUTTON_RIGHT_STICK));
        Assert.Equal("leftshoulder", Sdl3.GetStringForButton(Sdl3.SDL_GAMEPAD_BUTTON_LEFT_SHOULDER));
        Assert.Equal("rightshoulder", Sdl3.GetStringForButton(Sdl3.SDL_GAMEPAD_BUTTON_RIGHT_SHOULDER));
        Assert.Equal("dpup", Sdl3.GetStringForButton(Sdl3.SDL_GAMEPAD_BUTTON_DPAD_UP));
        Assert.Equal("dpdown", Sdl3.GetStringForButton(Sdl3.SDL_GAMEPAD_BUTTON_DPAD_DOWN));
        Assert.Equal("dpleft", Sdl3.GetStringForButton(Sdl3.SDL_GAMEPAD_BUTTON_DPAD_LEFT));
        Assert.Equal("dpright", Sdl3.GetStringForButton(Sdl3.SDL_GAMEPAD_BUTTON_DPAD_RIGHT));
        // our PadButtons bits are SDL button indices
        Assert.Equal(1u << Sdl3.SDL_GAMEPAD_BUTTON_GUIDE, (uint)PadButtons.Guide);
        Assert.Equal(1u << Sdl3.SDL_GAMEPAD_BUTTON_DPAD_RIGHT, (uint)PadButtons.DPadRight);
    }

    [Fact]
    public void Axis_constants_match_the_loaded_sdl()
    {
        Assert.Equal("leftx", Sdl3.GetStringForAxis(Sdl3.SDL_GAMEPAD_AXIS_LEFTX));
        Assert.Equal("lefty", Sdl3.GetStringForAxis(Sdl3.SDL_GAMEPAD_AXIS_LEFTY));
        Assert.Equal("rightx", Sdl3.GetStringForAxis(Sdl3.SDL_GAMEPAD_AXIS_RIGHTX));
        Assert.Equal("righty", Sdl3.GetStringForAxis(Sdl3.SDL_GAMEPAD_AXIS_RIGHTY));
        Assert.Equal("lefttrigger", Sdl3.GetStringForAxis(Sdl3.SDL_GAMEPAD_AXIS_LEFT_TRIGGER));
        Assert.Equal("righttrigger", Sdl3.GetStringForAxis(Sdl3.SDL_GAMEPAD_AXIS_RIGHT_TRIGGER));
    }

    [Theory]
    [InlineData(Sdl3.SDL_GAMEPAD_TYPE_STANDARD, "standard", PadStyle.Generic)]
    [InlineData(Sdl3.SDL_GAMEPAD_TYPE_XBOX360, "xbox360", PadStyle.Xbox)]
    [InlineData(Sdl3.SDL_GAMEPAD_TYPE_XBOXONE, "xboxone", PadStyle.Xbox)]
    [InlineData(Sdl3.SDL_GAMEPAD_TYPE_PS3, "ps3", PadStyle.PlayStation)]
    [InlineData(Sdl3.SDL_GAMEPAD_TYPE_PS4, "ps4", PadStyle.PlayStation)]
    [InlineData(Sdl3.SDL_GAMEPAD_TYPE_PS5, "ps5", PadStyle.PlayStation)]
    [InlineData(Sdl3.SDL_GAMEPAD_TYPE_NINTENDO_SWITCH_PRO, "switchpro", PadStyle.Nintendo)]
    [InlineData(Sdl3.SDL_GAMEPAD_TYPE_NINTENDO_SWITCH_JOYCON_LEFT, "joyconleft", PadStyle.Nintendo)]
    [InlineData(Sdl3.SDL_GAMEPAD_TYPE_NINTENDO_SWITCH_JOYCON_RIGHT, "joyconright", PadStyle.Nintendo)]
    [InlineData(Sdl3.SDL_GAMEPAD_TYPE_NINTENDO_SWITCH_JOYCON_PAIR, "joyconpair", PadStyle.Nintendo)]
    [InlineData(Sdl3.SDL_GAMEPAD_TYPE_GAMECUBE, "gamecube", PadStyle.Nintendo)]
    public void Gamepad_type_constants_match_the_loaded_sdl(int type, string sdlName, PadStyle style)
    {
        Assert.Equal(sdlName, Sdl3.GetStringForType(type));
        Assert.Equal(style, Sdl3.ToPadStyle(type));
    }

    [Fact]
    public void Unknown_gamepad_type_is_generic()
    {
        Assert.Equal(PadStyle.Generic, Sdl3.ToPadStyle(Sdl3.SDL_GAMEPAD_TYPE_UNKNOWN));
        Assert.Equal(PadStyle.Generic, Sdl3.ToPadStyle(999));
    }

    [Fact]
    public void Init_flag_and_bool_marshalling_work()
    {
        Assert.Equal(0u, Sdl3.SDL_WasInit(Sdl3.SDL_INIT_GAMEPAD));
        Assert.True(Sdl3.SDL_SetHint(Sdl3.SDL_HINT_NO_SIGNAL_HANDLERS, "1"));
        Assert.True(Sdl3.SDL_Init(Sdl3.SDL_INIT_GAMEPAD), Sdl3.GetError());
        try
        {
            Assert.Equal(Sdl3.SDL_INIT_GAMEPAD, Sdl3.SDL_WasInit(Sdl3.SDL_INIT_GAMEPAD));
            Assert.Equal(Sdl3.SDL_INIT_JOYSTICK, Sdl3.SDL_WasInit(Sdl3.SDL_INIT_JOYSTICK)); // implied
            Sdl3.SDL_UpdateGamepads();
            var ids = Sdl3.GetGamepads(); // may be empty without a controller
            Assert.NotNull(ids);
            Assert.False(Sdl3.SDL_GamepadConnected(0)); // null gamepad → false (and SDL sets an error)
            Assert.Equal(0, Sdl3.SDL_OpenGamepad(0xFFFFFFF0)); // invalid id → NULL
            Assert.NotEqual("", Sdl3.GetError());
        }
        finally
        {
            Sdl3.SDL_QuitSubSystem(Sdl3.SDL_INIT_GAMEPAD);
        }
        Assert.Equal(0u, Sdl3.SDL_WasInit(Sdl3.SDL_INIT_GAMEPAD));
    }
}

public class XInputNativeTests
{
    [Fact]
    public void XInput_loads_on_windows()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only");
        Assert.True(XInput.IsAvailable);
        Assert.Equal("xinput1_4.dll", XInput.LoadedLibrary); // Windows 8+
        Assert.False(XInput.TryGetState(7, out _));           // invalid slot
        _ = XInput.HasGuide;                                   // must not throw
    }

    [Fact]
    public void XInput_state_is_converted_to_sdl_conventions()
    {
        var g = new XInputGamepad
        {
            Buttons = XInput.A | XInput.Y | XInput.BACK | XInput.START | XInput.GUIDE | XInput.DPAD_LEFT | XInput.RIGHT_SHOULDER,
            LeftTrigger = 255,
            RightTrigger = 0,
            ThumbLX = short.MaxValue,
            ThumbLY = short.MaxValue, // XInput: + = up
        };
        var s = XInput.ToSnapshot(g);
        Assert.Equal(PadButtons.South | PadButtons.North | PadButtons.Back | PadButtons.Start | PadButtons.Guide
                     | PadButtons.DPadLeft | PadButtons.RightShoulder, s.Buttons);
        Assert.Equal(1f, s.LeftX);
        Assert.Equal(-1f, s.LeftY); // SDL: - = up
        Assert.Equal(1f, s.LeftTrigger);
        Assert.Equal(0f, s.RightTrigger);
        Assert.Equal(-1f, XInput.ToSnapshot(new XInputGamepad { ThumbLX = short.MinValue }).LeftX); // clamped
    }
}
