using System.Text;

namespace RetroHub.Core.Library.Sniffers;

/// <summary>
/// Base class: wraps <see cref="SniffCore"/> so a sniffer never throws, whatever the file contains.
/// Sniffers are stateless and therefore thread-safe.
/// </summary>
public abstract class RomSnifferBase : IRomSniffer
{
    public abstract string Id { get; }
    public abstract int HeaderBytes { get; }

    public SniffResult? Sniff(string path, ReadOnlySpan<byte> header)
    {
        try { return SniffCore(path, header); }
        catch (Exception) { return null; } // odd/short/unreadable file: "not mine"
    }

    protected abstract SniffResult? SniffCore(string path, ReadOnlySpan<byte> header);
}

internal static class SnifferUtil
{
    /// <summary>Printable ASCII from a fixed-size header field (stops at NUL, other control bytes become spaces); null if empty.</summary>
    public static string? Ascii(ReadOnlySpan<byte> data, int offset, int length)
    {
        if (offset < 0 || offset >= data.Length) return null;
        length = Math.Min(length, data.Length - offset);
        var sb = new StringBuilder(length);
        foreach (var b in data.Slice(offset, length))
        {
            if (b == 0) break;
            sb.Append(b is >= 0x20 and < 0x7F ? (char)b : ' ');
        }
        return TitleParser.CleanHeaderTitle(sb.ToString());
    }

    /// <summary>A product/game code made only of A–Z/0–9 (rejects blank or garbage codes in homebrew headers).</summary>
    public static string? Code(ReadOnlySpan<byte> data, int offset, int length)
    {
        if (offset < 0 || offset + length > data.Length) return null;
        var chars = new char[length];
        for (var i = 0; i < length; i++)
        {
            var b = data[offset + i];
            if (b is not ((>= (byte)'A' and <= (byte)'Z') or (>= (byte)'0' and <= (byte)'9'))) return null;
            chars[i] = (char)b;
        }
        var s = new string(chars);
        return s.All(c => c == '0') ? null : s;
    }

    /// <summary>Region from the last letter of a Nintendo-style game code ("BPEE" → USA).</summary>
    public static string? RegionFromCodeLetter(char c) => c switch
    {
        'E' => "USA",
        'J' => "Japan",
        'P' or 'X' or 'Y' or 'Z' or 'V' => "Europe",
        'D' => "Germany",
        'F' => "France",
        'S' => "Spain",
        'I' => "Italy",
        'H' => "Netherlands",
        'U' => "Australia",
        'K' => "Korea",
        'C' => "China",
        _ => null,
    };

    public static long FileLength(string path)
    {
        try { return new FileInfo(path).Length; }
        catch (Exception) { return -1; }
    }

    /// <summary>Reads exactly <paramref name="count"/> bytes at <paramref name="offset"/>; null when the file is shorter or unreadable.</summary>
    public static byte[]? ReadAt(string path, long offset, int count)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1);
            if (offset < 0 || offset + count > fs.Length) return null;
            fs.Position = offset;
            var buf = new byte[count];
            fs.ReadExactly(buf);
            return buf;
        }
        catch (Exception) { return null; }
    }

    public static bool At(ReadOnlySpan<byte> data, int offset, ReadOnlySpan<byte> magic) =>
        offset >= 0 && offset + magic.Length <= data.Length && data.Slice(offset, magic.Length).SequenceEqual(magic);
}
