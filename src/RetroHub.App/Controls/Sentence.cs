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

    /// <summary>Marks a program name in a sentence: <c>"Startet mit " + Sentence.Name("mGBA") + " …"</c>.</summary>
    public const string NameMark = "⁠⁠";

    /// <summary>A program name inside a sentence: read SemiBold Papier (QA 2, DESIGN 3.1), never the number face.</summary>
    public static string Name(string name) => NameMark + name + NameMark;

    private static void Apply(TextBlock tb, string? text)
    {
        var inlines = tb.Inlines ??= new InlineCollection();
        inlines.Clear();
        if (string.IsNullOrEmpty(text)) return;
        var parts = text.Split(NameMark);
        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length == 0) continue;
            if (i % 2 == 1)
            {
                // the look comes from the style "Run.name" (SemiBold Papier; on paper – the Systeminfo card – ink)
                var name = new Run(parts[i]);
                name.Classes.Add("name");
                inlines.Add(name);
            }
            else AddWithNumbers(inlines, parts[i]);
        }
    }

    private static void AddWithNumbers(InlineCollection inlines, string text)
    {
        var at = 0;
        foreach (Match m in Number().Matches(text))
        {
            if (m.Index > at) inlines.Add(new Run(text[at..m.Index]));
            inlines.Add(new Run(m.Value) { FontFamily = FontRoles.Number, FontWeight = FontWeight.Bold, FontFeatures = Tabular });
            at = m.Index + m.Length;
        }
        if (at < text.Length) inlines.Add(new Run(text[at..]));
    }

    /// <summary>
    /// The same rule for drawn text: every number of <paramref name="t"/> (made from <paramref name="text"/>) in the
    /// number face, bold, tabular – "Nintendo, 1996" on a front, never Atkinson's slashed zero in a year.
    /// </summary>
    public static FormattedText WithNumbers(FormattedText t, string text)
    {
        foreach (Match m in Number().Matches(text))
        {
            t.SetFontTypeface(FontRoles.NumberFace, m.Index, m.Length);
            t.SetFontFeatures(Tabular, m.Index, m.Length);
        }
        return t;
    }
}
