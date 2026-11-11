using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Xml;
using RetroHub.Core.IO;
using RetroHub.Core.Logging;

namespace RetroHub.Core.Metadata;

/// <summary>
/// The GameTDB database of a platform (<see cref="GameTdb.DatabaseUrl"/>), fetched at most every <see cref="MaxAge"/>
/// and cached as data/metadata/gametdb/&lt;platform&gt;.json with only what matching needs (id, type, regions, titles;
/// ~2–3 MB for Switch instead of the 20 MB XML). A refresh sends the stored ETag (If-None-Match); 304 only renews the
/// date. The server builds the ZIP on demand when its copy is stale: the first byte can take 40+ s (measured
/// 2026-11-11: 43 s for an uncached language), more on a busy line, so one attempt may take <see cref="DefaultTimeout"/>
/// and a transient failure (timeout, network error, HTTP 5xx/408/429) is retried once after <see cref="DefaultRetryDelay"/>
/// (GameTDB asks crawlers for 30 s between requests; by then the ZIP is usually built). A failed fetch falls back to a
/// stale cache, else returns null; a failure is shared for <see cref="RetryAfter"/> only (a short cool-down, so the next
/// run asks again instead of failing instantly for the rest of the session). Never throws (except cancellation).
/// </summary>
public sealed class GameTdbIndexStore
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(30);
    public static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(3);
    public static readonly TimeSpan DefaultRetryDelay = TimeSpan.FromSeconds(30);
    private const long MaxZipBytes = 32L * 1024 * 1024;
    private const long MaxXmlBytes = 256L * 1024 * 1024;
    private const int FormatVersion = 1;

    internal sealed record CacheEntry(string Id, string? Type, string? Regions, List<string> Titles);
    private sealed record CacheFile(int Version, string Platform, DateTime FetchedUtc, string? ETag, List<CacheEntry> Entries);

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly string _dir;
    private readonly HttpClient _http;
    private readonly Func<DateTime> _now;
    private readonly TimeSpan _timeout;
    private readonly TimeSpan _retryDelay;
    private readonly object _gate = new();
    private readonly Dictionary<string, (Task<GameTdbIndex?> Task, DateTime Started)> _loads = new(StringComparer.Ordinal);
    private int _requests;

    /// <param name="timeout">One attempt (headers and body); default <see cref="DefaultTimeout"/>.</param>
    /// <param name="retryDelay">Pause before the one retry after a transient failure; default <see cref="DefaultRetryDelay"/>.</param>
    public GameTdbIndexStore(string cacheDir, HttpClient http, Func<DateTime>? utcNow = null, TimeSpan? timeout = null, TimeSpan? retryDelay = null)
    {
        _dir = cacheDir;
        _http = http;
        _now = utcNow ?? (() => DateTime.UtcNow);
        _timeout = timeout ?? DefaultTimeout;
        _retryDelay = retryDelay ?? DefaultRetryDelay;
    }

    /// <summary>Number of HTTP requests sent to www.gametdb.com by this store (diagnostics/tests).</summary>
    public int Requests => Volatile.Read(ref _requests);

    public string CacheFileFor(string platform) => Path.Combine(_dir, platform + ".json");

    public Task<GameTdbIndex?> GetAsync(string platform, CancellationToken ct)
    {
        if (!GameTdb.IsValidPlatform(platform)) return Task.FromResult<GameTdbIndex?>(null);
        lock (_gate)
        {
            if (_loads.TryGetValue(platform, out var l))
            {
                if (!l.Task.IsCompleted) return l.Task;
                if (l.Task.IsCompletedSuccessfully)
                {
                    var ok = l.Task.Result != null;
                    if ((ok && _now() - l.Started < MaxAge) || (!ok && _now() - l.Started < RetryAfter)) return l.Task;
                }
            }
            var task = LoadAsync(platform, ct);
            _loads[platform] = (task, _now());
            return task;
        }
    }

    private async Task<GameTdbIndex?> LoadAsync(string platform, CancellationToken ct)
    {
        await Task.Yield();
        var cached = ReadCache(platform);
        if (cached != null && _now() - cached.FetchedUtc < MaxAge) return ToIndex(cached);

        var fetched = await FetchAsync(platform, cached?.ETag, ct).ConfigureAwait(false);
        if (fetched.Transient)
        {
            Log.Info($"GameTDB database of {platform}: trying again in {_retryDelay.TotalSeconds:0} s");
            await Task.Delay(_retryDelay, ct).ConfigureAwait(false);
            fetched = await FetchAsync(platform, cached?.ETag, ct).ConfigureAwait(false);
        }
        if (fetched is { NotModified: true } && cached != null)
        {
            Write(platform, cached with { FetchedUtc = _now() });
            Log.Info($"GameTDB database of {platform} unchanged (304)");
            return ToIndex(cached);
        }
        if (fetched is { Entries: { } entries })
        {
            var file = new CacheFile(FormatVersion, platform, _now(), fetched.ETag, entries);
            Write(platform, file);
            return ToIndex(file);
        }
        if (cached != null)
        {
            Log.Info($"Using the stale GameTDB database of {platform} from {cached.FetchedUtc:yyyy-MM-dd}");
            return ToIndex(cached);
        }
        return null;
    }

    private static GameTdbIndex ToIndex(CacheFile f) =>
        new(f.Entries.Select(e => new GameTdbEntry(e.Id, e.Type, e.Regions, e.Titles ?? [])));

    private void Write(string platform, CacheFile file)
    {
        try { SafeFile.WriteAllText(CacheFileFor(platform), JsonSerializer.Serialize(file, Json)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log.Warn($"Could not cache the GameTDB database of {platform}", ex); }
    }

    private CacheFile? ReadCache(string platform)
    {
        var path = CacheFileFor(platform);
        try
        {
            if (!File.Exists(path)) return null;
            var c = JsonSerializer.Deserialize<CacheFile>(File.ReadAllText(path), Json);
            if (c == null || c.Entries == null || c.Version != FormatVersion || !string.Equals(c.Platform, platform, StringComparison.Ordinal)) return null;
            return c;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            Log.Warn($"GameTDB cache {path} unreadable", ex);
            return null;
        }
    }

    /// <summary>One attempt: 304, entries, or a failure (<see cref="Transient"/>: worth one retry).</summary>
    private readonly record struct FetchResult(bool NotModified, string? ETag, List<CacheEntry>? Entries, bool Transient)
    {
        public static FetchResult Failed(bool transient) => new(false, null, null, transient);
    }

    private async Task<FetchResult> FetchAsync(string platform, string? etag, CancellationToken ct)
    {
        var url = GameTdb.DatabaseUrl(platform);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(_timeout);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            ArtworkService.AddPoliteHeaders(req);
            if (!string.IsNullOrEmpty(etag) && EntityTagHeaderValue.TryParse(etag, out var tag)) req.Headers.IfNoneMatch.Add(tag);
            Interlocked.Increment(ref _requests);
            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (resp.StatusCode == HttpStatusCode.NotModified) return new FetchResult(true, etag, null, false);
            if (!resp.IsSuccessStatusCode)
            {
                var code = (int)resp.StatusCode;
                Log.Warn($"GameTDB database {url} failed: HTTP {code} after {sw.ElapsedMilliseconds} ms");
                return FetchResult.Failed(transient: code >= 500 || resp.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests);
            }
            if (resp.Content.Headers.ContentLength is > MaxZipBytes)
            {
                Log.Warn($"GameTDB database {url} skipped: {resp.Content.Headers.ContentLength} bytes");
                return FetchResult.Failed(transient: false);
            }
            var bytes = await ReadCappedAsync(resp.Content, timeout.Token).ConfigureAwait(false);
            if (bytes == null)
            {
                Log.Warn($"GameTDB database {url} skipped: larger than {MaxZipBytes} bytes");
                return FetchResult.Failed(transient: false);
            }
            var entries = Parse(bytes, platform);
            if (entries == null) return FetchResult.Failed(transient: false);
            Log.Info($"Fetched the GameTDB database of {platform}: {entries.Count} games ({bytes.Length / 1024} KB, {sw.ElapsedMilliseconds} ms)");
            return new FetchResult(false, resp.Headers.ETag?.ToString(), entries, false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (OperationCanceledException)
        {
            Log.Warn($"GameTDB database {url} timed out after {sw.ElapsedMilliseconds} ms (limit {_timeout.TotalSeconds:0} s)");
            return FetchResult.Failed(transient: true);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            Log.Warn($"GameTDB database {url} could not be fetched after {sw.ElapsedMilliseconds} ms: {ex.Message}");
            return FetchResult.Failed(transient: true);
        }
        catch (Exception ex) when (ex is InvalidDataException or XmlException)
        {
            Log.Warn($"GameTDB database {url} is unreadable: {ex.Message}");
            return FetchResult.Failed(transient: false);
        }
    }

    /// <summary>The body, or null when it exceeds <see cref="MaxZipBytes"/> (also without a Content-Length).</summary>
    private static async Task<byte[]?> ReadCappedAsync(HttpContent content, CancellationToken ct)
    {
        await using var src = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var ms = new MemoryStream();
        var buf = new byte[64 * 1024];
        int n;
        while ((n = await src.ReadAsync(buf, ct).ConfigureAwait(false)) > 0)
        {
            if (ms.Length + n > MaxZipBytes) return null;
            ms.Write(buf, 0, n);
        }
        return ms.ToArray();
    }

    /// <summary>Reads &lt;platform&gt;tdb.xml from the ZIP (or a bare XML) in one streaming pass. Null when it is neither.</summary>
    internal static List<CacheEntry>? Parse(byte[] data, string platform)
    {
        using var ms = new MemoryStream(data, writable: false);
        if (data.Length >= 4 && data[0] == 'P' && data[1] == 'K')
        {
            using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
            var entry = zip.Entries.FirstOrDefault(e => e.Name.Equals(platform + "tdb.xml", StringComparison.OrdinalIgnoreCase))
                        ?? zip.Entries.FirstOrDefault(e => e.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));
            if (entry == null || entry.Length > MaxXmlBytes)
            {
                Log.Warn($"GameTDB archive of {platform} holds no usable XML");
                return null;
            }
            using var xml = entry.Open();
            return ParseXml(xml);
        }
        if (data.Length > 0 && (data[0] == '<' || data[0] == 0xEF)) return ParseXml(ms);
        Log.Warn($"GameTDB database of {platform} is neither a ZIP nor XML");
        return null;
    }

    /// <summary>
    /// Every &lt;game&gt;: &lt;id&gt;, &lt;type&gt;, &lt;region&gt;, each &lt;locale&gt;&lt;title&gt; and the name attribute without its
    /// trailing language list ("… (EN,FR,DE)"). No DTD, no external entities.
    /// </summary>
    internal static List<CacheEntry> ParseXml(Stream xml)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreWhitespace = true,
            IgnoreProcessingInstructions = true,
        };
        var list = new List<CacheEntry>();
        using var r = XmlReader.Create(xml, settings);
        while (r.Read())
        {
            if (r.NodeType != XmlNodeType.Element || r.Name != "game") continue;
            var name = r.GetAttribute("name");
            string? id = null, type = null, region = null;
            var titles = new List<string>();
            void AddTitle(string? t)
            {
                t = t?.Trim();
                if (!string.IsNullOrEmpty(t) && !titles.Contains(t, StringComparer.Ordinal)) titles.Add(t);
            }
            if (name != null) AddTitle(StripLanguageList(name));
            if (!r.IsEmptyElement)
            {
                using var sub = r.ReadSubtree();
                sub.Read(); // <game>
                var inLocale = false;
                var more = sub.Read();
                while (more && !sub.EOF)
                {
                    if (sub.NodeType == XmlNodeType.EndElement && sub.Name == "locale") inLocale = false;
                    else if (sub.NodeType == XmlNodeType.Element)
                    {
                        // ReadElementContentAsString already moves to the next node: no Read after it
                        if (sub.Depth == 1 && sub.Name is "id" or "type" or "region")
                        {
                            var field = sub.Name;
                            var v = sub.ReadElementContentAsString().Trim();
                            if (field == "id") id = v;
                            else if (field == "type") type = v;
                            else region = v;
                            continue;
                        }
                        if (sub.Name == "title" && inLocale) { AddTitle(sub.ReadElementContentAsString()); continue; }
                        if (sub.Name == "locale") inLocale = !sub.IsEmptyElement;
                    }
                    more = sub.Read();
                }
            }
            if (GameTdb.IsValidId(id) && titles.Count > 0) list.Add(new CacheEntry(id!, NullIfEmpty(type), NullIfEmpty(region), titles));
        }
        return list;
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    /// <summary>"Hyrule Warriors: Age of Calamity (EN,FR,DE)" → "Hyrule Warriors: Age of Calamity".</summary>
    internal static string StripLanguageList(string name)
    {
        var s = name.TrimEnd();
        if (s.EndsWith(')'))
        {
            var open = s.LastIndexOf('(');
            if (open > 0)
            {
                var inner = s[(open + 1)..^1];
                if (inner.Length > 0 && inner.All(c => char.IsAsciiLetterUpper(c) || c == ',')) s = s[..open].TrimEnd();
            }
        }
        return s;
    }
}
