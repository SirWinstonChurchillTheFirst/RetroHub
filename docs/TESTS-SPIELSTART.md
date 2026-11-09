# Spielstart – End-to-End-Tests auf dem echten Desktop

Stand: 2026-11-09 · Branch `feat/gamescreen` · Build: Debug (`src/RetroHub.App/bin/Debug/net10.0/win-x64/RetroHub.App.exe`)

Alle Fälle unten wurden **wirklich ausgeführt**: RetroHub aus dem Worktree, `RETROHUB_ROOT=D:\RetroHub` (echte Emulatoren,
echte Homebrew-ROMs, echte Datenbank `D:\RetroHub\data\database\retrohub.db`), Start per `--launch`. Beendet wurde jedes
Spiel über RetroHubs eigenen Code-Pfad („Spiel beenden“ → `LaunchCoordinator.StopCommand` → `RequestCloseAsync`), nicht
über Tastendrücke. Es wurden **keine** Tasten an fremde Fenster geschickt.

## Testhaken (nur Debug-Builds)

| Schalter | Wirkung |
|---|---|
| `--launch <pfad-oder-id>` | regulärer Schalter (auch in Release): Spiel auflösen, Spieldetail öffnen, starten wie „Spielen“ |
| `--launch-mode embedded\|fullscreen\|windowed`, `--emulator <ref>` | reguläre Überschreibungen (z. B. `retroarch/fceumm`) |
| `--test-quit-after <s>` | nach *s* Sekunden Sitzung: `StopCommand` (derselbe Weg wie der Button „Beenden“) |
| `--test-release-after <s>` | nach *s* Sekunden: `GameScreen.ReleaseToDesktop()` (derselbe Weg wie „Eigenes Fenster“) |
| `--test-exit-after-return <s>` | *s* Sekunden nach der Rückkehr (oder nach einem Start, der nicht möglich war) schließt RetroHub |
| `--test-report <datei>` | JSON-Zeilen mit Zeitstempel (ms seit Start): Plan, Sitzung, Fenster gefunden/übernommen/bereit, Bildprobe im Host, Fokus, Rückkehr |
| `--test-fullscreen-after <s>` | nach *s* Sekunden Sitzung: RetroHubs eigenes Vollbild an (`MainWindow.SetFullscreen`, derselbe Weg wie F11/„Vollbild“) |
| `--test-overlays-after <s>` | nach *s* Sekunden: ein Toast, 2,5 s später ein Dialog, der nach 3 s wieder schließt (Airspace-Prüfung: Loch für Toasts, Maske + Tastaturfokus beim Dialog) |

`TestHooks.Allowed` ist nur in DEBUG-Builds `true`; ein Release-Build ignoriert die `--test-*`-Schalter (Log-Warnung).

Hilfsskripte (nicht im Repo, im Scratchpad der Sitzung): Start + Aufnahmen per Prozess-ID mit `PrintWindow`
(`PW_RENDERFULLCONTENT`, ohne Fokuswechsel) bzw. Bildschirmkopie (`build/tools/capture-window.ps1 -Screen`), Abfrage
der Emulatorfenster (Rechteck, Vordergrund) per `EnumWindows`/`GetForegroundWindow`.

**Hinweis zur Testumgebung:** Der Desktop war während der Tests in Benutzung (Browser auf dem Hauptmonitor 2560×1440,
100 %) und ein weiterer Agent ließ parallel eine eigene RetroHub-Instanz (anderer Worktree, anderer Root) laufen.
Bildschirmkopien zeigen deshalb teilweise fremde Fenster; maßgeblich sind die `PrintWindow`-Aufnahmen der
RetroHub-/Emulatorfenster und die Testberichte. Bildschirmkopien mit privaten Inhalten wurden nicht übernommen.

## Ergebnisse

