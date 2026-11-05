import sys, math
sys.path.insert(0, r"C:\Users\user\AppData\Roaming\Blender Foundation\Blender\5.2\extensions\blender_org")
from mmd_tools.core import pmx
a = pmx.load(r"C:\Users\user\Documents\umaviewer_exports\morphs\1001_morphs6.pmx")
b = pmx.load(r"C:\Users\user\Documents\umaviewer_exports\morphs\1001_morphs7.pmx")
an = {m.name: m for m in a.morphs}
for m in b.morphs:
    if m.name in an and m.name.startswith(("@", "✦", ">", "o_", "Tear", "Cheek")):
        oa = {o.index: o.offset for o in an[m.name].offsets}
        ds = [math.dist(o.offset, oa[o.index]) for o in m.offsets if o.index in oa]
        print("%-8s delta/dist %s" % (repr(m.name), [round(d, 3) for d in ds[:3]]))
