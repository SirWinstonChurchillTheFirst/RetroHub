using System.Globalization;
using System.Text;

namespace RetroHub.Core.Data;

/// <summary>
/// Text normalisation used for searching and sorting the library:
/// lower-case, diacritics removed ("Pokémon" → "pokemon", "Ärger" → "arger"), a few special letters
/// expanded ("ß" → "ss", "æ" → "ae", "ø" → "o", …), compatibility forms folded (full-width letters, ligatures),
/// apostrophes dropped ("Link's" → "links"), common separators (- : _ . , / ! ? brackets, quotes …) and
/// whitespace collapsed to single spaces. Other symbols (e.g. % &amp; + #) are kept.
/// The same function is applied to stored titles and to user queries, so both sides always match.
/// </summary>
public static class SearchText
{
    public static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        string decomposed;
        try { decomposed = text.Normalize(NormalizationForm.FormKD); }
        catch (ArgumentException) { decomposed = text; } // invalid UTF-16 (lone surrogates): use as is

        var sb = new StringBuilder(decomposed.Length);
        var pendingSpace = false;
        foreach (var ch in decomposed)
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (cat is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark)
                continue;
            if (IsApostrophe(ch)) continue;
            if (char.IsWhiteSpace(ch) || char.IsControl(ch) || IsSeparator(ch))
            {
                pendingSpace = sb.Length > 0;
                continue;
            }
            if (pendingSpace) { sb.Append(' '); pendingSpace = false; }
            var special = Special(ch);
            if (special != null) sb.Append(special);
            else sb.Append(char.ToLowerInvariant(ch));
        }
        return sb.ToString();
    }

    /// <summary>Splits an already normalised string into its search tokens.</summary>
    internal static string[] Tokens(string normalized) =>
        normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool IsApostrophe(char ch) => ch is '\'' or '’' or '‘' or 'ʼ' or '`';

    private static bool IsSeparator(char ch) => ch switch
    {
        '-' or '‐' or '‑' or '‒' or '–' or '—' or '―' => true,
        ':' or ';' or '_' or '.' or ',' or '/' or '\\' or '|' or '~' or '·' or '•' => true,
        '!' or '?' or '¡' or '¿' => true,
        '(' or ')' or '[' or ']' or '{' or '}' => true,
        '"' or '“' or '”' or '„' or '«' or '»' or '‹' or '›' => true,
        _ => false,
    };

    private static string? Special(char ch) => ch switch
    {
        'ß' or 'ẞ' => "ss",
        'æ' or 'Æ' => "ae",
        'œ' or 'Œ' => "oe",
        'ø' or 'Ø' => "o",
        'ł' or 'Ł' => "l",
        'đ' or 'Đ' or 'ð' or 'Ð' => "d",
        'þ' or 'Þ' => "th",
        'ı' => "i",
        _ => null,
    };
}
