using RetroHub.Core.Library;
using RetroHub.Core.Library.Sniffers;
using Xunit;

namespace RetroHub.Core.Tests.Library;

public sealed class SnifferTests : IDisposable
{
    private readonly TempRoot _tmp = new();
    public void Dispose() => _tmp.Dispose();

    /// <summary>Writes the image to disk and runs the sniffer exactly like the scanner does (header slice + path).</summary>
    private SniffResult? Sniff(string id, byte[] image, string name = "rom.bin")
    {
        var path = _tmp.Write("sniff/" + Guid.NewGuid().ToString("N")[..6] + "_" + name, image);
        var s = RomSniffers.ById(id)!;
        var n = Math.Min(s.HeaderBytes, image.Length);
        return s.Sniff(path, image.AsSpan(0, n));
    }

    public static TheoryData<string> AllIds() => new(RomSniffers.All.Select(s => s.Id));

    [Fact]
    public void All_contains_every_documented_id_once()
    {
        string[] expected = ["nes", "snes", "n64", "gb", "gbc", "gba", "nds", "3ds", "megadrive", "sms",
            "iso9660-ps1", "iso9660-ps2", "iso9660-psp", "nintendo-disc-gc", "nintendo-disc-wii"];
        Assert.Equal(expected.Order(), RomSniffers.All.Select(s => s.Id).Order());
    }

    [Theory, MemberData(nameof(AllIds))]
    public void Random_bytes_are_not_recognised(string id)
    {
        Assert.Null(Sniff(id, RomImages.Random(0x30000, seed: 1234)));
        Assert.Null(Sniff(id, RomImages.Random(0x30000, seed: 98765)));
    }

    [Theory, MemberData(nameof(AllIds))]
    public void Short_file_is_not_recognised(string id) => Assert.Null(Sniff(id, RomImages.Random(12, seed: 7)));

    [Theory, MemberData(nameof(AllIds))]
    public void Empty_file_is_not_recognised(string id) => Assert.Null(Sniff(id, []));

    [Theory, MemberData(nameof(AllIds))]
    public void Zero_filled_file_is_not_recognised(string id) => Assert.Null(Sniff(id, new byte[0x30000]));

