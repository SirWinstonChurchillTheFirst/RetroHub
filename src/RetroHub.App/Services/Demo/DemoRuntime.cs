using System.Diagnostics;
using RetroHub.Core.Emulation;
using RetroHub.Core.Library;
using RetroHub.Core.Logging;

namespace RetroHub.App.Services.Demo;

/// <summary>Simulated scan: walks a fake file list with progress; reports a realistic result. Touches no files.</summary>
public sealed class DemoLibraryScanner(DemoGameRepository repository) : ILibraryScanner
{
    private int _scanning;
    public bool IsScanning => Volatile.Read(ref _scanning) == 1;

    public async Task<ScanReport> ScanAsync(ScanOptions options, IProgress<ScanProgress>? progress, CancellationToken ct)
    {
        if (Interlocked.Exchange(ref _scanning, 1) == 1)
            throw new InvalidOperationException("A scan is already running.");
        try
        {
            var sw = Stopwatch.StartNew();
            var games = repository.Query(new GameQuery { IncludeHidden = true });
            var total = games.Count + 14;
            progress?.Report(new ScanProgress("folders", 0, total, null));
            await Task.Delay(250, ct).ConfigureAwait(false);
            for (var i = 0; i < total; i++)
            {
                ct.ThrowIfCancellationRequested();
                var file = i < games.Count ? games[i].Path : $"games/unsorted/file{i:00}.bin";
                progress?.Report(new ScanProgress(i < total / 2 ? "files" : "hashes", i + 1, total, file));
                await Task.Delay(22, ct).ConfigureAwait(false);
            }
            foreach (var f in repository.Folders().Where(f => f.Enabled))
            {
                f.LastScanUtc = DateTime.UtcNow;
                repository.UpsertFolder(f);
            }
            return new ScanReport
            {
                Added = 0,
                Updated = 2,
                Unchanged = games.Count - 3,
                Missing = games.Count(g => g.Status == GameStatus.Missing),
                Duplicates = [new ScanIssue("games/gba/Pokemon Smaragd (Kopie).gba", "Gleicher Inhalt (CRC32) wie „Pokémon Smaragd-Edition“")],
                Unknown =
                [
                    new ScanIssue("games/unsorted/file03.bin", "Endung .bin passt zu mehreren Systemen (Mega Drive, PlayStation)"),
                    new ScanIssue("games/unsorted/readme.txt", "Keine Spieldatei"),
                ],
                Errors = [],
                Duration = sw.Elapsed,
            };
        }
        finally
        {
            Volatile.Write(ref _scanning, 0);
        }
    }

    public string? DetectSystem(string fullPath, string? folderHint = null) => null;
}

/// <summary>Simulated sessions: "running" for a few seconds (or until closed), then the play time is recorded.</summary>
public sealed class DemoSessionService(DemoGameRepository repository) : ISessionService
{
    /// <summary>How long a demo game "runs" by itself before it ends.</summary>
    public TimeSpan AutoEndAfter { get; set; } = TimeSpan.FromSeconds(8);

    private DemoGameSession? _current;
    public IGameSession? Current => _current is { State: SessionState.Starting or SessionState.Running } s ? s : null;

    public event Action<IGameSession>? SessionStarted;
    public event Action<IGameSession>? SessionEnded;

    public IGameSession Start(LaunchPlan plan)
    {
        if (!plan.CanLaunch) throw new InvalidOperationException("The plan has blocking problems.");
        if (Current != null) throw new InvalidOperationException("A game is already running.");
        var session = new DemoGameSession(plan, AutoEndAfter);
        _current = session;
        session.Ended += s =>
        {
            try
            {
                repository.AddSession(new PlaySession
                {
                    GameId = plan.Game.Id,
                    EmulatorId = plan.Emulator?.Def.Id,
                    StartUtc = s.StartedUtc,
                    EndUtc = DateTime.UtcNow,
                    Seconds = (long)s.Elapsed.TotalSeconds,
                    ExitCode = s.ExitCode,
                });
            }
            catch (Exception ex) { Log.Warn("Demo session could not be recorded", ex); }
            try { SessionEnded?.Invoke(s); } catch (Exception ex) { Log.Warn("SessionEnded handler failed", ex); }
        };
        Log.Info($"[demo] start '{plan.Game.Title}' with {plan.Emulator?.Def.Id ?? "?"} ({plan.Mode})");
        try { SessionStarted?.Invoke(session); } catch (Exception ex) { Log.Warn("SessionStarted handler failed", ex); }
        session.Run();
        return session;
    }
}

internal sealed class DemoGameSession(LaunchPlan plan, TimeSpan autoEnd) : IGameSession
{
    private readonly CancellationTokenSource _stop = new();
    private readonly Stopwatch _clock = new();
    private int _ended;

    public LaunchPlan Plan { get; } = plan;
    public SessionState State { get; private set; } = SessionState.Starting;
    public Process? Process => null;
    public DateTime StartedUtc { get; } = DateTime.UtcNow;
    public TimeSpan Elapsed => _clock.Elapsed;
    public int? ExitCode { get; private set; }
    public string? Error => null;
    public event Action<IGameSession>? Ended;

    internal void Run()
    {
        _clock.Start();
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(900, _stop.Token).ConfigureAwait(false);
                State = SessionState.Running;
                await Task.Delay(autoEnd, _stop.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            Finish(0);
        });
    }

    private void Finish(int code)
    {
        if (Interlocked.Exchange(ref _ended, 1) == 1) return;
        _clock.Stop();
        ExitCode = code;
        State = SessionState.Exited;
        try { Ended?.Invoke(this); } catch (Exception ex) { Log.Warn("Session Ended handler failed", ex); }
    }

    public async Task<bool> RequestCloseAsync(TimeSpan timeout)
    {
        _stop.Cancel();
        await Task.Delay(150).ConfigureAwait(false);
        Finish(0);
        return true;
    }

    public void Kill()
    {
        _stop.Cancel();
        Finish(-1);
    }

    public IReadOnlyList<nint> FindWindows() => [];
}
