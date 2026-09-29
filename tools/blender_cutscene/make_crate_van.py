"""Build the BATON CORPORATION crate and the van cargo area in Blender, render previews, export FBX.

Blender space: Z up, the van's rear doors at Y = 0 and its front bulkhead at Y = +VAN_LEN.
(The FBX export maps Blender +Y to Unity -Z, so in Unity the rear doors face +Z.)

usage: python3 make_crate_van.py [--out DIR] [--preview DIR] [--samples 32]
"""
import argparse
import math
import os
import sys

import bpy  # must come before bmesh when Blender runs as a Python module
import bmesh
import numpy as np
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import textures  # noqa: E402

CRATE = 1.15            # outer size of the crate (m); big enough for a crouching person
VAN_W, VAN_H, VAN_LEN = 2.3, 2.05, 3.6
WOOD_U, WOOD_V = 1.2, 0.3   # metres covered by the wood texture along / across the grain


# --- boards -------------------------------------------------------------------------

def board(bm, uv, rng, centre, length_dir, width_dir, L, W, T, mat=0, wobble=True):
    """A plank centred at `centre`: long along length_dir, wide along width_dir, thick along their cross."""
    ld = Vector(length_dir).normalized()
    wd = Vector(width_dir).normalized()
    td = ld.cross(wd).normalized()
    if wobble:  # hand-built: tiny random twist and offset
        rot = Matrix.Rotation(math.radians(rng.normal(0, 0.5)), 3, td)
        ld, wd = rot @ ld, rot @ wd
        centre = Vector(centre) + td * rng.normal(0, 0.0015) + ld * rng.normal(0, 0.002)
    centre = Vector(centre)
    ou, ov = rng.uniform(0, 1), rng.uniform(0, 1)
    corners = {}
    for sl in (-1, 1):
        for sw in (-1, 1):
            for st in (-1, 1):
                corners[(sl, sw, st)] = bm.verts.new(centre + ld * sl * L / 2 + wd * sw * W / 2 + td * st * T / 2)
    quads = [((-1, -1, 1), (1, -1, 1), (1, 1, 1), (-1, 1, 1), "t"), ((-1, 1, -1), (1, 1, -1), (1, -1, -1), (-1, -1, -1), "t"),
             ((-1, -1, -1), (1, -1, -1), (1, -1, 1), (-1, -1, 1), "w"), ((-1, 1, 1), (1, 1, 1), (1, 1, -1), (-1, 1, -1), "w"),
             ((-1, -1, -1), (-1, -1, 1), (-1, 1, 1), (-1, 1, -1), "l"), ((1, -1, 1), (1, -1, -1), (1, 1, -1), (1, 1, 1), "l")]
    for *keys, kind in quads:
        f = bm.faces.new([corners[k] for k in keys])
        f.material_index = mat
        for loop, k in zip(f.loops, keys):
            p = loop.vert.co - centre
            a = p.dot(ld) / WOOD_U
            b = (p.dot(wd) if kind != "w" else p.dot(td)) / WOOD_V
            if kind == "l":
                a, b = p.dot(wd) / WOOD_U, p.dot(td) / WOOD_V
            loop[uv].uv = (a + ou, b + ov)


def nail(bm, rng, pos, normal):
    n = Vector(normal).normalized()
    m = Matrix.Translation(Vector(pos) + n * 0.002) @ n.to_track_quat("Z", "Y").to_matrix().to_4x4()
    res = bmesh.ops.create_cone(bm, cap_ends=True, segments=8, radius1=0.0065, radius2=0.005, depth=0.004, matrix=m)
    for f in {f for v in res["verts"] for f in v.link_faces}:
        f.material_index = 1


