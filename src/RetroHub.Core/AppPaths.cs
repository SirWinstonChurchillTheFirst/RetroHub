namespace RetroHub.Core;

/// <summary>
/// The portable folder layout. Everything RetroHub writes lives below <see cref="Root"/>.
/// Paths stored in the database/config are portable: relative to Root when inside it
/// (with forward slashes), absolute otherwise (external game folders).
/// </summary>
public sealed class AppPaths
{
    public const string RootMarker = "RetroHub.root";

    public string Root { get; }
    /// <summary>Program files (replaced on update). In development this is the build output folder.</summary>
    public string App { get; }
    /// <summary>Built-in definitions shipped with the program: systems/, emulators/, lang/.</summary>
    public string Resources { get; }

    public string Data => Path.Combine(Root, "data");
    public string Database => Path.Combine(Data, "database");
    public string DatabaseFile => Path.Combine(Database, "retrohub.db");
    public string Config => Path.Combine(Data, "config");
    public string SettingsFile => Path.Combine(Config, "settings.json");
    public string Metadata => Path.Combine(Data, "metadata");
    public string Cache => Path.Combine(Data, "cache");
    public string Thumbnails => Path.Combine(Cache, "thumbs");
    public string Logs => Path.Combine(Data, "logs");

    public string Games => Path.Combine(Root, "games");
    public string Emulators => Path.Combine(Root, "emulators");
    public string Bios => Path.Combine(Root, "bios");
    public string Saves => Path.Combine(Root, "saves");
    public string SaveStates => Path.Combine(Root, "savestates");
    public string Screenshots => Path.Combine(Root, "screenshots");
    public string Artwork => Path.Combine(Root, "artwork");
    public string Covers => Path.Combine(Artwork, "covers");
    public string Backgrounds => Path.Combine(Artwork, "backgrounds");
    public string Logos => Path.Combine(Artwork, "logos");
    /// <summary>In-game screenshots (libretro "Named_Snaps"), one folder per system; created on first download.</summary>
    public string Snaps => Path.Combine(Artwork, "snaps");
    /// <summary>Title screens (libretro "Named_Titles"), one folder per system; created on first download.</summary>
    public string Titles => Path.Combine(Artwork, "titles");
    public string Themes => Path.Combine(Root, "themes");
    public string Plugins => Path.Combine(Root, "plugins");
    public string Backups => Path.Combine(Root, "backups");

    public AppPaths(string root, string app)
    {
        Root = Path.GetFullPath(root);
        App = Path.GetFullPath(app);
        Resources = Path.Combine(App, "resources");
    }

    /// <summary>
    /// Finds the portable root:
    /// 1. env RETROHUB_ROOT;
    /// 2. the exe sits in a folder named "app" → its parent (published layout: RetroHub\app\RetroHub.App.exe);
    /// 3. walk up from the exe folder to a folder containing <see cref="RootMarker"/> (development checkout);
    /// 4. the exe folder itself.
    /// </summary>
    public static AppPaths Resolve(string? exeDir = null)
    {
        exeDir ??= AppContext.BaseDirectory;
        exeDir = Path.GetFullPath(exeDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        var env = Environment.GetEnvironmentVariable("RETROHUB_ROOT");
        if (!string.IsNullOrWhiteSpace(env) && Directory.Exists(env))
            return new AppPaths(env, exeDir);

        var dir = new DirectoryInfo(exeDir);
        if (string.Equals(dir.Name, "app", StringComparison.OrdinalIgnoreCase) && dir.Parent != null)
            return new AppPaths(dir.Parent.FullName, exeDir);

        for (var d = dir; d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, RootMarker)))
                return new AppPaths(d.FullName, exeDir);

        return new AppPaths(exeDir, exeDir);
    }

    /// <summary>Creates every standard folder plus one games/saves/savestates/screenshots/covers subfolder per system.</summary>
    public void EnsureCreated(IEnumerable<string> gameFolders)
    {
        foreach (var d in new[] { Data, Database, Config, Metadata, Cache, Thumbnails, Logs, Games, Emulators, Bios,
                     Saves, SaveStates, Screenshots, Artwork, Covers, Backgrounds, Logos, Themes, Plugins,
                     Path.Combine(Plugins, "systems"), Path.Combine(Plugins, "emulators"), Backups })
            Directory.CreateDirectory(d);
        foreach (var f in gameFolders)
        {
            if (!IsSafeName(f)) continue;
            Directory.CreateDirectory(Path.Combine(Games, f));
        }
    }

    /// <summary>Stores a path portably: relative to Root (forward slashes) if inside it, else absolute.</summary>
    public string ToPortable(string path)
    {
        var full = Path.GetFullPath(path);
        var rel = Path.GetRelativePath(Root, full);
        if (rel == "." ) return "";
        if (rel.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(rel)) return full;
        return rel.Replace('\\', '/');
    }

    /// <summary>Resolves a stored (portable) path back to an absolute one.</summary>
    public string FromPortable(string stored)
    {
        if (string.IsNullOrEmpty(stored)) return Root;
        var p = stored.Replace('/', Path.DirectorySeparatorChar);
        return Path.IsPathRooted(p) ? Path.GetFullPath(p) : Path.GetFullPath(Path.Combine(Root, p));
    }

    /// <summary>True when <paramref name="path"/> is inside <paramref name="folder"/> (after normalisation).</summary>
    public static bool IsInside(string path, string folder)
    {
        var p = Path.GetFullPath(path).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        var f = Path.GetFullPath(folder).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        return p.StartsWith(f, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A single folder/file name without separators, "..", or invalid characters.</summary>
    public static bool IsSafeName(string name) =>
        !string.IsNullOrWhiteSpace(name) && name != "." && name != ".." &&
        name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
}
