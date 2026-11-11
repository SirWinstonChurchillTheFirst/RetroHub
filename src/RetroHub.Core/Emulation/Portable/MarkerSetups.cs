using RetroHub.Core.Emulation.ConfigFiles;
using RetroHub.Core.IO;
using RetroHub.Core.Logging;

namespace RetroHub.Core.Emulation.Portable;

/// <summary>
/// Dolphin: an empty portable.txt next to Dolphin.exe makes it use a "User" folder next to the exe instead of
/// Documents\Dolphin Emulator. Memory cards, Wii NAND saves, states and screenshots all live in
/// &lt;Dolphin folder&gt;\User (Dolphin's own layout) — RetroHub does not redirect them.
/// User\Config\Dolphin.ini: [AutoUpdate] UpdateTrack = (empty: Dolphin's "Don't update", AutoUpdate.cpp returns
/// before any request – Dolphin never replaces its own files) and [Analytics] PermissionAsked = True, Enabled = False
/// (the first-start statistics question is shown even in batch mode). Other lines stay; Dolphin fills in its defaults.
/// </summary>
public sealed class DolphinSetup : PortableSetupBase
{
    public override string Id => "dolphin";

    public override string Summary =>
        "Dolphin läuft portabel (portable.txt neben der Dolphin.exe). Spielstände, Savestates und Screenshots liegen in Dolphins " +
        "eigenem Ordner „User“ neben der exe (z. B. emulators/dolphin/User), nicht in saves/. Dolphins Update-Prüfung und die " +
        "Statistik-Frage beim ersten Start schaltet RetroHub ab.";

    protected override void Configure(PortableContext ctx)
    {
        ctx.EnsureMarker("portable.txt");
        var file = Path.Combine(ctx.ExeDir, "User", "Config", "Dolphin.ini");
        var ini = IniFile.Load(file);
        ini.Set("AutoUpdate", "UpdateTrack", "");
        ini.Set("Analytics", "PermissionAsked", "True");
        ini.Set("Analytics", "Enabled", "False");
        Saved(Id, file, ini.SaveIfChanged(file));
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
    public const string SettingsFile = "settings.xml";
    public const string KeyExtension = ".key";
    /// <summary>Comment after an imported key (after the game's file name).</summary>
    public const string ImportedComment = "(von RetroHub übernommen)";
    /// <summary>Larger .key files are not read (a disc key file has 16 bytes, a hex text version a few more).</summary>
    private const long MaxKeyFileSize = 1024;

    public override string Summary =>
        "Cemu läuft portabel (Ordner „portable“ neben der Cemu.exe). Einstellungen, keys.txt und Spielstände (mlc01) liegen dort, " +
        "z. B. emulators/cemu/portable, nicht in saves/. RetroHub schaltet Cemus Update-Prüfung ab und übernimmt importierte Keys " +
        "aus bios/wiiu/keys.txt. Liegt neben einem .wux/.wud-Abbild deiner eigenen Disc die passende " +
        ".key-Datei (gleicher Name), trägt RetroHub den Disc-Key beim Start in Cemus keys.txt ein (vorhandene Zeilen bleiben erhalten).";

    /// <summary>Hint for .wux/.wud games without a key (German, for the launch problems).</summary>
    public const string MissingDiscKeyMessage =
        "Cemu braucht für .wux/.wud-Abbilder den Disc-Key deiner eigenen Wii-U-Disc. Lege die passende .key-Datei mit demselben " +
        "Namen neben das Abbild (z. B. „Spiel.key“ neben „Spiel.wux“) – RetroHub trägt den Key beim Start in Cemus keys.txt " +
        "(Ordner „portable“ neben der Cemu.exe) ein. RetroHub liefert keine Keys mit.";

    /// <summary>Hint for a .key file next to the image that holds no usable disc key (German, for the launch problems).</summary>
    public static string InvalidDiscKeyMessage(string keyFile) =>
        $"Die Datei „{Path.GetFileName(keyFile)}“ neben dem Abbild ist kein gültiger Disc-Key (erwartet: 16 Byte oder 32 Hex-Ziffern). " +
        "RetroHub kann den Key daher nicht in Cemus keys.txt eintragen, und Cemu kann das Spiel ohne passenden Key nicht entschlüsseln.";

    protected override void Configure(PortableContext ctx)
    {
        var dir = ctx.Ensure(Path.Combine(ctx.ExeDir, "portable"));
        DisableUpdateCheck(Path.Combine(dir, SettingsFile));
        MergeStagedKeys(ctx.Paths.Bios, Path.Combine(dir, KeysFile));
        var game = ctx.GamePath;
        if (IsDiscImage(game)) ImportDiscKey(game!, Path.Combine(dir, KeysFile));
        Log.Debug($"Cemu keeps its data in {dir}");
    }

    /// <summary>
    /// portable\settings.xml: &lt;content&gt;&lt;check_update&gt;false&lt;/check_update&gt; (Cemu 2.6 CemuConfig, default true) –
    /// Cemu never offers to replace its own files. An existing file keeps everything else; a missing one is created with
    /// only this element (Cemu 2.6's XMLConfig falls back to the defaults for the rest; it skips its "Getting started"
    /// dialog then, which RetroHub does not need). An unreadable file is left alone (logged).
    /// </summary>
    internal static bool DisableUpdateCheck(string settingsXml)
    {
        try
        {
            System.Xml.Linq.XDocument doc;
            if (File.Exists(settingsXml))
            {
                try { doc = System.Xml.Linq.XDocument.Load(settingsXml, System.Xml.Linq.LoadOptions.PreserveWhitespace); }
                catch (System.Xml.XmlException ex)
                {
                    Log.Warn($"Cemu: {settingsXml} is not valid XML – update check left as it is", ex);
                    return false;
                }
            }
            else doc = new System.Xml.Linq.XDocument(new System.Xml.Linq.XDeclaration("1.0", "UTF-8", null), new System.Xml.Linq.XElement("content"));
            var root = doc.Root ?? throw new IOException("settings.xml has no root");
            var el = root.Element("check_update");
            if (el != null && el.Value.Trim() == "false") return false;
            if (el == null) root.Add(new System.Xml.Linq.XElement("check_update", "false"));
            else el.Value = "false";
            using var ms = new MemoryStream();
            using (var w = System.Xml.XmlWriter.Create(ms, new System.Xml.XmlWriterSettings { Encoding = new System.Text.UTF8Encoding(false), Indent = !File.Exists(settingsXml) }))
                doc.Save(w);
            SafeFile.WriteAllBytes(settingsXml, ms.ToArray());
            Log.Info($"Cemu: update check switched off in {settingsXml}");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Cemu: could not switch off the update check in {settingsXml}", ex);
            return false;
        }
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
    /// in that folder when the folder also holds only this one disc image (.wux/.wud), else null. Never throws.
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
            if (keys.Count != 1) return null;
            // several disc images in one folder (e.g. flat games/wiiu/): the single .key belongs to one of them, not
            // necessarily to this one
            return Directory.EnumerateFiles(dir).Count(IsDiscImage) == 1 ? keys[0] : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Log.Warn($"Cemu: could not look for a disc key next to {gamePath}", ex);
            return null;
        }
    }

