# RetroHub – Architektur & technische Entscheidungen

Stand: 2026-11-09 · Version 0.1 (in Entwicklung)

## 1. Ergebnis der MonHub-Analyse

MonHub (`MonHub`) ist eine C#/.NET 10 + Avalonia 11.3 Desktop-App (~15.000 Zeilen).
Die vollständigen Analyseberichte liegen in `docs/analysis/` (ui-shell, library-import, emulators-launch,
config-setup, domain-rest, research). MonHub wurde **nur gelesen, nicht verändert**.

### Übernommen bzw. weiterentwickelt

| MonHub-Bestandteil | Wo | In RetroHub |
|---|---|---|
| **Fenster-Embedding-Engine** (SetParent, Style-Chirurgie, Frame-Push-out für Qt, Worker-Thread, Lade-Maske per leerer Window-Region, WM_NULL-Probe, Fokus-Übergabe, Release) | `GameWindow.cs`, `GameScreen.axaml.cs` | Portiert nach `RetroHub.Core/Emulation/Windows`, aber pro Emulator über ein `EmbedProfile` (JSON) statt fest verdrahteter Qt-Erkennung |
| Minimierter Start ohne Fokus (`CreateProcess` + `SW_SHOWMINNOACTIVE`) | `GameWindow.cs` | übernommen, ergänzt um **Job Object** (Kindprozesse werden verfolgt und nie verwaist) |
| Exit-Erkennung, sanftes Beenden (WM_CLOSE) → Kill nach Timeout | `GameScreen.axaml.cs` | übernommen; Kill jetzt über den ganzen Prozessbaum |
| Portable Emulator-Konfiguration (melonDS-TOML, DeSmuME-INI, mGBA `portable.ini`, Azahar `user\`) inkl. „nicht schreiben, solange der Emulator läuft“ | `HubSetup.cs`, `Azahar.cs` | als `IPortableSetup`-Rezepte pro Emulator |
| Atomares Schreiben, Sicherung defekter Konfigs | `SafeFile.cs`, `HubConfig.cs` | `RetroHub.Core/IO/SafeFile.cs` (+ Flush auf Platte, `.bak`) |
| ROM-Header-Prüfungen (GBA/GB/NDS/3DS, verschlüsselter DS-Secure-Area) | `RomInfo.cs`, `Azahar.cs`, `GameLibrary.cs` | als `IRomSniffer`-Plugins, erweitert um NES/SNES/N64/MD/ISO9660/… |
| Token-basiertes Theme (ein Dictionary-Swap, alles `DynamicResource`), Motion-Stufen inkl. „wie Windows“ | `Themes/Base.axaml`, `HubThemes.cs`, `Ambient.cs` | Idee übernommen, neues modernes Design („Nova“) |
| Seiten-Host mit Keep-Alive, Idle-Vorbau, 170-ms-Übergang | `MainWindow.axaml.cs` | übernommen |
| Zeilen-Chunking statt WrapPanel (virtualisiertes Cover-Raster) | `DexPage` | übernommen für die Bibliothek |
| Eigene Titelleiste mit OS-Rahmen (Snap/Schatten bleiben) | `Dialogs.cs` | übernommen |
| Single-Instance + Crash-Logging | `App.axaml.cs` | übernommen |

### Bewusst nicht übernommen

- Pokémon-spezifisches (Save-Parser, Randomizer, Nuzlocke, Dex, Cheats, Sprites unter CC BY-NC).
- Keine Datenbank in MonHub (Bibliothek wird bei jedem Start aus dem Dateisystem gebaut) → RetroHub nutzt SQLite.
- Keine Controller-Navigation in MonHub (XInput nur zum Tastenbelegen) → RetroHub bekommt sie neu.
- Keine Spielzeit-Messung in MonHub (nur aus Pokémon-Saves gelesen) → RetroHub misst Sitzungen.
- Hart codierte Menüleisten-Höhen (`KnownBars`) → Emulatoren werden ohne Menüleisten konfiguriert, Rest per Profil.

Das **Design** orientiert sich zusätzlich an deinem eigenen Batocera-Theme „Nova v2“ (`D:\Retro\theme\es-theme-nova`):
Basis `#0B0A1A`, Flächen `#15142B/#1E1C3A`, Akzentverlauf `#7B61FF → #4C8DFF`, Radien 12/16/24, Auswahl 160 ms ease-out.

## 2. Tech-Stack-Bewertung

| Kriterium | **C# / .NET 10 + Avalonia 12** | Tauri + React | Electron + React |
|---|---|---|---|
| Performance / RAM | gut (~120–200 MB), nativer Renderer (Skia) | sehr gut (WebView2 ~150 MB) | schwach (300–500 MB) |
| Start | ~1 s (self-contained, R2R möglich) | schnell | langsam |
| Modernes UI | gut (Composition-Animationen, eigene Themes) | sehr gut (CSS) | sehr gut |
| **Emulator-Integration (Win32: SetParent, Job Objects, CreateProcess)** | **direkt per P/Invoke, in MonHub erprobt** | in Rust neu zu schreiben | per Node-FFI umständlich |
| Controller | SDL3 (P/Invoke) | Gamepad-API im WebView (fokusabhängig) | wie Tauri |
| Wiederverwendung MonHub | **hoch** | keine | keine |
| Portabilität | self-contained Ordner, keine Installation | WebView2-Runtime nötig | groß |

**Entscheidung:** C# / .NET 10, **Avalonia 12.1.5** (neue App → aktuelle Hauptversion; MonHub-Code wird beim Portieren angepasst),
`Microsoft.Data.Sqlite` (ohne ORM), **SDL3** für Gamepads (inkl. DualShock/DualSense) mit XInput-Fallback, xUnit v3 für Tests.
Keine weiteren schweren Abhängigkeiten.

## 3. Schichten

```
RetroHub.exe (Starter, winzig)  →  app\RetroHub.App.exe
┌────────────────────────────────────────────────────────────────────────┐
│ RetroHub.App (Avalonia)  Shell · Seiten · Theme · Lokalisierung (de)  │
│                          Cover-Raster · Spielansicht (Embedding-Host)  │
├───────────────────────┬────────────────────────────────────────────────┤
│ RetroHub.Input        │ RetroHub.Core                                  │
│  SDL3/XInput → UI-    │  AppPaths (portable) · Config · Log · SafeFile │
│  Aktionen; Exit-Combo │  Catalog: Systeme + Emulatoren (JSON, Plugins) │
│                       │  Library: SQLite-Repo · Scanner · Sniffer      │
│                       │  Emulation: Registry · LaunchPlanner ·         │
│                       │   Portable-Rezepte · SessionService (Job Obj.) │
│                       │   · Win32-Embedder                             │
└───────────────────────┴────────────────────────────────────────────────┘
```

- **Benutzeroberfläche** – nur `RetroHub.App`; kennt Core nur über Schnittstellen.
- **Spielebibliothek** – `Core/Library` (`IGameRepository`, `ILibraryScanner`, `IRomSniffer`).
- **Emulatorverwaltung** – `Core/Catalog` (Definitionen) + `Core/Emulation` (`IEmulatorRegistry`, `ILaunchPlanner`, `IPortableSetup`).
- **Prozesssteuerung** – `Core/Emulation` (`ISessionService`, `IGameSession`, Job Object, Fenster-Embedder).
- **Dateiverwaltung** – `AppPaths`, `SafeFile`.
- **Datenbank** – `Core/Data` (SQLite, WAL, Migrationen).
- **Metadaten** – `Core/Metadata` (lokale Cover, optional libretro-thumbnails; Nintendo Switch über GameTDB).
- **Einstellungen** – `Core/Config` (`settings.json`).

## 4. Erweiterbarkeit (Plugins)

Systeme und Emulatoren sind **JSON-Definitionen** (`app/resources/systems|emulators/*.json`).
Eigene Dateien in `plugins/systems/` bzw. `plugins/emulators/` ergänzen oder ersetzen sie (gleiche `id` = ersetzen).
Ein neues System oder ein neuer Emulator braucht damit **keine Codeänderung**. Code ist nur nötig für
Spezialfälle: ein Portable-Rezept (`IPortableSetup`) oder ein Header-Sniffer (`IRomSniffer`).

## 5. Ordnerstruktur (portabel)

```
RetroHub/
├── RetroHub.exe            Starter → app\RetroHub.App.exe
├── app/                    Programm (.NET-Runtime self-contained, DLLs) – wird bei Updates ersetzt
│   └── resources/          systems/*.json · emulators/*.json · lang/*.json
├── data/
│   ├── database/           retrohub.db (SQLite, WAL)
│   ├── config/             settings.json (+ .bak)
│   ├── metadata/           importierte Metadaten (z. B. gamelist.xml)
│   ├── cache/thumbs/       verkleinerte Cover
│   └── logs/               retrohub-JJJJMMTT.log (14 Tage)
├── games/<system>/         nes, snes, n64, gc, wii, gb, gbc, gba, nds, 3ds, psx, ps2, psp, …, pc
├── emulators/<emulator>/   retroarch, mgba, melonds, desmume, azahar, dolphin, pcsx2, ppsspp, duckstation, …
├── bios/                   BIOS-Dateien (vom Nutzer bereitgestellt, nie mitgeliefert)
├── saves/<system>/         Spielstände (In-Game-Saves)
├── savestates/<system>/    Savestates
├── screenshots/<system>/
├── artwork/covers|backgrounds|logos/<system>/
├── themes/  plugins/  backups/
└── RetroHub.root           (nur im Entwicklungs-Checkout: Marker für den Root)
```

Regeln: Alle gespeicherten Pfade sind relativ zum Root (externe Spieleordner absolut). Fehlende Ordner werden beim
Start angelegt. Originaldateien werden **nie** ohne Bestätigung gelöscht; „Entfernen“ entfernt nur den DB-Eintrag,
„Löschen“ verschiebt nach Bestätigung in den Papierkorb.

**Ausnahmen außerhalb des Ordners** (werden dokumentiert, sobald bestätigt):
- Windows legt für jede App Einträge in `%LOCALAPPDATA%\Microsoft\…` (z. B. Shader-Caches der GPU-Treiber) an – nicht steuerbar.
- Emulatoren ohne Portable-Modus (wird pro Emulator auf der Emulator-Seite angezeigt).
- SDL3/Avalonia schreiben nichts außerhalb.

## 6. Spielstart – drei Modi (ehrliche Bewertung)

1. **Libretro-Core direkt in RetroHub** – technisch möglich (P/Invoke, Software-Framebuffer), aber Frame-Pacing im
   Avalonia-Compositor, Audio-Resampling und Hardware-gerenderte Cores (N64, PSP, …) machen es zu einem eigenen
   Großprojekt (~3–5 Wochen nur für 2D-Cores). **Nicht in Version 1.** Später optional als eigener `RetroHub.Player`
   (SDL3-Fenster, gleiche Prozessisolation wie ein Emulator).
2. **Eingebettet** (`LaunchMode.Embedded`) – das Emulatorfenster wird per `SetParent` in die Spielansicht von RetroHub
   gesetzt. In MonHub **praktisch erprobt** mit mGBA, melonDS, DeSmuME, Azahar. Für RetroHub wird jedes Profil
   erneut getestet; nur getestete Emulatoren bekommen „Embedded“ freigeschaltet.
3. **Vollbild-Übergabe** (`LaunchMode.Fullscreen`, Standard für alle anderen) – RetroHub zeigt einen Übergang, startet den
   Emulator im Vollbild, blendet sich aus und kehrt nach dem Beenden automatisch zurück. Für Dolphin, PCSX2,
   DuckStation, PPSSPP, Cemu usw. – diese erzeugen ihre Render-Fenster neu und lassen sich nicht zuverlässig einbetten.

Zurück zu RetroHub: Emulator beenden, oder **Select+Start halten** / **Guide-Taste** (Exit-Combo, XInput, während des Spiels).

## 7. Controller

SDL3 (natives `SDL3.dll`, zlib-Lizenz) mit eigenen P/Invokes → abstrakte UI-Aktionen (Hoch/Runter/Links/Rechts,
Bestätigen, Zurück, Menü, Suche, Favorit, Seite links/rechts) mit Tastenwiederholung. Während ein Spiel läuft, wird SDL
geschlossen, damit es die Pads der Emulatoren nicht stört; nur die Exit-Combo wird per XInput beobachtet.
Fallback ohne SDL3: XInput (Xbox-kompatible Pads). Tastatur und Maus funktionieren immer.

## 8. Implementierungsplan

| Phase | Inhalt | Status |
|---|---|---|
| 1 Analyse | MonHub analysiert, Stack + Architektur festgelegt | ✅ |
| 2 Grundlage | Solution, Pfade, Logging, SafeFile, Settings, Verträge | ✅ |
| 3 Core | SQLite-Repo, Katalog (Systeme/Emulatoren), Scanner + Sniffer, Emulator-Registry, LaunchPlanner, Portable-Rezepte, SessionService + Job Object + Embedder, Input | in Arbeit |
| 4 UI | Shell, Theme „Nova“, Home, Bibliothek, Konsolen, Spieldetail, Emulatoren, Imports, Einstellungen, Spielansicht | offen |
| 5 Integration & Tests | echte Starts mit RetroArch/mGBA/melonDS/DeSmuME/Azahar + Homebrew-ROMs, Embedding-Test, Controller | offen |
| 6 Erweiterungen | Cover (lokal + libretro-thumbnails optional), Savegame-Verwaltung, Screenshots, Themes | offen |
| 7 Paket | portabler Windows-Ordner (self-contained), Starter-EXE | offen |
