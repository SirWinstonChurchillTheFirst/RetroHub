using System.Buffers.Binary;
using System.Text;
using RetroHub.Core.Library.Sniffers;

namespace RetroHub.Core.Tests.Library;

/// <summary>Builds minimal but structurally valid ROM/disc images for sniffer and scanner tests.</summary>
internal static class RomImages
{
    private static void Put(byte[] d, int offset, string ascii, int width = -1, byte pad = 0)
    {
        var b = Encoding.ASCII.GetBytes(ascii);
        if (width < 0) width = b.Length;
        for (var i = 0; i < width; i++) d[offset + i] = i < b.Length ? b[i] : pad;
    }

    /// <summary>Deterministic filler so different "games" have different contents (CRC).</summary>
    private static void Fill(byte[] d, int from, int seed)
    {
        var r = new Random(seed);
        var tmp = new byte[d.Length - from];
        r.NextBytes(tmp);
        tmp.CopyTo(d, from);
    }

    public static byte[] Nes(int prgBanks = 2, int chrBanks = 1, bool truncated = false, int seed = 1)
    {
        var d = new byte[16 + prgBanks * 16384 + chrBanks * 8192];
        Fill(d, 16, seed);
        Put(d, 0, "NES\x1A");
        d[4] = (byte)prgBanks;
        d[5] = (byte)chrBanks;
        for (var i = 6; i < 16; i++) d[i] = 0;
        return truncated ? d[..(d.Length / 2)] : d;
    }