| Fall | Ergebnis | Kernzahlen (aus dem Testbericht) |
|---|---|---|
| a) GBA · mGBA · **eingebettet** | ✅ funktioniert | Fenster nach 306–463 ms gefunden, Take 11–38 ms, bereit („answers“) 1,53–1,56 s nach Take, Host 1156×792 px (Leiste 21 px gemessen), Bildprobe im Host: 90–92 % nicht schwarz, `parentIsHost=true`; Beenden: WM_CLOSE → Ende nach 0,2–0,3 s, Exitcode 0 |
| b) GBA · mGBA · **Vollbild** | ✅ funktioniert | erstes Fenster nach 0,83 s, RetroHub versteckt (`Window.Hide`); nach Beenden: `shell-shown visible=true active=true foreground=true` |
| c) NES · RetroArch fceumm · **Vollbild** | ✅ funktioniert | Fenster nach 0,82 s, RetroArch Vordergrund, 0,0 2560×1440; Rückkehr `foreground=true` |
| d) NES · RetroArch fceumm · **eingebettet** | ✅ funktioniert (nicht als Standard freigeschaltet, s. u.) | Fenster nach 150–158 ms, Take 294–310 ms (RetroArch beschäftigt), bereit 1,52–1,54 s nach Take, Host 1055×792 (4:3), Bild im Host 92 % nicht schwarz, `parentIsHost=true`, `keyboardFocusOnGame=true` |
| e) GBC · RetroArch gambatte · **Fenster** | ✅ funktioniert | RetroArch-Fenster 496×491 sichtbar im Vordergrund, RetroHub-Overlay „Läuft gerade“ mit „Spiel beenden“ |
| f) fehlender Core (`retroarch/nestopia`) | ✅ Problem wird gezeigt, kein Absturz | Plan `canLaunch=false`, Dialog „Start nicht möglich“ mit Pfad zum `cores`-Ordner; RetroHub beendet sich danach regulär (Exitcode 0) |
| Zusatz: zweite Instanz mit `--launch` | ✅ | die laufende Instanz übernimmt die Anfrage (GB, gambatte, Fenster); Anfragedatei wird gelöscht |
| Zusatz: Absturz von RetroHub während des Spiels | ✅ | Emulator läuft weiter, nächster Start zeigt „uCity läuft noch“, Beenden funktioniert, `session.json` danach gelöscht |
| Zusatz: „Eigenes Fenster“ (RetroArch) | ✅ nach Korrektur | Fenster mit Rahmen 1071×831 zurück auf dem Desktop, im Vordergrund; Sitzung läuft mit Overlay weiter |

**NDS/3DS:** Es liegen keine NDS- oder 3DS-ROMs vor (auch keine Homebrew), daher wurden melonDS, DeSmuME und Azahar
**nicht** getestet. Ihre Profile (u. a. `ReadyTitleContains=" | "` für Azahar, `NudgeOnMove`/`OffUiThread=false` für
DeSmuME) sind nur durch Unit-Tests der Bereitschaftsregeln abgedeckt.

### a) GBA mit mGBA, eingebettet

Befehl: `--launch "games/gba/240p Test Suite (Homebrew).gba" --launch-mode embedded --test-quit-after 12 --test-exit-after-return 5`

Läufe a1–a4 (plus ein automatisierter Lauf im Interactive-Test über einen temporären Root). Beobachtet:
- Ladeansicht mit Cover, „„240p Test Suite“ startet“, „mGBA lädt das Spiel …“, animierter Balken; der Host ist
  währenddessen maskiert (`spielstart-a-eingebettet-laden.png`).
- Nach ~2 s: mGBA-Fenster im Host, Leiste oberhalb mit Titel, Emulator, Spielzeit, „Vollbild“, „Eigenes Fenster“,
  „Beenden“ (`spielstart-a-mgba-eingebettet.png`). mGBAs eigene Menüleiste (Datei/Emulation/…) ist Teil des
  Clientbereichs und bleibt sichtbar; die 21 px wurden gemessen und in die Seitenverhältnis-Rechnung einbezogen.
