using System.Text;

namespace RetroHub.Core.IO;

/// <summary>
/// Atomic file writes: write to "&lt;file&gt;.retrohub-tmp", flush to disk, then move over the target.
/// A crash mid-write never leaves a half-written config/database export behind.
/// (Idea taken from MonHub's Shared/SafeFile.cs, extended with flush-to-disk and a .bak copy.)
/// </summary>
public static class SafeFile
{
    public const string TempSuffix = ".retrohub-tmp";

    public static void WriteAllText(string path, string text, bool keepBackup = false) =>
        WriteAllBytes(path, new UTF8Encoding(false).GetBytes(text), keepBackup);

    public static void WriteAllBytes(string path, byte[] bytes, bool keepBackup = false)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmp = path + TempSuffix;
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            fs.Write(bytes, 0, bytes.Length);
            fs.Flush(flushToDisk: true);
        }
        if (keepBackup && File.Exists(path))
        {
            try { File.Copy(path, path + ".bak", overwrite: true); } catch (IOException) { /* best effort */ }
        }
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>Removes leftover temp files from a killed process (called once at startup).</summary>
    public static void SweepTemps(string folder)
    {
        if (!Directory.Exists(folder)) return;
        foreach (var f in Directory.EnumerateFiles(folder, "*" + TempSuffix, SearchOption.AllDirectories))
        {
            try { File.Delete(f); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>
    /// Copies a broken file aside ("&lt;file&gt;.corrupt-&lt;timestamp&gt;") before it gets replaced with defaults,
    /// so the user's data is never silently lost.
    /// </summary>
    public static string? PreserveCorrupt(string path)
    {
        if (!File.Exists(path)) return null;
        var target = $"{path}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}";
        try { File.Copy(path, target, overwrite: true); return target; } catch (IOException) { return null; }
    }
}
