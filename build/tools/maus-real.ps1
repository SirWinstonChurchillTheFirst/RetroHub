# Mouse check on a REAL Win32 window (R3 owner report "nur mit Pfeiltasten"). Runs ONLY inside the dev VM (PowerShell
# Direct, e.g. vm.ps1 exec), never on the host: it starts RetroHub.App --demo with a throw-away root, asks the window the
# same WM_NCHITTEST question Windows asks before every mouse press, and clicks with the window messages Windows sends
# after a HTCLIENT answer (WM_MOUSEMOVE / WM_LBUTTONDOWN / WM_LBUTTONUP). Element positions come from UI Automation
# (the app's own automation tree), so nothing is guessed. After each click the page is identified by its UIA content.
# NEEDS AN INTERACTIVE DESKTOP (a signed-in console session, session id >= 1, e.g. started by a scheduled task "only when
# the user is logged on"). Tried 2026-11-10 from PowerShell Direct (session 0, no desktop): the window came up at
# 1044 x 788 without DWM, every hit test answered HTCLIENT and Avalonia ignored the posted clicks – that environment
# proves nothing, so the script refuses to run there.
param([string]$AppDir = 'C:\rhwork\src\src\RetroHub.App\bin\Debug\net10.0\win-x64', [string]$Root = 'C:\rhwork\maus-real-root')
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type @'
using System; using System.Runtime.InteropServices; using System.Text;
public static class W {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc p, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern int GetWindowTextLength(IntPtr h);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr a, int x, int y, int cx, int cy, uint f);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool ScreenToClient(IntPtr h, ref POINT p);
  [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr h);
  [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
  [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
  public static IntPtr Find(uint pid) {
    IntPtr found = IntPtr.Zero;
    EnumWindows((h, l) => { uint p; GetWindowThreadProcessId(h, out p); var c = new StringBuilder(256); GetClassName(h, c, 256);
      if (p == pid && GetWindowTextLength(h) > 0 && c.ToString().StartsWith("Avalonia-")) { found = h; return false; } return true; }, IntPtr.Zero);
    return found;
  }
  static IntPtr LP(int x, int y) { return (IntPtr)(((y & 0xFFFF) << 16) | (x & 0xFFFF)); }
  public static int NcHit(IntPtr h, int sx, int sy) { return (int)SendMessage(h, 0x0084, IntPtr.Zero, LP(sx, sy)); }
  public static void Click(IntPtr h, int cx, int cy) {
    SendMessage(h, 0x0200, IntPtr.Zero, LP(cx, cy)); System.Threading.Thread.Sleep(80);
    SendMessage(h, 0x0201, (IntPtr)1, LP(cx, cy)); System.Threading.Thread.Sleep(80);
    SendMessage(h, 0x0202, IntPtr.Zero, LP(cx, cy));
  }
}
'@
if (Test-Path $Root) { Remove-Item $Root -Recurse -Force }
New-Item -ItemType Directory -Force $Root | Out-Null
Get-Process RetroHub.App -ErrorAction SilentlyContinue | Stop-Process -Force
$psi = New-Object Diagnostics.ProcessStartInfo (Join-Path $AppDir 'RetroHub.App.exe')
$psi.Arguments = '--demo'; $psi.UseShellExecute = $false; $psi.WorkingDirectory = $AppDir
$psi.EnvironmentVariables['RETROHUB_ROOT'] = $Root
$psi.EnvironmentVariables['DOTNET_ROOT'] = 'C:\rhtools\dotnet'
$p = [Diagnostics.Process]::Start($psi)
if ((Get-Process -Id $p.Id).SessionId -eq 0) { $p.Kill(); "REFUSED: session 0 has no interactive desktop (see the header)"; return }
"app pid $($p.Id), session $((Get-Process -Id $p.Id).SessionId), build $((Get-Item (Join-Path $AppDir 'RetroHub.App.dll')).LastWriteTime.ToString('s'))"
$h = [IntPtr]::Zero; $sw = [Diagnostics.Stopwatch]::StartNew()
while ($h -eq [IntPtr]::Zero -and $sw.Elapsed.TotalSeconds -lt 60) { Start-Sleep -Milliseconds 500; $h = [W]::Find([uint32]$p.Id) }
if ($h -eq [IntPtr]::Zero) { "FAIL no window"; $p.Kill(); return }
[void][W]::ShowWindow($h, 1)
[void][W]::SetWindowPos($h, [IntPtr]::Zero, 0, 0, 1920, 1080, 0x0014)
Start-Sleep -Seconds 6
$wr = New-Object W+RECT; [void][W]::GetWindowRect($h, [ref]$wr); "window rect $($wr.L),$($wr.T) - $($wr.R),$($wr.B)"

$fails = New-Object Collections.Generic.List[string]
$hitNames = @{ 0 = 'HTNOWHERE'; 1 = 'HTCLIENT'; 2 = 'HTCAPTION'; 8 = 'HTMINBUTTON'; 9 = 'HTMAXBUTTON'; 12 = 'HTTOP'; 20 = 'HTCLOSE' }

function Buttons {
  $ae = [Windows.Automation.AutomationElement]::FromHandle($h)
  $cond = New-Object Windows.Automation.PropertyCondition ([Windows.Automation.AutomationElement]::ControlTypeProperty), ([Windows.Automation.ControlType]::Button)
  $list = @()
  foreach ($e in $ae.FindAll([Windows.Automation.TreeScope]::Descendants, $cond)) {
    $c = $e.Current; $r = $c.BoundingRectangle
    if ($r.Width -le 0 -or $r.Height -le 0 -or $c.IsOffscreen) { continue }
    $n = if ($c.Name) { $c.Name } elseif ($c.HelpText) { $c.HelpText } else { $c.AutomationId }
    $list += [pscustomobject]@{ Name = $n; Id = $c.AutomationId; Rect = $r; El = $e }
  }
  $list
}
function PageSig {
  # what the page shows below the header: the named buttons/texts in its first 400 px (sorted)
  $ae = [Windows.Automation.AutomationElement]::FromHandle($h)
  $names = foreach ($e in $ae.FindAll([Windows.Automation.TreeScope]::Descendants, [Windows.Automation.Condition]::TrueCondition)) {
    $c = $e.Current; $r = $c.BoundingRectangle
    if ($c.Name -and -not $c.IsOffscreen -and $r.Top -gt ($wr.T + 80) -and $r.Top -lt ($wr.T + 1000)) { $c.Name }
  }
  (($names | Select-Object -Unique | Select-Object -First 6) -join ' | ')
}
function Hit($label, $sx, $sy, $expect) {
  $v = [W]::NcHit($h, [int]$sx, [int]$sy)
  $ok = $expect -contains $v
  "  NCHITTEST {0,-26} @ {1,4},{2,3} -> {3,2} {4,-10} {5}" -f $label, [int]$sx, [int]$sy, $v, $hitNames[$v], ($(if ($ok) { 'ok' } else { 'FAIL' }))
  if (-not $ok) { $fails.Add("$label hit $v") }
}
function ClickAt($label, $r) {
  $pt = New-Object W+POINT; $pt.X = [int]($r.X + $r.Width / 2); $pt.Y = [int]($r.Y + $r.Height / 2)
  $hit = [W]::NcHit($h, $pt.X, $pt.Y)
  if ($hit -ne 1) { $fails.Add("${label}: hit $hit before the click") }
  [void][W]::ScreenToClient($h, [ref]$pt)
  [W]::Click($h, $pt.X, $pt.Y); Start-Sleep -Milliseconds 1600
  $hit
}

$btn = Buttons
$header = $btn | Where-Object { $_.Rect.Top -lt ($wr.T + 72) } | Sort-Object { $_.Rect.X }
"header buttons (UIA): " + (($header | ForEach-Object { "$($_.Name)@$([int]$_.Rect.X)" }) -join ', ')
"--- WM_NCHITTEST on every header button (centre, 3 px under its top, 3 px over its bottom): must be HTCLIENT"
foreach ($b in $header) {
  $r = $b.Rect
  Hit $b.Name ($r.X + $r.Width / 2) ($r.Y + $r.Height / 2) @(1)
  Hit "$($b.Name) top" ($r.X + $r.Width / 2) ($r.Y + 3) @(1)
  Hit "$($b.Name) bottom" ($r.X + $r.Width / 2) ($r.Bottom - 3) @(1)
}
"--- WM_NCHITTEST on the bare header strip and the word mark: must be HTCAPTION (drag) - page: HTCLIENT"
Hit 'word mark' ($wr.L + 110) ($wr.T + 36) @(2)
$chan = $header | Where-Object { $_.Name -eq 'Konsolen' } | Select-Object -First 1
$tool = $header | Where-Object { $chan -and $_.Rect.X -gt $chan.Rect.Right } | Select-Object -First 1
if ($chan -and $tool) { $gx = ($chan.Rect.Right + $tool.Rect.Left) / 2; Hit 'strip between channels/tools' $gx ($wr.T + 36) @(2); Hit 'strip (top)' $gx ($wr.T + 12) @(2, 12) }
Hit 'page centre' ($wr.L + 960) ($wr.T + 540) @(1)

"--- clicks (page identified by its UIA content after each click)"
$before = PageSig; "  start page: $before"
foreach ($n in 'Bibliothek', 'Konsolen', 'Suche', 'Importe', 'Emulatoren', 'Einstellungen', 'Start') {
  $b = Buttons | Where-Object { $_.Rect.Top -lt ($wr.T + 72) -and $_.Name -eq $n } | Select-Object -First 1
  if (-not $b) { $fails.Add("no header button $n"); "  (no $n)"; continue }
  $hit = ClickAt $n $b.Rect
  $after = PageSig
  $changed = $after -ne $before
  "  click {0,-14} hit {1} -> page {2}: {3}" -f $n, $hit, ($(if ($changed) { 'CHANGED' } else { 'SAME (FAIL)' })), $after
  if (-not $changed) { $fails.Add("click $n did not change the page") }
  $before = $after
  if ($n -eq 'Suche') {
    # the search sheet: Esc via the page's own back path is keyboard; close it with a click on Start's channel next loop
  }
}

"--- in-page clicks"
# Bibliothek: the chevron plates beside the ticker step the shelf
$lib = Buttons | Where-Object { $_.Rect.Top -lt ($wr.T + 72) -and $_.Name -eq 'Bibliothek' } | Select-Object -First 1
[void](ClickAt 'Bibliothek' $lib.Rect)
$sig0 = PageSig
$next = Buttons | Where-Object { $_.Name -match 'chstes Fach' -and $_.Rect.Top -lt ($wr.T + 200) } | Select-Object -First 1
if ($next) { $hit = ClickAt 'chevron next' $next.Rect; $sig1 = PageSig; "  chevron '$($next.Name)' hit $hit -> " + ($(if ($sig1 -ne $sig0) { 'shelf CHANGED' } else { 'SAME (FAIL)' })) + ": $sig1"; if ($sig1 -eq $sig0) { $fails.Add('chevron next') } }
else { "  (no chevron 'nächstes Fach' found by UIA)" }
# Konsolen: a click on a closed spine turns its front forward, the position tag follows
$kon = Buttons | Where-Object { $_.Rect.Top -lt ($wr.T + 72) -and $_.Name -eq 'Konsolen' } | Select-Object -First 1
[void](ClickAt 'Konsolen' $kon.Rect)
$k0 = PageSig
$spines = Buttons | Where-Object { $_.Rect.Top -gt ($wr.T + 80) -and $_.Rect.Height -gt 300 -and $_.Rect.Width -lt 200 } | Sort-Object { $_.Rect.X }
"  wall spines (UIA): $(@($spines).Count)"
if (@($spines).Count -gt 2) { $s = $spines[2]; $hit = ClickAt "spine $($s.Name)" $s.Rect; $k1 = PageSig; "  spine '$($s.Name)' hit $hit -> " + ($(if ($k1 -ne $k0) { 'front CHANGED' } else { 'SAME (FAIL)' })) + ": $k1"; if ($k1 -eq $k0) { $fails.Add('spine click') } }

"--- caption buttons"
$cap = Buttons | Where-Object { $_.Rect.Top -lt ($wr.T + 72) -and $_.Rect.X -gt ($wr.R - 200) } | Sort-Object { $_.Rect.X }
"  caption buttons (UIA): " + (($cap | ForEach-Object { "$($_.Name)@$([int]$_.Rect.X)" }) -join ', ')
if (@($cap).Count -ge 3) {
  [void](ClickAt 'maximise' $cap[1].Rect); $z = [W]::IsZoomed($h); "  maximise -> zoomed $z"; if (-not $z) { $fails.Add('maximise') }
  $cap = Buttons | Where-Object { $_.Rect.Top -lt 72 -and $_.Rect.X -gt ($wr.R - 200) } | Sort-Object { $_.Rect.X }
  [void](ClickAt 'restore' $cap[1].Rect); $z = [W]::IsZoomed($h); "  restore -> zoomed $z"; if ($z) { $fails.Add('restore') }
  [void][W]::SetWindowPos($h, [IntPtr]::Zero, 0, 0, 1920, 1080, 0x0014); Start-Sleep -Seconds 2
  $cap = Buttons | Where-Object { $_.Rect.Top -lt 72 -and $_.Rect.X -gt ($wr.R - 200) } | Sort-Object { $_.Rect.X }
  [void](ClickAt 'minimise' $cap[0].Rect); $i = [W]::IsIconic($h); "  minimise -> iconic $i"; if (-not $i) { $fails.Add('minimise') }
  [void][W]::ShowWindow($h, 9); Start-Sleep -Seconds 2
  $cap = Buttons | Where-Object { $_.Rect.Top -lt 72 -and $_.Rect.X -gt ($wr.R - 200) } | Sort-Object { $_.Rect.X }
  [void](ClickAt 'close' $cap[-1].Rect); Start-Sleep -Seconds 3
  $gone = $p.HasExited -or -not [W]::IsWindow($h); "  close -> window gone $gone"; if (-not $gone) { $fails.Add('close') }
}
if (-not $p.HasExited) { $p.Kill() }
if ($fails.Count -eq 0) { "RESULT: all mouse checks passed" } else { "RESULT: $($fails.Count) FAIL: " + ($fails -join '; ') }
