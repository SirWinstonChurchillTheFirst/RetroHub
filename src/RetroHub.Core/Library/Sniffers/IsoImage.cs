using System.Buffers.Binary;
using System.Text;

namespace RetroHub.Core.Library.Sniffers;

/// <summary>
/// Minimal read-only ISO9660 reader for system detection. Supports cooked 2048-byte images (.iso) and raw
/// 2352-byte CD images (.bin/.img, Mode 1 and Mode 2 Form 1). Reads only the volume descriptor, the root directory
/// and a few small files. Never throws from <see cref="Inspect"/>.
/// </summary>
internal sealed class IsoImage : IDisposable
{
    private const int MaxDirectoryBytes = 256 * 1024;
    private const int MaxSmallFileBytes = 64 * 1024;

    private readonly FileStream _fs;
    private readonly int _sectorSize;
    private readonly int _dataOffset;

    public string? SystemIdentifier { get; private set; }
    public string? VolumeIdentifier { get; private set; }
    private uint _rootLba, _rootSize;

    private IsoImage(FileStream fs, int sectorSize, int dataOffset)
    {
        _fs = fs;
        _sectorSize = sectorSize;
        _dataOffset = dataOffset;
    }

    public void Dispose() => _fs.Dispose();

    /// <summary>Opens the image when a primary volume descriptor is found in one of the known layouts.</summary>
    public static IsoImage? Open(string path)
    {
        FileStream? fs = null;
        try
        {
            fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1);
            foreach (var (sector, offset) in new[] { (2048, 0), (2352, 24), (2352, 16), (2336, 8) })
            {
                var img = new IsoImage(fs, sector, offset);
                var pvd = img.ReadSector(16);
                if (pvd == null || pvd[0] != 1 || !pvd.AsSpan(1, 5).SequenceEqual("CD001"u8)) continue;
                img.SystemIdentifier = Text(pvd, 8, 32);
                img.VolumeIdentifier = Text(pvd, 40, 32);
                img._rootLba = BinaryPrimitives.ReadUInt32LittleEndian(pvd.AsSpan(156 + 2));
                img._rootSize = BinaryPrimitives.ReadUInt32LittleEndian(pvd.AsSpan(156 + 10));
                fs = null; // ownership moves to img
                return img;
            }
            return null;
        }
        catch (Exception) { return null; }
        finally { fs?.Dispose(); }
    }

    public byte[]? ReadSector(long lba)
    {
        var pos = lba * _sectorSize + _dataOffset;
        if (lba < 0 || pos + 2048 > _fs.Length) return null;
        _fs.Position = pos;
        var buf = new byte[2048];
        _fs.ReadExactly(buf);
        return buf;
    }

    private byte[]? ReadExtent(uint lba, uint size, int max)
    {
        var n = (int)Math.Min(size, (uint)max);
        var result = new byte[n];
        var done = 0;
        for (long s = lba; done < n; s++)
        {
            var sec = ReadSector(s);
            if (sec == null) return done == 0 ? null : result[..done];
            var take = Math.Min(2048, n - done);
            Array.Copy(sec, 0, result, done, take);
            done += take;
        }
        return result;
    }

    public sealed record Entry(string Name, uint Lba, uint Size, bool IsDirectory);

    public IReadOnlyList<Entry> RootEntries() => ReadDirectory(_rootLba, _rootSize);

    public IReadOnlyList<Entry> ReadDirectory(uint lba, uint size)
    {
        var list = new List<Entry>();
        var data = ReadExtent(lba, size, MaxDirectoryBytes);
        if (data == null) return list;
        var i = 0;
        while (i < data.Length)
        {
            int len = data[i];
            if (len == 0)
            {
                // Records never span sectors: skip to the next sector boundary.
                i = (i / 2048 + 1) * 2048;
                continue;
            }
            if (len < 34 || i + len > data.Length) break;
            var nameLen = data[i + 32];
            if (33 + nameLen <= len && nameLen > 0)
            {
                var raw = data.AsSpan(i + 33, nameLen);
                if (!(nameLen == 1 && raw[0] is 0 or 1)) // "." and ".."
                {
                    var name = Encoding.ASCII.GetString(raw);
                    var semi = name.IndexOf(';');
                    if (semi >= 0) name = name[..semi];
                    name = name.TrimEnd('.');
                    list.Add(new Entry(name,
                        BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(i + 2)),
                        BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(i + 10)),
                        (data[i + 25] & 0x02) != 0));
                }
            }
            i += len;
        }
        return list;
    }

    public byte[]? ReadFile(Entry e) => e.IsDirectory ? null : ReadExtent(e.Lba, e.Size, MaxSmallFileBytes);

    private static string? Text(byte[] d, int off, int len)
    {
        var s = Encoding.ASCII.GetString(d, off, len).Trim('\0', ' ');
        return s.Length == 0 ? null : s;
    }

    /// <summary>Everything the PlayStation-family sniffers need, read in one pass.</summary>
    public sealed record Info(
        string? SystemIdentifier, string? VolumeIdentifier,
        bool HasSystemCnf, bool Boot2, bool Boot, string? BootSerial, bool HasPsxExe,
        bool HasPspGame, string? PspDiscId, string? PspTitle);

    [ThreadStatic] private static (string Path, long Length, DateTime Time, Info? Info)? _last;

    /// <summary>Inspects an image (cached per thread for the last file, so the three ISO sniffers share one read).</summary>
    public static Info? Inspect(string path)
    {
        try
        {
            var fi = new FileInfo(path);
            if (!fi.Exists) return null;
            var key = (fi.FullName, fi.Length, fi.LastWriteTimeUtc);
            if (_last is { } l && l.Path == key.FullName && l.Length == key.Length && l.Time == key.LastWriteTimeUtc)
                return l.Info;
            var info = InspectUncached(path);
            _last = (key.FullName, key.Length, key.LastWriteTimeUtc, info);
            return info;
        }
        catch (Exception) { return null; }
    }

    private static Info? InspectUncached(string path)
    {
        using var iso = Open(path);
        if (iso == null) return null;
        var root = iso.RootEntries();
        var cnfEntry = root.FirstOrDefault(e => !e.IsDirectory && e.Name.Equals("SYSTEM.CNF", StringComparison.OrdinalIgnoreCase));
        bool boot = false, boot2 = false;
        string? serial = null;
        if (cnfEntry != null && iso.ReadFile(cnfEntry) is { } cnf)
        {
            foreach (var rawLine in Encoding.ASCII.GetString(cnf).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                var line = rawLine.Trim();
                var eq = line.IndexOf('=');
                if (eq <= 0) continue;
                var key = line[..eq].Trim();
                var value = line[(eq + 1)..].Trim();
                if (key.Equals("BOOT2", StringComparison.OrdinalIgnoreCase)) { boot2 = true; serial ??= SerialFromBootPath(value); }
                else if (key.Equals("BOOT", StringComparison.OrdinalIgnoreCase)) { boot = true; serial ??= SerialFromBootPath(value); }
            }
        }
        var psxExe = root.Any(e => !e.IsDirectory && e.Name.Equals("PSX.EXE", StringComparison.OrdinalIgnoreCase));

        var pspDir = root.FirstOrDefault(e => e.IsDirectory && e.Name.Equals("PSP_GAME", StringComparison.OrdinalIgnoreCase));
        string? discId = null, pspTitle = null;
        if (pspDir != null)
        {
            var sfo = iso.ReadDirectory(pspDir.Lba, pspDir.Size)
                .FirstOrDefault(e => !e.IsDirectory && e.Name.Equals("PARAM.SFO", StringComparison.OrdinalIgnoreCase));
            if (sfo != null && iso.ReadFile(sfo) is { } sfoData)
            {
                var values = ParamSfo(sfoData);
                values.TryGetValue("DISC_ID", out discId);
                values.TryGetValue("TITLE", out pspTitle);
                if (discId is { Length: 9 }) discId = $"{discId[..4]}-{discId[4..]}";
            }
        }
        return new Info(iso.SystemIdentifier, iso.VolumeIdentifier, cnfEntry != null, boot2, boot && !boot2, serial,
            psxExe, pspDir != null, discId, pspTitle);
    }

    /// <summary>"cdrom0:\SLUS_203.12;1" → "SLUS-20312".</summary>
    internal static string? SerialFromBootPath(string value)
    {
        var v = value.Trim().Trim('"');
        var cut = Math.Max(v.LastIndexOf('\\'), Math.Max(v.LastIndexOf('/'), v.LastIndexOf(':')));
        if (cut >= 0) v = v[(cut + 1)..];
        var semi = v.IndexOf(';');
        if (semi >= 0) v = v[..semi];
        var compact = new string(v.Where(char.IsAsciiLetterOrDigit).ToArray()).ToUpperInvariant();
        if (compact.Length < 6 || !compact[..4].All(char.IsAsciiLetter) || !compact[4..].All(char.IsAsciiDigit)) return null;
        return $"{compact[..4]}-{compact[4..]}";
    }

    /// <summary>Region from a Sony serial prefix (SLUS → USA, SLES → Europe, SLPS → Japan …).</summary>
    internal static string? RegionFromSerial(string? serial)
    {
        if (serial is not { Length: >= 4 }) return null;
        return serial[..4] switch
        {
            "SLUS" or "SCUS" or "LSP-" or "PAPX" => "USA",
            "SLES" or "SCES" or "SCED" or "SLED" => "Europe",
            "SLPS" or "SLPM" or "SCPS" or "SCPM" or "SIPS" or "SLAJ" or "SCAJ" => "Japan",
            "SLKA" or "SCKA" => "Korea",
            "ULUS" or "UCUS" or "NPUH" => "USA",
            "ULES" or "UCES" or "NPEH" => "Europe",
            "ULJS" or "ULJM" or "UCJS" or "NPJH" => "Japan",
            _ => null,
        };
    }

    /// <summary>Parses a PSF (PARAM.SFO) key/value table; string values only.</summary>
    internal static Dictionary<string, string> ParamSfo(byte[] d)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (d.Length < 0x14 || !d.AsSpan(0, 4).SequenceEqual("\0PSF"u8)) return result;
        var keyTable = (int)BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(0x08));
        var dataTable = (int)BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(0x0C));
        var count = (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(0x10)), 256u);
        for (var i = 0; i < count; i++)
        {
            var e = 0x14 + i * 16;
            if (e + 16 > d.Length) break;
            var keyOff = keyTable + BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(e));
            var fmt = BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(e + 2));
            var len = (int)BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(e + 4));
            var dataOff = dataTable + (int)BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(e + 12));
            if (keyOff < 0 || keyOff >= d.Length || dataOff < 0 || len < 0 || dataOff + len > d.Length) continue;
            var keyEnd = Array.IndexOf(d, (byte)0, keyOff);
            if (keyEnd < 0) continue;
            var key = Encoding.ASCII.GetString(d, keyOff, keyEnd - keyOff);
            if (fmt is 0x0204 or 0x0004)
                result[key] = Encoding.UTF8.GetString(d, dataOff, len).TrimEnd('\0').Trim();
        }
        return result;
    }
}
