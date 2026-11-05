import sys
sys.path.insert(0, r"C:\Users\user\AppData\Roaming\Blender Foundation\Blender\5.2\extensions\blender_org")
from mmd_tools.core import pmx
import bpy, numpy as np
m = pmx.load(r"C:\Users\user\Documents\umaviewer_exports\morphs\1001_morphs7.pmx")
print("morphs:", len(m.morphs))
for mo in m.morphs:
    if mo.name.startswith(("@", "✦", ">", "o_", "Tear", "Cheek")):
        xs = [o.offset[0] for o in mo.offsets]; zs = [o.offset[2] for o in mo.offsets]
        print("  %-8s n=%-3d mean dx %+.3f mean dz %+.3f" % (repr(mo.name), len(mo.offsets), sum(xs)/len(xs), sum(zs)/len(zs)))
for n in ["tex_chr1001_00_cheek0_blush.png"]:
    img = bpy.data.images.load(r"C:\Users\user\Documents\umaviewer_exports\morphs\Texture2D\%s" % n)
    w, h = img.size
    px = np.array(img.pixels[:], dtype=np.float32).reshape(h, w, 4)
    a = px[..., 3]
    u = len(np.unique(np.round(a*255).astype(int)))
    print(n, "alpha max %.3f unique levels %d" % (a.max(), u))
