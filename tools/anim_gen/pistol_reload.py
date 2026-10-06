"""Pistol reload animation for the Mixamo-rigged characters, built in Blender (bpy module).

  python3 pistol_reload.py [--preview DIR] [--out PistolReload.fbx]

Starts from the tjr pistol idle pose, drives both hands with IK targets through the reload
(gun tilts in, off hand to the belt for a magazine, slaps it in, racks the slide, back to the grip),
bakes it to plain bone keys and exports an armature-only FBX (Unity: Humanoid, used on the arms layer).
"""
import argparse
import math
import os

import bpy
from mathutils import Euler, Matrix, Quaternion, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(HERE))
SRC = os.path.join(REPO, "MuseumGame/Assets/Characters/Anim/tjr_PistolIdle.fbx")
PACK_IDLE = os.path.join(REPO, "MuseumGame/Assets/Animations/PistolLocomotion/pistol idle.fbx")
FPS = 30
END = 51                     # 1.7 s, matches SimpleGun.ReloadTime


def B(name):
    return "mixamorig:" + name


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--preview", default="")
    ap.add_argument("--out", default=os.path.join(REPO, "MuseumGame/Assets/Animations/PistolLocomotion/pistol reload.fbx"))
    a = ap.parse_args()

    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.render.fps = FPS
    bpy.ops.import_scene.fbx(filepath=SRC)
    arm = [o for o in bpy.data.objects if o.type == "ARMATURE"][0]
    scene.frame_set(1)
    W = arm.matrix_world
    Wi = W.inverted()

    # ---- base pose: the pack's two-handed "pistol idle" (frame 1) copied onto this character, held for the clip ----
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=PACK_IDLE)
    pack = [o for o in bpy.data.objects if o not in before and o.type == "ARMATURE"][0]
    scene.frame_set(1)
    # retarget: each bone gets the pack bone's world rotation relative to its rest, applied to its own rest
    Wp, Wt = pack.matrix_world.to_quaternion(), arm.matrix_world.to_quaternion()
    def depth(pb):
        n = 0
        while pb.parent:
            pb, n = pb.parent, n + 1
        return n
    for pb in sorted(arm.pose.bones, key=depth):
        src = pack.pose.bones.get(pb.name)
        if src is None:
            continue
        pose_w = Wp @ src.matrix.to_quaternion()
        rest_w_src = Wp @ src.bone.matrix_local.to_quaternion()
        rest_w_dst = Wt @ pb.bone.matrix_local.to_quaternion()
        want_w = (pose_w @ rest_w_src.inverted()) @ rest_w_dst
        m = (Wt.inverted() @ want_w).to_matrix().to_4x4()
        m.translation = pb.matrix.translation
        pb.rotation_mode = "QUATERNION"
        pb.matrix = m
        bpy.context.view_layer.update()
    for o in [o for o in bpy.data.objects if o not in before]:
        bpy.data.objects.remove(o)
    for act_ in [x for x in bpy.data.actions if x.users == 0]:
        bpy.data.actions.remove(act_)
    bpy.context.view_layer.update()
    base = {pb.name: pb.matrix_basis.copy() for pb in arm.pose.bones}
    old = arm.animation_data.action
    act = bpy.data.actions.new("PistolReload")
    arm.animation_data.action = act
    bpy.data.actions.remove(old)
    for pb in arm.pose.bones:
        pb.rotation_mode = "QUATERNION"
        pb.matrix_basis = base[pb.name]
        for f in (1, END):
            pb.keyframe_insert("location", frame=f)
            pb.keyframe_insert("rotation_quaternion", frame=f)
    scene.frame_set(1)
    bpy.context.view_layer.update()

    def world(name):
        return W @ arm.pose.bones[B(name)].matrix

    R0, L0 = world("RightHand"), world("LeftHand")
    hips = (W @ arm.pose.bones[B("Hips")].matrix).translation
    fwd = Vector((0, -1, 0))                      # the character faces -Y
    up = Vector((0, 0, 1))
    left = Vector((1, 0, 0))                      # character's left (+X)
    k = hips.z / 0.95                             # metres -> this file's units (tjr is imported 100x small)
    print("hands", R0.translation, L0.translation, "hips", hips, "unit", k)

    # ---- IK targets ----
    def empty(name, m):
        e = bpy.data.objects.new(name, None)
        scene.collection.objects.link(e)
        e.matrix_world = m
        e.rotation_mode = "QUATERNION"
        return e

    tr, tl = empty("T_R", R0), empty("T_L", L0)
    elbowR = world("RightForeArm").translation
    elbowL = world("LeftForeArm").translation
    pr = empty("P_R", Matrix.Translation(elbowR + (-left * 0.35 * k) + (-fwd * 0.25 * k) - up * 0.25 * k))
    pl = empty("P_L", Matrix.Translation(elbowL + (left * 0.35 * k) + (-fwd * 0.25 * k) - up * 0.25 * k))
    for side, t, p in (("Right", tr, pr), ("Left", tl, pl)):
        c = arm.pose.bones[B(side + "ForeArm")].constraints.new("IK")
        c.target, c.pole_target, c.chain_count, c.use_rotation = t, p, 2, True
        c.pole_angle = math.radians(-90)
        # the hand itself follows the target's rotation exactly
        cr = arm.pose.bones[B(side + "Hand")].constraints.new("COPY_ROTATION")
        cr.target = t

    def key(e, f, pos, rot):
        e.location = pos
        e.rotation_quaternion = rot
        e.keyframe_insert("location", frame=f)
        e.keyframe_insert("rotation_quaternion", frame=f)

    r0q, l0q = R0.to_quaternion(), L0.to_quaternion()
    rp0, lp0 = R0.translation.copy(), L0.translation.copy()

    # right hand: gun comes in toward the chest, lowers, rolls the grip toward the left hand and tips the muzzle down
    tilt = Quaternion(fwd, math.radians(-55)) @ Quaternion(left, math.radians(-25)) @ r0q
    rp1 = rp0 + (-fwd) * 0.16 * k - up * 0.14 * k + left * 0.08 * k
    key(tr, 1, rp0, r0q)
    key(tr, 9, rp1, tilt)
    key(tr, 28, rp1, tilt)
    key(tr, 30, rp1 + up * 0.04 * k, tilt)               # the mag slap knocks the gun up a touch
    key(tr, 33, rp1, tilt)
    key(tr, 38, rp1 + (-fwd) * 0.01 * k, Quaternion(fwd, math.radians(-15)) @ r0q)   # gun straightens for the rack
    key(tr, 42, rp1 + fwd * 0.02 * k, Quaternion(fwd, math.radians(-15)) @ r0q)
    key(tr, 49, rp0, r0q)
    key(tr, END, rp0, r0q)

    # left hand: off the gun, down to the magazine pouch on the left hip, up under the grip, slap, rack the slide, back
    pouch = hips + left * 0.2 * k + fwd * 0.05 * k + up * 0.02 * k
    grip_bottom = rp1 - up * 0.11 * k + left * 0.01 * k
    slide_top = rp1 + fwd * 0.07 * k + up * 0.09 * k
    lq_pouch = Quaternion(left, math.radians(70)) @ Quaternion(up, math.radians(20)) @ l0q
    lq_mag = Quaternion(left, math.radians(-25)) @ l0q
    lq_rack = Quaternion(fwd, math.radians(80)) @ Quaternion(left, math.radians(-30)) @ l0q
    key(tl, 1, lp0, l0q)
    key(tl, 6, lp0 + left * 0.05 * k - up * 0.04 * k, l0q)
    key(tl, 14, pouch, lq_pouch)
    key(tl, 18, pouch - up * 0.02 * k, lq_pouch)          # grab the magazine
    key(tl, 25, grip_bottom - up * 0.06 * k, lq_mag)
    key(tl, 29, grip_bottom + up * 0.03 * k, lq_mag)      # slap it home
    key(tl, 32, grip_bottom, lq_mag)
    key(tl, 37, slide_top, lq_rack)
    key(tl, 40, slide_top + (-fwd) * 0.11 * k, lq_rack)   # rack the slide back
    key(tl, 43, slide_top, lq_rack)
    key(tl, 49, lp0, l0q)
    key(tl, END, lp0, l0q)

    # ---- bake the IK into plain bone keys, then drop the rig helpers ----
    bpy.context.view_layer.objects.active = arm
    arm.select_set(True)
    bpy.ops.object.mode_set(mode="POSE")
    bpy.ops.nla.bake(frame_start=1, frame_end=END, only_selected=False, visual_keying=True,
                     clear_constraints=True, use_current_action=True, bake_types={"POSE"})
    bpy.ops.object.mode_set(mode="OBJECT")
    for e in (tr, tl, pr, pl):
        bpy.data.objects.remove(e)

    # head and upper spine: glance down at the gun, small lean in
    def add_rot(bone, frames_deg, axis):
        pb = arm.pose.bones[B(bone)]
        b = base[pb.name].to_quaternion()
        for f, deg in frames_deg:
            pb.rotation_quaternion = b @ Quaternion(axis, math.radians(deg))
            pb.keyframe_insert("rotation_quaternion", frame=f)
    add_rot("Head", [(1, 0), (10, 14), (28, 18), (40, 10), (49, 0), (END, 0)], Vector((1, 0, 0)))
    add_rot("Spine2", [(1, 0), (10, 4), (40, 4), (49, 0), (END, 0)], Vector((1, 0, 0)))

    if a.preview:
        preview(arm, a.preview, k, hips)

    # ---- export: armature + animation only ----
    for o in bpy.data.objects:
        o.select_set(o == arm)
    bpy.context.view_layer.objects.active = arm
    os.makedirs(os.path.dirname(a.out), exist_ok=True)
    bpy.ops.export_scene.fbx(filepath=a.out, use_selection=True, object_types={"ARMATURE"}, add_leaf_bones=False,
                             bake_anim=True, bake_anim_use_all_actions=False, bake_anim_use_nla_strips=False,
                             bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0)
    print("exported", a.out)


