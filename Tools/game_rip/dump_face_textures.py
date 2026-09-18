import UnityPy
from pathlib import Path

OUT = Path(r"C:\Users\user\Documents\dev\_uma_scratch\game_shaders")
env = UnityPy.load(str(OUT / "face_material_bundle.unity3d"))
for obj in env.objects:
    if obj.type.name != "Texture2D":
        continue
    data = obj.read()
    name = getattr(data, "m_Name", "") or f"texture_{obj.path_id}"
    image = data.image
    target = OUT / f"{name}.png"
    image.save(target)
    grey = image.convert("L")
    pixels = list(grey.getdata())
    print(f"{name}: {image.size[0]}x{image.size[1]} -> {target.name}")
    print(f"   channels: {[round(100.0 * sum(1 for p in image.convert('RGB').getdata() if p[c] > 215) / len(pixels)) for c in range(3)]}% bright per rgb")
