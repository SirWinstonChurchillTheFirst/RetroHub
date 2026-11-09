using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using RetroHub.App.Input;
using RetroHub.App.Theming;
using RetroHub.App.ViewModels;
using RetroHub.Core.Logging;

namespace RetroHub.App.Controls;

/// <summary>
/// The room light ("Fernsehlicht", DESIGN 2.5). One instance OUTSIDE the scaled page tree, window-sized.
/// <list type="number">
/// <item>Source: the page's <see cref="BackdropSpec"/> (game image → system wall → room).</item>
/// <item>Softening is precomputed off the UI thread: decode to 96 px, crop 16:9, resample to 64×36, blur (three box
/// passes ≈ gaussian), into a small LRU; shown as an Image (UniformToFill, high quality) at 0.38. No BlurEffect.</item>
/// <item>Picture light: saturation-weighted mean colour of the 64×36 pixels, clamped to L 0.55–0.70, S ≤ 0.70, cached per game.</item>
/// <item>Scrims flattened into ONE cached bitmap per window-size bucket (64 px grid).</item>
/// <item>Glow at the source: a radial gradient in the picture light, recoloured on change only.</item>
/// <item>Board light: the shared <see cref="ThemeManager.BoardLight"/> gets the picture light at 35 % (wall: light 22 %).</item>
/// <item>Grain (korn-128 at 2.5 %) only here, against banding.</item>
/// <item>Crossfade between two image layers (Light 420 ms), started only after <see cref="Motion.Dwell"/> of rest and
/// never during auto-repeat; when a repeat ends only the dwell restarts, and only if the target differs.</item>
/// </list>
/// Timers are Task.Delay + Dispatcher.Post (they also run under the headless test dispatcher).
/// </summary>
public sealed class RoomBackdrop : Panel
{
    public const double ImageOpacity = 0.38;

    private static readonly IImmutableSolidColorBrush RoomBrush = new ImmutableSolidColorBrush(Color.Parse("#14120F"));
    private static readonly Color Room = Color.Parse("#14120F");
    private static readonly Color RoomDeep = Color.Parse("#0A0908");

    private readonly Border _ground;          // room or the system wall (flat tone, interpolates)
    private readonly MotifGhost _ghost;       // motif outline at 7 % when a game has no picture (on the flat wall tone)
    private readonly Image _a;
    private readonly Image _b;
    private readonly TextureFill _grain;
    private readonly Image _scrim;
    private readonly Border _glow;
    private readonly RadialGradientBrush _glowBrush;
    private bool _aFront = true;

    private BackdropSpec _target = BackdropSpec.Room;
    private BackdropSpec? _shown;
    private int _version;
    private (int W, int H, int Stage)? _scrimKey;
    private int _scrimVersion;

    public RoomBackdrop()
    {
        IsHitTestVisible = false;
        _ground = new Border { Background = new SolidColorBrush(Room) };
        _ghost = new MotifGhost { IsVisible = false };
        _a = MakeImage();
        _b = MakeImage();
        _grain = new TextureFill { Texture = Texture.Grain, Strength = 0.025 };
        _scrim = new Image { Stretch = Stretch.Fill };
        RenderOptions.SetBitmapInterpolationMode(_scrim, BitmapInterpolationMode.HighQuality);
        _glowBrush = new RadialGradientBrush
        {
            GradientStops = { new GradientStop(Colors.Transparent, 0), new GradientStop(Colors.Transparent, 1) },
        };
        _glow = new Border { Background = _glowBrush };
        Children.Add(_ground);
        Children.Add(_ghost);
        Children.Add(_a);
        Children.Add(_b);
        Children.Add(_grain);
        Children.Add(_scrim);
        Children.Add(_glow);
        ApplyTransitions();
    }

    private static Image MakeImage()
    {
        var img = new Image { Stretch = Stretch.UniformToFill, Opacity = 0 };
        RenderOptions.SetBitmapInterpolationMode(img, BitmapInterpolationMode.HighQuality);
        return img;
    }

    /// <summary>Re-reads the motion level (crossfade and wall transitions; hard when off).</summary>
    public void ApplyTransitions()
    {
        var light = Motion.Light;
        foreach (var img in new[] { _a, _b })
            img.Transitions = light == TimeSpan.Zero ? null :
                [new DoubleTransition { Property = OpacityProperty, Duration = light, Easing = new SineEaseInOut() }];
        var scene = Motion.Scene;
        _ground.Transitions = scene == TimeSpan.Zero ? null :
            [new BrushTransition { Property = Border.BackgroundProperty, Duration = scene, Easing = new CubicEaseOut() }];
    }

