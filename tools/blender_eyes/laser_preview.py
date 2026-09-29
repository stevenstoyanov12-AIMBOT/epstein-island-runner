"""Render a short animated preview of the statue's laser attack in Blender (Cycles + glare for bloom):
charge motes spiralling into the eyes, then the three-layer beams firing. Uses the same spiral maths and
beam layers as LaserEye.cs, so what you see here is what the game draws.

usage: python3 laser_preview.py OUT_DIR   ->  OUT_DIR/laser.gif and frame PNGs
"""
import math
import os
import sys

import bpy
import numpy as np
import trimesh
from mathutils import Euler, Quaternion, Vector
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
STATUES = os.path.join(HERE, "..", "..", "UnityGame", "Assets", "Models", "Statues")
OUT = sys.argv[1] if len(sys.argv) > 1 else "laser_preview"
os.makedirs(OUT, exist_ok=True)

# eye centres, in the game's pivot space (x right, y up, z forward) - same numbers as StatueSceneBuilder
EYES = [(-0.033, 0.5995, 0.067), (0.031, 0.5995, 0.066)]
TARGET = Vector((-1.2, -3.5, 0.9))      # where the beams go (test space), off to the viewer's side


def to_test(p):
    """pivot space -> this scene (bust mesh space): X = -x, Y = -z (face looks toward -Y), Z = y."""
    return Vector((-p[0], -p[2], p[1]))


def emission(name, colour, strength, alpha=1.0):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    em = nt.nodes.new("ShaderNodeEmission")
    em.inputs["Color"].default_value = (*colour, 1)
    em.inputs["Strength"].default_value = strength
    if alpha < 1:
        tr = nt.nodes.new("ShaderNodeBsdfTransparent")
        mix = nt.nodes.new("ShaderNodeMixShader")
        mix.inputs[0].default_value = alpha
        nt.links.new(tr.outputs[0], mix.inputs[1])
        nt.links.new(em.outputs[0], mix.inputs[2])
        nt.links.new(mix.outputs[0], out.inputs[0])
    else:
        nt.links.new(em.outputs[0], out.inputs[0])
    return m


def cylinder(name, a, b, radius, mat):
    d = b - a
    bpy.ops.mesh.primitive_cylinder_add(vertices=16, radius=radius, depth=d.length, location=(a + b) / 2)
    o = bpy.context.active_object
    o.name = name
    o.rotation_mode = "QUATERNION"
    o.rotation_quaternion = d.to_track_quat("Z", "Y")
    o.data.materials.append(mat)
    return o


bpy.ops.wm.read_factory_settings(use_empty=True)
sc = bpy.context.scene

# bust
m = trimesh.load(os.path.join(STATUES, "Bust_Portrait.obj"), force="mesh")
v = m.vertices
me = bpy.data.meshes.new("Bust")
me.from_pydata(np.column_stack([v[:, 0], v[:, 2], v[:, 1]]).tolist(), [], m.faces[:, ::-1].tolist())
for p in me.polygons:
    p.use_smooth = True
marble = bpy.data.materials.new("Marble")
marble.use_nodes = True
marble.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.75, 0.68, 0.55, 1)
me.materials.append(marble)
sc.collection.objects.link(bpy.data.objects.new("Bust", me))

iris = emission("Iris", (1.0, 0.08, 0.03), 3)
mote_mat = emission("Mote", (1.0, 0.25, 0.08), 25)
core_mat = emission("Core", (1.0, 0.85, 0.8), 40)
glow_mat = emission("Glow", (1.0, 0.06, 0.02), 12, alpha=0.6)
haze_mat = emission("Haze", (1.0, 0.03, 0.01), 3, alpha=0.18)

centres = [to_test(e) for e in EYES]
forward = Vector((0, -1, 0))
for c in centres:   # glowing irises
    bpy.ops.mesh.primitive_uv_sphere_add(radius=0.0065, location=c + forward * 0.0125)
    bpy.context.active_object.scale = (1, 0.35, 1)
    bpy.context.active_object.data.materials.append(iris)

# charge motes: same spiral as LaserEye.Charge (per eye, 28 motes with random phase, speed, tilt)
rng = np.random.default_rng(7)
N = 28
motes = []
for ei, c in enumerate(centres):
    for i in range(N):
        bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=1, radius=1.0)
        o = bpy.context.active_object
        o.data.materials.append(mote_mat)
        tilt = Euler((math.radians(rng.uniform(-35, 35)), math.radians(rng.uniform(-35, 35)), math.radians(rng.uniform(0, 360)))).to_quaternion()
        motes.append((o, c, rng.random(), rng.uniform(1.6, 2.6), tilt))

