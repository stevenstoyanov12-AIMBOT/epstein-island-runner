"""A simple mannequin with a skeleton and two looping animations (Walk, Run), made in Blender.

Body parts are rigidly attached to the bones (no skin weights), so it imports cleanly into Unity as a
Legacy-animated model. Height ~1.8 m, feet at the origin, facing -Y (Unity +Z after export).

usage: python3 make_runner.py [--out DIR] [--preview DIR]
"""
import argparse
import math
import os

import bpy
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
ap = argparse.ArgumentParser()
ap.add_argument("--out", default=os.path.join(HERE, "..", "..", "UnityGame", "Assets", "Models", "Characters"))
ap.add_argument("--preview", default=None)
args = ap.parse_args()

bpy.ops.wm.read_factory_settings(use_empty=True)
sc = bpy.context.scene

# --- skeleton ------------------------------------------------------------------------------------
BONES = {  # name: (head, tail, parent)
    "Hips": ((0, 0, 0.95), (0, 0, 1.08), None),
    "Spine": ((0, 0, 1.08), (0, 0, 1.45), "Hips"),
    "Head": ((0, 0, 1.5), (0, 0, 1.78), "Spine"),
}
for s, x in (("L", 1), ("R", -1)):
    BONES.update({
        f"UpperArm.{s}": ((x * 0.2, 0, 1.42), (x * 0.22, 0, 1.15), "Spine"),
        f"Forearm.{s}": ((x * 0.22, 0, 1.15), (x * 0.23, 0, 0.9), f"UpperArm.{s}"),
        f"Thigh.{s}": ((x * 0.1, 0, 0.95), (x * 0.1, 0, 0.52), "Hips"),
        f"Shin.{s}": ((x * 0.1, 0, 0.52), (x * 0.1, 0, 0.08), f"Thigh.{s}"),
    })

arm_data = bpy.data.armatures.new("RunnerRig")
rig = bpy.data.objects.new("Runner", arm_data)
sc.collection.objects.link(rig)
bpy.context.view_layer.objects.active = rig
bpy.ops.object.mode_set(mode="EDIT")
for name, (h, t, parent) in BONES.items():
    b = arm_data.edit_bones.new(name)
    b.head, b.tail = h, t
    b.roll = 0
    if parent:
        b.parent = arm_data.edit_bones[parent]
bpy.ops.object.mode_set(mode="OBJECT")

# --- body: rounded parts parented to bones ----------------------------------------------------------
skin = bpy.data.materials.new("Mannequin")
skin.use_nodes = True
skin.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.75, 0.74, 0.72, 1)
skin.node_tree.nodes["Principled BSDF"].inputs["Roughness"].default_value = 0.45
joint = bpy.data.materials.new("Joints")
joint.use_nodes = True
joint.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.12, 0.12, 0.14, 1)


def part(name, bone, a, b, radius, mat=skin):
    a, b = Vector(a), Vector(b)
    bpy.ops.mesh.primitive_cylinder_add(vertices=16, radius=radius, depth=(b - a).length, location=(a + b) / 2)
    o = bpy.context.active_object
    o.name = name
    o.rotation_mode = "QUATERNION"
    o.rotation_quaternion = (b - a).to_track_quat("Z", "Y")
    bev = o.modifiers.new("Round", "BEVEL")
    bev.width = radius * 0.6
    bev.segments = 4
    bpy.ops.object.modifier_apply(modifier="Round")
    bpy.ops.object.shade_smooth()
    o.data.materials.append(mat)
    attach(o, bone)
    return o


def ball(name, bone, c, r, mat=joint, scale=(1, 1, 1)):
    bpy.ops.mesh.primitive_uv_sphere_add(radius=r, location=c, segments=24, ring_count=12)
    o = bpy.context.active_object
    o.name = name
    o.scale = scale
    bpy.ops.object.transform_apply(scale=True)
    bpy.ops.object.shade_smooth()
    o.data.materials.append(mat)
    attach(o, bone)
    return o


def attach(o, bone):
    mw = o.matrix_world.copy()
    o.parent = rig
    o.parent_type = "BONE"
    o.parent_bone = bone
    o.matrix_world = mw


part("Pelvis", "Hips", (0, 0, 0.9), (0, 0, 1.06), 0.15)
part("Torso", "Spine", (0, 0, 1.1), (0, 0, 1.44), 0.17)
ball("Chest", "Spine", (0, -0.01, 1.35), 0.19, mat=skin, scale=(1.15, 0.75, 0.9))
ball("Neck", "Head", (0, 0, 1.5), 0.05)
ball("Skull", "Head", (0, 0, 1.64), 0.11, mat=skin, scale=(0.9, 1.0, 1.15))
for s, x in (("L", 1), ("R", -1)):
    ball(f"Shoulder.{s}", f"UpperArm.{s}", (x * 0.2, 0, 1.42), 0.065)
    part(f"UpperArmMesh.{s}", f"UpperArm.{s}", (x * 0.2, 0, 1.4), (x * 0.22, 0, 1.17), 0.05)
    ball(f"Elbow.{s}", f"Forearm.{s}", (x * 0.22, 0, 1.15), 0.045)
    part(f"ForearmMesh.{s}", f"Forearm.{s}", (x * 0.22, 0, 1.13), (x * 0.23, 0, 0.93), 0.042)
    ball(f"Hand.{s}", f"Forearm.{s}", (x * 0.23, 0, 0.87), 0.05, mat=skin, scale=(0.8, 0.6, 1.2))
    ball(f"Hip.{s}", f"Thigh.{s}", (x * 0.1, 0, 0.93), 0.075)
    part(f"ThighMesh.{s}", f"Thigh.{s}", (x * 0.1, 0, 0.9), (x * 0.1, 0, 0.55), 0.07)
    ball(f"Knee.{s}", f"Shin.{s}", (x * 0.1, 0, 0.52), 0.055)
    part(f"ShinMesh.{s}", f"Shin.{s}", (x * 0.1, 0, 0.5), (x * 0.1, 0, 0.1), 0.055)
    ball(f"Foot.{s}", f"Shin.{s}", (x * 0.1, -0.05, 0.05), 0.06, mat=skin, scale=(0.85, 1.9, 0.6))

