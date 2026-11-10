using RetroHub.Core;
using RetroHub.Core.Catalog;
using RetroHub.Core.Library;

namespace RetroHub.App.Services.Demo;

/// <summary>The demo library: realistic titles with metadata, favourites, play history. Deterministic.</summary>
public static class DemoData
{
    private sealed record Entry(string System, string Title, int Year, string Developer, string Genre, int Players = 1, string? Description = null);

    private static readonly Entry[] Entries =
    [
        new("nes", "Super Mario Bros.", 1985, "Nintendo", "Jump 'n' Run", 2, "Der Klassiker, der das Jump 'n' Run definiert hat: Mario rettet Prinzessin Toadstool durch acht Welten voller Gumbas und Röhren."),
        new("nes", "The Legend of Zelda", 1986, "Nintendo", "Action-Adventure", 1, "Link erkundet das offene Hyrule, sammelt die Fragmente des Triforce der Weisheit und stellt sich Ganon."),
        new("nes", "Metroid", 1986, "Nintendo", "Action-Adventure"),
        new("nes", "Mega Man 2", 1988, "Capcom", "Action"),
        new("nes", "Castlevania", 1986, "Konami", "Action"),
        new("nes", "Kirby's Adventure", 1993, "HAL Laboratory", "Jump 'n' Run"),
        new("snes", "Super Mario World", 1990, "Nintendo", "Jump 'n' Run", 2, "Mario und Yoshi durchqueren Dinosaurier-Land – mit Geheimausgängen, Sternenstraße und Cape-Feder."),
        new("snes", "The Legend of Zelda: A Link to the Past", 1991, "Nintendo", "Action-Adventure", 1, "Zwischen Licht- und Schattenwelt: Link muss sieben Weise befreien, um Hyrule vor Ganon zu bewahren."),
        new("snes", "Super Metroid", 1994, "Nintendo", "Action-Adventure", 1, "Samus Aran kehrt nach Zebes zurück. Atmosphärisch, nicht-linear und bis heute Vorbild eines ganzen Genres."),
        new("snes", "Chrono Trigger", 1995, "Square", "Rollenspiel", 1, "Eine Zeitreise über Jahrtausende mit dreizehn möglichen Enden – eines der besten Rollenspiele aller Zeiten."),
        new("snes", "Donkey Kong Country", 1994, "Rare", "Jump 'n' Run", 2),
        new("snes", "Secret of Mana", 1993, "Square", "Action-Rollenspiel", 3),
        new("snes", "F-Zero", 1990, "Nintendo", "Rennspiel"),
        new("n64", "Super Mario 64", 1996, "Nintendo", "3D-Jump 'n' Run", 1, "Marios Sprung in die dritte Dimension: 120 Sterne in Prinzessin Peachs Schloss warten auf dich."),
        new("n64", "The Legend of Zelda: Ocarina of Time", 1998, "Nintendo", "Action-Adventure"),
        new("n64", "Mario Kart 64", 1996, "Nintendo", "Rennspiel", 4),
        new("n64", "GoldenEye 007", 1997, "Rare", "Ego-Shooter", 4),
        new("n64", "Banjo-Kazooie", 1998, "Rare", "3D-Jump 'n' Run"),
        new("gc", "Super Smash Bros. Melee", 2001, "HAL Laboratory", "Kampfspiel", 4),
        new("gc", "Metroid Prime", 2002, "Retro Studios", "Action-Adventure"),
        new("gc", "The Legend of Zelda: The Wind Waker", 2002, "Nintendo", "Action-Adventure", 1, "Mit Segelboot und Taktstock über ein endloses Meer – im zeitlosen Cel-Shading-Look."),
        new("gc", "Pikmin", 2001, "Nintendo", "Strategie"),
        new("gc", "F-Zero GX", 2003, "Amusement Vision", "Rennspiel", 4),
        new("gb", "Tetris", 1989, "Nintendo", "Puzzle", 2, "Das Puzzle, das den Game Boy groß gemacht hat. Einfach zu lernen, unmöglich zu meistern."),
        new("gb", "Pokémon Rote Edition", 1996, "Game Freak", "Rollenspiel"),
        new("gb", "Super Mario Land 2: 6 Golden Coins", 1992, "Nintendo", "Jump 'n' Run"),
        new("gb", "Kirby's Dream Land", 1992, "HAL Laboratory", "Jump 'n' Run"),
        new("gb", "The Legend of Zelda: Link's Awakening", 1993, "Nintendo", "Action-Adventure"),
        new("gbc", "Pokémon Kristall-Edition", 2000, "Game Freak", "Rollenspiel", 1, "Reise durch Johto, fang Suicune und erlebe erstmals Tag und Nacht in der Welt der Pokémon."),
        new("gbc", "The Legend of Zelda: Oracle of Seasons", 2001, "Capcom", "Action-Adventure"),
        new("gbc", "Wario Land 3", 2000, "Nintendo", "Jump 'n' Run"),
        new("gbc", "Shantae", 2002, "WayForward", "Action-Plattformer"),
        new("gba", "Pokémon Smaragd-Edition", 2004, "Game Freak", "Rollenspiel", 1, "Hoenn in seiner schönsten Form – mit Kampfzone, Rayquaza und Doppelkämpfen."),
        new("gba", "Metroid Fusion", 2002, "Nintendo", "Action-Adventure"),
        new("gba", "Advance Wars", 2001, "Intelligent Systems", "Strategie", 4),
        new("gba", "Golden Sun", 2001, "Camelot", "Rollenspiel"),
        new("gba", "Mario Kart: Super Circuit", 2001, "Intelligent Systems", "Rennspiel", 4),
        new("gba", "Fire Emblem", 2003, "Intelligent Systems", "Taktik-Rollenspiel"),
        new("gba", "Castlevania: Aria of Sorrow", 2003, "Konami", "Action-Adventure"),
        new("nds", "New Super Mario Bros.", 2006, "Nintendo", "Jump 'n' Run", 4),
        new("nds", "Mario Kart DS", 2005, "Nintendo", "Rennspiel", 8),
        new("nds", "Pokémon Platin-Edition", 2008, "Game Freak", "Rollenspiel"),
        new("nds", "Professor Layton und das geheimnisvolle Dorf", 2007, "Level-5", "Rätsel-Adventure", 1, "Der Gentleman-Professor und sein Assistent Luke lösen in St. Mystere über hundert knifflige Rätsel."),
        new("nds", "Phoenix Wright: Ace Attorney", 2005, "Capcom", "Adventure"),
        new("3ds", "The Legend of Zelda: A Link Between Worlds", 2013, "Nintendo", "Action-Adventure"),
        new("3ds", "Super Mario 3D Land", 2011, "Nintendo", "3D-Jump 'n' Run"),
        new("3ds", "Fire Emblem Awakening", 2012, "Intelligent Systems", "Taktik-Rollenspiel"),
        new("3ds", "Kid Icarus: Uprising", 2012, "Project Sora", "Action", 6),
        new("psx", "Final Fantasy VII", 1997, "Square", "Rollenspiel", 1, "Cloud, Tifa und Aerith gegen Shinra und Sephiroth – das Rollenspiel, das eine Generation geprägt hat."),
        new("psx", "Metal Gear Solid", 1998, "Konami", "Stealth-Action"),
        new("psx", "Crash Bandicoot", 1996, "Naughty Dog", "Jump 'n' Run"),
        new("psx", "Spyro the Dragon", 1998, "Insomniac Games", "3D-Jump 'n' Run"),
        new("psx", "Castlevania: Symphony of the Night", 1997, "Konami", "Action-Adventure"),
        new("psx", "Gran Turismo", 1997, "Polyphony Digital", "Rennsimulation", 2),
        new("ps2", "Shadow of the Colossus", 2005, "Team Ico", "Action-Adventure", 1, "Sechzehn Kolosse, ein Pferd, ein Schwert – und eine leise, gewaltige Welt."),
        new("ps2", "Final Fantasy X", 2001, "Square", "Rollenspiel"),
        new("ps2", "Ratchet & Clank", 2002, "Insomniac Games", "Action-Plattformer"),
        new("ps2", "Okami", 2006, "Clover Studio", "Action-Adventure"),
        new("ps2", "Kingdom Hearts", 2002, "Square", "Action-Rollenspiel"),
        new("psp", "Lumines", 2004, "Q Entertainment", "Puzzle", 2),
        new("psp", "Monster Hunter Freedom Unite", 2009, "Capcom", "Action-Rollenspiel", 4),
        new("psp", "Patapon", 2007, "Pyramid", "Rhythmus-Strategie"),
        new("psp", "Crisis Core: Final Fantasy VII", 2007, "Square Enix", "Action-Rollenspiel"),
        new("md", "Sonic the Hedgehog", 1991, "Sonic Team", "Jump 'n' Run"),
        new("md", "Streets of Rage 2", 1992, "Sega", "Beat 'em up", 2),
        new("md", "Gunstar Heroes", 1993, "Treasure", "Run and Gun", 2),
    ];

