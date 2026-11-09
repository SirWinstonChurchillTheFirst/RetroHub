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
/// Disc keys: encrypted disc images (.wux/.wud) need the disc key in keys.txt. When the launched file is such an
/// image and a "&lt;same name&gt;.key" lies next to it (or the folder holds exactly one .key file), RetroHub adds that key
/// to portable\keys.txt — once (keys already in the file, compared without comments, are not added again). Existing
/// lines, their order and line endings stay untouched; the .key file is only read. Accepted: 16 bytes binary, or text
/// with exactly 32 hex digits (whitespace allowed). Cemu's keys.txt: one key per line as 32 hex digits, text after
/// '#' is a comment.
/// </summary>
public sealed class CemuSetup : PortableSetupBase
{
    public override string Id => "cemu";

    public const string KeysFile = "keys.txt";
    public const string KeyExtension = ".key";
    /// <summary>Comment after an imported key (after the game's file name).</summary>
    public const string ImportedComment = "(von RetroHub übernommen)";
    /// <summary>Larger .key files are not read (a disc key file has 16 bytes, a hex text version a few more).</summary>
    private const long MaxKeyFileSize = 1024;

    public override string Summary =>
        "Cemu läuft portabel (Ordner „portable“ neben der Cemu.exe). Einstellungen, keys.txt und Spielstände (mlc01) liegen dort, " +
        "z. B. emulators/cemu/portable, nicht in saves/. Liegt neben einem .wux/.wud-Abbild deiner eigenen Disc die passende " +
        ".key-Datei (gleicher Name), trägt RetroHub den Disc-Key beim Start in Cemus keys.txt ein (vorhandene Zeilen bleiben erhalten).";

    /// <summary>Hint for .wux/.wud games without a key (German, for the launch problems).</summary>
    public const string MissingDiscKeyMessage =
        "Cemu braucht für .wux/.wud-Abbilder den Disc-Key deiner eigenen Wii-U-Disc. Lege die passende .key-Datei mit demselben " +
        "Namen neben das Abbild (z. B. „Spiel.key“ neben „Spiel.wux“) – RetroHub trägt den Key beim Start in Cemus keys.txt " +
        "(Ordner „portable“ neben der Cemu.exe) ein. RetroHub liefert keine Keys mit.";

    protected override void Configure(PortableContext ctx)
    {
        var dir = ctx.Ensure(Path.Combine(ctx.ExeDir, "portable"));
        var game = ctx.GamePath;
        if (IsDiscImage(game)) ImportDiscKey(game!, Path.Combine(dir, KeysFile));
        Log.Debug($"Cemu keeps its data in {dir}");
    }

    /// <summary>keys.txt in Cemu's portable folder for that exe folder.</summary>
    public static string KeysPath(string cemuExeDir) => Path.Combine(cemuExeDir, "portable", KeysFile);

