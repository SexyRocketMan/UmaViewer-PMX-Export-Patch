"""How are the decal maps authored? art on transparency, on white, or on black?

    blender --background --factory-startup --python Tools/probe_decal_masks.py -- <out.txt> [<file.pmx>]

Three authoring styles need three different ways of being drawn, and they are told apart by where the *drawn* pixels
sit in (alpha, luminance):

* **art on transparency** - the art is opaque, the background has alpha 0: draw with the map's own alpha;
* **art on white** - everything is opaque and the art is the dark part: the mask is `1 - luminance` and the game draws
  it as a *multiply* (this is what the eye manga look like when they haze the eye and go pure white when the black is
  keyed out);
* **art on black** - everything is opaque and the art is the bright part: the mask is `luminance` (what the black key
  does).

So each map is reported as four fractions: opaque-and-light, opaque-and-dark, transparent-and-light,
transparent-and-dark, over the whole image (not the first few rows - that mistake was made once already).
"""
import sys

import bpy

ADDON = "bl_ext.user_default.uma_addon"

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]
out = argv[0]
pmx = argv[1] if len(argv) > 1 else r"C:\Users\user\Documents\umaviewer_exports\morphs\1001_nostream.pmx"

lines = []


def say(text=""):
    lines.append(text)


bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.preferences.addon_enable(module="bl_ext.blender_org.mmd_tools")
bpy.ops.preferences.addon_enable(module=ADDON)
bpy.ops.mmd_tools.import_model(filepath=pmx)

from bl_ext.user_default.uma_addon.uma_addon.addons.uma_addon.operators import MorphDrivers  # noqa: E402

ALPHA_CUT = 0.05
LIGHT_CUT = 0.85
DARK_CUT = 0.15

say("%s" % pmx)
say()
say("%-34s %-32s %8s %8s %8s %8s  %s"
    % ("material", "map", "op+light", "op+dark", "tr+light", "tr+dark", "reads as"))
seen = set()
for name in sorted(MorphDrivers.decal_materials()):
    material = bpy.data.materials.get(name)
    if material is None:
        continue
    for node in material.node_tree.nodes:
        if node.type != 'TEX_IMAGE' or node.image is None or node.image.name in seen:
            continue
        seen.add(node.image.name)
        image = node.image
        total_pixels = image.size[0] * image.size[1]
        stride = max(1, total_pixels // 60000)
        pixels = list(image.pixels)
        counts = {"op+light": 0, "op+dark": 0, "tr+light": 0, "tr+dark": 0, "mid": 0}
        sampled = 0
        for index in range(0, total_pixels, stride):
            base = index * 4
            red, green, blue, alpha = (pixels[base], pixels[base + 1], pixels[base + 2], pixels[base + 3])
            luminance = max(red, green, blue)
            sampled += 1
            opaque = alpha > ALPHA_CUT
            if luminance >= LIGHT_CUT:
                counts["op+light" if opaque else "tr+light"] += 1
            elif luminance <= DARK_CUT:
                counts["op+dark" if opaque else "tr+dark"] += 1
            else:
                counts["mid"] += 1
        fraction = {key: 100.0 * value / max(1, sampled) for key, value in counts.items()}
        if fraction["tr+light"] + fraction["tr+dark"] > 40:
            style = "art on transparency"
        elif fraction["op+light"] > 40 and fraction["op+dark"] > 5:
            style = "art on WHITE (multiply)"
        elif fraction["op+dark"] > 40 and fraction["op+light"] > 5:
            style = "art on BLACK (screen)"
        else:
            style = "mixed (mid %2.0f%%)" % fraction["mid"]
        say("%-34s %-32s %7.1f%% %7.1f%% %7.1f%% %7.1f%%  %s"
            % (material.name, image.name, fraction["op+light"], fraction["op+dark"],
               fraction["tr+light"], fraction["tr+dark"], style))

say()
say("`mid` (neither light nor dark) is not in those four columns; a style reading needs the light and dark halves to")
say("add up, so a map whose art is all mid-tones shows up as 'mixed', which is a fair description of a colour overlay.")

with open(out, "w", encoding="utf-8") as handle:
    handle.write("\n".join(lines) + "\n")
print("wrote %s" % out)
