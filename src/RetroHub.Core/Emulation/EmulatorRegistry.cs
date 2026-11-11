using System.Diagnostics;
using System.Text.RegularExpressions;
using RetroHub.Core.Catalog;
using RetroHub.Core.Config;
using RetroHub.Core.IO;
using RetroHub.Core.Logging;

namespace RetroHub.Core.Emulation;

/// <summary>
/// Finds the installed emulators of the catalog.
/// <list type="number">
/// <item>User path from settings (<see cref="AppSettings.EmulatorPaths"/>, portable) — wins when the file exists.</item>
/// <item>emulators/&lt;id&gt;/** (up to 3 folder levels below it) for <see cref="EmulatorDef.Exe"/> patterns:
/// case-insensitive, '*' / '?' wildcards, the first pattern with a match wins; among matches of one pattern
/// x64 builds first, then the shallowest, then the shortest name (so "azahar*.exe" picks azahar.exe, not
/// azahar-room.exe). Wildcard matches never pick uninstallers/updaters/crash handlers.</item>
/// <item>All of emulators/** (3 levels) for the exact (wildcard-free) patterns, for unusual folder names.</item>
/// </list>
/// Version: FileVersionInfo ProductVersion → FileVersion (without "+hash" suffixes), else a version-like token
/// from the folder/exe name ("mGBA-0.10.5-win64" → "0.10.5"). RetroArch has no version resource and a version-less
/// folder ("RetroArch-Win64"), so for it <c>retroarch.exe --version</c> is run once (hidden, stdout/stderr redirected,
/// 3 s timeout), falling back to the "Version: x.y.z" string inside the binary; the result is cached by exe path + size +
/// mtime in data/cache/emulator-versions.json (<see cref="EmulatorVersionCache"/>). Dolphin has no version resource either:
/// its binary is read (never run) for "Dolphin 2609b", cached the same way. RetroArch: <see cref="EmulatorInstall.InstalledCores"/>
/// = cores/*_libretro.dll next to the exe. Detection is read-only and never throws (unreadable folders are skipped).
/// </summary>
public sealed class EmulatorRegistry : IEmulatorRegistry
{
    private const int MaxDepth = 3;
    private static readonly string[] SideTools = ["unins", "uninst", "update", "crash", "setup", "redist", "vcredist", "dxwebsetup"];
    private static readonly Regex VersionToken = new(@"(?<![\d.])v?(\d+(?:\.\d+){1,3})(?![\d.]*\d)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly ICatalog _catalog;
    private readonly AppPaths _paths;
    private readonly SettingsStore _settings;
    private readonly object _gate = new();
    private readonly EmulatorVersionCache _versions;
    private IReadOnlyList<EmulatorInstall> _all = [];
    private Dictionary<string, EmulatorInstall> _byId = new(StringComparer.OrdinalIgnoreCase);

    public event Action? Changed;

    /// <param name="versionCacheFile">Where probed versions are cached; default data/cache/emulator-versions.json.</param>
    public EmulatorRegistry(ICatalog catalog, AppPaths paths, SettingsStore settings, string? versionCacheFile = null)
    {
        _catalog = catalog;
        _paths = paths;
        _settings = settings;
        _versions = new EmulatorVersionCache(versionCacheFile ?? Path.Combine(paths.Cache, "emulator-versions.json"));
        Scan();
    }

    public IReadOnlyList<EmulatorInstall> All => Volatile.Read(ref _all);

    public EmulatorInstall? Get(string emulatorId)
    {
        if (string.IsNullOrEmpty(emulatorId)) return null;
        return Volatile.Read(ref _byId).TryGetValue(emulatorId, out var e) ? e : null;
    }

    public void Refresh()
    {
        Scan();
        RaiseChanged();
    }

    public void SetUserPath(string emulatorId, string? exePath)
    {
        try
        {
            string? stored = string.IsNullOrWhiteSpace(exePath) ? null : _paths.ToPortable(exePath);
            try
            {
                _settings.Update(s =>
                {
                    // a hand-edited settings.json may contain "emulatorPaths": null
                    s.EmulatorPaths ??= new Dictionary<string, string>();
                    if (stored == null) s.EmulatorPaths.Remove(emulatorId);
                    else s.EmulatorPaths[emulatorId] = stored;
                });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Update changes Current before saving, so the new path is used in this session anyway
                Log.Warn($"Could not save the emulator path for {emulatorId}", ex);
            }
            if (stored != null && !File.Exists(_paths.FromPortable(stored)))
                Log.Warn($"User path for {emulatorId} does not exist: {exePath}");
        }
        catch (Exception ex)
        {
            Log.Error($"SetUserPath({emulatorId}) failed", ex);
        }
        Refresh();
    }

    private void RaiseChanged()
    {
        try { Changed?.Invoke(); }
        catch (Exception ex) { Log.Error("EmulatorRegistry.Changed handler failed", ex); }
    }

    private void Scan()
    {
        lock (_gate)
        {
            var sw = Stopwatch.StartNew();
            var list = new List<EmulatorInstall>();
            List<string>? everything = null; // lazily listed emulators/** for the global fallback
            IReadOnlyList<EmulatorDef> defs;
            try { defs = _catalog.Emulators; }
            catch (Exception ex)
            {
                Log.Error("Catalog emulators unavailable", ex);
                defs = [];
            }
            foreach (var def in defs)
            {
                try { list.Add(Detect(def, ref everything)); }
                catch (Exception ex)
                {
                    Log.Warn($"Detecting {def.Id} failed", ex);
                    list.Add(new EmulatorInstall { Def = def });
                }
            }
            var byId = new Dictionary<string, EmulatorInstall>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in list) byId.TryAdd(e.Def.Id, e);
            Volatile.Write(ref _byId, byId);
            Volatile.Write(ref _all, list);
            _versions.Save();
            Log.Info($"Emulators: {list.Count(e => e.ExePath != null)}/{list.Count} installed ({sw.ElapsedMilliseconds} ms): " +
                     string.Join(", ", list.Where(e => e.ExePath != null).Select(e => $"{e.Def.Id} {e.Version}".TrimEnd())));
        }
    }

