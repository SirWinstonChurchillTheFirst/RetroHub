using System.Diagnostics;
using RetroHub.Core;
using RetroHub.Core.Logging;

namespace RetroHub.App.Services;

/// <summary>Opens folders in Explorer and web pages in the browser. Never throws.</summary>
public static class ShellOpen
{
    /// <summary>
    /// Opens <paramref name="folder"/> in Explorer. A missing folder is created only when it lies inside
    /// <paramref name="createIfInside"/> (RetroHub's own portable folders) – never elsewhere.
    /// </summary>
    public static bool Folder(string folder, string? createIfInside = null)
    {
        try
        {
            var full = Path.GetFullPath(folder);
            if (!Directory.Exists(full))
            {
                if (createIfInside == null || !AppPaths.IsInside(full, createIfInside)) return false;
                Directory.CreateDirectory(full);
            }
            if (!OperatingSystem.IsWindows()) return false;
            var psi = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
            psi.ArgumentList.Add(full);
            Process.Start(psi)?.Dispose();
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn($"Opening folder '{folder}' failed", ex);
            return false;
        }
    }

    /// <summary>Opens an http(s) URL in the default browser (other schemes are refused).</summary>
    public static bool Url(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            return false;
        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true })?.Dispose();
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn($"Opening '{url}' failed", ex);
            return false;
        }
    }
}
