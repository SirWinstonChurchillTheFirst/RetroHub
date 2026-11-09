namespace RetroHub.App.Input;

/// <summary>
/// Seam for controller input. The gamepad service (RetroHub.Input, SDL3/XInput) will be adapted to this interface
/// later; the shell only depends on it. Events may be raised on any thread – the shell marshals them to the UI thread.
/// </summary>
public interface IInputSource : IDisposable
{
    /// <summary>A UI action (already debounced / with key repeat applied by the source).</summary>
    event Action<NavAction>? ActionRaised;

    /// <summary>Raised when a controller was connected or disconnected.</summary>
    event Action? ControllerChanged;

    /// <summary>Family of the most recently used controller; <see cref="ControllerFamily.None"/> when none is connected.</summary>
    ControllerFamily Controller { get; }

    /// <summary>Display name of the active controller (null when none).</summary>
    string? ControllerName { get; }

    /// <summary>
    /// Pauses the source while a game runs (the emulator owns the pads then) and resumes it afterwards.
    /// Must be idempotent.
    /// </summary>
    void SetSuspended(bool suspended);
}

/// <summary>No controller support (used until the gamepad module is plugged in, and in tests).</summary>
public sealed class NullInputSource : IInputSource
{
    public event Action<NavAction>? ActionRaised { add { } remove { } }
    public event Action? ControllerChanged { add { } remove { } }
    public ControllerFamily Controller => ControllerFamily.None;
    public string? ControllerName => null;
    public void SetSuspended(bool suspended) { }
    public void Dispose() { }
}
