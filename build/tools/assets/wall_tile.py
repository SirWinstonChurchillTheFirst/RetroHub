"""The Konsolen wall's material tile (R3 QA 2: the wall behind the binders was flat #121315 – "a code canvas").

Usage: python wall_tile.py <assets-r3 dir> <repo root> [--raw wall-felt-dark-1902.png] [--size 512]

Takes a generated material scan (comfy_materials.py, prompt "wall-felt-dark" in materials.json), removes its colour and
its large-scale light falloff, makes it seamless (make_tileable.tileable) and writes it as an OVERLAY that never shifts
the wall's tone: pixels lighter than the mean become white with alpha ~ (v - mean), darker ones black with alpha
~ (mean - v). The app draws it with TextureFill (whole device pixels) at a few percent over the system's wall tone.
Output: <repo>/src/RetroHub.App/Assets/Textures/wand-filz-512.png (+ a 2x2 seam check in <dir>/materials).
"""
import os
import sys

import numpy as np
from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from make_tileable import flatten, tileable  # noqa: E402


def main():
    root, repo = sys.argv[1], sys.argv[2]
    raw = sys.argv[sys.argv.index("--raw") + 1] if "--raw" in sys.argv else "wall-felt-dark-1902.png"
    size = int(sys.argv[sys.argv.index("--size") + 1]) if "--size" in sys.argv else 512
    a = np.asarray(Image.open(os.path.join(root, "raw", raw)).convert("RGB"), dtype=np.float32) / 255
    a = flatten(a, 64)
    grey = (a * np.array([0.2126, 0.7152, 0.0722], dtype=np.float32)).sum(-1, keepdims=True)
    t = tileable(np.repeat(grey, 3, -1))[..., :1]
    img = Image.fromarray((t[..., 0] * 255 + 0.5).astype(np.uint8), "L").resize((size, size), Image.LANCZOS)
    v = np.asarray(img, dtype=np.float32) / 255
    d = v - v.mean()
    # +-2.5 std -> alpha 1: the app's strength sets the visible contrast
    alpha = np.clip(np.abs(d) / (2.5 * d.std()), 0, 1)
    white = (d > 0).astype(np.float32)
    rgba = np.dstack([white, white, white, alpha])
    out = Image.fromarray((rgba * 255 + 0.5).astype(np.uint8), "RGBA")
    dst = os.path.join(repo, "src", "RetroHub.App", "Assets", "Textures", f"wand-filz-{size}.png")
    out.save(dst, optimize=True)
    chk = Image.new("RGB", (size * 2, size * 2), (24, 25, 28))
    over = Image.new("RGBA", chk.size)
    for i in range(2):
        for j in range(2):
            over.paste(out, (i * size, j * size))
    faded = np.asarray(over, dtype=np.float32)
    faded[..., 3] *= 0.35
    chk = Image.alpha_composite(chk.convert("RGBA"), Image.fromarray(faded.astype(np.uint8), "RGBA"))
    chk.save(os.path.join(root, "materials", "_seamcheck-wand-filz.png"))
    print(dst, os.path.getsize(dst), "bytes")


if __name__ == "__main__":
    main()