    public static byte[] Snes(string title, bool hiRom = false, bool copier = false, bool checksum = true, int seed = 2)
    {
        var d = new byte[0x20000];
        Fill(d, 0, seed);
        var b = hiRom ? 0xFFC0 : 0x7FC0;
        Put(d, b, title, 21, (byte)' ');
        d[b + 0x15] = (byte)(hiRom ? 0x21 : 0x20);
        d[b + 0x16] = 0x00;
        d[b + 0x17] = 0x07;
        d[b + 0x18] = 0x00;
        d[b + 0x19] = 0x01; // USA
        d[b + 0x1A] = 0x33; // extended header
        Put(d, b - 0x10 + 2, "ATBE");
        d[b + 0x1B] = 0x00;
        if (checksum)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(d.AsSpan(b + 0x1C), 0xEDCB);
            BinaryPrimitives.WriteUInt16LittleEndian(d.AsSpan(b + 0x1E), 0x1234);
        }
        else
        {
            BinaryPrimitives.WriteUInt16LittleEndian(d.AsSpan(b + 0x1C), 0x0000);
            BinaryPrimitives.WriteUInt16LittleEndian(d.AsSpan(b + 0x1E), 0x0000);
        }
        BinaryPrimitives.WriteUInt16LittleEndian(d.AsSpan(b + 0x3C), 0x8000);
        if (!copier) return d;
        var withHeader = new byte[d.Length + 512];
        d.CopyTo(withHeader, 512);
        return withHeader;
    }

    /// <summary>N64 in "z64" (big endian), "v64" (byte-swapped) or "n64" (word-swapped) order.</summary>
    public static byte[] N64(string order = "z64", string title = "TEST N64 GAME", string code = "NTEE", int seed = 3)
    {
        var d = new byte[0x2000];
        Fill(d, 0x40, seed);
        BinaryPrimitives.WriteUInt32BigEndian(d, 0x80371240);
        Put(d, 0x20, title, 20, (byte)' ');
        Put(d, 0x3B, code);
        d[0x3F] = 0;
        if (order == "v64")
            for (var i = 0; i + 1 < d.Length; i += 2) (d[i], d[i + 1]) = (d[i + 1], d[i]);
        else if (order == "n64")
            for (var i = 0; i + 3 < d.Length; i += 4) { (d[i], d[i + 3]) = (d[i + 3], d[i]); (d[i + 1], d[i + 2]) = (d[i + 2], d[i + 1]); }
        return d;
    }

    public static readonly byte[] GbLogo =
    [
        0xCE, 0xED, 0x66, 0x66, 0xCC, 0x0D, 0x00, 0x0B, 0x03, 0x73, 0x00, 0x83, 0x00, 0x0C, 0x00, 0x0D,
        0x00, 0x08, 0x11, 0x1F, 0x88, 0x89, 0x00, 0x0E, 0xDC, 0xCC, 0x6E, 0xE6, 0xDD, 0xDD, 0xD9, 0x99,
        0xBB, 0xBB, 0x67, 0x63, 0x6E, 0x0E, 0xEC, 0xCC, 0xDD, 0xDC, 0x99, 0x9F, 0xBB, 0xB9, 0x33, 0x3E,
    ];

    public static byte[] Gb(string title = "TESTGAME", byte cgb = 0x00, byte sizeCode = 0, bool truncated = false, int seed = 4)
    {
        var d = new byte[32768 << sizeCode];
        Fill(d, 0x150, seed);
        GbLogo.CopyTo(d, 0x104);
        Put(d, 0x134, title, cgb == 0 ? 16 : 15);
        d[0x143] = cgb;
        d[0x148] = sizeCode;
        d[0x14A] = 0x01;
        return truncated ? d[..(d.Length / 2)] : d;
    }

    public static byte[] Gba(string title = "TESTGBA", string code = "BTEE", int seed = 5)
    {
        var d = new byte[0x10000];
        Fill(d, 0xC0, seed);
        d[0] = 0x2E; d[1] = 0x00; d[2] = 0x00; d[3] = 0xEA;
        new byte[] { 0x24, 0xFF, 0xAE, 0x51, 0x69, 0x9A, 0xA2, 0x21 }.CopyTo(d, 4);
        Put(d, 0xA0, title, 12);
        Put(d, 0xAC, code, 4);
        Put(d, 0xB0, "01");
        d[0xB2] = 0x96;
        return d;
    }

    public static byte[] Nds(string title = "TESTNDS", string code = "ATEP", bool logoCrc = true, bool truncated = false, int seed = 6)
    {
        var d = new byte[0x4000];
        Fill(d, 0x200, seed);
        Put(d, 0x00, title, 12);
        Put(d, 0x0C, code, 4);
        Put(d, 0x10, "01");
        d[0x12] = 0;
        BinaryPrimitives.WriteUInt32LittleEndian(d.AsSpan(0x80), 0x4000);
        if (logoCrc) BinaryPrimitives.WriteUInt16LittleEndian(d.AsSpan(0x15C), 0xCF56);
        BinaryPrimitives.WriteUInt16LittleEndian(d.AsSpan(0x15E), NdsSniffer.Crc16(d.AsSpan(0, 0x15E)));
        return truncated ? d[..0x1000] : d;
    }

    public static byte[] N3dsCci(string product = "CTR-P-ATEP")
    {
        var d = new byte[0x4400];
        Put(d, 0x100, "NCSD");
        BinaryPrimitives.WriteUInt32LittleEndian(d.AsSpan(0x120), 0x4000 / 0x200);
        Put(d, 0x4000 + 0x100, "NCCH");
        Put(d, 0x4000 + 0x150, product, 16);
        d[0x4000 + 0x18F] = 0x04;
        return d;
    }

    public static byte[] N3dsCxi(string product = "CTR-P-BTEJ")
    {
        var d = new byte[0x400];
        Put(d, 0x100, "NCCH");
        Put(d, 0x150, product, 16);
        return d;
    }

    public static byte[] MegaDrive(string title = "TEST MEGA GAME", bool at101 = false, bool smd = false, int seed = 7)
    {
        var d = new byte[0x8000];
        Fill(d, 0x200, seed);
        for (var i = 0x100; i < 0x200; i++) d[i] = (byte)' ';
        Put(d, at101 ? 0x101 : 0x100, "SEGA MEGA DRIVE");
        Put(d, 0x120, title, 48, (byte)' ');
        Put(d, 0x150, title, 48, (byte)' ');
        Put(d, 0x180, "GM 00001234-00");
        Put(d, 0x1F0, "JUE");
        if (!smd) return d;
        // Interleave into SMD blocks: 512-byte header, each 16 KB block = odd bytes then even bytes.
        var s = new byte[0x200 + d.Length];
        s[8] = 0xAA; s[9] = 0xBB; s[10] = 0x06;
        for (var block = 0; block < d.Length / 0x4000; block++)
        {
            var src = block * 0x4000;
            var dst = 0x200 + block * 0x4000;
            for (var i = 0; i < 0x2000; i++)
            {
                s[dst + i] = d[src + i * 2 + 1];
                s[dst + 0x2000 + i] = d[src + i * 2];
            }
        }
        return s;
    }

    public static byte[] Sms(int offset = 0x7FF0, int regionNibble = 4, int seed = 8)
    {
        var d = new byte[0x8000];
        Fill(d, 0, seed);
        Put(d, offset, "TMR SEGA");
        d[offset + 0x0F] = (byte)((regionNibble << 4) | 0x0C);
        return d;
    }

    /// <summary>GameCube/Wii plain disc header (first 0x440 bytes) followed by filler.</summary>
    public static byte[] NintendoDisc(bool wii, string code = "GTEE", string maker = "01", string title = "Test Disc Game", int seed = 9)
    {
        var d = new byte[0x8000];
        Fill(d, 0x440, seed);
        Put(d, 0, code);
        Put(d, 4, maker);
        if (wii) BinaryPrimitives.WriteUInt32BigEndian(d.AsSpan(0x18), 0x5D1C9EA3);
        else BinaryPrimitives.WriteUInt32BigEndian(d.AsSpan(0x1C), 0xC2339F3D);
        Put(d, 0x20, title, 0x40);
        return d;
    }

    public static byte[] Wbfs(string code = "RTEP")
    {
        var disc = NintendoDisc(true, code);
        var d = new byte[0x8000];
        Put(d, 0, "WBFS");
        disc.AsSpan(0, 0x400).CopyTo(d.AsSpan(0x200));
        return d;
    }

    public static byte[] Rvz(bool wii, string code)
    {
        var disc = NintendoDisc(wii, code);
        var d = new byte[0x1000];
        Put(d, 0, "RVZ\x01");
        BinaryPrimitives.WriteUInt32BigEndian(d.AsSpan(0x48), wii ? 2u : 1u);
        disc.AsSpan(0, 0x80).CopyTo(d.AsSpan(0x58));
        return d;
    }

    public static byte[] Random(int length, int seed = 42)
    {
        var d = new byte[length];
        new Random(seed).NextBytes(d);
        return d;
    }
}