    /// <summary>Design QA (DESIGN 7.4): the demo carries every catalog system (see <see cref="DemoCatalog"/>).</summary>
    public static bool AllSystems { get; set; }

    /// <summary>A few games for each system the "all systems" demo adds (no covers: every box a spine placeholder).</summary>
    private static readonly Entry[] ExtraEntries =
    [
        new("virtualboy", "Mario's Tennis", 1995, "Nintendo", "Sport", 1), new("virtualboy", "Wario Land", 1995, "Nintendo", "Jump 'n' Run"),
        new("wii", "Super Mario Galaxy", 2007, "Nintendo", "3D-Jump 'n' Run"), new("wii", "Xenoblade Chronicles", 2010, "Monolith Soft", "Rollenspiel"),
        new("wii", "Wii Sports", 2006, "Nintendo", "Sport", 4),
        new("wiiu", "Super Mario 3D World", 2013, "Nintendo", "3D-Jump 'n' Run", 4), new("wiiu", "Splatoon", 2015, "Nintendo", "Shooter", 8),
        new("switch", "The Legend of Zelda: Breath of the Wild", 2017, "Nintendo", "Action-Adventure"), new("switch", "Celeste", 2018, "Maddy Makes Games", "Jump 'n' Run"),
        new("switch", "Hades", 2020, "Supergiant Games", "Action-Rollenspiel"),
        new("atari2600", "Pitfall!", 1982, "Activision", "Action"), new("atari2600", "Adventure", 1980, "Atari", "Adventure"),
        new("atari2600", "River Raid", 1982, "Activision", "Shoot 'em up"),
        new("atari7800", "Ninja Golf", 1990, "BlueSky Software", "Sport"), new("atari7800", "Food Fight", 1987, "General Computer", "Action"),
        new("lynx", "California Games", 1989, "Epyx", "Sport", 4), new("lynx", "Chip's Challenge", 1989, "Epyx", "Puzzle"),
        new("pcengine", "Bonk's Adventure", 1989, "Red Company", "Jump 'n' Run"), new("pcengine", "Blazing Lazers", 1989, "Compile", "Shoot 'em up"),
        new("ngp", "SNK vs. Capcom: The Match of the Millennium", 1999, "SNK", "Kampfspiel", 2), new("ngp", "Sonic the Hedgehog Pocket Adventure", 1999, "Sega", "Jump 'n' Run"),
        new("wonderswan", "Gunpey", 1999, "Bandai", "Puzzle"), new("wonderswan", "Klonoa: Moonlight Museum", 1999, "Namco", "Jump 'n' Run"),
        new("msx", "Metal Gear", 1987, "Konami", "Stealth-Action"), new("msx", "Knightmare", 1986, "Konami", "Shoot 'em up"),
        new("mastersystem", "Alex Kidd in Miracle World", 1986, "Sega", "Jump 'n' Run"), new("mastersystem", "Phantasy Star", 1987, "Sega", "Rollenspiel"),
        new("mastersystem", "Wonder Boy III: The Dragon's Trap", 1989, "Westone", "Action-Adventure"),
        new("gamegear", "Shinobi", 1991, "Sega", "Action"), new("gamegear", "Sonic Triple Trouble", 1994, "Aspect", "Jump 'n' Run"),
        new("segacd", "Sonic CD", 1993, "Sega", "Jump 'n' Run"), new("segacd", "Snatcher", 1994, "Konami", "Adventure"),
        new("sega32x", "Knuckles' Chaotix", 1995, "Sega", "Jump 'n' Run", 2), new("sega32x", "Virtua Racing Deluxe", 1994, "Sega", "Rennspiel", 2),
        new("saturn", "Panzer Dragoon Saga", 1998, "Team Andromeda", "Rollenspiel"), new("saturn", "NiGHTS into Dreams", 1996, "Sonic Team", "Action"),
        new("saturn", "Guardian Heroes", 1996, "Treasure", "Beat 'em up", 2),
        new("dreamcast", "Jet Set Radio", 2000, "Smilebit", "Action"), new("dreamcast", "Shenmue", 1999, "Sega AM2", "Adventure"),
        new("dreamcast", "Rez", 2001, "United Game Artists", "Rhythmus-Shooter"),
        new("arcade", "Metal Slug", 1996, "Nazca", "Run and Gun", 2), new("arcade", "Street Fighter II", 1991, "Capcom", "Kampfspiel", 2),
        new("pc", "Doom", 1993, "id Software", "Ego-Shooter"), new("pc", "Monkey Island 2", 1991, "LucasArts", "Adventure"),
    ];

