using System.IO.Compression;
using System.Security.Cryptography;
using RetroHub.Core.Catalog;
using RetroHub.Core.Emulation.Portable;
using RetroHub.Core.IO;
using RetroHub.Core.Logging;

namespace RetroHub.Core.Emulation.Firmware;

/// <summary>
/// Where the imported files go. RetroHub's own copy always lives in bios/ (bios/&lt;catalog file&gt;, bios/switch/,
/// bios/wiiu/keys.txt, bios/pcsx2/bios/) – one folder to back up. The Switch and Wii U keys are also put straight into
/// the emulator (Eden user/keys, Cemu portable/keys.txt) when it is installed inside the RetroHub folder; the Switch
/// firmware goes into Eden's NAND (user/nand/system/Contents/registered) or, while Eden is not installed, waits in
/// bios/switch/firmware/ until <see cref="EdenSetup"/> installs it before Eden's first start. Emulators outside the
/// RetroHub folder (the player's own installs) are never written to.
/// </summary>
public sealed class FirmwareLocations
{
    public FirmwareLocations(AppPaths paths, string? edenDir, string? cemuDir)
    {
        Paths = paths;
        EdenDir = edenDir;
        CemuDir = cemuDir;
    }

    public AppPaths Paths { get; }
    /// <summary>Eden's exe folder inside the RetroHub folder, or null.</summary>
    public string? EdenDir { get; }
    /// <summary>Cemu's exe folder inside the RetroHub folder, or null.</summary>
    public string? CemuDir { get; }

    public string Bios => Paths.Bios;
    public string SwitchKeys => Path.Combine(Paths.Bios, EdenSetup.BiosSubfolder);
    public string? EdenKeys => EdenDir == null ? null : Path.Combine(EdenDir, "user", "keys");
    public string SwitchFirmwareStage => EdenSetup.StagedFirmwarePath(Paths.Bios);
    public string? EdenFirmware => EdenDir == null ? null : EdenSetup.FirmwarePath(EdenDir);
    /// <summary>Where the firmware is now: Eden's NAND when Eden is there, else the waiting copy in bios/.</summary>
    public string FirmwareTarget => EdenFirmware ?? SwitchFirmwareStage;
    public string WiiUKeys => CemuSetup.StagedKeysPath(Paths.Bios);
    public string? CemuKeys => CemuDir == null ? null : CemuSetup.KeysPath(CemuDir);
    public string Ps2Bios => Path.Combine(Paths.Bios, "pcsx2", "bios");

    /// <summary>The installed Eden/Cemu (only when inside the RetroHub folder; a missing install falls back to the bundled folder).</summary>
    public static FirmwareLocations From(AppPaths paths, IEmulatorRegistry? registry)
    {
        string? Dir(string id, string exe)
        {
            var install = registry?.Get(id);
            var path = install?.ExePath;
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
                return AppPaths.IsInside(path, paths.Root) ? Path.GetDirectoryName(Path.GetFullPath(path)) : null;
            var bundled = Path.Combine(paths.Emulators, id);
            return File.Exists(Path.Combine(bundled, exe)) ? bundled : null;
        }
        return new FirmwareLocations(paths, Dir("eden", "eden.exe"), Dir("cemu", "Cemu.exe"));
    }
}

public enum FirmwareItemState { Present, Missing, Unknown }

/// <summary>One file (or file group) of a console: "prod.keys", "Firmware", "scph5501.bin". Never holds key content.</summary>
public sealed record FirmwareItemStatus(string Name, FirmwareItemState State, bool Required = false, int Count = 0);

public sealed record FirmwareConsoleStatus(FirmwareConsole Console, IReadOnlyList<FirmwareItemStatus> Items)
{
    public int Present => Items.Count(i => i.State == FirmwareItemState.Present);
    public int Unknown => Items.Count(i => i.State == FirmwareItemState.Unknown);
}

public enum ImportOutcome
{
    /// <summary>Copied (there was nothing yet).</summary>
    Imported,
    /// <summary>Copied over a different file; the old one was kept as "….retrohub-old-&lt;time&gt;".</summary>
    Replaced,
    /// <summary>The same file is already there.</summary>
    AlreadyPresent,
    /// <summary>Not copied: a known-good dump is already there and the new file matches no known dump.</summary>
    KeptExisting,
    /// <summary>Copied, but its MD5 matches none of the known dumps (it may still work).</summary>
    ImportedUnknownVersion,
    Failed,
}

