using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using RetroHub.App.Localization;
using RetroHub.App.Services;
using RetroHub.App.Theming;
using RetroHub.Core.Catalog;
using RetroHub.Core.Library;

namespace RetroHub.App.ViewModels;

/// <summary>One game as the UI shows it (tiles, lists, detail). Instances are shared per game id via <see cref="GameItemCache"/>.</summary>
public sealed partial class GameItemViewModel : ObservableObject
{
    private readonly AppServices _services;

    public GameItemViewModel(AppServices services, Game game)
    {
        _services = services;
        Update(game);
    }

    public Game Game { get; private set; } = null!;
    public long Id => Game.Id;

    [ObservableProperty] public partial string Title { get; private set; } = "";
    [ObservableProperty] public partial string SystemId { get; private set; } = "";
    [ObservableProperty] public partial string SystemName { get; private set; } = "";
    [ObservableProperty] public partial string SystemShort { get; private set; } = "";
    [ObservableProperty] public partial Color Accent { get; private set; }
    [ObservableProperty] public partial IBrush AccentBrush { get; private set; } = Brushes.Transparent;
    /// <summary>The system's "Farbe" (DESIGN 2.4): label bar, list bar, spine band.</summary>
    [ObservableProperty] public partial IBrush SystemColorBrush { get; private set; } = Brushes.Transparent;
    /// <summary>Release year ("" when unknown – the label line then shows only the code).</summary>
    [ObservableProperty] public partial string YearText { get; private set; } = "";
    /// <summary>Full path of the cover image, or null (placeholder art is generated then).</summary>
    [ObservableProperty] public partial string? CoverPath { get; private set; }
    /// <summary>A cover image is referenced; without one the box is a spine placeholder that carries its own title.</summary>
    [ObservableProperty] public partial bool HasCover { get; private set; }
    [ObservableProperty] public partial bool IsFavorite { get; private set; }
    [ObservableProperty] public partial bool IsMissing { get; private set; }
    [ObservableProperty] public partial string PlayTimeText { get; private set; } = "";
    [ObservableProperty] public partial string LastPlayedText { get; private set; } = "";
    [ObservableProperty] public partial string MetaText { get; private set; } = "";
    [ObservableProperty] public partial bool HasPlayed { get; private set; }
    [ObservableProperty] public partial double CoverAspect { get; private set; } = 1.4;

    public string Initials => Fmt.Initials(Title);

    public void Update(Game game)
    {
        Game = game;
        var sys = _services.Catalog.GetSystem(game.SystemId);
        Title = game.DisplayTitle;
        SystemId = game.SystemId;
        SystemName = sys?.Name ?? game.SystemId;
        SystemShort = string.IsNullOrEmpty(sys?.ShortName) ? game.SystemId.ToUpperInvariant() : sys!.ShortName;
        Accent = ColorUtil.Parse(sys?.Accent, Color.Parse("#948A7A"));
        var look = SystemPalette.Get(game.SystemId, Accent);
        AccentBrush = look.ColorBrush;
        SystemColorBrush = look.ColorBrush;
        YearText = game.Year is { } year ? year.ToString(System.Globalization.CultureInfo.InvariantCulture) : "";
        CoverPath = string.IsNullOrEmpty(game.CoverPath) ? null : _services.Paths.FromPortable(game.CoverPath);
        HasCover = CoverPath != null;
        IsFavorite = game.Favorite;
        IsMissing = game.Status == GameStatus.Missing;
        HasPlayed = game.LastPlayedUtc != null;
        PlayTimeText = Fmt.PlayTime(game.PlaySeconds);
        LastPlayedText = Fmt.LastPlayed(game.LastPlayedUtc);
        // Region/language under the title tells versions apart ("SNES · Deutsch" vs "SNES · Englisch, NTSC").
        var meta = string.IsNullOrWhiteSpace(game.Region) ? SystemShort : $"{SystemShort} · {game.Region}";
        MetaText = game.Year is { } y ? $"{meta} · {y}" : meta;
        CoverAspect = sys?.Category == SystemCategory.Handheld ? 1.0 : 1.4;
        OnPropertyChanged(nameof(Initials));
    }

    /// <summary>Sets the favourite flag optimistically (the repository write happens in the caller).</summary>
    internal void SetFavoriteLocal(bool favorite)
    {
        Game.Favorite = favorite;
        IsFavorite = favorite;
    }

    public override string ToString() => Title;
}

/// <summary>Keeps one <see cref="GameItemViewModel"/> per game id so state (favourite, playtime) is consistent across pages.</summary>
public sealed class GameItemCache(AppServices services)
{
    private readonly Dictionary<long, WeakReference<GameItemViewModel>> _items = new();

    public GameItemViewModel Get(Game game)
    {
        if (_items.TryGetValue(game.Id, out var weak) && weak.TryGetTarget(out var item))
        {
            item.Update(game);
            return item;
        }
        item = new GameItemViewModel(services, game);
        _items[game.Id] = new WeakReference<GameItemViewModel>(item);
        if (_items.Count > 4096) Compact();
        return item;
    }

    public IReadOnlyList<GameItemViewModel> GetAll(IEnumerable<Game> games) => games.Select(Get).ToList();

    public GameItemViewModel? TryGet(long id) =>
        _items.TryGetValue(id, out var weak) && weak.TryGetTarget(out var item) ? item : null;

    private void Compact()
    {
        foreach (var key in _items.Where(kv => !kv.Value.TryGetTarget(out _)).Select(kv => kv.Key).ToList())
            _items.Remove(key);
    }
}
