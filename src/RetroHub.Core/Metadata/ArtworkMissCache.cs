using System.Globalization;
using System.Text.Json;
using RetroHub.Core.IO;
using RetroHub.Core.Logging;

namespace RetroHub.Core.Metadata;

/// <summary>
/// Remembers online lookups that found nothing (HTTP 404 / no index match) so repeated scans do not ask the server
/// again: data/metadata/artwork-misses.json, <c>{ "version": 2, "misses": { "key": "2026-11-16T12:00:00Z", … } }</c>
/// with the time each miss EXPIRES (<see cref="Ttl"/> by default; a shorter time for a guess made while the index
/// was unavailable). Keys are "&lt;system id&gt;|&lt;rom name&gt;" (lower-case). Thread-safe; IO problems are logged, never thrown.
/// <para>
/// <see cref="FormatVersion"/> is raised whenever the matching changes so much that old misses may be wrong (new
/// sources, new name rules): a file of another version — or the flat 0.1.x format — is discarded once, so an update
/// looks everything up again instead of waiting out a week of stale misses.
/// </para>
/// </summary>
public sealed class ArtworkMissCache
{
    public static readonly TimeSpan Ttl = TimeSpan.FromDays(7);

    /// <summary>2: expiry times; Satellaview set, language-tag regions, GameTDB timeouts (the release after 0.1.1).</summary>
    public const int FormatVersion = 2;

    private readonly string _file;
    private readonly Func<DateTime> _now;
    private readonly object _gate = new();
    private Dictionary<string, DateTime>? _map;
    private bool _dirty;

    public ArtworkMissCache(string file, Func<DateTime>? utcNow = null)
    {
        _file = file;
        _now = utcNow ?? (() => DateTime.UtcNow);
    }

    public static string KeyFor(string systemId, string romName) => $"{systemId}|{romName}".ToLowerInvariant();

    /// <summary>True while a miss recorded for this key has not expired.</summary>
    public bool IsRecentMiss(string key)
    {
        lock (_gate)
        {
            var map = Load();
            return map.TryGetValue(key, out var until) && _now() < until;
        }
    }

    /// <summary>Records a miss that expires after <paramref name="ttl"/> (default <see cref="Ttl"/>, at most <see cref="Ttl"/>).</summary>
    public void AddMiss(string key, TimeSpan? ttl = null)
    {
        var life = ttl is { } t && t < Ttl ? t : Ttl;
        lock (_gate)
        {
            Load()[key] = _now() + life;
            _dirty = true;
        }
    }

    public void Remove(string key)
    {
        lock (_gate)
        {
            if (Load().Remove(key)) _dirty = true;
        }
    }

    public int Count { get { lock (_gate) return Load().Count; } }

    /// <summary>Writes the file atomically when something changed; expired entries are dropped.</summary>
    public void Save()
    {
        lock (_gate)
        {
            if (!_dirty || _map == null) return;
            var now = _now();
            var keep = _map.Where(kv => now < kv.Value).OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .ToDictionary(kv => kv.Key, kv => kv.Value.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
            try
            {
                var doc = new Dictionary<string, object> { ["version"] = FormatVersion, ["misses"] = keep };
                SafeFile.WriteAllText(_file, JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true }));
                _dirty = false;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Warn($"Could not save {_file}", ex);
            }
        }
    }

    private Dictionary<string, DateTime> Load()
    {
        if (_map != null) return _map;
        _map = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        try
        {
            if (!File.Exists(_file)) return _map;
            using var doc = JsonDocument.Parse(File.ReadAllText(_file));
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("version", out var v) || v.ValueKind != JsonValueKind.Number || !v.TryGetInt32(out var version) ||
                version != FormatVersion || !root.TryGetProperty("misses", out var misses) || misses.ValueKind != JsonValueKind.Object)
            {
                // the 0.1.x file (flat, times of recording) or another version: start over once
                Log.Info($"{_file}: misses of an older RetroHub version discarded – every cover is looked up again");
                _dirty = true;
                return _map;
            }
            var now = _now();
            foreach (var p in misses.EnumerateObject())
            {
                if (p.Value.ValueKind == JsonValueKind.String &&
                    DateTime.TryParse(p.Value.GetString(), CultureInfo.InvariantCulture,
                        DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var until) &&
                    now < until && until - now <= Ttl + TimeSpan.FromDays(1)) // a far-future time (clock was wrong) is not trusted
                    _map[p.Name] = until;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Log.Warn($"{_file} unreadable, starting empty", ex);
        }
        return _map;
    }
}
