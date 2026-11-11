"""RetroHub R3 props: the physical objects of the Konsolen page, rendered headless in Blender (Cycles).

Run (never without -b, always --factory-startup so the owner's Blender prefs stay untouched):
  blender.exe -b --factory-startup -P blender_props.py -- <assets-r3 dir> <job>[,<job>...] [--samples N]

Jobs: spine-l spine-s front-l front-s sleeves-l sleeves-s board-stage board-edge bookend tag-l tag-s rail (+rail-notch) reiter sign tv
(or "all"). Every object is lit identically (soft key sun from top-left-front, cool fill from the right, dim neutral
world) and seen by one orthographic camera (pitch 1.6 deg down, yaw 2.4 deg: the top face and the right side face show
a few px, like the app's SleeveTop/SleeveSide). Scale: 1 Blender unit = 100 px at 1x; renders are 2x (200 px/BU).

Per object it writes <dir>/props/<name>/ :
  base.npy   linear premultiplied RGBA float32 (2x), the tinted regions rendered with albedo 0
  band.npy   same with the printed colour band at albedo 1   (light map = band - base)
  body.npy   same with the tinted body at albedo 1           (light map = body - base)
  shadow.npy the object invisible, the board / wall behind as shadow catcher (alpha = shadow)
  meta.json  canvas size and the rects (front face, label, motif recess, band ...) in 2x canvas pixels
build_sprites.py turns these into the PNG sprites. Materials come from <dir>/materials/*-1024.png (make_tileable.py).
"""
import json
import math
import os
import random
import sys

import bpy
import numpy as np
from mathutils import Euler, Matrix, Vector

PPU = 200.0          # px per Blender unit at 2x
PX = 0.01            # one 1x pixel in BU
PITCH, YAW = 1.6, 2.4
KEY_DIR = Vector((-0.50, -0.70, 0.52)).normalized()   # toward the key light (top-left-front)
FILL_DIR = Vector((0.75, -0.55, 0.15)).normalized()   # toward the fill (right, cool)
KEY_DIST, KEY_SIZE = 7.0, 4.0                          # the key is a near disc: light falls off down the object
KEY_POWER = 3.0 * math.pi * KEY_DIST ** 2               # ~ the irradiance of a 3.0 sun at the object's centre
TOP_POWER = 900.0                                       # a broad soft box above and in front: sheen, top faces

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
ROOT = argv[0]
JOBS = argv[1].split(",") if len(argv) > 1 else ["all"]
SAMPLES = int(argv[argv.index("--samples") + 1]) if "--samples" in argv else 64
MAT_DIR = os.path.join(ROOT, "materials")
OUT = os.path.join(ROOT, "props")
TMP = os.path.join(ROOT, "blend", "tmp")
os.makedirs(OUT, exist_ok=True)
os.makedirs(TMP, exist_ok=True)

# the tint switches every material reads (set per pass)
TINT_NODES = {"band": [], "body": []}


# ----------------------------------------------------------------------------------------------------------------- scene

GPU_KIND = None if os.environ.get("RH_BLENDER_GPU") == "1" else ""


def _enable_gpu():
    """Turn on the first OptiX (else CUDA) device in this session's (factory) prefs; returns the kind or ''."""
    prefs = bpy.context.preferences.addons["cycles"].preferences
    for kind in ("OPTIX", "CUDA"):
        try:
            prefs.compute_device_type = kind
            prefs.get_devices()
            devs = [d for d in prefs.devices if d.type == kind]
        except Exception:
            continue
        if devs:
            for d in prefs.devices:
                d.use = d.type == kind
            print("GPU", kind, [d.name for d in devs], flush=True)
            return kind
    print("GPU requested but none found - CPU", flush=True)
    return ""


def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    TINT_NODES["band"].clear()
    TINT_NODES["body"].clear()
    _images.clear()
    sc = bpy.context.scene
    sc.render.engine = "CYCLES"
    # Default CPU ONLY (owner's rule: the GPU belongs to ComfyUI / the desktop; a GPU overload killed the shell once):
    # Cycles on the CPU, OpenImageDenoise on the CPU, at most 8 threads (run_blender.ps1 also passes -t 8).
    # RH_BLENDER_GPU=1 (run_blender.ps1 -Gpu): the final full-quality pass on the GPU — only with Blender alone on it.
    sc.render.threads_mode = "FIXED"
    sc.render.threads = 8
    sc.cycles.samples = SAMPLES
    sc.cycles.use_denoising = True
    sc.cycles.denoiser = "OPENIMAGEDENOISE"
    gpu = GPU_KIND if GPU_KIND is not None else _enable_gpu()
    sc.cycles.device = "GPU" if gpu else "CPU"
    try:
        sc.cycles.denoising_use_gpu = bool(gpu)
    except Exception:
        pass
    sc.cycles.seed = 7
    sc.cycles.max_bounces = 6
    sc.render.film_transparent = True
    sc.view_settings.view_transform = "Standard"
    sc.view_settings.look = "None"
    sc.render.image_settings.file_format = "OPEN_EXR"
    sc.render.image_settings.color_depth = "32"
    sc.render.image_settings.color_mode = "RGBA"
    sc.render.resolution_percentage = 100
    # world: dim neutral room
    w = bpy.data.worlds.new("room")
    sc.world = w
    w.use_nodes = True
    bg = w.node_tree.nodes.get("Background")
    bg.inputs["Color"].default_value = (0.55, 0.57, 0.60, 1)
    bg.inputs["Strength"].default_value = 0.10
    # key: soft sun from top-left-front, neutral white
    key = bpy.data.lights.new("key", "AREA")
    key.shape = "DISK"
    key.size = KEY_SIZE
    key.energy = KEY_POWER
    key.color = (1.0, 0.99, 0.97)
    kob = bpy.data.objects.new("key", key)
    sc.collection.objects.link(kob)
    top = bpy.data.lights.new("top", "AREA")
    top.shape = "RECTANGLE"
    top.size, top.size_y = 14.0, 4.0
    top.energy = TOP_POWER
    top.color = (0.96, 0.98, 1.0)
    tob = bpy.data.objects.new("top", top)
    sc.collection.objects.link(tob)
    for name, d, strength, angle, col in (("fill", FILL_DIR, 0.45, 40, (0.80, 0.90, 1.0)),):
        ld = bpy.data.lights.new(name, "SUN")
        ld.energy = strength
        ld.angle = math.radians(angle)
        ld.color = col
        ob = bpy.data.objects.new(name, ld)
        sc.collection.objects.link(ob)
        ob.rotation_euler = d.to_track_quat("Z", "Y").to_euler()
    cam_d = bpy.data.cameras.new("cam")
    cam_d.type = "ORTHO"
    cam = bpy.data.objects.new("cam", cam_d)
    sc.collection.objects.link(cam)
    sc.camera = cam
    cam.rotation_euler = Euler((math.radians(90 - PITCH), 0, math.radians(YAW)))
    return sc


def cam_basis():
    R = Euler((math.radians(90 - PITCH), 0, math.radians(YAW))).to_matrix()
    return R @ Vector((1, 0, 0)), R @ Vector((0, 1, 0)), R @ Vector((0, 0, -1))


def frame(objs, pad_px, extra=(0, 0, 0, 0)):
    """Fit the camera to the objects' projected bounds (+pad on every side, +extra l,t,r,b in 2x px) on an integer grid."""
    right, up, fwd = cam_basis()
    dg = bpy.context.evaluated_depsgraph_get()
    us, vs = [], []
    for o in objs:
        oe = o.evaluated_get(dg)
        me = oe.to_mesh()
        mw = o.matrix_world
        for v in me.vertices:
            p = mw @ v.co
            us.append(p.dot(right))
            vs.append(p.dot(up))
        oe.to_mesh_clear()
    l, t, r_, b = extra
    u0 = math.floor(min(us) * PPU) - pad_px - l
    u1 = math.ceil(max(us) * PPU) + pad_px + r_
    v0 = math.floor(min(vs) * PPU) - pad_px - b
    v1 = math.ceil(max(vs) * PPU) + pad_px + t
    W, H = int(u1 - u0), int(v1 - v0)
    sc = bpy.context.scene
    sc.render.resolution_x, sc.render.resolution_y = W, H
    cam = sc.camera
    cam.data.ortho_scale = max(W, H) / PPU
    cam.data.sensor_fit = "AUTO"
    uc, vc = (u0 + W / 2) / PPU, (v0 + H / 2) / PPU
    cam.location = right * uc + up * vc - fwd * 60
    # the key hangs at a fixed distance from the object's own centre, so every object gets the same light
    centre = Vector((0, 0, 0))
    n = 0
    for o in objs:
        for c in o.bound_box:
            centre += o.matrix_world @ Vector(c)
            n += 1
    centre /= max(n, 1)
    k = sc.objects["key"]
    k.location = centre + KEY_DIR * KEY_DIST
    k.rotation_euler = (-KEY_DIR).to_track_quat("-Z", "Y").to_euler()
    hi = max((o.matrix_world @ Vector(c)).z for o in objs for c in o.bound_box)
    t = sc.objects["top"]
    tdir = Vector((0.0, -0.55, 1.0)).normalized()
    t.location = Vector((centre.x, centre.y, hi)) + tdir * 6.0
    t.rotation_euler = (-tdir).to_track_quat("-Z", "Y").to_euler()
    return {"w": W, "h": H, "u0": u0, "v1": v1}


