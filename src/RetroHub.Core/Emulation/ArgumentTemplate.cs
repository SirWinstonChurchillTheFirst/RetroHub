using System.Text;
using RetroHub.Core.Catalog;
using RetroHub.Core.Logging;

namespace RetroHub.Core.Emulation;

/// <summary>Values for the placeholders of <see cref="EmulatorDef.Args"/>. Null values expand to "".</summary>
public sealed record ArgumentContext
{
    /// <summary>Absolute path of the game file.</summary>
    public string? Rom { get; init; }
    public string? RomDir { get; init; }
    /// <summary>File name without extension.</summary>
    public string? RomName { get; init; }
    /// <summary>Absolute path of the libretro core dll (RetroArch only).</summary>
    public string? Core { get; init; }
    public string? EmuDir { get; init; }
    public string? Root { get; init; }
    public string? Bios { get; init; }
    public string? Saves { get; init; }
    public string? SaveStates { get; init; }
    public string? Screenshots { get; init; }
    public string? System { get; init; }
    /// <summary>data/config.</summary>
    public string? Config { get; init; }
    /// <summary>data/config/retroarch/retrohub.cfg (RetroHub's --appendconfig file).</summary>
    public string? RetroArchOverride { get; init; }
    public bool Fullscreen { get; init; }
    /// <summary>What a "{fullscreen}" token expands to in fullscreen mode.</summary>
    public IReadOnlyList<string> FullscreenArgs { get; init; } = [];

    /// <summary>Fills the folder placeholders from the portable layout.</summary>
    public static ArgumentContext For(AppPaths paths, string? systemId, string? romPath, string? exePath, string? corePath,
        LaunchMode mode, IReadOnlyList<string>? fullscreenArgs)
    {
        string? rom = romPath == null ? null : SafeFull(romPath);
        return new ArgumentContext
        {
            Rom = rom,
            RomDir = rom == null ? null : Path.GetDirectoryName(rom),
            RomName = rom == null ? null : Path.GetFileNameWithoutExtension(rom),
            Core = corePath,
            EmuDir = exePath == null ? null : Path.GetDirectoryName(SafeFull(exePath)),
            Root = paths.Root,
            Bios = paths.Bios,
            Saves = paths.Saves,
            SaveStates = paths.SaveStates,
            Screenshots = paths.Screenshots,
            System = systemId,
            Config = paths.Config,
            RetroArchOverride = RetroArchOverridePath(paths),
            Fullscreen = mode == LaunchMode.Fullscreen,
            FullscreenArgs = fullscreenArgs ?? [],
        };
    }

    /// <summary>Where RetroHub keeps its RetroArch override config (never the user's retroarch.cfg).</summary>
    public static string RetroArchOverridePath(AppPaths paths) => Path.Combine(paths.Config, "retroarch", "retrohub.cfg");

    private static string SafeFull(string p)
    {
        try { return Path.GetFullPath(p); } catch { return p; }
    }
}

/// <summary>
/// Expands emulator argument templates. One template token = one argv entry (no shell quoting is ever done here;
/// the session uses ProcessStartInfo.ArgumentList).
/// <list type="bullet">
/// <item>Placeholders: {rom} {romDir} {romName} {core} {emuDir} {root} {bios} {saves} {savestates} {screenshots}
/// {system} {config} {retroarchOverride} (names case-insensitive), substituted anywhere inside a token
/// ("-L{core}", "--savedir={saves}/{system}").</item>
/// <item>A token that is exactly "{fullscreen}" becomes <see cref="ArgumentContext.FullscreenArgs"/> (each expanded,
/// each its own entry) in fullscreen mode and disappears otherwise.</item>
/// <item>Unknown placeholders stay as they are (logged once per expansion).</item>
/// <item>A token that consisted only of placeholders and expands to "" is dropped (no empty argv entries).</item>
/// </list>
/// </summary>
public static class ArgumentTemplate
{
    public const string FullscreenToken = "{fullscreen}";