/// <summary>What happened to one recognised file. <see cref="Count"/>: firmware files or added keys.</summary>
public sealed record ImportedItem(string ConsoleId, string Name, ImportOutcome Outcome, string Source, int Count = 1);

public enum SkipReason
{
    /// <summary>Not a file RetroHub knows.</summary>
    Unknown,
    /// <summary>The name fits but the size does not (e.g. a DSi BIOS named bios7.bin).</summary>
    WrongSize,
    /// <summary>A few .nca files – a firmware has hundreds; a game's .nca is not imported.</summary>
    LoneNca,
    /// <summary>An archive other than .zip (unpack it first).</summary>
    UnsupportedArchive,
    /// <summary>Could not be read.</summary>
    Unreadable,
}

public sealed record SkippedFile(string Name, SkipReason Reason);

public sealed record FirmwareImportResult(IReadOnlyList<ImportedItem> Items, IReadOnlyList<SkippedFile> Skipped, bool Truncated)
{
    public static readonly FirmwareImportResult Empty = new([], [], false);
    public bool Changed => Items.Any(i => i.Outcome is ImportOutcome.Imported or ImportOutcome.Replaced or ImportOutcome.ImportedUnknownVersion);
}

/// <summary>
/// "Firmware &amp; Keys importieren" (port and extension of MonHub's Bios.Import): takes the files, folders and .zip
/// archives the player picked, recognises each file by content, MD5, name and size, and copies it to the place the
/// emulator expects (see <see cref="FirmwareLocations"/>). The sources are only read. Recognition:
/// <list type="bullet">
/// <item>Switch prod.keys / title.keys by their lines (any file name), Wii U keys.txt by its 32-hex-digit lines, a Wii U
/// disc key as "*.key" of 16 bytes or 32 hex digits (<see cref="FirmwareFiles"/>).</item>
/// <item>Switch firmware: a folder (or a folder inside a .zip) with at least <see cref="FirmwareFiles.MinFirmwareFiles"/>
/// files named "&lt;32 hex&gt;[.cnmt].nca".</item>
/// <item>Catalog BIOS (systems/*.json "bios"): first by MD5 under any name ("SCPH-5501.BIN" → scph5501.bin), then by
/// file name with the catalog's sizes; a name match whose MD5 is unknown is copied as "unbekannte Version" unless a
/// known-good dump is already there. Files from a folder named "DSi" only count when nothing else matches.</item>
/// <item>PS2 BIOS by its ROMDIR (<see cref="FirmwareFiles.IsPs2Bios"/>), kept under its own name in bios/pcsx2/bios/.</item>
/// </list>
/// A file that is already there unchanged is not written again; a different one is replaced and the old copy kept as
/// "….retrohub-old-&lt;time&gt;" (a whole old firmware folder likewise). Folders are read 6 levels deep and at most
/// <see cref="MaxFiles"/> files. Key contents are never logged. Never throws; failures are logged and reported.
/// </summary>
public sealed class FirmwareImporter
{
    public const int MaxFiles = 20000;
    private const int MaxDepth = 6;

    private readonly FirmwareLocations _loc;
    private readonly IReadOnlyList<FirmwareConsole> _consoles;

    public FirmwareImporter(FirmwareLocations locations, IReadOnlyList<FirmwareConsole> consoles)
    {
        _loc = locations;
        _consoles = consoles;
    }

    // ------------------------------------------------------------------ status

    public IReadOnlyList<FirmwareConsoleStatus> Status() => _consoles.Select(Status).ToList();

    public FirmwareConsoleStatus Status(FirmwareConsole console)
    {
        try
        {
            return new FirmwareConsoleStatus(console, console.Kind switch
            {
                FirmwareConsoleKind.Switch => SwitchStatus(),
                FirmwareConsoleKind.WiiU => [WiiUStatus()],
                FirmwareConsoleKind.Ps2 => [Ps2Status()],
                FirmwareConsoleKind.Assets => console.Assets.Select(a => new FirmwareItemStatus(a.Target + "/",
                    File.Exists(Path.Combine(_loc.Bios, a.StatusFile.Replace('/', Path.DirectorySeparatorChar))) ? FirmwareItemState.Present
                        : Directory.Exists(Path.Combine(_loc.Bios, a.Target)) ? FirmwareItemState.Unknown : FirmwareItemState.Missing, true)).ToList(),
                FirmwareConsoleKind.CatalogBios when console.System != null => BiosCheck.Check(console.System, _loc.Bios)
                    .Select(s => new FirmwareItemStatus(Path.GetFileName(s.File.File),
                        s.State switch { BiosState.Ok => FirmwareItemState.Present, BiosState.Mismatch => FirmwareItemState.Unknown, _ => FirmwareItemState.Missing },
                        s.File.Required))
                    .ToList(),
                _ => [],
            });
        }
        catch (Exception ex)
        {
            Log.Warn($"Firmware status for {console.Id} failed", ex);
            return new FirmwareConsoleStatus(console, []);
        }
    }

