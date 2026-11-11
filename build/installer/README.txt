RetroHub
========

Your retro game centre: all your console, handheld and PC games in one
window – with mouse, keyboard or controller. The emulators come with it,
already set up. Games, BIOS files, keys and firmware are NOT included – you
need your own, from your own devices and games.

FIRST START
1. Start RetroHub: desktop icon, Start menu, or type "RetroHub" into
   Windows search.
2. Put your games in (see below). RetroHub finds them on the next scan.
3. Pick a game and start it. Hold Select + Start to get back to RetroHub
   from any game (how long: Settings > Zurück zu RetroHub).
4. Don't forget to save inside the game!
RetroHub speaks German and English (Settings).

WHERE DO THE GAMES GO?
- One folder per system: games\<system>\, e.g. games\gba\, games\snes\,
  games\ps1\, games\switch\. RetroHub creates them. Subfolders are fine.
- Or your existing collection: Imports > Add folder (e.g. an external
  drive). Your files stay where they are – RetroHub never changes game
  files.
- Multi-file disc games (.cue + .bin) belong together in one folder;
  several discs of one game via .m3u.

WHAT DOES EACH CONSOLE NEED?
Settings > Firmware & Keys shows, per console, what is there and what is
missing. "Import firmware & keys" lets you pick your files (one, several,
or a .zip); RetroHub recognises what each file is (by content, checksum,
name and size) and puts it exactly where the emulator expects it. A whole
folder works with "Import a folder". With a controller: put the files into
bios\import\ first, then choose "Take from bios/import". Your originals are
only read. A replaced older file is kept as "….retrohub-old-<time>".

  Game Boy, Game Boy Color – mGBA: needs nothing.
  Game Boy Advance – mGBA: nothing (BIOS optional, for the boot logo):
      gba_bios.bin -> bios\
  Nintendo DS – melonDS, DeSmuME: nothing (BIOS optional; some encrypted
      ROMs only boot with it): bios7.bin, bios9.bin, firmware.bin -> bios\
  Nintendo 3DS – Azahar: nothing – DECRYPTED games only (.3ds, .cci, .cxi).
  GameCube, Wii – Dolphin: needs nothing.
  Wii U – Cemu: only for encrypted disc images (.wux, .wud) the disc keys:
      keys.txt or <game>.key
      -> bios\wiiu\keys.txt and emulators\cemu\portable\keys.txt
  Nintendo Switch – Eden: KEYS AND FIRMWARE FROM YOUR OWN SWITCH:
      prod.keys, title.keys -> bios\switch\ and emulators\eden\user\keys\
      firmware (folder or .zip of .nca files)
      -> emulators\eden\user\nand\system\Contents\registered\
  NES (Famicom Disk System) – RetroArch: only for .fds games:
      disksys.rom -> bios\
  PlayStation – RetroArch (PCSX ReARMed): runs without; more accurate with
      a BIOS: scph5501.bin (USA), scph5502.bin (EU), scph5500.bin (JP),
      scph1001.bin -> bios\
  PlayStation 2 – RetroArch (LRPS2): a PS2 BIOS (your own dump, any name,
      e.g. SCPH-70004…BIN) -> bios\pcsx2\bios\
  PSP – RetroArch (PPSSPP): the data folder "PPSSPP" (no BIOS): the
      "assets" folder of the PPSSPP download (https://www.ppsspp.org)
      -> bios\PPSSPP\  (not included; use "Import a folder")
  Mega-CD / Sega CD – RetroArch (Genesis Plus GX): the BIOS of the game's
      region: bios_CD_U.bin, bios_CD_E.bin, bios_CD_J.bin -> bios\
  Saturn – RetroArch (Beetle Saturn): one of the Saturn BIOS files:
      sega_101.bin (JP), mpr-17933.bin (USA/EU) -> bios\
  Dreamcast – RetroArch (Flycast): nothing (BIOS optional):
      dc_boot.bin, dc_flash.bin -> bios\dc\
  PC Engine CD – RetroArch (Beetle PCE Fast): only for CD games:
      syscard3.pce -> bios\
  Atari Lynx – RetroArch (Handy): nothing (optional): lynxboot.img -> bios\
  Atari 7800 – RetroArch (ProSystem): nothing (optional, boot logo):
      7800 BIOS (U).rom -> bios\
  MSX – RetroArch (blueMSX): the data folders "Machines" and "Databases"
      from the blueMSX download -> bios\Machines\, bios\Databases\
      (not included; use "Import a folder")
  Neo Geo (arcade) – RetroArch (FBNeo): only for Neo Geo games:
      neogeo.zip -> bios\ (or next to the games)
  All other systems (SNES, N64, Mega Drive, Master System, Game Gear, 32X,
  Atari 2600, Virtual Boy, Neo Geo Pocket, WonderSwan) need nothing.

NINTENDO SWITCH: KEYS AND FIRMWARE
Eden only starts Switch games with the keys (prod.keys, usually also
title.keys) and the firmware of your own Switch. You have to dump both from
your own console – RetroHub never includes or downloads them.
1. Settings > Firmware & Keys > Nintendo Switch > Import: choose prod.keys
   and title.keys.
2. "Nintendo Switch: firmware folder": choose the folder with your
   firmware's .nca files – or import the firmware as a .zip via "Import
   firmware & keys".
3. Done. If Eden is not installed yet, the firmware waits in
   bios\switch\firmware\ and moves into place on Eden's first start. An
   older firmware in Eden is not mixed in but set aside as
   "registered.retrohub-old-<time>".

SWITCH EMULATOR EDEN
  Eden is not part of the setup. If "Switch emulator Eden" is ticked during
  the installation (default), Setup downloads it from Eden's own website
  (stable.eden-emu.dev), checks the file and unpacks it into
  emulators\eden\. If that fails (no internet, site unreachable), Setup
  tells you – everything else is installed anyway.
  Adding Eden later:
  a) run Setup again (with internet) and leave "Switch emulator Eden"
     ticked – your games and saves stay. Or
  b) download it yourself: https://eden-emu.dev/downloads -> Windows,
     "amd64-msvc-standard" (.zip). Unpack the zip's contents directly into
     emulators\eden\, so that emulators\eden\eden.exe exists (no
     subfolder). RetroHub finds Eden on its next start.