def crate_face(bm, uv, rng, centre, normal, up, width, height, count=6, solid=False, diagonal=False, missing=None):
    """One side of a crate: slats with gaps, a frame of thicker boards on top, optional diagonal brace.

    width runs along up x normal, height along up. `missing` leaves out that slat (a peep slot)."""
    n, u = Vector(normal).normalized(), Vector(up).normalized()
    r = u.cross(n).normalized()
    c = Vector(centre)
    frame_w = min(0.1, height * 0.3)
    frame_t, slat_t = 0.028, 0.02
    inner_w, inner_h = width - 2 * frame_t, height - 2 * frame_t
    slat_c = c - n * (frame_t + slat_t / 2)            # slats sit just inside the frame
    gap = 0.0 if solid else 0.016
    h = (inner_h - gap * (count - 1)) / count
    for i in range(count):
        if i == missing:
            continue
        off = -inner_h / 2 + h / 2 + i * (h + gap)
        board(bm, uv, rng, slat_c + u * off, r, u, inner_w, h - 0.002, slat_t)
    # frame boards, flush with the outside
    fc = c - n * frame_t / 2
    Lw, Lh = inner_w, inner_h
    for s in (-1, 1):
        board(bm, uv, rng, fc + u * s * (Lh / 2 - frame_w / 2), r, u, Lw, frame_w, frame_t)
        board(bm, uv, rng, fc + r * s * (Lw / 2 - frame_w / 2), u, r, Lh - 2 * frame_w, frame_w, frame_t)
        for t in (-1, 1):  # nails at the frame corners
            nail(bm, rng, c + u * s * (Lh / 2 - frame_w / 2) + r * t * (Lw / 2 - 0.03), n)
            nail(bm, rng, c + r * s * (Lw / 2 - frame_w / 2) + u * t * (Lh / 2 - frame_w - 0.03), n)
    if diagonal:
        span = r * (Lw - 2 * frame_w) + u * (Lh - 2 * frame_w)
        d = span.normalized()
        across = d.cross(n)
        length = span.length + 0.02
        board(bm, uv, rng, fc, d, across, length, frame_w * 0.95, frame_t * 0.9)
        for t in (-0.42, 0, 0.42):
            nail(bm, rng, c + d * t * length, n)


PEEK_SLAT = 3   # slat left out of the player's crate, at crouched eye height
PEEK_EYE = 0.73  # height of that slot above the crate's base (skids included)
LOW_CRATE = 0.30  # body height of the flat crates the player's crate is stacked on


def build_crate(rng, name, sx=CRATE, sy=CRATE, sz=CRATE, slats=6, diagonal=True, peek=False):
    """Crate of outer size sx x sy x sz standing on skids (0.06 m). peek: one slat missing on the -Y side."""
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new("UVMap")
    c = Vector((0, 0, sz / 2))
    crate_face(bm, uv, rng, c + Vector((sx / 2, 0, 0)), (1, 0, 0), (0, 0, 1), sy, sz, slats, diagonal=diagonal)
    crate_face(bm, uv, rng, c + Vector((-sx / 2, 0, 0)), (-1, 0, 0), (0, 0, 1), sy, sz, slats, diagonal=diagonal)
    crate_face(bm, uv, rng, c + Vector((0, sy / 2, 0)), (0, 1, 0), (0, 0, 1), sx, sz, slats)
    crate_face(bm, uv, rng, c + Vector((0, -sy / 2, 0)), (0, -1, 0), (0, 0, 1), sx, sz, slats,
               missing=PEEK_SLAT if peek else None)
    crate_face(bm, uv, rng, c + Vector((0, 0, sz / 2)), (0, 0, 1), (0, 1, 0), sx, sy, 6, solid=not peek)
    crate_face(bm, uv, rng, c + Vector((0, 0, -sz / 2)), (0, 0, -1), (0, 1, 0), sx, sy, 6, solid=True)
    # skids under the crate so it sits on runners like a real shipping crate
    for x in (-0.4, 0, 0.4):
        board(bm, uv, rng, (x * sx / CRATE, 0, -0.03), (0, 1, 0), (1, 0, 0), sy, 0.09, 0.06)
    for v in bm.verts:
        v.co.z += 0.06
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    obj = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(obj)
    bev = obj.modifiers.new("Bevel", "BEVEL")
    bev.width = 0.0035
    bev.segments = 2
    bev.limit_method = "ANGLE"
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.modifier_apply(modifier="Bevel")
    return obj


