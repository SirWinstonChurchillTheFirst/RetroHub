using System.Text.RegularExpressions;
using RetroHub.Core.Data;
using RetroHub.Core.Library;

namespace RetroHub.Core.Metadata;

/// <summary>
/// GameTDB (https://www.gametdb.com), the cover source for systems libretro-thumbnails has no set for (Nintendo
/// Switch). GameTDB allows use in software and asks for a link back (FAQ "Developers"); RetroHub names it in the
/// settings. Two endpoints:
/// <list type="bullet">
/// <item>the database, <c>https://www.gametdb.com/&lt;platform&gt;tdb.zip?LANG=DE</c> (one ZIP with &lt;platform&gt;tdb.xml,
/// every locale inside, ~1.9 MB for Switch), fetched at most every 30 days with an ETag (<see cref="GameTdbIndexStore"/>);</item>
/// <item>the covers, <c>https://art.gametdb.com/&lt;platform&gt;/coverM/&lt;LANG&gt;/&lt;ID&gt;.jpg</c> (352 × 570 JPEG front scans,
/// 80–160 KB), tried in the order <see cref="CoverLanguages"/>; a missing language is a clean 404.</item>
/// </list>
/// The ID is GameTDB's product code (5 characters for Switch, e.g. "AXEAB"); file names never contain it, so games are
/// matched by title (<see cref="GameTdbIndex"/>).
/// </summary>
public static partial class GameTdb
{
    public const string DatabaseHost = "https://www.gametdb.com";
    public const string ArtHost = "https://art.gametdb.com";
    /// <summary>The cover size used for library boxes: 352 × 570 (coverHQ is ~800 KB and too heavy for a grid).</summary>
    public const string CoverSize = "coverM";

    /// <summary>Cover languages in the order they are asked for: German box, else the English/European one, else the US box.</summary>
    public static readonly IReadOnlyList<string> CoverLanguages = ["DE", "EN", "US"];

    [GeneratedRegex("^[a-z0-9]{1,16}$")]
    private static partial Regex PlatformRx();

    [GeneratedRegex("^[A-Z0-9]{4,10}$")]
    private static partial Regex IdRx();

    public static bool IsValidPlatform(string? platform) => platform != null && PlatformRx().IsMatch(platform);

    /// <summary>A GameTDB product code that is safe to put into a URL (upper-case letters and digits).</summary>
    public static bool IsValidId(string? id) => id != null && IdRx().IsMatch(id);

    /// <summary>The database ZIP of a platform ("switch" → https://www.gametdb.com/switchtdb.zip?LANG=DE).</summary>
    public static Uri DatabaseUrl(string platform)
    {
        if (!IsValidPlatform(platform)) throw new ArgumentException($"invalid GameTDB platform '{platform}'", nameof(platform));
        return new Uri($"{DatabaseHost}/{platform}tdb.zip?LANG=DE");
    }

    /// <summary>The front cover of <paramref name="id"/> in <paramref name="language"/> (coverM by default).</summary>
    public static Uri CoverUrl(string platform, string language, string id, string size = CoverSize)
    {
        if (!IsValidPlatform(platform)) throw new ArgumentException($"invalid GameTDB platform '{platform}'", nameof(platform));
        if (!IsValidId(id)) throw new ArgumentException($"invalid GameTDB id '{id}'", nameof(id));
        if (language.Length is < 2 or > 4 || !language.All(char.IsAsciiLetterUpper)) throw new ArgumentException($"invalid language '{language}'", nameof(language));
        if (size.Length is 0 or > 12 || !size.All(char.IsAsciiLetter)) throw new ArgumentException($"invalid size '{size}'", nameof(size));
        return new Uri($"{ArtHost}/{platform}/{size}/{language}/{id}.jpg");
    }

    /// <summary>
    /// Region preference for GameTDB's region lists ("USA,EUR,AUS"): Europe or region-free (ALL) 0, USA/Australia 1,
    /// Japan 3, anything else 4, none 5; a multi-region list takes its best part.
    /// </summary>
    public static int RegionRank(string? regions)
    {
        if (string.IsNullOrWhiteSpace(regions)) return 5;
        var best = 4;
        foreach (var p in regions.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var r = p.ToUpperInvariant() switch
            {
                "ALL" or "EUR" or "GER" or "DEU" => 0,
                "USA" or "AUS" or "NZ" => 1,
                "JPN" => 3,
                _ => 4,
            };
            best = Math.Min(best, r);
        }
        return best;
    }
}

/// <summary>One game of a GameTDB database: product code, type ("Switch" = cartridge, "eShop"), regions and every title.</summary>
public sealed record GameTdbEntry(string Id, string? Type, string? Regions, IReadOnlyList<string> Titles);

