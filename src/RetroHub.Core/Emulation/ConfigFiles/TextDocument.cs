using System.Text;
using RetroHub.Core.IO;

namespace RetroHub.Core.Emulation.ConfigFiles;

/// <summary>
/// A text file as a list of lines that remembers everything needed to write it back byte-for-byte:
/// the encoding (incl. BOM), and each line's own terminator ("\r\n", "\n", "\r" or none for the last line).
/// The editors (<see cref="IniFile"/>, <see cref="TomlFile"/>, <see cref="RetroArchConfig"/>) only replace the
/// lines they change, so comments, ordering, blank lines and unknown keys of emulator configs survive.
/// </summary>
public abstract class TextDocument
{
    private protected readonly List<string> Lines = new();
    private protected readonly List<string> Terminators = new();
    private Encoding _encoding = new UTF8Encoding(false);
    private byte[] _preamble = [];

    /// <summary>Line terminator used for lines this editor adds (the file's first terminator, else CRLF).</summary>
    public string NewLine { get; private set; } = "\r\n";

    /// <summary>Encoding the file is written back with (detected on load).</summary>
    public Encoding Encoding => _encoding;

    /// <summary>True when the document did not exist on disk when it was loaded.</summary>
    public bool IsNew { get; private set; } = true;

    /// <summary>True after any change that alters the text (Set with a different value, Remove of an existing key...).</summary>
    public bool IsDirty { get; protected set; }

    /// <summary>Counts text changes; editors compare it before/after an operation to report "changed".</summary>
    private protected int Edits;

    /// <summary>Read-only view of the current lines (without terminators).</summary>
    public IReadOnlyList<string> RawLines => Lines;

    private protected void LoadFrom(string path, Encoding? asciiEncoding)
    {
        if (!File.Exists(path))
        {
            IsNew = true;
            if (asciiEncoding != null) _encoding = asciiEncoding;
            return;
        }
        var bytes = File.ReadAllBytes(path);
        IsNew = false;
        var text = Decode(bytes, asciiEncoding, out _encoding, out _preamble);
        SetText(text);
    }

    private protected void ParseFrom(string text)
    {
        IsNew = false;
        SetText(text);
    }

    private void SetText(string text)
    {
        Lines.Clear();
        Terminators.Clear();
        string? firstTerminator = null;
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c != '\r' && c != '\n') continue;
            string term = c == '\r' && i + 1 < text.Length && text[i + 1] == '\n' ? "\r\n" : c.ToString();
            Lines.Add(text[start..i]);
            Terminators.Add(term);
            firstTerminator ??= term;
            i += term.Length - 1;
            start = i + 1;
        }
        if (start < text.Length)
        {
            Lines.Add(text[start..]);
            Terminators.Add("");
        }
        if (firstTerminator != null) NewLine = firstTerminator;
        IsDirty = false;
    }

    /// <summary>
    /// Encoding detection: BOM (UTF-8 / UTF-16 LE/BE) wins; pure ASCII → <paramref name="asciiEncoding"/>
    /// (default UTF-8 without BOM; an ANSI-reading emulator such as DeSmuME passes Latin-1 so non-ASCII paths
    /// written later stay readable for it); valid UTF-8 → UTF-8 without BOM; anything else → Latin-1, which
    /// round-trips every byte unchanged.
    /// </summary>
    internal static string Decode(byte[] bytes, Encoding? asciiEncoding, out Encoding encoding, out byte[] preamble)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            encoding = new UTF8Encoding(true);
            preamble = [0xEF, 0xBB, 0xBF];
            return new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3);
        }
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            encoding = new UnicodeEncoding(false, true);
            preamble = [0xFF, 0xFE];
            return new UnicodeEncoding(false, false).GetString(bytes, 2, bytes.Length - 2);
        }
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            encoding = new UnicodeEncoding(true, true);
            preamble = [0xFE, 0xFF];
            return new UnicodeEncoding(true, false).GetString(bytes, 2, bytes.Length - 2);
        }
        preamble = [];
        bool ascii = true;
        foreach (var b in bytes)
            if (b >= 0x80) { ascii = false; break; }
        if (ascii)
        {
            encoding = asciiEncoding ?? new UTF8Encoding(false);
            return Encoding.ASCII.GetString(bytes);
        }
        try
        {
            var strict = new UTF8Encoding(false, throwOnInvalidBytes: true);
            var s = strict.GetString(bytes);
            encoding = new UTF8Encoding(false);
            return s;
        }
        catch (DecoderFallbackException)
        {
            encoding = Encoding.Latin1;
            return Encoding.Latin1.GetString(bytes);
        }
    }

    /// <summary>The document as text (with the original terminators).</summary>
    public override string ToString()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < Lines.Count; i++) sb.Append(Lines[i]).Append(Terminators[i]);
        return sb.ToString();
    }

    /// <summary>The bytes that <see cref="Save"/> writes (preamble + encoded text).</summary>
    public byte[] ToBytes()
    {
        var body = _encoding.GetBytes(ToString());
        if (_preamble.Length == 0) return body;
        var all = new byte[_preamble.Length + body.Length];
        _preamble.CopyTo(all, 0);
        body.CopyTo(all, _preamble.Length);
        return all;
    }

    /// <summary>Atomic write via <see cref="SafeFile"/>. Creates the folder if needed.</summary>
    public void Save(string path)
    {
        SafeFile.WriteAllBytes(path, ToBytes());
        IsNew = false;
        IsDirty = false;
    }

    /// <summary>Saves only when something changed or the file does not exist yet. Returns true when written.</summary>
    public bool SaveIfChanged(string path)
    {
        if (!IsDirty && !IsNew && File.Exists(path)) return false;
        Save(path);
        return true;
    }

    private protected void ReplaceLine(int index, string text)
    {
        if (Lines[index] == text) return;
        Lines[index] = text;
        IsDirty = true;
        Edits++;
    }

    private protected void InsertLine(int index, string text)
    {
        // the line before the insertion point must end with a terminator, otherwise both would merge;
        // a file without a final newline keeps that style (the new last line gets none)
        string term = NewLine;
        if (index > 0 && Terminators[index - 1].Length == 0)
        {
            Terminators[index - 1] = NewLine;
            if (index == Lines.Count) term = "";
        }
        Lines.Insert(index, text);
        Terminators.Insert(index, term);
        IsDirty = true;
        Edits++;
    }

    private protected void RemoveLine(int index)
    {
        // keep the file's "no newline at end" state when the last line goes away
        if (index == Lines.Count - 1 && index > 0 && Terminators[index].Length == 0) Terminators[index - 1] = "";
        Lines.RemoveAt(index);
        Terminators.RemoveAt(index);
        IsDirty = true;
        Edits++;
    }

    private protected void AppendLine(string text) => InsertLine(Lines.Count, text);
}
