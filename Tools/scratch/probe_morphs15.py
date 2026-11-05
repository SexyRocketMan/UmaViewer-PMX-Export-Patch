import sys
sys.path.insert(0, r"C:\Users\user\AppData\Roaming\Blender Foundation\Blender\5.2\extensions\blender_org")
from mmd_tools.core import pmx
m = pmx.load(r"C:\Users\user\Documents\umaviewer_exports\morphs\1001_morphs15.pmx")
tex = [t.path for t in m.textures]
print("textures:", len(tex))
for i, t in enumerate(tex):
    if "tear" in t.lower(): print("  ", i, t)
for mat in m.materials:
    if "tear000" in mat.name or "tear001" in mat.name:
        print(mat.name, "->", tex[mat.texture] if mat.texture >= 0 else None)
print("morphs:", len(m.morphs))
