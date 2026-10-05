# Crate and van (Blender)

Builds the BATON CORPORATION crate (with stencil decal) and the van cargo area with hinged rear
doors, for the crate cutscene. Needs Blender as a Python module (`pip install bpy`).

```
python3 make_crate_van.py --preview previews
```

Writes FBX files and textures to `UnityGame/Assets/Models/Cutscene` and Cycles previews to `--preview`.

## Night sky

`python3 night_sky.py ../../UnityGame/Assets/Models/Cutscene/Textures/night_sky.png` renders the
star-filled 360-degree skybox: stars with real colours and a power-law brightness spread, the Milky Way
with dust lanes, a warm city glow at the horizon, a moon, and a Half-Life style nebula.
