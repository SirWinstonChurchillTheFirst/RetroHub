"""Installer artwork for RetroHub-Setup.exe (run once; the results are checked in next to this script):
- wizard-<w>x<h>.bmp   the banner on the welcome and finish page, at every DPI size (100-200 %)
- small-<n>x<n>.png    the small picture at the top right of the other pages
- setup.ico            the icon of RetroHub-Setup.exe
RetroHub look ("Fernsehlicht", docs/design/DESIGN.md): near-black #0B0C0E, white, Standby-Cyan #2FD3F5, opaque
neutrals, no glow, no gradients. Only simple shapes (a TV with a library grid, its standby light) - no logos, no
console or game artwork, no trademarks. Fonts: Archivo Expanded + Atkinson Hyperlegible Next (SIL OFL, the app's own
fonts in src/RetroHub.App/Assets/Fonts).
Run:  python build/installer/art/make_art.py   (needs Pillow)"""
import os
from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
FONTS = os.path.join(HERE, "..", "..", "..", "src", "RetroHub.App", "Assets", "Fonts")
TITLE_FONT = os.path.join(FONTS, "ArchivoExpanded", "ArchivoExpanded-ExtraBold.ttf")
SUB_FONT = os.path.join(FONTS, "Atkinson", "AtkinsonHyperlegibleNext-SemiBold.ttf")

# design tokens (src/RetroHub.App/Themes/Tokens.axaml)
ROOM_DEEP = (6, 7, 8)
BASE = (11, 12, 14)
SURFACE = (26, 28, 32)
SURFACE_HI = (36, 39, 45)
LINE = (52, 56, 64)
TEXT1 = (255, 255, 255)
TEXT2 = (195, 200, 209)
TEXT3 = (160, 167, 178)
ACCENT = (47, 211, 245)

S = 4  # everything is drawn at 4x and scaled down (clean edges without anti-aliasing tricks)


def font(path, size):
    return ImageFont.truetype(path, size * S)


def fit_font(path, text, max_width, start):
    """Largest size <= start at which text fits max_width (both in 1x pixels)."""
    size = start
    while size > 8:
        f = font(path, size)
        if f.getlength(text) <= max_width * S:
            return f
        size -= 1
    return font(path, 8)


def tracked(d, xy, text, f, fill, tracking):
    """Text with extra letter spacing (tracking in 1x pixels)."""
    x, y = xy
    for ch in text:
        d.text((x, y), ch, font=f, fill=fill)
        x += f.getlength(ch) + tracking * S