def preview(arm, out, k, hips):
    """Workbench renders of a few key frames with a pistol proxy in the right hand."""
    scene = bpy.context.scene
    os.makedirs(out, exist_ok=True)
    scene.frame_set(1)
    hand = arm.pose.bones[B("RightHand")]
    hm = arm.matrix_world @ hand.matrix
    gun = bpy.data.objects.new("GunProxy", bpy.data.meshes.new("gun"))
    import bmesh
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.scale(bm, vec=(0.035 * k, 0.19 * k, 0.035 * k), verts=bm.verts)     # slide/barrel along -Y
    bmesh.ops.translate(bm, vec=(0, -0.06 * k, 0.03 * k), verts=bm.verts)
    bm.to_mesh(gun.data)
    bm.free()
    scene.collection.objects.link(gun)
    gun.parent = arm
    gun.parent_type = "BONE"
    gun.parent_bone = hand.name
    gun.matrix_world = Matrix.Translation(hm.translation)

    scene.render.engine = "CYCLES"                  # CPU renderer (no GPU here)
    scene.cycles.device = "CPU"
    scene.cycles.samples = 12
    scene.cycles.use_denoising = True
    world = bpy.data.worlds.new("W")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs["Strength"].default_value = 1.2
    scene.world = world
    sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN"))
    sun.data.energy = 3.0
    sun.rotation_euler = (math.radians(40), 0, math.radians(-30))
    scene.collection.objects.link(sun)
    scene.render.resolution_x, scene.render.resolution_y = 420, 520
    cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
    scene.collection.objects.link(cam)
    scene.camera = cam
    cam.data.lens = 50
    tgt = hips + Vector((0.0, -0.2, 0.3)) * k
    cam.location = hips + Vector((1.2, -2.3, 0.6)) * k            # from the front, a bit to his left
    cam.data.clip_start = 0.01 * k
    cam.rotation_euler = (tgt - cam.location).to_track_quat("-Z", "Y").to_euler()
    for f in (1, 9, 14, 18, 25, 29, 37, 40, 51):
        scene.frame_set(f)
        scene.render.filepath = os.path.join(out, f"reload_{f:02d}.png")
        bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(gun)
    bpy.data.objects.remove(cam)


if __name__ == "__main__":
    main()
