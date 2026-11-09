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
        Accent = ColorUtil.Parse(def.Accent, Color.Parse(ThemeManager.DefaultAccent));
        var deep = ColorUtil.Mix(Accent, Color.Parse("#0B0A1A"), 0.55);
        CardBrush = ThemeManager.Gradient(ColorUtil.WithAlpha(ColorUtil.Mix(Accent, Color.Parse("#15142B"), 0.25), 0xF0), ColorUtil.WithAlpha(deep, 0xF0),
            new RelativePoint(0, 0, RelativeUnit.Relative), new RelativePoint(1, 1, RelativeUnit.Relative));
        AccentBrush = new SolidColorBrush(Accent);
        GlowBrush = ThemeManager.Radial(ColorUtil.WithAlpha(ColorUtil.Mix(Accent, Colors.White, 0.2), 0x70), new RelativePoint(0.85, 0.1, RelativeUnit.Relative), 0.9);
        AmbientBrush = ThemeManager.Radial(ColorUtil.WithAlpha(Accent, 0x55), new RelativePoint(0.8, 0.0, RelativeUnit.Relative), 0.9);
        Update(gameCount, playSeconds);
    }

    public SystemDef Def { get; }
    public string Id => Def.Id;
    public string Name => Def.Name;
    public string ShortName => string.IsNullOrEmpty(Def.ShortName) ? Def.Id.ToUpperInvariant() : Def.ShortName;
    public string Manufacturer => Def.Manufacturer;
    public string SubTitle => Def.Year is { } y ? $"{Def.Manufacturer} · {y}" : Def.Manufacturer;
    public string CategoryText => Fmt.Category(Def.Category);
    public bool IsHandheld => Def.Category == SystemCategory.Handheld;
    public Color Accent { get; }
    public IBrush AccentBrush { get; }
    public IBrush CardBrush { get; }
    public IBrush GlowBrush { get; }
    /// <summary>Static ambient background for the system page (pre-computed gradient, no blur).</summary>
    public IBrush AmbientBrush { get; }

    [ObservableProperty] public partial int GameCount { get; private set; }
    [ObservableProperty] public partial string CountText { get; private set; } = "";
    [ObservableProperty] public partial string PlayTimeText { get; private set; } = "";
    [ObservableProperty] public partial bool IsEmpty { get; private set; }

    public void Update(int gameCount, long playSeconds)
    {
        GameCount = gameCount;
        IsEmpty = gameCount == 0;
        CountText = gameCount == 0 ? Loc.T("systems.noGames") : gameCount == 1 ? Loc.T("systems.oneGame") : Loc.T("systems.games", gameCount);
        PlayTimeText = playSeconds > 0 ? Fmt.PlayTime(playSeconds) : Loc.T("time.never");
    }

    public override string ToString() => Name;
}
