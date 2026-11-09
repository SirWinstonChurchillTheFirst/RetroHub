using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.VisualTree;
using RetroHub.App.Input;
using RetroHub.App.Theming;

namespace RetroHub.App.Controls;

/// <summary>
/// Focus visuals of boxes on a shelf (DESIGN 8.1), drawn by ONE pair of layers per grid/shelf instead of by the tiles:
/// <see cref="Back"/> (behind the rows: light cone from above, a pre-rendered bitmap per light colour) and <see cref="Front"/> (above the rows: lit board
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
    private readonly Control _watched;
    private ScrollViewer? _viewport;
    private readonly List<Visual> _raised = new();
    private GameTile? _tile;
    private Rect _target;
    private bool _visible;

    public FocusLayer(Panel host, Control watched)
    {
        _host = host;
        _watched = watched;
        Back = new BackLayer(this);
        Front = new FrontLayer(this);
        watched.AddHandler(InputElement.GotFocusEvent, OnGotFocus, RoutingStrategies.Bubble, handledEventsToo: true);
        watched.AddHandler(InputElement.LostFocusEvent, OnLostFocus, RoutingStrategies.Bubble, handledEventsToo: true);
        host.LayoutUpdated += (_, _) => { if (_tile != null) Reposition(); };
        host.AttachedToVisualTree += (_, _) =>
        {
            NavRepeatTracker.AnyChanged += OnRepeatChanged;
            // the cone is clipped to the visible viewport: re-render it when the content scrolls
            _viewport = host.FindAncestorOfType<ScrollViewer>();
            if (_viewport != null) _viewport.ScrollChanged += OnViewportScrolled;
        };
        host.DetachedFromVisualTree += (_, _) =>
        {
            NavRepeatTracker.AnyChanged -= OnRepeatChanged;
            if (_viewport != null) _viewport.ScrollChanged -= OnViewportScrolled;
            _viewport = null;
        };
    }

    private void OnViewportScrolled(object? sender, ScrollChangedEventArgs e)
    {
        if (_visible) Back.InvalidateVisual();
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

    // ===== shared, cached brushes and the pre-rendered cone (one per light colour) =====
    private static readonly Dictionary<uint, Bitmap> ConeBitmaps = new();
    private static readonly IBrush FocusContact = new ImmutableLinearGradientBrush(
        [new ImmutableGradientStop(0, Color.FromArgb(0xB3, 0, 0, 0)), new ImmutableGradientStop(1, Color.FromArgb(0, 0, 0, 0))],
        startPoint: new RelativePoint(0, 0, RelativeUnit.Relative), endPoint: new RelativePoint(0, 1, RelativeUnit.Relative));

    /// <summary>Cone geometry (DESIGN 8.2, M1 QA): bottom 1.15 × box width, top 0.5 ×, starting 96 px above the box.</summary>
    public const double ConeBottomWidth = 1.15, ConeTopWidth = 0.5, ConeAbove = 96, ConePeakAlpha = 0.12;

    private const int ConeW = 160, ConeH = 96;

    /// <summary>
    /// The light cone as a small premultiplied bitmap, rendered once per light colour: alpha 0 at the top rising to
    /// <see cref="ConePeakAlpha"/> at the bottom (the light lands on the board), a trapezoid from
    /// <see cref="ConeTopWidth"/> to the full width with a soft horizontal edge (no BlurEffect, no per-frame work).
    /// </summary>
    internal static Bitmap ConeBitmap(Color light)
    {
        var key = light.ToUInt32();
        lock (ConeBitmaps)
        {
            if (ConeBitmaps.TryGetValue(key, out var cached)) return cached;
            var wb = new WriteableBitmap(new PixelSize(ConeW, ConeH), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
            var pixels = new byte[ConeW * ConeH * 4];
            var topHalf = ConeTopWidth / ConeBottomWidth / 2 * ConeW;
            var falloff = ConeW * 0.07; // ≈ 12 px at a 196-px box
            for (var y = 0; y < ConeH; y++)
            {
                var t = (y + 0.5) / ConeH;
                var half = topHalf + (ConeW / 2.0 - topHalf) * t;
                var vertical = ConePeakAlpha * t * t * (3 - 2 * t); // smooth rise toward the board
                for (var x = 0; x < ConeW; x++)
                {
                    var dx = Math.Abs(x + 0.5 - ConeW / 2.0);
                    var e = Math.Clamp((half - dx) / falloff, 0, 1);
                    e = e * e * (3 - 2 * e);
                    var a = vertical * e;
                    var i = (y * ConeW + x) * 4;
                    pixels[i] = (byte)Math.Round(light.B * a);
                    pixels[i + 1] = (byte)Math.Round(light.G * a);
                    pixels[i + 2] = (byte)Math.Round(light.R * a);
                    pixels[i + 3] = (byte)Math.Round(255 * a);
                }
            }
            using (var fb = wb.Lock())
                for (var y = 0; y < ConeH; y++)
                    System.Runtime.InteropServices.Marshal.Copy(pixels, y * ConeW * 4, fb.Address + y * fb.RowBytes, ConeW * 4);
            ConeBitmaps[key] = wb;
            return wb;
        }
    }

    private void RenderBack(DrawingContext ctx)
    {
        if (!_visible || NavRepeatTracker.Current?.IsActive == true) return; // no cone while a direction is held
        var t = _target;
        // the cone never paints over the header above the boxes (poster title, ticker): it starts at most 24 px above
        // the rows; it is clipped to the grid content and the scroll viewport
        var limit = _watched.TranslatePoint(new Point(0, 0), Back) is { } w ? w.Y - 24 : double.NegativeInfinity;
        var clip = new Rect(Back.Bounds.Size);
        if (_viewport != null && _viewport.TranslatePoint(new Point(0, 0), Back) is { } v)
            clip = clip.Intersect(new Rect(v, _viewport.Bounds.Size));
        var top = Math.Max(clip.Top, limit);
        clip = new Rect(clip.X, top, clip.Width, Math.Max(0, clip.Bottom - top));
        var dest = new Rect(t.Center.X - t.Width * ConeBottomWidth / 2, t.Top - ConeAbove, t.Width * ConeBottomWidth, t.Height + ConeAbove);
        if (clip.Height <= 0 || dest.Top >= t.Bottom) return;
        var bmp = ConeBitmap(LightColor);
        using (ctx.PushClip(clip))
            ctx.DrawImage(bmp, new Rect(0, 0, ConeW, ConeH), dest);
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
