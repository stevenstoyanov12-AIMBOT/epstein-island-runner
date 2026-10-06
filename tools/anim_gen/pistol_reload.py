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

    last_q = {}
    def key(e, f, pos, rot):
        rot = rot.copy()
        prev = last_q.get(e.name)
        if prev is not None and prev.dot(rot) < 0:                      # same hemisphere as the last key: no wrist flips
            rot.negate()
        last_q[e.name] = rot
        e.location = pos
        e.rotation_quaternion = rot
        e.keyframe_insert("location", frame=f)
        e.keyframe_insert("rotation_quaternion", frame=f)

    r0q, l0q = R0.to_quaternion(), L0.to_quaternion()
    rp0, lp0 = R0.translation.copy(), L0.translation.copy()

    # ---- the gun is animated; the right hand follows it, the left hand works on it ----
    # gun frame: X = gun's right side, Y = barrel, Z = top of the slide; origin = the grip (right hand)
    def frame(barrel, top):
        barrel, top = barrel.normalized(), top.normalized()
        right = barrel.cross(top).normalized()
        top = right.cross(barrel).normalized()
        m = Matrix((right, barrel, top)).transposed()
        return m.to_quaternion()
    g0 = frame(fwd, up)
    hand_in_gun = g0.inverted() @ r0q                                 # right hand orientation relative to the gun
    left_in_gun = g0.inverted() @ (lp0 - rp0)                         # support hand on the gun, gun space
    left_rot_in_gun = g0.inverted() @ l0q

    def gun_pts(gp, gq):
        X, Y, Z = gq @ Vector((1, 0, 0)), gq @ Vector((0, 1, 0)), gq @ Vector((0, 0, 1))
        return dict(X=X, Y=Y, Z=Z,
                    magwell=gp - Z * 0.075 * k - Y * 0.015 * k,          # bottom of the grip
                    rear=gp + Z * 0.065 * k - Y * 0.035 * k,             # back of the slide
                    support=gp + gq @ left_in_gun)

    def hand_rot(fingers, palm):
        """Hand orientation from where the fingers point and where the palm faces (bone Y = fingers, Z = palm)."""
        y, z = fingers.normalized(), palm.normalized()
        x = y.cross(z).normalized()
        z = x.cross(y).normalized()
        return Matrix((x, y, z)).transposed().to_quaternion()

    gkeys = {}                                                        # frame -> (gun pos, gun rot)
    def gkey(f, gp, gq):
        gkeys[f] = (gp, gq)
        key(tr, f, gp, gq @ hand_in_gun)

    inspect_p = rp0 + (-fwd) * 0.07 * k - up * 0.11 * k + left * 0.05 * k             # in front of the chest, not under the chin
    inspect_q = Quaternion(fwd, math.radians(-38)) @ Quaternion(left, math.radians(28)) @ g0   # canted, muzzle up a bit
    rack_q = Quaternion(fwd, math.radians(-22)) @ Quaternion(left, math.radians(10)) @ g0
    gkey(1, rp0, g0)
    gkey(4, rp0 + (-fwd) * 0.03 * k, g0)
    gkey(9, inspect_p, inspect_q)
    gkey(27, inspect_p, inspect_q)
    gkey(30, inspect_p + (inspect_q @ Vector((0, 0, 1))) * 0.03 * k, inspect_q)      # the slap knocks it up
    gkey(33, inspect_p, inspect_q)
    gkey(37, inspect_p + fwd * 0.02 * k, rack_q)
    gkey(40, inspect_p + fwd * 0.05 * k, rack_q)                                      # pushed forward as the slide racks
    gkey(43, inspect_p + fwd * 0.02 * k, rack_q)
    gkey(49, rp0, g0)
    gkey(END, rp0, g0)

    def at(f):
        """Gun pose at frame f (linear between gun keys; the IK target curves smooth it)."""
        fs = sorted(gkeys)
        for a_, b_ in zip(fs, fs[1:]):
            if a_ <= f <= b_:
                t = (f - a_) / (b_ - a_)
                return gkeys[a_][0].lerp(gkeys[b_][0], t), gkeys[a_][1].slerp(gkeys[b_][1], t)
        return gkeys[fs[-1]]

    # ---- left hand: off the gun, magazine from the left hip pouch, in, slap, rack, back ----
    pouch = hips + left * 0.19 * k + fwd * 0.03 * k + up * 0.04 * k
    pouch_q = hand_rot(-up + fwd * 0.2, -left)                         # fingers down, palm against the hip
    key(tl, 1, lp0, l0q)
    key(tl, 5, lp0 + left * 0.06 * k - up * 0.04 * k, l0q)
    key(tl, 12, pouch + up * 0.03 * k, pouch_q)
    key(tl, 16, pouch, pouch_q)                                       # grab
    key(tl, 19, pouch + up * 0.05 * k, pouch_q)                       # pull the magazine out
    gp, gq = at(24); g = gun_pts(gp, gq)
    key(tl, 24, g["magwell"] - g["Z"] * 0.14 * k, hand_rot(g["Y"], g["Z"]))           # magazine lined up under the grip
    gp, gq = at(28); g = gun_pts(gp, gq)
    key(tl, 28, g["magwell"] - g["Z"] * 0.05 * k, hand_rot(g["Y"], g["Z"]))           # sliding in
    gp, gq = at(30); g = gun_pts(gp, gq)
    key(tl, 30, g["magwell"] - g["Z"] * 0.01 * k, hand_rot(g["Y"], g["Z"]))           # heel of the hand slaps it home
    gp, gq = at(32); g = gun_pts(gp, gq)
    key(tl, 32, g["magwell"] - g["Z"] * 0.04 * k, hand_rot(g["Y"], g["Z"]))
    gp, gq = at(37); g = gun_pts(gp, gq)
    over = hand_rot(g["X"] + g["Y"] * 0.3, -g["Z"])                   # palm down on the slide, fingers across it
    key(tl, 37, g["rear"] + g["Z"] * 0.04 * k - g["X"] * 0.03 * k, over)
    gp, gq = at(40); g = gun_pts(gp, gq)
    key(tl, 40, g["rear"] + g["Z"] * 0.03 * k - g["Y"] * 0.09 * k - g["X"] * 0.03 * k, over)   # rack it back hard
    gp, gq = at(43); g = gun_pts(gp, gq)
    key(tl, 43, g["rear"] + g["Z"] * 0.06 * k - g["Y"] * 0.06 * k - g["X"] * 0.05 * k, over)   # let go
    gp, gq = at(47); g = gun_pts(gp, gq)
    key(tl, 47, g["support"], gq @ left_rot_in_gun)                  # back on the grip
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
    add_rot("Head", [(1, 0), (9, 9), (16, 6), (24, 11), (32, 11), (40, 8), (49, 0), (END, 0)], Vector((1, 0, 0)))
    add_rot("Spine2", [(1, 0), (9, 2), (14, 3), (24, 2), (40, 2), (49, 0), (END, 0)], Vector((1, 0, 0)))
    add_rot("Spine1", [(1, 0), (12, 6), (19, 6), (26, 0), (END, 0)], Vector((0, 1, 0)))   # turn toward the pouch

    if a.preview:
        preview(arm, a.preview, k, hips, hand_in_gun)

    # ---- export: armature + animation only ----
    for o in bpy.data.objects:
        o.select_set(o == arm)
    bpy.context.view_layer.objects.active = arm
    os.makedirs(os.path.dirname(a.out), exist_ok=True)
    bpy.ops.export_scene.fbx(filepath=a.out, use_selection=True, object_types={"ARMATURE"}, add_leaf_bones=False,
                             bake_anim=True, bake_anim_use_all_actions=False, bake_anim_use_nla_strips=False,
                             bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0)
    print("exported", a.out)


