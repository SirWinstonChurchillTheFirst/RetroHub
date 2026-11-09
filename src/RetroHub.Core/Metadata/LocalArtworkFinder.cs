using RetroHub.Core.Catalog;
using RetroHub.Core.Library;
using RetroHub.Core.Logging;

namespace RetroHub.Core.Metadata;

/// <summary>
/// Finds artwork that is already on disk. Order (first hit wins):
/// <list type="number">
/// <item>artwork/&lt;covers|backgrounds|logos&gt;/&lt;system folder&gt;/ — every folder name of the system plus its id —
/// for the ROM file name without extension, the stored title, the title parsed from the file name, and
/// "Title (Region)"; case-insensitive, extensions .png/.jpg/.jpeg/.webp (in that order).</item>
/// <item>The same folders by normalised title ("advance wars.jpg" for "Advance Wars (Europe).gba").</item>
/// <item>Next to the ROM: &lt;rom name&gt;.png etc. (covers only); for PC/folder games also cover.* / folder.* / boxart.*
/// in the game's own folder.</item>
/// <item>ES-DE / Batocera layouts in the ROM's folder: media/covers|fanart|marquees/&lt;rom name&gt;.*,
/// downloaded_images/&lt;rom name&gt;-image.*, images/&lt;rom name&gt;-image|-fanart|-marquee.*.</item>
/// </list>
/// Directory listings are cached for the lifetime of the finder (one resolve run). Never throws for IO problems.
/// </summary>
public sealed class LocalArtworkFinder
{
    private sealed class DirIndex
    {
        public readonly Dictionary<string, string> ByName = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, string> ByKey = new(StringComparer.Ordinal);
    }

    private readonly AppPaths _paths;
    private readonly ICatalog _catalog;
    private readonly Dictionary<string, DirIndex?> _dirs = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public LocalArtworkFinder(AppPaths paths, ICatalog catalog)
    {
        _paths = paths;
        _catalog = catalog;
    }

    /// <summary>Root folder of an artwork kind (artwork/covers, artwork/backgrounds, artwork/logos).</summary>
    public static string KindRoot(AppPaths paths, ArtworkKind kind) => kind switch
    {
        ArtworkKind.Cover => paths.Covers,
        ArtworkKind.Background => paths.Backgrounds,
        _ => paths.Logos,
    };

    /// <summary>Canonical folder for new artwork of a system: its first folder name, else its id.</summary>
    public static string SystemFolder(SystemDef? sys, string systemId)
    {
        var f = sys?.Folders.FirstOrDefault(AppPaths.IsSafeName);
        return f ?? (AppPaths.IsSafeName(systemId) ? systemId : "unknown");
    }

    /// <summary>Full path of an existing image for this game and kind, or null.</summary>
    public string? Find(Game game, ArtworkKind kind)
    {
        try { return FindCore(game, kind); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Log.Debug($"Local artwork lookup for '{game.Path}' failed: {ex.Message}");
            return null;
        }
    }

