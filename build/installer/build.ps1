<#
  Builds RetroHub-Setup.exe: the published RetroHub app + the bundled emulators (emulators.json), all below one folder.

  Usage (after build\publish.ps1):
      powershell -ExecutionPolicy Bypass -File build\installer\build.ps1 -AppDir dist\RetroHub [-WorkDir <folder>]

  -AppDir     the published app: the folder with app\RetroHub.App.exe (and RetroHub.exe, the starter) – i.e. dist\RetroHub
              from publish.ps1 – or the app folder itself (RetroHub.App.exe directly inside).
  -WorkDir    downloads\ (one folder per file), extract\, stage\ and out\ go here. Default: artifacts\installer.
  -Placeholder  no real app: a dummy app\RetroHub.App.exe is staged, the setup is named RetroHub-Setup-PLATZHALTER.exe.
              Only to check that the installer script compiles – that setup is NOT usable or testable.
  -Offline    never download; every file must already be in <WorkDir>\downloads.
  -NoCompile  stop after staging (no ISCC).

  Needs: 7-Zip (7z.exe), Inno Setup 6 (ISCC.exe), Python 3 + Pillow only if the art files are missing.
  Every download is checked against its SHA-256 from emulators.json – a changed file on the server stops the build.
  The staged emulator folders are checked for keys, BIOS/firmware, games, saves and configs – any hit stops the build.
