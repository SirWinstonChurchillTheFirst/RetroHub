using Avalonia.Media;

namespace RetroHub.App.ViewModels;

/// <summary>What the room behind a page shows (DESIGN 2.5).</summary>
public enum BackdropKind
{
    /// <summary>Plain room (#14120F): library, workbench pages.</summary>
    Room,
    /// <summary>TV light of a game above the stage board (Start, Detail).</summary>
    TvLight,
    /// <summary>The painted wall of a system as a flat tone (system page, consoles).</summary>
    Wall,
}

/// <summary>
/// The room-light request of a page for the shell's RoomBackdrop. <paramref name="StageShare"/> = where the stage
/// board's top edge sits (share of the window height; below it the room is plain); <paramref name="GlowX"/>/
/// <paramref name="GlowY"/> = centre of the glow at the source (the TV / the box), relative to the window.
/// </summary>
public sealed record BackdropSpec(
    BackdropKind Kind,
    string? ImagePath = null,
    string? SystemId = null,
    Color Accent = default,
    long GameId = 0,
    double StageShare = 0.54,
    double GlowX = 0.75,
    double GlowY = 0.33)
{
    public static readonly BackdropSpec Room = new(BackdropKind.Room);
}
