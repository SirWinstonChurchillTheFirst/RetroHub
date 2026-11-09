namespace RetroHub.Core.Metadata;

/// <summary>The three kinds of artwork a game can have (folders below artwork/).</summary>
public enum ArtworkKind { Cover, Background, Logo }

/// <summary>Result of <see cref="ArtworkService.ResolveAsync"/>. Counts refer to covers; backgrounds/logos are counted in <see cref="ExtrasFound"/>.</summary>
/// <param name="LocalFound">Covers found on disk (artwork/covers, next to the ROM, ES-DE/Batocera layouts).</param>
/// <param name="Downloaded">Covers downloaded from libretro-thumbnails (only when online covers are enabled).</param>
/// <param name="NotFound">Games that still have no cover after all steps.</param>
/// <param name="Errors">Network/IO failures (logged; the affected games keep no cover and are retried next time).</param>
public sealed record ArtworkReport(int LocalFound, int Downloaded, int NotFound, int Errors)
{
    /// <summary>Backgrounds and logos found locally.</summary>
    public int ExtrasFound { get; init; }
    /// <summary>Games looked at (those that were missing at least one kind of artwork).</summary>
    public int Checked { get; init; }
    /// <summary>Games whose online lookup was skipped because a recent 404 is remembered (artwork-misses.json).</summary>
    public int SkippedRecentMiss { get; init; }
    public TimeSpan Duration { get; init; }

    public static ArtworkReport Empty { get; } = new(0, 0, 0, 0);
}

/// <summary>Progress of a resolve run. <see cref="Phase"/> is "local" or "online".</summary>
public sealed record ArtworkProgress(string Phase, int Done, int Total, string? CurrentTitle);
