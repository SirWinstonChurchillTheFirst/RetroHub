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
        foreach (var layer in new[] { Back, Front })
        {
            layer.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
            layer.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
            layer.Width = layer.Height = 0;
        }
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

    /// <summary>
    /// How far the cone may reach above the watched content (the grid: 24 px so it never paints over the poster title;
    /// a Start shelf: the free gap above its boxes). The cone may draw that far above the layer's own bounds.
    /// </summary>
    public double ConeHeadroom { get; set; } = 24;

    /// <summary>The cone may paint above the back layer's bounds (Start shelves: the layer spans only the row).</summary>
    public bool ConeOutsideBounds { get; set; }

    /// <summary>
    /// Strength of the cone (1 = 12 % at its foot). The detail stage takes 0.5: its room is lit by the TV light already,
    /// a full cone read as a grey roof above the box (M4 QA).
    /// </summary>
    public double ConeOpacity { get; set; } = 1;

    /// <summary>
    /// The lit board piece covers the board front opaquely (Regal hell mixed with 16 % light, DESIGN 4.2) instead of
    /// tinting it: on the stage board the static 10 px contact shadow lay on the front under the box and the tint
    /// over it read as no light at all – the box looked sunk into the board (M4 QA).
    /// </summary>
    public bool OpaquePiece { get; set; }

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
        // the UNLIFTED rectangle from the layout positions: TranslatePoint would contain the lift transform itself
        // (M2 QA: the ring was lifted twice – 1.06² – and stood 5–12 px away from the box, the cone showing in the gap)
        if (LayoutOffset(art, _host) is not { } origin) return;
        var rect = new Rect(origin.X + r.X, origin.Y + r.Y, r.Width, r.Height);
        // the pulled box of the detail stage: ring, board piece and shadow take in its generated spine (DESIGN 7.3)
        if (tile.Spine is { IsVisible: true } spine && spine.SpineRect is { Width: > 0 } sr)
            rect = new Rect(rect.X - sr.Width, rect.Y, rect.Width + sr.Width, rect.Height);
        // the lift scales from the bottom centre: the rectangle the box will have when the step animation is done
        var lifted = Motion.Current == EffectiveMotion.Full // reduced/off: no lift
            ? new Rect(rect.Center.X - rect.Width * Lift / 2, rect.Bottom - rect.Height * Lift, rect.Width * Lift, rect.Height * Lift)
            : rect;
        // on whole pixels, the bottom kept on the board: the 3 px gap of the ring hugs the box
        var left = Math.Round(lifted.X);
        var right = Math.Round(lifted.Right);
        var bottom = Math.Round(lifted.Bottom);
        lifted = new Rect(left, Math.Round(lifted.Y), right - left, bottom - Math.Round(lifted.Y));
        if (_visible && lifted == _target) return;
        _target = lifted;
        _visible = true;
        PlaceRegion(lifted);
        Back.InvalidateVisual();
        Front.InvalidateVisual();
    }

    private Rect _region;

    /// <summary>
    /// The two layers are only as large as the focus visuals around <paramref name="t"/> (cone above, ring, lit board
    /// piece and contact shadow below), placed by a translate: a focus move then dirties the old and the new spot
    /// instead of the whole content. With layers as large as the content (a 1000-game grid is 45 000 px tall) every
    /// move repainted the whole viewport – ≈ 150 ms per move in the headless CPU raster (M3 perf).
    /// </summary>
    private void PlaceRegion(Rect t)
    {
        var side = Math.Ceiling(t.Width * (ConeBottomWidth - 1) / 2) + 12;
        var region = new Rect(Math.Floor(t.X - side), Math.Floor(t.Top - ConeAbove - 12),
            Math.Ceiling(t.Width + 2 * side), Math.Ceiling(t.Height + ConeAbove + 12 + BoardFront + 16 + 4));
        if (region == _region) return;
        var resize = region.Size != _region.Size;
        _region = region;
        foreach (var layer in new[] { Back, Front })
        {
            if (resize)
            {
                layer.Width = region.Width;
                layer.Height = region.Height;
            }
            layer.RenderTransform = new TranslateTransform(region.X, region.Y);
        }
    }

    /// <summary>
    /// Position of <paramref name="v"/>'s origin in <paramref name="ancestor"/> from the layout bounds alone (render
    /// transforms such as the lift or a boundary shake are ignored); null when it is not a descendant.
    /// </summary>
    internal static Point? LayoutOffset(Visual v, Visual ancestor)
    {
        double x = 0, y = 0;
        for (Visual? c = v; c != null; c = c.GetVisualParent())
        {
            if (c == ancestor) return new Point(x, y);
            x += c.Bounds.X;
            y += c.Bounds.Y;
        }
        return null;
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

    /// <summary>
    /// Cone geometry (DESIGN 8.1, M2 QA, M3 QA): a short, HARD-EDGED trapezoid that ends at the top edge of the lifted
    /// box – it never fills behind the box. Top 0.6 × box width at <see cref="ConeAbove"/> px above the box, bottom
    /// 1.1 × box width on the box's top edge, light colour 0 → 12 % from top to bottom, linear, no horizontal feathering
    /// (M3 QA: the soft, wide 18 % cone read as a glow). Clipped to the headroom (grid: 24 px; a Start shelf: up to the
    /// stage board).
    /// </summary>
    public const double ConeBottomWidth = 1.1, ConeTopWidth = 0.6, ConeAbove = 32, ConePeakAlpha = 0.12;

    private const int ConeW = 176, ConeH = 32;

    /// <summary>
    /// The light cone as a small premultiplied bitmap, rendered once per light colour (shared): alpha 0 at the top
    /// rising linearly to <see cref="ConePeakAlpha"/> at the bottom, a trapezoid from <see cref="ConeTopWidth"/> to the
    /// full width with a crisp (one-pixel antialiased) edge – no BlurEffect, no per-frame work.
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
            for (var y = 0; y < ConeH; y++)
            {
                var t = (y + 0.5) / ConeH;
                var half = topHalf + (ConeW / 2.0 - topHalf) * t;
                var vertical = ConePeakAlpha * t;
                for (var x = 0; x < ConeW; x++)
                {
                    var dx = Math.Abs(x + 0.5 - ConeW / 2.0);
                    var e = Math.Clamp(half - dx + 0.5, 0, 1); // pixel coverage: a hard edge
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
        // all in host coordinates; the layer itself is only the region around the box (see PlaceRegion)
        using var toHost = ctx.PushTransform(Matrix.CreateTranslation(-_region.X, -_region.Y));
        var host = _host.Bounds.Size;
        var limit = _watched.TranslatePoint(new Point(0, 0), _host) is { } w ? w.Y - ConeHeadroom : double.NegativeInfinity;
        var clip = ConeOutsideBounds
            ? new Rect(0, -ConeHeadroom, host.Width, host.Height + ConeHeadroom)
            : new Rect(host);
        if (_viewport != null && _viewport.TranslatePoint(new Point(0, 0), _host) is { } v)
            clip = clip.Intersect(new Rect(v, _viewport.Bounds.Size));
        var top = Math.Max(clip.Top, limit);
        clip = new Rect(clip.X, top, clip.Width, Math.Max(0, clip.Bottom - top));
        if (clip.Height <= 0) return;
        // the cone ends at the box's top edge; where the headroom is shorter than the cone, only its lower part shows
        var above = Math.Min(ConeAbove, t.Top - clip.Top);
        if (above < 4) return;
        var x = t.Center.X - t.Width * ConeBottomWidth / 2;
        var width = t.Width * ConeBottomWidth;
        var bmp = ConeBitmap(LightColor);
        var srcTop = ConeH * (1 - above / ConeAbove);
        using (ctx.PushClip(clip))
        using (ctx.PushOpacity(Math.Clamp(ConeOpacity, 0, 1)))
            ctx.DrawImage(bmp, new Rect(0, srcTop, ConeW, ConeH - srcTop), new Rect(x, t.Top - above, width, above));
    }

    private void RenderFront(DrawingContext ctx)
    {
        if (!_visible) return;
        using var toHost = ctx.PushTransform(Matrix.CreateTranslation(-_region.X, -_region.Y));
        var t = _target;
        var light = LightColor;
        var brush = new ImmutableSolidColorBrush(light);
        // the lit board piece (M3 QA): the front under the box (as wide as the ring) tinted with 16 % light, 2 px light
        // on the board top; the stronger contact shadow falls only BELOW the front (on the front it cancelled the tint).
        // The ring stands on the board top – its legs end there and the light bar closes it: the box stands on the shelf.
        var outer = RingGap + RingWidth;
        var piece = new Rect(t.X - outer, t.Bottom, t.Width + 2 * outer, BoardFront);
        if (OpaquePiece)
            // the front under the box replaced by Regal hell + 16 % light (Bernstein ≈ #4C3D26); its 1 px bottom edge stays
            ctx.FillRectangle(new ImmutableSolidColorBrush(Mix(ShelfBoard.FrontBrush.Color, light, 0.16)), piece.WithHeight(BoardFront - 1));
        else
            ctx.FillRectangle(new ImmutableSolidColorBrush(ColorUtil.WithAlpha(light, 0x29)), piece);
        ctx.FillRectangle(FocusContact, new Rect(t.X, t.Bottom + BoardFront, t.Width, 16));
        ctx.FillRectangle(brush, new Rect(piece.X, t.Bottom, piece.Width, 2));
        Chamfer.DrawStandingRing(ctx, t, brush, RingGap, RingWidth);
    }

    private const double RingGap = 3, RingWidth = 3;

    private static Color Mix(Color a, Color b, double t) => Color.FromRgb(
        (byte)Math.Round(a.R + (b.R - a.R) * t), (byte)Math.Round(a.G + (b.G - a.G) * t), (byte)Math.Round(a.B + (b.B - a.B) * t));

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
