using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using RetroHub.App.Theming;

namespace RetroHub.App.Controls;

/// <summary>
/// A measured poster title (DESIGN 3.2): <see cref="Source"/> is laid out with a <see cref="TextLayout"/> at each of
/// <see cref="Sizes"/> (largest first) on the available width with at most <see cref="TextBlock.MaxLines"/> lines,
/// breaking ONLY at word boundaries; the largest size without overflow wins. When none fits, the
/// <see cref="FallbackSource"/> (e.g. the system code) is shown in the spine role instead, else the smallest size with
/// an ellipsis. Never a per-character break in the middle of a word ("PlayStati / on"). Measured on change only
/// (text, width bucket) – a handful of layouts, never in Render. Style the font family/weight with a class
/// ("poster"); this control only sets the size and line height.
/// </summary>
public sealed class FitText : TextBlock
{
    public static readonly StyledProperty<string?> SourceProperty = AvaloniaProperty.Register<FitText, string?>(nameof(Source));
    public static readonly StyledProperty<string?> FallbackSourceProperty = AvaloniaProperty.Register<FitText, string?>(nameof(FallbackSource));
    /// <summary>Candidate sizes, largest first ("72,56,40").</summary>
    public static readonly StyledProperty<string> SizesProperty = AvaloniaProperty.Register<FitText, string>(nameof(Sizes), "72,56,40");
    public static readonly StyledProperty<double> FallbackSizeProperty = AvaloniaProperty.Register<FitText, double>(nameof(FallbackSize), 28);
    /// <summary>Last resort before the fallback: the smallest size with this many lines (0 = off).</summary>
    public static readonly StyledProperty<int> LastResortLinesProperty = AvaloniaProperty.Register<FitText, int>(nameof(LastResortLines));
    /// <summary>Line height as a factor of the chosen size.</summary>
    public static readonly StyledProperty<double> LineFactorProperty = AvaloniaProperty.Register<FitText, double>(nameof(LineFactor), 1.1);

    private (string? Text, int Width, string Sizes, int Lines) _key;
    private int _lines;

    static FitText()
    {
        AffectsMeasure<FitText>(SourceProperty, FallbackSourceProperty, SizesProperty, FallbackSizeProperty, LineFactorProperty);
    }

    protected override Type StyleKeyOverride => typeof(TextBlock);

    public string? Source { get => GetValue(SourceProperty); set => SetValue(SourceProperty, value); }
    public string? FallbackSource { get => GetValue(FallbackSourceProperty); set => SetValue(FallbackSourceProperty, value); }
    public string Sizes { get => GetValue(SizesProperty); set => SetValue(SizesProperty, value); }
    public double FallbackSize { get => GetValue(FallbackSizeProperty); set => SetValue(FallbackSizeProperty, value); }
    public double LineFactor { get => GetValue(LineFactorProperty); set => SetValue(LineFactorProperty, value); }
    public int LastResortLines { get => GetValue(LastResortLinesProperty); set => SetValue(LastResortLinesProperty, value); }

    /// <summary>The chosen size (NaN = the fallback is shown).</summary>
    public double ChosenSize { get; private set; } = double.NaN;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SourceProperty || change.Property == FallbackSourceProperty || change.Property == SizesProperty)
            _key = default;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = availableSize.Width;
        if (!double.IsInfinity(width) && width > 0) Fit(width);
        return base.MeasureOverride(availableSize);
    }

    private void Fit(double width)
    {
        if (_lines == 0) _lines = MaxLines > 0 ? MaxLines : 2; // the configured line count (MaxLines may grow to LastResortLines)
        var lines = _lines;
        var key = (Source, (int)Math.Floor(width), Sizes, lines);
        if (key == _key) return;
        _key = key;
        var sizes = ParseSizes(Sizes);
        var face = new Typeface(FontFamily, FontStyle, FontWeight, FontStretch);
        var size = Choose(Source ?? "", face, sizes, width, lines);
        var usedLines = lines;
        if (size == null && LastResortLines > lines && sizes.Length > 0 && Fits(Source ?? "", face, sizes[^1], width, LastResortLines))
        {
            size = sizes[^1];
            usedLines = LastResortLines;
        }
        if (MaxLines != usedLines && size != null) SetCurrentValue(MaxLinesProperty, usedLines);
        TextWrapping = TextWrapping.Wrap;
        if (size is { } s)
        {
            ChosenSize = s;
            Classes.Set("fit-fallback", false);
            Apply(Source, s, TextTrimming.None);
        }
        else if (!string.IsNullOrEmpty(FallbackSource))
        {
            ChosenSize = double.NaN;
            Classes.Set("fit-fallback", true);
            Apply(FallbackSource, FallbackSize, TextTrimming.CharacterEllipsis);
        }
        else
        {
            ChosenSize = sizes.Length > 0 ? sizes[^1] : FontSize;
            Classes.Set("fit-fallback", false);
            Apply(Source, ChosenSize, TextTrimming.WordEllipsis);
        }
    }

    private void Apply(string? text, double size, TextTrimming trimming)
    {
        if (Text != text) Text = text;
        if (FontSize != size) FontSize = size;
        var lh = Math.Round(size * LineFactor);
        if (LineHeight != lh) LineHeight = lh;
        if (TextTrimming != trimming) TextTrimming = trimming;
    }

    private static double[] ParseSizes(string s) =>
        s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(v => double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0)
            .Where(d => d > 0).ToArray();

    /// <summary>
    /// The largest of <paramref name="sizes"/> at which <paramref name="text"/> fits <paramref name="width"/> in at most
    /// <paramref name="maxLines"/> lines, wrapping only at word boundaries (a word wider than the line = no fit); null = none.
    /// </summary>
    public static double? Choose(string text, Typeface face, IReadOnlyList<double> sizes, double width, int maxLines)
    {
        if (string.IsNullOrWhiteSpace(text)) return sizes.Count > 0 ? sizes[0] : null;
        foreach (var size in sizes)
            if (Fits(text, face, size, width, maxLines)) return size;
        return null;
    }

    public static bool Fits(string text, Typeface face, double size, double width, int maxLines)
    {
        using var layout = new TextLayout(text, face, size, null, TextAlignment.Left, TextWrapping.WrapWithOverflow, maxWidth: width);
        if (layout.TextLines.Count > maxLines) return false;
        foreach (var line in layout.TextLines)
            if (line.WidthIncludingTrailingWhitespace - TrailingSpace(line) > width + 0.5) return false;
        return true;
    }

    private static double TrailingSpace(TextLine line) => line.WidthIncludingTrailingWhitespace - line.Width;
}
