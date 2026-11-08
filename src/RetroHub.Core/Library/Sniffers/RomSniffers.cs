namespace RetroHub.Core.Library.Sniffers;

/// <summary>
/// All built-in header sniffers. A <see cref="Catalog.SystemDef"/> references them by <see cref="IRomSniffer.Id"/>
/// in its <c>Sniffers</c> list. Ids: nes, snes, n64, gb, gbc, gba, nds, 3ds, megadrive, sms, iso9660-ps1,
/// iso9660-ps2, iso9660-psp, nintendo-disc-gc, nintendo-disc-wii.
/// The <see cref="SniffResult.SystemId"/> they report is a hint ("psx", "ps2", "gc", "gamegear" …); the scanner maps
/// a hit to the catalog system that lists the sniffer, so catalog ids may differ.
/// </summary>
public static class RomSniffers
{
    public static IReadOnlyList<IRomSniffer> All { get; } =
    [
        new NesSniffer(),
        new SnesSniffer(),
        new N64Sniffer(),
        new GameBoySniffer(),
        new GameBoyColorSniffer(),
        new GbaSniffer(),
        new NdsSniffer(),
        new N3dsSniffer(),
        new MegaDriveSniffer(),
        new SmsSniffer(),
        new Ps1IsoSniffer(),
        new Ps2IsoSniffer(),
        new PspIsoSniffer(),
        new GameCubeDiscSniffer(),
        new WiiDiscSniffer(),
    ];

    /// <summary>Built-in sniffer by id (case-insensitive), or null.</summary>
    public static IRomSniffer? ById(string id) =>
        All.FirstOrDefault(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
}