def to_px(fr, p):
    right, up, _ = cam_basis()
    return (p.dot(right) * PPU - fr["u0"], fr["v1"] - p.dot(up) * PPU)


def rect_px(fr, ob, x0, z0, x1, z1, y=0.0):
    """A rect on the object's local front plane (y) -> canvas px [x, y, w, h] (2x)."""
    pts = [to_px(fr, ob.matrix_world @ Vector((x, y, z))) for x, z in ((x0, z0), (x1, z0), (x0, z1), (x1, z1))]
    xs, ys = [p[0] for p in pts], [p[1] for p in pts]
    return [round(min(xs), 1), round(min(ys), 1), round(max(xs) - min(xs), 1), round(max(ys) - min(ys), 1)]


# ------------------------------------------------------------------------------------------------------------- materials

_images = {}


def tex_image(name):
    if name not in _images:
        _images[name] = bpy.data.images.load(os.path.join(MAT_DIR, f"{name}-1024.png"))
        _images[name].colorspace_settings.name = "sRGB"
    return _images[name]


def socket(node, ident, out=False):
    for s in (node.outputs if out else node.inputs):
        if s.identifier == ident or s.name == ident:
            return s
    raise KeyError(f"{node.bl_idname}: {ident}")


class NB:
    """A tiny node-graph builder."""

    def __init__(self, mat):
        mat.use_nodes = True
        self.t = mat.node_tree
        self.t.nodes.clear()
        self.out = self.n("ShaderNodeOutputMaterial")

    def n(self, kind, **props):
        nd = self.t.nodes.new(kind)
        for k, v in props.items():
            setattr(nd, k, v)
        return nd

    def link(self, a, b):
        self.t.links.new(a, b)

    def val(self, v, name=None):
        nd = self.n("ShaderNodeValue")
        nd.outputs[0].default_value = v
        if name:
            nd.name = name
        return nd.outputs[0]

    def math(self, op, a, b=None, clamp=False):
        nd = self.n("ShaderNodeMath", operation=op, use_clamp=clamp)
        for i, x in enumerate((a, b)):
            if x is None:
                continue
            if isinstance(x, (int, float)):
                nd.inputs[i].default_value = x
            else:
                self.link(x, nd.inputs[i])
        return nd.outputs[0]

    def mix(self, fac, a, b):
        nd = self.n("ShaderNodeMix", data_type="RGBA")
        for ident, x in (("Factor_Float", fac), ("A_Color", a), ("B_Color", b)):
            s = socket(nd, ident)
            if isinstance(x, (int, float)):
                s.default_value = x
            elif isinstance(x, tuple):
                s.default_value = x
            else:
                self.link(x, s)
        return socket(nd, "Result_Color", out=True)

    def mulc(self, col, f):
        nd = self.n("ShaderNodeMix", data_type="RGBA", blend_type="MULTIPLY")
        socket(nd, "Factor_Float").default_value = 1
        self.link(col, socket(nd, "A_Color"))
        if isinstance(f, tuple):
            socket(nd, "B_Color").default_value = f
        else:
            cn = self.n("ShaderNodeCombineColor")
            for i in range(3):
                self.link(f, cn.inputs[i])
            self.link(cn.outputs[0], socket(nd, "B_Color"))
        return socket(nd, "Result_Color", out=True)


def textured(nb, image, scale, rnd, proj_blend=0.25):
    """Box-projected tile of a material scan in object space (scale = BU per tile), seeded offset."""
    tc = nb.n("ShaderNodeTexCoord")
    mp = nb.n("ShaderNodeMapping")
    mp.inputs["Scale"].default_value = (1 / scale, 1 / scale, 1 / scale)
    mp.inputs["Location"].default_value = (rnd.random(), rnd.random(), rnd.random())
    nb.link(tc.outputs["Object"], mp.inputs["Vector"])
    it = nb.n("ShaderNodeTexImage", projection="BOX", interpolation="Cubic")
    it.projection_blend = proj_blend
    it.image = tex_image(image)
    nb.link(mp.outputs[0], it.inputs["Vector"])
    return it.outputs["Color"], tc


def edge_wear(nb, rnd, radius=0.02, gain=6.0, noise_scale=60.0, threshold=0.45):
    """Worn edges: where the bevel-rounded normal leaves the true normal, broken up by noise (0..1)."""
    bv = nb.n("ShaderNodeBevel", samples=8)
    bv.inputs["Radius"].default_value = radius * 0.5
    ao = nb.n("ShaderNodeAmbientOcclusion", inside=True, only_local=True, samples=16)
    ao.inputs["Distance"].default_value = radius * 1.5
    edge = nb.math("MULTIPLY", nb.math("SUBTRACT", 0.92, socket(ao, "AO", out=True)), gain * 0.5, clamp=True)
    tc = nb.n("ShaderNodeTexCoord")
    nz = nb.n("ShaderNodeTexNoise")
    nz.inputs["Scale"].default_value = noise_scale
    nz.inputs["Detail"].default_value = 6
    mp = nb.n("ShaderNodeMapping")
    mp.inputs["Location"].default_value = (rnd.random() * 10, rnd.random() * 10, rnd.random() * 10)
    nb.link(tc.outputs["Object"], mp.inputs["Vector"])
    nb.link(mp.outputs[0], nz.inputs["Vector"])
    n = nb.math("SUBTRACT", nz.outputs["Fac"], threshold)
    n = nb.math("MULTIPLY", n, 5.0, clamp=True)
    return nb.math("MULTIPLY", edge, nb.math("ADD", n, 0.25), clamp=True), bv


def bump_from(nb, col, strength, distance=0.004, normal=None):
    bw = nb.n("ShaderNodeRGBToBW")
    nb.link(col, bw.inputs[0])
    bp = nb.n("ShaderNodeBump")
    bp.inputs["Strength"].default_value = strength
    bp.inputs["Distance"].default_value = distance
    nb.link(bw.outputs[0], bp.inputs["Height"])
    if normal is not None:
        nb.link(normal, bp.inputs["Normal"])
    return bp.outputs["Normal"]


def warp(nb, rnd, normal, scale=0.35, strength=0.18):
    """Low-frequency undulation (board not perfectly flat): soft light variation across a face."""
    tc = nb.n("ShaderNodeTexCoord")
    mp = nb.n("ShaderNodeMapping")
    mp.inputs["Location"].default_value = (rnd.random() * 9, rnd.random() * 9, rnd.random() * 9)
    nb.link(tc.outputs["Object"], mp.inputs["Vector"])
    nz = nb.n("ShaderNodeTexNoise")
    nz.inputs["Scale"].default_value = scale
    nz.inputs["Detail"].default_value = 1.5
    nb.link(mp.outputs[0], nz.inputs["Vector"])
    bp = nb.n("ShaderNodeBump")
    bp.inputs["Strength"].default_value = strength
    bp.inputs["Distance"].default_value = 0.05
    nb.link(nz.outputs["Fac"], bp.inputs["Height"])
    nb.link(normal, bp.inputs["Normal"])
    return bp.outputs["Normal"]


