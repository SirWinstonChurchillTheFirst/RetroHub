"""Compose the Konsolen page at 1920x1080 from the rendered sprites (art-direction contact sheet, not app code).

Usage: python contact_konsolen.py <assets-r3 dir> <fonts dir> [out.png]
Lays the page out like SystemsPage.axaml (wall spines 128x460 on the stage board, the focused front 520x600 in slot 3,
the position rail, the "Noch leer" sleeves on the low edge with the bookend) and tints every sprite with its system's
colour through the light maps exactly the way the app is meant to (colour fill under the light map as opacity mask).
Text (codes, counts, names, nav) is set by this script with the app's fonts - the sprites themselves carry none.
Motifs are stand-in ink glyphs (the app prints its MotifCatalog into the debossed square).
"""
import json
import os
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFont

A = sys.argv[1]
FONTS = sys.argv[2]
OUT = sys.argv[3] if len(sys.argv) > 3 else os.path.join(A, "contact-konsolen.png")
SP = os.path.join(A, "sprites")

BG = (0x0B, 0x0C, 0x0E)
SURFACE = (0x1A, 0x1C, 0x20)
TEXT = (0xFF, 0xFF, 0xFF)
TEXT2 = (0xA9, 0xAE, 0xB6)
ACCENT = (0x2F, 0xD3, 0xF5)
INK = (0x17, 0x19, 0x1C)


def hexc(s):
    s = s.lstrip("#")
    return tuple(int(s[i:i + 2], 16) for i in (0, 2, 4))


def mix(a, b, t):
    return tuple(round(x + (y - x) * t) for x, y in zip(a, b))


# id, code, colour, housing, count
WALL = [("nes", "NES", "#C51F1B", "WarmGrey", 4), ("gb", "GB", "#8BAC0F", "WarmGrey", 2),
        ("snes", "SNES", "#6652BE", "WarmGrey", 18), ("n64", "N64", "#12A05E", "Anthracite", 2),
        ("gbc", "GBC", "#8E3BD0", "Berry", 4), ("gba", "GBA", "#5A46D9", "Violet", 4),
        ("gamecube", "GameCube", "#6450CC", "Violet", 3), ("nds", "NDS", "#8FA3B8", "CoolGrey", 4),
        ("wii", "Wii", "#3FC0EE", "White", 3), ("wiiu", "Wii U", "#12A0CC", "White", 3),
        ("3ds", "3DS", "#C21D26", "Anthracite", 5), ("switch", "Switch", "#C41F2A", "Anthracite", 8)]
FOCUS = 3
UNUSED = [("VB", "#C8121C"), ("PS1", "#9AA0B4"), ("PS2", "#2C4FD0"), ("PSP", "#A9B4C8"), ("SMS", "#C42019"),
          ("Mega Drive", "#1B66B2"), ("Game Gear", "#2D9CDB"), ("Mega-CD", "#4A86D0"), ("32X", "#F2AE00"),
          ("Saturn", "#5A5FB8"), ("Dreamcast", "#F26F21"), ("2600", "#C0702A"), ("7800", "#C8243A"),
          ("Lynx", "#F2A21A"), ("PC Engine", "#F05A28"), ("NGP", "#2E5DB8"), ("WonderSwan", "#10A8B5"),
          ("MSX", "#3368A3"), ("Arcade", "#FF2E88"), ("PC", "#C7B48A")]

# R3 v2 colours: housings become neutral (no warm grey, beige, wood); violet/berry keep a trace of their colour
HOUSING = {"Black": (0x1F, 0x21, 0x25), "Anthracite": (0x2B, 0x2E, 0x33), "CoolGrey": (0x45, 0x49, 0x50),
           "WarmGrey": (0x3C, 0x3F, 0x45), "White": (0xA9, 0xAD, 0xB3), "Beige": (0x8A, 0x8E, 0x94), "Wood": (0x3C, 0x3F, 0x45)}


