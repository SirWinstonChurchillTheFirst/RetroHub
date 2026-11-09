using RetroHub.App.Localization;
using RetroHub.App.ViewModels;
using RetroHub.Core;
using RetroHub.Core.Catalog;
using RetroHub.Core.Library;
using RetroHub.Core.Logging;

namespace RetroHub.App.Services;

/// <summary>"Start this game now" from the command line (desktop shortcuts per game use this).</summary>
/// <param name="Target">Portable path, absolute path or numeric game id.</param>
/// <param name="Mode">--launch-mode override (null: the game's/emulator's default).</param>
/// <param name="Emulator">--emulator override ("mgba", "retroarch/fceumm"; null: the game's/system's default).</param>
/// <param name="WorkingDirectory">Where the starting process ran (relative paths), null = unknown.</param>
public sealed record LaunchRequest(string Target, LaunchMode? Mode = null, string? Emulator = null, string? WorkingDirectory = null);

/// <summary>Dev/test switches (only honoured in DEBUG builds, see <see cref="TestHooks"/>).</summary>
public sealed record TestHookOptions
{
    /// <summary>--test-quit-after &lt;s&gt;: after the game ran this long, "Spiel beenden" is triggered (same code path as the button).</summary>
    public double? QuitAfterSeconds { get; init; }
    /// <summary>--test-exit-after-return &lt;s&gt;: this long after the return to the hub (or after a launch that could not start), RetroHub closes.</summary>
    public double? ExitAfterReturnSeconds { get; init; }
    /// <summary>--test-release-after &lt;s&gt;: an embedded game is sent to its own window ("Eigenes Fenster", same code path) after that long.</summary>
    public double? ReleaseAfterSeconds { get; init; }
    /// <summary>--test-report &lt;file&gt;: JSON lines with what the game screen / launch flow observed.</summary>
    public string? ReportFile { get; init; }

    public bool Any => QuitAfterSeconds != null || ExitAfterReturnSeconds != null || ReleaseAfterSeconds != null || ReportFile != null;
}

/// <summary>
/// Parsed command line of RetroHub.App:
/// <c>--launch &lt;path-or-id&gt; [--launch-mode embedded|fullscreen|windowed] [--emulator &lt;ref&gt;]</c>, <c>--demo</c>,
/// and the dev-only test switches. Both "--name value" and "--name=value" work. Unknown arguments are ignored (logged).
/// </summary>
public sealed record CommandLineOptions
{
    public LaunchRequest? Launch { get; init; }
    public bool Demo { get; init; }
    public TestHookOptions Test { get; init; } = new();
    /// <summary>Problems found while parsing (German, for a toast); the rest of the options still apply.</summary>
    public IReadOnlyList<string> Errors { get; init; } = [];

    public static CommandLineOptions Parse(IReadOnlyList<string> args, string? workingDirectory = null)
    {
        string? target = null, emulator = null, report = null;
        LaunchMode? mode = null;
        double? quitAfter = null, exitAfter = null, releaseAfter = null;
        bool demo = false, modeGiven = false, emulatorGiven = false;
        var errors = new List<string>();

        for (int i = 0; i < args.Count; i++)
        {
            var arg = args[i] ?? "";
            string name = arg, inline = "";
            bool hasInline = false;
            if (arg.StartsWith("--", StringComparison.Ordinal) && arg.IndexOf('=') is var eq and > 2)
            {
                name = arg[..eq];
                inline = arg[(eq + 1)..];
                hasInline = true;
            }

            string? Value()
            {
                if (hasInline) return Unquote(inline);
                if (i + 1 < args.Count && !(args[i + 1] ?? "").StartsWith("--", StringComparison.Ordinal)) return Unquote(args[++i]);
                errors.Add(Loc.T("cli.missingValue", name));
                return null;
            }

            switch (name.ToLowerInvariant())
            {
                case "--demo":
                    demo = true;
                    break;
                case "--launch":
                    target = Value();
                    if (string.IsNullOrWhiteSpace(target)) target = null;
                    break;
                case "--launch-mode":
                case "--mode":
                    modeGiven = true;
                    var m = Value();
                    if (m == null) break;
                    mode = ParseMode(m);
                    if (mode == null) errors.Add(Loc.T("cli.badMode", m));
                    break;
                case "--emulator":
                    emulatorGiven = true;
                    emulator = Value();
                    if (string.IsNullOrWhiteSpace(emulator)) emulator = null;
                    break;
                case "--test-quit-after":
                    quitAfter = Seconds(Value());
                    break;
                case "--test-exit-after-return":
                    exitAfter = Seconds(Value());
                    break;
                case "--test-release-after":
                    releaseAfter = Seconds(Value());
                    break;
                case "--test-report":
                    report = Value();
                    break;
                default:
                    Log.Info($"Command line: ignoring '{arg}'");
                    break;
            }
        }

        if (target == null && (modeGiven || emulatorGiven))
            errors.Add(Loc.T("cli.launchMissing"));

        return new CommandLineOptions
        {
            Launch = target == null ? null : new LaunchRequest(target, mode, emulator, workingDirectory),
            Demo = demo,
            Test = new TestHookOptions { QuitAfterSeconds = quitAfter, ExitAfterReturnSeconds = exitAfter, ReleaseAfterSeconds = releaseAfter, ReportFile = report },
            Errors = errors,
        };
    }

