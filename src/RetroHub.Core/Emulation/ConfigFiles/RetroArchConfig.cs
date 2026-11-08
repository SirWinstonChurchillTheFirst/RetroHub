namespace RetroHub.Core.Emulation.ConfigFiles;

/// <summary>
/// Editor for RetroArch's cfg format: one <c>key = "value"</c> per line, '#' comments, no sections.
/// Keys compare exactly (RetroArch keys are lower-case). Values are written quoted; RetroArch's format has no
/// escapes, so a value must not contain '"' (rejected). Everything else in the file is preserved.
/// RetroHub only ever writes its own override file with this (see the "retroarch" portable recipe),
/// never the user's retroarch.cfg.
/// </summary>
public sealed class RetroArchConfig : TextDocument
{
    private RetroArchConfig() { }

    public static RetroArchConfig Load(string path)
    {
        var c = new RetroArchConfig();
        c.LoadFrom(path, null);
        return c;
    }

    public static RetroArchConfig Parse(string text)
    {
        var c = new RetroArchConfig();
        c.ParseFrom(text);
        return c;
    }

    private static string? KeyOf(string line)
    {
        var t = line.TrimStart();
        if (t.Length == 0 || t[0] == '#') return null;
        int eq = t.IndexOf('=');
        if (eq <= 0) return null;
        var key = t[..eq].Trim();
        return key.Length == 0 ? null : key;
    }

    /// <summary>The value without quotes, or null when the key is missing.</summary>
    public string? Get(string key)
    {
        foreach (var line in Lines)
            if (KeyOf(line) == key)
            {
                var v = line[(line.IndexOf('=') + 1)..].Trim();
                return v.Length >= 2 && v[0] == '"' && v[^1] == '"' ? v[1..^1] : v;
            }
        return null;
    }

    /// <summary>Sets <c>key = "value"</c> (first occurrence; later duplicates removed). Returns true when the text changed.</summary>
    public bool Set(string key, string value)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Contains('=') || key.Contains(' ') || key.Contains('#'))
            throw new ArgumentException($"Invalid RetroArch key '{key}'", nameof(key));
        if (value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            throw new ArgumentException($"RetroArch values cannot contain quotes or line breaks: '{value}'", nameof(value));
        int edits = Edits;
        var line = $"{key} = \"{value}\"";
        int first = -1;
        for (int i = 0; i < Lines.Count; i++)
        {
            if (KeyOf(Lines[i]) != key) continue;
            if (first < 0) { first = i; ReplaceLine(i, line); }
            else { RemoveLine(i); i--; }
        }
        if (first < 0) AppendLine(line);
        return Edits != edits;
    }

    /// <summary>Removes every occurrence of the key. Returns true when something was removed.</summary>
    public bool Remove(string key)
    {
        bool removed = false;
        for (int i = Lines.Count - 1; i >= 0; i--)
            if (KeyOf(Lines[i]) == key) { RemoveLine(i); removed = true; }
        return removed;
    }

    /// <summary>Puts "# comment" as the first line unless that exact comment line exists. Returns true when added.</summary>
    public bool EnsureComment(string comment)
    {
        var line = "# " + comment.Replace("\r", " ").Replace("\n", " ");
        if (Lines.Contains(line)) return false;
        InsertLine(0, line);
        return true;
    }

    /// <summary>All keys in file order.</summary>
    public IReadOnlyList<string> Keys() => Lines.Select(KeyOf).OfType<string>().Distinct().ToList();
}