def principled(nb, base, rough, spec=0.5, metal=0.0, normal=None, aniso=0.0):
    p = nb.n("ShaderNodeBsdfPrincipled")
    if isinstance(base, tuple):
        p.inputs["Base Color"].default_value = base
    else:
        nb.link(base, p.inputs["Base Color"])
    for k, v in (("Roughness", rough), ("Specular IOR Level", spec), ("Metallic", metal)):
        if isinstance(v, (int, float)):
            p.inputs[k].default_value = v
        else:
            nb.link(v, p.inputs[k])
    if aniso:
        p.inputs["Anisotropic"].default_value = aniso
    if normal is not None:
        nb.link(normal, p.inputs["Normal"])
    nb.link(p.outputs[0], nb.out.inputs["Surface"])
    return p


def z_band(nb, tc, z0, z1, axis=2):
    sep = nb.n("ShaderNodeSeparateXYZ")
    nb.link(tc.outputs["Object"], sep.inputs[0])
    z = sep.outputs[axis]
    return nb.math("MULTIPLY", nb.math("GREATER_THAN", z, z0), nb.math("LESS_THAN", z, z1))


def mat_cardboard(name, rnd, band=None, tint_body=True, body_level=0.92, wear=0.55, tex_scale=3.6, band_axis=2, rough=0.78):
    """Tintable printed cardboard: the body (tint 'body') and an optional printed band (tint 'band', z0..z1 in object
    space). Tinted albedo = normalised scan luminance x level x TINT; worn edges show light grey fibre."""
    m = bpy.data.materials.new(name)
    nb = NB(m)
    col, tc = textured(nb, "cardboard-grey", tex_scale, rnd)
    bw = nb.n("ShaderNodeRGBToBW")
    nb.link(col, bw.inputs[0])
    # scan mean ~0.74 -> level, its fibre and mottling contrast x3 so it survives a dark tint
    lum = nb.math("MULTIPLY", nb.math("ADD", nb.math("MULTIPLY", nb.math("SUBTRACT", bw.outputs[0], 0.74), 1.6), 0.74),
                  body_level / 0.74)
    t_body = nb.val(1.0 if not tint_body else 0.0, "TINT_BODY")
    if tint_body:
        TINT_NODES["body"].append(t_body.node)
    body = nb.math("MULTIPLY", lum, t_body)
    alb = nb.mix(0, (0, 0, 0, 1), (0, 0, 0, 1))
    # albedo as grey from 'body' (scalar)
    cn = nb.n("ShaderNodeCombineColor")
    for i in range(3):
        nb.link(body, cn.inputs[i])
    alb = cn.outputs[0]
    if band:
        t_band = nb.val(0.0, "TINT_BAND")
        TINT_NODES["band"].append(t_band.node)
        inb = z_band(nb, tc, band[0], band[1], band_axis)
        smooth_lum = nb.math("MULTIPLY", nb.math("ADD", nb.math("MULTIPLY", nb.math("SUBTRACT", bw.outputs[0], 0.74), 0.8), 0.74),
                             body_level / 0.74)    # printed ink: the board's mottling shows only faintly
        bcol = nb.math("MULTIPLY", smooth_lum, t_band)
        cb = nb.n("ShaderNodeCombineColor")
        for i in range(3):
            nb.link(bcol, cb.inputs[i])
        alb = nb.mix(inb, alb, cb.outputs[0])
        rough = nb.math("SUBTRACT", rough, nb.math("MULTIPLY", inb, rough * 0.28))
    wf, bv = edge_wear(nb, rnd, radius=0.03, gain=7.0, noise_scale=22.0, threshold=0.56)
    wf = nb.math("MULTIPLY", wf, wear)
    worn = nb.mulc(col, (0.62, 0.62, 0.63, 1))
    alb = nb.mix(wf, alb, worn)
    # dust/scratch overlay, faint
    dcol, _ = textured(nb, "dust-scratches", 2.4, rnd, 0.3)
    dbw = nb.n("ShaderNodeRGBToBW")
    nb.link(dcol, dbw.inputs[0])
    alb = nb.mix(nb.math("MULTIPLY", dbw.outputs[0], 0.12), alb, (0.5, 0.5, 0.52, 1))
    nrm = bump_from(nb, col, 0.5, 0.004, bv.outputs["Normal"])
    nrm = warp(nb, rnd, nrm)
    principled(nb, alb, rough, spec=0.5, normal=nrm)
    return m


def mat_paper(name, rnd, level=0.86):
    m = bpy.data.materials.new(name)
    nb = NB(m)
    col, tc = textured(nb, "label-paper-white", 1.2, rnd)
    calm = nb.mix(0.85, col, (0.94, 0.94, 0.94, 1))
    alb = nb.mulc(calm, (level, level, level * 0.995, 1))
    bv = nb.n("ShaderNodeBevel", samples=8)
    bv.inputs["Radius"].default_value = 0.003
    nrm = bump_from(nb, col, 0.12, 0.002, bv.outputs["Normal"])
    principled(nb, alb, 0.82, spec=0.3, normal=nrm)
    return m


def mat_powder(name, rnd, level=1.0, tex="metal-powder-black", scale=3.0, wear=0.5):
    """Matte black powder-coated steel (dielectric coat, fine orange peel)."""
    m = bpy.data.materials.new(name)
    nb = NB(m)
    col, tc = textured(nb, tex, scale, rnd)
    hsv = nb.n("ShaderNodeHueSaturation")
    hsv.inputs["Saturation"].default_value = 0.15
    hsv.inputs["Value"].default_value = 1.5 * level
    nb.link(col, hsv.inputs["Color"])
    alb = hsv.outputs[0]
    wf, bv = edge_wear(nb, rnd, radius=0.015, gain=5, noise_scale=70, threshold=0.5)
    alb = nb.mix(nb.math("MULTIPLY", wf, wear), alb, (0.22, 0.225, 0.235, 1))
    nrm = bump_from(nb, col, 0.5, 0.002, bv.outputs["Normal"])
    principled(nb, alb, nb.math("ADD", 0.42, nb.math("MULTIPLY", wf, 0.3)), spec=0.5, normal=nrm)
    return m


def mat_laminate(name, rnd):
    m = bpy.data.materials.new(name)
    nb = NB(m)
    col, tc = textured(nb, "laminate-edge-dark", 4.0, rnd)
    hsv = nb.n("ShaderNodeHueSaturation")
    hsv.inputs["Saturation"].default_value = 0.12
    hsv.inputs["Value"].default_value = 0.30
    nb.link(col, hsv.inputs["Color"])
    nrm = bump_from(nb, col, 0.15, 0.002)
    principled(nb, hsv.outputs[0], 0.48, spec=0.5, normal=nrm)
    return m


def mat_brushed(name, rnd, level=0.55, rough=0.38, tint=False):
    """Brushed aluminium (metallic). tint=True: an anodised inlay that takes the band colour (TINT_BAND)."""
    m = bpy.data.materials.new(name)
    nb = NB(m)
    col, tc = textured(nb, "metal-brushed-dark", 2.0, rnd, 0.1)
    hsv = nb.n("ShaderNodeHueSaturation")
    hsv.inputs["Saturation"].default_value = 0.0
    hsv.inputs["Value"].default_value = level * 1.9
    nb.link(col, hsv.inputs["Color"])
    alb = hsv.outputs[0]
    if tint:
        t = nb.val(0.0, "TINT_BAND")
        TINT_NODES["band"].append(t.node)
        alb = nb.mulc(alb, t)
    wf, bv = edge_wear(nb, rnd, radius=0.008, gain=4, noise_scale=120, threshold=0.4)
    alb = nb.mix(nb.math("MULTIPLY", wf, 0.4), alb, (0.75, 0.75, 0.76, 1))
    nrm = bump_from(nb, col, 0.12, 0.001, bv.outputs["Normal"])
    principled(nb, alb, rough, spec=0.5, metal=0.0 if tint else 0.85, normal=nrm, aniso=0.0 if tint else 0.5)
    return m


def mat_flat(name, rgb, rough=0.9):
    m = bpy.data.materials.new(name)
    nb = NB(m)
    principled(nb, (*rgb, 1), rough, spec=0.3)
    return m


# ---------------------------------------------------------------------------------------------------------------- meshes

def link(ob):
    bpy.context.scene.collection.objects.link(ob)
    return ob