    /// <summary>embedded/eingebettet, fullscreen/vollbild, windowed/fenster (case-insensitive); null when unknown.</summary>
    public static LaunchMode? ParseMode(string text) => text.Trim().ToLowerInvariant() switch
    {
        "embedded" or "embed" or "eingebettet" => LaunchMode.Embedded,
        "fullscreen" or "vollbild" => LaunchMode.Fullscreen,
        "windowed" or "window" or "fenster" => LaunchMode.Windowed,
        _ => null,
    };

    private static double? Seconds(string? text) =>
        double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var s) && s >= 0 ? s : null;

    private static string Unquote(string s)
    {
        s = s.Trim();
        return s.Length >= 2 && s[0] == '"' && s[^1] == '"' ? s[1..^1] : s;
    }
}

/// <summary>Outcome of resolving a <see cref="LaunchRequest.Target"/> to a library entry.</summary>
public sealed record LaunchTargetResult(Game? Game, string? Error, bool Added = false);

/// <summary>
/// Resolves a launch target to a game in the library: numeric id, portable path (relative to the RetroHub root),
/// absolute path, or a path relative to the caller's working directory. A file that is not in the library yet is
/// added: by a quick scan of its library folder when it lies in one, otherwise by detecting its system and adding just
/// that file. Runs off the UI thread; never throws.
/// </summary>
public static class LaunchTargetResolver
{
    /// <summary>How long to wait for a running library scan before trying to add the file anyway.</summary>
    public static TimeSpan ScanWait { get; set; } = TimeSpan.FromSeconds(60);

