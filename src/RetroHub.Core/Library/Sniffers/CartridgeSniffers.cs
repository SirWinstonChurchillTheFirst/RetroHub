using System.Buffers.Binary;
using static RetroHub.Core.Library.Sniffers.SnifferUtil;

namespace RetroHub.Core.Library.Sniffers;

/// <summary>iNES / NES 2.0: "NES\x1A" magic. Broken when the file is shorter than the PRG+CHR sizes in the header.</summary>
public sealed class NesSniffer : RomSnifferBase
{
    public override string Id => "nes";
    public override int HeaderBytes => 16;

    protected override SniffResult? SniffCore(string path, ReadOnlySpan<byte> h)
    {
        if (h.Length < 16 || !At(h, 0, "NES\x1A"u8)) return null;
        var nes2 = (h[7] & 0x0C) == 0x08;
        long prg, chr;
        if (nes2)
        {
            prg = Size(h[4], h[9] & 0x0F, 16384);
            chr = Size(h[5], h[9] >> 4, 8192);
        }
        else
        {
            prg = h[4] * 16384L;
            chr = h[5] * 8192L;
        }
        var trainer = (h[6] & 0x04) != 0 ? 512 : 0;
        var len = FileLength(path);
        var broken = prg > 0 && len >= 0 && len < 16 + trainer + prg + chr;
        return new SniffResult("nes", Broken: broken);

        static long Size(byte lsb, int msb, long unit) =>
            msb == 0x0F ? (1L << Math.Min(lsb >> 2, 40)) * ((lsb & 3) * 2 + 1) : ((msb << 8) | lsb) * unit;
    }
}

/// <summary>
/// SNES internal header at 0x7FC0 (LoROM) / 0xFFC0 (HiROM), shifted by a 512-byte copier header when the file size
/// says so. Scored: checksum+complement, map mode, printable title, ROM size byte, reset vector.
/// </summary>
public sealed class SnesSniffer : RomSnifferBase
{
    public override string Id => "snes";
    public override int HeaderBytes => 0x200 + 0x10000;

    protected override SniffResult? SniffCore(string path, ReadOnlySpan<byte> h)
    {
        var len = FileLength(path);
        if (len < 0) len = h.Length;
        var copier = len % 1024 == 512 ? 0x200 : 0;
        int best = -1, bestBase = -1;
        foreach (var (offset, hi) in new[] { (0x7FC0, false), (0xFFC0, true) })
        {
            var b = offset + copier;
            if (b + 0x40 > h.Length) continue;
            var s = Score(h, b, hi);
            if (s > best) { best = s; bestBase = b; }
        }
        if (bestBase < 0 || best < 6) return null;

        var title = Ascii(h, bestBase, 21);
        string? serial = null;
        if (h[bestBase + 0x1A] == 0x33) serial = Code(h, bestBase - 0x10 + 2, 4); // extended header game code
        var region = h[bestBase + 0x19] switch
        {
            0x00 => "Japan", 0x01 => "USA", 0x02 => "Europe", 0x03 => "Sweden", 0x06 => "France",
            0x07 => "Netherlands", 0x08 => "Spain", 0x09 => "Germany", 0x0A => "Italy", 0x0B => "China",
            0x0D => "Korea", 0x10 => "Brazil", 0x11 => "Australia",
            _ => (string?)null,
        };
        return new SniffResult("snes", title, serial, region);
    }

    private static int Score(ReadOnlySpan<byte> h, int b, bool hi)
    {
        // The title must be plain printable ASCII (Japanese titles use JIS X 0201 katakana 0xA1–0xDF, allowed too).
        for (var i = 0; i < 21; i++)
        {
            var c = h[b + i];
            if (c is not ((>= 0x20 and < 0x7F) or (>= 0xA1 and <= 0xDF))) return 0;
        }
        var s = 2;
        var complement = BinaryPrimitives.ReadUInt16LittleEndian(h[(b + 0x1C)..]);
        var checksum = BinaryPrimitives.ReadUInt16LittleEndian(h[(b + 0x1E)..]);
        if ((complement ^ checksum) == 0xFFFF) s += 4;
        var map = h[b + 0x15];
        if ((map & 0xE0) == 0x20 && ((map & 0x01) == (hi ? 1 : 0) || (map & 0x0F) == 0x05)) s += 2;
        if (h[b + 0x17] is >= 0x07 and <= 0x0D) s += 1;
        if (BinaryPrimitives.ReadUInt16LittleEndian(h[(b + 0x3C)..]) >= 0x8000) s += 1;
        return s;
    }
}