def box(name, x0, y0, z0, x1, y1, z1, mat=None, bevel=0.0, segs=3):
    me = bpy.data.meshes.new(name)
    v = [(x0, y0, z0), (x1, y0, z0), (x1, y1, z0), (x0, y1, z0), (x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1)]
    f = [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]
    me.from_pydata(v, [], f)
    me.update()
    ob = link(bpy.data.objects.new(name, me))
    if mat:
        ob.data.materials.append(mat)
    if bevel > 0:
        add_bevel(ob, bevel, segs)
    return ob


def add_bevel(ob, width, segs=3):
    md = ob.modifiers.new("bevel", "BEVEL")
    md.width = width
    md.segments = segs
    md.limit_method = "ANGLE"
    md.angle_limit = math.radians(30)
    md.harden_normals = False
    return md


def cut(ob, cutter):
    md = ob.modifiers.new("cut", "BOOLEAN")
    md.operation = "DIFFERENCE"
    md.object = cutter
    try:
        md.solver = "EXACT"
    except Exception:
        pass
    cutter.hide_render = True
    cutter.hide_viewport = True
    # the cutter moves with its object (lean, turn)
    cutter.parent = ob
    cutter.matrix_parent_inverse = ob.matrix_world.inverted()
    # boolean must run before the bevel
    while ob.modifiers[0].name != md.name:
        bpy.ops.object.select_all(action="DESELECT")
        bpy.context.view_layer.objects.active = ob
        with bpy.context.temp_override(object=ob):
            bpy.ops.object.modifier_move_up(modifier=md.name)
    return md


def smooth(ob):
    for p in ob.data.polygons:
        p.use_smooth = True


def catchers(y_wall, floor=True, wall=True, z_floor=0.0, wall_from=-50, wall_to=50):
    """Board (floor) and wall behind, used as shadow catchers in the shadow pass and as hidden bounce otherwise."""
    out = []
    dark = mat_flat("catcher", (0.05, 0.052, 0.056))
    if floor:
        out.append(box("floor", -60, -20, z_floor - 0.5, 60, y_wall + 0.01, z_floor, dark))
    if wall:
        out.append(box("wall", -60, y_wall, wall_from, 60, y_wall + 0.5, wall_to, dark))
    for o in out:
        o.visible_camera = False
    return out


# ------------------------------------------------------------------------------------------------------------------ jobs

# R3 QA 2: the Konsolen spines are taller (520 / 400 px: no dead band above the row) and are lever-arch binders – a long
# label pocket that carries the printed code and motif, a metal-rimmed finger hole instead of the debossed motif square
SPINE_DIMS = {"l": (1.28, 5.20, 1.60), "s": (0.96, 4.00, 1.30), "m": (0.72, 2.00, 1.00), "xs": (0.56, 1.60, 0.85)}
HOLE = {"l": (1.06, 0.29), "s": (0.84, 0.22)}   # finger hole: centre height, radius (BU)
TAG_Z = {"l": 0.27, "s": 0.23, "m": 0.175, "xs": 0.15}


def spine(size, variant):
    """The big system 'Fach' of the Konsolen wall: a stiff cardboard slipcase standing on the board, its front a
    printed spine: colour band, a recessed label holder with a paper insert (the code), a debossed square where the
    motif is printed, room for the count tag at the foot."""
    # l/s: the Konsolen wall (1080 / ~1100); m/xs: the system spines on Start's shelf (72 x 200 / 56 x 160)
    W, H, D = SPINE_DIMS[size]
    small = size in ("m", "xs")
    rnd = random.Random(1000 + variant * 17 + {"l": 0, "s": 500, "m": 800, "xs": 900}[size])
    band = (H - (0.20 if size == "l" else 0.18 if size == "s" else 0.13), H - (0.06 if small else 0.08))
    mat = mat_cardboard(f"spine-{size}{variant}", rnd, band=band, rough=0.56)   # laminated print: a satin sheen
    ob = box("spine", 0, 0, 0, W, D, H, mat)
    # label holder (recess 2 px) with a paper insert
    m = round(W * (0.22 if small else 0.25), 2)
    lt = H - {"l": 0.32, "s": 0.28, "m": 0.21, "xs": 0.19}[size]
    lb = lt - round(H * (0.42 if small else 0.50), 2)
    cutter = box("c_label", m, -0.1, lb, W - m, 0.04, lt)
    cut(ob, cutter)
    extra = []
    if small:
        s = round(W - 2 * max(0.12, round(W * 0.14, 2)), 2)
        mb = {"m": 0.36, "xs": 0.31}[size]
        cut(ob, box("c_motif", (W - s) / 2, -0.1, mb, (W + s) / 2, 0.03, mb + s))
    else:
        # the finger hole: a round cut through the front into the dark inside, a pressed steel grommet around it
        hz, hr = HOLE[size]
        bpy.ops.mesh.primitive_cylinder_add(vertices=64, radius=hr, depth=0.9, location=(W / 2, 0.30, hz), rotation=(math.pi / 2, 0, 0))
        hc = bpy.context.active_object
        hc.name = "c_hole"
        cut(ob, hc)
        steel = mat_brushed(f"grommet-{size}{variant}", rnd, level=0.62, rough=0.32)
        bpy.ops.mesh.primitive_torus_add(major_segments=96, minor_segments=16, major_radius=hr + 0.012,
                                         minor_radius=0.034 if size == "l" else 0.027, location=(W / 2, -0.004, hz), rotation=(math.pi / 2, 0, 0))
        ring = bpy.context.active_object
        ring.name = "grommet"
        ring.scale = (1, 1, 0.55)   # pressed flat against the board
        ring.data.materials.append(steel)
        smooth(ring)
        # the inside of the binder behind the hole: dark board, a hint of the rings' lever
        inside = box("inside", W / 2 - hr - 0.1, 0.25, hz - hr - 0.1, W / 2 + hr + 0.1, 0.30, hz + hr + 0.1, mat_flat("inside", (0.035, 0.037, 0.04)))
        extra = [ring, inside]
    add_bevel(ob, 0.04 if size == "l" else 0.03 if size == "s" else 0.02, 4)
    # slight per-item variation: a hair of lean and a dented corner
    ob.rotation_euler = (0, math.radians(rnd.uniform(-0.25, 0.25)), 0)
    paper = mat_paper(f"label-{size}{variant}", rnd, 0.60)
    lab = box("label", m + 0.015, 0.03, lb + 0.015, W - m - 0.015, 0.05, lt - 0.015, paper, bevel=0.004, segs=2)
    lab.parent = ob
    for e in extra:
        e.parent = ob
        e.matrix_parent_inverse = ob.matrix_world.inverted()
    smooth(ob)
    objs = [ob, lab] + extra
    meta = {"kind": "spine", "size": size, "variant": variant}
    cat = catchers(D + 0.35)

    def rects(fr):
        r = {
            "front": rect_px(fr, ob, 0, 0, W, H),
            "band": rect_px(fr, ob, 0, band[0], W, band[1]),
            "label": rect_px(fr, ob, m + 0.015, lb + 0.015, W - m - 0.015, lt - 0.015, y=0.03),
            "tagCenterY": round(to_px(fr, ob.matrix_world @ Vector((W / 2, 0, TAG_Z[size])))[1], 1),
            "tagCenterX": round(to_px(fr, ob.matrix_world @ Vector((W / 2, 0, 0.27)))[0], 1),
        }
        if small:
            r["motif"] = rect_px(fr, ob, (W - s) / 2, mb, (W + s) / 2, mb + s, y=0.03)
        else:
            r["hole"] = rect_px(fr, ob, W / 2 - hr, hz - hr, W / 2 + hr, hz + hr)
        return r
    return objs, cat, meta, rects


# the open front's compartment, in 1x px from the front's top-left: (inset, top, inner board top, inner board height)
FRONT_RECESS = {"l": (26, 168, 376, 10), "s": (20, 128, 285, 8)}