def body_colour(housing, colour):
    if housing in ("Violet", "Berry"):
        return mix((0x2B, 0x2E, 0x33), colour, 0.32)
    return HOUSING[housing]


def font(name, size):
    paths = {"cond": "ArchivoCondensed/ArchivoCondensed-Bold.ttf", "poster": "ArchivoExpanded/ArchivoExpanded-ExtraBold.ttf",
             "ui": "ArchivoSemiCondensed/ArchivoSemiCondensed-SemiBold.ttf", "uim": "ArchivoSemiCondensed/ArchivoSemiCondensed-Medium.ttf",
             "num": "ArchivoSemiExpanded/ArchivoSemiExpanded-Bold.ttf", "read": "Atkinson/AtkinsonHyperlegibleNext-Medium.ttf"}
    return ImageFont.truetype(os.path.join(FONTS, paths[name]), size)


_cache = {}


def load(name):
    if name not in _cache:
        p = os.path.join(SP, name + ".png")
        _cache[name] = Image.open(p).convert("RGBA") if os.path.exists(p) else None
    return _cache[name]


def meta(name):
    return json.load(open(os.path.join(SP, name + ".json")))


def fill_through(canvas, mask_img, colour, pos):
    """Colour through a light map (grey+alpha): exactly FillRectangle(colour) under PushOpacityMask(lightmap)."""
    a = mask_img.getchannel("A")
    layer = Image.new("RGBA", mask_img.size, colour + (255,))
    layer.putalpha(a)
    canvas.alpha_composite(layer, pos)


def sprite(canvas, name, front_xy, band=None, body=None, shadow=True):
    """Draw sprite <name>@1x with its front rect's top-left at front_xy (shadow, base, body tint, band tint)."""
    m = meta(name + "@1x" if os.path.exists(os.path.join(SP, name + "@1x.json")) else name)
    fx, fy = m["front"][0], m["front"][1]
    pos = (round(front_xy[0] - fx), round(front_xy[1] - fy))
    if shadow and load(f"{name}-shadow@1x"):
        canvas.alpha_composite(load(f"{name}-shadow@1x"), dest=clip_pos(canvas, pos), source=clip_src(pos))
    paste(canvas, load(f"{name}@1x"), pos)
    if body and load(f"{name}-body@1x"):
        fill_masked(canvas, load(f"{name}-body@1x"), body, pos)
    if band and load(f"{name}-band@1x"):
        fill_masked(canvas, load(f"{name}-band@1x"), band, pos)
    return m, pos


def clip_pos(canvas, pos):
    return (max(0, pos[0]), max(0, pos[1]))


def clip_src(pos):
    return (max(0, -pos[0]), max(0, -pos[1]))


def paste(canvas, img, pos):
    canvas.alpha_composite(img, dest=clip_pos(canvas, pos), source=clip_src(pos))


def fill_masked(canvas, mask_img, colour, pos):
    layer = Image.new("RGBA", mask_img.size, colour + (255,))
    layer.putalpha(mask_img.getchannel("A"))
    paste(canvas, layer, pos)


def hslice(name, width, slice_px):
    """9-slice horizontally (left cap, tiled middle, right cap) a part to `width` (1x canvas px incl. its padding)."""
    out = {}
    for layer in ("", "-shadow"):
        l, r, t = load(f"{name}{layer}-left@1x"), load(f"{name}{layer}-right@1x"), load(f"{name}{layer}-tile@1x")
        img = Image.new("RGBA", (width, l.height), (0, 0, 0, 0))
        x = l.width
        while x < width - r.width:
            img.alpha_composite(t.crop((0, 0, min(t.width, width - r.width - x), t.height)), (x, 0))
            x += t.width
        img.alpha_composite(l, (0, 0))
        img.alpha_composite(r, (width - r.width, 0))
        out[layer] = img
    return out


