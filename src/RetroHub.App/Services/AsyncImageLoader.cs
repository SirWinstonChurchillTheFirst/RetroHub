using System.Security.Cryptography;
using System.Text;
using Avalonia.Media.Imaging;
using RetroHub.Core.Logging;

namespace RetroHub.App.Services;

/// <summary>
/// Loads cover images off the UI thread: <see cref="Bitmap.DecodeToWidth"/> on the thread pool (only the pixels
/// needed for the tile width), an in-memory LRU cache by (path, width), de-duplication of concurrent requests,
/// and cancellation when a tile is recycled before its image arrived. Missing/broken files yield null (placeholder).
/// With <see cref="DiskCacheFolder"/> set (data/cache/thumbs), large source images (&gt; <see cref="DiskCacheMinBytes"/>)
/// are decoded once and saved as a downscaled JPEG (quality 90; PNG when the image has transparent pixels) named after a hash of (path, size, mtime, width); later loads decode
/// the small file. Core has no image library, so this App-side cache is RetroHub's thumbnail cache.
/// </summary>
public sealed class AsyncImageLoader
{
    public static AsyncImageLoader Shared { get; } = new(capacity: 400);

    private readonly int _capacity;
    private readonly object _gate = new();
    private readonly LinkedList<(string Key, Bitmap Bitmap)> _lru = new();
    private readonly Dictionary<string, LinkedListNode<(string Key, Bitmap Bitmap)>> _map = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Task<Bitmap?>> _inflight = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _failed = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _decoders = new(Math.Clamp(Environment.ProcessorCount / 2, 2, 4));

    public AsyncImageLoader(int capacity) { _capacity = Math.Max(16, capacity); }

    /// <summary>Folder for downscaled copies of large covers; null = memory cache only.</summary>
    public string? DiskCacheFolder { get; set; }

    /// <summary>Source files at least this big get a disk thumbnail.</summary>
    public long DiskCacheMinBytes { get; set; } = 256 * 1024;

    /// <summary>Decode widths are bucketed so nearby tile sizes share cache entries.</summary>
    public static int Bucket(double pixelWidth) => pixelWidth <= 0 ? 256 : (int)(Math.Ceiling(pixelWidth / 64.0) * 64);

    public int Count { get { lock (_gate) return _map.Count; } }

    /// <summary>Cached bitmap if present (synchronous fast path for recycled tiles).</summary>
    public Bitmap? TryGetCached(string path, int width)
    {
        var key = Key(path, width);
        lock (_gate)
        {
            if (!_map.TryGetValue(key, out var node)) return null;
            _lru.Remove(node);
            _lru.AddFirst(node);
            return node.Value.Bitmap;
        }
    }

    public async Task<Bitmap?> LoadAsync(string path, int width, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var key = Key(path, width);
        Task<Bitmap?> task;
        lock (_gate)
        {
            if (_map.TryGetValue(key, out var node))
            {
                _lru.Remove(node);
                _lru.AddFirst(node);
                return node.Value.Bitmap;
            }
            if (_failed.Contains(key)) return null;
            if (_inflight.TryGetValue(key, out var existing)) task = existing;
            else
            {
                task = DecodeAsync(path, width, key);
                _inflight[key] = task;
            }
        }
        // the shared decode keeps running for other waiters; only this caller stops waiting
        return await task.WaitAsync(ct).ConfigureAwait(false);
    }

    private async Task<Bitmap?> DecodeAsync(string path, int width, string key)
    {
        await _decoders.WaitAsync().ConfigureAwait(false);
        try
        {
            var bitmap = await Task.Run(() => Decode(path, width)).ConfigureAwait(false);
            lock (_gate)
            {
                _inflight.Remove(key);
                if (bitmap == null) { _failed.Add(key); return null; }
                var node = new LinkedListNode<(string, Bitmap)>((key, bitmap));
                _lru.AddFirst(node);
                _map[key] = node;
                // evicted bitmaps are not disposed: a tile may still show one; the GC releases them
                while (_map.Count > _capacity && _lru.Last is { } last)
                {
                    _lru.RemoveLast();
                    _map.Remove(last.Value.Key);
                }
            }
            return bitmap;
        }
        catch (Exception ex)
        {
            Log.Warn($"Cover '{path}' could not be decoded", ex);
            lock (_gate)
            {
                _inflight.Remove(key);
                _failed.Add(key);
            }
            return null;
        }
        finally
        {
            _decoders.Release();
        }
    }