- Rückkehr: Spieldetail mit „Willkommen zurück – Diese Sitzung: unter 1 Min.“, „Spielen“ fokussiert,
  Starts hochgezählt (`spielstart-a-rueckkehr.png`).
- **Fehler gefunden und behoben:** Die erste Fassung des Testberichts las die Pixel per `GetPixel` direkt vom
  Bildschirm-DC (1024 Aufrufe ≈ 7 s), was den UI-Thread blockierte (Spielzeitanzeige stand still). Jetzt wird das
  Fenster einmal in einen Speicher-DC kopiert (`PrintWindow`) – keine Hänger mehr (`fits 28`, Uhr läuft).
- Fokus: In den Läufen aus der Konsole war RetroHub nicht das aktive Fenster (Start aus einem Hintergrundprozess);
  dann gibt RetroHub den Fokus bewusst **nicht** weiter. Im Interactive-Test (RetroHub im Vordergrund) meldete der
  Bericht `foregroundIsGame=true, keyboardFocusOnGame=true`.
- Im frischen Root des Interactive-Tests wurden die Leisten mit (0,0) gemessen (Host dann exakt 3:2, 1188×792);
  mit der Konfiguration in `D:\RetroHub` (21,0). Ursache nicht untersucht – in beiden Fällen war das Bild vollständig.

### b) GBA mit mGBA, Vollbild

Befehl: `--launch … --launch-mode fullscreen --test-quit-after 12 --test-exit-after-return 5`
- Übergang „Spiel wird gestartet“ → nach 0,83 s erstes mGBA-Fenster → `SetForegroundWindow(emu)` → `Window.Hide()`.
- mGBA im Vollbild (`spielstart-b-mgba-vollbild.png`, `PrintWindow` des mGBA-Fensters).
- Nach Beenden: RetroHub wieder sichtbar, aktiv, Vordergrund; Spieldetail, Toast, „Spielen“ fokussiert
  (`spielstart-b-rueckkehr.png`).
- **Fehler gefunden und behoben (Lauf b1):** `Window.Opened` wird nach `Hide()`/`Show()` erneut ausgelöst. Dadurch
  startete der `--launch`-Auftrag nach der Rückkehr **ein zweites Mal** (und der Startscan lief erneut). Die
  Start-Handler in `App` und `MainWindow` laufen jetzt nur noch beim ersten `Opened`. Lauf b2 bestätigt die Korrektur.

### c) NES mit RetroArch (fceumm), Vollbild

- Fenster nach 0,82 s, RetroArch ist Vordergrundfenster mit 0,0 2560×1440 (Abfrage per `GetForegroundWindow`).
- Bildschirmkopie nach 8 s zeigt das 240p-Test-Suite-Bild im Vollbild (`spielstart-c-retroarch-vollbild.png`); nach
  4 s war RetroArch noch schwarz (lädt).
- **Unsicherheit:** In Lauf c2 zeigten die Bildschirmkopien nach 5 s/9 s/12 s den Browser statt RetroArch, obwohl der
  Bericht die Übergabe meldet. In den Läufen c3/c4 war RetroArch nachweislich Vordergrund und auf der Aufnahme. Ob c2
  ein Problem der Aufnahme (GDI-Kopie eines Vollbild-Swapchains) oder ein Vordergrundproblem war, ist **nicht
  geklärt**. `PrintWindow` auf das Vollbild-RetroArch lieferte nur Schwarz und blockierte ~6 s – nicht verwenden.
- Rückkehr wie bei b) (`spielstart-c-rueckkehr.png`), Beenden per WM_CLOSE in 0,23 s, Exitcode 0.

### d) NES mit RetroArch (fceumm), eingebettet – ehrliche Bewertung

