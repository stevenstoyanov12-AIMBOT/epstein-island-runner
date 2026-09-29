"""Generate an autumn tree: bark mesh plus leaf meshes split by colour, and fallen leaves.

Outputs OBJ files (Tree_Bark, Tree_Leaves_<Colour>) in metres, +Y up, trunk base at the origin.

usage: python3 autumn_tree.py [--seed 7] [--leaves 5200] [--out DIR] [--preview DIR]
"""
import argparse
import os

import numpy as np
import trimesh

from build import render, write_obj
from sdf import fbm

LEAF_COLOURS = {
    "Red": (0.72, 0.16, 0.08),
    "Orange": (0.90, 0.40, 0.08),
    "Amber": (0.93, 0.60, 0.12),
    "Yellow": (0.95, 0.78, 0.22),
}


# --- branches -------------------------------------------------------------------

def grow(rng, start, direction, length, radius, depth, out):
    """Recursively grow a branch as a bent polyline; children split off along it."""
    pts, radii = [start], [radius]
    d = direction / np.linalg.norm(direction)
    steps = 6
    for i in range(steps):
        d = d + rng.normal(0, 0.12, 3) + np.array([0, 0.06 if depth < 3 else -0.03, 0])
        d /= np.linalg.norm(d)
        pts.append(pts[-1] + d * length / steps)
        radii.append(radius * (1 - (0.3 if depth == 0 else 0.55) * (i + 1) / steps))
    out.append((np.array(pts), np.array(radii), depth))
    if depth >= 5:
        return
    n_children = rng.integers(2, 4) if depth > 0 else 4
    for k in range(n_children):
        t = rng.uniform(0.55, 1.0) if depth > 0 else 1.0
        idx = min(int(t * steps), steps)
        base_dir = d if depth > 0 else np.array([0, 1.0, 0])
        # rotate away from the parent by 25-50 degrees around a random axis
        ang = np.radians(rng.uniform(25, 50) if depth > 0 else 38)
        spin = (2 * np.pi * k / n_children + rng.uniform(-0.4, 0.4)) if depth == 0 else rng.uniform(0, 2 * np.pi)
        perp = np.cross(base_dir, [0.3, 0.1, 0.9])
        perp /= np.linalg.norm(perp)
        perp = trimesh.transformations.rotation_matrix(spin, base_dir)[:3, :3] @ perp
        child = np.cos(ang) * base_dir + np.sin(ang) * perp
        grow(rng, pts[idx], child, length * rng.uniform(0.62, 0.78), radii[idx] * (0.8 if depth == 0 else 0.72), depth + 1, out)


def tube(pts, radii, sides, rng):
    """Tube along a polyline with bark ridges; returns vertices and faces."""
    tangents = np.gradient(pts, axis=0)
    tangents /= np.linalg.norm(tangents, axis=1, keepdims=True)
    n = np.cross(tangents[0], [1.0, 0, 0])
    if np.linalg.norm(n) < 1e-3:
        n = np.cross(tangents[0], [0, 0, 1.0])
    n /= np.linalg.norm(n)
    verts = []
    ang = np.linspace(0, 2 * np.pi, sides, endpoint=False)
    for p, r, t in zip(pts, radii, tangents):
        n = n - t * (n @ t)  # parallel transport keeps the rings from twisting
        n /= np.linalg.norm(n)
        b = np.cross(t, n)
        ring = p + r * (np.cos(ang)[:, None] * n + np.sin(ang)[:, None] * b)
        ridge = 1 + 0.08 * np.abs(np.sin(ang * 5 + p[1] * 3))  # vertical bark ridges
        verts.append(p + (ring - p) * ridge[:, None])
    verts = np.concatenate(verts)
    faces = []
    for i in range(len(pts) - 1):
        for j in range(sides):
            a, b = i * sides + j, i * sides + (j + 1) % sides
            c, d = a + sides, b + sides
            faces += [(a, c, b), (b, c, d)]
    tip = len(verts)
    verts = np.vstack([verts, pts[-1]])
    last = (len(pts) - 1) * sides
    faces += [(last + j, tip, last + (j + 1) % sides) for j in range(sides)]
    return verts, np.array(faces)


def trunk_flare(rng):
    """Short, wide root flare at the base of the trunk."""
    pts = np.array([[0, -0.03, 0], [0, 0.08, 0], [0, 0.22, 0], [0, 0.45, 0]])
    return pts, np.array([0.40, 0.33, 0.29, 0.27])


# --- leaves ---------------------------------------------------------------------

