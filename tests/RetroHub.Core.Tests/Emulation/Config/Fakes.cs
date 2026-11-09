using RetroHub.Core;
using RetroHub.Core.Catalog;
using RetroHub.Core.Config;
using RetroHub.Core.Emulation;
using RetroHub.Core.Library;

namespace RetroHub.Core.Tests.Emulation.Config;

/// <summary>In-memory catalog (the real JSON catalog is built elsewhere).</summary>
internal sealed class FakeCatalog : ICatalog
{
    public List<SystemDef> SystemList { get; } = new();
    public List<EmulatorDef> EmulatorList { get; } = new();

    public IReadOnlyList<SystemDef> Systems => SystemList;
    public IReadOnlyList<EmulatorDef> Emulators => EmulatorList;
    public SystemDef? GetSystem(string id) => SystemList.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
    public EmulatorDef? GetEmulator(string id) => EmulatorList.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase));
    public SystemDef? SystemForFolder(string folderName) =>
        SystemList.FirstOrDefault(s => s.Folders.Contains(folderName, StringComparer.OrdinalIgnoreCase));
    public IReadOnlyList<SystemDef> SystemsForExtension(string extension) =>
        SystemList.Where(s => s.Extensions.Contains(extension)).ToList();
    public IReadOnlyList<string> LoadWarnings => [];

    public FakeCatalog Add(SystemDef s) { SystemList.Add(s); return this; }
    public FakeCatalog Add(EmulatorDef e) { EmulatorList.Add(e); return this; }
}

/// <summary>Registry with hand-set installs.</summary>
internal sealed class FakeRegistry : IEmulatorRegistry
{
    private readonly Dictionary<string, EmulatorInstall> _map = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<EmulatorInstall> All => _map.Values.ToList();
    public EmulatorInstall? Get(string emulatorId) => _map.TryGetValue(emulatorId, out var e) ? e : null;
    public void Refresh() { }
    public void SetUserPath(string emulatorId, string? exePath) { }
#pragma warning disable CS0067
    public event Action? Changed;
#pragma warning restore CS0067

    public FakeRegistry Set(EmulatorInstall install) { _map[install.Def.Id] = install; return this; }
}

/// <summary>
/// A throw-away portable root: &lt;temp&gt;/retrohub-emucfg-xxxx/root. The extra level lets tests assert that
/// nothing was written next to the root (Base must contain only "root").
/// </summary>
internal sealed class TempRoot : IDisposable
{
    public string Base { get; }
    public string Root { get; }
    public AppPaths Paths { get; }
    public SettingsStore Settings { get; }

    public TempRoot()
    {
        Base = Directory.CreateTempSubdirectory("retrohub-emucfg-").FullName;
        Root = Path.Combine(Base, "root");
        Directory.CreateDirectory(Root);
        Paths = new AppPaths(Root, Path.Combine(Root, "app"));
        Settings = new SettingsStore(Paths.SettingsFile);
    }

    /// <summary>Creates a file (and its folders) below Root; returns the full path.</summary>
    public string Touch(string relative, string content = "")
    {
        var full = Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return full;
    }

    public string Touch(string relative, byte[] content)
    {
        var full = Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, content);
        return full;
    }

    /// <summary>All files below Root (relative, '/'), sorted.</summary>
    public List<string> Files() => Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)
        .Select(f => Path.GetRelativePath(Root, f).Replace('\\', '/')).Order(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>True when the temp base holds nothing but the root folder.</summary>
    public bool NothingOutsideRoot() =>
        Directory.EnumerateFileSystemEntries(Base).Select(Path.GetFileName).SequenceEqual(["root"]);

    public void Dispose()
    {
        try { Directory.Delete(Base, recursive: true); } catch { /* best effort */ }
    }
}

internal static class Make
{
    public static Game Game(string systemId, string path, GameKind kind = GameKind.Rom) =>
        new() { SystemId = systemId, Path = path, Title = System.IO.Path.GetFileNameWithoutExtension(path), Kind = kind };

    public static LaunchPlan Plan(string systemId, LaunchMode mode, string rom = "games/x/game.bin") => new()
    {
        Game = Game(systemId, rom),
        System = new SystemDef { Id = systemId, Name = systemId.ToUpperInvariant() },
        Mode = mode,
    };

    public static EmulatorInstall Install(EmulatorDef def, string? exe, params string[] cores) =>
        new() { Def = def, ExePath = exe, InstalledCores = cores };
}
