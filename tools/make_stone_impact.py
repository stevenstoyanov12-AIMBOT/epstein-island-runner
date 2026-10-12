# Generates the stone-impact effect assets with Blender's bpy module.
# Run: python tools/make_stone_impact.py  (needs `pip install bpy numpy`)
import bpy, bmesh, random, math, os
import numpy as np
from mathutils import Vector, noise

OUT = os.path.join(os.path.dirname(__file__), "..", "UnityGame", "Assets", "Effects", "StoneImpact")
OUT = os.path.abspath(OUT)
random.seed(7); np.random.seed(7)
bpy.ops.wm.read_factory_settings(use_empty=True)

# ---------- 8 low-poly stone chunks ----------
for i in range(8):
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=2, radius=0.5)
    off = Vector((random.uniform(-50, 50) for _ in range(3)))
    sx, sy, sz = random.uniform(.7, 1.3), random.uniform(.5, 1.0), random.uniform(.6, 1.2)
    for v in bm.verts:
        n = noise.noise(v.co * 2.2 + off)
        v.co = v.co * (1 + 0.35 * n)
        v.co.x *= sx; v.co.y *= sy; v.co.z *= sz
    # flat cuts make it read as broken stone, not a pebble
    for _ in range(random.randint(2, 3)):
        d = Vector((random.uniform(-1, 1) for _ in range(3))).normalized()
        bmesh.ops.bisect_plane(bm, geom=bm.verts[:] + bm.edges[:] + bm.faces[:],
                               plane_co=d * random.uniform(.15, .3), plane_no=d, clear_outer=True)
        bmesh.ops.holes_fill(bm, edges=[e for e in bm.edges if e.is_boundary])
    bmesh.ops.triangulate(bm, faces=bm.faces[:])
    me = bpy.data.meshes.new(f"StoneChunk_{i}")
    bm.to_mesh(me); bm.free()
    for p in me.polygons: p.use_smooth = False
    ob = bpy.data.objects.new(f"StoneChunk_{i}", me)
    bpy.context.collection.objects.link(ob)
    ob.location.x = i * 1.5
    bpy.context.view_layer.objects.active = ob
    ob.select_set(True)
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(island_margin=0.02)
    bpy.ops.object.mode_set(mode='OBJECT')
    ob.select_set(False)
    # stays ~100 tris so 40 of them per hit is nothing
    print(ob.name, len(me.polygons), "tris")

for ob in bpy.data.objects:
    ob.select_set(True)
bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, "StoneChunks.fbx"), use_selection=True,
                         apply_scale_options='FBX_SCALE_UNITS', axis_forward='-Z', axis_up='Y',
                         mesh_smooth_type='FACE', bake_space_transform=True)