    // title → (days since last played, play minutes, play count)
    private static readonly Dictionary<string, (double Days, int Minutes, int Count)> History = new()
    {
        ["Pokémon Smaragd-Edition"] = (0.08, 2760, 41),
        ["The Legend of Zelda: A Link to the Past"] = (1.2, 845, 17),
        ["Super Mario 64"] = (2.5, 610, 12),
        ["Metroid Prime"] = (4.1, 1220, 9),
        ["Chrono Trigger"] = (6.3, 1980, 22),
        ["Mario Kart DS"] = (8.0, 315, 14),
        ["Tetris"] = (11.0, 190, 30),
        ["Final Fantasy VII"] = (15.0, 3105, 27),
        ["Professor Layton und das geheimnisvolle Dorf"] = (21.0, 540, 8),
        ["Super Metroid"] = (33.0, 470, 6),
        ["Shadow of the Colossus"] = (48.0, 655, 7),
        ["Sonic the Hedgehog"] = (60.0, 85, 5),
    };

    private static readonly HashSet<string> Favorites =
    [
        "Pokémon Smaragd-Edition", "The Legend of Zelda: A Link to the Past", "Super Metroid", "Chrono Trigger",
        "Super Mario 64", "Metroid Prime", "Final Fantasy VII", "Tetris", "Shadow of the Colossus", "Advance Wars",
        "Castlevania: Symphony of the Night",
    ];