| Frage | Beobachtung |
|---|---|
| Fenster gefunden? | ja, nach 150–158 ms (Titel zuerst „RetroArch“, nach dem Laden „RetroArch FCEUmm (SVN) e0f3086“) |
| Bild im Host? | ja (`spielstart-d-retroarch-eingebettet.png`), 4:3-Host 1055×792, Bildprobe 92 % nicht schwarz, `parentIsHost=true` |
| Eingabefokus? | RetroArch holt sich beim Start selbst den Vordergrund – obwohl es minimiert/ohne Aktivierung gestartet wird. Das eingebettete Kindfenster war `GetForegroundWindow()` und hatte den Tastaturfokus (`keyboardFocusOnGame=true`). Tasten wurden **nicht** gesendet (Sicherheitsregel), echtes Spielen per Tastatur ist also nicht nachgewiesen. |
| Rückkehr? | ja, WM_CLOSE → Ende nach 0,24 s, Exitcode 0, Spieldetail. Da RetroArch den Vordergrund hatte, war RetroHub danach zunächst **nicht** aktiv (`active=false`); behoben: hatte das eingebettete Spiel den Vordergrund, aktiviert sich RetroHub nach dem Ende selbst. |
| „Eigenes Fenster“? | funktioniert; das Fenster landete zuerst **hinter** RetroHub (der Embedder aktiviert nur, wenn RetroHub selbst Vordergrund war). Behoben: nach dem Button bekommt das freigegebene Fenster den Vordergrund (Lauf d3: RetroArch Vordergrund, 744,333 1071×831 mit Rahmen). |

Fazit: RetroArch-Embedding funktionierte in 3 von 3 Läufen (d1–d3) technisch. **Die Katalog-JSON wurde nicht
geändert** (Standard bleibt Vollbild); nicht geprüft sind: Spielen mit Tastatur/Controller im eingebetteten Zustand,
Cores mit Hardware-Rendering (N64/PSX), Shader, RetroArch-Menü im Host, Größenänderung des Fensters. Empfehlung an
den Lead: erst nach einem Test mit echter Eingabe und einem HW-Core freigeben.

### e) GBC mit RetroArch (gambatte), Fenster

`--launch "games/gbc/uCity (Homebrew).gbc" --emulator retroarch/gambatte --launch-mode windowed`
- RetroArch-Fenster 496×491 bei 338,366, Vordergrund, uCity-Titelbild (`spielstart-e-gambatte-fenster.png`).
- RetroHub zeigt „Läuft gerade · uCity · RetroArch · gambatte · Fenster“, Hinweis, „Spiel beenden“ fokussiert
  (`spielstart-e-overlay.png`). Beenden per WM_CLOSE in 0,22 s.

### f) Fehlender Core

`--launch "games/nes/…nes" --emulator retroarch/nestopia` → Planner: `CoreMissing` (blockierend). Dialog
„Start nicht möglich“ mit dem Hinweis auf Online-Updater und den Pfad `…\RetroArch-Win64\cores`
(`spielstart-f-core-fehlt.png`). Kein Prozessstart, keine Ausnahme, Exitcode 0.

### Zusätze

- **Zweite Instanz:** RetroHub A lief ohne Spiel; `RetroHub.App.exe --launch "D:\RetroHub\games\gb\240p Test Suite (Homebrew).gb" --emulator retroarch/gambatte --launch-mode windowed`
  endete sofort (Exitcode 0), A startete das Spiel 3,7 s nach dem eigenen Start (Datei in `data/cache/requests/`,
  danach gelöscht).
- **Wiederaufnahme nach Absturz:** A startete uCity (Fenster), A wurde per `Process.Kill` beendet; RetroArch lief
  weiter, `data/cache/session.json` blieb. B zeigte beim Start das Overlay „uCity läuft noch“ + Toast
  (`spielstart-wiederaufgenommen.png`), Beenden nach 7 s, Sitzung mit 17,7 s gespeichert, `session.json` gelöscht.
  Behoben dabei: der Emulatorname fehlte bei wiederaufgenommenen Sitzungen (kommt jetzt aus `GameSession.EmulatorId`).

