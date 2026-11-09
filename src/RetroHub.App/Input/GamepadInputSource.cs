using RetroHub.Core.Logging;
using RetroHub.Input;

namespace RetroHub.App.Input;

/// <summary>
/// Adapts the gamepad service (RetroHub.Input: SDL3 with XInput fallback) to the shell's <see cref="IInputSource"/>.
/// Events come from the input thread; the shell marshals them to the UI thread and only acts while RetroHub is the
/// active window. While a game runs the source is suspended: SDL is shut down so it never touches the pads the
/// emulator is using.
/// </summary>
public sealed class GamepadInputSource : IInputSource
{
    private readonly GamepadService _service;
    private readonly object _gate = new();
    private bool _suspended;

    public event Action<NavAction, bool>? ActionRaised;
    public event Action? DirectionReleased;
    public event Action? ControllerChanged;

    private GamepadInputSource(GamepadService service)
    {
        _service = service;
        _service.ActionRepeated += OnAction;
        _service.DirectionsReleased += OnReleased;
        _service.Connected += OnPadChanged;
        _service.Disconnected += OnPadChanged;
        _service.LastUsedStyleChanged += _ => Raise(ControllerChanged);
    }

    /// <summary>Creates and starts the gamepad service; returns a <see cref="NullInputSource"/> if that fails.</summary>
    public static IInputSource TryCreate(bool swapAcceptBack)
    {
        try
        {
            var service = new GamepadService { SwapAcceptBack = swapAcceptBack };
            var source = new GamepadInputSource(service);
            service.Start();
            Log.Info($"Gamepad input: backend {service.Backend}, {service.Pads.Count} pad(s)");
            return source;
        }
        catch (Exception ex)
        {
            Log.Warn("Gamepad input unavailable – keyboard and mouse only", ex);
            return new NullInputSource();
        }
    }

    public string Backend => _service.Backend;

    public ControllerFamily Controller
    {
        get
        {
            if (_service.Pads.Count == 0) return ControllerFamily.None;
            return _service.LastUsedStyle switch
            {
                PadStyle.Xbox => ControllerFamily.Xbox,
                PadStyle.PlayStation => ControllerFamily.PlayStation,
                _ => ControllerFamily.Generic,
            };
        }
    }

    public string? ControllerName
    {
        get
        {
            var pads = _service.Pads;
            if (pads.Count == 0) return null;
            var style = _service.LastUsedStyle;
            return (pads.FirstOrDefault(p => p.Style == style) ?? pads[0]).Name;
        }
    }

    public void SetSuspended(bool suspended)
    {
        lock (_gate)
        {
            if (_suspended == suspended) return;
            _suspended = suspended;
            if (suspended) _service.Suspend(); else _service.Resume();
        }
    }

    /// <summary>Maps the gamepad's UI actions onto the shell's navigation actions.</summary>
    internal static NavAction? Map(UiAction action) => action switch
    {
        UiAction.Up => NavAction.Up,
        UiAction.Down => NavAction.Down,
        UiAction.Left => NavAction.Left,
        UiAction.Right => NavAction.Right,
        UiAction.Accept => NavAction.Accept,
        UiAction.Back => NavAction.Back,
        UiAction.Menu => NavAction.Menu,
        UiAction.View => NavAction.View,
        UiAction.Search => NavAction.Search,
        UiAction.Favorite => NavAction.Favorite,
        UiAction.PageLeft => NavAction.PageLeft,
        UiAction.PageRight => NavAction.PageRight,
        // Guide/Home opens the menu; triggers page like the shoulder buttons.
        UiAction.Home => NavAction.Menu,
        UiAction.TriggerLeft => NavAction.PageLeft,
        UiAction.TriggerRight => NavAction.PageRight,
        _ => null,
    };

    private void OnAction(UiAction action, bool isRepeat)
    {
        if (_suspended) return;
        if (Map(action) is { } nav)
        {
            try { ActionRaised?.Invoke(nav, isRepeat); } catch (Exception ex) { Log.Warn("Gamepad action handler failed", ex); }
        }
    }

    private void OnReleased()
    {
        if (_suspended) return;
        Raise(DirectionReleased);
    }

    private void OnPadChanged(GamepadInfo info) => Raise(ControllerChanged);

    private static void Raise(Action? handler)
    {
        try { handler?.Invoke(); } catch (Exception ex) { Log.Warn("Controller-changed handler failed", ex); }
    }

    public void Dispose()
    {
        _service.ActionRepeated -= OnAction;
        _service.DirectionsReleased -= OnReleased;
        _service.Dispose();
    }
}
