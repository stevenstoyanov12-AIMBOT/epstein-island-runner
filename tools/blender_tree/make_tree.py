"""Build the autumn tree in Blender (bpy): bark with real displaced relief, maple leaf cards,
fallen leaves, roots. Renders previews with Cycles and exports FBX + textures for Unity.

usage: python3 make_tree.py [--seed 7] [--leaves 14000] [--out DIR] [--preview DIR]
"""
import argparse
import math
import os
import shutil
import sys

import bpy  # must come before bmesh when Blender runs as a Python module
import bmesh
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "statue_gen"))
sys.path.insert(0, HERE)
from autumn_tree import grow  # noqa: E402  (branch skeleton)
import textures  # noqa: E402

BARK_TILE_U = 0.55   # metres of circumference covered by the bark texture width
BARK_TILE_V = 1.1    # metres of length covered by its height


# --- skeleton ---------------------------------------------------------------------

def catmull_rom(pts, per_seg):
    p = np.vstack([pts[0] * 2 - pts[1], pts, pts[-1] * 2 - pts[-2]])
    out = []
    for i in range(1, len(p) - 2):
        for t in np.linspace(0, 1, per_seg, endpoint=False):
            t2, t3 = t * t, t * t * t
            out.append(0.5 * ((2 * p[i]) + (-p[i - 1] + p[i + 1]) * t
                              + (2 * p[i - 1] - 5 * p[i] + 4 * p[i + 1] - p[i + 2]) * t2
                              + (-p[i - 1] + 3 * p[i] - 3 * p[i + 1] + p[i + 2]) * t3))
    out.append(pts[-1])
    return np.array(out)


def roots(rng, count=6):
    out = []
    for k in range(count):
        a = 2 * math.pi * k / count + rng.uniform(-0.3, 0.3)
        d = np.array([math.cos(a), 0, math.sin(a)])
        pts = [np.array([0, 0.35, 0]), d * 0.18 + [0, 0.12, 0], d * 0.5 + [0, 0.0, 0],
               d * rng.uniform(0.8, 1.1) + [0, -0.08, 0]]
        out.append((np.array(pts), np.array([0.2, 0.15, 0.07, 0.02]), -1))
    return out


# --- meshes -----------------------------------------------------------------------

def tube_bmesh(bm, uv_layer, weight_layer, pts, radii):
    """Add a UV-mapped tube; resolution follows the radius so thick parts get more detail."""
    r0 = float(radii[0])
    sides = int(np.clip(2 * math.pi * r0 / 0.018, 6, 56))
    seglen = np.linalg.norm(np.diff(pts, axis=0), axis=1).sum()
    spacing = np.clip(r0 * 0.35, 0.012, 0.12)
    per_seg = max(2, int(seglen / spacing / (len(pts) - 1)))
    if r0 < 0.012:  # leafy twigs: few sides and rings, they are only a few millimetres thick
        sides, per_seg = 4, 2
    path = catmull_rom(pts, per_seg)
    rad = np.interp(np.linspace(0, 1, len(path)), np.linspace(0, 1, len(radii)), radii)
    tang = np.gradient(path, axis=0)
    tang /= np.linalg.norm(tang, axis=1, keepdims=True)
    n = np.cross(tang[0], [1.0, 0, 0])
    if np.linalg.norm(n) < 1e-3:
        n = np.cross(tang[0], [0, 0, 1.0])
    n /= np.linalg.norm(n)
    u_repeat = max(1, round(2 * math.pi * r0 / BARK_TILE_U))
    arc = np.concatenate([[0], np.cumsum(np.linalg.norm(np.diff(path, axis=0), axis=1))])
    rings = []
    for p, r, t in zip(path, rad, tang):
        n = n - t * (n @ t)
        n /= np.linalg.norm(n)
        b = np.cross(t, n)
        ring = []
        for j in range(sides + 1):  # duplicate seam column for clean UVs
            a = 2 * math.pi * j / sides
            v = bm.verts.new(p + r * (math.cos(a) * n + math.sin(a) * b))
            v[weight_layer] = float(np.clip(r / 0.12, 0.05, 1.0))  # displacement strength
            ring.append(v)
        rings.append(ring)
    for i in range(len(rings) - 1):
        for j in range(sides):
            f = bm.faces.new((rings[i][j], rings[i][j + 1], rings[i + 1][j + 1], rings[i + 1][j]))
            for loop, (jj, ii) in zip(f.loops, ((j, i), (j + 1, i), (j + 1, i + 1), (j, i + 1))):
                loop[uv_layer].uv = (jj / sides * u_repeat, arc[ii] / BARK_TILE_V)
    tip = bm.verts.new(path[-1] + tang[-1] * rad[-1] * 0.5)
    tip[weight_layer] = 0.0
    for v in rings[-1]:
        v[weight_layer] = 0.0  # keep the end caps smooth instead of spiky
    for j in range(sides):
        f = bm.faces.new((rings[-1][j], rings[-1][j + 1], tip))
        for loop in f.loops:
            loop[uv_layer].uv = (j / sides * u_repeat, arc[-1] / BARK_TILE_V)


