import sys, importlib.util, math
sys.path.insert(0, r"C:\Users\user\AppData\Roaming\Blender Foundation\Blender\5.2\extensions\blender_org")
import bpy
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.preferences.addon_enable(module="bl_ext.blender_org.mmd_tools")
bpy.ops.mmd_tools.import_model(filepath=r"C:\Users\user\Documents\umaviewer_exports\morphs\1001_morphs12.pmx")
path = r"C:\Users\user\AppData\Roaming\Blender Foundation\Blender\5.2\extensions\user_default\uma_addon\uma_addon\addons\uma_addon\operators\MorphDrivers.py"
spec = importlib.util.spec_from_file_location("mdrv", path)
mod = importlib.util.module_from_spec(spec); spec.loader.exec_module(mod)
rep = mod.setup_advanced_morphs()
print("REPORT", rep)
root = next(o for o in bpy.data.objects if getattr(o, "mmd_type", "") == "ROOT")
print("props:", [k for k in root.keys()], "speed", root.get("Spin_Speed"), "offset", root.get("Spin"))
m = next(x for x in bpy.data.materials if x.name == "mtl_eff_chr_emo_eye_000")
rot = next(n for n in m.node_tree.nodes if n.type == "VECTOR_ROTATE")
print("rotate type", rot.rotation_type, "center", tuple(round(c,2) for c in rot.inputs['Center'].default_value))
print("driver expr:", [d.driver.expression for d in m.node_tree.animation_data.drivers])
print("blend", m.surface_render_method, "transparent_shadow", m.use_transparent_shadow)
for frame in (0, 6, 12):
    bpy.context.scene.frame_set(frame)
    root.update_tag(); bpy.context.view_layer.update()
    print("frame %d angle %.4f (1 turn/s: %.4f)" % (frame, rot.inputs['Angle'].default_value, frame*0.2617993877991494))