# lights and camera
w = bpy.data.worlds.new("W"); sc.world = w; w.use_nodes = True
w.node_tree.nodes["Background"].inputs["Color"].default_value = (0.01, 0.012, 0.03, 1)
moon = bpy.data.objects.new("Moon", bpy.data.lights.new("Moon", "SUN"))
moon.data.energy = 0.6; moon.data.color = (0.6, 0.7, 1.0)
moon.rotation_euler = (math.radians(50), 0, math.radians(-30))
sc.collection.objects.link(moon)
cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam")); sc.collection.objects.link(cam); sc.camera = cam
cam.data.lens = 28
cam.location = (0.55, -0.95, 0.72)
cam.rotation_euler = (Vector((-0.12, -0.3, 0.6)) - cam.location).to_track_quat("-Z", "Y").to_euler()

sc.render.engine = "CYCLES"
sc.cycles.samples = 16
sc.cycles.use_denoising = True
sc.render.resolution_x, sc.render.resolution_y = 480, 360
sc.view_settings.view_transform = "AgX" if "AgX" in [i.identifier for i in sc.view_settings.bl_rna.properties["view_transform"].enum_items] else "Filmic"

FPS = 24
CHARGE, BEAM = 36, 30
frames = []
beams = []
for f in range(0, CHARGE + BEAM, 2):
    t_sec = f / FPS
    for o in beams:
        bpy.data.objects.remove(o)
    beams = []
    if f < CHARGE:
        k = f / CHARGE
        shown = round(6 + (N - 6) * k)
        for idx, (o, c, phase, speed, tilt) in enumerate(motes):
            i = idx % N
            on = i < shown
            o.hide_render = not on
            if not on:
                continue
            t = (t_sec * speed * (0.8 + 0.8 * k) + phase) % 1.0
            radius = 0.08 * (1 - t) ** 1.6
            ang = (phase + t * 1.75) * 2 * math.pi
            local = tilt @ Vector((math.cos(ang), math.sin(ang), 0)) * radius + Vector((0, 0, 1)) * radius * 0.6
            o.location = c + forward * 0.015 + to_test(local)
            s = (0.004 + 0.008 * t) * (0.7 + 0.6 * k) / 2
            o.scale = (s, s, s)
    else:
        for o, *_ in motes:
            o.hide_render = True
        age = (f - CHARGE) / FPS
        fade = min(1.0, (BEAM - (f - CHARGE)) / 6)
        snap = math.exp(-age * 12)
        for c in centres:
            a = c + forward * 0.01
            beams.append(cylinder("Core", a, TARGET, (0.006 + 0.01 * snap) / 2 * fade, core_mat))
            beams.append(cylinder("Glow", a, TARGET, (0.02 + 0.03 * snap) / 2 * fade, glow_mat))
            beams.append(cylinder("Haze", a, TARGET, (0.06 + 0.08 * snap) / 2 * math.sqrt(fade), haze_mat))
    path = os.path.join(OUT, f"f{f:03d}.png")
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True)
    frames.append(path)

from scipy import ndimage


def bloom(path):
    """Soft bloom like Unity's: blur only the brightest parts and add them back."""
    a = np.asarray(Image.open(path).convert("RGB")).astype(float) / 255
    bright = np.clip(a - 0.75, 0, None) * 4
    glow = sum(np.stack([ndimage.gaussian_filter(bright[..., c], s) for c in range(3)], -1) * wgt
               for s, wgt in ((3, 0.5), (9, 0.35), (22, 0.25)))
    return Image.fromarray((np.clip(a + glow, 0, 1) * 255).astype(np.uint8))


imgs = [bloom(p) for p in frames]
imgs[len(imgs) // 3].save(os.path.join(OUT, "charge.png"))
imgs[CHARGE // 2 + 4].save(os.path.join(OUT, "beam.png"))
imgs[0].save(os.path.join(OUT, "laser.gif"), save_all=True, append_images=imgs[1:], duration=int(2000 / FPS), loop=0)
print("gif", os.path.join(OUT, "laser.gif"))