/// <summary>N64 in all three byte orders: .z64 (big endian), .v64 (16-bit swapped), .n64 (32-bit little endian).</summary>
public sealed class N64Sniffer : RomSnifferBase
{
    public override string Id => "n64";
    public override int HeaderBytes => 0x40;

    protected override SniffResult? SniffCore(string path, ReadOnlySpan<byte> h)
    {
        if (h.Length < 0x40) return null;
        var n = h[..0x40].ToArray();
        switch (BinaryPrimitives.ReadUInt32BigEndian(n))
        {
            case 0x80371240: break;
            case 0x37804012:
                for (var i = 0; i + 1 < n.Length; i += 2) (n[i], n[i + 1]) = (n[i + 1], n[i]);
                break;
            case 0x40123780:
                for (var i = 0; i + 3 < n.Length; i += 4)
                {
                    (n[i], n[i + 3]) = (n[i + 3], n[i]);
                    (n[i + 1], n[i + 2]) = (n[i + 2], n[i + 1]);
                }
                break;
            default: return null;
        }
        var serial = Code(n, 0x3B, 4);
        var region = (char)n[0x3E] switch
        {
            'E' => "USA", 'J' => "Japan", 'P' or 'X' or 'Y' => "Europe", 'D' => "Germany", 'F' => "France",
            'I' => "Italy", 'S' => "Spain", 'U' => "Australia", _ => null,
        };
        var len = FileLength(path);
        // Boot code (IPL3) ends at 0x1000; anything shorter is a truncated dump.
        return new SniffResult("n64", Ascii(n, 0x20, 20), serial, region, Broken: len >= 0 && len < 0x1000);
    }
}

/// <summary>Shared Game Boy header checks (Nintendo logo at 0x104).</summary>
internal static class GameBoyHeader
{
    public static ReadOnlySpan<byte> Logo =>
    [
        0xCE, 0xED, 0x66, 0x66, 0xCC, 0x0D, 0x00, 0x0B, 0x03, 0x73, 0x00, 0x83, 0x00, 0x0C, 0x00, 0x0D,
        0x00, 0x08, 0x11, 0x1F, 0x88, 0x89, 0x00, 0x0E, 0xDC, 0xCC, 0x6E, 0xE6, 0xDD, 0xDD, 0xD9, 0x99,
        0xBB, 0xBB, 0x67, 0x63, 0x6E, 0x0E, 0xEC, 0xCC, 0xDD, 0xDC, 0x99, 0x9F, 0xBB, 0xB9, 0x33, 0x3E,
    ];

    /// <summary>Null when the header is not a Game Boy header; otherwise the result for <paramref name="systemId"/>.</summary>
    public static SniffResult? Read(string path, ReadOnlySpan<byte> h, string systemId)
    {
        if (h.Length < 0x150) return null;
        // CGB boot ROM checks only the first half of the logo; DMG checks all of it. Accept the CGB rule.
        if (!h.Slice(0x104, 24).SequenceEqual(Logo[..24])) return null;
        var cgb = h[0x143];
        var title = Ascii(h, 0x134, (cgb & 0x80) != 0 ? 15 : 16);
        var region = h[0x14A] == 0x00 ? "Japan" : null;
        var sizeCode = h[0x148];
        var len = FileLength(path);
        var broken = sizeCode <= 8 && len >= 0 && len < (32768L << sizeCode);
        return new SniffResult(systemId, title, null, region, broken);
    }
}

