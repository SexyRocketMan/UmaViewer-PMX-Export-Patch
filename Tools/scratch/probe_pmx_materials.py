import sys

sys.path.insert(0, r"C:\Users\user\AppData\Roaming\Blender Foundation\Blender\5.2\extensions\blender_org")
from mmd_tools.core import pmx

path = r"C:\Users\user\Documents\umaviewer_exports\morphs\1001_morphs.pmx"
model = pmx.load(path)
print("attr keys:", sorted(vars(model.materials[0]).keys()))
for i, mat in enumerate(model.materials):
    d = vars(mat)
    print(
        i,
        mat.name,
        "diffuse=", [round(c, 3) for c in d.get("diffuse", [])],
        "edge=", d.get("edge_size"),
        "flags=0x%02X" % d.get("flags", 0),
        "toon=", d.get("toon_texture"),
        "tex=", d.get("texture"),
    )