    [Theory, MemberData(nameof(AllIds))]
    public void Missing_file_never_throws(string id)
    {
        var s = RomSniffers.ById(id)!;
        Assert.Null(s.Sniff(_tmp.Full("does/not/exist.bin"), ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void Nes_ines_header()
    {
        var r = Sniff("nes", RomImages.Nes());
        Assert.NotNull(r);
        Assert.Equal("nes", r.SystemId);
        Assert.False(r.Broken);
    }

    [Fact]
    public void Nes_truncated_is_broken()
    {
        var r = Sniff("nes", RomImages.Nes(truncated: true));
        Assert.True(r!.Broken);
    }

    [Fact]
    public void Nes_20_header()
    {
        var img = RomImages.Nes();
        img[7] = 0x08; // NES 2.0 identifier
        var r = Sniff("nes", img);
        Assert.Equal("nes", r!.SystemId);
        Assert.False(r.Broken);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Snes_lorom_hirom_with_and_without_copier_header(bool hiRom, bool copier)
    {
        var r = Sniff("snes", RomImages.Snes("SUPER TEST GAME", hiRom, copier));
        Assert.NotNull(r);
        Assert.Equal("snes", r.SystemId);
        Assert.Equal("SUPER TEST GAME", r.Title);
        Assert.Equal("USA", r.Region);
        Assert.Equal("ATBE", r.Serial);
    }

    [Fact]
    public void Snes_without_checksum_still_recognised_by_structure()
    {
        var r = Sniff("snes", RomImages.Snes("HOMEBREW", checksum: false));
        Assert.Equal("HOMEBREW", r!.Title);
    }

    [Theory]
    [InlineData("z64")]
    [InlineData("v64")]
    [InlineData("n64")]
    public void N64_all_byte_orders(string order)
    {
        var r = Sniff("n64", RomImages.N64(order));
        Assert.NotNull(r);
        Assert.Equal("n64", r.SystemId);
        Assert.Equal("TEST N64 GAME", r.Title);
        Assert.Equal("NTEE", r.Serial);
        Assert.Equal("USA", r.Region);
        Assert.False(r.Broken);
    }

    [Fact]
    public void N64_too_short_is_broken() => Assert.True(Sniff("n64", RomImages.N64()[..0x800])!.Broken);

    [Fact]
    public void Gb_dmg_cart()
    {
        var r = Sniff("gb", RomImages.Gb("POKEMON RED"));
        Assert.Equal("gb", r!.SystemId);
        Assert.Equal("POKEMON RED", r.Title);
        Assert.Null(Sniff("gbc", RomImages.Gb("POKEMON RED"))); // no CGB flag
    }

    [Fact]
    public void Gbc_dual_mode_and_cgb_only()
    {
        var dual = RomImages.Gb("DUALGAME", cgb: 0x80);
        Assert.Equal("gb", Sniff("gb", dual)!.SystemId);
        Assert.Equal("gbc", Sniff("gbc", dual)!.SystemId);

        var only = RomImages.Gb("CGBONLY", cgb: 0xC0, sizeCode: 1);
        Assert.Equal("gbc", Sniff("gb", only)!.SystemId);
        var r = Sniff("gbc", only)!;
        Assert.Equal("gbc", r.SystemId);
        Assert.Equal("CGBONLY", r.Title);
        Assert.False(r.Broken);
    }

    [Fact]
    public void Gb_truncated_is_broken() => Assert.True(Sniff("gb", RomImages.Gb(sizeCode: 2, truncated: true))!.Broken);

    [Fact]
    public void Gba_header()
    {
        var r = Sniff("gba", RomImages.Gba("POKEMON EMER", "BPEE"));
        Assert.Equal("gba", r!.SystemId);
        Assert.Equal("POKEMON EMER", r.Title);
        Assert.Equal("BPEE", r.Serial);
        Assert.Equal("USA", r.Region);
    }

    [Fact]
    public void Gba_without_fixed_byte_is_rejected()
    {
        var img = RomImages.Gba();
        img[0xB2] = 0;
        Assert.Null(Sniff("gba", img));
    }

    [Fact]
    public void Nds_header_with_logo_crc()
    {
        var r = Sniff("nds", RomImages.Nds("POKEMON W2", "IRDO"));
        Assert.Equal("nds", r!.SystemId);
        Assert.Equal("POKEMON W2", r.Title);
        Assert.Equal("IRDO", r.Serial);
        Assert.False(r.Broken);
    }

    [Fact]
    public void Nds_homebrew_without_logo_crc_but_valid_header_crc()
    {
        var r = Sniff("nds", RomImages.Nds(logoCrc: false));
        Assert.Equal("nds", r!.SystemId);
        Assert.Equal("Europe", r.Region);
    }

    [Fact]
    public void Nds_truncated_is_broken() => Assert.True(Sniff("nds", RomImages.Nds(truncated: true))!.Broken);

    [Fact]
    public void N3ds_cci_and_cxi()
    {
        var cci = Sniff("3ds", RomImages.N3dsCci("CTR-P-EKJA"));
        Assert.Equal("3ds", cci!.SystemId);
        Assert.Equal("CTR-P-EKJA", cci.Serial);
        Assert.Equal("World", cci.Region);

        var cxi = Sniff("3ds", RomImages.N3dsCxi("CTR-P-BTEJ"));
        Assert.Equal("CTR-P-BTEJ", cxi!.Serial);
        Assert.Equal("Japan", cxi.Region);
    }

    [Fact]
    public void N3ds_partition_past_end_is_broken()
    {
        var img = RomImages.N3dsCci()[..0x1000];
        Assert.True(Sniff("3ds", img)!.Broken);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void MegaDrive_plain_offset101_and_smd(bool at101, bool smd)
    {
        var r = Sniff("megadrive", RomImages.MegaDrive("SONIC TEST", at101, smd));
        Assert.NotNull(r);
        Assert.Equal("megadrive", r.SystemId);
        Assert.Equal("SONIC TEST", r.Title);
        Assert.Equal("GM 00001234-00", r.Serial);
        Assert.Equal("World", r.Region);
    }

    [Theory]
    [InlineData(0x7FF0, 4, "sms")]
    [InlineData(0x3FF0, 3, "sms")]
    [InlineData(0x1FF0, 4, "sms")]
    [InlineData(0x7FF0, 6, "gamegear")]
    public void Sms_tmr_sega(int offset, int nibble, string system)
    {
        var r = Sniff("sms", RomImages.Sms(offset, nibble));
        Assert.Equal(system, r!.SystemId);
    }

    [Theory]
    [InlineData(2048, 2)]
    [InlineData(2352, 2)]
    [InlineData(2352, 1)]
    public void Ps2_iso_boot2(int sectorSize, int mode)
    {
        var img = IsoBuilder.Ps2().Build(sectorSize, mode);
        var r = Sniff("iso9660-ps2", img);
        Assert.NotNull(r);
        Assert.Equal("ps2", r.SystemId);
        Assert.Equal("SLUS-20312", r.Serial);
        Assert.Equal("USA", r.Region);
        Assert.Null(Sniff("iso9660-ps1", img));
        Assert.Null(Sniff("iso9660-psp", img));
    }

    [Theory]
    [InlineData(2048, 2)]
    [InlineData(2352, 2)]
    public void Ps1_disc_boot(int sectorSize, int mode)
    {
        var img = IsoBuilder.Ps1("SLES_012.34").Build(sectorSize, mode);
        var r = Sniff("iso9660-ps1", img);
        Assert.Equal("psx", r!.SystemId);
        Assert.Equal("SLES-01234", r.Serial);
        Assert.Equal("Europe", r.Region);
        Assert.Null(Sniff("iso9660-ps2", img));
    }

    [Fact]
    public void Ps1_early_disc_with_psx_exe_only()
    {
        var img = new IsoBuilder().File("PSX.EXE", new byte[4096]).Build();
        Assert.Equal("psx", Sniff("iso9660-ps1", img)!.SystemId);
    }

    [Fact]
    public void Plain_data_iso_is_not_a_playstation_disc()
    {
        var img = new IsoBuilder { SystemId = "WIN32" }.File("README.TXT", "hello").Build();
        foreach (var id in new[] { "iso9660-ps1", "iso9660-ps2", "iso9660-psp" }) Assert.Null(Sniff(id, img));
    }

    [Fact]
    public void Psp_iso_param_sfo()
    {
        var img = IsoBuilder.Psp().Build();
        var r = Sniff("iso9660-psp", img);
        Assert.Equal("psp", r!.SystemId);
        Assert.Equal("ULUS-10041", r.Serial);
        Assert.Equal("Test PSP Game", r.Title);
        Assert.Equal("USA", r.Region);
        Assert.Null(Sniff("iso9660-ps1", img));
    }

    [Fact]
    public void GameCube_and_wii_discs_are_distinguished()
    {
        var gc = RomImages.NintendoDisc(wii: false, "GALE", "01", "Super Smash Bros Melee");
        var wii = RomImages.NintendoDisc(wii: true, "RMCP", "01", "Mario Kart Wii");

        var g = Sniff("nintendo-disc-gc", gc);
        Assert.Equal("gc", g!.SystemId);
        Assert.Equal("GALE01", g.Serial);
        Assert.Equal("Super Smash Bros Melee", g.Title);
        Assert.Equal("USA", g.Region);
        Assert.Null(Sniff("nintendo-disc-wii", gc));

        var w = Sniff("nintendo-disc-wii", wii);
        Assert.Equal("wii", w!.SystemId);
        Assert.Equal("RMCP01", w.Serial);
        Assert.Equal("Europe", w.Region);
        Assert.Null(Sniff("nintendo-disc-gc", wii));
    }

    [Fact]
    public void Wii_wbfs_and_rvz_containers()
    {
        Assert.Equal("RTEP01", Sniff("nintendo-disc-wii", RomImages.Wbfs("RTEP"))!.Serial);
        Assert.Equal("wii", Sniff("nintendo-disc-wii", RomImages.Rvz(true, "RTEE"))!.SystemId);
        Assert.Null(Sniff("nintendo-disc-gc", RomImages.Rvz(true, "RTEE")));
        Assert.Equal("gc", Sniff("nintendo-disc-gc", RomImages.Rvz(false, "GTEE"))!.SystemId);
        Assert.Null(Sniff("nintendo-disc-wii", RomImages.Rvz(false, "GTEE")));
    }

    [Fact]
    public void Serial_from_boot_path()
    {
        Assert.Equal("SLUS-20312", IsoImage.SerialFromBootPath(@"cdrom0:\SLUS_203.12;1"));
        Assert.Equal("SCES-50000", IsoImage.SerialFromBootPath(@"cdrom0:\SCES_500.00;1"));
        Assert.Null(IsoImage.SerialFromBootPath(@"cdrom:\MAIN.EXE;1"));
    }
}
