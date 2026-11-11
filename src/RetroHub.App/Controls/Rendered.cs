using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace RetroHub.App.Controls;

/// <summary>
/// The rendered objects of the room (R3 "gezeichnet, nicht gecodet"): spines, the open front, sleeves, boards, the rail,
/// count tags, the bookend and the motif plaques were modelled and lit in Blender (scripts in build/tools/assets) and
/// baked to PNG sprites at 1x and 2x (<c>Assets/Rendered</c>). One render serves every system colour: the tinted regions
/// were rendered at albedo 0, and grey+alpha LIGHT MAPS (<c>-band</c>, <c>-body</c>, <c>-color</c>, …) say where and how
/// strongly each colour shows. A sprite with its tints is composited ONCE on the CPU (shadow, object, then each colour
/// through its map) into a cached premultiplied bitmap, so drawing it is one <c>DrawImage</c> – a wall of twelve spines
/// costs twelve bitmap draws, no opacity masks per frame. The 1x or 2x source is picked by the device pixels the object
/// covers (UiScale × render scaling); scaled draws use high-quality filtering, 1:1 draws are snapped to device pixels.
/// Without a render platform (plain unit tests) every call returns null/false and the callers draw their old shapes.
/// </summary>
public static class Rendered
{
    private const string Root = "avares://RetroHub.App/Assets/Rendered/";

    /// <summary>The metadata of one sprite (<c>&lt;name&gt;.json</c>; every rect in 1x px from the sprite's top-left).</summary>
    public sealed class Meta
    {
        public Size Canvas { get; init; }
        /// <summary>The object's front face (what the layout places).</summary>
        public Rect Front { get; init; }
        public IReadOnlyDictionary<string, Rect> Rects { get; init; } = new Dictionary<string, Rect>();
        public IReadOnlyDictionary<string, double> Values { get; init; } = new Dictionary<string, double>();
        public Thickness Slice { get; init; }
        public IReadOnlyList<string> Maps { get; init; } = [];

        public Rect Rect(string key) => Rects.TryGetValue(key, out var r) ? r : default;
        public double Value(string key, double fallback = 0) => Values.TryGetValue(key, out var v) ? v : fallback;
    }

    private static readonly Dictionary<string, Meta?> Metas = new();
    private static readonly Dictionary<string, Layer?> Layers = new();
    private static readonly Dictionary<string, Bitmap?> Composites = new();
    private static readonly LinkedList<string> Order = new();
    private static long _bytes;
    private const long Budget = 160L * 1024 * 1024;
    private static bool? _available;

    /// <summary>Premultiplied BGRA pixels of one PNG layer.</summary>
    private sealed record Layer(int Width, int Height, byte[] Pixels);

    /// <summary>True when the sprites load (assets present and a render platform to decode them).</summary>
    public static bool Available
    {
        get
        {
            if (_available is { } a) return a;
            try { _available = Load("bookend@1x.png") != null; }
            catch { _available = false; }
            return _available.Value;
        }
    }

    /// <summary>The metadata of <paramref name="name"/> (null when missing).</summary>
    public static Meta? MetaOf(string name)
    {
        lock (Metas)
        {
            if (Metas.TryGetValue(name, out var m)) return m;
            m = null;
            try
            {
                var uri = new Uri(Root + name + ".json");
                if (AssetLoader.Exists(uri))
                {
                    using var s = AssetLoader.Open(uri);
                    using var doc = JsonDocument.Parse(s);
                    m = Parse(doc.RootElement);
                }
            }
            catch { m = null; }
            Metas[name] = m;
            return m;
        }
    }

