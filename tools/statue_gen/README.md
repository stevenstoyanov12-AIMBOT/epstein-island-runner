# Statue generator

Generates the life-size veiled marble statue used in `UnityGame` (with bullet damage) as
OBJ meshes plus preview renders. The statue is modelled as a signed distance field
(`veiled_statue.py`), meshed with marching cubes, smoothed and decimated into three LODs.

```
pip install numpy scipy scikit-image trimesh fast-simplification pillow
python3 build.py --out ../../UnityGame/Assets/Models/Statues --preview previews
```

Options: `--seed` (different damage pattern), `--yaw/--pitch/--roll` (head pose),
`--voxel` (detail; smaller is finer and slower), `--lods` (triangle counts per LOD),
`--name` (output file prefix).

## Destructible Venus (Blender)

`venus_statue.py` models a Milo-style Venus on a block plinth (bare torso in contrapposto, broken arms,
heavy drapery, rock base). `build_venus.py` samples it, cuts it into Voronoi pieces with rough breaks
(`Fig_##` figure, `Base_##` plinth) plus a seamless `Intact` shell, then finishes it in Blender via the `bpy`
module: decimation, smoothing, UVs, `Marble` / `MarbleBroken` materials, FBX export and Cycles previews.

```
pip install bpy numpy scipy scikit-image trimesh fast-simplification pillow   # bpy needs Python 3.11
python3 build_venus.py                       # ~30 min on 4 cores
python3 build_venus.py --voxel 0.004 --no-render   # quick draft
```

Writes `UnityGame/Assets/Models/Statues/VenusStatue.fbx`, `blend/VenusStatue.blend` and `previews/Venus_*.png`.
In Unity: Tools > Build Venus Statue Scene (script `DestructibleStatue.cs`, test gun `StatueShooter.cs`).
Options: `--fig` / `--base` piece counts, `--tris` triangle budget, `--seed` for a different break pattern.
