using System.Text.Json;
using RetroHub.Core.IO;
using RetroHub.Core.Logging;

namespace RetroHub.Core.Metadata;

/// <summary>
/// Remembers online lookups that found nothing (HTTP 404 / no index match) for <see cref="Ttl"/> so repeated scans do
/// not ask the server again: data/metadata/artwork-misses.json, { "key": "2026-11-09T12:00:00Z", … }.
/// Keys are "&lt;system id&gt;|&lt;rom name&gt;" (lower-case). Thread-safe; IO problems are logged, never thrown.
/// </summary>
public sealed class ArtworkMissCache
{
    public static readonly TimeSpan Ttl = TimeSpan.FromDays(7);

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

    /// <summary>True when a miss for this key was recorded less than <see cref="Ttl"/> ago.</summary>
    public bool IsRecentMiss(string key)
    {
        lock (_gate)
        {
            var map = Load();
            return map.TryGetValue(key, out var when) && _now() - when < Ttl;
        }
    }

    public void AddMiss(string key)
    {
        lock (_gate)
        {
            Load()[key] = _now();
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
            var keep = _map.Where(kv => now - kv.Value < Ttl).OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .ToDictionary(kv => kv.Key, kv => kv.Value.ToString("yyyy-MM-ddTHH:mm:ssZ"));
            try
            {
                SafeFile.WriteAllText(_file, JsonSerializer.Serialize(keep, new JsonSerializerOptions { WriteIndented = true }));
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
            if (File.Exists(_file))
            {
                var raw = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_file)) ?? new();
                var now = _now();
                foreach (var (k, v) in raw)
                    if (DateTime.TryParse(v, System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var t)
                        && now - t < Ttl)
                        _map[k] = t;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Log.Warn($"{_file} unreadable, starting empty", ex);
        }
        return _map;
    }
}
