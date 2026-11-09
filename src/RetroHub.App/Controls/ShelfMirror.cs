using System.Collections;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using RetroHub.App.Theming;
using RetroHub.App.ViewModels;

namespace RetroHub.App.Controls;

/// <summary>
/// The "Regalspiegel" (DESIGN 7.2.1): a 12 px band over the content width with ONE spine per game in sort order, in the
/// colour of its system at 70 % on the room. The spines stand on the band's bottom edge and are as tall as their
/// system's box format (a DVD case taller than a cartridge box), so runs of one system read as a row of spines; at
/// ≥ 4 px per game a 1 px gap separates them. More games than pixels: each pixel column takes the most frequent
/// colour of its games. Above the band a paper bracket marks the part of the library in view, and a 2 × 20 px mark in
/// the light colour shows the focused game.
/// <para>
/// Render cost: the spines are rasterised ONCE per change of the list or width (a <see cref="WriteableBitmap"/> of at
/// most width × 12 device pixels); a frame draws that bitmap, the bracket and the mark – three draw calls at 1000+ games.
/// Not focusable (the ticker is the control); a click or drag scrolls the grid to that position.
/// </para>
/// </summary>
public sealed class ShelfMirror : Control
{
    public static readonly StyledProperty<IEnumerable?> ItemsProperty = AvaloniaProperty.Register<ShelfMirror, IEnumerable?>(nameof(Items));
    /// <summary>First and last item index in view (−1 = unknown).</summary>
    public static readonly StyledProperty<int> FirstVisibleProperty = AvaloniaProperty.Register<ShelfMirror, int>(nameof(FirstVisible), -1);
    public static readonly StyledProperty<int> LastVisibleProperty = AvaloniaProperty.Register<ShelfMirror, int>(nameof(LastVisible), -1);
    public static readonly StyledProperty<int> FocusIndexProperty = AvaloniaProperty.Register<ShelfMirror, int>(nameof(FocusIndex), -1);
    /// <summary>Colour of the focus mark (null = the user's light colour; a system's light in a system context).</summary>
    public static readonly StyledProperty<Color?> MarkColorProperty = AvaloniaProperty.Register<ShelfMirror, Color?>(nameof(MarkColor));
    /// <summary>Ground the 70 % spine colours are mixed onto (room, or the system wall in a system context).</summary>
    public static readonly StyledProperty<Color> GroundProperty = AvaloniaProperty.Register<ShelfMirror, Color>(nameof(Ground), SystemPalette.Room);

    public const double BandHeight = 12;
    /// <summary>Space above the band: bracket at 4 px above it, the mark reaches 4 px higher still.</summary>
    public const double Top = 8;

    private static readonly IImmutableSolidColorBrush Bracket = new ImmutableSolidColorBrush(ColorUtil.WithAlpha(SystemPalette.Paper, 0xB3));

    /// <summary>Spine colour strength on the ground (M3 QA: 60 %, was 70 %).</summary>
    public const double SpineStrength = 0.60;

    private WriteableBitmap? _bitmap;
    private (int Count, int Width, Color Ground, int Version) _built;
    private int _version;
    private IReadOnlyList<Spine> _systems = [];

    /// <summary>One game in the mirror: its system (format → height) and the system's colour.</summary>
    internal readonly record struct Spine(string? SystemId, Color Color);

    /// <summary>Raised when the user clicks/drags on the band: the item index under the pointer.</summary>
    public event Action<int>? Scrub;

    static ShelfMirror()
    {
        AffectsRender<ShelfMirror>(FirstVisibleProperty, LastVisibleProperty, FocusIndexProperty, MarkColorProperty, GroundProperty);
        FocusableProperty.OverrideDefaultValue<ShelfMirror>(false);
        HeightProperty.OverrideDefaultValue<ShelfMirror>(Top + BandHeight + 4);
    }

