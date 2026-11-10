using System.Text.RegularExpressions;

namespace RetroHub.Core.Library;

/// <summary>File/folder rules shared by the scanner, the folder watcher and drag &amp; drop.</summary>
public static partial class ScanRules
{
    /// <summary>Folder names that never contain games (case-insensitive).</summary>
    public static readonly IReadOnlySet<string> SkippedFolderNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "bios", "saves", "savestates", "states", "media", "images", "videos", "manuals", "manual",
        "downloaded_media", "screenshots", "snaps", "covers", "boxart", "marquees",
        "$RECYCLE.BIN", "System Volume Information", "__MACOSX",
        // update/DLC packages next to a game (e.g. Switch .nsp updates) are not separate games
        "updates", "update", "dlc",
    };

    /// <summary>Extensions that are never games and are skipped without a report (readmes, artwork, saves, configs …).</summary>
    public static readonly IReadOnlySet<string> IgnorableExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        // documents (".md" is NOT here: it is the Mega Drive ROM extension)
        ".txt", ".nfo", ".diz", ".pdf", ".rtf", ".doc", ".docx", ".htm", ".html",
        // images / video / audio
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".tif", ".tiff", ".ico", ".svg",
        ".mp4", ".mkv", ".avi", ".webm", ".mov", ".mp3", ".ogg", ".wav", ".flac", ".m4a",
        // saves / states / memory cards
        ".srm", ".sav", ".eep", ".sra", ".fla", ".mpk", ".dsv", ".rtc", ".mcr", ".state", ".auto", ".oops",
        // configs / databases / logs / checksums
        ".cfg", ".ini", ".xml", ".json", ".yml", ".yaml", ".toml", ".db", ".dat", ".log", ".sfv", ".md5",
        ".sha1", ".crc", ".lpl", ".opt", ".cht", ".pal", ".bak",
        // disc/title keys next to a dump (Wii U ".key" beside the ".wux"/".wud")
        ".key",
        // patches (applied by the emulator / user, not games themselves)
        ".ips", ".bps", ".ups", ".xdelta", ".ppf",
        // temp / partial downloads
        ".tmp", ".part", ".crdownload", ".partial", SafeFileTempExtension,
    };

    private const string SafeFileTempExtension = ".retrohub-tmp";

    /// <summary>Ignorable extension (also ".state1"…".state9", ".state.auto", ".st0"…".st9" savestates).</summary>
    public static bool IsIgnorableExtension(string extension)
    {
        if (string.IsNullOrEmpty(extension)) return false;
        if (IgnorableExtensions.Contains(extension)) return true;
        var e = extension.ToLowerInvariant();
        if (e.StartsWith(".state", StringComparison.Ordinal)) return true;
        if (e.Length == 4 && e.StartsWith(".st", StringComparison.Ordinal) && char.IsAsciiDigit(e[3])) return true;
        return false;
    }

    /// <summary>Incomplete downloads / atomic-write temp files the watcher and scanner must ignore.</summary>
    public static bool IsTempFile(string path)
    {
        var name = Path.GetFileName(path);
        if (name.Length == 0) return false;
        return name.EndsWith(".part", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith(".crdownload", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith(".partial", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith(SafeFileTempExtension, StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("~$", StringComparison.Ordinal);
    }

    /// <summary>A folder the scanner never enters (skip list, dot-folders like ".git").</summary>
    public static bool IsSkippedFolder(string folderName) =>
        SkippedFolderNames.Contains(folderName) || folderName.StartsWith('.');

    /// <summary>
    /// Optical disc images and disc containers. They are never hashed (CRC32 of a disc image is not what metadata
    /// databases list, and they are big). ".bin" is not here: it is also a cartridge ROM extension (Mega Drive, Atari).
    /// </summary>
    public static bool IsDiscImageExtension(string extension) =>
        extension.ToLowerInvariant() is ".iso" or ".chd" or ".cso" or ".zso" or ".rvz" or ".wia" or ".wbfs" or ".gcz"
            or ".gcm" or ".ciso" or ".cdi" or ".img" or ".mdf" or ".nrg" or ".pbp" or ".ecm" or ".dax" or ".nsp" or ".xci";

    /// <summary>
    /// For a file symbolic link, the final target (a <see cref="FileInfo"/> of the link itself reports length 0 and the
    /// link's own time stamps). Anything else, or a link that cannot be resolved, is returned unchanged.
    /// </summary>
    internal static FileInfo FollowLink(FileInfo file)
    {
        try
        {
            if (file.LinkTarget == null) return file;
            return file.ResolveLinkTarget(returnFinalTarget: true) as FileInfo ?? file;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return file;
        }
    }

    /// <summary>Multi-file descriptors the scanner understands (their referenced files are hidden).</summary>
    public static bool IsPlaylistExtension(string extension) =>
        extension.ToLowerInvariant() is ".m3u" or ".cue" or ".gdi" or ".ccd" or ".mds";

    [GeneratedRegex(@"\[(01[0-9A-Fa-f]{14})\]")]
    private static partial Regex SwitchTitleId();

    [GeneratedRegex(@"(?i)\b(update|upd)\b[\s_-]*v?\d|\bDLC\b\s*\d|[-–]\s*DLC\b|[(\[]DLC[)\]]")]
    private static partial Regex SwitchAddOnWords();

    /// <summary>
    /// A Switch update or DLC package lying loose next to the games (not in an "Updates"/"DLC" folder, which is skipped
    /// anyway): a title-ID tag whose last three hex digits are not "000" (base games end in 000, updates in 800, DLC in
    /// other values – plain file-name arithmetic, no keys), or "Update v1.4.1" / "DLC" in the name. Only for
    /// .nsp/.nsz/.xci/.xcz; such files are not listed as games of their own.
    /// </summary>
    public static bool IsSwitchAddOn(string fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return false;
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        if (ext is not (".nsp" or ".nsz" or ".xci" or ".xcz")) return false;
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var id = SwitchTitleId().Match(stem);
        if (id.Success) return !id.Groups[1].Value.EndsWith("000", StringComparison.Ordinal);
        return SwitchAddOnWords().IsMatch(stem);
    }
}
