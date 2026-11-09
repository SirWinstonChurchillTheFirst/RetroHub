using System.Text.Json;
using RetroHub.Core.Logging;

namespace RetroHub.Core.Catalog;

/// <summary>
/// The loaded catalog: built-in system/emulator definitions (app/resources/systems|emulators/*.json) merged with
/// plugin definitions (plugins/systems|emulators/*.json; same id = the plugin replaces the built-in completely).
/// Immutable after loading, therefore thread-safe. All lookups are case-insensitive.
/// <para>
/// Note for code inside the <c>RetroHub.Core.*</c> namespaces: the simple name <c>Catalog</c> resolves to the
/// namespace there, so write <c>Catalog.Catalog.Load(paths)</c> or use an alias
/// (<c>using CatalogImpl = RetroHub.Core.Catalog.Catalog;</c>); program against <see cref="ICatalog"/> where possible.
/// </para>
/// </summary>
public sealed class Catalog : ICatalog
{
    private static readonly IReadOnlyList<SystemDef> NoSystems = [];

    private readonly Dictionary<string, SystemDef> _systemsById;
    private readonly Dictionary<string, EmulatorDef> _emulatorsById;
    private readonly Dictionary<string, SystemDef> _systemsByFolder;
    private readonly Dictionary<string, IReadOnlyList<SystemDef>> _systemsByExtension;
    private readonly Dictionary<string, IReadOnlyList<EmulatorDef>> _emulatorsBySystem;

    /// <summary>Systems sorted by <see cref="SystemDef.SortOrder"/>, then <see cref="SystemDef.Name"/>.</summary>
    public IReadOnlyList<SystemDef> Systems { get; }
    /// <summary>Emulators sorted by name.</summary>
    public IReadOnlyList<EmulatorDef> Emulators { get; }
    /// <summary>German, user-readable problems found while loading (plugin errors, inconsistent references, …).</summary>
    public IReadOnlyList<string> LoadWarnings { get; }

    /// <summary>The JSON options used for definition files (camelCase, comments, trailing commas), e.g. to export a definition.</summary>
    public static JsonSerializerOptions JsonOptions => CatalogJson.Options;

    private Catalog(List<SystemDef> systems, List<EmulatorDef> emulators, List<string> warnings)
    {
        Systems = systems
            .OrderBy(s => s.SortOrder)
            .ThenBy(s => s.Name, StringComparer.InvariantCultureIgnoreCase)
            .ThenBy(s => s.Id, StringComparer.Ordinal)
            .ToList()
            .AsReadOnly(); // read-only wrappers: callers cannot mutate the shared catalog by casting
        Emulators = emulators
            .OrderBy(e => e.Name, StringComparer.InvariantCultureIgnoreCase)
            .ThenBy(e => e.Id, StringComparer.Ordinal)
            .ToList()
            .AsReadOnly();
        LoadWarnings = warnings.AsReadOnly();

        _systemsById = Systems.ToDictionary(s => s.Id, StringComparer.OrdinalIgnoreCase);
        _emulatorsById = Emulators.ToDictionary(e => e.Id, StringComparer.OrdinalIgnoreCase);

        _systemsByFolder = new Dictionary<string, SystemDef>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in Systems)
            foreach (var f in s.Folders)
                _systemsByFolder.TryAdd(f, s); // uniqueness is enforced by the builder

        // Second level: real-world folder names ("Game Boy Advance", "Nintendo 64", "Sony PlayStation 2") matched
        // against normalised folder aliases, display names, short names and "manufacturer + name".
        // A key that would point to two different systems is dropped (never guess).
        var byName = new Dictionary<string, SystemDef?>(StringComparer.Ordinal);
        foreach (var s in Systems)
        {
            var keys = new List<string>(s.Folders) { s.Name, s.ShortName, $"{s.Manufacturer} {s.Name}", $"{s.Manufacturer} {s.ShortName}" };
            foreach (var k in keys.Select(NormalizeFolderKey).Where(k => k.Length >= 2).Distinct())
            {
                if (byName.TryGetValue(k, out var existing) && existing != null && existing.Id != s.Id) byName[k] = null;
                else if (!byName.ContainsKey(k)) byName[k] = s;
            }
        }
        _systemsByName = byName.Where(kv => kv.Value != null).ToDictionary(kv => kv.Key, kv => kv.Value!, StringComparer.Ordinal);

