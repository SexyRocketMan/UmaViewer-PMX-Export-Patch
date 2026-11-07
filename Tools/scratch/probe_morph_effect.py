import sys

sys.path.insert(0, r"C:\Users\user\AppData\Roaming\Blender Foundation\Blender\5.2\extensions\blender_org")
import logging

logging.disable(logging.CRITICAL)

import bpy

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.preferences.addon_enable(module="bl_ext.blender_org.mmd_tools")

path = r"C:\Users\user\Documents\umaviewer_exports\morphs\1001_morphs.pmx"
bpy.ops.mmd_tools.import_model(filepath=path)

meshes = [o for o in bpy.data.objects if o.type == "MESH"]
print("meshes:", [(o.name, len(o.data.vertices)) for o in meshes])
ob = meshes[0]
sk = ob.data.shape_keys
print("shape keys:", len(sk.key_blocks))

basis = sk.key_blocks[0]
bco = [tuple(v.co) for v in basis.data]
for name in ["Cheek_Show", "Manga_000", "Manga_001", "Manga_002", "Manga_003", "Tear_L", "Tear_R"]:
    kb = sk.key_blocks.get(name)
    if kb is None:
        print(name, "MISSING")
        continue
    moved = []
    for i, v in enumerate(kb.data):
        d = (v.co[0] - bco[i][0]) ** 2 + (v.co[1] - bco[i][1]) ** 2 + (v.co[2] - bco[i][2]) ** 2
        if d > 1e-10:
            moved.append((i, d**0.5))
    moved.sort(key=lambda t: -t[1])
    print(f"{name}: moved_verts={len(moved)} max_delta={moved[0][1]:.4f}" if moved else f"{name}: NO MOVEMENT")
    if moved:
        i = moved[0][0]
        print(f"   v{i} basis={tuple(round(c,4) for c in bco[i])} morph={tuple(round(c,4) for c in kb.data[i].co)}")

print("---- materials alpha / edge / double-sided ----")
for m in ob.data.materials:
    print(m.name if m else None)
print("---- PMX materials via parser ----")
from mmd_tools.core import pmx

model = pmx.load(path)
for i, mat in enumerate(model.materials):
    