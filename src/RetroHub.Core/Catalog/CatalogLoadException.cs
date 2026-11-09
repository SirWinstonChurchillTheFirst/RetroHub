namespace RetroHub.Core.Catalog;

/// <summary>
/// Thrown by <see cref="Catalog.Load(AppPaths)"/> when a BUILT-IN definition file (app/resources/…) is missing or broken.
/// That is a packaging bug, not a user error; plugin problems never throw, they end up in <see cref="ICatalog.LoadWarnings"/>.
/// The message is German (it may be shown in the startup error dialog).
/// </summary>
public sealed class CatalogLoadException : Exception
{
    /// <summary>The file (or folder) that caused the problem.</summary>
    public string Path { get; }

    public CatalogLoadException(string path, string message, Exception? inner = null) : base(message, inner)
    {
        Path = path;
    }
}
