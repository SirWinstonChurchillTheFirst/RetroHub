using System.Net;
using System.Text.Json;
using RetroHub.Core.IO;
using RetroHub.Core.Logging;

namespace RetroHub.Core.Metadata;

/// <summary>
/// The list of boxart names per system, fetched once from the GitHub git-trees API of the libretro-thumbnails
/// repository (unauthenticated, ~6 MB JSON for GBA) and cached as data/metadata/libretro-thumbnails/&lt;system&gt;.json
/// for <see cref="MaxAge"/>. A failed fetch falls back to a stale cache file, else returns null (the caller then tries
/// direct URLs); failures are remembered for <see cref="RetryAfter"/> so a rate limit is not hammered. Never throws
/// (except for cancellation).
/// </summary>
public sealed class ThumbnailIndexStore
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(30);
    public static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(15);
    private const string Prefix = LibretroThumbnails.BoxartFolder + "/";

    private sealed record CacheFile(string LibretroName, DateTime FetchedUtc, bool Truncated, List<string> Boxarts);

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly string _dir;
    private readonly HttpClient _http;
    private readonly Func<DateTime> _now;
    private readonly TimeSpan _timeout;
    private readonly object _gate = new();
    private readonly Dictionary<string, (Task<ThumbnailIndex?> Task, DateTime Started)> _loads = new(StringComparer.OrdinalIgnoreCase);

    public ThumbnailIndexStore(string cacheDir, HttpClient http, Func<DateTime>? utcNow = null, TimeSpan? timeout = null)
    {
        _dir = cacheDir;
        _http = http;
        _now = utcNow ?? (() => DateTime.UtcNow);
        _timeout = timeout ?? TimeSpan.FromMinutes(2); // the SNES listing is ~4.5 MB: room for a slow or busy line
    }

    /// <summary>Number of HTTP requests sent to GitHub by this store (diagnostics/tests).</summary>
    public int Requests => Volatile.Read(ref _requests);
    private int _requests;

    public string CacheFileFor(string systemId) => Path.Combine(_dir, systemId + ".json");

    public Task<ThumbnailIndex?> GetAsync(string systemId, string libretroName, CancellationToken ct)
    {
        lock (_gate)
        {
            if (_loads.TryGetValue(systemId, out var l))
            {
                // a running load or a good index is shared; a failed one is retried after RetryAfter; a cancelled one at once
                if (!l.Task.IsCompleted) return l.Task;
                if (l.Task.IsCompletedSuccessfully)
                {
                    var ok = l.Task.Result != null;
                    if ((ok && _now() - l.Started < MaxAge) || (!ok && _now() - l.Started < RetryAfter)) return l.Task;
                }
            }
            var task = LoadAsync(systemId, libretroName, ct);
            _loads[systemId] = (task, _now());
            return task;
        }
    }

    private async Task<ThumbnailIndex?> LoadAsync(string systemId, string libretroName, CancellationToken ct)
    {
        await Task.Yield();
        var cached = ReadCache(systemId, libretroName);
        if (cached != null && _now() - cached.FetchedUtc < MaxAge) return new ThumbnailIndex(cached.Boxarts, cached.Truncated);

        var fetched = await FetchAsync(libretroName, ct).ConfigureAwait(false);
        if (fetched != null)
        {
            var file = new CacheFile(libretroName, _now(), fetched.Value.Truncated, fetched.Value.Names);
            try { SafeFile.WriteAllText(CacheFileFor(systemId), JsonSerializer.Serialize(file, Json)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log.Warn($"Could not cache the thumbnail index of {systemId}", ex); }
            return new ThumbnailIndex(fetched.Value.Names, fetched.Value.Truncated);
        }
        if (cached != null)
        {
            Log.Info($"Using the stale thumbnail index of {systemId} from {cached.FetchedUtc:yyyy-MM-dd}");
            return new ThumbnailIndex(cached.Boxarts, cached.Truncated);
        }
        return null;
    }

    private CacheFile? ReadCache(string systemId, string libretroName)
    {
        var path = CacheFileFor(systemId);
        try
        {
            if (!File.Exists(path)) return null;
            var c = JsonSerializer.Deserialize<CacheFile>(File.ReadAllText(path), Json);
            if (c == null || c.Boxarts == null || !string.Equals(c.LibretroName, libretroName, StringComparison.Ordinal)) return null;
            return c;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            Log.Warn($"Thumbnail index cache {path} unreadable", ex);
            return null;
        }
    }

    private async Task<(List<string> Names, bool Truncated)?> FetchAsync(string libretroName, CancellationToken ct)
    {
        var url = LibretroThumbnails.TreeUrl(libretroName);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(_timeout);
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            ArtworkService.AddPoliteHeaders(req);
            req.Headers.Accept.ParseAdd("application/vnd.github+json");
            Interlocked.Increment(ref _requests);
            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (resp.StatusCode == HttpStatusCode.NotFound)
            {
                Log.Info($"No libretro-thumbnails repository for '{libretroName}' ({url})");
                return ([], false);
            }
            if (!resp.IsSuccessStatusCode)
            {
                var reset = resp.Headers.TryGetValues("X-RateLimit-Reset", out var v) ? v.FirstOrDefault() : null;
                Log.Warn($"GitHub index for '{libretroName}' failed: HTTP {(int)resp.StatusCode}" + (reset != null ? $" (rate limit resets at unix {reset})" : ""));
                return null;
            }
            await using var stream = await resp.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token).ConfigureAwait(false);
            var names = new List<string>();
            var truncated = doc.RootElement.TryGetProperty("truncated", out var t) && t.ValueKind == JsonValueKind.True;
            if (doc.RootElement.TryGetProperty("tree", out var tree) && tree.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in tree.EnumerateArray())
                {
                    if (!item.TryGetProperty("path", out var p) || p.GetString() is not { } path) continue;
                    if (item.TryGetProperty("type", out var ty) && ty.GetString() != "blob") continue;
                    if (!path.StartsWith(Prefix, StringComparison.Ordinal) || !path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) continue;
                    var name = path[Prefix.Length..^4];
                    if (name.Length > 0 && !name.Contains('/')) names.Add(name);
                }
            }
            if (truncated) Log.Warn($"GitHub index for '{libretroName}' is truncated ({names.Count} boxarts listed)");
            Log.Info($"Fetched the libretro-thumbnails index for '{libretroName}': {names.Count} boxarts");
            return (names, truncated);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException or IOException)
        {
            Log.Warn($"GitHub index for '{libretroName}' could not be fetched: {ex.Message}");
            return null;
        }
    }
}
