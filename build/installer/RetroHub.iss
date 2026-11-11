; Installer for RetroHub (launcher + mGBA, melonDS, DeSmuME, Azahar, Dolphin, Cemu, RetroArch with cores).
; Eden (Switch) is NOT inside this setup: the optional task "eden" downloads it during the installation from Eden's own
; server, checks its SHA-256 and unpacks it into {app}\emulators\eden (see InstallEden below).
; Build with build.ps1 – it assembles the staging folder and passes it as /DStage=<folder>, plus Eden's pinned
; download from emulators.json (/DEdenUrl, /DEdenSha256, /DEdenSize, /DEdenVersion).
; Everything goes below ONE folder (the RetroHub root, see AppPaths.Resolve: the exe lives in {app}\app, so the
; root is {app}). The player's folders and every file an emulator writes itself (saves, settings, keys) survive an
; uninstall and an update – Setup only removes and replaces the files it installed.

#define AppName "RetroHub"
#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#ifndef Stage
  #error Stage folder missing – build with build.ps1 (ISCC /DStage=<staging folder> RetroHub.iss)
#endif
#ifndef OutputName
  #define OutputName "RetroHub-Setup"
#endif
#define AppExe "app\RetroHub.App.exe"
; Eden's pinned download (defaults = emulators.json "downloadAtInstall"; build.ps1 passes the manifest's values)
#ifndef EdenVersion
  #define EdenVersion "0.2.1"
#endif
#ifndef EdenUrl
  #define EdenUrl "https://stable.eden-emu.dev/v0.2.1/Eden-Windows-v0.2.1-amd64-msvc-standard.zip"
#endif
#ifndef EdenSha256
  #define EdenSha256 "ff498e5da9630216926ac3cbe9fb493b14930665c728a40a8f5b59507fdd7ebf"
#endif
#ifndef EdenSize
  #define EdenSize "31319001"
#endif

