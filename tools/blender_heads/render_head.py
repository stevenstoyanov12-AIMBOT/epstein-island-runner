"""Render a head model (grey or vertex-coloured) from a few angles, Ghibli-ish soft lighting.
usage: python3 render_head.py MODEL.(glb|ply|obj) OUT_PREFIX [--colored]"""
import math, sys, bpy
from mathutils import Vector
src, out = sys.argv[1], sys.argv[2]
colored = "--colored" in sys.argv
bpy.ops.wm.read_factory_settings(use_empty=True)
if src.endswith(".glb"): bpy.ops.import_scene.gltf(filepath=src)
elif src.endswith(".ply"): bpy.ops.wm.ply_import(filepath=src)
else: bpy.ops.wm.obj_import(filepath=src)
objs=[o for o in bpy.data.objects if o.type=="MESH"]
m=bpy.data.materials.new("M"); m.use_nodes=True; nt=m.node_tree; b=nt.nodes["Principled BSDF"]
b.inputs["Roughness"].default_value=0.8
if colored:
    a=nt.nodes.new("ShaderNodeVertexColor"); nt.links.new(a.outputs["Color"], b.inputs["Base Color"])
else:
    b.inputs["Base Color"].default_value=(0.7,0.7,0.7,1)
for o in objs:
    o.data.materials.clear(); o.data.materials.append(m)
    for p in o.data.polygons: p.use_smooth=True
sc=bpy.context.scene
w=bpy.data.worlds.new("W"); sc.world=w; w.use_nodes=True; w.node_tree.nodes["Background"].inputs["Strength"].default_value=1.0
sun=bpy.data.objects.new("S",bpy.data.lights.new("S","SUN")); sun.data.energy=2.5; sun.rotation_euler=(math.radians(40),0,math.radians(25)); sc.collection.objects.link(sun)
cam=bpy.data.objects.new("C",bpy.data.cameras.new("C")); sc.collection.objects.link(cam); sc.camera=cam; cam.data.lens=60
sc.render.engine="CYCLES"; sc.cycles.samples=16; sc.cycles.use_denoising=True
sc.render.resolution_x=sc.render.resolution_y=500; sc.view_settings.view_transform="Standard"
for name,ang in (("front",0),("three",35),("side",90)):
    a=math.radians(ang); cam.location=(5*math.sin(a),-5*math.cos(a),0.3)
    cam.rotation_euler=(Vector((0,0,0.1))-cam.location).to_track_quat("-Z","Y").to_euler()
    sc.render.filepath=f"{out}_{name}.png"; bpy.ops.render.render(write_still=True)
