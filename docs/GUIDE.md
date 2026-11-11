# RetroHub – Guide

Your retro game centre: all your console, handheld and PC games in one window – with mouse, keyboard or
controller. The emulators come with it, already set up. Games, BIOS files, keys and firmware are **not**
included – you need your own, from your own devices and games.

## First start

1. Start RetroHub: desktop icon, Start menu, or type "RetroHub" into Windows search.
2. Put your games in (see below). RetroHub finds them on the next scan.
3. Pick a game and start it. **Hold Select + Start** to get back to RetroHub from any game (how long: *Settings ›
   Zurück zu RetroHub*).
4. Don't forget to save inside the game!

## Where do the games go?

- **One folder per system:** `games\<system>\`, e.g. `games\gba\`, `games\snes\`, `games\ps1\`, `games\switch\`.
  RetroHub creates them. Subfolders are fine.
- **Or your existing collection:** *Imports › Add folder* (e.g. an external drive). Your files stay where they
  are – RetroHub never changes game files.
- Multi-file disc games (`.cue` + `.bin`) belong together in one folder; several discs of one game via `.m3u`.

## What does each console need?

**The short version:** *Settings › Firmware & Keys* shows, per console, what is there and what is missing.
**"Import firmware & keys"** lets you pick your files (one, several, or a `.zip`); RetroHub recognises what each
file is (by content, checksum, name and size) and puts it exactly where the emulator expects it. A whole folder
works with "Import a folder". With a controller: put the files into `bios\import\` first, then choose **"Take
from bios/import"**. Your originals are only read. A replaced older file is kept as `….retrohub-old-<time>`.

| Console | Emulator | Needs | Files (names) | Goes to |
|---|---|---|---|---|
| Game Boy, Game Boy Color | mGBA | nothing | – | – |
| Game Boy Advance | mGBA | nothing (BIOS optional: boot logo) | `gba_bios.bin` | `bios\` |
| Nintendo DS | melonDS, DeSmuME | nothing (BIOS optional; some encrypted ROMs only boot with it) | `bios7.bin`, `bios9.bin`, `firmware.bin` | `bios\` |
| Nintendo 3DS | Azahar | nothing – **decrypted** games only (`.3ds`, `.cci`, `.cxi`) | – | – |
| GameCube, Wii | Dolphin | nothing | – | – |
| Wii U | Cemu | only for encrypted disc images (`.wux`, `.wud`): the disc keys | `keys.txt` or `<game>.key` | `bios\wiiu\keys.txt` and `emulators\cemu\portable\keys.txt` |
| Nintendo Switch | Eden | **keys and firmware from your own Switch** | `prod.keys`, `title.keys`, firmware folder or `.zip` of `.nca` files | keys: `bios\switch\` and `emulators\eden\user\keys\`; firmware: `emulators\eden\user\nand\system\Contents\registered\` |
| NES (Famicom Disk System) | RetroArch | only for `.fds` games | `disksys.rom` | `bios\` |
| PlayStation | RetroArch (PCSX ReARMed) | runs without; more accurate with a BIOS | `scph5501.bin` (USA), `scph5502.bin` (EU), `scph5500.bin` (JP), `scph1001.bin` | `bios\` |
| PlayStation 2 | RetroArch (LRPS2) | **a PS2 BIOS** (any name, e.g. `SCPH-70004…BIN`) | your own dump | `bios\pcsx2\bios\` |
| PSP | RetroArch (PPSSPP) | the data folder `PPSSPP` (no BIOS) | the "assets" folder of the PPSSPP download | `bios\PPSSPP\` |
| Mega-CD / Sega CD | RetroArch (Genesis Plus GX) | the BIOS of the game's region | `bios_CD_U.bin`, `bios_CD_E.bin`, `bios_CD_J.bin` | `bios\` |
| Saturn | RetroArch (Beetle Saturn) | one of the Saturn BIOS files | `sega_101.bin` (JP), `mpr-17933.bin` (USA/EU) | `bios\` |
| Dreamcast | RetroArch (Flycast) | nothing (BIOS optional) | `dc_boot.bin`, `dc_flash.bin` | `bios\dc\` |
| PC Engine CD | RetroArch (Beetle PCE Fast) | only for CD games | `syscard3.pce` | `bios\` |
| Atari Lynx | RetroArch (Handy) | nothing (optional) | `lynxboot.img` | `bios\` |
| Atari 7800 | RetroArch (ProSystem) | nothing (optional: boot logo) | `7800 BIOS (U).rom` | `bios\` |
| MSX | RetroArch (blueMSX) | the data folders `Machines` and `Databases` | from the blueMSX download | `bios\Machines\`, `bios\Databases\` |
| Neo Geo (arcade) | RetroArch (FBNeo) | only for Neo Geo games | `neogeo.zip` | `bios\` (or next to the games) |

All other systems (SNES, N64, Mega Drive, Master System, Game Gear, 32X, Atari 2600, Virtual Boy, Neo Geo
Pocket, WonderSwan) need nothing.

### Nintendo Switch: keys and firmware

Eden only starts Switch games with the **keys (`prod.keys`, usually also `title.keys`) and the firmware of your
own Switch**. You have to dump both from your own console – RetroHub never includes or downloads them.

1. *Settings › Firmware & Keys › Nintendo Switch › Import*: choose `prod.keys` and `title.keys`.
2. *Nintendo Switch: firmware folder*: choose the folder with your firmware's `.nca` files – or import the
   firmware as a `.zip` via "Import firmware & keys".
3. Done. If Eden is not installed yet, the firmware waits in `bios\switch\firmware\` and moves into place on
   Eden's first start. An older firmware in Eden is not mixed in but set aside as
   `registered.retrohub-old-<time>`.

### Wii U: disc keys

Decrypted games (folders with `code\`, `content\`, `meta\`, or `.wua`) need no keys. For encrypted disc images
(`.wux`, `.wud`) Cemu needs the disc key of your own disc: put the matching `.key` file with the same name next
to the image (`Game.key` next to `Game.wux`) – RetroHub adds it on start – or import your `keys.txt` / `.key`
files under *Firmware & Keys*. Keys are never added twice.

## Everything in one folder

Everything lives in the RetroHub folder (usually `C:\Users\<name>\RetroHub`). Nothing goes to `%APPDATA%`,
"Documents" or another emulator installation.

| Folder | Contents |
|---|---|
| `games\<system>\` | your games |
| `bios\` | BIOS, keys, firmware, data folders – everything you import (good to back up) |
| `bios\switch\`, `bios\wiiu\`, `bios\pcsx2\bios\`, `bios\import\` | Switch keys, Wii U keys, PS2 BIOS, inbox for the controller |
| `saves\<system>\` | save files (mGBA, melonDS, DeSmuME, RetroArch); 3DS: `saves\3ds\nand\` and `saves\3ds\sdmc\` |
| `savestates\<system>\` | save states (mGBA, melonDS, DeSmuME, RetroArch) |
| `screenshots\<system>\` | screenshots (mGBA, DeSmuME, Azahar, RetroArch) |
| `emulators\dolphin\User\` | GameCube memory cards, Wii NAND, Dolphin's states, screenshots, settings |
| `emulators\cemu\portable\` | Wii U saves (`mlc01`), `keys.txt`, settings |
| `emulators\eden\user\` | Switch saves (`nand`), keys, firmware, SD card, settings |
| `emulators\azahar\user\` | Azahar's settings and save states |
| `emulators\<emulator>\` | the emulators themselves with their settings |
| `artwork\` | covers and pictures |
| `data\` | database, RetroHub's settings, `data\logs\` |
| `app\` | RetroHub itself – don't touch |

RetroHub sets up every bundled emulator as portable before starting it and switches off their own update checks
(Dolphin, Cemu, Azahar, Eden, mGBA) – no emulator replaces its own files; updates come with the RetroHub setup.
If you chose your own program outside the RetroHub folder under *Emulators*, RetroHub leaves it untouched – it
keeps saving where it always did.

## Starting: embedded or fullscreen

- **Embedded** (mGBA, melonDS, DeSmuME, Azahar): the game runs inside the RetroHub window.
- **Fullscreen** (Dolphin, Cemu, Eden, RetroArch): the emulator takes over the screen.
- On each game's page you can choose the emulator and the start mode (*embedded*, *fullscreen*, *window*).
  RetroHub itself goes fullscreen with **F11** (*Settings › Im Vollbild starten*).

## Controller

- Use RetroHub with Xbox, PlayStation and other controllers: A/Cross = start, B/Circle = back, X = favourite,
  Y = search, shoulder buttons = next section. On/off under *Settings › Mit Controller bedienen*.
- **Hold Select + Start** = back to RetroHub.
- In-game buttons are set in each emulator (RetroArch recognises common controllers by itself).

## Updating and uninstalling

- **Update:** run the new setup. Program and emulators are replaced; games, saves, `bios\`, settings and the
  emulators' data stay.
- **Uninstall:** via Windows "Apps". Only the program and the emulators are removed. Your games, saves, save
  states, screenshots, covers, `bios\` (keys, BIOS, firmware) and the emulators' data stay in the RetroHub
  folder. Delete the folder yourself if you no longer need them.

## Troubleshooting

- **A game does not start:** its page says what is missing (emulator, core, BIOS, keys). *Settings › Firmware &
  Keys* shows the state per console.
- **"unknown version":** the file has the right name but a different checksum than known good dumps. It often
  works anyway; otherwise dump it again.
- **A game is missing from the library:** is it in the right `games\<system>\` folder? *Imports › Scan now*.
- **Logs:** `data\logs\` – attach the newest file when asking for help. Keys are never written there.

## Licence

RetroHub is free and must stay free. Some bundled RetroArch cores may only be distributed
**non-commercially**: **FBNeo** (arcade), **Snes9x** (SNES), **Genesis Plus GX** (Master System, Mega Drive,
Game Gear, Mega-CD) and **PicoDrive** (32X). RetroHub must therefore never be sold or bundled for a fee. The
licence texts are in the emulators' folders (`emulators\retroarch\licenses\`). RetroHub is an unofficial
project with no connection to Nintendo, Sony, Sega, Atari or any other manufacturer. Only use copies of games,
BIOS and keys that you own.
