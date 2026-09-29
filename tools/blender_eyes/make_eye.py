"""Model a glowing red eyeball in Blender and export it for the watching statue.

The eye looks along Unity +Z (Blender -Y). Texture is painted over the sphere's latitude: pupil at the front
pole, a glowing red iris with radial fibres, a dark limbal ring and a bloodshot, dark-crimson white.

usage: python3 make_eye.py [--out DIR] [--preview DIR]
"""
import argparse
import math
import os

import bpy
import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))


def eye_texture(path, w=1024, h=512):
    u = (np.arange(w) + 0.5) / w
    v = (np.arange(h) + 0.5) / h
    U, V = np.meshgrid(u, v)
    theta = (V) * np.pi                      # image top (row 0) = sphere top pole = the front of the eye
    rng = np.random.default_rng(3)
    col = np.zeros((h, w, 3))
    # sclera: dark crimson with veins creeping toward the iris
    col[:] = [0.35, 0.05, 0.05]
    veins = np.zeros((h, w))
    for _ in range(40):
        a0, ph = rng.uniform(0, 1), rng.uniform(0, 6.28)
        wig = a0 + 0.006 * np.sin(theta * rng.uniform(10, 25) + ph) + 0.003 * np.sin(theta * 50 + ph)
        d = np.minimum(np.abs(U - wig), 1 - np.abs(U - wig))
        veins = np.maximum(veins, np.exp(-(d / 0.0015) ** 2) * rng.uniform(0.4, 1) * np.clip((theta - 0.45) / 0.8, 0, 1) * np.clip((2.4 - theta) / 0.8, 0, 1))
    col = col * (1 - veins[..., None]) + np.array([0.75, 0.05, 0.03]) * veins[..., None]
    # iris: glowing red with radial fibres, hot toward the pupil
    R = 0.42
    iris = theta < R
    fib = 0.75 + 0.25 * np.sin(U * 2 * np.pi * 70 + 3 * np.sin(U * 2 * np.pi * 9)) * np.sin(theta * 30)
    t = np.clip(theta / R, 0, 1)
    irisc = (np.array([1.0, 0.55, 0.2]) * (1 - t)[..., None] + np.array([0.95, 0.05, 0.02]) * t[..., None]) * fib[..., None]
    col[iris] = irisc[iris]
    ring = np.abs(theta - R) < 0.025
    col[ring] = [0.08, 0.0, 0.0]                                   # limbal ring
    col[theta < 0.12] = [0.01, 0.0, 0.0]                           # pupil
    col[(theta >= 0.12) & (theta < 0.14)] = [1.0, 0.8, 0.45]       # hot rim round the pupil
    Image.fromarray((np.clip(col, 0, 1) * 255).astype(np.uint8)).save(path)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", default=os.path.join(HERE, "..", "..", "UnityGame", "Assets", "Models", "Statues"))
    ap.add_argument("--preview", default="previews")
    args = ap.parse_args()
    bpy.ops.wm.read_factory_settings(use_empty=True)
    os.makedirs(os.path.join(args.out, "Textures"), exist_ok=True)
    tex = os.path.join(args.out, "Textures", "statue_eye.png")
    eye_texture(tex)

    bpy.ops.mesh.primitive_uv_sphere_add(segments=48, ring_count=24, radius=0.013)
    eye = bpy.context.active_object
    eye.name = "StatueEye"
    # slightly bulging cornea at the front pole
    for v in eye.data.vertices:
        if v.co.z > 0.009:
            v.co *= 1 + 0.12 * (v.co.z - 0.009) / 0.004
    bpy.ops.object.shade_smooth()
    eye.rotation_euler = (math.radians(90), 0, 0)        # front pole (+Z) -> Blender -Y -> Unity +Z
    bpy.ops.object.transform_apply(rotation=True)
    m = bpy.data.materials.new("StatueEye")
    m.use_nodes = True
    nt = m.node_tree
    img = nt.nodes.new("ShaderNodeTexImage")
    img.image = bpy.data.images.load(tex)
    em = nt.nodes["Principled BSDF"]
    nt.links.new(img.outputs["Color"], em.inputs["Base Color"])
    nt.links.new(img.outputs["Color"], em.inputs["Emission Color"])
    em.inputs["Emission Strength"].default_value = 1.5
    eye.data.materials.append(m)

    # preview
    sc = bpy.context.scene
    world = bpy.data.worlds.new("W"); sc.world = world
    world.use_nodes = True; world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.1
    cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam")); sc.collection.objects.link(cam); sc.camera = cam
    cam.location = (0.02, -0.07, 0.015)
    cam.data.lens = 80
    cam.data.clip_start = 0.001
    from mathutils import Vector
    cam.rotation_euler = (Vector((0, 0, 0)) - cam.location).to_track_quat("-Z", "Y").to_euler()
    sc.render.engine = "CYCLES"; sc.cycles.samples = 32
    sc.render.resolution_x = sc.render.resolution_y = 500
    os.makedirs(args.preview, exist_ok=True)
    sc.render.filepath = os.path.join(args.preview, "eye.png")
    bpy.ops.render.render(write_still=True)

    bpy.ops.object.select_all(action="DESELECT")
    eye.select_set(True)
    bpy.ops.export_scene.fbx(filepath=os.path.join(args.out, "StatueEye.fbx"), use_selection=True,
                             axis_forward="-Z", axis_up="Y", apply_scale_options="FBX_SCALE_UNITS",
                             bake_space_transform=True, path_mode="STRIP")


if __name__ == "__main__":
    main()
