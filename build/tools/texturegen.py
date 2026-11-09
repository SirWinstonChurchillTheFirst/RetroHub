"""Era textures of the "Fernsehlicht" design (docs/design/DESIGN.md 4.5), generated deterministically.

Standard library only (zlib PNG writer, fixed seed). Run from anywhere:

    python -I build/tools/texturegen.py [output-dir]

Default output: src/RetroHub.App/Assets/Textures/ (the PNGs are committed). Running it twice gives identical bytes.
Colours are white or black with alpha; the app draws them at 4-10 % opacity over the walls (TextureFill, device pixels).
"""
import math
import os
import random
import struct
import sys
import zlib


def write_png(path, w, h, pixels):
    """pixels: list of rows, each a list of (r, g, b, a) tuples."""
    raw = bytearray()
    for row in pixels:
        raw.append(0)  # filter: none
        for r, g, b, a in row:
            raw += bytes((r, g, b, a))

    def chunk(kind, data):
        c = struct.pack('>I', len(data)) + kind + data
        return c + struct.pack('>I', zlib.crc32(kind + data) & 0xFFFFFFFF)

    png = b'\x89PNG\r\n\x1a\n'
    png += chunk(b'IHDR', struct.pack('>IIBBBBB', w, h, 8, 6, 0, 0, 0))
    png += chunk(b'IDAT', zlib.compress(bytes(raw), 9))
    png += chunk(b'IEND', b'')
    with open(path, 'wb') as f:
        f.write(png)


def blank(w, h):
    return [[(0, 0, 0, 0) for _ in range(w)] for _ in range(h)]


WHITE = (255, 243, 224)  # warm paper white (#FFF3E0), never pure white
BLACK = (0, 0, 0)


def coverage_disc(w, h, cx, cy, r, ss=4, wrap=True):
    """Antialiased coverage (0..1) of a disc, tiled seamlessly when wrap is set."""
    cov = [[0.0] * w for _ in range(h)]
    for y in range(h):
        for x in range(w):
            hit = 0
            for sy in range(ss):
                for sx in range(ss):
                    px = x + (sx + 0.5) / ss
                    py = y + (sy + 0.5) / ss
                    best = 1e9
                    for ox in ((-w, 0, w) if wrap else (0,)):
                        for oy in ((-h, 0, h) if wrap else (0,)):
                            best = min(best, math.hypot(px - (cx + ox), py - (cy + oy)))
                    if best <= r:
                        hit += 1
            cov[y][x] = hit / (ss * ss)
    return cov


def korn(out):
    rnd = random.Random(1985)
    w = h = 128
    px = blank(w, h)
    for y in range(h):
        for x in range(w):
            a = rnd.randint(0, 36)
            px[y][x] = (*(WHITE if rnd.random() < 0.5 else BLACK), a)
    write_png(os.path.join(out, 'korn-128.png'), w, h, px)


def scan(out):
    px = blank(4, 3)
    for x in range(4):
        px[0][x] = (*BLACK, 110)
    write_png(os.path.join(out, 'scan-4x3.png'), 4, 3, px)


def lcd(out):
    px = blank(4, 4)
    for i in range(4):
        px[0][i] = (*BLACK, 70)
        px[i][0] = (*BLACK, 70)
    write_png(os.path.join(out, 'lcd-4.png'), 4, 4, px)