def vtext(canvas, text, fnt, colour, box, top_pad=10, alpha=235):
    """Code turned -90 deg (reads bottom to top), centred across the box, hanging from its top."""
    l, t, r, b = fnt.getbbox(text)
    tw, th = r - l, b - t
    im = Image.new("RGBA", (tw + 4, th + 4), (0, 0, 0, 0))
    ImageDraw.Draw(im).text((2 - l, 2 - t), text, font=fnt, fill=colour + (alpha,))
    im = im.rotate(90, expand=True, resample=Image.BICUBIC)
    x = round(box[0] + (box[2] - im.width) / 2)
    y = round(box[1] + top_pad)
    if im.height > box[3] - 2 * top_pad:
        im = im.crop((0, im.height - (box[3] - 2 * top_pad), im.width, im.height))
    canvas.alpha_composite(im, (x, y))


def glyph(d, box, kind, colour):
    """Stand-in motif: a generic ink shape (the app prints its own motif here)."""
    x, y, w, h = box
    c = colour + (230,)
    cx, cy = x + w / 2, y + h / 2
    s = min(w, h) * 0.62
    if kind == "disc":
        d.ellipse((cx - s / 2, cy - s / 2, cx + s / 2, cy + s / 2), outline=c, width=max(3, round(s * 0.12)))
        d.ellipse((cx - s * 0.1, cy - s * 0.1, cx + s * 0.1, cy + s * 0.1), fill=c)
    elif kind == "hand":
        d.rectangle((cx - s * 0.33, cy - s / 2, cx + s * 0.33, cy + s / 2), fill=c)
        d.rectangle((cx - s * 0.22, cy - s * 0.38, cx + s * 0.22, cy - s * 0.02), fill=INK + (255,))
    else:
        d.rectangle((cx - s / 2, cy - s * 0.42, cx + s / 2, cy + s * 0.42), fill=c)
        d.rectangle((cx - s * 0.36, cy - s * 0.26, cx + s * 0.36, cy + s * 0.30), fill=INK + (255,))


MOTIF = {"nes": "cart", "gb": "hand", "snes": "cart", "n64": "cart", "gbc": "hand", "gba": "hand", "gamecube": "disc",
         "nds": "hand", "wii": "disc", "wiiu": "hand", "3ds": "hand", "switch": "hand"}
# the app's MotifCatalog motif and "Zweitfarbe" per system (SystemPalette)
RELIEF = {"nes": ("Cart8", "#A8A39A"), "gb": ("HandheldTall", "#306230"), "snes": ("Cart16", "#BDB7AD"),
          "n64": ("Cart64", "#4A4A50"), "gbc": ("HandheldTallDouble", "#2FB8A6"), "gba": ("HandheldWide", "#A9A2E8"),
          "gamecube": ("MiniDisc", "#F29A3B"), "nds": ("Clamshell", "#3C4654"), "wii": ("Remote", "#E6EEF2"),
          "wiiu": ("Tablet", "#F0F0F0"), "3ds": ("ClamshellWide", "#2B2B30"), "switch": ("Dock", "#2E2E33")}


def rel_lum(c):
    def ch(v):
        v /= 255
        return v / 12.92 if v <= 0.04045 else ((v + 0.055) / 1.055) ** 2.4
    r, g, b = (ch(float(x)) for x in c)
    return 0.2126 * r + 0.7152 * g + 0.0722 * b


def contrast(a, b):
    la, lb = sorted((rel_lum(a), rel_lum(b)), reverse=True)
    return (la + 0.05) / (lb + 0.05)


def lift(colour, wall):
    import colorsys
    h, l, sat = colorsys.rgb_to_hls(*(x / 255 for x in colour))
    l = max(l, 0.55)
    while True:
        c = tuple(round(x * 255) for x in colorsys.hls_to_rgb(h, l, sat))
        if contrast(c, wall) >= 3.2 or l >= 0.86:
            return c
        l += 0.02


