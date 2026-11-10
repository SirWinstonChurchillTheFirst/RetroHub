# RetroHub Designsprache „Fernsehlicht“

Version 1.1 · Art Direction · Stand 2026-11-10 (umgesetzt bis M6)
Ersetzt v1.0 (`docs/design/DESIGN-SPEC-v1.0.md`, bleibt als Historie). Grundlage für v1.1 ist die Kritik an v1.0 (Wertung 7/10). Jeder Kritikpunkt ist in Abschnitt 13 entschieden.
Gilt für RetroHub.App (Avalonia 12.1.5, .NET 10). Nur Dark Mode. Alle Maße beziehen sich auf eine Referenzfläche von 1920×1080 logischen Pixeln und werden über `UiScale` skaliert (siehe 7.0).

**Priorität des Owners:** wenige, sehr gut gemachte Screens. Zuerst kommen **Start**, **Bibliothek** und **Spiel-Detail**. Bilder sollen präsent sein, die App soll eine eigene, handgemachte Identität haben und nicht wie ein KI-Dashboard aussehen. Es gibt keine Marken, Logos oder Trade Dress, auch keine farbigen ABXY-Tasten, keine PlayStation-Symbole und keine Switch- oder Wii-Silhouetten.

---

## 0. Kurzfassung in zwölf Sätzen

1. **Metapher**: das Spielzimmer um elf Uhr nachts. Die einzige Lichtquelle ist ein *sichtbarer* Fernseher. Sein Licht fällt auf die Regalbretter, auf denen die Sammlung steht.
2. **Nichts schwebt.** Alles steht auf einem Brett oder hängt an einem Brett. Es gibt keine Karten und keine Panels, außer bei echten Overlays (Dialog, Auswahl).
3. **Sieben Objekte** bilden die ganze App: *Brett*, *Schachtel*, *Rücken*, *Etikett*, *Wand*, *Bildschirm* und *Lichtkegel* (1.2). Ein neues UI-Element ist eines davon oder entfällt.
4. **Raum**: warmes Braunschwarz #14120F, opak, ohne Violett. Farbe kommt nur aus Spielbildern und aus den Gehäusen der Systeme.
5. **Standby-Bernstein #FFB547** ist die einzige Markenfarbe. Ihre Einsatzliste ist abschließend und per grep prüfbar.
6. **Systeme sind Gehäuse.** Jedes System hat eine hart zweigeteilte, gestrichene Wand im Material seines Gehäuses, ein Farbband, ein Kategorie-Motiv und eine Ära-Textur. Logos gibt es nicht.
7. **Typografie**: Archivo in vier Breiten ist die Stimme von Verpackung und Bedienung (Expanded für Plakate, Condensed für Rücken, SemiCondensed für UI). Atkinson Hyperlegible Next ist die Leseschrift. **Zahlen sind immer Archivo.**
8. **Fokus heißt, das Objekt wird ins Licht gehoben.** Es wächst vom Brett nach oben, ein Lichtkegel fällt von oben, das Brettstück darunter wird hell, und ein gefaster Ring umgibt es. Kleine Elemente invertieren stattdessen.
9. **Start**: Der Fernseher und die Schachtel des Spiels stehen auf dem Bühnenbrett. Darunter laufen Bretter mit Papier-Etiketten.
10. **Bibliothek**: ein dichtes, ruhiges Regal ohne Bild-Hintergrund. Oben stehen ein Ticker als Fach-Filter und der *Regalspiegel* als Positionsleiste.
11. **Spiel-Detail**: Die Schachtel ist aus dem Regal gezogen und zeigt einen generierten Rücken. Darunter liegt die *Rückseite* mit Text, Bildschirmfotos und Startzeilen.
12. **Bewegung** ist schnell und ruhig (90/140/220 ms). Es gibt genau einen inszenierten Moment: das Einschalten beim Spielstart.

---

## 1. Konzept

### 1.1 Die Metapher und was sie entscheidet

| Im Zimmer | In der UI | Konsequenz |
|---|---|---|
| Dunkler Raum | Grundfläche `Raum` #14120F (warm, opak) | Nichts leuchtet ohne Lichtquelle |
| Röhrenfernseher | Objekt **Bildschirm** (4:3, auf dem Bühnenbrett, Start) und die Bildschirmfotos auf der Rückseite (Detail) | Das Raumlicht hat eine sichtbare Quelle. Der Schein sitzt *am* Bildschirm bzw. an der Schachtel, nicht an einem unsichtbaren Punkt |
| Fernsehlicht auf den Brettern | Lichtkante aller Bretter in der Bildfarbe des fokussierten Spiels (eine geteilte Brush) | Wechselt das Spiel, wechselt das Licht auf jedem Brett |
| Regalbretter | Objekt **Brett** (Front 24 bzw. 10 px, Lichtkante, Schatten nach unten) | Cover stehen. Der Fokus wächst nach oben. Etiketten hängen am Brett |
| Spielschachteln | Objekt **Schachtel** (Cover, contain, unten bündig, nie beschnitten) | Gemischte Box-Formate sind gewollt |
| Spielrücken | Objekt **Rücken** (Platzhalter-Cover, „Alle anzeigen“, Systemrücken, Rücken der gezogenen Schachtel) | Fehlende Daten werden zu gestalteten Objekten |
| Preisschild am Regal | Objekt **Etikett** (Papier, Tinte, eckig) | Ersetzt Abschnittsüberschriften und „Alle anzeigen“-Links |
| Gestrichene Fachrückwand | Objekt **Wand** (zweifarbig hart geteilt, Farbband, Textur, Motiv) | Plattform-Identität über Material, Farbe und Form, nie über Marken |
| Leselampe | Objekt **Lichtkegel** (Fokus von oben) | Das einzige farbige Licht im UI. Kein Glow und kein Halo |
| Standby-LED | Bernstein-Quadrat in der Wortmarke | Eine Markenfarbe, sparsam |
| Kanalanzeige (OSD) | Kanalziffer und Systemcode, hart ein und aus | Tab-Wechsel und Spielstart sprechen die Sprache des Fernsehers |
| Einschalten der Röhre | Starttransition | Der einzige inszenierte Moment |

### 1.2 Objektkatalog (abschließend)

| Objekt | Form | Wo | Umsetzung |
|---|---|---|---|
| **Brett** | Front 24 px (Bühnenbrett, trägt Etiketten) oder 10 px (Regalkante im Grid). Fläche Regal hell #2A2620, oben 1 px `RhBoardLightBrush`, unten 1 px Kante #3A342B, darunter 16 px Schatten #000 45 % → 0 | Start, Detail, Bibliothek, Konsolen, Systemseite, Liste (Vorschau) | `ShelfBoard` (Render-Override, statische Brushes); im Grid zeichnet `CoverRowPresenter` das Brett selbst |
| **Schachtel** | Cover contain, unten bündig, horizontal zentriert, R 2, Kontaktschatten 10 px #000 55 % → 0 auf dem Brett | überall, wo Spiele stehen | `CoverArt` (Bild bzw. Platzhalter); der Kontaktschatten gehört zum Brett |
| **Rücken** | Hochkantes Feld, oben 8 px Farbband, Text −90° gedreht in Archivo Condensed | Platzhalter-Cover (10.2), „Alle anzeigen“, Systemrücken (Start, Konsolen), Schachtelrücken (Detail) | `CoverArt.DrawPlaceholder`, `SystemSpine`, `BoxSpine` |
| **Etikett** | Papier #F4ECDF, Tinte #14120F, Archivo SemiCondensed SemiBold 15, Innenabstand 8×3, R 0. Variante „Neu“ in Bernstein | Regaletiketten am Bühnenbrett, Abschnittsköpfe in Einstellungen, Aufkleber | `ShelfLabel` (Border + TextBlock) |
| **Wand** | Wand hell oben 38 %, Wand unten 62 %, dazwischen 1 px Lichtkante #17FFF3E0, oben 8 px Farbband, Textur, Motiv stehend rechts | Systemfront, Banner, Systemseite, „Kein Signal“ im Bildschirm | `SystemWall` (nur Volltonflächen, damit `BrushTransition` interpoliert) |
| **Bildschirm** | 4:3, 2 px Rahmen Raum-Tief #0A0908, R 0, Scanlines `scan-4x3` bei 18 %. *Die einzige Fläche mit Scanlines* | Start (Bühne), Detail (Rückseite) | `PixelScreen` (Gerätepixel-Skalierung, 7.0) |
| **Lichtkegel** | Trapez von oben, Lichtfarbe 14 % → 0, plus beleuchtetes Brettstück und gefaster Ring | fokussierte Schachtel, Wand oder Primärbutton | `FocusLayer` pro Grid/Regal (8.1) |

Was keines dieser Objekte ist, kommt nur als Overlay vor (Dialog, Auswahlliste, Mehr-Menü) oder als flache Zeile in den Werkbank-Seiten.

### 1.3 Warum das nicht generisch ist

- **Kein Hero auf Unschärfe, zweimal.** Start und Detail haben verschiedene Aufgaben und Kompositionen. Auf **Start** ist der *Bildschirm* der Held (was gleich läuft). Im **Detail** ist es die *Schachtel* (was man besitzt).
- **Keine Netflix-Reihen.** Es gibt keine H2-Überschrift mit „Alle anzeigen ›“ über einem Karussell. Das Etikett hängt am Brett unter den Spielen, „Alle anzeigen“ ist ein Rücken am Ende des Bretts.
- **Kein KPI-Dashboard.** Zahlen stehen in Sätzen und sind dort nur hervorgehoben („**46** Stunden in **41** Sitzungen, zuletzt heute“). Es gibt keine Zahlenspalten und keine Summen-Überschrift mit Balkendiagramm.
- **Kein Glow, kein Halo, kein Glas.** Das Licht hat eine Richtung (von oben bzw. vom Bildschirm) und landet auf dem Brett.
- **Kein Standard-Fokusring.** Der Ring ist gefast wie die Icons und wird nur für Objekte verwendet (Schachtel, Wand, Primärbutton).
- **Keine Dribbble-Schrift.** Archivo mit Breitenkontrast spricht wie Spielverpackungen der 90er, Atkinson liest sich auf drei Meter.
- **Kein Retro-Filter.** Korn gibt es nur im Hintergrund (gegen Banding), Scanlines nur im Bildschirm-Objekt.
- **Keine Marken-Glyphen.** Controller-Tasten sind positionsbasiert und einfarbig (5.3).
- **Leerer warmer Raum ist Teil der Identität.** Freie Flächen werden nicht mit Wasserzeichen gefüllt.

### 1.4 Was wir von welcher Referenz nehmen (nur Prinzipien)

| Referenz | Übernommenes Prinzip | Wo in RetroHub |
|---|---|---|
| PS5 Home / Game Hub | Fester Fokus-Anker, die Reihe läuft darunter. Hintergrund folgt dem Spiel (bei uns nur Start und Detail) | Bretter auf Start, Konsolen |
| Xbox 2023 | Interaktion in der unteren Hälfte. Wechsel erst nach Verweilzeit | Start-Aufteilung, Verweilzeit 220 ms |
| Steam Big Picture | Feste Bild-Fallback-Kette pro Fläche, gestaltete Platzhalter, kontextabhängige Fußleiste | 10.1, Rücken-Platzhalter, Fußleiste |
| Nintendo Switch HOME | Ruhige, flache Chrome. Sofortiges Fokus-Feedback | Kopfleiste, Fokus in 90 ms |
| Nintendo Classics | Ein Raum pro Plattform als Farbfläche | Systemseite, Wand |
| Neoretro (Pegasus) | Positionszähler n/N | Fußleiste „12 von 66“ (das Jahres-Wasserzeichen entfällt in v1.1) |
| Analogue OS | Medium als Identität (Modul statt Logo), Haarlinien statt Karten, Inversion für kleine Elemente | Motive, Ticker, Kopfleiste |
| Daijishō | Systemfarbe färbt die Auswahl. Meta als Satz. Ortsangabe in der Fußleiste | Systemlicht-Fokus, Meta-Sätze, Ortssatz |
| RetroArch XMB/Ozone | Eine Grunddauer, Bilder erst nach kurzer Ruhe laden, Grenz-Feedback | Motion-Tokens, DecodeDwell |
| Playdate | Gestaltete typografische Spielkarten, Ritual für Neues | Rücken-Platzhalter, Aufkleber „Neu“ |
| LaunchBox / Big Box | Ebenenstapel Konsolen → System → Spiel, B geht genau eine Ebene zurück | Navigation |
| Röhrenfernseher (kein Produkt) | Kanalanzeige, Einschaltlinie, Service-Menü | OSD, Start, Einstellungen |

Bewusst **nicht** übernommen: Logos und Wortmarken, Hardware-Fotos, Trade Dress (Classic-Mini-Skins, rot-blaue Schienenpaare, vierfarbige Knopf-Rauten, farbige ABXY-Tasten, PlayStation-Symbole), Video, Live-Blur, Promo-Kacheln, pulsierende Dauer-Animationen und Jahres-Wasserzeichen.

---

## 2. Farbsystem

### 2.1 Grundneutrale (warm, opak)

| Token | Bestehender Key | Hex | Verwendung |
|---|---|---|---|
| Raum-Tief | `RhRoomDeepColor` (neu) | #0A0908 | Bildschirmrahmen, Letterbox, Spalt im Fokusring, Start-Overlay |
| Raum | `RhBaseColor` | #14120F | App-Grund |
| Regal | `RhSurfaceColor` | #1E1B17 | Overlays, Eingabefelder |
| Regal hell | `RhSurfaceHiColor` | #2A2620 | Brettfront, Hover, Fokuszeile, Sekundärbuttons |
| Kante | `RhLineColor` | #3A342B | Brett-Unterkante, Trennlinien (opak) |
| Lichtkante | `RhLightEdgeBrush` (neu) | #17FFF3E0 | Teilungslinie der Wände, Overlays oben |
| Brettlicht | `RhBoardLightBrush` (neu, **eine** geteilte `SolidColorBrush`, dynamisch) | siehe 2.5 | Oberkante aller Bretter |
| Papier | `RhText1Color` | #F4ECDF | Primärtext, Etiketten, Inversion (15,9 : 1 auf Raum) |
| Text 2 | `RhText2Color` | #C9BFAF | Sekundärtext (10,3 : 1) |
| Text 3 | `RhText3Color` | #948A7A | Tertiärtext, nur ≥ 15 px (5,5 : 1 auf Raum, 5,0 : 1 auf Regal) |
| Tinte | `RhTextOnAccentBrush` | #14120F | Text auf Papier, Bernstein und hellen Systemfarben |

Regeln:
- **Kein reines Schwarz und kein reines Weiß** in Flächen. Schatten verwenden #000 mit Alpha.
- **Keine Alpha-Weiß-Flächen.** Hover und Auswahl sind opak.

### 2.2 Signaturfarbe „Standby-Bernstein“

| Rolle | Hex | Kontrast |
|---|---|---|
| Bernstein (Akzent A) | #FFB547 | 10,6 : 1 auf Raum; Tinte darauf 10,6 : 1 |
| Bernstein tief (gedrückt) | #E59A2E | Tinte darauf 8,0 : 1 |
| Bernstein Text | #FFC877 | Links auf Raum |

**Einsatz, abschließend:** (1) „Spielen“ und andere Primäraktionen (`Ordner hinzufügen`, `Jetzt scannen`, `Speichern` in Dialogen), (2) Fokusring und Lichtkegel in gemischten Ansichten (Start, Bibliothek „Alle“, Suche), (3) Standby-Quadrat der Wortmarke, (4) Kanalmarke unter dem aktiven Tab und Ticker-Unterstrich bei „Alle“, (5) Aufkleber „Neu“, (6) Fortschritt und Fokusmarke im Regalspiegel. **Nirgendwo sonst.** QA: grep auf `#FFB547|RhAccent` und Abgleich mit dieser Liste.

**Lichtfarben** (Benutzer-Presets, Pipeline `ThemeManager.ApplyAccent` bleibt): Bernstein #FFB547 (Standard), Phosphor #8FE3A0, Röhrenweiß #EDE3D0, Eisblau #8CC4FF, Kirsche #FF8A80, Neonrosa #FF8CC6. Auf allen ist Tinte ≥ 8 : 1.

### 2.3 Statusfarben

| Status | Hex | Immer zusammen mit |
|---|---|---|
| Bereit/OK | #8FD694 | Häkchen-Icon |
| Warnung | #FF9A4A | Dreieck-Icon (unterscheidet sich vom Bernstein durch Form *und* Text) |
| Fehler | #FF6A5E | gefastes Achteck mit Kreuz |
| Favorit | #FFD36A | gefüllter Stern |
| Fenster schließen (Hover) | #C42B1C | OS-Konvention |

Es gibt keine Status-Pillen, nur Icon und Text. Flächen für Hinweise haben die Statusfarbe bei 14 % auf Regal.

### 2.4 Systeme: Gehäuse, Farbband, Licht

Neu in v1.1: Die Wand wird **aus dem Gehäusematerial** abgeleitet und nicht mehr aus dem Akzent. Viele Systeme teilen sich ein Material, so wie in einem echten Regal viele graue Geräte stehen. Unterscheidbar werden sie über Farbband, Motiv und Textur. Die Wand ist **zweifarbig hart geteilt**: oben 38 % *Wand hell*, unten 62 % *Wand*, dazwischen 1 px Lichtkante, am oberen Rand ein 8-px-Band in *Farbe*. Verläufe gibt es auf Wänden nicht.

**Gehäuse** (Werte berechnet und geprüft mit `scratchpad/design/tools/palette2.py`):

| Gehäuse | Wand | Wand hell | Papier auf Wand / Wand hell |
|---|---|---|---|
| warmgrau | #211F1B | #322E28 | 14,0 / 11,5 |
| kühlgrau | #1D2027 | #2D313B | 13,9 / 11,1 |
| schwarz | #0F0F11 | #18181B | 16,3 / 15,1 |
| anthrazit | #1C1C20 | #2A2A30 | 14,5 / 12,2 |
| violett | #1B1626 | #2B213B | 15,1 / 12,9 |
| weiß (dunkel gestrichen) | #2A2B2D | #3D3F43 | 12,1 / 9,0 |
| beige | #2A2418 | #3E3524 | 13,1 / 10,3 |
| holz | #24170E | #382419 | 14,9 / 12,5 |
| beere (transparent) | #20132A | #311D3F | 15,1 / 13,0 |

**Systeme** (Farbe und Zweitfarbe sind von Hand gesetzt wie in v1.0. Licht = HSL(h_Farbe, clamp(s, 0,25, 0,85), L ab 0,72), L steigt in 0,02-Schritten, bis Licht/Wand hell ≥ 4,5 *und* Licht/Raum ≥ 7,0):

| System | Gehäuse | Motiv | Farbe | Zweitfarbe | Licht | Licht/Wand hell | Text auf Farbe |
|---|---|---|---|---|---|---|---|
| nes | warmgrau | Cart8 | #C51F1B | #A8A39A | #EE8481 | 5,3 | Papier 5,0 |
| snes | warmgrau | Cart16 | #6652BE | #BDB7AD | #A397D8 | 5,1 | Papier 5,1 |
| n64 | anthrazit | Cart64 | #12A05E | #4A4A50 | #7FF1BC | 10,3 | Tinte 5,5 |
| gb | warmgrau | Handheld hoch | #8BAC0F | #306230 | #DAF47C | 11,0 | Tinte 7,1 |
| gbc | beere | Handheld hoch (doppelt) | #8E3BD0 | #2FB8A6 | #BD8CE3 | 5,8 | Papier 4,9 |
| gba | violett | Handheld quer | #5A46D9 | #A9A2E8 | #A499EA | 6,0 | Papier 5,4 |
| gamecube | violett | MiniDisc | #6450CC | #F29A3B | #A498E1 | 5,9 | Papier 5,0 |
| wii | weiß | Remote (Fernbedienung, geschwenkt; R2) | #3FC0EE | #E6EEF2 | #7CD4F3 | 6,3 | Tinte 8,9 |
| wiiu | weiß | Tablet (zweiter Bildschirm) | #12A0CC | #F0F0F0 | #7CD7F3 | 6,5 | Tinte 6,2 |
| nds | kühlgrau | Klappe | #8FA3B8 | #3C4654 | #A6B7C9 | 6,4 | Tinte 7,2 |
| 3ds | anthrazit | ClamshellWide (Klappe mit Tiefen-Treppe) | #C21D26 | #2B2B30 | #EC8389 | 5,6 | Papier 5,1 |
| switch | anthrazit | Dock (Handgerät über Sockel) | #C41F2A | #2E2E33 | #EB848B | 5,6 | Papier 5,0 |
| virtualboy | schwarz | TwinLens (Stereobrille) | #C8121C | #1A0505 | #F37C83 | 6,8 | Papier 5,0 |
| arcade | schwarz | Bedienfeld | #FF2E88 | #22E4FF | #F47BAF | 7,0 | Tinte 5,3 |
| pc | beige | Diskette | #C7B48A | #3FBF6A | #D1C19E | 6,8 | Tinte 9,2 |
| atari2600 | holz | Stick | #C0702A | #2A1A10 | #E5B58A | 7,9 | Tinte 5,0 |
| atari7800 | schwarz | StickGrip | #C8243A | #C0C0C0 | #E98693 | 7,0 | Papier 4,8 |
| lynx | schwarz | HandheldLong (sehr breit, Griffe) | #F2A21A | #2B2B2B | #F4C77B | 11,3 | Tinte 8,9 |
| pcengine | weiß | Card | #F05A28 | #E8E4DA | #F4997B | 4,9 | Tinte 5,5 |
| ngp | anthrazit | HandheldCompact | #2E5DB8 | #D9D4C8 | #8DAAE2 | 6,1 | Papier 5,3 |
| wonderswan | weiß | HandheldNarrow | #10A8B5 | #F0E6C8 | #7CEAF3 | 7,5 | Tinte 6,5 |
| msx | beige | Tastenblock | #3368A3 | #D8D2C4 | #92B6DD | 5,7 | Papier 4,9 |
| mastersystem | schwarz | CartWide (liegend) | #C42019 | #1A1A1A | #EF8580 | 7,0 | Papier 5,0 |
| megadrive | schwarz | CartGrip | #1B66B2 | #121212 | #83B7EC | 8,4 | Papier 5,0 |
| gamegear | schwarz | HandheldBig | #2D9CDB | #1A1A1A | #85C6EA | 9,5 | Tinte 6,1 |
| segacd | schwarz | Lade | #4A86D0 | #1A1A1A | #8EB3E2 | 8,2 | Tinte 5,0 |
| sega32x | schwarz | Modulstapel | #F2AE00 | #1A1A1A | #F4D27B | 12,1 | Tinte 9,6 |
| saturn | anthrazit | Disc mit Bändern | #5A5FB8 | #2A2A2E | #9B9ED4 | 5,6 | Papier 4,8 |
| dreamcast | weiß | Disc mit Halbton | #F26F21 | #EDEBE6 | #F4A87B | 5,4 | Tinte 6,3 |
| ps1 | kühlgrau | Disc mit Spur | #9AA0B4 | #3A3F4E | #A6AEC9 | 5,9 | Tinte 7,2 |
| ps2 | schwarz | Turm (Farbe auf L 0,55) | #2C4FD0 | #08090F | #899DE6 | 6,8 | Papier 5,7 |
| psp | schwarz | Handheld breit | #A9B4C8 | #0C0D10 | #A6B2C9 | 8,3 | Tinte 8,9 |

**Regeln und Test** (`SystemPaletteTests`, Core-unabhängig im App-Testprojekt):
1. Licht/Wand hell ≥ 4,5 und Licht/Raum ≥ 7,0.
2. Text auf Farbe ≥ 4,5. Sonst wird *Farbe* in 4-%-Schritten abgedunkelt.
3. **Unterscheidbarkeit**: Zwei Systeme mit gleichem Gehäuse *und* gleichem Motiv brauchen ΔE2000(Farbe) ≥ 15. **Seit M5 hat jedes Katalogsystem ein eigenes Motiv** (6.2, Test `Every_catalog_system_has_its_own_motif`); die Regel bleibt für benutzerdefinierte Systeme. Zwei Systeme mit gleichem Gehäuse und gleicher Farbe (ΔE < 15) brauchen verschiedene Motive. Beispiel: nes und mastersystem haben fast dieselbe Farbe, aber verschiedene Gehäuse. 3ds und switch teilen Gehäuse und Farbe, haben aber verschiedene Motive.

**Unbekannte bzw. benutzerdefinierte Systeme**: `SystemPalette.From(accent)` ergibt Gehäuse anthrazit, Farbe = accent (für Kontrast korrigiert), Licht nach Formel, Motiv Cart8, Textur bayer-8.

**Wo Systemfarben auftreten:**
- **Farbe**: Farbband (8 px) an Wänden und Rücken, Schachtelrücken im Detail, Etikett-Balken (3×14), Ticker-Unterstrich, Regalspiegel, Motiv-Füllung. Flächen über 120×120 px bekommen nie die volle Farbe.
- **Wand / Wand hell**: Wände, Platzhalter-Grund, Seitengrund der Systemseite und der gefilterten Bibliothek.
- **Licht**: Fokusring, Lichtkegel und Brettlicht im Systemkontext, Etikett-Text auf Wänden, OSD-Code beim Start.

### 2.5 Fernsehlicht: wie das Spielbild den Raum färbt

**Wo:** nur auf **Start** und **Detail**, und dort nur im Bühnenbereich oberhalb des Bühnenbretts. Unterhalb des Bretts ist Raum. **Bibliothek: aus.** Dort liegt Raum, bei gewähltem System die Wand dieses Systems als Vollton. Systemseite und Konsolen: Wand. Werkbank: Raum.

**Pipeline** (Control `RoomBackdrop`, eine Instanz *außerhalb* des skalierten Seitenbaums, siehe 7.0):
1. **Quelle**: fokussiertes Spiel → Snap → Titelbild → Cover → Systemwand.
2. **Weichzeichnen, vorab und abseits des UI-Threads**: Der Job decodiert das Bild auf 96 px Breite, beschneidet auf 16:9, rechnet auf **64×36** herunter und zeichnet in float weich (drei Box-Durchgänge ≈ Gauß). **R2**: Das Feld wird bilinear auf **256×144** hochgerechnet und mit Interleaved-Gradient-Noise (±½ LSB) auf 8 bit gebracht – die 8-bit-Stufen des alten 64×36-Bitmaps wurden bei 1920 zu 30 px breiten Bändern. Das Ergebnis kommt in einen eigenen LRU-Cache (64 Einträge, Schlüssel Pfad + Änderungszeit). Dargestellt wird es als `Image Stretch=UniformToFill` mit `RenderOptions.BitmapInterpolationMode="HighQuality"` und Deckkraft 0,38. **Kein `BlurEffect`.**
3. **Bildlicht**: Im selben Job wird aus den 64×36 Pixeln die nach Sättigung gewichtete Mittelfarbe gebildet und auf L 0,55–0,70 und S ≤ 0,70 geklemmt. Sie wird pro Spiel gecacht (`Dictionary<long, Color>`).
4. **Scrims und Schein, zusammen in float gerechnet (R2)**: (a) horizontal Raum 94 % → 52 %, (b) vertikal transparent bis 60 % der Bühnenhöhe → Raum 100 % an der Brettoberkante, (c) Vignette Raum-Tief, (d) Kopfblende, (e) der Schein (Punkt 5) werden pro Pixel vormultipliziert übereinander gelegt und die Farbe mit Interleaved-Gradient-Noise (±½ LSB) quantisiert – **ein** Bitmap in Geräteauflösung (höchstens 2560 breit), auf einem Hintergrund-Thread (`RoomBackdrop.LightPixels`). Als Skia-Verläufe gezeichnet stuften sie sich in Konturringe und senkrechte Bänder, und das Korn unter den Scrims erreichte sie nicht. Neu gerechnet wird pro Fenstergrößen-Bucket (64-px-Raster) und Bühnenhöhe (150 ms nach dem letzten Resize) sowie sofort bei neuer Lichtfarbe/-stärke/-position.
5. **Schein an der Quelle**: Bildlicht 16 % → 0, Radius 55 % der Fensterbreite, nur über dem Brett. Er sitzt auf Start in der Mitte des Bildschirm-Objekts und im Detail in der Mitte der Schachtel; er ist Teil des Licht-Bitmaps aus Punkt 4.
6. **Brettlicht**: `RhBoardLightBrush` ist eine einzige, app-weit geteilte `SolidColorBrush` mit `ColorTransition` 420 ms. Auf Start und Detail ist sie Bildlicht bei 35 %, in Bibliothek und Systemkontext die Kontext-Lichtfarbe bei 22 % (Bernstein bzw. Licht), sonst #17FFF3E0. Jedes Brett nutzt diese Instanz, also kostet ein Wechsel eine Farbe.
7. **Korn**: `korn-128.png` bei **6 %** *nur innerhalb von `RoomBackdrop`* (unter dem Inhalt), als Dither gegen Banding (QA 2: bei 2,5 % blieb es unter einem LSB, der weichgezeichnete Hintergrund stufte sich bei 2560 sichtbar in Konturringe; 6 % ≈ ±2 LSB auf Raum, ein statisches Bitmap, keine Kosten pro Frame).
8. **Überblendung**: zwei Bildebenen A/B, `DoubleTransition` auf Opacity, 420 ms SineEaseInOut. Der Wechsel startet erst, wenn der Fokus `Dwell` (220 ms) ruht, und nie während `.nav-repeat`. Endet ein Repeat, wird nur der Dwell-Timer neu gestartet, und auch nur dann, wenn das Zielspiel vom angezeigten abweicht (8.4).
9. **Kein Bild**: Wand des Systems (zweifarbig) plus Motiv-Umriss bei 7 % rechts unten. Ohne Spiel (leere Bibliothek): Raum mit Schein in Bernstein bei 5 %.

---

## 3. Typografie

### 3.1 Familien, Dateien, Rollen (verifiziert)

Archivo ist das System: vier Breiten aus *einer* Familie tragen Plakat, Zahl, Bedienung und Rücken. Atkinson Hyperlegible Next ist die Leseschrift, gebaut für Lesbarkeit aus der Distanz. Poppins entfällt; die Dateien und ihre Lizenz sind seit M6 gelöscht (Test `The_old_theme_and_the_stock_controls_are_gone`).

