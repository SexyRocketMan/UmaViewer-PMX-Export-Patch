import sys

sys.path.insert(0, r"C:\Users\user\AppData\Roaming\Blender Foundation\Blender\5.2\extensions\blender_org")
import bpy
import math
from mathutils import Vector

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.preferences.addon_enable(module="bl_ext.blender_org.mmd_tools")

path = r"C:\Users\user\Documents\umaviewer_exports\morphs\1001_morphs2.pmx"
out = r"C:\Users\user\Documents\umaviewer_exports\morphs\_decal_check"
bpy.ops.mmd_tools.import_model(filepath=path)

mesh = [o for o in bpy.data.objects if o.type == "MESH"][0]
arm = [o for o in bpy.data.objects if o.type == "ARMATURE"][0]
head = arm.matrix_world @ arm.pose.bones["Head"].head
print("HEAD", tuple(round(c, 3) for c in head))

scene = bpy.context.scene
scene.render.engine = "BLENDER_EEVEE"
scene.render.resolution_x = 512
scene.render.resolution_y = 512
scene.render.film_transparent = True

cam_data = bpy.data.cameras.new("cam")
cam = bpy.data.objects.new("cam", cam_data)
scene.collection.objects.link(cam)
scene.camera = cam
cam_data.lens = 85

sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", type="SUN"))
sun.data.energy = 3
sun.rotation_euler = (math.radians(50), 0, math.radians(-30))
scene.collection.objects.link(sun)

sk = mesh.data.shape_keys
names = ["Cheek_Show", "Manga_000", "Manga_001", "Manga_002", "Manga_003", "Tear_L", "Tear_R"]


def shoot(tag):
    for i, offset in enumerate([Vector((0, -0.45, 0.0)), Vector((0, 0, -0.45)), Vector((0.45, 0, 0))]):
        cam.location = head + offset
        direction = head - cam.location
        cam.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = ""
        bpy.ops.render.render(write_still=False)
        bpy.data.images["Render Result"].save_render(f"{out}_{tag}_{i}.png", scene=scene)
        print("wrote", f"{out}_{tag}_{i}.png")


shoot("rest")
for n in names:
    kb = sk.key_blocks.get(n)
    if kb:
        kb.value = 1.0
shoot("deployed")
print("done")