def build_stencil(name="Crate_Stencil", sides=(1, -1)):
    """Decal quads carrying the stencil texture, just outside the slats of the two plain sides."""
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new("UVMap")
    s = CRATE
    y = s / 2 - 0.028 + 0.007    # just outside the slats (inside the frame), clear of their hand-built wobble
    w, hgt = 1.0, 0.5   # big enough to read across the van
    zc = s / 2 + 0.06 + 0.1
    for side in sides:
        pts = [(-w / 2, -hgt / 2), (w / 2, -hgt / 2), (w / 2, hgt / 2), (-w / 2, hgt / 2)]
        # seen from outside, 'right' is -X on the +Y side and +X on the -Y side
        vs = [bm.verts.new((-px * side, y * side, zc + pz)) for px, pz in pts]
        f = bm.faces.new(vs)
        for loop, (u, v) in zip(f.loops, ((0, 0), (1, 0), (1, 1), (0, 1))):
            loop[uv].uv = (u, v)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    obj = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(obj)
    return obj


# --- van ----------------------------------------------------------------------------

def box(bm, uv, lo, hi, mat, uv_scale=1.0):
    lo, hi = Vector(lo), Vector(hi)
    res = bmesh.ops.create_cube(bm, size=1.0)
    for v in res["verts"]:
        v.co = Vector(((v.co.x + 0.5) * (hi.x - lo.x) + lo.x, (v.co.y + 0.5) * (hi.y - lo.y) + lo.y,
                       (v.co.z + 0.5) * (hi.z - lo.z) + lo.z))
    for f in {f for v in res["verts"] for f in v.link_faces}:
        f.material_index = mat
        n = f.normal
        for loop in f.loops:
            p = loop.vert.co
            if abs(n.z) > 0.5:
                loop[uv].uv = (p.x / uv_scale, p.y / uv_scale)
            elif abs(n.x) > 0.5:
                loop[uv].uv = (p.y / uv_scale, p.z / uv_scale)
            else:
                loop[uv].uv = (p.x / uv_scale, p.z / uv_scale)


def build_van(rng):
    """Cargo box of a delivery van seen from inside: ribbed steel walls, plank floor, rear doors with windows."""
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new("UVMap")
    W, H, L, t = VAN_W, VAN_H, VAN_LEN, 0.05
    x0, x1 = -W / 2, W / 2
    box(bm, uv, (x0 - t, 0, -0.08), (x1 + t, L, 0), 0, 1.0)                  # chassis under the floor
    for i, x in enumerate(np.arange(x0 + 0.115, x1, 0.23)):                  # floor planks
        board(bm, uv, rng, (x, L / 2, 0.012), (0, 1, 0), (1, 0, 0), L, 0.222, 0.024, mat=1, wobble=False)
    box(bm, uv, (x0 - t, 0, 0), (x0, L, H), 0, 1.0)                           # side walls
    box(bm, uv, (x1, 0, 0), (x1 + t, L, H), 0, 1.0)
    box(bm, uv, (x0 - t, 0, H), (x1 + t, L, H + t), 0, 1.0)                   # roof
    box(bm, uv, (x0 - t, L, 0), (x1 + t, L + t, H + t), 0, 1.0)               # front bulkhead
    for y in np.arange(0.3, L, 0.6):                                          # wall and roof ribs
        for side in (-1, 1):
            xa = x0 if side < 0 else x1 - 0.05
            box(bm, uv, (xa, y - 0.03, 0.02), (xa + 0.05, y + 0.03, H), 0, 1.0)
        box(bm, uv, (x0, y - 0.03, H - 0.05), (x1, y + 0.03, H), 0, 1.0)
    for side in (-1, 1):                                                      # tie-down rails and wheel arches
        xa = x0 if side < 0 else x1 - 0.04
        box(bm, uv, (xa, 0.05, 0.85), (xa + 0.04, L - 0.05, 0.93), 0, 1.0)
        xa = x0 if side < 0 else x1 - 0.32
        box(bm, uv, (xa, 0.55, 0.02), (xa + 0.32, 1.45, 0.42), 0, 1.0)
    # caged ceiling lamp near the front
    box(bm, uv, (-0.12, L - 0.9, H - 0.06), (0.12, L - 0.7, H), 0, 1.0)
    me = bpy.data.meshes.new("VanCargo")
    bm.to_mesh(me)
    bm.free()
    obj = bpy.data.objects.new("VanCargo", me)
    bpy.context.collection.objects.link(obj)
    return obj


