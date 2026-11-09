using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace RetroHub.Core.Catalog;

/// <summary>
/// JSON handling for definition files: camelCase, comments and trailing commas allowed, each file is an array of
/// definitions (a single object is accepted as a one-element array). Elements are deserialized one by one, so a single
/// bad entry in a plugin file only drops that entry.
/// </summary>
internal static class CatalogJson
{
    /// <summary>Plugin files larger than this are ignored (a definition file is a few KB; protects against junk files).</summary>
    public const long MaxPluginFileBytes = 4 * 1024 * 1024;

    public static readonly JsonSerializerOptions Options = CreateOptions();

    private static readonly JsonDocumentOptions DocOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        MaxDepth = 64,
    };

    private static JsonSerializerOptions CreateOptions()
    {
        var o = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            WriteIndented = true,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        };
        o.MakeReadOnly();
        return o;
    }

    /// <summary>
    /// Reads all definitions of type <typeparamref name="T"/> from <paramref name="file"/>.
    /// Built-in (<paramref name="plugin"/> = false): any problem throws <see cref="CatalogLoadException"/>.
    /// Plugin: problems are reported through <paramref name="warn"/> and the affected file/entry is skipped.
    /// </summary>
    public static List<T> ReadFile<T>(string file, bool plugin, Action<string> warn) where T : class
    {
        var result = new List<T>();
        var name = DisplayName(file, plugin);
        JsonDocument doc;
        try
        {
            if (plugin && new FileInfo(file).Length > MaxPluginFileBytes)
            {
                warn($"{name}: Datei ist größer als {MaxPluginFileBytes / (1024 * 1024)} MB und wird ignoriert.");
                return result;
            }
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            doc = JsonDocument.Parse(stream, DocOptions);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            var msg = ex is JsonException je
                ? $"{name}: ungültiges JSON ({FirstLine(je.Message)})."
                : $"{name}: Datei kann nicht gelesen werden ({ex.Message}).";
            if (!plugin) throw new CatalogLoadException(file, "Eingebaute Definitionsdatei defekt – " + msg, ex);
            warn(msg + " Die Datei wird ignoriert.");
            return result;
        }

        using (doc)
        {
            var root = doc.RootElement;
            IEnumerable<JsonElement> items;
            if (root.ValueKind == JsonValueKind.Array) items = root.EnumerateArray();
            else if (root.ValueKind == JsonValueKind.Object) items = [root];
            else
            {
                var msg = $"{name}: erwartet ein JSON-Array von Definitionen.";
                if (!plugin) throw new CatalogLoadException(file, "Eingebaute Definitionsdatei defekt – " + msg);
                warn(msg + " Die Datei wird ignoriert.");
                return result;
            }

            var typeInfo = Options.GetTypeInfo(typeof(T));
            var index = 0;
            foreach (var item in items)
            {
                var where = $"{name} (Eintrag {index + 1})";
                index++;
                var unknown = new List<string>();
                CollectUnknownProperties(item, typeInfo, "", unknown);
                if (unknown.Count > 0)
                {
                    var msg = $"{where}: unbekannte Eigenschaft(en) {string.Join(", ", unknown)}.";
                    if (!plugin) throw new CatalogLoadException(file, "Eingebaute Definitionsdatei defekt – " + msg);
                    warn(msg + " Sie werden ignoriert.");
                }
                try
                {
                    var def = item.Deserialize<T>(Options);
                    if (def == null)
                    {
                        if (!plugin) throw new CatalogLoadException(file, $"Eingebaute Definitionsdatei defekt – {where}: leerer Eintrag.");
                        warn($"{where}: leerer Eintrag wird übersprungen.");
                        continue;
                    }
                    result.Add(def);
                }
                catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException or ArgumentException or FormatException)
                {
                    var msg = $"{where}: {FirstLine(ex.Message)}";
                    if (!plugin) throw new CatalogLoadException(file, "Eingebaute Definitionsdatei defekt – " + msg, ex);
                    warn(msg + " Der Eintrag wird übersprungen.");
                }
            }
        }
        return result;
    }

    /// <summary>Lists *.json files of a folder in a stable order (ordinal, case-insensitive).</summary>
    public static IReadOnlyList<string> ListFiles(string folder)
    {
        return Directory.EnumerateFiles(folder, "*.json", SearchOption.TopDirectoryOnly)
            .Where(f => string.Equals(Path.GetExtension(f), ".json", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Short file label for messages: "plugins/systems/foo.json" or "resources/systems/foo.json".</summary>
    public static string DisplayName(string file, bool plugin)
    {
        var dir = Path.GetFileName(Path.GetDirectoryName(file) ?? "");
        return $"{(plugin ? "plugins" : "resources")}/{dir}/{Path.GetFileName(file)}";
    }

    /// <summary>Property names in <paramref name="el"/> that the target type does not know (recursing into objects/arrays).</summary>
    private static void CollectUnknownProperties(JsonElement el, JsonTypeInfo ti, string path, List<string> unknown)
    {
        if (el.ValueKind != JsonValueKind.Object || ti.Kind != JsonTypeInfoKind.Object) return;
        foreach (var p in el.EnumerateObject())
        {
            // "$schema" / "$comment" are conventional annotations, not typos
            if (p.Name.StartsWith('$')) continue;
            var jp = ti.Properties.FirstOrDefault(x => string.Equals(x.Name, p.Name, StringComparison.OrdinalIgnoreCase));
            if (jp == null)
            {
                unknown.Add($"„{path}{p.Name}“");
                continue;
            }
            var pti = ti.Options.GetTypeInfo(jp.PropertyType);
            if (pti.Kind == JsonTypeInfoKind.Object)
                CollectUnknownProperties(p.Value, pti, $"{path}{p.Name}.", unknown);
            else if (pti.Kind == JsonTypeInfoKind.Enumerable && pti.ElementType != null && p.Value.ValueKind == JsonValueKind.Array)
            {
                var eti = ti.Options.GetTypeInfo(pti.ElementType);
                if (eti.Kind != JsonTypeInfoKind.Object) continue;
                var i = 0;
                foreach (var item in p.Value.EnumerateArray())
                    CollectUnknownProperties(item, eti, $"{path}{p.Name}[{i++}].", unknown);
            }
        }
    }

    private static string FirstLine(string s)
    {
        var i = s.IndexOfAny(['\r', '\n']);
        return i < 0 ? s : s[..i];
    }
}
