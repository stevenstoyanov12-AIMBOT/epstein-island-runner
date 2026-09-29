"""Render the bust with its eyes exactly as the Unity scene builds them, to check the socket fit.

The eyeball and pupil geometry mirror StatueSceneBuilder.EyeMesh / EyeSurface. Positions are in the bust's
pivot space (x, height, forward). --look tilts the pupils (degrees right, up) to check their full travel.
usage: python3 fit_test.py OUT.png [--x 0.037 --y 0.595 --z 0.067] [--look 0 0] [--close]
"""
import argparse
import math
import os

import bpy
import numpy as np
import trimesh
from mathutils import Euler, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
STATUES = os.path.join(HERE, "..", "..", "UnityGame", "Assets", "Models", "Statues")

ap = argparse.ArgumentParser()
ap.add_argument("out")
ap.add_argument("--xl", type=float, default=-0.037)
ap.add_argument("--xr", type=float, default=0.037)
ap.add_argument("--y", type=float, default=0.595)
ap.add_argument("--z", type=float, default=0.067)
ap.add_argument("--look", type=float, nargs=2, default=(0.0, 0.0))
ap.add_argument("--iris", type=float, default=0.42)
ap.add_argument("--close", action="store_true", help="close-up on the eyes")
ap.add_argument("--cam", type=float, nargs=3, default=(0.0, -0.55, 0.6))
args = ap.parse_args()


def surface(theta):
    r0 = 0.013
    z = r0 * np.cos(theta)
    return r0 * (1 + 0.12 * np.maximum(0, (z - 0.009) / 0.004)) * 1.05


def cap(name, max_theta, segs, rings, lift, material, uv=False):
    """Same cap as the Unity EyeMesh, facing +Z in its own space (Unity forward)."""
    verts, uvs, faces = [], [], []
    for r in range(rings + 1):
        th = max_theta * r / rings
        rad = (surface(0.0) if max_theta < math.pi else surface(th)) * lift
        for s in range(segs + 1):
            ph = 2 * math.pi * s / segs
            verts.append((math.sin(th) * math.cos(ph) * rad, math.sin(th) * math.sin(ph) * rad, math.cos(th) * rad))
            uvs.append((s / segs, 1 - th / math.pi))
    for r in range(rings):
        for s in range(segs):
            a = r * (segs + 1) + s
            b = a + segs + 1
            faces.append((a, a + 1, b + 1, b))
    me = bpy.data.meshes.new(name)
    me.from_pydata(verts, [], faces)
    if uv:
        layer = me.uv_layers.new()
        for poly in me.polygons:
            for li in poly.loop_indices:
                layer.data[li].uv = uvs[me.loops[li].vertex_index]
    for p in me.polygons:
        p.use_smooth = True
    me.materials.append(material)
    o = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(o)
    return o


def glow(name, colour=None, image=None):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    em = nt.nodes.new("ShaderNodeEmission")
    if image:
        img = nt.nodes.new("ShaderNodeTexImage")
        img.image = bpy.data.images.load(image)
        nt.links.new(img.outputs["Color"], em.inputs["Color"])
    else:
        em.inputs["Color"].default_value = colour
    nt.links.new(em.outputs["Emission"], nt.nodes["Material Output"].inputs["Surface"])
    return m


bpy.ops.wm.read_factory_settings(use_empty=True)
m = trimesh.load(os.path.join(STATUES, "Bust_Portrait.obj"), force="mesh")
v = m.vertices
# test space: Blender X = x, Y = z_obj (so the face looks toward -Y), Z = height
me = bpy.data.meshes.new("Bust")
me.from_pydata(np.column_stack([v[:, 0], v[:, 2], v[:, 1]]).tolist(), [], m.faces[:, ::-1].tolist())
for p in me.polygons:
    p.use_smooth = True
marble = bpy.data.materials.new("Marble")
marble.use_nodes = True
marble.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.8, 0.72, 0.58, 1)
me.materials.append(marble)
bpy.context.collection.objects.link(bpy.data.objects.new("Bust", me))

eye_mat = glow("Eye", image=os.path.join(STATUES, "Textures", "statue_eye.png"))
pupil_mat = glow("Pupil", colour=(0.01, 0, 0, 1))
rim_mat = glow("Rim", colour=(1, 0.8, 0.45, 1))
# Unity forward (+Z) is test-space -Y
to_test = Euler((math.radians(90), 0, 0))
for side in (-1, 1):
    centre = Vector((args.xl if side < 0 else args.xr, -args.z, args.y))
    look = to_test.to_matrix() @ Euler((math.radians(-args.look[1]), math.radians(args.look[0]), 0)).to_matrix()
    for nm, th, lift, mat, uv in (("Iris", args.iris, 1.003, eye_mat, True), ("Rim", 0.19, 1.006, rim_mat, False),
                                  ("Pupil", 0.15, 1.009, pupil_mat, False)):
        o = cap(nm, th, 36, 8, lift, mat, uv=uv)
        o.location = centre
        o.rotation_euler = look.to_euler()

sc = bpy.context.scene
w = bpy.data.worlds.new("W"); sc.world = w; w.use_nodes = True
w.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.4
key = bpy.data.objects.new("Key", bpy.data.lights.new("Key", "AREA"))
key.data.energy = 30; key.location = (0.3, -0.6, 0.9)
key.rotation_euler = (Vector((0, 0, 0.6)) - key.location).to_track_quat("-Z", "Y").to_euler()
sc.collection.objects.link(key)
cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam")); sc.collection.objects.link(cam); sc.camera = cam
cam.data.lens = 85 if not args.close else 90
cam.data.clip_start = 0.01
target = Vector((0, 0, 0.6)) if not args.close else Vector((args.xr, -args.z, args.y))
cam.location = tuple(args.cam)
cam.rotation_euler = (target - cam.location).to_track_quat("-Z", "Y").to_euler()
sc.render.engine = "CYCLES"; sc.cycles.samples = 24; sc.cycles.use_denoising = True
sc.render.resolution_x, sc.render.resolution_y = (600, 600) if not args.close else (800, 400)
sc.render.filepath = args.out
bpy.ops.render.render(write_still=True)
