from PIL import Image
from pathlib import Path

src = Path(r"C:\Users\user\Documents\dev\_uma_scratch\game_shaders")
base = Image.open(src / "tex_chr1001_00_face_base.png").convert("RGB")
shad = Image.open(src / "tex_chr1001_00_face_shad_c.png").convert("RGB")
ctrl = Image.open(src / "tex_chr1001_00_face_ctrl.png").convert("RGB")

def channels(image, label):
    r, g, b = image.split()
    out = Image.new("RGB", (image.width * 3 + 16, image.height), (20, 20, 24))
    for index, channel in enumerate((r, g, b)):
        out.paste(Image.merge("RGB", (channel, channel, channel)), (index * (image.width + 8), 0))
    return out

rows = [channels(base, "base"), channels(shad, "shad_c")]
sheet = Image.new("RGB", (rows[0].width, sum(r.height + 10 for r in rows)), (20, 20, 24))
y = 0
for row in rows:
    sheet.paste(row, (0, y)); y += row.height + 10
sheet.save(src / "face_channels.png")
print("wrote face_channels.png", sheet.size, "- top: _base (r|g|b), bottom: _shad_c (r|g|b)")
