using Avalonia.Media;
using RetroHub.App.Controls;
using RetroHub.App.Localization;
using RetroHub.App.Theming;
using RetroHub.App.ViewModels;
using RetroHub.Core.Library;
using Xunit;

namespace RetroHub.App.Tests;

/// <summary>Regressions of the M1 visual QA: measured names, meta sentences, motifs, launch problems.</summary>
public class DesignM1Tests
{
    // ===================== Konsolen: system names never break inside a word =====================

    /// <summary>
    /// The wall name is measured 28 → 24 → 20 px Archivo Expanded on the full inner width (wall − 2 × 20), two lines,
    /// breaking only at word boundaries; the last resort is 20 px on three lines. Long names must fit at the
    /// reference wall width (268) and at a narrow one (230) – never "PlayStati / on", never "Game B…".
    /// </summary>
    [Theory]
    [InlineData("PlayStation Portable", 268)]
    [InlineData("PlayStation Portable", 230)]
    [InlineData("Game Boy Advance", 268)]
    [InlineData("Game Boy Advance", 230)]
    [InlineData("Sega Mega Drive / Genesis", 268)]
    [InlineData("Sega Mega Drive / Genesis", 230)]
    [InlineData("Nintendo Entertainment System", 268)]
    [InlineData("Super Nintendo Entertainment System", 268)]
    public Task System_name_fits_the_wall_on_word_boundaries(string name, double wall) => UiTest.Run(() =>
    {
        var inner = wall - 40;
        var size = FitText.Choose(name, FontRoles.PosterFace, [28, 24, 20], inner, 2);
        var fits = size != null || FitText.Fits(name, FontRoles.PosterFace, 20, inner, 3);
        Assert.True(fits, $"'{name}' does not fit {inner} px without breaking a word");
    });

    [Fact]
    public Task A_word_wider_than_the_line_never_counts_as_fitting() => UiTest.Run(() =>
    {
        // "PlayStation" at 28 px is wider than 120 px: wrapping would break it inside the word
        Assert.False(FitText.Fits("PlayStation", FontRoles.PosterFace, 28, 120, 2));
        Assert.Null(FitText.Choose("PlayStation", FontRoles.PosterFace, [28, 24, 20], 120, 2));
    });

    [Fact]
    public Task The_largest_fitting_size_wins() => UiTest.Run(() =>
    {
        Assert.Equal(28, FitText.Choose("Game Boy", FontRoles.PosterFace, [28, 24, 20], 228, 2));
        Assert.Equal(72, FitText.Choose("Tetris", FontRoles.PosterFace, [72, 56, 40], 764, 2));
    });

    // ===================== M1 QA: placeholder titles in full, never broken inside a word =====================

    /// <summary>
    /// DESIGN 10.2 (M2 QA): breaks only at spaces and after hyphens – never "Castlevani / a" or "Hyrule Warrior / s";
    /// the size steps down only for a word wider than the line; more than 4 lines end in a character ellipsis.
    /// </summary>
    [Theory]
    [InlineData("Castlevania", 120)]
    [InlineData("Hyrule Warriors", 96)]
    [InlineData("Freshly-Picked Tingle's Rosy Rupeeland", 137)]
    [InlineData("Professor Layton und das geheimnisvolle Dorf", 137)]
    [InlineData("The Legend of Zelda", 137)]
    [InlineData("Pokémon Smaragd-Edition", 96)]
    public Task Placeholder_titles_break_only_between_words(string title, double width) => UiTest.Run(() =>
    {
        var start = CoverArt.PlaceholderTitleSize(width, 200);
        var fit = PlaceholderTitle.Fit(title, width, 1000, start, PlaceholderTitle.MaxLines);
        var trimmed = fit.Trimmed;
        Assert.InRange(fit.Size, PlaceholderTitle.MinSize, start);
        var lines = fit.Text.Split('\n');
        Assert.InRange(lines.Length, 1, PlaceholderTitle.MaxLines);
        // joining the lines again (a space, or nothing after a hyphen) gives the title back (or its start before "…"):
        // no break inside a word
        var joined = lines[0];
        foreach (var line in lines.Skip(1)) joined += joined.EndsWith('-') ? line : " " + line;
        if (trimmed)
        {
            Assert.EndsWith("…", joined);
            Assert.StartsWith(joined.TrimEnd('…'), title);
        }
        else Assert.Equal(title, joined);
        foreach (var line in lines)
        {
            var w = new FormattedText(line, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, FontRoles.PosterFace, fit.Size, null).Width;
            Assert.True(w <= width + 0.5, $"'{line}' is {w:0} px wide at {fit.Size} px (line {width})");
        }
    });

