import sys, math
sys.path.insert(0, r"C:\Users\user\AppData\Roaming\Blender Foundation\Blender\5.2\extensions\blender_org")
from mmd_tools.core import pmx
m = pmx.load(r"C:\Users\user\Documents\umaviewer_exports\morphs\1001_morphs10.pmx")
print("morphs:", len(m.morphs))
from collections import Counter
print("types:", Counter(type(mo).__name__ for mo in m.morphs))
for mo in m.morphs:
    if "spin" in mo.name:
        offs = mo.offsets
        # rotation angle implied by a delta: |delta| = 2 sin(a/2) |u-c|
        print("%-14s n=%d" % (mo.name, len(offs)))
        if mo.name.endswith("spin4"):
            for o in offs:
                print("   v%d uv offset (%.4f, %.4f)" % (o.index, o.offset[0], o.offset[1]))
