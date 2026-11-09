using System.Buffers.Binary;
using RetroHub.Core.Logging;

namespace RetroHub.Core.Metadata;

/// <summary>
/// Pixel size of an image from its header alone – no decode (DESIGN 11.4: the library stores cover sizes so a system's
/// slot format is known before a grid is built and rows never jump). PNG (IHDR), JPEG (first SOFn segment), WebP
/// (VP8, VP8L, VP8X), GIF and BMP. Reads at most <see cref="MaxScan"/> bytes; anything unreadable → null.
/// </summary>
public static class ImageHeader
{
    /// <summary>JPEG files may carry large EXIF/ICC segments before the frame header: stop searching after this.</summary>
    public const int MaxScan = 1 << 20;

    /// <summary>Plausible pixel range (rejects garbage headers).</summary>
    private const int MaxSide = 1 << 16;

    public static (int Width, int Height)? TryRead(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.SequentialScan);
            return TryRead(fs);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Log.Debug($"Image header of '{path}' not readable: {ex.Message}");
            return null;
        }
    }

    public static (int Width, int Height)? TryRead(Stream stream)
    {
        Span<byte> head = stackalloc byte[32];
        var n = ReadFully(stream, head);
        if (n < 12) return null;
        head = head[..n];
        (int, int)? size = null;
        if (n >= 24 && head[0] == 0x89 && head[1] == 'P' && head[2] == 'N' && head[3] == 'G' && head[12] == 'I' && head[13] == 'H' && head[14] == 'D' && head[15] == 'R')
            size = (BinaryPrimitives.ReadInt32BigEndian(head[16..]), BinaryPrimitives.ReadInt32BigEndian(head[20..]));
        else if (head[0] == 0xFF && head[1] == 0xD8) size = Jpeg(stream, head);
        else if (n >= 30 && head[..4].SequenceEqual("RIFF"u8) && head[8..12].SequenceEqual("WEBP"u8)) size = WebP(head);
        else if (head[..3].SequenceEqual("GIF"u8)) size = (BinaryPrimitives.ReadUInt16LittleEndian(head[6..]), BinaryPrimitives.ReadUInt16LittleEndian(head[8..]));
        else if (n >= 26 && head[0] == 'B' && head[1] == 'M')
            size = (BinaryPrimitives.ReadInt32LittleEndian(head[18..]), Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(head[22..])));
        return size is { } s && s.Item1 is > 0 and <= MaxSide && s.Item2 is > 0 and <= MaxSide ? s : null;
    }

    private static int ReadFully(Stream s, Span<byte> buffer)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var r = s.Read(buffer[total..]);
            if (r <= 0) break;
            total += r;
        }
        return total;
    }

    private static (int, int)? WebP(ReadOnlySpan<byte> h)
    {
        var chunk = h[12..16];
        if (chunk.SequenceEqual("VP8 "u8))
        {
            // lossy: frame tag (3), start code 9D 01 2A, then 14-bit width/height
            if (h[23] != 0x9D || h[24] != 0x01 || h[25] != 0x2A) return null;
            return (BinaryPrimitives.ReadUInt16LittleEndian(h[26..]) & 0x3FFF, BinaryPrimitives.ReadUInt16LittleEndian(h[28..]) & 0x3FFF);
        }
        if (chunk.SequenceEqual("VP8L"u8))
        {
            if (h[20] != 0x2F) return null;
            var bits = BinaryPrimitives.ReadUInt32LittleEndian(h[21..]);
            return ((int)(bits & 0x3FFF) + 1, (int)((bits >> 14) & 0x3FFF) + 1);
        }
        if (chunk.SequenceEqual("VP8X"u8))
        {
            var w = 1 + (h[24] | h[25] << 8 | h[26] << 16);
            var hh = 1 + (h[27] | h[28] << 8 | h[29] << 16);
            return (w, hh);
        }
        return null;
    }

    /// <summary>Walks the JPEG segments (the 32 bytes already read first) to the first frame header SOF0–SOF15 (not DHT/JPG/DAC).</summary>
    private static (int, int)? Jpeg(Stream stream, ReadOnlySpan<byte> already)
    {
        var buffer = new byte[already.Length];
        already.CopyTo(buffer);
        var reader = new ByteReader(stream, buffer);
        reader.Skip(2); // SOI
        while (reader.Position < MaxScan)
        {
            var b = reader.Next();
            if (b < 0) return null;
            if (b != 0xFF) continue; // tolerate padding/garbage between segments
            int marker;
            do { marker = reader.Next(); } while (marker == 0xFF);
            if (marker < 0) return null;
            if (marker is 0xD8 or 0x01 || marker is >= 0xD0 and <= 0xD7) continue; // stand-alone markers
            if (marker == 0xD9 || marker == 0xDA) return null; // end of image / start of scan before any frame header
            var hi = reader.Next();
            var lo = reader.Next();
            if (hi < 0 || lo < 0) return null;
            var length = hi << 8 | lo;
            if (length < 2) return null;
            if (marker is >= 0xC0 and <= 0xCF && marker is not (0xC4 or 0xC8 or 0xCC))
            {
                reader.Next(); // precision
                int h1 = reader.Next(), h2 = reader.Next(), w1 = reader.Next(), w2 = reader.Next();
                if ((h1 | h2 | w1 | w2) < 0) return null;
                return (w1 << 8 | w2, h1 << 8 | h2);
            }
            reader.Skip(length - 2);
        }
        return null;
    }

    /// <summary>Byte-wise reader over the already-read head followed by the stream (skips by seeking when possible).</summary>
    private sealed class ByteReader(Stream stream, byte[] head)
    {
        private int _headPos;
        private readonly byte[] _one = new byte[1];
        public long Position { get; private set; }

        public int Next()
        {
            Position++;
            if (_headPos < head.Length) return head[_headPos++];
            return stream.Read(_one, 0, 1) == 1 ? _one[0] : -1;
        }

        public void Skip(int count)
        {
            while (count > 0 && _headPos < head.Length) { _headPos++; count--; Position++; }
            if (count <= 0) return;
            Position += count;
            if (stream.CanSeek) stream.Seek(count, SeekOrigin.Current);
            else for (var i = 0; i < count && stream.ReadByte() >= 0; i++) { }
        }
    }
}
