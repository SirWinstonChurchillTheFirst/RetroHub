using System.Text;
using System.Text.RegularExpressions;
using RetroHub.Core.Data;
using RetroHub.Core.Library;

namespace RetroHub.Core.Metadata;

/// <summary>
/// Naming rules of libretro-thumbnails (https://github.com/libretro-thumbnails, served by thumbnails.libretro.com):
/// <c>https://thumbnails.libretro.com/&lt;LibretroName&gt;/Named_Boxarts/&lt;name&gt;.png</c>, where <c>&lt;name&gt;</c> is the
/// No-Intro/Redump name with the characters <c>&amp;*/:`&lt;&gt;?\|"</c> replaced by "_".
/// </summary>
public static class LibretroThumbnails
{
    public const string ThumbnailHost = "https://thumbnails.libretro.com";
    public const string BoxartFolder = "Named_Boxarts";
    private const string ForbiddenChars = "&*/:`<>?\\|\"";

    /// <summary>Applies libretro's file name rule: each of <c>&amp;*/:`&lt;&gt;?\|"</c> becomes "_".</summary>
    public static string Sanitize(string name)
    {
        if (string.IsNullOrEmpty(name)) return "";
        var sb = new StringBuilder(name.Length);
        foreach (var c in name) sb.Append(ForbiddenChars.Contains(c) ? '_' : c);
        return sb.ToString();
    }

    /// <summary>"Nintendo - Game Boy Advance" → "Nintendo_-_Game_Boy_Advance" (GitHub repository name).</summary>
    public static string RepoName(string libretroName) => libretroName.Trim().Replace(' ', '_');

    /// <summary>GitHub git-trees API URL listing every file of the system's thumbnail repository.</summary>
    public static Uri TreeUrl(string libretroName) =>
        new($"https://api.github.com/repos/libretro-thumbnails/{Uri.EscapeDataString(RepoName(libretroName))}/git/trees/master?recursive=1");

    /// <summary>Boxart URL for an already sanitised thumbnail name (without ".png").</summary>
    public static Uri BoxartUrl(string libretroName, string sanitizedName) =>
        new($"{ThumbnailHost}/{Uri.EscapeDataString(libretroName)}/{BoxartFolder}/{Uri.EscapeDataString(sanitizedName)}.png");

    /// <summary>
    /// Region preference for picking a cover: the ROM's own region first (rank −1), then Europe/Germany (0) &gt; USA (1)
    /// &gt; World (2) &gt; Japan (3) &gt; any other region (4) &gt; no region (5). Multi-region tags ("USA, Europe") take their best part.
    /// </summary>
    public static int RegionRank(string? region, string? preferred = null)
    {
        if (string.IsNullOrWhiteSpace(region)) return 5;
        var parts = region.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (!string.IsNullOrWhiteSpace(preferred))
        {
            var own = preferred.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (own.Length > 0 && own.All(o => parts.Contains(o, StringComparer.OrdinalIgnoreCase))) return -1;
        }
        var best = 4;
        foreach (var p in parts)
        {
            var r = p.ToLowerInvariant() switch
            {
                "europe" or "germany" or "austria" or "switzerland"
                    or "deutsch" or "german" or "deutschland" or "europa" or "österreich" or "schweiz" or "pal" or "pal-g" => 0,
                "usa" or "ntsc" or "ntsc-u" => 1,
                "world" or "weltweit" => 2,
                "japan" or "japanisch" or "japanese" or "ntsc-j" => 3,
                _ => 4,
            };
            best = Math.Min(best, r);
        }
        return best;
    }

    /// <summary>
    /// Names to try directly on thumbnails.libretro.com when no file index is available (at most 4, sanitised, distinct):
    /// the ROM file name without extension (usually already the No-Intro name), then the cleaned title with its region,
    /// or with the common regions when the ROM carries none.
    /// </summary>
    public static IReadOnlyList<string> DirectCandidates(Game game)
    {
        var list = new List<string>();
        void Add(string? n)
        {
            if (string.IsNullOrWhiteSpace(n)) return;
            var s = Sanitize(n.Trim());
            if (!list.Contains(s, StringComparer.OrdinalIgnoreCase)) list.Add(s);
        }
        var stem = ArtworkNames.Stem(game);
        Add(stem);
        var parsed = TitleParser.Parse(stem, isFileName: false);
        var title = string.IsNullOrWhiteSpace(parsed.Title) || TitleParser.IsMeaningless(parsed.Title) ? game.Title : parsed.Title;
        var region = parsed.Region ?? game.Region;
        if (region != null) Add($"{title} ({region})");
        else
        {
            Add($"{title} (Europe)");
            Add($"{title} (USA)");
            Add($"{title} (World)");
        }
        return list.Take(4).ToList();
    }
}

