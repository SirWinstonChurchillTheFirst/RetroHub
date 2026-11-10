"""RetroHub R3: the category motifs as small printed reliefs (embossed board with ink), rendered headless in Cycles.

Run (CPU only, never without -b, always --factory-startup):
  blender.exe -b --factory-startup -t 8 -P blender_motifs.py -- <assets-r3 dir> <Motif>[,<Motif>...|all] [--samples N]
Input: <dir>/motifs-src/<Motif>.npz from motif_maps.py (the app's MotifCatalog, rasterised on the 120 grid).
Light, camera and scale are blender_props.py's (same key/fill/top box, same oblique ortho view, 200 px per BU at 2x).
The motif's 120 grid is 1.6 BU = 160 px at 1x / 320 px at 2x.

Per motif it writes <dir>/props/motif-<Motif>/ :
  base.npy   every tinted part at albedo 0 (cuts are dark gloss ink, the relief's sheen and edges are in it)
  lit.npy    every tinted part at albedo 1 (all three tints at once - the parts never overlap)
  role.npy   flat emission of the role coverage (R Color, G Body, B Lift|Light) for splitting lit-base per role
  shadow.npy the relief invisible, the recess floor behind it as shadow catcher
  meta.json  canvas and the 120-grid rect ("front") in 2x px
"""
import json
import os
import random
import sys

import bpy
import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import blender_props as bp  # noqa: E402  (same argv layout: <dir> <jobs> [--samples N])

SIZE = 1.6
UNIT = SIZE / 120.0


def relief_mesh(name, h):
    res = h.shape[0]
    me = bpy.data.meshes.new(name)
    jj, ii = np.mgrid[0:res, 0:res]
    x = (ii + 0.5) / res * SIZE
    z = SIZE - (jj + 0.5) / res * SIZE
    y = -h * UNIT
    co = np.stack([x, y, z], -1).reshape(-1, 3).astype(np.float32)
    me.vertices.add(len(co))
    me.vertices.foreach_set("co", co.ravel())
    idx = np.arange(res * res).reshape(res, res)
    a, b, c, d = idx[:-1, :-1].ravel(), idx[:-1, 1:].ravel(), idx[1:, 1:].ravel(), idx[1:, :-1].ravel()
    # winding so the normal faces -Y (toward the camera)
    quads = np.stack([a, d, c, b], -1)
    nq = len(quads)
    me.loops.add(nq * 4)
    me.loops.foreach_set("vertex_index", quads.ravel().astype(np.int32))
    me.polygons.add(nq)
    me.polygons.foreach_set("loop_start", (np.arange(nq) * 4).astype(np.int32))
    me.polygons.foreach_set("loop_total", np.full(nq, 4, np.int32))
    uvl = me.uv_layers.new(name="UVMap")
    vu = ((ii + 0.5) / res).ravel()
    vv = (1 - (jj + 0.5) / res).ravel()
    uv = np.stack([vu, vv], -1)[quads.ravel()]
    uvl.data.foreach_set("uv", uv.astype(np.float32).ravel())
    me.update(calc_edges=True)
    me.validate()
    me.polygons.foreach_set("use_smooth", np.ones(nq, bool))
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    return ob


def role_image(name, cov):
    res = cov.shape[0]
    img = bpy.data.images.new(name, res, res, alpha=True, float_buffer=True)
    img.colorspace_settings.name = "Non-Color"
    img.alpha_mode = "CHANNEL_PACKED"
    px = np.zeros((res, res, 4), np.float32)
    px[..., 0:3] = cov[..., 0:3]
    px[..., 3] = cov[..., 3]
    img.pixels.foreach_set(px[::-1].ravel())
    img.pack()
    return img


