# RetroHub Designsprache „Fernsehlicht"

Version 1.0 · Art Direction · Stand 2026-11-09
Gilt für RetroHub.App (Avalonia 12, .NET 10). Nur Dark Mode. Alle Maße beziehen sich auf eine Referenzfläche von 1920×1080 logischen Pixeln und werden über `UiScale` skaliert (siehe 7.0).

---

## 0. Kurzfassung in zehn Sätzen

1. Die Metapher ist **das Spielzimmer um elf Uhr nachts**: Das Zimmer ist dunkel, das einzige Licht kommt vom Röhrenfernseher, und im Widerschein steht die Sammlung im Regal.
2. **Raum**: Der Hintergrund ist ein warmes Braunschwarz und kein Violett. Er wird nur von der Bildfarbe des ausgewählten Spiels eingefärbt.
3. **Regal**: Cover *stehen* auf einer Regalkante. Sie werden nicht in Karten gesteckt und unten bündig eingepasst, nie beschnitten. Unterschiedliche Box-Formate sind dadurch gewollt und wirken wie eine echte Sammlung.
4. **Fächer**: Jedes System hat eine eigene Rückwand. Sie besteht aus einer dunklen Wandfarbe, einer Ära-Textur und einem eigenen Motiv (Modul, Handheld, Disc, Bedienfeld) und enthält keine Logos.
5. **Etiketten und Rücken**: Fehlt ein Cover, entsteht ein gestalteter Spielrücken mit dem vollen Titel und keine Initialen-Kachel.
6. **Licht hat eine Richtung**: von oben bzw. vom Bildschirm. Schatten fallen nur nach unten. Es gibt keine farbigen Glows, kein Glas und keine Verläufe als Deko.
7. **Signaturfarbe ist „Standby-Bernstein" #FFB547**. Sie markiert genau zwei Dinge: die Hauptaktion (Spielen) und den Fokus in gemischten Ansichten. Innerhalb eines Systems übernimmt dessen Lichtfarbe den Fokus.
8. **Typografie wie Spielverpackungen**: Archivo Expanded für Plakattitel, Archivo Condensed für Rücken und Etiketten, Poppins für Fließtext und UI.
9. **Formen sind klein**: Radien 0/2/4/6. Kreise gibt es nur für Controller-Tasten und Statuspunkte.
10. **Das eine große Bewegungsmoment ist der Start**: Das Cover fährt in die Mitte, dann folgt der Röhren-Einschalteffekt. Alles andere ist schnell und ruhig (90/140/220 ms).

---

## 1. Konzept

### 1.1 Die Metapher und was sie entscheidet

| Element im Zimmer | UI-Entsprechung | Konsequenz für jede Entscheidung |
|---|---|---|
| Dunkler Raum | Grundfläche `Raum` #14120F (warm, nie violett) | Es gibt keine Flächen, die „leuchten", ohne dass es eine Lichtquelle gibt |
| Röhrenfernseher | „Bühne" bzw. „Bildschirm": das fokussierte Spiel | Die Hintergrundfarbe kommt **aus dem Bild des fokussierten Spiels**. Wechselt der Fokus, wechselt das Licht im Raum (Überblendung) |
| Regalbretter | 2 px Regalkante mit 1 px Lichtkante und Schatten nach unten | Cover stehen auf einer Linie. Der Fokus wächst **nach oben** vom Regal weg |
| Fächer mit gestrichener Rückwand | Systemwand: Wandfarbe, Ära-Textur, Motiv | Plattform-Identität entsteht über Farbe, Material und Form, nie über Marken |
| Spielrücken und Etiketten | Platzhalter-Cover, System-Etiketten, Aufkleber „Neu" | Fehlende Daten werden zu gestalteten Objekten statt zu Lücken |
| Standby-LED | Bernstein-Punkt in der Wortmarke und als Hauptaktion | Eine einzige Markenfarbe, sparsam eingesetzt |
| Einschalten der Röhre | Starttransition | Der einzige inszenierte Moment der App |

### 1.2 Warum das nicht generisch ist

- **Kein Violett-Blau-Verlauf**: Der Verlauf `#7B61FF→#4C8DFF` entfällt vollständig, auch in Fokusringen, Buttons und Logo. Farbe kommt aus Spielbildern und Plattformen.
- **Keine Karten-Kits**: Die Inhalte stehen auf Regalen oder hängen an Wänden. Panels gibt es nur für echte Overlays (Dialog, Menü).
- **Kein Glas**: Alle Flächen sind opak. Transparenz gibt es nur für Scrims über Bildern.
- **Licht statt Glow**: Die einzige „Leuchte" ist ein Lichtkegel hinter dem fokussierten Objekt. Er hat die Farbe des Lichts in diesem Raum, kommt von oben und wird nie als Rahmen-Glow gezeichnet.
- **Typografie mit Breitenkontrast**: Der Gegensatz zwischen Expanded und Condensed ist das typische Mittel von Spielverpackungen der 90er. Er ersetzt die überall verwendeten ALL-CAPS-Overlines.
- **Meta als Satz**: „Rollenspiel von Game Freak, 2004. Ein Spieler." statt `JAHR / GENRE`-Labels und Mittelpunkt-Ketten. Fehlende Werte verschwinden einfach aus dem Satz.
- **Kein KPI-Dashboard**: Zahlen erscheinen nur dort, wo sie etwas über *dieses* Spiel oder *dieses* System sagen.

### 1.3 Was wir von welcher Referenz nehmen (nur Prinzipien)

| Referenz | Übernommenes Prinzip | Wo in RetroHub |
|---|---|---|
| PS5 Home und Game Hub | Fokus wächst vom gemeinsamen Boden nach oben. Fester Fokus-Anker, die Reihe läuft darunter. Hintergrund folgt dem Spiel | Home-Regale, Konsolen-Karussell, Bühne |
| Xbox 2023 | Interaktion in der unteren Hälfte, Bild oben. Wechsel erst nach Verweilzeit. Dicker, satter Fokusring | Home-Aufteilung, Fernsehlicht-Verzögerung, Fokusring 3 px |
| Steam Big Picture | Eine Bildrolle pro Fläche mit fester Fallback-Kette. Bewusst gestaltete Platzhalter. Kontextabhängige Fußleiste. LB/RB-Tasten sichtbar | Fallback-Kette, Rücken-Platzhalter, Fußleiste, Ticker |
| Nintendo Switch HOME | Ruhige, flache Chrome als Bühne für Bilder. Auf dem Home-Regal ein Label nur für das fokussierte Element. Sofortiges Fokus-Feedback | Home ohne Kachel-Beschriftungen, Fokus in 90 ms |
| Nintendo Classics (NSO) | „Raum pro Plattform" durch Farbfläche. Cover-Wand ohne Zwischenrahmen | Systemseite, Fachrückwand |
| ES-DE Slate | Farbstreifen als Plattform-Signatur | Rückenleiste (Sammlung als proportionale Farbstreifen) |
| Neoretro (Pegasus) | Riesige Jahreszahl als typografisches Element. Farbband pro System. Positionszähler n/N | Jahres-Wasserzeichen im Detail, Ticker, „Spiel 3 von 7" |
| Analogue 3D / Pocket | Medien-Objekt als Identität (Modul statt Logo). Inversions-Auswahl. Haarlinien statt Karten | Motive, Inversions-Fokus in Listen und Einstellungen |
| Daijishō | Systemfarbe färbt die Auswahl im System. Meta als Satz. Ortsangabe in der Fußleiste | Systemlicht-Fokus, Meta-Sätze, Ortssatz |
| RetroArch XMB/Ozone | Eine Grunddauer für alle Bewegungen. Bilder erst nach kurzer Ruhe laden. „Wackeln" an der Grenze | Motion-Tokens, Decode-Verzögerung, Grenz-Feedback |
| Playdate | Gestaltete, typografische Spielkarten. Ritual für Neues. Die Karte wird zum Spiel | Rücken-Platzhalter, Aufkleber „Neu", Starttransition |
| LaunchBox/Big Box | Ebenenstapel Konsolen → System → Spiel. B geht genau eine Ebene zurück und stellt den Fokus wieder her | Navigation |

Bewusst **nicht** übernommen: Logos und Wortmarken, Hardware-Fotos, Trade Dress (Classic-Mini-Skins, rot-blaue Schienen, vierfarbige Knopf-Rauten), Video, Live-Blur, Promo-Kacheln, pulsierende Dauer-Animationen.

---

## 2. Farbsystem

### 2.1 Grundneutrale (warm, opak)

| Token (neu) | Bestehender Key | Hex | Verwendung |
|---|---|---|---|
| Raum-Tief | `RhRoomDeepColor` (neu) | #0A0908 | Vignette, Letterbox, Start-Overlay |
| Raum | `RhBaseColor` | #14120F | App-Grund |
| Regal | `RhSurfaceColor` | #1E1B17 | Overlays, Eingabefelder, Listenzeilen-Hover, Menü-Drawer |
| Regal hell | `RhSurfaceHiColor` | #2A2620 | Hover, gedrückt-Zustand, Knopfflächen sekundär |
| Kante | `RhLineColor` | #3A342B | Regalkante, Trennlinien (opak, keine Alpha-Linien) |
| Lichtkante | `RhLightEdgeBrush` (neu) | #FFF3E0 bei 9 % (#17FFF3E0) | 1 px Oberkante von Regalbrettern und Overlays |
| Papier | `RhText1Color` | #F4ECDF | Primärtext, Fokus-Inversion (15,9 : 1 auf Raum) |
| Text 2 | `RhText2Color` | #C9BFAF | Sekundärtext (10,3 : 1) |
| Text 3 | `RhText3Color` | #948A7A | Tertiärtext, nur ≥ 15 px (5,5 : 1 auf Raum, 5,0 : 1 auf Regal) |
| Tinte | `RhTextOnAccentBrush` | #14120F | Text auf hellen Flächen (Bernstein, Papier, helle Systemfarben) |

Regeln:
- **Kein reines Schwarz und kein reines Weiß** in Flächen (OLED-Blooming, Härte im Fenster). Ausnahme: Schatten verwenden #000000 mit Alpha.
- **Alpha-Weiß-Flächen** (`#14FFFFFF` usw.) entfallen. Hover und Auswahl nutzen opake Werte, damit sie über eingefärbtem Hintergrund nicht kippen.

### 2.2 Signaturfarbe „Standby-Bernstein"

| Rolle | Hex | Kontrast |
|---|---|---|
| Bernstein (Akzent A) | #FFB547 | 10,6 : 1 auf Raum; Tinte darauf 10,6 : 1 |
| Bernstein tief (Akzent B, gedrückt) | #E59A2E | Tinte darauf 8,0 : 1 |
| Bernstein Text (auf Raum) | #FFC877 | für Links und Zahlen-Hervorhebung |

Einsatz, abschließend: (1) Hauptbutton „Spielen" und andere Primäraktionen (`Jetzt scannen`, `Speichern` in Dialogen), (2) Fokusring in gemischten Ansichten (Home, Bibliothek „Alle", Suche), (3) Standby-Punkt in der Wortmarke, (4) Aufkleber „Neu", (5) Fortschrittsbalken. **Nirgendwo sonst.**

**Benutzer-Lichtfarben** ersetzen die bisherigen Akzent-Presets (`ThemeManager.ApplyAccent` bleibt die Pipeline):

| Preset-Id | Name | Hex | Tinte darauf |
|---|---|---|---|
| amber (Standard) | Bernstein | #FFB547 | 10,6 : 1 |
| phosphor | Phosphor | #8FE3A0 | 12,2 : 1 |
| tube | Röhrenweiß | #EDE3D0 | 14,7 : 1 |
| ice | Eisblau | #8CC4FF | 10,2 : 1 |
| cherry | Kirsche | #FF8A80 | 8,2 : 1 |
| neon | Neonrosa | #FF8CC6 | 8,7 : 1 |

### 2.3 Statusfarben

| Status | Hex | Immer kombiniert mit |
|---|---|---|
| Bereit/OK | #8FD694 | Häkchen-Icon |
| Warnung | #FF9A4A | Dreieck-Icon (unterscheidet sich vom Bernstein durch Form *und* Text) |
| Fehler/blockierend | #FF6A5E | Kreuz-Achteck-Icon |
| Favorit | #FFD36A | gefüllter Stern |
| Fenster schließen (Hover) | #C42B1C | bleibt (OS-Konvention) |

Status-Soft-Flächen (für Problem-Hinweise): Farbe bei 14 % auf Regal. Es gibt keine Status-Pills, nur Icon und Text.

### 2.4 Systempaletten

Jedes System bekommt fünf Werte. **Farbe** und **Zweitfarbe** werden von Hand festgelegt, ausgehend vom `accent` in `systems/*.json`. Korrigiert wurde dabei für Kontrast, für den Brief (z. B. SNES warm, Arcade neon) und gegen graue Plattenfarben. Die übrigen Werte werden **zur Laufzeit berechnet** (Klasse `SystemPalette`), damit auch benutzerdefinierte Systeme funktionieren:

- **Wand** = HSL(h, min(s, 0,42)·0,9, 0,105): Fachrückwand unten, Raumfarbe im System
- **Wand hell** = HSL(h, min(s, 0,42), 0,17): Fachrückwand oben
- **Licht** = HSL(h, clamp(s, 0,25, 0,85), 0,72). L steigt in 0,02-Schritten, bis Kontrast zu Wand hell ≥ 4,5 : 1. Verwendet für Fokusring, Etikett-Text und Spotlicht im System.
- **Text auf Farbe** = Papier oder Tinte, je nachdem, was höheren Kontrast hat. Liegt der Wert < 4,5, wird *Farbe* in 4-%-Schritten abgedunkelt (die Tabelle enthält bereits korrigierte Werte).

Die JSON-`accent` bleibt Fallback für Systeme ohne Eintrag (`SystemPalette.From(accent)`).

