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
    public Task Light_colour_swap_replaces_the_tokens() => UiTest.Run(() =>
    {
        var app = Application.Current!;
        ThemeManager.ApplyAccent("#8CC4FF"); // Eisblau preset
        Assert.True(app.TryGetResource("RhAccentBrush", null, out var brush));
        Assert.Equal(Color.Parse("#8CC4FF"), ((ISolidColorBrush)brush!).Color);
        Assert.True(app.TryGetResource("SystemAccentColor", null, out var fluent));
        Assert.Equal(Color.Parse("#8CC4FF"), (Color)fluent!);
        // the legacy gradient keys are solid now (no decorative gradients)
        Assert.True(app.TryGetResource("RhAccentGradientBrush", null, out var legacy));
        Assert.IsAssignableFrom<ISolidColorBrush>(legacy);

        ThemeManager.ApplyAccent(null); // back to Standby-Bernstein
        app.TryGetResource("RhAccentBBrush", null, out var b);
        Assert.Equal(Color.Parse("#FFB547"), ((ISolidColorBrush)b!).Color);
        Assert.Equal(Color.Parse("#FFB547"), ThemeManager.CurrentAccent);

        // an old "Nova" accent from a v0 settings file is not part of the warm palette: Bernstein
        ThemeManager.ApplyAccent("#7B61FF");
        Assert.Equal(Color.Parse("#FFB547"), ThemeManager.CurrentAccent);
        ThemeManager.ApplyAccent(null);
    });

    [Fact]
    public void Ink_reaches_8_to_1_on_every_light_colour()
    {
        // DESIGN 2.2: on all light colours ink is ≥ 8 : 1
        foreach (var p in ThemeManager.Presets)
            Assert.True(ColorUtil.Contrast(Color.Parse("#14120F"), Color.Parse(p.Hex)) >= 8.0, $"{p.Id} {p.Hex}");
    }

    [Fact]
    public Task Board_light_is_one_shared_brush() => UiTest.Run(() =>
    {
        var app = Application.Current!;
        ThemeManager.ApplyAccent(null);
        Assert.True(app.TryGetResource("RhBoardLightBrush", null, out var a));
        ThemeManager.SetBoardLight(Color.Parse("#59FFB547"));
        ThemeManager.ApplyAccent(null);
        Assert.True(app.TryGetResource("RhBoardLightBrush", null, out var b));
        Assert.Same(a, b);
        Assert.Same(ThemeManager.BoardLight, a);
        ThemeManager.SetBoardLight(ThemeManager.NeutralBoardLight);
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
