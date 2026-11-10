# Material scans with ComfyUI (FLUX.2 klein base 4B) - safely: one instance, hidden, low VRAM, stopped right after.
# Usage: .\run_comfy.ps1 -Out <assets-r3 dir> [-Only name1,name2]
# Rules (owner's GPU safety): no other ComfyUI/Blender of ours running, < 2500 MiB VRAM in use before the start,
# --lowvram, no browser, no console window, ComfyUI's config/models/venv untouched; the process is stopped by its PID.
param([Parameter(Mandatory)][string]$Out, [string]$Only = '', [string]$Comfy = 'D:\ai\ComfyUI', [int]$Port = 8189)
$ErrorActionPreference = 'Stop'
$ours = Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -match "main\.py.*--port $Port" -or ($_.Name -match 'blender' -and $_.CommandLine -match 'blender_(props|motifs)') }
if ($ours) { throw "leftover process(es) of ours: $($ours.ProcessId -join ', ') - stop them first" }
$used = [int]((nvidia-smi --query-gpu=memory.used --format=csv,noheader,nounits) | Select-Object -First 1)
if ($used -ge 2500) { throw "GPU memory in use: $used MiB (>= 2500) - not starting" }
$py = Join-Path $Comfy 'venv\Scripts\pythonw.exe'
if (-not (Test-Path $py)) { $py = Join-Path $Comfy 'venv\Scripts\python.exe' }
$comfyOut = Join-Path $Out 'comfy-out'
New-Item -ItemType Directory -Force $comfyOut | Out-Null
$p = Start-Process -WindowStyle Hidden -PassThru -FilePath $py -WorkingDirectory $Comfy `
    -ArgumentList 'main.py', '--listen', '127.0.0.1', '--port', $Port, '--preview-method', 'none', '--lowvram', '--output-directory', "`"$comfyOut`"" `
    -RedirectStandardError (Join-Path $Out 'comfy.stderr.log') -RedirectStandardOutput (Join-Path $Out 'comfy.stdout.log')
Set-Content (Join-Path $Out 'comfy.pid') $p.Id
try {
    $t0 = Get-Date
    while ($true) {
        try { Invoke-RestMethod "http://127.0.0.1:$Port/system_stats" -TimeoutSec 3 | Out-Null; break } catch {}
        if ($p.HasExited) { throw 'ComfyUI exited during start (see comfy.stderr.log)' }
        if (((Get-Date) - $t0).TotalSeconds -gt 180) { throw 'ComfyUI did not come up in 180 s' }
        Start-Sleep -Seconds 2
    }
    $args2 = @((Join-Path $PSScriptRoot 'comfy_materials.py'), $Out, '--host', "127.0.0.1:$Port")
    if ($Only) { $args2 += @('--only', $Only) }
    & python @args2
} finally {
    Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
    $p.WaitForExit(30000) | Out-Null
    "ComfyUI stopped (pid $($p.Id)); VRAM now: $(nvidia-smi --query-gpu=memory.used --format=csv,noheader)"
}