    private Bitmap? Decode(string path, int width)
    {
        var info = new FileInfo(path);
        if (!info.Exists) return null;
        var thumbBase = ThumbBase(info, width);
        if (thumbBase != null)
        {
            foreach (var ext in ThumbExtensions)
            {
                var thumb = thumbBase + ext;
                if (!File.Exists(thumb)) continue;
                try
                {
                    using var cached = new FileStream(thumb, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024);
                    return new Bitmap(cached);
                }
                catch (Exception ex)
                {
                    Log.Debug($"Thumbnail {thumb} unreadable, decoding the original: {ex.Message}");
                }
            }
        }
        Bitmap bitmap;
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024))
            bitmap = Bitmap.DecodeToWidth(stream, width, BitmapInterpolationMode.HighQuality);
        if (thumbBase != null) SaveThumb(bitmap, thumbBase);
        return bitmap;
    }

    private static readonly string[] ThumbExtensions = [".jpg", ".png"];

    /// <summary>Disk thumbnail path for a large file without extension (".jpg" opaque / ".png" with alpha), or null (no cache folder / small file).</summary>
    internal string? ThumbBase(FileInfo source, int width)
    {
        var folder = DiskCacheFolder;
        if (folder == null || source.Length < DiskCacheMinBytes) return null;
        var id = $"{source.FullName.ToLowerInvariant()}|{source.Length}|{source.LastWriteTimeUtc.Ticks}|{width}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id)), 0, 12).ToLowerInvariant();
        return Path.Combine(folder, hash);
    }

    /// <summary>
    /// Saves the decoded bitmap: JPEG (small) when it is fully opaque, else PNG — JPEG has no alpha channel, so
    /// covers with transparent corners/edges (common in libretro-thumbnails boxarts) would turn black.
    /// </summary>
    private static void SaveThumb(Bitmap bitmap, string thumbBase)
    {
        var opaque = IsOpaque(bitmap);
        var thumb = thumbBase + (opaque ? ".jpg" : ".png");
        // the SafeFile suffix: leftovers of a killed process are swept from data/ at start-up
        var tmp = thumb + "." + Environment.CurrentManagedThreadId + RetroHub.Core.IO.SafeFile.TempSuffix;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(thumb)!);
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                if (opaque) bitmap.Save(fs, new JpegBitmapEncoderOptions { Quality = 90 });
                else bitmap.Save(fs, new PngBitmapEncoderOptions());
            }
            File.Move(tmp, thumb, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Debug($"Saving thumbnail {thumb} failed: {ex.Message}");
            try { File.Delete(tmp); } catch (Exception) { /* best effort */ }
        }
    }

    /// <summary>True when every pixel has alpha 255 (unknown pixel formats count as "may be transparent").</summary>
    internal static bool IsOpaque(Bitmap bitmap)
    {
        if (bitmap.AlphaFormat == Avalonia.Platform.AlphaFormat.Opaque) return true;
        var fmt = bitmap.Format;
        if (fmt == null || (fmt.Value != Avalonia.Platform.PixelFormat.Bgra8888 && fmt.Value != Avalonia.Platform.PixelFormat.Rgba8888)) return false;
        var size = bitmap.PixelSize;
        var stride = size.Width * 4;
        var buffer = new byte[stride * size.Height];
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(buffer, System.Runtime.InteropServices.GCHandleType.Pinned);
        try { bitmap.CopyPixels(new Avalonia.PixelRect(size), handle.AddrOfPinnedObject(), buffer.Length, stride); }
        catch (Exception ex)
        {
            Log.Debug($"Reading thumbnail pixels failed: {ex.Message}");
            return false;
        }
        finally { handle.Free(); }
        for (var i = 3; i < buffer.Length; i += 4)
            if (buffer[i] != 255) return false;
        return true;
    }

    /// <summary>Forget failures (e.g. after new covers were imported).</summary>
    public void ResetFailures() { lock (_gate) _failed.Clear(); }

    private static string Key(string path, int width) => $"{width}|{path}";
}
