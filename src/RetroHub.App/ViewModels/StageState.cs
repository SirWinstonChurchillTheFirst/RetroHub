using Avalonia.Media;

namespace RetroHub.App.ViewModels;

/// <summary>What the Start stage shows (DESIGN 7.1).</summary>
public enum StageKind
{
    /// <summary>The game to continue (no shelf focus yet).</summary>
    Continue,
    /// <summary>A game focused on a shelf.</summary>
    Game,
    /// <summary>A system spine focused on the "Konsolen" shelf.</summary>
    System,
    /// <summary>Empty library: the set is off, an empty-shelf drawing instead of the box.</summary>
    Empty,
}

/// <summary>
/// One state of the Start stage: label line, measured title, meta sentence (numbers become Archivo runs in the view),
/// the box (a game), the TV (system for "Kein Signal") and the primary action ("Spielen" for games, "Öffnen" for a
/// system). The TV picture itself (snap → title screen) arrives later in <see cref="HomeViewModel.StageScreen"/>.
/// </summary>
public sealed record StageState(
    StageKind Kind,
    string Label,
    string Title,
    string Meta,
    GameItemViewModel? Game,
    SystemItemViewModel? System,
    string? SystemId,
    Color Accent,
    string? Code,
    bool IsTvSystem,
    string PrimaryLabel)
{
    public bool IsGame => Kind is StageKind.Continue or StageKind.Game;
    public bool IsSystem => Kind == StageKind.System;
    public bool IsEmpty => Kind == StageKind.Empty;
    /// <summary>The box on the stage: the game itself, or the last played game of the system.</summary>
    public GameItemViewModel? Box => Game;

    /// <summary>The label line in the "Licht" of the subject's system (DESIGN 7.1: "Etikett 18, Licht"; Cyan is not on the list).</summary>
    public IBrush LabelBrush => SystemId != null ? Theming.SystemPalette.Get(SystemId, Accent).LightBrush : Brushes.Transparent;

    /// <summary>Same subject (the view only re-animates the text when the subject changed).</summary>
    public bool SameSubject(StageState? other) =>
        other != null && other.Kind == Kind && ReferenceEquals(other.Game, Game) && ReferenceEquals(other.System, System) && other.Label == Label;
}
