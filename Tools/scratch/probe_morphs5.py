import sys
sys.path.insert(0, r"C:\Users\user\AppData\Roaming\Blender Foundation\Blender\5.2\extensions\blender_org")
from mmd_tools.core import pmx
m = pmx.load(r"C:\Users\user\Documents\umaviewer_exports\morphs\1001_morphs5.pmx")
print("morphs:", len(m.morphs))
for mo in m.morphs:
    if mo.name.startswith(("@", "✦", ">", "o_", "Tear", "Cheek")):
        xs = [round(o.offset[0], 3) for o in mo.offsets[0:2]]
        print("  ", repr(mo.name), "offsets:", len(mo.offsets))