    private EmulatorInstall Detect(EmulatorDef def, ref List<string>? everything)
    {
        // a native "emulator" is the game exe itself: nothing to find
        if (def.Kind == EmulatorKind.Native) return new EmulatorInstall { Def = def };

        string? exe = null;
        bool user = false;
        var settings = _settings.Current;
        // EmulatorPaths is null when settings.json says "emulatorPaths": null (hand-edited) — treat as empty
        if (settings.EmulatorPaths?.TryGetValue(def.Id, out var stored) == true && !string.IsNullOrWhiteSpace(stored))
        {
            try
            {
                var full = _paths.FromPortable(stored);
                if (File.Exists(full)) { exe = full; user = true; }
                else Log.Warn($"Configured path for {def.Id} not found, auto-detecting: {full}");
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                Log.Warn($"Invalid configured path for {def.Id}: {stored}", ex);
            }
        }

        if (exe == null && AppPaths.IsSafeName(def.Id))
        {
            var own = Path.Combine(_paths.Emulators, def.Id);
            if (Directory.Exists(own)) exe = FindExe(def.Exe, own, ListFiles(own), exactOnly: false);
        }
        if (exe == null && def.Exe.Any(p => !HasWildcard(p)))
        {
            everything ??= ListFiles(_paths.Emulators);
            exe = FindExe(def.Exe, _paths.Emulators, everything, exactOnly: true);
        }

        if (exe == null) return new EmulatorInstall { Def = def };
        return new EmulatorInstall
        {
            Def = def,
            ExePath = exe,
            UserConfigured = user,
            Version = ReadVersion(exe, _paths.Emulators) ?? ProbeVersion(def, exe),
            InstalledCores = def.Kind == EmulatorKind.RetroArch ? Cores(exe) : [],
        };
    }