def bayer(out):
    px = blank(8, 8)
    for y in range(8):
        for x in range(8):
            if ((x // 2) + (y // 2)) % 2 == 0:
                px[y][x] = (*WHITE, 48)
    write_png(os.path.join(out, 'bayer-8.png'), 8, 8, px)


def dither16(out):
    px = blank(8, 8)
    for y in range(8):
        for x in range(8):
            if (x - y) % 4 == 0 and y % 2 == 0:
                px[y][x] = (*WHITE, 56)
    write_png(os.path.join(out, 'dither16-8.png'), 8, 8, px)


def rillen(out):
    px = blank(4, 6)
    for y in (0, 1):
        for x in range(4):
            px[y][x] = (*WHITE, 26)
    write_png(os.path.join(out, 'rillen-1x6.png'), 4, 6, px)


def poly(out):
    # true triangular lattice: one period is 48 wide and two rows (2 x 42) high
    w, h = 48, 84
    ss = 4
    lines = []
    row = 42
    for k in range(-1, 3):
        y0 = k * row
        off = 0 if k % 2 == 0 else 24
        lines.append(((-48, y0), (96, y0)))
        for vx in range(-48 + off, 97, 48):
            # from a vertex on this row down to the two vertices of the next row
            lines.append(((vx, y0), (vx - 24, y0 + row)))
            lines.append(((vx, y0), (vx + 24, y0 + row)))

    def dist(px_, py_, a, b):
        (ax, ay), (bx, by) = a, b
        dx, dy = bx - ax, by - ay
        t = max(0.0, min(1.0, ((px_ - ax) * dx + (py_ - ay) * dy) / (dx * dx + dy * dy)))
        return math.hypot(px_ - (ax + t * dx), py_ - (ay + t * dy))

    px = blank(w, h)
    for y in range(h):
        for x in range(w):
            hit = 0
            for sy in range(ss):
                for sx in range(ss):
                    qx, qy = x + (sx + 0.5) / ss, y + (sy + 0.5) / ss
                    if any(dist(qx, qy, a, b) <= 0.5 for a, b in lines):
                        hit += 1
            if hit:
                px[y][x] = (*WHITE, round(40 * hit / (ss * ss)))
    write_png(os.path.join(out, 'poly-48.png'), w, h, px)


def halbton(out):
    w = h = 12
    cov = coverage_disc(w, h, 6, 6, 2.5)
    px = [[(*WHITE, round(48 * cov[y][x])) if cov[y][x] > 0 else (0, 0, 0, 0) for x in range(w)] for y in range(h)]
    write_png(os.path.join(out, 'halbton-12.png'), w, h, px)


def maske(out):
    colours = [(255, 64, 48), (64, 230, 96), (72, 120, 255)]
    px = blank(6, 6)
    for y in range(6):
        for x in range(6):
            if y == 5:
                px[y][x] = (*BLACK, 140)
            else:
                px[y][x] = (*colours[x // 2], 110)
    write_png(os.path.join(out, 'maske-6.png'), 6, 6, px)


def lochblech(out):
    w = h = 10
    a = coverage_disc(w, h, 2.5, 2.5, 2.0)
    b = coverage_disc(w, h, 7.5, 7.5, 2.0)
    px = blank(w, h)
    for y in range(h):
        for x in range(w):
            c = max(a[y][x], b[y][x])
            if c > 0:
                px[y][x] = (*BLACK, round(120 * c))
    write_png(os.path.join(out, 'lochblech-10.png'), w, h, px)


def holz(out):
    rnd = random.Random(2600)
    w, h = 256, 32
    tones = [(58, 36, 20), (74, 46, 26), (92, 58, 32), (110, 70, 40), (128, 84, 48)]
    phases = [rnd.uniform(0, math.tau) for _ in range(3)]
    px = blank(w, h)
    for y in range(h):
        for x in range(w):
            t = x / w * math.tau
            v = (math.sin(y * 0.55 + 2.2 * math.sin(t + phases[0]) + 0.8 * math.sin(3 * t + phases[1]))
                 + 0.35 * math.sin(y * 1.9 + math.sin(2 * t + phases[2])))
            idx = int((v + 1.35) / 2.7 * len(tones))
            px[y][x] = (*tones[max(0, min(len(tones) - 1, idx))], 255)
    write_png(os.path.join(out, 'holz-256x32.png'), w, h, px)


def raster(out):
    px = blank(16, 16)
    for i in range(16):
        px[0][i] = (*WHITE, 36)
        px[i][0] = (*WHITE, 36)
    write_png(os.path.join(out, 'raster-16.png'), 16, 16, px)


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    default = os.path.normpath(os.path.join(here, '..', '..', 'src', 'RetroHub.App', 'Assets', 'Textures'))
    out = sys.argv[1] if len(sys.argv) > 1 else default
    os.makedirs(out, exist_ok=True)
    for gen in (korn, scan, lcd, bayer, dither16, rillen, poly, halbton, maske, lochblech, holz, raster):
        gen(out)
    print('textures written to', out)


if __name__ == '__main__':
    main()
