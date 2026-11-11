# RetroHub – Anleitung

Dein Retro-Spielzentrum: alle deine Spiele von Konsolen, Handhelds und PC in einem Fenster – mit Maus,
Tastatur oder Controller. Die Emulatoren sind schon dabei und eingerichtet. Spiele, BIOS-Dateien, Keys und
Firmware sind **nicht** dabei – die musst du selbst haben, von deinen eigenen Geräten und Spielen.

## Erster Start

1. RetroHub starten: Desktop-Symbol, Startmenü oder „RetroHub“ in die Windows-Suche tippen.
2. Spiele hineinlegen (siehe unten). RetroHub findet sie beim nächsten Scan von selbst.
3. Spiel auswählen und starten. **Select + Start gedrückt halten** bringt dich aus jedem Spiel zurück zu
   RetroHub (wie lange, stellst du unter *Einstellungen › Zurück zu RetroHub* ein).
4. Im Spiel selbst speichern nicht vergessen!

## Wohin mit den Spielen?

- **Ein Ordner pro System:** `games\<System>\`, z. B. `games\gba\`, `games\snes\`, `games\ps1\`,
  `games\switch\`. Die Ordner legt RetroHub selbst an. Unterordner sind erlaubt.
- **Oder deine vorhandene Sammlung:** *Importe › Ordner hinzufügen* und den Ordner wählen (z. B. eine externe
  Festplatte). Deine Dateien bleiben, wo sie sind – RetroHub verändert Spieldateien nie.
- Disc-Spiele mit mehreren Dateien (`.cue` + `.bin`) zusammen in einen Ordner legen; mehrere Discs eines Spiels
  per `.m3u`.

## Was braucht welche Konsole?

**Das Wichtigste:** Unter *Einstellungen › Firmware & Keys* steht für jede Konsole, was da ist und was fehlt.
Mit **„Firmware & Keys importieren“** wählst du deine Dateien (einzeln, mehrere oder als `.zip`) – RetroHub
erkennt, was es ist (am Inhalt, an der Prüfsumme, am Namen und an der Größe), und legt es genau dorthin, wo der
Emulator es braucht. Ein ganzer Ordner geht mit „Ordner importieren“. Mit dem Controller: Dateien vorher in
`bios\import\` legen und **„Aus bios/import übernehmen“** wählen. Deine Originale werden nur gelesen. Eine
ersetzte ältere Datei bleibt als `….retrohub-old-<Zeit>` liegen.

| Konsole | Emulator | Braucht | Dateien (Name) | Landet in |
|---|---|---|---|---|
| Game Boy, Game Boy Color | mGBA | nichts | – | – |
| Game Boy Advance | mGBA | nichts (BIOS optional: Startlogo) | `gba_bios.bin` | `bios\` |
| Nintendo DS | melonDS, DeSmuME | nichts (BIOS optional; manche verschlüsselte ROMs starten nur damit) | `bios7.bin`, `bios9.bin`, `firmware.bin` | `bios\` |
| Nintendo 3DS | Azahar | nichts – nur **entschlüsselte** Spiele (`.3ds`, `.cci`, `.cxi`) | – | – |
| GameCube, Wii | Dolphin | nichts | – | – |
| Wii U | Cemu | nur für verschlüsselte Disc-Abbilder (`.wux`, `.wud`): die Disc-Keys | `keys.txt` oder `<Spiel>.key` | `bios\wiiu\keys.txt` und `emulators\cemu\portable\keys.txt` |
| Nintendo Switch | Eden | **Keys und Firmware deiner eigenen Switch** | `prod.keys`, `title.keys`, Firmware-Ordner oder `.zip` mit `.nca`-Dateien | Keys: `bios\switch\` und `emulators\eden\user\keys\`; Firmware: `emulators\eden\user\nand\system\Contents\registered\` |
| NES (Famicom Disk System) | RetroArch | nur für `.fds`-Spiele | `disksys.rom` | `bios\` |
| PlayStation | RetroArch (PCSX ReARMed) | läuft ohne; mit BIOS genauer | `scph5501.bin` (USA), `scph5502.bin` (EU), `scph5500.bin` (JP), `scph1001.bin` | `bios\` |
| PlayStation 2 | RetroArch (LRPS2) | **PS2-BIOS** (beliebiger Name, z. B. `SCPH-70004…BIN`) | eigener Dump | `bios\pcsx2\bios\` |
| PSP | RetroArch (PPSSPP) | den Datenordner `PPSSPP` (kein BIOS) | Ordner „assets“ aus dem PPSSPP-Download | `bios\PPSSPP\` |
| Mega-CD / Sega CD | RetroArch (Genesis Plus GX) | BIOS der Region des Spiels | `bios_CD_U.bin`, `bios_CD_E.bin`, `bios_CD_J.bin` | `bios\` |
| Saturn | RetroArch (Beetle Saturn) | eines der Saturn-BIOS | `sega_101.bin` (JP), `mpr-17933.bin` (USA/EU) | `bios\` |
| Dreamcast | RetroArch (Flycast) | nichts (BIOS optional) | `dc_boot.bin`, `dc_flash.bin` | `bios\dc\` |
| PC Engine CD | RetroArch (Beetle PCE Fast) | nur für CD-Spiele | `syscard3.pce` | `bios\` |
| Atari Lynx | RetroArch (Handy) | nichts (optional) | `lynxboot.img` | `bios\` |
| Atari 7800 | RetroArch (ProSystem) | nichts (optional: Startlogo) | `7800 BIOS (U).rom` | `bios\` |
| MSX | RetroArch (blueMSX) | die Datenordner `Machines` und `Databases` | aus dem blueMSX-Download | `bios\Machines\`, `bios\Databases\` |
| Arcade (auch Neo Geo) | RetroArch (MAME 0.282) | Romsets passend zu **MAME 0.282**; Neo-Geo-Spiele zusätzlich das BIOS-Set | `neogeo.zip` | `bios\mameios\` (oder neben die Spiele) |

Alle anderen Systeme (SNES, N64, Mega Drive, Master System, Game Gear, 32X, Atari 2600, Virtual Boy,
Neo Geo Pocket, WonderSwan) brauchen nichts.

### Nintendo Switch: Keys und Firmware

Eden startet Switch-Spiele nur mit den **Keys (`prod.keys`, meist auch `title.keys`) und der Firmware deiner
eigenen Switch**. Beides musst du selbst von deiner Konsole sichern – RetroHub liefert es nie mit und lädt es
nie herunter. So geht's:

1. *Einstellungen › Firmware & Keys › Nintendo Switch › Importieren*: `prod.keys` und `title.keys` wählen.
2. *Nintendo Switch: Firmware-Ordner*: den Ordner mit den `.nca`-Dateien deiner Firmware wählen – oder die
   Firmware als `.zip` über „Firmware & Keys importieren“.
3. Fertig. Ist Eden noch nicht installiert, wartet die Firmware in `bios\switch\firmware\` und kommt beim
   ersten Start von Eden an ihren Platz. Eine ältere Firmware in Eden wird nicht gemischt, sondern als
   `registered.retrohub-old-<Zeit>` beiseitegelegt.

### Wii U: Disc-Keys

Entschlüsselte Spiele (Ordner mit `code\`, `content\`, `meta\` oder `.wua`) laufen ohne Keys. Für
verschlüsselte Disc-Abbilder (`.wux`, `.wud`) braucht Cemu den Disc-Key deiner eigenen Disc: Leg die passende
`.key`-Datei mit demselben Namen neben das Abbild (`Spiel.key` neben `Spiel.wux`) – RetroHub trägt sie beim
Start ein – oder importiere deine `keys.txt` bzw. `.key`-Dateien unter *Firmware & Keys*. Doppelte Keys werden
nicht doppelt eingetragen.

## Alles in einem Ordner

Alles liegt im RetroHub-Ordner (normalerweise `C:\Users\<Name>\RetroHub`). Nichts landet in `%APPDATA%`,
in „Dokumente“ oder in einer anderen Emulator-Installation.

| Ordner | Was drin ist |
|---|---|
| `games\<System>\` | deine Spiele |
| `bios\` | BIOS, Keys, Firmware, Datenordner – alles, was du importierst (gut zum Sichern) |
| `bios\switch\`, `bios\wiiu\`, `bios\pcsx2\bios\`, `bios\import\` | Switch-Keys, Wii-U-Keys, PS2-BIOS, Eingang für den Controller |
| `saves\<System>\` | Spielstände (mGBA, melonDS, DeSmuME, RetroArch); 3DS: `saves\3ds\nand\` und `saves\3ds\sdmc\` |
| `savestates\<System>\` | Savestates (mGBA, melonDS, DeSmuME, RetroArch) |
| `screenshots\<System>\` | Screenshots (mGBA, DeSmuME, Azahar, RetroArch) |
| `emulators\dolphin\User\` | GameCube-Memory-Cards, Wii-NAND, Dolphins Savestates, Screenshots, Einstellungen |
| `emulators\cemu\portable\` | Wii-U-Spielstände (`mlc01`), `keys.txt`, Einstellungen |
| `emulators\eden\user\` | Switch-Spielstände (`nand`), Keys, Firmware, SD-Karte, Einstellungen |
| `emulators\azahar\user\` | Azahars Einstellungen und Savestates |
| `emulators\<Emulator>\` | die Emulatoren selbst mit ihren Einstellungen |
| `artwork\` | Cover und Bilder |
| `data\` | Datenbank, Einstellungen von RetroHub, `data\logs\` |
| `app\` | RetroHub selbst – nicht anfassen |

RetroHub richtet jeden mitgelieferten Emulator vor seinem Start portabel ein und schaltet deren eigene
Update-Prüfungen ab (Dolphin, Cemu, Azahar, Eden, mGBA) – kein Emulator ersetzt seine Dateien selbst; Updates
kommen mit dem RetroHub-Setup. Hast du unter *Emulatoren* ein eigenes Programm außerhalb des RetroHub-Ordners
gewählt, lässt RetroHub es unverändert – es speichert dann weiter dort, wo es immer gespeichert hat.

## Starten: eingebettet oder Vollbild

- **Eingebettet** (mGBA, melonDS, DeSmuME, Azahar): das Spiel läuft im RetroHub-Fenster.
- **Vollbild** (Dolphin, Cemu, Eden, RetroArch): der Emulator übernimmt den Bildschirm.
- Für jedes Spiel kannst du auf seiner Seite den Emulator und den Startmodus wählen (*Eingebettet*, *Vollbild*,
  *Fenster*). RetroHub selbst schaltest du mit **F11** in den Vollbildmodus (*Einstellungen › Im Vollbild
  starten*).

## Controller

- RetroHub bedienst du mit Xbox-, PlayStation- und anderen Controllern: A/Kreuz = Starten, B/Kreis = Zurück,
  X = Favorit, Y = Suche, Schultertasten = Abschnitt wechseln. Ein- und ausschalten unter *Einstellungen ›
  Mit Controller bedienen*.
- **Select + Start halten** = zurück zu RetroHub.
- Die Tasten **im Spiel** stellst du im jeweiligen Emulator ein (RetroArch erkennt gängige Controller von selbst).

## Update und Deinstallieren

- **Update:** einfach das neue Setup ausführen. Programm und Emulatoren werden ersetzt; Spiele, Spielstände,
  `bios\`, Einstellungen und die Daten der Emulatoren bleiben.
- **Deinstallieren:** über Windows „Apps“. Entfernt werden nur Programm und Emulatoren. Deine Spiele,
  Spielstände, Savestates, Screenshots, Cover, `bios\` (Keys, BIOS, Firmware) und die Daten der Emulatoren
  bleiben im RetroHub-Ordner. Brauchst du sie nicht mehr, lösche den Ordner selbst.

## Wenn etwas nicht klappt

- **Spiel startet nicht:** Die Spielseite sagt, was fehlt (Emulator, Core, BIOS, Keys). *Einstellungen ›
  Firmware & Keys* zeigt den Stand je Konsole.
- **„unbekannte Version“:** die Datei hat den richtigen Namen, aber eine andere Prüfsumme als bekannte gute
  Dumps. Oft läuft sie trotzdem; sonst neu sichern.
- **Spiel fehlt in der Bibliothek:** liegt es im richtigen `games\<System>\`-Ordner? *Importe › Jetzt scannen*.
- **Protokolle:** `data\logs\` – bei Fragen die neueste Datei mitschicken. Keys stehen dort nie drin.

## Lizenz

RetroHub ist kostenlos. Es darf weder verkauft noch kommerziell genutzt werden, denn einige mitgelieferte
RetroArch-Cores erlauben nur eine **nicht-kommerzielle** Weitergabe: **Snes9x** (SNES), **Genesis Plus GX**
(Master System, Mega Drive, Game Gear, Mega-CD) und **PicoDrive** (32X). Spenden zur Unterstützung von
RetroHub sind willkommen und erlaubt. Arcade-Spiele laufen mit dem MAME-Core (GPL-2.0-or-later). Die Lizenztexte liegen in den Ordnern der Emulatoren
(`emulators\retroarch\licenses\`). RetroHub ist ein inoffizielles Projekt ohne Verbindung zu Nintendo, Sony,
Sega, Atari oder anderen Herstellern. Nutze nur Kopien von Spielen, BIOS und Keys, die dir gehören.