def preview(arm, out, k, hips, hand_in_gun):
    """Renders every 3rd frame with a pistol in the right hand and a magazine in the left while it carries one."""
    scene = bpy.context.scene
    os.makedirs(out, exist_ok=True)
    import bmesh

    def box(name, parts):
        me = bpy.data.meshes.new(name)
        bm = bmesh.new()
        for size, centre in parts:
            r = bmesh.ops.create_cube(bm, size=1.0)
            bmesh.ops.scale(bm, vec=size, verts=r["verts"])
            bmesh.ops.translate(bm, vec=centre, verts=r["verts"])
        bm.to_mesh(me); bm.free()
        o = bpy.data.objects.new(name, me)
        scene.collection.objects.link(o)
        return o

    def attach(o, bone_name, local_rot):
        pb = arm.pose.bones[B(bone_name)]
        scene.frame_set(1)
        hm = arm.matrix_world @ pb.matrix
        o.parent, o.parent_type, o.parent_bone = arm, "BONE", pb.name
        wq = hm.to_quaternion() @ local_rot
        o.matrix_world = Matrix.Translation(hm.translation) @ wq.to_matrix().to_4x4() @ Matrix.Scale(k, 4)

    # pistol in gun space: Y barrel, Z slide top, origin at the grip
    gun = box("Pistol", [((0.032, 0.19, 0.035), (0, 0.03, 0.06)),       # slide
                         ((0.03, 0.045, 0.11), (0, -0.02, -0.005))])    # grip
    attach(gun, "RightHand", hand_in_gun.inverted())
    mag = box("Magazine", [((0.022, 0.035, 0.11), (0, 0.05, 0.0))])
    attach(mag, "LeftHand", Quaternion())
    for f, vis in ((1, False), (15, False), (16, True), (30, True), (31, False)):
        mag.hide_render = not vis
        mag.keyframe_insert("hide_render", frame=f)

    scene.render.engine = "CYCLES"                  # CPU renderer (no GPU here)
    scene.cycles.device = "CPU"
    scene.cycles.samples = 12
    scene.cycles.use_denoising = True
    world = bpy.data.worlds.new("W")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs["Strength"].default_value = 3.5
    scene.world = world
    sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN"))
    sun.data.energy = 6.0
    sun.rotation_euler = (math.radians(40), 0, math.radians(-30))
    scene.collection.objects.link(sun)
    scene.render.resolution_x, scene.render.resolution_y = 360, 440
    cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
    scene.collection.objects.link(cam)
    scene.camera = cam
    cam.data.lens = 55
    cam.data.clip_start = 0.01 * k
    tgt = hips + Vector((0.05, -0.2, 0.42)) * k
    views = {"front": Vector((0.25, -1.6, 0.5)), "left": Vector((1.6, -0.5, 0.45))}
    for f in (1, 9, 14, 18, 24, 28, 30, 37, 40, 47):
        scene.frame_set(f)
        for vn, off in views.items():
            cam.location = hips + off * k
            cam.rotation_euler = (tgt - cam.location).to_track_quat("-Z", "Y").to_euler()
            scene.render.filepath = os.path.join(out, f"reload_{vn}_{f:02d}.png")
            bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(gun)
    bpy.data.objects.remove(mag)
    bpy.data.objects.remove(cam)


if __name__ == "__main__":
    main()
