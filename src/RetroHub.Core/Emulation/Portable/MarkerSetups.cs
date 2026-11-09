using RetroHub.Core.Emulation.ConfigFiles;
using RetroHub.Core.IO;
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

/// <summary>
/// Eden (Switch, yuzu successor): a folder named "user" next to eden.exe makes it keep config\, keys\, nand\
/// (saves), sdmc\, shader\ and log\ there instead of %APPDATA%\eden (Windows checks the exe folder, not the working
/// directory). RetroHub also creates user\config\: Eden shows a "migrate from yuzu/suyu/…" dialog on start when that
/// folder is missing and %APPDATA%\yuzu (or another legacy folder) exists — accepting it would move or wipe data.
/// Keys convenience: prod.keys / title.keys found in bios/switch/ (or directly in bios/) are copied into
/// user\keys\ when the copy there is missing, or differs and is older. The source is never moved or deleted, an
/// empty source never overwrites anything, and a newer different copy in Eden's folder (e.g. installed through
/// Eden's menu) is kept. A differing copy that gets replaced is first saved as "prod.keys.retrohub-old-&lt;time&gt;"
/// (the mtime rule is not reliable: a copy made by Eden's "Install decryption keys" may keep the source's old
/// timestamp). Firmware
/// is not copied (Eden installs it itself through its menu).
/// user\config\qt-config.ini, [UI]: confirmStop=2 (ConfirmStop::Ask_Never; Eden's default Ask_Always would answer
/// RetroHub's WM_CLOSE during a game with "Are you sure you want to close Eden?" and stay open) and
/// check_for_updates=false. A missing file is created with just these keys (Eden fills in its defaults); the
/// legacy-migration check only looks at the config folder.
/// </summary>
public sealed class EdenSetup : PortableSetupBase
{
    public override string Id => "eden";

    /// <summary>Folder below bios/ where RetroHub looks for the Switch keys first.</summary>
    public const string BiosSubfolder = "switch";
    public const string ProdKeys = "prod.keys";
    public const string TitleKeys = "title.keys";
    public const string QtConfig = "qt-config.ini";

    /// <summary>Eden's ConfirmStop enum: Ask_Always = 0, Ask_Based_On_Game = 1, Ask_Never = 2.</summary>
    public const string ConfirmStopNever = "2";

    /// <summary>Suffix of the copy kept when a differing key file in Eden's folder is replaced.</summary>
    public const string BackupInfix = ".retrohub-old-";

    public override string Summary =>
        "Eden läuft portabel (Ordner „user“ neben der eden.exe, z. B. emulators/eden/user). Einstellungen, Spielstände (nand), " +
        "Shader-Cache und Keys liegen dort, nicht in saves/. Eigene prod.keys/title.keys aus bios/switch/ kopiert RetroHub " +
        "nach user/keys (eine abweichende ältere Kopie dort bleibt als „….retrohub-old-<Zeit>“ erhalten); die Firmware deiner " +
        "Switch installierst du in Eden über dessen Menü („Firmware installieren“). RetroHub schaltet Edens Nachfrage beim " +
        "Beenden und die Update-Prüfung ab.";

    /// <summary>Hint when no prod.keys exist (German, for the launch problems).</summary>
    public const string MissingKeysMessage =
        "Eden braucht die Schlüssel (prod.keys, meist auch title.keys) deiner eigenen Switch. Lege sie in den Ordner " +
        "„bios/switch/“ – RetroHub kopiert sie beim Start in Edens Ordner „user/keys/“ neben der eden.exe. Die Firmware deiner Switch " +
        "installierst du in Eden über das Menü. RetroHub liefert keine Keys oder Firmware mit.";

    protected override void Configure(PortableContext ctx)
    {
        var user = ctx.Ensure(Path.Combine(ctx.ExeDir, "user"));
        // an existing config folder keeps Eden's legacy-data migration dialog away
        var config = ctx.Ensure(Path.Combine(user, "config"));
        var file = Path.Combine(config, QtConfig);
        var ini = IniFile.Load(file);
        AzaharSetup.Set(ini, "UI", "confirmStop", ConfirmStopNever);
        AzaharSetup.Set(ini, "UI", "check_for_updates", "false");
        Saved(Id, file, ini.SaveIfChanged(file));
        var keysDir = Path.Combine(user, "keys");
        foreach (var name in new[] { ProdKeys, TitleKeys })
            CopyKey(ctx.Paths.Bios, keysDir, name);
        if (!HasProdKeys(ctx.Paths.Bios, ctx.ExeDir))
            Log.Warn($"Eden: no {ProdKeys} in {Path.Combine(ctx.Paths.Bios, BiosSubfolder)} or {keysDir} – encrypted Switch games (.nsp/.xci) will not start");
        Log.Debug($"Eden keeps its data in {user}");
    }

    /// <summary>The key file in bios/switch/ or else directly in bios/ (non-empty), or null.</summary>
    public static string? FindKeySource(string biosRoot, string name)
    {
        foreach (var candidate in new[] { Path.Combine(biosRoot, BiosSubfolder, name), Path.Combine(biosRoot, name) })
        {
            var info = new FileInfo(candidate);
            if (info.Exists && info.Length > 0) return info.FullName;
        }
        return null;
    }

    /// <summary>True when prod.keys is in RetroHub's bios folder or already in Eden's portable keys folder.</summary>
    public static bool HasProdKeys(string biosRoot, string edenExeDir)
    {
        if (FindKeySource(biosRoot, ProdKeys) != null) return true;
        var installed = new FileInfo(Path.Combine(edenExeDir, "user", "keys", ProdKeys));
        return installed.Exists && installed.Length > 0;
    }

    /// <summary>Copies one key file into Eden's keys folder when needed. True when it was copied.</summary>
    internal static bool CopyKey(string biosRoot, string keysDir, string name)
    {
        var src = FindKeySource(biosRoot, name);
        if (src == null)
        {
            var empty = new FileInfo(Path.Combine(biosRoot, BiosSubfolder, name));
            if (empty.Exists) Log.Warn($"Eden: {empty.FullName} is empty – not copied");
            return false;
        }
        var dst = Path.Combine(keysDir, name);
        var bytes = File.ReadAllBytes(src);
        if (bytes.Length == 0) return false;
        var srcTime = File.GetLastWriteTimeUtc(src);
        if (File.Exists(dst))
        {
            var existing = File.ReadAllBytes(dst);
            if (existing.AsSpan().SequenceEqual(bytes)) return false;
            if (File.GetLastWriteTimeUtc(dst) >= srcTime)
            {
                Log.Info($"Eden: {dst} differs from {src} but is newer – kept (delete it to take the one from bios/)");
                return false;
            }
        }
        string? backup = null;
        if (File.Exists(dst))
        {
            backup = BackupPath(dst);
            File.Copy(dst, backup); // never overwrites an earlier backup
        }
        SafeFile.WriteAllBytes(dst, bytes);
        File.SetLastWriteTimeUtc(dst, srcTime);
        Log.Info(backup == null
            ? $"Eden: copied {src} to {dst}"
            : $"Eden: copied {src} to {dst} (previous copy saved as {backup})");
        return true;
    }

    /// <summary>A not yet existing "&lt;file&gt;.retrohub-old-yyyyMMdd-HHmmss[-n]" next to the file.</summary>
    private static string BackupPath(string file)
    {
        var stem = $"{file}{BackupInfix}{DateTime.Now:yyyyMMdd-HHmmss}";
        var path = stem;
        for (int n = 2; File.Exists(path); n++) path = $"{stem}-{n}";
        return path;
    }
}
