<#
  Builds the portable RetroHub folder:  dist\RetroHub\
      RetroHub.exe        (NativeAOT starter)
      app\                (self-contained .NET app, RetroHub.App.exe + resources\)
      LIESMICH.txt
  and a zip dist\RetroHub-<version>-win-x64.zip.

  Usage:  powershell -ExecutionPolicy Bypass -File build\publish.ps1 [-SkipTests] [-NoZip]
  The .NET runtime pack is pinned to a patched version (CVE-2026-111497 affects runtimes < 10.0.14).
#>
param([switch]$SkipTests, [switch]$NoZip, [string]$Runtime = "10.0.14")

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$dist = Join-Path $repo "dist\RetroHub"
$version = ([xml](Get-Content (Join-Path $repo "Directory.Build.props"))).Project.PropertyGroup.Version

# NativeAOT needs vswhere/link.exe from Visual Studio
$vsInstaller = "C:\Program Files (x86)\Microsoft Visual Studio\Installer"
if (Test-Path $vsInstaller) { $env:PATH = "$vsInstaller;$env:PATH" }

if (-not $SkipTests) {
    Write-Host "== Tests" -ForegroundColor Cyan
    dotnet test (Join-Path $repo "RetroHub.slnx") -c Release -nologo --filter "Category!=Interactive"
    if ($LASTEXITCODE -ne 0) { throw "Tests fehlgeschlagen" }
}

Write-Host "== Clean $dist" -ForegroundColor Cyan
if (Test-Path (Join-Path $dist "app")) { Remove-Item -Recurse -Force (Join-Path $dist "app") }
New-Item -ItemType Directory -Force $dist | Out-Null

Write-Host "== App (self-contained, runtime $Runtime)" -ForegroundColor Cyan
dotnet publish (Join-Path $repo "src\RetroHub.App") -c Release -r win-x64 --self-contained true `
    -p:RuntimeFrameworkVersion=$Runtime -p:PublishReadyToRun=true -p:DebugType=none `
    -o (Join-Path $dist "app") -nologo
if ($LASTEXITCODE -ne 0) { throw "Publish App fehlgeschlagen" }

Write-Host "== Starter (NativeAOT)" -ForegroundColor Cyan
$starterOut = Join-Path $repo "build\obj\starter"
dotnet publish (Join-Path $repo "src\RetroHub.Starter") -c Release -r win-x64 -p:DebugType=none `
    -p:RuntimeFrameworkVersion=$Runtime -o $starterOut -nologo   # pins the ILCompiler pack too (NU1903 on older ones)
if ($LASTEXITCODE -ne 0) { throw "Publish Starter fehlgeschlagen" }
Copy-Item (Join-Path $starterOut "RetroHub.exe") (Join-Path $dist "RetroHub.exe") -Force

Copy-Item (Join-Path $repo "build\LIESMICH.txt") (Join-Path $dist "LIESMICH.txt") -Force

if (-not $NoZip) {
    $zip = Join-Path $repo "dist\RetroHub-$version-win-x64.zip"
    if (Test-Path $zip) { Remove-Item $zip }
    # only the program; user data folders are created on first start
    Compress-Archive -Path (Join-Path $dist "RetroHub.exe"), (Join-Path $dist "app"), (Join-Path $dist "LIESMICH.txt") -DestinationPath $zip
    Write-Host "== $zip" -ForegroundColor Green
}
Write-Host "Fertig: $dist" -ForegroundColor Green
