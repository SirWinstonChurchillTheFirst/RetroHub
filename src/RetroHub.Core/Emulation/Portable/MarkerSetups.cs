using RetroHub.Core.Logging;

namespace RetroHub.Core.Emulation.Portable;

/// <summary>
/// Dolphin: an empty portable.txt next to Dolphin.exe makes it use a "User" folder next to the exe instead of
/// Documents\Dolphin Emulator. Memory cards, Wii NAND saves, states and screenshots all live in
/// &lt;Dolphin folder&gt;\User (Dolphin's own layout) — RetroHub does not redirect them.
/// </summary>
public sealed class DolphinSetup : PortableSetupBase
{
    public override string Id => "dolphin";

    public override string Summary =>
        "Dolphin läuft portabel (portable.txt neben der Dolphin.exe). Spielstände, Savestates und Screenshots liegen in Dolphins " +
        "eigenem Ordner „User“ neben der exe (z. B. emulators/dolphin/User), nicht in saves/.";

    protected override void Configure(PortableContext ctx)
    {
        ctx.EnsureMarker("portable.txt");
        Log.Debug($"Dolphin keeps its data in {Path.Combine(ctx.ExeDir, "User")}");
    }
}

/// <summary>
/// PCSX2 2.x: a portable marker next to the exe makes it keep settings, memory cards (memcards\), states (sstates\)
/// and screenshots (snaps\) in its own folder instead of Documents\PCSX2. RetroHub creates an empty portable.ini
/// unless portable.ini or portable.txt already exists; nothing is redirected to saves/.
/// </summary>
public sealed class Pcsx2Setup : PortableSetupBase
{
    public override string Id => "pcsx2";

    public override string Summary =>
        "PCSX2 läuft portabel (portable.ini neben der exe). Memory Cards, Savestates und Screenshots liegen in PCSX2s eigenen " +
        "Unterordnern (memcards, sstates, snaps) neben der exe. Das PS2-BIOS wählst du beim ersten Start von PCSX2 aus.";

    protected override void Configure(PortableContext ctx)
    {
        if (File.Exists(Path.Combine(ctx.ExeDir, "portable.txt"))) return;
        ctx.EnsureMarker("portable.ini");
    }
}

/// <summary>
/// PPSSPP: the zip build is portable by itself (memstick\ next to the exe). An "installed.txt" next to the exe would
/// switch it to Documents\PPSSPP. RetroHub does not delete that file — it may be the user's deliberate choice and
/// their existing saves would seem to vanish — it only logs a warning.
/// </summary>
public sealed class PpssppSetup : PortableSetupBase
{
    public override string Id => "ppsspp";

    public override string Summary =>
        "PPSSPP (Zip-Version) ist von sich aus portabel: Spielstände und Savestates liegen im Ordner „memstick“ neben der exe.";

    protected override void Configure(PortableContext ctx)
    {
        var installed = Path.Combine(ctx.ExeDir, "installed.txt");
        if (File.Exists(installed))
            Log.Warn($"PPSSPP is not portable: {installed} exists, so it keeps its data in Documents\\PPSSPP. Delete that file to make it portable.");
    }
}

/// <summary>
/// Cemu 2.x: a folder named "portable" next to Cemu.exe makes it keep settings.xml, keys.txt, the MLC (Wii U
/// internal storage with the saves), shader caches and logs there instead of %APPDATA%\Cemu. RetroHub creates
/// that folder; Wii U saves therefore live in &lt;Cemu folder&gt;\portable\mlc01, not in saves/.
/// </summary>
public sealed class CemuSetup : PortableSetupBase
{
    public override string Id => "cemu";

    public override string Summary =>
        "Cemu läuft portabel (Ordner „portable“ neben der Cemu.exe). Einstellungen, keys.txt und Spielstände (mlc01) liegen dort, " +
        "z. B. emulators/cemu/portable, nicht in saves/.";

    protected override void Configure(PortableContext ctx)
    {
        var dir = ctx.Ensure(Path.Combine(ctx.ExeDir, "portable"));
        Log.Debug($"Cemu keeps its data in {dir}");
    }
}