def build_bark(branches):
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new("UVMap")
    w = bm.verts.layers.float.new("disp")
    for pts, radii, depth in branches:
        tube_bmesh(bm, uv, w, pts, np.maximum(radii, 0.007))
    me = bpy.data.meshes.new("Tree_Bark")
    bm.to_mesh(me)
    obj = bpy.data.objects.new("Tree_Bark", me)
    bpy.context.collection.objects.link(obj)
    # copy the per-vertex strength into a vertex group for the Displace modifier
    vg = obj.vertex_groups.new(name="disp")
    bm2 = bmesh.new()
    bm2.from_mesh(me)
    lay = bm2.verts.layers.float.get("disp")
    for v in bm2.verts:
        vg.add([v.index], v[lay], "REPLACE")
    bm2.free()
    bm.free()
    return obj


def leaf_card(size, curl, twist, rng):
    """3x3-vertex leaf card, cupped and bent; returns local verts, faces, uvs (0..1)."""
    verts, uvs = [], []
    for j in range(3):
        for i in range(3):
            x, y = (i / 2 - 0.5) * size, (j / 2 - 0.12) * size
            z = curl * (x / size) ** 2 * size + twist * (y / size) ** 2 * size
            verts.append((x, y, z))
            uvs.append((i / 2, j / 2))
    faces = []
    for j in range(2):
        for i in range(2):
            a = j * 3 + i
            faces.append((a, a + 1, a + 4, a + 3))
    return np.array(verts), faces, uvs


