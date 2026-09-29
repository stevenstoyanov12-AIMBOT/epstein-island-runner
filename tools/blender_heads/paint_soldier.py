"""Paint the Meshy 'tiny soldier' head in the colours of the reference drawing (vertex colours by region),
crop it to the head, render previews and export a GLB/FBX for Unity.
Blender coords after glTF import: X right, Z up, face looks toward -Y (d = -Y is 'frontness').
usage: python3 paint_soldier.py MODEL.glb OUT_DIR PREVIEW_PREFIX"""
import math, sys, bpy, bmesh
import numpy as np
from mathutils import Vector
src, out_dir, prev = sys.argv[1], sys.argv[2], sys.argv[3]

def lin(c):  # sRGB 0-255 -> linear
    c = np.array(c) / 255.0
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)

HELMET, HELMET_DK = lin((122, 116, 62)), lin((88, 84, 44))
STAR = lin((70, 72, 40))
SKIN, CHEEK = lin((255, 222, 196)), lin((246, 168, 150))
HAIR, HAIR_DK = lin((236, 196, 106)), lin((204, 156, 70))
FRAME, LENS, LENS_HI = lin((28, 30, 38)), lin((52, 66, 84)), lin((96, 120, 142))
STRAP = lin((96, 104, 96))
LEAF, STEM = lin((110, 150, 90)), lin((150, 170, 110))
MOUTH, NOSE = lin((150, 90, 80)), lin((248, 206, 182))

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
obj = next(o for o in bpy.data.objects if o.type == "MESH")
bpy.context.view_layer.objects.active = obj
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
me = obj.data

# crop to the head: drop the shirt below the chin (measured on grid renders: chin at z 0.26)
bm = bmesh.new(); bm.from_mesh(me)
bmesh.ops.delete(bm, geom=[v for v in bm.verts if v.co.z < 0.25], context="VERTS")
bm.to_mesh(me); bm.free()

co = np.array([v.co[:] for v in me.vertices]); nrm = np.array([v.normal[:] for v in me.vertices])
x, y, z = co[:, 0], co[:, 1], co[:, 2]; d = -y; ax = np.abs(x)
col = np.tile(HAIR, (len(co), 1))                                        # default: hair (sides/back)

# helmet: above the brim line, measured from the side view (front tip high, sloping down to the back)
brim = np.interp(d, [-0.6, -0.35, -0.1, 0.05, 0.14], [0.55, 0.56, 0.59, 0.645, 0.72])
brim = brim - 0.05 * np.clip((ax - 0.2) / 0.1, 0, 1)                     # a little lower at the sides
helmet = z > brim
col[helmet] = HELMET
col[helmet & (nrm[:, 2] < -0.3)] = HELMET_DK                             # underside of the brim
ang = np.arctan2(z - 0.86, x); r = np.hypot(x, z - 0.86)
star = helmet & (d > 0.0) & (r < 0.075 * (0.55 + 0.45 * np.cos(5 * (ang - math.pi / 2))))
col[star] = STAR

# blond fringe under the brim: forward-facing forehead surface (the brim underside faces down instead)
fringe = helmet & (ax < 0.25) & (z < 0.71) & (d < 0.075) & (nrm[:, 1] < -0.35) & (nrm[:, 2] > -0.3)
col[fringe] = HAIR
helmet &= ~fringe
fringe |= ~helmet & (ax < 0.25) & (z > 0.645) & (d > -0.06)
col[fringe] = HAIR
# face: the front of the head under the brim
face = ~helmet & ~fringe & (d > -0.06) & (ax < 0.25)
col[face] = SKIN
col[face & (np.hypot(ax - 0.165, (z - 0.43) * 1.3) < 0.045)] = CHEEK     # rosy cheeks under the glasses
col[face & (ax < 0.05) & (np.abs(z - 0.412) < 0.005) & (d > 0.02)] = MOUTH
# sunglasses: dark frame, slate lenses with a lighter reflection band
glass = ~helmet & (ax < 0.245) & (z > 0.468) & (z < 0.648) & (d > 0.05)
col[glass] = FRAME
lens = glass & (ax > 0.036) & (ax < 0.228) & (z > 0.482) & (z < 0.634)
col[lens] = LENS
col[lens & (np.abs((z - 0.56) - 0.5 * (ax - 0.13)) < 0.01)] = LENS_HI
# ear-side chin strap
col[~helmet & (x > 0.22) & (z > 0.3) & (z < 0.49) & (d > -0.06)] = STRAP
# the leaf in the mouth
col[(d > 0.1) & (z > 0.34) & (z < 0.44) & ~glass] = LEAF
# a few darker strands in the hair
hair_mask = np.all(col == HAIR, axis=1)
col[hair_mask & (np.sin(z * 90 + x * 30) > 0.85)] = HAIR_DK

attr = me.color_attributes.new("Col", "FLOAT_COLOR", "POINT")
for i, c in enumerate(col):
    attr.data[i].color = (*c, 1.0)
me.color_attributes.active_color = attr

m = bpy.data.materials.new("Soldier"); m.use_nodes = True; nt = m.node_tree; b = nt.nodes["Principled BSDF"]
vc = nt.nodes.new("ShaderNodeVertexColor"); vc.layer_name = "Col"
nt.links.new(vc.outputs["Color"], b.inputs["Base Color"]); b.inputs["Roughness"].default_value = 0.75
me.materials.clear(); me.materials.append(m)
for p in me.polygons: p.use_smooth = True

sc = bpy.context.scene
w = bpy.data.worlds.new("W"); sc.world = w; w.use_nodes = True
w.node_tree.nodes["Background"].inputs["Color"].default_value = (0.55, 0.7, 0.85, 1)
w.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.9
sun = bpy.data.objects.new("S", bpy.data.lights.new("S", "SUN")); sun.data.energy = 2.2
sun.rotation_euler = (math.radians(40), 0, math.radians(25)); sc.collection.objects.link(sun)
cam = bpy.data.objects.new("C", bpy.data.cameras.new("C")); sc.collection.objects.link(cam); sc.camera = cam; cam.data.lens = 85
sc.render.engine = "CYCLES"; sc.cycles.samples = 24; sc.cycles.use_denoising = True
sc.render.resolution_x = sc.render.resolution_y = 500; sc.view_settings.view_transform = "Standard"
for name, ang in (("front", 0), ("three", 35), ("side", 80)):
    a = math.radians(ang); cam.location = (3.2 * math.sin(a), -3.2 * math.cos(a), 0.58)
    cam.rotation_euler = (Vector((0, 0, 0.56)) - cam.location).to_track_quat("-Z", "Y").to_euler()
    sc.render.filepath = f"{prev}_{name}.png"; bpy.ops.render.render(write_still=True)

import os
os.makedirs(out_dir, exist_ok=True)
bpy.ops.object.select_all(action="DESELECT"); obj.select_set(True)
bpy.ops.export_scene.gltf(filepath=os.path.join(out_dir, "SoldierHead.glb"), use_selection=True, export_vertex_color="ACTIVE")
bpy.ops.export_scene.fbx(filepath=os.path.join(out_dir, "SoldierHead.fbx"), use_selection=True, colors_type="LINEAR",
                         axis_forward="-Z", axis_up="Y", apply_scale_options="FBX_SCALE_UNITS")
