"""Do the decal maps carry meaningful per-pixel alpha, and what is the material's own alpha?

    blender --background --factory-startup --python Tools/probe_decal_alpha.py -- <out.txt> [<file.pmx>]

The first attempt sampled `image.pixels[:4000]` - the first few rows of a 256x256 map - and every decal's top edge is
transparent margin, so it read "alpha is zero everywhere" from a strip that would say that about any decal. This
samples the whole image with a stride instead, and reads `mmd_material.alpha` beside it, because that uniform is what
mmd_tools' own shader multiplies in and it may be where a decal's transparency actually comes from.
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

seen = set()
say("material                             image                                   channel depth  alpha: min/mean/max  transparent%  mmd alpha  flags")
for name in sorted(MorphDrivers.decal_materials()):
    material = bpy.data.materials.get(name)
    if material is None:
        continue
    mmd = getattr(material, "mmd_material", None)
    alpha = getattr(mmd, "alpha", None)
    flags = getattr(mmd, "is_double_sided", None)
    for node in material.node_tree.nodes:
        if node.type != 'TEX_IMAGE' or node.image is None or node.image.name in seen:
            continue
        seen.add(node.image.name)
        image = node.image
        channels = image.channels
        total = image.size[0] * image.size[1]
        stride = max(1, total // 20000)
        pixels = list(image.pixels)
        samples = [pixels[index * 4 + 3] for index in range(0, total, stride)]
        alphas = samples or [0.0]
        say("%-36s %-38s %2d     %2d   %.3f/%.3f/%.3f   %5.1f%%        %s        %s"
            % (material.name, image.name, channels, image.depth, min(alphas),
               sum(alphas) / len(alphas), max(alphas),
               100.0 * sum(1 for value in alphas if value < 0.01) / len(alphas),
               round(alpha, 3) if isinstance(alpha, float) else alpha, flags))

with open(out, "w", encoding="utf-8") as handle:
    handle.write("\n".join(lines) + "\n")
print("wrote %s" % out)