    /// <summary>Top of the stage board in window coordinates (DIP); below it the room is plain. NaN = use the spec's share.</summary>
    public double StageY { get; private set; } = double.NaN;

    public void SetStageY(double y)
    {
        if (double.IsNaN(y) && double.IsNaN(StageY)) return;
        if (Math.Abs(y - StageY) < 1) return;
        StageY = y;
        UpdateScrim(immediate: _scrimKey == null);
        UpdateGlowGeometry();
    }

    private double StageBottom => !double.IsNaN(StageY) && StageY > 0 ? StageY : Bounds.Height * _target.StageShare;

    /// <summary>The page's room request. TV light swaps after the dwell; walls and the room switch at once.</summary>
    public void Show(BackdropSpec spec)
    {
        _target = spec;
        var version = ++_version;
        if (spec.Kind != BackdropKind.TvLight)
        {
            Apply(spec, null, null);
            return;
        }
        if (_shown is { Kind: BackdropKind.TvLight } s && s.ImagePath == spec.ImagePath && s.SystemId == spec.SystemId)
        {
            _shown = spec;
            UpdateScrim(immediate: false);
            UpdateGlowGeometry();
            return;
        }
        // first appearance of the page: no dwell (nothing to wait for)
        if (_shown == null || _shown.Kind != BackdropKind.TvLight) _ = LoadAndApplyAsync(spec, version);
        else ScheduleDwell(version);
    }

    /// <summary>The auto-repeat ended: restart the dwell, only when the target differs from what is shown.</summary>
    public void OnRepeatEnded()
    {
        if (_shown != _target) ScheduleDwell(++_version);
    }

    private void ScheduleDwell(int version)
    {
        _ = Task.Delay(Motion.Dwell).ContinueWith(_ => Dispatcher.UIThread.Post(() =>
        {
            if (version != _version) return;
            if (NavRepeatTracker.Current?.IsActive == true) return; // resumes via OnRepeatEnded
            _ = LoadAndApplyAsync(_target, version);
        }), TaskScheduler.Default);
    }

    private async Task LoadAndApplyAsync(BackdropSpec spec, int version)
    {
        Soft? soft = null;
        if (!string.IsNullOrEmpty(spec.ImagePath))
        {
            try { soft = await Task.Run(() => SoftCache.Get(spec.ImagePath!)).ConfigureAwait(true); }
            catch (Exception ex) { Log.Warn("Room light: softening failed", ex); }
        }
        if (version != _version) return;
        Apply(spec, soft?.Bitmap, soft?.Light);
    }

    private void Apply(BackdropSpec spec, Bitmap? bitmap, Color? light)
    {
        _shown = spec;
        var look = spec.SystemId != null ? SystemPalette.Get(spec.SystemId, spec.Accent == default ? null : spec.Accent) : null;
        switch (spec.Kind)
        {
            case BackdropKind.Room:
                SetGround(Room, look: null);
                FadeTo(null);
                SetGlow(null, 0);
                _scrim.IsVisible = false;
                _grain.IsVisible = false;
                ThemeManager.SetBoardLight(spec.AccentBoard ? ThemeManager.BoardLightFor(ThemeManager.CurrentAccent, 0.22) : ThemeManager.NeutralBoardLight);
                break;
            case BackdropKind.Wall:
                SetGround(look?.Wall ?? Room, look: null);
                FadeTo(null);
                SetGlow(null, 0);
                _scrim.IsVisible = false;
                _grain.IsVisible = false;
                ThemeManager.SetBoardLight(look != null ? ThemeManager.BoardLightFor(look.Light, 0.22) : ThemeManager.NeutralBoardLight);
                break;
            default: // TV light
                _grain.IsVisible = true;
                _scrim.IsVisible = true;
                if (bitmap != null)
                {
                    SetGround(Room, look: null);
                    FadeTo(bitmap);
                    var l = light ?? Room;
                    SetGlow(l, 0.16);
                    ThemeManager.SetBoardLight(ThemeManager.BoardLightFor(l, 0.35));
                }
                else if (look != null)
                {
                    // no picture: the system's wall as ONE flat tone (a 38/62 split across the window reads as a
                    // rendering seam) plus the motif outline at 7 % standing bottom right on the stage board
                    SetGround(look.Wall, look);
                    FadeTo(null);
                    SetGlow(look.Light, 0.10);
                    ThemeManager.SetBoardLight(ThemeManager.BoardLightFor(look.Light, 0.35));
                }
                else
                {
                    // no game at all (empty library): room with a faint Bernstein glow
                    SetGround(Room, look: null);
                    FadeTo(null);
                    SetGlow(ThemeManager.CurrentAccent, 0.05);
                    ThemeManager.SetBoardLight(ThemeManager.NeutralBoardLight);
                }
                UpdateScrim(immediate: _scrimKey == null);
                break;
        }
        UpdateGlowGeometry();
    }

