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
from sdf import fbm, smax, smin

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
    cloth = d - 0.010 - 0.018 * np.clip(s / 0.35, 0, 1)
    warp = 1.8 * fbm(p * np.array([5.0, 2.0, 5.0]), 3, 4) + 4.0 * s
    folds = 0.7 * np.abs(np.sin(theta * 7 + warp)) + 0.3 * np.abs(np.sin(theta * 15 + 1.1 + 1.5 * warp))
    cloth = cloth - (0.002 + 0.02 * np.clip(s / 0.35, 0, 1)) * folds

    # sash: thicker diagonal band from the left shoulder down across the chest to the right hip
    u = (x / width) * 0.55 + (y - neck) * 1.6          # distance along the diagonal band
    band = np.abs(u + 0.12) - 0.16
    sash_folds = np.abs(np.sin((y * 1.0 - x * 0.6) * 45 + 3.0 * fbm(p * 6, 2, 9)))
    sash = smax(d - 0.022 - 0.006 * sash_folds, band, 0.02)
    cloth = smin(cloth, sash, 0.012)

    # scooped neckline that leaves the neck and collarbones bare
    r = np.sqrt(x ** 2 + (z - np.median(z)) ** 2 * 0.6)
    line = neck - 0.05 * np.exp(-(x / (0.25 * width)) ** 2) * (z < 0) + 0.02 * np.clip(r / width, 0, 1)
    cloth = smax(cloth, y - line, 0.006)
    return cloth


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("name")
    ap.add_argument("--neck", type=float, default=0.46, help="height where the cloth starts")
    ap.add_argument("--voxel", type=float, default=0.003)
    ap.add_argument("--tris", type=int, default=60000)
    ap.add_argument("--preview", default="previews")
    args = ap.parse_args()

    bust = trimesh.load(os.path.join(STATUES, f"{args.name}.obj"), force="mesh")
    d, lo = body_sdf(bust, args.voxel)
    g = np.stack(np.meshgrid(*[lo[i] + np.arange(d.shape[i]) * args.voxel for i in range(3)],
                             indexing="ij"), -1).reshape(-1, 3)
    width = np.ptp(bust.vertices[:, 0])
    f = drape_field(g, d.reshape(-1), args.neck, width).reshape(d.shape)
    f[:, 0, :] = np.maximum(f[:, 0, :], 0.001)  # close the bottom
    v, faces, _, _ = marching_cubes(f.astype(np.float32), 0.0, spacing=(args.voxel,) * 3)
    drape = trimesh.Trimesh(v + lo, faces[:, ::-1], process=True)
    if drape.volume < 0:
        drape.invert()
    trimesh.smoothing.filter_taubin(drape, iterations=8)
    drape = decimate(drape, args.tris)

    write_obj(drape, os.path.join(STATUES, f"{args.name}_Drape.obj"), f"{args.name}_Drape")
    both = trimesh.util.concatenate([bust, drape])
    os.makedirs(args.preview, exist_ok=True)
    render(both, os.path.join(args.preview, f"{args.name}_draped_front.png"), yaw=0)
    render(both, os.path.join(args.preview, f"{args.name}_draped_three_quarter.png"), yaw=-35)
    print(f"{args.name}_Drape: {len(drape.faces)} tris")


if __name__ == "__main__":
    main()