def leaf_template():
    """A pointed leaf, slightly cupped along its midrib: 7 vertices, 6 triangles, ~9 cm long."""
    v = np.array([[0, 0, 0], [0.018, 0.01, 0.025], [0.028, 0.004, 0.05], [0, 0.006, 0.09],
                  [-0.028, 0.004, 0.05], [-0.018, 0.01, 0.025], [0, 0.0, 0.05]])
    f = np.array([[0, 6, 1], [1, 6, 2], [2, 6, 3], [3, 6, 4], [4, 6, 5], [5, 6, 0]])
    return v, f


def place_leaves(rng, anchors, count, spread, scale_range):
    """Scatter leaves around anchor points with random orientation; returns list of (verts, faces)."""
    tv, tf = leaf_template()
    out = []
    for _ in range(count):
        a = anchors[rng.integers(len(anchors))]
        pos = a + rng.normal(0, spread, 3)
        R = trimesh.transformations.random_rotation_matrix(rng.random(3))[:3, :3]
        out.append((pos + (tv * rng.uniform(*scale_range)) @ R.T, tf))
    return out


def merge(parts):
    verts, faces, off = [], [], 0
    for v, f in parts:
        verts.append(v)
        faces.append(f + off)
        off += len(v)
    return trimesh.Trimesh(np.concatenate(verts), np.concatenate(faces), process=True)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--seed", type=int, default=7)
    ap.add_argument("--leaves", type=int, default=9000)
    ap.add_argument("--fallen", type=int, default=700)
    ap.add_argument("--out", default="../../UnityGame/Assets/Models/Trees")
    ap.add_argument("--preview", default="previews")
    args = ap.parse_args()
    rng = np.random.default_rng(args.seed)

    branches = []
    grow(rng, np.array([0, 0, 0.0]), np.array([0.05, 1, 0.02]), 1.7, 0.27, 0, branches)
    parts = [tube(*trunk_flare(rng), 16, rng)]
    for pts, radii, depth in branches:
        parts.append(tube(pts, np.maximum(radii, 0.008), max(5, 14 - 2 * depth), rng))
    bark = merge(parts)
    # knotty surface: push vertices in and out a little along their normals
    bark.vertices += bark.vertex_normals * (0.012 * fbm(bark.vertices * 6.0, 3, 1))[:, None]

    # leaf anchors along the thinnest branches, where foliage grows
    twigs = np.concatenate([pts[2:] for pts, _, depth in branches if depth >= 4])
    leaves = place_leaves(rng, twigs, args.leaves, 0.16, (1.2, 1.9))
    # fallen leaves lying on the ground around the trunk
    fallen = []
    tv, tf = leaf_template()
    for _ in range(args.fallen):
        r = 2.6 * np.sqrt(rng.random())
        a = rng.uniform(0, 2 * np.pi)
        R = trimesh.transformations.rotation_matrix(rng.uniform(0, 2 * np.pi), [0, 1, 0])[:3, :3]
        tilt = trimesh.transformations.rotation_matrix(rng.uniform(-0.25, 0.25), [1, 0, 0])[:3, :3]
        pos = np.array([r * np.cos(a), 0.012 + rng.uniform(0, 0.01), r * np.sin(a)])
        fallen.append((pos + (tv * rng.uniform(1.0, 1.4)) @ (R @ tilt).T, tf))

    os.makedirs(args.out, exist_ok=True)
    write_obj(bark, os.path.join(args.out, "Tree_Bark.obj"), "Tree_Bark")
    names = list(LEAF_COLOURS)
    # warmer colours lower and outside, yellows higher in the crown
    groups = {n: [] for n in names}
    for v, f in leaves + fallen:
        h = np.clip((v[:, 1].mean() - 2.0) / 3.0, 0, 1)
        idx = int(np.clip(rng.normal(h * 3.2, 0.9), 0, 3.999))
        groups[names[idx]].append((v, f))
    meshes = []
    for n in names:
        m = merge(groups[n])
        write_obj(m, os.path.join(args.out, f"Tree_Leaves_{n}.obj"), f"Tree_Leaves_{n}")
        meshes.append((m, LEAF_COLOURS[n]))
        print(f"Tree_Leaves_{n}: {len(m.faces)} tris")
    print(f"Tree_Bark: {len(bark.faces)} tris, height {bark.bounds[1][1]:.2f} m")

    os.makedirs(args.preview, exist_ok=True)
    scene = [(bark, (0.42, 0.30, 0.22))] + meshes
    render_coloured(scene, os.path.join(args.preview, "tree_front.png"), 0)
    render_coloured(scene, os.path.join(args.preview, "tree_side.png"), -60)


def render_coloured(parts, path, yaw):
    """Render several meshes, each with its own flat colour, via build.render's vertex colours."""
    all_m = trimesh.util.concatenate([m for m, _ in parts])
    cols = np.concatenate([np.tile(c, (len(m.vertices), 1)) for m, c in parts])
    render(all_m, path, yaw=yaw, pitch=4, colors=cols)


if __name__ == "__main__":
    main()