def materials(img, rnd):
    """(the printed relief, the role emission) materials; returns them and the tint value nodes."""
    m = bpy.data.materials.new("relief")
    nb = bp.NB(m)
    uv = nb.n("ShaderNodeUVMap")
    uv.uv_map = "UVMap"
    it = nb.n("ShaderNodeTexImage", interpolation="Linear", extension="EXTEND")
    it.image = img
    nb.link(uv.outputs[0], it.inputs["Vector"])
    sep = nb.n("ShaderNodeSeparateColor")
    nb.link(it.outputs["Color"], sep.inputs[0])
    r, g, b = sep.outputs[0], sep.outputs[1], sep.outputs[2]
    cut = it.outputs["Alpha"]
    tints = [nb.val(0.0, f"T{k}") for k in range(3)]
    ink = nb.math("ADD", nb.math("ADD", nb.math("MULTIPLY", r, tints[0]), nb.math("MULTIPLY", g, tints[1])),
                  nb.math("MULTIPLY", b, tints[2]))
    # the board under the ink: its fibre shows faintly through the print
    col, _ = bp.textured(nb, "cardboard-grey", 1.4, rnd)
    bw = nb.n("ShaderNodeRGBToBW")
    nb.link(col, bw.inputs[0])
    grain = nb.math("ADD", nb.math("MULTIPLY", nb.math("SUBTRACT", bw.outputs[0], 0.74), 1.8), 0.86)
    # worn edges: on the relief's rims the ink is rubbed off and the light board shows (neutral: it lives in the base)
    wf, bv = bp.edge_wear(nb, rnd, radius=0.012, gain=6.0, noise_scale=40.0, threshold=0.5)
    wf = nb.math("MULTIPLY", wf, nb.math("SUBTRACT", 1.0, cut))
    wf = nb.math("MULTIPLY", wf, 0.8)
    keep = nb.math("SUBTRACT", 1.0, wf)
    alb = nb.math("ADD", nb.math("MULTIPLY", nb.math("MULTIPLY", ink, grain), keep), nb.math("MULTIPLY", cut, 0.03))
    alb = nb.math("ADD", alb, nb.math("MULTIPLY", nb.math("MULTIPLY", wf, nb.math("ADD", nb.math("ADD", r, g), b)), 0.42))
    cn = nb.n("ShaderNodeCombineColor")
    for i in range(3):
        nb.link(alb, cn.inputs[i])
    cover = nb.math("MINIMUM", nb.math("ADD", nb.math("ADD", r, g), nb.math("ADD", b, cut)), 1.0)
    rough = nb.math("SUBTRACT", 0.52, nb.math("MULTIPLY", cut, 0.22))
    nrm = bp.bump_from(nb, col, 0.35, 0.002, bv.outputs["Normal"])
    p = bp.principled(nb, cn.outputs[0], rough, spec=0.5, normal=nrm)
    tr = nb.n("ShaderNodeBsdfTransparent")
    mx = nb.n("ShaderNodeMixShader")
    nb.link(cover, mx.inputs[0])
    nb.link(tr.outputs[0], mx.inputs[1])
    nb.link(p.outputs[0], mx.inputs[2])
    nb.link(mx.outputs[0], nb.out.inputs["Surface"])

    e = bpy.data.materials.new("role")
    nb2 = bp.NB(e)
    uv2 = nb2.n("ShaderNodeUVMap")
    uv2.uv_map = "UVMap"
    it2 = nb2.n("ShaderNodeTexImage", interpolation="Linear", extension="EXTEND")
    it2.image = img
    nb2.link(uv2.outputs[0], it2.inputs["Vector"])
    sep2 = nb2.n("ShaderNodeSeparateColor")
    nb2.link(it2.outputs["Color"], sep2.inputs[0])
    tot = nb2.math("MINIMUM", nb2.math("ADD", nb2.math("ADD", sep2.outputs[0], sep2.outputs[1]),
                                       nb2.math("ADD", sep2.outputs[2], it2.outputs["Alpha"])), 1.0)
    em = nb2.n("ShaderNodeEmission")
    nb2.link(it2.outputs["Color"], em.inputs["Color"])
    em.inputs["Strength"].default_value = 1.0
    tr2 = nb2.n("ShaderNodeBsdfTransparent")
    mx2 = nb2.n("ShaderNodeMixShader")
    nb2.link(tot, mx2.inputs[0])
    nb2.link(tr2.outputs[0], mx2.inputs[1])
    nb2.link(em.outputs[0], mx2.inputs[2])
    nb2.link(mx2.outputs[0], nb2.out.inputs["Surface"])
    return m, e, [t.node for t in tints]


def run_motif(name):
    src = os.path.join(bp.ROOT, "motifs-src", f"{name}.npz")
    data = np.load(src)
    cov, h, third = data["role"], data["height"], str(data["third"])
    sc = bp.reset_scene()
    rnd = random.Random(9000 + sum(map(ord, name)))
    ob = relief_mesh("relief", h)
    img = role_image("role", cov)
    mat, emat, tints = materials(img, rnd)
    ob.data.materials.append(mat)
    dark = bp.mat_flat("catcher", (0.05, 0.052, 0.056))
    floor = bp.box("floor", -3, 0.002, -3, 5, 0.3, 5, dark)
    floor.visible_camera = False
    fr = bp.frame([ob], 12)
    meta = {"kind": "motif", "motif": name, "third": third, "canvas": [fr["w"], fr["h"]], "scale": 2,
            "front": bp.rect_px(fr, ob, 0, 0, SIZE, SIZE, y=0.0)}
    d = os.path.join(bp.OUT, f"motif-{name}")
    os.makedirs(d, exist_ok=True)
    stub = os.path.join(bp.TMP, f"motif-{name}")
    for pname, v in (("base", 0.0), ("lit", 1.0)):
        for t in tints:
            t.outputs[0].default_value = v
        np.save(os.path.join(d, pname + ".npy"), bp.render_np(stub))
    # role: flat emission, no denoise (it would smear the coverage)
    ob.data.materials[0] = emat
    samples = sc.cycles.samples
    sc.cycles.samples = 16
    sc.cycles.use_denoising = False
    np.save(os.path.join(d, "role.npy"), bp.render_np(stub))
    sc.cycles.samples = samples
    sc.cycles.use_denoising = True
    ob.data.materials[0] = mat
    for t in tints:
        t.outputs[0].default_value = 0.0
    ob.visible_camera = False
    floor.visible_camera = True
    floor.is_shadow_catcher = True
    np.save(os.path.join(d, "shadow.npy"), bp.render_np(stub))
    with open(os.path.join(d, "meta.json"), "w") as f:
        json.dump(meta, f, indent=1)
    print("DONE", f"motif-{name}", meta["canvas"], flush=True)


def main():
    want = bp.JOBS
    names = sorted(f[:-4] for f in os.listdir(os.path.join(bp.ROOT, "motifs-src")) if f.endswith(".npz"))
    if "all" not in want:
        names = [n for n in names if n in want]
    for n in names:
        run_motif(n)


if __name__ == "__main__":
    main()