    private List<FirmwareItemStatus> SwitchStatus()
    {
        FirmwareItemStatus Key(string name, KeyFileKind kind, bool required)
        {
            var files = new[] { Path.Combine(_loc.SwitchKeys, name), Path.Combine(_loc.Bios, name) }
                .Concat(_loc.EdenKeys == null ? [] : [Path.Combine(_loc.EdenKeys, name)])
                .Where(f => new FileInfo(f).Exists && new FileInfo(f).Length > 0)
                .ToList();
            if (files.Count == 0) return new FirmwareItemStatus(name, FirmwareItemState.Missing, required);
            return new FirmwareItemStatus(name, files.Any(f => FirmwareFiles.ClassifyFile(f) == kind) ? FirmwareItemState.Present : FirmwareItemState.Unknown, required);
        }
        var nca = CountNca(_loc.FirmwareTarget);
        if (nca == 0 && _loc.EdenFirmware != null) nca = CountNca(_loc.SwitchFirmwareStage);
        return
        [
            Key(EdenSetup.ProdKeys, KeyFileKind.SwitchProdKeys, true),
            Key(EdenSetup.TitleKeys, KeyFileKind.SwitchTitleKeys, false),
            new FirmwareItemStatus("Firmware", nca >= FirmwareFiles.MinFirmwareFiles ? FirmwareItemState.Present
                : nca > 0 ? FirmwareItemState.Unknown : FirmwareItemState.Missing, true, nca),
        ];
    }

    internal static int CountNca(string dir)
    {
        try { return Directory.Exists(dir) ? Directory.EnumerateFiles(dir, "*.nca").Count(f => FirmwareFiles.IsNcaName(Path.GetFileName(f))) : 0; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return 0; }
    }

