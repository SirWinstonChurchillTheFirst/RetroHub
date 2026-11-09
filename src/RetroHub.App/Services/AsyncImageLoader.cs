using Avalonia.Media.Imaging;
using RetroHub.Core.Logging;

namespace RetroHub.App.Services;

/// <summary>
/// Loads cover images off the UI thread: <see cref="Bitmap.DecodeToWidth"/> on the thread pool (only the pixels
/// needed for the tile width), an in-memory LRU cache by (path, width), de-duplication of concurrent requests,
/// and cancellation when a tile is recycled before its image arrived. Missing/broken files yield null (placeholder).
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
            var bitmap = await Task.Run(() =>
            {
                if (!File.Exists(path)) return null;
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024);
                return Bitmap.DecodeToWidth(stream, width, BitmapInterpolationMode.HighQuality);
            }).ConfigureAwait(false);
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

    /// <summary>Forget failures (e.g. after new covers were imported).</summary>
    public void ResetFailures() { lock (_gate) _failed.Clear(); }

    private static string Key(string path, int width) => $"{width}|{path}";
}
