using System.Diagnostics;
using System.Text.RegularExpressions;
using RetroHub.Core.Catalog;
using RetroHub.Core.Config;
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
/// from the folder/exe name ("mGBA-0.10.5-win64" → "0.10.5"). RetroArch: <see cref="EmulatorInstall.InstalledCores"/>
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
    private IReadOnlyList<EmulatorInstall> _all = [];
    private Dictionary<string, EmulatorInstall> _byId = new(StringComparer.OrdinalIgnoreCase);

    public event Action? Changed;

    public EmulatorRegistry(ICatalog catalog, AppPaths paths, SettingsStore settings)
    {
        _catalog = catalog;
        _paths = paths;
        _settings = settings;
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
        if (settings.EmulatorPaths.TryGetValue(def.Id, out var stored) && !string.IsNullOrWhiteSpace(stored))
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
            Version = ReadVersion(exe, _paths.Emulators),
            InstalledCores = def.Kind == EmulatorKind.RetroArch ? Cores(exe) : [],
        };
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
