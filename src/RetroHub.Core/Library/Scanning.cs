namespace RetroHub.Core.Library;

/// <summary>Result of a header check on a file.</summary>
public sealed record SniffResult(string SystemId, string? Title = null, string? Serial = null, string? Region = null, bool Broken = false);

/// <summary>
/// Identifies a system from file contents (header magic). Implementations must only read the bytes they need,
/// never throw for short/odd files (return null), and be thread-safe.
/// </summary>
public interface IRomSniffer
{
    /// <summary>Id referenced from SystemDef.Sniffers (e.g. "gba", "nes", "iso9660").</summary>
    string Id { get; }
    /// <summary>Bytes needed from the file start (the scanner passes at most this many; may be fewer for small files).</summary>
    int HeaderBytes { get; }
    /// <param name="path">Full path (for sniffers that need to seek further, e.g. ISO9660 at 0x8000).</param>
    /// <param name="header">First min(HeaderBytes, length) bytes.</param>
    SniffResult? Sniff(string path, ReadOnlySpan<byte> header);
}

public sealed record ScanOptions
{
    /// <summary>Only these folders (LibraryFolder ids); null = all enabled folders.</summary>
    public IReadOnlyList<long>? FolderIds { get; init; }
    /// <summary>Compute CRC32 for new/changed files (slower; used for duplicate detection + metadata).</summary>
    public bool ComputeHashes { get; init; } = true;
    /// <summary>Re-check files even when size+mtime are unchanged.</summary>
    public bool Full { get; init; }
}

public sealed record ScanProgress(string Phase, int FilesSeen, int FilesTotal, string? CurrentFile);

public sealed record ScanIssue(string Path, string Reason);

public sealed record ScanReport
{
    public int Added { get; init; }
    public int Updated { get; init; }
    public int Unchanged { get; init; }
    /// <summary>Known games whose file was not found (marked Missing, not deleted).</summary>
    public int Missing { get; init; }
    public IReadOnlyList<ScanIssue> Duplicates { get; init; } = [];
    /// <summary>Files with an unknown extension or an ambiguous system that could not be resolved.</summary>
    public IReadOnlyList<ScanIssue> Unknown { get; init; } = [];
    public IReadOnlyList<ScanIssue> Errors { get; init; } = [];
    public TimeSpan Duration { get; init; }
}

/// <summary>
/// Walks library folders, detects systems, updates the repository. Runs entirely off the UI thread.
/// Never modifies or deletes game files.
/// </summary>
public interface ILibraryScanner
{
    Task<ScanReport> ScanAsync(ScanOptions options, IProgress<ScanProgress>? progress, CancellationToken ct);
    /// <summary>Detects the system of a single file (used for drag &amp; drop / manual add). Null = unknown or ambiguous.</summary>
    string? DetectSystem(string fullPath, string? folderHint = null);
    /// <summary>True while a scan is running.</summary>
    bool IsScanning { get; }
}
