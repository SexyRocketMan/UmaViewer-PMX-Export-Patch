import sys
sys.path.insert(0, r"C:\Users\user\AppData\Roaming\Blender Foundation\Blender\5.2\extensions\blender_org")
from mmd_tools.core import pmx
from collections import Counter
m = pmx.load(r"C:\Users\user\Documents\umaviewer_exports\morphs\1001_morphs13.pmx")
print("morphs:", len(m.morphs), Counter(type(x).__name__ for x in m.morphs))
tears = [x for x in m.morphs if "Tear" in x.name]
print("tear morphs:", [(x.name, len(x.offsets) if hasattr(x, "offsets") else 0) for x in tears][:14])
spins = [x for x in m.morphs if "scr" in x.name]
print("scroll morph sample:", [(x.name, len(x.offsets)) for x in spins[:3]])
