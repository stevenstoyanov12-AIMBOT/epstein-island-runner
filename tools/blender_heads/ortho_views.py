"""Orthographic front/side renders of the head region with a coordinate grid, to locate features.
usage: python3 ortho_views.py MODEL.glb OUT_PREFIX  (model coords: x right, y up, front = -z)"""
import math, sys, bpy
from PIL import Image, ImageDraw
src, out = sys.argv[1], sys.argv[2]
Y0, Y1 = 0.2, 1.0            # head region shown (model y)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
m=bpy.data.materials.new("M"); m.use_nodes=True; m.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value=(0.75,0.75,0.75,1)
for o in bpy.data.objects:
    if o.type=="MESH":
        o.data.materials.clear(); o.data.materials.append(m)
        for p in o.data.polygons: p.use_smooth=True
sc=bpy.context.scene
w=bpy.data.worlds.new("W"); sc.world=w; w.use_nodes=True; w.node_tree.nodes["Background"].inputs["Strength"].default_value=1.2
sun=bpy.data.objects.new("S",bpy.data.lights.new("S","SUN")); sun.data.energy=2; sun.rotation_euler=(math.radians(35),0,math.radians(20)); sc.collection.objects.link(sun)
cam=bpy.data.objects.new("C",bpy.data.cameras.new("C")); sc.collection.objects.link(cam); sc.camera=cam
cam.data.type="ORTHO"; cam.data.ortho_scale=Y1-Y0
sc.render.engine="CYCLES"; sc.cycles.samples=12; sc.cycles.use_denoising=True
R=800; sc.render.resolution_x=sc.render.resolution_y=R; sc.view_settings.view_transform="Standard"
cy=(Y0+Y1)/2
# blender coords after glTF import: X=x, Y=z(model)... glTF +Y up -> Blender +Z up, glTF +Z -> Blender -Y
views={"front":((0,-5,cy),(90,0,0),"x"), "side":((-5,0,cy),(90,0,-90),"z")}
for name,(loc,rot,hax) in views.items():
    cam.location=loc; cam.rotation_euler=tuple(math.radians(a) for a in rot)
    p=f"{out}_{name}.png"; sc.render.filepath=p; bpy.ops.render.render(write_still=True)
    im=Image.open(p).convert("RGB"); d=ImageDraw.Draw(im)
    s=R/(Y1-Y0)
    for i in range(0,17):
        yv=Y0+i*0.05; py=R-(yv-Y0)*s; d.line([(0,py),(R,py)],fill=(255,0,0) if i%2==0 else (255,160,160)); d.text((2,py-11),f"y{yv:.2f}",fill=(200,0,0))
    for i in range(-8,9):
        hv=i*0.05; px=R/2+hv*s; d.line([(px,0),(px,R)],fill=(0,0,255) if i%2==0 else (160,160,255)); d.text((px+2,2),f"{hax}{hv:+.2f}",fill=(0,0,200))
    im.save(p)