    /// <summary>
    /// Exes without a version resource: RetroArch answers "--version"; Dolphin (no resource, folder "Dolphin-x64") is
    /// only read, never run — its binary carries "Dolphin 2609b" / the user agent "Dolphin/2609b". Cached like RetroArch.
    /// </summary>
    private string? ProbeVersion(EmulatorDef def, string exe)
    {
        if (def.Kind == EmulatorKind.RetroArch) return _versions.GetOrProbe(exe, EmulatorVersionProbe.RetroArch);
        if (EmulatorVersionProbe.IsDolphin(def, exe)) return _versions.GetOrProbe(exe, EmulatorVersionProbe.DolphinFromBinary);
        return null;
    }

    private static bool HasWildcard(string pattern) => pattern.IndexOfAny(['*', '?']) >= 0;

    /// <summary>First pattern with a match wins; within a pattern: x64 first, shallowest, shortest name.</summary>
    internal static string? FindExe(IEnumerable<string> patterns, string root, List<string> files, bool exactOnly)
    {
        foreach (var raw in patterns)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var pattern = raw.Trim().Replace('\\', '/');
            if (pattern.Split('/').Any(part => part == ".." || part == ".")) continue; // no traversal from plugin JSON
            bool wildcard = HasWildcard(pattern);
            if (exactOnly && wildcard) continue;
            var regex = new Regex("^" + Regex.Escape(pattern).Replace(@"\*", ".*").Replace(@"\?", ".") + "$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            bool byPath = pattern.Contains('/');
            var hits = files.Where(rel =>
                {
                    var name = rel[(rel.LastIndexOf('/') + 1)..];
                    if (!regex.IsMatch(byPath ? rel : name) && !(byPath && SuffixMatch(rel, regex, pattern))) return false;
                    return !wildcard || !IsSideTool(name);
                })
                .OrderByDescending(rel => rel.Contains("x64", StringComparison.OrdinalIgnoreCase) || rel.Contains("win64", StringComparison.OrdinalIgnoreCase))
                .ThenBy(rel => rel.Count(c => c == '/'))
                .ThenBy(rel => rel.Length - rel.LastIndexOf('/'))
                .ThenBy(rel => rel, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (hits.Count > 0) return Path.GetFullPath(Path.Combine(root, hits[0].Replace('/', Path.DirectorySeparatorChar)));
        }
        return null;
    }

    /// <summary>A pattern with a subfolder ("bin/foo.exe") may match the end of a deeper relative path.</summary>
    private static bool SuffixMatch(string rel, Regex regex, string pattern)
    {
        int segments = pattern.Count(c => c == '/') + 1;
        var parts = rel.Split('/');
        if (parts.Length < segments) return false;
        return regex.IsMatch(string.Join('/', parts[^segments..]));
    }

    private static bool IsSideTool(string fileName)
    {
        var n = fileName.ToLowerInvariant();
        return SideTools.Any(t => n.Contains(t));
    }

    /// <summary>Files up to <see cref="MaxDepth"/> folder levels below root, relative with '/'. Never throws.</summary>
    internal static List<string> ListFiles(string root)
    {
        var list = new List<string>();
        try
        {
            if (!Directory.Exists(root)) return list;
            var options = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                RecurseSubdirectories = true,
                MaxRecursionDepth = MaxDepth,
                AttributesToSkip = FileAttributes.ReparsePoint,
                ReturnSpecialDirectories = false,
            };
            foreach (var f in Directory.EnumerateFiles(root, "*", options))
                list.Add(Path.GetRelativePath(root, f).Replace('\\', '/'));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            Log.Warn($"Cannot list {root}", ex);
        }
        return list;
    }