        var byExt = new Dictionary<string, List<SystemDef>>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in Systems)
            foreach (var e in s.Extensions)
            {
                if (!byExt.TryGetValue(e, out var list)) byExt[e] = list = [];
                list.Add(s);
            }
        _systemsByExtension = byExt.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<SystemDef>)kv.Value.AsReadOnly(), StringComparer.OrdinalIgnoreCase);

        _emulatorsBySystem = Systems.ToDictionary(
            s => s.Id,
            s => (IReadOnlyList<EmulatorDef>)Emulators.Where(e => e.Systems.Contains(s.Id, StringComparer.OrdinalIgnoreCase)).ToList().AsReadOnly(),
            StringComparer.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------------ loading

    /// <summary>
    /// Loads <c>paths.Resources/systems|emulators/*.json</c> (built-in) and then <c>paths.Plugins/systems|emulators/*.json</c>.
    /// Plugin problems become <see cref="LoadWarnings"/> (never an exception).
    /// </summary>
    /// <exception cref="CatalogLoadException">A built-in folder is missing or a built-in file is broken (packaging bug).</exception>
    public static Catalog Load(AppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return LoadFrom(paths.Resources, paths.Plugins);
    }

    /// <summary>
    /// Same as <see cref="Load(AppPaths)"/> with explicit folders: <paramref name="resourcesDir"/> must contain
    /// systems/ and emulators/; <paramref name="pluginsDir"/> may be null or missing.
    /// </summary>
    /// <exception cref="CatalogLoadException">A built-in folder is missing or a built-in file is broken.</exception>
    public static Catalog LoadFrom(string resourcesDir, string? pluginsDir)
    {
        var builder = new CatalogBuilder(strictBuiltIns: true);
        var started = DateTime.UtcNow;

        foreach (var kind in new[] { "systems", "emulators" })
        {
            var dir = Path.Combine(resourcesDir, kind);
            if (!Directory.Exists(dir))
                throw new CatalogLoadException(dir, $"Eingebaute Definitionen fehlen: Ordner „{dir}“ nicht gefunden. Bitte RetroHub neu entpacken.");
            IReadOnlyList<string> files;
            try { files = CatalogJson.ListFiles(dir); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new CatalogLoadException(dir, $"Eingebaute Definitionen nicht lesbar: „{dir}“ ({ex.Message}).", ex);
            }
            if (files.Count == 0)
                throw new CatalogLoadException(dir, $"Eingebaute Definitionen fehlen: keine *.json-Dateien in „{dir}“.");
            foreach (var file in files) ReadInto(builder, kind, file, plugin: false);
        }

        if (!string.IsNullOrWhiteSpace(pluginsDir))
        {
            foreach (var kind in new[] { "systems", "emulators" })
            {
                var dir = Path.Combine(pluginsDir, kind);
                try
                {
                    if (!Directory.Exists(dir)) continue;
                    foreach (var file in CatalogJson.ListFiles(dir)) ReadInto(builder, kind, file, plugin: true);
                }
                catch (Exception ex) when (ex is not CatalogLoadException)
                {
                    // last line of defence: a plugin folder must never stop RetroHub from starting
                    builder.Warn($"plugins/{kind}: Ordner kann nicht gelesen werden ({ex.Message}).");
                }
            }
        }

        var catalog = Build(builder);
        Log.Info($"Catalog loaded: {catalog.Systems.Count} systems, {catalog.Emulators.Count} emulators, " +
                 $"{catalog.LoadWarnings.Count} warnings ({(DateTime.UtcNow - started).TotalMilliseconds:0} ms)");
        foreach (var w in catalog.LoadWarnings) Log.Warn("Catalog: " + w);
        return catalog;
    }

    /// <summary>
    /// Builds a catalog from in-memory definitions (tests, tools). The same normalisation and validation as
    /// <see cref="Load(AppPaths)"/> applies; duplicate ids become warnings (the later definition wins).
    /// </summary>
    public static Catalog FromDefinitions(IEnumerable<SystemDef> systems, IEnumerable<EmulatorDef> emulators)
    {
        ArgumentNullException.ThrowIfNull(systems);
        ArgumentNullException.ThrowIfNull(emulators);
        var builder = new CatalogBuilder(strictBuiltIns: false);
        foreach (var s in systems) if (s != null) builder.AddSystem(s, "(intern)", plugin: false);
        foreach (var e in emulators) if (e != null) builder.AddEmulator(e, "(intern)", plugin: false);
        return Build(builder);
    }

    private static Catalog Build(CatalogBuilder builder)
    {
        var (systems, emulators, warnings) = builder.Build();
        return new Catalog(systems, emulators, warnings);
    }

    private static void ReadInto(CatalogBuilder builder, string kind, string file, bool plugin)
    {
        var source = CatalogJson.DisplayName(file, plugin);
        try
        {
            if (kind == "systems")
                foreach (var d in CatalogJson.ReadFile<SystemDef>(file, plugin, builder.Warn)) builder.AddSystem(d, source, plugin);
            else
                foreach (var d in CatalogJson.ReadFile<EmulatorDef>(file, plugin, builder.Warn)) builder.AddEmulator(d, source, plugin);
        }
        catch (Exception ex) when (plugin && ex is not CatalogLoadException)
        {
            builder.Warn($"{source}: konnte nicht geladen werden ({ex.Message}). Die Datei wird ignoriert.");
        }
    }

    // ------------------------------------------------------------------ lookups

    public SystemDef? GetSystem(string id) =>
        string.IsNullOrWhiteSpace(id) ? null : _systemsById.GetValueOrDefault(id.Trim());

    public EmulatorDef? GetEmulator(string id) =>
        string.IsNullOrWhiteSpace(id) ? null : _emulatorsById.GetValueOrDefault(id.Trim());

    public SystemDef? SystemForFolder(string folderName)
    {
        if (string.IsNullOrWhiteSpace(folderName)) return null;
        return _systemsByFolder.GetValueOrDefault(folderName.Trim())
               ?? _systemsByName.GetValueOrDefault(NormalizeFolderKey(folderName));
    }

    private readonly Dictionary<string, SystemDef> _systemsByName;

    /// <summary>Lower-case letters and digits only: "Game Boy Advance" → "gameboyadvance", "Wii U" → "wiiu".</summary>
    internal static string NormalizeFolderKey(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (var c in s.Normalize(System.Text.NormalizationForm.FormD))
            if (char.IsLetterOrDigit(c) && c < 128) sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }

    /// <summary>All systems listing this extension, in catalog order. Accepts ".ISO", "iso" or "*.iso".</summary>
    public IReadOnlyList<SystemDef> SystemsForExtension(string extension)
    {
        var e = CatalogBuilder.NormalizeExtension(extension);
        return e != null && _systemsByExtension.TryGetValue(e, out var list) ? list : NoSystems;
    }

    /// <summary>Emulators whose <see cref="EmulatorDef.Systems"/> contains the system, sorted by name.</summary>
    public IReadOnlyList<EmulatorDef> EmulatorsForSystem(string systemId) =>
        !string.IsNullOrWhiteSpace(systemId) && _emulatorsBySystem.TryGetValue(systemId.Trim(), out var list) ? list : [];

    /// <summary>The system's preferred emulators (<see cref="SystemDef.Emulators"/>) parsed; every one resolves to an emulator.</summary>
    public IReadOnlyList<EmulatorRef> EmulatorRefsFor(string systemId) =>
        GetSystem(systemId) is { } s ? s.Emulators.Select(EmulatorRef.Parse).ToList() : [];

    /// <summary>True when <paramref name="extension"/> is one of the system's playlist formats (.m3u/.cue/.gdi).</summary>
    public bool IsPlaylistExtension(string systemId, string extension)
    {
        var e = CatalogBuilder.NormalizeExtension(extension);
        return e != null && GetSystem(systemId) is { } s && s.PlaylistExtensions.Contains(e, StringComparer.OrdinalIgnoreCase);
    }
}