def front(size, variant):
    """The open front of the focused system: the same printed cardboard as a big panel, band at the top, and a
    compartment cut into it (3 px deep walls, its own small board) where the app stands up to three boxes."""
    # l/s: the Konsolen front with its compartment; m/xs: Start's small front (200 / 160 square, no compartment)
    W, H, D = {"l": (5.20, 6.00, 1.60), "s": (3.80, 4.60, 1.30), "m": (2.00, 2.00, 1.00), "xs": (1.60, 1.60, 0.85)}[size]
    small = size in ("m", "xs")
    rnd = random.Random(3000 + variant * 31 + {"l": 0, "s": 500, "m": 800, "xs": 900}[size])
    band = (H - 0.22, H - 0.08) if not small else (H - 0.16, H - 0.06)
    mat = mat_cardboard(f"front-{size}", rnd, band=band, wear=0.45, rough=0.56)
    ob = box("front", 0, 0, 0, W, D, H, mat)
    if not small:
        ins, top, btop, bh = [v * PX for v in FRONT_RECESS[size]]
        rz1, rz0 = H - top, H - btop - bh
        cut(ob, box("c_recess", ins, -0.1, rz0, W - ins, 0.34, rz1))
    # a debossed rule framing the face under the band (a pressed line in the board, 1 px wide, ~0.6 px deep)
    ri, rw, rd = (0.16 if size == "l" else 0.12 if size == "s" else 0.07), 0.012, 0.006
    rt = band[0] - ri * 0.6
    for nm, a in (("t", (ri, rt - rw, W - ri, rt)), ("b", (ri, ri, W - ri, ri + rw)),
                  ("l", (ri, ri, ri + rw, rt)), ("r", (W - ri - rw, ri, W - ri, rt))):
        cut(ob, box(f"c_rule{nm}", a[0], -0.1, a[1], a[2], rd, a[3]))
    add_bevel(ob, 0.03 if not small else 0.02, 3)
    smooth(ob)
    cat = catchers(D + 0.35)
    if small:
        return [ob], cat, {"kind": "front", "size": size}, lambda fr: {
            "front": rect_px(fr, ob, 0, 0, W, H), "band": rect_px(fr, ob, 0, band[0], W, band[1])}
    shelf = box("innerboard", ins - 0.01, 0.0, rz0 - 0.01, W - ins + 0.01, 0.35, H - btop, mat_powder("inner", rnd, level=0.75),
                bevel=0.008, segs=2)
    smooth(shelf)
    return [ob, shelf], cat, {"kind": "front", "size": size}, lambda fr: {
        "front": rect_px(fr, ob, 0, 0, W, H), "band": rect_px(fr, ob, 0, band[0], W, band[1]),
        "recess": rect_px(fr, ob, ins, rz0, W - ins, rz1, y=0.34),
        "innerBoard": rect_px(fr, ob, ins, rz0, W - ins, H - btop)}


# R3 QA 3 ("the front is a flat UI plate next to rendered binders"): the big Konsolen front IS the pulled binder – the
# same lever-arch binder as the spines, turned 25 deg toward the room, so its spine (band, label pocket, finger hole with
# its steel ring) shows as a strip at the left and its cover faces the room. The cover keeps the system colour as an
# 8 px printed edge band (the spine's band runs round onto it), a steel edge protector along its foot, and a window cut
# deep into the binder where the app stands up to three boxes on an inner board (real AO from the cut).
BINDER = {"l": dict(W=5.20, H=6.00, S=1.28, rec=(26, 168, 376, 10)), "s": dict(W=3.80, H=4.60, S=0.96, rec=(20, 128, 285, 8))}
BINDER_TURN = 25.0


def binder_front(size):
    p = BINDER[size]
    W, H, S = p["W"], p["H"], p["S"]
    a = math.radians(BINDER_TURN)
    C = (W - S * math.sin(a)) / math.cos(a)          # cover length so the whole projection is W wide
    rnd = random.Random(3100 + (0 if size == "l" else 500))
    band = (H - 0.16, H - 0.08)                       # an 8 px printed band round the binder, 8 px under its top edge
    mat = mat_cardboard(f"binder-{size}", rnd, band=band, wear=0.85, rough=0.62)
    ob = box("binder", 0, 0, 0, C, S, H, mat)
    # the window into the binder (on the cover, x along the cover): inset from the cover's left/right, deep into it
    ins, top, btop, bh = [v * PX for v in p["rec"]]
    win_l, win_r = ins + 0.04, C - ins
    rz1, rz0 = H - top, H - btop - bh
    depth = S * 0.62
    cut(ob, box("c_window", win_l, -0.1, rz0, win_r, depth, rz1))
    # the spine face (x = 0): label pocket and finger hole like the closed spines (same proportions)
    m = round(S * 0.25, 2)
    lt = H - (0.32 if size == "l" else 0.28)
    lb = lt - round(H * 0.42, 2)
    cut(ob, box("c_label", -0.1, m, lb, 0.04, S - m, lt))
    hz, hr = (1.16, 0.29) if size == "l" else (0.92, 0.22)
    bpy.ops.mesh.primitive_cylinder_add(vertices=64, radius=hr, depth=0.9, location=(0.30, S / 2, hz), rotation=(0, math.pi / 2, 0))
    hc = bpy.context.active_object
    hc.name = "c_hole"
    cut(ob, hc)
    add_bevel(ob, 0.035 if size == "l" else 0.028, 4)
    smooth(ob)
    extra = []
    steel = mat_brushed(f"grommet-b{size}", rnd, level=0.62, rough=0.32)
    bpy.ops.mesh.primitive_torus_add(major_segments=96, minor_segments=16, major_radius=hr + 0.012,
                                     minor_radius=0.034 if size == "l" else 0.027, location=(-0.004, S / 2, hz), rotation=(0, math.pi / 2, 0))
    ring = bpy.context.active_object
    ring.scale = (0.55, 1, 1)
    ring.data.materials.append(steel)
    smooth(ring)
    extra.append(ring)
    extra.append(box("hole_in", 0.25, S / 2 - hr - 0.1, hz - hr - 0.1, 0.30, S / 2 + hr + 0.1, hz + hr + 0.1,
                     mat_flat("inside", (0.035, 0.037, 0.04))))
    # the label in the pocket stands in the shade (no print: the app prints the code on the closed spines only)
    extra.append(box("label", 0.03, m + 0.015, lb + 0.015, 0.05, S - m - 0.015, lt - 0.015, mat_paper(f"blabel-{size}", rnd, 0.46),
                     bevel=0.004, segs=2))
    # inside the window: a dark back (the binder's inner board) and the inner board the boxes stand on
    extra.append(box("back", win_l - 0.02, depth - 0.02, rz0 - 0.02, win_r + 0.02, depth + 0.02, rz1 + 0.02,
                     mat_powder("back", rnd, level=0.07, tex="cardboard-grey", scale=2.5, wear=0.0)))
    ib = box("innerboard", win_l - 0.01, -0.005, rz0 - 0.01, win_r + 0.01, depth, rz0 + bh, mat_powder("inner", rnd, level=0.75),
             bevel=0.008, segs=2)
    smooth(ib)
    extra.append(ib)
    # the steel edge protector along the cover's foot (lever-arch binders stand on it)
    eh = 0.09 if size == "l" else 0.07
    kp = box("kante", 0.05, -0.016, -0.004, C - 0.02, 0.05, eh, mat_brushed(f"kante-{size}", rnd, level=0.58, rough=0.34), bevel=0.01, segs=3)
    smooth(kp)
    extra.append(kp)
    for e in extra:
        e.parent = ob
    ob.rotation_euler = (0, 0, a)
    bpy.context.view_layer.update()
    for e in extra:
        e.matrix_parent_inverse = Matrix.Identity(4)
    bpy.context.view_layer.update()
    cat = catchers(4.2, wall=False)
    objs = [ob] + extra

    def rects(fr):
        pts = [ob.matrix_world @ Vector(v) for v in ((0, S, 0), (0, S, H), (C, 0, 0), (C, 0, H), (0, 0, 0), (0, 0, H))]
        px = [to_px(fr, q) for q in pts]
        xs, ys = [q[0] for q in px], [q[1] for q in px]
        front = [round(min(xs), 1), round(min(ys), 1), round(max(xs) - min(xs), 1), round(max(ys) - min(ys), 1)]
        return {
            "front": front,
            "cover": rect_px(fr, ob, 0, 0, C, H),
            "band": rect_px(fr, ob, 0, band[0], C, band[1]),
            "recess": rect_px(fr, ob, win_l, rz0, win_r, rz1),
            "innerBoard": rect_px(fr, ob, win_l, rz0, win_r, rz0 + bh),
            "edge": rect_px(fr, ob, 0.05, 0, C - 0.02, eh, y=-0.016),
        }
    return objs, cat, {"kind": "front", "size": size, "binder": True}, rects


