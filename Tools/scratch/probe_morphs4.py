import sys
sys.path.insert(0, r"C:\Users\user\AppData\Roaming\Blender Foundation\Blender\5.2\extensions\blender_org")
from mmd_tools.core import pmx
m = pmx.load(r"C:\Users\user\Documents\umaviewer_exports\morphs\1001_morphs4.pmx")
print("morphs:", len(m.morphs))
for mo in m.morphs:
    if mo.name.startswith(("@", "✦", ">", "o_", "Tear", "Cheek")):
        n = len(mo.offsets) if hasattr(mo, "offsets") else 0
        print("  ", repr(mo.name), "offsets:", n)
tex = [t.path for t in m.textures]
for i, t in enumerate(tex):
    if "blush" in t: print("texture", i, t)
