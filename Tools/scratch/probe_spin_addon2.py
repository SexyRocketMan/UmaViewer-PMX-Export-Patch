import sys, importlib.util, math
sys.path.insert(0, r"C:\Users\user\AppData\Roaming\Blender Foundation\Blender\5.2\extensions\blender_org")
import bpy
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.preferences.addon_enable(module="bl_ext.blender_org.mmd_tools")
bpy.ops.mmd_tools.import_model(filepath=r"C:\Users\user\Documents\umaviewer_exports\morphs\1001_morphs11.pmx")
path = r"C:\Users\user\AppData\Roaming\Blender Foundation\Blender\5.2\extensions\user_default\uma_addon\uma_addon\addons\uma_addon\operators\MorphDrivers.py"
spec = importlib.util.spec_from_file_location("morphdrivers_standalone", path)
mod = importlib.util.module_from_spec(spec); spec.loader.exec_module(mod)
mod.build_spin()
root = next(o for o in bpy.data.objects if getattr(o, "mmd_type", "") == "ROOT")
m = next(x for x in bpy.data.materials if x.name == "mtl_eff_chr_emo_eye_000")
mp = next(n for n in m.node_tree.nodes if n.type == "MAPPING" and n.label == "Spin")
for v in (0.0, 0.25, 0.5):
    root["Spin"] = v
    root.update_tag()
    bpy.context.view_layer.update()
    print("Spin=%.2f -> rotation Z %.4f (want %.4f)" % (v, mp.inputs['Rotation'].default_value[2], v*2*math.pi))
