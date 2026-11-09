using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using RetroHub.App.Input;
using RetroHub.App.Theming;

namespace RetroHub.App.Controls;

/// <summary>
/// Focus visuals of boxes on a shelf (DESIGN 8.1), drawn by ONE pair of layers per grid/shelf instead of by the tiles:
/// <see cref="Back"/> (behind the rows: light cone from above) and <see cref="Front"/> (above the rows: lit board
/// piece, stronger contact shadow, chamfered ring). Both live in the scroll content, so they scroll along; covers
/// keep clipping. On focus the tile's <see cref="CoverArt.ArtRect"/> is translated into the layer and the visuals are
/// set for the target rectangle AFTER the lift (×1.06 from bottom centre), so they never trail the scale animation.
/// The focused tile and its row container get ZIndex 1 (the lifted box covers the caption above, never the reverse).
/// Cost: two elements per grid, independent of the tile count; no brush per tile.
/// </summary>
public sealed class FocusLayer
{
    public const double Lift = 1.06;

    private readonly Panel _host;
    private readonly List<Visual> _raised = new();
    private GameTile? _tile;
    private Rect _target;
    private bool _visible;

    public FocusLayer(Panel host, Control watched)
    {
        _host = host;
        Back = new BackLayer(this);
        Front = new FrontLayer(this);
        watched.AddHandler(InputElement.GotFocusEvent, OnGotFocus, RoutingStrategies.Bubble, handledEventsToo: true);
        watched.AddHandler(InputElement.LostFocusEvent, OnLostFocus, RoutingStrategies.Bubble, handledEventsToo: true);
        host.LayoutUpdated += (_, _) => { if (_tile != null) Reposition(); };
        host.AttachedToVisualTree += (_, _) => NavRepeatTracker.AnyChanged += OnRepeatChanged;
        host.DetachedFromVisualTree += (_, _) => NavRepeatTracker.AnyChanged -= OnRepeatChanged;
    }

    private void OnRepeatChanged(bool active)
    {
        if (_visible) Back.InvalidateVisual();
    }

    /// <summary>Layer behind the rows (add as the first child of the host panel).</summary>
    public Control Back { get; }
    /// <summary>Layer above the rows (add as the last child of the host panel).</summary>
    public Control Front { get; }

    /// <summary>Height of the board front under the boxes (10 = shelf edge in the grid, 24 = stage board).</summary>
    public double BoardFront { get; set; } = ShelfBoard.EdgeHeight;

    /// <summary>Light colour of ring, cone and board piece; null = the user's light colour (Bernstein).</summary>
    public Color? Light { get; set; }

    private Color LightColor => Light ?? ThemeManager.CurrentAccent;

    private void OnGotFocus(object? sender, FocusChangedEventArgs e)
    {
        var tile = (e.Source as Visual)?.FindAncestorOfType<GameTile>(includeSelf: true);
        if (tile == null || tile.Art == null)
        {
            Hide();
            return;
        }
        if (_tile != tile)
        {
            Detach();
            _tile = tile;
            tile.Art.ArtRectChanged += OnArtChanged;
            tile.DataContextChanged += OnTileRecycled;
        }
        Raise(tile);
        Reposition();
        // :focus-visible may be applied after GotFocus: look again once the input pass is done
        Avalonia.Threading.Dispatcher.UIThread.Post(() => { if (_tile == tile) Reposition(); }, Avalonia.Threading.DispatcherPriority.Loaded);
    }

    private void OnLostFocus(object? sender, RoutedEventArgs e)
    {
        if (_tile == null) return;
        // focus moved to another tile: GotFocus follows and repositions; elsewhere: hide
        if (e.Source is Visual v && v.FindAncestorOfType<GameTile>(includeSelf: true) == _tile && !_tile.IsKeyboardFocusWithin) Hide();
    }

    private void OnArtChanged(object? sender, EventArgs e) => Reposition();

    /// <summary>The focused tile was recycled for another item (virtualization): the layer is hidden.</summary>
    private void OnTileRecycled(object? sender, EventArgs e) => Hide();

    private void Detach()
    {
        if (_tile?.Art != null) _tile.Art.ArtRectChanged -= OnArtChanged;
        if (_tile != null) _tile.DataContextChanged -= OnTileRecycled;
        _tile = null;
    }

    public void Hide()
    {
        Detach();
        Lower();
        if (!_visible) return;
        _visible = false;
        Back.InvalidateVisual();
        Front.InvalidateVisual();
    }

    private void Raise(Control tile)
    {
        Lower();
        for (Visual? v = tile; v != null && v != _host; v = v.GetVisualParent())
        {
            // never the host's own children: the front layer must stay above the rows
            if (v is Control c && v.GetVisualParent() is Panel parent && parent != _host)
            {
                c.ZIndex = 1;
                _raised.Add(c);
            }
        }
    }

    private void Lower()
    {
        foreach (var v in _raised) if (v is Control c) c.ZIndex = 0;
        _raised.Clear();
    }

