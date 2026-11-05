import sys
sys.path.insert(0, r"C:\Users\user\AppData\Roaming\Blender Foundation\Blender\5.2\extensions\blender_org")
from mmd_tools.core import pmx
m = pmx.load(r"C:\Users\user\Documents\umaviewer_exports\morphs\1001_morphs16.pmx")
for mat in m.materials:
    c = getattr(mat, "comment", "") or ""
    if "scroll" in c:
        print(mat.name, "|", [t for t in c.split() if t.startswith("scroll=")])
print("morphs:", len(m.morphs))
