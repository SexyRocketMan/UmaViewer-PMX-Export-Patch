import sys, importlib.util
sys.path.insert(0, r"C:\Users\user\AppData\Roaming\Blender Foundation\Blender\5.2\extensions\blender_org")
import bpy
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.preferences.addon_enable(module="bl_ext.blender_org.mmd_tools")
bpy.ops.mmd_tools.import_model(filepath=r"C:\Users\user\Documents\umaviewer_exports\morphs\1001_morphs16.pmx")
path = r"C:\Users\user\AppData\Roaming\Blender Foundation\Blender\5.2\extensions\user_default\uma_addon\uma_addon\addons\uma_addon\operators\MorphDrivers.py"
spec = importlib.util.spec_from_file_location("mdrv", path)
mod = importlib.util.module_from_spec(spec); spec.loader.exec_module(mod)
rep = mod.setup_advanced_morphs()
print("ADVANCED", rep["decal_materials"], "decals, spin", rep["spin"]["drivers"], "scroll", rep["tear_scroll"]["drivers"])
root = next(o for o in bpy.data.objects if getattr(o, "mmd_type", "") == "ROOT")
print("props:", [k for k in root.keys()])
m = next(x for x in bpy.data.materials if x.name == "mtl_chr_tear000")
mp = next((n for n in m.node_tree.nodes if n.type == "MAPPING" and n.label == "Tear Scroll"), None)
print("mapping:", bool(mp), "drivers:", [d.driver.expression for d in (m.node_tree.animation_data.drivers if m.node_tree.animation_data else [])])
for frame in (0, 12, 24):
    bpy.context.scene.frame_set(frame)
    root.update_tag(); bpy.context.view_layer.update()
    print("frame %d loc %s" % (frame, tuple(round(c, 4) for c in mp.inputs['Location'].default_value)))
