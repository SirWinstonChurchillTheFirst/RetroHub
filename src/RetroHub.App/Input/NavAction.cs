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

/// <summary>Which button glyphs the hint bar shows.</summary>
public enum GlyphStyle { Auto, Xbox, PlayStation }

/// <summary>Controller family reported by an input source (decides the glyphs in <see cref="GlyphStyle.Auto"/>).</summary>
public enum ControllerFamily { None, Xbox, PlayStation, Generic }
