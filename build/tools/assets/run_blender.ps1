# Render props headless (no window: -b; owner's prefs untouched: --factory-startup), BelowNormal priority.
# CPU only: the scripts set Cycles to the CPU + OpenImageDenoise, -t 8 caps the threads (the GPU stays free).
# Usage: .\run_blender.ps1 -Out <assets-r3 dir> -Jobs spine-l,sleeves-l [-Samples 64] [-Script blender_motifs.py]
param([Parameter(Mandatory)][string]$Out, [Parameter(Mandatory)][string]$Jobs, [int]$Samples = 64, [string]$Script = "blender_props.py", [switch]$Gpu,
      [string]$Blender = 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe')
$script = Join-Path $PSScriptRoot $Script
# -Gpu: final full-quality pass on the GPU (OptiX), only when Blender is the GPU's only heavy user (no ComfyUI)
if ($Gpu) { $env:RH_BLENDER_GPU = '1' } else { Remove-Item Env:RH_BLENDER_GPU -ErrorAction SilentlyContinue }
New-Item -ItemType Directory -Force (Join-Path $Out 'blend') | Out-Null
$tag = ($Jobs -replace ',', '_')
if ($tag.Length -gt 60) { $tag = $tag.Substring(0, 40) + '-' + [Math]::Abs($Jobs.GetHashCode()) }
$log = Join-Path $Out ("blend\run-" + [IO.Path]::GetFileNameWithoutExtension($Script) + '-' + $tag + '.log')
$p = Start-Process -FilePath $Blender -ArgumentList '-b', '--factory-startup', '-t', '8', '-P', "`"$script`"", '--', "`"$Out`"", $Jobs, '--samples', $Samples `
    -NoNewWindow -PassThru -RedirectStandardOutput $log -RedirectStandardError "$log.err"
try { $p.PriorityClass = 'BelowNormal' } catch {}
$p.WaitForExit()
Get-Content $log | Select-String 'GPU|DONE|Error|Traceback|File "|line \d' | Select-Object -First 40
Get-Content "$log.err" | Select-String -NotMatch 'HIPEW' | Select-Object -Last 15
