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
/// passes ≈ gaussian), upsample ×4 to 256×144 and quantise with interleaved-gradient-noise dithering (R2), into a small
/// LRU; shown as an Image (UniformToFill, high quality) at 0.38. No BlurEffect.</item>
/// <item>Picture light: saturation-weighted mean colour of the 64×36 pixels, clamped to L 0.55–0.70, S ≤ 0.70, cached per game.</item>
/// <item>Scrims AND the glow at the source are computed together in float on a worker thread into ONE premultiplied
/// bitmap at device resolution (≤ 2560 px wide), the colour dithered by ±½ LSB (R2: drawn as gradients they quantised
/// into visible contour rings and vertical bands; the grain under the scrim could not reach them). Re-rendered per
/// window-size bucket (64 px), stage height, glow colour/strength and glow position.</item>
/// <item>Board light: the shared <see cref="ThemeManager.BoardLight"/> gets the picture light at 35 % (wall: light 22 %).</item>
/// <item>Grain (korn-128 at 6 %, QA 2: at 2.5 % it stayed under one LSB and the blurred backdrop banded into contour rings) only here, against banding.</item>
/// <item>Crossfade between two image layers (Light 420 ms), started only after <see cref="Motion.Dwell"/> of rest and
/// never during auto-repeat; when a repeat ends only the dwell restarts, and only if the target differs.</item>
/// </list>
/// Timers are Task.Delay + Dispatcher.Post (they also run under the headless test dispatcher).
/// </summary>
public sealed class RoomBackdrop : Panel
{
    public const double ImageOpacity = 0.38;

    private static readonly Color Room = Color.Parse("#14120F");
    private static readonly Color RoomDeep = Color.Parse("#0A0908");

    private readonly Border _ground;          // room or the system wall (flat tone, interpolates)
    private readonly MotifGhost _ghost;       // motif outline at 7 % when a game has no picture (on the flat wall tone)
    private readonly Image _a;
    private readonly Image _b;
    private readonly TextureFill _grain;
    private readonly Image _scrim;            // scrims + glow, one dithered bitmap
    private bool _aFront = true;

    private BackdropSpec _target = BackdropSpec.Room;
    private BackdropSpec? _shown;
    private int _version;
    private LightKey? _scrimKey;
    private int _scrimVersion;
    private Color _glowColour;
    private double _glowStrength;

    /// <summary>What the light bitmap depends on (sizes in DIP, 64-px buckets; glow in 1/1000 of the window).</summary>
    internal readonly record struct LightKey(int W, int H, int Stage, uint Glow, int GlowStrength, int GlowX, int GlowY, double Scale);

    /// <summary>Renders of the light bitmap so far (tests: a focus move that does not change the light renders nothing).</summary>
    internal int LightRenders { get; private set; }

    /// <summary>Opacity of the grain tile: about ±2 LSB on the dark room, enough to dither the 64×36 backdrop's steps.</summary>
    public const double GrainStrength = 0.06;

    public RoomBackdrop()
    {
        IsHitTestVisible = false;
        _ground = new Border { Background = new SolidColorBrush(Room) };
        _ghost = new MotifGhost { IsVisible = false };
        _a = MakeImage();
        _b = MakeImage();
        _grain = new TextureFill { Texture = Texture.Grain, Strength = GrainStrength };
        _scrim = new Image { Stretch = Stretch.Fill };
        RenderOptions.SetBitmapInterpolationMode(_scrim, BitmapInterpolationMode.HighQuality);
        Children.Add(_ground);
        Children.Add(_ghost);
        Children.Add(_a);
        Children.Add(_b);
        Children.Add(_grain);
        Children.Add(_scrim);
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
                SetGround(look == null ? Room : spec.WallShade > 0 ? ColorUtil.Mix(look.Wall, RoomDeep, spec.WallShade) : look.Wall, look: null);
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

    /// <summary>The glow at the source (part of the light bitmap; it is only shown with the scrims, i.e. in TV light).</summary>
    private void SetGlow(Color? colour, double strength)
    {
        _glowColour = colour ?? Colors.Transparent;
        _glowStrength = colour == null ? 0 : strength;
    }

    private void UpdateGlowGeometry()
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        var stage = Math.Max(1, StageBottom);
        // the glow moves with the subject: a new light bitmap when colour or position changed
        if (_scrim.IsVisible) UpdateScrim(immediate: _scrimKey == null);
        // the motif outline stands on the stage board, right, 80 % of the stage height (the detail: 40 %, M4 QA)
        var size = Math.Round(stage * _target.GhostShare);
        _ghost.Alpha = _target.GhostAlpha;
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

    // ===== the light bitmap: scrims + glow in float, dithered; rendered 150 ms after the last resize =====

    private void UpdateScrim(bool immediate)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        var scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        var key = new LightKey((int)Math.Ceiling(w / 64) * 64, (int)Math.Ceiling(h / 64) * 64, (int)Math.Round(StageBottom / 8) * 8,
            _glowStrength > 0 ? _glowColour.ToUInt32() : 0u, (int)Math.Round(_glowStrength * 1000),
            (int)Math.Round(_target.GlowX * 1000), (int)Math.Round(_target.GlowY * 1000), scale);
        if (_scrimKey == key) return;
        // a new light (colour, strength, position) renders at once; a resize or a moving board waits until it rests
        var newLight = _scrimKey is not { } old || old.Glow != key.Glow || old.GlowStrength != key.GlowStrength
                       || old.GlowX != key.GlowX || old.GlowY != key.GlowY;
        _scrimKey = key;
        var version = ++_scrimVersion;
        if (immediate || newLight) StartLightRender(key, version);
        else _ = Task.Delay(150).ContinueWith(_ => Dispatcher.UIThread.Post(() =>
        {
            if (version == _scrimVersion) StartLightRender(key, version);
        }), TaskScheduler.Default);
    }

