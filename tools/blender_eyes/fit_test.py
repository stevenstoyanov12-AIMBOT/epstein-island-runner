"""Render the bust with its eyes placed as the Unity scene places them, to check the socket fit.

Positions are in the bust's pivot space as used in StatueSceneBuilder (x, height, forward).
usage: python3 fit_test.py OUT.png --x 0.034 --y 0.595 --z 0.0705 --scale 1.0
"""
import argparse
import math
import os

import bpy
import bmesh
import numpy as np
import trimesh
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
STATUES = os.path.join(HERE, "..", "..", "UnityGame", "Assets", "Models", "Statues")

ap = argparse.ArgumentParser()
ap.add_argument("out")
ap.add_argument("--x", type=float, default=0.034)
ap.add_argument("--y", type=float, default=0.595)
ap.add_argument("--z", type=float, default=0.0705)
ap.add_argument("--scale", type=float, default=1.0)
ap.add_argument("--yaw", type=float, default=0.0)
args = ap.parse_args()

bpy.ops.wm.read_factory_settings(use_empty=True)
m = trimesh.load(os.path.join(STATUES, "Bust_Portrait.obj"), force="mesh")
v = m.vertices
# bust space (x, up, forward=-z_obj) -> Blender (x, -forward... ) : Blender X=x, Y=-forward, Z=up
bv = np.column_stack([v[:, 0], v[:, 2], v[:, 1]])
me = bpy.data.meshes.new("Bust")
me.from_pydata(bv.tolist(), [], m.faces[:, ::-1].tolist())
bust = bpy.data.objects.new("Bust", me)
bpy.context.collection.objects.link(bust)
for p in me.polygons:
    p.use_smooth = True
mat = bpy.data.materials.new("Marble")
mat.use_nodes = True
mat.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.8, 0.72, 0.58, 1)
me.materials.append(mat)

bpy.ops.import_scene.fbx(filepath=os.path.join(STATUES, "StatueEye.fbx"))
eye = bpy.context.selected_objects[0]
eye.location = (0, 0, 0)
for side in (-1, 1):
    e = eye if side < 0 else eye.copy()
    if side > 0:
        bpy.context.collection.objects.link(e)
    # the eye looks along Blender -Y; the face here looks along -Y too (forward = -Y)
    e.location = (side * args.x, -args.z, args.y)
    e.scale = (args.scale,) * 3
    e.rotation_euler = (math.radians(90), 0, math.radians(args.yaw))   # keep the FBX import tilt
# self-lit eye material with its texture, as in Unity (the FBX carries no texture path)
em = bpy.data.materials.new("EyeGlow")
em.use_nodes = True
nt = em.node_tree
img = nt.nodes.new("ShaderNodeTexImage")
img.image = bpy.data.images.load(os.path.join(STATUES, "Textures", "statue_eye.png"))
emit = nt.nodes.new("ShaderNodeEmission")
nt.links.new(img.outputs["Color"], emit.inputs["Color"])
nt.links.new(emit.outputs["Emission"], nt.nodes["Material Output"].inputs["Surface"])
for o in bpy.data.objects:
    if o.type == "MESH" and o.name.startswith("StatueEye"):
        o.data.materials.clear()
        o.data.materials.append(em)

sc = bpy.context.scene
w = bpy.data.worlds.new("W"); sc.world = w; w.use_nodes = True
w.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.4
key = bpy.data.objects.new("Key", bpy.data.lights.new("Key", "AREA"))
key.data.energy = 30; key.location = (0.3, -0.6, 0.9)
key.rotation_euler = (Vector((0, 0, 0.6)) - key.location).to_track_quat("-Z", "Y").to_euler()
sc.collection.objects.link(key)
cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam")); sc.collection.objects.link(cam); sc.camera = cam
cam.data.lens = 85; cam.data.clip_start = 0.01
cam.location = (0.0, -0.55, 0.6)
cam.rotation_euler = (Vector((0, 0, 0.6)) - cam.location).to_track_quat("-Z", "Y").to_euler()
sc.render.engine = "CYCLES"; sc.cycles.samples = 24; sc.cycles.use_denoising = True
sc.render.resolution_x = sc.render.resolution_y = 600
sc.render.filepath = args.out
bpy.ops.render.render(write_still=True)
