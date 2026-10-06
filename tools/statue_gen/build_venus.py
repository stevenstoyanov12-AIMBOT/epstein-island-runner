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
HEAD_C = np.array([0.02, 1.93, -0.08])   # statue space; the face keeps its detail when meshes are thinned
HEAD_R = 0.21
REPO = os.path.dirname(os.path.dirname(HERE))
M = VenusStatue()


def decimate_keep_head(m, target, head_budget):
    """Decimate, but give the head region its own (much larger) triangle budget. The two parts are thinned
    separately with the same clean decimator as everything else, then welded back together at the seam."""
    near = np.linalg.norm(m.triangles_center - HEAD_C, axis=1) < HEAD_R
    if not near.any() or near.all():
        return decimate(m, target) if target < len(m.faces) else m
    hb = int(min(head_budget, near.sum()))
    parts = []
    for mask, budget in ((near, hb), (~near, max(300, target - hb))):
        sub = m.submesh([np.where(mask)[0]], append=True)
        if budget < len(sub.faces):
            sub = decimate(sub, budget)
        parts.append(sub)
    out = trimesh.util.concatenate(parts)
    out.merge_vertices(digits_vertex=4)                 # weld the seam (0.1 mm)
    out.update_faces(out.nondegenerate_faces())
    out.remove_unreferenced_vertices()
    return out


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
            field = np.empty(sub.shape, np.float32)
            ys_, zs_ = np.arange(a[1], b[1]), np.arange(a[2], b[2])
            step = max(1, 1_500_000 // (len(ys_) * len(zs_)))          # x-slabs of ~1.5M points: bounded memory
            for x0 in range(a[0], b[0], step):
                xs_ = np.arange(x0, min(x0 + step, b[0]))
                g = np.stack(np.meshgrid(xs_, ys_, zs_, indexing="ij"), -1).reshape(-1, 3)
                p = lo + g * voxel
                f = cell_field(warp(p), seeds, i) + gap / 2
                f = np.maximum(f, -p[:, 1] if side > 0 else p[:, 1] + gap / 2)
                sl = slice(x0 - a[0], x0 - a[0] + len(xs_))
                field[sl] = np.maximum(sub[sl], f.reshape(len(xs_), len(ys_), len(zs_)).astype(np.float32))
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
            pieces.append((f"{prefix}_{k:02d}", m))
    return pieces


def broken_faces(m, vol, lo, voxel):
    """Faces that lie inside the original surface are fracture faces (second material)."""
    c = (m.triangles_center - lo) / voxel
    d = map_coordinates(vol, c.T, order=1, mode="nearest")
    return d < -1.2 * voxel


# --- Blender -----------------------------------------------------------------------

def blender_decimate(ob, target, head_budget, centre):
    """Seamless thinning in Blender: pass 1 thins everything but the head, pass 2 thins only the head to its own
    budget. One mesh, no split, so no cracks; Blender's collapse doesn't leave needle triangles."""
    import bpy
    import gc

    def bake(mod_setup):
        """Apply a temporary modifier by evaluating it into a new mesh (cheaper than modifier_apply)."""
        mod_setup()
        ev = ob.evaluated_get(bpy.context.evaluated_depsgraph_get())
        new = bpy.data.meshes.new_from_object(ev)
        old = ob.data
        ob.modifiers.clear()
        ob.data = new
        bpy.data.meshes.remove(old)
        gc.collect()

    # very dense meshes: an even first pass in Blender keeps memory in check (Blender's collapse leaves no needles)
    nf = len(ob.data.polygons)
    if nf > 1_200_000:
        bake(lambda: setattr(ob.modifiers.new("pre", "DECIMATE"), "ratio", 1_200_000 / nf))
    me = ob.data
    co = np.empty(len(me.vertices) * 3, np.float32)
    me.vertices.foreach_get("co", co)
    to_b = np.array([[-1, 0, 0], [0, 0, 1], [0, 1, 0]], float)
    pts = co.reshape(-1, 3) @ to_b + centre                               # statue space
    inside = np.linalg.norm(pts - HEAD_C, axis=1) < HEAD_R
    dg = bpy.context.evaluated_depsgraph_get

    def faces_after():
        ev = ob.evaluated_get(dg())
        mm = ev.to_mesh()
        n = len(mm.polygons)
        ev.to_mesh_clear()
        return n

    def search(mod, want):
        lo_, hi_ = 0.002, 1.0
        for _ in range(6):
            mod.ratio = (lo_ + hi_) / 2
            n = faces_after()
            if n > want: hi_ = mod.ratio
            else: lo_ = mod.ratio
        mod.ratio = lo_

    if head_budget and inside.any() and not inside.all():
        vg = ob.vertex_groups.new(name="head")
        vg.add(np.where(inside)[0].tolist(), 1.0, "REPLACE")
        nhead = int(np.count_nonzero(inside)) * 2                            # ~2 triangles per vertex
        body = ob.modifiers.new("body", "DECIMATE")                          # pass 1: head protected
        body.vertex_group, body.invert_vertex_group = "head", True
        head = ob.modifiers.new("head", "DECIMATE")                          # pass 2: only the head
        head.vertex_group, head.invert_vertex_group = "head", False
        head.ratio = min(1.0, head_budget / max(nhead, 1))
        search(body, target)
    else:
        mod = ob.modifiers.new("all", "DECIMATE")
        search(mod, target)
    ev = ob.evaluated_get(dg())
    new = bpy.data.meshes.new_from_object(ev)
    old = ob.data
    ob.modifiers.clear()
    ob.vertex_groups.clear()
    ob.data = new
    bpy.data.meshes.remove(old)


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

    marble = material("Marble", (0.95, 0.94, 0.92), 0.32)
    core = material("MarbleBroken", (0.99, 0.99, 0.97), 0.8)

    root = bpy.data.objects.new("VenusStatue", None)
    scene.collection.objects.link(root)
    total = sum(len(m.faces) for _, m in pieces)
    to_b = np.array([[-1, 0, 0], [0, 0, 1], [0, 1, 0]], float)   # Y-up (figure faces -Z) -> Z-up (faces -Y)

    pieces.append(("Intact", intact))                                      # the biggest mesh last
    order = pieces                                                         # popped one by one: frees as we go
    del intact
    while order:
        name, m = order.pop(0)
        share = len(m.faces) / total
        target = max(400, int(budget * share))
        head_budget = 0
        if name == "Intact":
            target, head_budget = int(budget * 0.8), 30000          # seamless shell shown until the first hit
        elif name.startswith("Base"):
            target = max(300, target // 3)                          # flat plinth faces decimate well
        else:
            head_budget = 9000
        centre = m.centroid
        verts = ((m.vertices - centre) @ to_b.T).astype(np.float32)
        me = bpy.data.meshes.new(name)
        me.vertices.add(len(verts))
        me.vertices.foreach_set("co", verts.ravel())
        nf = len(m.faces)
        me.loops.add(nf * 3)
        me.loops.foreach_set("vertex_index", m.faces.astype(np.int32).ravel())
        me.polygons.add(nf)
        me.polygons.foreach_set("loop_start", np.arange(0, nf * 3, 3, dtype=np.int32))
        me.polygons.foreach_set("loop_total", np.full(nf, 3, np.int32))
        me.update(calc_edges=True)
        me.validate()
        ob = bpy.data.objects.new(name, me)
        ob.location = centre @ to_b.T
        ob.parent = root
        scene.collection.objects.link(ob)
        del m, verts                                                         # free the source mesh
        if target < nf:
            blender_decimate(ob, target, head_budget, centre)
        # fracture faces get the second material (sampled from the volume at each face centre)
        me = ob.data
        me.materials.clear()
        me.materials.append(marble)
        me.materials.append(core)
        cen = np.empty(len(me.polygons) * 3, np.float32)
        me.polygons.foreach_get("center", cen)
        world = cen.reshape(-1, 3) @ to_b + centre                        # back to statue space (to_b is orthonormal)
        flags = np.zeros(len(world), bool) if name == "Intact" else \
            map_coordinates(vol, ((world - lo) / voxel).T, order=1, mode="nearest") < -1.2 * voxel
        me.polygons.foreach_set("material_index", flags.astype(np.int32))
        me.polygons.foreach_set("use_smooth", np.ones(len(me.polygons), bool))
        me.update()

    # sharp edges where fracture faces meet the polished surface, smooth elsewhere
    bpy.ops.object.select_all(action="DESELECT")
    objs = [o for o in scene.objects if o.type == "MESH"]
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.shade_smooth_by_angle(angle=np.radians(50))
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
    ap.add_argument("--voxel", type=float, default=0.0022)
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
