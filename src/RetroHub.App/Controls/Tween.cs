using Avalonia;
using Avalonia.Controls;

namespace RetroHub.App.Controls;

/// <summary>
/// A small time function driven by the render loop (DESIGN 9.6): <c>apply(progress)</c> runs once per frame with the
/// share of the duration that has passed (0 … 1), the last call is always <c>apply(1)</c>. Only render transforms,
/// opacity and what a control draws itself are changed in it – never layout. A zero duration (motion off, or the
/// caller's own rule for reduced motion) applies the end state at once; a control without a window (closed, not yet
/// shown) too. Frames, not a timer: an animation never hangs half-way when the window stops rendering.
/// <para><see cref="Hold"/> is the preview hook: while it is set, every tween started stands still at that moment of its
/// time (a tween shorter than that is done) – the design harness renders one coherent frame from the middle of a
/// movement; stills cannot show motion otherwise.</para>
/// </summary>
public sealed class Tween
{
    /// <summary>Preview/test hook: newly started tweens stand still this far into their time (null = they run).</summary>
    public static TimeSpan? Hold { get; set; }

    /// <summary>Tweens started with a non-zero duration (tests: nothing runs under "Aus").</summary>
    public static int Started { get; private set; }

    private static readonly List<Tween> Held = new();

    /// <summary>Ends every tween <see cref="Hold"/> stopped half-way (their end states and ends run), and lifts the hold.</summary>
    public static void ReleaseHeld()
    {
        Hold = null;
        var held = Held.ToList();
        Held.Clear();
        foreach (var t in held) t.Finish();
    }

    private readonly Visual _owner;
    private readonly double _ms;
    private readonly Action<double> _apply;
    private readonly Action? _done;
    private readonly System.Diagnostics.Stopwatch _clock = new();
    private bool _over;

    private Tween(Visual owner, double ms, Action<double> apply, Action? done)
    {
        _owner = owner;
        _ms = ms;
        _apply = apply;
        _done = done;
    }

    /// <summary>The tween is running (or held by <see cref="Hold"/>).</summary>
    public bool IsRunning => !_over;

    /// <summary>Starts a tween on <paramref name="owner"/>'s window; see the class summary.</summary>
    public static Tween Run(Visual owner, TimeSpan duration, Action<double> apply, Action? done = null)
    {
        var t = new Tween(owner, duration.TotalMilliseconds, apply, done);
        t.Begin();
        return t;
    }

    private void Begin()
    {
        if (_ms <= 0 || TopLevel.GetTopLevel(_owner) is not { } top)
        {
            Finish();
            return;
        }
        Started++;
        if (Hold is { } h)
        {
            var p = h.TotalMilliseconds / _ms;
            if (p >= 1)
            {
                Finish();
                return;
            }
            Held.Add(this);
            Safe(Math.Max(0, p));
            return;
        }
        _clock.Start();
        Safe(0);
        top.RequestAnimationFrame(Frame);
    }

    private void Frame(TimeSpan _)
    {
        if (_over) return;
        var p = _clock.Elapsed.TotalMilliseconds / _ms;
        if (p >= 1 || TopLevel.GetTopLevel(_owner) is not { } top)
        {
            Finish();
            return;
        }
        Safe(p);
        top.RequestAnimationFrame(Frame);
    }

    private void Finish()
    {
        if (_over) return;
        _over = true;
        Safe(1);
        try { _done?.Invoke(); } catch (Exception ex) { RetroHub.Core.Logging.Log.Warn("Animation end failed", ex); }
    }

    private void Safe(double p)
    {
        try { _apply(p); }
        catch (Exception ex)
        {
            _over = true;
            RetroHub.Core.Logging.Log.Warn("Animation frame failed", ex);
        }
    }

    /// <summary>Stops the tween where it is; <paramref name="complete"/>: jumps to its end state (and runs its end).</summary>
    public void Stop(bool complete = false)
    {
        if (_over) return;
        if (complete) Finish();
        else _over = true;
    }
}

/// <summary>Small shared movements built on <see cref="Tween"/> (R2 animation, DESIGN 9.6).</summary>
public static class Anim
{
    /// <summary>Rows of a list that come in one after another (at most this many; the rest are simply there).</summary>
    public const int MaxStaggered = 6;
    /// <summary>The stagger of a menu's rows.</summary>
    public const double RowStagger = 20;

