"""Shade a batch of exported models and report what the add-on made of each one.

    blender -b --factory-startup -P Tools/verify_export_matrix.py -- <out.txt> <shader-id> <file-or-folder> [more...]

One block per file, so a folder of exports from different settings - and from different exporters - can be compared
side by side. Everything reported is read back off the scene *after* the Shading run: the point is to catch a model
the add-on silently does less for, not to prove the operator returned.

What each block answers:
  * did it import, and with how many morphs / materials / textures (a "no extra morphs" export is expected to have
    fewer morphs and no decal parts);
  * did the Shading run finish, and which materials it recognised;
  * what the decals got (the shipped blush for a multiply decal, `CLIP` on the spinning eye, `REPEAT` elsewhere);
  * whether the weather looked found anything (wet slots filled, dirt maps bound);
  * whether the eye and its knobs came through (drivers), and whether the outline shell exists.
"""
import glob
import os
import sys
import traceback

import bpy

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
out_path = argv[0] if argv else r"C:\tmp\matrix.txt"
shader_id = argv[1] if len(argv) > 1 else "umashader-0.2.6"
targets = argv[2:] if len(argv) > 2 else [r"C:\tmp\v6\1001_00.pmx"]

files = []
for target in targets:
    if os.path.isdir(target):
        files.extend(sorted(glob.glob(os.path.join(target, "**", "*.pmx"), recursive=True)))
    else:
        files.append(target)

lines = ["shader: %s" % shader_id, "files: %d" % len(files), ""]


def count_materials():
    return len([material for material in bpy.data.materials if material is not None])


def report_one(path):
    block = ["== %s" % os.path.basename(path)]
    try:
        bpy.ops.wm.read_factory_settings(use_empty=True)
        bpy.ops.preferences.addon_enable(module="bl_ext.blender_org.mmd_tools")
        bpy.ops.preferences.addon_enable(module="bl_ext.user_default.uma_addon")
        bpy.ops.mmd_tools.import_model(filepath=path)
    except Exception as error:
        block.append("  IMPORT FAILED: %s" % error)
        return block

    meshes = [obj for obj in bpy.context.scene.objects if obj.type == 'MESH']
    if not meshes:
        block.append("  no mesh imported")
        return block
    mesh = max(meshes, key=lambda obj: len(obj.data.vertices))
    keys = mesh.data.shape_keys
    from bl_ext.user_default.uma_addon.uma_addon.addons.uma_addon.operators import MorphDrivers
    roots = [obj for obj in bpy.context.scene.objects if getattr(obj, "mmd_root", None) is not None]
    tokens = ""
    for root in roots:
        tokens = MorphDrivers._comment(root.mmd_root) or ""
        if tokens:
            break
    block.append("  imported: %d mesh(es), %d material(s), %d morph(s), metadata tokens: %s"
                 % (len(meshes), count_materials(), len(keys.key_blocks) - 1 if keys else 0,
                    "yes" if ("export settings:" in tokens or "object morphs:" in tokens) else "no (legacy)"))

    scene = bpy.context.scene
    scene.uma_scene.shader = shader_id
    bpy.ops.object.select_all(action='DESELECT')
    mesh.select_set(True)
    bpy.context.view_layer.objects.active = mesh
    try:
        result = bpy.ops.uma.apply_shader()
        block.append("  shading: %s" % (result,))
    except Exception as error:
        block.append("  SHADING FAILED: %s" % error)
        block.append("  " + traceback.format_exc().strip().splitlines()[-1])
        return block

    decals, wet_filled, wet_mirrored, dirt_bound = [], 0, 0, 0
    drivers = 0
    for obj in bpy.context.scene.objects:
        if obj.type != 'MESH':
            continue
        for slot in obj.material_slots:
            material = slot.material
            if material is None or material.node_tree is None:
                continue
            tree = material.node_tree
            if material.name.endswith("[decal]"):
                maps = [(node.image.name if node.image else None, node.extension)
                        for node in tree.nodes if node.type == 'TEX_IMAGE']
                decals.append("%s %s" % (material.name, maps))
            for node in tree.nodes:
                if node.type != 'TEX_IMAGE' or node.image is None:
                    continue
                name = node.image.name or ""
                if "_wet" in name:
                    wet_filled += 1
                elif node.name.startswith("Wet "):
                    wet_mirrored += 1
                if node.name == "Dirt Map" and node.image is not None:
                    dirt_bound += 1
            if material.name.endswith("[eye]") and tree.animation_data is not None:
                drivers = max(drivers, len(tree.animation_data.drivers))

    block.append("  decals: %d (multiply using the shipped blush: %s)"
                 % (len(decals), "yes" if any("blush" in entry for entry in decals) else "no"))
    for entry in decals[:10]:
        block.append("    %s" % entry)
    block.append("  weather: %d wet image(s) bound, %d wet slot(s) mirrored, %d dirt map(s)"
                 % (wet_filled, wet_mirrored, dirt_bound))
    spin = [entry.split(" ")[0] for entry in decals if "CLIP" in entry]
    block.append("  spinning decals on CLIP: %s" % (spin or "none found"))
    spinning = sorted({entry.split(" ")[0] for entry in decals if "CLIP" in entry})
    block.append("  spinning decals on CLIP: %s" % (spinning or "none found"))
    block.append("  eye drivers: %d" % drivers)
    outlines = [obj.name for obj in bpy.context.scene.objects if obj.name.startswith("1001") and obj.type == 'MESH']
    block.append("  outline shell present: %s" % ("yes" if any(
        any(modifier.type == 'NODES' for modifier in obj.modifiers) for obj in bpy.context.scene.objects
        if obj.type == 'MESH') else "no"))
    return block


for path in files:
    lines.extend(report_one(path))
    lines.append("")

with open(out_path, "w", encoding="utf-8") as handle:
    handle.write("\n".join(lines) + "\n")
print("wrote %s (%d file(s))" % (out_path, len(files)))