    /// <summary>Product/file version of the exe, else a version from the folder or exe name; null when none.</summary>
    internal static string? ReadVersion(string exe, string emulatorsRoot)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(exe);
            foreach (var v in new[] { info.ProductVersion, info.FileVersion })
                if (CleanVersion(v) is { } clean) return clean;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Log.Debug($"No version resource in {exe}: {ex.Message}");
        }
        if (VersionFile(exe, emulatorsRoot) is { } fromFile) return fromFile;
        // folder names from the exe's folder up to (excluding) emulators/, then the exe name itself
        var names = new List<string>();
        try
        {
            var dir = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(exe))!);
            var stop = Path.GetFullPath(emulatorsRoot).TrimEnd('\\', '/');
            for (var d = dir; d != null && !string.Equals(d.FullName.TrimEnd('\\', '/'), stop, StringComparison.OrdinalIgnoreCase); d = d.Parent)
            {
                names.Add(d.Name);
                if (names.Count >= MaxDepth + 1) break;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException) { _ = ex; }
        names.Add(Path.GetFileNameWithoutExtension(exe));
        foreach (var n in names)
            if (VersionFromName(n) is { } v) return v;
        return null;
    }

    /// <summary>Name of the optional version file the installer writes (emulators/&lt;id&gt;/version.txt).</summary>
    public const string VersionFileName = "version.txt";

    /// <summary>
    /// For exes without a version resource (Cemu, Azahar, Eden in the flat installer layout): the first line of a
    /// version.txt in the exe's folder or a parent folder below emulators/ ("2.6", "v0.2.1" → "0.2.1"). Only a
    /// version-like token is taken; a file larger than 256 bytes is ignored. Null when there is none.
    /// </summary>
    internal static string? VersionFile(string exe, string emulatorsRoot)
    {
        try
        {
            var stop = Path.GetFullPath(emulatorsRoot).TrimEnd('\\', '/');
            var dir = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(exe))!);
            for (int level = 0; dir != null && level <= MaxDepth; dir = dir.Parent, level++)
            {
                if (string.Equals(dir.FullName.TrimEnd('\\', '/'), stop, StringComparison.OrdinalIgnoreCase)) break;
                var file = new FileInfo(Path.Combine(dir.FullName, VersionFileName));
                if (!file.Exists) continue;
                if (file.Length is 0 or > 256) return null;
                var first = File.ReadLines(file.FullName).FirstOrDefault(l => l.Trim().Length > 0);
                return first == null ? null : VersionFromName(first.Trim());
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or PathTooLongException)
        {
            Log.Debug($"No version file for {exe}: {ex.Message}");
        }
        return null;
    }

    /// <summary>
    /// "1.19.1+abcdef" → "1.19.1"; "1, 2, 3, 4" → "1.2.3.4"; "0.10.5.0" → "0.10.5" (4-part padding);
    /// "mgba" (mGBA's real ProductVersion) / "0.0.0.0" / empty → null; "v2.1 (git)" → "2.1".
    /// </summary>
    internal static string? CleanVersion(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return null;
        v = v.Trim();
        int plus = v.IndexOf('+');
        if (plus >= 0) v = v[..plus];
        v = Regex.Replace(v, @"\s*,\s*", ".").Trim().TrimEnd('.');
        if (v.Length == 0 || !v.Any(char.IsDigit)) return null;
        if (!char.IsDigit(v[0]))
        {
            // text around the number ("v2.1", "melonDS 1.1"): take the version token, else give up
            var m = VersionToken.Match(v);
            if (!m.Success) return null;
            v = m.Groups[1].Value;
        }
        if (Regex.IsMatch(v, @"^[0.\s]+$")) return null;
        var four = Regex.Match(v, @"^(\d+\.\d+\.\d+)\.0$");
        return four.Success ? four.Groups[1].Value : v;
    }

    /// <summary>Version-like token (digits with 1–3 dots) from a folder/file name, or null.</summary>
    internal static string? VersionFromName(string name)
    {
        var m = VersionToken.Match(name);
        return m.Success ? m.Groups[1].Value : null;
    }

    /// <summary>RetroArch: names of cores/*_libretro.dll next to the exe (sorted).</summary>
    internal static IReadOnlyList<string> Cores(string exe)
    {
        try
        {
            var dir = Path.Combine(Path.GetDirectoryName(exe)!, "cores");
            if (!Directory.Exists(dir)) return [];
            const string suffix = "_libretro.dll";
            return Directory.EnumerateFiles(dir, "*" + suffix, new EnumerationOptions { IgnoreInaccessible = true })
                .Select(Path.GetFileName)
                .OfType<string>()
                .Where(f => f.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && f.Length > suffix.Length)
                .Select(f => f[..^suffix.Length])
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Cannot list RetroArch cores next to {exe}", ex);
            return [];
        }
    }
}