# --- animation ---------------------------------------------------------------------------------------
# Bones point along their length; bending a leg forward/back is a rotation about the bone's local X axis.
def key(bone, frame, x=0.0, loc_z=None):
    pb = rig.pose.bones[bone]
    pb.rotation_mode = "XYZ"
    pb.rotation_euler = (math.radians(x), 0, 0)
    pb.keyframe_insert("rotation_euler", frame=frame)
    if loc_z is not None:
        pb.location = (0, loc_z, 0)   # bone-local Y is along the bone: moves the hips up and down
        pb.keyframe_insert("location", frame=frame)


def cycle(name, frames, thigh, knee, arm, elbow, bob, lean):
    act = bpy.data.actions.new(name)
    rig.animation_data_create()
    rig.animation_data.action = act
    for i in range(frames + 1):
        ph = 2 * math.pi * i / frames
        s = math.sin(ph)
        for side, sign in (("L", 1), ("R", -1)):
            sw = s * sign                                    # +1: this leg forward
            key(f"Thigh.{side}", i, x=-thigh * sw)
            # the knee bends most while the leg swings through (lifting the foot), little on contact
            lift = max(0.0, math.sin(ph + (0 if sign > 0 else math.pi) - 0.9))
            key(f"Shin.{side}", i, x=knee * (0.25 + lift))
            key(f"UpperArm.{side}", i, x=arm * sw)           # arms swing opposite to the legs
            key(f"Forearm.{side}", i, x=-elbow)
        key("Hips", i, x=0, loc_z=bob * abs(math.cos(ph)) - bob / 2)
        key("Spine", i, x=-lean)
        key("Head", i, x=lean * 0.5)
    for fc in act.fcurves if hasattr(act, "fcurves") else []:
        for kp in fc.keyframe_points:
            kp.interpolation = "BEZIER"
    act.use_fake_user = True
    return act


walk = cycle("Walk", 24, thigh=28, knee=40, arm=22, elbow=15, bob=0.03, lean=3)
run = cycle("Run", 16, thigh=50, knee=85, arm=45, elbow=75, bob=0.07, lean=12)
sc.frame_start, sc.frame_end = 0, 24
sc.render.fps = 24

os.makedirs(args.out, exist_ok=True)
bpy.ops.object.select_all(action="SELECT")
bpy.ops.export_scene.fbx(filepath=os.path.join(args.out, "Runner.fbx"), use_selection=True,
                         axis_forward="-Z", axis_up="Y", apply_scale_options="FBX_SCALE_UNITS",
                         bake_space_transform=False, add_leaf_bones=False, bake_anim=True,
                         bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
                         bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0)
print("exported", os.path.join(args.out, "Runner.fbx"))

if args.preview:
    from PIL import Image
    os.makedirs(args.preview, exist_ok=True)
    w = bpy.data.worlds.new("W"); sc.world = w; w.use_nodes = True
    w.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.6
    sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN")); sun.data.energy = 3
    sun.rotation_euler = (math.radians(50), 0, math.radians(30)); sc.collection.objects.link(sun)
    cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam")); sc.collection.objects.link(cam); sc.camera = cam
    cam.location = (3.2, -1.2, 1.1)
    cam.rotation_euler = (Vector((0, 0, 0.9)) - cam.location).to_track_quat("-Z", "Y").to_euler()
    sc.render.engine = "CYCLES"; sc.cycles.samples = 8; sc.cycles.use_denoising = True
    sc.render.resolution_x, sc.render.resolution_y = 240, 300
    for act, frames in ((walk, 24), (run, 16)):
        rig.animation_data.action = act
        imgs = []
        for f in range(0, frames, 2):
            sc.frame_set(f)
            p = os.path.join(args.preview, f"{act.name}_{f:02d}.png")
            sc.render.filepath = p
            bpy.ops.render.render(write_still=True)
            imgs.append(Image.open(p).convert("RGB"))
        imgs[0].save(os.path.join(args.preview, f"{act.name}.gif"), save_all=True, append_images=imgs[1:],
                     duration=int(2000 / 24), loop=0)
        strip = Image.new("RGB", (240 * 4, 300))
        for i in range(4):
            strip.paste(imgs[i * len(imgs) // 4], (i * 240, 0))
        strip.save(os.path.join(args.preview, f"{act.name}_strip.png"))
