import sys

sys.path.insert(0, r"C:\Users\user\AppData\Roaming\Blender Foundation\Blender\5.2\extensions\blender_org")
from collections import Counter

from mmd_tools.core import pmx

for path in [
    r"C:\Users\user\Documents\umaviewer_exports\morphs\1001_cheek5.pmx",
    r"C:\Users\user\Documents\umaviewer_exports\morphs\1001_morphs.pmx",
    r"F:\app-vrm\mamama miku\PMX\Appearance Miku_BDEF_mod-v03.pmx",
]:
    try:
        model = pmx.load(path)
        kinds = Counter(type(m).__name__ for m in model.morphs)
        print(path.split("\\")[-1], "morphs:", kinds)
        first = model.morphs[0] if model.morphs else None
        print("   first:", type(first).__name__, getattr(first, "name", "?"), "type=", getattr(first, "type", "?"), "cat=", getattr(first, "category", "?"))
        # how many are VertexMorph
        vm = [m for m in model.morphs if isinstance(m, pmx.VertexMorph)]
        print("   VertexMorph:", len(vm), "offsets:", [len(m.offsets) for m in vm[:5]])
    except Exception as e:
        print(path, "ERROR", type(e).__name__, e)
