using Avalonia.Input;
using Avalonia.Threading;
using RetroHub.App.Theming;

namespace RetroHub.App.Input;

/// <summary>
/// Detects a HELD direction (auto-repeat) for the shell (DESIGN 8.4). Gamepad: the input translator flags repeat
/// events and reports the release of all directions. Keyboard: a second KeyDown of the same arrow without KeyUp is a
/// repeat, KeyUp ends it. <see cref="IsActive"/> turns on with the FIRST repeat event (a single press never sets it)
/// and off on release or <see cref="Motion.RepeatRelease"/> (150 ms) after the last repeat – whichever comes first
/// (safety net for lost KeyUps). While active: no lift transition, no cone, rows glide 90 ms linear, decodes wait,
/// TV light and stage pause; afterwards only their dwell timer restarts.
/// </summary>
public sealed class NavRepeatTracker
{
    private readonly DispatcherTimer _release;
    private Key? _heldKey;
    private long _lastRepeat;

    public NavRepeatTracker()
    {
        _release = new DispatcherTimer(DispatcherPriority.Input) { Interval = Motion.RepeatRelease };
        _release.Tick += (_, _) =>
        {
            _release.Stop();
            SetActive(false);
        };
    }

    /// <summary>The shell's tracker (one shell per process); null before the window exists.</summary>
    public static NavRepeatTracker? Current { get; set; }

    public bool IsActive { get; private set; }

    /// <summary>Raised when <see cref="IsActive"/> changes (UI thread).</summary>
    public event Action<bool>? Changed;

    /// <summary>A direction action from the gamepad (UI thread).</summary>
    public void OnPadDirection(bool isRepeat)
    {
        if (isRepeat) Repeat();
    }

    /// <summary>All pad directions were released (UI thread).</summary>
    public void OnPadReleased() => End();

    /// <summary>Keyboard KeyDown; returns true when it was an auto-repeat of a held arrow key.</summary>
    public bool OnKeyDown(Key key)
    {
        if (key is not (Key.Up or Key.Down or Key.Left or Key.Right or Key.PageUp or Key.PageDown))
        {
            _heldKey = null;
            return false;
        }
        if (_heldKey == key)
        {
            Repeat();
            return true;
        }
        _heldKey = key;
        return false;
    }

    public void OnKeyUp(Key key)
    {
        if (_heldKey == key) _heldKey = null;
        End();
    }

    private void Repeat()
    {
        _lastRepeat = Environment.TickCount64;
        SetActive(true);
        _release.Stop();
        _release.Start();
    }

    private void End()
    {
        _release.Stop();
        SetActive(false);
    }

    /// <summary>For tests: ends a repeat whose release timer could not run (the headless dispatcher does not drive timers).</summary>
    public void ExpireIfStale()
    {
        if (IsActive && Environment.TickCount64 - _lastRepeat > Motion.RepeatRelease.TotalMilliseconds) End();
    }

    /// <summary>Raised by any tracker when its state changes (focus layers redraw their cone).</summary>
    public static event Action<bool>? AnyChanged;

    private void SetActive(bool on)
    {
        if (IsActive == on) return;
        IsActive = on;
        Changed?.Invoke(on);
        AnyChanged?.Invoke(on);
    }
}