WII U: DISC KEYS
Decrypted games (folders with code\, content\, meta\, or .wua) need no
keys. For encrypted disc images (.wux, .wud) Cemu needs the disc key of
your own disc: put the matching .key file with the same name next to the
image (Game.key next to Game.wux) – RetroHub adds it on start – or import
your keys.txt / .key files under Firmware & Keys. Keys are never added
twice.

EVERYTHING IN ONE FOLDER
Everything lives in the RetroHub folder (usually C:\Users\<name>\RetroHub).
Nothing goes to %APPDATA%, "Documents" or another emulator installation.
  games\<system>\        your games
  bios\                  BIOS, keys, firmware, data folders – everything
                         you import (good to back up)
  bios\switch\, bios\wiiu\, bios\pcsx2\bios\, bios\import\
                         Switch keys, Wii U keys, PS2 BIOS, inbox for the
                         controller
  saves\<system>\        save files (mGBA, melonDS, DeSmuME, RetroArch);
                         3DS: saves\3ds\nand\ and saves\3ds\sdmc\
  savestates\<system>\   save states (mGBA, melonDS, DeSmuME, RetroArch)
  screenshots\<system>\  screenshots (mGBA, DeSmuME, Azahar, RetroArch)
  emulators\dolphin\User\   GameCube memory cards, Wii NAND, Dolphin's
                         states, screenshots, settings
  emulators\cemu\portable\  Wii U saves (mlc01), keys.txt, settings
  emulators\eden\user\   Switch saves (nand), keys, firmware, SD card,
                         settings
  emulators\azahar\user\ Azahar's settings and save states
  emulators\<emulator>\  the emulators themselves with their settings
  artwork\               covers and pictures
  data\                  database, RetroHub's settings, data\logs\
  app\                   RetroHub itself – don't touch

RetroHub sets up every bundled emulator as portable before starting it and
switches off their own update checks (Dolphin, Cemu, Azahar, Eden, mGBA) –
no emulator replaces its own files; updates come with the RetroHub setup.
If you chose your own program outside the RetroHub folder under
"Emulators", RetroHub leaves it untouched – it keeps saving where it always
did.

STARTING: EMBEDDED OR FULLSCREEN
- Embedded (mGBA, melonDS, DeSmuME, Azahar): the game runs inside the
  RetroHub window.
- Fullscreen (Dolphin, Cemu, Eden, RetroArch): the emulator takes over the
  screen.
- On each game's page you can choose the emulator and the start mode
  (embedded, fullscreen, window). RetroHub itself goes fullscreen with F11
  (Settings > Im Vollbild starten).