| System | Farbe | Zweitfarbe | Wand | Wand hell | Licht | Text auf Farbe | Änderung ggü. JSON |
|---|---|---|---|---|---|---|---|
| nes | #C51F1B | #A8A39A | #251111 | #3E1A19 | #ED8482 | Papier 5,0 | dunkler (Kontrast) |
| snes | #6652BE | #BDB7AD | **#1B1820** | **#2B2631** | #A396D9 | Papier 5,1 | Flieder statt Blauviolett, Wand warm überschrieben |
| n64 | #12A05E | #4A4A50 | #11251B | #193E2D | #7FF1BC | Tinte 5,5 | Grün minimal angehoben |
| gb | #8BAC0F | #306230 | #212511 | #363E19 | #DAF47C | Tinte 7,1 | unverändert |
| gbc | #8E3BD0 | #2FB8A6 | #1C1125 | #2D193E | #BD8CE3 | Papier 4,9 | Beere, Petrol als Zweitfarbe |
| gba | #5A46D9 | #A9A2E8 | #131125 | #1E193E | #9589E7 | Papier 5,4 | unverändert |
| gamecube | #6450CC | #F29A3B | #151125 | #21193E | #9D8BE4 | Papier 5,0 | „verspieltes Violett" mit Orange |
| wii | #3FC0EE | #E6EEF2 | #112025 | #19343E | #7CD4F3 | Tinte 8,9 | heller |
| wiiu | #12A0CC | #F0F0F0 | #112025 | #19353E | #7CD7F3 | Tinte 6,2 | — |
| nds | #8FA3B8 | #3C4654 | #151B20 | #222B35 | #A6B7C9 | Tinte 7,2 | Silber-Schiefer statt Grau |
| 3ds | #C21D26 | #2B2B30 | #251112 | #3E191B | #EC8389 | Papier 5,1 | dunkler |
| switch | #C41F2A | #2E2E33 | #251112 | #3E191B | #F17E85 | Papier 5,0 | dunkler, nie Rot+Blau |
| virtualboy | #C8121C | #1A0505 | **#100606** | **#1C0A0B** | #F37C82 | Papier 5,0 | Wand fast schwarz |
| arcade | #FF2E88 | #22E4FF | **#120A14** | **#1E1024** | #F47BAF | Tinte 5,3 | Neon-Magenta statt Bernstein (Kollision mit Marke) |
| pc | #C7B48A | #3FBF6A | #231E12 | #3B311C | #D1C19E | Tinte 9,2 | Beige-Gehäuse + Phosphor statt Windows-Blau |
| atari2600 | #C0702A | #2A1A10 | #251A11 | #3E2A19 | #E5B58A | Tinte 5,0 | Holzton |
| atari7800 | #C8243A | #C0C0C0 | #251113 | #3E191E | #E98693 | Papier 4,8 | — |
| lynx | #F2A21A | #2B2B2B | #251D11 | #3E3019 | #F4C77B | Tinte 8,9 | — |
| pcengine | #F05A28 | #E8E4DA | #251611 | #3E2219 | #F4997B | Tinte 5,5 | — |
| ngp | #2E5DB8 | #D9D4C8 | #111825 | #19263E | #8DAAE2 | Papier 5,3 | — |
| wonderswan | #10A8B5 | #F0E6C8 | #112325 | #193B3E | #7CEAF3 | Tinte 6,5 | — |
| msx | #3368A3 | #D8D2C4 | #111A25 | #192A3E | #94B6DC | Papier 4,9 | dunkler |
| mastersystem | #C42019 | #1A1A1A | #251211 | #3E1B19 | #EE8781 | Papier 5,0 | dunkler |
| megadrive | #1B66B2 | #121212 | #111B25 | #192B3E | #83B8EC | Papier 5,0 | dunkler |
| gamegear | #2D9CDB | #1A1A1A | #111E25 | #19303E | #85C6EA | Tinte 6,1 | — |
| segacd | #4A86D0 | #1A1A1A | #111A25 | #19293E | #8EB3E2 | Tinte 5,0 | — |
| sega32x | #F2AE00 | #1A1A1A | #251F11 | #3E3319 | #F4D27B | Tinte 9,6 | — |
| saturn | #5A5FB8 | #2A2A2E | #111224 | #1A1C3D | #9B9ED4 | Papier 4,8 | — |
| dreamcast | #F26F21 | #EDEBE6 | #251811 | #3E2719 | #F4A87B | Tinte 6,3 | — |
| ps1 | #9AA0B4 | #3A3F4E | #17191E | #252832 | #A6AEC9 | Tinte 7,2 | Grau-Lavendel statt Stahlgrau |
| ps2 | #2C4FD0 | #08090F | **#0C0F1C** | **#141A33** | #899DE6 | Papier 5,7 | „dunkel-futuristisch": Wand fast schwarz-blau |
| psp | #A9B4C8 | #0C0D10 | #151920 | #222935 | #A6B2C9 | Tinte 8,9 | Silber auf Klavierlack |

Fett gedruckte Wandwerte sind manuelle Überschreibungen. Alle anderen Wand-, Wand-hell- und Lichtwerte sind die Ausgabe der Formel (berechnet mit dem Skript `scratchpad/design/tools/palette.py`, Kontraste geprüft; manuelle Wände ebenfalls ≥ 5,5 : 1). Licht erreicht auf Wand hell ≥ 5,5 : 1 und auf Raum ≥ 6,2 : 1.

**Wo Systemfarben auftreten:**
- **Farbe**: Etikett-Balken (4×16 px), Rückenband im Platzhalter, Motiv-Füllung, Ticker-Unterstrich, Rückenleiste. Flächen größer als 120×120 px bekommen nie die volle Farbe, nur Wand und Wand hell.
- **Wand / Wand hell**: Fachrückwand, Raumfarbe auf der Systemseite, Platzhalter-Grund.
- **Licht**: Fokusring und Spotlicht im Systemkontext, Etikett-Text, Zahlen im System-Header.

### 2.5 Fernsehlicht: wie das Spielbild den Raum färbt

Pipeline (Control `RoomBackdrop`, eine Instanz in `MainWindow`, hinter `PageHost`):

1. **Quelle**: fokussiertes Spiel → Snap (`Named_Snaps`) → Titelbild (`Named_Titles`) → Cover → keine. Bis Snaps und Titel in Core geholt werden (M4b), ist es das Cover.
2. **Decode**: auf 48 px Breite (eigener LRU-Cache, 96 Einträge; nutzt `AsyncImageLoader` mit eigenem Bucket). Die Darstellung ist `Image Stretch=UniformToFill` in Fenstergröße mit `RenderOptions.BitmapInterpolationMode="MediumQuality"`. Die bilineare Hochskalierung ergibt eine weiche Fläche, also einen **„Blur" ohne BlurEffect**. Deckkraft 0,46.
3. **Bildlicht** (dominante Farbe): aus einem 8×8-Decode die nach Sättigung gewichtete Mittelfarbe bilden und auf L 0,55–0,70 und S ≤ 0,70 klemmen. Wird pro Spiel im Speicher gecacht (`Dictionary<string, Color>`), später optional als DB-Spalte. Verwendet für Spotlicht und Deckenlicht.
4. **Scrims** (statisch, geteilt, einmal pro App):
   - a) horizontal: Raum bei 94 % (x 0 %) → 76 % (x 50 %) → 52 % (x 100 %)
   - b) vertikal: transparent bis 44 % Höhe → Raum 100 % bei 90 % Höhe
   - c) Vignette: RadialGradient, Zentrum (60 %, 35 %), Radius 85 %, transparent → Raum-Tief bei 70 %
   - d) Deckenlicht: RadialGradient, Zentrum (70 %, 0 %), Radius 60 %, Bildlicht bei 14 % → 0
   - e) Korn: Textur `korn-128.png` bei 3,5 % über allem (auch über Seiten, unter der Fokus-Ebene; ein einziges Element)
5. **Überblendung**: zwei Bildebenen A/B. `DoubleTransition` auf Opacity, 420 ms, `SineEaseInOut`. Der Wechsel startet erst, wenn der Fokus **220 ms** ruht, und nie, solange eine Richtungstaste per Auto-Repeat gehalten wird.
6. **Kein Bild (oder Konsolen- bzw. Systemfokus)**: Wand-hell → Wand vertikal, plus Ära-Textur bei 6 % und Motiv-Silhouette bei 7 % rechts unten. In gemischtem Kontext ohne Fokus: Raum mit einem Deckenlicht in Bernstein bei 5 %.
7. **Stärke pro Seite**: Home und Detail 100 %; Bibliothek 45 % (Grid liegt darüber); Systemseite: Wandfarbe statt Spielbild, Spielbild nur bei 25 % im Header; Konsolen: Wand des fokussierten Systems; Emulatoren, Importe, Einstellungen: aus (nur Raum und Korn).

---

## 3. Typografie

### 3.1 Rollen und Familien

| Rolle | Plan A (mit neuen OFL-Schriften) | Plan B (nur Poppins) |
|---|---|---|
| Plakat (Spieltitel groß, Systemname) | **Archivo Expanded ExtraBold** | Poppins Bold, Laufweite −2 % |
| Überschrift (Regal-Titel, Dialog-Titel) | Poppins SemiBold | Poppins SemiBold |
| UI und Fließtext | Poppins Regular / Medium | Poppins Regular / Medium |
| Etikett, Ticker-Codes, Zahlen-Beschriftung | **Archivo SemiCondensed Bold** | Poppins SemiBold, Laufweite +3 % |
| Rücken (gedrehte Titel, Codes) | **Archivo Condensed Bold** | Poppins SemiBold, 85 % Größe |
| Zahlen (Spielzeit, Anzahl, Jahr) | **Archivo SemiExpanded Bold**, `tnum` | Poppins SemiBold |

**Warum Archivo:** Es ist eine Grotesk mit Breitenachse (62–125). Breite Plakattitel und schmale Rückenschrift kommen so aus *einer* Familie. Genau dieses Mittel nutzten Spielverpackungen der 90er auf Front und Rücken. Poppins bleibt für alles „Mittlere" (UI, Lesetext). Die Regel lautet: **Archivo nur in den Extremen (Expanded oder Condensed), nie in normaler Breite.** So bleiben beide Familien klar unterscheidbar.

**Download (Freigabe durch den Owner nötig):** Google Fonts, Familie „Archivo" (OFL 1.1). Aus dem ZIP `static/` werden nur vier Dateien nach `src/RetroHub.App/Assets/Fonts/` übernommen. Die erwarteten Dateinamen sind unten angegeben. Den internen Familiennamen vor Verwendung mit `fontTools` prüfen (`python -I -c "from fontTools.ttLib import TTFont; print(TTFont(p)['name'].getDebugName(1))"`):
- `Archivo_Expanded-ExtraBold.ttf` → erwartete Familie „Archivo Expanded" (Gewicht 800)
- `Archivo_SemiExpanded-Bold.ttf` → „Archivo SemiExpanded" (700)
- `Archivo_SemiCondensed-Bold.ttf` → „Archivo SemiCondensed" (700)
- `Archivo_Condensed-Bold.ttf` → „Archivo Condensed" (700)

Gesamt etwa 600 KB. Die OFL.txt von Archivo kommt neben die von Poppins. Referenz in XAML: `avares://RetroHub.App/Assets/Fonts#Archivo Expanded`.

Die variable Datei wird nicht verwendet, weil die Achsensteuerung in Avalonia nicht zuverlässig ist. Statische Schnitte sind deterministisch.

**Plan B** ist sofort umsetzbar. Alle Font-Rollen sind Ressourcen (`RhFontPoster`, `RhFontLabel`, `RhFontSpine`, `RhFontNumber`). Plan B setzt sie auf Poppins, Plan A tauscht nur diese vier Ressourcen. Kein Layout hängt vom Schnitt ab, weil Titel über Größenstufen und nicht über Messung umbrechen.

### 3.2 Schriftgrade (Referenz 1080p; ×UiScale)

| Stil (Key) | Familie / Schnitt | Größe / Zeilenhöhe | Laufweite | Farbe |
|---|---|---|---|---|
| `RhTextPosterXL` | Archivo Expanded ExtraBold | 72 / 76 | −1 % | Papier |
| `RhTextPoster` | Archivo Expanded ExtraBold | 56 / 62 | −1 % | Papier |
| `RhTextPosterS` | Archivo Expanded ExtraBold | 40 / 46 | 0 | Papier |
| `RhTextH2` | Poppins SemiBold | 24 / 30 | 0 | Papier |
| `RhTextBodyL` | Poppins Regular | 20 / 32 | 0 | Text 2 |
| `RhTextBody` | Poppins Regular | 18 / 28 | 0 | Text 2 |
| `RhTextUi` | Poppins Medium | 18 / 24 | 0 | Papier |
| `RhTextButton` | Poppins SemiBold | 20 / 24 | 0 | je nach Button |
| `RhTextCaption` | Poppins SemiBold | 16 / 21 | 0 | Text 2 (fokussiert: Papier) |
| `RhTextMeta` | Poppins Regular | 15 / 20 | 0 | Text 3 |
| `RhTextLabel` | Archivo SemiCondensed Bold | 15 / 18 | +3 % | Text 3 oder Licht |
| `RhTextCode` | Archivo SemiCondensed Bold | 17 / 20 | +4 % | Papier / Text 3 |
| `RhTextSpine` | Archivo Condensed Bold | 14–18 | +2 % | Text auf Farbe |
| `RhTextNumber` | Archivo SemiExpanded Bold | 40 / 44 | −1 % | Papier |
| `RhTextNumberS` | Archivo SemiExpanded Bold | 28 / 32 | 0 | Papier |
| `RhTextYear` | Archivo Expanded ExtraBold | 260 / 260 | −3 % | Papier bei 5 % |

