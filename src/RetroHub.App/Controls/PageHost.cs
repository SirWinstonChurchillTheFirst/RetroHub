using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using RetroHub.App.Theming;

namespace RetroHub.App.Controls;

/// <summary>
/// Keep-alive page host (idea from MonHub MainWindow): every page is created once and stays in the panel; switching
/// only toggles visibility and plays a short enter transition (DESIGN 9.2, R2 animation):
/// <list type="bullet">
/// <item>between the header's channels and tools the new page comes from the side it lies on: TranslateX ±24 → 0 with
/// Opacity 0 → 1 (Scene 280, CubicEaseOut), while the page left behind fades out in Blink (90 ms) without moving;</item>
/// <item>deeper (a game, a system) TranslateY 12 → 0, back −10 → 0, with the fade;</item>
/// <item>reduced motion: the fade alone (100 ms); off: nothing.</item>
/// </list>
/// Each switch cancels the previous animation. Header and footer never move (they are not in here).
/// </summary>
public sealed class PageHost : Panel
{
    private readonly Dictionary<object, Control> _pages = new();
    private Tween? _enter, _leave;
    private Control? _leaving;

    public Control? Current { get; private set; }

    public IReadOnlyDictionary<object, Control> Pages => _pages;

    /// <summary>How far a page enters from the side when the channel changes.</summary>
    public const double SideShift = 24;

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

    /// <summary>
    /// Shows <paramref name="page"/>. <paramref name="direction"/>: +1 = a channel/tool to the right of the one left,
    /// −1 = to the left, 0 = deeper or back (vertical enter).
    /// </summary>
    public void Show(Control page, bool isBack, int direction = 0)
    {
        if (Current == page)
        {
            page.IsVisible = true;
            return;
        }
        var old = Current;
        EndLeave();
        foreach (var child in Children)
            if (child != page && child != old) child.IsVisible = false;
        page.IsVisible = true;
        Current = page;
        if (old != null) Leave(old);
        Enter(page, isBack, direction);
    }

    /// <summary>The page left behind fades out in Blink without moving (full motion only; else it goes at once).</summary>
    private void Leave(Control old)
    {
        if (!Motion.AllowsMovement)
        {
            old.IsVisible = false;
            return;
        }
        _leaving = old;
        old.IsHitTestVisible = false;
        // under the new page: it draws first
        old.ZIndex = -1;
        _leave = Tween.Run(old, Motion.Blink, p => old.Opacity = 1 - p, () =>
        {
            if (_leaving == old) EndLeave();
        });
    }

    private void EndLeave()
    {
        _leave?.Stop();
        _leave = null;
        if (_leaving is not { } old) return;
        _leaving = null;
        if (old != Current) old.IsVisible = false;
        old.Opacity = 1;
        old.ZIndex = 0;
        old.IsHitTestVisible = true;
    }

    private void Enter(Control page, bool isBack, int direction)
    {
        _enter?.Stop();
        _enter = null;
        var duration = Motion.Content;
        if (page.RenderTransform is not TranslateTransform t)
        {
            t = new TranslateTransform();
            if (page.RenderTransform == null) page.RenderTransform = t;
        }
        if (duration == TimeSpan.Zero || !ReferenceEquals(page.RenderTransform, t))
        {
            page.Opacity = 1;
            t.X = t.Y = 0;
            return;
        }
        var move = Motion.AllowsMovement;
        var dx = move && direction != 0 ? SideShift * Math.Sign(direction) : 0.0;
        var dy = move && direction == 0 ? (isBack ? -10.0 : 12.0) : 0.0;
        _enter = Tween.Run(page, duration, p =>
        {
            var e = Ease.CubicOut(p);
            page.Opacity = e;
            t.X = dx * (1 - e);
            t.Y = dy * (1 - e);
        });
    }

    /// <summary>The element that had focus inside each page when it was left, and what it showed (restored when coming back).</summary>
    private readonly Dictionary<Control, (WeakReference<IInputElement> Element, WeakReference<object>? Shown)> _lastFocus = new();

    public void RememberFocus(Control page, IInputElement? focused)
    {
        if (focused is Visual v && IsInside(v, page))
            _lastFocus[page] = (new WeakReference<IInputElement>(focused), (focused as StyledElement)?.DataContext is { } dc ? new WeakReference<object>(dc) : null);
    }

    /// <summary>
    /// The element to focus on coming back. A virtualised box may have been recycled for another game meanwhile (the list
    /// reloaded after a favourite toggle, say): then the box that now shows the remembered item, else nothing.
    /// </summary>
    public IInputElement? RecallFocus(Control page)
    {
        if (!_lastFocus.TryGetValue(page, out var memo) || !memo.Element.TryGetTarget(out var e) || e is not Visual v || !IsInside(v, page) || !e.IsEffectivelyVisible)
            return null;
        if (memo.Shown == null || !memo.Shown.TryGetTarget(out var shown) || e is not StyledElement se || ReferenceEquals(se.DataContext, shown)) return e;
        foreach (var tile in Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(page).OfType<GameTile>())
            if (ReferenceEquals(tile.DataContext, shown) && tile.IsEffectivelyVisible) return tile;
        return null;
    }

    private static bool IsInside(Visual v, Visual root)
    {
        for (Visual? x = v; x != null; x = Avalonia.VisualTree.VisualExtensions.GetVisualParent(x)) if (x == root) return true;
        return false;
    }
}
