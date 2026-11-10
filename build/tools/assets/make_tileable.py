"""Make the raw material scans seamless and small.

Usage: python make_tileable.py <assets-r3 dir>
Reads <dir>/raw/<name>-<seed>.png, picks the seed listed in PICK (or the first), removes low-frequency light falloff
(divide by a heavy blur), makes it tile by cross-fading with its half-period roll under a smooth window, then writes
<dir>/materials/<name>-1024.png (for Blender) and <dir>/materials/<name>-<N>.png (app size) plus a 2x2 seam check.
"""
import os
import sys

import numpy as np
from PIL import Image, ImageFilter

PICK = {"metal-powder-black": 1102, "laminate-edge-dark": 1202, "cardboard-grey": 1301, "label-paper-white": 1401,
        "plastic-scuffed": 1502, "dust-scratches": 1601, "wall-plaster-dark": 1701, "metal-brushed-dark": 1802}
# the new room is neutral (Schwarz/Weiss/Cyan): every scan keeps only a trace of its own colour
SATURATION = 0.15
APP_SIZE = {"metal-powder-black": 256, "laminate-edge-dark": 256, "cardboard-grey": 256, "label-paper-white": 128,
            "plastic-scuffed": 256, "dust-scratches": 256, "wall-plaster-dark": 256,
            "metal-brushed-dark": 256}


def flatten(a, radius=96):
    """Divide out the large-scale light falloff, keep the mean."""
    im = Image.fromarray(np.clip(a * 255, 0, 255).astype(np.uint8))
    blur = np.asarray(im.filter(ImageFilter.GaussianBlur(radius)), dtype=np.float32) / 255
    mean = a.reshape(-1, a.shape[-1]).mean(0)
    return np.clip(a / np.maximum(blur, 1e-3) * mean, 0, 1)


def tileable(a):
    h, w = a.shape[:2]
    rolled = np.roll(np.roll(a, h // 2, 0), w // 2, 1)
    y = np.linspace(0, 1, h)[:, None]
    x = np.linspace(0, 1, w)[None, :]
    # window: 1 in the middle, 0 at the edges, steep sides so ghosting stays in a narrow cross
    wy = np.clip(np.sin(np.pi * y) * 1.6, 0, 1)
    wx = np.clip(np.sin(np.pi * x) * 1.6, 0, 1)
    wgt = (wy * wx)[..., None]
    out = a * wgt + rolled * (1 - wgt)
    # restore the contrast the cross-fade lost (variance of a 50/50 mix of uncorrelated patches drops)
    mean = out.reshape(-1, out.shape[-1]).mean(0)
    std_a = a.reshape(-1, a.shape[-1]).std(0)
    std_o = out.reshape(-1, out.shape[-1]).std(0)
    return np.clip((out - mean) * (std_a / np.maximum(std_o, 1e-4)) + mean, 0, 1)


def main():
    root = sys.argv[1]
    raw = os.path.join(root, "raw")
    dst = os.path.join(root, "materials")
    os.makedirs(dst, exist_ok=True)
    names = sorted({f.rsplit("-", 1)[0] for f in os.listdir(raw) if f.endswith(".png")})
    for name in names:
        seeds = sorted(f for f in os.listdir(raw) if f.rsplit("-", 1)[0] == name)
        pick = f"{name}-{PICK[name]}.png" if name in PICK else seeds[0]
        a = np.asarray(Image.open(os.path.join(raw, pick)).convert("RGB"), dtype=np.float32) / 255
        if name != "dust-scratches":
            a = flatten(a, 40 if name == "wall-plaster-dark" else 96)
            grey = (a * np.array([0.2126, 0.7152, 0.0722], dtype=np.float32)).sum(-1, keepdims=True)
            a = grey + (a - grey) * SATURATION
        t = tileable(a)
        img = Image.fromarray((t * 255 + 0.5).astype(np.uint8))
        if name == "dust-scratches":
            # white specks on black -> luminance as alpha on white
            lum = np.asarray(img.convert("L"), dtype=np.float32) / 255
            lum = np.clip((lum - 0.08) / 0.7, 0, 1)
            rgba = np.dstack([np.full_like(lum, 1), np.full_like(lum, 1), np.full_like(lum, 1), lum])
            img = Image.fromarray((rgba * 255).astype(np.uint8), "RGBA")
        img.save(os.path.join(dst, f"{name}-1024.png"), optimize=True)
        n = APP_SIZE.get(name, 256)
        img.resize((n, n), Image.LANCZOS).save(os.path.join(dst, f"{name}-{n}.png"), optimize=True)
        # seam check: 2x2 tiles at 512
        s = img.resize((512, 512), Image.LANCZOS)
        chk = Image.new(s.mode, (1024, 1024))
        for i in range(2):
            for j in range(2):
                chk.paste(s, (i * 512, j * 512))
        chk.save(os.path.join(dst, f"_seamcheck-{name}.png"))
        print(name, pick)


if __name__ == "__main__":
    main()
