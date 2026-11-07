import bpy
bpy.ops.preferences.addon_enable(module="bl_ext.user_default.uma_addon")
scene = bpy.context.scene
print("uma_scene:", hasattr(scene, "uma_scene"))
if hasattr(scene, "uma_scene"):
    sc = scene.uma_scene
    print("advanced_morphs:", hasattr(sc, "advanced_morphs"), getattr(sc, "advanced_morphs", None))
    print("props:", [p.identifier for p in sc.bl_rna.properties if p.identifier in ("fix_outlines","shader_control","advanced_morphs","face_normals","shader")])
# and the panel draw function should reference it
import bl_ext.user_default.uma_addon.addons.uma_addon.panels.AddonPanels as P
src = __import__("inspect").getsource(P._draw_shading_switches)
print("panel mentions:", "advanced_morphs" in src)
