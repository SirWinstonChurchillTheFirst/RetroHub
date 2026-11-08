using System.Collections.Concurrent;
using System.Security.Cryptography;
using RetroHub.Core.Catalog;
using RetroHub.Core.Logging;

namespace RetroHub.Core.Emulation;

public enum BiosState
{
    /// <summary>Found and (if MD5s are listed) a known-good dump.</summary>
    Ok,
    /// <summary>Found, but its MD5 matches none of the known dumps.</summary>
    Mismatch,
    Missing,
}

/// <summary>Result for one <see cref="BiosFile"/> of a system.</summary>
public sealed record BiosStatus(BiosFile File, BiosState State, string? FoundPath, string? Md5);

/// <summary>
/// Looks for a system's BIOS files in bios/ and its subfolders (up to 3 levels, e.g. bios/psx/scph5501.bin),
/// file names case-insensitive. A <see cref="BiosFile.File"/> with a subpath ("dc/dc_boot.bin") must match that
/// relative path suffix. Names with ".." or rooted paths are rejected (no path traversal from plugin JSON).
/// MD5s are cached by (path, size, mtime), so repeated launches do not re-hash. Never throws.
/// </summary>
public static class BiosCheck
{
    private const int MaxDepth = 3;
    private static readonly ConcurrentDictionary<(string Path, long Size, DateTime Time), string> Md5Cache = new();

    public static IReadOnlyList<BiosStatus> Check(SystemDef system, string biosRoot)
    {
        var result = new List<BiosStatus>();
        if (system.Bios.Length == 0) return result;
        var files = ListFiles(biosRoot);
        foreach (var bios in system.Bios)
        {
            try { result.Add(CheckOne(bios, biosRoot, files)); }
            catch (Exception ex)
            {
                Log.Warn($"BIOS check failed for {bios.File}", ex);
                result.Add(new BiosStatus(bios, BiosState.Missing, null, null));
            }
        }
        return result;
    }

    /// <summary>
    /// Launch problems: a missing <see cref="BiosFile.Required"/> file is blocking; an MD5 mismatch is a
    /// non-blocking warning; a missing optional file is no problem (emulators fall back to HLE).
    /// </summary>
    public static IReadOnlyList<LaunchProblem> Problems(SystemDef system, string biosRoot) =>
        ProblemsFor(system, Check(system, biosRoot));

    public static IReadOnlyList<LaunchProblem> ProblemsFor(SystemDef system, IReadOnlyList<BiosStatus> statuses)
    {
        var problems = new List<LaunchProblem>();
        foreach (var s in statuses)
        {
            var label = s.File.Description is { Length: > 0 } d ? $"„{s.File.File}“ ({d})" : $"„{s.File.File}“";
            switch (s.State)
            {
                case BiosState.Missing when s.File.Required:
                    problems.Add(new LaunchProblem(LaunchProblemKind.BiosMissing,
                        $"Für {system.Name} wird die BIOS-Datei {label} benötigt. Lege sie in den Ordner „bios/“ " +
                        "(oder einen Unterordner davon). RetroHub liefert keine BIOS-Dateien mit.", Blocking: true));
                    break;
                case BiosState.Mismatch:
                    problems.Add(new LaunchProblem(LaunchProblemKind.BiosMissing,
                        $"Die BIOS-Datei {label} wurde gefunden, ihre Prüfsumme (MD5 {s.Md5}) passt aber zu keiner bekannten " +
                        "Version. Das Spiel startet eventuell nicht richtig.", Blocking: false));
                    break;
            }
        }
        return problems;
    }

    /// <summary>True when the relative BIOS name is safe (no "..", not rooted, no invalid characters).</summary>
    internal static bool IsSafeRelative(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || Path.IsPathRooted(name)) return false;
        var parts = name.Split('/', '\\');
        return parts.All(AppPaths.IsSafeName);
    }

    private static BiosStatus CheckOne(BiosFile bios, string biosRoot, List<string> files)
    {
        if (!IsSafeRelative(bios.File))
        {
            Log.Warn($"Ignoring unsafe BIOS file name '{bios.File}'");
            return new BiosStatus(bios, BiosState.Missing, null, null);
        }
        var wanted = bios.File.Replace('\\', '/');
        var candidates = files
            .Where(f => Matches(f, wanted))
            // shallow first: bios/x.bin before bios/sub/x.bin
            .OrderBy(f => f.Count(ch => ch == '/'))
            .ThenBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (candidates.Count == 0) return new BiosStatus(bios, BiosState.Missing, null, null);

        if (bios.Md5.Length == 0)
            return new BiosStatus(bios, BiosState.Ok, Full(biosRoot, candidates[0]), null);

        string? firstMd5 = null;
        foreach (var rel in candidates)
        {
            var full = Full(biosRoot, rel);
            var md5 = Md5Of(full);
            if (md5 == null) continue;
            firstMd5 ??= md5;
            if (bios.Md5.Any(m => string.Equals(m.Trim(), md5, StringComparison.OrdinalIgnoreCase)))
                return new BiosStatus(bios, BiosState.Ok, full, md5);
        }
        return new BiosStatus(bios, BiosState.Mismatch, Full(biosRoot, candidates[0]), firstMd5);
    }

    private static bool Matches(string relative, string wanted)
    {
        if (string.Equals(relative, wanted, StringComparison.OrdinalIgnoreCase)) return true;
        return relative.EndsWith("/" + wanted, StringComparison.OrdinalIgnoreCase);
    }

    private static string Full(string root, string rel) => Path.GetFullPath(Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar)));

    /// <summary>All files below the root (relative, '/'-separated), depth-limited, inaccessible folders skipped.</summary>
    private static List<string> ListFiles(string root)
    {
        var list = new List<string>();
        if (!Directory.Exists(root)) return list;
        var options = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = true,
            MaxRecursionDepth = MaxDepth,
            AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System,
        };
        try
        {
            foreach (var f in Directory.EnumerateFiles(root, "*", options))
                list.Add(Path.GetRelativePath(root, f).Replace('\\', '/'));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Cannot list BIOS folder {root}", ex);
        }
        return list;
    }

    /// <summary>Lower-case hex MD5, cached; null when the file cannot be read.</summary>
    internal static string? Md5Of(string path)
    {
        try
        {
            var info = new FileInfo(path);
            var key = (info.FullName, info.Length, info.LastWriteTimeUtc);
            if (Md5Cache.TryGetValue(key, out var cached)) return cached;
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var md5 = Convert.ToHexStringLower(MD5.HashData(fs));
            Md5Cache[key] = md5;
            return md5;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Cannot read BIOS file {path}", ex);
            return null;
        }
    }
}