/// <summary>
/// The boxart names of one libretro-thumbnails system (file names without ".png", already sanitised by libretro),
/// with lookup by exact name and by normalised title (<see cref="SearchText.Normalize"/>) + region preference.
/// </summary>
public sealed partial class ThumbnailIndex
{
    private sealed record Entry(string Name, string? Region, int Penalty, int TagCount);

    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal) { "the", "a", "an", "and", "version", "edition", "der", "die", "das" };

    [GeneratedRegex(@"\((?:[^()]*\b)?(Beta|Proto|Prototype|Demo|Sample|Kiosk|Pirate|Virtual Console|Debug|Promo|Hack)\b[^()]*\)", RegexOptions.IgnoreCase)]
    private static partial Regex BadTag();

    [GeneratedRegex(@"\([^()]*\)|\[[^\[\]]*\]")]
    private static partial Regex AnyTag();

    private readonly HashSet<string> _names;
    private readonly Dictionary<string, List<Entry>> _byKey = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<Entry>> _byLooseKey = new(StringComparer.Ordinal);

    /// <param name="truncated">GitHub cut the listing short (very large repositories): a name missing from it may still exist.</param>
    public ThumbnailIndex(IEnumerable<string> names, bool truncated = false)
    {
        Truncated = truncated;
        _names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in names)
        {
            if (string.IsNullOrWhiteSpace(raw) || !_names.Add(raw)) continue;
            var parsed = TitleParser.Parse(raw, isFileName: false);
            var entry = new Entry(raw, parsed.Region, BadTag().IsMatch(raw) ? 1 : 0, AnyTag().Matches(raw).Count);
            Add(_byKey, Key(parsed.Title), entry);
            Add(_byLooseKey, LooseKey(parsed.Title), entry);
        }
    }

    public int Count => _names.Count;

    /// <summary>The listing is incomplete: "no match" does not mean "no boxart".</summary>
    public bool Truncated { get; }
    public IEnumerable<string> Names => _names;

    public bool Contains(string sanitizedName) => _names.Contains(sanitizedName);

    /// <summary>
    /// Best boxart name for a game, or null:
    /// 1. the sanitised ROM file name exactly (No-Intro named ROMs);
    /// 2. same normalised title (parsed from the file name, then the stored title) — best region / fewest odd tags;
    /// 3. same title words ignoring order, articles and "Version"/"Edition" ("Pokemon - Emerald Version" ≈ "Pokemon Emerald").
    /// Tags such as (Beta)/(Proto)/(Demo)/(Virtual Console) are only chosen when the ROM itself carries such a tag or nothing else exists.
    /// </summary>
    public string? Match(Game game)
    {
        var stem = ArtworkNames.Stem(game);
        var exact = LibretroThumbnails.Sanitize(stem);
        if (_names.TryGetValue(exact, out var hit)) return hit;

        var parsed = TitleParser.Parse(stem, isFileName: false);
        var region = parsed.Region ?? game.Region;
        var romIsOdd = BadTag().IsMatch(stem);
        var titles = new[] { parsed.Title, game.Title }.Where(t => !string.IsNullOrWhiteSpace(t) && !TitleParser.IsMeaningless(t)).Distinct().ToList();

        foreach (var t in titles)
            if (_byKey.TryGetValue(Key(t), out var list)) return Best(list, region, romIsOdd);
        foreach (var t in titles)
            if (_byLooseKey.TryGetValue(LooseKey(t), out var list)) return Best(list, region, romIsOdd);
        return null;
    }

    private static string? Best(List<Entry> list, string? region, bool romIsOdd) => list
        .OrderBy(e => romIsOdd ? 0 : e.Penalty)
        .ThenBy(e => LibretroThumbnails.RegionRank(e.Region, region))
        .ThenBy(e => e.TagCount)
        .ThenBy(e => e.Name.Length)
        .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
        .FirstOrDefault()?.Name;

    /// <summary>Normalised title: sanitised like libretro (so "&amp;" and "_" compare equal), then <see cref="SearchText.Normalize"/>.</summary>
    internal static string Key(string title) => SearchText.Normalize(LibretroThumbnails.Sanitize(title));

    /// <summary>Title words without stop words, sorted.</summary>
    internal static string LooseKey(string title)
    {
        var words = Key(title).Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => !StopWords.Contains(w)).Order(StringComparer.Ordinal);
        return string.Join(' ', words);
    }

    private static void Add(Dictionary<string, List<Entry>> map, string key, Entry e)
    {
        if (key.Length == 0) return;
        if (!map.TryGetValue(key, out var list)) map[key] = list = new List<Entry>(1);
        list.Add(e);
    }
}

/// <summary>Name helpers shared by the local and online lookups.</summary>
public static class ArtworkNames
{
    public static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".webp"];

    /// <summary>
    /// The name a game's artwork is stored and looked up under: normally the ROM file name without extension
    /// ("Advance Wars (USA).gba" → "Advance Wars (USA)"). PC games (exe/shortcut) and ROMs with a meaningless file name
    /// ("game.iso", "disc1.bin") use their title instead: exe names such as "Game", "Launcher" or "start" are shared by
    /// many games, and artwork/covers/pc/Game.png must never become every such game's cover.
    /// </summary>
    public static string Stem(Game game)
    {
        var file = FileStem(game);
        // titles may contain ':' or '?' ("Hades: Battle out of Hell"): libretro's rule makes them file-name safe
        var title = LibretroThumbnails.Sanitize(game.Title?.Trim() ?? "");
        if (string.IsNullOrEmpty(title) || TitleParser.IsMeaningless(title) || !AppPaths.IsSafeName(title)) return file;
        if (game.Kind is GameKind.Executable or GameKind.Shortcut) return title;
        return TitleParser.IsMeaningless(TitleParser.Parse(file, isFileName: false).Title) ? title : file;
    }

    /// <summary>The file name without extension exactly ("Game.exe" → "Game"); for URLs the last segment.
    /// Used for images that sit next to the file (where the file name is specific) and for ES-DE/Batocera media.</summary>
    public static string FileStem(Game game)
    {
        var p = game.Path ?? "";
        if (p.Contains("://", StringComparison.Ordinal))
        {
            var last = p.TrimEnd('/').Split('/').LastOrDefault() ?? "";
            return string.IsNullOrWhiteSpace(last) ? game.Title : last;
        }
        var name = Path.GetFileName(p.Replace('\\', '/').Replace('/', Path.DirectorySeparatorChar));
        var stem = Path.GetFileNameWithoutExtension(name);
        return string.IsNullOrWhiteSpace(stem) ? game.Title : stem;
    }

    /// <summary>True for a file extension RetroHub accepts as artwork.</summary>
    public static bool IsImage(string path) =>
        ImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
}
