"""Carve a marble drape (a himation-style cloak) over the body of a bust mesh.

The head and neck stay untouched; below the neckline the clothing is replaced by a marble
cloth shell that follows the body with hanging folds and a diagonal sash over one shoulder.

usage: python3 drape_bust.py Bust_Portrait [--neck 0.46] [--preview DIR]
"""
import argparse
import os

import numpy as np
import trimesh
from scipy import ndimage
from skimage.measure import marching_cubes

from build import decimate, render, write_obj
from sdf import capsule, fbm, length, smax, smin, sphere

STATUES = "../../UnityGame/Assets/Models/Statues"


def body_sdf(mesh, voxel):
    """Approximate signed distance to the mesh on a grid (negative inside)."""
    pad = 0.06
    lo = mesh.bounds[0] - pad
    lo[1] = mesh.bounds[0][1]
    hi = mesh.bounds[1] + pad
    vox = mesh.voxelized(voxel).fill()
    occ = np.zeros(np.ceil((hi - lo) / voxel).astype(int) + 1, bool)
    idx = np.round((vox.points - lo) / voxel).astype(int)
    idx = idx[(idx >= 0).all(1) & (idx < occ.shape).all(1)]
    occ[tuple(idx.T)] = True
    occ = ndimage.binary_closing(occ, iterations=2)
    d = (ndimage.distance_transform_edt(~occ) - ndimage.distance_transform_edt(occ)) * voxel
    d = ndimage.gaussian_filter(d, 1.2)
    return d, lo


def drape_field(p, d, neck, width):
    x, y, z = p[:, 0], p[:, 1], p[:, 2]
    theta = np.arctan2(x, -z)
    s = np.clip(neck - y, 0, None)

    # base cloth: follows the body, loosening further down
    cloth = d - 0.007 - 0.006 * np.clip(s / 0.35, 0, 1)   # snug over the body
    warp = 1.8 * fbm(p * np.array([5.0, 2.0, 5.0]), 3, 4) + 4.0 * s
    folds = 0.7 * np.abs(np.sin(theta * 7 + warp)) + 0.3 * np.abs(np.sin(theta * 15 + 1.1 + 1.5 * warp))
    cloth = cloth - (0.002 + 0.011 * np.clip(s / 0.35, 0, 1)) * folds

    # sash: thicker diagonal band from the left shoulder down across the chest to the right hip
    u = (x / width) * 0.55 + (y - neck) * 1.6          # distance along the diagonal band
    band = np.abs(u + 0.12) - 0.16
    sash_folds = np.abs(np.sin((y * 1.0 - x * 0.6) * 45 + 3.0 * fbm(p * 6, 2, 9)))
    sash = smax(d - 0.016 - 0.005 * sash_folds, band, 0.02)
    cloth = smin(cloth, sash, 0.012)

    # scooped neckline that leaves the neck and collarbones bare
    r = np.sqrt(x ** 2 + (z - np.median(z)) ** 2 * 0.6)
    line = neck - 0.05 * np.exp(-(x / (0.25 * width)) ** 2) * (z < 0) + 0.02 * np.clip(r / width, 0, 1)
    cloth = smax(cloth, y - line, 0.006)
    return cloth


def bullet_damage(p, f, neck, rng, count):
    """Chip bullet craters with hairline cracks into the front and back of the cloth (never above the neckline)."""
    mid = np.median(p[:, 2])
    surf = np.flatnonzero((np.abs(f) < 0.002) & (p[:, 1] < neck - 0.06))
    rng.shuffle(surf)
    hits = []
    for i in surf:
        if len(hits) == count:
            break
        if all(np.linalg.norm(p[i] - h) > 0.07 for h in hits):
            hits.append(p[i])
    for c in hits:
        near = np.flatnonzero(length(p - c) < 0.08)
        pp, dd = p[near], f[near]
        n = np.array([0.0, 0.0, -1.0 if c[2] < mid else 1.0])  # front faces -Z, back +Z
        r = rng.uniform(0.008, 0.014)
        jag = 0.25 * r * fbm(pp * 350.0, 2, int(rng.integers(1000)))
        core = sphere(pp, c + n * 0.2 * r, 0.75 * r) + jag
        spall = sphere(pp, c + n * (2.4 * r - 0.003), 2.4 * r) + jag
        dd = smax(dd, -np.minimum(core, spall), 0.0015)
        for _ in range(rng.integers(2, 5)):
            a = rng.uniform(0, 2 * np.pi)
            pts = [c + np.array([np.cos(a), np.sin(a), 0.0]) * r * 0.8]
            for _ in range(3):
                a += rng.normal(0, 0.45)
                pts.append(pts[-1] + np.array([np.cos(a), np.sin(a), 0.0]) * rng.uniform(0.01, 0.022))
            for a0, b0 in zip(pts[:-1], pts[1:]):
                dd = np.maximum(dd, -capsule(pp, a0, b0, 0.0022))
        f[near] = dd
    return f


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("name")
    ap.add_argument("--neck", type=float, default=0.46, help="height where the cloth starts")
    ap.add_argument("--voxel", type=float, default=0.003)
    ap.add_argument("--tris", type=int, default=60000)
    ap.add_argument("--preview", default="previews")
    ap.add_argument("--seed", type=int, default=3)
    ap.add_argument("--hits", type=int, default=16, help="bullet impacts on the cloth")
    args = ap.parse_args()

    bust = trimesh.load(os.path.join(STATUES, f"{args.name}.obj"), force="mesh")
    d, lo = body_sdf(bust, args.voxel)
    g = np.stack(np.meshgrid(*[lo[i] + np.arange(d.shape[i]) * args.voxel for i in range(3)],
                             indexing="ij"), -1).reshape(-1, 3)
    width = np.ptp(bust.vertices[:, 0])
    f = drape_field(g, d.reshape(-1), args.neck, width).reshape(d.shape)
    f = bullet_damage(g, f.reshape(-1), args.neck, np.random.default_rng(args.seed), args.hits).reshape(d.shape)
    f[:, 0, :] = np.maximum(f[:, 0, :], 0.001)  # close the bottom
    v, faces, _, _ = marching_cubes(f.astype(np.float32), 0.0, spacing=(args.voxel,) * 3)
    drape = trimesh.Trimesh(v + lo, faces[:, ::-1], process=True)
    if drape.volume < 0:
        drape.invert()
    trimesh.smoothing.filter_taubin(drape, iterations=4)
    drape = decimate(drape, args.tris)

    write_obj(drape, os.path.join(STATUES, f"{args.name}_Drape.obj"), f"{args.name}_Drape")
    both = trimesh.util.concatenate([bust, drape])
    os.makedirs(args.preview, exist_ok=True)
    render(both, os.path.join(args.preview, f"{args.name}_draped_front.png"), yaw=0)
    render(both, os.path.join(args.preview, f"{args.name}_draped_three_quarter.png"), yaw=-35)
    render(both, os.path.join(args.preview, f"{args.name}_draped_back.png"), yaw=180)
    print(f"{args.name}_Drape: {len(drape.faces)} tris")


if __name__ == "__main__":
    main()