CONTROLLER
- Use RetroHub with Xbox, PlayStation and other controllers: A/Cross =
  start, B/Circle = back, X = favourite, Y = search, shoulder buttons = next
  section. On/off under Settings > Mit Controller bedienen.
- Hold Select + Start = back to RetroHub.
- In-game buttons are set in each emulator (RetroArch recognises common
  controllers by itself).

UPDATING AND UNINSTALLING
- Update: run the new setup – it finds the previous folder. Program and
  emulators are replaced; games, saves, bios\, settings and the emulators'
  data stay.
- Uninstall: via Windows "Apps". Only the program and the emulators are
  removed. Your games, saves, save states, screenshots, covers, bios\ (keys,
  BIOS, firmware) and the emulators' data stay in the RetroHub folder.
  Delete the folder yourself if you no longer need them.

TROUBLESHOOTING
- A game does not start: its page says what is missing (emulator, core,
  BIOS, keys). Settings > Firmware & Keys shows the state per console.
- "unknown version": the file has the right name but a different checksum
  than known good dumps. It often works anyway; otherwise dump it again.
- A game is missing from the library: is it in the right games\<system>\
  folder? Imports > Scan now.
- Logs: data\logs\ – attach the newest file when asking for help. Keys are
  never written there.

INCLUDED SOFTWARE
All emulators are unmodified official Windows builds. Each licence text is
in the emulator's folder. Eden is not part of the setup; if selected, it is
downloaded from Eden's own server during the installation.
- mGBA 0.10.5 (Game Boy, Game Boy Color, GBA) – MPL-2.0 – https://mgba.io
- melonDS 1.1 (DS) – GPL-3.0 – https://melonds.kuribo64.net
- DeSmuME 0.9.13 (DS) – GPL-2.0 – https://desmume.org
- Azahar 2126.2 (3DS) – GPL-2.0 – https://azahar-emu.org
- Dolphin 2609b (GameCube, Wii) – GPL-2.0-or-later – https://dolphin-emu.org
- Cemu 2.6 (Wii U) – MPL-2.0 – https://cemu.info
- Eden 0.2.1 (Switch, optional) – GPL-3.0-or-later – https://eden-emu.dev
  downloaded from https://stable.eden-emu.dev, source code:
  https://git.eden-emu.dev/eden-emu/eden (licence: emulators\eden\LICENSE.txt)
- RetroArch 1.22.2 – GPL-3.0 – https://www.retroarch.com
  with these cores (licences and source links: emulators\retroarch\licenses\,
  overview in CORES.txt):
  FBNeo (arcade, non-commercial licence), FCEUmm (NES, GPL-2.0),
  Snes9x (SNES, non-commercial licence), Beetle VB (Virtual Boy, GPL-2.0),
  Mupen64Plus-Next (N64, GPL-2.0), Stella (Atari 2600, GPL-2.0),
  ProSystem (Atari 7800, GPL-2.0), Handy (Lynx, Zlib),
  Beetle PCE Fast (PC Engine, GPL-2.0), Beetle NeoPop (Neo Geo Pocket,
  GPL-2.0), Beetle WonderSwan (GPL-2.0), blueMSX (MSX, GPL-2.0),
  Genesis Plus GX (Master System, Mega Drive, Game Gear, Mega-CD,
  non-commercial licence), PicoDrive (32X, non-commercial licence),
  Beetle Saturn (GPL-2.0), Flycast (Dreamcast, GPL-2.0),
  PCSX ReARMed (PlayStation, GPL-2.0), LRPS2 (PlayStation 2, GPL-3.0),
  PPSSPP (PSP, GPL-2.0)
- .NET Runtime – MIT; Avalonia – MIT
- Fonts: Archivo, Atkinson Hyperlegible Next – SIL Open Font License 1.1
The source code of the GPL-licensed programs is available from the
respective projects (links above).

LICENCE
RetroHub is free and must stay free. Some bundled RetroArch cores may only
be distributed non-commercially: FBNeo (arcade), Snes9x (SNES), Genesis Plus
GX (Master System, Mega Drive, Game Gear, Mega-CD) and PicoDrive (32X).
RetroHub must therefore never be sold or bundled for a fee. RetroHub is an
unofficial project with no connection to Nintendo, Sony, Sega, Atari or any
other manufacturer. All trademarks belong to their owners. Only use copies
of games, BIOS and keys that you own.