    private void SetGround(Color colour, SystemLook? look)
    {
        if (_ground.Background is SolidColorBrush b) b.Color = colour;
        else _ground.Background = new SolidColorBrush(colour);
        _ghost.IsVisible = look != null;
        _ghost.Look = look;
    }

    private void FadeTo(Bitmap? bitmap)
    {
        var front = _aFront ? _a : _b;
        var back = _aFront ? _b : _a;
        if (bitmap == null)
        {
            front.Opacity = 0;
            back.Opacity = 0;
            return;
        }
        if (front.Source == bitmap && front.Opacity > 0) return;
        back.Source = bitmap;
        back.Opacity = ImageOpacity;
        front.Opacity = 0;
        _aFront = !_aFront;
    }

    private void SetGlow(Color? colour, double strength)
    {
        var c = colour ?? Colors.Transparent;
        _glowBrush.GradientStops[0].Color = ColorUtil.WithAlpha(c, colour == null ? (byte)0 : (byte)Math.Round(255 * strength));
        _glowBrush.GradientStops[1].Color = ColorUtil.WithAlpha(c, 0);
        _glow.IsVisible = colour != null;
    }

    private void UpdateGlowGeometry()
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        var stage = Math.Max(1, StageBottom);
        // the glow is part of the stage: it ends at the board
        _glow.Height = stage;
        _glow.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
        var r = 0.55 * w;
        _glowBrush.Center = new RelativePoint(_target.GlowX * w, _target.GlowY * h, RelativeUnit.Absolute);
        _glowBrush.GradientOrigin = _glowBrush.Center;
        _glowBrush.RadiusX = new RelativeScalar(r, RelativeUnit.Absolute);
        _glowBrush.RadiusY = new RelativeScalar(r, RelativeUnit.Absolute);
        // the motif outline stands on the stage board, right, 80 % of the stage height
        var size = Math.Round(stage * 0.8);
        _ghost.Width = size;
        _ghost.Height = size;
        _ghost.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
        _ghost.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
        _ghost.Margin = new Thickness(Math.Max(0, w * _target.GhostX - size / 2), Math.Max(0, stage - size), 0, 0);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BoundsProperty)
        {
            UpdateScrim(immediate: _scrimKey == null);
            UpdateGlowGeometry();
        }
    }

    // ===== flattened scrims: one bitmap per window-size bucket, rendered 150 ms after the last resize =====

    private void UpdateScrim(bool immediate)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        var key = ((int)Math.Ceiling(w / 64) * 64, (int)Math.Ceiling(h / 64) * 64, (int)Math.Round(StageBottom / 8) * 8);
        if (_scrimKey == key) return;
        var version = ++_scrimVersion;
        if (immediate) RenderScrim(key);
        else _ = Task.Delay(150).ContinueWith(_ => Dispatcher.UIThread.Post(() =>
        {
            if (version == _scrimVersion) RenderScrim(key);
        }), TaskScheduler.Default);
    }

    private void RenderScrim((int W, int H, int Stage) key)
    {
        _scrimKey = key;
        var scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        // gradients upscale invisibly: half the device resolution is plenty
        var pw = Math.Max(16, (int)(key.W * scale / 2));
        var ph = Math.Max(16, (int)(key.H * scale / 2));
        var rtb = new RenderTargetBitmap(new PixelSize(pw, ph), new Vector(96, 96));
        using (var ctx = rtb.CreateDrawingContext())
        {
            var sx = (double)pw / key.W;
            var sy = (double)ph / key.H;
            using (ctx.PushTransform(Matrix.CreateScale(sx, sy)))
                DrawScrims(ctx, key.W, key.H, Math.Min(key.Stage, key.H));
        }
        var old = _scrim.Source as IDisposable;
        _scrim.Source = rtb;
        _scrim.Width = key.W;
        _scrim.Height = key.H;
        _scrim.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
        _scrim.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
        old?.Dispose();
    }

    private static IBrush Linear(Color a, Color b, double x0, double y0, double x1, double y1) => new LinearGradientBrush
    {
        StartPoint = new RelativePoint(x0, y0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(x1, y1, RelativeUnit.Relative),
        GradientStops = { new GradientStop(a, 0), new GradientStop(b, 1) },
    };

    /// <summary>(a) horizontal room 94 % at the left → 52 % from 60 % of the width on, (b) vertical clear until 60 % of the stage → room at the board,
    /// room below, (c) vignette in room-deep, (d) head scrim room-deep 70 % → 0 over 160 px.</summary>
    private static void DrawScrims(DrawingContext ctx, double w, double h, double stage)
    {
        ctx.FillRectangle(new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(ColorUtil.WithAlpha(Room, 0xF0), 0),
                new GradientStop(ColorUtil.WithAlpha(Room, 0x85), 0.6),
                new GradientStop(ColorUtil.WithAlpha(Room, 0x85), 1),
            },
        }, new Rect(0, 0, w, stage));
        var fadeTop = stage * 0.6;
        ctx.FillRectangle(Linear(ColorUtil.WithAlpha(Room, 0), Room, 0, 0, 0, 1), new Rect(0, fadeTop, w, Math.Max(1, stage - fadeTop)));
        ctx.FillRectangle(RoomBrush, new Rect(0, stage, w, Math.Max(0, h - stage)));
        var vignette = new RadialGradientBrush
        {
            Center = new RelativePoint(0.5, 0.42, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(0.5, 0.42, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(0.75, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(0.75, RelativeUnit.Relative),
            GradientStops = { new GradientStop(ColorUtil.WithAlpha(RoomDeep, 0), 0.55), new GradientStop(ColorUtil.WithAlpha(RoomDeep, 0x99), 1) },
        };
        ctx.FillRectangle(vignette, new Rect(0, 0, w, stage));
        ctx.FillRectangle(Linear(ColorUtil.WithAlpha(RoomDeep, 0xB3), ColorUtil.WithAlpha(RoomDeep, 0), 0, 0, 0, 1), new Rect(0, 0, w, 160));
    }

    // ===== softened pictures: off-thread, LRU (64), keyed by path + modification time =====

    internal sealed record Soft(Bitmap Bitmap, Color Light);

    private static class SoftCache
    {
        private const int Capacity = 64;
        private static readonly LinkedList<(string Key, Soft Value)> Order = new();
        private static readonly Dictionary<string, LinkedListNode<(string Key, Soft Value)>> Map = new();

        public static Soft? Get(string path)
        {
            if (!File.Exists(path)) return null;
            var key = path + "|" + File.GetLastWriteTimeUtc(path).Ticks;
            lock (Map)
            {
                if (Map.TryGetValue(key, out var hit))
                {
                    Order.Remove(hit);
                    Order.AddFirst(hit);
                    return hit.Value.Value;
                }
            }
            var made = Soften(path);
            if (made == null) return null;
            lock (Map)
            {
                var node = Order.AddFirst((key, made));
                Map[key] = node;
                while (Order.Count > Capacity)
                {
                    var last = Order.Last!;
                    Order.RemoveLast();
                    Map.Remove(last.Value.Key);
                }
            }
            return made;
        }
    }

    private const int OutW = 64, OutH = 36;

    /// <summary>Decode to 96 px, crop 16:9, resample to 64×36, blur, light colour. Runs on a worker thread.</summary>
    internal static Soft? Soften(string path)
    {
        Bitmap decoded;
        using (var stream = File.OpenRead(path))
            decoded = Bitmap.DecodeToWidth(stream, 96, BitmapInterpolationMode.HighQuality);
        using (decoded)
        {
            var sw = decoded.PixelSize.Width;
            var sh = decoded.PixelSize.Height;
            if (sw <= 0 || sh <= 0) return null;
            var src = new byte[sw * sh * 4];
            unsafe
            {
                fixed (byte* p = src)
                    decoded.CopyPixels(new PixelRect(0, 0, sw, sh), (nint)p, src.Length, sw * 4);
            }
            // crop to 16:9 around the centre
            double cw = sw, ch = sw * 9.0 / 16;
            if (ch > sh) { ch = sh; cw = sh * 16.0 / 9; }
            var cx = (sw - cw) / 2;
            var cy = (sh - ch) / 2;
            var dst = new float[OutW * OutH * 4];
            for (var y = 0; y < OutH; y++)
                for (var x = 0; x < OutW; x++)
                {
                    var fx = cx + (x + 0.5) * cw / OutW - 0.5;
                    var fy = cy + (y + 0.5) * ch / OutH - 0.5;
                    Sample(src, sw, sh, fx, fy, dst, (y * OutW + x) * 4);
                }
            for (var pass = 0; pass < 3; pass++) BoxBlur(dst, OutW, OutH, 2);

            // saturation-weighted mean → picture light
            double r = 0, g = 0, b = 0, wsum = 0;
            for (var i = 0; i < OutW * OutH; i++)
            {
                double bb = dst[i * 4], gg = dst[i * 4 + 1], rr = dst[i * 4 + 2];
                var max = Math.Max(rr, Math.Max(gg, bb));
                var min = Math.Min(rr, Math.Min(gg, bb));
                var weight = (max <= 0 ? 0 : (max - min) / max) + 0.05;
                r += rr * weight; g += gg * weight; b += bb * weight; wsum += weight;
            }
            var mean = Color.FromRgb(ToByte(r / wsum), ToByte(g / wsum), ToByte(b / wsum));
            var (h, s, l) = ColorUtil.ToHsl(mean);
            var light = ColorUtil.FromHsl(h, Math.Min(s, 0.70), Math.Clamp(l, 0.55, 0.70));

            var bmp = new WriteableBitmap(new PixelSize(OutW, OutH), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
            using (var fb = bmp.Lock())
            {
                var row = new byte[OutW * 4];
                for (var y = 0; y < OutH; y++)
                {
                    for (var x = 0; x < OutW * 4; x++)
                    {
                        var v = dst[y * OutW * 4 + x];
                        row[x] = (x & 3) == 3 ? (byte)255 : ToByte(v);
                    }
                    System.Runtime.InteropServices.Marshal.Copy(row, 0, fb.Address + y * fb.RowBytes, row.Length);
                }
            }
            return new Soft(bmp, light);
        }
    }

    private static byte ToByte(double v) => (byte)Math.Clamp(Math.Round(v), 0, 255);

    /// <summary>Bilinear sample of a BGRA (premultiplied) buffer, alpha composited over the room colour.</summary>
    private static void Sample(byte[] src, int w, int h, double fx, double fy, float[] dst, int o)
    {
        var x0 = Math.Clamp((int)Math.Floor(fx), 0, w - 1);
        var y0 = Math.Clamp((int)Math.Floor(fy), 0, h - 1);
        var x1 = Math.Min(w - 1, x0 + 1);
        var y1 = Math.Min(h - 1, y0 + 1);
        var tx = Math.Clamp(fx - x0, 0, 1);
        var ty = Math.Clamp(fy - y0, 0, 1);
        for (var c = 0; c < 3; c++)
        {
            double P(int x, int y)
            {
                var i = (y * w + x) * 4;
                var a = src[i + 3] / 255.0;
                var room = c switch { 0 => Room.B, 1 => Room.G, _ => Room.R };
                return src[i + c] + (1 - a) * room; // premultiplied over the room
            }
            var top = P(x0, y0) * (1 - tx) + P(x1, y0) * tx;
            var bottom = P(x0, y1) * (1 - tx) + P(x1, y1) * tx;
            dst[o + c] = (float)(top * (1 - ty) + bottom * ty);
        }
        dst[o + 3] = 255;
    }

    private static void BoxBlur(float[] px, int w, int h, int radius)
    {
        var tmp = new float[px.Length];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
                for (var c = 0; c < 3; c++)
                {
                    float sum = 0; var n = 0;
                    for (var k = -radius; k <= radius; k++)
                    {
                        var xx = Math.Clamp(x + k, 0, w - 1);
                        sum += px[(y * w + xx) * 4 + c]; n++;
                    }
                    tmp[(y * w + x) * 4 + c] = sum / n;
                }
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
                for (var c = 0; c < 3; c++)
                {
                    float sum = 0; var n = 0;
                    for (var k = -radius; k <= radius; k++)
                    {
                        var yy = Math.Clamp(y + k, 0, h - 1);
                        sum += tmp[(yy * w + x) * 4 + c]; n++;
                    }
                    px[(y * w + x) * 4 + c] = sum / n;
                }
    }
}

/// <summary>The motif of a system as a 1.5 px outline in its light at 7 % (TV light without a picture, DESIGN 2.5.9).</summary>
internal sealed class MotifGhost : Control
{
    private SystemLook? _look;

    public MotifGhost() { IsHitTestVisible = false; }

    public SystemLook? Look
    {
        get => _look;
        set { if (_look == value) return; _look = value; InvalidateVisual(); }
    }

    public override void Render(DrawingContext context)
    {
        if (_look == null || Bounds.Width <= 0) return;
        MotifCatalog.DrawOutline(context, _look, new Rect(Bounds.Size), 0.07, 1.5);
    }
}
