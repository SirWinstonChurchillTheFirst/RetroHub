using System.Collections.Concurrent;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using RetroHub.Core.Logging;

namespace RetroHub.App.Localization;

/// <summary>
/// UI text lookup. Language files are flat JSON objects ("nav.home": "Home") in app/resources/lang/&lt;lang&gt;.json;
/// the same files are embedded into the assembly as a fallback (tests, broken installs).
/// Lookup order: current language → German (reference language) → the key itself (logged once).
/// Use <see cref="T(string)"/> in code and <c>{l:T key}</c> in XAML.
/// </summary>
public static class Loc
{
    public const string ReferenceLanguage = "de";

    private static Dictionary<string, string> _strings = new(StringComparer.Ordinal);
    private static Dictionary<string, string> _fallback = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, byte> ReportedMissing = new(StringComparer.Ordinal);

    /// <summary>Current language code ("de", "en").</summary>
    public static string Language { get; private set; } = ReferenceLanguage;

    public static CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo("de-DE");

    /// <summary>Raised after <see cref="Load"/> switched the language; bound XAML texts update themselves.</summary>
    public static event Action? LanguageChanged;

    /// <summary>Observable source for XAML bindings (indexer "Item[]" changes on language switch).</summary>
    public static LocSource Source { get; } = new();

    /// <summary>Languages that ship with RetroHub (code, native name).</summary>
    public static IReadOnlyList<(string Code, string Name)> Available { get; } = [("de", "Deutsch"), ("en", "English")];

    /// <summary>Loads <paramref name="language"/> from <paramref name="resourcesDir"/>/lang (if given) or the embedded copy.</summary>
    public static void Load(string language, string? resourcesDir = null)
    {
        if (string.IsNullOrWhiteSpace(language) || language.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            language = ReferenceLanguage;
        var fallback = ReadLanguage(ReferenceLanguage, resourcesDir) ?? new Dictionary<string, string>(StringComparer.Ordinal);
        var strings = language == ReferenceLanguage ? fallback : ReadLanguage(language, resourcesDir);
        if (strings == null)
        {
            Log.Warn($"Language '{language}' not found, using '{ReferenceLanguage}'");
            language = ReferenceLanguage;
            strings = fallback;
        }
        _fallback = fallback;
        _strings = strings;
        Language = language;
        try { Culture = CultureInfo.GetCultureInfo(language == "de" ? "de-DE" : language); }
        catch (CultureNotFoundException) { Culture = CultureInfo.InvariantCulture; }
        ReportedMissing.Clear();
        Source.Raise();
        try { LanguageChanged?.Invoke(); } catch (Exception ex) { Log.Warn("LanguageChanged handler failed", ex); }
    }

    /// <summary>Text for <paramref name="key"/> in the current language.</summary>
    public static string T(string key)
    {
        if (_strings.TryGetValue(key, out var s)) return s;
        if (_fallback.TryGetValue(key, out s)) return s;
        if (ReportedMissing.TryAdd(key, 0)) Log.Warn($"Missing UI text key '{key}'");
        return key;
    }

    /// <summary>Formatted text: <c>Loc.T("library.count", 12)</c> with "{0} Spiele" in the file.</summary>
    public static string T(string key, params object?[] args)
    {
        var format = T(key);
        try { return string.Format(Culture, format, args); }
        catch (FormatException) { return format; }
    }

    /// <summary>True when the key exists in the reference language (used by tests).</summary>
    public static bool Has(string key) => _fallback.ContainsKey(key) || _strings.ContainsKey(key);

    /// <summary>All keys of a language file (embedded copy), for tests and tooling.</summary>
    public static IReadOnlyDictionary<string, string> ReadEmbedded(string language) =>
        ReadLanguage(language, null) ?? new Dictionary<string, string>();

    private static Dictionary<string, string>? ReadLanguage(string language, string? resourcesDir)
    {
        if (resourcesDir != null)
        {
            var file = Path.Combine(resourcesDir, "lang", language + ".json");
            try
            {
                if (File.Exists(file)) return Parse(File.ReadAllText(file));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                Log.Warn($"Language file {file} unreadable, using the built-in copy", ex);
            }
        }
        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"RetroHub.App.lang.{language}.json");
            if (stream == null) return null;
            using var reader = new StreamReader(stream);
            return Parse(reader.ReadToEnd());
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            Log.Error($"Built-in language '{language}' unreadable", ex);
            return null;
        }
    }

    private static Dictionary<string, string> Parse(string json)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        foreach (var p in doc.RootElement.EnumerateObject())
            if (p.Value.ValueKind == JsonValueKind.String) result[p.Name] = p.Value.GetString() ?? "";
        return result;
    }
}

/// <summary>Bindable view of <see cref="Loc"/>: <c>Source[key]</c>, refreshed on language change.</summary>
public sealed class LocSource : INotifyPropertyChanged
{
    public string this[string key] => Loc.T(key);
    public event PropertyChangedEventHandler? PropertyChanged;
    internal void Raise() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
}
