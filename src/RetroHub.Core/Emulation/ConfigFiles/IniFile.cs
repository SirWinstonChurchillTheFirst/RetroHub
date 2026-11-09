using System.Text;

namespace RetroHub.Core.Emulation.ConfigFiles;

/// <summary>
/// Minimal line-based INI editor for emulator configs (DeSmuME desmume.ini, mGBA config.ini, Qt QSettings files
/// such as Azahar's qt-config.ini, DuckStation settings.ini).
/// <list type="bullet">
/// <item>Only lines that are set/removed change; comments (';' / '#'), blank lines, ordering, unknown keys,
/// the encoding and the line endings are preserved.</item>
/// <item>Sections and keys compare case-insensitively (like Windows' GetPrivateProfileString and MonHub's SetIni).
/// Keys are literal text up to the first '=', so Qt-style keys with backslashes ("use_custom_storage\default")
/// and keys with spaces ("Window Force Ratio") work as-is.</item>
/// <item>"key=value" and "key = value" are both understood; an updated line keeps its spacing, new keys use the
/// style most common in the file.</item>
/// <item>Section null or "" = keys before the first [section].</item>
/// </list>
/// </summary>
public sealed class IniFile : TextDocument
{
    private IniFile() { }

    /// <summary>Loads a file; a missing file gives an empty document (<see cref="TextDocument.IsNew"/>).</summary>
    /// <param name="asciiEncoding">Encoding to use when the file is pure ASCII or missing (default UTF-8 without BOM).</param>
    public static IniFile Load(string path, Encoding? asciiEncoding = null)
    {
        var ini = new IniFile();
        ini.LoadFrom(path, asciiEncoding);
        return ini;
    }

    public static IniFile Parse(string text)
    {
        var ini = new IniFile();
        ini.ParseFrom(text);
        return ini;
    }

    /// <summary>Section name of a "[name]" line (trimmed), or null when the line is no section header.</summary>
    internal static string? SectionOf(string line)
    {
        var t = line.Trim();
        if (t.Length < 2 || t[0] != '[') return null;
        int close = t.IndexOf(']');
        if (close < 0) return null;
        return t[1..close].Trim();
    }

    private static bool IsComment(string line)
    {
        var t = line.TrimStart();
        return t.Length == 0 || t[0] == ';' || t[0] == '#';
    }

    /// <summary>Key of a "key=value" line, or null.</summary>
    private static string? KeyOf(string line)
    {
        if (IsComment(line) || SectionOf(line) != null) return null;
        int eq = line.IndexOf('=');
        if (eq <= 0) return null;
        var key = line[..eq].Trim();
        return key.Length == 0 ? null : key;
    }

    private static string ValueOf(string line)
    {
        int eq = line.IndexOf('=');
        return eq < 0 ? "" : line[(eq + 1)..].Trim();
    }

    private static bool SameSection(string? a, string? b) =>
        string.Equals(a ?? "", b ?? "", StringComparison.OrdinalIgnoreCase);

    /// <summary>(header line index or -1 for the top level, first content line, end exclusive) for every block of that section.</summary>
    private List<(int Header, int Start, int End)> Blocks(string? section)
    {
        var result = new List<(int, int, int)>();
        string? current = "";
        int header = -1, start = 0;
        for (int i = 0; i <= Lines.Count; i++)
        {
            string? s = i < Lines.Count ? SectionOf(Lines[i]) : null;
            if (i < Lines.Count && s == null) continue;
            if (SameSection(current, section)) result.Add((header, start, i));
            if (i == Lines.Count) break;
            current = s;
            header = i;
            start = i + 1;
        }
        return result;
    }

    /// <summary>Line indices of every occurrence of the key in the section, ascending.</summary>
    private List<int> Hits(string? section, string key)
    {
        var hits = new List<int>();
        foreach (var (_, start, end) in Blocks(section))
            for (int i = start; i < end; i++)
                if (string.Equals(KeyOf(Lines[i]), key, StringComparison.OrdinalIgnoreCase)) hits.Add(i);
        return hits;
    }

    /// <summary>All section names in file order (the unnamed top level is not listed).</summary>
    public IReadOnlyList<string> Sections()
    {
        var list = new List<string>();
        foreach (var l in Lines)
            if (SectionOf(l) is { } s && !list.Contains(s, StringComparer.OrdinalIgnoreCase)) list.Add(s);
        return list;
    }

