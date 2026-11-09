using System.Text;

namespace RetroHub.Core.Emulation.ConfigFiles;

/// <summary>
/// Minimal line-based TOML editor (melonDS 1.x melonDS.toml). Port of MonHub HubSetup.SetToml/GetToml semantics:
/// <list type="bullet">
/// <item>Tables are "[name]" lines; the name compares exactly (ordinal, like MonHub), whitespace inside the
/// brackets is ignored. Table null = top-level keys (before the first table).</item>
/// <item>Keys compare exactly (ordinal). <see cref="Set"/> takes a raw TOML value ("true", "4", "\"text\"");
/// <see cref="SetString"/> quotes a string. <see cref="Get"/> returns the value with quotes removed
/// (basic-string escapes resolved).</item>
/// <item>Missing keys are added after the table's last key; missing tables are appended at the end; top-level
/// keys are always inserted before the first table (TOML requires that).</item>
/// <item>Multi-line arrays and multi-line strings are skipped as a whole so lines inside them are never
/// mistaken for tables or keys; <see cref="Set"/> / <see cref="Remove"/> of such a key replace / remove all of
/// its lines, and new keys are never inserted inside such a value.</item>
/// <item>Comments, blank lines, ordering, encoding and line endings are preserved.</item>
/// </list>
/// Not a full TOML parser: dotted keys, inline tables and array-of-tables are left untouched (never edited).
/// </summary>
public sealed class TomlFile : TextDocument
{
    private TomlFile() { }

    public static TomlFile Load(string path)
    {
        var t = new TomlFile();
        t.LoadFrom(path, null);
        return t;
    }

    public static TomlFile Parse(string text)
    {
        var t = new TomlFile();
        t.ParseFrom(text);
        return t;
    }

    /// <summary>Continuation = a line inside a multi-line value (array / string) that started on a Key line above.</summary>
    private enum Kind { Other, Table, ArrayTable, Key, Continuation }

    private readonly record struct LineInfo(Kind Kind, string? Name);

    /// <summary>Classifies each line; lines inside multi-line values are <see cref="Kind.Continuation"/>.</summary>
    private List<LineInfo> Classify()
    {
        var info = new List<LineInfo>(Lines.Count);
        int depth = 0;            // open '[' of a multi-line array
        string? openString = null; // """ or ''' of a multi-line string
        foreach (var line in Lines)
        {
            if (openString != null)
            {
                info.Add(new LineInfo(Kind.Continuation, null));
                if (CountOccurrences(line, openString) % 2 == 1) openString = null;
                continue;
            }
            if (depth > 0)
            {
                info.Add(new LineInfo(Kind.Continuation, null));
                depth += BracketBalance(line);
                if (depth < 0) depth = 0;
                continue;
            }
            var t = line.Trim();
            if (t.Length == 0 || t[0] == '#') { info.Add(new LineInfo(Kind.Other, null)); continue; }
            if (t.StartsWith("[[", StringComparison.Ordinal))
            {
                info.Add(new LineInfo(Kind.ArrayTable, null));
                continue;
            }
            if (t[0] == '[')
            {
                int close = t.IndexOf(']');
                var name = close > 0 ? NormalizeTable(t[1..close]) : null;
                info.Add(new LineInfo(name == null ? Kind.Other : Kind.Table, name));
                continue;
            }
            int eq = t.IndexOf('=');
            if (eq <= 0) { info.Add(new LineInfo(Kind.Other, null)); continue; }
            var key = t[..eq].Trim();
            info.Add(new LineInfo(Kind.Key, key));
            var value = t[(eq + 1)..].TrimStart();
            if (value.StartsWith("\"\"\"", StringComparison.Ordinal) || value.StartsWith("'''", StringComparison.Ordinal))
            {
                var q = value[..3];
                if (CountOccurrences(value, q) == 1) openString = q;
            }
            else if (value.StartsWith('['))
            {
                depth = BracketBalance(value);
                if (depth < 0) depth = 0;
            }
        }
        return info;
    }

