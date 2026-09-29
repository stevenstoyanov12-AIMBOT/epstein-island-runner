import math, sys, bpy, bmesh
from mathutils import Vector
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=sys.argv[1])
o=next(x for x in bpy.data.objects if x.type=="MESH"); bpy.context.view_layer.objects.active=o
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
bm=bmesh.new(); bm.from_mesh(o.data); bmesh.ops.delete(bm, geom=[v for v in bm.verts if v.co.z<0.25], context="VERTS"); bm.to_mesh(o.data); bm.free()
m=bpy.data.materials.new("G"); m.use_nodes=True; m.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value=(0.6,0.6,0.6,1)
o.data.materials.clear(); o.data.materials.append(m)
for p in o.data.polygons: p.use_smooth=True
sc=bpy.context.scene
w=bpy.data.worlds.new("W"); sc.world=w; w.use_nodes=True
w.node_tree.nodes["Background"].inputs["Color"].default_value=(0.55,0.7,0.85,1); w.node_tree.nodes["Background"].inputs["Strength"].default_value=0.9
sun=bpy.data.objects.new("S",bpy.data.lights.new("S","SUN")); sun.data.energy=2.2; sun.rotation_euler=(math.radians(40),0,math.radians(25)); sc.collection.objects.link(sun)
cam=bpy.data.objects.new("C",bpy.data.cameras.new("C")); sc.collection.objects.link(cam); sc.camera=cam
sc.render.engine="CYCLES"; sc.cycles.samples=24; sc.cycles.use_denoising=True
sc.render.resolution_x=sc.render.resolution_y=500; sc.view_settings.view_transform="Standard"
for name,ang in (("front",0),("three",35),("close",0)):
    a=math.radians(ang); cam.location=(3.2*math.sin(a),-3.2*math.cos(a),0.58); cam.data.lens=260 if name=="close" else 85
    cam.rotation_euler=(Vector((0,0,0.56))-cam.location).to_track_quat("-Z","Y").to_euler()
    sc.render.filepath=f"{sys.argv[2]}_{name}.png"; bpy.ops.render.render(write_still=True)
if len(sys.argv) > 3:
    bpy.ops.object.select_all(action="DESELECT"); o.select_set(True); o.name = "SoldierHeadGrey"
    bpy.ops.export_scene.fbx(filepath=sys.argv[3], use_selection=True, axis_forward="-Z", axis_up="Y", apply_scale_options="FBX_SCALE_UNITS")
