using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using RetroHub.App.Theming;

namespace RetroHub.App.Controls;

/// <summary>
/// The picture of the Einstellungen TV (DESIGN 7.6.2, M6 QA): a static test card that shows what the settings change –
/// an object, not an inspector. A grey ramp of the app's own neutrals on top, a hairline raster, and a mini shelf with
/// three boxes; the middle box carries the standing ring and the lit board piece in the current <see cref="Light"/>.
/// <list type="bullet">
/// <item>Lichtfarbe: the ring, the board piece and the TV's power LED follow the colour (resource, live).</item>
/// <item>Kanalanzeige: <see cref="FlashOsd"/> shows "1 Start" top right in the light, hard, for 1200 ms.</item>
/// <item>Bewegung: <see cref="Glide"/> replays the shelf glide in the chosen level (full 220 ms CubicEaseOut, reduced
/// and off: the boxes jump).</item>
/// </list>
/// It draws itself into <see cref="PixelScreen.PictureRect"/> of its bounds (the set's picture), under the scanlines.
/// </summary>
public sealed class TestCard : Control
{
    public static readonly StyledProperty<IBrush?> LightProperty = AvaloniaProperty.Register<TestCard, IBrush?>(nameof(Light));

    private static readonly IImmutableSolidColorBrush Tube = new ImmutableSolidColorBrush(Color.Parse("#16140F"));
    private static readonly IImmutableSolidColorBrush Raster = new ImmutableSolidColorBrush(Color.FromArgb(0x60, 0x3A, 0x34, 0x2B));
    private static readonly IImmutableSolidColorBrush Gap = new ImmutableSolidColorBrush(Color.Parse("#0A0908"));
    private static readonly IImmutableSolidColorBrush Front = new ImmutableSolidColorBrush(Color.Parse("#2A2620"));
    private static readonly IImmutableSolidColorBrush Lip = new ImmutableSolidColorBrush(Color.Parse("#3A352D"));
    private static readonly IImmutableSolidColorBrush BoardLight = new ImmutableSolidColorBrush(Color.Parse("#30FFF3E0"));

    /// <summary>The grey ramp: Raum-Tief, Raum, Regal, Regal hell, Kante, Text 3, Text 2, Papier.</summary>
    private static readonly IImmutableSolidColorBrush[] Ramp =
        new[] { "#0A0908", "#14120F", "#1E1B17", "#2A2620", "#3A342B", "#948A7A", "#C9BFAF", "#F4ECDF" }
            .Select(h => (IImmutableSolidColorBrush)new ImmutableSolidColorBrush(Color.Parse(h))).ToArray();

    /// <summary>Three boxes of the mini shelf: their systems (wall + band from the palette, no names).</summary>
    private static readonly string[] Boxes = ["nes", "gba", "n64", "snes", "megadrive"];

    private readonly DispatcherTimer _timer;
    private DateTime _osdUntil, _glideStart;
    private double _glideMs, _glideFrom;
    private FormattedText? _osdDigit, _osdName;
    private double _osdSize;
    private IBrush? _osdBrush;

    public TestCard()
    {
        IsHitTestVisible = false;
        ClipToBounds = true;
        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += (_, _) =>
        {
            InvalidateVisual();
            var now = DateTime.UtcNow;
            if (now >= _osdUntil && (now - _glideStart).TotalMilliseconds >= _glideMs) _timer.Stop();
        };
        Bind(LightProperty, this.GetResourceObservable("RhAccentBrush"));
    }

    static TestCard() { AffectsRender<TestCard>(LightProperty); }

    public IBrush? Light { get => GetValue(LightProperty); set => SetValue(LightProperty, value); }

    /// <summary>Is the channel display standing on the card now (tests)?</summary>
    public bool OsdShown => DateTime.UtcNow < _osdUntil;

    /// <summary>Kanalanzeige switched on: the OSD stands on the card for 1200 ms, hard in and out.</summary>
    public void FlashOsd()
    {
        _osdUntil = DateTime.UtcNow + TimeSpan.FromMilliseconds(1200);
        InvalidateVisual();
        _timer.Start();
    }

    /// <summary>Bewegung changed: the shelf glides one box to the left in the new level (it jumps when reduced/off).</summary>
    public void Glide()
    {
        _glideStart = DateTime.UtcNow;
        _glideFrom = 1;
        _glideMs = Motion.Current == EffectiveMotion.Full ? 220 : 0;
        InvalidateVisual();
        if (_glideMs > 0) _timer.Start();
    }

    private double GlideOffset()
    {
        if (_glideMs <= 0) return 0;
        var p = Math.Clamp((DateTime.UtcNow - _glideStart).TotalMilliseconds / _glideMs, 0, 1);
        return _glideFrom * Math.Pow(1 - p, 3); // CubicEaseOut towards 0
    }