SLEEVES_L = [(0.44, 1.50), (0.50, 1.60), (0.58, 1.46), (0.64, 1.56)]
SLEEVES_S = [(0.36, 1.04), (0.42, 1.12), (0.48, 1.00), (0.54, 1.10)]


def sleeve(size, idx, pose="up"):
    """A small 'Noch leer' slipcase: printed cardboard in the system colour (body tint), a paper label with a colour tab
    (band tint) at its top. pose: up | lean (rests 6 deg on its left neighbour) | flat (lies on its side)."""
    table = SLEEVES_L if size == "l" else SLEEVES_S
    W, H = table[idx]
    D = 1.20 if size == "l" else 0.95
    rnd = random.Random(5000 + idx * 13 + (0 if size == "l" else 400) + {"up": 0, "lean": 77, "flat": 155}[pose])
    # label in object space of the upright sleeve
    lm = max(0.05, round(W * 0.16, 2))
    lt = H - (0.08 if size == "l" else 0.06)
    lb = lt - round(H * 0.58, 2)
    tab = (lt - (0.09 if size == "l" else 0.07), lt)
    mat = mat_cardboard(f"sleeve-{size}{idx}{pose}", rnd, band=None, wear=0.7, tex_scale=2.6)
    ob = box("sleeve", 0, 0, 0, W, D, H, mat)
    # a dented top corner on some
    if rnd.random() < 0.6:
        left = rnd.random() < 0.5
        d = rnd.uniform(0.03, 0.05)
        c = box("c_dent", -d, -0.2, -d, d, D + 0.2, d)
        c.location = ((0 if left else W), 0, H)
        c.rotation_euler = (0, math.radians(45), 0)
        cut(ob, c)
    add_bevel(ob, 0.012, 2)
    smooth(ob)
    paper = mat_paper(f"slabel-{size}{idx}{pose}", rnd, 0.66)
    lab = box("label", lm, -0.004, lb, W - lm, 0.004, lt, paper, bevel=0.003, segs=1)
    lab.parent = ob
    tabm = mat_cardboard(f"tab-{size}{idx}{pose}", rnd, band=(tab[0] - 1, tab[1] + 1), tint_body=False, wear=0.0, tex_scale=2.0)
    # the tab is a printed stripe on the label: a hair in front of it
    tb = box("tab", lm + 0.008, -0.0055, tab[0], W - lm - 0.008, -0.004, tab[1], tabm)
    tb.parent = ob
    for nd in tabm.node_tree.nodes:  # tab body untinted -> pure band
        if nd.name == "TINT_BODY":
            nd.outputs[0].default_value = 0.0
    objs = [ob, lab, tb]
    if pose == "lean":
        ob.rotation_euler = (0, math.radians(-6.5), 0)
        ob.location = (0, 0, 0)
    elif pose == "flat":
        # lies on its left side: the spine now runs along the board
        ob.rotation_euler = (0, math.radians(90), 0)
        ob.location = (0, 0, W)
    cat = catchers(D + 0.06)
    meta = {"kind": "sleeve", "size": size, "idx": idx, "pose": pose, "w": round(W * 100), "h": round(H * 100)}

    def rects(fr):
        return {"front": rect_px(fr, ob, 0, 0, W, H), "label": rect_px(fr, ob, lm, lb, W - lm, lt, y=-0.004),
                "tab": rect_px(fr, ob, lm, tab[0], W - lm, tab[1], y=-0.0055)}
    return objs, cat, meta, rects


def board(kind):
    """The shelf board: a black powder-coated steel shelf with a folded front lip, dark laminate on top."""
    T, D = (0.22, 3.0) if kind == "stage" else (0.12, 2.4)
    rnd = random.Random(7000 if kind == "stage" else 7100)
    L = 9.0
    lip = box("lip", -L / 2, 0, -T, L / 2, 0.03, 0, mat_powder("lip", rnd, level=0.75), bevel=0.012, segs=3)
    top = box("top", -L / 2, 0.03, -0.03, L / 2, D, 0, mat_laminate("lam", rnd))
    smooth(lip)
    # wall behind (shadow catcher) starts below the board: the board's shadow falls on it
    dark = mat_flat("catcher", (0.05, 0.052, 0.056))
    wall = box("wall", -60, 0.45, -3, 60, 0.95, -T + 0.001, dark)
    wall.visible_camera = False
    meta = {"kind": "board", "board": kind, "thickness": round(T * 100)}
    return [lip, top], [wall], meta, lambda fr: {
        "front": rect_px(fr, lip, -L / 2, -T, L / 2, 0), "top": rect_px(fr, top, -L / 2, 0, L / 2, 0, y=D)}


def bookend():
    """An L of bent sheet steel (powder-coated black) at the end of a row, turned 12 deg toward the room (R3 QA: at 28 deg
    with a punched slot its face read as a door; now mostly its folded edge shows, the back corner cut on a slope)."""
    rnd = random.Random(7300)
    # R3 QA 2: black powder coat vanished on the black room (a dark bar) – brushed steel, so the fold and its turn read
    # R3 QA 3 (still a dark plate without a shape at x 1520): light steel (#8C939C-ish with a bright folded edge), an L
    # whose foot points out of the row (40 px, its edge catches the light), turned only 12 deg so the L stays readable
    mat = mat_brushed("bookend", rnd, level=0.78, rough=0.30)
    t = 0.05
    up = box("upright", 0, 0, 0, t, 1.0, 1.30, mat)
    corner = box("c_corner", -0.1, 0.55, 0.95, 0.2, 1.4, 1.7)
    corner.rotation_euler = (math.radians(-40), 0, 0)
    cut(up, corner)
    add_bevel(up, 0.014, 3)
    foot = box("foot", 0, 0, 0, 0.40 + t, 1.0, 0.05, mat, bevel=0.012, segs=3)
    smooth(up)
    smooth(foot)
    pivot = bpy.data.objects.new("pivot", None)
    link(pivot)
    up.parent = pivot
    foot.parent = pivot
    pivot.rotation_euler = (0, 0, math.radians(-12))
    cat = catchers(1.6)
    return [up, foot], cat, {"kind": "bookend"}, lambda fr: {
        "foot": [round(v, 1) for v in to_px(fr, Vector((0, 0, 0)))]}


def tag(size):
    """The count tag: a small anodised aluminium plate riveted to the spine (the app prints the number on it)."""
    W, H = (0.96, 0.28) if size == "l" else (0.72, 0.22)
    rnd = random.Random(7400 if size == "l" else 7450)
    pl = box("plate", 0, 0, 0, W, 0.025, H, mat_brushed("tag", rnd, level=0.20, rough=0.45), bevel=0.01, segs=3)
    smooth(pl)
    rv = mat_brushed("rivet", rnd, level=0.75, rough=0.25)
    objs = [pl]
    rr = 0.036 if size == "l" else 0.028
    for x in (0.075 if size == "l" else 0.06, W - (0.075 if size == "l" else 0.06)):
        bpy.ops.mesh.primitive_uv_sphere_add(radius=rr, segments=24, ring_count=12, location=(x, -0.002, H / 2))
        s = bpy.context.active_object
        s.scale = (1, 0.6, 1)
        s.data.materials.append(rv)
        smooth(s)
        objs.append(s)
    cat = catchers(0.026, floor=False, wall=True, wall_from=-5, wall_to=5)
    edge = 0.14 if size == "l" else 0.11
    return objs, cat, {"kind": "tag", "size": size, "slice": round(edge * 100)}, lambda fr: {"front": rect_px(fr, pl, 0, 0, W, H)}


RAIL_L, RAIL_H, RAIL_D = 6.0, 0.24, 0.14


