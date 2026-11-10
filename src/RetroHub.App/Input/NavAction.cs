namespace RetroHub.App.Input;

/// <summary>
/// Abstract UI actions. Keyboard, mouse buttons and (later) gamepads are translated into these; the shell's
/// <see cref="NavRouter"/> executes them. Gamepad mapping (Xbox / PlayStation):
/// D-pad/stick = directions, A/Cross = Accept, B/Circle = Back, X/Square = Favorite, Y/Triangle = Search,
/// Start/Options = Menu, View/Share = View, LB/L1 = PageLeft, RB/R1 = PageRight.
/// </summary>
public enum NavAction
{
    Up,
    Down,
    Left,
    Right,
    Accept,
    Back,
    Menu,
    View,
    Search,
    Favorite,
    PageLeft,
    PageRight,
}

/// <summary>Controller family reported by an input source (only pad or keyboard glyphs follow from it; DESIGN 5.3).</summary>
public enum ControllerFamily { None, Xbox, PlayStation, Generic }