def build_door(side):
    """One rear door leaf with a window, modelled around its hinge so it can swing open in Unity.

    side -1 is the leaf on Blender's -X side. Returns the object (origin at the hinge)."""
    W, H, t = VAN_W, VAN_H, 0.05
    gap, win_w, win_z0, win_z1 = 0.012, 0.62, 1.12, 1.78
    x0, x1 = -W / 2, W / 2
    a, b = (x0 - t, -gap / 2) if side < 0 else (gap / 2, x1 + t)
    hinge = Vector((a if side < 0 else b, 0, 0))
    mid = (a + b) / 2
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new("UVMap")
    box(bm, uv, (a, -t, 0), (b, 0, win_z0), 0, 1.0)
    box(bm, uv, (a, -t, win_z1), (b, 0, H + t), 0, 1.0)
    box(bm, uv, (a, -t, win_z0), (mid - win_w / 2, 0, win_z1), 0, 1.0)
    box(bm, uv, (mid + win_w / 2, -t, win_z0), (b, 0, win_z1), 0, 1.0)
    box(bm, uv, (mid - 0.02, -t - 0.03, 0.9), (mid + 0.02, -t, 1.0), 0, 1.0)  # outside handle
    for v in bm.verts:
        v.co -= hinge
    name = "VanDoor_L" if side < 0 else "VanDoor_R"
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    obj = bpy.data.objects.new(name, me)
    obj.location = hinge
    bpy.context.collection.objects.link(obj)
    print(f"{name} hinge (Blender) {tuple(round(c, 3) for c in hinge)}")
    return obj


# --- materials, previews, export -------------------------------------------------------

def material(name, tex, albedo, normal=None, alpha=False, tint=(1, 1, 1, 1)):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    img = nt.nodes.new("ShaderNodeTexImage")
    img.image = bpy.data.images.load(os.path.join(tex, albedo))
    mix = nt.nodes.new("ShaderNodeMix")
    mix.data_type = "RGBA"
    mix.blend_type = "MULTIPLY"
    mix.inputs[0].default_value = 1.0
    nt.links.new(img.outputs["Color"], mix.inputs[6])
    mix.inputs[7].default_value = tint
    nt.links.new(mix.outputs[2], bsdf.inputs["Base Color"])
    if alpha:
        nt.links.new(img.outputs["Alpha"], bsdf.inputs["Alpha"])
    if normal:
        nimg = nt.nodes.new("ShaderNodeTexImage")
        nimg.image = bpy.data.images.load(os.path.join(tex, normal))
        nimg.image.colorspace_settings.name = "Non-Color"
        nm = nt.nodes.new("ShaderNodeNormalMap")
        nt.links.new(nimg.outputs["Color"], nm.inputs["Color"])
        nt.links.new(nm.outputs["Normal"], bsdf.inputs["Normal"])
    bsdf.inputs["Roughness"].default_value = 0.8
    return m


def camera(loc, target, lens=24):
    cam = bpy.data.objects.get("Camera")
    if cam is None:
        cam = bpy.data.objects.new("Camera", bpy.data.cameras.new("Camera"))
        bpy.context.collection.objects.link(cam)
    bpy.context.scene.camera = cam
    cam.data.lens = lens
    cam.location = loc
    d = Vector(target) - Vector(loc)
    cam.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()


