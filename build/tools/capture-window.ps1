<#
  Dev tool: captures a process's main window (or the whole primary screen) to a PNG.
  Usage: capture-window.ps1 -ProcessName RetroHub.App -Out shot.png [-Screen] [-PrintWindow]
    -Screen       copy the primary screen (what DWM composed; misses GL/Vulkan windows in independent-flip mode)
    -PrintWindow  ask the window itself to render (PW_RENDERFULLCONTENT) - works for fullscreen OpenGL/Vulkan/D3D
                  emulators whose picture never reaches the screen copy; captures only that window, not the desktop
#>
param([string]$ProcessName = "RetroHub.App", [Parameter(Mandatory)][string]$Out, [switch]$Screen, [switch]$PrintWindow)
Add-Type -AssemblyName System.Drawing, System.Windows.Forms
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class W {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
}
"@
[W]::SetProcessDPIAware() | Out-Null
if ($Screen) {
  $b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
  $x = $b.X; $y = $b.Y; $w = $b.Width; $h = $b.Height
} else {
  $p = Get-Process -Name $ProcessName -ErrorAction Stop | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
  if (-not $p) { throw "no window for $ProcessName" }
  $hwnd = $p.MainWindowHandle
  if (-not $PrintWindow) { [W]::SetForegroundWindow($hwnd) | Out-Null; Start-Sleep -Milliseconds 400 }
  $r = New-Object W+RECT
  [W]::GetWindowRect($hwnd, [ref]$r) | Out-Null
  $x = $r.L; $y = $r.T; $w = $r.R - $r.L; $h = $r.B - $r.T
}
$bmp = New-Object System.Drawing.Bitmap $w, $h
$g = [System.Drawing.Graphics]::FromImage($bmp)
if ($PrintWindow -and -not $Screen) {
  $hdc = $g.GetHdc()
  $ok = [W]::PrintWindow($hwnd, $hdc, 2)   # PW_RENDERFULLCONTENT
  $g.ReleaseHdc($hdc)
  if (-not $ok) { Write-Warning "PrintWindow failed" }
} else {
  $g.CopyFromScreen($x, $y, 0, 0, $bmp.Size)
}
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()
Write-Output "$Out ${w}x$h"