/// <summary>
/// Version probes for exes without a version resource. Only used for emulators known to answer a version switch
/// without side effects (RetroArch prints "Version: 1.22.2 (Git …)" to stdout and exits; verified to write no files).
/// </summary>
internal static partial class EmulatorVersionProbe
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

    [GeneratedRegex(@"(?:Version:\s*v?|--\s*v)(\d+\.\d+(?:\.\d+){0,2})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VersionLine();

    [GeneratedRegex(@"RetroArch(?:-WIMP)?/(\d+\.\d+(?:\.\d+){0,2})", RegexOptions.CultureInvariant)]
    private static partial Regex UserAgentToken();

    [GeneratedRegex(@"Dolphin[ /](\d{4}[a-z]?(?:-\d{1,6})?|\d+\.\d+(?:\.\d+)?(?:-\d{1,6})?)\0", RegexOptions.CultureInvariant)]
    private static partial Regex DolphinToken();

    /// <summary>Dolphin by catalog id or exe name ("Dolphin.exe"; not DolphinTool/DolphinNoGUI's helper tools).</summary>
    public static bool IsDolphin(EmulatorDef def, string exe) =>
        string.Equals(def.Id, "dolphin", StringComparison.OrdinalIgnoreCase)
        || string.Equals(Path.GetFileNameWithoutExtension(exe), "Dolphin", StringComparison.OrdinalIgnoreCase);

    /// <summary>Dolphin: the version descriptor in the binary (read only, never executed); null when none.</summary>
    public static string? DolphinFromBinary(string exe)
    {
        try
        {
            var info = new FileInfo(exe);
            if (!info.Exists || info.Length > 256L * 1024 * 1024) return null;
            return DolphinFromBinaryText(System.Text.Encoding.Latin1.GetString(File.ReadAllBytes(exe)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OutOfMemoryException)
        {
            Log.Debug($"Reading {exe} for a Dolphin version failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// "…\0Dolphin 2609b\0…Dolphin/2609b\0" → "2609b"; old builds "Dolphin 5.0-21460" → "5.0-21460". Only whole
    /// zero-terminated strings count ("Dolphin 2003-2024 team" in a licence text does not); the most frequent wins.
    /// </summary>
    internal static string? DolphinFromBinaryText(string text)
    {
        var best = DolphinToken().Matches(text).Select(m => m.Groups[1].Value)
            .GroupBy(v => v).OrderByDescending(g => g.Count()).ThenByDescending(g => g.Key.Length).FirstOrDefault();
        return best?.Key;
    }

    /// <summary>RetroArch: "--version" output, else the version string embedded in the binary.</summary>
    public static string? RetroArch(string exe) => FromProcess(exe, "--version") ?? FromBinary(exe);

    /// <summary>
    /// "Version: 1.22.2 (Git 69a4f0e) Nov 20 2025" → "1.22.2"; old builds: "RetroArch: Frontend for libretro -- v1.9.0 -- …" → "1.9.0".
    /// </summary>
    public static string? ParseVersionOutput(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var m = VersionLine().Match(text);
        return m.Success ? m.Groups[1].Value : null;
    }

    /// <summary>Runs the exe hidden with redirected output and a hard timeout (the whole process tree is killed then).</summary>
    public static string? FromProcess(string exe, string args)
    {
        if (!LooksLikeExe(exe)) return null;
        try
        {
            var psi = new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                WorkingDirectory = Path.GetDirectoryName(exe)!,
            };
            using var p = Process.Start(psi);
            if (p == null) return null;
            p.StandardInput.Close();
            var stdout = p.StandardOutput.ReadToEndAsync();
            var stderr = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(Timeout))
            {
                try { p.Kill(entireProcessTree: true); }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { _ = ex; }
                Log.Warn($"'{exe} {args}' did not exit within {Timeout.TotalSeconds:0} s – killed");
                return null;
            }
            var all = (stdout.Wait(1000) ? stdout.Result : "") + "\n" + (stderr.Wait(1000) ? stderr.Result : "");
            var v = ParseVersionOutput(all);
            Log.Debug($"'{Path.GetFileName(exe)} {args}' → {v ?? "(no version)"}");
            return v;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            Log.Debug($"Version probe '{exe} {args}' failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>Scans the binary for "Version: x.y.z" / "RetroArch-WIMP/x.y.z" (ASCII); the most frequent hit wins.</summary>
    public static string? FromBinary(string exe)
    {
        try
        {
            var info = new FileInfo(exe);
            if (!info.Exists || info.Length > 256L * 1024 * 1024) return null;
            return FromBinaryText(System.Text.Encoding.Latin1.GetString(File.ReadAllBytes(exe)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OutOfMemoryException)
        {
            Log.Debug($"Reading {exe} for a version string failed: {ex.Message}");
            return null;
        }
    }

    internal static string? FromBinaryText(string text)
    {
        var best = VersionLine().Matches(text).Select(m => m.Groups[1].Value)
            .Concat(UserAgentToken().Matches(text).Select(m => m.Groups[1].Value))
            .GroupBy(v => v).OrderByDescending(g => g.Count()).ThenByDescending(g => g.Key.Length).FirstOrDefault();
        return best?.Key;
    }

    /// <summary>A real PE file (starts with "MZ", at least 4 KB) — test fakes and placeholders are never executed.</summary>
    internal static bool LooksLikeExe(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length < 4096) return false;
            Span<byte> head = stackalloc byte[2];
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return fs.Read(head) == 2 && head[0] == (byte)'M' && head[1] == (byte)'Z';
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _ = ex; return false; }
    }
}

/// <summary>
/// data/cache/emulator-versions.json: probed versions keyed by the full exe path (lower-case), valid while size and
/// mtime are unchanged. A probe that found nothing is cached too, so a version-less exe is not executed on every start.
/// Never throws.
/// </summary>
internal sealed class EmulatorVersionCache
{
    internal sealed record Entry(long Size, long Mtime, string? Version);

    private static readonly System.Text.Json.JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
    };

    private readonly string _file;
    private readonly object _gate = new();
    private Dictionary<string, Entry>? _map;
    private bool _dirty;

    public EmulatorVersionCache(string file) { _file = file; }

    /// <summary>Probes run by this instance (tests: a cached exe is not executed again).</summary>
    public int Probes { get; private set; }

    public string? GetOrProbe(string exe, Func<string, string?> probe)
    {
        long size, mtime;
        try
        {
            var info = new FileInfo(exe);
            if (!info.Exists) return null;
            size = info.Length;
            mtime = info.LastWriteTimeUtc.Ticks;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { _ = ex; return null; }
        // fakes (empty files in tests) are neither probed nor cached
        if (!EmulatorVersionProbe.LooksLikeExe(exe)) return null;

        var key = Path.GetFullPath(exe).ToLowerInvariant();
        lock (_gate)
        {
            if (Load().TryGetValue(key, out var e) && e.Size == size && e.Mtime == mtime) return e.Version;
        }
        string? version = null;
        try { version = probe(exe); }
        catch (Exception ex) { Log.Warn($"Version probe for {exe} failed", ex); }
        lock (_gate)
        {
            Probes++;
            Load()[key] = new Entry(size, mtime, version);
            _dirty = true;
        }
        Log.Info($"Probed the version of {exe}: {version ?? "(none)"}");
        return version;
    }

    public void Save()
    {
        lock (_gate)
        {
            if (!_dirty || _map == null) return;
            try
            {
                SafeFile.WriteAllText(_file, System.Text.Json.JsonSerializer.Serialize(_map, Json));
                _dirty = false;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Warn($"Could not save {_file}", ex);
            }
        }
    }

    private Dictionary<string, Entry> Load()
    {
        if (_map != null) return _map;
        _map = new Dictionary<string, Entry>(StringComparer.Ordinal);
        try
        {
            if (File.Exists(_file))
            {
                var raw = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(_file), Json);
                if (raw != null)
                    foreach (var (k, v) in raw)
                        if (v != null) _map[k] = v;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or NotSupportedException)
        {
            Log.Warn($"{_file} unreadable, probing again", ex);
        }
        return _map;
    }
}