    private static Meta Parse(JsonElement e)
    {
        Rect R(JsonElement a) => new(a[0].GetDouble(), a[1].GetDouble(), a[2].GetDouble(), a[3].GetDouble());
        var rects = new Dictionary<string, Rect>();
        var values = new Dictionary<string, double>();
        var maps = new List<string>();
        Size canvas = default;
        Thickness slice = default;
        foreach (var p in e.EnumerateObject())
        {
            var v = p.Value;
            switch (v.ValueKind)
            {
                case JsonValueKind.Array when p.Name == "canvas":
                    canvas = new Size(v[0].GetDouble(), v[1].GetDouble());
                    break;
                case JsonValueKind.Array when p.Name == "maps":
                    foreach (var x in v.EnumerateArray()) if (x.GetString() is { } sx) maps.Add(sx);
                    break;
                case JsonValueKind.Array when v.GetArrayLength() == 4:
                    rects[p.Name] = R(v);
                    break;
                case JsonValueKind.Array when v.GetArrayLength() == 2:
                    values[p.Name + "X"] = v[0].GetDouble();
                    values[p.Name + "Y"] = v[1].GetDouble();
                    break;
                case JsonValueKind.Number when p.Name == "slice":
                    slice = new Thickness(v.GetDouble(), 0, v.GetDouble(), 0);
                    values["slice"] = v.GetDouble();
                    break;
                case JsonValueKind.Object when p.Name == "slice":
                    slice = new Thickness(v.GetProperty("l").GetDouble(), v.GetProperty("t").GetDouble(), v.GetProperty("r").GetDouble(), v.GetProperty("b").GetDouble());
                    break;
                case JsonValueKind.Number:
                    values[p.Name] = v.GetDouble();
                    break;
            }
        }
        return new Meta
        {
            Canvas = canvas,
            Front = rects.TryGetValue("front", out var f) ? f : new Rect(canvas),
            Rects = rects,
            Values = values,
            Slice = slice,
            Maps = maps,
        };
    }