    /// <summary>
    /// The 16-byte key from a .key file: 16 bytes binary, or text holding exactly 32 hex digits (whitespace ignored).
    /// Anything else → null (logged unless <paramref name="quiet"/>). Only reads the file.
    /// </summary>
    public static byte[]? ReadKey(string keyFile, bool quiet = false)
    {
        try
        {
            var info = new FileInfo(keyFile);
            if (!info.Exists) return null;
            if (info.Length == 0 || info.Length > MaxKeyFileSize)
            {
                if (!quiet) Log.Warn($"Cemu: {keyFile} is not a disc key ({info.Length} bytes; expected 16 bytes or 32 hex digits) – ignored");
                return null;
            }
            var bytes = File.ReadAllBytes(keyFile);
            if (bytes.Length == 16) return bytes;
            var text = new string(System.Text.Encoding.UTF8.GetString(bytes).Where(c => !char.IsWhiteSpace(c) && c != '﻿').ToArray());
            if (text.Length == 32 && text.All(Uri.IsHexDigit)) return Convert.FromHexString(text);
            if (!quiet) Log.Warn($"Cemu: {keyFile} is not a disc key ({bytes.Length} bytes; expected 16 bytes or 32 hex digits) – ignored");
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (!quiet) Log.Warn($"Cemu: could not read {keyFile}", ex);
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
    /// Appends the keys (32 hex digits each, any case) that keys.txt does not hold yet, each on its own line
    /// "&lt;key&gt; # &lt;comment&gt;" (comment optional). Every existing byte, the order and the line endings stay
    /// (CRLF for a new file); duplicates within <paramref name="keys"/> are added once. Returns how many keys were
    /// added (0 = file untouched). Never logs a key. Throws on I/O errors (callers decide).
    /// </summary>
    public static int MergeKeys(string keysTxt, IEnumerable<(string Key, string Comment)> keys)
    {
        var existing = File.Exists(keysTxt) ? File.ReadAllBytes(keysTxt) : [];
        var text = System.Text.Encoding.UTF8.GetString(existing);
        var known = ParseKeys(text).Select(k => k.Key).ToHashSet(StringComparer.Ordinal);
        var eol = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : text.Contains('\n') ? "\n" : "\r\n";
        var sb = new System.Text.StringBuilder();
        var added = 0;
        foreach (var (raw, comment) in keys)
        {
            var key = raw.Trim().ToUpperInvariant();
            if (key.Length != 32 || !key.All(Uri.IsHexDigit) || !known.Add(key)) continue;
            if (added == 0 && text.Length > 0 && !text.EndsWith('\n')) sb.Append(eol);
            sb.Append(key.ToLowerInvariant());
            var c = (comment ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
            if (c.Length > 0) sb.Append(" # ").Append(c);
            sb.Append(eol);
            added++;
        }
        if (added == 0) return 0;
        SafeFile.WriteAllBytes(keysTxt, [.. existing, .. new System.Text.UTF8Encoding(false).GetBytes(sb.ToString())]);
        return added;
    }

    /// <summary>
    /// Merges the keys of RetroHub's own copy bios/wiiu/keys.txt (written by "Firmware &amp; Keys importieren") into
    /// Cemu's keys.txt. True when Cemu's file changed. Never throws.
    /// </summary>
    public static bool MergeStagedKeys(string biosRoot, string keysTxt)
    {
        var staged = StagedKeysPath(biosRoot);
        try
        {
            if (!File.Exists(staged)) return false;
            var added = MergeKeys(keysTxt, ParseKeys(File.ReadAllText(staged)));
            if (added > 0) Log.Info($"Cemu: added {added} key(s) from {staged} to {keysTxt}");
            return added > 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Cemu: could not merge {staged} into {keysTxt}", ex);
            return false;
        }
    }

    /// <summary>Folder below bios/ where the import keeps the Wii U keys.</summary>
    public const string BiosSubfolder = "wiiu";

    /// <summary>RetroHub's copy of the imported Wii U keys: bios/wiiu/keys.txt.</summary>
    public static string StagedKeysPath(string biosRoot) => Path.Combine(biosRoot, BiosSubfolder, KeysFile);

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
            if (MergeKeys(keysTxt, [(hex, $"{Path.GetFileName(gamePath)} {ImportedComment}")]) == 0)
            {
                Log.Debug($"Cemu: disc key from {keyFile} already in {keysTxt}");
                return false;
            }
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
/// timestamp). Firmware: "Firmware &amp; Keys importieren" puts the .nca files straight into Eden's NAND
/// (user\nand\system\Contents\registered, flat, like Eden 0.2.1's own "Install Firmware" in
/// qt_common/util/content.cpp); imported while Eden was not installed, they wait in bios\switch\firmware\ and are
/// copied there before Eden's first start, as long as Eden's folder holds no firmware yet (never over one).
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
        "Switch kommt über „Einstellungen › Firmware & Keys“ nach user/nand/system/Contents/registered. RetroHub schaltet Edens " +
        "Nachfrage beim Beenden und die Update-Prüfung ab.";

    /// <summary>Hint when no prod.keys exist (German, for the launch problems).</summary>
    public const string MissingKeysMessage =
        "Eden braucht die Schlüssel (prod.keys, meist auch title.keys) deiner eigenen Switch. Lege sie in den Ordner " +
        "„bios/switch/“ oder importiere sie unter „Einstellungen › Firmware & Keys“ – RetroHub kopiert sie in Edens Ordner " +
        "„user/keys/“ neben der eden.exe. Dort importierst du auch die Firmware deiner Switch. RetroHub liefert keine Keys oder " +
        "Firmware mit.";

    /// <summary>Eden's firmware folder for that exe folder: user/nand/system/Contents/registered.</summary>
    public static string FirmwarePath(string edenExeDir) =>
        Path.Combine(edenExeDir, "user", "nand", "system", "Contents", "registered");

    /// <summary>Firmware imported while Eden was not installed: bios/switch/firmware/.</summary>
    public static string StagedFirmwarePath(string biosRoot) => Path.Combine(biosRoot, BiosSubfolder, "firmware");

    /// <summary>
    /// Copies the waiting firmware (bios/switch/firmware/*.nca) into Eden's NAND when Eden has none yet. True when copied.
    /// Never throws (logged).
    /// </summary>
    public static bool InstallStagedFirmware(string biosRoot, string edenExeDir)
    {
        var staged = StagedFirmwarePath(biosRoot);
        var target = FirmwarePath(edenExeDir);
        try
        {
            if (!Directory.Exists(staged)) return false;
            var files = Directory.EnumerateFiles(staged, "*.nca").ToList();
            if (files.Count == 0) return false;
            if (Directory.Exists(target) && Directory.EnumerateFiles(target, "*.nca").Any()) return false;
            Directory.CreateDirectory(target);
            foreach (var f in files) File.Copy(f, Path.Combine(target, Path.GetFileName(f)), overwrite: false);
            Log.Info($"Eden: installed the firmware from {staged} ({files.Count} files) into {target}");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Eden: could not install the firmware from {staged} into {target}", ex);
            return false;
        }
    }

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
        InstallStagedFirmware(ctx.Paths.Bios, ctx.ExeDir);
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
    internal static string BackupPath(string file)
    {
        var stem = $"{file}{BackupInfix}{DateTime.Now:yyyyMMdd-HHmmss}";
        var path = stem;
        for (int n = 2; File.Exists(path); n++) path = $"{stem}-{n}";
        return path;
    }
}
