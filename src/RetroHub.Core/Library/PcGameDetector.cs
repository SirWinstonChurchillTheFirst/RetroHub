namespace RetroHub.Core.Library;

/// <summary>
/// Picks the main executable of a PC game folder. Heuristic:
/// 1. ignore installers/uninstallers/redistributables/crash handlers (unins*, setup*, install*, vcredist*, dxsetup*,
///    *crash*, UnityCrashHandler*, *redist*, …);
/// 2. in the folder itself: an exe named like the folder, else the largest exe;
/// 3. else a shortcut (.lnk/.url) in the folder itself;
/// 4. else the same rules one level deeper (e.g. "Game\bin\game.exe", "Game\Binaries\…").
/// </summary>
public static class PcGameDetector
{
    private static readonly string[] ExcludedPrefixes =
        ["unins", "setup", "install", "vcredist", "vc_redist", "dxsetup", "dxwebsetup", "unitycrashhandler",
         "dotnetfx", "ndp", "oalinst", "physx", "uplay_", "directx", "config", "launcherpatcher", "cleanup"];

    private static readonly string[] ExcludedContains = ["crash", "redist", "uninstall", "updater", "patcher", "benchmark"];

    private static readonly EnumerationOptions TopLevel = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
        MatchCasing = MatchCasing.CaseInsensitive,
    };

    /// <summary>True when an exe name looks like a helper (installer, crash handler …), never the game.</summary>
    public static bool IsExcludedExe(string fileName)
    {
        var n = Path.GetFileNameWithoutExtension(fileName).ToLowerInvariant();
        foreach (var p in ExcludedPrefixes) if (n.StartsWith(p, StringComparison.Ordinal)) return true;
        foreach (var c in ExcludedContains) if (n.Contains(c, StringComparison.Ordinal)) return true;
        return false;
    }

    /// <summary>Full path of the main executable or shortcut of a game folder, or null when none is found.</summary>
    public static string? FindMainExecutable(string gameFolder)
    {
        try
        {
            var dir = new DirectoryInfo(gameFolder);
            if (!dir.Exists) return null;
            var key = Normalize(dir.Name);

            var exe = PickExe(dir, key);
            if (exe != null) return exe;

            var shortcut = dir.EnumerateFiles("*", TopLevel)
                .Where(f => f.Extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase) ||
                            f.Extension.Equals(".url", StringComparison.OrdinalIgnoreCase))
                .Where(f => !IsExcludedExe(f.Name))
                .OrderBy(f => Normalize(Path.GetFileNameWithoutExtension(f.Name)) == key ? 0 : 1)
                .ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (shortcut != null) return shortcut.FullName;

            // One level deeper: collect candidates across all subfolders, then apply the same rule.
            var deeper = dir.EnumerateDirectories("*", TopLevel)
                .Where(d => d.LinkTarget == null && !ScanRules.IsSkippedFolder(d.Name))
                .SelectMany(d => Executables(d))
                .ToList();
            return Choose(deeper, key);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return null;
        }
    }

    private static string? PickExe(DirectoryInfo dir, string key) => Choose(Executables(dir).ToList(), key);

    private static IEnumerable<FileInfo> Executables(DirectoryInfo dir)
    {
        try
        {
            return dir.EnumerateFiles("*.exe", TopLevel)
                .Where(f => f.Extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) && !IsExcludedExe(f.Name))
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static string? Choose(List<FileInfo> exes, string key)
    {
        if (exes.Count == 0) return null;
        var named = exes.Where(f => Normalize(Path.GetFileNameWithoutExtension(f.Name)) == key)
            .OrderBy(f => f.FullName.Length).FirstOrDefault();
        if (named != null) return named.FullName;
        return exes.OrderByDescending(SafeLength).ThenBy(f => f.FullName, StringComparer.OrdinalIgnoreCase).First().FullName;
    }

    private static long SafeLength(FileInfo f)
    {
        try { return ScanRules.FollowLink(f).Length; } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return 0; }
    }

    /// <summary>"Half-Life 2" / "half_life2" / "HalfLife2" all become "halflife2".</summary>
    private static string Normalize(string s) => new(s.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
