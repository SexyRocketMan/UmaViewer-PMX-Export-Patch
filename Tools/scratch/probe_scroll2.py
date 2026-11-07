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
root = next(o for o in bpy.data.objects if getattr(o, "mmd_type", "") == "ROOT")
spin = next(x for x in bpy.data.materials if x.name == "mtl_eff_chr_emo_eye_000")
tear = next(x for x in bpy.data.materials if x.name == "mtl_chr_tear000")
def ext(m):
    return [(n.extension) for n in m.node_tree.nodes if n.type == "TEX_IMAGE"]
print("spin tex ext:", ext(spin), "tear tex ext:", ext(tear))
print("props:", [k for k in root.keys()], "advanced:", rep["decal_materials"], "scroll drivers:", rep["tear_scroll"]["drivers"])
