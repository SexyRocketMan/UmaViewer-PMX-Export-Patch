import sys
sys.path.insert(0, r"C:\Users\user\AppData\Roaming\Blender Foundation\Blender\5.2\extensions\blender_org")
import bpy, numpy as np
for n in ["tex_chr1001_00_cheek0_blush.png", "tex_chr1001_00_cheek1_blush.png"]:
    img = bpy.data.images.load(r"C:\Users\user\Documents\umaviewer_exports\morphs\Texture2D\%s" % n)
    w, h = img.size
    px = np.array(img.pixels[:], dtype=np.float32).reshape(h, w, 4)
    a = px[..., 3]
    rgb = px[..., :3]
    print(n, "alpha max %.3f mean %.3f" % (a.max(), a.mean()),
          "darkest rgb", [round(float(v),3) for v in rgb[np.unravel_index(np.argmax(a), a.shape)]])
