import sys, math
sys.path.insert(0, r"C:\Users\user\AppData\Roaming\Blender Foundation\Blender\5.2\extensions\blender_org")
from mmd_tools.core import pmx
a = pmx.load(r"C:\Users\user\Documents\umaviewer_exports\morphs\1001_morphs6.pmx")
b = pmx.load(r"C:\Users\user\Documents\umaviewer_exports\morphs\1001_morphs9.pmx")
an = {m.name: m for m in a.morphs}
print("morphs:", len(b.morphs), "names:", [m.name for m in b.morphs if m.name in ("Blush","Tear_L","Tear_R") or m.name.startswith(("@","✦",">","o_"))])
for m in b.morphs:
    if m.name in an:
        oa = {o.index: o.offset for o in an[m.name].offsets}
        ds = [math.dist(o.offset, oa[o.index]) for o in m.offsets if o.index in oa]
        if ds and (m.name.startswith(("@","✦",">","o_","Tear","Blush"))):
            print("%-8s lift %.3f" % (repr(m.name), sum(ds)/len(ds)))
