using System.Text.RegularExpressions;

namespace RetroHub.Core.Catalog;

/// <summary>
/// Merges, normalises and cross-validates definitions. Used by <see cref="Catalog.Load(AppPaths)"/> and
/// <see cref="Catalog.FromDefinitions"/>. Never throws for data problems except duplicate built-in ids in strict mode
/// (a packaging bug); everything else becomes a German warning and the offending value is dropped or corrected.
/// </summary>
internal sealed partial class CatalogBuilder
{
    private sealed record Entry<T>(T Def, string Source, bool Plugin, int Seq);

    private static readonly HashSet<string> KnownPlaceholders = new(StringComparer.Ordinal)
    {
        "rom", "romDir", "romName", "core", "emuDir", "root", "bios", "saves", "savestates", "screenshots", "system",
        "fullscreen", "retroarchOverride",
    };

    private const string FullscreenToken = "{fullscreen}";
    /// <summary>Upper bound for EmbedProfile bar heights (px at 100 %); real menu/status bars are 20–40 px.</summary>
    private const int MaxBarHeight = 200;

    private readonly bool _strictBuiltIns;
    private readonly List<string> _warnings = [];
    private readonly Dictionary<string, Entry<SystemDef>> _systems = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Entry<EmulatorDef>> _emulators = new(StringComparer.OrdinalIgnoreCase);
    private int _seq;

    /// <param name="strictBuiltIns">True: two built-in definitions with the same id throw (packaging bug).</param>
    public CatalogBuilder(bool strictBuiltIns) { _strictBuiltIns = strictBuiltIns; }

    public void Warn(string message) => _warnings.Add(message);

    public void AddSystem(SystemDef raw, string source, bool plugin)
    {
        var def = NormalizeSystem(raw, source);
        if (def == null) return;
        Add(_systems, def.Id, def, source, plugin, "System");
    }

    public void AddEmulator(EmulatorDef raw, string source, bool plugin)
    {
        var def = NormalizeEmulator(raw, source);
        if (def == null) return;
        Add(_emulators, def.Id, def, source, plugin, "Emulator");
    }

    private void Add<T>(Dictionary<string, Entry<T>> map, string id, T def, string source, bool plugin, string what)
    {
        if (map.TryGetValue(id, out var existing))
        {
            if (!existing.Plugin && !plugin)
            {
                var msg = $"{source}: {what} „{id}“ ist doppelt definiert (bereits in {existing.Source}).";
                if (_strictBuiltIns) throw new CatalogLoadException(source, "Eingebaute Definitionen fehlerhaft – " + msg);
                Warn(msg + " Die spätere Definition gilt.");
            }
            else if (existing.Plugin && plugin)
                Warn($"{source}: {what} „{id}“ ist bereits in {existing.Source} definiert. Die spätere Definition gilt.");
            else if (existing.Plugin && !plugin)
                return; // cannot happen with Load (built-ins first), but a plugin always wins over a built-in
            // a plugin replacing a built-in is the intended override mechanism → no warning
        }
        map[id] = new Entry<T>(def, source, plugin, _seq++);
    }

