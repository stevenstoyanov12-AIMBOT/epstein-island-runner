"""Render the statue's eye charge-up as a 4x4 flipbook in Blender (16 frames, 256 px each).

Frame 0 = start of the charge, frame 15 = just before the laser fires. Energy is drawn emissive on black, then
converted so the alpha is the brightness (for additive blending in Unity) and given a soft glow.
  - three thin rings close in on the eye one after another, brightening as they tighten
  - four spiralling streaks of energy are pulled inward, winding faster as the charge builds
  - a core swells from a dim red ember to a white-hot point

usage: python3 charge_flipbook.py [OUT.png] [--preview DIR]
"""
import argparse
import math
import os

import bpy
import numpy as np
from PIL import Image
from scipy import ndimage

HERE = os.path.dirname(os.path.abspath(__file__))
ap = argparse.ArgumentParser()
ap.add_argument("out", nargs="?", default=os.path.join(HERE, "..", "..", "UnityGame", "Assets", "Models", "Statues",
                                                     "Textures", "eye_charge.png"))
ap.add_argument("--preview", default=None)
args = ap.parse_args()

FRAMES, CELL = 16, 256


def emissive(name, colour, strength):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    em = nt.nodes.new("ShaderNodeEmission")
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    nt.links.new(em.outputs[0], out.inputs[0])
    em.inputs["Color"].default_value = (*colour, 1)
    em.inputs["Strength"].default_value = strength
    return m, em


def ring(name):
    bpy.ops.mesh.primitive_torus_add(major_radius=1.0, minor_radius=0.007, major_segments=96, minor_segments=8)
    o = bpy.context.active_object
    o.name = name
    return o


def spiral(name, phase):
    cu = bpy.data.curves.new(name, "CURVE")
    cu.dimensions = "3D"
    cu.bevel_depth = 0.006
    cu.bevel_resolution = 2
    sp = cu.splines.new("POLY")
    sp.points.add(39)
    o = bpy.data.objects.new(name, cu)
    bpy.context.collection.objects.link(o)
    return o, sp


def set_spiral(sp, phase, k, turn):
    # an arc from far out winding in toward the centre; its tail thins by shrinking the radius of later points
    n = len(sp.points)
    outer = 1.05 - 0.55 * k
    for i in range(n):
        f = i / (n - 1)
        r = outer * (1 - f) ** 1.3 + 0.03
        a = phase + turn + f * (0.8 + 0.5 * k) * math.pi
        sp.points[i].co = (r * math.cos(a), r * math.sin(a), 0, 1)


bpy.ops.wm.read_factory_settings(use_empty=True)
sc = bpy.context.scene
w = bpy.data.worlds.new("W"); sc.world = w; w.use_nodes = True
w.node_tree.nodes["Background"].inputs["Color"].default_value = (0, 0, 0, 1)

cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
sc.collection.objects.link(cam); sc.camera = cam
cam.data.type = "ORTHO"
cam.data.ortho_scale = 2.4
cam.location = (0, 0, 5)

ring_mat, ring_em = emissive("Ring", (1.0, 0.06, 0.02), 1)
spiral_mat, spiral_em = emissive("Spiral", (1.0, 0.1, 0.03), 1)
core_mat, core_em = emissive("Core", (1.0, 0.3, 0.1), 3)

rings = [ring(f"Ring{i}") for i in range(3)]
for r in rings:
    r.data.materials.append(ring_mat)
spirals = []
for i in range(4):
    o, sp = spiral(f"Spiral{i}", i)
    o.data.materials.append(spiral_mat)
    spirals.append((o, sp, i * math.pi * 2 / 4))
bpy.ops.mesh.primitive_uv_sphere_add(radius=1.0, segments=32, ring_count=16)
core = bpy.context.active_object
core.data.materials.append(core_mat)

sc.render.engine = "CYCLES"
sc.cycles.samples = 24
sc.cycles.use_denoising = True
sc.render.resolution_x = sc.render.resolution_y = CELL
sc.view_settings.view_transform = "Standard"

cells = []
for f in range(FRAMES):
    k = f / (FRAMES - 1)
    # rings: each closes in from the edge to the core over the charge, staggered, brightening as they tighten
    for i, r in enumerate(rings):
        t = (k * 1.6 - i * 0.3)
        t = min(max(t, 0.0), 1.0)
        rad = 1.0 - 0.85 * t
        r.scale = (rad, rad, rad)
        r.hide_render = t <= 0.0 or t >= 1.0
    ring_em.inputs["Strength"].default_value = 1.2 + 1.8 * k
    # spirals: pulled inward and wound faster as the charge builds
    for o, sp, ph in spirals:
        set_spiral(sp, ph, k, turn=k * 4.0)
        o.hide_render = k < 0.05
    spiral_em.inputs["Strength"].default_value = 0.8 + 1.4 * k
    # core: dim red ember to a white-hot point
    s = 0.04 + 0.1 * k ** 1.5
    core.scale = (s, s, s)
    core_em.inputs["Color"].default_value = (1.0, 0.08 + 0.8 * k ** 3, 0.04 + 0.75 * k ** 3, 1)
    core_em.inputs["Strength"].default_value = 1.5 + 6 * k ** 2
    path = os.path.join(bpy.app.tempdir, f"charge_{f:02d}.png")
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True)
    cells.append(np.asarray(Image.open(path).convert("RGB")).astype(float) / 255)

sheet = np.zeros((CELL * 4, CELL * 4, 4))
for f, c in enumerate(cells):
    # soft glow around the bright energy, and fade the whole thing in over the first frames
    glow = sum(np.stack([ndimage.gaussian_filter(c[..., ch], s) for ch in range(3)], -1) * wgt
               for s, wgt in ((2, 0.5), (6, 0.35), (14, 0.2)))
    rgb = np.clip(c + glow, 0, 1) * min(1.0, 0.35 + f / 5)
    alpha = rgb.max(-1)
    col = rgb / np.maximum(alpha[..., None], 1e-4)          # colour at full strength; alpha carries brightness
    row, colm = f // 4, f % 4
    sheet[row * CELL:(row + 1) * CELL, colm * CELL:(colm + 1) * CELL, :3] = col
    sheet[row * CELL:(row + 1) * CELL, colm * CELL:(colm + 1) * CELL, 3] = alpha
os.makedirs(os.path.dirname(os.path.abspath(args.out)), exist_ok=True)
Image.fromarray((np.clip(sheet, 0, 1) * 255).astype(np.uint8), "RGBA").save(args.out)
print(args.out)
if args.preview:
    os.makedirs(args.preview, exist_ok=True)
    frames = [Image.fromarray((np.clip(sheet[r * CELL:(r + 1) * CELL, c * CELL:(c + 1) * CELL, :3]
                                       * sheet[r * CELL:(r + 1) * CELL, c * CELL:(c + 1) * CELL, 3:], 0, 1) * 255).astype(np.uint8))
              for r in range(4) for c in range(4)]
    frames[0].save(os.path.join(args.preview, "charge.gif"), save_all=True, append_images=frames[1:] + [frames[-1]] * 4,
                   duration=94, loop=0)
    strip = Image.new("RGB", (CELL * 4, CELL))
    for i, fi in enumerate((0, 5, 10, 15)):
        strip.paste(frames[fi], (i * CELL, 0))
    strip.save(os.path.join(args.preview, "charge_strip.png"))
