"""Collect the sprites the app needs from <assets-r3>/sprites into one export folder, optimised, with a manifest.

Usage: python package_assets.py <assets-r3 dir> [out dir]   (default <dir>/export/Rendered)

Keeps: the 1x and 2x of every object layer (base, band/body/color/lift/light light maps, shade/gloss, shadow); for the
long parts (boards, rail, tag) only the 9-slice pieces (left/right caps and the seamless middle tile), not the full
render; the per-object .json rects; the app-size material tiles (materials/<name>-<N>.png).
Drops: previews (_*), the full-length board/rail/tag renders. RGB under alpha 0 is zeroed (smaller PNGs), every PNG
is re-saved with optimize=True. Writes manifest.json (file -> bytes) and prints the total.
"""
import json
import os
import shutil
import sys

import numpy as np
from PIL import Image

SLICED = ("board-stage", "board-edge", "rail", "tag-l", "tag-s")


def keep(fn):
    if fn.startswith("_"):
        return False
    stem = fn.split("@")[0]
    for s in SLICED:
        if stem in (s, f"{s}-shadow"):          # the full render of a sliced part
            return False
    return fn.endswith(".png") or fn.endswith(".json")


def optimise(src, dst):
    im = Image.open(src)
    if im.mode in ("RGBA", "LA"):
        a = np.asarray(im).copy()
        clear = a[..., -1] == 0
        a[clear] = 0
        im = Image.fromarray(a, im.mode)
        # a light map / shadow whose grey is constant: store it as LA anyway (Avalonia masks read the alpha)
    im.save(dst, optimize=True)


def main():
    root = sys.argv[1]
    out = sys.argv[2] if len(sys.argv) > 2 else os.path.join(root, "export", "Rendered")
    if os.path.isdir(out):
        shutil.rmtree(out)
    os.makedirs(os.path.join(out, "materials"))
    man = {}
    sp = os.path.join(root, "sprites")
    for fn in sorted(os.listdir(sp)):
        if not keep(fn):
            continue
        d = os.path.join(out, fn)
        if fn.endswith(".png"):
            optimise(os.path.join(sp, fn), d)
        else:
            shutil.copy(os.path.join(sp, fn), d)
        man[fn] = os.path.getsize(d)
    md = os.path.join(root, "materials")
    for fn in sorted(os.listdir(md)):
        if fn.endswith("-1024.png") or fn.startswith("_"):
            continue
        d = os.path.join(out, "materials", fn)
        optimise(os.path.join(md, fn), d)
        man["materials/" + fn] = os.path.getsize(d)
    total = sum(man.values())
    json.dump({"total_bytes": total, "files": man}, open(os.path.join(out, "manifest.json"), "w"), indent=1)
    groups = {}
    for k, v in man.items():
        g = k.split("/")[0] if "/" in k else k.split("@")[0].split("-")[0]
        groups[g] = groups.get(g, 0) + v
    for g, v in sorted(groups.items(), key=lambda x: -x[1]):
        print(f"{g:12s} {v / 1e6:6.2f} MB")
    print(f"TOTAL {total / 1e6:.2f} MB in {len(man)} files -> {out}")


if __name__ == "__main__":
    main()