## Spielzeit in der Datenbank

Gelesen mit `sqlite3` (Python, nur lesend) aus `D:\RetroHub\data\database\retrohub.db` nach den Läufen:

| Spiel | System | Sitzungen | Sekunden gesamt |
|---|---|---|---|
| 240p Test Suite | gba (mGBA) | 6 | 75 |
| 240p Test Suite | nes (RetroArch) | 7 | 100 |
| uCity | gbc (RetroArch) | 2 (Fall e + Wiederaufnahme-Test) | 32 (14 + 18) |
| 240p Test Suite | gb (RetroArch) | 1 | 8 |

Jede Sitzung mit Exitcode 0. Die Werte stimmen mit den `session-ended`-Sekunden der Berichte überein.

## Automatisierte Tests

`dotnet test --project tests/RetroHub.App.Tests` – 91/91 bestanden (nach dem Review; vorher 88/88); `dotnet test --project tests/RetroHub.Core.Tests` – 606/606 bestanden. Neu:
- `GameScreenLogicTests` (14): Seitenverhältnis-Fit mit Leisten und 150 %-Skalierung, Zufallsprüfung (500 Fälle),
  2-Stichproben-Regel der Leisten, Bereitschaft (5 Antworten + 1,5 s, Titelregel Azahar, 30-s-Grenze, neuer Take),
  Fenstersuche (64 px, 60 s), Auto-Ausblenden der Leiste.
- `CommandLineTests` (12): `--launch`/`--launch-mode`/`--emulator`, `=`-Form, Fehler, Testschalter, Pfadkandidaten,
  Weiterleitungsdateien.
- `LaunchFlowTests` (10, headless mit gescriptetem Session-Service): Vollbild versteckt/zeigt das Fenster, Vollbild ohne
  Fenster behält das Overlay, eingebettet → Spielansicht → Freigabe, Fenster-Modus, Exit-Combo (an/aus),
  „Hart beenden?“ mit „Weiter warten“ zuerst, versteckt → Fenster für die Frage sichtbar, Wiederaufnahme, CLI-Start.
- `GameScreenInteractiveTests` (Category=Interactive): startet die echte App über einen temporären Root mit einer
  Kopie von mGBA und der GBA-ROM außerhalb der Bibliothek (Erkennung + Hinzufügen), eingebettet, prüft Bericht und
  Datenbank. Bestanden (Fenster nach 457 ms, bereit nach 2,0 s, Bild im Host 92 %, Spielzeit 9 s gespeichert).

## Review (unabhängige Prüfung, 2026-11-09)

Neu ausgeführt mit dem Build des Reviews (`RETROHUB_ROOT=D:\RetroHub`, Aufnahmen nur per Fensterkopie von RetroHub):

| Lauf | Ergebnis |
|---|---|
| a1 mGBA eingebettet | Fenster nach 459 ms, bereit 1,54 s nach Take, Bild 92 % nicht schwarz, `parentIsHost=true`, Tastaturfokus im Spiel; Beenden → Ende nach 0,2 s, Exitcode 0, RetroHub sichtbar + aktiv |
| a2 mGBA eingebettet + Overlays + RetroHub-Vollbild | Toast sichtbar **über** dem Spiel (Loch 962,649–1156,752 im Host, `spielstart-a-toast-ueber-spiel.png`); Dialog: Spiel maskiert, Tastaturfokus von mGBA zurück zu RetroHub (`keyboard-back ok=true`), nach dem Schließen wieder im Spiel (`spielstart-a-dialog-maskiert.png`); F11-Pfad: Host 2128×1440 (3:2 + 21 px mGBA-Menü), Leiste nach 2 s ausgeblendet (`spielstart-a-retrohub-vollbild.png`) – damit erstmals auf dem Desktop geprüft |
| a3 (Endstand) | wie a1/a2, Exitcode 0 |
| d1 RetroArch fceumm eingebettet | Fenster nach 151 ms, Take 292 ms, bereit 1,63 s nach Take, Bild im 4:3-Host 1055×792, Rückkehr aktiv |
| d2 RetroArch eingebettet → „Eigenes Fenster“ | 1,5 s nach der Freigabe ist RetroArch Vordergrund (`release-foreground`: PID = Spiel) |
| c1 RetroArch fceumm Vollbild | Fenster nach 0,86 s, RetroHub versteckt; nach Beenden `visible/active/foreground=true` (nur Bericht, keine Bildschirmkopie) |

