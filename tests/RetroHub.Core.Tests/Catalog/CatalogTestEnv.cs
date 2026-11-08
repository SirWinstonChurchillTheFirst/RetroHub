using RetroHub.Core;

namespace RetroHub.Core.Tests.Catalog;

/// <summary>
/// A temporary portable root: app/resources/ holds a copy of the built-in definitions from the source tree
/// (src/RetroHub.Core/Resources), plugins/ is empty. Deleted on dispose.
/// </summary>
internal sealed class CatalogTestEnv : IDisposable
{
    public string Root { get; }
    public AppPaths Paths { get; }
    public string PluginSystems => Path.Combine(Paths.Plugins, "systems");
    public string PluginEmulators => Path.Combine(Paths.Plugins, "emulators");

    public CatalogTestEnv(bool copyBuiltIns = true)
    {
        Root = Path.Combine(Path.GetTempPath(), "retrohub-catalog-tests", Guid.NewGuid().ToString("N"));
        var app = Path.Combine(Root, "app");
        Directory.CreateDirectory(app);
        Paths = new AppPaths(Root, app);
        Directory.CreateDirectory(PluginSystems);
        Directory.CreateDirectory(PluginEmulators);
        if (copyBuiltIns)
        {
            foreach (var kind in new[] { "systems", "emulators" })
            {
                var src = Path.Combine(SourceResources, kind);
                var dst = Path.Combine(Paths.Resources, kind);
                Directory.CreateDirectory(dst);
                foreach (var f in Directory.GetFiles(src, "*.json"))
                    File.Copy(f, Path.Combine(dst, Path.GetFileName(f)));
            }
        }
    }

    /// <summary>src/RetroHub.Core/Resources of this checkout (found via the RetroHub.root marker).</summary>
    public static string SourceResources
    {
        get
        {
            for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
                if (File.Exists(Path.Combine(d.FullName, AppPaths.RootMarker)))
                    return Path.Combine(d.FullName, "src", "RetroHub.Core", "Resources");
            throw new InvalidOperationException("RetroHub.root not found above " + AppContext.BaseDirectory);
        }
    }

    public void WritePluginSystems(string fileName, string json) => File.WriteAllText(Path.Combine(PluginSystems, fileName), json);
    public void WritePluginEmulators(string fileName, string json) => File.WriteAllText(Path.Combine(PluginEmulators, fileName), json);

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