def tv(d, box, radius, line, led=True):
    """A TV: housing (surface, 1 line), dark screen with scanlines, the standby light bottom right."""
    x0, y0, x1, y1 = [v * S for v in box]
    r = radius * S
    d.rounded_rectangle((x0, y0, x1, y1), radius=r, fill=SURFACE, outline=LINE, width=max(1, line * S))
    pad = max(2, (x1 - x0) // 14)
    bottom = max(pad * 2, (y1 - y0) // 7)
    sx0, sy0, sx1, sy1 = x0 + pad, y0 + pad, x1 - pad, y1 - bottom
    d.rounded_rectangle((sx0, sy0, sx1, sy1), radius=max(1, r // 2), fill=ROOM_DEEP)
    if led:
        lr = max(2 * S, (y1 - sy1) // 5)
        cx, cy = x1 - pad - lr * 2, (sy1 + y1) // 2
        d.rounded_rectangle((cx - lr * 2, cy - lr // 2, cx + lr, cy + lr // 2), radius=lr // 2, fill=ACCENT)
    return sx0, sy0, sx1, sy1


def banner(W=328, H=628):
    """Drawn for the 200 % size (328x628); the other sizes are scaled from it."""
    im = Image.new("RGB", (W * S, H * S), BASE)
    d = ImageDraw.Draw(im)

    # floor: the lower third is the deeper room colour, separated by one hairline
    floor = 452
    d.rectangle((0, floor * S, W * S, H * S), fill=ROOM_DEEP)
    d.line((0, floor * S, W * S, floor * S), fill=LINE, width=S)

    # wordmark, the standby bar, the subtitle
    left = 28
    title = fit_font(TITLE_FONT, "RetroHub", W - 2 * left, 46)
    d.text((left * S, 54 * S), "RetroHub", font=title, fill=TEXT1)
    d.rounded_rectangle((left * S, 128 * S, (left + 44) * S, 134 * S), radius=3 * S, fill=ACCENT)
    sub = font(SUB_FONT, 13)
    tracked(d, (left * S, 148 * S), "RETRO-GAMING-LAUNCHER", sub, TEXT3, 1.6)

    # the TV standing on the floor line, showing a game library: a grid of tiles, one selected (cyan outline)
    tx0, ty0, tx1, ty1 = 34, 238, W - 34, 430
    sx0, sy0, sx1, sy1 = tv(d, (tx0, ty0, tx1, ty1), 10, 1)
    for y in range(sy0 + 3 * S, sy1, 4 * S):  # scanlines
        d.line((sx0 + 2 * S, y, sx1 - 2 * S, y), fill=(13, 15, 18), width=S)
    cols, rows, gap = 4, 2, 7 * S
    gw = (sx1 - sx0) - 2 * 16 * S
    gh = (sy1 - sy0) - 2 * 18 * S
    tw = (gw - gap * (cols - 1)) // cols
    th = (gh - gap * (rows - 1)) // rows
    for r in range(rows):
        for c in range(cols):
            x = sx0 + 16 * S + c * (tw + gap)
            y = sy0 + 18 * S + r * (th + gap)
            d.rounded_rectangle((x, y, x + tw, y + th), radius=2 * S, fill=SURFACE_HI)
            # a label line under each "cover"
            d.rectangle((x + 4 * S, y + th - 9 * S, x + tw - 10 * S, y + th - 6 * S), fill=LINE)
            if (r, c) == (0, 1):
                d.rounded_rectangle((x - 3 * S, y - 3 * S, x + tw + 3 * S, y + th + 3 * S), radius=4 * S,
                                    outline=ACCENT, width=2 * S)
    # feet on the floor
    for fx in (tx0 + 40, tx1 - 56):
        d.rounded_rectangle((fx * S, ty1 * S, (fx + 16) * S, (floor + 1) * S), radius=2 * S, fill=SURFACE_HI)

    # a quiet row of "cartridge spines" on the floor (neutral, no system colours, no names)
    x = left
    for i, w in enumerate([18, 12, 22, 14, 18, 12, 20, 16, 14]):
        h = 96 + (i * 37) % 44
        y1 = H - 40
        d.rounded_rectangle((x * S, (y1 - h) * S, (x + w) * S, y1 * S), radius=2 * S,
                            fill=SURFACE if i % 2 else SURFACE_HI)
        d.rectangle(((x + 3) * S, (y1 - h + 10) * S, (x + w - 3) * S, (y1 - h + 13) * S), fill=LINE)
        x += w + 5
    return im.resize((W, H), Image.LANCZOS)


def small(n):
    """Rounded near-black square with a small white-edged TV and its cyan standby light (transparent outside)."""
    im = Image.new("RGBA", (n * S, n * S), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    d.rounded_rectangle((0, 0, n * S - 1, n * S - 1), radius=n * S // 5, fill=BASE + (255,))
    m = n * 0.18
    box = (m, n * 0.24, n - m, n - n * 0.22)
    x0, y0, x1, y1 = [v * S for v in box]
    r = n * S * 0.08
    d.rounded_rectangle((x0, y0, x1, y1), radius=r, fill=SURFACE + (255,), outline=TEXT1 + (255,),
                        width=max(S, int(n * S * 0.045)))
    pad = (x1 - x0) * 0.13
    sy1 = y1 - (y1 - y0) * 0.27
    d.rounded_rectangle((x0 + pad, y0 + pad, x1 - pad, sy1), radius=r / 2, fill=ROOM_DEEP + (255,))
    lh = max(S, (y1 - sy1) * 0.34)
    cy = (sy1 + y1) / 2
    d.rounded_rectangle((x1 - pad - lh * 3.2, cy - lh / 2, x1 - pad, cy + lh / 2), radius=lh / 2, fill=ACCENT + (255,))
    return im.resize((n, n), Image.LANCZOS)


def main():
    big = banner()
    # 100 / 125 / 150 / 175 / 200 % of the modern wizard's 164x314
    for w, h in [(164, 314), (205, 393), (246, 471), (287, 550), (328, 628)]:
        out = big if (w, h) == big.size else big.resize((w, h), Image.LANCZOS)
        out.save(os.path.join(HERE, f"wizard-{w}x{h}.bmp"))
    for n in (55, 69, 83, 96, 110):
        small(n).save(os.path.join(HERE, f"small-{n}x{n}.png"))
    icon = small(256)
    icon.save(os.path.join(HERE, "setup.ico"), sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
    print("ok")


if __name__ == "__main__":
    main()
