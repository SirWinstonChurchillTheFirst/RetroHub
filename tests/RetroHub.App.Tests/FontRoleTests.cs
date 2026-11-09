using Avalonia;
using Avalonia.Media;
using RetroHub.App.Theming;
using Xunit;

namespace RetroHub.App.Tests;

/// <summary>
/// The font roles of DESIGN 3.1 resolve to the shipped files: family, weight and width. Guards the Avalonia 12.1.5
/// finding that several Archivo widths in ONE folder all resolve to "Archivo Condensed" (one folder per width).
/// </summary>
public class FontRoleTests
{
    /// <summary>Typographic family (name ID 16) when the file has one, else the family (ID 1).</summary>
    private static string Family(GlyphTypeface face) => string.IsNullOrEmpty(face.TypographicFamilyName) ? face.FamilyName : face.TypographicFamilyName;

    private static GlyphTypeface Resolve(string resourceKey, FontWeight weight)
    {
        Assert.True(Application.Current!.TryGetResource(resourceKey, null, out var res), resourceKey);
        var family = Assert.IsType<FontFamily>(res);
        Assert.True(FontManager.Current.TryGetGlyphTypeface(new Typeface(family, FontStyle.Normal, weight), out var face), $"{resourceKey} {weight}");
        return face!;
    }

    [Theory]
    [InlineData("RhFontPoster", FontWeight.ExtraBold, "Archivo Expanded", 800, FontStretch.Expanded)]
    [InlineData("RhFontNumber", FontWeight.Bold, "Archivo SemiExpanded", 700, FontStretch.SemiExpanded)]
    [InlineData("RhFontUi", FontWeight.Medium, "Archivo SemiCondensed", 500, FontStretch.SemiCondensed)]
    [InlineData("RhFontUi", FontWeight.SemiBold, "Archivo SemiCondensed", 600, FontStretch.SemiCondensed)]
    [InlineData("RhFont", FontWeight.Medium, "Archivo SemiCondensed", 500, FontStretch.SemiCondensed)]
    [InlineData("RhFontSpine", FontWeight.Bold, "Archivo Condensed", 680, FontStretch.Condensed)]
    [InlineData("RhFontRead", FontWeight.Normal, "Atkinson Hyperlegible Next", 400, FontStretch.Normal)]
    [InlineData("RhFontRead", FontWeight.Medium, "Atkinson Hyperlegible Next", 500, FontStretch.Normal)]
    [InlineData("RhFontRead", FontWeight.SemiBold, "Atkinson Hyperlegible Next", 600, FontStretch.Normal)]
    public Task Role_resolves_to_its_file(string key, FontWeight weight, string family, int fileWeight, FontStretch stretch) => UiTest.Run(() =>
    {
        var face = Resolve(key, weight);
        Assert.Equal(family, Family(face));
        Assert.Equal(fileWeight, (int)face.Weight);
        Assert.Equal(stretch, face.Stretch);
    });

    [Fact]
    public Task Code_roles_use_the_same_files() => UiTest.Run(() =>
    {
        Assert.True(FontManager.Current.TryGetGlyphTypeface(FontRoles.PosterFace, out var poster));
        Assert.Equal("Archivo Expanded", Family(poster!));
        Assert.True(FontManager.Current.TryGetGlyphTypeface(FontRoles.SpineFace, out var spine));
        Assert.Equal("Archivo Condensed", Family(spine!));
        Assert.True(FontManager.Current.TryGetGlyphTypeface(FontRoles.ReadFace, out var read));
        Assert.Equal("Atkinson Hyperlegible Next", Family(read!));
    });

    [Fact]
    public Task Tabular_figures_make_digits_equally_wide() => UiTest.Run(() =>
    {
        // DESIGN 3.1: "+tnum" works in Avalonia 12; numbers always use the number role with tnum
        static double Width(string s, bool tnum)
        {
            var layout = new Avalonia.Media.TextFormatting.TextLayout(s, FontRoles.NumberFace, 40, Brushes.White,
                fontFeatures: tnum ? [new FontFeature { Tag = "tnum" }] : null);
            return layout.WidthIncludingTrailingWhitespace;
        }
        Assert.Equal(Width("8888", true), Width("1111", true), 1);
        Assert.NotEqual(Width("8888", false), Width("1111", false), 1);
    });
}
