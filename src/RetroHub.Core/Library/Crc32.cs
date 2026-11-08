using System.Buffers;
using System.Buffers.Binary;

namespace RetroHub.Core.Library;

/// <summary>
/// CRC-32 (IEEE 802.3, reflected polynomial 0xEDB88320, init/xorout 0xFFFFFFFF) — the checksum used by
/// No-Intro/libretro DAT files and zip archives. Table-driven "slicing-by-8" implementation (well above 1 GB/s
/// on current CPUs, so the disk is always the bottleneck). Thread-safe; no allocations except the read buffer.
/// </summary>
public static class Crc32
{
    private const uint Polynomial = 0xEDB88320u;
    private static readonly uint[] Table = BuildTable();

    private static uint[] BuildTable()
    {
        var t = new uint[8 * 256];
        for (uint i = 0; i < 256; i++)
        {
            var c = i;
            for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? (c >> 1) ^ Polynomial : c >> 1;
            t[i] = c;
        }
        for (var i = 0; i < 256; i++)
            for (var s = 1; s < 8; s++)
                t[s * 256 + i] = (t[(s - 1) * 256 + i] >> 8) ^ t[t[(s - 1) * 256 + i] & 0xFF];
        return t;
    }

    /// <summary>CRC-32 of a buffer.</summary>
    public static uint Compute(ReadOnlySpan<byte> data) => ~Append(0xFFFFFFFFu, data);

    /// <summary>
    /// Continues a running (non-finalised) CRC state. Start with 0xFFFFFFFF, feed every chunk, finish with ~state.
    /// </summary>
    public static uint Append(uint state, ReadOnlySpan<byte> data)
    {
        var t = Table;
        var crc = state;
        while (data.Length >= 8)
        {
            var lo = BinaryPrimitives.ReadUInt32LittleEndian(data) ^ crc;
            var hi = BinaryPrimitives.ReadUInt32LittleEndian(data[4..]);
            crc = t[7 * 256 + (lo & 0xFF)] ^ t[6 * 256 + ((lo >> 8) & 0xFF)] ^
                  t[5 * 256 + ((lo >> 16) & 0xFF)] ^ t[4 * 256 + (lo >> 24)] ^
                  t[3 * 256 + (hi & 0xFF)] ^ t[2 * 256 + ((hi >> 8) & 0xFF)] ^
                  t[1 * 256 + ((hi >> 16) & 0xFF)] ^ t[hi >> 24];
            data = data[8..];
        }
        foreach (var b in data) crc = (crc >> 8) ^ t[(crc ^ b) & 0xFF];
        return crc;
    }

    /// <summary>Streams a stream from its current position to the end.</summary>
    public static uint Compute(Stream stream, CancellationToken ct = default)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(1 << 20);
        try
        {
            var state = 0xFFFFFFFFu;
            int n;
            while ((n = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                ct.ThrowIfCancellationRequested();
                state = Append(state, buffer.AsSpan(0, n));
            }
            return ~state;
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }

    /// <summary>CRC-32 of a whole file (sequential read, shared read access). Throws on IO errors.</summary>
    public static uint ComputeFile(string path, CancellationToken ct = default)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
            1, FileOptions.SequentialScan);
        return Compute(fs, ct);
    }

    /// <summary>Uppercase 8-digit hex, the format stored in <see cref="Game.Crc32"/>.</summary>
    public static string ToHex(uint crc) => crc.ToString("X8");
}