    private void StartLightRender(LightKey key, int version)
    {
        _ = Task.Run(() => RenderLight(key)).ContinueWith(t => Dispatcher.UIThread.Post(() =>
        {
            if (t.IsFaulted) { Log.Warn("Room light: light bitmap failed", t.Exception); return; }
            if (version != _scrimVersion) { t.Result.Dispose(); return; }
            LightRenders++;
            var previous = _scrim.Source as IDisposable;
            _scrim.Source = t.Result;
            _scrim.Width = key.W;
            _scrim.Height = key.H;
            _scrim.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
            _scrim.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
            previous?.Dispose();
        }), TaskScheduler.Default);
    }

    /// <summary>Widest light bitmap; wider windows get it upscaled (the dither survives a factor below 2).</summary>
    private const int MaxLightWidth = 2560;

    /// <summary>
    /// The scrims and the glow, composited per pixel in float (premultiplied source-over, bottom to top):
    /// (a) horizontal room 94 % at the left → 52 % from 60 % of the width on, (b) vertical clear until 60 % of the stage →
    /// room at the board, room below, (c) vignette in room-deep (0 at 55 % → 60 % at the ellipse of 75 %), (d) head scrim
    /// room-deep 70 % → 0 over 160 px, (e) the glow: the picture light at its strength → 0 over 0.55 of the width around
    /// (GlowX, GlowY), only above the board. The colour is quantised with interleaved gradient noise (±½ LSB), so the
    /// output never shows contour rings; the alpha is rounded (its steps are below the noise).
    /// </summary>
    internal static WriteableBitmap RenderLight(LightKey key) => ToBitmap(LightPixels(key, out var pw, out var ph), pw, ph);

    internal static byte[] LightPixels(LightKey key, out int pw, out int ph)
    {
        var w = (double)key.W;
        var h = (double)key.H;
        var stage = (double)Math.Clamp(key.Stage, 1, key.H);
        var width = Math.Max(16, (int)Math.Round(key.W * key.Scale));
        var height = Math.Max(16, (int)Math.Round(key.H * key.Scale));
        if (width > MaxLightWidth) { height = (int)Math.Round(height * (double)MaxLightWidth / width); width = MaxLightWidth; }
        pw = width;
        ph = height;
        var sx = w / width;
        var sy = h / height;
        var glow = Color.FromUInt32(key.Glow);
        var glowA = key.GlowStrength / 1000.0;
        var gx = key.GlowX / 1000.0 * w;
        var gy = key.GlowY / 1000.0 * h;
        var gr = 0.55 * w;
        var vcx = 0.5 * w;
        var vcy = 0.42 * stage;
        var vrx = 0.75 * w;
        var vry = 0.75 * stage;
        var fadeTop = stage * 0.6;
        double rR = Room.R, rG = Room.G, rB = Room.B, dR = RoomDeep.R, dG = RoomDeep.G, dB = RoomDeep.B;
        var stride = width * 4;
        var pixels = new byte[stride * height];
        Parallel.For(0, height, py =>
        {
            var v = (py + 0.5) * sy;
            var row = py * stride;
            var vFade = v >= stage ? 1.0 : v <= fadeTop ? 0.0 : (v - fadeTop) / Math.Max(1, stage - fadeTop);
            var head = v < 160 ? 0xB3 / 255.0 * (1 - v / 160) : 0;
            var above = v < stage;
            var dyV = (v - vcy) / vry;
            var dyG = v - gy;
            for (var px = 0; px < width; px++)
            {
                var u = (px + 0.5) * sx;
                double cR = 0, cG = 0, cB = 0, a = 0;
                if (above)
                {
                    var t = u / w;
                    var hAlpha = t >= 0.6 ? 0x85 / 255.0 : (0xF0 + (0x85 - 0xF0) * (t / 0.6)) / 255.0;
                    Over(ref cR, ref cG, ref cB, ref a, rR, rG, rB, hAlpha);
                    Over(ref cR, ref cG, ref cB, ref a, rR, rG, rB, vFade);
                    var dx = (u - vcx) / vrx;
                    var tv = Math.Sqrt(dx * dx + dyV * dyV);
                    Over(ref cR, ref cG, ref cB, ref a, dR, dG, dB, 0.6 * Math.Clamp((tv - 0.55) / 0.45, 0, 1));
                    Over(ref cR, ref cG, ref cB, ref a, dR, dG, dB, head);
                    if (glowA > 0)
                    {
                        var ex = u - gx;
                        var d = Math.Sqrt(ex * ex + dyG * dyG) / gr;
                        if (d < 1) Over(ref cR, ref cG, ref cB, ref a, glow.R, glow.G, glow.B, glowA * (1 - d));
                    }
                }
                else
                {
                    Over(ref cR, ref cG, ref cB, ref a, rR, rG, rB, 1);
                    Over(ref cR, ref cG, ref cB, ref a, dR, dG, dB, head);
                }
                var noise = Ign(px, py) - 0.5;
                var a8 = (int)Math.Round(a * 255);
                var o = row + px * 4;
                pixels[o] = (byte)Math.Clamp((int)Math.Round(cB + noise), 0, a8);
                pixels[o + 1] = (byte)Math.Clamp((int)Math.Round(cG + noise), 0, a8);
                pixels[o + 2] = (byte)Math.Clamp((int)Math.Round(cR + noise), 0, a8);
                pixels[o + 3] = (byte)a8;
            }
        });
        return pixels;
    }

