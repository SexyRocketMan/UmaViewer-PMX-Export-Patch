import sys

sys.path.insert(0, r"C:\Users\user\AppData\Roaming\Blender Foundation\Blender\5.2\extensions\blender_org")
import bpy

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.preferences.addon_enable(module="bl_ext.blender_org.mmd_tools")

path = r"C:\Users\user\Documents\umaviewer_exports\morphs\1001_morphs2.pmx"
bpy.ops.mmd_tools.import_model(filepath=path)

mesh = [o for o in bpy.data.objects if o.type == "MESH"][0]
names = {m.name for m in mesh.data.materials}
decal_mats = {i for i, m in enumerate(mesh.data.materials) if m and m.name.startswith(("mtl_chr1001_00_cheek", "mtl_eff_chr_emo_eye", "mtl_chr1001_00_tear"))}
print("decal material slots:", sorted(decal_mats))

sk = mesh.data.shape_keys
deploy = ["Cheek_Show", "Manga_000", "Manga_001", "Manga_002", "Manga_003", "Tear_L", "Tear_R"]
dg = bpy.context.evaluated_depsgraph_get()


def decal_area(label):
    bpy.context.view_layer.update()
    dg.update()
    ev = mesh.evaluated_get(dg)
    me = ev.to_mesh()
    total = 0.0
    for poly in me.polygons:
        if poly.material_index in decal_mats:
            total += poly.area
    ev.to_mesh_clear()
    print(f"{label}: decal polygon area = {total:.6f}")


decal_area("rest")
for n in deploy:
    sk.key_blocks[n].value = 1.0
decal_area("deployed")

# and one at a time, to see each part deploy on its own
for n in deploy:
    for other in deploy:
        sk.key_blocks[other].value = 0.0
    sk.key_blocks[n].value = 1.0
    decal_area(f"  only {n}")
