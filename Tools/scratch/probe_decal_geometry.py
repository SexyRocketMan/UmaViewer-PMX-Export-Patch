"""How much geometry does each decal have, and what does the exporter think its blend is?

    blender --background --factory-startup --python Tools/probe_decal_geometry.py -- <out.txt> [<file.pmx>]

Two questions, both about whether a "render in front" trick can work on the decals at all:

* **vertex count per decal material** - the eyebrow trick that draws a part in front of the hair is *vertex*
  displacement along the view vector, and it was measured then to do nothing on a 4-vertex plane; a decal that is one
  quad cannot be pushed forward that way.
* the material's comment tokens, so what the exporter said about the part is next to its geometry.
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

decal_names = MorphDrivers.decal_materials()
mesh = max([obj for obj in bpy.context.scene.objects if obj.type == 'MESH'],
           key=lambda item: len(item.data.vertices))
say("%s - %d decal name(s), body mesh %r" % (pmx, len(decal_names), mesh.name))
say()
say("%-34s %6s %6s  %s" % ("material", "polys", "verts", "tokens"))
for index, slot in enumerate(mesh.material_slots):
    material = slot.material
    if material is None:
        continue
    if not any(material.name == name or material.name.startswith(name + ".")
               for name in decal_names):
        continue
    polygons = [polygon for polygon in mesh.data.polygons if polygon.material_index == index]
    vertices = set()
    for polygon in polygons:
        vertices.update(polygon.vertices)
    comment = getattr(getattr(material, "mmd_material", None), "comment", "") or ""
    tokens = [token for token in comment.split() if "=" in token]
    say("%-34s %6d %6d  %s" % (material.name, len(polygons), len(vertices), " ".join(tokens) or "none"))
say()
say("for comparison, a body material and the brows:")
for index, slot in enumerate(mesh.material_slots):
    material = slot.material
    if material is None:
        continue
    if not (material.name.endswith("_mayu") or material.name.endswith("_hair")):
        continue
    polygons = [polygon for polygon in mesh.data.polygons if polygon.material_index == index]
    vertices = set()
    for polygon in polygons:
        vertices.update(polygon.vertices)
    say("%-34s %6d %6d" % (material.name, len(polygons), len(vertices)))

with open(out, "w", encoding="utf-8") as handle:
    handle.write("\n".join(lines) + "\n")
print("wrote %s" % out)
