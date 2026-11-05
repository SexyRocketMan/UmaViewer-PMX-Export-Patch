import sys, math
sys.path.insert(0, r"C:\Users\user\AppData\Roaming\Blender Foundation\Blender\5.2\extensions\blender_org")
from mmd_tools.core import pmx
a = pmx.load(r"C:\Users\user\Documents\umaviewer_exports\morphs\1001_morphs6.pmx")
b = pmx.load(r"C:\Users\user\Documents\umaviewer_exports\morphs\1001_morphs11.pmx")
an = {m.name: m for m in a.morphs}
print("morphs:", len(b.morphs))
m = [x for x in b.morphs if x.name == "@_@_L"][0]
oa = {o.index: o.offset for o in an["@_@_L"].offsets}
ds = [math.dist(o.offset, oa[o.index]) for o in m.offsets]
print("lift %.3f" % (sum(ds)/len(ds)), "spins:", len([x for x in b.morphs if "spin" in x.name]))
