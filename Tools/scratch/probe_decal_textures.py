import sys

sys.path.insert(0, r"C:\Users\user\AppData\Roaming\Blender Foundation\Blender\5.2\extensions\blender_org")
import bpy
import numpy as np

paths = [
    r"C:\Users\user\Documents\umaviewer_exports\morphs\Texture2D\tex_chr1001_00_cheek0.png",
    r"C:\Users\user\Documents\umaviewer_exports\morphs\Texture2D\tex_chr1001_00_cheek1.png",
    r"C:\Users\user\Documents\umaviewer_exports\morphs\Texture2D\tex_chr_tear00.png",
    r"C:\Users\user\Documents\umaviewer_exports\morphs\Texture2D\mtl_eff_chr_emo_eye_002.png",
]
for p in paths:
    try:
        img = bpy.data.images.load(p)
    except Exception as e:
        print(p.rsplit("\\", 1)[-1], "ERR", e)
        continue
    w, h = img.size
    px = np.array(img.pixels[:], dtype=np.float32).reshape(h, w, 4)
    a = px[..., 3]
    rgb = px[..., :3]
    lum = rgb.mean(axis=2)
    print(
        p.rsplit("\\", 1)[-1],
        f"{w}x{h}",
        "alpha min/mean/max %.3f/%.3f/%.3f" % (a.min(), a.mean(), a.max()),
        "frac(alpha<0.5) %.3f" % float((a < 0.5).mean()),
        "lum min/mean/max %.3f/%.3f/%.3f" % (lum.min(), lum.mean(), lum.max()),
        "frac(lum>0.95) %.3f" % float((lum > 0.95).mean()),
    )