Regeln:
- **Keine Großbuchstaben-Overlines** („GUTEN TAG", „JAHR", „WEITERSPIELEN" entfallen). Labels sind Satzschreibung in `RhTextLabel` oder `RhTextMeta`. Versalien gibt es nur dort, wo der Inhalt selbst Versalien ist (Systemcodes wie GBA, SNES).
- **Mindestgröße** für Lesbares ist 15 px. Text 3 erst ab 15 px.
- **Lange deutsche Titel**: Plakattitel haben `MaxLines=2`, `TextTrimming=CharacterEllipsis`. Die Größe folgt Stufen nach Zeichenanzahl (keine Messschleife): ≤ 18 Zeichen XL (72), ≤ 30 Zeichen Poster (56), sonst PosterS (40). Kachel-Bildunterschriften haben 2 Zeilen, danach Ellipse. Listenzeilen 1 Zeile; der fokussierte Titel scrollt nach 600 ms Verweilen als Laufschrift (40 px/s), nur in der Liste.
- **Zahlen** mit tabellarischen Ziffern (`FontFeatures="+tnum"`, falls in Avalonia 12 verfügbar; sonst rechtsbündig setzen).
- **Meta-Sätze statt Ketten**: `"{Genre} von {Entwickler}, {Jahr}. {Spieler}."` Fehlende Teile fallen samt Satzzeichen weg (Builder in `MetaSentence.cs`, siehe 10). Keine `A · B · C`-Ketten mehr.

---

## 4. Form und Oberfläche

### 4.1 Radien

| Key | Wert | Verwendung |
|---|---|---|
| `RhRadiusNone` (neu) | 0 | Regalkanten, Fachrückwände, Banner, Bühnen, randlose Bilder |
| `RhRadiusSmall` | 2 | Cover, Platzhalter, Etiketten, Aufkleber, Rücken |
| `RhRadiusChip` | 2 | (Alias, bleibt für Kompatibilität) |
| `RhRadiusTile` | 2 | Spielkacheln |
| `RhRadiusControl` (neu) | 4 | Buttons, Eingabefelder, Listenzeilen, Fokusring außen, Tastenkappen LB/RB |
| `RhRadiusPanel` | 6 | Dialoge, Menü-Overlay (Maximum der App) |
| `RhRadiusPill` | 4 | Key bleibt, Wert sinkt von 999 auf 4 (keine Pillen mehr) |

Kreise (`CornerRadius = Höhe/2`) gibt es nur für Controller-Glyphen (A/B/X/Y), Statuspunkte (8 px) und den runden Favoriten-Marker. Die 16 Literal-Radien in den axaml-Dateien werden ersetzt; im QA wird nach `CornerRadius="[0-9]` gegrept.

### 4.2 Kanten und Linien

- **Regalkante**: 2 px `Kante` #3A342B, darüber 1 px `Lichtkante` (#FFF3E0 bei 9 %), darunter 16 px Schatten (LinearGradient #000 bei 45 % → 0). Diese Einheit heißt `RhShelfEdge` (ein Control oder Border-Stack, statisch).
- **Trennlinie** in Listen: 1 px Kante. Keine Rahmen um Listen.
- **Overlay-Rahmen** (Dialog, Drawer): 1 px Kante, oben zusätzlich 1 px Lichtkante.
- **Keine Rahmen um Inhaltsgruppen.** Gruppen entstehen durch Abstand (48 px zwischen Gruppen, 16 px innerhalb) und durch Überschriften.

### 4.3 Schatten (Licht kommt von oben)

| Key | Wert | Wo |
|---|---|---|
| `RhContactShadow` | gezeichneter Verlauf: 10 px hoch, #000 bei 55 % → 0, unter jedem Cover in `CoverArt.Render` | alle Cover (kein BoxShadow!) |
| `RhFocusShadow` (ersetzt `RhFocusGlow`) | `0 18 32 -10 #D9000000` | nur das eine fokussierte Element |
| `RhPanelShadow` | `0 24 64 -16 #F0000000` | Dialoge, Drawer |
| `RhButtonGlow` | → `RhNoShadow` | Key bleibt, Wert leer |

Es gibt **keine farbigen Schatten**. Farbiges Licht ist ausschließlich das Spotlicht (8.1).

### 4.4 Texturen

Texturen werden **programmatisch erzeugt**. Ein kleines Konsolenprojekt `tools/TextureGen` (C#, `SkiaSharp`, das Avalonia ohnehin mitbringt; fester Seed, deterministisch) schreibt PNGs nach `src/RetroHub.App/Assets/Textures/`. Die PNGs werden committet, der Build hängt nicht vom Generator ab. Eine Python-Variante ist möglich (Pillow), wird aber nicht benötigt.

| Datei | Größe | Muster (Pixel-Rezept) | Einsatz | Deckkraft |
|---|---|---|---|---|
| `korn-128.png` | 128×128 | Rauschen, je Pixel weiß oder schwarz, Alpha gleichverteilt 0–36 | gesamte App (eine Ebene) | 3,5 % |
| `scan-4x3.png` | 4×3 | Zeile 0: #000 Alpha 110; Zeilen 1–2 transparent | nur „Bildschirm"-Flächen (Snaps, Titel, Bühnen-Screen) | 18 % |
| `lcd-4.png` | 4×4 | 3×3 transparenter Punkt, 1 px Gitter #000 Alpha 70 | Handheld-Wände (GB, GBC, GBA, DS, NGP, WS, Lynx, GG) | 8 % |
| `bayer-8.png` | 8×8 | 2×2-Zellen-Schachbrett, Alpha 0/48 (8-Bit-Dither) | NES, SMS, Atari, MSX | 7 % |
| `dither16-8.png` | 8×8 | diagonale Punktreihe (1 px Punkte, Abstand 4, versetzt) | SNES, Mega Drive, PC Engine | 7 % |
| `rillen-1x6.png` | 4×6 | 2 px Linie #FFF Alpha 26, 4 px transparent | SNES, Mega Drive, PS2 (Lüftungsrippen) | 6 % |
| `poly-48.png` | 48×42 | Dreiecksgitter, 1 px Linien #FFF Alpha 40 | N64, PS1, Saturn | 6 % |
| `halbton-12.png` | 12×12 | ein Punkt Ø 5 px, kantengeglättet | GameCube, Dreamcast, Mega-CD, Wii | 7 % |
| `maske-6.png` | 6×6 | Lochmaske: Spalten R/G/B je 1 px Alpha 80, dann 1 px Lücke | Arcade | 10 % |
| `lochblech-10.png` | 10×10 | Kreis Ø 4 px, versetzte Reihe | PC, Switch, PSP | 6 % |
| `holz-256x32.png` | 256×32 | Sinus-Maserung, 5 Töne Braun, Alpha 60 | Atari 2600/7800 | 10 % |
| `raster-16.png` | 16×16 | 1 px Gitterlinie oben und links, #FFF Alpha 36 | Master System, MSX, Game Gear | 7 % |

Avalonia: `<ImageBrush x:Key="RhTexLcd" Source="avares://RetroHub.App/Assets/Textures/lcd-4.png" TileMode="Tile" DestinationRect="0,0,4,4" />`. `DestinationRect` ohne `%` ist absolut. Das Element bekommt `RenderOptions.BitmapInterpolationMode="None"`, damit Pixelmuster scharf bleiben. Eine Brush-Instanz pro Textur, app-weit geteilt, und nie pro Kachel erzeugt. In `CoverArt` (C#) wird sie einmal statisch geladen und gecacht.

**Wo nie Textur**: hinter dem Cover-Grid selbst (nur Korn), auf Text-Flächen von Dialogen, auf Buttons.

### 4.5 Transluzenz (selten)

Erlaubt nur:
1. Scrims über Bildern (Fernsehlicht, Bühne, Start-Overlay).
2. Fußleiste: Raum-Verlauf 0 % → 96 % über 40 px oberhalb, damit Inhalt darunter auslaufen kann.
3. Menü-Drawer und Dialog-Hintergrund: Raum-Tief bei 72 % über der Seite (ohne Blur).
4. Overlay „Läuft": Scrim Raum-Tief bei 94 %.

Die Keys `RhGlassBrush`, `RhGlassHiBrush`, `RhGlassFaintBrush` und `RhGlassBorderBrush` bleiben für die Kompatibilität bestehen, zeigen aber auf opake Werte (Regal, Regal hell, Regal, Kante).

---

## 5. Ikonografie

### 5.1 Stilregeln „Gefaste Linie"

- Raster 24×24, Live-Area 20×20 (2 px Rand). Strichstärke 2,0 bei 24 px, skaliert proportional (32 px → 2,67).
- Nur **0°, 45°, 90°** für Geraden. Bögen nur für echte Kreise (Lupe, Disc, Uhr).
- **Ecken gefast statt gerundet**: Außenecken bekommen einen 45°-Schnitt von 2 px. Das ist eine Anspielung auf Pixel und Modulgehäuse und grenzt den Stil klar von Lucide ab.
- `StrokeLineCap=Square`, `StrokeLineJoin=Miter`. Keine runden Enden.
- **Gefüllte Variante** (`…Filled`) für ausgewählte Tabs und aktive Toggles: Innenfläche gefüllt, Detailstriche in Raum-Farbe ausgespart.
- Größen: 20 (inline), 24 (Buttons), 28 (Kopfleiste bei 10 ft), 48 (Leerzustände). Farbe immer `Foreground` (currentColor), kein Duoton (außer Plattform-Motiven).
- Geometrie in `Themes/Icons.axaml` als `StreamGeometry` (bestehender Mechanismus `RhIcon`), Key-Schema `RhIcon.Name`.

Beispiele (24er Raster):
```
Start (Fernseher):   M5,4 H19 L21,6 V16 L19,18 H5 L3,16 V6 Z  M9,21 H15  M12,18 V21
Bibliothek (Regal):  M3,21 H21  M5,21 V5 H9 V21  M11,21 V8 H14 V21  M16.2,21 L15,9.4 L18,8.8 L19.6,20.6
Konsolen (Modul):    M6,3 H16 L18,5 V21 H6 Z  M9,3 V6  M12,3 V6  M15,3 V6  M9,10 H15 V17 H9 Z
Emulatoren (Chip):   M8,7 H16 L17,8 V16 L16,17 H8 L7,16 V8 Z  M10,3 V7 M14,3 V7 M10,17 V21 M14,17 V21 M3,10 H7 M3,14 H7 M17,10 H21 M17,14 H21
Importe:             M12,3 V14  M8,10 L12,14 L16,10  M4,13 V19 L5,20 H19 L20,19 V13
Spielen (gefüllt):   M7,4 L20,12 L7,20 Z
Zufall (Würfel):     M6,4 H18 L20,6 V18 L18,20 H6 L4,18 V6 Z + 3 Punkte 2×2 diagonal
```

### 5.2 Benötigte Icons

| Gruppe | Icons (Key) |
|---|---|
| Navigation | `Start` (Fernseher), `Library` (Regal), `Systems` (Modul), `Emulators` (Chip), `Imports` (Pfeil in Fach), `Settings` (Schieberegler), `Search` (Lupe), `Menu`, `Back` (Winkel links), `More` (drei Quadrate 2×2) |
| Aktionen | `Play`, `Favorite` / `FavoriteFilled` (fünfzackiger Stern mit gefasten Spitzen), `Random` (Würfel), `Folder`, `External` (Quadrat mit Pfeil 45°), `Refresh`, `Sort`, `Filter`, `Grid`, `List`, `Cover` (Bild mit Berg), `Remove`, `Add`, `Close`, `Fullscreen`, `Window`, `Power` (Beenden), `Expand`, `Collapse` |
| Meta | `Clock` (Spielzeit), `Calendar` (zuletzt gespielt), `Players` (zwei Köpfe), `Region` (Globus mit 2 Meridianen), `File`, `Disk` (Speicher), `Controller`, `Starts` (Zähler mit Häkchen) |
| Status | `Ok` (Häkchen), `Warning` (Dreieck mit Ausrufezeichen), `Error` (gefastes Achteck mit Kreuz), `Info`, `Missing` (Datei mit Strich), `NotReady` (Stecker getrennt) |
| Medientyp (klein, 20 px) | `MediaCart8`, `MediaCart16`, `MediaCart64`, `MediaDisc`, `MediaMiniDisc`, `MediaCard`, `MediaFloppy`, `MediaHandheld`, `MediaArcade`: vereinfachte Versionen der Plattform-Motive, für Etiketten und Listen |
| Controller-Glyphen | bestehend: A/B/X/Y (Xbox-Farben bleiben: A #5BD07A, B #FF6B6B, X #5AA2FF, Y #FFD15C), PS-Symbole, LB/RB/LT/RT als Tastenkappen-Texte, Menu, View, D-Pad |

Die alten Icons im Lucide-Stil (Strich 1,9, runde Enden) werden vollständig ersetzt, damit kein Stilmix entsteht.

---

## 6. Plattform-Motive

### 6.1 Zeichenregeln

- Motive sind **Kategorien, keine Produkte**: „Modul mit Griffrillen", „Handheld hochkant", „Disc", „Bedienfeld". Keine Proportionen, die ein konkretes Gerät eindeutig kopieren. Keine Schriftzüge, keine Knopf-Farbanordnungen eines Herstellers.
- Raster 120×120. Höchstens 14 Pfad-Segmente. Flache Füllungen in **Farbe** und **Zweitfarbe**. Details (Rillen, Bildschirm, Knöpfe) als 2-px-Linien in **Wand** oder ausgespart. Keine Verläufe im Motiv.
- Jedes Motiv „steht": Die Unterkante liegt auf y=120 und schließt mit der Regalkante bzw. der Unterkante der Fläche ab. Ein Motiv wird nur unten angeschnitten, nie willkürlich seitlich.
- Ablage: `Themes/Motifs.axaml` als `StreamGeometry`-Paare (`RhMotif.Cart8.Body`, `RhMotif.Cart8.Detail` …), dazu `Theming/MotifCatalog.cs`, der jedem System Motiv, Textur und Farben zuordnet. Für C#-Rendering (`CoverArt`, `SystemWall`) werden dieselben Pfad-Strings einmal mit `Geometry.Parse` gecacht.

### 6.2 Grundformen (Pfaddaten, 120er Raster, Skizzenqualität, Feinschliff in M5)

```
Cart8   (8-Bit-Modul, Griffrillen oben)
  Body:   M14,10 H106 V120 H14 Z
  Label:  M24,46 H96 V112 H24 Z
  Detail: M24,18 H96 M24,25 H96 M24,32 H96            (Rillen, 2 px)
Cart16  (16-Bit-Modul, gerundete Schultern)
  Body:   M18,22 Q18,8 32,8 H88 Q102,8 102,22 V120 H18 Z
  Label:  M30,34 H90 V100 H30 Z
  Detail: M18,108 H102                                (Griffkante)
Cart64  (64-Bit-Modul, abgeschrägte Schultern)
  Body:   M12,34 L32,8 H88 L108,34 V120 H12 Z
  Label:  M28,40 H92 V104 H28 Z
Card    (Kartenmodul, flach)
  Body:   M20,60 H100 V120 H20 Z    Detail: M28,108 H92 (Kontaktreihe als 8 Striche)
Handheld hochkant
  Body:   M30,4 H90 L98,12 V120 H22 V12 Z
  Screen: M34,16 H86 V62 H34 Z
  Detail: Kreuz M36,82 h8 v-8 h8 v8 h8 v8 h-8 v8 h-8 v-8 h-8 Z, zwei Punkte Ø 9 bei (78,84) (90,76)
Handheld quer
  Body:   M14,40 H106 L114,48 V112 L106,120 H14 L6,112 V48 Z
  Screen: M38,52 H82 V100 H38 Z      Detail: Schulterlinien M14,40 L22,32 H34  M106,40 L98,32 H86
Klappe  (zwei Bildschirme)
  Body:   M18,10 H102 V56 H18 Z  M18,64 H102 V120 H18 Z   Scharnier: M14,58 H106 V62 H14 Z
Disc
  Body:   Kreis Mitte (60,64) r 52, ausgespart r 9
  Detail: Datenspur-Bögen r 30 und r 40 über 70°, 2 px
MiniDisc  Kreis r 38, Loch r 13, bei (60,82)
Bedienfeld (Arcade)
  Body:   M8,72 H112 L104,120 H16 Z
  Detail: Stick M40,72 V44; Kugel Kreis (40,38) r 9; 6 Knöpfe Ø 10 in 2×3 ab (66,84)
Diskette
  Body:   M14,8 H96 L110,22 V120 H14 Z
  Detail: Schieber M36,8 H84 V42 H36 Z; Etikett M28,64 H96 V120 H28 Z
Tablett mit Schienen
  Body:   M24,30 H96 V104 H24 Z (Screen), Schienen M8,30 H20 V104 H8 Z / M100,30 H112 V104 H100 Z
Stab    (Fernbedienungs-Form)
  Body:   M46,4 H74 L80,10 V120 H40 V10 Z    Detail: Kreuz klein + 1 Kreis
Lamellen-Turm
  Body:   M30,6 H90 V120 H30 Z    Detail: 7 horizontale Rillen, eine Linie in Licht (die „Lichtlinie")
```

### 6.3 Zuordnung pro Familie

| Familie / System | Motiv | Farbgebung im Motiv | Textur | Charakter-Notiz |
|---|---|---|---|---|
| **gb** | Handheld hochkant, Bildschirm mit „Grünstufen" (4 Balken #0F380F, #306230, #8BAC0F, #9BBC0F) | Body Zweitfarbe aufgehellt #C9C6B4, Bildschirm-Grünstufen | lcd-4 | „grünlich": Wand olivgrün, LCD-Raster |
| **gbc** | Handheld hochkant, **doppelt** gezeichnet: Umriss in Farbe und 6 px versetzt in Zweitfarbe, Füllung 35 % (durchsichtiges Gehäuse) | Beere + Petrol | lcd-4 | |
| **gba** | Handheld quer | Farbe Body, Screen Wand, Schultern Zweitfarbe | lcd-4 | |
| **nds** | Klappe | Silber-Schiefer, Screens Wand | lcd-4 | |
| **3ds** | Klappe, oberer Screen breiter (+8 px) | Rot, Screens Wand | lcd-4 | |
| **nes** | Cart8 | Body Zweitfarbe (Grau), Label Farbe (Rot) | bayer-8 | |
| **snes** | Cart16 | Body warmgrau #BDB7AD, Label Flieder | dither16-8 + rillen | „warm klassisch": Wand warm überschrieben |
| **n64** | Cart64 | Body Zweitfarbe (Anthrazit), Label Farbe | poly-48 | |
| **gamecube** | MiniDisc vor einem **abgerundeten Quadrat-Umriss** (r 10, 2 px, Zweitfarbe Orange) | Violett + Orange | halbton-12 | „verspielt": Motiv um −6° gedreht (Formen dürfen kippen, Text nie) |
| **wii** | Stab | Body #E6EEF2, Details Farbe | halbton-12 bei 4 % | hellstes Motiv; Wand bleibt dunkel |
| **wiiu** | Tablett ohne Schienen, zwei Punkte als Sticks | Farbe + Weiß | halbton-12 | |
| **switch** | Tablett mit Schienen, **beide Schienen in Farbe** (nie Rot/Blau-Paar) | Rot + Anthrazit | lochblech-10 | |
| **virtualboy** | Drahtgitter-Version des Tabletts nur als Linien in Licht (keine Füllung) | Rot-Linien auf Schwarz | scan-4x3 | |
| **ps1** | Disc mit Datenspur | Zweitfarbe-Ringe auf Farbe | poly-48 | |
| **ps2** | Lamellen-Turm, Lichtlinie in Licht | Fast Schwarz + Blau | rillen-1x6 + scan | „dunkel-futuristisch" |
| **psp** | Handheld quer, gestreckt (Breite 112, Höhe 56) | Klavierlack #0C0D10, Rahmen Silber | lochblech-10 | |
| **mastersystem** | Cart8 vor Raster | Schwarz + Rot | raster-16 | |
| **megadrive** | Cart16 + **Rennstreifen** (3 parallele Balken 60°, 6 px, links oben) | Schwarz + Blau | rillen + dither16 | Sega-Familienzeichen: Rennstreifen |
| **gamegear** | Handheld quer | Schwarz + Cyan | raster-16 | |
| **segacd** | Disc + Rennstreifen | Blau + Schwarz | halbton-12 | |
| **sega32x** | Cart16 auf Cart16 gestapelt (oberes schmaler) | Gelb + Schwarz | dither16 | |
| **saturn** | Disc mit **Umlaufbahn** (Ellipse 2 px, 15° geneigt, um die Disc) | Indigo + Anthrazit | poly-48 | |
| **dreamcast** | Disc + Halbton-Welle (Halbtonpunkte nehmen nach rechts zu) | Orange + Weiß | halbton-12 | keine Spirale (Markenzeichen) |
| **arcade** | Bedienfeld, Linien **doppelt**: 2 px Farbe + 8 px Farbe bei 22 % darunter (Neon ohne Blur) | Magenta + Cyan | maske-6 | „Neon": das einzige leuchtende Motiv |
| **pc** | Diskette | Beige Body, Etikett Phosphorgrün | lochblech-10 | |
| **atari2600 / 7800** | Bedienfeld vereinfacht (nur Stick) vor Holzband (unteres Drittel) | Holz + Farbe | holz-256x32 | |
| **lynx** | Handheld quer, sehr breit (Breite 116) | Gelb + Anthrazit | lcd-4 | |
| **pcengine** | Card | Orange + Creme | dither16 | |
| **ngp** | Handheld quer, kompakt, Kreis statt Kreuz | Blau + Creme | lcd-4 | |
| **wonderswan** | Handheld hochkant, schmal (Breite 52) | Petrol + Creme | lcd-4 | |
| **msx** | Tastenblock (3 Reihen à 5 gefaste Quadrate 14 px) | Blau + Creme | raster-16 | |
| *unbekannt* | Cart8 neutral | `SystemPalette.From(accent)` | bayer-8 | Fallback |

### 6.4 Wo Motive erscheinen

1. **Fachrückwand** (Systemseite-Header, Bibliothek-Banner, Konsolen-Karussell): Wand-hell → Wand (vertikal), Textur, Motiv **voll** in Farbe und Zweitfarbe, Höhe 72 % der Fläche, rechts (rechter Rand 6 %), auf der Unterkante stehend.
2. **Fachschild** (Home, 248×140): Motiv voll, 104 px hoch, rechts unten.
3. **Platzhalter-Cover**: Motiv als Silhouette nur in Farbe bei 18 %, 45 % der Coverhöhe, rechts unten.
4. **Detail-Hintergrund ohne Bild**: Motiv als 1,5-px-Umriss in Licht bei 10 %, 520 px hoch, rechts unten hinter dem Titelbereich.
5. **Etikett und Listen**: das 20-px-Medientyp-Icon.
6. **Leerzustände**: 1,5-px-Linienzeichnung in Papier bei 60 % (z. B. „Noch keine Spiele in diesem Fach": leeres Regal mit Motiv-Umriss).

---

## 7. Screens

### 7.0 Raster, Skalierung, Kopf- und Fußleiste

**UiScale**: `s = clamp(min(W/1920, H/1080), 0,80, 2,0)` aus der Client-Größe in DIP, quantisiert auf 0,05 und erst 150 ms nach dem letzten Resize angewandt. Umsetzung: `LayoutTransformControl` mit `ScaleTransform(s)` um den Seiteninhalt (Kopfleiste, PageHost, Fußleiste). Alle Maße in diesem Dokument sind logische Referenzpixel. Bei 1100×700 ist s = 0,80, logisch also 1375×875. Bei 2560×1440 (100 % Windows-Skalierung) ist s = 1,33. Bitmap-Decodes rechnen mit `Breite × s × RenderScaling`.

**Seitenrand**: 96 px bei logischer Breite ≥ 1600, sonst 56 px. Spaltenabstände im Grid 28 px (≥ 1600) bzw. 20 px.

**Entscheidung Shell: Kopfleiste statt Seitenleiste.** Die 248-px-Sidebar entfällt. Begründung: Sie gibt 100 % Breite für Regale frei, entspricht dem „Konsolen-Startmenü" statt einem Admin-Panel und funktioniert bei 1100 px ohne Kollaps-Modus.

```
1920 × 1080 (logisch)
┌─────────────────────────────────────────────────────────────────────────────────────────┐ y 0
│ ●RetroHub   Start   Bibliothek   Konsolen          ⌕   ⇩²  ▣   ⚙      Ⓐ Xbox  21:14  – □ ✕ │ 72  Kopfleiste
│ x48         x260 (Tabs, Abstand 40)                 Icon-Buttons 48×48, Abstand 8   rechts 170 px frei
├─────────────────────────────────────────────────────────────────────────────────────────┤ y 72
│                                                                                         │
│                                  Seite (volle Breite)                                   │
│                                                                                         │
│                                                                                         │
├─────────────────────────────────────────────────────────────────────────────────────────┤ y 1024
│ Bibliothek › Game Boy Advance, Spiel 3 von 7          (Y) Suche  (X) Favorit  (A) Öffnen  (B) Zurück │ 56 Fußleiste
└─────────────────────────────────────────────────────────────────────────────────────────┘ y 1080
```

**Kopfleiste (72 px)**
- **Wortmarke**: Bernstein-Quadrat 8×8 (R 1), 10 px Abstand, dann „RetroHub" in Archivo Expanded ExtraBold 20. Das ist die „Standby-LED". Overline und Play-Logo entfallen.
- **Tabs**: Poppins SemiBold 20, Text 2. Aktiver Tab in Papier mit einem 3 px × 28 px **Bernstein-Strich** 8 px unter der Grundlinie, linksbündig (Kanalanzeige).
- **Icon-Buttons**: 48×48, Icon 28. In Reihenfolge Suche, Importe, Emulatoren, Einstellungen. Die Statusmarke ist ein 8-px-Punkt oben rechts am Icon (Importe: Bernstein bei neuen Funden; Emulatoren: Warnung, wenn ein benutztes System keinen Emulator hat).
- **Controller-Status**: Glyphe plus Name (bei < 1500 logisch nur die Glyphe), dann Uhr in Poppins Medium 18.
- **Hintergrund**: auf Start und Detail transparent über einem Scrim (Raum-Tief 70 % → 0 über 160 px); auf allen anderen Seiten Raum. Keine Linie darunter.
- Die Fläche bleibt Drag-Area. Rechts bleiben 170 px für Caption-Buttons frei.

**Fußleiste (56 px)**
- **Links**: Ortssatz in Poppins Regular 16, Text 2, z. B. „Bibliothek › Game Boy Advance, Spiel 3 von 7". Elternteile Text 3, aktuelles Element Text 2. Der statische Text „66 Spiele · 13 Plattformen" entfällt.
- **Rechts**: höchstens 4 Hinweise (bei < 1300 logisch: 3). Glyphe 28 px plus Poppins Medium 16 Papier, Abstand 28. Reihenfolge von rechts: B Zurück, A, dann X/Y. Die Hinweise kommen vom fokussierten Element (`IHintSource` am fokussierten Control, Fallback auf die Seite).
- **Hintergrund**: transparent über dem Raum-Verlauf (siehe 4.5).

**Navigation**
- *Menu* fokussiert den aktiven Tab der Kopfleiste. *B* in der Kopfleiste kehrt zum gemerkten Fokus der Seite zurück.
- *Hoch* vom oberen Seitenrand erreicht die Kopfleiste über die normale räumliche Navigation (`FindNextElement`). Der bisherige Sprung „Links am Rand → Sidebar" entfällt.
- *LB/RB* sind immer seitenlokal: Bibliothek = Fach-Filter, System = vorheriges/nächstes System, Detail = Abschnitt.
- *B* geht genau eine Ebene zurück und stellt den Fokus wieder her (PageHost speichert das bereits).
- Bei 1100 px (logisch 1375) passt alles: Wortmarke 150 + Tabs ≈ 330 + Icons 216 + Status ≈ 130 ergibt etwa 830 < 1205.

**Menü-Drawer** ist nicht nötig. Die Kopfleiste ist das Menü. Einstellungen, Emulatoren und Importe sind volle Seiten (Ebene „Werkbank").

---

### 7.1 Start (Home): „Der Fernseher im Regal"

Home ist ein Startmenü: oben die **Bühne** (das fokussierte Spiel im Fernsehlicht), darunter **Regale**. Keine StatTiles, keine Begrüßung, keine zweite H1.

```
1920 × 1080
┌─────────────────────────────────────────────────────────────────────────────────────────┐ 0
│ Kopfleiste (transparent)                                                                │
│                                                                                    ░░░░ │ 72
│                                                                                ░ 2004 ░ │  Jahr 260 px, 5 %
│    ┌───────────────┐   Zuletzt gespielt: heute                                    ░░░░ │ 196  RhTextLabel, Licht
│    │               │   Pokémon Smaragd-                                               │ 224  Plakat 56/62 (2 Zeilen)
│    │     COVER     │   Edition                                                        │
│    │   bis 300×420 │                                                                  │ 
│    │  (steht unten │   Game Boy Advance. 46 Stunden gespielt, 41 Starts.             │ 372  BodyL 20
│    │   bündig)     │                                                                  │
│    │               │   ┏━━━━━━━━━━━━━━━┓ ┌──────────┐ ┌────┐                        │ 432
│    │               │   ┃ ▶  Spielen    ┃ │ Details  │ │ ☆  │                        │ 64 hoch
│    └───────────────┘   ┗━━━━━━━━━━━━━━━┛ └──────────┘ └────┘                        │ 496
│ ═══════════════════════════════════════════════════════════════════════════════════════ │ 548  Regalkante (Bühne)
│                                                                                         │
│    Zuletzt gespielt                                                     Alle anzeigen › │ 600  H2 24
│    ▯▯▯  ▯▯  ▯▯▯  ▯  ▯▯▯  ▯▯  ▯▯▯  ▯▯  ▯▯▯  ▯▯ ›                                        │ 640–860 Cover H 220
│ ═══════════════════════════════════════════════════════════════════════════════════════ │ 862
│    Deine Fächer                                                                          │ 900
│    ┌────────┐┌────────┐┌────────┐┌────────┐┌────────┐┌────────┐                         │ 940– (angeschnitten
│    │GBA   ▟█││SNES ▟█ ││...     │                                                       │       = „mehr unten")
├─────────────────────────────────────────────────────────────────────────────────────────┤ 1024
│ Start › Weiterspielen                          (X) Favorit  (A) Spielen  (B) Zurück       │
└─────────────────────────────────────────────────────────────────────────────────────────┘
```

**Bühne (y 72–548)**
- **Cover**: Rahmen 300×420 bei x 96. Das Bild wird contain-eingepasst und **unten bündig** auf die Bühnen-Regalkante bei y 548 gestellt. Kontaktschatten, kein Rahmen, R 2.
- **Textspalte** ab x 448 (Cover + 52), Breite bis x 1500:
  - Label „Zuletzt gespielt: heute" (`RhTextLabel`, Bernstein-Text). Bei Fokus in einem Regal steht dort der Regalname („Favorit", „Neu in der Sammlung").
  - Titel `RhTextPoster` mit Größenstufen.
  - Meta-Satz `RhTextBodyL`.
  - Aktionsreihe: **Spielen** 280×64 (Bernstein, Tinte, Icon Play 24, R 4), **Details** 64 hoch (Regal hell, Papier), **Favorit** 64×64 nur Icon.
- **Rechts (x 1500–1824)**: Jahres-Wasserzeichen, rechtsbündig, Grundlinie y 520. Sobald Snaps verfügbar sind (M4b), steht hier der **Bildschirm**: Snap in 4:3, 420×315, ganzzahlig skaliert, `BitmapInterpolationMode=None`, scan-4x3 bei 18 %, 2 px Raum-Tief-Rand, steht auf der Regalkante. Das Jahr rückt dann hinter den Bildschirm.
- **Hintergrund**: Fernsehlicht des Bühnen-Spiels (100 %).
- **Die Bühne folgt dem Fokus**: Fokus auf einer Regal-Kachel bedeutet, dass die Bühne nach 220 ms dieses Spiel zeigt. Text blendet 120 ms aus und 180 ms ein mit 8 px x-Versatz; das Cover blendet über. Ein Fokus auf einem Fachschild zeigt das System auf der Bühne (Wand als Hintergrund, Systemname als Titel, Satz „7 Spiele, zuletzt Pokémon Smaragd-Edition."). Ohne Regal-Fokus zeigt die Bühne das Weiterspielen-Spiel.
- **Erster Fokus** liegt auf „Spielen" (wie bisher `FocusDefault`).

**Regale**
- **Reihenfolge**: Zuletzt gespielt (≤ 11), Favoriten (≤ 16), Neu in der Sammlung (≤ 12), Deine Fächer (Systeme mit Spielen, nach Spielzeit). Leere Regale entfallen.
- **Cover**: Höhe 220, Breite nach Bild (contain), Abstand 24, unten bündig auf der Regalkante. **Keine Bildunterschriften**; die Bühne ist die Bildunterschrift. Platzhalter tragen den Titel selbst.
- **Fester Fokus-Anker**: Die fokussierte Kachel steht in Slot 2 (x = 96 + 1 Kachel). Die Reihe gleitet darunter: `TranslateTransform` 220 ms CubicEaseOut. Am Anfang der Reihe steht der Fokus in Slot 1.
- **Letzte Kachel jeder Reihe**: „Alle anzeigen", ein Rücken-artiges Feld 120×220 in Regal hell mit Pfeil und Text. Es führt in die Bibliothek mit dem passenden Filter bzw. der passenden Sortierung.
- **Fachschilder**: 248×140, Abstand 16, R 0 (Wände haben keine Rundung). Inhalt: Wand-Verlauf, Textur, Motiv 104 px, oben links Systemname `RhTextPosterS` auf 22 px reduziert (Archivo Expanded ExtraBold 22), darunter „7 Spiele" Poppins 15 Text 2. Sie stehen ebenfalls auf einer Regalkante.

**Leere Bibliothek**: Die Bühne zeigt eine Linienzeichnung (leeres Regal, 240 px, Papier 60 %), dazu den Titel „Das Regal ist noch leer", den Satz „Lege Spieldateien in einen Bibliotheksordner und starte einen Scan." und die Buttons **Ordner hinzufügen** (Bernstein) und **Importe öffnen**.

**≈ 1100 px (logisch 1375×875)**: Bühne y 72–470, Cover-Rahmen 220×308, Plakat-Stufen −1 (max. 56), Jahr ausgeblendet, Spielen 240×64. Regal-Cover Höhe 180. Fachschilder 208×120.

---

### 7.2 Bibliothek: „Das Regal"

Zwei Ansichten: **Regal** (Standard) und **Liste** (View-Taste). Karussells leben auf Home, Konsolen und der Systemseite. Die Bibliothek bleibt dicht und durchsuchbar.

#### 7.2.1 Regal-Ansicht

```
1920 × 1080
┌─────────────────────────────────────────────────────────────────────────────────────────┐
│ Kopfleiste                                                                              │ 72
├─────────────────────────────────────────────────────────────────────────────────────────┤
│ [LB] Alle 66  NES 6  SNES 8  N64 4  GB 3  GBC 2  GBA 7  NDS 5  PS1 6 … [RB]   ⌕  ↕Titel ☆ ▦│ 136  Ticker 64
│      ‾‾‾‾                                                                                 │      (Unterstrich 3 px)
├─────────────────────────────────────────────────────────────────────────────────────────┤ ─── Header scrollt mit:
│  66 Spiele in 13 Fächern                                                                │ 168  Poster 40
│  ███▇▇▇▇▇▇▇▅▅▅▅███████▆▆▆▆▆▆▆▆▅▅▅▅▅▅▅▅▅▅▅▅▅██████▆▆▆▆▆▆▆▆▆▆▆▆▆▆▆▆▆▆▆▆▆▆▆▆▆▆▆                │ 232  Rückenleiste 1728×28
│  NES        SNES      N64  GB  GBA          …                                            │ 268  Codes unter Segmenten ≥ 64 px
├─────────────────────────────────────────────────────────────────────────────────────────┤ 312
│                                                                                         │
│   ▯▯   ┌─┐  ▯▯▯  ┌──┐  ▯▯   ┌─┐  ▯▯▯  ┌──┐     ← 8 Spalten, Fach 196×245, Abstand 28   │ 340
│   ▯▯   │ │  ▯▯▯  │  │  ▯▯   │ │  ▯▯▯  │  │       Cover stehen unten bündig               │
│ ═══════════════════════════════════════════════════════════════════════════════════════ │ 585  Regalkante
│   Pokémon      Super Mario   Golden Sun   …        Titel 2 Zeilen, Caption 16/21         │ 601
│   ▮GBA 2004    ▮SNES 1991    ▮GBA 2001             Etikett-Zeile 15                      │ 645
│                                                                                         │ 
│   ▯▯ …                                                       (Zeilenabstand 347)        │ 687
├─────────────────────────────────────────────────────────────────────────────────────────┤ 1024
│ Bibliothek › Alle Spiele, 12 von 66      (LB/RB) Fach  (Y) Suche  (A) Öffnen  (B) Zurück  │
└─────────────────────────────────────────────────────────────────────────────────────────┘
```

**Ticker (Fach-Filter), 64 px, fix (scrollt nicht mit)**
- Tastenkappen **LB** und **RB**: 44×32, R 4, Papier-Fläche, Tinte-Text Archivo SemiCondensed Bold 15, ganz links und ganz rechts vom Ticker.
- **Einträge**: Code `RhTextCode` (17) plus Anzahl Poppins 14 Text 3, Abstand 32. Aktiv: Code in Papier, 3-px-Unterstrich in **Farbe** des Systems (bei „Alle": Bernstein), 6 px unter der Grundlinie, so breit wie der Code. Inaktiv: Code Text 2.
- **Überlauf**: horizontal verschiebbar, der aktive Eintrag bleibt sichtbar. An den Rändern liegen 48 px Raum-Verläufe als Overlay (kein OpacityMask).
- **Werkzeuge rechts**, jeweils 44 hoch, rahmenlos, Icon 24 plus optionaler Text: Suche (Y) als Icon, das bei Aktivierung ein 360-px-Feld über den Ticker schiebt (180 ms); Sortierung als „↕ Titel" (öffnet ein Auswahl-Overlay); Favoriten als Stern-Toggle; Ansicht Regal/Liste. Fokus ist Inversion (siehe 8).
- Die 14 System-Chips mit Rahmen entfallen. Der Ticker *ist* der Filter.

**Kopf (scrollt mit, `CoverGrid.Header`)**
- **„Alle"**: „66 Spiele in 13 Fächern" (Archivo Expanded ExtraBold 40). Darunter die **Rückenleiste**: ein horizontaler Balken über die Inhaltsbreite, 28 px hoch, aufgeteilt in Segmente pro System. Breite proportional zur Spielanzahl, mindestens 12 px, Füllung = Systemfarbe, 2 px Raum-Lücken. Unter Segmenten ≥ 64 px steht der Code (Archivo SemiCondensed Bold 13, Text 3). Die Leiste ist Information (Verteilung der Sammlung) und gleichzeitig die einzige „bunte" Fläche. Nicht fokussierbar; der Ticker ist das Bedienelement.
- **Ein System gewählt**: **Fachrückwand-Banner** 200 px hoch, volle Breite (randlos über den Seitenrand). Systemname Plakat 56, Satz „Handheld von Nintendo, 2001. 7 Spiele, 46 Stunden gespielt." BodyL; Motiv rechts 144 px hoch; rechts davon bis zu 3 meistgespielte Cover (Höhe 150) auf der Banner-Kante als „Schaufenster". Wechsel per LB/RB: Wandfarbe `BrushTransition` 280 ms, Motiv gleitet 24 px seitlich und blendet über (220 ms).
- **Favoriten-Filter aktiv**: Kopf „11 Favoriten" ohne Rückenleiste.

**Regal-Grid**
- **Fach (Slot)**: in gemischter Ansicht Seitenverhältnis 1 : 1,25 (196×245 bei 8 Spalten und 1920). Spaltenzahl = floor((B + 28) / (176 + 28)), mindestens 3. Breite wird bis 1,15× gestreckt (`MaxStretch`).
- **Cover**: contain, **unten bündig, horizontal zentriert**. Hochformat füllt die Höhe, Querformat (SNES/N64-US) füllt die Breite und steht niedriger. Das ist gewollt: Es sieht aus wie ein echtes Regal.
- **Regalkante** unter jeder Zeile über die volle Inhaltsbreite (gezeichnet von `CoverRowPresenter`, siehe 11), 16 px Schatten darunter.
- **Bildunterschrift**: 16 px unter der Kante. Titel Caption 16/21, 2 Zeilen, Text 2. Darunter die Etikettzeile: Systemfarben-Balken 3×14 plus Code (`RhTextLabel` 15, Text 3), dann Jahr. In einer gefilterten Ansicht entfällt der Code (nur Jahr/Region).
- **Status**: `IsMissing` bzw. nicht startbar ergibt Cover-Deckkraft 0,45 plus Icon `Missing`/`NotReady` 20 px in einer 28×28 Raum-Tief-Fläche (R 2) oben rechts *im* Cover. Der Favorit ist ein Stern 18 px, gefüllt Favoritengelb, in einer 26×26 Raum-Tief-Fläche (R 2) oben links. Er wird nur gezeigt, wenn das Spiel Favorit ist. Der bisherige Badge oben links (Systempille) entfällt, weil das Etikett unten steht.
- **Aufkleber „Neu"** (hinzugefügt < 7 Tage und nie gestartet): Bernstein-Fläche, Tinte-Text „Neu" Archivo SemiCondensed Bold 13, Innenabstand 6×3, R 2, überlappt die obere linke Ecke um −4/−4. Beim ersten Fokus fährt er in 250 ms 12 px nach oben links und blendet aus („auspacken"). Er gilt danach als gesehen (Speicherung pro Sitzung; dauerhaft optional).
- **Grid-Hintergrund**: Raum plus Fernsehlicht bei 45 % des fokussierten Spiels plus Korn.
- **Zeilenabstand**: 245 + 16 + 66 + 20 = 347. Sichtbar sind etwa 2,7 Zeilen bei weggescrolltem Kopf, also 21 Spiele.

**≈ 1100 px (logisch 1375)**: Rand 56, Abstand 20 ergibt 6 Spalten à 194. Ticker: Werkzeuge nur als Icons. Rückenleiste ohne Codes unter 80 px Segmentbreite. Banner 160 hoch ohne Schaufenster.

#### 7.2.2 Listen-Ansicht (View-Taste)

```
┌────────────────────────────────────────────┬──────────────────────────────────────────┐
│ Ticker (wie oben)                                                                     │
├────────────────────────────────────────────┼──────────────────────────────────────────┤
│ ▮ Advance Wars                  GBA   ★     │                                          │
│ ▮ Banjo-Kazooie                 N64         │            ┌──────────────┐               │
│█▌Castlevania: Aria of Sorrow    GBA   ★ ███│            │  Cover max   │               │  ← Fokuszeile:
│ ▮ Chrono Trigger                SNES        │            │   360×480    │               │    Papier-Fläche,
│ ▮ Donkey Kong Country 2         SNES        │            └──────────────┘               │    Tinte-Text,
│ ▮ …                                         │ ═══════════════════════════════════       │    4 px Farbband links
│   Liste 44 % · Zeilen 64 px                 │  Castlevania: Aria of Sorrow (Poster 40)  │
│                                             │  Action von Konami, 2003. Nie gespielt.   │
│                                             │  [▶ Spielen]  [Details]                   │
└────────────────────────────────────────────┴──────────────────────────────────────────┘
```
- **Liste** 44 % der Breite, Zeilen 64 px. Links ein 4×24 Systemfarben-Balken, Titel Poppins Medium 20 (1 Zeile, Laufschrift im Fokus nach 600 ms), rechts Code `RhTextLabel` und Favoritenstern. Kein Tabellenkopf, keine Spalten „Zuletzt / Spielzeit / –". Trennlinie 1 px Kante.
- **Fokuszeile** (Inversion): Fläche Papier, Text Tinte, Farbbalken in Systemfarbe auf 8 px verbreitert. Wechsel 90 ms.
- **Vorschau** 56 %: Cover contain max 360×480 auf eigener Regalkante, Titel PosterS (40), Meta-Satz, Spielen und Details. Die Vorschau ist ein Fernsehlicht-Ausschnitt (eigener Scrim). A auf einer Zeile öffnet das Detail, X setzt Favorit.
- **Optional (M3b)**: XMB-Fokuslinie. Die Fokuszeile bleibt bei y = 35 % der Liste, die Liste gleitet darunter (167 ms).

---

### 7.3 Spiel-Detail: „Das Spiel unter der Lampe"

```
1920 × 1080
┌─────────────────────────────────────────────────────────────────────────────────────────┐
│ Kopfleiste (transparent)                                                                │ 72
│                                                                              ░░░░░░░░░  │
│      ┌─────────────────┐   ▮ Game Boy Advance                              ░  2004  ░  │ 168  Etikett 18 Licht
│      │                 │   Pokémon Smaragd-                                ░░░░░░░░░  │ 200  Plakat XL/Poster
│      │                 │   Edition                                                    │
│      │     COVER       │                                                              │
│      │  bis 340×476    │   Rollenspiel von Game Freak, 2004. Ein Spieler. Europa.     │ 372  BodyL 22
│      │  (steht unten)  │                                                              │
│      │                 │   ┏━━━━━━━━━━━━━━━━┓ ┌───────────┐ ┌────┐   46 Std. │ heute │ 41│ 456  Aktionsreihe 64
│      │                 │   ┃ ▶   Spielen    ┃ │ ☆ Favorit │ │ ⋯  │   gespielt│zuletzt│Starts
│      └─────────────────┘   ┗━━━━━━━━━━━━━━━━┛ └───────────┘ └────┘                     │ 520
│ ═══════════════════════════════════════════════════════════════════════════════════════ │ 600  Regalkante
│                                                                                         │
│      Über das Spiel                               Start                                 │ 648  H2
│      Hoenn in seiner schönsten Form – mit        ┌──────────────────────────────────┐  │
│      Kampfzone, Rayquaza und Doppelkämpfen.      │ Emulator     Automatisch (mGBA) › │  │ 64 hoch
│      (BodyL 20/32, max. 68 Zeichen,              │ Startmodus   Eingebettet        › │  │
│       6 Zeilen, dann „Weiterlesen")              │ ✓ Bereit zum Start                │  │
│                                                  │ Datei        Pokémon….gba, 15,5 MB›│  │
│      x 120 – 1000                                 x 1080 – 1800                         │
├─────────────────────────────────────────────────────────────────────────────────────────┤ 1024
│ Bibliothek › Game Boy Advance › Pokémon Smaragd-Edition   (X) Favorit (A) Spielen (B) Zurück│
└─────────────────────────────────────────────────────────────────────────────────────────┘
```

**Oberteil, die Lampe (y 72–600)**
- **Hintergrund**: Fernsehlicht des Spiels (100 %). Ohne Bild: Fachrückwand des Systems plus Motiv-Umriss (6.4 Punkt 4).
- **Cover**: Rahmen 340×476 bei x 120, unten bündig auf der Regalkante y 600. Fokussierbar (A = Coveransicht groß; *Mehr* bietet „Cover ändern").
- **Textspalte** ab x 520:
  - System-Etikett: Balken 4×18 in Farbe plus Systemname (Archivo SemiCondensed Bold 18, Licht).
  - Titel mit Größenstufen.
  - Meta-Satz (BodyL 22).
- **Aktionsreihe**: **Spielen** 300×64 (Bernstein), **Favorit** 64 hoch (Regal hell, Icon plus Text, gefüllter Stern wenn aktiv), **Mehr** 64×64 (öffnet ein Overlay mit „Cover ändern", „Cover entfernen", „Ordner öffnen", „Dateiinfo", „Startoptionen zurücksetzen").
- **Zahlen rechts der Aktionsreihe** (ab x ≈ 1260): 3 Spalten à 150 mit vertikalen 1-px-Kanten (48 hoch) dazwischen. `RhTextNumberS` (28) für den Wert, Poppins 15 Text 3 für die Bedeutung („gespielt", „zuletzt", „Starts"). Nur wenn `HasPlayed`; sonst ein Satz in BodyL: „Noch nie gespielt. Seit 25. September in der Sammlung."
- **Jahres-Wasserzeichen**: `RhTextYear` rechtsbündig bei x 1800, Grundlinie y 590, hinter allem. Blendet beim Seitenaufbau in 600 ms ein (ExponentialEaseOut). Nur wenn ein Jahr bekannt ist.
- **Spielen nicht möglich**: Der Button wird zum **Lösungs-Button** (Regal hell, Rahmen 2 px Warnung, Icon Warning, Text „Emulator einrichten"). Er führt zu Emulatoren, gefiltert auf dieses System. Kein halbtransparenter Bernstein.

**Unterteil (Hinunter scrollen, y 600+)**
- **Links „Über das Spiel"** (x 120–1000): Beschreibung, sonst die **Bildschirm-Galerie** (Titelbild und Snap nebeneinander, je 4:3 in 400×300, ganzzahlig skaliert, scan-4x3, A = Vollbild-Diptychon). Wenn beides fehlt, entfällt die Spalte und „Start" rückt nach links. Der Satz „Für dieses Spiel ist noch keine Beschreibung vorhanden." entfällt.
- **Rechts „Start"** (x 1080–1800): **Auswahlzeilen** à 64 px statt Combos im Panel. Label Poppins 18 Text 3 links (Breite 200), Wert Poppins Medium 18 Papier, Winkel rechts. A öffnet ein Auswahl-Overlay (Liste mit Inversion). Darunter die Statuszeile mit Icon (OK/Warnung/Fehler) und Text aus `Problems`/`PlanSummary`. Die Dateizeile zeigt nur Dateiname und Größe; Pfad und CRC stehen im Dateiinfo-Overlay (Cascadia Mono 15).
- **LB/RB** springen zwischen Oberteil, „Über das Spiel" und „Start".

**≈ 1100 px (logisch 1375×875)**: Cover-Rahmen 240×336, Titel max. Poster (56), Zahlen unter die Aktionsreihe (eigene Zeile), Jahr 180 px. Unterteil einspaltig: Start zuerst, dann Über das Spiel.

---

### 7.4 Konsolen: „Die Fächerwand"

Ein horizontales Regal von **Systemkästen**. Nicht fokussierte Systeme stehen als **Rücken** (schmal), das fokussierte dreht sich **mit der Front nach vorn** (breit). Das ist die animierte Plattformwahl.

```
1920 × 1080
┌─────────────────────────────────────────────────────────────────────────────────────────┐
│ Kopfleiste                                                                              │ 72
│                                                                                         │
│       ┌──┐┌──┐ ┌──────────────────────────────┐ ┌──┐┌──┐┌──┐┌──┐┌──┐┌──┐┌──┐┌──┐┌──    │ 160
│       │N ││S │ │ Game Boy                     │ │G ││G ││N ││3 ││P ││P ││P ││M ││      │
│       │E ││N │ │ Advance                      │ │B ││B ││D ││D ││S ││S ││S ││D ││      │   Rücken 128×460
│       │S ││E │ │                              │ │C ││  ││S ││S ││1 ││2 ││P ││  ││      │   (Code gedreht)
│       │  ││S │ │ Nintendo, 2001               │ │  ││  ││  ││  ││  ││  ││  ││  ││      │
│       │  ││  │ │                      ▟████▙  │ │  ││  ││  ││  ││  ││  ││  ││  ││      │   Front 520×600
│       │6 ││8 │ │ 7 Spiele             █▀▀▀▀█  │ │2 ││3 ││5 ││4 ││6 ││5 ││4 ││3 ││      │
│ ══════╧══╧╧══╧═╧══════════════════════════════╧═╧══╧╧══╧╧══╧╧══╧╧══╧╧══╧╧══╧╧══╧╧═══ │ 760  Regalkante
│                                                                                         │
│       NES  SNES  [GBA]  GBC  GB  NDS  3DS  PS1  PS2  PSP  MD          Fach 3 von 13      │ 800  Ticker als Index
│                                                                                         │
│       Ohne Spiele:  Wii · Wii U · Switch · Saturn …   (Textbuttons, fokussierbar)        │ 880
├─────────────────────────────────────────────────────────────────────────────────────────┤
│ Konsolen › Game Boy Advance                     (LB/RB) Springen  (A) Öffnen  (B) Zurück  │
└─────────────────────────────────────────────────────────────────────────────────────────┘
```

- **Rücken** (unfokussiert): 128×460, R 0. Wand-hell-Fläche, oben 8-px-Band in Farbe, Code gedreht −90° (Archivo Condensed Bold 28, Papier), unten Anzahl `RhTextNumberS` (28). Textur 6 %. Abstand 8. Alle stehen auf der Regalkante y 760.
- **Front** (fokussiert): 520×600, Fachrückwand komplett. Name Archivo Expanded ExtraBold 44 (2 Zeilen, 40 px Innenrand), „Nintendo, 2001" Poppins 18 Text 2, „7 Spiele" `RhTextNumber` (40) unten links, Motiv 72 % Höhe rechts unten. Fokusring 3 px Licht (siehe 8).
- **Fokus-Anker**: Die Front steht in Slot 3 (x = 96 + 2 Rücken + 16). Die Reihe gleitet darunter.
- **Animation pro Wechsel**: Das alte Element schrumpft von 520 auf 128 Breite, das neue wächst (Breiten-Transition 220 ms CubicEaseOut; nur 2 Elemente ändern das Layout, das ist bei ≤ 32 Elementen unkritisch). Der Inhalt der Front blendet 120 ms nach dem Wachsen ein (140 ms). Der Hintergrund wechselt zur Wand des neuen Systems (`BrushTransition` 280 ms).
- **Ticker**: Codes (17) aller Systeme mit Spielen, aktiv Papier mit Unterstrich in Farbe, rechts „Fach 3 von 13".
- **Ohne Spiele**: Textbuttons Poppins 16 Text 3 in einer Zeile. A öffnet die Systemseite mit Leerzustand und Ordner-Hinweis.
- **Maus**: Klick auf einen Rücken fokussiert und öffnet ihn; das Mausrad scrollt horizontal.
- **≈ 1100 px**: Front 380×460, Rücken 96×360, Name 36.

---

### 7.5 Systemseite: „Das Fach"

```
1920 × 1080
┌─────────────────────────────────────────────────────────────────────────────────────────┐
│ Kopfleiste (auf Wand-Farbe)                                                             │ 72
│▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓│
│▓  [LB] ‹ GBC                                                         GB › [RB]        ▓│ 96   Nachbarn
│▓                                                                         ▟███████▙    ▓│
│▓  Game Boy Advance                     (Plakat 56 → 72 bei kurzen Namen)  █▛▀▀▀▀▀▜█    ▓│ 176
│▓  Handheld von Nintendo, 2001.                                          █▌ ▓▓▓▓ ▐█    ▓│ 260  BodyL
│▓                                                                        █▙▄▄▄▄▄▟█    ▓│
│▓  7         46 Std.      heute                                          ▀███████▀    ▓│ 316  Zahlen 40
│▓  Spiele    gespielt     zuletzt                                         (Motiv 300)  ▓│
│▓  ● mGBA ist bereit   ⋯ Systeminfo                                                    ▓│ 384  Status
│═════════════════════════════════════════════════════════════════════════════════════════│ 440  Regalkante
│                                                                                         │
│   ▯▯▯  ▯▯▯  ▯▯▯  ▯▯▯  ▯▯▯  ▯▯▯  ▯▯▯  ▯▯▯     (Grid, Fachformat nach Boxen des Systems)    │
│ ════════════════════════════════════════════════                                        │
│   Titel …                                                                               │
├─────────────────────────────────────────────────────────────────────────────────────────┤
│ Konsolen › Game Boy Advance, Spiel 1 von 7      (LB/RB) System  (A) Öffnen  (B) Zurück    │
└─────────────────────────────────────────────────────────────────────────────────────────┘
```
- **Fachrückwand** y 72–440 (34 % der Höhe), randlos, scrollt mit dem Grid (`CoverGrid.Header`). Inhalt:
  - Wand-hell → Wand, Textur 8 %, Motiv 300 px hoch rechts (rechter Rand 120), stehend auf der Kante.
  - Nachbarn oben als Textbuttons mit LB/RB-Kappen.
  - Name Plakat, Satz BodyL, Zahlenreihe (`RhTextNumber` 40 plus Poppins 15 Text 2) für Spiele, Spielzeit, zuletzt gespielt. Nur vorhandene Werte; ohne Spielzeit nur „7 Spiele".
  - **Emulator-Status** als eine Zeile: Statuspunkt 8 px plus Text Poppins 16. Wenn kein Emulator: Warnungs-Icon plus „Kein Emulator eingerichtet" plus Button „Einrichten".
  - „⋯ Systeminfo" öffnet ein Overlay mit Ordnern, Endungen und BIOS. Das bisherige Status-Panel rechts entfällt.
- **Raum**: Unter der Kante bleibt die Wandfarbe als Verlauf erhalten (Wand bei 60 % → Raum bis y 900). Man ist sichtbar „in diesem Fach".
- **Grid**: Fokusring und Spotlicht in **Licht** des Systems. Etikett-Code entfällt (nur Jahr/Region). **Fachformat**: Startwert aus der BoxAspect-Tabelle (11.4), danach der Median der geladenen Cover-Seitenverhältnisse (einmalige Neuberechnung, wenn ≥ 8 Cover geladen sind und der Median > 8 % abweicht).
- **Systemwechsel mit LB/RB**: Wand `BrushTransition` 280 ms. Motiv: das alte gleitet 32 px aus und blendet aus (160 ms), das neue gleitet ein (220 ms). Das Grid wird ausgetauscht (Seiteneintritt 280 ms).
- **Wenige Spiele (≤ 6)**: Unter dem Grid steht ein „Fachende": eine Linienzeichnung, die sagt „Weitere Spiele kommen über Importe ins Fach." Dazu ein Button **Importe öffnen**.
- **≈ 1100 px**: Wand 320 hoch, Motiv 200, Zahlen 28.

---

### 7.6 Werkbank-Seiten (Emulatoren, Importe, Einstellungen)

Gemeinsame Sprache: **flache Listen auf Raum**, Haarlinien, Inversions-Fokus, keine Karten in Karten, kein Fernsehlicht. Inhaltsbreite max. 1280, linksbündig bei x 96; ab logischer Breite ≥ 1600 rechts eine **Inspektor-Spalte** (x 1440–1824) mit Details zum fokussierten Eintrag.

**Emulatoren**
```
Emulatoren                                              (Poster 40)
Erkannte Programme auf diesem PC. RetroHub lädt nie etwas ohne deine Zustimmung herunter.   (Body 18, 1 Zeile)
───────────────────────────────────────────────────────────────────────────────
[Chip] mGBA                         ✓ Installiert, 0.10.3          GB GBC GBA    ›    88 px
───────────────────────────────────────────────────────────────────────────────
[Chip] Dolphin                      ▲ Nicht gefunden               GC Wii        ›
───────────────────────────────────────────────────────────────────────────────
                                                     [Neu suchen]  (Sekundär, unten)
Inspektor (fokussierte Zeile): Pfad (Mono 15), Startmodi als Satz, Kerne, [Ordner öffnen] [Website]
```
- Zeile 88 px: Medientyp-Icon `Emulators` 28 (statt Buchstaben-Avatar), Name Archivo Expanded ExtraBold 22, Status als Icon plus Text (OK grün / Warnung), unterstützte Systeme als Etiketten (Farbbalken plus Code). Fokus: Inversion. Die Aktionen liegen im Inspektor bzw. bei < 1600 als ausklappender Bereich unter der Zeile (A klappt auf, 180 ms Höhe).

**Importe**
```
Importe
───────────── Bibliotheksordner ─────────────────────────   ───── Letzter Scan ─────
games\  (eingebaut)                     Aktiv     ⏻          Heute, 14:02
E:\Retro\PS2                            Aktiv     ⏻  ⋯        412 Dateien geprüft, 12 neu
E:\Retro\Alt                            Pausiert  ⏻  ⋯        3 Hinweise ›
[+ Ordner hinzufügen]                                         [ Jetzt scannen ]  (Bernstein)
                                                               ▮▮▮▮▮▮▮▮▯▯▯▯▯▯ 58 %  Segmentbalken
Cover suchen: 31 von 66 Spielen ohne Cover. [Cover suchen]
Spieldateien werden nie verändert.
```
- Ordnerzeilen 72 px. Pfad Poppins Medium 18 (nicht fett), Zustandswort als Text (kein Badge-Zoo), Kippschalter, Mehr-Menü.
- Eine Überschrift pro Gruppe als `RhTextH2` mit Haarlinie rechts daneben.
- **Fortschritt als Segmentbalken**: 14 Segmente à 24×10, Abstand 4, Bernstein gefüllt / Regal hell leer, Prozent in `RhTextNumberS`. Das ist die „Lade-Ära"-Anmutung; der Balken ist determiniert. Bei unbestimmter Dauer läuft ein Segment-Lauflicht (3 Segmente wandern, 80 ms pro Schritt).
- Toasts unten rechts mit Abstand zur Fußleiste. Sie verdecken nie die Scan-Spalte (Toast-Host bei Breite ≥ 1600 links von der Inspektor-Spalte).

**Einstellungen**
```
Einstellungen
Darstellung      │ Lichtfarbe          ● Bernstein  ○ Phosphor  ○ Röhrenweiß …
Bewegung         │ Bewegung            Wie Windows   [Voll]   Reduziert   Aus
Steuerung        │ Hintergrund folgt Auswahl                         [■■■□]  Kippschalter
Sprache          │ Kopfleiste: Uhr anzeigen                          [■■■□]
Über RetroHub    │ Vollbild beim Start                               [□■■■]
(Abschnittsindex │ Größe der Oberfläche   Automatisch  90 %  100 %  110 %  125 %
 280 px links)   │
```
- Links ein Abschnittsindex (280 px; Fokus springt mit LB/RB). Rechts Zeilen von 72 px: Label Poppins Medium 20 Papier, Beschreibung Poppins 15 Text 3 (nur wo nötig, 1 Zeile).
- **Kippschalter**: 64×32, R 4, Schieber 26×26 R 2. An: Bernstein-Fläche, Schieber Tinte. Aus: Regal hell, Schieber Text 3.
- **Segment-Auswahl**: Text-Optionen mit 3-px-Unterstrich (wie Tabs), kein Gradient.
- **Lichtfarben-Muster**: Quadrate 32×32 R 2. Aktiv mit Papier-Ring 2 px bei 3 px Abstand.
- Neu: „Größe der Oberfläche" (UiScale-Override), „Hintergrund folgt Auswahl", optional „Navigationsklang".

---

## 8. Fokus- und Auswahl-Sprache (Controller)

Grundsatz: **Der Fokus ist das Objekt, das ins Licht gehoben wird.** Er hat immer drei Komponenten: Hebung (Skalierung und Versatz nach oben), Licht (Ring und Spotlicht in Lichtfarbe) und Schatten nach unten. Andere Elemente ändern sich nicht. Gedimmt wird nur als Status.

### 8.1 Pro Elementtyp

| Element | Fokus-Darstellung | Zeit |
|---|---|---|
| **Cover-Kachel** (Grid, Regale) | Skalierung 1,06 mit Ursprung **unten Mitte** (wächst vom Regal nach oben). **Ring**: 3 px in Lichtfarbe (gemischt: Bernstein; System: Licht), 3 px Abstand zum Bild, außen R 4, *um das tatsächliche Bildrechteck* (nicht um den Slot). **Spalt**: der 3-px-Abstand ist Raum-Tief und trennt Ring und Bild auch bei hellen Covern. **Spotlicht**: RadialGradient hinter dem Cover, Zentrum (50 %, 10 %), Radien 80 %/65 % der Bildgröße × 1,6, Lichtfarbe 20 % → 0. **Schatten** `RhFocusShadow`. Bildunterschrift Text 2 → Papier. | Ring 0 → 1 in 90 ms linear; Skalierung 140 ms CubicEaseOut |
| **Fachschild / Rücken / Front** | wie Cover. Ring R 0 außen (Wände sind eckig). Kein Spot (die Wand ist das Licht). | wie oben |
| **Primärbutton „Spielen"** | Bernstein bleibt. Ring 3 px **Papier** bei 3 px Abstand (Raum-Tief-Spalt), Skalierung 1,03 | 90 / 140 ms |
| **Sekundärbutton, Werkzeug, Tab** | **Inversion**: Fläche Papier, Text und Icon Tinte. Keine Skalierung. | 90 ms Farbwechsel |
| **Listenzeile, Auswahlzeile, Einstellungszeile** | Inversion plus 8-px-Balken links in Systemfarbe bzw. Bernstein | 90 ms |
| **Ticker-Eintrag** | Inversion der Code-Fläche (Papier, 6×4 Innenabstand, R 2), Unterstrich bleibt | 90 ms |
| **Kippschalter, Segment, Muster** | Ring 3 px Papier, 3 px Abstand | 90 ms |
| **Kopfleisten-Icon** | Inversion (48×48, R 4) | 90 ms |
| **Textfeld (Suche)** | Rahmen 2 px Bernstein, Fläche Regal, Cursor Bernstein | 90 ms |

### 8.2 Maus (Hover)

- **Kachel**: Ring bei 40 % Deckkraft, keine Skalierung, kein Spot. Klick = Fokus plus Aktion.
- **Buttons und Zeilen**: Fläche Regal hell (statt Inversion).
- Hover wechselt nie das Fernsehlicht. Nur der Fokus tut das, und ein Mausklick setzt den Fokus.

### 8.3 Verhalten

- **Schnelles Scrollen** (Auto-Repeat einer Richtungstaste aktiv, `MainWindow` setzt die Klasse `.nav-repeat` am Root): keine Skalierung (sofort 1,06 ohne Transition bzw. bleibt 1,0), Spot aus, Fernsehlicht pausiert. Nur der Ring springt. 120 ms nach Loslassen kehrt alles zurück.
- **Grenz-Feedback**: Drückt man über die erste oder letzte Kachel bzw. Zeile hinaus (und es gibt kein Ziel), läuft am fokussierten Element `TranslateX` 0 → −6 → +4 → 0 in 180 ms (Keyframes).
- **Kein Dauer-Pulsieren**, kein „Atmen". Der Fokus ist stark genug durch Ring, Spalt und Spot.
- **Wiederherstellen**: Jede Seite merkt sich das fokussierte Element (PageHost, bereits vorhanden). Regale merken den Index pro Regal.
- **Fokus nie verdeckt**: `BringRowIntoView`-Marge = 3 (Ring) + 3 (Spalt) + 6 % Skalierung von 245 (≈ 15) + 18 (Schatten) ≈ **40 px**.
- **Ein BoxShadow gleichzeitig**: Nur das fokussierte Element trägt `RhFocusShadow`.

---

## 9. Motion

### 9.1 Tokens (`Theming/Motion.cs` plus Ressourcen)

| Token | Dauer | Easing | Verwendung |
|---|---|---|---|
| `Blink` | 90 ms | Linear | Fokusring, Inversion, Farbwechsel |
| `Step` | 140 ms | CubicEaseOut | Kachel-Hebung, Button-Skalierung, Inhalt der Front einblenden |
| `Glide` | 220 ms | CubicEaseOut | Regal- und Karussell-Gleiten, Rücken ↔ Front, Suchfeld, Aufklapper |
| `Scene` | 280 ms | CubicEaseOut | Seiteneintritt, Systemwechsel (Wand), Overlays |
| `Light` | 420 ms | SineEaseInOut | Fernsehlicht-Überblendung |
| `Dwell` | 220 ms | — | Verweilzeit vor Fernsehlicht- und Bühnenwechsel |
| `DecodeDwell` | 50 ms | — | Cover-Decode erst nach kurzer Ruhe (bei Auto-Repeat) |
| `Year` | 600 ms | ExponentialEaseOut | Jahres-Wasserzeichen |
| `PowerOn` | 520 ms gesamt | siehe 9.2 | Start eines Spiels |

### 9.2 Was sich bewegt

- **Seiteneintritt**: Inhalt Opacity 0 → 1 und TranslateY 16 → 0 (280 ms); Rückwärts (B) TranslateY −12 → 0. Kopf- und Fußleiste bewegen sich nie. Keine Staffelung pro Kachel.
- **Bühnenwechsel (Home)**: Text Opacity 1 → 0 (120 ms), dann neu 0 → 1 mit TranslateX 8 → 0 (180 ms). Cover überblendet mit zwei Image-Ebenen (180 ms).
- **Regal-Gleiten**: TranslateX der Reihe 220 ms. Der Fokusring wandert nicht; er erscheint an der neuen Kachel (90 ms).
- **Konsolen**: Breite 128 ↔ 520 (220 ms), Inhalt der Front verzögert (+120 ms, 140 ms), Wand-Brush 280 ms.
- **Aufkleber „Neu"**: 250 ms auspacken (TranslateX/Y −12, Opacity → 0).
- **Starttransition „Einschalten"** (der eine inszenierte Moment):
  1. 0–220 ms: Das fokussierte Cover (bzw. Detail-Cover) skaliert zur Bildschirmmitte auf 1,4× (`TransformOperationsTransition`, CubicEaseIn). Der Raum dimmt auf Raum-Tief 94 %.
  2. 220–320 ms: Das Cover blendet aus. Eine **2-px-Papier-Linie** wächst aus der Mitte horizontal auf volle Breite (ScaleX 0 → 1).
  3. 320–520 ms: Die Linie öffnet sich vertikal zur Startfläche (ScaleY 2 px → volle Höhe, CubicEaseOut). Darin erscheint das Start-Overlay (Titelbild bzw. Cover 240×336, Titel Poster, Text „mGBA startet …", Segment-Lauflicht).
  4. Mindestdauer des Overlays bleibt 900 ms. Die alte GameScreen-Startdarstellung wird vollständig ausgeblendet (behebt das Doppel-Overlay).
  5. **Rückkehr aus dem Spiel**: Ausschalten. Inhalt kollabiert vertikal auf eine Linie (160 ms), die Linie auf einen Punkt (80 ms), dann blendet die Seite ein (Scene).
  6. Die Transition liegt nie über dem eingebetteten Emulatorfenster: Sie endet, bevor das HWND sichtbar wird.

### 9.3 Reduzierte Bewegung / Aus

- **`.motion-reduced`**: keine Skalierung, keine Translationen, kein Gleiten (Reihen springen). Überblendungen auf 100 ms. Fernsehlicht-Wechsel 200 ms ohne Verweilzeit-Änderung. Starttransition = Überblendung 150 ms. Jahr sofort.
- **`.motion-off`**: alle Transitions null; Fernsehlicht schaltet hart. Fokus bleibt voll sichtbar (Ring, Spot, Inversion sind statisch).
- Muster wie bisher: Zustandswerte in `^:state /template/ PART_x`-Styles, Transitions per Klasse am Root nullbar.

### 9.4 Performance-Budget

- **60 fps** bei Grid mit 8×3 sichtbaren Kacheln und gehaltener Richtungstaste auf einem Mittelklasse-iGPU-System.
- **Verboten auf Kacheln**: BoxShadow (außer dem einen fokussierten), BlurEffect, OpacityMask, pro Kachel erzeugte Brushes, animierte Verläufe.
- **BlurEffect** wird **nirgends** verwendet. Weichheit entsteht durch kleine Decodes und bilineare Skalierung.
- **Animiert werden nur** RenderTransform, Opacity und Brush (Wand). Ausnahme: Breite der zwei Konsolen-Elemente.
- **Texturen**: eine geteilte ImageBrush-Instanz je Datei; Korn als eine einzige Ebene.
- **Fernsehlicht**: höchstens zwei 48-px-Bitmaps im Speicher pro Ebene; keine Arbeit während Auto-Repeat.
- **Decodes**: Cover auf Slotbreite × Skalierung, bucketiert (bestehend). Bei Auto-Repeat erst nach `DecodeDwell` decodieren, Platzhalter bis dahin.
- **Messung**: Debug-Overlay (bestehende FPS-Anzeige oder `RendererDiagnostics`) im QA-Lauf mit 1000 Demo-Spielen.

---

## 10. Graceful Degradation

### 10.1 Bild-Fallback-Ketten (pro Fläche festgelegt)

| Fläche | Kette |
|---|---|
| Cover-Kachel, Detail-Cover, Bühnen-Cover | Cover → Titelbild (4:3 auf Wand mit 6 px Raum-Tief-Rahmen) → **Rücken-Platzhalter** |
| Fernsehlicht | Snap → Titelbild → Cover → Systemwand |
| Bildschirm (Home rechts, Detail-Galerie) | Snap → Titelbild → *entfällt* (Jahr bzw. Spalte rückt nach) |
| Konsolen / System | immer generiert (Wand + Motiv), keine Bilder nötig |

### 10.2 Rücken-Platzhalter (statt Initialen)

Gezeichnet in `CoverArt.DrawPlaceholder` (C#, gecachte Brushes und Geometrien). Seitenverhältnis 0,72 (B/H) hochkant, im Slot unten bündig:

```
┌──┬────────────────┐
│G │░░░░░░░░░░░░░░░░│  Grund: Wand hell → Wand (vertikal), Textur des Systems 8 %
│B │░░░░░░░░░░░░░░░░│  Rückenband: 16 % der Breite, Farbe, Code −90° gedreht
│A │░░░░░░░░░░░░░░░░│    (Archivo Condensed Bold, 0,09 × Breite, Text auf Farbe)
│  │░░░░░░░░░▟██▙░░░│  Motiv-Silhouette: Farbe 18 %, 45 % der Höhe, rechts unten
│  │Castlevania:░█░░│  Titel: Poppins Bold (Plan A: Archivo SemiExpanded Bold),
│  │Aria of░░░░░░░░░│    Größe = 0,105 × Breite (≈ 20 px bei 196), Papier,
│  │Sorrow░░░░░░░░░░│    linksbündig, unten verankert, 14 px Innenrand, max. 4 Zeilen
└──┴────────────────┘  Oben 1 px Lichtkante; R 2; Kontaktschatten wie echte Cover
```
- Variation pro Spiel: Der Textur-Versatz kommt aus `hash(Titel) % Kachelgröße`. Der Titel ist immer vollständig (Umbruch an Leerzeichen und Bindestrichen, keine Initialen).
- `ShowBadge` bzw. die Badge-Pille entfällt. `ShowTitle` bleibt.
- Der Platzhalter wird **nicht** abgedunkelt; „gedimmt" bleibt dem Status vorbehalten.

### 10.3 Fehlende Metadaten

| Fehlt | Verhalten |
|---|---|
| Beschreibung | Abschnitt „Über das Spiel" zeigt die Bildschirm-Galerie, sonst entfällt er; kein Ersatzsatz |
| Genre / Entwickler / Spieler / Region | Teil des Meta-Satzes entfällt. Builder: `[Genre][ von Entwickler][, Jahr]. [Spieler.] [Region.]`. Ist nur das System bekannt: „Game Boy Advance." |
| Jahr | kein Wasserzeichen, kein Jahr im Etikett |
| Spielzeit / zuletzt (nie gespielt) | Zahlenreihe ersetzt durch „Noch nie gespielt. Seit {Datum} in der Sammlung." Keine „–"-Zellen, kein „noch nie" in jeder Listenzeile |
| Cover bei allen Spielen eines Systems | Grid aus Rücken-Platzhaltern; wirkt wie ein Regal voller Spielrücken |
| Snaps/Titel (vor M4b) | Fernsehlicht aus dem Cover; Bildschirm-Fläche entfällt |
| Emulator | Spielen wird zu „Emulator einrichten"; Kachel gedimmt plus `NotReady`-Icon |
| Datei fehlt | Kachel gedimmt plus `Missing`; Detail: Statuszeile Fehler plus „Datei suchen" (Ordner öffnen) |
| Systemdaten (benutzerdefiniertes System) | `SystemPalette.From(accent)`, Motiv Cart8, Textur bayer-8 |
| Keine Spiele | Linienzeichnung plus ein Satz plus eine Aktion (siehe Home, System) |

Texte für Leerzustände: Sie benennen, was fehlt, und die Lösung, z. B. „Kein Emulator für Game Boy Advance eingerichtet. Wähle einen in Emulatoren."

---

## 11. Umsetzung in Avalonia

### 11.1 Dateien

| Datei | Änderung |
|---|---|
| `Themes/Tokens.axaml` | Neue Werte für alle bestehenden Keys (Farben, Radien, Schatten), neue Keys: `RhRoomDeepColor/Brush`, `RhLightEdgeBrush`, `RhRadiusNone`, `RhRadiusControl`, `RhFocusShadow`, `RhShelfShadowBrush`, Font-Rollen `RhFontPoster/Label/Spine/Number`, Text-Stile als `RhText…`-Größen. Glas-Keys zeigen auf opake Werte. |
| `Themes/Typography.axaml` (neu) | `Style Selector="TextBlock.poster-xl"` usw. (Klassen statt Keys, damit sich kein Token-Key mit einem Style-Key überschneidet) |
| `Themes/Textures.axaml` (neu) | eine `ImageBrush` pro Textur (`RhTex…`) |
| `Themes/Motifs.axaml` (neu) | `StreamGeometry` je Motiv-Teil (`RhMotif.…`) |
| `Themes/Icons.axaml` | komplett neue Geometrien („gefaste Linie"), gleiche Keys plus neue |
| `Themes/Controls.axaml` | ControlThemes: `RhPrimaryButton` (Bernstein, Ring), `RhGhostButton` (Inversion), `RhTab`, `RhTickerItem`, `RhChoiceRow` (ComboBox als Auswahlzeile), `RhToggle` (Kippschalter), `RhShelfEdge`, `RhSystemSpine`; GameTile-Template ohne `PART_Glow`/`PART_Ring` (zeichnet `CoverArt`), mit `PART_Caption`, `PART_Label`, `PART_Sticker`, `PART_Fav`, `PART_Missing` |
| `Themes/Styles.axaml` | `.nav-repeat`, `.motion-*` erweitert, Typografie-Klassen einbinden |
| `Themes/FluentOverrides.axaml` | Fluent-Keys auf neue Neutrale, `ControlCornerRadius 4`, `OverlayCornerRadius 6` |
| `Theming/ThemeManager.cs` | Lichtfarben-Presets (2.2); `RhAccentGradientBrush` und `…DiagBrush` werden `SolidColorBrush` (Akzent A); `RhFocusRingBrush` = Solid; `RhFocusGlow` → Schatten; `RhButtonGlow` leer; `RhAmbientBrushA/B` → Deckenlicht in Bernstein 5 % / transparent |
| `Theming/SystemPalette.cs` (neu) | Tabelle 2.4 plus Formel; liefert Farbe, Zweitfarbe, Wand, WandHell, Licht, TextAufFarbe und gecachte Brushes (WallBrush, SpotBrush, RingBrush) |
| `Theming/MotifCatalog.cs` (neu) | System-Id → Motiv, Textur, BoxAspect |
| `Theming/Motion.cs` | Tokens aus 9.1 |
| `Theming/MetaSentence.cs` (neu) | Meta-Satz-Builder (de, über Lokalisierung) |
| `Controls/CoverArt.cs` | Contain plus unten bündig (`ArtRect`), Kontaktschatten, Fokusring und Spot selbst zeichnen (`FocusAmount` 0..1 als animierbare StyledProperty, AffectsRender), neuer Rücken-Platzhalter, Texturen |
| `Controls/CoverGrid.cs` | `CoverRowPresenter` zeichnet die Regalkante (Render-Override, statische Brushes); Bildunterschrift unter der Kante; `SlotAspect` pro Instanz; `BringRowIntoView`-Marge 40; Auto-Repeat-Erkennung (`IsRepeating`) |
| `Controls/Shelf.cs` | fester Fokus-Anker (Slot 1/2) mit TranslateTransform statt ScrollViewer-Sprung; unten bündig; „Alle anzeigen"-Rücken |
| `Controls/RoomBackdrop.cs` (neu) | Fernsehlicht (2.5): 2 Image-Ebenen, Scrims, Korn, Verweilzeit-Timer, Stärke pro Seite (`Intensity`) |
| `Controls/SystemWall.cs` (neu) | zeichnet Fachrückwand (Wand-Verlauf, Textur, Motiv) für Banner, Front, Rücken, Fachschild; Parameter `SystemId`, `MotifScale`, `ShowMotif` |
| `Controls/SystemShelf.cs` (neu) | Konsolen-Karussell (Rücken ↔ Front, eigene Links/Rechts-Behandlung im Tunnel wie CoverGrid) |
| `Controls/YearMark.cs` (neu, optional) | Jahres-Wasserzeichen mit Einblendung |
| `Controls/SmallControls.cs` | `StatTile` wird nicht mehr verwendet (bleibt bis Aufräumen in M6) |
| `Views/MainWindow.axaml(.cs)` | Sidebar → Kopfleiste; `RoomBackdrop` hinter PageHost; Fußleiste mit Ortssatz; `UiScale` (`LayoutTransformControl`); Menu → Kopfleiste; Rand-Links-Sprung entfernen; `.nav-repeat` setzen |
| `Views/Pages/*.axaml` | neu nach Abschnitt 7 |
| `Assets/Fonts/` | 4 Archivo-Dateien plus OFL (nach Freigabe) |
| `Assets/Textures/` (neu) | generierte PNGs |
| `tools/TextureGen/` (neu, Repo-Root) | SkiaSharp-Konsolenprojekt, `dotnet run -- <out>` |
| Core (M4b) | `ArtworkKind.Snap`, `ArtworkKind.Title`; `LibretroThumbnails` holt `Named_Snaps`/`Named_Titles`; VM-Properties `SnapPath`, `TitlePath`, `BackgroundPath` |

### 11.2 Asset-Pipeline

1. **Motive und Icons** werden als SVG-Pfad-Strings entworfen (Raster 120 bzw. 24, z. B. in Inkscape oder Figma mit Pixel-Snap), dann **als Pfaddaten in XAML übernommen**. Keine SVG-Dateien zur Laufzeit, keine SVG-Bibliothek. Mehrfarbige Motive sind zwei bis drei `StreamGeometry`-Teile (Body, Label, Detail), gefärbt vom `SystemWall`-Renderer.
2. **Texturen**: `tools/TextureGen` erzeugt alle PNGs aus Tabelle 4.4 deterministisch. Die PNGs werden committet und als `AvaloniaResource` eingebunden.
3. **Schriften**: nach Freigabe aus Google Fonts (ZIP), Familiennamen per fontTools prüfen, nur die vier statischen Schnitte.
4. **Palette**: `tools/palette.py` (bzw. ein Unit-Test in C#) prüft alle Kontraste aus 2.4. Ein Test schlägt fehl, wenn Licht/Wand hell < 4,5 oder Text auf Farbe < 4,5.

### 11.3 Meilensteine

| # | Inhalt | Fertig, wenn |
|---|---|---|
| **M1 Tokens, Shell, Fokus** | Tokens (Farben, Radien, Schatten, Typo Plan B), ThemeManager-Umbau, Icons neu, Kopfleiste statt Sidebar, Fußleiste mit Ortssatz, UiScale, Korn, Fokus-System (CoverArt zeichnet Ring/Spot/Kontaktschatten; Inversion für Buttons/Zeilen), `.nav-repeat`, Motion-Tokens, SystemPalette | Keine Violett-/Blau-Werte mehr im UI (grep `7B61FF|4C8DFF`), keine Radien > 6; alle Seiten bedienbar mit Kopfleiste; Fokus auf jedem Element aus 3 m erkennbar |
| **M2 Start** | RoomBackdrop (Fernsehlicht), Bühne mit Fokus-Folge, Regale mit Anker, Fachschilder, SystemWall (zunächst nur Wand plus Textur, Motive als Platzhalter-Cart), Leerzustand | Home 1920/1600/1100 Screenshots; StatTiles und Begrüßung entfernt |
| **M3 Bibliothek** | Ticker mit LB/RB, Rückenleiste, Banner, Regal-Grid mit Kanten und Stand-Fit, Rücken-Platzhalter, Aufkleber, Listen-Ansicht mit Vorschau; Archivo (falls freigegeben) | 1000-Spiele-Demo bei 60 fps; Platzhalter-Wand lesbar |
| **M4 Detail** | Lampe, Aktionsreihe, Zahlen/Satz, Jahr, Auswahlzeilen, Mehr-Overlay, Dateiinfo-Overlay, Lösungs-Button; **M4b**: Snaps/Titel in Core, Bildschirm-Galerie, Fernsehlicht aus Snap | Detail ohne Beschreibung/Genre sieht vollständig aus |
| **M5 Konsolen & System** | Alle Motive final (6.2/6.3), Texturen komplett, SystemShelf (Rücken ↔ Front), Systemseite mit Fachrückwand und Wechsel, BoxAspect plus Median | jedes System auf einen Blick unterscheidbar ohne Text |
| **M6 Werkbank & Start** | Emulatoren, Importe, Einstellungen im Listen-Stil; Segmentbalken; Starttransition „Einschalten"; Doppel-Overlay behoben; Aufräumen (StatTile, Glas-Keys, alte Radien) | Alle Seiten im QA grün |

### 11.4 BoxAspect-Startwerte (H/B des Cover-Slots auf der Systemseite)

NES 1,38 · SNES 1,00 (US quer, EU/JP hoch; Median korrigiert) · N64 0,90 · GB/GBC/GBA 1,00 · NDS 0,90 · 3DS 0,86 · GC/Wii/Wii U 1,40 · Switch 1,62 · PS1 1,00 · PS2 1,42 · PSP 1,72 · MD/SMS 1,40 · Saturn 1,00 · Dreamcast 1,00 · GG 1,25 · Arcade 1,40 · PC 1,40 · sonst 1,25.

### 11.5 Visuelle QA-Checkliste (nach jedem Meilenstein, mit echten Screenshots)

Screenshots unter `artifacts/screens/` in **1920×1080, 1600×900, 1100×700** und **2560×1440**, jeweils Demo-Katalog *und* echter Katalog (mit grauen und hellen Akzenten), dazu eine Bibliothek mit 0 % Covern.

**Identität**
- [ ] Kein `#7B61FF`/`#4C8DFF`, kein Violett-Blau-Verlauf, keine Glas-Flächen (grep plus Sichtprüfung)
- [ ] Keine Logos, Wortmarken, Hardware-Fotos, kein Rot/Blau-Schienenpaar, keine vierfarbigen Knopf-Rauten
- [ ] Radien nur 0/2/4/6 (grep `CornerRadius`); Kreise nur Glyphen und Punkte
- [ ] Keine ALL-CAPS-Overlines (Ausnahme: Systemcodes)
- [ ] Bernstein nur auf Spielen/Primär, Fokus gemischt, Wortmarke, Aufkleber, Fortschritt

**Lesbarkeit (3 m)**
- [ ] Kein lesbarer Text < 15 px (bei UiScale 1,0); Text 3 nie unter 15 px
- [ ] Kontraste: Text ≥ 4,5 : 1, Fokusring ≥ 3 : 1 gegen Raum *und* gegen helles Cover (Raum-Tief-Spalt vorhanden)
- [ ] Lange Titel („Castlevania: Symphony of the Night", „Super Mario Advance 4: Super Mario Bros. 3") brechen sauber, keine Überlappung
- [ ] Fokus aus 3 m auf jedem Screenshot in < 1 s auffindbar (Testperson)

**Komposition**
- [ ] Bilder bzw. Farbe besetzen die obere Hälfte von Start, Detail, System; Chrome < 15 % der Fläche
- [ ] Cover stehen auf Kanten (unten bündig), kein Cover beschnitten
- [ ] Keine Karten in Karten; Panels nur bei Overlays
- [ ] Fehlende Daten: keine „–", kein „Genre:", keine Ersatzsätze für Beschreibung
- [ ] 1100 px: kein horizontales Abschneiden (Ticker hat Rand-Verläufe), Fokus nie hinter Fußleiste

**Plattformen**
- [ ] Alle Systeme mit Spielen sind im Konsolen-Screenshot ohne Lesen unterscheidbar (Farbe, Motiv, Textur)
- [ ] Graue/helle Systeme (NDS, PS1, PSP, Wii, GB, Lynx, 32X) wirken nicht wie Platzhalter, Text auf Farbe ≥ 4,5

**Bewegung und Leistung**
- [ ] Bildschirmaufnahme: Fokuswechsel, Regal-Gleiten, Konsolenwechsel, Fernsehlicht (erst nach Verweilzeit), Start-Einschalten, Rückkehr
- [ ] `.motion-reduced` und `.motion-off` geprüft (keine Skalierung bzw. keine Transition, Fokus sichtbar)
- [ ] 60 fps bei gehaltener Taste im 1000er-Grid; kein Fernsehlicht-Flackern beim schnellen Scrollen
- [ ] Nur ein Element mit BoxShadow gleichzeitig (Visual-Tree-Dump im Debug)

**Verhalten**
- [ ] B stellt den vorherigen Fokus wieder her (Konsolen → System → Detail → zurück)
- [ ] Fußleiste zeigt passende Hinweise zum fokussierten Element und den Ortssatz
- [ ] Start-Overlay ohne doppelte Titel/Fortschrittsbalken; nichts liegt über dem Emulatorfenster

---

## 12. Was entfällt (Abgleich mit dem Audit)

- Sidebar 248/84 px, Markenquadrat mit Play-Dreieck, „RETRO-LAUNCHER"-Overline
- Begrüßung „Guten Tag / Was spielen wir heute?", 4 StatTiles, Hero-Watermark „GBA"
- Initialen-Platzhalter mit Kreisscheibe und Ringen, Badge-Pillen oben links
- Gradient-Fokusring, Glow-Schatten, Gradient-Buttons mit Glanzkante
- Dimm-Ebene auf allen unfokussierten Kacheln (Dimmen nur noch als Status)
- System-Chips mit Rahmen (ersetzt durch Ticker), Tabellen-Liste mit Spalten
- 4 Statistik-Karten im Detail, Panel „Start-Einstellungen" mit Combos, Dateipfad im Hauptfluss, Ersatzsatz für fehlende Beschreibung
- Konsolen-Kategoriekacheln mit Geister-Text und zwei generischen Icons
- Status-Panel als Hero auf der Systemseite
- Buchstaben-Avatare bei Emulatoren, Karten-in-Karten bei Importen, Windows-Settings-Klon
