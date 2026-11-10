using System.Text.Json;
using System.Text.Json.Serialization;
using RetroHub.App.Input;
using RetroHub.Core.IO;
using RetroHub.Core.Logging;

namespace RetroHub.App.Services;

/// <summary>
/// Purely visual preferences owned by the UI (data/config/ui.json). Kept out of <c>AppSettings</c> so the core
/// contract does not need UI-only fields.
/// </summary>
public sealed class UiPreferences
{
    /// <summary>Show the controller hint bar at the bottom.</summary>
    public bool ShowHints { get; set; } = true;
    /// <summary>The channel display (OSD) on a channel switch and at a game start (DESIGN 7.0, "Kanalanzeige").</summary>
    public bool ShowOsd { get; set; } = true;
    /// <summary>Last library sort (GameSort name) and system filter.</summary>
    public string LibrarySort { get; set; } = "Title";
}

/// <summary>Loads/saves <see cref="UiPreferences"/> atomically; a broken file is preserved and replaced by defaults.</summary>
public sealed class UiPreferencesStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly string? _file;
    public UiPreferences Current { get; private set; } = new();
    public event Action<UiPreferences>? Changed;

    /// <param name="file">Null = in memory only (tests).</param>
    public UiPreferencesStore(string? file) { _file = file; }

    public UiPreferences Load()
    {
        if (_file == null) return Current;
        try
        {
            if (File.Exists(_file))
                Current = JsonSerializer.Deserialize<UiPreferences>(File.ReadAllText(_file), Json) ?? new UiPreferences();
        }
        catch (Exception ex) when (ex is JsonException or IOException or NotSupportedException or UnauthorizedAccessException)
        {
            var kept = SafeFile.PreserveCorrupt(_file);
            Log.Warn($"ui.json unreadable, using defaults (copy kept at {kept})", ex);
            Current = new UiPreferences();
        }
        return Current;
    }

    public void Update(Action<UiPreferences> change)
    {
        change(Current);
        if (_file != null)
        {
            try { SafeFile.WriteAllText(_file, JsonSerializer.Serialize(Current, Json)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log.Warn("ui.json could not be saved", ex); }
        }
        try { Changed?.Invoke(Current); } catch (Exception ex) { Log.Warn("UiPreferences listener failed", ex); }
    }
}
