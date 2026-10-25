"""What the decal materials on a real export look like *before* shading, which is what the unlit material must fit.

    blender --background --factory-startup --python Tools/probe_decals.py -- <out.txt> [<file.pmx>]

For each material `MorphDrivers.decal_materials()` names: its image nodes (node name and the file it holds), whether
the image has an alpha channel worth using, the texture extension, and the draw settings the decal pass writes today.
The node *names* are the point: the library's `Uma Decal` has to be bound by the same rule the shaded copies use, and
a decal material is an mmd_tools import, so its main image node may be `mmd_base_tex` rather than `Image Texture`.
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

names = sorted(MorphDrivers.decal_materials())
say("%s - %d decal material name(s)" % (pmx, len(names)))
say()
for name in names:
    material = bpy.data.materials.get(name)
    if material is None:
        say("%-34s MISSING from the file" % name)
        continue
    images = [(node.name, node.image.name if node.image else None) for node in material.node_tree.nodes
              if node.type == 'TEX_IMAGE']
    say("%-34s %s" % (name, "nodes=%d" % len(material.node_tree.nodes)))
    for node_name, image_name in images:
        node = material.node_tree.nodes[node_name]
        depth = None
        if node.image is not None:
            depth = (node.image.depth, node.image.size[0], node.image.size[1])
        say("     image node %-20s %-40s extension=%-7s image=%s"
            % (node_name, image_name, node.extension, depth))
    say("     surface_render_method=%r transparent_shadow=%r backface_culling=%r"
        % (material.surface_render_method, material.use_transparent_shadow, material.use_backface_culling))
    comment = getattr(getattr(material, "mmd_material", None), "comment", "") or ""
    tokens = [token for token in comment.split() if token.startswith(("morph=", "blend=", "scroll="))]
    say("     tokens: %s" % (tokens or "none"))
say()
say("alpha channels (does the map carry transparency at all?):")
for name in names:
    material = bpy.data.materials.get(name)
    if material is None:
        continue
    for node in material.node_tree.nodes:
        if node.type != 'TEX_IMAGE' or node.image is None:
            continue
        pixels = list(node.image.pixels[:4000])
        alphas = pixels[3::4]
        say("  %-34s %-40s alpha min=%.3f max=%.3f opaque_fraction=%.2f"
            % (name, node.image.name, min(alphas), max(alphas),
               sum(1 for value in alphas if value > 0.99) / max(1, len(alphas))))

with open(out, "w", encoding="utf-8") as handle:
    handle.write("\n".join(lines) + "\n")
print("wrote %s (%d lines)" % (out, len(lines)))
