import sys, math
sys.path.insert(0, r"C:\Users\user\AppData\Roaming\Blender Foundation\Blender\5.2\extensions\blender_org")
from mmd_tools.core import pmx
a = pmx.load(r"C:\Users\user\Documents\umaviewer_exports\morphs\1001_morphs6.pmx")
b = pmx.load(r"C:\Users\user\Documents\umaviewer_exports\morphs\1001_morphs12.pmx")
an = {m.name: m for m in a.morphs}
m = [x for x in b.morphs if x.name == "@_@_L"][0]
oa = {o.index: o.offset for o in an["@_@_L"].offsets}
lock = [math.dist(o.offset, oa[o.index]) for o in m.offsets]
print("morphs:", len(b.morphs), "lift %.3f" % (sum(lock)/len(lock)), "spins:", len([x for x in b.morphs if "spin" in x.name]))