    private void Reposition()
    {
        var tile = _tile;
        var art = tile?.Art;
        if (tile == null || art == null || !tile.IsKeyboardFocusWithin || !tile.IsEffectivelyVisible || !IsVisibleFocus(tile))
        {
            if (_visible) { _visible = false; Back.InvalidateVisual(); Front.InvalidateVisual(); }
            return;
        }
        var r = art.ArtRect;
        if (r.Width <= 0) return;
        var tl = art.TranslatePoint(r.TopLeft, _host);
        var br = art.TranslatePoint(r.BottomRight, _host);
        if (tl == null || br == null) return;
        var rect = new Rect(tl.Value, br.Value);
        // the lift scales from the bottom centre: the rectangle the box will have when the step animation is done
        var lifted = Motion.Current == EffectiveMotion.Full // reduced/off: no lift
            ? new Rect(rect.Center.X - rect.Width * Lift / 2, rect.Bottom - rect.Height * Lift, rect.Width * Lift, rect.Height * Lift)
            : rect;
        lifted = new Rect(Math.Round(lifted.X / 2) * 2, Math.Round(lifted.Y / 2) * 2, Math.Round(lifted.Width / 2) * 2, Math.Round(lifted.Height / 2) * 2);
        if (_visible && lifted == _target) return;
        _target = lifted;
        _visible = true;
        Back.InvalidateVisual();
        Front.InvalidateVisual();
    }

    /// <summary>Only keyboard/controller focus shows the object focus (a mouse click does not lift the box).</summary>
    private static bool IsVisibleFocus(Control tile) => tile.Classes.Contains(":focus-visible");

    /// <summary>Re-evaluates the focus visuals (e.g. after the auto-repeat ended).</summary>
    public void Refresh()
    {
        if (_tile != null) Reposition();
        Back.InvalidateVisual();
        Front.InvalidateVisual();
    }

    // ===== shared, cached brushes (one per light colour) =====
    private static readonly Dictionary<uint, IBrush> ConeBrushes = new();
    private static readonly IBrush FocusContact = new ImmutableLinearGradientBrush(
        [new ImmutableGradientStop(0, Color.FromArgb(0xB3, 0, 0, 0)), new ImmutableGradientStop(1, Color.FromArgb(0, 0, 0, 0))],
        startPoint: new RelativePoint(0, 0, RelativeUnit.Relative), endPoint: new RelativePoint(0, 1, RelativeUnit.Relative));

    private static IBrush ConeBrush(Color light)
    {
        var key = light.ToUInt32();
        lock (ConeBrushes)
        {
            if (ConeBrushes.TryGetValue(key, out var b)) return b;
            b = new ImmutableLinearGradientBrush(
                [new ImmutableGradientStop(0, ColorUtil.WithAlpha(light, 0x24)), new ImmutableGradientStop(1, ColorUtil.WithAlpha(light, 0))],
                startPoint: new RelativePoint(0, 0, RelativeUnit.Relative), endPoint: new RelativePoint(0, 1, RelativeUnit.Relative));
            ConeBrushes[key] = b;
            return b;
        }
    }

    private void RenderBack(DrawingContext ctx)
    {
        if (!_visible || NavRepeatTracker.Current?.IsActive == true) return; // no cone while a direction is held
        var t = _target;
        var top = t.Top - 120;
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(new Point(t.Center.X - t.Width * 0.3, top), true);
            g.LineTo(new Point(t.Center.X + t.Width * 0.3, top));
            g.LineTo(new Point(t.Center.X + t.Width * 0.65, t.Bottom));
            g.LineTo(new Point(t.Center.X - t.Width * 0.65, t.Bottom));
            g.EndFigure(true);
        }
        ctx.DrawGeometry(ConeBrush(LightColor), null, geo);
    }

    private void RenderFront(DrawingContext ctx)
    {
        if (!_visible) return;
        var t = _target;
        var light = LightColor;
        // lit board piece: front tinted with 12 % light, focus contact shadow, 2 px light on the top edge
        ctx.FillRectangle(new ImmutableSolidColorBrush(ColorUtil.WithAlpha(light, 0x1F)), new Rect(t.X, t.Bottom, t.Width, BoardFront));
        ctx.FillRectangle(FocusContact, new Rect(t.X, t.Bottom, t.Width, Math.Min(16, BoardFront + 6)));
        ctx.FillRectangle(new ImmutableSolidColorBrush(light), new Rect(t.X, t.Bottom, t.Width, 2));
        Chamfer.DrawRing(ctx, t, new ImmutableSolidColorBrush(light));
    }

    private sealed class BackLayer(FocusLayer owner) : Control
    {
        static BackLayer() { IsHitTestVisibleProperty.OverrideDefaultValue<BackLayer>(false); }
        public override void Render(DrawingContext context) => owner.RenderBack(context);
    }

    private sealed class FrontLayer(FocusLayer owner) : Control
    {
        static FrontLayer() { IsHitTestVisibleProperty.OverrideDefaultValue<FrontLayer>(false); }
        public override void Render(DrawingContext context) => owner.RenderFront(context);
    }
}