| Rolle | Familie / Schnitt | Datei (`src/RetroHub.App/Assets/Fonts/…`) | FontFamily in XAML | FontWeight |
|---|---|---|---|---|
| **Plakat** (Spieltitel, Systemname, Wortmarke) | Archivo Expanded ExtraBold | `ArchivoExpanded/ArchivoExpanded-ExtraBold.ttf` | `avares://RetroHub.App/Assets/Fonts/ArchivoExpanded#Archivo Expanded` | 800 |
| **Zahl** (Zahlen in Sätzen, OSD-Ziffer, Zähler) | Archivo SemiExpanded Bold | `ArchivoSemiExpanded/ArchivoSemiExpanded-Bold.ttf` | `…/ArchivoSemiExpanded#Archivo SemiExpanded` | 700 |
| **UI** (Tabs, Zeilen, Bildunterschriften, Fußleiste) | Archivo SemiCondensed Medium | `ArchivoSemiCondensed/ArchivoSemiCondensed-Medium.ttf` | `…/ArchivoSemiCondensed#Archivo SemiCondensed` | 500 |
| **Etikett / Button** | Archivo SemiCondensed SemiBold | `ArchivoSemiCondensed/ArchivoSemiCondensed-SemiBold.ttf` | `…/ArchivoSemiCondensed#Archivo SemiCondensed` | 600 |
| **Rücken** (gedrehte Titel, Codes, OSD-Code) | Archivo Condensed Bold | `ArchivoCondensed/ArchivoCondensed-Bold.ttf` | `…/ArchivoCondensed#Archivo Condensed` | 700 (Datei meldet 680, einzige Datei im Ordner) |
| **Lesen** (Beschreibung, Meta-Sätze, Erklärungen) | Atkinson Hyperlegible Next Regular / Medium | `Atkinson/AtkinsonHyperlegibleNext-Regular.ttf`, `-Medium.ttf`, `-SemiBold.ttf` | `…/Atkinson#Atkinson Hyperlegible Next` | 400 / 500 / 600 |

Jede Familie hat einen eigenen Ordner mit eigener `OFL.txt`. Ressourcen-Keys: `RhFontPoster`, `RhFontNumber`, `RhFontUi`, `RhFontSpine`, `RhFontRead`. Die Fallback-Kette jeder Ressource endet in `fonts:Inter#Inter`.

**Verifikation (2026-11-09, Avalonia 12.1.5 headless, Skia):**
- Die Dateien stammen aus den offiziellen Repos (`Omnibus-Type/Archivo` `fonts/ttf`, `googlefonts/atkinson-hyperlegible-next` `fonts/ttf`), OFL 1.1. fontTools war offline nicht installierbar, deshalb wurde die Name-Tabelle mit einem eigenen Parser ausgelesen (`scratchpad/design/tools/fontinfo.py`, nur Standardbibliothek, `python -I`). Ergebnis: Bei ExtraBold, Medium und SemiBold steht in ID 1 der Stilname mit Gewicht („Archivo Expanded ExtraBold“), die typografische Familie in ID 16 („Archivo Expanded“). Bei Bold steht ID 1 = „Archivo Condensed“ ohne ID 16. Die **Bold-Dateien von Condensed und SemiCondensed melden `usWeightClass` 680**, SemiExpanded Bold meldet 700.
- **Befund:** Liegen mehrere Archivo-Breiten in *einem* Ordner, löst Avalonia 12.1.5 **jede** Anfrage (jede Breite, jedes Gewicht, auch mit `FontStretch`) auf „Archivo Condensed w680“ auf. Mit **einem Ordner pro Breite** wird jede Rolle exakt aufgelöst (Expanded w800, SemiExpanded w700, SemiCondensed w500/w600). Eine Anfrage nach SemiCondensed w700 landet dagegen bei Medium 500. Deshalb wird SemiCondensed Bold nicht ausgeliefert.
- **`tnum` funktioniert:** `TextBlock.FontFeatures = { FontFeature.Parse("+tnum") }`. Gemessen bei 40 px: Archivo SemiExpanded Bold „1111“ 70 → 77 px, „8888“ 75 → 77 px; Atkinson „1111“ 66 → 102 px, „8888“ 99 → 102 px. Beide Familien enthalten `tnum` in GSUB.
- **Zeichenabdeckung:** Archivo und Atkinson decken ÄÖÜäöüß, „“‚‘›‹, –—…, € und · ab. Es fehlen ★ (U+2605) sowie in Atkinson → und ↕. Pfeile und Sterne sind deshalb immer Icons und nie Text.
- **Atkinson hat eine durchgestrichene Null.** Das ist gut für Lesbarkeit, wirkt in Jahreszahlen aber technisch. Deshalb gilt: **Zahlen in Sätzen werden als Archivo-Run gesetzt** (Zahl-Rolle). `MetaSentence` liefert Inlines und keinen String.
- Probe-Render: `artifacts/screens/font-probe4.png` (lokal, nicht versioniert).
- **Regressionstest (M1):** `FontRoleTests` prüft für jede `RhFont*`-Ressource per `FontManager.Current.TryGetGlyphTypeface` den erwarteten `FamilyName`, das Gewicht und `FontStretch`.

**Warum diese Kombination:** Breitenkontrast (Expanded gegen Condensed) ist das Mittel der Verpackungen der 90er. SemiCondensed spart in der Bedienung Breite für lange deutsche Wörter („Bibliotheksordner“, „Startoptionen zurücksetzen“). Atkinson Hyperlegible Next ist für schwache Sicht und Distanz gebaut (offene Formen, unterscheidbares Il1, O0), also genau für 10-ft-Lesetext. Sie ist humanistisch und konkurriert nicht mit der Grotesk-Stimme.

### 3.2 Schriftgrade (Referenz 1080p; ×UiScale)

| Stil (Klasse) | Rolle | Größe / Zeilenhöhe | Laufweite | Farbe |
|---|---|---|---|---|
| `poster-xl` | Plakat | 72 / 76 | −1 % | Papier |
| `poster` | Plakat | 56 / 62 | −1 % | Papier |
| `poster-s` | Plakat | 40 / 46 | 0 | Papier |
| `h2` | Etikett/Button (600) | 24 / 30 | 0 | Papier (nur Dialog-Titel und Werkbank-Gruppen) |
| `read-l` | Lesen 400 | 20 / 30 | 0 | Text 2 |
| `read` | Lesen 400 | 18 / 28 | 0 | Text 2 |
| `ui` | UI 500 | 18 / 24 | 0 | Papier |
| `ui-l` | UI 500 | 20 / 26 | 0 | Papier (Listenzeilen, Tabs) |
| `button` | Etikett/Button 600 | 20 / 24 | +1 % | je nach Button |
| `caption` | UI 500 | 17 / 21 | 0 | Text 2 (fokussiert: Papier) |
| `meta` | Lesen 400 | 15 / 20 | 0 | Text 3 |
| `label` | Etikett 600 | 15 / 18 | +3 % | Tinte auf Papier (Etikett) bzw. Text 3 oder Licht |
| `code` | Rücken | 17 / 20 | +4 % | Papier / Text 2 |
| `spine` | Rücken | 14–28 (abhängig von der Rückenbreite) | +2 % | Text auf Farbe |
| `number` | Zahl | wie der umgebende Text, `+tnum` | 0 | Papier |
| `osd` | Zahl (Ziffer) + Rücken (Code) | 40 / 44 | 0 | Licht |

Regeln:
- **Keine Versalien-Overlines.** Versalien gibt es nur, wo der Inhalt selbst welche hat (Systemcodes, gedruckte Titel). Rücken setzen den Titel in Originalschreibung.
- **Mindestgröße** für Lesbares ist 15 px. Text 3 erst ab 15 px.
- **Titel auf Start und Detail werden gemessen**, Bildunterschriften nicht. Ein `TextLayout` misst den Titel bei 72, 56 und 40 px mit der Spaltenbreite und `MaxLines=2`. Gewählt wird die größte Stufe ohne Kürzung. Passt keine, gilt 40 px mit `CharacterEllipsis`. Das passiert einmal pro Titelwechsel (drei Layouts, vernachlässigbar). Grid-Bildunterschriften: 2 Zeilen, dann Ellipse. Listenzeilen: 1 Zeile, der fokussierte Titel läuft nach 600 ms Verweilen als Laufschrift (40 px/s).
- **Zahlen** stehen immer in der Zahl-Rolle mit `+tnum`, auch in Sätzen (Run-Inline).
- **Mono** (QA 2): Pfade, Dateiendungen und Prüfsummen immer in Cascadia Mono Regular 15, Text 2. **Programmnamen** in 
ead SemiBold Papier (in Sätzen über Sentence.Name), ihre **Versionen** in Zahl Text 3. Eine Regel für Lieferschein, Scan-Datei, Dateiinfo, Systeminfo und Emulatoren.
- **Meta-Sätze statt Ketten**: `"{Genre} von {Entwickler}, {Jahr}. {Spieler}."` Fehlende Teile fallen samt Satzzeichen weg. Ist nur das System bekannt: „Game Boy Advance.“ Es gibt keine `A · B · C`-Ketten.

---

## 4. Form und Oberfläche

### 4.1 Radien und Fasen

| Key | Wert | Verwendung |
|---|---|---|
| `RhRadiusNone` | 0 | Bretter, Wände, Rücken, Etiketten, Gehäuse und Mulde des Bildschirms, Banner |
| `PixelScreen.TubeRadius` | 6 | **einzige Ausnahme an Objekten:** das Glas der Röhre (M2) |
| `RhRadiusSmall` (Aliase `RhRadiusChip`, `RhRadiusTile`) | 2 | Cover, Platzhalter, Aufkleber, Papier-Chips |
| `RhRadiusControl` | 4 | Sekundärbuttons, Eingabefelder, Listenzeilen |
| `RhRadiusPanel` | 6 | Dialoge, Overlays (Maximum der App) |
| `RhRadiusPill` | 4 | Key bleibt, Wert sinkt von 999 auf 4 |
| `RhChamferRing` (neu) | 6 | Außenfase des Fokusrings |
| `RhChamferPlate` (neu) | 4 | Primärbutton „Spielen“, Tastenkappen (LB/RB-Ersatz, Tastatur) |

**Gefaste Platte** (`ChamferPlate`, Render-Override, eine gecachte `StreamGeometry` pro Größe): ein Rechteck mit 45°-Schnitten an allen vier Ecken. Sie wird für den Primärbutton und die Tastenkappen verwendet und ist die Form-Signatur neben dem gefasten Ring. Kreise gibt es nur für Statuspunkte (8 px), die Tastenraute (5.3) und das Disc-Motiv.

### 4.2 Brett

| Variante | Front | Oberkante | Unterkante | Schatten | Wo |
|---|---|---|---|---|---|
| **Bühnenbrett** | 24 px Regal hell | 1 px `RhBoardLightBrush` | 1 px Kante | 16 px #000 45 % → 0 | Start (Bühne und Bretter), Detail, Konsolen, Systemseite |
| **Regalkante** | 12 px Regal hell, darin oben 2 px **Lippe** #3A352D (Regal hell + 6 % Papier) unter dem Brettlicht | 1 px `RhBoardLightBrush` | 1 px Kante | 16 px #000 45 % → 0 | Bibliothek-Grid, Listen-Vorschau, leere Bibliothek |