    public ShelfMirror() => Cursor = new Cursor(StandardCursorType.Hand);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ThemeManager.AccentChanged += InvalidateVisual;
    }

    public IEnumerable? Items { get => GetValue(ItemsProperty); set => SetValue(ItemsProperty, value); }
    public int FirstVisible { get => GetValue(FirstVisibleProperty); set => SetValue(FirstVisibleProperty, value); }
    public int LastVisible { get => GetValue(LastVisibleProperty); set => SetValue(LastVisibleProperty, value); }
    public int FocusIndex { get => GetValue(FocusIndexProperty); set => SetValue(FocusIndexProperty, value); }
    public Color? MarkColor { get => GetValue(MarkColorProperty); set => SetValue(MarkColorProperty, value); }
    public Color Ground { get => GetValue(GroundProperty); set => SetValue(GroundProperty, value); }

    /// <summary>Number of spine rasterisations so far (tests: a scroll or a focus move never re-rasterises).</summary>
    internal int RasterCount { get; private set; }

    internal int Count => _systems.Count;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ItemsProperty)
        {
            _systems = (Items?.Cast<object>() ?? []).Select(o => o is GameItemViewModel g
                ? new Spine(g.SystemId, (g.SystemColorBrush as ISolidColorBrush)?.Color ?? SystemPalette.Get(g.SystemId).Color)
                : new Spine(null, Color.Parse("#948A7A"))).ToList();
            _version++;
            InvalidateVisual();
        }
    }

    private Rect Band => new(0, Top, Bounds.Width, BandHeight);

    public override void Render(DrawingContext ctx)
    {
        var band = Band;
        var n = _systems.Count;
        if (band.Width <= 0 || n == 0) return;
        var scaling = TopLevel.GetTopLevel(this) is { } top ? top.RenderScaling * UiScaleOf(this, top) : 1.0;
        var pw = Math.Max(1, (int)Math.Round(band.Width * scaling));
        var ph = Math.Max(1, (int)Math.Round(BandHeight * scaling));
        if (_bitmap == null || _built != (n, pw, Ground, _version))
        {
            _bitmap?.Dispose();
            _bitmap = Rasterise(_systems, pw, ph, Ground);
            _built = (n, pw, Ground, _version);
            RasterCount++;
        }
        using (ctx.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.None }))
            ctx.DrawImage(_bitmap, new Rect(0, 0, pw, ph), band);

        // the spines stand on a tiny shelf (M3 QA: on bare ground the band read as a bar chart): 1 px board light on
        // a 2 px board in Regal hell, right under the band
        ctx.FillRectangle(ThemeManager.BoardLight, new Rect(0, band.Bottom, band.Width, 1));
        ctx.FillRectangle(ShelfBoard.FrontBrush, new Rect(0, band.Bottom + 1, band.Width, 2));

        var unit = band.Width / n;
        // bracket over the part in view: a 1 px frame line in paper 70 %, 4 px above the band, with 4 px ticks down at
        // both ends – a frame around what is in view, not a progress bar (M3 QA)
        if (FirstVisible >= 0 && LastVisible >= FirstVisible)
        {
            var x0 = Math.Floor(FirstVisible * unit);
            var x1 = Math.Max(x0 + 2, Math.Ceiling((Math.Min(LastVisible, n - 1) + 1) * unit));
            var y = band.Y - 4;
            ctx.FillRectangle(Bracket, new Rect(x0, y - 1, x1 - x0, 1));
            ctx.FillRectangle(Bracket, new Rect(x0, y, 1, 4));
            ctx.FillRectangle(Bracket, new Rect(x1 - 1, y, 1, 4));
        }
        // focus mark: 2 × 20, over the band, in the light colour
        if (FocusIndex >= 0 && FocusIndex < n)
        {
            var x = Math.Round((FocusIndex + 0.5) * unit - 1);
            x = Math.Clamp(x, 0, band.Width - 2);
            ctx.FillRectangle(new ImmutableSolidColorBrush(MarkColor ?? ThemeManager.CurrentAccent), new Rect(x, band.Y - 6, 2, 20));
        }
    }

    private static double UiScaleOf(Visual v, TopLevel top) =>
        v.TransformToVisual(top) is { } m ? Math.Max(0.1, Math.Sqrt(m.M11 * m.M11 + m.M12 * m.M12)) : 1.0;

    /// <summary>
    /// Spine heights per box format: a format of 1.0 (square SNES/GBA boxes) stands 8 of 12 px, a DVD case (1.4) 10 px,
    /// a slim case (≥ 1.6) the full 12 – runs of one system read as a row of spines of one height.
    /// </summary>
    internal static double SpineShare(string? systemId) =>
        Math.Clamp(0.5 + (SystemPalette.BoxAspect(systemId) - 0.86) / (1.62 - 0.86) * 0.5, 0.58, 1.0);

    /// <summary>Rasterises the spines into a premultiplied BGRA bitmap of <paramref name="pw"/> × <paramref name="ph"/>.</summary>
    internal static WriteableBitmap Rasterise(IReadOnlyList<Spine> systems, int pw, int ph, Color ground)
    {
        var n = systems.Count;
        var pixels = new int[pw * ph];
        var colours = new Dictionary<Spine, (int Argb, double Share)>();
        (int, double) Look(Spine s)
        {
            if (colours.TryGetValue(s, out var c)) return c;
            var mixed = ColorUtil.Mix(ground, s.Color, SpineStrength);
            c = (unchecked((int)(0xFF000000u | (uint)mixed.R << 16 | (uint)mixed.G << 8 | mixed.B)), SpineShare(s.SystemId));
            colours[s] = c;
            return c;
        }
        var perPixel = (double)pw / n;
        var gap = perPixel >= 4; // spines wide enough to show as single spines: 1 px gap of ground between them
        for (var x = 0; x < pw; x++)
        {
            var first = (int)Math.Floor(x / perPixel);
            var last = Math.Min(n - 1, (int)Math.Floor((x + 1) / perPixel - 1e-9));
            Spine id;
            if (first >= last) id = systems[Math.Min(first, n - 1)];
            else
            {
                // more games than pixels: the most frequent system of this column
                var counts = new Dictionary<Spine, int>();
                for (var i = first; i <= last; i++) counts[systems[i]] = counts.GetValueOrDefault(systems[i]) + 1;
                id = counts.MaxBy(kv => kv.Value).Key;
            }
            if (gap)
            {
                // the last device pixel of each spine stays ground
                var end = (int)Math.Floor((first + 1) * perPixel) - 1;
                if (x == end && first < n - 1) continue;
            }
            var (argb, share) = Look(id);
            var h = Math.Max(2, (int)Math.Round(ph * share));
            for (var y = ph - h; y < ph; y++) pixels[y * pw + x] = argb;
        }
        var bmp = new WriteableBitmap(new PixelSize(pw, ph), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using (var fb = bmp.Lock())
            for (var y = 0; y < ph; y++)
                System.Runtime.InteropServices.Marshal.Copy(pixels, y * pw, fb.Address + y * fb.RowBytes, pw);
        return bmp;
    }

    // ===== pointer: click or drag scrolls the grid to that position =====
    private bool _dragging;

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _dragging = true;
        e.Pointer.Capture(this);
        RaiseScrub(e.GetPosition(this).X);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragging) RaiseScrub(e.GetPosition(this).X);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _dragging = false;
        e.Pointer.Capture(null);
    }

    private void RaiseScrub(double x)
    {
        var n = _systems.Count;
        if (n == 0 || Bounds.Width <= 0) return;
        Scrub?.Invoke(Math.Clamp((int)(x / Bounds.Width * n), 0, n - 1));
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        ThemeManager.AccentChanged -= InvalidateVisual;
        _bitmap?.Dispose();
        _bitmap = null;
    }
}