    private string? FindCore(Game game, ArtworkKind kind)
    {
        var sys = _catalog.GetSystem(game.SystemId);
        var stem = ArtworkNames.Stem(game);
        var names = NameCandidates(game, stem);

        // 1 + 2: artwork/<kind>/<system folder>/
        var root = KindRoot(_paths, kind);
        var folders = (sys?.Folders ?? []).Append(game.SystemId).Where(AppPaths.IsSafeName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var dirs = folders.Select(f => Index(Path.Combine(root, f))).OfType<DirIndex>().ToList();
        foreach (var d in dirs)
            foreach (var n in names)
                if (d.ByName.TryGetValue(n, out var hit)) return hit;
        var keys = names.Select(ThumbnailIndex.Key).Where(k => k.Length > 0).Distinct().ToList();
        foreach (var d in dirs)
            foreach (var k in keys)
                if (d.ByKey.TryGetValue(k, out var hit)) return hit;

        // 3 + 4: next to the ROM
        if (game.Path.Contains("://", StringComparison.Ordinal)) return null; // steam:// and other URLs
        var romDir = Path.GetDirectoryName(_paths.FromPortable(game.Path));
        if (string.IsNullOrEmpty(romDir)) return null;
        var here = Index(romDir);
        if (kind == ArtworkKind.Cover && here != null)
        {
            if (here.ByName.TryGetValue(stem, out var hit)) return hit;
            // cover.png in a shared system folder (games/pc/cover.png) would match every game there
            if (IsOwnFolderGame(game, sys) && !folders.Contains(Path.GetFileName(romDir), StringComparer.OrdinalIgnoreCase))
                foreach (var n in new[] { "cover", "folder", "boxart", "box" })
                    if (here.ByName.TryGetValue(n, out hit)) return hit;
        }
        foreach (var (sub, suffix) in Layouts(kind))
        {
            var d = Index(Path.Combine(romDir, sub));
            if (d != null && d.ByName.TryGetValue(stem + suffix, out var hit)) return hit;
        }
        return null;
    }

    /// <summary>Names a local image may carry, in order: file stem, stored title, parsed title, "Title (Region)", folder name (PC).</summary>
    internal static List<string> NameCandidates(Game game, string stem)
    {
        var list = new List<string>();
        void Add(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return;
            s = s.Trim();
            if (!list.Contains(s, StringComparer.OrdinalIgnoreCase)) list.Add(s);
        }
        Add(stem);
        Add(game.Title);
        var parsed = TitleParser.Parse(stem, isFileName: false);
        Add(parsed.Title);
        var region = parsed.Region ?? game.Region;
        if (region != null) Add($"{parsed.Title} ({region})");
        // a sanitised variant ("Mario _ Luigi") for files saved with libretro's naming rule
        foreach (var n in list.ToList()) Add(LibretroThumbnails.Sanitize(n));
        return list;
    }

    private static bool IsOwnFolderGame(Game game, SystemDef? sys) =>
        game.Kind is GameKind.Executable or GameKind.Shortcut || sys?.FolderGames == true || sys?.Category == SystemCategory.Pc;

    private static IEnumerable<(string Sub, string Suffix)> Layouts(ArtworkKind kind) => kind switch
    {
        ArtworkKind.Cover => [("media/covers", ""), ("media/box2dfront", ""), ("downloaded_images", "-image"), ("images", "-image"), ("images", "-thumb")],
        ArtworkKind.Background => [("media/fanart", ""), ("images", "-fanart")],
        _ => [("media/marquees", ""), ("images", "-marquee")],
    };

    private DirIndex? Index(string dir)
    {
        dir = Path.GetFullPath(dir.Replace('/', Path.DirectorySeparatorChar));
        lock (_gate)
        {
            if (_dirs.TryGetValue(dir, out var cached)) return cached;
        }
        DirIndex? index = null;
        try
        {
            if (Directory.Exists(dir))
            {
                index = new DirIndex();
                var options = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = false, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System };
                // extension order decides which file wins when "x.png" and "x.jpg" both exist
                var files = Directory.EnumerateFiles(dir, "*", options).Where(ArtworkNames.IsImage)
                    .OrderBy(f => Array.FindIndex(ArtworkNames.ImageExtensions, e => e.Equals(Path.GetExtension(f), StringComparison.OrdinalIgnoreCase)))
                    .ThenBy(f => f, StringComparer.OrdinalIgnoreCase);
                foreach (var f in files)
                {
                    var name = Path.GetFileNameWithoutExtension(f);
                    index.ByName.TryAdd(name, f);
                    var key = ThumbnailIndex.Key(TitleParser.Parse(name, isFileName: false).Title);
                    if (key.Length > 0) index.ByKey.TryAdd(key, f);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            Log.Debug($"Cannot list {dir}: {ex.Message}");
            index = null;
        }
        lock (_gate) _dirs[dir] = index;
        return index;
    }

    /// <summary>Drops cached listings (after a download added files).</summary>
    public void Invalidate(string dir)
    {
        lock (_gate) _dirs.Remove(Path.GetFullPath(dir));
    }
}
