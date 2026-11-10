"""Rasterise the app's category motifs (src/RetroHub.App/Theming/MotifCatalog.cs) into relief source maps for Blender.

Usage: python motif_maps.py <repo root> <assets-r3 dir> [--res 512]

The motifs stay defined in ONE place (the C# catalog): this script reads the `Create` switch, evaluates each motif's
part list with small Python stand-ins for P/C/B/X/Disc/Rings/Dot/Group/Perforation/Cross/KeyBlock, and paints the
parts in order (painter's algorithm, like the app) on the 120 x 120 grid at <res> px (4x supersampled).
Per motif it writes <dir>/motifs-src/<Motif>.npz with
  role   float32 [res,res,5]  coverage of the visible paint per pixel: Color, Body, Lift|Light, Cut, (unused 0)
  height float32 [res,res]    relief height in grid units (a part standing on the ground rises 4.0, a part on a part
                               rises 1.5 more, a cut sinks 2.0 into what it cuts; slightly rounded edges)
and a flat preview <dir>/motifs-src/_<Motif>.png. Channel 2 holds Lift for most motifs and Light for TwinLens
(no motif has both; meta says which).
"""
import math
import os
import re
import sys

import numpy as np
from PIL import Image, ImageDraw

GRID = 120.0
STAND, STACK, SINK = 4.0, 1.5, 2.0


# ------------------------------------------------------------------------------------------- C# stand-ins (geometry)

def P(d):
    return ("path", d)


def Part(geom, paint, stroke=0):
    return {"geom": geom, "paint": paint, "stroke": float(stroke)}


def C(d):
    return Part(P(d), "Color")


def B(d):
    return Part(P(d), "Body")


def X(d):
    return Part(P(d), "Cut")


def Ellipse(rect):
    x, y, w, h = rect
    return ("ellipse", (x, y, w, h))


def Dot(cx, cy, r):
    return ("ellipse", (cx - r, cy - r, 2 * r, 2 * r))


def Disc(cx, cy, r, hole):
    kids = [Dot(cx, cy, r)] + ([Dot(cx, cy, hole)] if hole > 0 else [])
    return ("evenodd", kids)


def Rings(cx, cy, *radii):
    return ("evenodd", [Dot(cx, cy, r) for r in radii])


def Group(*parts):
    return ("union", list(parts))


def Perforation(x0, y0, pitch, r):
    return ("union", [Dot(x0 + c * pitch, y0 + rw * pitch, r) for rw in range(3) for c in range(3)])


def Cross(cx, cy, a, t):
    return (f"M{cx - t / 2},{cy - a} H{cx + t / 2} V{cy - t / 2} H{cx + a} V{cy + t / 2} H{cx + t / 2} V{cy + a} "
            f"H{cx - t / 2} V{cy + t / 2} H{cx - a} V{cy - t / 2} H{cx - t / 2} Z")


def KeyBlock():
    g = []
    for row in range(3):
        for col in range(5):
            x, y = 10 + col * 21, 60 + row * 20
            g.append(P(f"M{x + 3},{y} H{x + 11} L{x + 14},{y + 3} V{y + 11} L{x + 11},{y + 14} H{x + 3} L{x},{y + 11} V{y + 3} Z"))
    return ("union", g)


ENV = {k: v for k, v in globals().items() if k in ("P", "Part", "C", "B", "X", "Ellipse", "Dot", "Disc", "Rings", "Group",
                                                    "Perforation", "Cross", "KeyBlock")}


def parse_catalog(path):
    src = open(path, encoding="utf-8").read()
    body = src[src.index("private static Part[] Create(Motif motif) => motif switch"):]
    body = body[body.index("{") + 1:body.index("        _ =>")]
    body = re.sub(r"//[^\n]*", "", body)
    out = {}
    for m in re.finditer(r"Motif\.(\w+)\s*=>\s*(.*?)(?=\n\s*Motif\.\w+\s*=>|\Z)", body, re.S):
        name, expr = m.group(1), m.group(2).strip().rstrip(",")
        py = expr.replace("new EllipseGeometry(new Rect(", "Ellipse((").replace("new(", "Part(")
        py = re.sub(r"Paint\.(\w+)", r"'\1'", py)
        out[name] = eval(py, dict(ENV))
    return out


# ------------------------------------------------------------------------------------------------ path flattening

def tokens(d):
    return re.findall(r"[MmLlHhVvQqAaZzCc]|-?\d*\.?\d+(?:e-?\d+)?", d)


