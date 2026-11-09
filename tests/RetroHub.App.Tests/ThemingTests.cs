using Avalonia;
using Avalonia.Media;
using RetroHub.App.Theming;
using RetroHub.App.ViewModels;
using RetroHub.Core.Config;
using Xunit;

namespace RetroHub.App.Tests;

/// <summary>Accent token swap, motion levels, formatting helpers.</summary>
public class ThemingTests
{
    [Fact]
    public Task Accent_swap_replaces_the_tokens() => UiTest.Run(() =>
    {
        var app = Application.Current!;
        ThemeManager.ApplyAccent("#2F9BFF");
        Assert.True(app.TryGetResource("RhAccentBrush", null, out var brush));
        Assert.Equal(Color.Parse("#2F9BFF"), ((ISolidColorBrush)brush!).Color);
        Assert.True(app.TryGetResource("SystemAccentColor", null, out var fluent));
        Assert.Equal(Color.Parse("#2F9BFF"), (Color)fluent!);

        ThemeManager.ApplyAccent(null); // back to Nova
        app.TryGetResource("RhAccentBBrush", null, out var b);
        Assert.Equal(Color.Parse("#4C8DFF"), ((ISolidColorBrush)b!).Color);
        Assert.Equal(Color.Parse("#7B61FF"), ThemeManager.CurrentAccent);
    });

    [Theory]
    [InlineData(MotionLevel.System, true, EffectiveMotion.Full)]
    [InlineData(MotionLevel.System, false, EffectiveMotion.Off)]
    [InlineData(MotionLevel.Reduced, true, EffectiveMotion.Reduced)]
    [InlineData(MotionLevel.Off, true, EffectiveMotion.Off)]
    [InlineData(MotionLevel.Full, false, EffectiveMotion.Full)]
    public void Motion_level_follows_windows_only_for_system(MotionLevel level, bool windowsAnimations, EffectiveMotion expected) =>
        Assert.Equal(expected, Motion.Resolve(level, windowsAnimations));

    [Theory]
    [InlineData("The Legend of Zelda: A Link to the Past", "LZ")]
    [InlineData("Tetris", "TE")]
    [InlineData("Pokémon Smaragd-Edition", "PS")]
    [InlineData("F-Zero", "FZ")]
    public void Placeholder_initials(string title, string expected) => Assert.Equal(expected, Fmt.Initials(title));

    [Fact]
    public void Gradient_partner_shifts_hue()
    {
        var b = ColorUtil.GradientPartner(Color.Parse("#7B61FF"));
        var (h, _, _) = ColorUtil.ToHsl(b);
        Assert.InRange(h, 205, 230); // Nova: #7B61FF (≈252°) → #4C8DFF (≈218°)
    }
}