Gefundene und behobene Fehler:
- **„Hart beenden?“ blieb offen**, wenn das Spiel sich währenddessen doch selbst beendete (Frage zu einem Spiel, das es
  nicht mehr gibt). Die Frage schließt jetzt beim Sitzungsende selbst (Regressionstest).
- **Toasts unter dem eingebetteten Spiel unsichtbar** (Airspace): der Host bekommt jetzt nur dort ein Loch, wo der
  Toast-Stapel liegt; das Spiel bleibt drumherum sichtbar.
- **Tastatur bei Dialogen über dem Spiel**: der Win32-Fokus blieb im (maskierten) Emulator, Enter/Pfeile gingen ans
  unsichtbare Spiel. RetroHub holt den Fokus jetzt beim Maskieren zurück und gibt ihn danach dem Spiel wieder.
- **„Eigenes Fenster“ / Aufgeben**: hatte das Spiel zuvor den Vordergrund, aktivierte sich RetroHub beim Abbau der
  Spielansicht selbst wieder und konnte dem gerade freigegebenen Fenster den Vordergrund wegnehmen (bei echtem
  Mausklick, wenn RetroHub die letzte Eingabe bekam). Jetzt nur noch beim Spielende. Hinweis: im Testlauf d2 hatte
  RetroArch schon vorher den Vordergrund, der Lauf beweist also nur, dass es mit der Korrektur funktioniert, nicht dass
  es vorher scheiterte.
- **Liegen gebliebene Startanfragen** (zweite Instanz, während die erste gerade schloss) hätten bei einem späteren
  zweiten Start plötzlich ein Spiel gestartet; Anfragen älter als 2 Minuten werden jetzt verworfen (Regressionstest).

## Bekannte Einschränkungen

- Das Auto-Ausblenden der Leiste im RetroHub-Vollbild ist auf dem Desktop nur im Zustand „ausgeblendet“ gesehen
  (kein Mauszeiger an der Oberkante bewegt – keine synthetischen Eingaben).
- Das Loch für Toasts ist ein Rechteck um den ganzen Toast-Stapel (+8 px); dort ist das Spiel kurz verdeckt.
- Die Exit-Combo (XInput) konnte ohne Controller nicht real ausgelöst werden; geprüft ist der Weg ab `Triggered`.
- `Foreground.BringToFront` (Rückkehr aus dem Vollbild) nutzt als letzten Ausweg einen synthetischen Alt-Tastendruck;
  in allen Läufen genügte der erste Versuch (`foreground=true`).
- Ein Fenster, das erst nach dem 60-s-Aufgeben erscheint, bleibt minimiert in der Taskleiste.

## Echte Sammlung (E:\Spiele, nur lesend) – 09.11.2026

Externer Bibliotheksordner `E:\Spiele` (12 Systeme, Ordnernamen wie „Game Boy Advance“, „Nintendo 64“,
„Wii U“). Vorher/nachher wurden Größe und Änderungszeit aller 128 Dateien/Ordner in der Sammlung verglichen: **unverändert**.