#>
param(
    [string]$AppDir,
    [string]$WorkDir,
    [string]$Manifest = (Join-Path $PSScriptRoot "emulators.json"),
    [switch]$Placeholder,
    [switch]$Offline,
    [switch]$NoCompile,
    [string]$Iscc = (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
    [string]$SevenZip = (Join-Path $env:ProgramFiles "7-Zip\7z.exe")
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue" # Invoke-WebRequest is many times faster without its progress bar
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
if (-not $WorkDir) { $WorkDir = Join-Path $repo "artifacts\installer" }
$WorkDir = [IO.Path]::GetFullPath($WorkDir)
$downloads = Join-Path $WorkDir "downloads"
$extract = Join-Path $WorkDir "extract"
$stage = Join-Path $WorkDir "stage"
$out = Join-Path $WorkDir "out"
$version = ([xml](Get-Content (Join-Path $repo "Directory.Build.props"))).Project.PropertyGroup.Version
if (-not $version) { $version = "0.1.1" }

if (-not (Test-Path $SevenZip)) { throw "7-Zip fehlt: $SevenZip" }
if (-not $NoCompile -and -not (Test-Path $Iscc)) { throw "Inno Setup 6 fehlt: $Iscc" }
$m = Get-Content $Manifest -Raw -Encoding UTF8 | ConvertFrom-Json

function Step($text) { Write-Host "== $text" -ForegroundColor Cyan }

# ---------------------------------------------------------------- downloads (each file in its own folder)
function Get-Dep([string]$url, [string]$folder, [string]$sha256, [string]$fileName) {
    if (-not $fileName) { $fileName = [Uri]::UnescapeDataString((Split-Path ([Uri]$url).AbsolutePath -Leaf)) }
    $dir = Join-Path $downloads $folder
    $file = Join-Path $dir $fileName
    if (-not (Test-Path $file)) {
        if ($Offline) { throw "Fehlt (offline): $file`n  Quelle: $url" }
        New-Item -ItemType Directory -Force $dir | Out-Null
        Write-Host "   lade $url"
        $part = "$file.part"
        Invoke-WebRequest -Uri $url -OutFile $part -UseBasicParsing
        Move-Item -Force $part $file
    }
    $have = (Get-FileHash $file -Algorithm SHA256).Hash
    if ($have -ne $sha256.ToUpperInvariant()) {
        throw "Prüfsumme stimmt nicht: $file`n  Quelle   $url`n  erwartet $sha256`n  ist      $have"
    }
    return $file
}

function Invoke-7z([string[]]$arguments) {
    & $SevenZip @arguments | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "7-Zip fehlgeschlagen ($LASTEXITCODE): $($arguments -join ' ')" }
}

# robocopy: fast for RetroArch's ~15 000 files; exit codes below 8 mean success
function Copy-Tree([string]$from, [string]$to, [string[]]$excludeFiles) {
    New-Item -ItemType Directory -Force $to | Out-Null
    $rc = @($from, $to, "/E", "/NFL", "/NDL", "/NJH", "/NJS", "/NP", "/R:1", "/W:1")
    if ($excludeFiles) { $rc += "/XF"; $rc += $excludeFiles }
    & robocopy @rc | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Kopieren fehlgeschlagen ($LASTEXITCODE): $from -> $to" }
    $global:LASTEXITCODE = 0
}

# ---------------------------------------------------------------- 0. art (checked in; regenerated only when missing)
if (-not (Test-Path (Join-Path $PSScriptRoot "art\wizard-164x314.bmp")) -or -not (Test-Path (Join-Path $PSScriptRoot "art\setup.ico"))) {
    Step "Installer-Bilder"
    python (Join-Path $PSScriptRoot "art\make_art.py")
    if ($LASTEXITCODE -ne 0) { throw "make_art.py fehlgeschlagen" }
}

# ---------------------------------------------------------------- 1. the app
Step "App"
if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
New-Item -ItemType Directory -Force $stage | Out-Null
$outputName = "RetroHub-Setup"
if ($Placeholder) {
    # NOT a real app – only so ISCC has something to pack
    $outputName = "RetroHub-Setup-PLATZHALTER"
    New-Item -ItemType Directory -Force (Join-Path $stage "app") | Out-Null
    Set-Content (Join-Path $stage "app\RetroHub.App.exe") "PLATZHALTER - kein Programm. Dieses Setup ist nur ein Kompiliertest und NICHT nutzbar." -Encoding ASCII
    Set-Content (Join-Path $stage "app\PLATZHALTER.txt") "Platzhalter-Build von build\installer\build.ps1 -Placeholder: enthält keine RetroHub-App." -Encoding UTF8
} else {
    if (-not $AppDir) { throw "-AppDir fehlt (z. B. dist\RetroHub aus build\publish.ps1) – oder -Placeholder für einen reinen Kompiliertest." }
    $AppDir = [IO.Path]::GetFullPath($AppDir)
    if (Test-Path (Join-Path $AppDir "app\RetroHub.App.exe")) {
        Copy-Tree (Join-Path $AppDir "app") (Join-Path $stage "app")
        $starter = Join-Path $AppDir "RetroHub.exe"
        if (Test-Path $starter) { Copy-Item $starter (Join-Path $stage "RetroHub.exe") }
    } elseif (Test-Path (Join-Path $AppDir "RetroHub.App.exe")) {
        Copy-Tree $AppDir (Join-Path $stage "app")
    } else {
        throw "Keine veröffentlichte App in $AppDir (erwartet app\RetroHub.App.exe oder RetroHub.App.exe)."
    }
    # user data never comes along from a publish folder that was started once
    foreach ($d in "games", "emulators", "bios", "saves", "savestates", "screenshots", "artwork", "data", "plugins", "themes", "backups") {
        if (Test-Path (Join-Path $stage "app\$d")) { throw "Die App-Quelle enthält Nutzerdaten ($d\) – bitte frisch veröffentlichen: $AppDir" }
    }
}

# ---------------------------------------------------------------- 2. emulators
if (Test-Path $extract) { Remove-Item -Recurse -Force $extract }
New-Item -ItemType Directory -Force $extract | Out-Null

foreach ($e in $m.emulators) {
    Step "$($e.name) $($e.version)"
    $archive = Get-Dep $e.url $e.id $e.sha256
    $tmp = Join-Path $extract $e.id
    Invoke-7z @("x", "-y", "-bso0", "-bsp0", $archive, "-o$tmp")
    $src = $tmp
    if ($e.archiveRoot) { $src = Join-Path $tmp $e.archiveRoot }
    if (-not (Test-Path (Join-Path $src $e.exe))) { throw "$($e.name): $($e.exe) fehlt nach dem Entpacken ($src)" }
    $target = Join-Path $stage $e.target
    $exclude = @()
    if ($e.exclude) { $exclude = @($e.exclude) }
    Copy-Tree $src $target $exclude
    foreach ($l in @($e.licenseFiles)) {
        if (-not $l) { continue }
        $lic = Get-Dep $l.url "license-$($e.id)" $l.sha256
        Copy-Item $lic (Join-Path $target $l.as)
    }
    # portable markers (empty files) – created when the archive does not bring them
    foreach ($marker in @($e.markers)) {
        if ($marker -and -not (Test-Path (Join-Path $target $marker))) { New-Item -ItemType File (Join-Path $target $marker) | Out-Null }
    }
    # version.txt: RetroHub shows it for exes without a version resource (EmulatorRegistry.VersionFile)
    Set-Content (Join-Path $target "version.txt") $e.version -Encoding ASCII
}

# Eden: never staged – Setup downloads it during the installation (RetroHub.iss, InstallEden). Here only the pinned URL
# and SHA-256 are checked, so a moved or changed file on Eden's server is noticed at build time, not by a player.
$eden = @($m.downloadAtInstall | Where-Object { $_.id -eq "eden" })[0]
if (-not $eden) { throw "emulators.json: downloadAtInstall-Eintrag 'eden' fehlt" }
Step "$($eden.name) $($eden.version) (nur Prüfung – wird beim Installieren geladen)"
$edenZip = Get-Dep $eden.url $eden.id $eden.sha256
if ((Get-Item $edenZip).Length -ne $eden.size) { throw "Eden: Größe stimmt nicht mit emulators.json überein" }
$edenList = & $SevenZip l -ba -slt $edenZip | Where-Object { $_ -like "Path = *" } | ForEach-Object { $_.Substring(7) }
if (-not ($edenList -contains $eden.exe)) { throw "Eden: $($eden.exe) liegt nicht oben im Archiv – Setup entpackt flach nach $($eden.target)" }
foreach ($p in $edenList) {
    $top = ($p -split '[\\/]')[0]
    $known = @($eden.uninstallFiles | ForEach-Object { $_.TrimEnd('\') })
    if (-not ($known -contains $top)) { throw "Eden: '$top' fehlt in uninstallFiles (emulators.json) – sonst bliebe es beim Deinstallieren liegen" }
}
$iss = Get-Content (Join-Path $PSScriptRoot "RetroHub.iss") -Raw -Encoding UTF8
foreach ($f in $eden.uninstallFiles) {
    $name = "{app}\emulators\eden\" + $f.TrimEnd('\')
    if (-not $iss.Contains("Name: `"$name`"")) { throw "RetroHub.iss [UninstallDelete]: Eintrag für $name fehlt" }
}

# RetroArch cores: only the listed dlls from the stable core pack of the same release, with their licence texts
$c = $m.retroarchCores
Step "RetroArch-Cores ($(@($c.cores).Count))"
$pack = Get-Dep $c.url $c.id $c.sha256
$coreTmp = Join-Path $extract "cores"
$names = @($c.cores | ForEach-Object { Join-Path $c.archiveDir "$($_.core)_libretro.dll" })
Invoke-7z (@("e", "-y", "-bso0", "-bsp0", $pack, "-o$coreTmp") + $names)
$coreTarget = Join-Path $stage $c.target
$licTarget = Join-Path $stage $c.licenseTarget
New-Item -ItemType Directory -Force $coreTarget, $licTarget | Out-Null
$overview = @(
    "RetroArch-Cores in RetroHub (aus RetroArch_cores.7z $($c.version), $($c.url))",
    "Core | Systeme | Lizenz | Quellcode",
    ""
)
foreach ($core in $c.cores) {
    $dll = Join-Path $coreTmp "$($core.core)_libretro.dll"
    if (-not (Test-Path $dll)) { throw "Core fehlt im Paket: $($core.core)_libretro.dll" }
    Copy-Item $dll $coreTarget
    $lic = Get-Dep $core.licenseUrl "license-core-$($core.core)" $core.licenseSha256
    Copy-Item $lic (Join-Path $licTarget "$($core.core)_libretro-LICENSE.txt")
    # further licence texts the main one refers to (MAME: docs/legal/*)
    foreach ($x in @($core.extraLicenses)) {
        if (-not $x) { continue }
        $xf = Get-Dep $x.url "license-core-$($core.core)-$($x.name)" $x.sha256
        Copy-Item $xf (Join-Path $licTarget "$($core.core)_libretro-$($x.name).txt")
    }
    $overview += "$($core.core)_libretro.dll | $($core.systems) | $($core.license) | $($core.source)"
}
$overview += "", "Die Lizenztexte liegen in diesem Ordner (<core>_libretro-LICENSE.txt)."
Set-Content (Join-Path $licTarget "CORES.txt") $overview -Encoding UTF8

# ---------------------------------------------------------------- 3. safety check: nothing private or copyrighted
Step "Prüfung: keine Keys, BIOS, Firmware, Spiele, Spielstände, Configs"
$forbiddenFiles = @(
    "*.keys", "keys.txt", "aes_keys.txt", "seeddb.bin", "otp.bin", "secret_sector.bin", "boot9.bin", "boot11.bin",
    "nand.bin", "keys.bin", "bios7.bin", "bios9.bin", "firmware.bin", "scph*.bin", "ps2-*.bin", "*.nca", "*.nsp", "*.xci",
    "*.wux", "*.wud", "*.wua", "*.rpx", "*.iso", "*.rvz", "*.gcz", "*.wbfs", "*.3ds", "*.cia", "*.cci", "*.cxi", "*.nds",
    "*.gba", "*.gbc", "*.sfc", "*.smc", "*.z64", "*.n64", "*.chd", "*.cue",
    "*.sav", "*.srm", "*.dsv", "*.sgm", "*.ss?", "*.state*", "*.gci", "*.raw",
    "retroarch.cfg", "retroarch-core-options.cfg", "config.ini", "qt.ini", "melonDS.toml", "desmume.ini",
    "qt-config.ini", "settings.xml", "Dolphin.ini", "*.lpl", "library.sqlite3", "rtc.bin", "wfcsettings.bin"
)
# data folders of the emulators: created by Setup/RetroHub/the emulator, never staged
$forbiddenDirs = @("emulators\dolphin\User", "emulators\azahar\user", "emulators\eden\user", "emulators\cemu\portable",
    "emulators\retroarch\config", "emulators\retroarch\playlists", "emulators\retroarch\thumbnails")
$emuStage = Join-Path $stage "emulators"
$hits = @()
foreach ($pattern in $forbiddenFiles) {
    $hits += Get-ChildItem $emuStage -Recurse -File -Filter $pattern -ErrorAction SilentlyContinue | ForEach-Object { $_.FullName }
}
# Eden must not be inside the setup at all (it is downloaded during the installation)
$forbiddenDirs += "emulators\eden"
$hits += Get-ChildItem $stage -Recurse -File -Filter "eden*.exe" -ErrorAction SilentlyContinue | ForEach-Object { $_.FullName }
# FBNeo must not be in the setup (its licence forbids asking for donations): no core dll anywhere, no licence text.
# (RetroArch's own package keeps its playlist icons/database/info entries named "FBNeo - Arcade Games" – those are
# RetroArch files describing a core the player may add himself, not FBNeo.)
$hits += Get-ChildItem $emuStage -Recurse -File -Filter "fbneo*.dll" -ErrorAction SilentlyContinue | ForEach-Object { $_.FullName }
$hits += Get-ChildItem (Join-Path $stage $c.licenseTarget) -File -Filter "fbneo*" -ErrorAction SilentlyContinue | ForEach-Object { $_.FullName }
foreach ($d in $forbiddenDirs) {
    if (Test-Path (Join-Path $stage $d)) { $hits += (Join-Path $stage $d) }
}
# RetroArch's empty portable data folders (saves, states, system, screenshots) may exist but must stay empty
foreach ($d in "saves", "states", "system", "screenshots") {
    $p = Join-Path $emuStage "retroarch\$d"
    if ((Test-Path $p) -and (Get-ChildItem $p -Recurse -File)) { $hits += $p }
}
if ($hits.Count -gt 0) { throw "Verbotene Dateien im Staging-Ordner:`n  " + (($hits | Sort-Object -Unique) -join "`n  ") }

$sizeMb = [math]::Round(((Get-ChildItem $stage -Recurse -File | Measure-Object Length -Sum).Sum) / 1MB)
Write-Host "   Staging: $stage ($sizeMb MB)" -ForegroundColor Green
if ($NoCompile) { return }

# ---------------------------------------------------------------- 4. installer
Step "Inno Setup -> $out\$outputName.exe"
New-Item -ItemType Directory -Force $out | Out-Null
# RetroArch's shader presets go up to ~165 characters below the stage folder; ISCC reads its sources with the classic
# 260-character limit ("Das System kann den angegebenen Pfad nicht finden"). A deep WorkDir is therefore reached through
# a short junction in %TEMP% (only the link is removed afterwards, never the files behind it).
$longest = (Get-ChildItem $stage -Recurse -File | ForEach-Object { $_.FullName.Length } | Measure-Object -Maximum).Maximum
$junction = $null
$issStage = $stage
if ($longest -gt 240) {
    $junction = Join-Path $env:TEMP ("rh-stage-" + [Guid]::NewGuid().ToString("N").Substring(0, 8))
    New-Item -ItemType Junction -Path $junction -Target $stage | Out-Null
    $issStage = $junction
}
try {
    & $Iscc "/Qp" "/DStage=$issStage" "/DAppVersion=$version" "/DOutputName=$outputName" "/O$out" `
        "/DEdenVersion=$($eden.version)" "/DEdenUrl=$($eden.url)" "/DEdenSha256=$($eden.sha256.ToLowerInvariant())" "/DEdenSize=$($eden.size)" `
        (Join-Path $PSScriptRoot "RetroHub.iss")
    $isccExit = $LASTEXITCODE
} finally {
    if ($junction) { cmd /c rmdir "$junction" | Out-Null } # rmdir on a junction removes the link only
}
if ($isccExit -ne 0) { throw "ISCC fehlgeschlagen ($isccExit)" }
$setup = Get-Item (Join-Path $out "$outputName.exe")
Write-Host ("Fertig: {0} ({1:N0} MB, SHA-256 {2})" -f $setup.FullName, ($setup.Length / 1MB), (Get-FileHash $setup.FullName -Algorithm SHA256).Hash) -ForegroundColor Green
if ($Placeholder) { Write-Host "ACHTUNG: Platzhalter-Build ohne RetroHub-App – nur ein Kompiliertest, nicht nutzbar." -ForegroundColor Yellow }
