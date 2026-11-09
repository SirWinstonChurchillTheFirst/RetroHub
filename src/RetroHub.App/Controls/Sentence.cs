using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using RetroHub.App.Theming;

namespace RetroHub.App.Controls;

/// <summary>
/// Sets a sentence on a TextBlock with every number as an Archivo run (number role, bold, tabular figures) –
/// "Zahlen sind immer Archivo" (DESIGN 3.1/3.2): Atkinson's slashed zero reads technical in years, and numbers are
/// highlighted inside sentences instead of standing in columns. Usage: <c>c:Sentence.Text="{Binding MetaSentence}"</c>.
/// </summary>
public static partial class Sentence
{
    public static readonly AttachedProperty<string?> TextProperty =
        AvaloniaProperty.RegisterAttached<TextBlock, string?>("Text", typeof(Sentence));

    private static readonly FontFeatureCollection Tabular = [new FontFeature { Tag = "tnum" }];

    static Sentence()
    {
        TextProperty.Changed.AddClassHandler<TextBlock>((tb, e) => Apply(tb, e.NewValue as string));
    }

    public static string? GetText(TextBlock tb) => tb.GetValue(TextProperty);
    public static void SetText(TextBlock tb, string? value) => tb.SetValue(TextProperty, value);

    [GeneratedRegex(@"\d+(?:[.,:]\d+)*")]
    private static partial Regex Number();

    private static void Apply(TextBlock tb, string? text)
    {
        var inlines = tb.Inlines ??= new InlineCollection();
        inlines.Clear();
        if (string.IsNullOrEmpty(text)) return;
        var at = 0;
        foreach (Match m in Number().Matches(text))
        {
            if (m.Index > at) inlines.Add(new Run(text[at..m.Index]));
            inlines.Add(new Run(m.Value) { FontFamily = FontRoles.Number, FontWeight = FontWeight.Bold, FontFeatures = Tabular });
            at = m.Index + m.Length;
        }
        if (at < text.Length) inlines.Add(new Run(text[at..]));
    }
}