    public (List<SystemDef> Systems, List<EmulatorDef> Emulators, List<string> Warnings) Build()
    {
        // ---- emulators: systems + cores must exist
        var emulators = new Dictionary<string, EmulatorDef>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, e) in _emulators)
        {
            var d = e.Def;
            var ctx = $"{e.Source}: Emulator „{id}“";
            var systems = new List<string>();
            foreach (var s in d.Systems)
            {
                if (_systems.ContainsKey(s)) systems.Add(s);
                else Warn($"{ctx}: unbekanntes System „{s}“ wird ignoriert.");
            }
            var cores = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var (sys, list) in d.Cores)
            {
                if (!_systems.ContainsKey(sys)) { Warn($"{ctx}: Core-Zuordnung für unbekanntes System „{sys}“ wird ignoriert."); continue; }
                cores[sys] = list;
                if (!systems.Contains(sys, StringComparer.OrdinalIgnoreCase)) systems.Add(sys); // Systems = union of the core mapping
            }
            emulators[id] = d with { Systems = systems.ToArray(), Cores = cores };
        }

        // ---- systems: emulator references must resolve
        var systemsList = new List<(SystemDef Def, Entry<SystemDef> Entry)>();
        foreach (var (id, e) in _systems)
        {
            var d = e.Def;
            var ctx = $"{e.Source}: System „{id}“";
            var refs = new List<string>();
            foreach (var text in d.Emulators)
            {
                var r = EmulatorRef.Parse(text);
                if (!emulators.TryGetValue(r.EmulatorId, out var emu))
                {
                    Warn($"{ctx}: unbekannter Emulator „{r}“ wird ignoriert.");
                    continue;
                }
                if (r.Core != null && emu.Kind != EmulatorKind.RetroArch)
                {
                    Warn($"{ctx}: „{emu.Id}“ ist kein RetroArch-Emulator, der Core „{r.Core}“ wird ignoriert.");
                    r = r with { Core = null };
                }
                else if (r.Core != null && !(emu.Cores.TryGetValue(id, out var cs) && cs.Contains(r.Core, StringComparer.OrdinalIgnoreCase)))
                    Warn($"{ctx}: Core „{r.Core}“ ist in „{emu.Id}“ nicht für dieses System eingetragen.");
                if (!emu.Systems.Contains(id, StringComparer.OrdinalIgnoreCase))
                    Warn($"{ctx}: Emulator „{emu.Id}“ führt dieses System nicht in seiner Systemliste.");
                var s = r.ToString();
                if (!refs.Contains(s, StringComparer.OrdinalIgnoreCase)) refs.Add(s);
            }
            systemsList.Add((d with { Emulators = refs.ToArray() }, e));
        }

        // ---- folder names must be unique across systems; plugins win, then the earlier definition
        var owner = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var ordered = systemsList.OrderBy(x => x.Entry.Plugin ? 0 : 1).ThenBy(x => x.Entry.Seq).ToList();
        var fixedFolders = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var (def, entry) in ordered)
        {
            var kept = new List<string>();
            foreach (var f in def.Folders)
            {
                if (owner.TryGetValue(f, out var other))
                    Warn($"{entry.Source}: System „{def.Id}“: Ordnername „{f}“ gehört bereits zu System „{other}“ und wird hier ignoriert.");
                else { owner[f] = def.Id; kept.Add(f); }
            }
            fixedFolders[def.Id] = kept.ToArray();
        }

        var systemsOut = systemsList.Select(x => x.Def with { Folders = fixedFolders[x.Def.Id] }).ToList();
        return (systemsOut, emulators.Values.ToList(), [.. _warnings]);
    }

    // ------------------------------------------------------------------ per-definition normalisation

    private SystemDef? NormalizeSystem(SystemDef d, string source)
    {
        var id = NormalizeId(d.Id);
        if (id == null)
        {
            Warn($"{source}: System mit ungültiger ID „{d.Id}“ wird übersprungen (erlaubt: a–z, 0–9, „-“, „_“, „.“ – nicht am Ende).");
            return null;
        }
        var ctx = $"{source}: System „{id}“";

        var folders = new List<string>();
        foreach (var raw in d.Folders ?? [])
        {
            var f = raw?.Trim();
            if (string.IsNullOrEmpty(f)) continue;
            if (!IsPlainName(f)) { Warn($"{ctx}: ungültiger Ordnername „{raw}“ wird ignoriert."); continue; }
            if (!folders.Contains(f, StringComparer.OrdinalIgnoreCase)) folders.Add(f);
        }
        if (folders.Count == 0) folders.Add(id);

        var exts = NormalizeExtensions(d.Extensions, ctx);
        var playlists = NormalizeExtensions(d.PlaylistExtensions, ctx);
        foreach (var p in playlists.Where(p => !exts.Contains(p)))
            Warn($"{ctx}: Playlist-Endung „{p}“ fehlt in der Liste der Endungen.");

        var sniffers = new List<string>();
        foreach (var raw in d.Sniffers ?? [])
        {
            var s = raw?.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(s)) continue;
            if (!sniffers.Contains(s)) sniffers.Add(s);
        }

        var refs = new List<string>();
        foreach (var raw in d.Emulators ?? [])
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var r = EmulatorRef.Parse(raw);
            var emuId = NormalizeId(r.EmulatorId);
            var core = r.Core == null ? null : NormalizeCore(r.Core);
            if (emuId == null || (r.Core != null && core == null))
            {
                Warn($"{ctx}: ungültiger Emulator-Verweis „{raw}“ wird ignoriert.");
                continue;
            }
            refs.Add(new EmulatorRef(emuId, core).ToString());
        }

        // JsonStringEnumConverter also accepts plain numbers, including undefined ones ("category": 42)
        var category = d.Category;
        if (!Enum.IsDefined(category))
        {
            Warn($"{ctx}: unbekannte Kategorie „{(int)category}“ – „{SystemCategory.Console}“ wird verwendet.");
            category = SystemCategory.Console;
        }

        double? aspect = d.AspectRatio;
        if (aspect is { } a && !(a >= 0.1 && a <= 10))
        {
            Warn($"{ctx}: Seitenverhältnis {a} ist ungültig und wird ignoriert.");
            aspect = null;
        }

        return d with
        {
            Id = id,
            Name = string.IsNullOrWhiteSpace(d.Name) ? id : d.Name.Trim(),
            Category = category,
            ShortName = d.ShortName?.Trim() ?? "",
            Manufacturer = d.Manufacturer?.Trim() ?? "",
            Folders = folders.ToArray(),
            Extensions = exts.ToArray(),
            PlaylistExtensions = playlists.ToArray(),
            Sniffers = sniffers.ToArray(),
            Emulators = refs.ToArray(),
            AspectRatio = aspect,
            Accent = NormalizeAccent(d.Accent, ctx),
            LibretroName = string.IsNullOrWhiteSpace(d.LibretroName) ? null : d.LibretroName.Trim(),
            GameTdb = NormalizeGameTdb(d.GameTdb, ctx),
            Bios = NormalizeBios(d.Bios, ctx),
        };
    }

    /// <summary>GameTDB platform: lower-case letters/digits ("switch"); anything else is dropped with a warning.</summary>
    private string? NormalizeGameTdb(string? value, string ctx)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var v = value.Trim().ToLowerInvariant();
        if (v.Length <= 16 && v.All(char.IsAsciiLetterOrDigit)) return v;
        Warn($"{ctx}: GameTDB-Plattform „{value}“ ist ungültig und wird ignoriert.");
        return null;
    }

    private EmulatorDef? NormalizeEmulator(EmulatorDef d, string source)
    {
        var id = NormalizeId(d.Id);
        if (id == null)
        {
            Warn($"{source}: Emulator mit ungültiger ID „{d.Id}“ wird übersprungen (erlaubt: a–z, 0–9, „-“, „_“, „.“ – nicht am Ende).");
            return null;
        }
        var ctx = $"{source}: Emulator „{id}“";

        // the kind decides how the planner starts it – guessing would launch it the wrong way, so skip the entry
        if (!Enum.IsDefined(d.Kind))
        {
            Warn($"{ctx}: unbekannte Art „{(int)d.Kind}“ (erlaubt: {string.Join(", ", Enum.GetNames<EmulatorKind>())}) – der Emulator wird übersprungen.");
            return null;
        }

        var exe = new List<string>();
        foreach (var raw in d.Exe ?? [])
        {
            var e = raw?.Trim();
            if (string.IsNullOrEmpty(e)) continue;
            // only file-name patterns below emulators/<id>/ – no paths, no ".." (no escaping the folder from a plugin)
            if (!IsPlainName(e.Replace('*', 'x').Replace('?', 'x'))) { Warn($"{ctx}: ungültiger Exe-Name „{raw}“ wird ignoriert."); continue; }
            if (!exe.Contains(e, StringComparer.OrdinalIgnoreCase)) exe.Add(e);
        }
        if (exe.Count == 0 && d.Kind != EmulatorKind.Native)
            Warn($"{ctx}: keine Exe-Namen angegeben – der Emulator kann nicht gefunden werden.");

        var systems = new List<string>();
        foreach (var raw in d.Systems ?? [])
        {
            var s = NormalizeId(raw);
            if (s == null) { if (!string.IsNullOrWhiteSpace(raw)) Warn($"{ctx}: ungültige System-ID „{raw}“ wird ignoriert."); continue; }
            if (!systems.Contains(s)) systems.Add(s);
        }

        var cores = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var (rawSys, rawList) in d.Cores ?? [])
        {
            var sys = NormalizeId(rawSys);
            if (sys == null) { Warn($"{ctx}: ungültige System-ID „{rawSys}“ in der Core-Zuordnung wird ignoriert."); continue; }
            var list = new List<string>();
            foreach (var raw in rawList ?? [])
            {
                var c = NormalizeCore(raw);
                if (c == null) { Warn($"{ctx}: ungültiger Core-Name „{raw}“ wird ignoriert."); continue; }
                if (!list.Contains(c)) list.Add(c);
            }
            if (list.Count > 0) cores[sys] = cores.TryGetValue(sys, out var prev) ? prev.Concat(list).Distinct().ToArray() : list.ToArray();
        }
        if (d.Kind == EmulatorKind.RetroArch && cores.Count == 0)
            Warn($"{ctx}: RetroArch-Emulator ohne Core-Zuordnung.");
        if (d.Kind != EmulatorKind.RetroArch && cores.Count > 0)
            Warn($"{ctx}: Core-Zuordnung wird nur bei RetroArch verwendet und hier ignoriert.");

        var args = CleanTokens(d.Args, ["{rom}"]);
        var fsArgs = CleanTokens(d.FullscreenArgs, []);
        foreach (var token in args.Concat(fsArgs))
            foreach (Match m in PlaceholderRegex().Matches(token))
                if (!KnownPlaceholders.Contains(m.Groups[1].Value))
                    Warn($"{ctx}: unbekannter Platzhalter „{m.Value}“ in den Argumenten.");
        // "{fullscreen}" only works as a token of its own (it expands to FullscreenArgs or to nothing)
        foreach (var token in args.Where(t => t != FullscreenToken && t.Contains(FullscreenToken, StringComparison.Ordinal)))
            Warn($"{ctx}: „{FullscreenToken}“ muss ein eigenes Argument sein, „{token}“ wird nicht ersetzt.");
        if (fsArgs.Any(t => t.Contains(FullscreenToken, StringComparison.Ordinal)))
        {
            Warn($"{ctx}: „{FullscreenToken}“ ist in den Vollbild-Argumenten nicht erlaubt und wird entfernt.");
            fsArgs = fsArgs.Where(t => !t.Contains(FullscreenToken, StringComparison.Ordinal)).ToArray();
        }

        var modes = new List<LaunchMode>();
        foreach (var m in d.Modes ?? [])
        {
            if (!Enum.IsDefined(m)) { Warn($"{ctx}: unbekannter Startmodus „{(int)m}“ wird ignoriert."); continue; }
            if (!modes.Contains(m)) modes.Add(m);
        }
        var defaultMode = d.DefaultMode;
        if (!Enum.IsDefined(defaultMode))
        {
            var fallback = modes.Count > 0 ? modes[0] : LaunchMode.Fullscreen;
            Warn($"{ctx}: unbekannter Standardmodus „{(int)defaultMode}“ – „{fallback}“ wird verwendet.");
            defaultMode = fallback;
        }
        if (modes.Count == 0) modes.Add(defaultMode);
        if (!modes.Contains(defaultMode))
        {
            Warn($"{ctx}: Standardmodus „{defaultMode}“ ist nicht in den unterstützten Modi; „{modes[0]}“ wird verwendet.");
            defaultMode = modes[0];
        }
        var embed = d.Embed;
        if (modes.Contains(LaunchMode.Embedded) && embed == null)
        {
            Warn($"{ctx}: Modus „Embedded“ ohne Einbettungsprofil – Standardwerte werden verwendet.");
            embed = new EmbedProfile();
        }
        if (embed != null) embed = NormalizeEmbed(embed, ctx);

        return d with
        {
            Id = id,
            Name = string.IsNullOrWhiteSpace(d.Name) ? id : d.Name.Trim(),
            Exe = exe.ToArray(),
            Systems = systems.ToArray(),
            Cores = d.Kind == EmulatorKind.RetroArch ? cores : new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase),
            Args = args,
            FullscreenArgs = fsArgs,
            Modes = modes.ToArray(),
            DefaultMode = defaultMode,
            Embed = embed,
            Portable = string.IsNullOrWhiteSpace(d.Portable) ? null : d.Portable.Trim().ToLowerInvariant(),
            Website = NormalizeUrl(d.Website, ctx, "Website"),
            DownloadUrl = NormalizeUrl(d.DownloadUrl, ctx, "Download-Adresse"),
            License = string.IsNullOrWhiteSpace(d.License) ? null : d.License.Trim(),
            Notes = string.IsNullOrWhiteSpace(d.Notes) ? null : d.Notes.Trim(),
        };
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>Lower-case id of [a-z0-9][a-z0-9._-]*, max 64 chars, not ending in "."; null if invalid.</summary>
    internal static string? NormalizeId(string? raw)
    {
        var s = raw?.Trim().ToLowerInvariant();
        return !string.IsNullOrEmpty(s) && IdRegex().IsMatch(s) ? s : null;
    }

    /// <summary>libretro core name (becomes cores/&lt;name&gt;_libretro.dll): lower-case [a-z0-9_.-]; null if invalid.</summary>
    internal static string? NormalizeCore(string? raw)
    {
        var s = raw?.Trim().ToLowerInvariant();
        return !string.IsNullOrEmpty(s) && IdRegex().IsMatch(s) ? s : null;
    }

    /// <summary>Lower-case, leading dot (".gba"); null if it is not a plain single extension.</summary>
    internal static string? NormalizeExtension(string? raw)
    {
        var s = raw?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(s)) return null;
        if (s.StartsWith("*.", StringComparison.Ordinal)) s = s[1..];
        if (!s.StartsWith('.')) s = "." + s;
        return ExtensionRegex().IsMatch(s) ? s : null;
    }

    private List<string> NormalizeExtensions(string[]? raw, string ctx)
    {
        var list = new List<string>();
        foreach (var r in raw ?? [])
        {
            if (string.IsNullOrWhiteSpace(r)) continue;
            var e = NormalizeExtension(r);
            if (e == null) { Warn($"{ctx}: ungültige Dateiendung „{r}“ wird ignoriert."); continue; }
            if (!list.Contains(e)) list.Add(e);
        }
        return list;
    }

    private BiosFile[] NormalizeBios(BiosFile[]? raw, string ctx)
    {
        var list = new List<BiosFile>();
        foreach (var b in raw ?? [])
        {
            if (b == null) continue;
            var file = b.File?.Trim().Replace('\\', '/');
            // relative path below bios/, every segment a plain name (no "..", no drive, no root)
            if (string.IsNullOrEmpty(file) || file.StartsWith('/') || !file.Split('/').All(IsPlainName))
            {
                Warn($"{ctx}: ungültiger BIOS-Dateiname „{b.File}“ wird ignoriert.");
                continue;
            }
            var md5 = new List<string>();
            foreach (var m in b.Md5 ?? [])
            {
                var h = m?.Trim().ToLowerInvariant();
                if (string.IsNullOrEmpty(h)) continue;
                if (!Md5Regex().IsMatch(h)) { Warn($"{ctx}: ungültige MD5-Prüfsumme „{m}“ für „{file}“ wird ignoriert."); continue; }
                if (!md5.Contains(h)) md5.Add(h);
            }
            if (list.Any(x => string.Equals(x.File, file, StringComparison.OrdinalIgnoreCase)))
            {
                Warn($"{ctx}: BIOS-Datei „{file}“ ist doppelt eingetragen.");
                continue;
            }
            list.Add(b with { File = file, Md5 = md5.ToArray(), Description = string.IsNullOrWhiteSpace(b.Description) ? null : b.Description.Trim() });
        }
        return list.ToArray();
    }

    /// <summary>Bar heights must be sane pixel values (the embedder subtracts them from the host size).</summary>
    private EmbedProfile NormalizeEmbed(EmbedProfile e, string ctx)
    {
        int Clamp(int value, string what)
        {
            if (value is >= 0 and <= MaxBarHeight) return value;
            var fixedValue = Math.Clamp(value, 0, MaxBarHeight);
            Warn($"{ctx}: Leistenhöhe {what} = {value} ist ungültig (0–{MaxBarHeight}), {fixedValue} wird verwendet.");
            return fixedValue;
        }
        return e with
        {
            BarTop = Clamp(e.BarTop, "oben"),
            BarBottom = Clamp(e.BarBottom, "unten"),
            ReadyTitleContains = string.IsNullOrEmpty(e.ReadyTitleContains) ? null : e.ReadyTitleContains,
        };
    }

    private string? NormalizeAccent(string? raw, string ctx)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Trim();
        if (AccentRegex().IsMatch(s)) return s.ToUpperInvariant();
        Warn($"{ctx}: ungültige Akzentfarbe „{raw}“ (erwartet #RRGGBB) wird ignoriert.");
        return null;
    }

    /// <summary>Only absolute http(s) links (the UI opens them in the browser; no file:// or other schemes from plugins).</summary>
    private string? NormalizeUrl(string? raw, string ctx, string what)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (Uri.TryCreate(raw.Trim(), UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
            return uri.ToString();
        Warn($"{ctx}: {what} „{raw}“ ist keine http(s)-Adresse und wird ignoriert.");
        return null;
    }

    private static string[] CleanTokens(string[]? raw, string[] fallback) =>
        raw == null ? fallback : raw.Where(t => t != null).ToArray();

    /// <summary>A single file/folder name: no separators, "..", wildcards, or invalid characters.</summary>
    private static bool IsPlainName(string s) =>
        AppPaths.IsSafeName(s) && s.IndexOfAny(['/', '\\', ':', '*', '?']) < 0 && s.Trim() == s && !s.EndsWith('.');

    // ids are folder names (games/<id>, saves/<id>, …): no trailing "." – Windows strips it ("gba." = "gba")
    [GeneratedRegex("^[a-z0-9](?:[a-z0-9._-]{0,62}[a-z0-9_-])?$")]
    private static partial Regex IdRegex();

    [GeneratedRegex(@"^\.[a-z0-9_+-]{1,15}$")]
    private static partial Regex ExtensionRegex();

    [GeneratedRegex("^[0-9a-f]{32}$")]
    private static partial Regex Md5Regex();

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex AccentRegex();

    [GeneratedRegex(@"\{([A-Za-z]+)\}")]
    private static partial Regex PlaceholderRegex();
}