/// <summary>Writes small ISO9660 images (cooked 2048 or raw 2352-byte Mode 1/Mode 2 sectors).</summary>
internal sealed class IsoBuilder
{
    private sealed record FileNode(string Name, byte[] Data);
    private sealed record DirNode(string Name, List<FileNode> Files);

    private readonly List<FileNode> _rootFiles = [];
    private readonly List<DirNode> _dirs = [];
    public string SystemId { get; set; } = "PLAYSTATION";
    public string VolumeId { get; set; } = "TESTDISC";

    public IsoBuilder File(string name, byte[] data) { _rootFiles.Add(new FileNode(name, data)); return this; }
    public IsoBuilder File(string name, string text) => File(name, Encoding.ASCII.GetBytes(text));

    public IsoBuilder Dir(string name, params (string Name, byte[] Data)[] files)
    {
        _dirs.Add(new DirNode(name, files.Select(f => new FileNode(f.Name, f.Data)).ToList()));
        return this;
    }

    public static IsoBuilder Ps2(string boot = "SLUS_203.12") =>
        new IsoBuilder { VolumeId = "TESTPS2" }.File("SYSTEM.CNF", $"BOOT2 = cdrom0:\\{boot};1\r\nVER = 1.00\r\nVMODE = NTSC\r\n");

    public static IsoBuilder Ps1(string boot = "SLUS_007.70") =>
        new IsoBuilder { VolumeId = "TESTPS1" }.File("SYSTEM.CNF", $"BOOT = cdrom:\\{boot};1\r\nTCB = 4\r\nEVENT = 10\r\nSTACK = 801FFFF0\r\n");

    public static IsoBuilder Psp(string discId = "ULUS10041", string title = "Test PSP Game") =>
        new IsoBuilder { SystemId = "PSP GAME", VolumeId = "UMD" }.Dir("PSP_GAME", ("PARAM.SFO", ParamSfo(("DISC_ID", discId), ("TITLE", title))));

    /// <summary>Raw sector layouts: 2048 = cooked; 2352 + mode 1 or 2.</summary>
    public byte[] Build(int sectorSize = 2048, int mode = 2)
    {
        var sectors = new List<byte[]>();
        for (var i = 0; i < 16; i++) sectors.Add(new byte[2048]);
        sectors.Add(new byte[2048]); // 16 PVD (filled below)
        var term = new byte[2048];
        term[0] = 255; Encoding.ASCII.GetBytes("CD001").CopyTo(term, 1); term[6] = 1;
        sectors.Add(term); // 17
        const int rootLba = 18;
        sectors.Add(new byte[2048]); // 18 root dir
        var dirLbas = new List<int>();
        foreach (var _ in _dirs) { dirLbas.Add(sectors.Count); sectors.Add(new byte[2048]); }

        int Place(byte[] data)
        {
            var lba = sectors.Count;
            for (var o = 0; o < Math.Max(1, data.Length); o += 2048)
            {
                var s = new byte[2048];
                Array.Copy(data, o, s, 0, Math.Min(2048, data.Length - o));
                sectors.Add(s);
            }
            return lba;
        }

        var root = new List<byte[]> { Record("\0", rootLba, 2048, true), Record("\x01", rootLba, 2048, true) };
        for (var i = 0; i < _dirs.Count; i++)
        {
            var dir = _dirs[i];
            var sub = new List<byte[]> { Record("\0", dirLbas[i], 2048, true), Record("\x01", rootLba, 2048, true) };
            foreach (var f in dir.Files) sub.Add(Record(f.Name + ";1", Place(f.Data), f.Data.Length, false));
            Concat(sub).CopyTo(sectors[dirLbas[i]], 0);
            root.Add(Record(dir.Name, dirLbas[i], 2048, true));
        }
        foreach (var f in _rootFiles) root.Add(Record(f.Name + ";1", Place(f.Data), f.Data.Length, false));
        Concat(root).CopyTo(sectors[rootLba], 0);

        var pvd = sectors[16];
        pvd[0] = 1; Encoding.ASCII.GetBytes("CD001").CopyTo(pvd, 1); pvd[6] = 1;
        Pad(pvd, 8, SystemId, 32);
        Pad(pvd, 40, VolumeId, 32);
        BinaryPrimitives.WriteUInt32LittleEndian(pvd.AsSpan(80), (uint)sectors.Count);
        BinaryPrimitives.WriteUInt32BigEndian(pvd.AsSpan(84), (uint)sectors.Count);
        Record("\0", rootLba, 2048, true).CopyTo(pvd, 156);

        if (sectorSize == 2048) return Concat(sectors);
        var raw = new byte[sectors.Count * 2352];
        for (var i = 0; i < sectors.Count; i++)
        {
            var o = i * 2352;
            raw[o] = 0x00; for (var k = 1; k <= 10; k++) raw[o + k] = 0xFF; raw[o + 11] = 0x00;
            raw[o + 15] = (byte)mode;
            var dataOffset = mode == 2 ? 24 : 16;
            sectors[i].CopyTo(raw, o + dataOffset);
        }
        return raw;
    }

