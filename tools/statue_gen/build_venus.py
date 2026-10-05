"""Build the destructible Venus statue: sample the SDF, cut it into Voronoi pieces, finish it in Blender.

Outputs (default paths):
  UnityGame/Assets/Models/Statues/VenusStatue.fbx   one object per piece: Fig_## (figure + rock), Base_## (plinth)
  tools/statue_gen/blend/VenusStatue.blend          the same scene, for tweaking by hand
  tools/statue_gen/previews/Venus_*.png             Cycles renders

usage: python3 build_venus.py [--voxel 0.0026] [--fig 34] [--base 10] [--tris 190000] [--no-render]
Needs: pip install bpy numpy scipy scikit-image trimesh fast-simplification pillow   (Python 3.11 for bpy)
"""
import argparse
import os
import time
from multiprocessing import Pool

import numpy as np
import trimesh
from scipy.ndimage import binary_dilation, map_coordinates
from skimage.measure import marching_cubes

from build import decimate
from sdf import fbm
from venus_statue import PLINTH_H, VenusStatue

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(HERE))
M = VenusStatue()


# --- sampling ----------------------------------------------------------------------

def _eval(points):
    return M(points).astype(np.float32)


def sample(voxel, block=16):
    """Dense SDF volume, evaluated finely only in blocks near the surface."""
    lo, hi = M.bounds_min, M.bounds_max
    n = np.ceil((hi - lo) / voxel).astype(int) + 1
    nb = -(-n // block)
    # coarse pass at block centres
    cent = lo + (np.stack(np.meshgrid(*[np.arange(k) for k in nb], indexing="ij"), -1).reshape(-1, 3) + 0.5) \
        * block * voxel
    with Pool(os.cpu_count()) as pool:
        coarse = np.concatenate(pool.map(_eval, np.array_split(cent, 64)))
    reach = 1.6 * block * voxel * np.sqrt(3) / 2 + 2 * voxel
    near = np.abs(coarse) < reach
    vol = np.empty(nb * block, np.float32)
    vol[...] = np.repeat(np.repeat(np.repeat(coarse.reshape(nb), block, 0), block, 1), block, 2)
    idx = np.argwhere(near.reshape(nb))
    print(f"  refining {len(idx)}/{near.size} blocks")
    off = np.stack(np.meshgrid(*[np.arange(block)] * 3, indexing="ij"), -1).reshape(-1, 3)
    jobs = [lo + ((b * block)[None] + off) * voxel for b in idx]
    with Pool(os.cpu_count()) as pool:
        for b, vals in zip(idx, pool.imap(_eval, jobs, chunksize=8)):
            s = b * block
            vol[s[0]:s[0] + block, s[1]:s[1] + block, s[2]:s[2] + block] = vals.reshape(block, block, block)
    return vol[:n[0], :n[1], :n[2]], lo


# --- fracture ----------------------------------------------------------------------

def seeds_in(vol, lo, voxel, mask, count, rng, weight=None):
    pts = np.argwhere(mask & (vol < -voxel))
    pts = lo + pts * voxel
    w = np.ones(len(pts)) if weight is None else weight(pts)
    order = rng.choice(len(pts), size=min(len(pts), 40000), replace=False, p=w / w.sum())
    cand = pts[order]
    # spread them out: greedy farthest-point picking from the weighted candidates
    picked = [cand[0]]
    dmin = np.linalg.norm(cand - cand[0], axis=1)
    for _ in range(count - 1):
        i = int(np.argmax(dmin * (0.6 + 0.4 * rng.random(len(cand)))))
        picked.append(cand[i])
        dmin = np.minimum(dmin, np.linalg.norm(cand - cand[i], axis=1))
    return np.array(picked)


def warp(p, amt=0.022):
    """Shared domain warp so neighbouring pieces get the same rough, non-planar break."""
    w = np.stack([fbm(p * 7.0, 2, 101), fbm(p * 7.0, 2, 202), fbm(p * 7.0, 2, 303)], 1)
    return p + amt * w


def cell_field(p, seeds, i):
    """Signed distance-ish to the boundary of Voronoi cell i (negative inside)."""
    c = seeds[i]
    others = np.delete(seeds, i, 0)
    pc = np.einsum("ij,ij->i", p - c, p - c)
    best = np.full(len(p), -np.inf)
    for o in others:
        po = np.einsum("ij,ij->i", p - o, p - o)
        best = np.maximum(best, (pc - po) / (2 * np.linalg.norm(c - o)))
    return best


def cut_pieces(vol, lo, voxel, groups, gap):
    """groups: list of (prefix, seeds, sign) where sign selects y>0 (figure) or y<0 (plinth)."""
    shape = np.array(vol.shape)
    y0 = int(np.ceil(-lo[1] / voxel))                 # first voxel row with y >= 0
    pieces = []
    for prefix, seeds, side in groups:
        ya, yb = (y0, shape[1]) if side > 0 else (0, y0)
        # per-cell voxel bounding boxes, labelled one x-slab at a time to keep memory down
        bmin = np.full((len(seeds), 3), 1 << 30)
        bmax = np.full((len(seeds), 3), -1)
        count = np.zeros(len(seeds), int)
        for x in range(shape[0]):
            cc = np.argwhere(vol[x, ya:yb] < 2 * voxel)
            if not len(cc):
                continue
            cc = np.column_stack([np.full(len(cc), x), cc[:, 0] + ya, cc[:, 1]])
            pw = warp(lo + cc * voxel).astype(np.float32)
            lab = ((pw[:, None, :] - seeds[None].astype(np.float32)) ** 2).sum(-1).argmin(1)
            np.minimum.at(bmin, lab, cc)
            np.maximum.at(bmax, lab, cc)
            count += np.bincount(lab, minlength=len(seeds))
        order = np.argsort(-seeds[:, 1])
        for k, i in enumerate(order):
            if count[i] < 50:
                continue
            a = np.maximum(bmin[i] - 4, 0)
            b = np.minimum(bmax[i] + 5, shape)
            sub = vol[a[0]:b[0], a[1]:b[1], a[2]:b[2]]
            g = np.stack(np.meshgrid(*[np.arange(a[j], b[j]) for j in range(3)], indexing="ij"), -1).reshape(-1, 3)
            p = lo + g * voxel
            f = cell_field(warp(p), seeds, i) + gap / 2
            f = np.maximum(f, -p[:, 1] if side > 0 else p[:, 1] + gap / 2)
            field = np.maximum(sub, f.reshape(sub.shape).astype(np.float32))
            pad = np.pad(field, 1, constant_values=1.0)
            try:
                v, fc, _, _ = marching_cubes(pad, 0.0, spacing=(voxel,) * 3)
            except (ValueError, RuntimeError):
                continue
            m = trimesh.Trimesh(v + lo + (a - 1) * voxel, fc[:, ::-1], process=True)
            parts = [x for x in m.split(only_watertight=False) if len(x.faces) > 120]
            if not parts:
                continue
            m = trimesh.util.concatenate(parts)
            if m.volume < 0:
                m.invert()
            trimesh.smoothing.filter_taubin(m, iterations=4)
            m = decimate(m, max(2000, len(m.faces) // 4))     # Blender does the final reduction
            pieces.append((f"{prefix}_{k:02d}", m))
    return pieces


def broken_faces(m, vol, lo, voxel):
    """Faces that lie inside the original surface are fracture faces (second material)."""
    c = (m.triangles_center - lo) / voxel
    d = map_coordinates(vol, c.T, order=1, mode="nearest")
    return d < -1.2 * voxel


# --- Blender -----------------------------------------------------------------------

def blender_scene(pieces, intact, vol, lo, voxel, budget, blend_path, fbx_path, preview_dir, render):
    import bpy

    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene

    def material(name, color, rough):
        mat = bpy.data.materials.new(name)
        mat.use_nodes = True
        bsdf = mat.node_tree.nodes["Principled BSDF"]
        bsdf.inputs["Base Color"].default_value = (*color, 1)
        bsdf.inputs["Roughness"].default_value = rough
        bsdf.inputs["Subsurface Weight"].default_value = 0.15
        bsdf.inputs["Subsurface Radius"].default_value = (0.02, 0.015, 0.01)
        return mat

    marble = material("Marble", (0.86, 0.84, 0.80), 0.32)
    core = material("MarbleBroken", (0.93, 0.92, 0.89), 0.75)

    root = bpy.data.objects.new("VenusStatue", None)
    scene.collection.objects.link(root)
    total = sum(len(m.faces) for _, m in pieces)
    to_b = np.array([[-1, 0, 0], [0, 0, 1], [0, 1, 0]], float)   # Y-up (figure faces -Z) -> Z-up (faces -Y)

    for name, m in [("Intact", intact)] + pieces:
        share = len(m.faces) / total
        target = max(400, int(budget * share))
        if name == "Intact":
            target = int(budget * 0.8)                       # seamless shell shown until the first hit
        elif name.startswith("Base"):
            target = max(300, target // 3)                  # flat plinth faces decimate well
        if target < len(m.faces):
            m = decimate(m, target)
        flags = broken_faces(m, vol, lo, voxel) if name != "Intact" else np.zeros(len(m.faces), bool)
        centre = m.centroid
        verts = (m.vertices - centre) @ to_b.T
        me = bpy.data.meshes.new(name)
        me.from_pydata(verts.tolist(), [], m.faces.tolist())
        me.materials.append(marble)
        me.materials.append(core)
        me.polygons.foreach_set("material_index", flags.astype(np.int32))
        me.update()
        ob = bpy.data.objects.new(name, me)
        ob.location = centre @ to_b.T
        ob.parent = root
        scene.collection.objects.link(ob)
        me.polygons.foreach_set("use_smooth", np.ones(len(me.polygons), bool))   # faces already wound outward (trimesh)

    # sharp edges where fracture faces meet the polished surface, smooth elsewhere
    bpy.ops.object.select_all(action="DESELECT")
    objs = [o for o in scene.objects if o.type == "MESH"]
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.shade_smooth_by_angle(angle=np.radians(50))
    # UVs for marble textures later
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=np.radians(60), island_margin=0.004)
    bpy.ops.object.mode_set(mode="OBJECT")
    tris = sum(len(o.data.polygons) for o in objs)
    print(f"  blender: {len(objs)} pieces, {tris} triangles")

    os.makedirs(os.path.dirname(fbx_path), exist_ok=True)
    root.select_set(True)
    bpy.ops.export_scene.fbx(filepath=fbx_path, use_selection=True, object_types={"EMPTY", "MESH"},
                             axis_forward="-Z", axis_up="Y", apply_scale_options="FBX_SCALE_UNITS",
                             bake_space_transform=True, mesh_smooth_type="FACE", add_leaf_bones=False,
                             use_mesh_modifiers=True)
    os.makedirs(os.path.dirname(blend_path), exist_ok=True)

    bpy.ops.wm.save_as_mainfile(filepath=blend_path, compress=True)
    if render:
        renders(scene, root, objs, preview_dir)


def renders(scene, root, objs, out):
    import bpy
    from mathutils import Vector
    os.makedirs(out, exist_ok=True)
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = 48
    scene.cycles.use_denoising = True
    scene.render.resolution_x, scene.render.resolution_y = 900, 1300
    scene.view_settings.view_transform = "AgX"
    world = bpy.data.worlds.new("World")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs["Color"].default_value = (0.55, 0.42, 0.50, 1)
    world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.6
    scene.world = world

    floor = bpy.data.meshes.new("Floor")
    floor.from_pydata([(-8, -8, -PLINTH_H), (8, -8, -PLINTH_H), (8, 8, -PLINTH_H), (-8, 8, -PLINTH_H)], [], [(0, 1, 2, 3)])
    fo = bpy.data.objects.new("Floor", floor)
    fm = bpy.data.materials.new("FloorMat")
    fm.use_nodes = True
    fm.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.75, 0.62, 0.70, 1)
    floor.materials.append(fm)
    scene.collection.objects.link(fo)

    sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN"))
    sun.data.energy = 4.0
    sun.data.angle = np.radians(3)
    sun.data.color = (1.0, 0.86, 0.72)
    sun.rotation_euler = (np.radians(50), 0, np.radians(-35))
    scene.collection.objects.link(sun)

    cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
    cam.data.lens = 60
    scene.collection.objects.link(cam)
    scene.camera = cam

    def shoot(name, yaw, height, dist, look_z, lens=60):
        a = np.radians(yaw)
        cam.data.lens = lens
        cam.location = (np.sin(a) * dist, -np.cos(a) * dist, height)
        d = Vector((0, 0, look_z)) - cam.location
        cam.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = os.path.join(out, f"Venus_{name}.png")
        bpy.ops.render.render(write_still=True)
        print("  rendered", scene.render.filepath)

    intact = scene.objects["Intact"]
    pieces = [o for o in objs if o is not intact]
    for o in pieces:
        o.hide_render = True
    shoot("front", 0, 0.9, 6.2, 0.55)
    shoot("three_quarter", -35, 1.0, 6.2, 0.55)
    shoot("upper", -20, 1.75, 2.2, 1.45, lens=70)
    # shot-up state: knock a few upper pieces loose to show how it breaks
    intact.hide_render = True
    for o in pieces:
        o.hide_render = False
    figs = sorted([o for o in objs if o.name.startswith("Fig")], key=lambda o: -o.location.z)
    rng = np.random.default_rng(4)
    for o in figs[2:9:2]:
        o.location += Vector((rng.uniform(-0.6, 0.6), rng.uniform(-0.9, -0.3), 0))
        o.location.z = -PLINTH_H + 0.12
        o.rotation_euler = rng.uniform(-1.2, 1.2, 3).tolist()
    shoot("damaged", -25, 1.0, 6.2, 0.55)
    bpy.data.objects.remove(fo)
    bpy.data.objects.remove(sun)
    bpy.data.objects.remove(cam)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--voxel", type=float, default=0.0026)
    ap.add_argument("--fig", type=int, default=34)
    ap.add_argument("--base", type=int, default=10)
    ap.add_argument("--tris", type=int, default=190000)
    ap.add_argument("--seed", type=int, default=7)
    ap.add_argument("--fbx", default=os.path.join(REPO, "UnityGame/Assets/Models/Statues/VenusStatue.fbx"))
    ap.add_argument("--blend", default=os.path.join(HERE, "blend/VenusStatue.blend"))
    ap.add_argument("--preview", default=os.path.join(HERE, "previews"))
    ap.add_argument("--no-render", action="store_true")
    ap.add_argument("--cache", default="", help="save/load the cut pieces here (path prefix) to rerun only the Blender step")
    args = ap.parse_args()

    t = time.time()
    if args.cache and os.path.exists(args.cache + ".npz"):
        z = np.load(args.cache + ".npz", allow_pickle=True)
        vol, lo = np.load(args.cache + ".vol.npy", mmap_mode="r"), z["lo"]
        pieces = [(n, trimesh.Trimesh(v, f, process=False)) for n, v, f in z["pieces"]]
        intact = trimesh.Trimesh(z["iv"], z["if"], process=False)
        print(f"loaded {len(pieces)} pieces from cache")
        blender_scene(pieces, intact, vol, lo, args.voxel, args.tris, args.blend, args.fbx, args.preview, not args.no_render)
        print(f"done in {time.time() - t:.0f}s")
        return
    vol, lo = sample(args.voxel)
    print(f"sdf {vol.shape} in {time.time() - t:.0f}s")

    rng = np.random.default_rng(args.seed)
    ys = lo[1] + np.arange(vol.shape[1]) * args.voxel
    up = np.broadcast_to((ys >= 0.02)[None, :, None], vol.shape)
    down = np.broadcast_to((ys < -0.02)[None, :, None], vol.shape)
    # denser pieces on the upper body, where most shots land
    fig = seeds_in(vol, lo, args.voxel, up, args.fig, rng, weight=lambda p: 0.5 + p[:, 1])
    base = seeds_in(vol, lo, args.voxel, down, args.base, rng)
    pieces = cut_pieces(vol, lo, args.voxel, [("Fig", fig, 1), ("Base", base, -1)], gap=0.0006)
    print(f"{len(pieces)} pieces, {sum(len(m.faces) for _, m in pieces)} raw tris in {time.time() - t:.0f}s")
    v, f, _, _ = marching_cubes(np.pad(vol, 1, constant_values=1.0), 0.0, spacing=(args.voxel,) * 3)
    intact = trimesh.Trimesh(v + lo - args.voxel, f[:, ::-1], process=True)
    if intact.volume < 0:
        intact.invert()
    trimesh.smoothing.filter_taubin(intact, iterations=4)
    intact = decimate(intact, len(intact.faces) // 4)

    if args.cache:
        np.save(args.cache + ".vol.npy", vol)
        np.savez(args.cache + ".npz", lo=lo, iv=intact.vertices, **{"if": intact.faces},
                 pieces=np.array([(n, m.vertices, m.faces) for n, m in pieces], dtype=object))
    if args.cache:
        np.save(args.cache + ".vol.npy", vol)
        np.savez(args.cache + ".npz", lo=lo, iv=intact.vertices, **{"if": intact.faces},
                 pieces=np.array([(n, m.vertices, m.faces) for n, m in pieces], dtype=object))
    blender_scene(pieces, intact, vol, lo, args.voxel, args.tris, args.blend, args.fbx, args.preview, not args.no_render)
    print(f"done in {time.time() - t:.0f}s")


if __name__ == "__main__":
    main()
