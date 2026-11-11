"""Turn the Blender passes (props/<name>/*.npy, linear premultiplied RGBA at 2x) into the app's PNG sprites.

Usage: python build_sprites.py <assets-r3 dir> [name ...]

Per prop, at 2x and 1x (2x2 box-downsampled in linear light):
  <name>@{2x,1x}.png         the object itself (straight-alpha sRGB RGBA); its tinted regions are dark (albedo 0) –
                             only their sheen and worn edges are in it
  <name>-band@{2x,1x}.png    light map of the printed colour band: grey+alpha, alpha = coverage x light^(1/2.2)
  <name>-body@{2x,1x}.png    light map of the tinted body (same encoding)
  <name>-shadow@{2x,1x}.png  the cast + contact shadow on the board / wall (black, alpha = shadow)
How the app tints (one render serves every system colour, no blend modes needed):
  draw shadow, draw base, then for band/body: FillRectangle(system colour) under PushOpacityMask(ImageBrush(light map)).
  Result ~= base + colour x light (exact for a dark base under a pure power-law gamma).
Also writes <name>.json (rects in 1x px, from meta.json) and a preview <name>-preview@2x.png tinted with sample colours.
"""
import json
import os
import sys

import numpy as np
from PIL import Image

PREVIEW_BAND = (0x12 / 255, 0xA0 / 255, 0x5E / 255)  # a system colour (N64 green) for the preview only
PREVIEW_BODY = (0x3A / 255, 0x3D / 255, 0x44 / 255)


def srgb(x):
    x = np.clip(x, 0, 1)
    return np.where(x <= 0.0031308, x * 12.92, 1.055 * np.power(x, 1 / 2.4) - 0.055)


def lin(x):
    x = np.asarray(x, dtype=np.float32)
    return np.where(x <= 0.04045, x / 12.92, np.power((x + 0.055) / 1.055, 2.4))