    private static void Pad(byte[] d, int off, string s, int len)
    {
        for (var i = 0; i < len; i++) d[off + i] = i < s.Length ? (byte)s[i] : (byte)' ';
    }

    private static byte[] Record(string name, int lba, int size, bool dir)
    {
        var n = Encoding.ASCII.GetBytes(name);
        var len = 33 + n.Length;
        if (len % 2 == 1) len++;
        var r = new byte[len];
        r[0] = (byte)len;
        BinaryPrimitives.WriteUInt32LittleEndian(r.AsSpan(2), (uint)lba);
        BinaryPrimitives.WriteUInt32BigEndian(r.AsSpan(6), (uint)lba);
        BinaryPrimitives.WriteUInt32LittleEndian(r.AsSpan(10), (uint)size);
        BinaryPrimitives.WriteUInt32BigEndian(r.AsSpan(14), (uint)size);
        r[25] = (byte)(dir ? 0x02 : 0x00);
        r[28] = 1; r[31] = 1;
        r[32] = (byte)n.Length;
        n.CopyTo(r, 33);
        return r;
    }

    private static byte[] Concat(List<byte[]> parts)
    {
        var all = new byte[parts.Sum(p => p.Length)];
        var o = 0;
        foreach (var p in parts) { p.CopyTo(all, o); o += p.Length; }
        return all;
    }

    /// <summary>Builds a PARAM.SFO with UTF-8 string values.</summary>
    public static byte[] ParamSfo(params (string Key, string Value)[] entries)
    {
        var keys = new List<byte>();
        var data = new List<byte>();
        var index = new List<byte[]>();
        foreach (var (k, v) in entries)
        {
            var e = new byte[16];
            BinaryPrimitives.WriteUInt16LittleEndian(e.AsSpan(0), (ushort)keys.Count);
            BinaryPrimitives.WriteUInt16LittleEndian(e.AsSpan(2), 0x0204);
            var val = Encoding.UTF8.GetBytes(v + "\0");
            var max = (val.Length + 3) / 4 * 4;
            BinaryPrimitives.WriteUInt32LittleEndian(e.AsSpan(4), (uint)val.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(e.AsSpan(8), (uint)max);
            BinaryPrimitives.WriteUInt32LittleEndian(e.AsSpan(12), (uint)data.Count);
            index.Add(e);
            keys.AddRange(Encoding.ASCII.GetBytes(k + "\0"));
            data.AddRange(val);
            data.AddRange(new byte[max - val.Length]);
        }
        while (keys.Count % 4 != 0) keys.Add(0);
        var keyTable = 0x14 + 16 * entries.Length;
        var dataTable = keyTable + keys.Count;
        var d = new byte[dataTable + data.Count];
        Encoding.ASCII.GetBytes("\0PSF").CopyTo(d, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(d.AsSpan(4), 0x0101);
        BinaryPrimitives.WriteUInt32LittleEndian(d.AsSpan(8), (uint)keyTable);
        BinaryPrimitives.WriteUInt32LittleEndian(d.AsSpan(12), (uint)dataTable);
        BinaryPrimitives.WriteUInt32LittleEndian(d.AsSpan(16), (uint)entries.Length);
        for (var i = 0; i < index.Count; i++) index[i].CopyTo(d, 0x14 + i * 16);
        keys.ToArray().CopyTo(d, keyTable);
        data.ToArray().CopyTo(d, dataTable);
        return d;
    }
}
