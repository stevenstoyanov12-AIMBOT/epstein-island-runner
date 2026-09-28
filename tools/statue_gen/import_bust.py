"""Convert a GLB bust (e.g. from Meshy) into a life-size, -Z-facing OBJ for the statue scene.

usage: python3 import_bust.py input.glb OutputName [--height 0.75] [--out DIR] [--preview DIR]
"""
import argparse
import os

import numpy as np
import trimesh

from build import decimate, render, write_obj


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("input")
    ap.add_argument("name")
    ap.add_argument("--height", type=float, default=0.75, help="bust height in metres")
    ap.add_argument("--tris", type=int, default=120000)
    ap.add_argument("--out", default="../../UnityGame/Assets/Models/Statues")
    ap.add_argument("--preview", default="previews")
    args = ap.parse_args()

    m = trimesh.load(args.input, force="mesh", process=True)
    m.merge_vertices()
    # glTF faces +Z; the statue scene expects models facing -Z
    m.apply_transform(trimesh.transformations.rotation_matrix(np.pi, [0, 1, 0]))
    m.apply_scale(args.height / m.extents[1])
    lo, hi = m.bounds
    m.apply_translation([-(lo[0] + hi[0]) / 2, -lo[1], -(lo[2] + hi[2]) / 2])  # base centred at origin
    m = decimate(m, args.tris)
    m.visual = trimesh.visual.ColorVisuals(m)

    write_obj(m, os.path.join(args.out, f"{args.name}.obj"), args.name)
    os.makedirs(args.preview, exist_ok=True)
    render(m, os.path.join(args.preview, f"{args.name}_front.png"), yaw=0)
    render(m, os.path.join(args.preview, f"{args.name}_three_quarter.png"), yaw=-35)
    print(f"{args.name}: {len(m.faces)} tris, extents {np.round(m.extents, 3)}")


if __name__ == "__main__":
    main()