def arc_points(p0, rx, ry, phi, large, sweep, p1, n=24):
    """SVG endpoint arc -> points (excluding p0)."""
    x1, y1 = p0
    x2, y2 = p1
    phi = math.radians(phi)
    cp, sp = math.cos(phi), math.sin(phi)
    dx, dy = (x1 - x2) / 2, (y1 - y2) / 2
    x1p, y1p = cp * dx + sp * dy, -sp * dx + cp * dy
    rx, ry = abs(rx), abs(ry)
    lam = (x1p / rx) ** 2 + (y1p / ry) ** 2
    if lam > 1:
        rx, ry = rx * math.sqrt(lam), ry * math.sqrt(lam)
    num = rx * rx * ry * ry - rx * rx * y1p * y1p - ry * ry * x1p * x1p
    den = rx * rx * y1p * y1p + ry * ry * x1p * x1p
    co = math.sqrt(max(0, num / den)) * (-1 if large == sweep else 1)
    cxp, cyp = co * rx * y1p / ry, -co * ry * x1p / rx
    cx, cy = cp * cxp - sp * cyp + (x1 + x2) / 2, sp * cxp + cp * cyp + (y1 + y2) / 2

    def ang(ux, uy, vx, vy):
        a = math.atan2(ux * vy - uy * vx, ux * vx + uy * vy)
        return a

    t1 = ang(1, 0, (x1p - cxp) / rx, (y1p - cyp) / ry)
    dt = ang((x1p - cxp) / rx, (y1p - cyp) / ry, (-x1p - cxp) / rx, (-y1p - cyp) / ry)
    if not sweep and dt > 0:
        dt -= 2 * math.pi
    elif sweep and dt < 0:
        dt += 2 * math.pi
    pts = []
    for i in range(1, n + 1):
        t = t1 + dt * i / n
        x, y = rx * math.cos(t), ry * math.sin(t)
        pts.append((cp * x - sp * y + cx, sp * x + cp * y + cy))
    return pts


def subpaths(d):
    """Path data -> list of (points, closed)."""
    tk = tokens(d)
    i, cmd = 0, None
    cur = (0.0, 0.0)
    out, pts, closed = [], [], False

    def num():
        nonlocal i
        v = float(tk[i])
        i += 1
        return v

    while i < len(tk):
        if re.match(r"[A-Za-z]", tk[i]):
            cmd = tk[i]
            i += 1
        rel = cmd.islower()
        c = cmd.upper()
        ox, oy = cur if rel else (0, 0)
        if c == "M":
            if pts:
                out.append((pts, closed))
            cur = (ox + num(), oy + num())
            pts, closed = [cur], False
            cmd = "l" if rel else "L"
        elif c == "L":
            cur = (ox + num(), oy + num())
            pts.append(cur)
        elif c == "H":
            cur = ((cur[0] if rel else 0) + num(), cur[1])
            pts.append(cur)
        elif c == "V":
            cur = (cur[0], (cur[1] if rel else 0) + num())
            pts.append(cur)
        elif c == "Q":
            q = (ox + num(), oy + num())
            e = (ox + num(), oy + num())
            for k in range(1, 13):
                t = k / 12
                pts.append(((1 - t) ** 2 * cur[0] + 2 * (1 - t) * t * q[0] + t * t * e[0],
                            (1 - t) ** 2 * cur[1] + 2 * (1 - t) * t * q[1] + t * t * e[1]))
            cur = e
        elif c == "C":
            q1 = (ox + num(), oy + num())
            q2 = (ox + num(), oy + num())
            e = (ox + num(), oy + num())
            for k in range(1, 17):
                t = k / 16
                a, b_, c_, d_ = (1 - t) ** 3, 3 * (1 - t) ** 2 * t, 3 * (1 - t) * t * t, t ** 3
                pts.append((a * cur[0] + b_ * q1[0] + c_ * q2[0] + d_ * e[0], a * cur[1] + b_ * q1[1] + c_ * q2[1] + d_ * e[1]))
            cur = e
        elif c == "A":
            rx, ry, phi, large, sweep = num(), num(), num(), int(num()), int(num())
            e = (ox + num(), oy + num())
            pts.extend(arc_points(cur, rx, ry, phi, large, sweep, e))
            cur = e
        elif c == "Z":
            closed = True
            if pts:
                out.append((pts, True))
                cur = pts[0]
            pts, closed = [], False
        else:
            raise ValueError(cmd)
    if pts:
        out.append((pts, closed))
    return out


# --------------------------------------------------------------------------------------------------- rasterising