def motif_relief(canvas, motif, box, colour, second, wall):
    """The rendered relief of <motif> (sprites motif-<Motif>@2x) scaled into box (x, y, size): shadow, base, then each
    paint role's colour through its light map (Color = Farbe, Body = Zweitfarbe lifted on dark walls, Lift, Light)."""
    name = f"motif-{motif}"
    m = meta(name)
    x, y, size = box
    k = size / m["front"][2]                       # 1x front is 160 px; the 2x sprite is scaled by k / 2
    W2 = round(m["canvas"][0] * k)
    H2 = round(m["canvas"][1] * k)
    pos = (round(x - m["front"][0] * k), round(y - m["front"][1] * k))
    body = hexc(second) if contrast(hexc(second), wall) >= 1.6 else mix(wall, (0xF2, 0xF3, 0xF5), 0.38)
    tints = {"color": colour, "body": body, "lift": lift(colour, wall), "light": (0xE6, 0xEE, 0xF2)}

    def L(n):
        p = os.path.join(SP, f"{n}@2x.png")
        return Image.open(p).convert("RGBA").resize((W2, H2), Image.LANCZOS) if os.path.exists(p) else None
    paste(canvas, L(f"{name}-shadow"), pos)
    paste(canvas, L(name), pos)
    for role in ("body", "color", "lift", "light"):
        mk = L(f"{name}-{role}")
        if mk is not None:
            fill_masked(canvas, mk, tints[role], pos)


COVERS = [((0x1F, 0x4E, 0x8C), (0xE8, 0xB4, 0x2C)), ((0x8C, 0x1F, 0x2A), (0xF0, 0xEC, 0xE2)), ((0x14, 0x6B, 0x4A), (0xF2, 0x7A, 0x1E))]


def cover_art(w, h, k):
    """A placeholder cover (abstract shapes, no text) - the app shows the real box art here."""
    a, b = COVERS[k % len(COVERS)]
    im = Image.new("RGBA", (w, h), a + (255,))
    d = ImageDraw.Draw(im)
    if k % 3 == 0:
        d.polygon([(0, h * 0.62), (w, h * 0.38), (w, h), (0, h)], fill=b + (255,))
        d.ellipse((w * 0.55, h * 0.10, w * 0.92, h * 0.10 + w * 0.37), fill=(0xF4, 0xF2, 0xEC, 255))
    elif k % 3 == 1:
        for i in range(5):
            d.rectangle((0, h * (0.18 + i * 0.13), w, h * (0.18 + i * 0.13) + 5), fill=b + (255,))
        d.ellipse((w * 0.2, h * 0.30, w * 0.8, h * 0.30 + w * 0.6), fill=(0x16, 0x16, 0x1A, 255))
    else:
        d.rectangle((w * 0.12, h * 0.12, w * 0.88, h * 0.56), fill=b + (255,))
        d.rectangle((w * 0.12, h * 0.64, w * 0.60, h * 0.70), fill=(0xF4, 0xF2, 0xEC, 255))
    return im


def game_case(canvas, x, bottom, k):
    """Keep case (sprite 'case', 1x 120 x 150) standing on y = bottom: shadow, cover, shade, gloss, frame."""
    m = meta("case")
    fx, fy, fw, fh = m["front"]
    pos = (round(x - fx), round(bottom - fh - fy))
    paste(canvas, load("case-shadow@1x"), pos)
    wx, wy, ww, wh = m["window"]
    paste(canvas, cover_art(round(ww) + 2, round(wh) + 2, k), (pos[0] + round(wx) - 1, pos[1] + round(wy) - 1))
    for layer in ("case-shade@1x", "case-gloss@1x", "case@1x"):
        paste(canvas, load(layer), pos)


