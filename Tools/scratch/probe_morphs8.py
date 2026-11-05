import sys, math
sys.path.insert(0, r"C:\Users\user\AppData\Roaming\Blender Foundation\Blender\5.2\extensions\blender_org")
from mmd_tools.core import pmx
import bpy, numpy as np
a = pmx.load(r"C:\Users\user\Documents\umaviewer_exports\morphs\1001_morphs6.pmx")
b = pmx.load(r"C:\Users\user\Documents\umaviewer_exports\morphs\1001_morphs8.pmx")
an = {m.name: m for m in a.morphs}
print("morphs:", len(b.morphs))
for m in b.morphs:
    if m.name.startswith(("@", "✦", ">", "o_", "Tear", "Cheek")):
        oa = {o.index: o.offset for o in an[m.name].offsets}
        ds = [math.dist(o.offset, oa[o.index]) for o in m.offsets if o.index in oa]
        xs = [o.offset[0] for o in m.offsets]
        print("  %-8s n=%-3d lift %.3f mean dx %+.3f" % (repr(m.name), len(m.offsets), sum(ds)/len(ds), sum(xs)/len(xs)))
img = bpy.data.images.load(r"C:\Users\user\Documents\umaviewer_exports\morphs\Texture2D\tex_chr1001_00_cheek0_blush.png")
w, h = img.size
px = np.array(img.pixels[:], dtype=np.float32).reshape(h, w, 4)
a_c = np.round(px[...,3]*255).astype(int)
print("blush alpha max %.3f unique levels %d" % (px[...,3].max(), len(np.unique(a_c))))