    private FirmwareItemStatus WiiUStatus()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in new[] { _loc.WiiUKeys, _loc.CemuKeys })
        {
            if (file == null || !File.Exists(file)) continue;
            try
            {
                foreach (var k in CemuSetup.ParseKeys(File.ReadAllText(file)))
                    if (!k.Comment.Contains("example key", StringComparison.OrdinalIgnoreCase)) keys.Add(k.Key);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log.Warn($"Cannot read {file}", ex); }
        }
        return new FirmwareItemStatus(CemuSetup.KeysFile, keys.Count > 0 ? FirmwareItemState.Present : FirmwareItemState.Missing, false, keys.Count);
    }

    private FirmwareItemStatus Ps2Status()
    {
        int good = 0, other = 0;
        if (Directory.Exists(_loc.Ps2Bios))
            foreach (var f in Directory.EnumerateFiles(_loc.Ps2Bios))
            {
                if (f.Contains(EdenSetup.BackupInfix, StringComparison.Ordinal)) continue;
                if (FirmwareFiles.IsPs2BiosFile(f)) good++;
                else if (Path.GetExtension(f).Equals(".bin", StringComparison.OrdinalIgnoreCase)) other++;
            }
        var state = good > 0 ? FirmwareItemState.Present : other > 0 ? FirmwareItemState.Unknown : FirmwareItemState.Missing;
        return new FirmwareItemStatus("PS2-BIOS", state, true, good);
    }

    // ------------------------------------------------------------------ import

    /// <summary>A file to look at: from disk or from a .zip (opened lazily).</summary>
    private sealed class Candidate
    {
        public required string Name { get; init; }
        /// <summary>Path (or "archive.zip/inner/path") for ranking and messages – never shown with key content.</summary>
        public required string FullName { get; init; }
        /// <summary>The folder it sits in (firmware files are grouped by it).</summary>
        public required string Group { get; init; }
        public required long Size { get; init; }
        public required Func<Stream> Open { get; init; }
        public string? DiskPath { get; init; }

        private byte[]? _bytes;
        private string? _md5;
        public byte[] Bytes()
        {
            if (_bytes != null) return _bytes;
            using var s = Open();
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            return _bytes = ms.ToArray();
        }
        public string Md5() => _md5 ??= Convert.ToHexStringLower(MD5.HashData(Bytes()));
        public bool InDsiFolder => FullName.Replace('\\', '/').Split('/').SkipLast(1).Any(p => p.Contains("dsi", StringComparison.OrdinalIgnoreCase));
    }

    /// <param name="sources">Files, folders and .zip archives the player picked.</param>
    /// <param name="progress">Optional: number of firmware files copied so far.</param>
    public FirmwareImportResult Import(IEnumerable<string> sources, IProgress<int>? progress = null, CancellationToken cancel = default)
    {
        var items = new List<ImportedItem>();
        var skipped = new List<SkippedFile>();
        var zips = new List<ZipArchive>();
        bool truncated = false;
        try
        {
            var candidates = new List<Candidate>();
            foreach (var src in sources.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (candidates.Count >= MaxFiles) { truncated = true; break; }
                Collect(src, candidates, zips, skipped, ref truncated);
            }
            cancel.ThrowIfCancellationRequested();
            Recognise(candidates, items, skipped, progress, cancel);
        }
        catch (OperationCanceledException) { Log.Info("Firmware import cancelled"); }
        catch (Exception ex)
        {
            Log.Warn("Firmware import failed", ex);
            items.Add(new ImportedItem("", "", ImportOutcome.Failed, ""));
        }
        finally
        {
            foreach (var z in zips) z.Dispose();
        }
        foreach (var i in items)
            Log.Info($"Firmware import: {i.ConsoleId} {i.Name} ← {i.Source}: {i.Outcome}{(i.Count > 1 ? $" ({i.Count})" : "")}");
        if (skipped.Count > 0) Log.Info($"Firmware import: {skipped.Count} file(s) not recognised");
        return new FirmwareImportResult(items, skipped, truncated);
    }

    private static void Collect(string src, List<Candidate> list, List<ZipArchive> zips, List<SkippedFile> skipped, ref bool truncated)
    {
        try
        {
            if (Directory.Exists(src))
            {
                var options = new EnumerationOptions
                {
                    RecurseSubdirectories = true, MaxRecursionDepth = MaxDepth, IgnoreInaccessible = true,
                    AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System,
                };
                foreach (var f in Directory.EnumerateFiles(src, "*", options))
                {
                    if (list.Count >= MaxFiles) { truncated = true; return; }
                    AddFile(f, list, zips, skipped);
                }
            }
            else if (File.Exists(src)) AddFile(src, list, zips, skipped);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Firmware import: cannot read {src}", ex);
            skipped.Add(new SkippedFile(Path.GetFileName(src), SkipReason.Unreadable));
        }
    }

    private static readonly string[] OtherArchives = [".7z", ".rar", ".tar", ".gz", ".xz"];

    private static void AddFile(string path, List<Candidate> list, List<ZipArchive> zips, List<SkippedFile> skipped)
    {
        var name = Path.GetFileName(path);
        if (name.Contains(EdenSetup.BackupInfix, StringComparison.Ordinal) || name.EndsWith(SafeFile.TempSuffix, StringComparison.Ordinal)) return;
        var ext = Path.GetExtension(path);
        if (ext.Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            ZipArchive zip;
            try { zip = ZipFile.OpenRead(path); }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                // a .zip that is no archive: Neo Geo BIOS sets are .zip files too, but they open – this one is broken
                Log.Warn($"Firmware import: {path} is not a readable zip", ex);
                skipped.Add(new SkippedFile(name, SkipReason.Unreadable));
                return;
            }
            // a ROM set zip (neogeo.zip) is itself the BIOS: keep the archive as one candidate as well
            list.Add(FromDisk(path));
            zips.Add(zip);
            foreach (var entry in zip.Entries)
            {
                if (entry.FullName.EndsWith('/') || entry.Length == 0) continue;
                if (list.Count >= MaxFiles) return;
                var e = entry;
                var inner = e.FullName.Replace('\\', '/');
                var dir = inner.Contains('/') ? inner[..inner.LastIndexOf('/')] : "";
                list.Add(new Candidate
                {
                    Name = e.Name, FullName = path + "/" + inner, Group = path + "|" + dir, Size = e.Length, Open = e.Open,
                });
            }
            return;
        }
        if (OtherArchives.Contains(ext, StringComparer.OrdinalIgnoreCase))
        {
            skipped.Add(new SkippedFile(name, SkipReason.UnsupportedArchive));
            return;
        }
        list.Add(FromDisk(path));
    }

    private static Candidate FromDisk(string path)
    {
        var full = Path.GetFullPath(path);
        return new Candidate
        {
            Name = Path.GetFileName(full), FullName = full, Group = Path.GetDirectoryName(full) ?? "",
            Size = new FileInfo(full).Length, DiskPath = full,
            Open = () => new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite),
        };
    }

    private void Recognise(List<Candidate> all, List<ImportedItem> items, List<SkippedFile> skipped, IProgress<int>? progress, CancellationToken cancel)
    {
        var hasSwitch = _consoles.Any(c => c.Kind == FirmwareConsoleKind.Switch);
        var hasWiiU = _consoles.Any(c => c.Kind == FirmwareConsoleKind.WiiU);
        var hasPs2 = _consoles.Any(c => c.Kind == FirmwareConsoleKind.Ps2);
        var used = new HashSet<Candidate>();
        var zipsAsWhole = all.Where(c => c.DiskPath != null && c.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)).ToList();

        // 0. data folders (PPSSPP assets, blueMSX Machines/Databases), found by their anchor file
        foreach (var con in _consoles.Where(c => c.Kind == FirmwareConsoleKind.Assets))
            foreach (var asset in con.Assets)
            {
                var anchor = all.FirstOrDefault(c => !used.Contains(c) && c.Name.Equals(asset.Anchor, StringComparison.OrdinalIgnoreCase)
                    && AssetRoot(c, asset) != null);
                if (anchor == null) continue;
                var root = AssetRoot(anchor, asset)!;
                var files = all.Where(c => !used.Contains(c) && Norm(c.FullName).StartsWith(root + "/", StringComparison.OrdinalIgnoreCase)).ToList();
                items.Add(CopyAssets(con, asset, root, files, cancel));
                used.UnionWith(files);
            }

        // 1. Switch firmware: the biggest folder with enough .nca files
        var ncas = all.Where(c => FirmwareFiles.IsNcaName(c.Name)).ToList();
        if (ncas.Count > 0)
        {
            var groups = ncas.GroupBy(c => c.Group, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Count()).ToList();
            var firmware = groups.FirstOrDefault(g => g.Count() >= FirmwareFiles.MinFirmwareFiles);
            if (firmware != null && hasSwitch)
            {
                var files = firmware.ToList();
                items.Add(InstallFirmware(files, progress, cancel));
                used.UnionWith(files);
            }
            foreach (var c in ncas.Where(c => !used.Contains(c)))
            {
                skipped.Add(new SkippedFile(c.Name, SkipReason.LoneNca));
                used.Add(c);
            }
        }

        // 2. key files (small text) and Wii U disc keys
        var wiiuKeys = new List<(string Key, string Comment)>();
        var wiiuSources = new List<string>();
        foreach (var c in all.Where(c => !used.Contains(c) && c.Size is > 0 and <= FirmwareFiles.MaxKeyFileSize))
        {
            cancel.ThrowIfCancellationRequested();
            if (c.Name.EndsWith(CemuSetup.KeyExtension, StringComparison.OrdinalIgnoreCase) && c.Size <= 1024 && hasWiiU)
            {
                var key = DiscKey(c.Bytes());
                if (key != null)
                {
                    wiiuKeys.Add((key, $"{c.Name} {CemuSetup.ImportedComment}"));
                    wiiuSources.Add(c.Name);
                    used.Add(c);
                    continue;
                }
            }
            if (!LooksLikeText(c.Name)) continue;
            byte[] bytes;
            try { bytes = c.Bytes(); }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { continue; }
            switch (FirmwareFiles.Classify(bytes))
            {
                case KeyFileKind.SwitchProdKeys when hasSwitch && !c.Name.StartsWith("dev", StringComparison.OrdinalIgnoreCase):
                    items.Add(SwitchKey(EdenSetup.ProdKeys, bytes, c.Name));
                    used.Add(c);
                    break;
                case KeyFileKind.SwitchTitleKeys when hasSwitch:
                    items.Add(SwitchKey(EdenSetup.TitleKeys, bytes, c.Name));
                    used.Add(c);
                    break;
                case KeyFileKind.WiiUKeys when hasWiiU:
                    foreach (var k in CemuSetup.ParseKeys(System.Text.Encoding.UTF8.GetString(bytes)))
                        wiiuKeys.Add((k.Key, k.Comment));
                    wiiuSources.Add(c.Name);
                    used.Add(c);
                    break;
            }
        }
        if (wiiuKeys.Count > 0) items.Add(MergeWiiUKeys(wiiuKeys, string.Join(", ", wiiuSources.Distinct())));

        // 3. catalog BIOS: by MD5 first (any name), then by name and size
        var biosConsoles = _consoles.Where(c => c.Kind == FirmwareConsoleKind.CatalogBios).ToList();
        var byMd5 = new Dictionary<string, (FirmwareConsole Console, BiosFile File)>(StringComparer.OrdinalIgnoreCase);
        foreach (var con in biosConsoles)
            foreach (var b in con.Bios)
                foreach (var m in b.Md5) byMd5.TryAdd(m, (con, b));
        var picks = new Dictionary<string, (FirmwareConsole Console, BiosFile File, Candidate Source, bool Known, int Rank)>(StringComparer.OrdinalIgnoreCase);
        void Offer(FirmwareConsole con, BiosFile file, Candidate c, bool known)
        {
            var rank = (known ? 0 : 2) + (c.InDsiFolder ? 1 : 0);
            if (!picks.TryGetValue(file.File, out var have) || rank < have.Rank) picks[file.File] = (con, file, c, known, rank);
        }
        foreach (var c in all.Where(c => !used.Contains(c)))
        {
            cancel.ThrowIfCancellationRequested();
            if (c.Size is > 0 and <= FirmwareFiles.MaxHashSize && byMd5.Count > 0)
            {
                string md5;
                try { md5 = c.Md5(); }
                catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
                {
                    skipped.Add(new SkippedFile(c.Name, SkipReason.Unreadable));
                    used.Add(c);
                    continue;
                }
                if (byMd5.TryGetValue(md5, out var hit))
                {
                    Offer(hit.Console, hit.File, c, true);
                    used.Add(c);
                    continue;
                }
            }
            var byName = biosConsoles.SelectMany(con => con.Bios.Select(b => (con, b)))
                .Where(x => Path.GetFileName(x.b.File).Equals(c.Name, StringComparison.OrdinalIgnoreCase)).ToList();
            if (byName.Count == 0) continue;
            // a zip read entry by entry also offers the zip itself – an entry never stands for neogeo.zip
            var fits = byName.Where(x => x.b.Sizes.Length == 0 || x.b.Sizes.Contains(c.Size)).ToList();
            if (fits.Count == 0)
            {
                skipped.Add(new SkippedFile(c.Name, SkipReason.WrongSize));
                used.Add(c);
                continue;
            }
            foreach (var (con, b) in fits) Offer(con, b, c, false);
            used.Add(c);
        }
        foreach (var p in picks.Values.OrderBy(p => p.Console.SortOrder).ThenBy(p => p.File.File, StringComparer.OrdinalIgnoreCase))
            items.Add(CopyBios(p.Console, p.File, p.Source, p.Known));

        // 4. PS2 BIOS by content
        if (hasPs2)
            foreach (var c in all.Where(c => !used.Contains(c) && c.Size is >= 512 * 1024 and <= 8 * 1024 * 1024))
            {
                cancel.ThrowIfCancellationRequested();
                bool isBios;
                try { using var s = c.Open(); isBios = FirmwareFiles.IsPs2Bios(s, c.Size); }
                catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { isBios = false; }
                if (!isBios) continue;
                items.Add(CopyPs2(c));
                used.Add(c);
            }

        // 5. the rest: unknown (a zip that was read entry by entry is not itself "unknown")
        foreach (var c in all.Where(c => !used.Contains(c)))
        {
            if (zipsAsWhole.Contains(c)) continue;
            skipped.Add(new SkippedFile(c.Name, SkipReason.Unknown));
        }
    }

    private static string Norm(string path) => path.Replace('\\', '/').TrimEnd('/');

    /// <summary>The asset folder that holds the anchor (AnchorDepth folders above the anchor's own folder), or null.</summary>
    private static string? AssetRoot(Candidate anchor, AssetFolder asset)
    {
        // the anchor must sit where it sits in the target ("Shared Roms/MSX.rom" below Machines/)
        var inside = asset.StatusFile[(asset.StatusFile.IndexOf('/') + 1)..];
        if (!Norm(anchor.FullName).EndsWith("/" + inside, StringComparison.OrdinalIgnoreCase)) return null;
        var parts = Norm(anchor.FullName).Split('/');
        var dirCount = parts.Length - 1 - asset.AnchorDepth;
        return dirCount <= 0 ? null : string.Join('/', parts[..dirCount]);
    }

    /// <summary>Copies the folder below bios/&lt;target&gt; (files already there unchanged are skipped; different ones replaced).</summary>
    private ImportedItem CopyAssets(FirmwareConsole console, AssetFolder asset, string root, List<Candidate> files, CancellationToken cancel)
    {
        var source = root[(root.LastIndexOf('/') + 1)..];
        var target = Path.Combine(_loc.Bios, asset.Target);
        int written = 0;
        try
        {
            foreach (var f in files)
            {
                cancel.ThrowIfCancellationRequested();
                var rel = Norm(f.FullName)[(root.Length + 1)..];
                if (!BiosCheck.IsSafeRelative(rel)) continue;
                var dst = Path.GetFullPath(Path.Combine(target, rel.Replace('/', Path.DirectorySeparatorChar)));
                if (!AppPaths.IsInside(dst, target)) continue;
                if (File.Exists(dst) && new FileInfo(dst).Length == f.Size) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                using (var src = f.Open())
                using (var outStream = new FileStream(dst, FileMode.Create, FileAccess.Write, FileShare.None))
                    src.CopyTo(outStream);
                written++;
            }
            return new ImportedItem(console.Id, asset.Target + "/", written > 0 ? ImportOutcome.Imported : ImportOutcome.AlreadyPresent, source, Math.Max(written, files.Count));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Log.Warn($"Firmware import: copying {asset.Target} failed", ex);
            return new ImportedItem(console.Id, asset.Target + "/", ImportOutcome.Failed, source, written);
        }
    }

    private static readonly string[] TextExtensions = [".keys", ".txt", ".key", ".ini", ""];

    /// <summary>Key files: .keys/.txt (or no extension, "prod.keys.txt" style double extensions included).</summary>
    private static bool LooksLikeText(string name) =>
        TextExtensions.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase);

    /// <summary>A Wii U disc key file: 16 bytes, or 32 hex digits as text. Upper-case hex or null.</summary>
    private static string? DiscKey(byte[] bytes)
    {
        if (bytes.Length == 16) return Convert.ToHexString(bytes);
        var text = new string(System.Text.Encoding.UTF8.GetString(bytes).Where(c => !char.IsWhiteSpace(c) && c != '﻿').ToArray());
        return text.Length == 32 && text.All(Uri.IsHexDigit) ? text.ToUpperInvariant() : null;
    }

    // ------------------------------------------------------------------ writers

    private const string SwitchId = "switch", WiiUId = "wiiu", Ps2Id = "ps2";

    /// <summary>bios/switch/&lt;name&gt; and, when Eden is installed, Eden's user/keys/&lt;name&gt;.</summary>
    private ImportedItem SwitchKey(string name, byte[] bytes, string source)
    {
        try
        {
            var outcome = WriteFile(Path.Combine(_loc.SwitchKeys, name), bytes);
            if (_loc.EdenKeys != null)
            {
                var eden = WriteFile(Path.Combine(_loc.EdenKeys, name), bytes);
                if (outcome == ImportOutcome.AlreadyPresent) outcome = eden;
            }
            return new ImportedItem(SwitchId, name, outcome, source);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Firmware import: writing {name} failed", ex);
            return new ImportedItem(SwitchId, name, ImportOutcome.Failed, source);
        }
    }

    private ImportedItem MergeWiiUKeys(List<(string Key, string Comment)> keys, string source)
    {
        try
        {
            var added = CemuSetup.MergeKeys(_loc.WiiUKeys, keys);
            if (_loc.CemuKeys != null) added = Math.Max(added, CemuSetup.MergeKeys(_loc.CemuKeys, keys));
            return new ImportedItem(WiiUId, CemuSetup.KeysFile, added > 0 ? ImportOutcome.Imported : ImportOutcome.AlreadyPresent, source, added);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("Firmware import: merging the Wii U keys failed", ex);
            return new ImportedItem(WiiUId, CemuSetup.KeysFile, ImportOutcome.Failed, source);
        }
    }

    private ImportedItem CopyBios(FirmwareConsole console, BiosFile file, Candidate source, bool known)
    {
        var name = Path.GetFileName(file.File);
        try
        {
            if (!BiosCheck.IsSafeRelative(file.File)) return new ImportedItem(console.Id, name, ImportOutcome.Failed, source.Name);
            var target = Path.GetFullPath(Path.Combine(_loc.Bios, file.File.Replace('/', Path.DirectorySeparatorChar)));
            if (!AppPaths.IsInside(target, _loc.Bios)) return new ImportedItem(console.Id, name, ImportOutcome.Failed, source.Name);
            if (!known && file.Md5.Length > 0 && File.Exists(target))
            {
                var have = BiosCheck.Md5Of(target);
                if (have != null && file.Md5.Contains(have, StringComparer.OrdinalIgnoreCase))
                    return new ImportedItem(console.Id, name, ImportOutcome.KeptExisting, source.Name);
            }
            var outcome = WriteFile(target, source.Bytes());
            if (!known && file.Md5.Length > 0 && outcome is ImportOutcome.Imported or ImportOutcome.Replaced)
                outcome = ImportOutcome.ImportedUnknownVersion;
            return new ImportedItem(console.Id, name, outcome, source.Name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Log.Warn($"Firmware import: copying {name} failed", ex);
            return new ImportedItem(console.Id, name, ImportOutcome.Failed, source.Name);
        }
    }

    private ImportedItem CopyPs2(Candidate source)
    {
        try
        {
            var name = AppPaths.IsSafeName(source.Name) ? source.Name : "ps2-bios.bin";
            return new ImportedItem(Ps2Id, name, WriteFile(Path.Combine(_loc.Ps2Bios, name), source.Bytes()), source.Name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Log.Warn("Firmware import: copying the PS2 BIOS failed", ex);
            return new ImportedItem(Ps2Id, source.Name, ImportOutcome.Failed, source.Name);
        }
    }

    /// <summary>Writes the bytes unless the same file is there; a different one is kept as "….retrohub-old-&lt;time&gt;".</summary>
    private static ImportOutcome WriteFile(string target, byte[] bytes)
    {
        if (File.Exists(target))
        {
            if (File.ReadAllBytes(target).AsSpan().SequenceEqual(bytes)) return ImportOutcome.AlreadyPresent;
            File.Copy(target, EdenSetup.BackupPath(target));
            SafeFile.WriteAllBytes(target, bytes);
            return ImportOutcome.Replaced;
        }
        SafeFile.WriteAllBytes(target, bytes);
        return ImportOutcome.Imported;
    }

    /// <summary>
    /// Copies the firmware files into a fresh "registered.retrohub-new" folder next to the target, then swaps it in: an
    /// old firmware folder with .nca files is kept as "registered.retrohub-old-&lt;time&gt;" (Eden's own "Install
    /// Firmware" empties the folder first, so two firmware versions never mix). The same set of files already there →
    /// nothing is written.
    /// </summary>
    private ImportedItem InstallFirmware(List<Candidate> files, IProgress<int>? progress, CancellationToken cancel)
    {
        var target = _loc.FirmwareTarget;
        var source = files[0].Group.Contains('|') ? Path.GetFileName(files[0].Group[..files[0].Group.IndexOf('|')]) : Path.GetFileName(files[0].Group);
        try
        {
            if (Directory.Exists(target))
            {
                var present = Directory.EnumerateFiles(target).ToDictionary(f => Path.GetFileName(f), f => new FileInfo(f).Length, StringComparer.OrdinalIgnoreCase);
                if (present.Count == files.Count && files.All(f => present.TryGetValue(f.Name, out var len) && len == f.Size))
                    return new ImportedItem(SwitchId, "Firmware", ImportOutcome.AlreadyPresent, source, files.Count);
            }
            var fresh = target.TrimEnd('\\', '/') + ".retrohub-new";
            if (Directory.Exists(fresh)) Directory.Delete(fresh, true);
            Directory.CreateDirectory(fresh);
            int done = 0;
            foreach (var f in files)
            {
                cancel.ThrowIfCancellationRequested();
                if (!AppPaths.IsSafeName(f.Name)) continue;
                using (var src = f.Open())
                using (var dst = new FileStream(Path.Combine(fresh, f.Name), FileMode.Create, FileAccess.Write, FileShare.None))
                    src.CopyTo(dst);
                done++;
                progress?.Report(done);
            }
            var replaced = false;
            if (Directory.Exists(target))
            {
                if (Directory.EnumerateFileSystemEntries(target).Any())
                {
                    Directory.Move(target, EdenSetup.BackupPath(target.TrimEnd('\\', '/')));
                    replaced = true;
                }
                else Directory.Delete(target);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(target.TrimEnd('\\', '/'))!);
            Directory.Move(fresh, target);
            return new ImportedItem(SwitchId, "Firmware", replaced ? ImportOutcome.Replaced : ImportOutcome.Imported, source, done);
        }
        catch (OperationCanceledException)
        {
            TryDelete(target.TrimEnd('\\', '/') + ".retrohub-new");
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Log.Warn($"Firmware import: installing the Switch firmware into {target} failed", ex);
            TryDelete(target.TrimEnd('\\', '/') + ".retrohub-new");
            return new ImportedItem(SwitchId, "Firmware", ImportOutcome.Failed, source, 0);
        }
    }

    private static void TryDelete(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log.Warn($"Cannot delete {dir}", ex); }
    }
}