**Scan:** 56 Spiele in 2,9 s, alle dem richtigen System zugeordnet; `Updates/` und `DLC/` (Switch) übersprungen;
Wii-U-`.key`-Dateien werden still ignoriert. Sprach-Tags wie „(Deutsch)“, „(Englisch, NTSC)“ werden als Region gespeichert
und unter dem Titel angezeigt („SNES · Deutsch“), damit die neun Fassungen von „A Link to the Past“ unterscheidbar sind.

| System | Spiel | Emulator / Modus | Ergebnis |
|---|---|---|---|
| SNES | A Link to the Past (Deutsch/Englisch) | RetroArch snes9x, Vollbild | ✅ Bild (Nintendo-Intro), Rückkehr, Spielstand in `saves/snes` |
| N64 | Ocarina of Time | RetroArch mupen64plus_next (OpenGL), Vollbild | ✅ läuft (RetroArch-Log, interner RetroArch-Screenshot zeigt das N64-Logo; im Fenstermodus Titelbild sichtbar). Die Bildschirmaufnahme selbst sieht OpenGL-Vollbild nicht |
| GBA | The Minish Cap | mGBA, eingebettet | ✅ Bild im Host, Tastaturfokus auf dem Spiel |
| NDS | Phantom Hourglass | melonDS, eingebettet | ✅ Bild im Host |
| 3DS | A Link Between Worlds, Ocarina of Time 3D | Azahar, eingebettet | ✅ 60 FPS, Bild im Host (die Dumps sind entschlüsselt) |
| GameCube | The Wind Waker | Dolphin 2609b, Vollbild | ✅ nach Fix (s. u.), Fenstertitel „Direct3D 11 … (GZLP01)“, Vordergrund, Rückkehr |
| Wii | Skyward Sword | Dolphin, Vollbild | ✅ Bild (Wii-Remote-Hinweis), Rückkehr |
| Wii U | The Wind Waker HD (.wux) | Cemu 2.6, Vollbild | ❌ Cemu: „Could not decrypt title. Make sure that keys.txt contains the correct disc key“ – die Disc-Keys liegen als `.key` neben den Dumps, sind aber nicht in Cemus keys.txt |
| Switch | – | – | nicht testbar (kein Emulator installiert) |

**Gefundene und behobene Fehler:**
- Dolphin öffnete das Vollbild auf dem **Zweitmonitor**, während RetroHub sich auf dem Hauptmonitor versteckte.
  Neu: `MonitorPlacement` holt das Emulatorfenster bei der Vollbild-Übergabe auf RetroHubs Monitor (Log:
  „is on another monitor (-1920,468 1920x1080) – moving it…“; danach Dolphin 2560×1440 auf dem Hauptmonitor).
- Eingebettete Emulatoren zeigten ihre **Menü-/Statusleisten** (mGBA, melonDS, Azahar). Der Host ist jetzt nur so groß wie
  das Bild, die Leisten liegen außerhalb und werden abgeschnitten („Eigenes Fenster“ zeigt das volle Emulatorfenster).
- RetroArch legte Spielstände externer Spiele in `saves/<Spielordner>` ab; jetzt direkt `saves/<System>`.
- Cemu schrieb ohne Rezept nach `%APPDATA%\Cemu`; jetzt portabel (`emulators/cemu/Cemu_2.6/portable`).
- Weitergeleitete Startanfragen derselben Millisekunde wurden zufällig sortiert (flackernder Test).

**Bekannt / offen:**
- Cemu: zeigt eine Fehlermeldung statt sich zu beenden; „Beenden“ in RetroHub greift erst, wenn Cemu reagiert – der
  Testtreiber hat RetroHub nach 90 s abgebrochen, der Neustart hat die Sitzung per Absturz-Wiederherstellung verbucht (90 s).
- Dolphin zeigt auf der Emulatoren-Seite keine Version („Version unbekannt“).
- Bildschirmaufnahmen (GDI) sehen OpenGL-/D3D-Vollbild teils nicht; für Tests: Fenstermodus oder RetroArch-eigene Screenshots.