/// <summary>Game Boy: any valid header. Reports "gbc" for CGB-only carts (flag 0xC0), else "gb".</summary>
public sealed class GameBoySniffer : RomSnifferBase
{
    public override string Id => "gb";
    public override int HeaderBytes => 0x150;

    protected override SniffResult? SniffCore(string path, ReadOnlySpan<byte> h)
    {
        if (h.Length < 0x150) return null;
        return GameBoyHeader.Read(path, h, h[0x143] == 0xC0 ? "gbc" : "gb");
    }
}

/// <summary>Game Boy Color: valid header with CGB flag 0x80 (dual mode) or 0xC0 (CGB only).</summary>
public sealed class GameBoyColorSniffer : RomSnifferBase
{
    public override string Id => "gbc";
    public override int HeaderBytes => 0x150;

    protected override SniffResult? SniffCore(string path, ReadOnlySpan<byte> h)
    {
        if (h.Length < 0x150 || h[0x143] is not (0x80 or 0xC0)) return null;
        return GameBoyHeader.Read(path, h, "gbc");
    }
}

/// <summary>GBA: fixed value 0x96 at 0xB2 and the start of the Nintendo logo at 0x04.</summary>
public sealed class GbaSniffer : RomSnifferBase
{
    private static ReadOnlySpan<byte> LogoStart => [0x24, 0xFF, 0xAE, 0x51, 0x69, 0x9A, 0xA2, 0x21];
    public override string Id => "gba";
    public override int HeaderBytes => 0xC0;

    protected override SniffResult? SniffCore(string path, ReadOnlySpan<byte> h)
    {
        if (h.Length < 0xC0 || h[0xB2] != 0x96 || !At(h, 0x04, LogoStart)) return null;
        var code = Code(h, 0xAC, 4);
        return new SniffResult("gba", Ascii(h, 0xA0, 12), code, code == null ? null : RegionFromCodeLetter(code[3]));
    }
}

/// <summary>
/// Nintendo DS: logo CRC 0xCF56 at 0x15C, or a matching header CRC-16 at 0x15E (homebrew without logo).
/// Broken when the file is shorter than the "used ROM size" at 0x80 (truncated dump).
/// </summary>
public sealed class NdsSniffer : RomSnifferBase
{
    public override string Id => "nds";
    public override int HeaderBytes => 0x200;

    protected override SniffResult? SniffCore(string path, ReadOnlySpan<byte> h)
    {
        if (h.Length < 0x160) return null;
        var logoCrc = BinaryPrimitives.ReadUInt16LittleEndian(h[0x15C..]);
        var headerCrc = BinaryPrimitives.ReadUInt16LittleEndian(h[0x15E..]);
        if (logoCrc != 0xCF56 && Crc16(h[..0x15E]) != headerCrc) return null;
        if (h[0x12] is not (0 or 2 or 3)) return null; // unit code: DS, DSi-enhanced, DSi-only
        var code = Code(h, 0x0C, 4);
        var used = BinaryPrimitives.ReadUInt32LittleEndian(h[0x80..]);
        var len = FileLength(path);
        var broken = used is >= 0x200 and <= 0x40000000 && len >= 0 && len < used;
        return new SniffResult("nds", Ascii(h, 0x00, 12), code, code == null ? null : RegionFromCodeLetter(code[3]), broken);
    }

    /// <summary>CRC-16/MODBUS (poly 0xA001 reflected, init 0xFFFF) as used by the DS header.</summary>
    internal static ushort Crc16(ReadOnlySpan<byte> data)
    {
        ushort crc = 0xFFFF;
        foreach (var b in data)
        {
            crc ^= b;
            for (var i = 0; i < 8; i++) crc = (ushort)((crc & 1) != 0 ? (crc >> 1) ^ 0xA001 : crc >> 1);
        }
        return crc;
    }
}