    /// <summary>True for .wux / .wud (encrypted Wii U disc images).</summary>
    public static bool IsDiscImage(string? path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        var ext = Path.GetExtension(path);
        return ext.Equals(".wux", StringComparison.OrdinalIgnoreCase) || ext.Equals(".wud", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The key file for a disc image: "&lt;same base name&gt;.key" next to it (case-insensitive), else the only .key file
    /// in that folder, else null. Never throws.
    /// </summary>
    public static string? FindKeyFile(string gamePath)
    {
        try
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(gamePath));
            if (dir == null || !Directory.Exists(dir)) return null;
            var stem = Path.GetFileNameWithoutExtension(gamePath);
            var keys = Directory.EnumerateFiles(dir)
                .Where(f => Path.GetExtension(f).Equals(KeyExtension, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var same = keys.FirstOrDefault(f => Path.GetFileNameWithoutExtension(f).Equals(stem, StringComparison.OrdinalIgnoreCase));
            if (same != null) return same;
            return keys.Count == 1 ? keys[0] : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Log.Warn($"Cemu: could not look for a disc key next to {gamePath}", ex);
            return null;
        }
    }

    /// <summary>
    /// The 16-byte key from a .key file: 16 bytes binary, or text holding exactly 32 hex digits (whitespace ignored).
    /// Anything else → null (logged). Only reads the file.
    /// </summary>
    public static byte[]? ReadKey(string keyFile)
    {
        try
        {
            var info = new FileInfo(keyFile);
            if (!info.Exists) return null;
            if (info.Length == 0 || info.Length > MaxKeyFileSize)
            {
                Log.Warn($"Cemu: {keyFile} is not a disc key ({info.Length} bytes; expected 16 bytes or 32 hex digits) – ignored");
                return null;
            }
            var bytes = File.ReadAllBytes(keyFile);
            if (bytes.Length == 16) return bytes;
            var text = new string(System.Text.Encoding.UTF8.GetString(bytes).Where(c => !char.IsWhiteSpace(c) && c != '﻿').ToArray());
            if (text.Length == 32 && text.All(Uri.IsHexDigit)) return Convert.FromHexString(text);
            Log.Warn($"Cemu: {keyFile} is not a disc key ({bytes.Length} bytes; expected 16 bytes or 32 hex digits) – ignored");
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Cemu: could not read {keyFile}", ex);
            return null;
        }
    }

    /// <summary>The keys in a keys.txt text (upper-case hex): per line, text before '#' that is exactly 32 hex digits.</summary>
    public static IEnumerable<(string Key, string Comment)> ParseKeys(string text)
    {
        foreach (var raw in text.Split('\n'))
        {
            var hash = raw.IndexOf('#');
            var value = (hash < 0 ? raw : raw[..hash]).Trim();
            var comment = hash < 0 ? "" : raw[(hash + 1)..].Trim();
            if (value.Length == 32 && value.All(Uri.IsHexDigit)) yield return (value.ToUpperInvariant(), comment);
        }
    }

    /// <summary>
    /// True when keys.txt holds at least one key other than the example line of Cemu's template
    /// ("# example key (can be deleted)"). Never throws.
    /// </summary>
    public static bool HasDiscKeys(string keysTxt)
    {
        try
        {
            if (!File.Exists(keysTxt)) return false;
            return ParseKeys(File.ReadAllText(keysTxt))
                .Any(k => !k.Comment.Contains("example key", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Cemu: could not read {keysTxt}", ex);
            return false;
        }
    }

    /// <summary>
    /// Adds the disc key found next to <paramref name="gamePath"/> to keys.txt unless it is there already.
    /// True when keys.txt was written. Never throws (problems are logged).
    /// </summary>
    public static bool ImportDiscKey(string gamePath, string keysTxt)
    {
        try
        {
            var keyFile = FindKeyFile(gamePath);
            if (keyFile == null)
            {
                Log.Info($"Cemu: no disc key (.key) next to {gamePath} – Cemu can only start it when keys.txt already holds its key");
                return false;
            }
            if (!Path.GetFileNameWithoutExtension(keyFile).Equals(Path.GetFileNameWithoutExtension(gamePath), StringComparison.OrdinalIgnoreCase))
                Log.Info($"Cemu: using {keyFile}, the only .key file next to {gamePath}");
            var key = ReadKey(keyFile);
            if (key == null) return false;
            var hex = Convert.ToHexString(key);

            var existing = File.Exists(keysTxt) ? File.ReadAllBytes(keysTxt) : [];
            var text = System.Text.Encoding.UTF8.GetString(existing);
            if (ParseKeys(text).Any(k => k.Key == hex))
            {
                Log.Debug($"Cemu: disc key from {keyFile} already in {keysTxt}");
                return false;
            }
            // keep every existing byte; append one line with the file's own line ending (CRLF for a new file)
            var eol = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : text.Contains('\n') ? "\n" : "\r\n";
            var sb = new System.Text.StringBuilder();
            if (text.Length > 0 && !text.EndsWith('\n')) sb.Append(eol);
            sb.Append(hex.ToLowerInvariant()).Append(" # ").Append(Path.GetFileName(gamePath)).Append(' ').Append(ImportedComment).Append(eol);
            var added = new System.Text.UTF8Encoding(false).GetBytes(sb.ToString());
            SafeFile.WriteAllBytes(keysTxt, [.. existing, .. added]);
            Log.Info($"Cemu: added the disc key from {keyFile} to {keysTxt}");
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn($"Cemu: could not add the disc key for {gamePath} to {keysTxt}", ex);
            return false;
        }
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
