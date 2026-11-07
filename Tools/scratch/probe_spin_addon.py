import sys, importlib.util, math
sys.path.insert(0, r"C:\Users\user\AppData\Roaming\Blender Foundation\Blender\5.2\extensions\blender_org")
import bpy

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.preferences.addon_enable(module="bl_ext.blender_org.mmd_tools")
bpy.ops.mmd_tools.import_model(filepath=r"C:\Users\user\Documents\umaviewer_exports\morphs\1001_morphs11.pmx")

path = r"C:\Users\user\AppData\Roaming\Blender Foundation\Blender\5.2\extensions\user_default\uma_addon\uma_addon\addons\uma_addon\operators\MorphDrivers.py"
spec = importlib.util.spec_from_file_location("morphdrivers_standalone", path)
mod = importlib.util.module_from_spec(spec)
spec.loader.exec_module(mod)

report = mod.build_spin()
print("SPIN REPORT", report)
mats = [m for m in bpy.data.materials if m.name.startswith(mod.SPIN_MATERIAL)]
for m in mats:
    tree = m.node_tree
    nodes = [(n.type, n.name, n.label) for n in tree.nodes]
    drivers = [] if tree.animation_data is None else [(d.data_path, d.driver.expression) for d in tree.animation_data.drivers]
    print(m.name, "nodes with MAPPING:", [n for n in nodes if n[0] == "MAPPING"], "drivers:", drivers)
    # links into/out of the mapping
    mp = next((n for n in tree.nodes if n.type == "MAPPING" and n.label == "Spin"), None)
    if mp:
        print("   mapping in:", [(l.from_node.type, l.from_socket.name) for l in mp.inputs['Vector'].links],
              "out:", [(l.to_node.type, l.to_socket.name) for l in mp.outputs['Vector'].links])
        for n in tree.nodes:
            if n.type == "TEX_IMAGE":
                print("   tex", n.name, "vector from:", [(l.from_node.type, l.from_node.label) for l in n.inputs['Vector'].links])
root = next(o for o in bpy.data.objects if getattr(o, "mmd_type", "") == "ROOT")
root["Spin"] = 0.25
bpy.context.view_layer.update()
m0 = mats[0]
mp = next(n for n in m0.node_tree.nodes if n.type == "MAPPING" and n.label == "Spin")
print("Spin=0.25 -> rotation Z = %.4f (pi/2 = %.4f)" % (mp.inputs['Rotation'].default_value[2], math.pi/2))