[Setup]
AppId={{EA594C44-3BEC-401A-A8EC-9372B3221732}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
; the search in Settings > Apps matches name OR publisher
AppPublisher=RetroHub
VersionInfoVersion={#AppVersion}
VersionInfoProductName={#AppName}
VersionInfoDescription={#AppName} Setup
DefaultDirName={%USERPROFILE}\RetroHub
; after an uninstall the folder still holds the player's games and saves – reinstalling there is the normal case
DirExistsWarning=no
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Windows 10 1903 – the oldest version all bundled emulators support (Dolphin's build_info.txt)
MinVersion=10.0.18362
OutputBaseFilename={#OutputName}
Compression=lzma2/ultra64
SolidCompression=yes
LZMAUseSeparateProcess=yes
LZMANumBlockThreads=4
; the look: Windows 11 style that follows light/dark mode, no separator lines; RetroHub's banner (art\make_art.py)
WizardStyle=modern dynamic windows11 hidebevels
WizardImageFile=art\wizard-164x314.bmp,art\wizard-205x393.bmp,art\wizard-246x471.bmp,art\wizard-287x550.bmp,art\wizard-328x628.bmp
WizardSmallImageFile=art\small-55x55.png,art\small-69x69.png,art\small-83x83.png,art\small-96x96.png,art\small-110x110.png
; the same pictures in dark mode (otherwise Inno shows its own dark box picture)
WizardImageFileDynamicDark=art\wizard-164x314.bmp,art\wizard-205x393.bmp,art\wizard-246x471.bmp,art\wizard-287x550.bmp,art\wizard-328x628.bmp
WizardSmallImageFileDynamicDark=art\small-55x55.png,art\small-69x69.png,art\small-83x83.png,art\small-96x96.png,art\small-110x110.png
; few clicks: welcome (with the banner), folder only on the first install, desktop icon, go
DisableWelcomePage=no
DisableDirPage=auto
DisableReadyPage=yes
SetupIconFile=art\setup.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
; German first (RetroHub's default language), English preselected on an English Windows;
; the choice also becomes RetroHub's language on a new install (see CurStepChanged)
ShowLanguageDialog=yes
LanguageDetectionMethod=uilanguage
; "full" = Inno's is7z.dll, which also unpacks .zip (Eden's archive) – used by ExtractArchive in InstallEden
ArchiveExtraction=full

[Languages]
Name: "german"; MessagesFile: "compiler:Languages\German.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Messages]
german.WelcomeLabel1=Willkommen bei RetroHub!
german.WelcomeLabel2=Dein Retro-Spielzentrum: deine Spiele von Konsolen, Handhelds und PC in einem Fenster – mit Maus, Tastatur oder Controller.%n%nDie Emulatoren kommen gleich mit und sind fertig eingerichtet. Deine Spiele bringst du selbst mit.
german.FinishedHeadingLabel=Fertig – viel Spaß!
german.FinishedLabel=RetroHub ist installiert.%n%nLege deine Spiele in den Ordner „games“ im RetroHub-Ordner. Du findest RetroHub im Startmenü, auf dem Desktop oder über die Windows-Suche.
german.FinishedLabelNoIcons=RetroHub ist installiert.%n%nLege deine Spiele in den Ordner „games“ im RetroHub-Ordner. Du findest RetroHub über die Windows-Suche („RetroHub“).
german.ClickFinish=Klicke auf „Fertigstellen“.
german.WizardSelectTasks=Desktop-Symbol und Switch-Emulator
german.SelectTasksDesc=Fast fertig!
german.SelectTasksLabel2=Soll RetroHub auch ein Symbol auf dem Desktop bekommen? Im Startmenü und in der Windows-Suche findest du es sowieso.%n%nDer Switch-Emulator Eden ist nicht im Setup enthalten: Ist er ausgewählt, lädt Setup ihn während der Installation von Edens eigener Seite (ca. 30 MB, Internet nötig).
; games, saves and the emulators' data are kept on purpose – the final message says so, otherwise the folder looks like a leftover
german.UninstalledAll=%1 wurde entfernt.%n%nDeine Spiele, Spielstände, Screenshots, Einstellungen und die Daten der Emulatoren sind noch da – im RetroHub-Ordner (den du bei der Installation gewählt hast). Wenn du sie nicht mehr brauchst, kannst du den Ordner selbst löschen.
english.WelcomeLabel1=Welcome to RetroHub!
english.WelcomeLabel2=Your retro game hub: your console, handheld and PC games in one window – with mouse, keyboard or controller.%n%nThe emulators come right along, already set up. You bring your own games.
english.FinishedHeadingLabel=Done – have fun!
english.FinishedLabel=RetroHub is installed.%n%nPut your games into the "games" folder in the RetroHub folder. You'll find RetroHub in the Start menu, on the desktop or through the Windows search.
english.FinishedLabelNoIcons=RetroHub is installed.%n%nPut your games into the "games" folder in the RetroHub folder. You'll find RetroHub through the Windows search ("RetroHub").
english.ClickFinish=Click "Finish".
english.WizardSelectTasks=Desktop icon and Switch emulator
english.SelectTasksDesc=Almost done!
english.SelectTasksLabel2=Should RetroHub get an icon on the desktop too? You'll find it in the Start menu and the Windows search anyway.%n%nThe Switch emulator Eden is not part of this setup: if it is selected, Setup downloads it from Eden's own website during the installation (about 30 MB, needs internet).
english.UninstalledAll=%1 was removed.%n%nYour games, saves, screenshots, settings and the emulators' data are still there – in the RetroHub folder (the one you chose when installing). If you don't need them anymore, you can delete the folder yourself.

[CustomMessages]
german.ShortcutComment=Retro-Spiele und Emulatoren in einem Fenster
english.ShortcutComment=Retro games and emulators in one window
german.OpenGuide=Anleitung (LIESMICH) öffnen
english.OpenGuide=Open the guide (README)
german.GuideFile=LIESMICH.txt
english.GuideFile=README.txt
german.StillRunning=Aus dem RetroHub-Ordner läuft noch:
english.StillRunning=Still running from the RetroHub folder:
german.CloseFirst=Bitte schließe es zuerst (in Spielen vorher speichern!) und versuch es dann nochmal.
english.CloseFirst=Please close it first (save in your games before!) and try again.
german.PathTooLong=Dieser Ordnerpfad ist zu lang (höchstens 90 Zeichen). Windows erlaubt nur 260 Zeichen pro Datei, und manche Emulator-Dateien und 3DS-Spielstände liegen sehr tief im RetroHub-Ordner. Bitte wähle einen kürzeren Ordner, z. B. C:\Users\<Name>\RetroHub.
english.PathTooLong=This folder path is too long (90 characters at most). Windows only allows 260 characters per file, and some emulator files and 3DS saves lie very deep inside the RetroHub folder. Please choose a shorter folder, e.g. C:\Users\<name>\RetroHub.
german.EdenGroup=Switch-Emulator:
english.EdenGroup=Switch emulator:
german.EdenTask=Switch-Emulator Eden {#EdenVersion} herunterladen und installieren (von eden-emu.dev, GPL-3.0)
english.EdenTask=Download and install the Switch emulator Eden {#EdenVersion} (from eden-emu.dev, GPL-3.0)
german.EdenStatus=Switch-Emulator Eden wird von eden-emu.dev geladen …
english.EdenStatus=Downloading the Switch emulator Eden from eden-emu.dev …
german.EdenUnpacking=Switch-Emulator Eden wird entpackt …
english.EdenUnpacking=Unpacking the Switch emulator Eden …
german.EdenFailed=Der Switch-Emulator Eden konnte nicht installiert werden:%n%1%n%nRetroHub und alle anderen Emulatoren sind trotzdem installiert. Eden kannst du später nachholen: Setup noch einmal ausführen (mit Internet) oder Eden selbst herunterladen – siehe LIESMICH.txt im RetroHub-Ordner, Abschnitt „SWITCH-EMULATOR EDEN“.
english.EdenFailed=The Switch emulator Eden could not be installed:%n%1%n%nRetroHub and all other emulators are installed anyway. You can add Eden later: run Setup again (with internet) or download Eden yourself – see README.txt in the RetroHub folder, section "SWITCH EMULATOR EDEN".
german.SetupCancelled=Abgebrochen – RetroHub oder ein Emulator läuft noch.
english.SetupCancelled=Cancelled – RetroHub or an emulator is still running.

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
; checked by default; downloaded during the installation (InstallEden), never inside this setup
Name: "eden"; Description: "{cm:EdenTask}"; GroupDescription: "{cm:EdenGroup}"

[Dirs]
; the player's folders (RetroHub creates the rest on its first start) – never removed
Name: "{app}\games"; Flags: uninsneveruninstall
Name: "{app}\bios"; Flags: uninsneveruninstall
Name: "{app}\saves"; Flags: uninsneveruninstall
Name: "{app}\savestates"; Flags: uninsneveruninstall
Name: "{app}\screenshots"; Flags: uninsneveruninstall
Name: "{app}\artwork"; Flags: uninsneveruninstall
Name: "{app}\data"; Flags: uninsneveruninstall
Name: "{app}\plugins"; Flags: uninsneveruninstall
Name: "{app}\themes"; Flags: uninsneveruninstall
Name: "{app}\backups"; Flags: uninsneveruninstall
; portable mode of the emulators from the first start on – also when started without RetroHub (their data stays in
; the RetroHub folder instead of %APPDATA% / Documents). RetroHub's portable recipes create the same folders.
; Azahar: "user" next to azahar.exe. Eden (only with its task): "user" (+ "user\config", which keeps Eden's
; yuzu-migration dialog away). Cemu: "portable" next to Cemu.exe. Dolphin: portable.txt (a staged file);
; mGBA: portable.ini (in its archive).
Name: "{app}\emulators\azahar\user"; Flags: uninsneveruninstall
Name: "{app}\emulators\eden\user"; Flags: uninsneveruninstall; Tasks: eden
Name: "{app}\emulators\eden\user\config"; Flags: uninsneveruninstall; Tasks: eden
Name: "{app}\emulators\cemu\portable"; Flags: uninsneveruninstall

[InstallDelete]
; An update replaces the program files: the old ones go first, so nothing stale (renamed libraries, plugins) stays.
; Only folders that hold program files exclusively – never an emulator's data folder (User\, user\, portable\, config
; files next to the exes) and never RetroArch's cores\ (the player may have added cores with RetroArch's updater).
Type: filesandordirs; Name: "{app}\app"
Type: files; Name: "{app}\emulators\azahar\*.dll"
Type: filesandordirs; Name: "{app}\emulators\azahar\plugins"
Type: files; Name: "{app}\emulators\dolphin\*.dll"
Type: filesandordirs; Name: "{app}\emulators\dolphin\Sys"
Type: filesandordirs; Name: "{app}\emulators\dolphin\QtPlugins"
Type: filesandordirs; Name: "{app}\emulators\dolphin\Languages"
Type: filesandordirs; Name: "{app}\emulators\cemu\resources"
Type: filesandordirs; Name: "{app}\emulators\cemu\gameProfiles\default"
Type: files; Name: "{app}\emulators\mgba\*.dll"
Type: filesandordirs; Name: "{app}\emulators\mgba\shaders"
Type: files; Name: "{app}\emulators\retroarch\*.dll"

[Files]
; RetroHub itself (self-contained .NET app) – {app}\app is program-only (AppPaths.App), replaced on every update
Source: "{#Stage}\app\*"; DestDir: "{app}\app"; Flags: ignoreversion recursesubdirs createallsubdirs
#if FileExists(Stage + "\RetroHub.exe")
; the small starter next to the folders (starts app\RetroHub.App.exe) – for people who open the folder
Source: "{#Stage}\RetroHub.exe"; DestDir: "{app}"; Flags: ignoreversion
#endif
; the emulators: unmodified official builds, flat in emulators\<catalog id>\ (EmulatorRegistry looks there first);
; only program files and licence texts are staged – build.ps1 refuses keys, BIOS, firmware, configs and saves
Source: "{#Stage}\emulators\*"; DestDir: "{app}\emulators"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "LIESMICH.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "README.txt"; DestDir: "{app}"; Flags: ignoreversion

[INI]
; a stamp of this install (also the same version again) – for support and later "what's new" hints
Filename: "{app}\app\install.ini"; Section: "Setup"; Key: "Version"; String: "{#AppVersion}"
Filename: "{app}\app\install.ini"; Section: "Setup"; Key: "Stamp"; String: "{code:InstallStamp}"
Filename: "{app}\app\install.ini"; Section: "Setup"; Key: "Language"; String: "{language}"

[Icons]
; one clean Start menu entry – found by the Windows search like any installed app. Straight to the app exe (not the
; starter), so pinning to the taskbar and the running-check work with the real process.
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}\app"; Comment: "{cm:ShortcutComment}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}\app"; Comment: "{cm:ShortcutComment}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; WorkingDir: "{app}\app"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent
; the long guide stays in the folder; here only on request
Filename: "{app}\{cm:GuideFile}"; Description: "{cm:OpenGuide}"; Flags: shellexec nowait postinstall skipifsilent unchecked

[UninstallDelete]
; program files only (install.ini and anything else in app\) – NOT the player's folders, NOT the emulators' data
Type: filesandordirs; Name: "{app}\app"
; Eden's program files: unpacked by [Code] (InstallEden), so they are not in the uninstall log. Exactly the top level
; of its archive (emulators.json "uninstallFiles"; build.ps1 checks both) – NOT its data folder user\
Type: files; Name: "{app}\emulators\eden\eden.exe"
Type: files; Name: "{app}\emulators\eden\eden-cli.exe"
Type: files; Name: "{app}\emulators\eden\LICENSE.txt"
Type: files; Name: "{app}\emulators\eden\README.md"
Type: filesandordirs; Name: "{app}\emulators\eden\LICENSES"
Type: files; Name: "{app}\emulators\eden\version.txt"
Type: dirifempty; Name: "{app}\emulators\eden"

[Code]
{ No "ready" page: the page before the install says what the next click does. }
procedure CurPageChanged(CurPageID: Integer);
begin
  { the desktop icon page is always the last one before the install }
  if CurPageID = wpSelectTasks then
    WizardForm.NextButton.Caption := SetupMessage(msgButtonInstall);
end;

{ The deepest bundled file (a RetroArch shader preset) is ~165 characters below the RetroHub folder, and 3DS games
  nest ~140 characters below saves\3ds\sdmc – with Windows' 260-character limit the folder itself must stay short. }
function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (CurPageID = wpSelectDir) and (Length(AddBackslash(WizardDirValue())) > 90) then
  begin
    MsgBox(CustomMessage('PathTooLong'), mbError, MB_OK);
    Result := False;
  end;
end;

{ Time of this install, written to install.ini – see [INI]. }
function InstallStamp(Param: String): String;
begin
  Result := GetDateTimeString('yyyymmddhhnnss', #0, #0);
end;

{ Programs still running from the RetroHub folder (RetroHub, emulators), one per line. }
function RunningFromApp(): String;
var
  Locator, Service, Processes, Process: Variant;
  I: Integer;
  AppDir, ExePath, ExeName: String;
begin
  Result := '';
  AppDir := Lowercase(AddBackslash(ExpandConstant('{app}')));
  try
    Locator := CreateOleObject('WbemScripting.SWbemLocator');
    Service := Locator.ConnectServer('.', 'root\CIMV2');
    Processes := Service.ExecQuery('SELECT Name, ExecutablePath FROM Win32_Process');
    for I := 0 to Processes.Count - 1 do
    begin
      Process := Processes.ItemIndex(I);
      if not VarIsNull(Process.ExecutablePath) then
      begin
        ExePath := Process.ExecutablePath;
        ExeName := Process.Name;
        { unins000.exe is this uninstaller itself }
        if (Pos(AppDir, Lowercase(ExePath)) = 1) and (Pos('unins', Lowercase(ExeName)) <> 1) then
          Result := Result + #13#10 + '   • ' + ExeName;
      end;
    end;
  except
    { no WMI – go on, Windows reports locked files }
  end;
end;

{ Asks until nothing runs from the RetroHub folder any more; False when the player cancels. }
function WaitUntilClosed(): Boolean;
var
  Running: String;
begin
  Result := True;
  Running := RunningFromApp();
  while Running <> '' do
  begin
    if MsgBox(CustomMessage('StillRunning') + Running + #13#10#13#10 + CustomMessage('CloseFirst'),
              mbError, MB_RETRYCANCEL) = IDCANCEL then
    begin
      Result := False;
      Exit;
    end;
    Running := RunningFromApp();
  end;
end;

{ An update while a game runs would replace the emulator under it (and Windows' automatic closing would end the
  game without saving) – ask the player to close it first. Silent installs skip the question. }
function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if WizardSilent() or not DirExists(ExpandConstant('{app}')) then Exit;
  if not WaitUntilClosed() then
    Result := CustomMessage('SetupCancelled');
end;

{ The uninstaller can't delete files of running programs – ask to close them first. }
function InitializeUninstall(): Boolean;
begin
  Result := UninstallSilent() or WaitUntilClosed();
end;

{ Download progress of Eden, shown under the status line of the installing page. }
function OnEdenDownload(const Url, FileName: String; const Progress, ProgressMax: Int64): Boolean;
begin
  if ProgressMax > 0 then
    WizardForm.FilenameLabel.Caption := IntToStr(Progress div 1048576) + ' / ' + IntToStr(ProgressMax div 1048576) + ' MB'
  else
    WizardForm.FilenameLabel.Caption := IntToStr(Progress div 1048576) + ' MB';
  Result := True;
end;

{ Eden (Switch) is not inside this setup (owner decision): with the task "eden" it is downloaded now from Eden's own
  server ({#EdenUrl}), checked against the pinned SHA-256 (DownloadTemporaryFile raises an exception on a mismatch)
  and unpacked flat into emulators\eden – eden.exe directly in the folder, as EmulatorRegistry expects; Setup already
  created user\ and user\config\ ([Dirs]). Any failure (no internet, server moved, wrong checksum, disk full) never
  aborts the installation: it is logged and the player is told how to add Eden later (LIESMICH, "SWITCH-EMULATOR EDEN"). }
procedure InstallEden();
var
  Zip, Target: String;
begin
  Target := ExpandConstant('{app}\emulators\eden');
  try
    WizardForm.StatusLabel.Caption := CustomMessage('EdenStatus');
    Log('Eden: downloading {#EdenUrl} (SHA-256 {#EdenSha256}, {#EdenSize} bytes)');
    DownloadTemporaryFile('{#EdenUrl}', 'eden.zip', '{#EdenSha256}', @OnEdenDownload);
    Zip := ExpandConstant('{tmp}\eden.zip');
    WizardForm.StatusLabel.Caption := CustomMessage('EdenUnpacking');
    WizardForm.FilenameLabel.Caption := '';
    ForceDirectories(Target);
    ExtractArchive(Zip, Target, '', True, nil);
    if not FileExists(Target + '\eden.exe') then
      RaiseException('eden.exe fehlt nach dem Entpacken / missing after unpacking');
    { eden.exe has no version resource: RetroHub shows this one (EmulatorRegistry.VersionFile) }
    SaveStringToFile(Target + '\version.txt', '{#EdenVersion}' + #13#10, False);
    DeleteFile(Zip);
    Log('Eden: installed into ' + Target);
  except
    Log('Eden: NOT installed – ' + GetExceptionMessage());
    if not WizardSilent() then
      MsgBox(FmtMessage(CustomMessage('EdenFailed'), [GetExceptionMessage()]), mbError, MB_OK);
  end;
  WizardForm.FilenameLabel.Caption := '';
end;

{ After the files: Eden (optional download), then the language of a first install.
  First install in English: RetroHub starts in English (data\config\settings.json; missing keys get RetroHub's
  defaults). An existing settings file – the player's own choice – is never touched. }
procedure CurStepChanged(CurStep: TSetupStep);
var
  ConfigDir, Settings: String;
begin
  if (CurStep = ssPostInstall) and WizardIsTaskSelected('eden') then
    InstallEden();
  if (CurStep = ssPostInstall) and (ActiveLanguage() = 'english') then
  begin
    ConfigDir := ExpandConstant('{app}\data\config');
    Settings := ConfigDir + '\settings.json';
    if not FileExists(Settings) then
    begin
      ForceDirectories(ConfigDir);
      SaveStringToFile(Settings, '{' + #13#10 + '  "version": 1,' + #13#10 + '  "language": "en"' + #13#10 + '}' + #13#10, False);
    end;
  end;
end;
