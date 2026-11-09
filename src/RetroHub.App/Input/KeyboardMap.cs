using Avalonia.Input;

namespace RetroHub.App.Input;

/// <summary>Translates keyboard input into <see cref="NavAction"/>s (the keyboard always works, with or without a pad).</summary>
public static class KeyboardMap
{
    /// <summary>
    /// Returns the action for a key, or null. <paramref name="inTextInput"/> = the focused element edits text:
    /// then only keys that cannot be typed are mapped (Escape, Up/Down, Ctrl+…).
    /// Enter/Space are deliberately not mapped: buttons handle them natively.
    /// </summary>
    public static NavAction? Map(Key key, KeyModifiers mods, bool inTextInput)
    {
        var ctrl = mods.HasFlag(KeyModifiers.Control);
        switch (key)
        {
            case Key.Up: return NavAction.Up;
            case Key.Down: return NavAction.Down;
            case Key.Left: return inTextInput ? null : NavAction.Left;
            case Key.Right: return inTextInput ? null : NavAction.Right;
            case Key.Escape: return NavAction.Back;
            case Key.Back: return inTextInput ? null : NavAction.Back;
            case Key.BrowserBack: return NavAction.Back;
            case Key.BrowserSearch: return NavAction.Search;
            case Key.F when ctrl: return NavAction.Search;
            case Key.F3: return NavAction.Search;
            case Key.F when !inTextInput && mods == KeyModifiers.None: return NavAction.Favorite;
            case Key.Apps: return NavAction.Menu;
            case Key.F10 when !mods.HasFlag(KeyModifiers.Shift): return NavAction.Menu;
            case Key.V when !inTextInput && mods == KeyModifiers.None: return NavAction.View;
            case Key.PageUp when ctrl: return NavAction.PageLeft;
            case Key.PageDown when ctrl: return NavAction.PageRight;
            case Key.Tab when ctrl: return mods.HasFlag(KeyModifiers.Shift) ? NavAction.PageLeft : NavAction.PageRight;
            default: return null;
        }
    }
}
