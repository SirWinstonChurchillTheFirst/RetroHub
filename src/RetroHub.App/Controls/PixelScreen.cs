using System.Globalization;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using RetroHub.App.Localization;
using RetroHub.App.Theming;
using RetroHub.Core.Logging;

namespace RetroHub.App.Controls;

/// <summary>
/// Object "Bildschirm" (DESIGN 1.2, 7.0, 7.1): the TV of the Start stage – a set standing on the stage board with a
/// room-deep bezel, a chin with a speaker grille, a 4:3 picture, the tube's falloff toward the corners and scanlines at
/// 18 % (the only surface with scanlines). The picture follows the chain snap → title screen → "Kein Signal" (the
/// system's wall as a test card: band, texture, motif at 50 % of the picture height, the code as an OSD) → off (empty
/// library: a dark tube with the collapsed line). Picture changes cross-fade over 180 ms between two layers.
/// <para>Pixel fidelity (7.0): the picture is placed in DEVICE pixels. With k = floor(min(frame/snap)) ≥ 2 and k·snap
/// filling ≥ 85 % of the frame height it is drawn k× nearest-neighbour on whole device pixels; otherwise contain with
/// high quality (a tube was soft too) and the scanlines cover the softness. Pictures of TV systems (home consoles)
/// with an aspect between 1.0 and 1.45 (8:7, 5:4, 4:3) fill the 4:3 frame like on a real set; handheld screens and
/// 16:9 pictures are letterboxed in room-deep.</para>
/// Snaps are small (≤ 640×528): decoded once at full size off the UI thread into a small LRU.
/// </summary>
public sealed class PixelScreen : Panel
{
    public static readonly StyledProperty<string?> PicturePathProperty = AvaloniaProperty.Register<PixelScreen, string?>(nameof(PicturePath));
    public static readonly StyledProperty<string?> SystemIdProperty = AvaloniaProperty.Register<PixelScreen, string?>(nameof(SystemId));
    public static readonly StyledProperty<Color> AccentProperty = AvaloniaProperty.Register<PixelScreen, Color>(nameof(Accent), Color.Parse("#948A7A"));
    public static readonly StyledProperty<string?> CodeProperty = AvaloniaProperty.Register<PixelScreen, string?>(nameof(Code));
    /// <summary>The set has nothing to show at all (empty library): the tube is dark with the collapsed line.</summary>
    public static readonly StyledProperty<bool> IsOffProperty = AvaloniaProperty.Register<PixelScreen, bool>(nameof(IsOff));
    /// <summary>The picture was made for a 4:3 TV (home console): near-4:3 pictures fill the frame.</summary>
    public static readonly StyledProperty<bool> IsTvSystemProperty = AvaloniaProperty.Register<PixelScreen, bool>(nameof(IsTvSystem), true);

    /// <summary>How much the scanlines darken the picture (DESIGN 7.1: 18 %).</summary>
    public const double ScanOpacity = 0.18;
    private const double ScanPngAlpha = 110 / 255.0;
    public static readonly TimeSpan Crossfade = TimeSpan.FromMilliseconds(180);

    private readonly SetLayer _set;
    private readonly PictureLayer _a;
    private readonly PictureLayer _b;
    private readonly TextureFill _scan;
    private readonly GlassLayer _glass;
    private bool _aFront = true;
    private int _version;
    private Picture? _shown;

