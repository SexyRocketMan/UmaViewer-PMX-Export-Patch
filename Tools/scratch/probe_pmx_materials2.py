import sys
sys.path.insert(0, r"C:\Users\user\AppData\Roaming\Blender Foundation\Blender\5.2\extensions\blender_org")
from mmd_tools.core import pmx
path = r"C:\Users\user\Documents\umaviewer_exports\morphs\1001_morphs2.pmx"
model = pmx.load(path)
for i, mat in enumerate(model.materials):
    d = vars(mat)
    print(i, mat.name, "diffuse=", [round(c,3) for c in d.get("diffuse", [])],
          "edge=", round(d.get("edge_size",0),3), "edge_on=", d.get("enabled_toon_edge"),
          "double=", d.get("is_double_sided"), "shadow=", d.get("enabled_self_shadow"),
          "drop=", d.get("enabled_drop_shadow"), "morphs=", len(model.morphs))
    if i >= 18: break
print("morph count:", len(model.morphs))
print("morph names tail:", [m.name for m in model.morphs[-8:]])