/// <summary>
/// Nintendo 3DS cartridge image (.3ds/.cci: "NCSD" at 0x100, game partition = NCCH at offset [0x120]*0x200)
/// or a bare NCCH (.cxi: "NCCH" at 0x100). Ported from MonHub's Rom3ds.Read.
/// </summary>
public sealed class N3dsSniffer : RomSnifferBase
{
    public override string Id => "3ds";
    public override int HeaderBytes => 0x200;

    protected override SniffResult? SniffCore(string path, ReadOnlySpan<byte> h)
    {
        if (h.Length < 0x200) return null;
        ReadOnlySpan<byte> ncch = h;
        if (At(h, 0x100, "NCSD"u8))
        {
            var offset = (long)BinaryPrimitives.ReadUInt32LittleEndian(h[0x120..]) * 0x200;
            var part = ReadAt(path, offset, 0x200);
            if (part == null) return new SniffResult("3ds", Broken: true); // partition table points past the end
            ncch = part;
        }
        if (!At(ncch, 0x100, "NCCH"u8)) return null;
        var product = Ascii(ncch, 0x150, 16); // "CTR-P-EKJA"
        string? region = null;
        if (product is { Length: >= 10 } && product.StartsWith("CTR-", StringComparison.Ordinal))
            region = product[9] == 'A' ? "World" : RegionFromCodeLetter(product[9]);
        return new SniffResult("3ds", null, product, region);
    }
}

/// <summary>Sega Mega Drive / Genesis: "SEGA" at 0x100 (or 0x101). Also de-interleaves .smd (Super Magic Drive) dumps.</summary>
public sealed class MegaDriveSniffer : RomSnifferBase
{
    public override string Id => "megadrive";
    public override int HeaderBytes => 0x200 + 0x4000;

    protected override SniffResult? SniffCore(string path, ReadOnlySpan<byte> h)
    {
        ReadOnlySpan<byte> rom = h;
        if (h.Length >= 0x4200 && h[8] == 0xAA && h[9] == 0xBB)
        {
            // SMD: 512-byte header, then 16 KB blocks: first 8 KB = odd bytes, second 8 KB = even bytes.
            var plain = new byte[0x4000];
            for (var i = 0; i < 0x2000; i++)
            {
                plain[i * 2] = h[0x200 + 0x2000 + i];
                plain[i * 2 + 1] = h[0x200 + i];
            }
            rom = plain;
        }
        if (rom.Length < 0x200 || !(At(rom, 0x100, "SEGA"u8) || At(rom, 0x101, "SEGA"u8))) return null;
        var title = Ascii(rom, 0x150, 48) ?? Ascii(rom, 0x120, 48);
        var serial = Ascii(rom, 0x180, 14);
        var r = Ascii(rom, 0x1F0, 3) ?? "";
        var regions = new List<string>();
        if (r.Contains('U')) regions.Add("USA");
        if (r.Contains('E')) regions.Add("Europe");
        if (r.Contains('J')) regions.Add("Japan");
        var region = regions.Count == 3 ? "World" : regions.Count == 0 ? null : string.Join(", ", regions);
        return new SniffResult("megadrive", title, serial, region);
    }
}

/// <summary>Sega Master System / Game Gear: "TMR SEGA" at 0x7FF0, 0x3FF0 or 0x1FF0. Region nibble 5–7 = Game Gear.</summary>
public sealed class SmsSniffer : RomSnifferBase
{
    public override string Id => "sms";
    public override int HeaderBytes => 0x8000;

    protected override SniffResult? SniffCore(string path, ReadOnlySpan<byte> h)
    {
        foreach (var off in new[] { 0x7FF0, 0x3FF0, 0x1FF0 })
        {
            if (off + 16 > h.Length || !At(h, off, "TMR SEGA"u8)) continue;
            var nibble = h[off + 0x0F] >> 4;
            var system = nibble is 5 or 6 or 7 ? "gamegear" : "sms";
            var region = nibble is 3 or 5 ? "Japan" : null;
            return new SniffResult(system, Region: region);
        }
        return null;
    }
}
