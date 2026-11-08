using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using RetroHub.Core.Logging;

namespace RetroHub.Input.Native;

/// <summary>
/// Fallback lookup for SDL3.dll, used only when the default .NET probing (deps.json / app folder) failed:
/// tries &lt;app&gt;\SDL3.dll and &lt;app&gt;\runtimes\win-&lt;arch&gt;\native\SDL3.dll.
/// </summary>
internal static class NativeLibraryResolver
{
    private static int _installed;

    public static void Install()
    {
        if (Interlocked.Exchange(ref _installed, 1) != 0) return;
        try
        {
            var asm = typeof(NativeLibraryResolver).Assembly;
            var alc = AssemblyLoadContext.GetLoadContext(asm) ?? AssemblyLoadContext.Default;
            alc.ResolvingUnmanagedDll += OnResolving;
        }
        catch (Exception ex) { Log.Warn("Input: could not install the native library resolver", ex); }
    }

    /// <summary>Candidate locations of SDL3.dll for the current process architecture.</summary>
    public static IEnumerable<string> Sdl3Candidates()
    {
        var baseDir = AppContext.BaseDirectory;
        var arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X86 => "win-x86",
            Architecture.Arm64 => "win-arm64",
            _ => "win-x64",
        };
        yield return Path.Combine(baseDir, "SDL3.dll");
        yield return Path.Combine(baseDir, "runtimes", arch, "native", "SDL3.dll");
    }

    private static nint OnResolving(Assembly assembly, string name)
    {
        if (assembly != typeof(NativeLibraryResolver).Assembly) return 0;
        if (!string.Equals(name, Sdl3.Lib, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(name, Sdl3.Lib + ".dll", StringComparison.OrdinalIgnoreCase)) return 0;
        foreach (var path in Sdl3Candidates())
        {
            try
            {
                if (File.Exists(path) && NativeLibrary.TryLoad(path, out var h))
                {
                    Log.Info($"Input: SDL3 loaded via fallback path {path}");
                    return h;
                }
            }
            catch { /* try the next candidate */ }
        }
        return 0;
    }
}
