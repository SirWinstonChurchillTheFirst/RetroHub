using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using RetroHub.App.Theming;

namespace RetroHub.App.Controls;

/// <summary>
/// Keep-alive page host (idea from MonHub MainWindow): every page is created once and stays in the panel; switching
/// only toggles visibility and plays a short enter transition (fade + 12 px slide, direction-aware for Back;
/// fade only for reduced motion; nothing when motion is off). Each switch cancels the previous animation.
/// </summary>
public sealed class PageHost : Panel
{
    private readonly Dictionary<object, Control> _pages = new();
    private CancellationTokenSource? _animation;

    public Control? Current { get; private set; }

    public IReadOnlyDictionary<object, Control> Pages => _pages;

    /// <summary>Returns the page for <paramref name="key"/>, creating it with <paramref name="factory"/> on first use.</summary>
    public Control GetOrCreate(object key, Func<Control> factory)
    {
        if (_pages.TryGetValue(key, out var page)) return page;
        page = factory();
        page.IsVisible = false;
        _pages[key] = page;
        Children.Add(page);
        return page;
    }

    public void Show(Control page, bool isBack)
    {
        if (Current == page)
        {
            page.IsVisible = true;
            return;
        }
        foreach (var child in Children)
            if (child != page) child.IsVisible = false;
        page.IsVisible = true;
        Current = page;
        Enter(page, isBack);
    }

    private void Enter(Control page, bool isBack)
    {
        _animation?.Cancel();
        _animation = null;
        var duration = Motion.Content;
        if (duration == TimeSpan.Zero)
        {
            page.Opacity = 1;
            if (page.RenderTransform is TranslateTransform t0) { t0.X = 0; t0.Y = 0; }
            return;
        }
        if (page.RenderTransform is not TranslateTransform) page.RenderTransform = new TranslateTransform();
        var cts = new CancellationTokenSource();
        _animation = cts;
        var move = Motion.AllowsMovement;
        var dy = move ? (isBack ? -10.0 : 12.0) : 0.0;
        var animation = new Animation
        {
            Duration = duration,
            Easing = new CubicEaseOut(),
            FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(OpacityProperty, 0d), new Setter(TranslateTransform.YProperty, dy) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(OpacityProperty, 1d), new Setter(TranslateTransform.YProperty, 0d) } },
            },
        };
        _ = RunAsync(animation, page, cts);
    }

    private static async Task RunAsync(Animation animation, Control page, CancellationTokenSource cts)
    {
        try { await animation.RunAsync(page, cts.Token); }
        catch (OperationCanceledException) { }
        finally
        {
            page.Opacity = 1;
            if (page.RenderTransform is TranslateTransform t) t.Y = 0;
        }
    }

    /// <summary>The element that had focus inside each page when it was left (restored when coming back).</summary>
    private readonly Dictionary<Control, WeakReference<IInputElement>> _lastFocus = new();

    public void RememberFocus(Control page, IInputElement? focused)
    {
        if (focused is Visual v && IsInside(v, page)) _lastFocus[page] = new WeakReference<IInputElement>(focused);
    }

    public IInputElement? RecallFocus(Control page) =>
        _lastFocus.TryGetValue(page, out var w) && w.TryGetTarget(out var e) && e is Visual v && IsInside(v, page) && e.IsEffectivelyVisible ? e : null;

    private static bool IsInside(Visual v, Visual root)
    {
        for (Visual? x = v; x != null; x = Avalonia.VisualTree.VisualExtensions.GetVisualParent(x)) if (x == root) return true;
        return false;
    }
}
