import sys
sys.path.insert(0, r"C:\Users\user\AppData\Roaming\Blender Foundation\Blender\5.2\extensions\blender_org")
import bpy
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.preferences.addon_enable(module="bl_ext.blender_org.mmd_tools")
bpy.ops.mmd_tools.import_model(filepath=r"C:\Users\user\Documents\umaviewer_exports\morphs\1001_morphs16.pmx")
for m in bpy.data.materials:
    if "eye" in m.name.lower() and "hi" not in m.name.lower():
        tree = m.node_tree
        imgs = [(n.name, n.image.name if n.image else None) for n in tree.nodes if n.type == "TEX_IMAGE"]
        print(m.name, "| image nodes:", imgs)
print("---- exported eyehi textures on disk ----")
import os
d = r"C:\Users\user\Documents\umaviewer_exports\morphs\Texture2D"
print([f for f in os.listdir(d) if "eyehi" in f])