    private static int CountOccurrences(string s, string what)
    {
        int n = 0, i = 0;
        while ((i = s.IndexOf(what, i, StringComparison.Ordinal)) >= 0) { n++; i += what.Length; }
        return n;
    }

    /// <summary>'[' minus ']' outside of quoted strings and comments.</summary>
    private static int BracketBalance(string s)
    {
        int bal = 0;
        char? quote = null;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (quote != null)
            {
                if (c == '\\' && quote == '"') { i++; continue; }
                if (c == quote) quote = null;
                continue;
            }
            if (c == '"' || c == '\'') quote = c;
            else if (c == '#') break;
            else if (c == '[') bal++;
            else if (c == ']') bal--;
        }
        return bal;
    }

    private static string NormalizeTable(string name) => string.Concat(name.Where(c => !char.IsWhiteSpace(c)));

    /// <summary>(start, end exclusive) of the table's key lines; top level for null. Null when the table is missing.</summary>
    private (int Start, int End, int Header)? Range(List<LineInfo> info, string? table)
    {
        int start, header = -1;
        if (table == null) start = 0;
        else
        {
            var wanted = NormalizeTable(table);
            header = info.FindIndex(l => l.Kind == Kind.Table && l.Name == wanted);
            if (header < 0) return null;
            start = header + 1;
        }
        int end = info.Count;
        for (int i = start; i < info.Count; i++)
            if (info[i].Kind is Kind.Table or Kind.ArrayTable) { end = i; break; }
        return (start, end, header);
    }

    /// <summary>The value with surrounding quotes removed (basic-string escapes resolved), raw otherwise; null when missing.</summary>
    public string? Get(string? table, string key)
    {
        var raw = GetRaw(table, key);
        return raw == null ? null : Unquote(raw);
    }

    /// <summary>The raw TOML value text (comment removed), or null.</summary>
    public string? GetRaw(string? table, string key)
    {
        var info = Classify();
        if (Range(info, table) is not { } r) return null;
        for (int i = r.Start; i < r.End; i++)
            if (info[i].Kind == Kind.Key && info[i].Name == key)
            {
                var line = Lines[i];
                var value = line[(line.IndexOf('=') + 1)..].Trim();
                return StripComment(value);
            }
        return null;
    }

    /// <summary>Sets a raw TOML value (e.g. "true", "4", "\"text\""). Returns true when the text changed.</summary>
    public bool Set(string? table, string key, string rawValue)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Contains('=') || key.Contains('\n') || key.Trim() != key)
            throw new ArgumentException($"Invalid TOML key '{key}'", nameof(key));
        int edits = Edits;
        var info = Classify();
        var line = $"{key} = {rawValue}";
        var range = Range(info, table);
        if (range is not { } r)
        {
            if (Lines.Count > 0 && Lines[^1].Trim().Length > 0) AppendLine("");
            AppendLine($"[{table}]");
            AppendLine(line);
            return Edits != edits;
        }
        int lastKey = -1;
        for (int i = r.Start; i < r.End; i++)
        {
            if (info[i].Kind != Kind.Key) continue;
            if (info[i].Name == key)
            {
                // keep a trailing comment and the line's indentation
                var old = Lines[i];
                var indent = old[..(old.Length - old.TrimStart().Length)];
                var oldValue = old[(old.IndexOf('=') + 1)..].Trim();
                var comment = CommentOf(oldValue);
                // a multi-line old value: its continuation lines go too (else they would dangle as invalid TOML)
                RemoveContinuation(info, i);
                ReplaceLine(i, indent + line + (comment.Length > 0 ? " " + comment : ""));
                return Edits != edits;
            }
            lastKey = i;
        }
        int insert = lastKey >= 0 ? lastKey + 1 : r.Start;
        // a key whose value spans lines: insert after its last line
        while (insert < r.End && info[insert].Kind == Kind.Continuation) insert++;
        InsertLine(insert, line);
        return Edits != edits;
    }

    /// <summary>Sets a string value (quoted, escaped as a TOML basic string).</summary>
    public bool SetString(string? table, string key, string value) => Set(table, key, Quote(value));

    public bool SetBool(string? table, string key, bool value) => Set(table, key, value ? "true" : "false");

    /// <summary>Removes the key from the table. Returns true when it was there.</summary>
    public bool Remove(string? table, string key)
    {
        var info = Classify();
        if (Range(info, table) is not { } r) return false;
        for (int i = r.Start; i < r.End; i++)
            if (info[i].Kind == Kind.Key && info[i].Name == key)
            {
                RemoveContinuation(info, i);
                RemoveLine(i);
                return true;
            }
        return false;
    }

    /// <summary>Removes the continuation lines of the (multi-line) value that starts on line <paramref name="keyLine"/>.</summary>
    private void RemoveContinuation(List<LineInfo> info, int keyLine)
    {
        int end = keyLine + 1;
        while (end < info.Count && info[end].Kind == Kind.Continuation) end++;
        for (int j = end - 1; j > keyLine; j--) RemoveLine(j);
    }

    public bool HasTable(string table) => Range(Classify(), table) != null;

    /// <summary>TOML basic string: "…" with backslash and quote escaped.</summary>
    public static string Quote(string value)
    {
        var sb = new StringBuilder("\"");
        foreach (var c in value)
        {
            switch (c)
            {
                case '\\': sb.Append(@"\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\n': sb.Append(@"\n"); break;
                case '\r': sb.Append(@"\r"); break;
                case '\t': sb.Append(@"\t"); break;
                default:
                    if (c < 0x20) sb.Append($"\\u{(int)c:X4}"); else sb.Append(c);
                    break;
            }
        }
        return sb.Append('"').ToString();
    }

    /// <summary>A path as melonDS writes it: forward slashes, quoted (MonHub HubSetup.Quote).</summary>
    public static string QuotePath(string path) => Quote(path.Replace('\\', '/'));

    internal static string Unquote(string raw)
    {
        if (raw.Length >= 2 && raw[0] == '\'' && raw[^1] == '\'') return raw[1..^1];
        if (raw.Length < 2 || raw[0] != '"' || raw[^1] != '"') return raw;
        var s = raw[1..^1];
        var sb = new StringBuilder();
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c != '\\' || i + 1 >= s.Length) { sb.Append(c); continue; }
            char n = s[++i];
            switch (n)
            {
                case 'n': sb.Append('\n'); break;
                case 't': sb.Append('\t'); break;
                case 'r': sb.Append('\r'); break;
                case '"': sb.Append('"'); break;
                case '\\': sb.Append('\\'); break;
                case 'u' when i + 4 < s.Length
                              && int.TryParse(s.AsSpan(i + 1, 4), System.Globalization.NumberStyles.HexNumber, null, out var code):
                    sb.Append((char)code);
                    i += 4;
                    break;
                default: sb.Append('\\').Append(n); break;
            }
        }
        return sb.ToString();
    }

    /// <summary>Value without a trailing "# comment" (quotes respected).</summary>
    private static string StripComment(string value)
    {
        int at = CommentStart(value);
        return at < 0 ? value : value[..at].TrimEnd();
    }

    private static string CommentOf(string value)
    {
        int at = CommentStart(value);
        return at < 0 ? "" : value[at..];
    }

    private static int CommentStart(string value)
    {
        char? quote = null;
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (quote != null)
            {
                if (c == '\\' && quote == '"') { i++; continue; }
                if (c == quote) quote = null;
                continue;
            }
            if (c == '"' || c == '\'') quote = c;
            else if (c == '#') return i;
        }
        return -1;
    }
}
