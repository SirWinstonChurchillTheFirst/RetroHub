using System.Text;
using System.Text.RegularExpressions;

namespace RetroHub.Core.Library;

/// <summary>Display title, sort title and region derived from a file or folder name.</summary>
public sealed record ParsedTitle(string Title, string SortTitle, string? Region);

/// <summary>
/// Turns No-Intro / GoodTools / Redump style file names into display titles:
/// "Legend of Zelda, The - A Link to the Past (USA) (Rev 1) [!].sfc" → "The Legend of Zelda - A Link to the Past", region "USA".
/// </summary>
public static partial class TitleParser
{
    private static readonly string[] SortArticles = ["The ", "Der ", "Die ", "Das ", "A ", "An "];

    /// <summary>No-Intro region words (a tag counts as region tag when every comma-separated part is one of these).</summary>
    private static readonly HashSet<string> RegionWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "USA", "Europe", "Japan", "World", "Germany", "France", "Spain", "Italy", "Netherlands", "Sweden",
        "Norway", "Denmark", "Finland", "Scandinavia", "UK", "United Kingdom", "Australia", "Brazil", "Canada",
        "Korea", "China", "Taiwan", "Hong Kong", "Asia", "Russia", "Poland", "Portugal", "Greece", "Mexico",
        "Latin America", "India", "Austria", "Switzerland", "Belgium", "Ireland", "New Zealand", "Unknown",
    };

    /// <summary>GoodTools country codes.</summary>
    private static readonly Dictionary<string, string> GoodToolsRegions = new(StringComparer.Ordinal)
    {
        ["U"] = "USA", ["E"] = "Europe", ["J"] = "Japan", ["W"] = "World", ["G"] = "Germany", ["F"] = "France",
        ["S"] = "Spain", ["I"] = "Italy", ["K"] = "Korea", ["C"] = "China", ["A"] = "Australia", ["B"] = "Brazil",
        ["Ch"] = "China", ["Nl"] = "Netherlands", ["Sw"] = "Sweden", ["UK"] = "UK", ["HK"] = "Hong Kong",
        ["UE"] = "USA, Europe", ["EU"] = "USA, Europe", ["JU"] = "USA, Japan", ["UJ"] = "USA, Japan",
        ["JE"] = "Europe, Japan", ["EJ"] = "Europe, Japan", ["JUE"] = "World", ["UEJ"] = "World", ["JEU"] = "World",
    };

    [GeneratedRegex(@"\(([^()]*)\)|\[([^\[\]]*)\]")]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"^(?<main>.+?),\s+(?<art>The|A|An|Der|Die|Das)(?<rest>\s*(?:[-:~].*)?)$", RegexOptions.IgnoreCase)]
    private static partial Regex TrailingArticleRegex();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex MultiSpace();

    /// <summary>Parses a name. <paramref name="name"/> is a file name (extension is removed when <paramref name="isFileName"/>) or a folder name.</summary>
    public static ParsedTitle Parse(string name, bool isFileName = true)
    {
        var raw = name ?? "";
        if (isFileName)
        {
            raw = Path.GetFileName(raw);
            var ext = Path.GetExtension(raw);
            // Only strip something that looks like an extension (".gba", ".7z"), not "Vol. 2".
            if (ext.Length is > 1 and <= 6 && !ext.Contains(' ')) raw = raw[..^ext.Length];
        }

        string? region = null;
        var stripped = TagRegex().Replace(raw, m =>
        {
            if (m.Groups[1].Success) region ??= RegionOf(m.Groups[1].Value);
            return " ";
        });
        // A stray unbalanced bracket ("Game (Beta") — cut at it.
        var cut = stripped.IndexOfAny(['(', '[']);
        if (cut > 0) stripped = stripped[..cut];

        var title = Clean(stripped);
        if (title.Length == 0) title = Clean(raw); // name consisted of tags only: keep it rather than show nothing

        var m2 = TrailingArticleRegex().Match(title);
        if (m2.Success)
        {
            var art = m2.Groups["art"].Value;
            art = char.ToUpperInvariant(art[0]) + art[1..].ToLowerInvariant();
            title = $"{art} {m2.Groups["main"].Value.Trim()}{m2.Groups["rest"].Value.TrimEnd()}";
        }

        return new ParsedTitle(title, SortTitleOf(title), region);
    }

    /// <summary>Drops a leading article ("The", "Der", "Die", "Das", "A", "An") for sorting.</summary>
    public static string SortTitleOf(string title)
    {
        foreach (var a in SortArticles)
            if (title.Length > a.Length && title.StartsWith(a, StringComparison.OrdinalIgnoreCase))
                return title[a.Length..].TrimStart();
        return title;
    }

    /// <summary>
    /// True when the file name carries no real title: digits only ("0001"), a hash, a serial ("SLUS_203.12",
    /// "NTR-IRBO", "AGB-001"), or a generic word ("rom", "game", "disc 1"). Then a header title is the better choice.
    /// </summary>
    public static bool IsMeaningless(string title)
    {
        var t = title.Trim();
        if (t.Length == 0) return true;
        if (MeaninglessRegex().IsMatch(t)) return true;
        return false;
    }

    [GeneratedRegex(@"^(?:[\d\s._-]+|[0-9a-fA-F]{8,}|[A-Z]{4}[ _-]?\d{3}\.?\d{2}|[A-Z]{3}(?:-[A-Z0-9]{1,5}){1,2}|(?=[A-Z0-9_-]*\d)[A-Z0-9_-]{3,14}|(?i:rom|game|image|disc|disk|track|untitled|unknown|default|boot|cart)\s*\d*)$")]
    private static partial Regex MeaninglessRegex();

    /// <summary>Cleans a raw header title (padding, control characters, runs of spaces).</summary>
    public static string? CleanHeaderTitle(string? headerTitle)
    {
        if (string.IsNullOrWhiteSpace(headerTitle)) return null;
        var sb = new StringBuilder(headerTitle.Length);
        foreach (var c in headerTitle) sb.Append(char.IsControl(c) ? ' ' : c);
        var s = MultiSpace().Replace(sb.ToString(), " ").Trim();
        return s.Length == 0 ? null : s;
    }

    private static string Clean(string s)
    {
        s = s.Replace('_', ' ');
        s = MultiSpace().Replace(s, " ").Trim();
        // '.' is not trimmed: "Super Mario Bros.", "Ken Griffey Jr." end with one, ".hack" starts with one.
        return s.Trim(' ', '-', ',', '~').Trim();
    }

    private static string? RegionOf(string tag)
    {
        var t = tag.Trim();
        if (GoodToolsRegions.TryGetValue(t, out var g)) return g;
        var parts = t.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return null;
        foreach (var p in parts)
            if (!RegionWords.Contains(p)) return null;
        return string.Join(", ", parts);
    }
}
