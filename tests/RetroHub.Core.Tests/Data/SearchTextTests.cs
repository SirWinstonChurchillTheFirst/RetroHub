using RetroHub.Core.Data;
using Xunit;

namespace RetroHub.Core.Tests.Data;

public sealed class SearchTextTests
{
    [Theory]
    [InlineData("Pokémon Rot", "pokemon rot")]
    [InlineData("POKÉMON", "pokemon")]
    [InlineData("Ärger mit Öl über Straße", "arger mit ol uber strasse")]
    [InlineData("The Legend of Zelda: Link's Awakening", "the legend of zelda links awakening")]
    [InlineData("F-Zero  X", "f zero x")]
    [InlineData("super_mario_world", "super mario world")]
    [InlineData("Ｆｕｌｌｗｉｄｔｈ", "fullwidth")]
    [InlineData("Œuvre Æon Ørsted Łódź", "oeuvre aeon orsted lodz")]
    [InlineData("100% Orange Juice", "100% orange juice")]
    [InlineData("  (Europe) [!]  ", "europe")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Normalize_FoldsCaseDiacriticsAndSeparators(string? input, string expected) =>
        Assert.Equal(expected, SearchText.Normalize(input));

    [Fact]
    public void Normalize_NeverEmitsFieldSeparator() =>
        Assert.DoesNotContain('\u001F', SearchText.Normalize("a\u001Fb\tc\r\nd"));
}
