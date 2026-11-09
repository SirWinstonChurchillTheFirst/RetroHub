using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace RetroHub.App.Controls;

/// <summary>
/// Trimming and line glue for game titles whose namesakes differ only at the END ("BS The Legend of Zelda – Ancient
/// Stone Tablets – Kapitel 1 … 4"; M3 QA): a plain trailing ellipsis cut away exactly the part that tells them apart.
/// </summary>
public static partial class TitleTrimming
{
    /// <summary>
    /// Caption → <see cref="TextTrimming"/> for one-line rows: with a title separator (" – ", " - ", ": ") the head up to
    /// and including the first separator stays, then "…", then as much of the tail as fits
    /// ("BS The Legend of Zelda – …Kapitel 1"); without one the usual character ellipsis. Trimming applies only when the
    /// title is wider than its column. One cached instance per prefix length.
    /// </summary>
    public static readonly IValueConverter Converter = new FuncValueConverter<string?, TextTrimming>(For);

    private static readonly Dictionary<int, TextTrimming> Cache = new();

    public static TextTrimming For(string? title)
    {
        var prefix = PrefixLength(title);
        if (prefix <= 0) return TextTrimming.CharacterEllipsis;
        lock (Cache)
        {
            if (!Cache.TryGetValue(prefix, out var t)) Cache[prefix] = t = new TextLeadingPrefixTrimming("…", prefix);
            return t;
        }
    }

    /// <summary>Length of the head kept before the ellipsis (through the first separator), 0 = none / nothing left behind it.</summary>
    public static int PrefixLength(string? title)
    {
        if (string.IsNullOrEmpty(title)) return 0;
        foreach (var sep in new[] { " – ", " – ", " - ", ": " })
        {
            var i = title.IndexOf(sep, StringComparison.Ordinal);
            if (i < 2) continue;
            var end = i + sep.Length;
            return title.Length - end >= 6 ? end : 0;
        }
        return 0;
    }

    /// <summary>
    /// Line glue for wrapped titles: a no-break space before an en dash between title parts (no line starts with
    /// "– Ancient") and before a trailing number ("Kapitel 1" never leaves the "1" alone on a line).
    /// </summary>
    public static string Glue(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        var s = text.Replace(" – ", " – ", StringComparison.Ordinal);
        return TrailingNumber().Replace(s, " $1");
    }

    [GeneratedRegex(@" (\d{1,4})$", RegexOptions.CultureInvariant)]
    private static partial Regex TrailingNumber();

    /// <summary>
    /// The short form of a title that keeps head and tail: "BS The Legend of Zelda – … Kapitel 1" (head through the
    /// first separator, the last part after the last separator); null when the title has fewer than three parts (then
    /// the tail directly follows the head and a plain ellipsis loses nothing that a short form would keep).
    /// </summary>
    public static string? HeadAndTail(string title)
    {
        var head = PrefixLength(title);
        if (head <= 0) return null;
        var best = -1;
        var len = 0;
        foreach (var sep in new[] { " – ", " – ", " - ", ": " })
        {
            var i = title.LastIndexOf(sep, StringComparison.Ordinal);
            if (i > best) { best = i; len = sep.Length; }
        }
        if (best + len <= head) return null; // only one separator
        var tail = title[(best + len)..].Trim();
        return tail.Length == 0 ? null : title[..head].TrimEnd() + " … " + tail;
    }
}

/// <summary>
/// A wrapped caption (grid captions, 2 lines) that keeps the distinguishing END of a long title (M3 QA): when
/// <see cref="Source"/> does not fit its lines, a title of three or more parts is set as head + "…" + tail
/// ("BS The Legend of Zelda – … Kapitel 1"); anything else gets the usual ellipsis. Measured only when the source or
/// the width changes, and only for titles with separators.
/// </summary>
public sealed class TailCaption : TextBlock
{
    public static readonly Avalonia.StyledProperty<string?> SourceProperty =
        Avalonia.AvaloniaProperty.Register<TailCaption, string?>(nameof(Source));

    private (string? Text, int Width, double Size) _key;

    static TailCaption() => AffectsMeasure<TailCaption>(SourceProperty);

    protected override Type StyleKeyOverride => typeof(TextBlock);

    public string? Source { get => GetValue(SourceProperty); set => SetValue(SourceProperty, value); }

    protected override Avalonia.Size MeasureOverride(Avalonia.Size availableSize)
    {
        var width = availableSize.Width;
        if (!double.IsInfinity(width) && width > 0)
        {
            var key = (Source, (int)Math.Floor(width), FontSize);
            if (key != _key)
            {
                _key = key;
                var text = Source ?? "";
                if (TitleTrimming.HeadAndTail(text) is { } shortForm)
                {
                    var face = new Typeface(FontFamily, FontStyle, FontWeight, FontStretch);
                    var lines = MaxLines > 0 ? MaxLines : 2;
                    if (!FitText.Fits(text, face, FontSize, width, lines) && FitText.Fits(shortForm, face, FontSize, width, lines)) text = shortForm;
                }
                if (Text != text) SetCurrentValue(TextProperty, text);
            }
        }
        else if (Text != Source) SetCurrentValue(TextProperty, Source);
        return base.MeasureOverride(availableSize);
    }
}
