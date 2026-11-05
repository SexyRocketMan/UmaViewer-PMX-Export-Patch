import sys, importlib.util
sys.path.insert(0, r"C:\Users\user\AppData\Roaming\Blender Foundation\Blender\5.2\extensions\blender_org")
import bpy
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.preferences.addon_enable(module="bl_ext.blender_org.mmd_tools")
bpy.ops.mmd_tools.import_model(filepath=r"C:\Users\user\Documents\umaviewer_exports\morphs\1001_morphs12.pmx")
# scene group property must be registered by the addon's own loading; here the scene has uma_scene once the addon ran
scene = bpy.context.scene
print("has uma_scene:", hasattr(scene, "uma_scene"))
if hasattr(scene, "uma_scene"):
    print("has advanced_morphs:", hasattr(scene.uma_scene, "advanced_morphs"), getattr(scene.uma_scene, "advanced_morphs", None))
path = r"C:\Users\user\AppData\Roaming\Blender Foundation\Blender\5.2\extensions\user_default\uma_addon\uma_addon\addons\uma_addon\operators\MorphDrivers.py"
spec = importlib.util.spec_from_file_location("mdrv", path)
mod = importlib.util.module_from_spec(spec); spec.loader.exec_module(mod)
rep = mod.setup_advanced_morphs()
print("REPORT", rep["decal_materials"], "decal materials")
m = next(x for x in bpy.data.materials if x.name == "mtl_eff_chr_emo_eye_000")
print("extensions:", [(n.name, n.extension) for n in m.node_tree.nodes if n.type == "TEX_IMAGE"])