    /// <summary>Kid Icarus is "missing" (moved drive) so the UI can show the honest problem state.</summary>
    private const string MissingTitle = "Kid Icarus: Uprising";

    /// <summary>
    /// Size of the demo library (design QA: the 1000-game demo of DESIGN 9.5). Null = the 66 hand-written games; more
    /// adds deterministic, plausible variants (sequels, regional releases, re-releases) without covers – the
    /// worst case for the grid (every box a generated spine placeholder).
    /// </summary>
    public static int? TargetCount { get; set; }

    private static readonly string[] Suffixes =
    [
        "2", "3", "II", "Deluxe", "Advance", "Gold", "Turbo", "Legends", "Origins", "Remix", "Special", "Zero", "Plus", "64", "DX",
        "Returns", "Chronicles", "Tactics", "Party", "World Tour",
    ];

    private static readonly string[] Regions = ["Europa", "USA", "Japan", "Deutsch", "Englisch"];

    public static List<Game> CreateGames(ICatalog catalog)
    {
        var games = CreateBaseGames(catalog);
        if (TargetCount is not { } target || target <= games.Count) return games;
        var now = DateTime.UtcNow;
        var baseCount = games.Count;
        long id = games.Max(g => g.Id) + 1;
        for (var i = 0; games.Count < target; i++)
        {
            var source = games[i % baseCount];
            var round = i / baseCount;
            var suffix = Suffixes[(i * 7 + round) % Suffixes.Length];
            var title = round % 3 == 2 ? source.Title : $"{source.Title} {suffix}";
            var hash = StableHash(title + i);
            games.Add(new Game
            {
                Id = id++,
                SystemId = source.SystemId,
                Path = $"games/{source.SystemId}/{SafeFileName(title)} ({i}){System.IO.Path.GetExtension(source.Path)}",
                Title = title,
                Year = source.Year is { } y ? y + 1 + (int)(hash % 4) : null,
                Developer = source.Developer,
                Publisher = source.Publisher,
                Genre = source.Genre,
                Players = source.Players,
                Size = source.Size,
                AddedUtc = now.AddDays(-30 - hash % 400),
                FileTimeUtc = source.FileTimeUtc,
                Favorite = hash % 37 == 0,
                Status = GameStatus.Ok,
                Kind = source.Kind,
                Crc32 = (hash * 2654435761u).ToString("X8"),
                // the same title twice in one system: a regional variant tells them apart
                Region = round % 3 == 2 ? Regions[(int)(hash % (uint)Regions.Length)] : source.Region,
                FolderId = 2,
            });
        }
        return games;
    }

