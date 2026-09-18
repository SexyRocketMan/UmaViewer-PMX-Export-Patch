"""Tile rendered images into a grid. For comparing the viewer's grid with Blender's."""

import sys

from PIL import Image

out_path = sys.argv[1]
columns = int(sys.argv[2])
paths = sys.argv[3:]
images = [Image.open(path).convert("RGB") for path in paths]
cell = max(image.height for image in images)
widths = [int(image.width * cell / image.height) for image in images]
cell_width = max(widths)
rows = (len(images) + columns - 1) // columns
sheet = Image.new("RGB", (cell_width * columns, cell * rows), (20, 20, 24))
for index, image in enumerate(images):
    scaled = image.resize((widths[index], cell))
    sheet.paste(scaled, ((index % columns) * cell_width, (index // columns) * cell))
sheet.save(out_path)
print(f"wrote {out_path}: {columns}x{rows} from {len(images)} images")