    /// <summary>Decodes one PNG to premultiplied BGRA (cached; null when missing).</summary>
    private static Layer? Load(string file)
    {
        lock (Layers)
        {
            if (Layers.TryGetValue(file, out var l)) return l;
            l = null;
            var uri = new Uri(Root + file);
            if (AssetLoader.Exists(uri))
            {
                using var s = AssetLoader.Open(uri);
                using var bmp = new Bitmap(s);
                var size = bmp.PixelSize;
                using var wb = new WriteableBitmap(size, new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
                using (var fb = wb.Lock())
                {
                    bmp.CopyPixels(fb);
                    var px = new byte[size.Width * size.Height * 4];
                    for (var y = 0; y < size.Height; y++)
                        Marshal.Copy(fb.Address + y * fb.RowBytes, px, y * size.Width * 4, size.Width * 4);
                    l = new Layer(size.Width, size.Height, px);
                }
            }
            Layers[file] = l;
            return l;
        }
    }

    /// <summary>Device pixels per DIP of <paramref name="visual"/> (its UiScale × the render scaling).</summary>
    public static double DeviceScale(Visual visual)
    {
        var top = TopLevel.GetTopLevel(visual);
        if (top == null) return 1;
        var m = visual.TransformToVisual(top) ?? Matrix.Identity;
        return Math.Max(0.01, Math.Sqrt(m.M11 * m.M11 + m.M12 * m.M12)) * top.RenderScaling;
    }

    /// <summary>1 or 2: the source that covers <paramref name="devicePxPerSpritePx"/> device pixels per 1x sprite pixel.</summary>
    public static int PickScale(double devicePxPerSpritePx) => devicePxPerSpritePx > 1.02 ? 2 : 1;

    /// <summary>A colour shown through one light map of a sprite.</summary>
    public readonly record struct Tint(string Role, Color Color);

    /// <summary>
    /// <paramref name="name"/> at <paramref name="scale"/> with its tints, composited: <c>-shadow</c> (when
    /// <paramref name="shadow"/>), the object, then each tint through its <c>-role</c> map. Cached (LRU, ≈160 MB).
    /// </summary>
    public static Bitmap? Composite(string name, int scale, bool shadow, params Tint[] tints)
    {
        var key = name + "@" + scale + (shadow ? "s" : "") + string.Concat(tints.Select(t => "|" + t.Role + t.Color.ToUInt32().ToString("X8")));
        lock (Composites)
        {
            if (Composites.TryGetValue(key, out var hit))
            {
                Touch(key);
                return hit;
            }
        }
        Bitmap? result = null;
        try
        {
            var baseLayer = Load($"{name}@{scale}x.png");
            if (baseLayer != null)
            {
                var w = baseLayer.Width;
                var h = baseLayer.Height;
                var px = new byte[w * h * 4];
                if (shadow && Load($"{name}-shadow@{scale}x.png") is { } sh && sh.Width == w && sh.Height == h) Buffer.BlockCopy(sh.Pixels, 0, px, 0, px.Length);
                Over(px, baseLayer.Pixels);
                foreach (var t in tints)
                    if (Load($"{name}-{t.Role}@{scale}x.png") is { } map && map.Width == w && map.Height == h) Fill(px, map.Pixels, t.Color);
                result = ToBitmap(px, w, h);
            }
        }
        catch { result = null; }
        Store(key, result, result == null ? 0 : (long)result.PixelSize.Width * result.PixelSize.Height * 4);
        return result;
    }

    private static void Store(string key, Bitmap? bmp, long bytes)
    {
        lock (Composites)
        {
            Composites[key] = bmp;
            Order.AddFirst(key);
            _bytes += bytes;
            while (_bytes > Budget && Order.Count > 1)
            {
                var old = Order.Last!.Value;
                Order.RemoveLast();
                if (Composites.Remove(old, out var gone) && gone != null) _bytes -= (long)gone.PixelSize.Width * gone.PixelSize.Height * 4;
            }
        }
    }

    private static void Touch(string key)
    {
        var node = Order.Find(key);
        if (node == null || node == Order.First) return;
        Order.Remove(node);
        Order.AddFirst(node);
    }

    private static Bitmap ToBitmap(byte[] px, int w, int h)
    {
        var handle = GCHandle.Alloc(px, GCHandleType.Pinned);
        try
        {
            return new WriteableBitmap(PixelFormat.Bgra8888, AlphaFormat.Premul, handle.AddrOfPinnedObject(), new PixelSize(w, h), new Vector(96, 96), w * 4);
        }
        finally { handle.Free(); }
    }

    /// <summary>Premultiplied "source over destination".</summary>
    private static void Over(byte[] dst, byte[] src)
    {
        for (var i = 0; i < dst.Length; i += 4)
        {
            int a = src[i + 3];
            if (a == 0) continue;
            if (a == 255)
            {
                dst[i] = src[i]; dst[i + 1] = src[i + 1]; dst[i + 2] = src[i + 2]; dst[i + 3] = 255;
                continue;
            }
            var k = 255 - a;
            dst[i] = (byte)(src[i] + (dst[i] * k + 127) / 255);
            dst[i + 1] = (byte)(src[i + 1] + (dst[i + 1] * k + 127) / 255);
            dst[i + 2] = (byte)(src[i + 2] + (dst[i + 2] * k + 127) / 255);
            dst[i + 3] = (byte)(a + (dst[i + 3] * k + 127) / 255);
        }
    }

    /// <summary>
    /// <paramref name="c"/> over the destination with the map's alpha (= FillRectangle(c) under an opacity mask of the map).
    /// </summary>
    private static void Fill(byte[] dst, byte[] map, Color c)
    {
        int cb = c.B, cg = c.G, cr = c.R, ca = c.A;
        for (var i = 0; i < dst.Length; i += 4)
        {
            var a = map[i + 3] * ca / 255;
            if (a == 0) continue;
            var k = 255 - a;
            dst[i] = (byte)((cb * a + dst[i] * k + 127) / 255);
            dst[i + 1] = (byte)((cg * a + dst[i + 1] * k + 127) / 255);
            dst[i + 2] = (byte)((cr * a + dst[i + 2] * k + 127) / 255);
            dst[i + 3] = (byte)(a + (dst[i + 3] * k + 127) / 255);
        }
    }

    private static readonly RenderOptions Smooth = new() { BitmapInterpolationMode = BitmapInterpolationMode.HighQuality };

    /// <summary>
    /// Draws <paramref name="name"/> so that its front face lands on <paramref name="front"/> (local DIP; the canvas around
    /// it – shadow, top and side faces – follows at the same scale). False when the sprite is not available.
    /// </summary>
    public static bool Draw(DrawingContext ctx, Visual visual, string name, Rect front, bool shadow = true, params Tint[] tints)
    {
        if (!Available || MetaOf(name) is not { } m || m.Front.Width <= 0 || m.Front.Height <= 0) return false;
        var sx = front.Width / m.Front.Width;
        var sy = front.Height / m.Front.Height;
        var dev = DeviceScale(visual);
        var scale = PickScale(Math.Max(sx, sy) * dev);
        if (Composite(name, scale, shadow, tints) is not { } bmp) return false;
        var dest = new Rect(front.X - m.Front.X * sx, front.Y - m.Front.Y * sy, m.Canvas.Width * sx, m.Canvas.Height * sy);
        // the bitmap may be a pixel larger than the canvas (rounding in the bake): map its pixels, not the canvas
        dest = dest.WithWidth(bmp.PixelSize.Width / (double)scale * sx).WithHeight(bmp.PixelSize.Height / (double)scale * sy);
        Blit(ctx, bmp, dest, dev, Math.Abs(sx - 1) < 0.001 && Math.Abs(sy - 1) < 0.001 && Math.Abs(dev - scale) < 0.001);
        return true;
    }

    /// <summary>Draws a composited bitmap into <paramref name="dest"/> (snapped when it maps 1:1 to device pixels).</summary>
    internal static void Blit(DrawingContext ctx, Bitmap bmp, Rect dest, double deviceScale, bool exact)
    {
        if (exact)
        {
            dest = new Rect(Math.Round(dest.X * deviceScale) / deviceScale, Math.Round(dest.Y * deviceScale) / deviceScale, dest.Width, dest.Height);
            ctx.DrawImage(bmp, new Rect(bmp.Size), dest);
            return;
        }
        using (ctx.PushRenderOptions(Smooth))
            ctx.DrawImage(bmp, new Rect(bmp.Size), dest);
    }

    private static readonly Dictionary<string, Bitmap?> Strips = new();

    /// <summary>
    /// A horizontally sliced part (<paramref name="name"/><c>-left</c>, tiled <c>-tile</c>, <c>-right</c>, each over its
    /// shadow piece) built to <paramref name="width"/> 1x px of FRONT (rail, count tag); cached per width and scale.
    /// Returns the bitmap and the front's offset inside it (1x px).
    /// </summary>
    public static (Bitmap? Bitmap, Point FrontOffset, Size Size) Strip(string name, double width, int scale, params Tint[] tints)
    {
        if (!Available || MetaOf(name) is not { } m) return (null, default, default);
        var pad = m.Front.X;
        var total = (int)Math.Round((width + 2 * pad) * scale);
        var key = $"{name}|{total}|{scale}" + string.Concat(tints.Select(t => "|" + t.Role + t.Color.ToUInt32().ToString("X8")));
        Bitmap? bmp;
        lock (Strips)
        {
            if (!Strips.TryGetValue(key, out bmp))
            {
                bmp = null;
                try { bmp = BuildStrip(name, total, scale, tints); }
                catch { bmp = null; }
                Strips[key] = bmp;
            }
        }
        if (bmp == null) return (null, default, default);
        return (bmp, m.Front.TopLeft, new Size(bmp.PixelSize.Width / (double)scale, bmp.PixelSize.Height / (double)scale));
    }

    private static Bitmap? BuildStrip(string name, int total, int scale, Tint[] tints)
    {
        var left = Load($"{name}-left@{scale}x.png");
        var right = Load($"{name}-right@{scale}x.png");
        var tile = Load($"{name}-tile@{scale}x.png");
        if (left == null || right == null || tile == null) return null;
        var h = left.Height;
        total = Math.Max(total, left.Width + right.Width);
        var px = new byte[total * h * 4];
        void Lay(string part, Layer? l)
        {
            if (l == null) return;
            if (part == "left") Blend(px, total, h, l, 0, l.Width);
            else if (part == "right") Blend(px, total, h, l, total - l.Width, l.Width);
            else
                for (var x = left.Width; x < total - right.Width; x += l.Width)
                    Blend(px, total, h, l, x, Math.Min(l.Width, total - right.Width - x));
        }
        foreach (var part in new[] { "tile", "left", "right" }) Lay(part, Load($"{name}-shadow-{part}@{scale}x.png"));
        foreach (var part in new[] { "tile", "left", "right" }) Lay(part, part == "tile" ? tile : part == "left" ? left : right);
        // R3 QA 3: a sliced part may carry a light map too (the Reiter's printed stripe): built the same way, then filled
        foreach (var t in tints)
        {
            if (Load($"{name}-{t.Role}-tile@{scale}x.png") is not { } mt) continue;
            var map = new byte[px.Length];
            var keep = px;
            px = map;
            Lay("tile", mt);
            Lay("left", Load($"{name}-{t.Role}-left@{scale}x.png"));
            Lay("right", Load($"{name}-{t.Role}-right@{scale}x.png"));
            px = keep;
            Fill(px, map, t.Color);
        }
        return ToBitmap(px, total, h);
    }

    private static readonly Dictionary<string, Bitmap?> Marks = new();

    /// <summary>
    /// R3 QA 3: a small overlay render (the rail's notch, <paramref name="file"/>@Nx.png, cut from the same render as the
    /// part it sits on) centred at <paramref name="centerX"/> on a sliced part drawn into <paramref name="partFront"/>.
    /// </summary>
    public static void DrawMark(DrawingContext ctx, Visual visual, string file, Rect partFront, Meta partMeta, double centerX, double k)
    {
        var dev = DeviceScale(visual);
        var scale = PickScale(k * dev);
        Bitmap? bmp;
        lock (Marks)
        {
            if (!Marks.TryGetValue(file + scale, out bmp))
            {
                bmp = null;
                try { if (Load($"{file}@{scale}x.png") is { } l) bmp = ToBitmap(l.Pixels, l.Width, l.Height); }
                catch { bmp = null; }
                Marks[file + scale] = bmp;
            }
        }
        if (bmp == null) return;
        var w = bmp.PixelSize.Width / (double)scale * k;
        var h = bmp.PixelSize.Height / (double)scale * k;
        var dest = new Rect(Math.Round(centerX - w / 2), partFront.Y - partMeta.Front.Y * k, w, h);
        Blit(ctx, bmp, dest, dev, Math.Abs(k - 1) < 0.001 && Math.Abs(dev - scale) < 0.001);
    }

    private static readonly Dictionary<(string, int, int, int), Bitmap?> Nines = new();

    /// <summary>
    /// R3 QA 3 (the TV cabinet): <paramref name="name"/> (object over its shadow) nine-sliced so that its front lands on
    /// <paramref name="front"/>; <paramref name="border"/> are the borders in the render's 1x px (measured on its front),
    /// <paramref name="target"/> the same borders on screen (DIP) – each side scales on its own, the middle stretches.
    /// Cached per device size. False when not available.
    /// </summary>
    public static bool DrawNine(DrawingContext ctx, Visual visual, string name, Rect front, Thickness border, Thickness target)
    {
        if (!Available || MetaOf(name) is not { } m || front.Width <= 0 || front.Height <= 0) return false;
        var dev = DeviceScale(visual);
        var scale = PickScale(Math.Max(target.Left / Math.Max(1, border.Left), target.Top / Math.Max(1, border.Top)) * dev);
        // the canvas pads (shadow) scale like the neighbouring border
        var kl = target.Left / border.Left; var kt = target.Top / border.Top; var kr = target.Right / border.Right; var kb = target.Bottom / border.Bottom;
        var padL = m.Front.X; var padT = m.Front.Y; var padR = m.Canvas.Width - m.Front.Right; var padB = m.Canvas.Height - m.Front.Bottom;
        var outer = new Rect(front.X - padL * kl, front.Y - padT * kt, front.Width + padL * kl + padR * kr, front.Height + padT * kt + padB * kb);
        var tw = (int)Math.Round(outer.Width * dev);
        var th = (int)Math.Round(outer.Height * dev);
        var key = (name, scale, tw, th);
        Bitmap? bmp;
        lock (Nines)
        {
            if (!Nines.TryGetValue(key, out bmp))
            {
                bmp = null;
                try
                {
                    var px = new byte[tw * th * 4];
                    var any = false;
                    foreach (var layer in new[] { name + "-shadow", name })
                    {
                        if (Load($"{layer}@{scale}x.png") is not { } src) continue;
                        // source borders in source px (pad + border), target borders in device px
                        var sb = new Thickness((padL + border.Left) * scale, (padT + border.Top) * scale, (padR + border.Right) * scale, (padB + border.Bottom) * scale);
                        var db = new Thickness((padL * kl + target.Left) * dev, (padT * kt + target.Top) * dev, (padR * kr + target.Right) * dev, (padB * kb + target.Bottom) * dev);
                        Over(px, NineSlice2(src, sb, db, tw, th));
                        any = true;
                    }
                    if (any) bmp = ToBitmap(px, tw, th);
                }
                catch { bmp = null; }
                Nines[key] = bmp;
                if (Nines.Count > 64) Nines.Clear();
            }
        }
        if (bmp == null) return false;
        var dest = new Rect(Math.Round(outer.X * dev) / dev, Math.Round(outer.Y * dev) / dev, tw / dev, th / dev);
        ctx.DrawImage(bmp, new Rect(bmp.Size), dest);
        return true;
    }

    /// <summary>Nine-slice with independent source and target borders per side (bilinear).</summary>
    private static byte[] NineSlice2(Layer src, Thickness s, Thickness d, int tw, int th)
    {
        static double Axis(double t, int target, int size, double sa, double sb, double da, double db)
        {
            if (t < da) return t * sa / Math.Max(1e-3, da);
            if (t >= target - db) return size - (target - t) * sb / Math.Max(1e-3, db);
            var mid = Math.Max(1e-3, target - da - db);
            return sa + (t - da) * (size - sa - sb) / mid;
        }
        var map = new double[tw];
        var mapY = new double[th];
        for (var x = 0; x < tw; x++) map[x] = Axis(x + 0.5, tw, src.Width, s.Left, s.Right, d.Left, d.Right) - 0.5;
        for (var y = 0; y < th; y++) mapY[y] = Axis(y + 0.5, th, src.Height, s.Top, s.Bottom, d.Top, d.Bottom) - 0.5;
        var dst = new byte[tw * th * 4];
        var sp = src.Pixels;
        for (var y = 0; y < th; y++)
        {
            var fy = Math.Clamp(mapY[y], 0, src.Height - 1);
            var y0 = (int)fy; var y1 = Math.Min(src.Height - 1, y0 + 1); var wy = fy - y0;
            for (var x = 0; x < tw; x++)
            {
                var fx = Math.Clamp(map[x], 0, src.Width - 1);
                var x0 = (int)fx; var x1 = Math.Min(src.Width - 1, x0 + 1); var wx = fx - x0;
                var i00 = (y0 * src.Width + x0) * 4; var i01 = (y0 * src.Width + x1) * 4;
                var i10 = (y1 * src.Width + x0) * 4; var i11 = (y1 * src.Width + x1) * 4;
                var o = (y * tw + x) * 4;
                for (var c = 0; c < 4; c++)
                {
                    var v = (sp[i00 + c] * (1 - wx) + sp[i01 + c] * wx) * (1 - wy) + (sp[i10 + c] * (1 - wx) + sp[i11 + c] * wx) * wy;
                    dst[o + c] = (byte)Math.Clamp((int)Math.Round(v), 0, 255);
                }
            }
        }
        return dst;
    }

    /// <summary>Blends <paramref name="src"/> (its first <paramref name="cols"/> columns) over the strip at column <paramref name="x0"/>.</summary>
    private static void Blend(byte[] dst, int dw, int dh, Layer src, int x0, int cols)
    {
        var rows = Math.Min(dh, src.Height);
        for (var y = 0; y < rows; y++)
            for (var x = 0; x < cols; x++)
            {
                var dx = x0 + x;
                if (dx < 0 || dx >= dw) continue;
                var si = (y * src.Width + x) * 4;
                var di = (y * dw + dx) * 4;
                int a = src.Pixels[si + 3];
                if (a == 0) continue;
                var k = 255 - a;
                dst[di] = (byte)(src.Pixels[si] + (dst[di] * k + 127) / 255);
                dst[di + 1] = (byte)(src.Pixels[si + 1] + (dst[di + 1] * k + 127) / 255);
                dst[di + 2] = (byte)(src.Pixels[si + 2] + (dst[di + 2] * k + 127) / 255);
                dst[di + 3] = (byte)(a + (dst[di + 3] * k + 127) / 255);
            }
    }

    /// <summary>
    /// Draws a sliced part with its front at <paramref name="front"/> (the front's height scales the part; the width is
    /// built to fit). False when not available.
    /// </summary>
    public static bool DrawStrip(DrawingContext ctx, Visual visual, string name, Rect front, params Tint[] tints)
    {
        if (!Available || MetaOf(name) is not { } m || m.Front.Height <= 0) return false;
        var k = front.Height / m.Front.Height;
        var dev = DeviceScale(visual);
        var scale = PickScale(k * dev);
        var (bmp, off, size) = Strip(name, front.Width / k, scale, tints);
        if (bmp == null) return false;
        var dest = new Rect(front.X - off.X * k, front.Y - off.Y * k, size.Width * k, size.Height * k);
        Blit(ctx, bmp, dest, dev, Math.Abs(k - 1) < 0.001 && Math.Abs(dev - scale) < 0.001);
        return true;
    }

    /// <summary>
    /// A board (<c>board-stage</c> / <c>board-edge</c>): its tile over its shadow tile, repeated along <paramref name="front"/>
    /// (the front face lands on it; the top face shows above it, the shadow below). False when not available.
    /// </summary>
    public static bool DrawBoard(DrawingContext ctx, Visual? visual, string name, Rect front)
    {
        if (!Available || MetaOf(name) is not { } m || m.Front.Height <= 0 || front.Width <= 0) return false;
        var k = front.Height / m.Front.Height;
        var dev = visual != null ? DeviceScale(visual) : 1;
        var scale = PickScale(k * dev);
        if (BoardTile(name, scale) is not { } tile) return false;
        var tw = tile.PixelSize.Width / (double)scale;
        var th = tile.PixelSize.Height / (double)scale;
        var y = front.Y - m.Front.Y * k;
        using (ctx.PushClip(new Rect(front.X, y, front.Width, th * k)))
        using (ctx.PushRenderOptions(Smooth))
            for (var x = front.X; x < front.Right; x += tw)
                ctx.DrawImage(tile, new Rect(tile.Size), new Rect(x, y, tw + 0.5, th * k));
        return true;
    }

    private static readonly Dictionary<(int, int, bool), Bitmap?> Cases = new();

    /// <summary>
    /// The generic keep case (Blender: dark plastic with a clear sleeve, no product shape) around a cover drawn by the app
    /// – the placeholder of a game without a cover (R3: the flat card read as drawn). The case is nine-sliced to the
    /// cover's size in device pixels and cached per size, as two bitmaps: <paramref name="over"/> false = its shadow
    /// (under the cover), true = the sleeve's shade and gloss and the frame with the window (over the cover). A grid of
    /// one box format reuses one pair: two bitmap draws per tile.
    /// </summary>
    public static bool DrawCase(DrawingContext ctx, Visual visual, Rect art, bool over)
    {
        if (!Available || art.Width < 24 || art.Height < 24 || MetaOf("case") is not { } m) return false;
        var dev = DeviceScale(visual);
        // the frame keeps its 1x thickness (the window grows with the cover), so small grid boxes keep a thin frame
        var scale = PickScale(dev);
        var k = dev / scale;                                  // source px -> device px
        var padL = m.Front.X; var padT = m.Front.Y;
        var padR = m.Canvas.Width - m.Front.Right; var padB = m.Canvas.Height - m.Front.Bottom;
        var tw = (int)Math.Round((art.Width + padL + padR) * dev);
        var th = (int)Math.Round((art.Height + padT + padB) * dev);
        Bitmap? bmp;
        lock (Cases)
        {
            if (!Cases.TryGetValue((tw, th, over), out bmp))
            {
                bmp = null;
                try { bmp = BuildCase(m, tw, th, scale, k, over); }
                catch { bmp = null; }
                Cases[(tw, th, over)] = bmp;
                if (Cases.Count > 256) Cases.Clear();
            }
        }
        if (bmp == null) return false;
        var dest = new Rect(art.X - padL, art.Y - padT, tw / dev, th / dev);
        dest = new Rect(Math.Round(dest.X * dev) / dev, Math.Round(dest.Y * dev) / dev, dest.Width, dest.Height);
        ctx.DrawImage(bmp, new Rect(bmp.Size), dest);
        return true;
    }

    private static Bitmap? BuildCase(Meta m, int tw, int th, int scale, double k, bool over)
    {
        var slice = m.Slice;
        var px = new byte[tw * th * 4];
        var layers = over ? new[] { "case-shade", "case-gloss", "case" } : new[] { "case-shadow" };
        var any = false;
        foreach (var name in layers)
        {
            if (Load($"{name}@{scale}x.png") is not { } src) continue;
            var sl = new Thickness(slice.Left * scale, slice.Top * scale, slice.Right * scale, slice.Bottom * scale);
            Over(px, NineSlice(src, sl, tw, th, k));
            any = true;
        }
        return any ? ToBitmap(px, tw, th) : null;
    }

    /// <summary>Nine-slices <paramref name="src"/> to <paramref name="tw"/> × <paramref name="th"/>: the borders scaled by <paramref name="k"/>, the middle stretched (bilinear).</summary>
    private static byte[] NineSlice(Layer src, Thickness sl, int tw, int th, double k)
    {
        var map = new double[tw];
        var mapY = new double[th];
        static double Axis(double t, int target, int size, double a, double b, double k)
        {
            var a2 = a * k; var b2 = b * k;
            if (t < a2) return t / k;
            if (t >= target - b2) return size - (target - t) / k;
            var mid = Math.Max(1e-3, target - a2 - b2);
            return a + (t - a2) * (size - a - b) / mid;
        }
        for (var x = 0; x < tw; x++) map[x] = Axis(x + 0.5, tw, src.Width, sl.Left, sl.Right, k) - 0.5;
        for (var y = 0; y < th; y++) mapY[y] = Axis(y + 0.5, th, src.Height, sl.Top, sl.Bottom, k) - 0.5;
        var dst = new byte[tw * th * 4];
        var sp = src.Pixels;
        for (var y = 0; y < th; y++)
        {
            var fy = Math.Clamp(mapY[y], 0, src.Height - 1);
            var y0 = (int)fy; var y1 = Math.Min(src.Height - 1, y0 + 1); var wy = fy - y0;
            for (var x = 0; x < tw; x++)
            {
                var fx = Math.Clamp(map[x], 0, src.Width - 1);
                var x0 = (int)fx; var x1 = Math.Min(src.Width - 1, x0 + 1); var wx = fx - x0;
                var i00 = (y0 * src.Width + x0) * 4; var i01 = (y0 * src.Width + x1) * 4;
                var i10 = (y1 * src.Width + x0) * 4; var i11 = (y1 * src.Width + x1) * 4;
                var o = (y * tw + x) * 4;
                for (var c = 0; c < 4; c++)
                {
                    var v = (sp[i00 + c] * (1 - wx) + sp[i01 + c] * wx) * (1 - wy) + (sp[i10 + c] * (1 - wx) + sp[i11 + c] * wx) * wy;
                    dst[o + c] = (byte)Math.Clamp((int)Math.Round(v), 0, 255);
                }
            }
        }
        return dst;
    }

    private static Bitmap? BoardTile(string name, int scale)
    {
        var key = name + "|tile|" + scale;
        lock (Strips)
        {
            if (Strips.TryGetValue(key, out var b)) return b;
            b = null;
            try
            {
                var tile = Load($"{name}-tile@{scale}x.png");
                if (tile != null)
                {
                    var px = new byte[tile.Pixels.Length];
                    if (Load($"{name}-shadow-tile@{scale}x.png") is { } sh && sh.Pixels.Length == px.Length) Buffer.BlockCopy(sh.Pixels, 0, px, 0, px.Length);
                    Over(px, tile.Pixels);
                    b = ToBitmap(px, tile.Width, tile.Height);
                }
            }
            catch { b = null; }
            Strips[key] = b;
            return b;
        }
    }
}

/// <summary>
/// A riveted metal tag (the rendered <c>tag-l</c>, sliced to its content's width) carrying a small text – the position
/// "Fach 4 von 12" on the Konsolen page. Falls back to a plain dark plate.
/// </summary>
public sealed class TagPlate : Avalonia.Controls.Decorator
{
    static TagPlate()
    {
        AffectsRender<TagPlate>(BoundsProperty);
    }

    public override void Render(DrawingContext context)
    {
        var r = new Rect(Bounds.Size);
        if (r.Width <= 0 || r.Height <= 0) return;
        // R3 QA 3: the position card in its clamp frame (rendered "sign"); the old riveted tag as the fallback
        if (!Rendered.DrawStrip(context, this, "sign", r) && !Rendered.DrawStrip(context, this, "tag-l", r))
            context.FillRectangle(Craft.Solid(Color.Parse("#30333A")), r);
    }
}