    public override void Render(DrawingContext ctx)
    {
        var pic = PixelScreen.PictureRect(Bounds.Size);
        if (pic.Width < 40 || pic.Height < 30) return;
        using var clip = ctx.PushClip(new RoundedRect(pic, PixelScreen.TubeRadius));
        ctx.FillRectangle(Tube, pic);
        var k = pic.Width / 384;

        // the grey ramp across the top 22 %
        var rampH = Math.Round(pic.Height * 0.22);
        var bw = pic.Width / Ramp.Length;
        for (var i = 0; i < Ramp.Length; i++)
            ctx.FillRectangle(Ramp[i], new Rect(Math.Round(pic.X + i * bw), pic.Y, Math.Ceiling(bw) + 1, rampH));
        ctx.FillRectangle(Gap, new Rect(pic.X, pic.Y + rampH, pic.Width, Math.Max(2, Math.Round(2 * k))));

        // a hairline raster below the ramp
        var step = Math.Round(24 * k);
        for (var x = pic.X + step; x < pic.Right; x += step) ctx.FillRectangle(Raster, new Rect(Math.Round(x), pic.Y + rampH + 2, 1, pic.Height - rampH - 2));
        for (var y = pic.Y + rampH + step; y < pic.Bottom; y += step) ctx.FillRectangle(Raster, new Rect(pic.X, Math.Round(y), pic.Width, 1));

        // the mini shelf: board front 10, lip 2, the board light on top
        var boardY = Math.Round(pic.Y + pic.Height * 0.80);
        var boardH = Math.Round(10 * k);
        ctx.FillRectangle(Front, new Rect(pic.X, boardY, pic.Width, boardH));
        ctx.FillRectangle(Lip, new Rect(pic.X, boardY + 1, pic.Width, Math.Max(1, Math.Round(2 * k))));
        ctx.FillRectangle(BoardLight, new Rect(pic.X, boardY, pic.Width, 1));

        // the boxes: 5 slots, the middle one focused; the row glides by one slot when Bewegung changes
        var boxW = Math.Round(56 * k);
        var boxH = Math.Round(boxW * 1.36);
        var pitch = boxW + Math.Round(20 * k);
        var centre = pic.Center.X;
        var shift = GlideOffset() * pitch;
        for (var i = 0; i < Boxes.Length; i++)
        {
            var x = Math.Round(centre - boxW / 2 + (i - 2) * pitch + shift);
            var focused = i == 2;
            var h = focused ? Math.Round(boxH * 1.06) : boxH;
            var w = focused ? Math.Round(boxW * 1.06) : boxW;
            var r = new Rect(Math.Round(x - (w - boxW) / 2), boardY - h, w, h);
            var look = SystemPalette.Get(Boxes[i]);
            var split = Math.Round(h * 0.38);
            ctx.FillRectangle(look.WallHiBrush, new Rect(r.X, r.Y, r.Width, split));
            ctx.FillRectangle(look.WallBrush, new Rect(r.X, r.Y + split, r.Width, r.Height - split));
            ctx.FillRectangle(look.ColorBrush, new Rect(r.X, r.Y, Math.Max(4, Math.Round(r.Width * 0.16)), r.Height));
            if (focused && Light is { } light && shift < 1)
            {
                // the lit board piece under the ring and the standing ring (DESIGN 8.1), in the current light
                var ring = Math.Max(2, Math.Round(2 * k));
                ctx.FillRectangle(light, new Rect(r.X - 2 * ring, boardY, r.Width + 4 * ring, ring));
                Chamfer.DrawStandingRing(ctx, r, light, ring, ring, Math.Round(4 * k));
            }
        }

        // the channel display, hard, top right in the light
        if (DateTime.UtcNow < _osdUntil && Light is { } osdLight)
        {
            var size = Math.Round(28 * k);
            if (_osdDigit == null || Math.Abs(_osdSize - size) > 0.5 || !ReferenceEquals(_osdBrush, osdLight))
            {
                _osdSize = size;
                _osdBrush = osdLight;
                _osdDigit = new FormattedText("1", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, FontRoles.NumberFace, size, osdLight);
                _osdName = new FormattedText("Start", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, FontRoles.SpineFace, Math.Round(20 * k), osdLight);
            }
            var top = pic.Y + rampH + Math.Round(14 * k);
            var right = pic.Right - Math.Round(16 * k);
            var nameX = right - _osdName!.Width;
            ctx.DrawText(_osdName, new Point(nameX, top + (_osdDigit.Height - _osdName.Height) / 2 + 2));
            ctx.DrawText(_osdDigit, new Point(nameX - Math.Round(8 * k) - _osdDigit.Width, top));
        }
    }
}