    /// <summary>
    /// A list unfolds from the button that opened it: the panel ScaleY 0.92 → 1 from the edge at its button with Opacity
    /// 0 → 1 and 6 px of travel (Step, CubicEaseOut), its first six rows following 20 ms apart (menus only, the one place
    /// besides the Fach that staggers). Reduced: the panel fades in (100 ms); off: it is there.
    /// </summary>
    public static void Unfold(Control panel, IReadOnlyList<Control> rows, bool fromTop)
    {
        var motion = Theming.Motion.Current;
        if (motion == Theming.EffectiveMotion.Off) return;
        if (motion == Theming.EffectiveMotion.Reduced)
        {
            panel.Opacity = 0;
            Tween.Run(panel, Theming.Motion.Scene, p => panel.Opacity = p, () => panel.ClearValue(Visual.OpacityProperty));
            return;
        }
        var dir = fromTop ? -1 : 1;
        var scale = new Avalonia.Media.ScaleTransform(1, 0.92);
        var shift = new Avalonia.Media.TranslateTransform(0, 6 * dir);
        panel.RenderTransformOrigin = new RelativePoint(0.5, fromTop ? 0 : 1, RelativeUnit.Relative);
        panel.RenderTransform = new Avalonia.Media.TransformGroup { Children = { scale, shift } };
        panel.Opacity = 0;
        var n = Math.Min(rows.Count, MaxStaggered);
        var moving = new List<(Control Row, Avalonia.Media.TranslateTransform T)>();
        for (var i = 0; i < n; i++)
        {
            var t = new Avalonia.Media.TranslateTransform(0, -6);
            rows[i].RenderTransform = t;
            rows[i].Opacity = 0;
            moving.Add((rows[i], t));
        }
        var step = Theming.Motion.StepDuration.TotalMilliseconds;
        var total = step + RowStagger * Math.Max(0, n - 1);
        Tween.Run(panel, TimeSpan.FromMilliseconds(total), p =>
        {
            var ms = p * total;
            var e = Ease.CubicOut(ms / step);
            scale.ScaleY = Ease.Lerp(0.92, 1, e);
            shift.Y = 6 * dir * (1 - e);
            panel.Opacity = e;
            for (var i = 0; i < moving.Count; i++)
            {
                var k = Ease.CubicOut((ms - RowStagger * i) / step);
                moving[i].Row.Opacity = k;
                moving[i].T.Y = -6 * (1 - k);
            }
        }, () =>
        {
            panel.ClearValue(Visual.RenderTransformProperty);
            panel.ClearValue(Visual.OpacityProperty);
            foreach (var (row, _) in moving)
            {
                row.ClearValue(Visual.RenderTransformProperty);
                row.ClearValue(Visual.OpacityProperty);
            }
        });
    }

    /// <summary>
    /// Content of a new compartment comes in from the side it lies on (<paramref name="direction"/> +1: from the right):
    /// TranslateX ±32 → 0 with Opacity (Glide, CubicEaseOut) on the container only – no per-tile work. Reduced: a 100 ms
    /// fade; off and while a direction is held: at once.
    /// </summary>
    public static void SlideIn(Control c, int direction)
    {
        if (direction == 0 || !Theming.Motion.Enabled || Input.NavRepeatTracker.Current?.IsActive == true) return;
        if (!Theming.Motion.AllowsMovement)
        {
            c.Opacity = 0;
            Tween.Run(c, Theming.Motion.Scene, p => c.Opacity = p, () => c.ClearValue(Visual.OpacityProperty));
            return;
        }
        if (c.RenderTransform != null && c.RenderTransform is not Avalonia.Media.TranslateTransform) return;
        var t = c.RenderTransform as Avalonia.Media.TranslateTransform ?? new Avalonia.Media.TranslateTransform();
        c.RenderTransform = t;
        Tween.Run(c, Theming.Motion.Glide, p =>
        {
            var e = Ease.CubicOut(p);
            t.X = 32 * Math.Sign(direction) * (1 - e);
            c.Opacity = e;
        }, () =>
        {
            t.X = 0;
            c.ClearValue(Visual.OpacityProperty);
        });
    }

    /// <summary>A dim layer coming up under an overlay (Scene; off: at once).</summary>
    public static void FadeIn(Control c)
    {
        if (!Theming.Motion.Enabled) return;
        c.Opacity = 0;
        Tween.Run(c, Theming.Motion.Scene, p => c.Opacity = Ease.CubicOut(p), () => c.ClearValue(Visual.OpacityProperty));
    }
}

/// <summary>Easing curves for <see cref="Tween"/> (progress 0 … 1 in, 0 … 1 out).</summary>
public static class Ease
{
    public static double CubicOut(double t) => 1 - Math.Pow(1 - Math.Clamp(t, 0, 1), 3);
    public static double CubicIn(double t) => Math.Pow(Math.Clamp(t, 0, 1), 3);
    public static double CubicInOut(double t)
    {
        t = Math.Clamp(t, 0, 1);
        return t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;
    }
    public static double SineInOut(double t) => -(Math.Cos(Math.PI * Math.Clamp(t, 0, 1)) - 1) / 2;
    /// <summary>Overshoots and comes back (the standard back-out; <paramref name="s"/> 1.70158 ≈ 10 % past the end).</summary>
    public static double BackOut(double t, double s = 1.70158)
    {
        t = Math.Clamp(t, 0, 1) - 1;
        return 1 + t * t * ((s + 1) * t + s);
    }
    public static double Lerp(double a, double b, double t) => a + (b - a) * t;
    /// <summary>0 → 1 → 0 over the progress (a bump: arcs, swings).</summary>
    public static double Bump(double t) => Math.Sin(Math.PI * Math.Clamp(t, 0, 1));
}