    public PixelScreen()
    {
        ClipToBounds = false;
        IsHitTestVisible = false;
        _set = new SetLayer(this);
        _a = new PictureLayer { Opacity = 0 };
        _b = new PictureLayer { Opacity = 0 };
        _scan = new TextureFill { Texture = Texture.Scan, Strength = ScanOpacity / ScanPngAlpha };
        _glass = new GlassLayer(this);
        Children.Add(_set);
        Children.Add(_a);
        Children.Add(_b);
        Children.Add(_scan);
        Children.Add(_glass);
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.Antialias);
        ApplyTransitions();
    }

    public string? PicturePath { get => GetValue(PicturePathProperty); set => SetValue(PicturePathProperty, value); }
    public string? SystemId { get => GetValue(SystemIdProperty); set => SetValue(SystemIdProperty, value); }
    public Color Accent { get => GetValue(AccentProperty); set => SetValue(AccentProperty, value); }
    public string? Code { get => GetValue(CodeProperty); set => SetValue(CodeProperty, value); }
    public bool IsOff { get => GetValue(IsOffProperty); set => SetValue(IsOffProperty, value); }
    public bool IsTvSystem { get => GetValue(IsTvSystemProperty); set => SetValue(IsTvSystemProperty, value); }

    /// <summary>What the picture shows now (tests, diagnostics).</summary>
    public PictureKind Showing => _shown?.Kind ?? PictureKind.None;

    // ===================== geometry =====================

    /// <summary>Bezel width of a set <paramref name="width"/> wide (17 at 560, 12 at 400).</summary>
    public static double BezelFor(double width) => Math.Max(8, Math.Round(width * 0.03));

    /// <summary>Height of the chin under the picture (speaker grille, two push buttons).</summary>
    public static double ChinFor(double width) => Math.Max(16, Math.Round(width * 0.075));

    /// <summary>The two feet the set stands on (the cabinet floats this much above the board).</summary>
    public static double FeetFor(double width) => Math.Max(5, Math.Round(width * 0.016));

    /// <summary>Corner radius of the tube's glass (DESIGN 4.1: at most 6).</summary>
    public const double TubeRadius = 6;

    /// <summary>Total height of a set <paramref name="width"/> wide: bezel + 4:3 picture + chin + feet (462 at 560, 330 at 400).</summary>
    public static double SetHeightFor(double width)
    {
        var b = BezelFor(width);
        return b + Math.Round((width - 2 * b) * 3 / 4) + ChinFor(width) + FeetFor(width);
    }

    /// <summary>The picture rectangle inside a set of <paramref name="size"/> (4:3, below the top bezel).</summary>
    public static Rect PictureRect(Size size)
    {
        var b = BezelFor(size.Width);
        var w = Math.Max(0, size.Width - 2 * b);
        var h = Math.Min(Math.Round(w * 3 / 4), Math.Max(0, size.Height - b - ChinFor(size.Width) - FeetFor(size.Width)));
        return new Rect(b, b, w, h);
    }
    /// <summary>The motif box of "Kein Signal": 50 % of the picture height, centred, standing slightly below the middle.</summary>
    public static Rect MotifRect(Rect picture)
    {
        var m = Math.Round(picture.Height * 0.50);
        return new Rect(Math.Round(picture.Center.X - m / 2), Math.Round(picture.Y + picture.Height * 0.56 - m / 2), m, m);
    }

    /// <summary>
    /// Where a picture of <paramref name="image"/> pixels lands in a frame of <paramref name="frame"/> DEVICE pixels:
    /// integer k ≥ 2 when k·image fills ≥ 85 % of the frame height (nearest neighbour), else contain (or fill for
    /// near-4:3 TV pictures). Returns the destination in device pixels, centred, on whole pixels.
    /// </summary>
    public static (Rect Dest, bool Integer) Place(Size frame, PixelSize image, bool tvSystem)
    {
        if (frame.Width <= 0 || frame.Height <= 0 || image.Width <= 0 || image.Height <= 0) return (default, false);
        var aspect = (double)image.Width / image.Height;
        var k = Math.Floor(Math.Min(frame.Width / image.Width, frame.Height / image.Height));
        if (k >= 2 && k * image.Height >= 0.85 * frame.Height && !(tvSystem && aspect is >= 1.0 and <= 1.45 && k * image.Width < 0.85 * frame.Width))
        {
            var w = k * image.Width;
            var h = k * image.Height;
            return (new Rect(Math.Round((frame.Width - w) / 2), Math.Round((frame.Height - h) / 2), w, h), true);
        }
        if (tvSystem && aspect is >= 1.0 and <= 1.45) return (new Rect(0, 0, Math.Round(frame.Width), Math.Round(frame.Height)), false);
        var s = Math.Min(frame.Width / image.Width, frame.Height / image.Height);
        var dw = Math.Round(image.Width * s);
        var dh = Math.Round(image.Height * s);
        return (new Rect(Math.Round((frame.Width - dw) / 2), Math.Round((frame.Height - dh) / 2), dw, dh), false);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _set.Arrange(new Rect(finalSize));
        var pic = PictureRect(finalSize);
        var glass = new RectangleGeometry(new Rect(pic.Size)) { RadiusX = TubeRadius, RadiusY = TubeRadius };
        foreach (var layer in new Control[] { _a, _b, _scan, _glass })
        {
            layer.Arrange(pic);
            layer.Clip = glass;
        }
        return finalSize;
    }

    // ===================== state =====================

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PicturePathProperty || change.Property == SystemIdProperty || change.Property == AccentProperty
            || change.Property == CodeProperty || change.Property == IsOffProperty || change.Property == IsTvSystemProperty)
            Refresh();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Refresh();
    }

    /// <summary>Re-reads the motion level (cross-fade; hard when motion is off).</summary>
    public void ApplyTransitions()
    {
        var d = Motion.Current == EffectiveMotion.Off ? TimeSpan.Zero : Motion.Current == EffectiveMotion.Reduced ? TimeSpan.FromMilliseconds(100) : Crossfade;
        foreach (var l in new[] { _a, _b })
            l.Transitions = d == TimeSpan.Zero ? null : [new DoubleTransition { Property = OpacityProperty, Duration = d }];
    }

    private void Refresh()
    {
        var version = ++_version;
        var look = string.IsNullOrEmpty(SystemId) ? null : SystemPalette.Get(SystemId, Accent);
        var path = PicturePath;
        if (IsOff || look == null && string.IsNullOrEmpty(path))
        {
            Show(new Picture(PictureKind.Off, null, look, Code, IsTvSystem));
            return;
        }
        if (string.IsNullOrEmpty(path))
        {
            Show(new Picture(PictureKind.NoSignal, null, look, Code, IsTvSystem));
            return;
        }
        if (SnapCache.TryGet(path) is { } cached)
        {
            Show(new Picture(PictureKind.Image, cached, look, Code, IsTvSystem));
            return;
        }
        var tv = IsTvSystem;
        var code = Code;
        _ = Task.Run(() => SnapCache.Load(path)).ContinueWith(t => Dispatcher.UIThread.Post(() =>
        {
            if (version != _version) return;
            var bmp = t.IsCompletedSuccessfully ? t.Result : null;
            Show(bmp != null ? new Picture(PictureKind.Image, bmp, look, code, tv) : new Picture(PictureKind.NoSignal, null, look, code, tv));
        }), TaskScheduler.Default);
    }

    private void Show(Picture picture)
    {
        if (_shown != null && _shown.SameAs(picture)) return;
        _shown = picture;
        var front = _aFront ? _a : _b;
        var back = _aFront ? _b : _a;
        back.Picture = picture;
        back.Opacity = 1;
        front.Opacity = 0;
        _aFront = !_aFront;
        _glass.InvalidateVisual();
    }

    public enum PictureKind { None, Image, NoSignal, Off }

    private sealed record Picture(PictureKind Kind, Bitmap? Bitmap, SystemLook? Look, string? Code, bool TvSystem)
    {
        public bool SameAs(Picture o) => Kind == o.Kind && ReferenceEquals(Bitmap, o.Bitmap) && Look?.Id == o.Look?.Id && Code == o.Code && TvSystem == o.TvSystem;
    }

    // ===================== layers =====================

    private static readonly IImmutableSolidColorBrush RoomDeep = new ImmutableSolidColorBrush(Color.Parse("#0A0908"));
    private static readonly IImmutableSolidColorBrush Cabinet = new ImmutableSolidColorBrush(Color.Parse("#1D1A16"));
    private static readonly IImmutableSolidColorBrush Outline = new ImmutableSolidColorBrush(Color.Parse("#2E2A23"));
    private static readonly IImmutableSolidColorBrush BottomShade = new ImmutableSolidColorBrush(Color.Parse("#141210"));
    private static readonly IImmutableSolidColorBrush TopLight = new ImmutableSolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xF3, 0xE0));
    private static readonly IImmutableSolidColorBrush Lip = new ImmutableSolidColorBrush(Color.Parse("#3A342B"));
    private static readonly IImmutableSolidColorBrush Slot = new ImmutableSolidColorBrush(Color.Parse("#0A0908"));
    private static readonly IImmutableSolidColorBrush SlotLight = new ImmutableSolidColorBrush(Color.Parse("#34302A"));
    private static readonly IImmutableSolidColorBrush DarkTube = new ImmutableSolidColorBrush(Color.Parse("#0D0C0A"));
    private static readonly IImmutableSolidColorBrush OffLine = new ImmutableSolidColorBrush(Color.FromArgb(0x33, 0xF4, 0xEC, 0xDF));
    private static readonly IImmutableSolidColorBrush NoSignalBrush = new ImmutableSolidColorBrush(Color.Parse("#F4ECDF"));

    /// <summary>
    /// The cabinet: a warm dark body standing on two feet, an outline in shelf-light, light from above on its top edge,
    /// the recessed tube (a room-deep frame whose lower lip catches the light) and the chin with a speaker grille and two
    /// push buttons. Generic set, no brand, no product silhouette.
    /// </summary>
    private sealed class SetLayer(PixelScreen owner) : Control
    {
        static SetLayer() { IsHitTestVisibleProperty.OverrideDefaultValue<SetLayer>(false); }

        public override void Render(DrawingContext ctx)
        {
            var size = Bounds.Size;
            if (size.Width <= 0 || size.Height <= 0) return;
            var feet = FeetFor(size.Width);
            var body = new Rect(0, 0, size.Width, size.Height - feet);
            // feet: two dark blocks under the cabinet, standing on the board
            var fw = Math.Round(size.Width * 0.11);
            foreach (var fx in new[] { Math.Round(size.Width * 0.08), Math.Round(size.Width * 0.92 - fw) })
                ctx.FillRectangle(RoomDeep, new Rect(fx, body.Bottom - 1, fw, feet + 1));
            ctx.FillRectangle(Outline, body);
            ctx.FillRectangle(Cabinet, body.Deflate(1));
            ctx.FillRectangle(TopLight, new Rect(1, 1, body.Width - 2, 1));
            ctx.FillRectangle(BottomShade, new Rect(1, body.Bottom - 3, body.Width - 2, 2));
            var pic = PictureRect(size);
            // the recess around the tube: a room-deep frame, its lower lip catches the light from above
            var recess = pic.Inflate(4);
            ctx.DrawRectangle(RoomDeep, null, new RoundedRect(recess, TubeRadius + 3));
            ctx.FillRectangle(Lip, new Rect(recess.X + TubeRadius, recess.Bottom, recess.Width - 2 * TubeRadius, 1));
            // chin: a speaker grille of horizontal slots on the right, two push buttons on the left
            var chinTop = recess.Bottom + 1;
            var chin = body.Bottom - 2 - chinTop;
            if (chin < 10) return;
            var bezel = BezelFor(size.Width);
            const int slots = 5;
            var slotH = Math.Max(2, Math.Round(chin * 0.075));
            var gap = Math.Max(2, Math.Round(chin * 0.085));
            var total = slots * slotH + (slots - 1) * gap;
            var y0 = Math.Round(chinTop + (chin - total) / 2);
            var w = Math.Round(size.Width * 0.24);
            var x0 = size.Width - bezel - w;
            for (var i = 0; i < slots; i++)
            {
                var y = y0 + i * (slotH + gap);
                ctx.FillRectangle(Slot, new Rect(x0, y, w, slotH));
                ctx.FillRectangle(SlotLight, new Rect(x0, y + slotH, w, 1));
            }
            var bw = Math.Round(size.Width * 0.045);
            var bh = Math.Max(4, Math.Round(chin * 0.22));
            var by = Math.Round(chinTop + (chin - bh) / 2);
            for (var i = 0; i < 2; i++)
            {
                var bx = bezel + i * (bw + Math.Round(bw * 0.4));
                ctx.FillRectangle(Slot, new Rect(bx, by, bw, bh));
                ctx.FillRectangle(SlotLight, new Rect(bx, by + bh, bw, 1));
            }
            _ = owner;
        }
    }
    /// <summary>One picture (snap, test card or the dark tube); two of them cross-fade.</summary>
    private sealed class PictureLayer : Control
    {
        private Picture? _picture;
        private FormattedText? _code;
        private FormattedText? _noSignal;
        private (string?, string?, int) _textKey;

        static PictureLayer()
        {
            IsHitTestVisibleProperty.OverrideDefaultValue<PictureLayer>(false);
            ClipToBoundsProperty.OverrideDefaultValue<PictureLayer>(true);
        }

        public Picture? Picture
        {
            get => _picture;
            set
            {
                _picture = value;
                UpdateMode();
                InvalidateVisual();
            }
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == BoundsProperty) UpdateMode();
        }

        /// <summary>The device-pixel transform of this layer: (DIP → device factor, device origin).</summary>
        private (double Total, Point Origin) Device()
        {
            var top = TopLevel.GetTopLevel(this);
            var rs = top?.RenderScaling ?? 1.0;
            var m = top != null ? this.TransformToVisual(top) ?? Matrix.Identity : Matrix.Identity;
            var ui = Math.Max(0.01, Math.Sqrt(m.M11 * m.M11 + m.M12 * m.M12));
            return (ui * rs, new Point(m.M31 * rs, m.M32 * rs));
        }

        /// <summary>Nearest neighbour for whole-number scaling, high quality otherwise (never set during Render).</summary>
        private void UpdateMode()
        {
            var integer = false;
            if (_picture is { Kind: PictureKind.Image, Bitmap: { } bmp } p && Bounds.Width > 0)
            {
                var (total, _) = Device();
                integer = Place(new Size(Math.Round(Bounds.Width * total), Math.Round(Bounds.Height * total)), bmp.PixelSize, p.TvSystem).Integer;
            }
            // the test card's era texture is a pixel pattern too: nearest neighbour
            if (_picture is { Kind: PictureKind.NoSignal }) integer = true;
            RenderOptions.SetBitmapInterpolationMode(this, integer ? BitmapInterpolationMode.None : BitmapInterpolationMode.HighQuality);
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (TopLevel.GetTopLevel(this) is { } top) top.ScalingChanged += OnScaling;
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            if (TopLevel.GetTopLevel(this) is { } top) top.ScalingChanged -= OnScaling;
            base.OnDetachedFromVisualTree(e);
        }

        private void OnScaling(object? sender, EventArgs e)
        {
            UpdateMode();
            InvalidateVisual();
        }

        public override void Render(DrawingContext ctx)
        {
            var r = new Rect(Bounds.Size);
            if (r.Width <= 0 || r.Height <= 0 || _picture is not { } p) return;
            switch (p.Kind)
            {
                case PictureKind.Image when p.Bitmap is { } bmp:
                    DrawImage(ctx, r, bmp, p.TvSystem);
                    break;
                case PictureKind.NoSignal when p.Look is { } look:
                    SystemWall.Draw(ctx, this, r, look, band: Math.Max(6, Math.Round(r.Height / 50)), motifShare: 0, motifInset: 0);
                    MotifCatalog.Draw(ctx, look, MotifRect(r));
                    DrawOsd(ctx, r, look, p.Code);
                    break;
                default:
                    ctx.FillRectangle(DarkTube, r);
                    ctx.FillRectangle(OffLine, new Rect(r.X + r.Width * 0.08, Math.Round(r.Center.Y), r.Width * 0.84, 1));
                    break;
            }
        }

        private void DrawImage(DrawingContext ctx, Rect r, Bitmap bmp, bool tv)
        {
            ctx.FillRectangle(RoomDeep, r);
            var (total, origin) = Device(); // DIP → device pixels
            var frame = new Size(Math.Round(r.Width * total), Math.Round(r.Height * total));
            var (dest, _) = Place(frame, bmp.PixelSize, tv);
            if (dest.Width <= 0) return;
            // device origin of the picture, snapped to a whole device pixel
            var dx = Math.Round(origin.X) - origin.X;
            var dy = Math.Round(origin.Y) - origin.Y;
            using (ctx.PushTransform(Matrix.CreateScale(1 / total, 1 / total)))
                ctx.DrawImage(bmp, new Rect(0, 0, bmp.PixelSize.Width, bmp.PixelSize.Height), dest.Translate(new Vector(dx, dy)));
        }

        private void DrawOsd(DrawingContext ctx, Rect r, SystemLook look, string? code)
        {
            var key = (code, look.Id, (int)r.Height);
            if (_textKey != key || _noSignal == null)
            {
                _textKey = key;
                var size = Math.Clamp(Math.Round(r.Height * 0.07), 18, 40);
                _code = string.IsNullOrEmpty(code) ? null
                    : new FormattedText(code, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, FontRoles.SpineFace, size, look.LightBrush);
                _noSignal = new FormattedText(Loc.T("screen.noSignal"), Loc.Culture, FlowDirection.LeftToRight, FontRoles.UiFace,
                    Math.Max(15, Math.Round(size * 0.66)), NoSignalBrush);
            }
            var s = r.Height / 400;
            var x = Math.Round(28 * s);
            var y = Math.Round(30 * s);
            if (_code is { } c)
            {
                ctx.DrawText(c, new Point(x, y));
                y += Math.Round(c.Height);
            }
            if (_noSignal is { } t) ctx.DrawText(t, new Point(x, y));
        }
    }

    /// <summary>Over the picture: scanlines (18 %) and the tube's falloff toward the corners; a dark tube catches a faint reflection.</summary>
    private sealed class GlassLayer : Control
    {
        private readonly PixelScreen _owner;

        static GlassLayer()
        {
            IsHitTestVisibleProperty.OverrideDefaultValue<GlassLayer>(false);
            ClipToBoundsProperty.OverrideDefaultValue<GlassLayer>(true);
        }

        public GlassLayer(PixelScreen owner)
        {
            _owner = owner;
            RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.HighQuality);
        }

        public override void Render(DrawingContext ctx)
        {
            var r = new Rect(Bounds.Size);
            if (r.Width <= 0 || r.Height <= 0) return;
            var owner = _owner;
            ctx.DrawImage(TubeFalloff, new Rect(0, 0, FalloffW, FalloffH), r);
            if (owner.Showing == PictureKind.Off)
                ctx.DrawImage(Reflection, new Rect(0, 0, FalloffW, FalloffH), r);
        }
    }

    // ===================== pre-rendered tube bitmaps (once per process) =====================

    private const int FalloffW = 96, FalloffH = 72;
    private static Bitmap? _falloff, _reflection;

    /// <summary>Room-deep darkening toward the edges and corners of the tube (0 in the middle, ≈ 45 % in the corners).</summary>
    private static Bitmap TubeFalloff => _falloff ??= Generate((nx, ny) =>
    {
        // a superellipse distance: flat in the middle, falling off near the edges like a curved tube
        var d = Math.Pow(Math.Pow(Math.Abs(nx), 4) + Math.Pow(Math.Abs(ny), 4), 0.25);
        var a = Math.Clamp((d - 0.62) / 0.5, 0, 1);
        return (0x0A, 0x09, 0x08, 0.46 * a * a);
    });

    /// <summary>A switched-off tube is dark glass: a soft, wide patch of the room light in its upper left.</summary>
    private static Bitmap Reflection => _reflection ??= Generate((nx, ny) =>
    {
        var dx = (nx + 0.42) / 0.75;
        var dy = (ny + 0.48) / 0.42;
        var d = Math.Sqrt(dx * dx + dy * dy);
        var a = Math.Clamp(1 - d, 0, 1);
        return (0xF4, 0xEC, 0xDF, 0.055 * a * a * (3 - 2 * a));
    });

    private static Bitmap Generate(Func<double, double, (byte R, byte G, byte B, double A)> f)
    {
        var wb = new WriteableBitmap(new PixelSize(FalloffW, FalloffH), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        var px = new byte[FalloffW * FalloffH * 4];
        for (var y = 0; y < FalloffH; y++)
            for (var x = 0; x < FalloffW; x++)
            {
                var nx = (x + 0.5) / FalloffW * 2 - 1;
                var ny = (y + 0.5) / FalloffH * 2 - 1;
                var (r, g, b, a) = f(nx, ny);
                var i = (y * FalloffW + x) * 4;
                px[i] = (byte)Math.Round(b * a);
                px[i + 1] = (byte)Math.Round(g * a);
                px[i + 2] = (byte)Math.Round(r * a);
                px[i + 3] = (byte)Math.Round(255 * a);
            }
        using (var fb = wb.Lock())
            for (var y = 0; y < FalloffH; y++)
                System.Runtime.InteropServices.Marshal.Copy(px, y * FalloffW * 4, fb.Address + y * fb.RowBytes, FalloffW * 4);
        return wb;
    }

    // ===================== snap cache =====================

    private static class SnapCache
    {
        private const int Capacity = 24;
        private static readonly LinkedList<(string Key, Bitmap Bitmap)> Order = new();
        private static readonly Dictionary<string, LinkedListNode<(string Key, Bitmap Bitmap)>> Map = new(StringComparer.OrdinalIgnoreCase);

        private static string? Key(string path)
        {
            try { return File.Exists(path) ? path + "|" + File.GetLastWriteTimeUtc(path).Ticks : null; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return null; }
        }

        public static Bitmap? TryGet(string path)
        {
            if (Key(path) is not { } key) return null;
            lock (Map)
            {
                if (!Map.TryGetValue(key, out var node)) return null;
                Order.Remove(node);
                Order.AddFirst(node);
                return node.Value.Bitmap;
            }
        }

        public static Bitmap? Load(string path)
        {
            if (TryGet(path) is { } hit) return hit;
            if (Key(path) is not { } key) return null;
            Bitmap bmp;
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                bmp = new Bitmap(stream);
            }
            catch (Exception ex)
            {
                Log.Warn($"Screen picture '{path}' could not be decoded", ex);
                return null;
            }
            lock (Map)
            {
                Map[key] = Order.AddFirst((key, bmp));
                while (Order.Count > Capacity && Order.Last is { } last)
                {
                    Order.RemoveLast();
                    Map.Remove(last.Value.Key);
                }
            }
            return bmp;
        }
    }
}
