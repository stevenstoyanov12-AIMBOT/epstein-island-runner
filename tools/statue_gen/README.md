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
