using System.Text.Json;
using System.Text.RegularExpressions;
using RetroHub.App.Localization;
using Xunit;

namespace RetroHub.App.Tests;

/// <summary>Every UI text key used in XAML or code exists in de.json (the reference language).</summary>
public partial class LocalizationTests
{
    private static IReadOnlyDictionary<string, string> German => Loc.ReadEmbedded("de");

    private static IEnumerable<string> SourceFiles(string pattern) =>
        Directory.EnumerateFiles(TestSupport.SourceDir, pattern, SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                        !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    [GeneratedRegex(@"\{l:T\s+([A-Za-z0-9_.]+)")]
    private static partial Regex XamlKey();

    [GeneratedRegex(@"Loc\.T\(\s*""([A-Za-z0-9_.]+)""")]
    private static partial Regex CodeKey();

    /// <summary>Any string literal that looks like a key ("detail.play") – catches keys passed indirectly (ternaries, NameKey, LabelKey).</summary>
    [GeneratedRegex(@"""([a-z][A-Za-z0-9]*(?:\.[A-Za-z0-9]+)+)""")]
    private static partial Regex KeyLikeLiteral();

    [Fact]
    public void Language_files_are_valid_json_objects()
    {
        foreach (var lang in new[] { "de", "en" })
        {
            var file = Path.Combine(TestSupport.SourceDir, "Resources", "lang", lang + ".json");
            using var doc = JsonDocument.Parse(File.ReadAllText(file), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
            Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
            Assert.True(Loc.ReadEmbedded(lang).Count > 0, $"{lang}.json is not embedded");
        }
        Assert.True(German.Count > 250);
    }

    [Fact]
    public void All_keys_used_in_xaml_exist()
    {
        var missing = SourceFiles("*.axaml")
            .SelectMany(f => XamlKey().Matches(File.ReadAllText(f)).Select(m => (File: Path.GetFileName(f), Key: m.Groups[1].Value)))
            .Where(x => !German.ContainsKey(x.Key))
            .Select(x => $"{x.File}: {x.Key}")
            .Distinct()
            .ToList();
        Assert.True(missing.Count == 0, "Missing in de.json:\n" + string.Join("\n", missing));
    }

    [Fact]
    public void All_keys_used_in_code_exist()
    {
        var prefixes = German.Keys.Select(k => k[..k.IndexOf('.')]).ToHashSet();
        var missing = new List<string>();
        foreach (var f in SourceFiles("*.cs"))
        {
            var text = File.ReadAllText(f);
            foreach (Match m in CodeKey().Matches(text))
                if (!German.ContainsKey(m.Groups[1].Value)) missing.Add($"{Path.GetFileName(f)}: {m.Groups[1].Value}");
            foreach (Match m in KeyLikeLiteral().Matches(text))
            {
                var key = m.Groups[1].Value;
                if (prefixes.Contains(key[..key.IndexOf('.')]) && !German.ContainsKey(key))
                    missing.Add($"{Path.GetFileName(f)}: {key} (literal)");
            }
        }
        Assert.True(missing.Count == 0, "Missing in de.json:\n" + string.Join("\n", missing.Distinct()));
    }

    [Fact]
    public void English_skeleton_only_uses_known_keys()
    {
        var unknown = Loc.ReadEmbedded("en").Keys.Where(k => !k.StartsWith('_') && !German.ContainsKey(k)).ToList();
        Assert.Empty(unknown);
    }

    [Fact]
    public void Placeholders_match_between_languages()
    {
        var en = Loc.ReadEmbedded("en");
        var placeholder = new Regex(@"\{\d+\}");
        foreach (var (key, value) in en)
        {
            if (key.StartsWith('_') || !German.TryGetValue(key, out var de)) continue;
            var a = placeholder.Matches(de).Select(m => m.Value).Order().ToList();
            var b = placeholder.Matches(value).Select(m => m.Value).Order().ToList();
            Assert.True(a.SequenceEqual(b), $"{key}: de {string.Join(",", a)} vs en {string.Join(",", b)}");
        }
    }

    [Fact]
    public void Lookup_falls_back_to_german_then_key()
    {
        Loc.Load("en");
        try
        {
            Assert.Equal("Library", Loc.T("nav.library"));
            Assert.Equal(German["imports.scanNow"], Loc.T("imports.scanNow")); // not in the skeleton → German
            Assert.Equal("does.not.exist", Loc.T("does.not.exist"));
            Assert.Equal("12 games", Loc.T("library.count", 12));
        }
        finally
        {
            Loc.Load("de");
        }
        Assert.Equal("Bibliothek", Loc.T("nav.library"));
    }
}
