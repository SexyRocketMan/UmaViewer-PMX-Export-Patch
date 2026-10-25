"""Is the exported face-blush layer usable? Size, coverage, and where it sits.

    blender --background --factory-startup --python Tools/probe_faceblush.py -- <out.txt> <folder>

Reads every `*_faceblush.png` in the model's `Texture2D` folder beside the pmx, plus the face atlas it was baked against,
and reports: the two sizes (they must match, or the add-on's mix node samples the wrong texels), how much of the layer
is the white "no blush" value, the bounding box of the non-white texels, and how strong the strongest blush is. A layer
that is all white, or whose blush sits in a corner, is a bake that ran but did not project.
"""
import os
import sys

import bpy

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]
out, folder = argv[0], argv[1]

lines = []


def say(text=""):
    lines.append(text)


def load(path):
    image = bpy.data.images.load(path, check_existing=False)
    width, height = image.size
    pixels = [0.0] * (width * height * 4)
    image.pixels.foreach_get(pixels)
    bpy.data.images.remove(image)
    return width, height, pixels


textures = os.path.join(folder, "Texture2D")
layers = sorted(name for name in os.listdir(textures) if name.endswith("_faceblush.png")) \
    if os.path.isdir(textures) else []
say("folder: %s" % textures)
say("face-blush layers: %s" % (layers or "NONE - the bake did not write a file"))
atlas = [name for name in os.listdir(textures)
         if "face" in name.lower() and "diff" in name.lower() and name.endswith(".png")] \
    if os.path.isdir(textures) else []
say("face atlas candidates: %s" % (atlas or "none found"))
say()

for layer_name in layers:
    width, height, pixels = load(os.path.join(textures, layer_name))
    white = 0
    tinted = 0
    strongest = 0.0
    min_x, max_x, min_y, max_y = width, -1, height, -1
    total = width * height
    for index in range(total):
        base = index * 4
        red, green, blue = pixels[base], pixels[base + 1], pixels[base + 2]
        darkness = 1.0 - min(red, green, blue)
        if max(abs(red - 1.0), abs(green - 1.0), abs(blue - 1.0)) < 0.004:
            white += 1
            continue
        tinted += 1
        strongest = max(strongest, darkness)
        x, y = index % width, index // width
        min_x, max_x = min(min_x, x), max(max_x, x)
        min_y, max_y = min(min_y, y), max(max_y, y)
    say("%s: %dx%d" % (layer_name, width, height))
    say("     white (no blush): %d of %d (%.2f%%)" % (white, total, 100.0 * white / total))
    say("     tinted texels:    %d (%.3f%% of the layer)" % (tinted, 100.0 * tinted / total))
    say("     strongest tint:   %.3f (1.0 = black)" % strongest)
    if tinted:
        say("     texel box: x %d..%d, y %d..%d  (centre %.1f%%, %.1f%% of the layer)"
            % (min_x, max_x, min_y, max_y,
               100.0 * (min_x + max_x) / 2.0 / width, 100.0 * (min_y + max_y) / 2.0 / height))
    for candidate in atlas:
        candidate_width, candidate_height, _pixels = load(os.path.join(textures, candidate))
        say("     against %s: %dx%d -> %s"
            % (candidate, candidate_width, candidate_height,
               "the same size, so the mix samples the right texels"
               if (candidate_width, candidate_height) == (width, height)
               else "DIFFERENT SIZE - the layer has to be baked at the atlas's size"))
        break

say()
say("a usable layer is mostly white with two tinted patches low on the texture, not an empty sheet and not a")
say("full-length smear - the box above is what says which it is.")

with open(out, "w", encoding="utf-8") as handle:
    handle.write("\n".join(lines) + "\n")
print("wrote %s" % out)
