using System.Buffers.Binary;
using static RetroHub.Core.Library.Sniffers.SnifferUtil;

namespace RetroHub.Core.Library.Sniffers;

/// <summary>PlayStation 1 disc: ISO9660 with SYSTEM.CNF "BOOT =" (no BOOT2), or PSX.EXE in the root (early titles).</summary>
public sealed class Ps1IsoSniffer : RomSnifferBase
{
    public override string Id => "iso9660-ps1";
    public override int HeaderBytes => 0;

    protected override SniffResult? SniffCore(string path, ReadOnlySpan<byte> header)
    {
        var info = IsoImage.Inspect(path);
        if (info == null || info.Boot2 || info.HasPspGame) return null;
        if (!(info.Boot || (!info.HasSystemCnf && info.HasPsxExe))) return null;
        return new SniffResult("psx", info.VolumeIdentifier, info.BootSerial, IsoImage.RegionFromSerial(info.BootSerial));
    }
}

/// <summary>PlayStation 2 disc (CD or DVD): ISO9660 with "BOOT2 =" in SYSTEM.CNF.</summary>
public sealed class Ps2IsoSniffer : RomSnifferBase
{
    public override string Id => "iso9660-ps2";
    public override int HeaderBytes => 0;

    protected override SniffResult? SniffCore(string path, ReadOnlySpan<byte> header)
    {
        var info = IsoImage.Inspect(path);
        if (info is not { Boot2: true }) return null;
        return new SniffResult("ps2", info.VolumeIdentifier, info.BootSerial, IsoImage.RegionFromSerial(info.BootSerial));
    }
}

/// <summary>PSP UMD image: ISO9660 with a PSP_GAME directory or system identifier "PSP GAME"; serial/title from PARAM.SFO.</summary>
public sealed class PspIsoSniffer : RomSnifferBase
{
    public override string Id => "iso9660-psp";
    public override int HeaderBytes => 0;

    protected override SniffResult? SniffCore(string path, ReadOnlySpan<byte> header)
    {
        var info = IsoImage.Inspect(path);
        if (info == null) return null;
        var isPsp = info.HasPspGame ||
                    (info.SystemIdentifier?.StartsWith("PSP GAME", StringComparison.OrdinalIgnoreCase) ?? false);
        if (!isPsp) return null;
        return new SniffResult("psp", TitleParser.CleanHeaderTitle(info.PspTitle) ?? info.VolumeIdentifier,
            info.PspDiscId, IsoImage.RegionFromSerial(info.PspDiscId));
    }
}

/// <summary>Shared Nintendo optical disc header (GameCube and Wii use the same 0x60-byte layout).</summary>
internal static class NintendoDisc
{
    public const uint GcMagic = 0xC2339F3D;   // at 0x1C
    public const uint WiiMagic = 0x5D1C9EA3;  // at 0x18

    /// <summary>Header bytes needed: WIA/RVZ keep a copy of the disc header at 0x58, WBFS at 0x200.</summary>
    public const int HeaderBytes = 0x200 + 0x60;

    /// <summary>Returns the offset of the plain disc header inside <paramref name="h"/>, or -1 (also handles .wbfs/.rvz/.wia).</summary>
    public static int DiscHeaderOffset(ReadOnlySpan<byte> h)
    {
        if (At(h, 0, "WBFS"u8)) return 0x200;
        if (At(h, 0, "RVZ\x01"u8) || At(h, 0, "WIA\x01"u8)) return 0x58;
        return 0;
    }

    public static SniffResult? Read(ReadOnlySpan<byte> h, uint magic, int magicOffset, string systemId)
    {
        var b = DiscHeaderOffset(h);
        if (b + 0x60 > h.Length) return null;
        if (BinaryPrimitives.ReadUInt32BigEndian(h[(b + magicOffset)..]) != magic) return null;
        var game = Code(h, b, 4);
        var maker = Code(h, b + 4, 2) ?? Ascii(h, b + 4, 2);
        var serial = game == null ? null : game + maker;
        var title = Ascii(h, b + 0x20, 0x40);
        return new SniffResult(systemId, title, serial, game == null ? null : RegionFromCodeLetter(game[3]));
    }

    /// <summary>Dolphin's .gcz: magic 0xB10BC001, sub type 0 = GameCube, 1 = Wii (no plain header to read).</summary>
    public static int GczSubType(ReadOnlySpan<byte> h) =>
        h.Length >= 8 && BinaryPrimitives.ReadUInt32LittleEndian(h) == 0xB10BC001
            ? (int)BinaryPrimitives.ReadUInt32LittleEndian(h[4..]) : -1;
}

/// <summary>GameCube disc (.iso/.gcm, also .rvz/.wia/.gcz containers): magic 0xC2339F3D at 0x1C.</summary>
public sealed class GameCubeDiscSniffer : RomSnifferBase
{
    public override string Id => "nintendo-disc-gc";
    public override int HeaderBytes => NintendoDisc.HeaderBytes;

    protected override SniffResult? SniffCore(string path, ReadOnlySpan<byte> h)
    {
        if (NintendoDisc.GczSubType(h) == 0) return new SniffResult("gc");
        return NintendoDisc.Read(h, NintendoDisc.GcMagic, 0x1C, "gc");
    }
}

/// <summary>Wii disc (.iso, .wbfs, .rvz/.wia/.gcz containers): magic 0x5D1C9EA3 at 0x18.</summary>
public sealed class WiiDiscSniffer : RomSnifferBase
{
    public override string Id => "nintendo-disc-wii";
    public override int HeaderBytes => NintendoDisc.HeaderBytes;

    protected override SniffResult? SniffCore(string path, ReadOnlySpan<byte> h)
    {
        if (NintendoDisc.GczSubType(h) == 1) return new SniffResult("wii");
        return NintendoDisc.Read(h, NintendoDisc.WiiMagic, 0x18, "wii");
    }
}