def wall_background(W, H):
    t = np.asarray(Image.open(os.path.join(A, "materials", "wall-plaster-dark-256.png")).convert("L"), dtype=np.float32)
    t = (t - t.mean()) / max(t.std(), 1e-3)
    reps = (H // t.shape[0] + 1, W // t.shape[1] + 1)
    tt = np.tile(t, reps)[:H, :W]
    base = np.array(BG, np.float32)[None, None, :] + 1.6 * tt[..., None]
    return Image.fromarray(np.clip(base, 0, 255).astype(np.uint8), "RGB").convert("RGBA")


def main():
    W, H = 1920, 1080
    cv = wall_background(W, H)
    d = ImageDraw.Draw(cv)
    # --- top bar (as today, recoloured)
    d.rectangle((48, 31, 56, 39), fill=ACCENT)
    d.text((66, 22), "RetroHub", font=font("num", 22), fill=TEXT)
    x = 250
    for i, (n, active) in enumerate((("Start", False), ("Bibliothek", False), ("Konsolen", True))):
        d.text((x, 27), str(i + 1), font=font("uim", 15), fill=TEXT2)
        d.text((x + 16, 22), n, font=font("ui", 20), fill=TEXT if active else TEXT2)
        if active:
            tw = d.textlength(n, font=font("ui", 20))
            d.rectangle((x, 52, x + 16 + tw, 54), fill=ACCENT)
        x += 16 + d.textlength(n, font=font("ui", 20)) + 40
    d.text((1574, 26), "Kein Controller", font=font("uim", 16), fill=TEXT2)
    d.text((1695, 22), "10:36", font=font("num", 20), fill=TEXT)

    stage_y = 88 + 600  # the spines and the front stand on y = 688
    # --- stage board first (its top face shows behind the feet)
    bm = meta("board-stage")
    tile, sh = load("board-stage-tile@1x"), load("board-stage-shadow-tile@1x")
    by = round(stage_y - bm["front"][1])
    for xx in range(0, W, tile.width):
        cv.alpha_composite(sh, (xx, by))
    for xx in range(0, W, tile.width):
        cv.alpha_composite(tile, (xx, by))

    # --- wall: spines left/right of the front (slot 3 at x = 372)
    fx0 = 372
    xs = []
    for i in range(len(WALL)):
        if i < FOCUS:
            xs.append(fx0 - (FOCUS - i) * 136)
        elif i == FOCUS:
            xs.append(fx0)
        else:
            xs.append(fx0 + 520 + 8 + (i - FOCUS - 1) * 136)
    # shadows of all first so neighbours sit on them
    order = list(range(len(WALL)))
    for i in order:
        sid, code, col, housing, count = WALL[i]
        colour = hexc(col)
        if i == FOCUS:
            continue
        v = (sum(map(ord, sid)) % 3)
        name = f"spine-l{v}"
        m, pos = sprite(cv, name, (xs[i], stage_y - 460), band=colour, body=body_colour(housing, colour))
        ox, oy = pos
        lab = m["label"]
        vtext(cv, code, font("cond", 30), INK, (ox + lab[0], oy + lab[1], lab[2], lab[3]), top_pad=12)
        mb = m["motif"]
        inset = 6
        motif_relief(cv, RELIEF[sid][0], (ox + mb[0] + inset, oy + mb[1] + inset, mb[2] - 2 * inset), colour, RELIEF[sid][1],
                     body_colour(housing, colour))
        # count tag (9-sliced metal plate)
        f = font("num", 21)
        tw = d.textlength(str(count), font=f)
        tm = meta("tag-l@1x" if os.path.exists(os.path.join(SP, "tag-l@1x.json")) else "tag-l")
        pw = max(48, round(tw + 30))
        parts = hslice("tag-l", pw + round(tm["front"][0] * 2), tm["slice"])
        tx = round(ox + m["tagCenterX"] - (pw + tm["front"][0] * 2) / 2)
        ty = round(oy + m["tagCenterY"] - tm["canvas"][1] / 2)
        cv.alpha_composite(parts["-shadow"], (tx, ty))
        cv.alpha_composite(parts[""], (tx, ty))
        ImageDraw.Draw(cv).text((tx + (pw + tm["front"][0] * 2 - tw) / 2, ty + tm["front"][1] + 2), str(count), font=f, fill=(236, 238, 240))

    # --- the focused front
    sid, code, col, housing, count = WALL[FOCUS]
    colour = hexc(col)
    m, (ox, oy) = sprite(cv, "front-l", (fx0, 88), band=colour, body=body_colour(housing, colour))
    d = ImageDraw.Draw(cv)
    d.text((fx0 + 46, 88 + 42), "Nintendo 64", font=font("poster", 44), fill=TEXT)
    d.text((fx0 + 48, 88 + 104), "Nintendo, 1996", font=font("read", 20), fill=(0xC8, 0xCC, 0xD2))
    # the compartment is in the sprite now; the game boxes stand on its inner board (keep cases, placeholder covers)
    board_top = 88 + 376
    for k, bx in enumerate((fx0 + 52, fx0 + 52 + 136)):
        game_case(cv, bx, board_top, k)
    d = ImageDraw.Draw(cv)
    d.text((fx0 + 46, 88 + 600 - 78), "2 Spiele", font=font("poster", 40), fill=TEXT)
    motif_relief(cv, RELIEF[sid][0], (fx0 + 520 - 166, 88 + 600 - 176, 136), colour, RELIEF[sid][1], body_colour(housing, colour))
    # focus: a thin cyan frame (no glow)
    d.rectangle((fx0 - 6, 88 - 6, fx0 + 520 + 5, 88 + 600 + 2), outline=ACCENT, width=3)

    # --- position rail
    rail_y = 688 + 24 + 44 - 3 - 18
    rx, rw = 96, 1920 - 96 - 220 - 48 - 96
    rm = meta("rail")
    parts = hslice("rail", rw + round(rm["front"][0] * 2), rm["slice"])
    px, py = round(rx - rm["front"][0]), round(rail_y - rm["front"][1])
    cv.alpha_composite(parts["-shadow"], (px, py))
    cv.alpha_composite(parts[""], (px, py))
    d = ImageDraw.Draw(cv)
    n = len(WALL)
    tick_x = [rx + 18 + i * (rw - 36) / (n - 1) for i in range(n)]
    for i, tx in enumerate(tick_x):
        c = hexc(WALL[i][2])
        d.rectangle((round(tx) - 1, rail_y + 2, round(tx) + 1, rail_y + 5), fill=c + (255,))
        d.rectangle((round(tx) - 1, rail_y + 14, round(tx) + 1, rail_y + 16), fill=c + (255,))
        if i < n - 1:
            for j in (1, 2):
                mx = round(tx + j * (tick_x[1] - tick_x[0]) / 3)
                d.rectangle((mx, rail_y + 3, mx, rail_y + 5), fill=(200, 205, 210, 150))
    sm = meta("slider")
    sx = round(tick_x[FOCUS] - sm["front"][2] / 2 - sm["front"][0])
    sy = round(rail_y + 9 - sm["front"][3] / 2 - sm["front"][1])
    paste(cv, load("slider-shadow@1x"), (sx, sy))
    paste(cv, load("slider@1x"), (sx, sy))
    fill_masked(cv, load("slider-band@1x"), colour, (sx, sy))
    d = ImageDraw.Draw(cv)
    d.text((round(tick_x[FOCUS]) - 14, rail_y - 30), "N64", font=font("cond", 17), fill=TEXT)
    # position counter on a wide tag
    tm = meta("tag-l")
    cw = 150
    parts = hslice("tag-l", cw + round(tm["front"][0] * 2), tm["slice"])
    cx0, cy0 = 1920 - 96 - cw - round(tm["front"][0]), rail_y - 6
    cv.alpha_composite(parts["-shadow"], (cx0, cy0))
    cv.alpha_composite(parts[""], (cx0, cy0))
    d.text((cx0 + 26, cy0 + 9), "Fach 4 von 12", font=font("uim", 16), fill=(236, 238, 240))

    # --- "Noch leer": sleeves on the low edge
    base_y = 788 + 168
    em = meta("board-edge")
    tile, sh = load("board-edge-tile@1x"), load("board-edge-shadow-tile@1x")
    by = round(base_y - em["front"][1])
    for xx in range(0, W, tile.width):
        cv.alpha_composite(sh, (xx, by))
    for xx in range(0, W, tile.width):
        cv.alpha_composite(tile, (xx, by))
    x = 96
    main_cv = cv
    cv = Image.new("RGBA", main_cv.size, (0, 0, 0, 0))   # the sleeves go on a layer above the bookend's foot
    for i, (code, col) in enumerate(UNUSED):
        colour = hexc(col)
        seed = sum(ord(ch) * (k + 3) for k, ch in enumerate(code))
        if i == len(UNUSED) - 1:
            name = "sleeve-lflat"
        elif i > 0 and seed % 5 == 0:
            name = "sleeve-llean"
        else:
            name = f"sleeve-l{seed % 4}"
        m = meta(name)
        fw, fh = m["front"][2], m["front"][3]
        if name == "sleeve-llean":
            x += 10
        m, (ox, oy) = sprite(cv, name, (x, base_y - fh), band=colour, body=mix((0x2E, 0x31, 0x36), colour, 0.16))
        lab = m["label"]
        f = font("cond", 14)
        if name == "sleeve-lflat":
            ImageDraw.Draw(cv).text((ox + lab[0] + 8, oy + lab[1] + (lab[3] - 18) / 2), code, font=f, fill=INK + (235,))
        elif name == "sleeve-llean":
            # turned with the sleeve: draw upright into a patch, rotate by the lean
            patch = Image.new("RGBA", (round(lab[2]), 160), (0, 0, 0, 0))
            vtext(patch, code, f, INK, (0, 0, lab[2] * 0.82, 160), top_pad=14)
            patch = patch.rotate(6.5, expand=True, resample=Image.BICUBIC, center=(0, 0))
            cv.alpha_composite(patch, (round(ox + lab[0] + 2), round(oy + lab[1] - 2)))
        else:
            vtext(cv, code, f, INK, (ox + lab[0], oy + lab[1], lab[2], lab[3]), top_pad=13)
        x += fw + 2 + (m["canvas"][0] - m["front"][0] - fw - 8 if name == "sleeve-llean" else 0)
    bm = meta("bookend")
    bpos = (round(x + 6 - bm["foot"][0]), round(base_y - bm["foot"][1]))
    paste(main_cv, load("bookend-shadow@1x"), bpos)
    paste(main_cv, load("bookend@1x"), bpos)
    main_cv.alpha_composite(cv)
    cv = main_cv
    d = ImageDraw.Draw(cv)
    d.text((98, base_y + 16), "Noch leer", font=font("uim", 15), fill=TEXT2)

    # --- footer
    d.line((0, 1024, W, 1024), fill=(0x22, 0x24, 0x28), width=1)
    d.text((48, 1041), "Konsolen › Nintendo 64", font=font("uim", 16), fill=TEXT2)
    hx = 1418
    for key, label in (("Enter", "Öffnen"), ("Esc", "Zurück"), ("F3", "Suche"), ("F10", "Menü")):
        kw = d.textlength(key, font=font("ui", 14)) + 16
        d.rectangle((hx, 1040, hx + kw, 1063), fill=(0xE8, 0xEA, 0xED))
        d.text((hx + 8, 1043), key, font=font("ui", 14), fill=INK)
        d.text((hx + kw + 10, 1041), label, font=font("ui", 16), fill=TEXT)
        hx += kw + 10 + d.textlength(label, font=font("ui", 16)) + 28
    cv.convert("RGB").save(OUT)
    print(OUT)


main()
