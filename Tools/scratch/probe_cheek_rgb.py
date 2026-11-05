import sys

sys.path.insert(0, r"C:\Users\user\AppData\Roaming\Blender Foundation\Blender\5.2\extensions\blender_org")
import bpy
import numpy as np

for name in ["tex_chr1001_00_cheek0.png", "tex_chr1001_00_cheek1.png"]:
    p = r"C:\Users\user\Documents\umaviewer_exports\morphs\Texture2D\%s" % name
    img = bpy.data.images.load(p)
    w, h = img.size
    px = np.array(img.pixels[:], dtype=np.float32).reshape(h, w, 4)
    rgb = px[..., :3]
    print(name, f"{w}x{h}")
    for c in range(3):
        ch = rgb[..., c]
        print("   ch%d min/mean/max %.4f/%.4f/%.4f" % (c, ch.min(), ch.mean(), ch.max()))
    lum = rgb.mean(axis=2)
    idx = np.unravel_index(np.argmin(lum), lum.shape)
    print("   darkest texel rgb:", [round(float(v), 4) for v in rgb[idx]], "at", idx, "of", (h, w))
    # how many texels are noticeably tinted (r - b)
    rb = rgb[..., 0] - rgb[..., 2]
    print("   r-b min/mean/max %.4f/%.4f/%.4f" % (rb.min(), rb.mean(), rb.max()))