def mask_of(geom, n, stroke=0.0):
    """Supersampled boolean mask (n x n) of a geometry on the 120 grid."""
    s = n / GRID
    kind, data = geom

    def blank():
        return Image.new("1", (n, n), 0)

    if kind == "path":
        if stroke > 0:
            im = blank()
            dr = ImageDraw.Draw(im)
            for pts, closed in subpaths(data):
                q = [(x * s, y * s) for x, y in pts]
                if closed:
                    q.append(q[0])
                dr.line(q, fill=1, width=max(1, round(stroke * s)), joint="curve")
            return np.asarray(im, bool)
        acc = np.zeros((n, n), bool)
        for pts, _ in subpaths(data):
            if len(pts) < 3:
                continue
            im = blank()
            ImageDraw.Draw(im).polygon([(x * s, y * s) for x, y in pts], fill=1)
            acc ^= np.asarray(im, bool)          # even-odd across subpaths (Geometry.Parse default)
        return acc
    if kind == "ellipse":
        x, y, w, h = data
        im = blank()
        dr = ImageDraw.Draw(im)
        if stroke > 0:
            dr.ellipse((x * s, y * s, (x + w) * s, (y + h) * s), outline=1, width=max(1, round(stroke * s)))
        else:
            dr.ellipse((x * s, y * s, (x + w) * s, (y + h) * s), fill=1)
        return np.asarray(im, bool)
    if kind in ("union", "evenodd"):
        acc = np.zeros((n, n), bool)
        for g in data:
            m = mask_of(g, n, stroke)
            acc = (acc ^ m) if kind == "evenodd" else (acc | m)
        return acc
    raise ValueError(kind)


def blur(a, sigma):
    r = max(1, int(math.ceil(sigma * 3)))
    k = np.exp(-0.5 * (np.arange(-r, r + 1) / sigma) ** 2)
    k /= k.sum()
    a = np.pad(a, r, mode="edge")
    a = np.apply_along_axis(lambda v: np.convolve(v, k, "valid"), 0, a)
    return np.apply_along_axis(lambda v: np.convolve(v, k, "valid"), 1, a).astype(np.float32)


ROLE = {"Color": 0, "Body": 1, "Lift": 2, "Light": 2, "Cut": 3}


def render_motif(parts, res, ss=4):
    n = res * ss
    role = np.full((n, n), -1, np.int8)
    height = np.zeros((n, n), np.float32)
    for p in parts:
        m = mask_of(p["geom"], n, p["stroke"])
        if p["paint"] == "Cut":
            under = height[m]
            height[m] = np.where(under > 0, np.maximum(under - SINK, 0.6), 0.0)
            # a cut on the bare ground shows the ground: no paint there
            role[m] = np.where(under > 0, ROLE["Cut"], -1)
        else:
            under = height[m]
            height[m] = np.where(under > 0, under + STACK, STAND)
            role[m] = ROLE[p["paint"]]
    # downsample: coverage per role, mean height
    cov = np.zeros((res, res, 5), np.float32)
    for k in range(4):
        cov[..., k] = (role == k).reshape(res, ss, res, ss).mean(axis=(1, 3))
    h = height.reshape(res, ss, res, ss).mean(axis=(1, 3))
    # rounded edges: a soft blur of the height (~0.45 grid units), the coverage stays crisp
    sigma = 0.6 * res / GRID
    h = blur(h, sigma)
    return cov, h


def main():
    repo, root = sys.argv[1], sys.argv[2]
    res = int(sys.argv[sys.argv.index("--res") + 1]) if "--res" in sys.argv else 512
    cat = parse_catalog(os.path.join(repo, "src", "RetroHub.App", "Theming", "MotifCatalog.cs"))
    dst = os.path.join(root, "motifs-src")
    os.makedirs(dst, exist_ok=True)
    pal = np.array([[0x12, 0xA0, 0x5E], [0x8A, 0x90, 0x98], [0x6F, 0xE0, 0xA8], [0x10, 0x11, 0x13]], np.float32)
    for name, parts in cat.items():
        cov, h = render_motif(parts, res)
        third = "Light" if any(p["paint"] == "Light" for p in parts) else "Lift"
        np.savez_compressed(os.path.join(dst, f"{name}.npz"), role=cov, height=h, third=np.array(third))
        rgb = np.einsum("hwk,kc->hwc", cov[..., :4], pal) + (1 - cov[..., :4].sum(-1, keepdims=True)) * 40
        Image.fromarray(np.clip(rgb, 0, 255).astype(np.uint8)).resize((128, 128), Image.LANCZOS).save(
            os.path.join(dst, f"_{name}.png"))
        print(name, len(parts), "parts", third, f"h {h.max():.1f}")


if __name__ == "__main__":
    main()