def render(path, samples):
    sc = bpy.context.scene
    sc.render.engine = "CYCLES"
    sc.cycles.samples = samples
    sc.cycles.use_denoising = True
    sc.render.resolution_x, sc.render.resolution_y = 1000, 700
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True)
    print(path)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", default=os.path.join(HERE, "..", "..", "UnityGame", "Assets", "Models", "Cutscene"))
    ap.add_argument("--preview", default="previews")
    ap.add_argument("--samples", type=int, default=32)
    args = ap.parse_args()
    rng = np.random.default_rng(3)

    bpy.ops.wm.read_factory_settings(use_empty=True)
    tex = os.path.join(args.out, "Textures")
    os.makedirs(tex, exist_ok=True)
    textures.wood(tex)
    textures.stencil(tex)
    textures.metal(tex)
    textures.facade(tex)
    textures.asphalt(tex)

    wood = material("CrateWood", tex, "wood_albedo.png", "wood_normal.png")
    floor = material("FloorWood", tex, "wood_albedo.png", "wood_normal.png", tint=(0.45, 0.4, 0.36, 1))
    steel = material("VanSteel", tex, "metal_albedo.png", "metal_normal.png")
    nails = bpy.data.materials.new("Nails")
    nails.use_nodes = True
    nails.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.12, 0.12, 0.13, 1)
    nails.node_tree.nodes["Principled BSDF"].inputs["Metallic"].default_value = 1.0
    stencil = material("Stencil", tex, "stencil.png", alpha=True)

    crate = build_crate(rng, "Crate")
    peek = build_crate(rng, "Crate_Peek", peek=True)
    low = build_crate(rng, "Crate_Low", sz=LOW_CRATE, slats=2, diagonal=False)
    for o in (crate, peek, low):
        o.data.materials.append(wood)
        o.data.materials.append(nails)
    sten = build_stencil()
    sten_back = build_stencil("Crate_Stencil_Back", sides=(1,))   # the peek crate's slot side stays bare
    for o in (sten, sten_back):
        o.data.materials.append(stencil)
    van = build_van(rng)
    van.data.materials.append(steel)
    van.data.materials.append(floor)
    doors = [build_door(-1), build_door(1)]
    for d in doors:
        d.data.materials.append(steel)
    parts = [crate, peek, low, sten, sten_back, van] + doors
    for o in parts:
        print(f"{o.name}: {sum(len(p.vertices) - 2 for p in o.data.polygons)} tris")

    # --- previews: the stash outside in soft light, then the view out through the peep slot
    world = bpy.data.worlds.new("World")
    bpy.context.scene.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes["Background"]
    bg.inputs["Color"].default_value = (0.5, 0.55, 0.62, 1)
    bg.inputs["Strength"].default_value = 0.8
    sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN"))
    sun.data.energy = 3
    sun.rotation_euler = (math.radians(45), 0, math.radians(30))
    bpy.context.collection.objects.link(sun)
    base = 0.024 + 2 * (LOW_CRATE + 0.06)
    stash = Vector((-0.5, 1.15, 0))
    low.location = stash + Vector((0, 0, 0.024))
    low2 = low.copy()
    bpy.context.collection.objects.link(low2)
    low2.location = stash + Vector((0, 0, 0.024 + LOW_CRATE + 0.06))
    for o in (peek, sten_back):
        o.location = stash + Vector((0, 0, base))
    crate.hide_render = sten.hide_render = True
    for o in [van] + doors:
        o.hide_render = True
    os.makedirs(args.preview, exist_ok=True)
    camera(stash + Vector((2.4, -3.2, 1.9)), stash + Vector((0, 0, 1.0)), 32)
    render(os.path.join(args.preview, "stash.png"), args.samples)

    # from inside the top crate, through the missing slat and out of the rear-door window
    for o in [van] + doors:
        o.hide_render = False
    sun.hide_render = True
    bg.inputs["Strength"].default_value = 0.03
    street = bpy.data.objects.new("StreetGlow", bpy.data.lights.new("StreetGlow", "AREA"))
    street.data.energy = 3000
    street.data.size = 4
    street.data.color = (1.0, 0.75, 0.45)
    street.location = (0, -4.0, 2.5)
    street.rotation_euler = (math.radians(-60), 0, 0)
    bpy.context.collection.objects.link(street)
    eye = stash + Vector((0, -CRATE / 2 + 0.25, base + PEEK_EYE))
    camera(eye, eye + Vector((0, -1.0, 0)), 16)
    render(os.path.join(args.preview, "peek_view.png"), args.samples)
    bpy.data.objects.remove(street)
    bpy.data.objects.remove(low2)
    for o in parts:
        o.location = (0, 0, 0) if o not in doors else o.location

    for o in parts:
        loc = o.location.copy()
        o.location = (0, 0, 0)   # export the mesh around its own origin (the hinge for the doors)
        bpy.ops.object.select_all(action="DESELECT")
        o.select_set(True)
        bpy.context.view_layer.objects.active = o
        path = os.path.join(args.out, f"{o.name}.fbx")
        bpy.ops.export_scene.fbx(filepath=path, use_selection=True, axis_forward="-Z", axis_up="Y",
                                 apply_scale_options="FBX_SCALE_UNITS", bake_space_transform=True,
                                 mesh_smooth_type="FACE", path_mode="STRIP")
        o.location = loc
        print(path)


if __name__ == "__main__":
    main()