def random_rotation(rng):
    q = rng.normal(size=4)
    q /= np.linalg.norm(q)
    w, x, y, z = q
    return np.array([[1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
                     [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
                     [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]])


def leaf_frame(direction, rng):
    """Rotation whose local +Y (leaf tip) follows `direction` and whose face turns up to the light."""
    y = direction / np.linalg.norm(direction)
    up = np.array([0, 0, 1.0]) + rng.normal(0, 0.35, 3)
    z = up - y * (up @ y)
    if np.linalg.norm(z) < 1e-3:
        z = np.cross(y, [1.0, 0, 0])
    z /= np.linalg.norm(z)
    x = np.cross(y, z)
    return np.column_stack([x, y, z])


def sprout_twigs(skel, rng, leaf_target):
    """Add short leafy twigs along the thinnest branches and attach every leaf by its stem.

    Returns (twig skeletons, leaf placements). Leaves sit at nodes along each twig, alternating
    sides, pointing outward and drooping slightly, with the stem end touching the twig."""
    hosts = [catmull_rom(p, 4) for p, _, d in skel if d >= 4]
    top = max(h[:, 2].max() for h in hosts)
    candidates = []
    for path in hosts:
        seg = np.linalg.norm(np.diff(path, axis=0), axis=1)
        arc = np.concatenate([[0], np.cumsum(seg)])
        for t in np.arange(0.06, arc[-1], 0.09):
            i = min(np.searchsorted(arc, t), len(path) - 1)
            tangent = path[min(i + 1, len(path) - 1)] - path[max(i - 1, 0)]
            candidates.append((path[i], tangent / np.linalg.norm(tangent)))
    per_twig = leaf_target / len(candidates)
    twigs, placements = [], []
    for base, tangent in candidates:
        side = np.cross(tangent, rng.normal(size=3))
        side /= np.linalg.norm(side)
        d = tangent * 0.6 + side * 0.8 + np.array([0, 0, 0.15])
        d /= np.linalg.norm(d)
        length = rng.uniform(0.14, 0.26)
        bend = np.array([0, 0, -0.04]) + rng.normal(0, 0.02, 3)
        pts = np.array([base, base + d * length * 0.5 + bend * 0.3, base + d * length + bend])
        twigs.append((pts, np.array([0.009, 0.006, 0.004]), 6))
        n_leaves = rng.poisson(per_twig)
        for k in range(n_leaves):
            t = 1.0 if k == 0 else rng.uniform(0.3, 1.0)       # one leaf at the tip, the rest along it
            node = pts[0] + (pts[2] - pts[0]) * t
            out_dir = d if k == 0 else d * 0.5 + np.cross(d, [0, 0, 1.0]) * (1 if k % 2 else -1)
            out_dir = out_dir + np.array([0, 0, -0.35]) + rng.normal(0, 0.2, 3)  # leaves droop
            R = leaf_frame(out_dir, rng)
            size = rng.uniform(0.09, 0.14)
            stem = rng.uniform(0.015, 0.035)
            # card origin is 12% up the leaf from its stem end, so offset along the leaf axis
            pos = node + R[:, 1] * (stem + 0.12 * size)
            h = np.clip((pos[2] - 1.5) / (top - 1.5), 0, 1)
            cell = int(np.clip(rng.normal(h * 3.3, 0.9), 0, 3.999))
            placements.append((pos, R, size, cell))
    return twigs, placements


def build_leaves(name, placements, rng):
    """placements: list of (position, rotation matrix, size). Atlas cell picked by height."""
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new("UVMap")
    for pos, R, size, cell in placements:
        lv, lf, luv = leaf_card(size, rng.uniform(0.05, 0.3), rng.uniform(-0.25, 0.25), rng)
        vs = [bm.verts.new(pos + R @ v) for v in lv]
        cu, cv = (cell % 2) * 0.5, 0.5 - (cell // 2) * 0.5   # atlas cells, v flipped (image rows)
        for f in lf:
            face = bm.faces.new([vs[k] for k in f])
            for loop, k in zip(face.loops, f):
                loop[uv].uv = (cu + luv[k][0] * 0.5, cv + luv[k][1] * 0.5)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    obj = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(obj)
    return obj


# --- materials, render, export -----------------------------------------------------

def image_node(nodes, path, non_color=False):
    n = nodes.new("ShaderNodeTexImage")
    n.image = bpy.data.images.load(path)
    if non_color:
        n.image.colorspace_settings.name = "Non-Color"
    return n


def bark_material(tex):
    m = bpy.data.materials.new("Bark")
    m.use_nodes = True
    nt = m.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    alb = image_node(nt.nodes, os.path.join(tex, "bark_albedo.png"))
    nrm = image_node(nt.nodes, os.path.join(tex, "bark_normal.png"), True)
    nmap = nt.nodes.new("ShaderNodeNormalMap")
    nt.links.new(alb.outputs["Color"], bsdf.inputs["Base Color"])
    nt.links.new(nrm.outputs["Color"], nmap.inputs["Color"])
    nt.links.new(nmap.outputs["Normal"], bsdf.inputs["Normal"])
    bsdf.inputs["Roughness"].default_value = 0.9
    return m


def build_grass(rng, tex, count=7000, inner=0.35, outer=1.5):
    """Tufts of curved, tapered grass blades in a ring around the trunk, thinning out at the edge."""
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new("UVMap")
    made = 0
    while made < count:
        a = rng.uniform(0, 2 * math.pi)
        # ragged, uneven edge instead of a perfect circle
        edge = outer * (0.8 + 0.12 * math.sin(3 * a + 1.0) + 0.08 * math.sin(7 * a + 2.0) + 0.05 * math.sin(13 * a))
        r = inner + (edge - inner) * math.sqrt(rng.random())
        if rng.random() > 1.2 - (r - inner) / (edge - inner):   # sparser toward the edge
            continue
        tuft = np.array([r * math.cos(a), r * math.sin(a), 0.0])
        for _ in range(rng.integers(4, 9)):
            base = tuft + np.append(rng.normal(0, 0.035, 2), 0.0)
            height = rng.uniform(0.1, 0.26) * (1.15 - 0.5 * (r - inner) / (edge - inner))
            width = rng.uniform(0.008, 0.014)
            yaw = rng.uniform(0, 2 * math.pi)
            lean = np.array([math.cos(yaw), math.sin(yaw), 0.0]) * rng.uniform(0.2, 0.6)
            side = np.array([-math.sin(yaw), math.cos(yaw), 0.0])
            tint = rng.random()
            rows = []
            for k, t in enumerate((0.0, 0.35, 0.7, 1.0)):
                c = base + np.array([0, 0, height * t]) + lean * height * t * t
                wdt = width * (1 - t) if k < 3 else 0.0
                rows.append((bm.verts.new(c - side * wdt), bm.verts.new(c + side * wdt), t))
            for (l0, r0, t0), (l1, r1, t1) in zip(rows[:-1], rows[1:]):
                f = bm.faces.new((l0, r0, r1, l1)) if l1 is not r1 else None
                if f:
                    for loop, (uu, vv) in zip(f.loops, ((0, t0), (1, t0), (1, t1), (0, t1))):
                        loop[uv].uv = (tint * 0.9 + uu * 0.1, vv)
            made += 1
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6)
    me = bpy.data.meshes.new("Tree_Grass")
    bm.to_mesh(me)
    bm.free()
    obj = bpy.data.objects.new("Tree_Grass", me)
    bpy.context.collection.objects.link(obj)
    m = bpy.data.materials.new("Grass")
    m.use_nodes = True
    img = m.node_tree.nodes.new("ShaderNodeTexImage")
    img.image = bpy.data.images.load(os.path.join(tex, "grass.png"))
    m.node_tree.links.new(img.outputs["Color"], m.node_tree.nodes["Principled BSDF"].inputs["Base Color"])
    obj.data.materials.append(m)
    return obj


def leaf_material(tex):
    m = bpy.data.materials.new("Leaves")
    m.use_nodes = True
    nt = m.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    alb = image_node(nt.nodes, os.path.join(tex, "leaves_atlas.png"))
    nt.links.new(alb.outputs["Color"], bsdf.inputs["Base Color"])
    nt.links.new(alb.outputs["Alpha"], bsdf.inputs["Alpha"])
    bsdf.inputs["Roughness"].default_value = 0.6
    # light glowing through thin leaves
    if "Transmission Weight" in bsdf.inputs:
        bsdf.inputs["Transmission Weight"].default_value = 0.15
    return m


def ground(size=8):
    bpy.ops.mesh.primitive_plane_add(size=size)
    g = bpy.context.active_object
    g.name = "PreviewGround"
    m = bpy.data.materials.new("Ground")
    m.use_nodes = True
    m.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.18, 0.22, 0.1, 1)
    g.data.materials.append(m)
    return g


def render(path, cam_loc, target, samples=48, res=900):
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.samples = samples
    scene.cycles.use_denoising = True
    scene.render.resolution_x = scene.render.resolution_y = res
    if "Camera" not in bpy.data.objects:
        cam = bpy.data.objects.new("Camera", bpy.data.cameras.new("Camera"))
        bpy.context.collection.objects.link(cam)
        scene.camera = cam
    cam = bpy.data.objects["Camera"]
    cam.location = cam_loc
    d = np.array(target) - np.array(cam_loc)
    cam.rotation_euler = (math.atan2(math.hypot(d[0], d[1]), -d[2]), 0, math.atan2(d[1], d[0]) - math.pi / 2)
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    print(path)


def lights():
    world = bpy.context.scene.world or bpy.data.worlds.new("World")
    bpy.context.scene.world = world
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs["Color"].default_value = (0.55, 0.65, 0.8, 1)
    world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.6
    sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN"))
    sun.data.energy = 4.0
    sun.data.angle = math.radians(3)
    sun.rotation_euler = (math.radians(50), 0, math.radians(35))
    bpy.context.collection.objects.link(sun)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--seed", type=int, default=7)
    ap.add_argument("--leaves", type=int, default=14000)
    ap.add_argument("--fallen", type=int, default=1400)
    ap.add_argument("--out", default=os.path.join(HERE, "..", "..", "UnityGame", "Assets", "Models", "Trees"))
    ap.add_argument("--preview", default="previews")
    ap.add_argument("--samples", type=int, default=48)
    args = ap.parse_args()
    rng = np.random.default_rng(args.seed)

    bpy.ops.wm.read_factory_settings(use_empty=True)
    tex = os.path.join(args.out, "Textures")
    os.makedirs(tex, exist_ok=True)
    textures.bark(tex)
    textures.leaves(tex)
    textures.grass(tex)

    # skeleton in Y-up space (from the numpy generator), converted to Blender's Z-up
    branches = []
    grow(rng, np.array([0, 0, 0.0]), np.array([0.05, 1, 0.02]), 1.7, 0.27, 0, branches)
    # shorten only the trunk by 0.5 m; the crown keeps its size and just sits lower
    squash = 0.5
    t_pts, t_r, _ = branches[0]
    branches[0] = (t_pts * [1, (t_pts[-1, 1] - squash) / t_pts[-1, 1], 1], t_r, 0)
    branches[1:] = [(p - [0, squash, 0], r, d) for p, r, d in branches[1:]]
    # fold the root flare into the start of the trunk so there is no seam between them
    trunk_pts, trunk_r, _ = branches[0]
    keep = trunk_pts[:, 1] > 0.45
    branches[0] = (np.vstack([[[0, -0.03, 0], [0, 0.1, 0], [0, 0.28, 0]], trunk_pts[keep]]),
                   np.concatenate([[0.40, 0.33, 0.29], trunk_r[keep]]), 0)
    # root the main limbs inside the top of the trunk, each on its own side and thicker there,
    # so together they swallow the trunk's end instead of leaving a ledge around it
    tpts, trad, _ = branches[0]
    tip, tdir = tpts[-1], (tpts[-1] - tpts[-3]) / np.linalg.norm(tpts[-1] - tpts[-3])
    top_r = trad[-1]
    for i, (pts, radii, depth) in enumerate(branches):
        if depth != 1 or np.linalg.norm(pts[0] - tip) > 1e-6:
            continue
        out = pts[1] - pts[0]
        out = out - tdir * (out @ tdir)
        out /= np.linalg.norm(out)
        start = tip - tdir * 0.5 + out * top_r * 0.3
        mid = tip - tdir * 0.12 + out * top_r * 0.45
        branches[i] = (np.vstack([start, mid, pts[1:]]),
                       np.concatenate([[top_r * 0.78, top_r * 0.74], radii[1:]]), depth)
    skel = roots(rng) + branches
    yz = np.array([[1, 0, 0], [0, 0, -1], [0, 1, 0]])  # (x, y, z)_yup -> (x, -z, y)_zup
    skel_z = [(pts @ yz.T, radii, depth) for pts, radii, depth in skel]

    # short twigs sprout from the thin branches and carry the leaves on stems
    twiglets, placements = sprout_twigs(skel_z, rng, args.leaves)
    skel_z += twiglets
    bark = build_bark(skel_z)
    disp_tex = bpy.data.textures.new("BarkHeight", "IMAGE")
    disp_tex.image = bpy.data.images.load(os.path.join(tex, "bark_height.png"))
    disp_tex.image.colorspace_settings.name = "Non-Color"
    mod = bark.modifiers.new("Relief", "DISPLACE")
    mod.texture = disp_tex
    mod.texture_coords = "UV"
    mod.vertex_group = "disp"
    mod.strength = 0.035
    mod.mid_level = 0.6
    bpy.context.view_layer.objects.active = bark
    bpy.ops.object.modifier_apply(modifier="Relief")
    bpy.ops.object.shade_smooth()
    bark.data.materials.append(bark_material(tex))

    crown = build_leaves("Tree_Leaves", placements, rng)
    fallen = []
    for _ in range(args.fallen):
        r = 2.8 * math.sqrt(rng.random())
        a = rng.uniform(0, 2 * math.pi)
        yaw, tilt = rng.uniform(0, 2 * math.pi), rng.uniform(-0.3, 0.3)
        Rz = np.array([[math.cos(yaw), -math.sin(yaw), 0], [math.sin(yaw), math.cos(yaw), 0], [0, 0, 1]])
        Rx = np.array([[1, 0, 0], [0, math.cos(tilt), -math.sin(tilt)], [0, math.sin(tilt), math.cos(tilt)]])
        pos = np.array([r * math.cos(a), r * math.sin(a), 0.012 + rng.uniform(0, 0.01)])
        fallen.append((pos, Rz @ Rx, rng.uniform(0.1, 0.15), int(rng.integers(4))))
    fallen_obj = build_leaves("Tree_FallenLeaves", fallen, rng)
    grass_obj = build_grass(rng, tex)
    leaf_mat = leaf_material(tex)
    for o in (crown, fallen_obj):
        o.data.materials.append(leaf_mat)
        bpy.context.view_layer.objects.active = o
        bpy.ops.object.shade_smooth()

    for o in (bark, crown, fallen_obj, grass_obj):
        print(f"{o.name}: {sum(len(p.vertices) - 2 for p in o.data.polygons)} tris")

    # previews
    lights()
    g = ground()
    os.makedirs(args.preview, exist_ok=True)
    render(os.path.join(args.preview, "tree_full.png"), (0, -9.5, 2.6), (0, 0, 2.2), args.samples)
    render(os.path.join(args.preview, "tree_trunk.png"), (0.9, -2.2, 1.9), (0, 0, 1.6), args.samples)
    render(os.path.join(args.preview, "tree_leaves.png"), (0.9, -2.2, 3.4), (0.4, 0, 3.3), args.samples)
    bpy.data.objects.remove(g)

    # export each part as FBX for Unity (Y-up, metres)
    for o in (bark, crown, fallen_obj, grass_obj):
        bpy.ops.object.select_all(action="DESELECT")
        o.select_set(True)
        bpy.context.view_layer.objects.active = o
        bpy.ops.export_scene.fbx(filepath=os.path.join(args.out, f"{o.name}.fbx"), use_selection=True,
                                 axis_forward="-Z", axis_up="Y", apply_scale_options="FBX_SCALE_UNITS",
                                 bake_space_transform=True, mesh_smooth_type="FACE", path_mode="STRIP")
        print(os.path.join(args.out, f"{o.name}.fbx"))


if __name__ == "__main__":
    main()
