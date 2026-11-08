using System.Text.Json;
using System.Text.Json.Serialization;
using RetroHub.Core.Catalog;
using RetroHub.Core.IO;
using RetroHub.Core.Logging;

namespace RetroHub.Core.Config;

[JsonConverter(typeof(JsonStringEnumConverter<MotionLevel>))]
public enum MotionLevel { System, Full, Reduced, Off }

/// <summary>data/config/settings.json. Unknown fields are kept out; missing fields get defaults.</summary>
public sealed class AppSettings
{
    public int Version { get; set; } = 1;
    public string Language { get; set; } = "de";
    public string Theme { get; set; } = "nova";
    /// <summary>Accent colour override (#RRGGBB); null = theme default.</summary>
    public string? Accent { get; set; }
    public MotionLevel Motion { get; set; } = MotionLevel.System;
    public bool StartFullscreen { get; set; }
    public bool ScanOnStartup { get; set; } = true;
    public bool WatchFolders { get; set; } = true;
    public bool ComputeHashes { get; set; } = true;
    /// <summary>Allow downloading cover art from thumbnails.libretro.com (off until the user enables it).</summary>
    public bool OnlineCovers { get; set; }
    public bool ControllerEnabled { get; set; } = true;
    /// <summary>Hold Select+Start (or press Guide) for this long during a game to return to RetroHub; 0 = off.</summary>
    public double ExitComboSeconds { get; set; } = 1.5;
    /// <summary>Per-system preferred emulator ("mgba" / "retroarch/mgba").</summary>
    public Dictionary<string, string> SystemEmulator { get; set; } = new();
    /// <summary>Per-emulator launch mode override.</summary>
    public Dictionary<string, LaunchMode> EmulatorMode { get; set; } = new();
    /// <summary>Per-emulator exe path (portable) set by the user, overriding auto-detection.</summary>
    public Dictionary<string, string> EmulatorPaths { get; set; } = new();
    /// <summary>Per-emulator extra arguments appended to the definition's args.</summary>
    public Dictionary<string, string[]> EmulatorExtraArgs { get; set; } = new();
    public string LibraryView { get; set; } = "grid";
    public double CoverSize { get; set; } = 1.0;
}

/// <summary>Loads/saves <see cref="AppSettings"/> atomically. A broken file is preserved, never overwritten silently.</summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly string _file;
    private readonly object _gate = new();
    public AppSettings Current { get; private set; } = new();
    public event Action<AppSettings>? Saved;

    public SettingsStore(string file) { _file = file; }

    public AppSettings Load()
    {
        lock (_gate)
        {
            try
            {
                if (File.Exists(_file))
                    Current = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_file), Json) ?? new AppSettings();
            }
            catch (Exception ex) when (ex is JsonException or IOException or NotSupportedException)
            {
                var kept = SafeFile.PreserveCorrupt(_file);
                Log.Warn($"settings.json unreadable, starting with defaults (copy kept at {kept})", ex);
                Current = new AppSettings();
            }
            return Current;
        }
    }

    public void Save()
    {
        lock (_gate)
        {
            SafeFile.WriteAllText(_file, JsonSerializer.Serialize(Current, Json), keepBackup: true);
        }
        Saved?.Invoke(Current);
    }

    public void Update(Action<AppSettings> change)
    {
        lock (_gate) change(Current);
        Save();
    }
}
