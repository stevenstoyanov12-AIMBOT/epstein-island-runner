# Autumn tree (Blender)

Builds the autumn tree with Blender running as a Python module (`pip install bpy`, done
automatically in cloud sessions by `.claude/hooks/session-start.sh`).

```
python3 make_tree.py --preview previews
```

Writes `Tree_Bark.fbx`, `Tree_Leaves.fbx`, `Tree_FallenLeaves.fbx` and their textures to
`UnityGame/Assets/Models/Trees`, and Cycles preview renders to `--preview`.
Options: `--seed` (tree shape), `--leaves`, `--fallen`, `--samples` (preview render quality).