    [Fact]
    public void Placeholder_title_size_is_one_size_per_row()
    {
        // shelf boxes 200 high: 0.105 × min(W, 0.72 × 200) clamped to 18…26 – the narrow GC case and the wide DS case alike
        Assert.Equal(18, CoverArt.PlaceholderTitleSize(143, 200));
        Assert.Equal(18, CoverArt.PlaceholderTitleSize(222, 200));
        Assert.Equal(19, CoverArt.PlaceholderTitleSize(196, 245)); // a library slot
        Assert.Equal(32, CoverArt.PlaceholderTitleSize(300, 420)); // the stage box on Start
        Assert.Equal(38, CoverArt.PlaceholderTitleSize(360, 504)); // the stage box on Detail
        Assert.Equal(15, CoverArt.PlaceholderSubSize(143, 160)); // subtitles never below 15 px
        Assert.False(PlaceholderTitle.Fit("The Legend of Zelda", 137, 1000, 18, PlaceholderTitle.MaxLines).Trimmed);
        Assert.Equal(18, PlaceholderTitle.Fit("The Legend of Zelda", 137, 1000, 18, PlaceholderTitle.MaxLines).Size); // no step-down for lines
        Assert.Equal(14, CoverArt.PlaceholderPad(196));
        Assert.Equal(24, CoverArt.PlaceholderPad(360));
    }

    [Fact]
    public Task Poster_titles_break_after_the_separator() => UiTest.Run(() =>
    {
        var text = FitText.Typeset("The Legend of Zelda - A Link to the Past");
        Assert.Equal("The Legend of Zelda – A Link to the Past", text);
        var (size, layout) = FitText.ChooseWithBreak(text, FontRoles.PosterFace, [72, 56, 40], 1100, 2);
        Assert.Equal(72, size);
        Assert.Equal("The Legend of Zelda –\nA Link to the Past", layout);
        // a title that fits on one line stays on one line
        Assert.Equal("Tetris", FitText.ChooseWithBreak("Tetris", FontRoles.PosterFace, [72, 56, 40], 764, 2).Text);
        Assert.Equal("Castlevania:\nAria of Sorrow", FitText.SeparatorBreak("Castlevania: Aria of Sorrow"));
    });

    [Theory]
    [InlineData("Nintendo Wii U", "Nintendo Wii U")]
    [InlineData("Game Boy Advance", "Game Boy Advance")]
    [InlineData("Sega Mega Drive", "Sega Mega Drive")]
    [InlineData("Nintendo 64", "Nintendo 64")]
    public void Product_names_stay_together_on_posters(string name, string expected) =>
        Assert.Equal(expected, SystemItemViewModel.KeepTogether(name));

    // ===================== boxes: placeholders take the system's box format =====================

    [Theory]
    [InlineData("Hyrule Warriors - Zeit der Verheerung", "Hyrule Warriors", "Zeit der Verheerung")]
    [InlineData("Castlevania: Aria of Sorrow", "Castlevania", "Aria of Sorrow")]
    [InlineData("240p Test Suite", "240p Test Suite", null)]
    [InlineData("Tetris -", "Tetris -", null)]
    public void Placeholder_titles_split_into_title_and_subtitle(string title, string main, string? sub)
    {
        var (m, s) = CoverArt.SplitTitle(title);
        Assert.Equal(main, m);
        Assert.Equal(sub, s);
    }

    [Fact]
    public void A_placeholder_has_the_box_format_of_its_system()
    {
        // DESIGN 11.4 start values (height / width): SNES square, NES and Switch portrait, unknown 0.72 (10.2)
        var slot = new Avalonia.Rect(0, 0, 200, 250);
        Assert.Equal(new Avalonia.Rect(0, 50, 200, 200), CoverArt.Compute(slot, null, false, CoverArt.PlaceholderAspectFor("snes")));
        var nes = CoverArt.Compute(slot, null, false, CoverArt.PlaceholderAspectFor("nes"));
        Assert.Equal(250, nes.Height);
        Assert.Equal(Math.Round(250 / 1.38), nes.Width);
        Assert.Equal(CoverArt.PlaceholderAspect, CoverArt.PlaceholderAspectFor("my-own-system"));
    }