def rail(notch=False):
    """R3 QA 3: the position rail as a steel running rail (24 px): a C-profile of bent sheet steel – a scale lip on top,
    the slot between the lips dark, a lower lip – with a screw at each end. notch=True cuts one 2 px notch into the top
    lip at the middle (build_sprites turns the difference into the notch overlay the app sets once per Fach)."""
    rnd = random.Random(7500)
    L, H, D = RAIL_L, RAIL_H, RAIL_D
    steel = mat_brushed("rail", rnd, level=0.72, rough=0.34)
    back = box("back", 0, D - 0.03, 0, L, D, H, steel, bevel=0.008, segs=2)
    top = box("lip_t", 0, 0, H - 0.075, L, D - 0.03, H, steel)
    low = box("lip_b", 0, 0, 0, L, D - 0.03, 0.06, steel, bevel=0.012, segs=3)
    if notch:
        cut(top, box("c_notch", L / 2 - 0.01, -0.1, H - 0.045, L / 2 + 0.01, D, H + 0.1))
    add_bevel(top, 0.012, 3)
    for o in (back, top, low):
        smooth(o)
    slot = box("slot", 0.02, D - 0.035, 0.06, L - 0.02, D - 0.03, H - 0.075, mat_flat("slot", (0.018, 0.019, 0.021)))
    objs = [back, top, low, slot]
    for x in (0.12, L - 0.12):
        bpy.ops.mesh.primitive_cylinder_add(vertices=32, radius=0.03, depth=0.02, location=(x, D - 0.04, (H - 0.075 + 0.06) / 2), rotation=(math.pi / 2, 0, 0))
        sc = bpy.context.active_object
        sc.data.materials.append(mat_brushed("screw", rnd, level=0.72, rough=0.28))
        add_bevel(sc, 0.008, 2)
        smooth(sc)
        objs.append(sc)
    cat = catchers(D + 0.001, floor=False, wall=True, wall_from=-5, wall_to=5)
    return objs, cat, {"kind": "rail", "slice": 24}, lambda fr: {"front": rect_px(fr, back, 0, 0, L, H, y=0),
                                                                   "notch": [round(to_px(fr, Vector((L / 2, 0, H)))[0], 1), 0, 0, 0]}


def reiter():
    """R3 QA 3: the rail's slider is an index tab (Reiter) – a paper tab held in a steel clip that rides on the rail's
    top lip. The system colour is only a 4 px printed stripe at the top of the paper (band tint). Sliced horizontally:
    the app builds it to the width of the code it prints on it."""
    rnd = random.Random(7600)
    W = 0.80
    paper = mat_paper("reiter", rnd, 0.70)
    tab = box("tab", 0, 0.02, 0.07, W, 0.03, 0.36, paper, bevel=0.006, segs=2)
    smooth(tab)
    stripe_m = mat_cardboard("stripe", rnd, band=(-1, 9), tint_body=False, wear=0.0, tex_scale=2.0)
    stripe = box("stripe", 0.006, 0.0185, 0.31, W - 0.006, 0.02, 0.35, stripe_m)
    clip = box("clip", -0.01, -0.012, 0.0, W + 0.01, 0.05, 0.11, mat_brushed("clip", rnd, level=0.66, rough=0.30), bevel=0.012, segs=3)
    smooth(clip)
    cat = catchers(0.06, floor=False, wall=True, wall_from=-5, wall_to=5)
    return [tab, stripe, clip], cat, {"kind": "reiter", "slice": 10}, lambda fr: {
        "front": rect_px(fr, clip, -0.01, 0.0, W + 0.01, 0.36, y=-0.012),
        "paper": rect_px(fr, tab, 0, 0.11, W, 0.31, y=0.02)}


def sign():
    """R3 QA 3: "Fach x von y" on a paper card in a steel clamp frame (Klemmrahmen) – the binders' label paper between two
    folded steel lips, a screw head in each end cap. Sliced horizontally; the app prints the position in ink."""
    rnd = random.Random(7650)
    L, H = 2.4, 0.34
    steel = mat_brushed("frame", rnd, level=0.52, rough=0.36)
    back = box("back", 0, 0.04, 0, L, 0.06, H, steel, bevel=0.01, segs=2)
    lt = box("lip_t", 0, 0, H - 0.05, L, 0.05, H, steel, bevel=0.012, segs=3)
    lb = box("lip_b", 0, 0, 0, L, 0.05, 0.05, steel, bevel=0.012, segs=3)
    cl = box("cap_l", 0, 0, 0, 0.12, 0.05, H, steel, bevel=0.012, segs=3)
    cr = box("cap_r", L - 0.12, 0, 0, L, 0.05, H, steel, bevel=0.012, segs=3)
    paper = box("paper", 0.12, 0.025, 0.04, L - 0.12, 0.04, H - 0.04, mat_paper("sign", rnd, 0.68))
    objs = [back, lt, lb, cl, cr, paper]
    for o in objs:
        smooth(o)
    for x in (0.06, L - 0.06):
        bpy.ops.mesh.primitive_cylinder_add(vertices=32, radius=0.026, depth=0.02, location=(x, -0.004, H / 2), rotation=(math.pi / 2, 0, 0))
        sc = bpy.context.active_object
        sc.data.materials.append(mat_brushed("screw", rnd, level=0.75, rough=0.28))
        add_bevel(sc, 0.008, 2)
        smooth(sc)
        objs.append(sc)
    cat = catchers(0.061, floor=False, wall=True, wall_from=-5, wall_to=5)
    return objs, cat, {"kind": "sign", "slice": 14}, lambda fr: {"front": rect_px(fr, back, 0, 0, L, H, y=0),
                                                                    "paper": rect_px(fr, paper, 0.12, 0.05, L - 0.12, H - 0.05, y=0.025)}


def tv():
    """R3 QA 3: the Start TV's cabinet, rendered (generic set, no brand, no product silhouette): graphite moulded plastic,
    a stepped recess around the glass (the app draws the picture into it), a chin with a power LED lens and a speaker
    grille of seven cut slots, two feet. Laid out like PixelScreen at 560 px (bezel 12 + 12, chin 40, feet 8); the app
    nine-slices it to every set size (the feet and the chin's LED/grille sit in the corner columns)."""
    rnd = random.Random(7800)
    W, Dp = 5.60, 1.6
    feet, chin, cab, rec = 0.08, 0.40, 0.12, 0.12
    glass_w = W - 2 * (cab + rec)
    glass_h = round(glass_w * 3 / 4, 2)
    Hb = cab + rec + glass_h + rec + chin        # body height without feet
    z0 = feet
    body_m = mat_powder("cabinet", rnd, level=0.46, tex="plastic-scuffed", scale=1.8, wear=0.35)
    body = box("body", 0, 0, z0, W, Dp, z0 + Hb, body_m)
    rx0, rx1 = cab, W - cab
    rz1 = z0 + Hb - cab
    rz0 = rz1 - (rec + glass_h + rec)
    cut(body, box("c_recess", rx0, -0.1, rz0, rx1, 0.10, rz1))
    # chin: LED lens (round cut) left under the glass, seven grille slots right under the glass
    cz = z0 + chin / 2
    gx1 = W - cab - rec
    bpy.ops.mesh.primitive_cylinder_add(vertices=32, radius=0.035, depth=0.2, location=(cab + rec + 0.03, 0, cz), rotation=(math.pi / 2, 0, 0))
    led = bpy.context.active_object
    cut(body, led)
    for i in range(7):
        zz = cz + (i - 3) * 0.03
        cut(body, box(f"c_slot{i}", gx1 - 1.20, -0.1, zz - 0.005, gx1, 0.025, zz + 0.005))
    add_bevel(body, 0.03, 4)
    smooth(body)
    deep = mat_flat("deep", (0.012, 0.013, 0.015), rough=0.6)
    floor_in = box("glassbed", rx0 + 0.01, 0.10, rz0 + 0.01, rx1 - 0.01, 0.12, rz1 - 0.01, deep)
    lens = box("lens", cab + rec, 0.02, cz - 0.03, cab + rec + 0.06, 0.04, cz + 0.03, mat_flat("lens", (0.02, 0.021, 0.024), rough=0.25))
    objs = [body, floor_in, lens]
    fw = 0.64
    for fx in (round(W * 0.12, 2), round(W * 0.88 - fw, 2)):
        f = box(f"foot{fx}", fx, 0.2, 0, fx + fw, Dp - 0.2, z0 + 0.01, mat_powder("foot", rnd, level=0.3, tex="plastic-scuffed", scale=1.6, wear=0.1),
                bevel=0.01, segs=2)
        smooth(f)
        objs.append(f)
    cat = catchers(Dp + 0.3)
    return objs, cat, {"kind": "tv"}, lambda fr: {
        "front": rect_px(fr, body, 0, 0, W, z0 + Hb),
        "body": rect_px(fr, body, 0, z0, W, z0 + Hb),
        "glass": rect_px(fr, body, rx0 + rec, rz0 + rec, rx1 - rec, rz1 - rec),
        "led": rect_px(fr, body, cab + rec, cz - 0.03, cab + rec + 0.06, cz + 0.03)}