    public static IReadOnlyList<string> Expand(IEnumerable<string>? tokens, ArgumentContext ctx)
    {
        var result = new List<string>();
        if (tokens == null) return result;
        var unknown = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in tokens)
        {
            if (token == null) continue;
            if (string.Equals(token.Trim(), FullscreenToken, StringComparison.OrdinalIgnoreCase))
            {
                if (!ctx.Fullscreen) continue;
                foreach (var f in ctx.FullscreenArgs)
                {
                    // nested "{fullscreen}" would recurse forever: drop it
                    if (f == null || string.Equals(f.Trim(), FullscreenToken, StringComparison.OrdinalIgnoreCase)) continue;
                    Add(result, f, ExpandToken(f, ctx, unknown));
                }
                continue;
            }
            Add(result, token, ExpandToken(token, ctx, unknown));
        }
        if (unknown.Count > 0)
            Log.Warn($"Unknown argument placeholder(s) left as-is: {string.Join(", ", unknown.Select(u => "{" + u + "}"))}");
        return result;
    }

    private static void Add(List<string> result, string original, string expanded)
    {
        if (expanded.Length == 0 && original.Length > 0) return;
        result.Add(expanded);
    }

    /// <summary>Substitutes the placeholders in one token.</summary>
    public static string ExpandToken(string token, ArgumentContext ctx) => ExpandToken(token, ctx, null);

    private static string ExpandToken(string token, ArgumentContext ctx, HashSet<string>? unknown)
    {
        if (token.IndexOf('{') < 0) return token;
        var sb = new StringBuilder(token.Length + 64);
        int i = 0;
        while (i < token.Length)
        {
            int open = token.IndexOf('{', i);
            if (open < 0) { sb.Append(token, i, token.Length - i); break; }
            int close = token.IndexOf('}', open + 1);
            if (close < 0) { sb.Append(token, i, token.Length - i); break; }
            sb.Append(token, i, open - i);
            var name = token[(open + 1)..close];
            if (TryResolve(name, ctx, out var value)) sb.Append(value);
            else
            {
                // not a known placeholder: keep the brace and continue right after it (so "{{rom}" still finds {rom})
                unknown?.Add(name);
                sb.Append('{');
                i = open + 1;
                continue;
            }
            i = close + 1;
        }
        return sb.ToString();
    }

    private static bool TryResolve(string name, ArgumentContext c, out string value)
    {
        string? v;
        switch (name.ToLowerInvariant())
        {
            case "rom": v = c.Rom; break;
            case "romdir": v = c.RomDir; break;
            case "romname": v = c.RomName; break;
            case "core": v = c.Core; break;
            case "emudir": v = c.EmuDir; break;
            case "root": v = c.Root; break;
            case "bios": v = c.Bios; break;
            case "saves": v = c.Saves; break;
            case "savestates": v = c.SaveStates; break;
            case "screenshots": v = c.Screenshots; break;
            case "system": v = c.System; break;
            case "config": v = c.Config; break;
            case "retroarchoverride": v = c.RetroArchOverride; break;
            default: value = ""; return false;
        }
        value = v ?? "";
        return true;
    }

    /// <summary>
    /// Splits a command line into argv entries with the Windows (MSVC CRT / CommandLineToArgvW) rules:
    /// whitespace separates; "…" groups; 2n backslashes before a quote → n backslashes + quote toggles;
    /// 2n+1 backslashes before a quote → n backslashes + a literal quote; other backslashes are literal;
    /// "" inside a quoted part → a literal quote.
    /// </summary>
    public static IReadOnlyList<string> SplitCommandLine(string? commandLine)
    {
        var args = new List<string>();
        if (string.IsNullOrWhiteSpace(commandLine)) return args;
        var cur = new StringBuilder();
        bool inQuotes = false, have = false;
        int i = 0, n = commandLine.Length;
        while (i < n)
        {
            char c = commandLine[i];
            if (c == '\\')
            {
                int count = 0;
                while (i < n && commandLine[i] == '\\') { count++; i++; }
                if (i < n && commandLine[i] == '"')
                {
                    cur.Append('\\', count / 2);
                    if (count % 2 == 1) { cur.Append('"'); i++; }
                    // even: the quote is handled by the next iteration
                }
                else cur.Append('\\', count);
                have = true;
                continue;
            }
            if (c == '"')
            {
                if (inQuotes && i + 1 < n && commandLine[i + 1] == '"')
                {
                    cur.Append('"');
                    i += 2;
                    continue;
                }
                inQuotes = !inQuotes;
                have = true;
                i++;
                continue;
            }
            if (!inQuotes && (c == ' ' || c == '\t' || c == '\r' || c == '\n'))
            {
                if (have) { args.Add(cur.ToString()); cur.Clear(); have = false; }
                i++;
                continue;
            }
            cur.Append(c);
            have = true;
            i++;
        }
        if (have) args.Add(cur.ToString());
        return args;
    }
}