    public static async Task<LaunchTargetResult> ResolveAsync(AppServices services, LaunchRequest request, CancellationToken ct = default)
    {
        try
        {
            return await ResolveCoreAsync(services, request, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Error($"Resolving launch target '{request.Target}' failed", ex);
            return new LaunchTargetResult(null, Loc.T("cli.resolveFailed", request.Target, ex.Message));
        }
    }

    private static async Task<LaunchTargetResult> ResolveCoreAsync(AppServices s, LaunchRequest request, CancellationToken ct)
    {
        var target = request.Target.Trim().Trim('"');
        var paths = s.Paths;
        var candidates = Candidates(paths, target, request.WorkingDirectory).ToList();
        var existing = candidates.FirstOrDefault(File.Exists);

        // a plain number that is not also a file name: a game id
        if (existing == null && long.TryParse(target, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var id))
        {
            var byId = s.Games.Get(id);
            return byId != null ? new LaunchTargetResult(byId, null) : new LaunchTargetResult(null, Loc.T("cli.noGameId", id));
        }

        if (existing == null)
        {
            // not on disk (e.g. an unplugged drive): a library entry for that path still starts the game's problem dialog
            foreach (var c in candidates)
                if (s.Games.GetByPath(paths.ToPortable(c)) is { } known) return new LaunchTargetResult(known, null);
            return new LaunchTargetResult(null, Loc.T("cli.fileNotFound", target));
        }

        var full = Path.GetFullPath(existing);
        var portable = paths.ToPortable(full);
        if (s.Games.GetByPath(portable) is { } game) return new LaunchTargetResult(game, null);

        // a scan that is running may be about to add it
        var waited = TimeSpan.Zero;
        while (s.Scanner.IsScanning && waited < ScanWait)
        {
            await Task.Delay(250, ct).ConfigureAwait(false);
            waited += TimeSpan.FromMilliseconds(250);
        }
        if (s.Games.GetByPath(portable) is { } scanned) return new LaunchTargetResult(scanned, null);

        // inside a library folder: a quick scan of that folder adds it like any other file of the library
        var folder = s.Games.Folders()
            .Where(f => f.Enabled)
            .Select(f => (Folder: f, Dir: SafeFromPortable(paths, f.Path)))
            .Where(x => x.Dir != null && AppPaths.IsInside(full, x.Dir))
            .OrderByDescending(x => x.Dir!.Length)
            .Select(x => x.Folder)
            .FirstOrDefault();
        if (folder != null && !s.Scanner.IsScanning)
        {
            try
            {
                Log.Info($"Launch target '{full}' not in the library – scanning folder {folder.Path}");
                await s.Scanner.ScanAsync(new ScanOptions { FolderIds = [folder.Id], ComputeHashes = false }, null, ct).ConfigureAwait(false);
            }
            catch (InvalidOperationException ex)
            {
                Log.Warn("Quick scan for the launch target could not run", ex);
            }
            if (s.Games.GetByPath(portable) is { } added) return new LaunchTargetResult(added, null, Added: true);
        }

        // anywhere else: detect the system of just this file and add it
        var systemId = s.Scanner.DetectSystem(full, Path.GetFileName(Path.GetDirectoryName(full)));
        if (systemId == null || s.Catalog.GetSystem(systemId) == null)
            return new LaunchTargetResult(null, Loc.T("cli.unknownSystem", Path.GetFileName(full)));
        var info = new FileInfo(full);
        var parsed = TitleParser.Parse(info.Name);
        var newGame = new Game
        {
            SystemId = systemId,
            Path = portable,
            Title = parsed.Title,
            SortTitle = parsed.SortTitle,
            Region = parsed.Region,
            Kind = GameKind.Rom,
            Status = GameStatus.Ok,
            Size = info.Length,
            FileTimeUtc = info.LastWriteTimeUtc,
            AddedUtc = DateTime.UtcNow,
        };
        newGame.Id = s.Games.Upsert(newGame);
        Log.Info($"Launch target '{full}' added to the library as {systemId} game {newGame.Id}");
        return new LaunchTargetResult(s.Games.Get(newGame.Id) ?? newGame, null, Added: true);
    }

    /// <summary>Where a target may be: absolute; relative to the caller's folder; portable (relative to the root).</summary>
    public static IEnumerable<string> Candidates(AppPaths paths, string target, string? workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(target)) yield break;
        string? Full(Func<string> make)
        {
            try { return Path.GetFullPath(make()); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
        }
        if (Path.IsPathRooted(target))
        {
            if (Full(() => target) is { } abs) yield return abs;
            yield break;
        }
        if (!string.IsNullOrEmpty(workingDirectory) && Full(() => Path.Combine(workingDirectory, target)) is { } rel) yield return rel;
        if (Full(() => paths.FromPortable(target)) is { } portable) yield return portable;
    }

    private static string? SafeFromPortable(AppPaths paths, string stored)
    {
        try { return paths.FromPortable(stored); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
    }
}

/// <summary>Carries out a <see cref="LaunchRequest"/> in the running UI: resolve → game detail → start as if "Spielen" was pressed.</summary>
public static class CommandLineLaunch
{
    /// <summary>Returns true when a launch was attempted (the coordinator reports the rest); false when the target could not be resolved.</summary>
    public static async Task<bool> RunAsync(UiContext ctx, LaunchRequest request)
    {
        Log.Info($"Command line launch: '{request.Target}' (mode {request.Mode?.ToString() ?? "default"}, emulator {request.Emulator ?? "default"})");
        TestHooks.Report("cli-launch", ("target", request.Target), ("mode", request.Mode?.ToString()), ("emulator", request.Emulator));
        var result = await LaunchTargetResolver.ResolveAsync(ctx.Services, request).ConfigureAwait(true);
        if (result.Game == null)
        {
            Log.Warn($"Command line launch: {result.Error}");
            TestHooks.Report("cli-error", ("error", result.Error));
            _ = ctx.Dialogs.InfoAsync(Loc.T("cli.title"), result.Error ?? Loc.T("launch.unknownError"));
            return false;
        }
        var item = ctx.Items.Get(result.Game);
        TestHooks.Report("cli-resolved", ("gameId", result.Game.Id), ("title", item.Title), ("added", result.Added));
        if (result.Added) ctx.Toasts.Show(Loc.T("cli.added", item.Title), ToastKind.Success);
        // the game detail behind the launch: the return lands there ("Willkommen zurück", Play button focused)
        if (!(ctx.Navigator.Current == PageKey.GameDetail && Equals(ctx.Navigator.CurrentArgument, item)))
            ctx.Navigator.Navigate(PageKey.GameDetail, item);
        await ctx.Launch.LaunchAsync(item, request.Emulator, request.Mode).ConfigureAwait(true);
        return true;
    }
}