    /// <summary>All keys of a section (first spelling, file order).</summary>
    public IReadOnlyList<string> Keys(string? section)
    {
        var list = new List<string>();
        foreach (var (_, start, end) in Blocks(section))
            for (int i = start; i < end; i++)
                if (KeyOf(Lines[i]) is { } k && !list.Contains(k, StringComparer.OrdinalIgnoreCase)) list.Add(k);
        return list;
    }

    public bool HasSection(string section) => Blocks(section).Any(b => b.Header >= 0);

    /// <summary>Trimmed value of the first occurrence of the key, or null.</summary>
    public string? Get(string? section, string key)
    {
        var hits = Hits(section, key);
        return hits.Count == 0 ? null : ValueOf(Lines[hits[0]]);
    }

    /// <summary>
    /// Sets key=value, adding the key (after the section's last key) or the section (at the end) when missing.
    /// Later duplicates of the key in that section are removed so the result is unambiguous.
    /// Returns true when the text changed.
    /// </summary>
    public bool Set(string? section, string key, string value)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Contains('=') || key.Contains('\n') || key.Contains('\r') || key.Trim() != key)
            throw new ArgumentException($"Invalid INI key '{key}'", nameof(key));
        if (section != null && (section.Contains(']') || section.Contains('\n') || section.Contains('\r')))
            throw new ArgumentException($"Invalid INI section '{section}'", nameof(section));
        value = (value ?? "").Replace("\r", "").Replace("\n", " ");
        int edits = Edits;

        var hits = Hits(section, key);
        if (hits.Count > 0)
        {
            ReplaceLine(hits[0], Format(Lines[hits[0]], value));
            for (int j = hits.Count - 1; j >= 1; j--) RemoveLine(hits[j]);
            return Edits != edits;
        }

        var blocks = Blocks(section);
        var target = blocks.FirstOrDefault(b => b.Header >= 0 || string.IsNullOrEmpty(section));
        if (blocks.Count > 0 && (target.Header >= 0 || string.IsNullOrEmpty(section)))
        {
            int insert = target.Start;
            for (int i = target.Start; i < target.End; i++)
                if (KeyOf(Lines[i]) != null) insert = i + 1;
            InsertLine(insert, NewKeyLine(key, value));
        }
        else
        {
            if (Lines.Count > 0 && Lines[^1].Trim().Length > 0 && SeparatesSections()) AppendLine("");
            AppendLine($"[{section}]");
            AppendLine(NewKeyLine(key, value));
        }
        return Edits != edits;
    }

    /// <summary>Removes every occurrence of the key in the section. Returns true when something was removed.</summary>
    public bool Remove(string? section, string key)
    {
        var hits = Hits(section, key);
        for (int j = hits.Count - 1; j >= 0; j--) RemoveLine(hits[j]);
        return hits.Count > 0;
    }

    /// <summary>Whether the file puts blank lines between sections (decides the style of an appended section).</summary>
    private bool SeparatesSections()
    {
        int headers = 0, separated = 0;
        for (int i = 1; i < Lines.Count; i++)
        {
            if (SectionOf(Lines[i]) == null) continue;
            headers++;
            if (Lines[i - 1].Trim().Length == 0) separated++;
        }
        return headers == 0 || separated * 2 >= headers;
    }

    private bool SpacedStyle()
    {
        int spaced = 0, tight = 0;
        foreach (var l in Lines)
        {
            if (KeyOf(l) == null) continue;
            int eq = l.IndexOf('=');
            if (l[eq - 1] == ' ' || l[eq - 1] == '\t') spaced++; else tight++;
        }
        return spaced > tight;
    }

    private string NewKeyLine(string key, string value) =>
        SpacedStyle() ? $"{key} = {value}" : $"{key}={value}";

    /// <summary>Rewrites a key line with a new value, keeping indentation, the key's spelling and the spacing around '='.</summary>
    private static string Format(string oldLine, string value)
    {
        int eq = oldLine.IndexOf('=');
        var left = oldLine[..eq];
        var right = oldLine[(eq + 1)..];
        var spacesAfter = right[..(right.Length - right.TrimStart().Length)];
        if (value.Length == 0) return left + "=";
        if (spacesAfter.Length == 0 && left.Length > 0 && (left[^1] == ' ' || left[^1] == '\t')) spacesAfter = " ";
        return left + "=" + spacesAfter + value;
    }
}