def half(a):
    h, w = a.shape[:2]
    a = a[: h - h % 2, : w - w % 2]
    return a.reshape(h // 2, 2, w // 2, 2, *a.shape[2:]).mean(axis=(1, 3))


def straight(p):
    a = p[..., 3:4]
    return np.where(a > 1e-5, p[..., :3] / np.maximum(a, 1e-5), 0), a[..., 0]


NEUTRAL_CHROMA = 8   # 8-bit levels: a sprite whose visible pixels never differ more between R, G and B is stored grey


def save_rgba(path, rgb_lin, alpha):
    out = (np.dstack([srgb(rgb_lin), np.clip(alpha, 0, 1)[..., None]]) * 255 + 0.5).astype(np.uint8)
    vis = out[..., 3] > 8
    chroma = (out[..., :3].max(-1).astype(int) - out[..., :3].min(-1))[vis]
    if chroma.size and chroma.max() <= NEUTRAL_CHROMA:
        # R3 QA 4: neutral renders (graphite binder, steel) as grey+alpha – half the bytes, no visible change (the lamps'
        # faint warm/cool cast is ≤ 8 levels); the app decodes LA like RGBA
        grey = np.round(out[..., :3].astype(np.float32) @ np.array([0.2126, 0.7152, 0.0722], np.float32)).astype(np.uint8)
        Image.fromarray(np.dstack([grey, out[..., 3]]), "LA").save(path, optimize=True)
        return
    Image.fromarray(out, "RGBA").save(path, optimize=True)


def save_la(path, grey, alpha):
    out = np.dstack([np.full_like(alpha, grey), np.clip(alpha, 0, 1)])
    Image.fromarray((out * 255 + 0.5).astype(np.uint8), "LA").save(path, optimize=True)


def lightmap(tinted, base):
    """(tinted - base) in premultiplied linear -> mask alpha = coverage x L^(1/2.2) (L = straight light, 0..1)."""
    d = np.clip(tinted[..., :3] - base[..., :3], 0, None)
    lum = d @ np.array([0.2126, 0.7152, 0.0722], dtype=np.float32)
    cov = np.maximum(tinted[..., 3], 1e-5)
    L = np.clip(lum / cov, 0, 1)
    return np.where(tinted[..., 3] > 1e-4, tinted[..., 3] * np.power(L, 1 / 2.2), 0)


def build(root, name, preview=True):
    src = os.path.join(root, "props", name)
    dst = os.path.join(root, "sprites")
    os.makedirs(dst, exist_ok=True)
    meta = json.load(open(os.path.join(src, "meta.json")))
    if meta["kind"] == "motif":
        return build_motif(root, name, meta, preview)
    if meta["kind"] == "case":
        return build_case(root, name, meta, preview)
    passes = {p: np.load(os.path.join(src, p + ".npy")) for p in ("base", "band", "body", "shadow")
              if os.path.exists(os.path.join(src, p + ".npy"))}
    for scale, suffix in ((2, "@2x"), (1, "@1x")):
        P = passes if scale == 2 else {k: half(v) for k, v in passes.items()}
        rgb, a = straight(P["base"])
        save_rgba(os.path.join(dst, f"{name}{suffix}.png"), rgb, a)
        maps = {}
        for k in ("band", "body"):
            if k in P:
                maps[k] = lightmap(P[k], P["base"])
                # render noise / faint bounce outside the tinted region: cut (keeps the PNG small, no grey haze)
                maps[k] = np.where(maps[k] < 0.03, 0, maps[k])
                if maps[k].max() > 0.02:
                    save_la(os.path.join(dst, f"{name}-{k}{suffix}.png"), 1.0, maps[k])
        save_la(os.path.join(dst, f"{name}-shadow{suffix}.png"), 0.0, P["shadow"][..., 3])
        if preview and scale == 2:
            prev = composite(P, maps, PREVIEW_BAND, PREVIEW_BODY)
            Image.fromarray((prev * 255 + 0.5).astype(np.uint8), "RGBA").save(os.path.join(dst, f"_{name}-preview@2x.png"))
    # rects in 1x px
    m1 = {k: ([round(x / 2, 1) for x in v] if isinstance(v, list) else (round(v / 2, 1) if isinstance(v, float) else v))
          for k, v in meta.items()}
    m1["canvas"] = [meta["canvas"][0] // 2, meta["canvas"][1] // 2]
    m1["scale"] = 1
    json.dump(m1, open(os.path.join(dst, f"{name}.json"), "w"), indent=1)
    if meta["kind"] in ("board", "rail", "tag", "reiter", "sign"):
        slices(root, name, meta)
    if name == "rail-notch":
        notch_overlay(root, meta)
    return m1


def notch_overlay(root, meta):
    """R3 QA 3: one notch of the rail as an overlay (the notched render where it differs from the plain rail, feathered,
    cropped 12 px wide around the notch); the app sets it once per Fach on the tiled rail."""
    dst = os.path.join(root, "sprites")
    for suffix, k in (("@2x", 2), ("@1x", 1)):
        a = np.asarray(Image.open(os.path.join(dst, f"rail-notch{suffix}.png")).convert("RGBA")).astype(np.float32) / 255
        b = np.asarray(Image.open(os.path.join(dst, f"rail{suffix}.png")).convert("RGBA")).astype(np.float32) / 255
        h = min(a.shape[0], b.shape[0])
        cx = int(round(meta["notch"][0] * k / 2))
        hw = 6 * k
        a, b = a[:h, cx - hw:cx + hw], b[:h, cx - hw:cx + hw]
        d = np.abs(a[..., :3] - b[..., :3]).max(-1)
        alpha = np.clip(d / 0.05, 0, 1)
        g = alpha.copy()   # grown by one pixel so the notch's soft rim comes along
        g[1:] = np.maximum(g[1:], alpha[:-1])
        g[:-1] = np.maximum(g[:-1], alpha[1:])
        g2 = g.copy()
        g2[:, 1:] = np.maximum(g2[:, 1:], g[:, :-1])
        g2[:, :-1] = np.maximum(g2[:, :-1], g[:, 1:])
        if "lip" in meta:
            # R3 QA 4: only the top lip (+2 px of its shade) – the slot's render noise under the notch read as a black block
            lip_bottom = int(round((meta["lip"][1] + meta["lip"][3]) * k / 2)) + 2 * k
            g2[lip_bottom:] = 0
        out = np.dstack([a[..., :3], g2 * a[..., 3]])
        Image.fromarray((out * 255 + 0.5).astype(np.uint8), "RGBA").save(os.path.join(dst, f"rail-notch-mark{suffix}.png"), optimize=True)


def htile(a, width):
    """A horizontally seamless strip of `width` px from the centre of a (HxWxC uint8): cross-fade with its half roll."""
    h, w = a.shape[:2]
    x0 = (w - width) // 2
    strip = a[:, x0:x0 + width].astype(np.float32)
    rolled = np.roll(strip, width // 2, axis=1)
    x = np.linspace(0, 1, width)[None, :, None]
    wgt = np.clip(np.sin(np.pi * x) * 1.5, 0, 1)
    return (strip * wgt + rolled * (1 - wgt) + 0.5).astype(np.uint8)


def slices(root, name, meta):
    """Long parts: a seamless middle tile (and end caps where the part has ends) for 9-slice / tiled drawing."""
    dst = os.path.join(root, "sprites")
    kind = meta["kind"]
    for suffix, k in (("@2x", 2), ("@1x", 1)):
        for layer in ("", "-shadow", "-band"):
            f = os.path.join(dst, f"{name}{layer}{suffix}.png")
            if not os.path.exists(f):
                continue
            im = Image.open(f)
            a = np.asarray(im)
            fx, fy, fw, fh = [v * k / 2 for v in meta["front"]]
            if kind == "board":
                Image.fromarray(htile(a, 256 * k), im.mode).save(os.path.join(dst, f"{name}{layer}-tile{suffix}.png"), optimize=True)
            elif kind in ("rail", "tag", "reiter", "sign"):
                cap = int(round(meta["slice"] * k)) + int(round(fx))
                Image.fromarray(a[:, :cap], im.mode).save(os.path.join(dst, f"{name}{layer}-left{suffix}.png"), optimize=True)
                r0 = int(round(fx + fw)) - int(round(meta["slice"] * k))
                Image.fromarray(a[:, r0:], im.mode).save(os.path.join(dst, f"{name}{layer}-right{suffix}.png"), optimize=True)
                tw = 128 if kind == "rail" else 16 if kind == "reiter" else 32
                Image.fromarray(htile(a, tw * k), im.mode).save(os.path.join(dst, f"{name}{layer}-tile{suffix}.png"), optimize=True)


def over(dst, rgb, a):
    """sRGB-space 'over' like the app's compositor. dst: HxWx4 straight sRGB float."""
    a = a[..., None]
    out_a = a + dst[..., 3:4] * (1 - a)
    out = (rgb * a + dst[..., :3] * dst[..., 3:4] * (1 - a)) / np.maximum(out_a, 1e-5)
    return np.dstack([out, out_a])


def composite(P, maps, band_rgb, body_rgb):
    """What the app will draw: shadow, base, colour through each light map (all sRGB 'over')."""
    h, w = P["base"].shape[:2]
    c = np.zeros((h, w, 4), np.float32)
    c = over(c, np.zeros((h, w, 3), np.float32), P["shadow"][..., 3])
    rgb, a = straight(P["base"])
    c = over(c, srgb(rgb), a)
    for k, col in (("body", body_rgb), ("band", band_rgb)):
        if k in maps:
            c = over(c, np.broadcast_to(np.array(col, np.float32), (h, w, 3)), maps[k])
    return np.clip(c, 0, 1)


MOTIF_PREVIEW = {"color": (0x12 / 255, 0xA0 / 255, 0x5E / 255), "body": (0x8A / 255, 0x90 / 255, 0x98 / 255),
                 "lift": (0x5F / 255, 0xD8 / 255, 0x9A / 255), "light": (0xE6 / 255, 0xEE / 255, 0xF2 / 255)}


def write_meta(dst, name, meta):
    m1 = {k: ([round(x / 2, 1) for x in v] if isinstance(v, list) and all(isinstance(x, (int, float)) for x in v)
              else (round(v / 2, 1) if isinstance(v, float) else v)) for k, v in meta.items()}
    m1["canvas"] = [meta["canvas"][0] // 2, meta["canvas"][1] // 2]
    m1["scale"] = 1
    json.dump(m1, open(os.path.join(dst, f"{name}.json"), "w"), indent=1)
    return m1


def build_motif(root, name, meta, preview):
    """Motif relief: base + one light map per paint role present (color, body, lift|light) + shadow.
    lit - base is split by the role pass (flat emission of the role coverage)."""
    src = os.path.join(root, "props", name)
    dst = os.path.join(root, "sprites")
    P2 = {p: np.load(os.path.join(src, p + ".npy")) for p in ("base", "lit", "role", "shadow")}
    roles = ["color", "body", meta.get("third", "Lift").lower()]
    for scale, suffix in ((2, "@2x"), (1, "@1x")):
        P = P2 if scale == 2 else {k: half(v) for k, v in P2.items()}
        rgb, a = straight(P["base"])
        save_rgba(os.path.join(dst, f"{name}{suffix}.png"), rgb, a)
        d = np.clip(P["lit"][..., :3] - P["base"][..., :3], 0, None) @ np.array([0.2126, 0.7152, 0.0722], np.float32)
        role = np.clip(P["role"][..., :3], 0, 1)
        tot = np.maximum(role.sum(-1), 1e-4)
        L = np.clip(d / tot, 0, 1)
        maps = {}
        for k, rn in enumerate(roles):
            if role[..., k].max() < 0.05:
                continue
            maps[rn] = role[..., k] * np.power(L, 1 / 2.2)
            save_la(os.path.join(dst, f"{name}-{rn}{suffix}.png"), 1.0, maps[rn])
        save_la(os.path.join(dst, f"{name}-shadow{suffix}.png"), 0.0, P["shadow"][..., 3])
        if preview and scale == 2:
            h, w = a.shape
            c = np.zeros((h, w, 4), np.float32)
            c = over(c, np.full((h, w, 3), 0.11, np.float32), np.ones((h, w), np.float32))
            c = over(c, np.zeros((h, w, 3), np.float32), P["shadow"][..., 3])
            c = over(c, srgb(rgb), a)
            for rn, mp in maps.items():
                c = over(c, np.broadcast_to(np.array(MOTIF_PREVIEW[rn], np.float32), (h, w, 3)), mp)
            Image.fromarray((np.clip(c, 0, 1) * 255 + 0.5).astype(np.uint8), "RGBA").save(os.path.join(dst, f"_{name}-preview@2x.png"))
    meta = dict(meta)
    meta["maps"] = [rn for k, rn in enumerate(roles) if np.clip(P2["role"][..., k], 0, 1).max() >= 0.05]
    # R3 QA 3: a light map of an earlier render whose role the motif no longer has would still be tinted by the app
    # (an old hinge / stair of frames over the new shape) – drop it
    for rn in ("color", "body", "lift", "light"):
        if rn not in meta["maps"]:
            for suffix in ("@1x", "@2x"):
                f = os.path.join(dst, f"{name}-{rn}{suffix}.png")
                if os.path.exists(f):
                    os.remove(f)
    return write_meta(dst, name, meta)


def build_case(root, name, meta, preview):
    """Keep case: frame (window cut out), shade over the cover (black, alpha = 1 - light), sleeve gloss (white), shadow.
    The base pass has the cover black behind the sleeve, so inside the window it IS the sleeve's gloss."""
    src = os.path.join(root, "props", name)
    dst = os.path.join(root, "sprites")
    P2 = {p: np.load(os.path.join(src, p + ".npy")) for p in ("base", "body", "shadow")}
    lum = np.array([0.2126, 0.7152, 0.0722], np.float32)
    for scale, suffix in ((2, "@2x"), (1, "@1x")):
        P = P2 if scale == 2 else {k: half(v) for k, v in P2.items()}
        d = np.clip(P["body"][..., :3] - P["base"][..., :3], 0, None) @ lum
        win = np.clip(d / 0.004, 0, 1)                       # where the cover shows
        rgb, a = straight(P["base"])
        save_rgba(os.path.join(dst, f"{name}{suffix}.png"), rgb, a * (1 - win))
        L = np.clip(d / np.maximum(P["body"][..., 3], 1e-4), 0, None)
        Lref = np.percentile(L[win > 0.99], 97) if (win > 0.99).any() else 1.0
        Ln = np.clip(L / max(Lref, 1e-4), 0, 1)
        save_la(os.path.join(dst, f"{name}-shade{suffix}.png"), 0.0, win * (1 - np.power(Ln, 1 / 2.2)))
        g = np.clip(rgb, 0, None) @ lum
        save_la(os.path.join(dst, f"{name}-gloss{suffix}.png"), 1.0, np.clip(srgb(g), 0, 1) * win * 0.55)   # a film, not a mirror
        save_la(os.path.join(dst, f"{name}-shadow{suffix}.png"), 0.0, P["shadow"][..., 3])
    return write_meta(dst, name, meta)


def main():
    root = sys.argv[1]
    names = sys.argv[2:] or sorted(os.listdir(os.path.join(root, "props")))
    for n in names:
        m = build(root, n)
        print(n, m["canvas"])


if __name__ == "__main__":
    main()
