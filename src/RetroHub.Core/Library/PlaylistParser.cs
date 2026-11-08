using System.Text;
using System.Text.RegularExpressions;

namespace RetroHub.Core.Library;

/// <summary>A file referenced by a multi-file descriptor. <see cref="Required"/> = the game cannot run without it.</summary>
public sealed record PlaylistReference(string FullPath, bool Required);

/// <summary>
/// Reads multi-file descriptors: .m3u (disc list), .cue (FILE "x" BINARY), .gdi (Dreamcast track list),
/// .ccd (CloneCD: same-name .img/.sub), .mds (Alcohol: same-name .mdf). Never throws; only reads the descriptor.
/// </summary>
public static partial class PlaylistParser
{
    /// <summary>Descriptors are tiny text files; anything bigger is not one (protects against huge misnamed files).</summary>
    private const long MaxDescriptorBytes = 1 << 20;

    [GeneratedRegex(@"^\s*FILE\s+(?:""(?<n>[^""]+)""|(?<n>\S+))", RegexOptions.IgnoreCase)]
    private static partial Regex CueFileRegex();

    [GeneratedRegex(@"^\s*\d+\s+\d+\s+\d+\s+\d+\s+(?:""(?<n>[^""]+)""|(?<n>\S+))")]
    private static partial Regex GdiTrackRegex();

    /// <summary>Direct references of a descriptor (not recursive). Empty for unknown types or unreadable files.</summary>
    public static IReadOnlyList<PlaylistReference> References(string descriptorPath)
    {
        try
        {
            var ext = Path.GetExtension(descriptorPath).ToLowerInvariant();
            var dir = Path.GetDirectoryName(Path.GetFullPath(descriptorPath)) ?? "";
            switch (ext)
            {
                case ".ccd":
                    return
                    [
                        new PlaylistReference(Path.ChangeExtension(Path.GetFullPath(descriptorPath), ".img"), true),
                        new PlaylistReference(Path.ChangeExtension(Path.GetFullPath(descriptorPath), ".sub"), false),
                    ];
                case ".mds":
                    return [new PlaylistReference(Path.ChangeExtension(Path.GetFullPath(descriptorPath), ".mdf"), true)];
                case ".m3u":
                case ".cue":
                case ".gdi":
                    break;
                default:
                    return [];
            }

            var lines = ReadLines(descriptorPath);
            if (lines == null) return [];
            var result = new List<PlaylistReference>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var first = true;
            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim().TrimStart('\uFEFF');
                string? name = null;
                switch (ext)
                {
                    case ".m3u":
                        if (line.Length == 0 || line.StartsWith('#')) continue;
                        name = line;
                        break;
                    case ".cue":
                        var m = CueFileRegex().Match(line);
                        if (m.Success) name = m.Groups["n"].Value;
                        break;
                    case ".gdi":
                        if (first) { first = false; continue; } // track count
                        var g = GdiTrackRegex().Match(line);
                        if (g.Success) name = g.Groups["n"].Value;
                        break;
                }
                if (string.IsNullOrWhiteSpace(name)) continue;
                var full = Resolve(dir, name);
                if (full != null && seen.Add(full)) result.Add(new PlaylistReference(full, true));
            }
            return result;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return [];
        }
    }

    /// <summary>
    /// Every file reachable from a descriptor (m3u → cue → bin), the descriptor itself excluded,
    /// plus whether a required file is missing. Depth-limited, cycle-safe.
    /// </summary>
    public static (IReadOnlyList<string> Files, bool AnyRequiredMissing) Expand(string descriptorPath)
    {
        var files = new List<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Path.GetFullPath(descriptorPath) };
        var missing = false;
        Walk(descriptorPath, 0);
        return (files, missing);

        void Walk(string path, int depth)
        {
            if (depth > 4) return;
            var refs = References(path);
            if (depth == 0 && refs.Count == 0 && Path.GetExtension(path).ToLowerInvariant() is ".m3u" or ".cue" or ".gdi")
                missing = true; // descriptor without any entry cannot start anything
            foreach (var r in refs)
            {
                if (!visited.Add(r.FullPath)) continue;
                files.Add(r.FullPath);
                var exists = File.Exists(r.FullPath);
                if (!exists && r.Required) missing = true;
                if (exists && ScanRules.IsPlaylistExtension(Path.GetExtension(r.FullPath)))
                    Walk(r.FullPath, depth + 1);
            }
        }
    }

    private static string? Resolve(string dir, string name)
    {
        try
        {
            name = name.Trim().Trim('"').Replace('/', Path.DirectorySeparatorChar);
            if (name.Length == 0 || name.Contains("://", StringComparison.Ordinal)) return null;
            var full = Path.GetFullPath(Path.IsPathRooted(name) ? name : Path.Combine(dir, name));
            if (!File.Exists(full))
            {
                // Many cue sheets carry a stale path ("C:\rips\x.bin") or a different case: try the bare name next to the descriptor.
                var bare = Path.Combine(dir, Path.GetFileName(name));
                if (File.Exists(bare)) return Path.GetFullPath(bare);
            }
            return full;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static List<string>? ReadLines(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length > MaxDescriptorBytes) return null;
        var bytes = File.ReadAllBytes(path);
        // Descriptors are ASCII/UTF-8 in practice; Latin-1 fallback keeps odd bytes from breaking the parse.
        string text;
        try { text = new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException) { text = Encoding.Latin1.GetString(bytes); }
        return text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).ToList();
    }
}
