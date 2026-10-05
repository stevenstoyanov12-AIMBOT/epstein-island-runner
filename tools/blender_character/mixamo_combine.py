"""Combine Mixamo downloads into one Unity-ready character: the skinned model plus in-place loops
Walk, Run and Shoot on a single skeleton.

  - Walk:  the Walking clip with its forward travel removed (the game moves the character).
  - Run:   the best repeating cycle found in the second half of Idle_To_Running, forward travel removed.
  - Shoot: the Shooting clip as is (the game layers it onto the upper body while walking or running).

usage: python3 mixamo_combine.py WALKING.fbx IDLE_TO_RUNNING.fbx SHOOTING.fbx [--out DIR] [--preview DIR]
"""
import argparse
import math
import os

import bpy
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
ap = argparse.ArgumentParser()
ap.add_argument("walk")
ap.add_argument("run")
ap.add_argument("shoot")
ap.add_argument("--out", default=os.path.join(HERE, "..", "..", "UnityGame", "Assets", "Models", "Characters"))
ap.add_argument("--preview", default=None)
args = ap.parse_args()

bpy.ops.wm.read_factory_settings(use_empty=True)
sc = bpy.context.scene


def import_fbx(path):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=path)
    new = [o for o in bpy.data.objects if o not in before]
    arm = next(o for o in new if o.type == "ARMATURE")
    return arm, new


def frame_range(arm):
    r = arm.animation_data.action.frame_range
    return int(round(r[0])), int(round(r[1]))


def pose_snapshot(arm):
    return {pb.name: (pb.location.copy(), pb.rotation_quaternion.copy(), pb.scale.copy()) for pb in arm.pose.bones}


def pose_distance(a, b):
    return sum((a[n][1].rotation_difference(b[n][1]).angle) ** 2 for n in a)


def find_cycle(src, lo, hi, min_len, max_len):
    """Frames (a, b) in [lo, hi] where the pose at b best matches the pose at a: one clean repeating cycle."""
    snaps = {}
    for f in range(lo, hi + 1):
        sc.frame_set(f)
        snaps[f] = pose_snapshot(src)
    best = None
    for a in range(lo, hi + 1):
        for b in range(a + min_len, min(hi, a + max_len) + 1):
            d = pose_distance(snaps[a], snaps[b])
            if best is None or d < best[0]:
                best = (d, a, b)
    return best[1], best[2]


def bake(target, src, name, a, b, in_place):
    """Copy src's animation between frames a..b onto target as a new action starting at frame 0."""
    act = bpy.data.actions.new(name)
    act.use_fake_user = True
    target.animation_data_create()
    target.animation_data.action = act
    sc.frame_set(a)
    hips0 = src.pose.bones["mixamorig:Hips"].location.copy()
    sc.frame_set(b)
    travel = src.pose.bones["mixamorig:Hips"].location.copy() - hips0
    for f in range(a, b + 1):
        sc.frame_set(f)
        for spb in src.pose.bones:
            tpb = target.pose.bones[spb.name]
            tpb.rotation_mode = spb.rotation_mode
            loc = spb.location.copy()
            if in_place and spb.name == "mixamorig:Hips":
                loc -= travel * ((f - a) / max(1, b - a))       # remove the steady forward travel
            tpb.location = loc
            tpb.rotation_quaternion = spb.rotation_quaternion
            tpb.scale = spb.scale
            for prop in ("location", "rotation_quaternion", "scale"):
                tpb.keyframe_insert(prop, frame=f - a)
    return act


# the model and skeleton come from the walking download; its own clip is replaced by the baked ones
target, target_objs = import_fbx(args.walk)
target.name = "Character"
src_walk, walk_objs = import_fbx(args.walk)
src_run, run_objs = import_fbx(args.run)
src_shoot, shoot_objs = import_fbx(args.shoot)
target.animation_data.action = None

for o in (src_walk, src_run, src_shoot):
    o.animation_data.action.use_fake_user = False

wa, wb = frame_range(src_walk)
walk = bake(target, src_walk, "Walk", wa, wb, in_place=True)
ra, rb = frame_range(src_run)
ca, cb = find_cycle(src_run, ra + (rb - ra) // 2, rb, 14, 26)
print(f"run cycle: frames {ca}-{cb} of {ra}-{rb}")
run = bake(target, src_run, "Run", ca, cb, in_place=True)
sa, sb = frame_range(src_shoot)
shoot = bake(target, src_shoot, "Shoot", sa, sb, in_place=False)

# drop the source copies (and the downloads' original actions) so only our three clips are exported
for o in walk_objs + run_objs + shoot_objs:
    bpy.data.objects.remove(o, do_unlink=True)
for act in list(bpy.data.actions):
    if act.name not in ("Walk", "Run", "Shoot"):
        bpy.data.actions.remove(act)
target.animation_data.action = walk
sc.render.fps = 30

os.makedirs(args.out, exist_ok=True)
bpy.ops.object.select_all(action="DESELECT")
for o in target_objs:
    o.select_set(True)
path = os.path.join(args.out, "Shooter.fbx")
bpy.ops.export_scene.fbx(filepath=path, use_selection=True, add_leaf_bones=False, bake_anim=True,
                         bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
                         bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0,
                         apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y")
print("exported", path, "clips", [a.name for a in bpy.data.actions])

if args.preview:
    from PIL import Image
    os.makedirs(args.preview, exist_ok=True)
    w = bpy.data.worlds.new("W"); sc.world = w; w.use_nodes = True
    w.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.7
    sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN")); sun.data.energy = 3
    sun.rotation_euler = (math.radians(50), 0, math.radians(30)); sc.collection.objects.link(sun)
    cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam")); sc.collection.objects.link(cam); sc.camera = cam
    cam.location = (3.4, -2.0, 1.2)
    cam.rotation_euler = (Vector((0, 0, 0.85)) - cam.location).to_track_quat("-Z", "Y").to_euler()
    sc.render.engine = "CYCLES"; sc.cycles.samples = 8; sc.cycles.use_denoising = True
    sc.render.resolution_x, sc.render.resolution_y = 240, 300
    for act in (walk, run, shoot):
        target.animation_data.action = act
        a, b = frame_range(target)
        imgs = []
        for i in range(4):
            sc.frame_set(a + (b - a) * i // 4)
            p = os.path.join(args.preview, f"{act.name}_{i}.png")
            sc.render.filepath = p
            bpy.ops.render.render(write_still=True)
            imgs.append(Image.open(p).convert("RGB"))
        strip = Image.new("RGB", (240 * 4, 300))
        for i, im in enumerate(imgs):
            strip.paste(im, (i * 240, 0))
        strip.save(os.path.join(args.preview, f"{act.name}_strip.png"))
