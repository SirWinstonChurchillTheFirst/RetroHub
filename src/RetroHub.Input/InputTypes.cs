namespace RetroHub.Input;

/// <summary>Abstract launcher UI actions produced from gamepad input.</summary>
public enum UiAction
{
    Up, Down, Left, Right,
    /// <summary>South button (A / Cross); East when <see cref="GamepadService.SwapAcceptBack"/> is on.</summary>
    Accept,
    /// <summary>East button (B / Circle); South when <see cref="GamepadService.SwapAcceptBack"/> is on.</summary>
    Back,
    /// <summary>Start / Options / +.</summary>
    Menu,
    /// <summary>Back / Select / Share / −.</summary>
    View,
    /// <summary>North button (Y / Triangle).</summary>
    Search,
    /// <summary>West button (X / Square).</summary>
    Favorite,
    /// <summary>Left shoulder (LB / L1 / L).</summary>
    PageLeft,
    /// <summary>Right shoulder (RB / R1 / R).</summary>
    PageRight,
    /// <summary>Left trigger (LT / L2 / ZL) pressed past the threshold.</summary>
    TriggerLeft,
    /// <summary>Right trigger (RT / R2 / ZR) pressed past the threshold.</summary>
    TriggerRight,
    /// <summary>Guide / PS / Home button.</summary>
    Home,
}

/// <summary>Button glyph family to show in the UI.</summary>
public enum PadStyle { Xbox, PlayStation, Nintendo, Generic }

/// <summary>A connected gamepad. <paramref name="InstanceId"/> is the SDL joystick instance id (SDL3 backend)
/// or the XInput user slot 0..3 (XInput backend).</summary>
public sealed record GamepadInfo(int InstanceId, string Name, PadStyle Style);

/// <summary>Gamepad buttons in SDL3 positional naming. Bit n = SDL_GamepadButton n.</summary>
[Flags]
public enum PadButtons : uint
{
    None = 0,
    /// <summary>Bottom face button (Xbox A, PS Cross, Nintendo B).</summary>
    South = 1u << 0,
    /// <summary>Right face button (Xbox B, PS Circle, Nintendo A).</summary>
    East = 1u << 1,
    /// <summary>Left face button (Xbox X, PS Square, Nintendo Y).</summary>
    West = 1u << 2,
    /// <summary>Top face button (Xbox Y, PS Triangle, Nintendo X).</summary>
    North = 1u << 3,
    Back = 1u << 4,
    Guide = 1u << 5,
    Start = 1u << 6,
    LeftStick = 1u << 7,
    RightStick = 1u << 8,
    LeftShoulder = 1u << 9,
    RightShoulder = 1u << 10,
    DPadUp = 1u << 11,
    DPadDown = 1u << 12,
    DPadLeft = 1u << 13,
    DPadRight = 1u << 14,
}

/// <summary>
/// One poll of one pad, backend-neutral. Sticks are -1..1 with <b>+Y = down</b> (SDL convention);
/// triggers are 0..1.
/// </summary>
public readonly record struct PadSnapshot(PadButtons Buttons, float LeftX = 0, float LeftY = 0,
    float LeftTrigger = 0, float RightTrigger = 0)
{
    public bool Has(PadButtons b) => (Buttons & b) == b;
}

/// <summary>The snapshot of one pad identified by a backend-specific id.</summary>
public readonly record struct PadInput(int PadId, PadSnapshot State);
