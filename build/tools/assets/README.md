# Rendered assets (R3, Schwarz/Weiß/Cyan)

Physical objects for the Konsolen page and the shelves, rendered instead of drawn. Nothing here contains text, logos
or product shapes; the app prints codes, counts and names itself.

Working dir below is `<A>` (scratch, e.g. `…\scratchpad\design\assets-r3`), never the repo.

| step | command | output |
|---|---|---|
| 1 materials (GPU, ComfyUI) | `.\run_comfy.ps1 -Out <A>` (prompts: `materials.json`, driver: `comfy_materials.py`) | `<A>\raw\<name>-<seed>.png` |
| 2 seamless tiles | `python make_tileable.py <A>` | `<A>\materials\<name>-1024.png` (Blender) / `-256`/`-128` (app) |
| 3 motif sources | `python motif_maps.py <repo> <A>` (reads `Theming\MotifCatalog.cs`) | `<A>\motifs-src\<Motif>.npz` |
| 4 objects (CPU) | `.\run_blender.ps1 -Out <A> -Jobs spine-l,front-l,sleeves-l,tag-l,spine-s,front-s,sleeves-s,tag-s,board-stage,board-edge,bookend,rail,slider,case` | `<A>\props\<name>\*.npy` |
| 5 motifs (CPU) | `.\run_blender.ps1 -Out <A> -Script blender_motifs.py -Jobs all` | `<A>\props\motif-<Motif>\*.npy` |
| 6 sprites | `python build_sprites.py <A>` | `<A>\sprites\*@1x.png / *@2x.png / *.json` |
| 7 package | `python package_assets.py <A>` | `<A>\export\Rendered\` + `manifest.json` |
| 8 contact sheet | `python contact_konsolen.py <A> <repo>\src\RetroHub.App\Assets\Fonts` | `<A>\contact-konsolen.png` |

Safety: Blender always `-b --factory-startup -t 8`, Cycles on the CPU with OpenImageDenoise (the scripts set it),
BelowNormal priority. ComfyUI only through `run_comfy.ps1` (one hidden instance, `--lowvram`, stopped by PID).
Never run Blender while ComfyUI runs. A full step 4 takes ~15 min on 8 threads, so run it in batches of jobs.

## How the app draws a sprite (one render for every system colour)

Order: `-shadow` (black, alpha), the object (`<name>@Nx`, tinted regions rendered at albedo 0 so only sheen, wear and
edges are in it), then for each light map: `FillRectangle(colour)` under `PushOpacityMask(ImageBrush(map))`.

- spines / fronts / sleeves: `-band` = system colour, `-body` = housing tone (sleeves: near-neutral, ~16 % colour).
- motifs: `-color` = Farbe, `-body` = Zweitfarbe (lifted on dark walls), `-lift` = LiftColor, `-light` = Licht.
  Scale the 120-grid rect (`front` in the json, 160 px at 1x) into the motif box.
- case (keep case, 9-slice `slice` l13 t8 r8 b8): cover image into `window`, then `case-shade` (black: darkens the
  cover = multiply by the light), `case-gloss` (white: the sleeve's sheen), then `case` (frame with the window cut out).
- boards: tile `-tile` (and `-shadow-tile`) horizontally; rail and tag: `-left`, tiled `-tile`, `-right`.
All rects in `<name>.json` are 1x px from the sprite's top-left; `front` is the object's front face.