    private static List<Game> CreateBaseGames(ICatalog catalog)
    {
        var now = DateTime.UtcNow;
        var list = new List<Game>(Entries.Length);
        long id = 1;
        foreach (var e in AllSystems ? Entries.Concat(ExtraEntries) : Entries)
        {
            var sys = catalog.GetSystem(e.System);
            var ext = sys?.Extensions.FirstOrDefault() ?? ".rom";
            var file = SafeFileName(e.Title) + ext;
            var hash = StableHash(e.Title);
            var game = new Game
            {
                Id = id++,
                SystemId = e.System,
                Path = $"games/{e.System}/{file}",
                Title = e.Title,
                Year = e.Year,
                Developer = e.Developer,
                Publisher = e.Developer,
                Genre = e.Genre,
                Players = e.Players,
                Description = e.Description,
                Size = 512 * 1024 + (hash % 64) * 256 * 1024,
                AddedUtc = now.AddDays(-(hash % 120)).AddHours(-(hash % 23)),
                FileTimeUtc = now.AddDays(-400 - hash % 300),
                Favorite = Favorites.Contains(e.Title),
                Status = e.Title == MissingTitle ? GameStatus.Missing : GameStatus.Ok,
                Kind = ext is ".cue" or ".m3u" ? GameKind.Playlist : GameKind.Rom,
                Crc32 = (hash * 2654435761u).ToString("X8"),
                Region = e.Title.Contains("Edition") || e.Title.Contains("Professor") ? "EUR" : "USA",
                FolderId = 1,
            };
            if (History.TryGetValue(e.Title, out var h))
            {
                game.LastPlayedUtc = now.AddDays(-h.Days);
                game.PlaySeconds = h.Minutes * 60L;
                game.PlayCount = h.Count;
            }
            list.Add(game);
        }
        // three "recently added" games so that shelf has fresh entries
        foreach (var t in new[] { "Okami", "Gunstar Heroes", "Patapon" })
            list.First(g => g.Title == t).AddedUtc = now.AddHours(-(StableHash(t) % 40) - 2);
        return list;
    }

    /// <summary>
    /// The play log behind the demo's play counts (the detail's Ausleihkarte, DESIGN 7.3): the last sessions of every
    /// played game (at most 8), the newest ending at its "last played" time, the others 1–4 days apart, durations
    /// around the game's average session. Deterministic (stable hash).
    /// </summary>
    public static List<PlaySession> CreateSessions(IEnumerable<Game> games)
    {
        var list = new List<PlaySession>();
        foreach (var g in games)
        {
            if (g.PlayCount <= 0 || g.LastPlayedUtc is not { } last) continue;
            var average = g.PlaySeconds / (double)g.PlayCount;
            var end = last;
            var hash = StableHash(g.Title);
            for (var i = 0; i < Math.Min(8, g.PlayCount); i++)
            {
                var h = hash * (uint)(i + 7) * 2654435761u;
                var seconds = (long)Math.Max(45, average * (0.55 + (h % 90) / 100.0));
                list.Add(new PlaySession
                {
                    GameId = g.Id,
                    EmulatorId = null,
                    StartUtc = end.AddSeconds(-seconds),
                    EndUtc = end,
                    Seconds = seconds,
                    ExitCode = 0,
                });
                end = end.AddDays(-(1 + (h >> 8) % 4)).AddHours(-((h >> 12) % 9));
            }
        }
        return list;
    }

    public static List<LibraryFolder> CreateFolders(AppPaths paths) =>
    [
        new LibraryFolder { Id = 1, Path = "games", IsBuiltIn = true, Recursive = true, Enabled = true, LastScanUtc = DateTime.UtcNow.AddHours(-3) },
        new LibraryFolder { Id = 2, Path = @"E:\Retro\ROMs", Recursive = true, Enabled = true, LastScanUtc = DateTime.UtcNow.AddDays(-2) },
        new LibraryFolder { Id = 3, Path = @"E:\Retro\PS2", SystemId = "ps2", Recursive = false, Enabled = false },
    ];

    private static string SafeFileName(string title)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(title.Select(c => invalid.Contains(c) ? ' ' : c).ToArray()).Trim();
    }

    /// <summary>FNV-1a (stable across runs, unlike string.GetHashCode).</summary>
    internal static uint StableHash(string s)
    {
        var h = 2166136261u;
        foreach (var c in s) { h ^= c; h *= 16777619u; }
        return h;
    }
}
