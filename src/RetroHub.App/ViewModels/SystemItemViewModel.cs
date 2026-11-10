using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using RetroHub.App.Localization;
using RetroHub.App.Theming;
using RetroHub.Core.Catalog;

namespace RetroHub.App.ViewModels;

/// <summary>A platform card (systems page, dashboard shelf, system page header).</summary>
public sealed partial class SystemItemViewModel : ObservableObject
{
    public SystemItemViewModel(SystemDef def, int gameCount, long playSeconds)
    {
        Def = def;
        Accent = ColorUtil.Parse(def.Accent, Color.Parse("#948A7A"));
        Look = SystemPalette.Get(def.Id, Accent);
        Update(gameCount, playSeconds);
    }

    public SystemDef Def { get; }
    public string Id => Def.Id;
    public string Name => Def.Name;
    /// <summary>The name for posters: product names that belong together never break apart ("Wii U", "Game Boy").</summary>
    public string PosterName => KeepTogether(Def.Name);

    private static readonly string[] Pairs = ["Game Boy", "Mega Drive", "Master System", "Game Gear", "Virtual Boy", "Neo Geo", "Mega-CD", "PC Engine"];

    /// <summary>
    /// Joins name parts that must stay on one line with a no-break space: the fixed pairs above and a short last
    /// one-character token ("Wii U", "PlayStation 2").
    /// </summary>
    public static string KeepTogether(string name)
    {
        foreach (var pair in Pairs) name = name.Replace(pair, pair.Replace(' ', ' '), StringComparison.Ordinal);
        var last = name.LastIndexOf(' ');
        if (last > 0 && name.Length - last - 1 == 1) name = name[..last] + ' ' + name[(last + 1)..];
        return name;
    }
    public string ShortName => string.IsNullOrEmpty(Def.ShortName) ? Def.Id.ToUpperInvariant() : Def.ShortName;
    public string Manufacturer => Def.Manufacturer;
    /// <summary>"Nintendo, 2001" (a sentence part, no "A · B" chains).</summary>
    public string SubTitle => Def.Year is { } y ? (string.IsNullOrEmpty(Def.Manufacturer) ? y.ToString() : $"{Def.Manufacturer}, {y}") : Def.Manufacturer;
    public string CategoryText => Fmt.Category(Def.Category);
    public bool IsHandheld => Def.Category == SystemCategory.Handheld;
    /// <summary>Catalog accent (only used for systems without a palette entry).</summary>
    public Color Accent { get; }
    /// <summary>Wall, colour band and light of this system (DESIGN 2.4).</summary>
    public SystemLook Look { get; }
    /// <summary>"Farbe": colour band, label bar, ticker underline.</summary>
    public IBrush AccentBrush => Look.ColorBrush;
    /// <summary>"Licht": focus ring and light in the system context.</summary>
    public IBrush LightBrush => Look.LightBrush;
    public IBrush WallBrush => Look.WallBrush;

    [ObservableProperty] public partial int GameCount { get; private set; }
    [ObservableProperty] public partial string CountText { get; private set; } = "";
    [ObservableProperty] public partial string PlayTimeText { get; private set; } = "";
    [ObservableProperty] public partial bool IsEmpty { get; private set; }
    /// <summary>The system whose front stands open on the Konsolen wall (its mark on the position rail).</summary>
    [ObservableProperty] public partial bool IsCurrent { get; set; }
    /// <summary>
    /// Up to three games standing on the inner shelf of the open front (Konsolen, M5 QA: the last played first, then by
    /// title); empty until loaded.
    /// </summary>
    [ObservableProperty] public partial IReadOnlyList<GameItemViewModel> Featured { get; set; } = [];

    public void Update(int gameCount, long playSeconds)
    {
        GameCount = gameCount;
        IsEmpty = gameCount == 0;
        CountText = gameCount == 0 ? Loc.T("systems.noGames") : gameCount == 1 ? Loc.T("systems.oneGame") : Loc.T("systems.games", gameCount);
        PlayTimeText = playSeconds > 0 ? Fmt.PlayTime(playSeconds) : Loc.T("time.never");
    }

    /// <summary>
    /// Returns <paramref name="current"/> (with updated counts) when it already shows exactly these systems in this
    /// order – so the cards are not rebuilt and keep their focus – otherwise new view models.
    /// </summary>
    public static IReadOnlyList<SystemItemViewModel> Reconcile(IReadOnlyList<SystemItemViewModel> current, IReadOnlyList<SystemDef> wanted,
        Core.Library.LibraryStats stats)
    {
        if (current.Count == wanted.Count && current.Select(c => c.Def).SequenceEqual(wanted))
        {
            foreach (var c in current) c.Update(stats.GamesPerSystem.GetValueOrDefault(c.Id), stats.PlaySecondsPerSystem.GetValueOrDefault(c.Id));
            return current;
        }
        return wanted.Select(s => new SystemItemViewModel(s, stats.GamesPerSystem.GetValueOrDefault(s.Id), stats.PlaySecondsPerSystem.GetValueOrDefault(s.Id)))
            .ToList();
    }

    public override string ToString() => Name;
}