    /// <summary>Premultiplied source-over of one layer (colour 0–255, alpha 0–1) onto the accumulated pixel.</summary>
    private static void Over(ref double cR, ref double cG, ref double cB, ref double a, double r, double g, double b, double alpha)
    {
        if (alpha <= 0) return;
        var k = 1 - alpha;
        cR = r * alpha + cR * k;
        cG = g * alpha + cG * k;
        cB = b * alpha + cB * k;
        a = alpha + a * k;
    }

    /// <summary>Interleaved gradient noise (Jimenez 2014) in [0, 1): a blue-ish dither pattern without a visible tile.</summary>
    internal static double Ign(int x, int y)
    {
        var f = 0.06711056 * x + 0.00583715 * y;
        f -= Math.Floor(f);
        var g = 52.9829189 * f;
        return g - Math.Floor(g);
    }

    private static WriteableBitmap ToBitmap(byte[] pixels, int pw, int ph)
    {
        var bmp = new WriteableBitmap(new PixelSize(pw, ph), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using var fb = bmp.Lock();
        var stride = pw * 4;
        for (var y = 0; y < ph; y++)
            System.Runtime.InteropServices.Marshal.Copy(pixels, y * stride, fb.Address + y * fb.RowBytes, stride);
        return bmp;
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

            return new Soft(ToBitmap(Upsample(dst), OutW * Up, OutH * Up), light);
        }
    }

    /// <summary>Stored size factor of the softened picture: 64×36 blurred → 256×144 bilinear, then dithered to 8 bit.</summary>
    private const int Up = 4;

    /// <summary>
    /// The blurred 64×36 field (float BGRA) upsampled ×<see cref="Up"/> (bilinear, smooth after the blur) and quantised
    /// with interleaved gradient noise: the 8-bit steps of the old 64×36 bitmap became 30 px wide bands at 1920.
    /// </summary>
    internal static byte[] Upsample(float[] src)
    {
        int w = OutW * Up, h = OutH * Up;
        var px = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
        {
            var fy = Math.Clamp((y + 0.5) / Up - 0.5, 0, OutH - 1);
            var y0 = (int)fy;
            var y1 = Math.Min(OutH - 1, y0 + 1);
            var ty = fy - y0;
            for (var x = 0; x < w; x++)
            {
                var fx = Math.Clamp((x + 0.5) / Up - 0.5, 0, OutW - 1);
                var x0 = (int)fx;
                var x1 = Math.Min(OutW - 1, x0 + 1);
                var tx = fx - x0;
                var noise = Ign(x, y) - 0.5;
                var o = (y * w + x) * 4;
                for (var c = 0; c < 3; c++)
                {
                    var top = src[(y0 * OutW + x0) * 4 + c] * (1 - tx) + src[(y0 * OutW + x1) * 4 + c] * tx;
                    var bottom = src[(y1 * OutW + x0) * 4 + c] * (1 - tx) + src[(y1 * OutW + x1) * 4 + c] * tx;
                    px[o + c] = ToByte(top * (1 - ty) + bottom * ty + noise);
                }
                px[o + 3] = 255;
            }
        }
        return px;
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

    private double _alpha = 0.07;

    /// <summary>Strength of the outline (7 %; the detail stage 4 %).</summary>
    public double Alpha
    {
        get => _alpha;
        set { if (_alpha == value) return; _alpha = value; InvalidateVisual(); }
    }

    public override void Render(DrawingContext context)
    {
        if (_look == null || Bounds.Width <= 0) return;
        MotifCatalog.DrawOutline(context, _look, new Rect(Bounds.Size), Alpha, 1.5);
    }
}
