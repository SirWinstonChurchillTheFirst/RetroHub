using System.Diagnostics;
using System.Runtime.InteropServices;

// RetroHub.exe: starts app\RetroHub.App.exe with the same arguments and exits.
var root = AppContext.BaseDirectory;
var app = Path.Combine(root, "app", "RetroHub.App.exe");
if (!File.Exists(app))
{
    MessageBoxW(0, $"RetroHub konnte nicht gestartet werden.\n\nDie Datei fehlt:\n{app}\n\nBitte RetroHub neu entpacken.", "RetroHub", 0x10);
    return 1;
}
var psi = new ProcessStartInfo(app) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(app)! };
foreach (var a in Environment.GetCommandLineArgs().Skip(1)) psi.ArgumentList.Add(a);
try { Process.Start(psi); return 0; }
catch (Exception ex)
{
    MessageBoxW(0, $"RetroHub konnte nicht gestartet werden:\n{ex.Message}", "RetroHub", 0x10);
    return 2;
}

[DllImport("user32.dll", CharSet = CharSet.Unicode)]
static extern int MessageBoxW(nint hWnd, string text, string caption, uint type);
