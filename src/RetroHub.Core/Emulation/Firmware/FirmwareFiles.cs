using System.Text;
using System.Text.RegularExpressions;
using RetroHub.Core.Emulation.Portable;

namespace RetroHub.Core.Emulation.Firmware;

/// <summary>What a small key/text file is, judged by its content (never by printing it).</summary>
public enum KeyFileKind { None, SwitchProdKeys, SwitchTitleKeys, WiiUKeys }

/// <summary>
/// Content checks for the player's own key and BIOS files. Nothing here logs or returns key material – only the
/// kind of file and counts.
/// </summary>
public static partial class FirmwareFiles
{
    /// <summary>Key and text files are small; bigger files are never read as text.</summary>
    public const long MaxKeyFileSize = 1024 * 1024;

    /// <summary>Files up to this size are hashed (MD5) to recognise BIOS dumps under any name.</summary>
    public const long MaxHashSize = 8 * 1024 * 1024;

    /// <summary>A Switch firmware has a few hundred .nca files; fewer than this in one folder is not a firmware.</summary>
    public const int MinFirmwareFiles = 10;

    [GeneratedRegex(@"^\s*([A-Za-z0-9_]+)\s*=\s*([0-9A-Fa-f]+)\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyValueLine();

    /// <summary>A system title of a Switch firmware: "&lt;32 hex&gt;.nca" or "&lt;32 hex&gt;.cnmt.nca".</summary>
    [GeneratedRegex(@"^[0-9A-Fa-f]{32}(\.cnmt)?\.nca$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex NcaName();

    public static bool IsNcaName(string fileName) => NcaName().IsMatch(fileName);

    /// <summary>
    /// prod.keys: "name = hex" lines with Switch key names (header_key, master_key_xx, key_area_key_…, titlekek_xx);
    /// title.keys: "&lt;32 hex rights id&gt; = &lt;32 hex key&gt;" lines; Cemu keys.txt: lines of 32 hex digits ("#" comments).
    /// Blank lines and ";"/"#" comments are ignored; at most one line in ten may be something else.
    /// </summary>
    public static KeyFileKind Classify(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length == 0 || bytes.Length > MaxKeyFileSize || bytes.IndexOf((byte)0) >= 0) return KeyFileKind.None;
        var text = Encoding.UTF8.GetString(bytes).TrimStart('﻿');
        int pairs = 0, other = 0, titleLike = 0;
        bool prodNames = false;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#') || line.StartsWith("//", StringComparison.Ordinal)) continue;
            var m = KeyValueLine().Match(line);
            if (!m.Success) { other++; continue; }
            pairs++;
            var name = m.Groups[1].Value;
            var value = m.Groups[2].Value;
            if (name.Length == 32 && name.All(Uri.IsHexDigit) && value.Length == 32) titleLike++;
            if (name.Equals("header_key", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("master_key_", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("key_area_key_", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("titlekek_", StringComparison.OrdinalIgnoreCase))
                prodNames = true;
        }
        if (pairs > 0 && other * 10 <= pairs)
        {
            if (prodNames) return KeyFileKind.SwitchProdKeys;
            if (titleLike == pairs) return KeyFileKind.SwitchTitleKeys;
            return KeyFileKind.None;
        }
        if (pairs == 0 && CemuSetup.ParseKeys(text).Any()) return KeyFileKind.WiiUKeys;
        return KeyFileKind.None;
    }

    /// <summary>The kind of a key file on disk (None when missing, empty, too big or unreadable). Never throws.</summary>
    public static KeyFileKind ClassifyFile(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length == 0 || info.Length > MaxKeyFileSize) return KeyFileKind.None;
            return Classify(File.ReadAllBytes(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return KeyFileKind.None; }
    }

    /// <summary>
    /// A PS2 BIOS (ROM0) dump: 512 KiB … 8 MiB with a ROMDIR – a 16-byte entry "RESET" at a 16-byte boundary, followed
    /// by the entry "ROMDIR" (the check PCSX2 makes before it lists a file as BIOS).
    /// </summary>
    public static bool IsPs2Bios(Stream data, long length)
    {
        if (length < 512 * 1024 || length > 8 * 1024 * 1024) return false;
        var entry = new byte[32];
        var limit = Math.Min(length - 32, 512 * 1024);
        for (long pos = 0; pos <= limit; pos += 16)
        {
            if (data.CanSeek) data.Position = pos;
            if (ReadFully(data, entry, data.CanSeek ? 32 : 16) < 16) return false;
            if (Starts(entry, "RESET"))
            {
                if (!data.CanSeek && ReadFully(data, entry.AsSpan(16), 16) < 16) return false;
                return Starts(entry.AsSpan(16), "ROMDIR");
            }
        }
        return false;
    }

    public static bool IsPs2BiosFile(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return IsPs2Bios(fs, fs.Length);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    private static bool Starts(ReadOnlySpan<byte> entry, string name)
    {
        for (int i = 0; i < name.Length; i++) if (entry[i] != name[i]) return false;
        // the name field is 10 bytes, zero-padded
        return entry[name.Length] == 0;
    }

    private static int ReadFully(Stream s, Span<byte> buffer, int count)
    {
        int total = 0;
        while (total < count)
        {
            int n = s.Read(buffer[total..count]);
            if (n == 0) break;
            total += n;
        }
        return total;
    }
}
