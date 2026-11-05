import sys
sys.path.insert(0, r"C:\Users\user\AppData\Roaming\Blender Foundation\Blender\5.2\extensions\blender_org")
from mmd_tools.core import pmx
m = pmx.load(r"C:\Users\user\Documents\umaviewer_exports\morphs\13.pmx".replace("morphs\\13","morphs\\1001_morphs13"))
for i, mat in enumerate(m.materials):
    if "tear" in mat.name.lower():
        print(i, mat.name, "tex", mat.texture)