/// <summary>
/// The entries of one GameTDB platform, matched by title. File names carry no product code, use " - " where GameTDB
/// writes ":", German titles, or a shortened title ("Cadence of Hyrule"), so three lookups are tried, first hit wins:
/// <list type="number">
/// <item>the normalised title (<see cref="SearchText.Normalize"/>: case, diacritics, apostrophes, ":" vs " - ") against
/// EVERY locale title of an entry, so the German, English and Japanese names all find it;</item>
/// <item>the same words in any order without articles ("the/a/an/and/der/die/das"; unlike libretro's loose key, "Edition"
/// and "Version" stay: "Hyrule Warriors - Definitive Edition" must not collapse onto other Hyrule Warriors entries);</item>
/// <item>the main title: the query equals the part of an entry title before its first ":", " - " or "–" (at least two
/// words and eight characters, so "Zelda" alone never matches half the database).</item>
/// </list>
/// Candidates are ranked: bundles/demos/passes last, then region (<see cref="GameTdb.RegionRank"/>), cartridge before
/// eShop, the shorter title, the product code.
/// </summary>
public sealed partial class GameTdbIndex
{
    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal) { "the", "a", "an", "and", "der", "die", "das" };

    [GeneratedRegex(@"\([^()]*\)|\[[^\[\]]*\]")]
    private static partial Regex Tags();

    [GeneratedRegex(@"\s\+\s|\bbundle\b|\bdemo\b|\btrial\b|\bseason pass\b|\bexpansion pass\b|\bdlc\b", RegexOptions.IgnoreCase)]
    private static partial Regex BundleRx();

    [GeneratedRegex(@"\s*(?::|\s-\s|–|—)\s*")]
    private static partial Regex MainSplit();

    private sealed record Candidate(GameTdbEntry Entry, int Penalty, int Region, int TypeRank, int ShortestTitle);

    private readonly Dictionary<string, List<Candidate>> _exact = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<Candidate>> _loose = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<Candidate>> _main = new(StringComparer.Ordinal);

    public GameTdbIndex(IEnumerable<GameTdbEntry> entries)
    {
        foreach (var e in entries)
        {
            if (!GameTdb.IsValidId(e.Id) || e.Titles.Count == 0) continue;
            Count++;
            var c = new Candidate(e,
                e.Titles.Any(t => BundleRx().IsMatch(t)) ? 1 : 0,
                GameTdb.RegionRank(e.Regions),
                string.Equals(e.Type, "eShop", StringComparison.OrdinalIgnoreCase) ? 1 : 0,
                e.Titles.Min(t => t.Length));
            foreach (var t in e.Titles)
            {
                var key = Key(t);
                if (key.Length == 0) continue;
                Add(_exact, key, c);
                Add(_loose, LooseKey(key), c);
                var main = Key(MainSplit().Split(StripTags(t), 2)[0]);
                if (main.Length > 0 && main != key) Add(_main, main, c);
            }
        }
    }

    public int Count { get; }

    /// <summary>The product code of the best entry for <paramref name="game"/>, or null.</summary>
    public string? Match(Game game)
    {
        var stem = ArtworkNames.Stem(game);
        var parsed = TitleParser.Parse(stem, isFileName: false).Title;
        var keys = new[] { parsed, game.Title, stem }
            .Where(t => !string.IsNullOrWhiteSpace(t) && !TitleParser.IsMeaningless(t))
            .Select(Key).Where(k => k.Length > 0).Distinct().ToList();
        foreach (var k in keys)
            if (_exact.TryGetValue(k, out var list)) return Best(list);
        foreach (var k in keys)
            if (_loose.TryGetValue(LooseKey(k), out var list)) return Best(list);
        foreach (var k in keys)
        {
            if (k.Length < 8 || k.Split(' ').Length < 2) continue;
            if (_main.TryGetValue(k, out var list)) return Best(list);
        }
        return null;
    }

    /// <summary>The product code of the best entry for a plain title (tests, diagnostics).</summary>
    public string? Match(string title) => Match(new Game { SystemId = "", Path = title + ".nsp", Title = title });

    private static string Best(List<Candidate> list) => list
        .OrderBy(c => c.Penalty)
        .ThenBy(c => c.Region)
        .ThenBy(c => c.TypeRank)
        .ThenBy(c => c.ShortestTitle)
        .ThenBy(c => c.Entry.Id, StringComparer.Ordinal)
        .First().Entry.Id;

    private static string StripTags(string s) => Tags().Replace(s, " ");

    /// <summary>Tags such as "(EN,FR)" or "[0100A6301214E000][v0]" removed, then <see cref="SearchText.Normalize"/>.</summary>
    internal static string Key(string title) => SearchText.Normalize(StripTags(title));

    internal static string LooseKey(string key) =>
        string.Join(' ', key.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => !StopWords.Contains(w)).Order(StringComparer.Ordinal));

    private static void Add(Dictionary<string, List<Candidate>> map, string key, Candidate c)
    {
        if (key.Length == 0) return;
        if (!map.TryGetValue(key, out var list)) map[key] = list = new List<Candidate>(1);
        if (!list.Any(x => ReferenceEquals(x.Entry, c.Entry))) list.Add(c);
    }
}