- Das Brett läuft über die **volle Inhaltsbreite** (Bühnenbrett auf Start und Detail randlos über die Fensterbreite).
- **Etiketten** hängen links an der Front des Bühnenbretts, bei x = Seitenrand. Sie sind 24 px hoch und bündig mit der Front.
- **Beleuchtetes Brettstück** (nur beim Fokus, gezeichnet vom `FocusLayer` *über* den Zeilen; M3-QA): Die Oberkante wird über die Breite des Rings (Schachtel + 2 × 6 px) 2 px in Lichtfarbe zu 100 %, die Front darunter Regal hell gemischt mit **16 %** Lichtfarbe (Bernstein ≈ #4C3D26). Der Fokus-Kontaktschatten fällt erst *unter* die Front (auf der Front hob er die Tönung wieder auf). Der Ring steht auf diesem Lichtstreifen (8.1).
- Die Regalkante war mit 10 px Regal hell auf Raum (≈ 1,3 : 1) nur eine Haarlinie (M3-QA). 12 px mit Lippe lesen sich als Brett, auf dem die Schachteln stehen.
- **Brett vor einer Wand** (M5): Steht ein Bühnenbrett vor einer Systemwand (Systemseite, Bibliotheks-Banner, Konsolen), ist seine Front *Wand hell + 10 % Papier* mit einer 2-px-Lippe (+ 18 %) unter dem Brettlicht und einer Unterkante aus der Wand × 0,65 (`ShelfBoard.WallLook`, Brushes pro System geteilt). Regal hell #2A2620 hatte vor den warmgrauen Wänden ≈ 1,1 : 1 und verschwand; jetzt ≥ 1,4 : 1 gegen *Wand*.
- Statisch: eine Brush-Instanz pro Schicht, app-weit.

### 4.3 Kanten und Linien

- **Trennlinie** in Listen: seit M7 eine **eingravierte Rille** (`Groove`, 2 px: 1 px #000 50 %, darunter 1 px #FFF3E0 5 %), keine Haarlinie. Listen haben keine Rahmen.
- **Overlay-Rahmen**: 1 px Kante, oben zusätzlich 1 px Lichtkante.
- **Keine Rahmen um Inhaltsgruppen.** Gruppen entstehen durch Abstand (48/16 px) und Etiketten.

### 4.4 Schatten (Licht kommt von oben)

| Key | Wert | Wo |
|---|---|---|
| Kontaktschatten | gezeichnet: 10 px #000 55 % → 0, auf Coverbreite, auf dem Brett | jede Schachtel (gezeichnet vom Brett bzw. `CoverRowPresenter`) |
| Fokus-Kontaktschatten | 16 px #000 70 % → 0 | nur die fokussierte Schachtel (`FocusLayer`) |
| `RhPanelShadow` | `0 24 64 -16 #F0000000` | nur Dialoge und Overlays |
| `RhFocusGlow`, `RhButtonGlow`, `RhFocusShadow` | → leer | Keys bleiben für Kompatibilität |

Außerhalb von Overlays gibt es **keine `BoxShadow`**. Farbiges Licht ist ausschließlich der Lichtkegel.

### 4.5 Texturen

Texturen werden programmatisch erzeugt: `tools/TextureGen` (C#, SkiaSharp, fester Seed) schreibt PNGs nach `src/RetroHub.App/Assets/Textures/`. Die PNGs werden committet.

| Datei | Größe | Muster | Einsatz | Deckkraft |
|---|---|---|---|---|
| `korn-128.png` | 128×128 | Rauschen weiß/schwarz, Alpha 0–36 | nur in `RoomBackdrop` | 6 % (QA 2) |
| `scan-4x3.png` | 4×3 | Zeile 0 #000 Alpha 110 | **nur** im Objekt Bildschirm | 18 % |
| `lcd-4.png` | 4×4 | 1-px-Gitter #000 Alpha 70 | Handheld-Wände | 8 % |
| `bayer-8.png` | 8×8 | 2×2-Schachbrett, Alpha 0/48 | NES, SMS, Atari, MSX, Fallback | 7 % |
| `dither16-8.png` | 8×8 | diagonale Punktreihe | SNES, Mega Drive, PC Engine, 32X | 7 % |
| `rillen-1x6.png` | 4×6 | 2 px #FFF Alpha 26, 4 px frei | SNES, Mega Drive, PS2 | 6 % |
| `poly-48.png` | 48×42 | Dreiecksgitter 1 px #FFF Alpha 40 | N64, PS1, Saturn | 6 % |
| `halbton-12.png` | 12×12 | Punkt Ø 5 px | GameCube, Dreamcast, Mega-CD, Wii | 7 % |
| `maske-6.png` | 6×6 | Lochmaske R/G/B | Arcade | 10 % |
| `lochblech-10.png` | 10×10 | Kreis Ø 4, versetzt | PC, Switch, PSP | 6 % |
| `holz-256x32.png` | 256×32 | Sinus-Maserung, 5 Brauntöne | Atari 2600 | 10 % |
| `raster-16.png` | 16×16 | 1-px-Gitter oben/links #FFF Alpha 36 | Master System, MSX, Game Gear | 7 % |

**Zweite Lage** (M5): SNES und Mega Drive tragen über dither16-8 die Rillen ihrer Gehäuse (rillen-1x6 bei 5 %, `SystemLook.Overlay`, gezeichnet von `SystemWall.DrawTextures` auf Wänden, Bannern und Rücken). Damit ist die Zuordnung aus 6.3 vollständig; alle zwölf Texturen sind generiert und im Einsatz (scan-4x3 nur im Bildschirm, korn-128 nur im Hintergrund).

**Pixelgenau trotz UiScale** (`TextureFill`, Control mit Render-Override): Pixelmuster werden **in Gerätepixeln** gezeichnet. Der Control ermittelt die Gesamttransformation zum TopLevel (`TransformToVisual`) und setzt mit `PushTransform` die inverse Skalierung. Gezeichnet wird mit einer gemeinsamen `ImageBrush`, deren `DestinationRect` = Kachelgröße × k Gerätepixel ist, mit k = max(1, round(UiScale × RenderScaling)), Ursprung auf ganze Gerätepixel gerundet, `BitmapInterpolationMode=None`. Dadurch entsteht bei UiScale 0,8 oder 1,33 keine Moiré, und bei 4K/200 % bleibt das Muster proportional (k = 2 bzw. 3). Pro Textur gibt es eine Brush-Instanz, nie eine pro Kachel.

**Materialtexturen (M7)** – nie als Epochentextur einer Wand, nur für gezeichnete Objekte (4.7):

| Datei | Größe | Muster | Einsatz | Deckkraft (`TextureFill`-Stärke) |
|---|---|---|---|---|
| `pappe-64.png` | 64×64 | senkrechte Fasern hell/dunkel, Alpha 0–22 | Pappschuber, Papier-Etiketten, Lieferschein | 0,35–0,7 |
| `brett-512x24.png` | 512×24 | lange, flache Maserung, Alpha 0–18 | Front aller Bretter, Senderskala | 0,8–1 |
| `staub-64.png` | 64×64 | lockere helle Staubpunkte | Staubspur eines leeren Platzes | 0,3–0,55 |

**Wo nie Textur:** hinter dem Grid, auf Text-Flächen von Dialogen, auf Buttons. Etiketten tragen seit M7 ihre Papierfasern (≤ 3 % effektiv).

### 4.6 Transluzenz (selten)

Erlaubt nur: (1) Scrims in `RoomBackdrop`, (2) Fußleiste: Raum-Verlauf 0 → 94 % über 40 px oberhalb – der Schatten ihrer **Vorderkante** (`FooterLip`, R2: 6 px Oberseite mit Lichtkante und Maserung; Listen laufen unter die Kante der Werkbank, statt in einer 56-px-Blende auszulaufen), (3) Dialog-Hintergrund Raum-Tief 72 % ohne Blur, (4) Overlay „Läuft“ Raum-Tief 94 %. Die Glas-, Gradient-, Glow- und Ambient-Keys aus v0 sind seit M6 gelöscht, ebenso `StatTile`, `Border.glass/.card`, die Chip-Pillen und die Stile für ComboBox, ToggleSwitch, Slider und ProgressBar (keiner davon kommt in RetroHub noch vor).

### 4.7 Gezeichnet, nicht gesetzt (M7, Owner-Feedback „fühlt sich hingecodet an“)

Jedes Objekt wird aus denselben Teilen gebaut (`Controls/Craft.cs`, `CraftParts.cs`) statt als flaches Rechteck mit Text:

- **Material**: Pappe (Fasern), Kunststoff (diagonaler Glanz `Sheen`, Wölbung `Curvature`: links 6 % Licht, Mitte 0, rechts 16 % Schwarz), Brett (Maserung), Blech (Buchstütze, Knopf).
- **Kanten**: links 1 px Licht, oben 1 px Licht, rechts 1 px #000 + weiche Abschattung, am Fuß Schmutz (#000 → 0 über 6–14 px).
- **Tiefe**: stehende Objekte zeigen eine **Seitenfläche** rechts (4–5 px, Ton × 0,55) und eine **Oberfläche** oben (3 px, + 12 % Licht), schräg nach hinten rechts – wir blicken leicht von oben ins Regal. Bretter zeigen ihre Oberseite (5 px bzw. 3 px) vor der Lichtkante.
- **Schatten**: Schlagschatten als Parallelogramm nach hinten rechts auf die Wand (10 px, #000 38 % → 0), Kontaktschatten wie bisher.
- **Papier**: Etiketten haben einen **geschnittenen Rand** (Polygon mit Ecken + 1–2 Punkten je Kante, ± 0,6–0,7 px), sind bis ± 1,5° geneigt, haben eine dunklere Unterkante (Dicke), einen weichen Schatten (1, 2) und Fasern; Druck auf Papier steht einen Hauch neben dem Register (zweiter Durchgang 25 %, 0,5 px).
- **Abnutzung**: 1–2 Kratzer, Macken an Brettkanten, eine eingedrückte Ecke, ± 1 px gedruckte Skalenstriche.
- **Variation** immer **geseedet** aus einem stabilen Hash der Id (FNV-1a, `Craft.Seed`) – nie `string.GetHashCode` (wechselt pro Prozess). Jedes Objekt sieht bei jedem Start gleich aus.
- **Kosten**: geteilte Brushes (eine pro Farbe/Alpha, Verläufe relativ), Geometrien pro Größe gecacht, keine Bitmaps pro Kachel, kein Blur. Im Grid kostet der Platzhalter pro Kachel ein Papier-Polygon (gecacht) und zwei Klebestreifen.

---

## 5. Ikonografie

### 5.1 Stilregeln „Gefaste Linie“ (unverändert gegenüber v1.0)

- Raster 24×24, Live-Area 20×20, Strich 2,0 bei 24 px (proportional skaliert).
- Geraden nur unter **0°, 45° und 90°**. Bögen nur für echte Kreise (Lupe, Disc, Uhr).
- **Ecken gefast** (2 px, 45°) statt gerundet. `StrokeLineCap=Square`, `StrokeLineJoin=Miter`.
- Gefüllte Variante `…Filled` für aktive Zustände. Größen 20/24/28/48. Farbe immer `Foreground`.
- `Themes/Icons.axaml`, `StreamGeometry`, Key-Schema `RhIcon.Name`.

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
| Navigation | `Start`, `Library`, `Systems`, `Emulators`, `Imports`, `Settings`, `Search`, `Menu`, `Back`, `More` |
| Aktionen | `Play`, `Favorite`/`FavoriteFilled`, `Random`, `Folder`, `External`, `Refresh`, `Sort`, `Filter`, `Grid`, `List`, `Cover`, `Remove`, `Add`, `Close`, `Fullscreen`, `Window`, `Power`, `Expand`, `Collapse`, `ArrowLeft`/`ArrowRight` (für Werte im Service-Menü) |
| Meta | `Clock`, `Calendar`, `Players`, `Region`, `File`, `Disk`, `Controller`, `Starts` |
| Status | `Ok`, `Warning`, `Error`, `Info`, `Missing`, `NotReady` |
| Medientyp (20 px) | `MediaCart8`, `MediaCart16`, `MediaCart64`, `MediaDisc`, `MediaMiniDisc`, `MediaCard`, `MediaFloppy`, `MediaHandheld`, `MediaArcade` |

Die alten Icons im Lucide-Stil werden vollständig ersetzt.

### 5.3 Controller-Glyphen (neu, markenfrei)

Die bisherigen Glyphen (Xbox-Farben A #5BD07A, B #FF6B6B, X #5AA2FF, Y #FFD15C, PS-Symbole, „Ⓐ Xbox“) **entfallen**. Neu sind positionsbasierte, einfarbige Glyphen:

| Glyphe | Zeichnung (28 px) | Bedeutung |
|---|---|---|
| **Tastenraute** `GlyphFace.South/East/West/North` | Kreisfläche Ø 28 in Regal hell. Darin vier Punkte Ø 6 als Raute (Abstand 7 vom Zentrum). Die gemeinte Position ist gefüllt Papier, die anderen sind 1,5-px-Ringe Text 3 | Süd = Bestätigen, Ost = Zurück, West = Favorit, Nord = Suche (bei `SwapAcceptBack` tauschen Süd und Ost die *Position*, die Texte bleiben) |
| **Schulter** `GlyphShoulder.Left/Right` | Gefaste Platte 40×24 in Papier, darin ein Keil ◤ bzw. ◥ in Tinte (gefüllt, 10 px) | Fach bzw. Abschnitt wechseln |
| **Menü / Ansicht** | Gefaste Platte 28×20 in Regal hell; Menü = drei Striche, Ansicht = zwei versetzte Quadrate | Kopfleiste bzw. Ansicht wechseln |
| **Steuerkreuz** | Kreuz 20 px, gefaste Enden | in Hilfetexten |
| **Tastatur** (letzte Eingabe Tastatur) | Gefaste Platte in Papier, Tastenname in Etikett 15 Tinte: „Enter“, „Esc“, „Bild↑“ wird zu „Bild auf“ | gleiche Positionen in der Fußleiste |

- Die **Bedeutung steht immer als Text** daneben („Öffnen“, „Zurück“). Die Glyphe zeigt nur die Position.
- Der **Gerätename** erscheint als reiner Text aus dem Betriebssystem (HID-Produktname, `ControllerName`) in UI 16 Text 3, ohne Logo und ohne Markenfarbe. Unter 1500 px logischer Breite wird er ausgeblendet.
- `ControllerFamily` steuert keine Glyphen-Optik mehr. Die Anzeige entscheidet nur zwischen Pad und Tastatur.

---

## 6. Plattform-Motive

### 6.1 Zeichenregeln

- Motive sind **Kategorien, keine Produkte**: „Modul“, „Handheld hochkant“, „Disc“, „Bedienfeld“. Es gibt keine Proportionen, die ein konkretes Gerät eindeutig kopieren, keine Schriftzüge, keine Knopf-Farbanordnungen und keine Markenzeichen (auch keine Spiralen, Umlaufbahnen oder Rennstreifen).
- **Außenseiter-Test (QA, M5):** Eine Person ohne Retro-Vorwissen sieht jedes Motiv einzeln, ohne Farbe und Text. Sie darf die *Kategorie* nennen („ein Modul“), aber nicht das *konkrete Produkt*. Ein Motiv, bei dem das Produkt genannt wird, wird vereinfacht.
- Raster 120×120. Höchstens 14 Pfad-Segmente. Flache Füllungen in Farbe und Zweitfarbe, Details als 2-px-Linien in Wand oder ausgespart. Keine Verläufe.
- Jedes Motiv **steht**: Die Unterkante liegt auf y = 120.
- Ablage: `Themes/Motifs.axaml` (`RhMotif.Cart8.Body` …), Zuordnung in `Theming/MotifCatalog.cs`. Für C#-Rendering werden die Pfade einmal per `Geometry.Parse` gecacht.

### 6.2 Grundformen (Pfaddaten im 120er Raster, final seit M5)

Quelle der Wahrheit ist `Theming/MotifCatalog.cs`. Jedes **Katalogsystem hat seit M5 ein eigenes Motiv** (`SystemPaletteTests.Every_catalog_system_has_its_own_motif`). Teile: B = Körper (Zweitfarbe, auf schwarzen Wänden angehoben), C = Farbe, X = ausgespart (Wandton), L = Linie in Licht.

```
Module
Cart8          nes           B M14,10 H106 V120 H14 Z · X drei 2-px-Rillen oben · C Etikett M24,46 H96 V112 H24 Z
CartWide       mastersystem  liegendes Modul: B Griffleiste M24,40 H96 V48 + Körper M6,48 H114 V120 · C Etikett links · X 3 senkrechte Rippen rechts
Cart16         snes          runde Schultern (Q), C Etikett mittig, X Rille am Fuß
CartGrip       megadrive     schmalerer, gerippter Griff (5 senkrechte Rillen) auf dem Körper gestuft, C Etikett
Cart64         n64           schräge Schultern M14,30 L26,10 H94 L106,30, C Etikett
CartStack      sega32x       Modul auf Modul (oben 80 % Breite, 8 px Fuge)
Card           pcengine      flache Karte M14,44 H106 V120 H14 Z, C Feld, X Kontaktreihe aus 8 Stegen
Handhelds
HandheldTall        gb          hochkant, Bildschirm oben, Kreuz links, zwei Tasten ÜBEREINANDER rechts, 3×3-Lochung am Fuß; gefaste Oberecken, keine runde Ecke (M5-QA)
HandheldTallDouble  gbc         dasselbe Gerät (Tasten übereinander, Lochung), sein Umriss 6 px versetzt dahinter (2-px-Linie in Zweitfarbe: zweite, klare Schale)
HandheldNarrow      wonderswan  schmal hochkant, kleiner Bildschirm, kleines Kreuz, zwei Tasten übereinander, drei Lautsprecherlinien
HandheldWide        gba         quer, eckige Schultern, 6-px-Fase nur am Fuß, Bildschirm mittig, Kreuz links, zwei flache Schlitztasten 10×4 rechts; Körper in Farbe angehoben (Paint.Lift: L ≥ 0,55 und ≥ 3,2 : 1 zur Wand; Violett auf violetter Wand hatte 2,8 : 1) (M5-QA)
HandheldBig         gamegear    quer und hoch, großer Bildschirm, Lautsprecherschlitze unter den Tasten
HandheldLong        lynx        sehr breit, an beiden Enden herabhängende Griffe
HandheldCompact     ngp         klein, fast quadratisch, Bildschirm oben, runder Knauf statt Kreuz
HandheldBroad       psp         breit und flach, Bildschirm füllt fast die Front
Clamshell           nds         zwei GLEICHE Hälften, Scharnier in Körperton, zwei gleiche Bildschirme
ClamshellWide       3ds         zwei GLEICH breite Hälften; im oberen Bildschirm eine Treppe aus drei versetzten 2-px-Rahmen (Tiefe), kein Pad (M5-QA)
Discs und Laufwerke
DiscTrack     ps1        Disc (60,68) r 52, Loch r 9, zwei Datenspur-Bögen (r 30/40) ausgespart
DiscBands     saturn     Disc mit zwei harten konzentrischen Bändern (Zweitfarbe)
DiscHalftone  dreamcast  Disc mit Halbtonpunkten, nach rechts zunehmend
MiniDisc      gamecube   kleine Disc (60,82) r 38, Loch r 13, 6-px-Ring in Zweitfarbe
Tray          segacd     große Disc (60,58) r 46 hinter einer breiten, flachen Schubladenfront M4,92 H116 V120 mit Griffmulde
Remote        wii        R2 (ersetzt SlotDisc, das schwächste Motiv): eine Fernbedienung, geschwenkt – schlanker Stab (32 × 106, runde Enden) in Zweitfarbe mit Kreuz, einer großen Taste in Farbe, zwei kleinen Tasten und vier Lämpchen, die Handschlaufe kringelt sich am Fuß, zwei Bewegungsbögen neben dem Kopf
Tablet        wiiu       zweiter Bildschirm: ein Tablett mit Kreuz und Tasten steht VOR einem großen Bildschirm-Umriss (2 px); kein Monitorfuß
Dock          switch     Hybrid: ein einteiliges Handgerät, 8 px über dem Schlitz eines flachen Sockels angehoben; keine Dock-Wände über dem Bildschirm, keine Seitenteile, kein Farbpaar
Andere Geräte
TwinLens      virtualboy  Stereobrille: Visier mit Nasenkerbe, zwei Linsen, Riemenansätze, nur Linien in Licht; KEIN Standfuß
ControlPanel  arcade      Bedienfeld mit Stick und 2×3 Knöpfen
Stick         atari2600   flacher Sockel, dünner Stab mit Kugel, eine Taste
StickGrip     atari7800   dicker Griff ohne Kugel, zwei Tasten auf dem Sockel
Floppy        pc          Diskette mit Schieber und Etikett
KeyBlock      msx         3 Reihen à 5 gefaste Tasten auf einem Block
Tower         ps2         R2: stehende Platte statt Turm – Vorderseite in Farbe angehoben (Paint.Lift), Ober- und Seitenfläche in Zweitfarbe (Tiefe), ein senkrechter Schubladenschlitz mit zwei Tasten, zwei Kartenschlitze am Fuß, kurze schräge Rippen an der Seite, auf einem **ovalen Ständer** (den kein PC-Gehäuse hat; die fünf waagerechten Fugen lasen sich als PC-Turm)
```

**Kontrast** (M5-QA, `DesignM5Tests.Every_motif_reads_against_its_wall`): Jedes Motiv hat mindestens ein gemaltes Teil mit ≥ 3 : 1 gegen seine Wand. Unter der Regel lagen ps2 (≈ 1,3), gba (2,8), 3ds (2,8) und switch (2,9); ihr Hauptteil ist jetzt `Paint.Lift` (die eigene Farbe, L ≥ 0,55 und so lange in 0,02-Schritten heller, bis ≥ 3,2 : 1).

**Relief ab Heldengröße** (M5-QA, `MotifCatalog.Draw(relief: true)`, nur ab 160 px: Systemseite, Front, Bibliotheks-Banner; Rücken, Platzhalter und der Bildschirm „Kein Signal“ bleiben flach): (1) die System-Textur in der Füllung bei 8 % (eine Brush pro Textur und Maßstab), (2) eine 6-px-Unterlippe jedes Teils in #000 30 % (das Objekt hat Dicke und steht), (3) eine 2-px-Lichtkante oben/links in Papier 18 % (Silhouette minus sich selbst verschoben). Drei Geometrien pro Motiv und 20-px-Größenstufe, gecacht; statisch. Bogen `motive-held.png`.

**Silhouette** (`MotifCatalog.Silhouette`, Platzhalter-Cover und Außenseiter-Bogen): alle Teile vereinigt, Aussparungen **in Malreihenfolge** abgezogen (ein später gemaltes Teil in einer Aussparung bleibt: die Tiefenrahmen im 3DS-Bildschirm), und jedes Teil, das über ein früheres gelegt ist, behält eine **2-px-Fuge** (Etiketten, Tasten, Bänder, Scharnier). Vorher schmolzen einfarbig Etikett und Körper zu einem Block, und Saturn, Dreamcast und eine leere Disc waren gleich (M5).

### 6.3 Zuordnung (Farbe, Textur)

Die Motiv- und Gehäusezuordnung steht in der Systemtabelle 2.4 (Motivnamen dort seit M5 wie in 6.2). Zusätzlich gelten:

| System | Farbgebung im Motiv | Textur | Notiz |
|---|---|---|---|
| gb | Körper Farbe, Bildschirm Wand | lcd-4 | keine vier Grünstufen im Bildschirm: zusammen mit dem grauen Körper wäre das das Produkt |
| gbc | Körper Farbe, versetzter Umriss Zweitfarbe | lcd-4 | |
| gba / nds / 3ds / ngp / lynx / gamegear / wonderswan / psp | Körper Farbe, Bildschirm Wand | lcd-4 (psp: lochblech-10, gamegear: raster-16) | |
| nes / mastersystem | Körper Zweitfarbe, Etikett Farbe | bayer-8 / raster-16 | |
| snes / megadrive / sega32x | Körper Zweitfarbe, Etikett Farbe | dither16-8 **plus rillen-1x6 als zweite Lage** (`SystemLook.Overlay`, 5 %); 32X ohne Rillen | **keine Rennstreifen** |
| n64 | Körper Zweitfarbe, Etikett Farbe | poly-48 | |
| gamecube | Violett, Ring Orange | halbton-12 | das Kippen um −6° entfällt (eine Disc sieht gekippt gleich aus) |
| wii | Disc Farbe, Block #E6EEF2 | halbton-12 bei 4 % | |
| wiiu | Tablett Farbe, Bildschirm-Umriss Weiß | halbton-12 | |
| switch | Handgerät Zweitfarbe, Sockel Farbe | lochblech-10 | kein Rot/Blau-Paar, keine Seitenteile |
| virtualboy | nur Linien in Licht | bayer-8 (Scanlines gehören nur dem Bildschirm-Objekt) | |
| ps1 / saturn / dreamcast / segacd | Disc Farbe, Details Zweitfarbe bzw. ausgespart | poly-48 / poly-48 / halbton-12 / halbton-12 | Saturn ohne Umlaufbahn |
| ps2 | Turm in Farbe auf L 0,55, Fugen ausgespart | rillen-1x6 | M5-QA: vorher fast schwarz auf schwarzer Wand |
| arcade | Bedienfeld Farbe, Stick Zweitfarbe | maske-6 | |
| pc | Beige, Etikett in Phosphorgrün | lochblech-10 | |
| atari2600 / 7800 | Stick vor Holzwand (2600) bzw. Griff auf Schwarz (7800) | holz-256x32 / bayer-8 | |
| pcengine | Orange + Creme | dither16-8 | |
| msx | Blau + Creme | raster-16 | |

### 6.4 Wo Motive erscheinen

1. **Wand** (Systemfront, Banner, Systemseite): Motiv voll, 72 % der Wandhöhe (höchstens 46 % der Breite), rechts, auf der Unterkante stehend. Systemseite: 300 px (≈ 1100: 220) auf dem Bühnenbrett. **Konsolen-Front** (M5-QA): unter dem inneren Brett rechts, so hoch wie der Raum darunter (1080 ≈ 190 px), mit Relief.
2. **Systemrücken**: **Auf der Fächerwand (Konsolen) steht das Motiv am Fuß des Rückens** (Rückenbreite − 2 × 18 px), damit die Wand ohne Lesen unterscheidbar ist (M5-Abnahme). Auf Start (M5-QA) dasselbe Objekt kleiner: 40 px über der Anzahl, wenn der Rücken ≥ 200 hoch ist und der Code darüber Platz lässt.
3. **Platzhalter-Cover**: Silhouette in Farbe bei 18 %, 45 % der Höhe, rechts unten.
4. **Bildschirm „Kein Signal“**: Motiv voll, 50 % der Bildschirmhöhe, auf der Wand im Bildschirm.
5. **Detail ohne Bild**: Motiv-Umriss 1,5 px in Licht bei 10 %, 520 px hoch, rechts unten im Bühnenbereich.
6. **Etiketten und Listen**: 20-px-Medientyp-Icon.
7. **Leerzustände**: 1,5-px-Linienzeichnung in Papier bei 60 %.

### 6.5 Außenseiter-Test (M5)

**Verfahren.** Der Vorschau-Harness rendert drei Bögen **ohne jeden Text** (`DesignPreviewTests.Motif_sheet`, Filter `motive`): `motive-umriss.png` (jedes Motiv einzeln als flache Papier-Silhouette, ohne Farbe – das ist der Bogen für den Außenseiter-Test), `motive-farbig.png` (jede Systemwand mit Band, Textur und Motiv – „ohne Text unterscheidbar“) und `motive-klein.png` (dieselben Wände in Rücken- bzw. Platzhaltergröße, 72 px). Die Reihenfolge steht nur in `motive-legende.txt`. Die Bögen wurden in M5 in drei Runden angesehen und die Motive dabei geändert (Wii, Wii U, Switch, Virtual Boy, Mega-CD, die Disc-Details). **Ehrlich:** Geprüft hat der Designer selbst, mit der Frage „Was sagt jemand ohne Retro-Vorwissen?“. Ein Durchgang mit einer echten Außenseiterin steht aus; die Bögen sind dafür vorbereitet (einzeln zeigen, nur „Was ist das?“ fragen, Antwort notieren). Die QA nach M5 verlangte zwei Nicht-Spieler; das kann der Bau nicht leisten, **die Spalte „Was ein Außenseiter nennt“ ist weiter eine Erwartung, kein Befund**. Clamshell (nds) blieb unverändert: zwei gleiche Hälften mit Scharnier sind jedes Klappgerät; die QA nannte es, schlug aber keine Änderung vor.

| Motiv (System) | Was ein Außenseiter nennt | Warum es kein Produkt ist |
|---|---|---|
| Cart8 (nes) | „ein Steckmodul / eine Kassette“ | Rechteck mit Rillen und Etikett haben fast alle Module; keine Kerben, keine Pfeile, kein Schriftfeld |
| CartWide (mastersystem) | „ein liegendes Modul“ | Proportion und Rippen sind generisch, kein Etikettlayout eines Herstellers |
| Cart16 (snes) | „ein Modul“ | runde Schultern allein benennen nichts; keine Griffmulden, keine Farbe des Originals |
| CartGrip (megadrive) | „ein Modul mit Griff“ | gestufter Griff mit Rillen statt der typischen Gehäusekontur |
| Cart64 (n64) | „ein Modul“ | flache Schrägen (flacher als v1.0), kein Griffbogen |
| CartStack (sega32x) | „zwei Module aufeinander“ | Kategorie „Aufsatz“, keine Pilzform des Geräts |
| Card (pcengine) | „eine Karte / Speicherkarte“ | Chipkarte mit Kontakten ist ein Allerweltsobjekt |
| HandheldTall (gb) | „ein Handheld hochkant“ | **Grenzfall, M5-QA geändert**: Tasten übereinander statt nebeneinander/diagonal, 3×3-Lochung statt schräger Lautsprecherschlitze, keine runde Ecke, kein vierstufig grüner Bildschirm, kein grauer Körper |
| HandheldTallDouble (gbc) | „ein Handheld mit zweiter Schale“ | wie oben, plus versetzter Umriss statt Gehäuseform |
| HandheldNarrow (wonderswan) | „ein schmales Handgerät / Pager“ | keine Tastenanordnung des Originals |
| HandheldWide (gba) | „eine Handkonsole quer“ | **M5-QA geändert**: eckige Schultern, Fase nur am Fuß, Schlitztasten statt runder Knöpfe |
| HandheldBig (gamegear) | „ein großes Handgerät“ | eckig, keine Rundungen des Originals |
| HandheldLong (lynx) | „ein Handheld mit Griffen“ | Griffe als einfache Lappen |
| HandheldCompact (ngp) | „ein kleines Handgerät“ | runder Knauf ist eine Kategorie, kein Markenzeichen |
| HandheldBroad (psp) | „ein Handheld mit großem Bildschirm“ | gefastes Achteck, kein Pillenumriss |
| Clamshell (nds) | „ein Klapp-Handheld“ | zwei Rechtecke mit Scharnier; jedes Klappgerät sieht so aus |
| ClamshellWide (3ds) | „ein Klapp-Handheld mit Tiefe“ | **M5-QA geändert**: gleich breite Hälften, kein Kreis-Pad; die Treppe aus drei versetzten Rahmen sagt „Tiefe“, nicht das Gerät |
| DiscTrack (ps1) | „eine CD“ | Datenspur-Bögen, kein Logo |
| DiscBands (saturn) | „eine CD / Schallplatte“ | konzentrische Bänder, **keine Umlaufbahn** |
| DiscHalftone (dreamcast) | „eine CD“ | Punktraster, **keine Spirale** |
| MiniDisc (gamecube) | „eine kleine Disc“ | das Medium, nicht der Würfel; kein Griff, kein Würfel-im-Würfel |
| Tray (segacd) | „ein CD-Laufwerk mit Schublade“ | Allerweltslaufwerk |
| Remote (wii) | „eine Fernbedienung, geschwenkt“ | **R2 geändert**: die flache Disc im Schlitzblock war das schwächste Motiv (weißer Block mit Sichel). Eine Fernbedienung mit Kreuz und Tasten ist eine Kategorie (jede TV-Fernbedienung), die Bewegungsbögen sagen „bewegungsgesteuert“; kein Gerätegehäuse |
| Tablet (wiiu) | „ein Tablet vor einem Fernseher“ | **geändert in M5**: das Tablett auf einem Monitorfuß las sich als Monitor. Jetzt „zweiter Bildschirm“: Tablett vor einem Bildschirm-Umriss, keine Sticks an Produktpositionen |
| Dock (switch) | „ein Handgerät über seiner Ladeschale“ | **geändert in M5**: der Bildschirm halb im Block entsprach dem Dock des Produkts. Jetzt ein einteiliges Handgerät über einem flachen Sockel mit Schlitz; keine abnehmbaren Seitenteile, kein Rot/Blau |
| TwinLens (virtualboy) | „eine VR-Brille“ | **geändert in M5**: Rahmen auf Zweibein war das Gerät; mit Bügel oben las es sich als Radio. Jetzt Visier mit Nasenkerbe und Riemen, ohne Fuß |
| ControlPanel (arcade) | „ein Spielhallen-Bedienfeld“ | generische 2×3-Knopfreihe |
| Stick (atari2600) | „ein Joystick“ | Kategorie schlechthin, kein Gehäuse eines Modells |
| StickGrip (atari7800) | „ein Joystick mit Griff“ | Griff ohne Kugel, Tasten am Sockel |
| Floppy (pc) | „eine Diskette“ | Allgemeingut („Speichern“-Symbol) |
| KeyBlock (msx) | „eine Tastatur“ | Tastenblock ohne Layout |
| Tower (ps2) | „ein stehendes Gerät auf einem Ständer“ | **R2**: Platte mit Tiefe auf ovalem Ständer, senkrechter Schlitz, keine waagerechten Fugen – das Restrisiko „PC-Turm“ ist damit kleiner; keine Logos, kein Gehäuseprofil des Produkts |

**Unterscheidbarkeit ohne Text**: Auf `motive-farbig.png` und `motive-klein.png` unterscheiden sich alle 32 Systeme durch die Kombination aus Motiv (jedes einzigartig), Wandton und Farbband; die Disc-Familie (ps1, saturn, dreamcast, gamecube, segacd) trennt sich über Details (Spur, Bänder, Punkte, Größe, Schublade) und Farbe; wii ist seit R2 keine Disc mehr (Fernbedienung). Am knappsten sind ps1 und saturn in Platzhaltergröße (graue gegen indigofarbene Disc).

---

## 7. Screens

Reihenfolge der Ausarbeitung und Umsetzung: **7.1 Start, 7.2 Bibliothek, 7.3 Detail.** Das sind die drei Screens, die perfekt werden müssen. 7.4 bis 7.6 folgen danach und verwenden nur Objekte, die es dann schon gibt.

### 7.0 Shell, Skalierung, Kopf- und Fußleiste

**UiScale**: `s = clamp(min(W/1920, H/1080), 0,80, 2,0)` aus der Client-Größe in DIP, quantisiert auf 0,05 und erst 150 ms nach dem letzten Resize angewandt. Bei 1100×700 ergibt sich s = 0,80, logisch also 1375×875. Bei 1600×900 ist s = 0,833, logisch 1920×1080. Bei 2560×1440 ist s = 1,33.

**Visueller Baum** (behebt Moiré, die Pixeltreue der Snaps und das Problem mit dem eingebetteten Fenster):
```
MainWindow
└─ Grid "Root"                      Klassen: .nav-repeat, .motion-reduced, .motion-off
   ├─ RoomBackdrop                  ungeskaliert, Fenstergröße (2.5)
   ├─ LayoutTransformControl        ScaleTransform(s)
   │   └─ Shell: Kopfleiste (72) · PageHost · Fußleiste (56)
   ├─ GameScreen / GameScreenHost   ungeskaliert. Ein NativeControlHost liegt nie im skalierten Teilbaum
   ├─ FlightLayer (Canvas)          ungeskaliert, für die Starttransition (9.3)
   └─ OsdLayer                      ungeskaliert, Kanalanzeige; Größen werden selbst × s gerechnet
```
- **Pixelflächen im skalierten Baum** rechnen selbst in Gerätepixeln: `TextureFill` (4.5) und `PixelScreen` (Bildschirm-Objekt). `PixelScreen` ermittelt die Rahmengröße in Gerätepixeln. Ist k = floor(min(Rahmen_B / Snap_B, Rahmen_H / Snap_H)) ≥ 2 *und* füllt k·Snap ≥ 85 % der Rahmenhöhe, zeichnet es ganzzahlig mit `BitmapInterpolationMode=None`, auf ganze Gerätepixel gerundet. Sonst zeichnet es contain mit `HighQuality` (eine Röhre war auch weich), und die Scanlines decken die Weichheit. Der Rest wird mit Raum-Tief gefüllt.
- **Decodes**: Bitmapbreite = Slotbreite × s × RenderScaling, bucketiert (bestehender `AsyncImageLoader`).
- **Seitenrand** 96 px (logische Breite ≥ 1600), sonst 56 px. Grid-Abstand 28 bzw. 20 px.

**Kopfleiste (72 px) „Kanäle“**
```
│ ■RetroHub    1 Start   2 Bibliothek   3 Konsolen              ⌕  ⇩  ▣  ⚙      Wireless Gamepad   21:14 │
│ x48          x260        ▔ (Kanalmarke 3×12)                   48×48, Abstand 8                  170 px frei │
```
- **Wortmarke**: Bernstein-Quadrat 8×8 (R 0), 10 px Abstand, „RetroHub“ in Plakat 20.
- **Kanäle**: Ziffer in Zahl 15 Text 3, 6 px Abstand, Name in UI 20 Text 2, Abstand zwischen Kanälen 40. Der aktive Kanal hat den Namen in Papier und darunter eine **Kanalmarke** 3×12 px in Bernstein, 8 px unter der Grundlinie, linksbündig unter der Ziffer. Auf der Tastatur schalten die Tasten 1/2/3 direkt um.
- **Icon-Buttons** 48×48, Icon 28, in der Reihenfolge Suche, Importe, Emulatoren, Einstellungen. Statusmarke ist ein 8-px-Punkt oben rechts (Importe: Bernstein bei neuen Funden; Emulatoren: Warnung, wenn ein benutztes System keinen Emulator hat). Fokus: Inversion.
- **Gerätename** als Text (5.3), danach die Uhr in UI 18.
- **Hintergrund**: auf Start und Detail transparent (der Kopf-Scrim ist Teil der geflachten Scrims: Raum-Tief 70 % → 0 über 160 px), sonst Raum. Keine Linie darunter. Die Fläche bleibt Drag-Area, rechts bleiben 170 px für die Caption-Buttons frei.

**Kanalanzeige (OSD)**: Bei jedem Kanalwechsel erscheint oben rechts im Seitenbereich (rechtsbündig am Seitenrand, y 104) die Kanalziffer in Zahl 40 und der Name in Rücken 28, beide in der aktuellen Lichtfarbe. Sie erscheint **hart**, steht 1200 ms und verschwindet hart (ein TextBlock-Paar, Opacity-Keyframes ohne Überblendung). Beim Spielstart zeigt sie 300 ms den Systemcode (9.3). In den Einstellungen abschaltbar („Kanalanzeige: An/Aus“).

**Fußleiste (56 px)**
- **Links**: Ortssatz in UI 16 Text 2, z. B. „Bibliothek › Game Boy Advance, Spiel 3 von 7“. Die Elternteile stehen in Text 3. Die Anzahl der Sammlung steht hier („Alle Spiele, 12 von 66“) und nicht in einer Überschrift.
- **Rechts**: höchstens 4 Hinweise (unter 1300 px logisch: 3). Glyphe 28 (5.3) plus UI 16 Papier, Abstand 28. Von rechts: Zurück, Bestätigen, dann Favorit/Suche. Die Hinweise kommen vom fokussierten Element (`IHintSource`, Fallback auf die Seite).
- **Hintergrund**: transparent über dem Raum-Verlauf (4.6).

**Navigation**
- *Menü* fokussiert den aktiven Kanal. *Zurück* in der Kopfleiste kehrt zum gemerkten Fokus der Seite zurück.
- *Hoch* vom oberen Seitenrand erreicht die Kopfleiste über die räumliche Navigation. Der Sprung „Links am Rand → Sidebar“ entfällt.
- *Schulter links/rechts* wirkt immer seitenlokal: Bibliothek = Fach, System = Nachbarsystem, Detail = Abschnitt, Einstellungen = Abschnitt.
- *Zurück* geht genau eine Ebene zurück und stellt den Fokus wieder her (PageHost).
- Bei 1100 px (logisch 1375) passt alles: Wortmarke 150, Kanäle ≈ 400, Icons 216, Status ≈ 130, zusammen ≈ 900 < 1205.

---

### 7.1 Start: „Der Fernseher im Regal“

**Aufgabe:** sofort weiterspielen und sehen, was gleich läuft. Held ist der **Bildschirm**. Daneben steht die Schachtel des Spiels auf demselben Brett. Text und Aktionen stehen links, unten verankert, und schweben nicht in einer freien Spalte.

```
1920 × 1080
┌───────────────────────────────────────────────────────────────────────────────────────────┐ 0
│ ■RetroHub   1 Start  2 Bibliothek  3 Konsolen                ⌕ ⇩ ▣ ⚙    Gamepad   21:14 │ 72  Kopfleiste (transparent)
│             ▔                                                                             │
│                                              ┌───────────┐    ┌──────────────────────────┐│ 140
│                                              │           │    │░░░░░░░░░░░░░░░░░░░░░░░░░░││
│  Zuletzt gespielt: heute                     │           │    │░░  BILDSCHIRM 560×420  ░░││  Etikett-Zeile (Licht)
│  Pokémon Smaragd-                            │   COVER   │    │░░  Snap → Titel →      ░░││  Plakat, gemessen
│  Edition                                     │  Rahmen   │    │░░  „Kein Signal“       ░░││
│                                              │  300×420  │    │░░  Scanlines 18 %      ░░││
│  Game Boy Advance. 46 Stunden in 41          │  (steht)  │    │░░░░░░░░░░░░░░░░░░░░░░░░░░││  read-l, Zahlen in Archivo
│  Sitzungen, zuletzt heute.                   │           │    │░░░░░░░░░░░░░░░░░░░░░░░░░░││
│  ◤▶  Spielen ◥  [ Details ]  [☆]             │           │    │░░░░░░░░░░░░░░░░░░░░░░░░░░││ 464–528 Aktionen
│▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁└───────────┘▁▁▁▁└──────────────────────────┘│ 560 Bühnenbrett (24, randlos)
│███████████████████████████████████████████████████████████████████████████████████████████│ 584
│                                                                                           │
│    ▯▯▯   ▯▯  ▯▯▯  ▯  ▯▯▯   ▯▯  ▯▯▯  ▯▯  ▯▯▯  ▯▯  ▮Alle                                   │ 632–832 Schachteln H 200
│▁▁[Zuletzt gespielt]▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁│ 832 Brett mit Etikett
│████████████████████████████████████████████████████████████████████████████████████████████│ 856
│    ▮▮ ▮▮ ▮▮ ▮▮ ▮▮ ▮▮ ▮▮                                                                   │ 908– nächstes Brett (angeschnitten)
├───────────────────────────────────────────────────────────────────────────────────────────┤ 1024
│ Start › Weiterspielen                               ◆ Spielen   ◆ Favorit   ◆ Zurück     │
└───────────────────────────────────────────────────────────────────────────────────────────┘
```

**Bühne (y 72–584, fix)**
- **Bühnenbrett** bei y 560, randlos über die Fensterbreite. Darauf stehen Schachtel und Bildschirm, beide mit der Unterkante auf y 560.
- **Bildschirm** (Stand M2-QA, ersetzt „Rahmen 560×420, 2 px Raum-Tief, R 0“): ein Gerät 560 breit und **468 hoch** bei x 1264–1824. Gehäuse Regal hell #2A2620 mit 1 px Lichtkante oben und 1 px Kante unten; 12 px Gehäuserand, darin eine **12 px tiefe Mulde** in Raum-Tief (R 0) um das Glas **512×384** (4:3, R 6, siehe 4.1); darunter das **Kinn 40** mit einer 6×6-Betriebsleuchte im *Licht* des Systems (aus: Raum) links und einem Lautsprechergitter aus 7 Linien (1 px Raum-Tief, Raster 3 px, 120 breit) rechts; zwei **Füße 64×8** in Raum-Tief bei 12 % von jedem Rand, ihre Unterkante ist die Brettoberkante (y 560). Bei 400 Breite: 9 + 9 + 273 + 9 + 29 + 6 = 335 hoch. Inhalt nach der Kette Snap → Titelbild → **„Kein Signal“** (Wand des Systems mit Farbband, Textur und Motiv auf 50 % Höhe, links oben der Systemcode in Rücken 28 Licht wie ein OSD; das Glas ist dabei um 4 % Papier aufgehellt). Scanlines 18 % über allem im Rahmen. Wechsel: Überblendung 180 ms. Der Schein (2.5) sitzt in der Mitte des Bildschirms.
- **Schachtel**: Rahmen 300×420 bei x 912–1212, contain, unten bündig, Kontaktschatten auf dem Brett. Querformat-Cover stehen niedriger. Fokussierbar: Bestätigen öffnet das Detail. Fokus nach 8. Keine Favoriten-Marke auf der Schachtel (der Stern-Button zeigt den Zustand).
- **Textblock** x 96–860, **unten verankert** (die Aktionsreihe endet bei y 528, 32 px über dem Brett; alles darüber stapelt nach oben):
  - Etikett-Zeile in Etikett 18, Licht: „Zuletzt gespielt: heute“. Bei Fokus auf einem Brett steht dort dessen Name („Favoriten“, „Neu in der Sammlung“).
  - Titel in Plakat, **gemessen** (3.2) auf 764 px Breite, maximal 2 Zeilen.
  - Meta-Satz `read-l` mit Zahlen als Archivo-Runs: „Game Boy Advance. **46** Stunden in **41** Sitzungen, zuletzt heute.“ Nie gespielt: „Game Boy Advance. Seit **25.** September in der Sammlung.“
  - Aktionsreihe: **Spielen** 280×64 (gefaste Platte, Bernstein, Tinte, Icon Play 24), **Details** 64 hoch (Regal hell, R 4), **Favorit** 64×64 (nur Icon).
- **Die Bühne folgt dem Fokus**: Ruht der Fokus `Dwell` (220 ms) auf einer Schachtel im Brett, zeigt die Bühne dieses Spiel. Der Text blendet 120 ms aus und 180 ms mit 8 px x-Versatz ein, Schachtel und Bildschirm überblenden in 180 ms, das Fernsehlicht in 420 ms. Bei Fokus auf einem **Systemrücken** zeigt die Bühne das System: Der Bildschirm zeigt „Kein Signal“ dieses Systems, die Schachtel ist die des zuletzt gespielten Spiels darin, der Titel ist der Systemname, der Satz lautet „Handheld von Nintendo, 2001. **7** Spiele, zuletzt Pokémon Smaragd-Edition.“ und die Primäraktion „Öffnen“. Ohne Brett-Fokus zeigt die Bühne das Weiterspielen-Spiel: **Kommt der Fokus auf die Bühne zurück (Aktionsreihe oder Schachtel) und ruht dort `Dwell` (220 ms), kehrt die Bühne zum Weiterspielen-Spiel zurück** (Etikett „Weiterspielen: gestern“, Primäraktion „Spielen“). Die Hauptaktion von Start ist also immer „Spielen“ für das letzte Spiel; ein gestöbertes Spiel startet man über seine Schachtel (M2-QA).
- **Fußleiste folgt dem Fokus sofort** (nicht erst nach dem Dwell): Aktionsreihe → „Start › Weiterspielen“, Bühnen-Schachtel → „Start › Bühne, ‹Titel›“, Brett → „Start › ‹Brett›, n von m“, „Alle“-Rücken → „Start › ‹Brett›, Alle anzeigen“. Hinweise: Bestätigen benennt die Aktion des Elements („Spielen“, „Öffnen“, „Details“, „Ordner hinzufügen“); „Favorit“ nur, wo F wirkt (ein Spiel auf Bühne oder Brett); leere Bibliothek nur „Ordner hinzufügen“ und „Menü“.
- **Erster Fokus**: „Spielen“.

**Bretter (y 600–1024, eigener Bereich mit vertikalem Fokus-Anker)**
- **Reihenfolge**: Zuletzt gespielt (≤ 11), Favoriten (≤ 16), Neu in der Sammlung (≤ 12), Fächer. Leere Bretter entfallen. **Varianten falten**: Auf den Start-Brettern steht ein Spiel je (System, normalisierter Titel) nur einmal; Regions- und Versionsmarken wie „(Germany)“, „(Rev 1)“, „[!]“ fallen beim Vergleich weg, es bleibt der erste Eintrag in der Ordnung des Bretts. Bibliothek und Systemseite bleiben vollständig.
- **Schachteln**: Höhe 200, Breite nach Bild (contain), Abstand 24, unten bündig. **Keine Bildunterschriften**, denn die Bühne ist die Bildunterschrift. Platzhalter tragen ihren Titel selbst. Keine Abzeichen auf den Schachteln (auch kein Favoriten-Stern).
- **Etikett** statt Überschrift: Es hängt links an der Brettfront bei x 96 (Papier, Tinte, Etikett 15, 8×3, R 0). Es gibt keine H2 und keinen „Alle anzeigen ›“-Link.
- **Ende jedes Bretts**: ein Rücken „Alle“ (72×200, Regal hell, Band in Bernstein, Text „Alle anzeigen“ in Rücken 18 gedreht). Er führt in die Bibliothek mit passendem Filter bzw. passender Sortierung.
- **Fächer-Brett**: `SystemShelf` (dasselbe Control wie Konsolen, 7.4). Systemrücken 72×200 (Wand hell, 8-px-Band in Farbe, Code −90° in Rücken 22 Papier **hängend 16 px unter dem Band** wie auf Konsolen, Motiv 40 px über der Anzahl, Anzahl unten in Zahl 15; M5-QA: vorher stand der Code am Fuß und dieselben Rücken lasen sich auf beiden Seiten verschieden). Der fokussierte Rücken wird zur **Front** 200×200 (Wand mit 8-px-Band und Motiv) mit **16 px Luft auf jeder Seite** (Platz 232; der Abstand der Rücken bleibt 8); Ring und Hebung 1,03 nach 8.2, die Front liegt dabei über ihren Nachbarn (ZIndex 1). Eine einzige Objektsprache.
- **Horizontaler Anker**: Die fokussierte Schachtel steht mit der linken Kante bei x 240 (am Brettanfang bei x 96). Die Reihe gleitet per `TranslateTransform` (Glide 220 ms; unter `.nav-repeat` 90 ms linear).
- **Vertikaler Anker**: Das fokussierte Brett steht mit der Schachtel-Oberkante bei y 632. Das nächste Brett ragt ab y 908 angeschnitten herein („mehr unten“). Bretter darüber gleiten unter die Bühne. **Nur der Bretterbereich als Ganzes ist beschnitten**, ab der Unterkante des Bühnenbretts (y 584); Reihen und Schachteln beschneiden nie (die gehobene Schachtel und ihr Ring ragen über die Reihe). Über der aktiven Reihe bleiben 48 px für Hebung (12), Ring (6) und den kurzen Kegel.

**Leere Bibliothek**: Der Bildschirm ist aus (Raum-Tief mit einer 1-px-Linie in Papier 20 % in der Mitte). Statt der Schachtel steht eine Linienzeichnung „leeres Regal“ (Papier 60 %) **direkt auf dem Bühnenbrett** (zwei Buchstützen und eine angelehnte Schachtel, ohne eigenes Brett). Das Brett „Noch leer“ darunter trägt **5 leere Fächer** 150×200 (M5-QA: eingetieft statt gestrichelt – Grund #000 20 %, 2-px-Schatten innen oben #000 40 %, 1 px Kante an den Seiten; Abstand 24, ab x 96), es wartet sichtbar auf Spiele. Text: „Das Regal ist noch leer“ und „Lege Spieldateien in einen Bibliotheksordner und starte einen Scan.“ Buttons: **Ordner hinzufügen** (Bernstein) und **Importe öffnen**.

**≈ 1100 px (logisch 1375×875)**: Bühnenbrett bei y 456 (M2-QA; vorher 464: Ring und Hebung stießen ans Brett). Bildschirm 400×300 bei x 879–1279, Schachtel-Rahmen 214×300 bei x 625–839, Textblock x 96–585 (Titel meist 40 oder 56). Bretter: Schachteln 160 hoch, Anker bei y 520 (40 px Luft unter dem Bühnenbrett), das nächste Brett ab y 752, Front 160 mit 16 px Luft je Seite.

---

### 7.2 Bibliothek: „Das Regal“

**Aufgabe:** viele Spiele schnell finden. Die Seite ist dicht und ruhig, hat **keinen Bild-Hintergrund** (2.5) und kein Kopf-Poster. Zwei Ansichten: **Regal** (Standard) und **Liste** (Ansicht-Taste).

#### 7.2.1 Regal-Ansicht

```
1920 × 1080
┌───────────────────────────────────────────────────────────────────────────────────────────┐
│ Kopfleiste                                                                                │ 72
├───────────────────────────────────────────────────────────────────────────────────────────┤
│ ◤ Alle 66  NES 6  SNES 8  N64 4  GB 3  GBC 2  GBA 7  NDS 5  PS1 6 … ◥    ⌕  ↕ Titel  ☆  ▦ │ 72–136 Ticker (fix)
│   ▔▔▔▔                                                                                    │
│  ▕▔▔▔▔▔▔▔▔▔▏                                                                              │ 142 Klammer (sichtbarer Bereich)
│  ███▇▇▇▇▇▇▇▅▅▅▅█████▆▆▆▆▆▆▆▅▅▅▅▅▅▅▅▅▅██████▆▆▆▆▆▆▆▆▆▆▆▆▆▆▆▆▆▆▆▆▆▆▆▆▆▆▆▆▆▆▆▆▆▆▆              │ 146–158 Regalspiegel (fix)
│      ┃ Fokusmarke                                                                         │
├───────────────────────────────────────────────────────────────────────────────────────────┤ 168
│                                                                                           │
│   ▯▯   ┌─┐   ▯▯▯  ┌──┐   ▯▯   ┌─┐   ▯▯▯  ┌──┐     8 Spalten, Fach 196×196 (Median), 28 │ 192
│ ▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁   │ 388 Regalkante 12 (Lippe 2)
│   Pokémon       Super Mario    Golden Sun                     caption 17/21, Block 42      │ 412
│   ▮GBA 2004     ▮SNES 1991     ▮GBA 2001                      Etikett-Zeile 15             │ 458
│                                                                                           │
│   ▯▯ …                                                       Zeilenabstand 310            │ 502
├───────────────────────────────────────────────────────────────────────────────────────────┤ 1024
│ Bibliothek › Alle Spiele, 12 von 66      ◤◥ Fach   ◆ Suche   ◆ Öffnen   ◆ Zurück          │
└───────────────────────────────────────────────────────────────────────────────────────────┘
```

**Ticker (Fach-Filter), 64 px, fix**
- Schulter-Glyphen (5.3) ganz links und ganz rechts.
- **Einträge**: Code in `code` (17) plus Anzahl in Zahl 15 Text 3, Abstand 32. Aktiver Eintrag: Code in Papier, 3-px-Unterstrich in der *Farbe* des Systems (bei „Alle“ Bernstein), 6 px unter der Grundlinie. Inaktiv: Text 2.
- **Überlauf**: horizontal verschiebbar, der aktive Eintrag bleibt sichtbar. An den Rändern liegen 48 px Raum-Verläufe als Overlay (keine OpacityMask).
- **Werkzeuge rechts** (44 hoch, rahmenlos): Suche (schiebt ein 360-px-Feld über den Ticker, 180 ms), Sortierung „↕ Titel“ (Auswahl-Overlay), Favoriten-Stern, Ansicht. Fokus: Inversion.

**Regalspiegel (neu, ersetzt die Rückenleiste und die Zähl-Überschrift), fix unter dem Ticker**
- Ein 12 px hohes Band über die Inhaltsbreite. **Ein Rücken pro Spiel** in Sortierreihenfolge, gefärbt in der *Farbe* seines Systems bei 60 % (M3-QA, vorher 70 %), mindestens 1 px breit, stehend auf einem kleinen Brett (1 px Brettlicht, 2 px Regal hell). Bei mehr Spielen als Pixeln bekommt jede Pixelspalte die häufigste Farbe ihrer Spiele. Bei Sortierung nach System entstehen geschlossene Läufe.
- **Klammer**: 1-px-Linie in Papier 70 % 4 px über dem Band mit 4-px-Ticks nach unten an beiden Enden (M3-QA), über die Breite des sichtbaren Bereichs (folgt dem Scrollen).
- **Fokusmarke**: 2 px breit, 20 px hoch, über das Band, in Bernstein (im Systemfilter: Licht).
- **Render**: Ein Control (`ShelfMirror`) rendert die Rücken bei jeder Änderung von Filter, Sortierung oder Anzahl einmal in ein `RenderTargetBitmap` (höchstens Breite × 12 px). Pro Frame werden nur dieses Bitmap, die Klammer und die Marke gezeichnet. Bei 1000+ Spielen sind das konstant 3 Draw-Calls.
- **Bedienung**: Mausklick oder Ziehen springt bzw. scrollt zur Position. Nicht fokussierbar, der Ticker ist das Bedienelement.

**Kopf (scrollt mit, `CoverGrid.Header`)**
- **„Alle“**: kein Kopf, das Grid beginnt sofort.
- **System gewählt**: **Wand-Banner** 200 px hoch, randlos. Systemname in Plakat 56, Satz `read-l` „Handheld von Nintendo, 2001. **7** Spiele, **46** Stunden gespielt.“, Motiv rechts 144 px hoch, stehend auf einem Bühnenbrett. Der Seitengrund wird zur *Wand* des Systems (Vollton) und `RhBoardLightBrush` zu dessen Licht. Wechsel per Schulter: Wandfarben `BrushTransition` 280 ms (Vollton, also echte Interpolation), Motiv gleitet 24 px und blendet über (220 ms).
- **Favoriten-Filter**: kein Kopf. Die Fußleiste sagt „Favoriten, 3 von 11“.

**Regal-Grid**
- **Fach** (M3-QA): Format = Median der Schachtelformate der ganzen Sammlung (11.4), bei „Alle“ geklemmt auf 1,00–1,40; im Systemfach der Median des Systems. Für einen Katalog aus SNES/GBA/DS ergibt das ≈ 1,0 (196×196) statt 1 : 1,25 mit 55 px toter Kopffreiheit über jeder quadratischen Schachtel. Spaltenzahl = floor((B + 28) / (176 + 28)), mindestens 3. Breite bis 1,15× gestreckt.
- **Schachtel**: contain, unten bündig, horizontal zentriert. Querformat steht niedriger, das ist gewollt.
- **Regalkante** (12 px mit Lippe, 4.2) unter jeder Zeile über die volle Inhaltsbreite, gezeichnet von `CoverRowPresenter` (Render-Override, statische Brushes). Er zeichnet auch die Kontaktschatten aller Schachteln der Zeile (aus deren `ArtRect`).
- **Bildunterschrift** (M3-QA): 12 px unter der Kante, **unter jeder Schachtel, auch unter Platzhaltern** (deren Fronttitel ist oft gekürzt, und nur so liegen alle Titel einer Reihe auf einer Linie). Titel in `caption` 17/21 in einem festen 42-px-Block (2 Zeilen), Text 2 (fokussiert Papier); die Etikett-Zeile steht deshalb in jeder Reihe auf derselben Höhe. Passt ein Titel aus drei oder mehr Teilen nicht in zwei Zeilen, wird er als Kopf + „…“ + Schluss gesetzt („BS The Legend of Zelda – … Kapitel 1“), denn Namensvettern unterscheiden sich am Ende. Ein Halbgeviertstrich hängt mit geschütztem Leerzeichen am Wort davor, eine Schlusszahl am Wort davor (keine Zeile beginnt mit „– …“, keine „1“ allein). Darunter die Etikett-Zeile: Farbbalken 3×14, Code in `label` Text 3, Variante (Namensvettern), Jahr in Zahl 15 Text 3. **In gefilterter Ansicht entfällt der Code** (Balken, Variante, Jahr bleiben; ist nichts davon da, bleibt die Zeile leer, ihre 20 px bleiben reserviert).
- **Status**: Fehlt die Datei oder ist das Spiel nicht startbar, sinkt die Cover-Deckkraft auf 0,45 und ein Icon `Missing`/`NotReady` 20 px erscheint in einer Raum-Tief-Fläche 28×28 (R 2) oben rechts im Cover. Favorit: Stern 18 px Favoritengelb in Raum-Tief 26×26 oben links. Es gibt keine System-Badges.
- **Aufkleber „Neu“** (hinzugefügt vor < 7 Tagen und nie gestartet): Etikett in Bernstein mit Tinte-Text „Neu“ (Etikett 13 ist hier als Ausnahme erlaubt, weil es Aufkleber-Text und keine Lesezeile ist), überlappt die obere linke Ecke um −4/−4. Beim ersten Fokus „auspacken“: 250 ms 12 px nach oben links und Ausblenden. Gilt danach pro Sitzung als gesehen.
- **Zeilenabstand** (M3-QA, Fach 1,0): 196 + 12 + 12 + 42 + 4 + 20 + 24 = **310** (vorher 361). Der Abstand 24 unter der Etikett-Zeile bleibt, denn er ist zugleich die Kopffreiheit der eingerasteten obersten Zeile (8.4). Bei 1080 sind zwei volle Reihen und die dritte angeschnitten sichtbar (≈ 24 Spiele).
- **Grid-Hintergrund**: Raum bzw. Systemwand. Kein Fernsehlicht, kein Korn.

**≈ 1100 px (logisch 1375)**: Rand 56, Abstand 20, 6 Spalten. Werkzeuge nur als Icons. Banner 160 hoch.

#### 7.2.2 Listen-Ansicht (Ansicht-Taste)

```
┌────────────────────────────────────────────┬──────────────────────────────────────────┐
│ Ticker + Regalspiegel (wie oben)                                                      │
├────────────────────────────────────────────┼──────────────────────────────────────────┤
│ ▮ Advance Wars                  GBA   ★     │            ┌──────────────┐              │
│ ▮ Banjo-Kazooie                 N64         │            │  Cover max   │              │
│█▌▁[Castlevania: Aria of Sorrow]▁ GBA  ★ ▁▁▁│            │   360×480    │              │  ← Fokuszeile
│ ▮ Chrono Trigger                SNES        │ ▁▁▁▁▁▁▁▁▁▁▁└──────────────┘▁▁▁▁▁▁▁▁▁▁▁▁▁ │
│ ▮ …                                         │  Castlevania: Aria of Sorrow (poster-s)  │
│   Liste 44 %, Zeilen 64 px                  │  Action von Konami, 2003. Nie gespielt.  │
│                                             │  ◤▶ Spielen ◥  [Details]                 │
└────────────────────────────────────────────┴──────────────────────────────────────────┘
```
- **Liste** 44 % der Breite, Zeilen 64 px: links ein 4×24-Balken in Farbe, Titel in `ui-l` Papier (1 Zeile, Laufschrift im Fokus), rechts Code in `label` Text 3 und Favoritenstern. Trennlinie 1 px Kante. Kein Tabellenkopf.
- **Fokuszeile**: Die Zeile wird Regal hell, der Balken wächst auf 8 px, oben liegt 1 px Lichtkante, und der Titel steht in einem **Papier-Chip** (Tinte, 8×4, R 2). Die Zeile wird nicht vollständig invertiert, weil ein 1280 px breiter Papierbalken heller wäre als jedes Spielbild.
- **Vorschau** 56 %: Schachtel contain max **420×560** (M3-QA, vorher 360×480: bei 1080 blieben ≈ 200 px leer darüber) auf einer Regalkante, Text unten verankert. Titel `poster-s` **gemessen** bei 40, sonst 32 px, bis 3 Zeilen, erst dann Ellipse. Meta-Satz (Datum als „Seit 8. November“, mit Jahr nur, wenn es nicht das laufende ist), Spielen und Details. Kein Fernsehlicht.
- **Listenzeile, zu langer Titel** (M3-QA, bis die Laufschrift kommt): Kopf bis zum ersten Trenner, „…“, dann so viel vom Schluss wie passt („BS The Legend of Zelda – …ne Tablets – Kapitel 2“; Avalonia `TextLeadingPrefixTrimming`).

#### 7.2.3 Stand M3 (umgesetzt, Entscheidungen aus der QA)

- **Schultertasten** blättern in der Bibliothek durch den Ticker (mit Umlauf: von „Alle“ nach links zum letzten System) und wechseln nie den Kanal; Kanäle wechseln über *Menü* oder die Tasten 1/2/3. Die Schulter-Glyphen an beiden Ticker-Enden sind per Maus klickbar. *Hoch* aus der ersten Regalzeile fokussiert den aktiven Ticker-Eintrag, *Runter* kehrt zur gemerkten Schachtel zurück.
- **Werkzeuge**: Suche, Sortierung, Favoriten (an: Stern gefüllt in Favoritengelb) und **ein** Ansicht-Werkzeug („Regal“/„Liste“ mit Icon). Es gibt keinen zweiten Unterstrich für die gewählte Ansicht, denn Bernstein gehört nach 2.2 nur dem Ticker-Eintrag „Alle“.
- **Regalspiegel**: Ein Rücken ist so hoch wie das Schachtelformat seines Systems (Format 1,0 steht 8 von 12 px, Hüllen ≥ 1,6 die vollen 12), ab 4 px Breite pro Spiel mit 1 px Fuge. So lesen sich Läufe eines Systems als Rückenreihe. Die Klammer folgt dem sichtbaren Bereich des Regals bzw. der Liste.
- **Fach eines Systems**: Das Wand-Banner ist der Kopf des Regals und läuft randlos über die Seitenränder. Es zeigt die Wand (38/62, Band, Textur), links Name in Plakat 56 (≈ 1100: 44) und den Satz, rechts das Motiv 144 px (≈ 1100: 112) auf einem Bühnenbrett. Beim Systemwechsel gleiten Text und Motiv 24 px herein (Glide). Steht der Fokus in der ersten Zeile, bleibt das Banner im Bild. Seitengrund = Wand, Brettlicht = Licht bei 22 %, Ring, Kegel und Spiegelmarke in Licht. Bei „Alle“ tragen die Bretter die Lichtfarbe bei 22 %.
- **Fachformat**: Median der gespeicherten Cover-Maße (≥ 8, sonst Startwert 11.4), *vor* dem Bau des Regals gesetzt. Rücken-Platzhalter übernehmen im Fach dieses Format und stehen damit wie ihre Nachbarn (PAL-SNES: quer).
- **Namensvettern**: Haben zwei Spiele eines Systems denselben Titel (ohne Regions- und Versionsmarken), steht in der Etikett-Zeile die Region bzw. Sprache („Englisch“, „Japan“, „Europa“). Fehlt sie, steht „Original“. Bildunterschriften setzen „ - “ als Halbgeviertstrich.
- **Aufkleber „Neu“**: nur, wenn höchstens ein Viertel der Bibliothek (und mehr als 3 Spiele) die Regel erfüllt. Ein frisch importiertes Regal ist keine Neuigkeit, und eine Wand voller Aufkleber markiert nichts.
- **Listen-Ansicht**: Die Vorschau folgt der Fokuszeile (Schachtel bis 360×480 auf einer Regalkante, die bis zum rechten Fensterrand läuft). *Links* aus „Spielen“ kehrt zur Zeile zurück. Der Titel der Fokuszeile steht als Tinte auf dem Papier-Chip (vorher Papier auf Papier, weil eine App-Klassenregel die Fokusregel überstimmte).
- **Leere Bibliothek**: Linienzeichnung „leeres Regal“ auf einer Regalkante, „Das Regal ist noch leer“ (poster-s), ein Satz, **Ordner hinzufügen** (Bernstein, erster Fokus) und **Importe öffnen**. Spiegel, Schultern und Werkzeuge entfallen. Fußleiste: „Noch keine Spiele“, Hinweise nur „Ordner hinzufügen“ und „Menü“. Ohne Treffer: Satz und Hinweis, Hinweise Suche, Fach, Menü.
- **Laufschrift** in Listenzeilen ist noch nicht umgesetzt (Kopf-…-Schluss-Kürzung, 7.2.2).

**M3-QA (zweite Runde)**
- **Einrasten** und Ausblendung unter dem Spiegel (8.4): keine Zeile wird mehr unter dem Regalspiegel angeschnitten.
- **Ticker**: Die Schulterplatten sind ruhig (36×22, Regal hell, 1 px Kante, Keil in Text 2; Hover/Druck: Papier mit Tinte-Keil). Sie bleiben in beiden Eingabearten neutrale Blätter-Platten: Tastenkappen „Strg+Bild auf“ an beiden Enden drängten bei 1920 die letzten Systeme aus dem Ticker, und die Taste nennt ohnehin die Fußleiste. 16 px Luft zwischen Ticker-Kante (mit 48-px-Verlauf) und rechter Platte; der aktive Eintrag wird ganz innerhalb der Verläufe gehalten.
- **Regalspiegel**: Spinen auf einem kleinen Brett (1 px Brettlicht über 2 px Regal hell) bei 60 % Farbe; die Klammer ist eine 1-px-Linie in Papier 70 % mit 4 px Ticks nach unten – ein Rahmen, kein Fortschrittsbalken.
- **Wand-Banner**: Textur 9 % (statt der Systemstärke), Motiv 160 (≈ 1100: 124) auf dem Bühnenbrett.
- **Leere Bibliothek**: kein Ticker. Das Brett (Regalkante) steht bei ≈ 46 % der Seitenhöhe, darauf **eine angelehnte Schachtel** als Geste (2 px Text 3, 200 hoch; M5-QA: Buchstützen und gestrichelte Umrisse waren der generischste Leerzustand im Build) und 5 eingetiefte Fächer 150×200 (≈ 1100: 4), wie auf Start. Text 48 px unter dem Brett.
- **Banner** (M5-QA): die Lichtzone der Wand ist fest 40 hoch (≈ 1100: 24) statt 38 %; die 38-%-Kante lief durch die Glyphen des Titels.
- **Fußleiste** sagt „Regal“ statt „Raster“. **Demo-Modus** in der Kopfleiste ist Text (Etikett 15, Text 3) hinter einem 6×6-Quadrat in Warnung, kein Papierblock.

---

### 7.3 Spiel-Detail: „Die Schachtel aus dem Regal“

**Aufgabe:** alles über *dieses* Spiel und sicher starten. Held ist die **Schachtel**: aus dem Regal gezogen, leicht gedreht, sodass ihr Rücken sichtbar wird. Darunter liegt die **Rückseite** mit Text, Bildschirmfotos und den Startzeilen, wie auf einer Packungsrückseite.

```
1920 × 1080
┌───────────────────────────────────────────────────────────────────────────────────────────┐
│ Kopfleiste (transparent)                                                                  │ 72
│                                                                                           │
│   ╱▔│┌──────────────────┐    ▮ Game Boy Advance                                           │ 188 Etikett (Licht)
│  ▕ P││                  │    Pokémon Smaragd-                                             │     Plakat, gemessen
│  ▕ o││                  │    Edition                                                      │
│  ▕ k││      COVER       │                                                                 │
│  ▕ é││  Rahmen 360×504  │    Rollenspiel von Game Freak, 2004. Ein Spieler. Europa.       │ read-l
│  ▕ m││   (steht unten)  │    46 Stunden in 41 Sitzungen, zuletzt heute.                   │
│  ▕ o││                  │                                                                 │
│  ▕GBA││                 │    ◤▶  Spielen ◥   [☆ Favorit]   [⋯]                            │ 552–616 Aktionen
│  ▕2004│└────────────────┘                                                                 │
│▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁│ 660 Bühnenbrett
│███████████████████████████████████████████████████████████████████████████████████████████│ 684
│  [Über das Spiel]                                 [Start]                                 │ 724 Etiketten
│  Hoenn in seiner schönsten Form – mit             Emulator     Automatisch (mGBA)       › │ 64 je Zeile
│  Kampfzone, Rayquaza und Doppelkämpfen.           Startmodus   Eingebettet              › │
│  (read-l, max. 68 Zeichen, 8 Zeilen)              ✓ Bereit zum Start                      │
│  ┌──────────┐  ┌──────────┐                       Datei        Pokémon….gba, 15,5 MB    › │
│  │  Titel   │  │   Snap   │  Bildschirme 400×300                                          │
│ ▁└──────────┘▁▁└──────────┘▁▁                     [Gespielt]  9. Nov. 1:12 h │ 7. Nov. …  │ Ausleihkarte
├───────────────────────────────────────────────────────────────────────────────────────────┤ 1024
│ Bibliothek › Game Boy Advance › Pokémon Smaragd-Edition     ◆ Spielen  ◆ Favorit  ◆ Zurück │
└───────────────────────────────────────────────────────────────────────────────────────────┘
```

**Bühne (y 72–684)**
- **Hintergrund**: Fernsehlicht des Spiels (2.5), Schein in der Mitte der Schachtel. Ohne Bild: Wand des Systems plus Motiv-Umriss (6.4 Punkt 5).
- **Schachtel**: Rahmen 360×504 bei x 124, contain, unten bündig auf dem Bühnenbrett bei y 660. Fokussierbar: Bestätigen öffnet die große Coveransicht, *Mehr* bietet „Cover ändern“.
- **Generierter Rücken** (`BoxSpine`, nur hier und nie im Grid): ein Viereck links an der Schachtel, 28 px breit (x 96–124), so hoch wie das *tatsächliche* Bildrechteck. Die Unterkante steht auf dem Brett, die Oberkante fällt um 8 px nach außen ab (Punkte (96, oben + 8), (124, oben), (124, 660), (96, 660)). Damit wirkt die Schachtel leicht gedreht, ohne 3D. Füllung: *Farbe* gemischt mit 18 % #000 (Schattenseite), 1 px Lichtkante (Papier 20 %) auf der schrägen Oberkante. Inhalt: Titel −90° gedreht in Rücken 18, Text auf Farbe, von unten nach oben lesend, gekürzt mit Ellipse. Am Fuß stehen Systemcode (Rücken 15) und Jahr (Zahl 15). Das ist der einzige Ort, an dem das Jahr allein steht. Ohne Jahr bleibt nur der Code.
- **Textspalte** ab x 540, unten verankert (Aktionsreihe endet bei y 616):
  - System-Etikett: Balken 4×18 in Farbe plus Systemname in Etikett 18, Licht.
  - Titel in Plakat, gemessen auf 1100 px, maximal 2 Zeilen.
  - Meta-Satz `read-l`: „Rollenspiel von Game Freak, **2004**. Ein Spieler. Europa.“ In der zweiten Zeile die Spielzeit als Satz: „**46** Stunden in **41** Sitzungen, zuletzt heute.“ Nie gespielt: „Noch nie gespielt. Seit **25.** September in der Sammlung.“ Es gibt keine Zahlenspalten.
  - Aktionsreihe: **Spielen** 300×64 (gefaste Platte, Bernstein), **Favorit** 64 hoch (Regal hell, Icon plus Text, gefüllter Stern wenn aktiv), **Mehr** 64×64 (Overlay mit „Cover ändern“, „Cover entfernen“, „Ordner öffnen“, „Dateiinfo“, „Startoptionen zurücksetzen“).
- **Rechts neben der Textspalte bleibt Raum.** Der leere warme Raum ist gewollt, es gibt kein Wasserzeichen.
- **Spielen nicht möglich**: Der Button wird zum **Lösungs-Button**: gefaste Platte in Regal hell, 2-px-Kontur in Warnung, Icon Warning, Text „Emulator einrichten“. Er führt zu Emulatoren, gefiltert auf dieses System.

**Rückseite (y 724+, die Seite scrollt)**
- **Links „Über das Spiel“** (Etikett als Kopf, x 96–1000): Beschreibung in `read-l`, maximal 68 Zeichen pro Zeile und 8 Zeilen, dann „Weiterlesen“ (Overlay). Darunter die **Bildschirme**: Titelbild und Snap als Bildschirm-Objekte, je 400×300, Abstand 32, auf einer Regalkante stehend (`PixelScreen`, Scanlines). Bestätigen öffnet ein Vollbild-Diptychon. Fehlt die Beschreibung, rücken die Bildschirme nach oben. Fehlt beides, entfällt die Spalte und „Start“ rückt nach links. Es gibt keinen Ersatzsatz.
- **Rechts „Start“** (Etikett als Kopf, x 1080–1824): **Auswahlzeilen** à 64 px. Label in `ui` Text 3 links (Breite 200), Wert in `ui` Papier, Winkel rechts. Bestätigen öffnet ein Auswahl-Overlay. Darunter die Statuszeile mit Icon und Text aus `Problems`/`PlanSummary`. Die Dateizeile zeigt nur Name und Größe. Pfad und CRC stehen im Dateiinfo-Overlay (Cascadia Mono 15).
- **Ausleihkarte** (unter „Start“, nur wenn Sitzungen existieren): Etikett „Gespielt“, daneben die letzten 5 Sitzungen aus `IGameRepository.Sessions(gameId, 5)` als **Stempel**. Jeder Stempel ist ein Kasten mit 1 px Kante, R 0, Innenabstand 10×6, Text in `label` Text 2 mit Zahlen in Zahl 15, z. B. „9. Nov. · 1:12 h“. Die Stempel stehen nebeneinander und brechen bei Platzmangel um. Das ist der einzige Ort mit Verlaufsdaten.
- **Schulter links/rechts** springt zwischen Bühne, „Über das Spiel“ und „Start“.

**≈ 1100 px (logisch 1375×875)**: Schachtel-Rahmen 240×336 plus Rücken 20 px, Bühnenbrett bei y 470, Textspalte ab x 380, Titel maximal 56. Rückseite einspaltig in der Reihenfolge Start, Ausleihkarte, Über das Spiel, Bildschirme.

#### 7.3.1 Stand M4 (umgesetzt, Entscheidungen beim Bau)

- **Gezogene Schachtel** (`RhPulledBox`, ein `GameTile` mit `BoxSpine` + `CoverArt` in *einer* Hebe-Fläche): Rücken und Front heben sich als ein Objekt (1,06), Ring, Kegel, Brettstück und Kontaktschatten umfassen beide (`FocusLayer` nimmt den Rücken über `GameTile.Spine` dazu). Bestätigen bzw. Klick öffnet das Cover groß (Overlay), nie das Detail erneut. Querformat-Rahmen **520** (≈ 1100: 360) statt 460: eine PAL-SNES-Schachtel stand mit 333 px als flacher Streifen auf der leeren Bühne, jetzt 377. M4-QA: **Die Bühne folgt dem Inhalt**: bei Querformat steht das Brett 88 px (≈ 1100: 64) unter der Oberkante des höheren von Schachtel und Textspalte, mindestens bei Fenster-y 560 (≈ 1100: 420), höchstens bei 660; Hochformat bleibt bei 660. Die Rückseite rückt mit hoch. **Fokus auf der Bühne**: Die Fokusebene liegt über Bühne *und* Brett (`StageHost`), das beleuchtete Brettstück deckt die Front opak (Regal hell + 16 % Licht ≈ #4C3D26, 2 px Licht oben, 1-px-Unterkante bleibt), der Fokus-Kontaktschatten fällt unter die Front; der Kegel hat auf der Bühne halbe Stärke (6 %), der Raum ist schon vom Fernsehlicht beleuchtet. Vorher zeichnete das Brett über das Stück, die Schachtel wirkte eingesunken.
- **Rücken** druckt den Titel ganz, sonst den Haupttitel (vor „ – “/„: “), erst dann mit Ellipse; Code und Jahr am Fuß. Die Schrift (Papier oder Tinte) wird gegen die abgedunkelte Fläche gewählt.
- **Platzhalter im Detail**: kein zweites Rückenband auf der Front (`CoverArt.PrintedSpine = false`, der `BoxSpine` ist die Seite der Schachtel), dafür das Farbband der Wand oben (8–12 px). So trägt auch eine Schachtel ohne Bild ihre Systemfarbe auf Front und Seite. M4-QA: ab 300 px Höhe steht der Titel **oben** unter dem Band (Plakat 32, 24 px Innenrand, bis über die 38/62-Teilung), das Motiv ist das Bildfeld darunter (50 % der Höhe, rechts unten, 14 px Abstand) – wie eine bedruckte Schachtelfront statt einer leeren oberen Hälfte.
- **Ohne Bild** zeigt die Bühne die Systemwand mit einem kleinen Motiv-Umriss (40 % der Bühnenhöhe, 4 %, rechts auf dem Brett stehend) – kein Wasserzeichen (M4-QA, `BackdropSpec.GhostShare/GhostAlpha`).
- **Rückseite in zwei Stapeln** (904 | 80 | 744): links „Über das Spiel“ (Text, darunter Titelbild und Snap als zwei kleine Bildschirm-Geräte 400 breit auf einer Regalkante, Bildunterschrift „Titelbild“/„Im Spiel“), darunter **„Daneben im Regal“**; rechts „Start“ und die Ausleihkarte. **Ohne Beschreibung und Bildschirme** rückt „Start“ nach links, rechts stehen Ausleihkarte und „Daneben im Regal“ – die rechte Hälfte ist nie leer (M4-Abnahme). ≈ 1100: ein Stapel Start, Ausleihkarte, Über das Spiel, Daneben im Regal.
- **Daneben im Regal** (neu): das Fach, aus dem die Schachtel gezogen wurde. Die Nachbarn desselben Systems in Titelreihenfolge (je Seite bis 4, so viele wie die Spalte fasst) stehen mit Bildunterschrift auf einer Regalkante, an der Stelle der gezogenen Schachtel bleibt eine **gestrichelte Lücke im Format der gezogenen Schachtel** (contain, auf der Kante stehend). Fokus wie im Regal (Hebung, Kegel, Ring), Bestätigen öffnet den Nachbarn. Echte Daten, Bildpräsenz, kein „Das könnte dir gefallen“. M4-QA: Das Fach wird **gefaltet wie die Start-Bretter** (gleicher Titel ohne Regions-/Versionsmarken = ein Platz); drei gleiche Zelda-Cover neben der Lücke lasen sich als Duplizierungsfehler. Die anderen Fassungen des Spiels selbst stehen als Zeile **„Auch im Regal“** unter „Start“ („Deutsch, Englisch (NTSC), Französisch ›“; eine Fassung öffnet direkt, mehrere als Liste an der Zeile). Die Bildunterschriften lassen den Systemcode weg (ein Fach = ein System), Balken, Fassung und Jahr bleiben. Die Kante endet mit der Spalte.
- **Gespielt** (M4-QA: Etikett „Gespielt“ statt „Ausleihkarte“, verliehen wird nichts): 5 Plätze, von links gefüllt: der Stempel **„Eingang“** (Tag der Aufnahme), solange die Aufzeichnung bis zum Anfang reicht, dann die Sitzungen alt → neu, dann freie Plätze als **eine gestrichelte Grundlinie** (1 px Kante, 4/4) – leere Kästen lasen sich als Lade-Skelett. Jeder Stempel ist ein **Gummistempel**: 2 px Rahmen in Text 3 zu 70 %, innen 3 px eingerückt eine 1-px-Linie, Datum 15 Text 2 über Dauer 18 Papier (Zahlen als Zahl-Runs, Wörter in UI, damit „unter 1 Min.“ passt), beide Zeilen 20 px; abwechselnd −1,5°/+1° gedreht (statische RenderTransform). Nie gespielt: nur der Eingang-Stempel und der Satz „Noch kein Eintrag.“ Mehr als 5 Sitzungen: die letzten 5 und „Die letzten 5 von 41 Sitzungen.“ Die Demo hat dafür ein erzeugtes Spielprotokoll.
- **Dateizeile** ist eine Zeile wie die Auswahlzeilen („Datei · Name · Größe ›“) und öffnet die Dateiinfo.
- **Overlays** liegen über dem Seitenbereich (Kopf- und Fußleiste bleiben): Mehr (Cover wählen, Cover entfernen, Ordner öffnen, Dateiinfo, Startoptionen zurücksetzen – nur bei eigener Wahl), Dateiinfo (Ordner und CRC in Cascadia Mono; **QA 2: klappt unter der Zeile „Datei“ auf**, 7.7), Weiterlesen (nur bei mehr als 8 Zeilen; **QA 2: öffnet den Text an Ort und Stelle**, 7.7), Cover groß auf einem Bühnenbrett, beide Bildschirme groß. Raum-Tief 72 % (Cover/Bildschirme 94 %), kein Blur; die Abdunklung reicht **unter die Kopfleiste** (`PageViewModel.HeaderDim`, deren Inhalt bleibt darüber), so entsteht an ihrer Unterkante keine Naht. M4-QA: **Mehr** ist eine 420 breite Liste ohne Titel, die am ⋯-Button hängt (links bündig, 8 px darunter; ohne Platz darüber), „Auch im Regal“ ebenso an seiner Zeile; **Dateiinfo** 760 breit, Bezeichnungen UI 18 Text 3, Werte Papier. Cover groß: Kontaktschatten auf der Bildbreite (das Brett zeichnet neu, wenn das Bild geladen ist). Bildschirme groß: je höchstens 720 breit, Abstand 48, Bildunterschriften 12 px unter dem Brett. **Zurück schließt zuerst das Overlay** (`MainWindow` fragt die Seite vor dem Seitenwechsel), der Fokus bleibt darin und kehrt danach zum auslösenden Element zurück.
- **Navigation**: Runter von der Bühne führt nach „Über das Spiel“ (wenn fokussierbar), sonst auf die erste Startzeile; Hoch von dort zurück zu „Spielen“. Schulter: Bühne → Über das Spiel → Start → Daneben im Regal. Die Fußleiste nennt die Aktion des fokussierten Elements („Spielen“, „Importe öffnen“, „Cover ansehen“, „Ändern“, „Dateiinfo“, „Ansehen“, „Öffnen“) plus Favorit, Bereich, Zurück; im Overlay nur Auswählen/Schließen.
- **Scrollen schneidet nie durch den Titel** (M4-QA: nur drei Halte über der Bühne): oben (0), die ganze Aktionsreihe 24 px unter der Kopfleiste („Spielen“ bleibt sichtbar), das Bühnenbrett bündig unter der Kopfleiste; darunter scrollt die Rückseite frei. Freies Scrollen, das dazwischen zur Ruhe kommt, rastet nach 180 ms auf den nächsten Halt ein; unter der Rückseite entsteht dafür bei Bedarf Platz. In der Rückseite bekommt die Kopfleiste Raum als Grund (`PageViewModel.HeaderGround`, kein hellerer Fernsehlicht-Streifen über der Rückseite) und die 12-px-Blende (8.4) liegt unter ihr. Unter dem Bühnenbrett ist die Seite Raum (opak), damit das ortsfeste Fernsehlicht beim Scrollen nicht hinter die Rückseite wandert.
- **Lösungs-Button** nennt das *erste* Hindernis: fehlt die Datei, „Importe öffnen“, sonst „Emulator einrichten“.
- Die Auswahl in den Startzeilen ist seit M6 eine **eigene Auswahlliste** (`DetailSheet.Choice`): sie hängt wie „Mehr“ an ihrer Zeile (unter der Wert-Spalte, ohne Platz darunter darüber), die aktuelle Wahl trägt ein Häkchen und bekommt den Fokus, Bestätigen übernimmt sofort, Zurück schließt ohne Änderung. Das `ComboBox`-Popup (`RhChoiceRow`) ist entfernt. Der Winkel rechts ist der gefaste Chevron (`Icon.ChevronRight`, 16 px, Text 3, im Fokus Papier, 16 px Einzug), kein gefülltes Medien-Dreieck (M4-QA).

---

### 7.4 Konsolen: „Die Fächerwand“

Ein horizontales Bühnenbrett voller **Systemrücken** (`Shelf` mit `SystemSpine`, dieselben Controls wie das Fächer-Brett auf Start). Das fokussierte System dreht seine **Front** nach vorn.

- **Rücken**: 128×460, R 0. Wand hell, oben 8-px-Band in Farbe, Code −90° in Rücken 28 Papier, unten Anzahl in Zahl 28. Textur 6 % (`TextureFill`). Abstand 8. Alle stehen auf dem Bühnenbrett bei y 760.
- **Front**: 520×600, Wand komplett (zweifarbig, Band, Textur, Motiv 72 % rechts unten). Name in Plakat 44 (2 Zeilen, 40 px Innenrand), „Nintendo, 2001“ in `read` Text 2, „**7** Spiele“ in Zahl 40 unten links. Fokus: gefaster Ring in Licht.
- **Wechsel**: Animiert wird **nur die Breite eines Clip-Containers** (128 ↔ 520, Glide 220 ms). Der Inhalt der Front hat fest 520 px Breite und bricht nie während der Animation um. Der Inhalt blendet 120 ms nach dem Wachsen ein (Step). Der Seitengrund wechselt per `BrushTransition` (Vollton) zur Wand des neuen Systems (280 ms).
- **Anker**: Die Front steht in Slot 3. Der Versatz der Reihe wird aus den **Zielbreiten** berechnet und nicht aus den animierenden Breiten (kein Doppelruck). Unter `.nav-repeat` springt die Breite ohne Transition und die Reihe gleitet 90 ms linear.
- **Positionsschiene** darunter (M5-QA, ersetzt den Ticker): eine Linie mit einem Strich je System, rechts „Fach 3 von 13“.
- **Ohne Spiele**: Etikett „Noch leer“ am Brett, daneben Textbuttons in `ui` 16 Text 3. Bestätigen öffnet die Systemseite im Leerzustand.
- **≈ 1100 px**: Front 380×460, Rücken 96×360, Name 36.

#### 7.4.1 Stand M5 (umgesetzt, Entscheidungen beim Bau)

- **Keine Kartenwand mehr.** `SystemCard`, `ShelfGridPanel` und das 6×2-Raster sind gelöscht. Die Seite ist eine Wand: oben die Fächer der Systeme mit Spielen, darunter der Ticker, darunter eine niedrige Reihe „Noch leer“. Nichts scrollt vertikal; die Gruppe steht mittig in der Seite (oben 40–140 px).
- **Rücken = Streifen der eigenen Wand**: 38 % Wand hell über 62 % Wand mit Lichtkante (wie Wand und Front), Band oben, der Code hängt unter dem Band (liest von unten nach oben und endet 16 px unter dem Band), **das Motiv steht am Fuß** (Breite − 2 × 18 px), darunter die Anzahl in Zahl 28 Papier. So ist die Wand ohne Lesen unterscheidbar (6.4.2, M5-Abnahme). `SystemSpine.ShowMotif`, `SpineHeight` (Rücken 460 in einer 600 hohen Zelle).
- **Front** wie spezifiziert; der Ring ist der **stehende Ring** (unten offen, 8.1), auch auf Start. Die Front wächst in der Höhe mit der Breite (460 → 600), beide stehen auf demselben Brett.
- **Seitengrund**: die Wand des fokussierten Systems **im Schatten** (Wand + 45 % Raum-Tief, `BackdropSpec.WallShade`). Mit der vollen Wand als Grund verschwand die untere Hälfte der Front (gleicher Ton); so steht die beleuchtete Front aus ihrer eigenen Wand heraus.
- **Brett vor der Wand**: Die Bretter der Seite nehmen den Ton der fokussierten Wand an (`Shelf.BoardLook`, 4.2 „Brett vor der Wand“); das neutrale Regal hell war auf dunklen Wänden nur ein Strich.
- **Ticker**: Codes in `code` Text 2, das aktuelle System in Papier mit 3-px-Unterstrich in seiner Farbe; nicht fokussierbar (die Wand ist das Bedienelement), ein Klick dreht die Front des Systems nach vorn (R2: ebenso der erste Klick auf einen Rücken). Bei 32 Systemen läuft er horizontal mit 48-px-Verläufen im Seitengrund und hält das aktuelle sichtbar. Rechts „Fach 4 von 13“ (Zahlen als Archivo-Runs).
- **„Noch leer“** (statt Textbuttons): Systeme ohne Spiele stehen als **dünne Rücken** 40×132 (≈ 1100: 36×104) auf einem eigenen Bühnenbrett, das Etikett „Noch leer“ hängt daran. Sie drehen keine Front, sind leiser gezeichnet (Status: leer; fokussiert heller) und öffnen die leere Systemseite. Runter von der Wand erreicht sie, hoch kehrt zum gemerkten Rücken zurück. Textbuttons mit 20 Systemnamen wären eine Liste unter einer Wand gewesen; die Rücken bleiben in der Objektsprache.
- **Leere Bibliothek**: Wo die Front stünde, steht „Die Fächerwand ist noch leer“ (poster-s), ein Satz und **Importe öffnen** (erster Fokus); daneben warten acht gestrichelte Rückenplätze auf dem Brett, darunter alle Systeme als „Noch leer“.
- **Fußleiste**: „Konsolen › Nintendo GameCube“ bzw. „Konsolen › Atari Lynx, noch leer“; Hinweise Öffnen, Zurück, Suche, Menü. Die Schultern wechseln auf dieser Seite weiter den Kanal (sie hat keine Abschnitte).
- **Viele Systeme** (Harness `demoall`, 32 Systeme): die Reihe gleitet, die letzte Front bleibt am rechten Seitenrand (Versatz geklemmt).

#### 7.4.2 Stand M5-QA (Entscheidungen nach der Bildprüfung)

- **Die Front trägt ein inneres Fach** (Bildpräsenz; die 520×600-Front war eine leere Platte): bei 62 % der Fronthöhe ein 10-px-Brett im Brett-Ton der Wand (`ShelfBoard.DrawOnWall`), darauf bis zu **drei Spiele des Systems** (zuletzt gespielt zuerst, dann Titel; `SystemItemViewModel.Featured`, eine Abfrage pro System beim Laden) als echte Schachteln (`CoverArt`, Kontaktschatten 10 px auf dem Brett, Platzhalter nach 10.2, wenn ein Cover fehlt). Kunsthöhe 150 (≈ 1100: zwei Schachteln, 110), Abstand 12, ab dem Innenrand, im Fachformat des Systems; nie in Name und Hersteller hinein (die Höhe weicht, nicht der Text). Unter dem Brett links „4 Spiele“ (Zahl 40), rechts das Motiv mit Relief, so hoch wie der Raum unter dem Brett. **Abweichung von der QA-Vorgabe** („Motiv 56 %“): 56 % hätten die Schachteln überdeckt; das Motiv steht deshalb kleiner unter dem Brett. Die Schachteln entstehen erst, wenn die Front zum ersten Mal aufgeht, nach `DecodeDwell` und nie während einer gehaltenen Richtung; das Laden läuft über `AsyncImageLoader` abseits des UI-Threads.
- **Positionsschiene statt Ticker** (`PositionRail`): Der Ticker wiederholte die Codes der Rücken direkt darüber (dritte Liste neben „Noch leer“) und lief bei 32 Systemen in „Fach 32 von 32“. Jetzt: eine 2-px-Linie in Kante über die Inhaltsbreite, je System ein 12×4-Strich in seiner Farbe, das aktuelle 24×6 mit seinem Code in Papier darüber (Rücken 17). Rechts „Fach n von m“ in einer festen 180er-Spalte, 48 px Abstand. Nicht fokussierbar; ein Klick auf einen Strich dreht diese Front nach vorn. Codes kommen überall aus `SystemDef.ShortName` (eine Tabelle; „GC“/„MD“ gibt es nur im Demo-Katalog).
- **„Noch leer“ lesbar**: Rücken 48×148 (≈ 1100: 48×116), Code 16 in Text 2 (fokussiert Papier), hängend wie auf der Wand; still wird der Rücken über sein Band (50 %), nicht über unlesbare Schrift. 32 px Luft zwischen Schiene und Rücken (≈ 1100: 20).
- **Ein Anker für den Code** auf allen Systemrücken: 16 px unter dem Band, von unten nach oben lesend (Start und Konsolen gleich).
- **Leere Fächerwand**: acht eingetiefte Rückenplätze (wie 7.1) statt gestrichelter Umrisse.

### 7.5 Systemseite: „Das Fach“

- **Wand** y 72–440 (randlos, scrollt mit dem Grid, `CoverGrid.Header`), zweifarbig mit Band, Textur 8 %, Motiv 300 px rechts (rechter Rand 120), stehend auf dem Bühnenbrett bei y 440.
- Inhalt: Nachbarn oben als Textbuttons mit Schulter-Glyphen. Name in Plakat (gemessen), darunter **ein Satz** statt einer Zahlenreihe: „Handheld von Nintendo, 2001. **7** Spiele, **46** Stunden gespielt, zuletzt heute.“ Emulator-Status als Zeile mit 8-px-Statuspunkt und `ui` 16. Ohne Emulator: Warnungs-Icon, „Kein Emulator eingerichtet“ und der Button „Einrichten“. „⋯ Systeminfo“ öffnet das Overlay mit Ordnern, Endungen und BIOS.
- **Seitengrund unter dem Brett**: Wand des Systems als Vollton.
- **Grid**: Fokus und Lichtkegel in **Licht**, Etikett-Code entfällt. **Fachformat** aus den gespeicherten Cover-Maßen (11.4), *bevor* die Seite gebaut wird. Es gibt keine Neuberechnung während des Browsens.
- **Systemwechsel** per Schulter: Wandfarben `BrushTransition` 280 ms. Das alte Motiv gleitet 32 px aus (160 ms), das neue gleitet ein (220 ms). Das Grid wird ausgetauscht (Scene).
- **Wenige Spiele (≤ 6)**: „Fachende“-Linienzeichnung plus „Weitere Spiele kommen über Importe ins Fach.“ und **Importe öffnen**.

#### 7.4.3 Stand M7 „gezeichnet“ (Owner: „besonders unten, wo die Konsolen aufgelistet sind“)

- **„Noch leer“ = Pappschuber** statt 48 × 148 Farbfelder: Front 40–46 px breit (geseedet), 0–8 px niedriger als die Reihe, Pappton (Wand hell 30 % zu #6B5E4C) mit Fasern, Seitenfläche 5 px, Oberfläche 3 px mit dem **offenen Mund** (dunkler Schlitz), Kanten, Schmutz am Fuß, 1–2 Kratzer, jede zweite mit eingedrückter Ecke. Etwa jeder vierte lehnt (nie der erste) am linken Nachbarn, so weit, dass seine Oberkante aufliegt (asin(7/h)). Darauf ein **Papieretikett** mit Streifen in der Systemfarbe und dem Code in Tinte (Rücken 16; kurze Schuber bis 13, damit lange Codes ganz stehen), im Schatten der Reihe abgetönt.
- **Fokus zieht den Schuber heraus**: gerade gerichtet, 8 px hoch, Etikett in vollem Papier (Step 140 ms; Reduziert/Aus: ohne Hub, sofort). Kein Lift 1,03 für Schuber.
- Die Reihe endet an einer **Buchstütze** (`Bookend`, Blech-L mit Stanzschlitz, eigener Schatten; `Shelf.Bookend`), nicht abgeschnitten. Die Breite zählt beim Scrollen mit.
- **Rücken der Fächerwand und von Start**: Seiten- und Oberfläche, Wölbung, **gedrucktes Farbband** (eingerückt 7/5 px, 10/8 px hoch, Druckkante) statt bündiger Oberkante, Code mit 1-px-Prägung, Spielzahl auf einer **aufgesetzten Plakette** (gefast, zwei Nieten), Motiv mit Relief, Höhe 0/4/8 px niedriger (Start 0/2/4), jede dritte mit abgestoßener Ecke. Die „Alle“-Spine von Start ist dasselbe Objekt (`BoxFace`).
- **Offene Front**: Lippe (6 px) rundum, das innere Fach ist **eingelassen** (dunklere Rückwand, Innenschatten, Seitenkanten), die Schachteln werfen Schatten auf die Rückwand.
- **Positionsschiene = Senderskala**: 18-px-Leiste mit gefasten Enden, Maserung, eingravierter Nut; ein Hauptstrich je System in seiner Farbe (± 0,5 px, Länge ± 1, wie Druck), zwei Nebenstriche dazwischen; der **Knopf** (20 × 13, gefast, Griffrillen) mit der Nadel im Systemlicht **gleitet** zum neuen System (Glide 220 ms; bei gehaltener Richtung und Reduziert/Aus springt er), der Code steht auf einem **Papierfähnchen**. „Fach 4 von 12“ steht in einem eingelassenen Fenster mit Rahmen.

#### 7.5.1 Stand M5 (umgesetzt, Entscheidungen beim Bau)

- **Banner** 392 hoch (Wand 368 + Brett 24; ≈ 1100: 320), randlos über die Seitenränder, Textur 8 %. Oben links die Nachbarn als Textbuttons „◤ Game Boy Color  Nintendo DS ◥“ (`ui` 16, Fokus Inversion; nur wenn es Nachbarn gibt), unten links bündig zum Brett: Name gemessen 72/56/44 (≈ 1100: 56/44/36, höchstens 2 Zeilen), der Satz in `read-l`, die Statuszeile mit **Einrichten** (nur ohne Emulator, öffnet Emulatoren) und **⋯ Systeminfo**. Rechts das Motiv 300 (≈ 1100: 220) auf dem Brett, mit Kontaktschatten.
- **Das Brett liest sich vor jeder Wand**: Bretter vor einer Systemwand (Systemseite, Bibliotheks-Banner, Konsolen) haben eine Front aus *Wand hell + 10 % Papier* mit 2-px-Lippe (+ 18 %), die Unterkante ist die Wand abgedunkelt (`ShelfBoard.WallLook`, 4.2). Vor den warmgrauen Wänden (NES, SNES, GB) verschwand das Regal-hell-Brett (Kontrast ≈ 1,1 : 1); jetzt ≥ 1,4 : 1 gegen die Wand (`DesignM5Tests`).
- **Satz**: wie das Bibliotheks-Banner plus „zuletzt heute/gestern/vor 3 Tagen“ (ab 30 Tagen „am 3. März“); unter einer Minute Spielzeit bleibt es bei „noch nicht gespielt“ ohne „zuletzt“. Leeres Fach: „Handheld von Nintendo, 2001. Noch keine Spiele im Fach.“
- **Schultern** wechseln das System **an Ort und Stelle** (`Navigator.ReplaceArgument`): Zurück führt danach weiterhin genau eine Ebene zurück (Konsolen bzw. Start), nicht durch alle besuchten Systeme. Ohne Nachbarn tun die Schultern nichts (kein Kanalwechsel aus dem Fach). Beim Wechsel blenden Text und Motiv aus und gleiten 32 px von der Seite der Bewegung herein (Glide); die Wand wechselt sofort, der Seitengrund per `BrushTransition`. Der Fokus geht auf die erste Schachtel.
- **Navigation**: Hoch aus der ersten Reihe fokussiert **Systeminfo**, von dort hoch die Nachbarn; runter aus dem Banner kehrt zur gemerkten Schachtel zurück. Bestätigen auf einer Schachtel öffnet das Detail.
- **Systeminfo** ist ein Overlay wie die Dateiinfo (760 breit, Raum-Tief 72 % auch unter der Kopfleiste): Ordner und Dateitypen in Cascadia Mono, Emulatoren, BIOS-Dateien mit „nötig“/„optional“, **Schließen**. Zurück schließt es zuerst, der Fokus kehrt zum Button zurück; Richtungen und Schultern bleiben im Overlay.
- **Fachende** (1–6 Spiele): unter der Reihe der Satz „Weitere Spiele kommen über Importe ins Fach.“ und **Importe öffnen** (`CoverGrid.Footer`). Statt einer eigenen Linienzeichnung genügt der Satz: die Regalkante läuft ohnehin über die volle Breite und zeigt den freien Platz.
- **Leeres Fach** (System ohne Spiele, z. B. aus „Noch leer“): die Leerzustand-Sprache von Bibliothek und Start (Linienzeichnung auf einer Regalkante, drei gestrichelte Plätze), „Das Fach ist noch leer“, „Lege Spieldateien in games/virtualboy/ oder füge unter „Importe“ einen Ordner hinzu.“ und **Importe öffnen** (erster Fokus). ≈ 1100: Figuren und Plätze 150 hoch, damit der Button unter dem Banner sichtbar bleibt; er und „Systeminfo“ holen sich beim Fokus ihren Platz (`BringIntoView`), denn das Grid scrollt von sich aus nur zeilenweise.
- Das Grid übernimmt die Fachformat-Logik der Bibliothek (Median der gespeicherten Cover-Maße ab 8, sonst Startwert), gesetzt vor den Spielen; Platzhalter nehmen dieses Format an.

#### 7.5.2 Stand M5-QA (Entscheidungen nach der Bildprüfung)

- **Feste Lichtzone**: Die obere Wandzone (Wand hell) ist fest **88** hoch (≈ 1100: 64) und trägt nur die Nachbarzeile (in ihr zentriert, x 96); ohne Nachbarn **24** (8-px-Band + 16), und das Banner wird um die Differenz niedriger (keine tote Streifenzone). `SystemWall.SplitHeight`. Der Titel beginnt mindestens 24 px unter der Kante: `FitText.MaxTextHeight` lässt eine Größe nur mit so vielen Zeilen zu, wie in den Raum zwischen Lichtzone und Satz/Statuszeile passen – zwei Zeilen 72 fallen auf 56/44 (≈ 1100: 44/36) zurück. Test `Banner_title_never_reaches_into_the_walls_light_zone`: alle 32 Namen des echten Katalogs bei 1920, 1600, 1100 und 2560.
- **Kein Banner-Rest beim Scrollen**: Ab 70 % herausgescrollt erscheint ein **kompakter Kopf** (64: Wand des Systems, 4-px-Band, Name in Plakat 28 bei x 96, Motiv 44 auf einem 6-px-Brett rechts − 120, 12-px-Verlauf darunter; Einblenden 140 ms). Zeilen ab 1 rasten 24 px unter ihm ein (`CoverGrid.StickyInset`), und am Ende wächst ein Abstandhalter, damit jede Raststellung erreichbar ist (`CoverGrid.SnapToEnd`) – vorher klemmte der ScrollViewer kurze Fächer zwischen zwei Rasten und ließ einen Streifen Banner mit halbem Motiv stehen.
- **Fachende in einer Zeile**: „Weitere Spiele kommen über Importe ins Fach.“ und **Importe öffnen** (48 hoch) nebeneinander; unter dem Footer bleibt eine Reserve für die Fußleisten-Blende (Rand 40 + Innenrand 64), und der Button holt sich beim Fokus seinen Platz samt 96 px darunter. ~~**Offen**: Bei ≈ 1100 liegt die Zeile ungefokussiert noch in der Blende.~~ **R2 gelöst**: Bei einer Reihe steht das Fachende als Papiernotiz im Fach neben der letzten Schachtel (7.8).
- **Leeres Fach**: eine angelehnte Schachtel (2 px Text 3) und drei eingetiefte Fächer statt Buchstützen und gestrichelter Umrisse; ≈ 1100: 120 hoch, Titel 32 unter dem Brett, damit **Importe öffnen** über der Blende steht.
- **Satz**: Zeitangaben in ganzen Wörtern („zuletzt vor einer Woche“, „vor 3 Wochen“, „vor 5 Minuten“; `Fmt.LastPlayedInSentence`). Das Kürzel „Wo.“ verlor vorher mit `TrimEnd('.')` beide Punkte.
- **Systeminfo als Karteikarte** (QA 2: ersetzt durch die eine Vorlage in 7.7): 8-px-Band in der Systemfarbe links, Name in Plakat 28, Motiv 64 oben rechts über einer 4-px-Brettlinie, Beschriftungen in Rücken 15 Text 2, Werte in Cascadia 15 Papier; R 0, 1 px Kante plus 1 px Lichtkante oben; Innenrand 32/48.
- **Nicht umgesetzt** (QA-Vorschlag „Systemcode in Rücken 20 rechts in der Nachbarzeile“): der Name steht groß direkt darunter; ein zweiter Code wäre die Wiederholung, die auf Konsolen gerade entfernt wurde.

### 7.6 Werkbank: Emulatoren, Importe, Einstellungen (M6)

Gemeinsame Sprache: **flache Zeilen auf Raum**, Haarlinien, Etiketten als Gruppenköpfe, kein Fernsehlicht. Es gibt keine Inspektor-Spalte (einzige Ausnahme: der Testbild-Fernseher der Einstellungen, ein Objekt, 7.6.2) und keine Karten in Karten. Die Liste ist höchstens 1100 breit, linksbündig bei x 96; daneben eine Seitenspalte 420 nach 56 Abstand. Kopf und Raster: 7.6.2. Fokus auf Zeilen wie in 8.2.

**Einstellungen: „Service-Menü“** (statt Windows-Settings-Layout)
```
[Darstellung]
Lichtfarbe                                         ◀  ■ Bernstein  ▶
Hintergrund folgt Auswahl                          ◀  An  ▶
Größe der Oberfläche                               ◀  Automatisch  ▶
[Bewegung]
Bewegung                                           ◀  Wie Windows  ▶
Kanalanzeige                                       ◀  An  ▶
[Steuerung]  …   [Sprache]  …   [Über RetroHub]  …
```
- Eine durchgehende Liste. Abschnittsköpfe sind Etiketten, Schulter links/rechts springt zwischen Abschnitten.
- Zeile 72 px: Label in `ui-l` Papier links, optional eine Beschreibung in `meta` Text 3 (1 Zeile). **Wert rechts** in Etikett 18 zwischen den Icons `ArrowLeft`/`ArrowRight` (20 px).
- **Steuerkreuz links/rechts ändert den Wert sofort**, Bestätigen schaltet weiter. Die Maus klickt die Pfeile. Schalter sind Textwerte „An“/„Aus“, es gibt keine Kippschalter und keine Segment-Controls. Lichtfarben zeigen vor dem Namen ein 16×16-Muster.
- Aktionen („Datenordner öffnen“) sind Zeilen mit dem Wert „Öffnen ›“.

**Emulatoren: nach eigenen Systemen**
- Zeilen = **Systeme mit Spielen**. Links ein Mini-Rücken (8×48 Band in Farbe plus Code in Rücken 22), dann ein Satz in `read`: „spielt mit **mGBA** 0.10.3“ (Programmname in Zahl-Rolle) oder „Kein Emulator gefunden. Einrichten ›“ mit Warnungs-Icon. Emulatoren für mehrere Systeme stehen als Text in jeder Zeile.
- **Bestätigen klappt die Zeile auf** (180 ms Höhe) bei jeder Breite: Pfad (Cascadia Mono 15), Startmodi als Satz, Kerne, [Ordner öffnen] [Website].
- Darunter das Etikett „Weitere erkannte Programme“ (zugeklappt).
- Der Satz „RetroHub lädt nie etwas ohne deine Zustimmung herunter.“ bleibt als `read` unter dem Titel.

**Importe**
- Ordnerzeilen 72 px: Pfad in `ui` Papier, Zustand als Wert „◀ Aktiv ▶“ / „◀ Pausiert ▶“ (Service-Menü-Logik), Mehr-Menü. [+ Ordner hinzufügen] in Bernstein.
- Scan-Spalte rechts mit Etikett „Letzter Scan“ und **Segmentbalken** (14 Segmente 24×10, Abstand 4, Bernstein bzw. Regal hell, Prozent in Zahl 28 mit `tnum`; unbestimmt: 3 wandernde Segmente, 80 ms pro Schritt).
- **Lieferschein** nach einem Scan (statt Toasts): ein Kasten mit 1 px Kante, R 0, darin Zeilen in Etikett 18 mit Zahlen in Zahl: „**412** Dateien geprüft“, „**12** neu“, „**3** Hinweise ›“. Er bleibt stehen, bis der nächste Scan beginnt. Toasts bleiben nur für einzelne Ereignisse außerhalb des Scans (z. B. „Cover gespeichert“).
- „Spieldateien werden nie verändert.“ steht als `meta` unter der Ordnerliste.

#### 7.6.1 Stand M6 (umgesetzt, Entscheidungen beim Bau)

- **Service-Menü** (`ServiceRow` + `ServiceSetting`, Theme in `Controls.axaml`): eine Liste, Abschnitte als Etikett mit 1-px-Linie darunter. Zeile 72 px: Label `ui-l` Papier, eine Zeile `meta` Text 3; der Wert steht in einer **festen 320-px-Spalte** zentriert zwischen den gefüllten Pfeilen (`Icon.ArrowLeft/Right`, 20 px), damit alle Werte eines Abschnitts auf einer Achse stehen wie im Service-Menü eines Fernsehers. Lichtfarben mit 16×16-Muster. Fokus wie 8.2, die Pfeile werden Papier; am Ende einer geordneten Reihe bleibt der Pfeil bei 30 % und die Zeile stößt kurz an (Grenz-Feedback 8.4). **Wahlwerte laufen um, geordnete Werte („Zurück zu RetroHub“: Aus, 0,5 … 3,0 s) halten an ihren Enden; Bestätigen schaltet immer weiter** (auch rundum). Aktionen sind Zeilen mit „Öffnen ›“. Inhaltsbreite: eine Spalte `1000*` mit `MaxWidth 1100` neben einer `*`-Spalte, also höchstens 1100 und linksbündig, schmaler bei 1100 px Fensterbreite.
- **Abschnitte**: Darstellung (Lichtfarbe, Bewegung, Kanalanzeige, Controller-Hinweise, Im Vollbild starten), Steuerung (Mit Controller bedienen, Zurück zu RetroHub; Gerätezustand als `meta` neben dem Etikett), Bibliothek (4 Schalter), Sprache, Ordner (10 Aktionszeilen, Pfad als Beschreibung). Ein eigener Abschnitt „Bewegung“ entfiel, weil Abschnitt und Zeile denselben Namen trugen. **Kanalanzeige An/Aus** ist neu (`UiPreferences.ShowOsd`, schaltet `OsdLayer` für Kanalwechsel und Start-Code). „Hintergrund folgt Auswahl“ und „Größe der Oberfläche“ aus der Skizze oben gibt es nicht: dafür existiert keine Funktion, eine Attrappe wäre unehrlich. Ein Abschnitt „Über RetroHub“ entfällt aus demselben Grund (noch keine Versions-/Lizenzseite).
- **Emulatoren nach Systemen**: eine Zeile je System **mit Spielen und mit Kandidaten** (PC/Native fallen weg). Farbband 8 px (im Fokus volle Zeilenhöhe, ersetzt den Bernstein-Balken: `RhFoldRow.own-bar`), Code in Rücken 22 (Spalte 124, „GameCube“ passt), Name `ui-l` mit „12 Spiele“ in `meta`, darunter der Satz „spielt mit **mGBA** 0.10.5“ (Programm und Version in der Zahl-Rolle – Atkinsons Null mit Schrägstrich las sich als „Ø“). Was spielt, folgt der Reihenfolge des Planers: `settings.SystemEmulator`, sonst der erste installierte Kandidat (RetroArch nur mit Core). Lücken als **ein Satz**: „Kein Emulator gefunden.“ bzw. „RetroArch ist da, der Core fehlt.“ plus „Einrichten ›“. Aufgeklappt (`FoldPanel`, 180 ms, bei reduzierter Bewegung sofort): Pfad, Startmodi, Cores, **„Reihenfolge, in der RetroHub sucht“** mit Zustand je Kandidat (immer, wenn das System nicht starten kann), [Ordner öffnen] [Website]. Gefiltert von „Emulator einrichten“: nur dieses System, aufgeklappt, „Alle Systeme“ daneben. Die Falte heißt **„Weitere Programme“** (nicht „erkannte“), weil sie auch nicht installierte unterstützte Programme mit ihrer Website enthält.
- **Importe**: links die Ordner als Service-Zeilen („◀ Aktiv ▶“ / „◀ Pausiert ▶“, games/ fest „Immer aktiv“ ohne Pfeile); die Beschreibung beginnt mit der Zeit („Durchsucht heute, mit Unterordnern, …“), damit eine schmale Zeile das Unwichtige abschneidet. **Bestätigen öffnet die Mehr-Liste** des Ordners (Im Explorer öffnen, Pausieren/Wieder aktivieren, Ordner entfernen), links/rechts schaltet den Zustand ohne Neuaufbau der Liste (der Fokus bleibt). Rechts die Scan-Spalte (420): Satz „Zuletzt gescannt heute.“, „Jetzt scannen“ (während des Scans ersetzt durch „Abbrechen“), Prozent in Zahl 28 über dem Segmentbalken (`SegmentBar`, 14 × 24×10, Lauflicht 80 ms), Phase und Datei. **Lieferschein**: Kasten 1 px Kante, R 0, Kopf „Lieferschein“ + Zeitpunkt, Zeilen mit rechtsbündiger Zahlenachse (Zahl 18) und Text in Etikett 18 (Nullen in Text 3), „3 Hinweise“ als aufklappbare Zeile, Fuß „Dauer 2,8 s.“. Der Abschluss-Toast entfällt, wenn Importe offen ist; außerhalb (Start-Scan) bleibt er, sonst erführe man nichts. Abweichung von 2.2: **„Ordner hinzufügen“ ist ein Sekundärbutton**, „Jetzt scannen“ die eine Bernstein-Platte der Seite – zwei Bernstein-Platten nebeneinander konkurrierten.
- Fokus bleibt frei von der Fußleiste: Emulatoren und Importe holen das fokussierte Element mit 48 px Luft oben und 96 px unten in Sicht.

#### 7.6.2 Stand M6-QA (Entscheidungen nach der Bildprüfung)

- **Ein Kopf, ein Raster** (`Controls/WorkbenchFrame`, alle drei Seiten): Der Kopf scrollt nie. Plakat 56 mit Oberkante bei y 96 (1080er Referenz), eine Meta-Zeile 8 px darunter, Kopf-Buttons rechtsbündig an der Listenkante mit dem Text auf der Meta-Zeile. Das erste Abschnittsetikett steht auf jeder Seite bei y 212. Darunter scrollt nur die Liste; an ihrer Viewport-Oberkante liegt dieselbe 12-px-Raum-Überlagerung (100 % → 0) wie im Grid (8.4). Vorher wurde „Einstellungen“ beim Scrollen unter der Kopfleiste horizontal zerschnitten. Raster: Liste höchstens 1100 ab dem Seitenrand, Abstand 56, Seitenspalte 420. Der ScrollViewer ist nur Inhalt + 24 breit, damit sein Balken 24 px rechts vom Inhalt steht und nicht am Fensterrand.
- **Service-Menü als Gerät**: Zeilen tragen Nummern „01“ … fortlaufend über alle Abschnitte (Rücken 17, Text 3, Spalte 44 ab Zeile + 24; das Label steht 48 px weiter, abweichend von der Skizze x 96/144, weil dort der 8-px-Fokusbalken liegt). Der Wert steht in einem **Anzeigefenster** 320 × 40, Raum-Tief, 1 px Kante, R 0, darin Zahl 20 mit `tnum` in Papier; im Fokus leuchtet seine Oberkante 1 px in der Lichtfarbe. **QA 2: Das Fenster gibt es nur noch an der fokussierten Zeile** (7.7). Die Pfeile sind gefüllte Dreiecke 12 × 20 (`Path`), 12 px außerhalb des Fensters, Text 3 bzw. Papier im Fokus; am Ende einer geordneten Reihe 30 % und Anstoßen. Wertspalte 392.
- **Testbild-Fernseher (Ausnahme zu „keine Inspektor-Spalte“)**: In der Seitenspalte steht der Fernseher von Start (`PixelScreen` 420 breit) auf einer Regalkante, ab 1500 px logischer Breite. Er zeigt ein statisches Testbild (`Controls/TestCard`, unter Scanlines und Glas): Graustufen der App-Neutralen, Haarlinienraster, ein Mini-Regal mit fünf Schachteln, die mittlere mit stehendem Ring und Brettstück in der Lichtfarbe; die Betriebs-LED leuchtet in der Lichtfarbe. Er ist ein Objekt und kein Inspektor: Lichtfarbe färbt Ring, Brettstück und LED live um, „Kanalanzeige: An“ lässt „1 Start“ 1200 ms hart auf dem Bild stehen, eine neue Bewegungsstufe lässt das Regal einmal gleiten (bei reduziert/aus springt es). Kein Fernsehlicht im Raum.
- **Abschnitte**: „Sprache“ ist die letzte Zeile von Darstellung (der eigene Abschnitt sagte dasselbe zweimal). Neben „Steuerung“ steht der Gerätezustand als Tatsache („Kein Controller verbunden.“ / „… verbunden.“), nie „folgt“.
- **Emulatoren**: Statt Band + Code ein **Mini-Rücken** 40 × 72 (`Controls/MiniSpine`): Wand mit Wand hell oben 38 %, 8-px-Band in Farbe, der Katalog-Code senkrecht in Rücken 17 Licht, das Motiv 20 px in Licht bei 60 % am Fuß (passt der Code nicht darüber, entfällt das Motiv); der Name beginnt bei x 172. Der Satz „RetroHub sucht in `emulators/<name>/` und lädt nie etwas ohne deine Zustimmung herunter.“ steht als Meta neben „Deine Systeme“. Fehlt **sechs oder mehr Systemen jedes Programm**, sagt es der Kopf einmal („Noch kein Emulator installiert – RetroHub sucht in emulators/.“), und die Zeilen sagen nur „Kein Emulator.“ in Text 3 ohne Warnzeichen; einzelne Lücken behalten das Zeichen. Aufgeklappt ist die Zeile ein **Schlüssel/Wert-Raster** (Schlüssel Etikett 15 Text 3 ab x 228, 168 breit; Werte ab x 412 in `read` Text 2; Zeilen 32 px; 16 px vor den Buttons): Programm („RetroArch 1.21.0 in `emulators/…`“, Pfad Cascadia Mono Regular 15 Text 2), Startmodi (nur wenn das System starten kann), Cores (nur die Cores **dieses** Systems: „Benötigt: a oder b – installiert: a.“) und Reihenfolge. Codes kommen immer aus dem Katalog; der Katalog nennt GameCube „GameCube“ (die Demo „GC“) – der senkrechte Rücken trennt den Code ohnehin vom Namen.
- **Importe**: Die Mehr-Liste eines Ordners klappt **unter seiner Zeile** auf (`FoldPanel`, 180 ms, drei 56-px-Zeilen ab x 120, Haarlinien in Kante), ohne Abdunklung; die Ordnerzeile behält ihren Fokus-Look (Klasse `open`). Während eines Scans heißt das Etikett „Scan läuft“, der Satz „Gestartet um 19:53.“, die Phase „29 von 80 Dateien geprüft.“ (Zahlen als Zahl-Runs). Die Hinweisgruppen heißen „1 doppelte Datei“, „2 nicht zugeordnet“; Ordner beschreiben sich als Satz („…, nur oberste Ebene, immer als PlayStation 2.“).
- **Lieferschein als Zettel**: Fläche Regal, oben eine 6-px-Zahnung (Abrisskante) in Raum (`Controls/Sawtooth`), Kopf „Lieferschein“ in Rücken 17 Text 2 mit dem Zeitpunkt rechts, Zeilen Wort – gepunktete Führungslinie (Kante, 2 an / 4 aus, `DotLeader`) – Zahl rechtsbündig. Hat der Scan Spiele gebracht, stehen sie als kleine Rücken (24 × 96, Band in Systemfarbe, Titel in Rücken 14) unter den Zahlen. Eine laufende Nummer („Nr. 0042“) gibt es nicht: RetroHub zählt Scans nicht, eine erfundene Nummer wäre unehrlich.
- **Detail-Auswahl**: Die Auswahlliste für Emulator und Startmodus ist kein Popup mehr, sondern klappt **unter ihrer Zeile** auf (180 ms, schiebt die folgenden Zeilen nach unten), Zeilen 56 px, der Text beginnt genau in der Wertspalte (Zeile + 216), das Häkchen steht links davor. Vor dem Öffnen holt die Seite Zeile und Liste mit 260 px Luft unten in Sicht. Kein Abdunkeln.
- **Shell**: Das Quadrat vor „Demo-Modus“ ist Text 3 (ein orangefarbenes Quadrat las sich neben der Wortmarke als zweite Standby-LED). Die Fußleiste eines Spiels oder Systems beginnt nie mit einer Werkbank-Seite: von Importe/Emulatoren/Einstellungen aus geöffnet heißt sie „Bibliothek › …“.

#### 7.6.3 Stand M7 „gezeichnet“

- Trennlinien sind **Rillen** (4.3). Das Wertfenster der fokussierten Zeile ist **eingelassen** (`WindowGlass`: Innenschatten oben/links, helle Unterkante, schwache diagonale Spiegelung). Die Pfeile ◀ ▶ sitzen dann auf kleinen **Tasten** (`KeyCap`, 26 × 34, Tiefenkante 3 px), die beim Klick auf 1 px nachgeben.
- Die „Bereit“-Segmente und der Scan-Balken sind **Kontrolllampen** (`Lamp`: gefaste Linse, Glasglanz oben links, dunkler Fuß, Rand).
- Die Mini-Rücken der Emulatoren tragen Wölbung, gedrucktes Band und Kanten.
- **Lieferschein**: unregelmäßig abgerissen (Zahnteilung 12–20 px, Tiefe 4–9 px, geseedet), Papierfasern, Wölbung rechts, Schatten unter der Unterkante, Leitpunkte in leicht wechselnder Stärke (Thermodruck). Der dunkle Papierton bleibt (Kontrast).
- Die **Spielen-Taste** (und jede `ChamferPlate` mit `Depth`) hat Tastenhub: 3 px dunklere Kante (Ton × 0,66) unter der Fläche, 1 px Licht oben; gedrückt sinkt die Fläche auf 1 px Kante.
- Der **Fernseher**: Gehäuse mit Korn, Wölbung, Kanten, Schmutz am Fuß, Stufe in die Mulde, Lautsprecherschlitze mit heller Unterlippe.
- Der **Regalspiegel** zeigt die Rücken mit heller Oberkante und ± 1 px Höhe; die Fokusmarke ist ein Klipp mit gefastem Kopf.

### 7.7 Stand QA 2 (Gesamt-QA nach M6, Entscheidungen nach der Bildprüfung)

Gesamtbefund: Start, Bibliothek, Detail, Konsolen und Systemseite lesen sich als ein Produkt; das Restrisiko lag in der Werkbank und in den Overlays. Umgesetzt:

- **Service-Menü ohne Kastenwand**: Ungefokussierte Zeilen haben keine Füllung und keinen Rahmen am Wert – Zahl 20 Papier zu 85 % in derselben 320er Spalte, die Pfeile Text 3 zu 40 % (am Ende einer geordneten Reihe 15 %). Nur die **fokussierte** Zeile (bzw. die Ordnerzeile mit offener Mehr-Liste) zeigt das Anzeigefenster: Raum-Tief, 1 px Kante, **2 px** Oberkante in der Lichtfarbe, Pfeile Papier – das eine leuchtende Display der Liste wie im OSD eines Fernsehers. Ein fester Wert („Immer aktiv“) ist schlichtes Text 2, nie ein Fenster. **Schmale Zeilen** (Zeile < 900 breit, z. B. Importe bei ≈ 1100): Wertspalte 292, Fenster 220, die Beschreibung bricht auf 2 Zeilen um (Zeile ≈ 88 statt abgeschnitten nach ≈ 270 px).
- **Keine Dialog-Kits mehr im Detail**: Die **Dateiinfo** klappt unter der Zeile „Datei“ auf (`FoldPanel`, 180 ms, wie die Auswahlliste): Schlüssel Etikett 15 Text 3 in der Label-Spalte der Zeilen (168 breit), Werte ab Zeile + 216 in `read` Text 2 (Zahlen als Zahl-Runs), Ordner, CRC und Seriennummer in Cascadia Mono Regular 15 Text 2, darunter **Ordner öffnen**; der Dateiname steht nicht noch einmal (die Zeile nennt ihn). Bestätigen auf der Zeile bzw. Zurück klappt zu, kein Abdunkeln. **Weiterlesen** öffnet „Über das Spiel“ an Ort und Stelle: bis zu 12 Zeilen, längerer Text scrollt mit Hoch/Runter darin, unten eine 12-px-Blende in Raum; der Knopf heißt dann „Weniger anzeigen“ und erscheint nur, wenn 8 Zeilen den Text kürzen. Auf dem Abdunkel-Layer liegen nur noch die Listen (Mehr, Auch im Regal) und die großen Ansichten.
- **Systeminfo** ist die eine verbliebene Tafel, mit einer Vorlage: Regal, 8-px-Band in der Systemfarbe oben, Name in Rücken 22, darunter das Schlüssel/Wert-Raster der Emulatoren-Zeile (Schlüssel Etikett 15 Text 3, 168 breit; Werte `read` Text 2 ab + 200; Ordner und Endungen in Cascadia Mono Regular 15), **Schließen** in der Wertspalte. Sie hängt 12 px unter der Aktionszeile des Banners (links auf dem Seitenrand) statt mittig zu schweben. Eine echte Falte im Banner hätte die feste Bannerhöhe des Grid-Kopfs (7.5.2) gesprengt.
- **Sitzungsanzeige statt Toast**: Nach einer Sitzung erscheint oben rechts – am Ort der Kanalanzeige, im *Licht* des Systems – die Gesamtspielzeit in Zahl 28 und darunter in Etikett 15 „Insgesamt gespielt, diese Sitzung unter 1 Min.“; hart an, 2400 ms, hart aus, unabhängig vom Schalter „Kanalanzeige“ (`OsdLayer.ShowNotice`, `LaunchCoordinator.SessionReturned`). **Meldungen** sind keine schwebenden Karten mehr: Sie docken **links in der Fußleiste** an und ersetzen 4 s lang den Ortssatz (Icon 18 + ein Satz, das Detail in Text 2 dahinter, die neueste oben), nie über Inhalt. Über dem eingebetteten Spiel schneidet der Spielbildschirm dafür sein Loch an genau dieser Stelle.
- **Detail-Bühne folgt jeder niedrigen Schachtel**: Nicht nur Querformat-Cover – jede Schachtel, deren Bild deutlich niedriger als ihr Rahmen ist (< 90 %, z. B. der quadratische GBA-Platzhalter), lässt das Brett zum Inhalt aufrücken (88 px Luft über dem höheren von Schachtel und Textspalte, Brett bei Fenster-y 560 … 660). Die leere Hintergrundfläche über dem Platzhalter schrumpft um ≈ 90 px. Unter der Rückseite stehen 160 px (der letzte Abschnitt endet nie in der Blende der Fußleiste). Sobald die Seite scrollt, hat die Kopfleiste Raum als Grund und darunter liegt die 12-px-Raum-Blende wie in der Werkbank (vorher nur in der Rückseite; Schachtel und Knöpfe wurden an der Kopfleiste hart geschnitten). **Bildschirme groß**: Abdunklung 96 %, die Bildunterschriften stehen auf einem festen 48-px-Band in Regal unter dem Brett.
- **Auch im Regal** zählt statt zu kürzen: „Chinesisch, Deutsch, Englisch (NTSC) und 4 weitere“. Ist im Meta-Satz nur die Region bekannt, steht sie vor dem Spielsatz in derselben Zeile („Japan. Noch nie gespielt. Seit …“) statt als Waise darüber.
- **Emulatoren – Gerätefach**: Die Seitenspalte (ab 1500 px logisch) trägt Etikett „Bereit“, „10 von 13“ in Zahl 28 und je System ein 24 × 10-Segment in seiner Farbe (kann starten) bzw. in seinem Wand hell (kann nicht), darunter **Ordner öffnen** und **Neu suchen** (im Kopf nur, wenn die Spalte fehlt). Ist nichts installiert, sagt das Label neben „Deine Systeme“ nur noch „RetroHub lädt nie etwas ohne deine Zustimmung herunter.“ (der Kopf nennt den Ordner schon). Programmnamen stehen in `read` SemiBold Papier, Versionen in Zahl 15 Text 3 – auch im aufgeklappten Raster („RetroArch 1.21.0“ hatte Atkinsons durchgestrichene Null).
- **Mono-Regel** (3.1): Pfade, Endungen, Prüfsummen überall in Cascadia Mono **Regular** 15 Text 2 (`TextBlock.mono`, `Run.mono`, `TextBlock.kv-mono`). Programmnamen in Sätzen über `Sentence.Name(…)` in `read` SemiBold Papier („Startet mit **RetroArch (snes9x)** im Vollbild.“), nicht in der Zahl-Rolle.
- **Importe**: Die Cover-Gruppe steht in der Ordnerspalte unter den Ordnern (die Scan-Spalte endete mit offenem Lieferschein unter der Fußleiste, während links Platz war). Der **Lieferschein** ist eine Stufe heller als Regal (#27231E ≈ Wand hell 60 % auf Raum), die Abrisskante 8 px; die geöffneten Hinweise zeigen **höchstens 3 Einträge** insgesamt, jede Gruppe endet mit „+ n weitere“. In der Mehr-Liste trägt „Pausieren“ zwei gefüllte Balken 4 × 14 (`Icon.Pause`), „Wieder aktivieren“ das Play-Dreieck von „Spielen“ – nicht die Kreispfeile von „Jetzt scannen“.
- **Start**: Das nächste Brett ragt nicht mehr herein. Zwischen der eigenen Blende und der Blende der Fußleiste blieb nur ein Streifen 2-px-Farbstriche, der wie ein Renderfehler las; die Blende endet jetzt 8 px über den Schachteln des nächsten Bretts. „Mehr unten“ sagt der Ortssatz („3 von 11“) und das Etikett am Brett.
- **Testbild**: Schachteln in Wand hell + 14 % Papier mit 6-px-Farbband oben und Rückenband, Raster in Text 3 zu 20 %, das Regal 24 px höher.
- **Nicht umgesetzt**: (a) Grid-Bildunterschriften am „ – “ teilen und den Rest in die Etikettzeile setzen – die Etikettzeile trägt schon Fassung/Sprache, und „… Kapitel 1“ ist die M3-Entscheidung „der unterscheidende Schluss bleibt“ (10.2); die Zeile 1 wird dabei nie mitten im Titel gekürzt. (b) „Cover wählen...“ mit drei Punkten: Der Text ist bereits U+2026; Archivo zeichnet die Ellipse eng. (c) Systeminfo als Falte im Banner (siehe oben).
---

### 7.8 Stand R2 „offen“ (die letzten offenen Punkte, Entscheidungen nach der Bildprüfung)

- **Raumlicht ohne Stufen** (2.5): Scrims und Schein in float, vormultipliziert, mit ±½ LSB Interleaved-Gradient-Noise in ein Bitmap in Geräteauflösung; das weiche Spielbild 256×144 geditht. Bei 25-facher Kontrastverstärkung sind die Konturringe und senkrechten Bänder weg.
- **Konsolen-Front mit 1–2 Spielen**: Die Schachteln werden größer (bis 196, ≈ 1100: 140), soweit das Fach reicht; bleibt neben der letzten Schachtel Platz, hält eine **Buchstütze** die kurze Reihe (`Bookend.Draw`, 70 % der Schachtelhöhe). Das Jahr unter dem Namen („Nintendo, **1996**“) steht in der Zahl-Schrift (`Sentence.WithNumbers` für gezeichneten Text).
- **Schwarzes Gehäuse** (Virtual Boy, PS2, Mega Drive …): Leere Hüllen (`SystemLook.CaseHi/CaseLo` #27272C/#1C1C20) und das innere Fach der Front (`SystemLook.Recess` #1D1C20) sind angehoben – auf #0F0F11 waren sie mit 1,1 : 1 schwarze Löcher.
- **Systemseite, Fachende**: Bei einer Reihe weniger Spiele (1–6, weniger als Spalten, ≥ 300 px frei) steht der Satz als **Papiernotiz** (`PaperNote`, −1,2°, Klebeband) mit „Importe öffnen“ darunter **im Fach rechts der letzten Schachtel**, der Fuß des Knopfs 8 px über dem Brett. Rechts/Runter von der letzten Schachtel erreicht den Knopf, Links/Hoch führt zurück. Bei ≈ 1100 liegt nichts mehr in der Blende. Der Fußbereich des Grids schneidet nicht mehr ab (`ClipToBounds=false`).
- **Detail**: Die Fernseher tragen „Titelbild“/„Im Spiel“ als **Papierstreifen am Kinn** (`PixelScreen.Caption`) statt einer Zeile unter dem Brett, die bei 1920 in der Blende lag; Geräte 360 breit auch bei 1920. Von der Bühne in eine Rückseite, die nicht darunter passt, geht die Seite auf den **Brett-Stopp**; Auswahlliste und Dateiinfo klappen auf einem Stopp auf (0 / Aktionen / Brett / frei darunter), nie auf einem Versatz, der den Titel unter dem Kopf schneidet. „Auch im Regal“ bricht auf zwei Zeilen um, Zeilen kürzen an Wortgrenzen.
- **Werkbank, Wertanzeige**: Der Wert der fokussierten Zeile steht auf einer **Fallblatt-Karte** (`FlapPlate`: oberes und unteres Blatt, Spalt über den Ziffern, Stifte in der Lichtfarbe, in die Werkbank eingelassen); ein Schritt klappt ein Blatt um (`FlapLeaf`, 180 ms, nur bei voller Bewegung, über Animations-Frames). Das schwarze 320×40-Fenster mit Lichtkante entfällt.
- **Emulatoren, Seitenspalte**: Unter dem Gerätefach ein **Schaukasten** (`SystemNiche`, 300 hoch): das fokussierte System auf seiner Wand mit Band und Textur, sein Motiv auf einem kleinen Brett, Lippe und Innenschatten; ein Wechsel schiebt das Motiv 14 px hoch und blendet es ein (Glide).
- **Testbild**: Schachteln ein Drittel größer (70 × 95 bei 384), mit Etikett und Kanten, die äußeren laufen aus dem Bild; Raster Text 3 zu 30 %.
- **Lieferschein im Demo**: Der erste vom Nutzer gestartete Demo-Scan findet drei neue Spiele, damit die neuen Rücken unter dem Lieferschein geprüft werden können; der stille Start-Scan nicht (`ScanOptions.Startup`).
- **Überlagerungen als Objekte**: **Systeminfo** ist eine **Karteikarte** aus hellem Papier (rote Kopflinie, Linien alle 32 px, Klebeband, Reiter in der Systemfarbe, Tinte; „Schließen“ als Tinten-Rahmen, fokussiert gefüllt) und fällt beim Öffnen 16 px herab (Glide; reduziert: nur Einblenden). Rechtsklick-Liste, „Mehr“ und Dialoge sind **genietete Werkbank-Platten** (`PanelPlate`: Fasen 6, Maserung, Lichtkanten, Schmutz am Fuß, vier Nieten, eigener Schatten) statt BoxShadow-Karten. **Meldungen** sind ein aufgeklebter **Papierstreifen** in der Fußleiste und gleiten aus ihr hoch (Step). Programmnamen in Sätzen tragen die Stilklasse `Run.name` (auf Papier Tinte).
- **Seitenende**: Die Fußleiste hat eine **Vorderkante** (`FooterLip`); Listen und Regale laufen unter die Kante der Werkbank, die Blende darüber ist nur noch ihr Schatten (40 px).
- **Mehr Bewegung** (alle mit 9.4): Fallblatt, Schaukasten, Karteikarte, Meldungsstreifen; ein Schuber unter der ruhenden Maus wird halb herausgezogen (Pull 0,45).
- **Funktion**: Dolphin-Version aus der Datei (`Dolphin 2609b`, ohne Start, gecacht); Vorschaubild-Cache räumt beim Start entfernte und geänderte Cover auf (Namen mit Pfad- und Stand-Hash).

## 8. Fokus- und Auswahl-Sprache (Controller)

Grundsatz: **Der Fokus hebt das Objekt ins Licht.** Bei Objekten (Schachtel, Wand, Primärbutton) besteht er aus Hebung, Lichtkegel von oben, beleuchtetem Brett und gefastem Ring. Bei kleinen Elementen ist er eine Inversion. Bei Zeilen ist er eine helle Zeile mit Balken und Papier-Chip. Andere Elemente ändern sich nicht. Gedimmt wird nur als Status.

### 8.1 Architektur: `FocusLayer` statt Zeichnen in der Kachel

- `CoverArt` zeichnet **nur** Bild bzw. Platzhalter und behält `ClipToBounds=true`. Neu ist die Eigenschaft `ArtRect` (contain, unten bündig, horizontal zentriert), die Brett und Fokus-Ebene lesen.
- Jedes `CoverGrid`, jedes Brett auf Start und jedes `SystemShelf` hat **eine** Fokus-Ebene *im Inhalt des ScrollViewers*, damit sie mitscrollt. Sie besteht aus zwei Teilen:
  - **`FocusBack`** (Canvas *hinter* den Zeilen): Lichtkegel, beleuchtetes Brettstück und Fokus-Kontaktschatten.
  - **`FocusFront`** (Canvas *über* den Zeilen): der gefaste Ring.
- **Positionierung**: Bei Fokuswechsel wird `ArtRect` über die **Layout-Positionen** (ohne RenderTransforms) in die Ebene umgerechnet; `TranslatePoint` enthielte die Hebung schon und höbe den Ring doppelt (M2-QA). Ring und Kegel werden für das **Zielrechteck nach Hebung** gesetzt (ArtRect × 1,06 von unten Mitte) und erscheinen in Blink (90 ms). Sie laufen der Skalierung nicht hinterher. Wird die fokussierte Kachel recycelt oder verlässt sie den sichtbaren Bereich, wird die Ebene ausgeblendet.
- **Z-Reihenfolge**: Die fokussierte Kachel bekommt `ZIndex=1` in ihrem `CoverRowPresenter`, und dessen Zeilencontainer bekommt `ZIndex=1` im `VirtualizingStackPanel`. So überdeckt die nach oben wachsende Schachtel die Bildunterschrift der Zeile darüber und wird nicht von ihr überdeckt. Horizontal wächst sie um 6 px je Seite, das ist weniger als der Abstand (≥ 20). Es entsteht also keine Überlappung mit den Nachbarn.
- **Geometrie** (gecacht pro Größen-Bucket, auf 2 px gerundet):
  - *Ring* (M3-QA: **stehender Ring**): Innen 3 px Raum-Tief (Spalt), außen 3 px Lichtfarbe, Außenfase `RhChamferRing` 6 px, um Seiten und Oberkante des Zielrechtecks. Unten ist er **offen**: Beide Schenkel enden flach auf der Brettoberkante (`ArtRect.Bottom`), der 2-px-Lichtstreifen des Brettstücks schließt ihn. Vorher lag die untere Kante 3–6 px in der Brettfront, und die Schachtel wirkte eingesunken. `Chamfer.DrawStandingRing`; Start-Bretter und Bühne nutzen denselben Ring.
  - *Lichtkegel* (M2-QA, M3-QA): kurzes, **hartkantiges** Trapez, das an der Oberkante der gehobenen Schachtel endet und nie hinter der Schachtel liegt. Oben 0,6 × Coverbreite bei Coveroberkante − 32, unten 1,1 × Coverbreite auf der Coveroberkante; Lichtfarbe linear von 0 (oben) auf 12 % (unten), ohne seitliches Ausblenden (der weiche 18-%-Kegel las sich als „Glow“). Ein vorab gerendertes Bitmap pro Lichtfarbe, geteilt; auf die Kopffreiheit beschnitten (Grid 24 px).
  - *Brettstück*: siehe 4.2 (2 px Licht über die Ringbreite, Front + 16 % Licht).
  - *Fokus-Kontaktschatten*: 16 px #000 70 % → 0 auf Coverbreite, **unter** der Brettfront.
- **Lichtfarbe**: in gemischtem Kontext Bernstein (bzw. die gewählte Benutzer-Lichtfarbe), im Systemkontext das *Licht* des Systems für Kegel und Brettstück. **Der Ring** (und die 2-px-Lichtkante, die ihn schließt) ist im Systemkontext das Licht **zur Hälfte mit Papier gemischt** (`FocusLayer.RingPaperShare` 0,5; SNES-Lavendel #A397D8 → #CCC2DC). QA 2: Der reine Systemton las sich schwächer als der Bernstein-Ring und konkurrierte mit den Farbbändern der Rücken daneben – die fokussierte Schachtel war im meistgenutzten Raster am schwersten zu finden. Ein Bernstein-Ring auch im System hätte die Lichtfarbe des Fachs verlassen; die Mischung hält die Farbfamilie und hebt den Ring über jedes Band.
- **Kosten**: zwei Elemente pro Grid, unabhängig von der Kachelzahl. Keine Kachel trägt Fokus-Visuals.

### 8.2 Pro Elementtyp

| Element | Fokus-Darstellung | Zeit |
|---|---|---|
| **Schachtel** (Grid, Bretter, Bühne, Detail) | Hebung 1,06 (Ursprung unten Mitte), Kegel, Brettstück, Fokus-Kontaktschatten, gefaster Ring. Bildunterschrift Text 2 → Papier | Ring 90 ms linear; Hebung 140 ms CubicEaseOut |
| **Systemrücken / Front / Wand-Banner** | Gefaster Ring in Licht (3 px Raum-Tief-Spalt + 3 px Licht, Fase 6), Hebung 1,03 von unten Mitte, über den Nachbarn (ZIndex 1). Kein Kegel, die Wand ist selbst die Lichtfläche | wie oben |
| **Primärbutton** (gefaste Platte) | Bernstein bleibt, gefaster Ring 3 px **Papier** mit Raum-Tief-Spalt, Skalierung 1,03 | 90 / 140 ms |
| **Sekundärbutton, Kopfleisten-Icon, Kanal, Ticker-Eintrag, Tastenkappe, Werkzeug** | **Inversion**: Fläche Papier, Text und Icon Tinte (Ticker: Code-Fläche 6×4, R 2; Unterstrich bleibt) | 90 ms |
| **Listen-, Auswahl-, Einstellungs-, Emulator-, Ordnerzeile** | Zeile Regal hell, 8-px-Balken links (Farbe des Systems bzw. Bernstein), 1 px Lichtkante oben, Label bzw. Titel in Papier-Chip (Tinte, 8×4, R 2). Im Service-Menü werden die Wert-Pfeile Papier | 90 ms |
| **Textfeld (Suche)** | Rahmen 2 px Bernstein, Fläche Regal, Cursor Bernstein | 90 ms |
| **Regalspiegel** | nicht fokussierbar | — |

### 8.3 Maus

- **Schachtel**: Hover hebt sie um 3 % (Step 140 ms, unter Reduziert/Aus keine Skalierung) und zeigt den Ring bei 40 % Deckkraft (`FocusLayer.Hover`, eine Ebene pro Raster), kein Kegel, kein Brettstück. Klick setzt den Fokus und löst die Aktion aus.
- **Alles per Maus erreichbar (R2)**, mit echten Zeiger-Tests (`MouseReachabilityTests`, Bewegung Aus):
  - **Zurück**: die Zurück-Taste der Maus (XButton1) wirkt überall wie Zurück (Dialog, Liste, Falte, Verlauf; nicht während ein Spiel in RetroHub läuft). Die Fußleisten-Hinweise sind Mausziele (`RhHintButton`: Glyph leuchtet, 1-px-Linie in Text 3, Druck 0,97; nie fokussierbar), nur „Öffnen/Starten“ nicht. „Zurück“ fällt bei schmalen Fenstern nie heraus. Das Abschnittswort der Ortszeile („Konsolen ›“) führt auf Drill-in-Seiten zurück zum Abschnitt.
  - **Ein Druck verschiebt nichts**: Fokus durch die Maus gleitet keine Reihe an den Anker, scrollt keine Liste und aktiviert kein anderes Brett vor dem Loslassen (sonst trifft das Loslassen nichts mehr). Auf einen geschlossenen Systemrücken dreht der erste Klick die Front nach vorn, der Klick auf die offene Front öffnet das System (Start-Brett „Konsolen“ und Fächerwand gleich).
  - **Reihen**: Shift+Rad bzw. das horizontale Rad schiebt ein Brett seitlich, auf „Noch leer“ auch das normale Rad. Am rechten Brettende stehen zwei **Winkel** (`ShelfEdge`, gefaste Metallplättchen 30 × 20 mit zwei Schrauben, eingeschnittenem Winkel, Lichtkante oben), nur solange der Zeiger über einem überlaufenden Brett steht; ein Klick schiebt eine Ansicht weiter, ein Plättchen ohne Ziel bleibt matt. Auf der Fächerwand und der Schiene schaltet das Rad das nächste System nach vorn; die Schiene zeigt beim Hover den Strich groß mit Code in Text 2 und den Namen als Tooltip und lässt sich ziehen. Das Rad über Ticker und Regalspiegel scrollt.
  - **Rechtsklick** auf Schachtel oder Systemrücken: eine Liste im Stil von „Mehr“ (Regal, 1-px-Kante, Lichtkante, 56-px-Zeilen) am Zeiger: Spielen, Details, Favorit, Ordner öffnen bzw. Öffnen, In der Bibliothek zeigen. Esc, Zurück oder ein Klick daneben schließen sie. Die Sortierung nutzt dieselbe Liste (das letzte Fluent-Menü ist weg).
  - **Listenansicht**: der erste Klick wählt die Zeile (die Vorschau füllt sich), der zweite öffnet.
  - **Falten und Listen** (Auswahl, Dateiinfo, Weiterlesen, Importe „Mehr“) schließen auf ihre eigene Zeile und auf einen Klick daneben, der Klick erreicht trotzdem sein Ziel. Der Schleier um einen Dialog bricht ab. Während das Bild ausgeht (Rückkehr), schluckt die Übergangsebene Klicks.
  - Auf **Start** führt ein Klick in die dunkle Fläche unter dem aktiven Brett das nächste Brett hoch (die verdeckten Schachteln darunter sind nie klickbar). Eine ruhende Maus auf einer Schachtel zeigt sie nach dem Dwell auf der Bühne, solange kein sichtbarer Tastatur-/Controller-Fokus in den Brettern steht.
- **Buttons und Zeilen**: Fläche Regal hell.
- Ein bloßes Überfahren wechselt nie das Fernsehlicht oder die Bühne. Nur der Fokus tut das, und auf Start (R2) eine Maus, die den Dwell lang auf einer Schachtel ruht.

### 8.4 Verhalten

- **Auto-Repeat-Erkennung (`NavRepeatTracker`, neu in `App/Input`)**:
  - *Gamepad*: `InputTranslator` weiß, ob eine Richtungsaktion aus dem Repeat-Takt stammt. `GamepadService.ActionTriggered` bekommt dafür ein `isRepeat`-Flag, das `GamepadInputSource` durchreicht (`IInputSource.ActionRaised(NavAction, bool isRepeat)`). Das Loslassen meldet `InputTranslator` als `DirectionReleased`.
  - *Tastatur*: Ein zweites `KeyDown` derselben Pfeiltaste ohne `KeyUp` dazwischen ist ein Repeat. `KeyUp` beendet ihn.
  - *Zustand*: Der Tracker setzt `.nav-repeat` am Root beim **ersten Repeat-Ereignis**. Er entfernt die Klasse beim Loslassen **oder** 150 ms nach dem letzten Repeat-Ereignis (`RepeatRelease`, Sicherheitsnetz für verlorene KeyUps), je nachdem, was zuerst eintritt. Ein einzelner Druck setzt die Klasse nie.
  - *Wirkung*: keine Hebungs-Transition (Skalierung sofort), kein Kegel, der Ring springt mit. Reihen gleiten 90 ms linear statt 220 ms. Decodes warten `DecodeDwell`. Fernsehlicht und Bühne pausieren.
  - *Nach dem Ende*: `RoomBackdrop` und Bühne starten **nur ihren Dwell-Timer neu**, und nur wenn das fokussierte Spiel vom angezeigten abweicht. Es gibt höchstens eine Überblendung pro Loslassen und kein Flackern.
- **Grenz-Feedback**: Gibt es über die erste oder letzte Kachel bzw. Zeile hinaus kein Ziel, läuft am fokussierten Element `TranslateX` 0 → −6 → +4 → 0 in 180 ms.
- **Kein Dauer-Pulsieren**, kein „Atmen“.
- **Wiederherstellen**: Jede Seite merkt sich den Fokus (PageHost), Bretter den Index pro Brett.
- **Fokus nie verdeckt**: `BringRowIntoView`-Marge unten **40 px** (die Fußleiste verdeckt nie den Fokus). **Einrasten (M3-QA)**: Vertikal scrollt das Grid nur auf Zeilengrenzen. Die oberste sichtbare Zeile beginnt immer genau *Zeilenabstand* (24 px) unter der Viewport-Oberkante; dort endet die Zeile darüber, und Hebung (≤ 12) plus Ring (6) passen hinein. Nach oben wird die Fokuszeile die oberste Zeile, nach unten gilt der kleinste eingerastete Versatz, bei dem die Fokuszeile samt Bildunterschrift sichtbar ist. Nur der untere Rand darf eine angeschnittene Zeile zeigen. Zeile 0 behält den Kopf (Wand-Banner). Freies Scrollen (Mausrad, Spiegel) blendet an der Viewport-Oberkante über eine fixe 12-px-Überlagerung im Seitengrund (100 % → 0, zwischen Zeilen und Fokusring, keine OpacityMask) aus, statt hart zu schneiden.

---

## 9. Motion

### 9.1 Tokens (`Theming/Motion.cs` plus Ressourcen)

| Token | Dauer | Easing | Verwendung |
|---|---|---|---|
| `Blink` | 90 ms | Linear | Ring, Inversion, Farbwechsel |
| `Step` | 140 ms | CubicEaseOut | Hebung, Button-Skalierung, Inhalt der Front |
| `Glide` | 220 ms | CubicEaseOut | Bretter, Konsolen, Suchfeld, Aufklapper |
| `GlideRepeat` | 90 ms | Linear | Gleiten unter `.nav-repeat` |
| `Scene` | 280 ms | CubicEaseOut | Seiteneintritt, Systemwechsel (Wand), Overlays |
| `Light` | 420 ms | SineEaseInOut | Fernsehlicht, Brettlicht (`ColorTransition`) |
| `Dwell` | 220 ms | — | Ruhe vor Fernsehlicht- und Bühnenwechsel |
| `DecodeDwell` | 50 ms | — | Cover-Decode bei Auto-Repeat |
| `RepeatRelease` | 150 ms | — | Sicherheitsnetz Ende Auto-Repeat |
| `Osd` | 1200 ms Standzeit | hart ein/aus | Kanalanzeige |
| `PowerOn` | 520 ms gesamt | siehe 9.3 | Spielstart |

### 9.2 Was sich bewegt

- **Seiteneintritt**: Inhalt Opacity 0 → 1 und TranslateY 16 → 0 (Scene). Rückwärts TranslateY −12 → 0. Kopf- und Fußleiste bewegen sich nie. Es gibt keine Staffelung pro Kachel.
- **Bühnenwechsel (Start)**: Text 120 ms aus, dann 180 ms ein mit TranslateX 8 → 0. Schachtel und Bildschirm überblenden mit zwei Ebenen (180 ms). Licht 420 ms.
- **Bretter**: TranslateX der Reihe (Glide bzw. GlideRepeat). Der Ring wandert nicht, er erscheint an der neuen Schachtel.
- **Konsolen**: Breite des Clip-Containers 128 ↔ 520 (Glide), Inhalt der Front +120 ms (Step), Wand 280 ms.
- **Aufkleber „Neu“**: 250 ms auspacken.
- **Schuber „Noch leer“** (M7): Fokus zieht ihn heraus – gerade richten und 8 px heben (Step 140 ms, CubicEaseOut); Reduziert/Aus: kein Hub, Zustand sofort.
- **Senderskala** (M7): die Nadel gleitet zum neuen System (Glide 220 ms); bei gehaltener Richtung, Reduziert und Aus springt sie.
- **Tasten** (M7): Spielen-Taste und Pfeiltasten geben beim Drücken nach (Tiefe 3 → 1 px).
- **Kanalanzeige**: hart, ohne Bewegung.

### 9.3 Starttransition „Einschalten“ (der eine inszenierte Moment)

1. **0 ms**: `FlightLayer` (ungeskaliert, über allem außer dem eingebetteten Fenster) bekommt eine **Kopie** der fokussierten Schachtel: dasselbe `Bitmap` bzw. derselbe Platzhalter, Startrechteck per `TranslatePoint(ArtRect)` zum TopLevel (enthält UiScale). Die Original-Kachel wird auf Opacity 0 gesetzt (nicht entfernt, damit Virtualisierung und Fokus unverändert bleiben).
2. **0–220 ms**: Die Kopie fliegt zur Fenstermitte und skaliert auf 1,4× (`TransformOperationsTransition`, CubicEaseIn). Der Raum dimmt auf Raum-Tief 94 %.
3. **220–320 ms**: Die Kopie blendet aus. Eine **2-px-Linie in der Licht-Farbe des Systems** (Phosphor dieser Konsole) wächst aus der Mitte auf volle Breite (ScaleX 0 → 1). Gleichzeitig zeigt die Kanalanzeige 300 ms lang den **Systemcode** (Rücken 40, Licht) oben rechts.
4. **320 ms**: **ein Frame Überbelichtung**: Papier bei 25 % über das ganze Fenster, 16 ms (nicht bei `.motion-reduced` und `.motion-off`).
5. **320–520 ms**: Die Linie öffnet sich vertikal zur Startfläche (ScaleY, CubicEaseOut). Darin erscheint das Start-Overlay: Titelbild bzw. Schachtel 240×336, Titel in Plakat, „mGBA startet …“ in `read-l` und das Segment-Lauflicht.
6. Die Mindestdauer des Overlays beträgt 900 ms. Die alte GameScreen-Startdarstellung wird vollständig ersetzt (behebt das Doppel-Overlay).
7. **Rückkehr („Ausschalten“)**: Das Overlay kollabiert vertikal auf eine Linie in Licht (160 ms), die Linie auf einen Punkt (80 ms). Dann blendet die Seite ein (Scene), und die Original-Kachel bekommt Opacity 1.
8. Die Transition liegt **nie** über dem eingebetteten Emulatorfenster. Sie endet, bevor das HWND sichtbar wird.

**Stand M6 (umgesetzt, `Controls/FlightLayer.cs`)**: Die Zeitleiste ist eine Funktion der Zeit (`Apply(ms)`), angetrieben von einem 15-ms-Takt nur während der 520 ms; Vorschau und Tests frieren einzelne Bilder ein (`Freeze`). Die Kopie ist ein `RenderTargetBitmap` der Schachtel (genau so, wie sie gerade aussieht), die **ganze Kachel** samt Rücken geht auf Opacity 0 und kommt beim Abbruch bzw. nach dem Ausschalten zurück. Die Startfläche ist das Start-Panel des Launch-Overlays (UiScale-skaliert): Schachtel 240×336 auf einer Regalkante, Titel `poster-s`, „mGBA startet …“ in `read-l`, `SegmentBar` als Lauflicht (die Fortschrittspille entfällt). Die beiden Kanten der sich öffnenden Fläche tragen das Licht und verblassen bis 520 ms. Das eingebettete Spielbild (maskiert, noch ladend) ist während „Starting“ unsichtbar (Opacity 0), damit es nicht durch Flug und Linie scheint. Die Kanalanzeige zeigt den Systemcode bei 220 ms für 300 ms in Rücken 40. Ausschalten nur nach einer gezeigten Sitzung (nicht nach einem fehlgeschlagenen Start); nach einem Vollbild-Handoff ohne Animation. Die Statushinweise im Lauf-Overlay sind Icon + Satz statt Pillen, „Läuft gerade“ steht in Text 3 (Bernstein-Liste 2.2).

**Stand M6-QA**: (1) **Die Kopie wird zur Schachtel**: Das Flugziel ist das Schachtelfach der Startfläche (240 × 336 auf der Regalkante, mittig, UiScale-skaliert), die Kopie wird dorthin *contain* skaliert (statt fest 1,4× in die Fenstermitte), und die Startfläche zeigt **dasselbe Bitmap** statt eines eigenen Platzhalters (`FlightLayer.LandingSlot/LandingImage/LandingArt`). Vorher verschwand eine Schachtel und eine andere erschien. Ohne Kopie (reduzierte Bewegung, keine Quelle) zeigt die Startfläche ihren Platzhalter mit `KeepTitleClear`: wo Titel und Motiv sich träfen, tritt das Motiv auf 7 % hinter den Titel zurück. (2) Der Segmentbalken der Startfläche leuchtet im **Licht des Systems**, die dunklen Segmente in dessen Wand hell; Bernstein bleibt der Fortschritt der App. (3) **Ausschalten**: Ein Raum-Tief-Hintergrund deckt das ganze Fenster, bis der Punkt verschwunden ist (160 + 80 ms); erst dann blendet die Seite in 280 ms ein (`OffDuration` 520 ms). Vorher lag die Linie über der schon fertigen Seite und las sich als Unterstreichung. Der `FlightLayer` liegt über den Toasts. (4) **QA 2: ersetzt durch die Sitzungsanzeige im OSD (7.7).** „Willkommen zurück“ erschien erst nach dem Einblenden (`LaunchCoordinator.ReturnToastDelay`, nach einem Vollbild-Handoff sofort); unter einer Minute heißt es „Diese Sitzung: unter 1 Min.“, nie „–“.

### 9.4 Reduzierte Bewegung / Aus

- **`.motion-reduced`**: keine Hebung, keine Translationen, kein Gleiten (Reihen springen). Überblendungen 100 ms, Fernsehlicht 200 ms. Start = Überblendung 150 ms ohne Flug und ohne Überbelichtung. Die Kanalanzeige bleibt (sie bewegt sich nicht).
- **`.motion-off`**: alle Transitions null, das Fernsehlicht schaltet hart. Der Fokus bleibt voll sichtbar (Ring, Kegel, Inversion sind statisch).
- Muster wie bisher: Zustandswerte in `^:state /template/ PART_x`-Styles, Transitions per Klasse am Root nullbar.

### 9.5 Performance-Budget

- **60 fps** im Grid mit 8×3 sichtbaren Kacheln bei gehaltener Richtungstaste, auf einer Mittelklasse-iGPU bei 2560×1440.
- **Verboten auf Kacheln**: BoxShadow, BlurEffect, OpacityMask, Brushes pro Kachel, animierte Verläufe, Fokus-Visuals in der Kachel.
- **BlurEffect** wird **nirgends** verwendet. Weichheit entsteht durch vorab geblurte 64×36-Bitmaps (2.5).
- **Animiert werden nur** RenderTransform, Opacity, Volltonfarben (Wand, Brettlicht) und die Breite eines Clip-Containers (Konsolen).
- **Bibliothek**: kein Fernsehlicht. Der einzige Lichtwechsel ist eine Farbe (`RhBoardLightBrush`).
- **Fernsehlicht** (nur Start/Detail): 2 Bildebenen 64×36, ein geflachtes Scrim-Bitmap, ein Schein. Während `.nav-repeat` findet keine Arbeit statt.
- **Platzhalter**: `FormattedText` (Titelumbruch, gedrehter Code) wird **pro Kachel bei Änderung** von Titel, Größe oder System erzeugt und gecacht, nie in `Render`.
- **Messung**: FPS-Overlay bzw. `RendererDiagnostics` im QA-Lauf mit 1000 Demo-Spielen und mit einer Bibliothek ohne Cover.
- **Stand M3 (gemessen headless, Skia auf der CPU in der Dev-VM, `DesignPreviewTests.Library_1000_perf`, Bericht `perf-library.txt`)**: Bei 1000 Spielen ohne Cover sind 64 Kacheln realisiert. In 118 Bewegungen wurden nur 16 Kacheln gebaut, weil der Zeilen-Presenter selbst der Container ist und an Ort und Stelle recycelt wird. Die Fokusebene ist nur so groß wie Kegel, Ring und Brettstück. Die Brettebene zeichnet nur neu, wenn sich die realisierten Zeilen ändern. Der Regalspiegel wird einmal pro Liste gerastert (unter 5 ms) und bei Bewegungen nie. Messwerte bei 1080p: Bewegung nach unten im eingeschwungenen Zustand Layout Median ≈ 1 ms (erster Besuch einer Zeile p95 ≈ 15–20 ms); Frame nach einer Bewegung ≈ 9–20 ms CPU-Raster; Standbild ≈ 2 ms. **Grenzen der Messung**: Headless wartet `CaptureRenderedFrame` laufende Transitions ab (Hebung 140 ms). Mit Bewegung „voll“ misst es deshalb ≈ 150 ms statt der Arbeit. Gemessen wird daher mit Bewegung „aus“. Die ersten ≈ 60 Bewegungen nach dem Öffnen kosten Median ≈ 25 ms Layout (Aufwärmen). Eine GPU-fps-Messung auf echter Hardware steht aus.

---

## 10. Graceful Degradation

### 10.1 Bild-Fallback-Ketten (pro Fläche)

| Fläche | Kette |
|---|---|
| Schachtel (Grid, Bretter, Bühne, Detail) | Cover → Titelbild (4:3 auf Wand mit 6 px Raum-Tief-Rahmen) → **Rücken-Platzhalter** |
| Bildschirm auf Start | Snap → Titelbild → **„Kein Signal“** (Systemwand im Bildschirm) → aus (leere Bibliothek) |
| Bildschirme auf der Rückseite | Titelbild und Snap. Was fehlt, entfällt |
| Fernsehlicht | Snap → Titelbild → Cover → Systemwand |
| Konsolen / System | immer generiert (Wand + Motiv) |

**Cover-Quellen (R2)**: lokal zuerst (artwork/covers, neben der Datei, ES-DE/Batocera). Online nur mit „Cover aus dem Internet laden“: libretro-thumbnails, für **Nintendo Switch GameTDB** (libretro hat dafür keinen Satz): Spieleliste `switchtdb.zip` höchstens monatlich (ETag), Abgleich über alle Titel jeder Sprache (auch deutsche wie „Zeit der Verheerung“), Vorderseite `coverM` (352 × 570, Format ≈ echte Hülle) in der Reihenfolge **DE → EN → US**, gespeichert als `artwork/covers/switch/<Dateiname>.jpg`. Kein `coverHQ` im Raster. Lose Switch-Updates/DLC (Titel-ID nicht auf `000`, „Update v…“, „DLC 1“) werden nie eigene Spiele. Die Cover sind die Kunst des Nutzers, nie UI-Dekoration.

### 10.2 Rücken-Platzhalter (statt Initialen)

Gezeichnet in `CoverArt.DrawPlaceholder`, Seitenverhältnis 0,72 (B/H) hochkant, unten bündig:
```
┌──┬────────────────┐
│G │░░░░░░░░░░░░░░░░│  Grund: zweifarbig wie die Wand (Wand hell 38 % / Wand 62 %; unter 200 px Breite einfarbig), Textur 8 %
│B │░░░░░░░░░░░░░░░░│  Rückenband: 16 % der Breite, volle Farbe, Code −90° in Rücken (0,09 × Breite), Text auf Farbe
│A │░░░░░░░░░░░░░░░░│  Motiv-Silhouette: EINE Geometrie (Teile vereinigt, Aussparungen abgezogen), Farbe 18 %,
│  │░░░░░░░░░░░░░░░░│    45 % der Höhe, rechts unten mit 14 px Abstand, immer ganz in den unteren 62 %
│  │░░░░░░░░░▟██▙░░░│  Titel: Plakat-Rolle, clamp(0,105 × min(B, 0,72 × H), 18, 26), Bühnen-Schachteln (H ≥ 300) bis 40;
│  │░░░░░░░░░░░░░░░░│    eine Größe für alle Schachteln einer Reihe, Zeilenhöhe 1,1; Untertitel Atkinson ≥ 15 px, Text 2;
│  │░░░░░░░░░░░░░░░░│    Papier, linksbündig,
│  │Castlevania:░█░░│    unten verankert, 14 px Innenrand, max. 4 Zeilen
│  │Aria of░░░░░░░░░│  Oben 1 px Lichtkante; R 2; Kontaktschatten wie echte Cover (vom Brett)
│  │Sorrow░░░░░░░░░░│
└──┴────────────────┘
```
- **Variation pro Spiel ohne eigene Brushes**: In `Render` werden `PushClip(ArtRect)` und `PushTransform(Matrix.CreateTranslation(hash(Titel) % Kachelgröße))` gesetzt. Dann wird mit der **geteilten** Textur-Brush gefüllt (der Brush-Ursprung folgt der Transformation). Die Textur selbst kommt hier ohne `TextureFill` aus, weil sie bei 8 % Deckkraft in einem Bild liegt. Moiré ist dort unkritisch, und es wird `MediumQuality` verwendet.
- **Text-Cache**: `FormattedText` für Titel und Code entsteht in `OnPropertyChanged` (Titel, Bounds-Bucket, System) und wird im Feld gehalten.
- Umbruch nur an Leerzeichen und nach Bindestrichen, nie im Wort; die Größe sinkt nur, wenn ein einzelnes Wort breiter als die Zeile ist (× 0,88, ≥ 13 px). Mehr als 4 Zeilen: die vierte Zeile endet mit „…“ (M2-QA). `ShowBadge` und die Badge-Pille entfallen; auf Start trägt keine Schachtel eine Favoriten-Marke.
- Der Platzhalter wird nicht abgedunkelt, denn Dimmen ist dem Status vorbehalten.
- **Bedruckter Rücken (M3)**: Ab 28 px Bandbreite trägt das Band wie ein echter Rücken oben den Code, darunter den Haupttitel (vor „ – “/„: “) von unten nach oben und am Fuß das Jahr (Archivo Condensed, Text auf Farbe, ≥ 15 px). Dazu kommt eine 1-px-Falzlinie zur Front. Der Titel steht **ganz oder gar nicht**, es gibt nie „Banjo-K…“. **Das Jahr hat Vorrang** (M3-QA): Es steht immer (wenn bekannt), der Titel nur, wenn er daneben ganz passt. So liest sich eine Reihe als ein Druck (Code + Jahr überall, Titel wo Platz ist) statt „nur Titel“ neben „nur Jahr“.
- **Untertitel mit Schluss** (M3-QA): Passt der Untertitel nicht in seine Zeilen, entfällt die gemeinsame Mitte und nur der letzte Teil wird gedruckt („Ancient Stone Tablets – Kapitel 1“ → „Kapitel 1“). Der unterscheidende Schluss bekommt nie eine Ellipse.
- **Schmale Hüllen (M1-Rest, M3)**: Passt ein Wort in Archivo Expanded nicht in die Zeile (Wii/Switch), wird der Titel in derselben Größe in **Archivo Condensed** gesetzt, statt die Plakatschrift auf 13 px schrumpfen zu lassen. Die Untergrenze ist dann 15 px.

- **Stand M7 „gezeichnet“**: Der Platzhalter ist eine **leere Hülle mit handbeklebtem Etikett**: Kunststoffglanz (diagonal, 9 % → 0), Scharnier-Rille neben dem Rücken (ab 120 px Breite), das Motiv **eingeprägt** (dunkle Kopie +1/+1, helle −1/−1, Fläche im Wandton mit 16 % Systemfarbe), Kanten, 0–2 Kratzer. Titel und Untertitel stehen in **Tinte auf einem Papieretikett** (#E9DFCC, geschnittener Rand, ± 1,2° geneigt, Innenrand 5–10 px = `CoverArt.LabelPad`, um den die Textbreite schrumpft) mit 1–2 Klebestreifen an den oberen Ecken. Alles geseedet aus dem Titel. **Volle Details nur auf Bühnenschachteln** (ab 240 px Breite, `CoverArt.StageDetailWidth`: Glanz, Relief-Schatten, Etikett mit eigenem Rand und Fasern, Doppeldruck); Grid- und Regalschachteln teilen sich 16 Etikettränder auf dem Einheitsquadrat und zeichnen nur Etikett, Schatten, Klebestreifen, Kanten und Motiv – die Kacheln werden bei jedem Fokusschritt neu gezeichnet (Messung 1000 Spiele: Eingabe + Layout Median 24,5 ms gegen 21,6–22,7 ms vorher). Das ersetzt keine Cover-Suche (Switch: GameTDB, 10.1).
- **Leerer Platz** (`EmptySlot`): statt Strichlinie eine **Staubspur** – die Stelle, an der eine Schachtel stand: Wand minimal sauberer, Staub ringsum (am Fuß am dichtesten), weicher heller Rand. „Daneben im Regal“ zeigt so den Platz der herausgezogenen Schachtel. Vertiefte Fächer (Konsolen leer) mit Innenschatten; die „Gespielt“-Linie ist eine Rille.

### 10.3 Fehlende Metadaten

| Fehlt | Verhalten |
|---|---|
| Beschreibung | Die Rückseite zeigt die Bildschirme, sonst entfällt die Spalte. Kein Ersatzsatz |
| Genre / Entwickler / Spieler / Region | Teil des Meta-Satzes entfällt. Ist nur das System bekannt: „Game Boy Advance.“ |
| Jahr | entfällt im Satz, in der Etikett-Zeile und am Schachtelrücken |
| Spielzeit (nie gespielt) | „Noch nie gespielt. Seit {Datum} in der Sammlung.“ Keine Ausleihkarte |
| Cover bei allen Spielen eines Systems | Grid aus Rücken-Platzhaltern, wirkt wie ein Regal voller Spielrücken |
| Snap/Titel | Bildschirm zeigt „Kein Signal“, Fernsehlicht aus dem Cover |
| Emulator | Spielen wird zu „Emulator einrichten“, Kachel gedimmt plus `NotReady` |
| Datei fehlt | Kachel gedimmt plus `Missing`; Detail: Statuszeile Fehler plus „Datei suchen“ |
| Systemdaten (benutzerdefiniert) | `SystemPalette.From(accent)`, Gehäuse anthrazit, Motiv Cart8, Textur bayer-8 |
| Keine Spiele | Linienzeichnung plus ein Satz plus eine Aktion |

Texte für Leerzustände benennen, was fehlt, und die Lösung, z. B. „Kein Emulator für Game Boy Advance eingerichtet. Wähle einen in Emulatoren.“

---

## 11. Umsetzung in Avalonia

### 11.1 Dateien

| Datei | Änderung |
|---|---|
| `Assets/Fonts/ArchivoExpanded/`, `ArchivoSemiExpanded/`, `ArchivoSemiCondensed/` (Medium, SemiBold), `ArchivoCondensed/`, `Atkinson/` | **bereits eingecheckt** (v1.1), je mit `OFL.txt`. Poppins wird in M6 entfernt |
| `Themes/Tokens.axaml` | neue Werte für alle Keys, neue Keys `RhRoomDeepColor/Brush`, `RhLightEdgeBrush`, `RhBoardLightBrush`, `RhRadiusNone/Control`, `RhChamferRing/Plate`, Font-Rollen `RhFontPoster/Number/Ui/Spine/Read`. Glas- und Glow-Keys zeigen auf opake bzw. leere Werte |
| `Themes/Typography.axaml` (neu) | Textklassen aus 3.2 (`TextBlock.poster-xl` …) inkl. `FontFeatures +tnum` für `number`/`osd` |
| `Themes/Textures.axaml` (neu) | eine `ImageBrush` pro Textur |
| `Themes/Motifs.axaml` (neu) | `StreamGeometry` je Motiv-Teil |
| `Themes/Icons.axaml` | „Gefaste Linie“ neu; Controller-Glyphen (5.3) |
| `Themes/Controls.axaml` | `RhPrimaryButton` (gefaste Platte), `RhGhostButton` (Inversion), `RhChannel`, `RhTickerItem`, `RhChoiceRow`, `RhValueRow` (Service-Menü), `RhShelfLabel`; GameTile-Template ohne `PART_Glow`/`PART_Ring` |
| `Themes/Styles.axaml` | `.nav-repeat`, `.motion-*`, Typografie einbinden |
| `Theming/ThemeManager.cs` | Lichtfarben-Presets, Akzent-Gradients werden Volltöne, `RhBoardLightBrush` mit `ColorTransition` |
| `Theming/SystemPalette.cs` (neu) | Gehäuse, Systemtabelle, Formel, gecachte Brushes |
| `Theming/MotifCatalog.cs` (neu) | System → Motiv, Textur, BoxAspect-Startwert |
| `Theming/Motion.cs` | Tokens 9.1 |
| `Theming/MetaSentence.cs` (neu) | baut **Inlines** (Runs, Zahlen als Archivo-Run), lokalisiert |
| `Input/NavRepeatTracker.cs` (neu); `RetroHub.Input/InputTranslator`, `GamepadService`, `IInputSource` | Repeat-Flag und Loslassen (8.4) |
| `Controls/CoverArt.cs` | contain + unten bündig, `ArtRect`, Rücken-Platzhalter mit Text-Cache, keine Fokus-Visuals |
| `Controls/CoverGrid.cs` | `CoverRowPresenter` zeichnet Regalkante und Kontaktschatten; `FocusLayer`; ZIndex; Marge 40; Slot-Aspekt pro Instanz |
| `Controls/FocusLayer.cs` (neu) | `FocusBack` / `FocusFront` (8.1) |
| `Controls/ShelfBoard.cs`, `ShelfLabel` (neu) | Brett und Etikett |
| `Controls/Shelf.cs` | Anker horizontal/vertikal, „Alle“-Rücken, FocusLayer |
| `Controls/SystemShelf.cs` (neu) | umgesetzt als `Shelf` + `SystemSpine` (Start-Fächer, Konsolen; M5: `SpineHeight`, `ShowMotif`, `Opens`, `Shelf.BoardLook`) |
| `Controls/SystemWall.cs` (neu) | zweifarbige Wand, Band, `TextureFill`, Motiv |
| `Controls/BoxSpine.cs` (neu) | Schachtelrücken im Detail |
| `Controls/PixelScreen.cs` (neu) | Bildschirm-Objekt mit Gerätepixel-Skalierung und Scanlines |
| `Controls/TextureFill.cs` (neu) | Textur in Gerätepixeln (4.5) |
| `Controls/ShelfMirror.cs` (neu) | Regalspiegel |
| `Controls/RoomBackdrop.cs` (neu) | Fernsehlicht (2.5): Off-Thread-Blur, LRU, geflachte Scrims, Schein, Korn |
| `Controls/ChamferPlate.cs` (neu) | gefaste Platte (Buttons, Tastenkappen) |
| `Controls/OsdLayer.cs`, `FlightLayer` (neu) | Kanalanzeige, Starttransition |
| `Views/MainWindow.axaml(.cs)` | Sidebar → Kopfleiste mit Kanälen; Baum aus 7.0; Fußleiste; NavRepeatTracker |
| `Views/Pages/*.axaml` | nach Abschnitt 7 |
| `tools/TextureGen/` (neu) | SkiaSharp-Generator |
| Core | `ArtworkKind.Snap`/`Title`, `LibretroThumbnails` holt `Named_Snaps`/`Named_Titles` (M2); **Cover-Pixelmaße** (`cover_w`, `cover_h`) beim Download/Import aus dem PNG/JPEG-Header speichern (M3) |

### 11.2 Asset-Pipeline

1. **Motive und Icons**: als SVG-Pfad-Strings entworfen (Raster 120 bzw. 24), als Pfaddaten in XAML übernommen. Keine SVG-Laufzeit.
2. **Texturen**: `tools/TextureGen` erzeugt die PNGs deterministisch. Sie werden committet.
3. **Schriften**: erledigt (3.1). Für neue Schnitte gilt dasselbe Verfahren: offizielles Repo, eigener Ordner pro Breite, Name-Tabelle prüfen, `FontRoleTests` erweitern.
4. **Palette**: `SystemPaletteTests` prüft die Regeln aus 2.4 (Kontraste und ΔE2000). Referenzskript: `scratchpad/design/tools/palette2.py`.

### 11.3 Meilensteine (Owner-Priorität: Start, Bibliothek, Detail)

| # | Inhalt | Fertig, wenn |
|---|---|---|
| **M1 Fundament** | Tokens, Font-Rollen + `FontRoleTests`, Icons, Controller-Glyphen, Kopfleiste mit Kanälen + OSD, Fußleiste, UiScale mit Baum aus 7.0, `TextureFill`, `ChamferPlate`, `ShelfBoard`/`ShelfLabel`, `CoverArt` (contain, `ArtRect`, Rücken-Platzhalter), `FocusLayer`, `NavRepeatTracker`, Motion-Tokens, `SystemPalette` + Tests | Kein `#7B61FF`/`#4C8DFF`, keine Radien > 6, keine Marken-Glyphen; alle Seiten mit Kopfleiste bedienbar; Fokus aus 3 m erkennbar |
| **M2 Start** | `RoomBackdrop`, `PixelScreen`, Bühne (Bildschirm + Schachtel + Text, gemessener Titel), Bretter mit Ankern und Etiketten, `SystemShelf` (Rücken + Front, Motive vorläufig Cart8), Leerzustand. Core: Snaps/Titel | Screenshots 1920/1600/1100/2560 mit Demo- *und* echtem Katalog; „Kein Signal“ geprüft |
| **M3 Bibliothek** | Ticker, `ShelfMirror`, Grid mit Regalkante und FocusLayer, Banner, Systemgrund, Aufkleber, Listen-Ansicht. Core: Cover-Maße | 1000-Spiele-Demo bei 60 fps; Bibliothek ohne Cover lesbar |
| **M4 Detail** | Schachtel mit `BoxSpine`, Meta-Satz mit Zahl-Runs, Aktionen, Lösungs-Button, Rückseite (Text, Bildschirme, Startzeilen), Ausleihkarte, „Daneben im Regal“, Overlays (Stand: 7.3.1) | Detail ohne Beschreibung, Genre und Bild sieht vollständig aus |
| **M5 Konsolen & System** | Motive final + Außenseiter-Test, Texturen komplett, Konsolen, Systemseite | Jedes System ist ohne Text unterscheidbar; kein Motiv wird als Produkt benannt. **Stand:** umgesetzt (6.2–6.5, 7.4.1–7.4.2, 7.5.1–7.5.2); Außenseiter-Test bisher nur als Selbstprüfung an den textfreien Bögen |
| **M6 Werkbank & Start** | Service-Menü, Emulatoren nach Systemen, Importe mit Lieferschein, Starttransition mit `FlightLayer`, Aufräumen (Poppins, StatTile, Glas-Keys) | Alle Seiten im QA grün. **Stand:** umgesetzt (7.6.1, 9.3 Stand M6, 7.3.1 Auswahlliste); die Starttransition ist nur als eingefrorene Einzelbilder headless geprüft, nicht als Bildschirmaufnahme in Echtzeit |

### 11.4 Fachformat (Slot-Aspekt) ohne Neuberechnung beim Browsen

- **Quelle** (umgesetzt in M3: Schema 2, `games.cover_w`/`cover_h`, `ImageHeader` für PNG/JPEG/WebP/GIF/BMP, Nachtrag älterer Cover beim Start und bei jedem Cover-Lauf; die Bibliothek nutzt den Median im Systemfach): Core speichert beim Cover-Download bzw. -Import Breite und Höhe (Header-Lesen, kein Decode). Die Systemseite berechnet beim Laden den **Median** der vorhandenen Maße und setzt den Slot-Aspekt, *bevor* das Grid gebaut wird. Gibt es weniger als 8 Maße, gelten die Startwerte.
- **Startwerte** (H/B): NES 1,38 · SNES 1,00 · N64 0,90 · GB/GBC/GBA 1,00 · NDS 0,90 · 3DS 0,86 · GC/Wii/Wii U 1,40 · Switch 1,62 · PS1 1,00 · PS2 1,42 · PSP 1,72 · MD/SMS 1,40 · Saturn 1,00 · Dreamcast 1,00 · GG 1,25 · Arcade 1,40 · PC 1,40 · sonst 1,25.
- **Gemischte Bibliothek** (M3-QA, ersetzt „bleibt bei 1 : 1,25“): Median der Schachtelformate *aller* Spiele (gespeichertes Covermaß, sonst Startwert des Systems, also das Format seines Platzhalters), geklemmt auf 1,00–1,40, vor dem Bau des Grids gesetzt. Es entscheidet die ganze Sammlung, nicht Suche oder Favoriten, damit das Fach beim Tippen nicht springt. 1,25 bleibt nur der Wert für eine leere Sammlung.

### 11.5 Visuelle QA-Checkliste (nach jedem Meilenstein, mit echten Screenshots)

Screenshots unter `artifacts/screens/` (Avalonia headless, `CaptureRenderedFrame`) in **1920×1080, 1600×900, 1100×700, 2560×1440**, jeweils mit Demo-Katalog *und* einem Vorschau-Katalog mit echten Covern, dazu eine Bibliothek mit 0 % Covern. Jedes Bild wird angesehen, bevor ein Ergebnis behauptet wird.

**Identität**
- [ ] Kein `#7B61FF`/`#4C8DFF`, kein Verlauf als Deko, keine Glasflächen, keine Wand mit Verlauf
- [ ] Keine Logos, Wortmarken, Hardware-Fotos, kein Rot/Blau-Schienenpaar, keine farbigen ABXY-Tasten, keine PS-Symbole
- [ ] Außenseiter-Test der Motive bestanden (6.1)
- [ ] Radien nur 0/2/4/6, Fasen nur Ring und Platte; Kreise nur Statuspunkte, Tastenraute, Discs
- [ ] Keine Versalien-Overlines, keine H2 über Brettern, keine Zahlenspalten, kein Wasserzeichen
- [ ] Bernstein nur gemäß Liste 2.2
- [ ] Scanlines nur im Bildschirm, Korn nur im Hintergrund

**Lesbarkeit (3 m)**
- [ ] Kein lesbarer Text < 15 px (Ausnahme: Aufkleber „Neu“), Text 3 nie unter 15 px
- [ ] `FontRoleTests` grün (jede Rolle löst auf Familie, Gewicht und Breite auf)
- [ ] Kontraste: Text ≥ 4,5 : 1, Ring ≥ 3 : 1 gegen Raum *und* helles Cover (Raum-Tief-Spalt)
- [ ] Lange Titel („Castlevania: Symphony of the Night“, „Super Mario Advance 4: Super Mario Bros. 3“) wählen die richtige Stufe und überlappen nichts
- [ ] Fokus auf jedem Screenshot in < 1 s auffindbar (Testperson)

**Komposition**
- [ ] Start und Detail haben verschiedene Kompositionen (Bildschirm-Held gegen Schachtel-Held)
- [ ] Alles steht auf einem Brett oder hängt daran; keine Karten in Karten
- [ ] Cover stehen unten bündig, kein Cover ist beschnitten
- [ ] Fehlende Daten: kein „–“, kein „Genre:“, kein Ersatzsatz
- [ ] 1100 px: kein horizontales Abschneiden, Fokus nie hinter der Fußleiste

**Plattformen**
- [ ] `SystemPaletteTests` grün (Kontraste, ΔE2000-Regel)
- [ ] Alle Systeme mit Spielen sind im Konsolen-Screenshot ohne Lesen unterscheidbar
- [ ] Graue, helle und schwarze Systeme (NDS, PS1, PSP, Wii, GB, Lynx, 32X, Mega Drive) wirken nicht wie Platzhalter

**Bewegung und Leistung**
- [ ] Bildschirmaufnahme (eigener Prozess, PrintWindow per PID): Fokuswechsel, Gleiten, Konsolenwechsel, Fernsehlicht nach Verweilzeit, Halten und Loslassen ohne Flackern, Einschalten, Rückkehr
- [ ] `.motion-reduced` und `.motion-off` geprüft
- [ ] 60 fps bei gehaltener Taste im 1000er-Grid bei 2560×1440
- [ ] Keine Texturen-Moiré bei UiScale 0,80 und 1,33 (Zoom auf 400 % im PNG)

**Verhalten**
- [ ] Zurück stellt den vorherigen Fokus wieder her (Konsolen → System → Detail → zurück)
- [ ] Fußleiste zeigt passende Hinweise und den Ortssatz
- [ ] Start-Overlay ohne doppelte Titel; nichts liegt über dem Emulatorfenster

---

## 12. Was entfällt

- Aus v0 (Audit): Sidebar 248/84 px, Markenquadrat mit Play-Dreieck, „RETRO-LAUNCHER“-Overline, Begrüßung, 4 StatTiles, Hero-Watermark „GBA“, Initialen-Platzhalter, Badge-Pillen, Gradient-Fokusring, Glow-Schatten, Gradient-Buttons, Dimm-Ebene auf allen unfokussierten Kacheln, System-Chips mit Rahmen, Tabellen-Liste, Statistik-Karten, Start-Panel mit Combos, Ersatzsatz für Beschreibung, Konsolen-Kategoriekacheln, Status-Panel als Hero, Buchstaben-Avatare, Karten-in-Karten.
- **Neu gestrichen in v1.1** (aus v1.0): Jahres-Wasserzeichen, Zahlenspalten im Detail und auf der Systemseite, „66 Spiele in 13 Fächern“-Poster und Rückenleiste als Kopf, H2-Brettitel mit „Alle anzeigen ›“, Fachschilder 248×140, radiales Spotlicht, runder versetzter Fokusring, Korn über allem, Scanlines außerhalb des Bildschirms, Fernsehlicht in der Bibliothek, Wände mit Verlauf, Systemwände aus der Akzentformel, Poppins, Xbox-Farbglyphen und PS-Symbole, Kippschalter und Segment-Controls, Inspektor-Spalte, Toasts für Scan-Ergebnisse, Motive „Stab“, „Tablett mit Schienen“, „Umlaufbahn“ und „Rennstreifen“, Schaufenster-Cover im Banner.

---

## 13. Entscheidungen zur Kritik

Legende: **A** = angenommen, **AP** = angepasst übernommen, **V** = verworfen.

### 13.1 Generik-Risiken

| # | Kritikpunkt | Entscheidung | Begründung (eine Zeile) | Abschnitt |
|---|---|---|---|---|
| G1 | Start und Detail nutzen denselben Hero (Cover links, Titel, Buttons auf Scrim) | **AP** | Start bekommt den Bildschirm als Held mit der Schachtel daneben auf dem Brett, Detail die gezogene Schachtel mit Rücken und Rückseite. Der Titel bleibt als Plakat im unten verankerten Textblock, weil er auf 3 m lesbar sein muss, und wandert nicht auf ein Etikett | 7.1, 7.3 |
| G2 | Jahres-Wasserzeichen ist ein KI-Editorial-Trope | **A** | Das Jahr steht im Satz und am Fuß des Schachtelrückens. Den Aufkleber „ab 2004“ verwerfen wir, weil „ab“ im Deutschen als Altersfreigabe gelesen wird | 7.3, 10.3 |
| G3 | Zahlenspalten sind KPI-Kacheln ohne Karte | **A** | Zahlen stehen in Sätzen, nur hervorgehoben. Die Ausleihkarte kommt, weil `Sessions()` in Core schon existiert | 7.3, 7.5 |
| G4 | „66 Spiele …“ plus Rückenleiste ist ein Dashboard-Widget | **A** | Daraus wird der Regalspiegel als fixer Positions-Scrubber (ein Rücken pro Spiel, Klammer, Fokusmarke). Die Anzahl wandert in die Fußleiste | 7.2.1 |
| G5 | Weicher Verlauf auf allen Wänden ist generisch | **A** | Wände sind hart zweigeteilt (38/62) mit Lichtkante. Volltöne interpolieren außerdem per `BrushTransition` | 1.2, 2.4 |
| G6 | Wandformel macht Familien ununterscheidbar | **AP** | Die Wand kommt aus dem Gehäusematerial (9 Materialien, L-Band 0,06–0,25). Statt „ΔE ≥ 8 zwischen allen Wänden“ (bei 32 dunklen Wänden physikalisch nicht erreichbar) gilt die testbare Regel „gleiches Gehäuse + gleiches Motiv → ΔE(Farbe) ≥ 15“ | 2.4 |
| G7 | Radiales Spotlicht ist ein Glow | **A** | Der Lichtkegel kommt von oben, dazu kommen beleuchtetes Brettstück und stärkerer Kontaktschatten. Farbe ist die Kontext-Lichtfarbe statt Bildlicht, damit im Grid nichts pro Cover flackert | 8.1 |
| G8 | Versetzter runder Ring auf allem ist Standard-A11y | **A** | Der Ring ist ein gefastes Achteck und nur für Objekte. Alles Kleine invertiert | 4.1, 8.2 |
| G9 | H2 plus „Alle anzeigen“ plus Karussell ist die Netflix-Reihe | **A** | Das Etikett hängt am 24-px-Brett, „Alle“ ist nur noch ein Rücken am Brettende | 7.1 |
| G10 | Fachschilder 248×140 sind System-Karten | **A** | Systemrücken 72×200 mit Front 200×200 auf Start, dasselbe Control wie Konsolen | 7.1, 7.4 |
| G11 | Fernsehlicht ohne sichtbare Quelle ist der Standard-Launcher-Hintergrund | **AP** | Der Schein sitzt am Bildschirm bzw. an der Schachtel, und das Brettlicht ist eine geteilte Farbe mit Transition. In der Bibliothek ist das Fernsehlicht aus. Auf Start und Detail bleibt es abgeschwächt (0,38) und nur über dem Brett, weil Bildpräsenz Owner-Priorität ist | 2.5 |
| G12 | Poppins ist die Dribbble-/SaaS-Schrift | **A** | Archivo wird das System, Atkinson Hyperlegible Next die Leseschrift. Beides ist heruntergeladen, geprüft und eingecheckt. Für Poppins gibt es keinen starken Grund mehr | 3.1 |
| G13 | Tabs mit Unterstrich sind die Standard-Web-Navigation | **A** | Kanäle mit Ziffer, Kanalmarke 3×12 und eine hart geschaltete Kanalanzeige. Zusätzlich lässt sie sich abschalten und die Tasten 1/2/3 schalten direkt | 7.0 |
| G14 | Einstellungen sind ein Windows-Settings-Klon | **A** | Service-Menü mit ◀ Wert ▶ und Steuerkreuz links/rechts. Schalter sind Textwerte | 7.6 |
| G15 | Vollbreite Papier-Inversion ist heller als jedes Bild | **A** | Zeilen werden Regal hell mit Balken, Lichtkante und Papier-Chip. Inversion gibt es nur für kleine Elemente | 8.2 |
| G16 | Xbox-ABXY-Farben und PS-Symbole sind Trade Dress | **A** | Positionsbasierte Tastenraute, Schulterkappen mit Keil, Bedeutung als Text, Gerätename als reiner Text | 5.3 |
| G17 | Switch-, Wii-, Saturn-Motive und Rennstreifen sind zu nah an Produkten | **A** | Dock, Schachtständer, Disc mit Bändern, Rennstreifen gestrichen. Dazu kommt der Außenseiter-Test. Zusätzlich: Cart64 bekommt flachere Schultern, Virtual Boy wird zur Doppellinse | 6 |
| G18 | Korn über allem und Scanlines überall sind der KI-Retro-Filter | **A** | Korn gibt es nur im Hintergrund (2,5 %), Scanlines nur im Bildschirm-Objekt | 2.5, 4.5 |
| G19 | Emulatoren und Importe sind eine generische Admin-Liste | **A** | Emulatoren nach eigenen Systemen mit Aufklappen statt Inspektor, Importe mit Lieferschein statt Toasts. Umsetzung erst in M6 (Owner-Priorität) | 7.6 |
| G20 | Einschaltlinie ist ein Klischee | **AP** | Die Linie ist in der Licht-Farbe des Systems, mit OSD-Code und einem Frame Überbelichtung. Die Überbelichtung entfällt bei reduzierter Bewegung (Lichtempfindlichkeit) | 9.3 |

### 13.2 Machbarkeit

| # | Problem | Entscheidung | Lösung (eine Zeile) | Abschnitt |
|---|---|---|---|---|
| F1 | UiScale per LayoutTransform zerstört Pixeltreue, und das HWND liegt im skalierten Baum | **A** | `RoomBackdrop`, GameScreen, `FlightLayer` und OSD liegen außerhalb. `TextureFill` und `PixelScreen` rechnen in Gerätepixeln mit ganzzahligem Faktor bzw. weicher Contain-Variante | 7.0, 4.5 |
| F2 | `BrushTransition` interpoliert keine Gradients | **A** | Alle animierten Wände sind Volltöne (zwei Flächen), das Brettlicht ist eine `SolidColorBrush` mit `ColorTransition` | 2.4, 2.5 |
| F3 | Ring und Spot werden in `CoverArt` geclippt, Z-Reihenfolge asymmetrisch | **A** | `FocusLayer` (Back/Front) pro Grid im Scroll-Inhalt, ZIndex auf Kachel und Zeile, `CoverArt` bleibt geclippt und liefert `ArtRect` | 8.1 |
| F4 | „Korn über Seiten, unter der Fokus-Ebene“ ist nicht baubar | **A** | Korn nur im Hintergrund | 2.5 |
| F5 | 48-px-Decode ×40 hochskaliert ergibt Rauten und Machbänder | **A** | SkiaSharp-Blur vorab auf einem Hintergrund-Thread, 64×36, LRU, `HighQuality`, Korn als Dither | 2.5 |
| F6 | Vollbild-Ebenenstapel macht nach jedem Fokus 420 ms lang das ganze Fenster dirty | **A** | Fernsehlicht nur auf Start/Detail, Scrims in ein Bitmap pro Größen-Bucket geflacht | 2.5, 9.5 |
| F7 | Textur-Versatz pro Spiel widerspricht „eine Brush“ | **A** | `PushClip` plus `PushTransform` mit geteilter Brush, `FormattedText` pro Kachel gecacht | 10.2 |
| F8 | Median-Neuberechnung lässt Zeilen beim Browsen springen | **A** | Cover-Maße in der DB, Aspekt vor dem Seitenaufbau | 11.4 |
| F9 | 220-ms-Anker-Transition hinkt bei Auto-Repeat | **A** | `GlideRepeat` 90 ms linear unter `.nav-repeat`; Konsolen-Versatz aus Zielbreiten | 7.1, 7.4 |
| F10 | Flug der Schachtel aus der virtualisierten Zeile ist nicht spezifiziert | **A** | Kopie im `FlightLayer`, Rechteck per `TranslatePoint` zum TopLevel, Original auf Opacity 0 bis zur Rückkehr | 9.3 |
| F11 | Größenstufen nach Zeichenzahl sind mit Archivo Expanded unzuverlässig | **A** | Messen per `TextLayout` (3 Stufen) nur für Start- und Detail-Titel | 3.2 |
| F12 | Archivo-Namen (ID 1 vs. 16) ungeprüft | **A, geprüft** | Es gibt einen echten Avalonia-Fehlfall: Mehrere Breiten in einem Ordner werden alle zu Condensed aufgelöst. Gelöst durch einen Ordner pro Breite; SemiCondensed Bold (w680) wird nicht geliefert; `FontRoleTests` sichern das ab | 3.1 |
| F13 | `+tnum` in Avalonia 12 und in den Fonts ungeprüft | **A, geprüft** | `TextBlock.FontFeatures` funktioniert, beide Familien haben `tnum` (gemessene Breiten gleich) | 3.1 |
| F14 | Repeat-Erkennung fehlt, Loslassen löst Crossfade aus | **A** | `NavRepeatTracker` mit Repeat-Flag aus `InputTranslator` bzw. KeyDown ohne KeyUp, Ende beim Loslassen oder nach 150 ms. Danach nur ein Dwell-Neustart bei geändertem Ziel | 8.4 |
| F15 | Front-Text bricht während der Breitenanimation um | **A** | Front-Inhalt fest 520 px in einem Clip-Container, animiert wird nur der Clip | 7.4 |

### 13.3 Stärkste Ideen (alle beibehalten)

Diese Ideen bleiben erhalten und wurden geschärft: Cover stehen auf Brettern, gemischte Formate sind gewollt (jetzt als Objekt *Brett* mit Etiketten). Rücken-Platzhalter statt Initialen (jetzt mit zweifarbigem Grund und Text-Cache). Meta als Satz (jetzt mit Zahl-Runs). Warme, opake Neutrale mit Bernstein und abschließender Liste. Systemlicht im System. Breitenkontrast in Archivo (jetzt als ganzes System). Gefaste Icons (jetzt auch Ring und Platte). Generierte Ära-Texturen (jetzt pixeltreu über `TextureFill`). Konsolen als Rücken und Front (jetzt auch auf Start). Aufkleber „Neu“. Performance-Disziplin (erweitert um `FocusLayer`, Off-Thread-Blur und Bibliothek ohne Fernsehlicht). Fallback-Ketten pro Fläche (plus „Kein Signal“). Ortssatz und Hinweise aus dem Fokus. „Alle“ als Rücken (der Kopf-Link ist jetzt ganz weg). Lösungs-Button. Ein inszenierter Moment. Konkrete QA-Liste (plus Außenseiter-Test, Schrift- und Palettentests, Moiré-Prüfung).