    // ===================== meta sentences =====================

    private static Game G(Action<Game>? set = null)
    {
        var g = new Game { SystemId = "snes", Path = "x.sfc", Title = "X", AddedUtc = new DateTime(2026, 9, 25, 12, 0, 0, DateTimeKind.Utc) };
        set?.Invoke(g);
        return g;
    }

    [Fact]
    public void Every_sentence_starts_upper_case()
    {
        Loc.Load("de");
        Assert.Equal("Super Nintendo Entertainment System. Unter 1 Min. gespielt, zuletzt heute.",
            HomeViewModel.BuildContinueMeta("Super Nintendo Entertainment System", "heute", Fmt.PlayTime(20)));
        var played = GameDetailViewModel.BuildPlaySentence(G(g => { g.PlaySeconds = 20; g.PlayCount = 2; g.LastPlayedUtc = DateTime.UtcNow; }));
        Assert.StartsWith("Unter 1 Min. in 2 Sitzungen gespielt", played);
        Assert.Equal("Unter 1 Min.", Fmt.SentenceStart("unter 1 Min."));
        Assert.Equal("46 Std.", Fmt.SentenceStart("46 Std."));
    }

    [Fact]
    public void Regions_become_words_and_one_player_is_a_word()
    {
        Loc.Load("de");
        var s = GameDetailViewModel.BuildMetaSentence(G(g => { g.Genre = "Rollenspiel"; g.Developer = "Game Freak"; g.Year = 2004; g.Players = 1; g.Region = "EUR"; }), "Game Boy Advance");
        Assert.Equal("Rollenspiel von Game Freak, 2004. Ein Spieler. Europa.", s);
        Assert.Equal("1–4 Spieler.", GameDetailViewModel.BuildMetaSentence(G(g => g.Players = 4), "SNES"));
        Assert.Equal("Japan.", GameDetailViewModel.BuildMetaSentence(G(g => g.Region = "JPN"), "SNES"));
    }

    [Fact]
    public void A_language_is_never_the_only_fact()
    {
        Loc.Load("de");
        // only a language: the sentence names the system instead of a lone "Englisch."
        Assert.Equal("Super Nintendo Entertainment System.",
            GameDetailViewModel.BuildMetaSentence(G(g => g.Region = "Englisch"), "Super Nintendo Entertainment System"));
        // beside other facts the language stays
        Assert.Equal("Action, 1993. Englisch.", GameDetailViewModel.BuildMetaSentence(G(g => { g.Genre = "Action"; g.Year = 1993; g.Region = "Englisch"; }), "SNES"));
    }

    // ===================== motifs and launch problems =====================

    [Fact]
    public Task Every_motif_parses_in_any_culture() => UiTest.Run(() =>
    {
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE"); // decimal comma
        try
        {
            foreach (var motif in Enum.GetValues<Motif>())
                Assert.NotEmpty(MotifCatalog.Get(motif).Parts);
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = culture; }
    });

    [Fact]
    public void Dark_bodies_are_lifted_off_the_black_wall()
    {
        // Master System, Mega Drive, PS2: the "Zweitfarbe" is near-black on a black wall – the body must stay visible
        foreach (var id in new[] { "mastersystem", "megadrive", "ps2", "segacd" })
        {
            var look = SystemPalette.Get(id);
            var body = Assert.IsAssignableFrom<ISolidColorBrush>(MotifCatalog.BodyBrush(look)).Color;
            Assert.True(ColorUtil.Contrast(body, look.Wall) >= 1.6, $"{id}: body {body} on wall {look.Wall}");
        }
    }

    [Fact]
    public void A_launch_problem_is_one_sentence_with_the_link_behind_an_action()
    {
        var p = ProblemItem.From("RetroArch ist nicht installiert. Entpacke den Emulator in den Ordner „emulators/retroarch/“.\nDownload: https://www.retroarch.com/?page=platforms", true);
        Assert.Equal("RetroArch ist nicht installiert.", p.Text);
        Assert.Equal("https://www.retroarch.com/?page=platforms", p.Url);
        Assert.True(p.HasUrl);
        var q = ProblemItem.From("Die Datei fehlt.", false);
        Assert.Equal("Die Datei fehlt.", q.Text);
        Assert.False(q.HasUrl);
    }
}