def slider():
    """The rail's slider: a small steel carriage with an anodised inlay that takes the current system's colour."""
    rnd = random.Random(7600)
    W, H, D = 0.22, 0.32, 0.20
    body = box("carriage", 0, 0, 0, W, D, H, mat_brushed("carriage", rnd, level=0.62, rough=0.3))
    c = box("c_window", 0.06, -0.1, 0.07, W - 0.06, 0.02, H - 0.07)
    cut(body, c)
    add_bevel(body, 0.015, 3)
    smooth(body)
    inlay = box("inlay", 0.06, 0.02, 0.07, W - 0.06, 0.05, H - 0.07, mat_brushed("inlay", rnd, level=0.9, rough=0.35, tint=True))
    cat = catchers(D + 0.02, floor=False, wall=True, wall_from=-5, wall_to=5)
    return [body, inlay], cat, {"kind": "slider"}, lambda fr: {"front": rect_px(fr, body, 0, 0, W, H)}


def case():
    """A generic keep case (no product shape: a plain dark plastic box with a clear sleeve) standing in the front's
    compartment. The cover is the app's image: rendered here as a white card (body tint) behind the sleeve, so the
    build step can turn it into a shade map (darkening over the cover) and the sleeve's gloss (in the base pass)."""
    W, H, D = 1.20, 1.50, 0.16
    rnd = random.Random(7700)
    shell = box("shell", 0, 0, 0, W, D, H, mat_powder("case", rnd, level=0.24, tex="plastic-scuffed", scale=1.6, wear=0.25))
    wl, wr, wb, wt = 0.095, W - 0.045, 0.05, H - 0.05
    cut(shell, box("c_window", wl, -0.1, wb, wr, 0.012, wt))
    for gx in (0.026, 0.052):   # two hinge grooves on the spine side
        cut(shell, box(f"c_hinge{gx}", gx, -0.1, 0.06, gx + 0.007, 0.005, H - 0.06))
    add_bevel(shell, 0.012, 3)
    smooth(shell)
    cm = bpy.data.materials.new("cover")
    nb = NB(cm)
    t = nb.val(0.0, "TINT_BODY")
    TINT_NODES["body"].append(t.node)
    cn = nb.n("ShaderNodeCombineColor")
    for i in range(3):
        nb.link(t, cn.inputs[i])
    principled(nb, cn.outputs[0], 0.55, spec=0.5)
    cover = box("cover", wl + 0.002, 0.009, wb + 0.002, wr - 0.002, 0.0115, wt - 0.002, cm)   # in front of the recess floor
    # the clear sleeve over the whole front, faintly scuffed
    sm = bpy.data.materials.new("sleeve")
    nb = NB(sm)
    dcol, _ = textured(nb, "dust-scratches", 1.2, rnd, 0.2)
    bw = nb.n("ShaderNodeRGBToBW")
    nb.link(dcol, bw.inputs[0])
    p = principled(nb, (1, 1, 1, 1), nb.math("ADD", 0.06, nb.math("MULTIPLY", bw.outputs[0], 0.5)), spec=0.5)
    p.inputs["Transmission Weight"].default_value = 1.0
    p.inputs["IOR"].default_value = 1.45
    sl = box("sleeve", 0.006, -0.004, 0.006, W - 0.006, -0.0025, H - 0.006, sm)
    sl.visible_shadow = False   # Cycles' glass would block the direct light (no caustics): the film only reflects
    cat = catchers(D + 0.22)
    meta = {"kind": "case", "slice": {"l": 13, "t": 8, "r": 8, "b": 8}}
    return [shell, cover, sl], cat, meta, lambda fr: {
        "front": rect_px(fr, shell, 0, 0, W, H), "window": rect_px(fr, shell, wl, wb, wr, wt, y=0.012)}


# --------------------------------------------------------------------------------------------------------------- passes

def set_tint(band, body):
    for nd in TINT_NODES["band"]:
        nd.outputs[0].default_value = band
    for nd in TINT_NODES["body"]:
        nd.outputs[0].default_value = body


def render_np(path_stub):
    sc = bpy.context.scene
    p = path_stub + ".exr"
    sc.render.filepath = p
    bpy.ops.render.render(write_still=True)
    img = bpy.data.images.load(p)
    w, h = img.size
    buf = np.empty(w * h * 4, dtype=np.float32)
    img.pixels.foreach_get(buf)
    bpy.data.images.remove(img)
    os.remove(p)
    return buf.reshape(h, w, 4)[::-1].copy()


def run(name, builder, pad=24, extra=(0, 0, 0, 0)):
    reset_scene()
    objs, cat, meta, rects = builder()
    fr = frame(objs, pad, extra)
    meta.update({"canvas": [fr["w"], fr["h"]], "scale": 2}, **rects(fr))
    d = os.path.join(OUT, name)
    os.makedirs(d, exist_ok=True)
    stub = os.path.join(TMP, name)
    passes = [("base", 0, 0)]
    if TINT_NODES["band"]:
        passes.append(("band", 1, 0))
    if TINT_NODES["body"]:
        passes.append(("body", 0, 1))
    for pname, b, y in passes:
        set_tint(b, y)
        np.save(os.path.join(d, pname + ".npy"), render_np(stub))
    # shadow: object hidden from the camera, the catchers receive its shadow
    set_tint(0, 0)
    for o in objs:
        o.visible_camera = False
    for c in cat:
        c.visible_camera = True
        c.is_shadow_catcher = True
    np.save(os.path.join(d, "shadow.npy"), render_np(stub))
    with open(os.path.join(d, "meta.json"), "w") as f:
        json.dump(meta, f, indent=1)
    print("DONE", name, meta["canvas"], flush=True)


def main():
    jobs = []
    want = set(JOBS)
    every = "all" in want
    for size in ("m", "xs"):
        if every or f"spine-{size}" in want:
            for v in range(3):
                jobs.append((f"spine-{size}{v}", (lambda s=size, v=v: spine(s, v)), 16, (0, 0, 0, 0)))
        if every or f"front-{size}" in want:
            jobs.append((f"front-{size}", (lambda s=size: front(s, 0)), 16, (0, 0, 0, 0)))
    for size in ("l", "s"):
        if every or f"spine-{size}" in want:
            for v in range(3):
                jobs.append((f"spine-{size}{v}", (lambda s=size, v=v: spine(s, v)), 24, (0, 0, 0, 0)))
        if every or f"front-{size}" in want:
            jobs.append((f"front-{size}", (lambda s=size: binder_front(s)), 24, (0, 0, 0, 0)))
        if every or f"sleeves-{size}" in want:
            for i in range(4):
                jobs.append((f"sleeve-{size}{i}", (lambda s=size, i=i: sleeve(s, i)), 16, (0, 0, 0, 0)))
            jobs.append((f"sleeve-{size}lean", (lambda s=size: sleeve(s, 1, "lean")), 16, (0, 0, 0, 0)))
            jobs.append((f"sleeve-{size}flat", (lambda s=size: sleeve(s, 3, "flat")), 16, (0, 0, 0, 0)))
        if every or f"tag-{size}" in want:
            jobs.append((f"tag-{size}", (lambda s=size: tag(s)), 8, (0, 0, 0, 0)))
    for k in ("stage", "edge"):
        if every or f"board-{k}" in want:
            jobs.append((f"board-{k}", (lambda k=k: board(k)), 0, (0, 0, 0, 40)))
    if every or "bookend" in want:
        jobs.append(("bookend", bookend, 16, (0, 0, 0, 0)))
    if every or "rail" in want:
        jobs.append(("rail", rail, 8, (0, 0, 0, 0)))
        jobs.append(("rail-notch", (lambda: rail(notch=True)), 8, (0, 0, 0, 0)))
    if every or "reiter" in want:
        jobs.append(("reiter", reiter, 8, (0, 0, 0, 0)))
    if every or "sign" in want:
        jobs.append(("sign", sign, 8, (0, 0, 0, 0)))
    if every or "tv" in want:
        jobs.append(("tv", tv, 16, (0, 0, 0, 0)))
    if every or "case" in want:
        jobs.append(("case", case, 12, (0, 0, 0, 0)))
    for name, b, pad, extra in jobs:
        run(name, b, pad, extra)


if __name__ == "__main__":
    main()