# ---------- textures ----------
def fbm(shape, scale, oct=5, seed=0):
    h, w = shape
    rng = np.random.default_rng(seed)
    out = np.zeros(shape, np.float32); amp = 1; tot = 0
    for o in range(oct):
        s = scale * 2 ** o
        g = rng.random((int(s) + 2, int(s) + 2)).astype(np.float32)
        ys = np.linspace(0, s, h); xs = np.linspace(0, s, w)
        y0 = ys.astype(int); x0 = xs.astype(int); fy = (ys - y0)[:, None]; fx = (xs - x0)[None, :]
        fy = fy * fy * (3 - 2 * fy); fx = fx * fx * (3 - 2 * fx)
        a = g[y0][:, x0]; b = g[y0][:, x0 + 1]; c = g[y0 + 1][:, x0]; d = g[y0 + 1][:, x0 + 1]
        out += amp * ((a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy)
        tot += amp; amp *= 0.5
    return out / tot

def save(name, rgba):
    h, w, _ = rgba.shape
    img = bpy.data.images.new(name, w, h, alpha=True)
    img.pixels = np.flipud(np.clip(rgba, 0, 1)).ravel().tolist()
    img.filepath_raw = os.path.join(OUT, name + ".png"); img.file_format = 'PNG'
    img.save()

# Dust puff flipbook: 4x4 frames, 128px each, puff grows and thins out
F, S = 4, 128
sheet = np.zeros((F * S, F * S, 4), np.float32)
yy, xx = np.mgrid[0:S, 0:S].astype(np.float32)
r = np.hypot(xx - S / 2, yy - S / 2) / (S / 2)
for k in range(F * F):
    t = k / (F * F - 1)
    n = fbm((S, S), 3, seed=11 + 0) * 0.6 + fbm((S, S), 6, seed=30 + k) * 0.4
    radius = 0.6 + 0.38 * t
    mask = np.clip(1 - r / (radius * (0.7 + 0.5 * n)), 0, 1) ** 1.5
    a = np.clip((n - 0.15 - 0.3 * t) * 3.0, 0, 1) * mask * (1 - t) ** 0.5
    shade = 0.75 + 0.25 * n
    row, col = divmod(k, F)
    cell = sheet[row * S:(row + 1) * S, col * S:(col + 1) * S]
    cell[..., 0] = shade; cell[..., 1] = shade * 0.97; cell[..., 2] = shade * 0.92; cell[..., 3] = a
save("DustPuff_4x4", sheet)

# Grit flakes: 2x2 atlas of jagged chips
G = 64
grit = np.zeros((2 * G, 2 * G, 4), np.float32)
yy, xx = np.mgrid[0:G, 0:G].astype(np.float32)
for k in range(4):
    ang = np.arctan2(yy - G / 2, xx - G / 2)
    rr = np.hypot(xx - G / 2, yy - G / 2) / (G / 2)
    m = random.randint(4, 6)
    th = np.sort(np.random.uniform(0, 2 * np.pi, m)); pr = np.random.uniform(.55, .95, m)
    px = pr * np.cos(th); py = pr * np.sin(th)
    u = (xx - G / 2) / (G / 2); w = (yy - G / 2) / (G / 2)
    a = np.ones_like(u)
    for j in range(m):  # inside test against each polygon edge
        x1, y1, x2, y2 = px[j], py[j], px[(j + 1) % m], py[(j + 1) % m]
        a *= ((x2 - x1) * (w - y1) - (y2 - y1) * (u - x1) >= 0)
    shade = 0.55 + 0.35 * fbm((G, G), 4, seed=50 + k)
    row, col = divmod(k, 2)
    c = grit[row * G:(row + 1) * G, col * G:(col + 1) * G]
    c[..., 0] = shade; c[..., 1] = shade * 0.96; c[..., 2] = shade * 0.9; c[..., 3] = a
save("Grit_2x2", grit)

# Impact decal: dark crater core + cracks radiating out
D = 256
yy, xx = np.mgrid[0:D, 0:D].astype(np.float32)
rr = np.hypot(xx - D / 2, yy - D / 2) / (D / 2)
ang = np.arctan2(yy - D / 2, xx - D / 2)
n = fbm((D, D), 8, seed=70)
core = np.clip(1 - rr / (0.28 + 0.08 * n), 0, 1) ** 0.6
ring = np.clip(1 - rr / 0.6, 0, 1) * 0.45 * n
cracks = np.zeros_like(rr)
for ci in range(7):  # jagged polyline cracks, thick at the core, thin at the tip
    a0 = ci / 7 * 2 * math.pi + random.uniform(-.3, .3); ln = random.uniform(.5, .9)
    x, y, steps = D / 2, D / 2, 6
    for st in range(steps):
        a0 += random.uniform(-.5, .5)
        nx = x + math.cos(a0) * ln * D / 2 / steps; ny = y + math.sin(a0) * ln * D / 2 / steps
        ex, ey = nx - x, ny - y; L2 = ex * ex + ey * ey
        tt = np.clip(((xx - x) * ex + (yy - y) * ey) / L2, 0, 1)
        dist = np.hypot(xx - (x + tt * ex), yy - (y + tt * ey))
        width = 3.0 * (1 - (st + tt) / steps) + 0.6
        cracks = np.maximum(cracks, np.clip(width - dist, 0, 1))
        if random.random() < .3:  # small side branch
            b = a0 + random.choice([-1, 1]) * random.uniform(.6, 1.1); bl = ln * D / 2 / steps * .8
            bx, by = math.cos(b) * bl, math.sin(b) * bl; B2 = bx * bx + by * by
            tb = np.clip(((xx - nx) * bx + (yy - ny) * by) / B2, 0, 1)
            db = np.hypot(xx - (nx + tb * bx), yy - (ny + tb * by))
            cracks = np.maximum(cracks, np.clip(1.2 * (1 - tb) + .4 - db, 0, 1))
        x, y = nx, ny
a = np.clip(np.maximum(core, np.maximum(ring, cracks * 0.9)), 0, 1)
a *= np.clip((1 - rr) * 4, 0, 1)
v = 0.08 + 0.1 * n
decal = np.stack([v, v * 0.95, v * 0.9, a], -1)
save("ImpactDecal", decal)
print("done ->", OUT)
