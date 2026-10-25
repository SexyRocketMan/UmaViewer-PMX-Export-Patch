"""Which way does each decal part face? The mirrored right-hand copies are the suspect.

    blender --background --factory-startup --python Tools/probe_decal_normals.py -- <out.txt> [<file.pmx>]

The report is "the right eye manga is darker than the left one, and it disappears in edit mode". Both eye manga slots
are the same four prefabs twice, and the right-hand instances are **mirrored** - which reverses their winding, so their
polygon normals point the other way. Normals are not cosmetic here: the exporter lifts every decal off the face along
its own normal (`docs/HANDOFF_ADVANCED_MORPHS.md`, "an offset is only clearance along the normal"), so a mirrored part
whose normals point *into* the head gets lifted *behind* the face - registered behind the eye it is drawn over, which
reads as darker, and (being behind the surface) disappears when the face is edited.

So this measures, per decal material: the part's polygon count, its average normal in world space, and whether that
normal points **away from the head** (positive dot with the direction from the head bone's position to the part).

The decals are collapsed onto a point inside the head at rest, where every quad has zero area and no normal at all, so
each deploy key is set to 1 first (`Manga_*`, `Tear_*`, `Cheek_Blush`).
"""
import sys

import bpy
from mathutils import Vector

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

mesh = max([obj for obj in bpy.context.scene.objects if obj.type == 'MESH'],
           key=lambda item: len(item.data.vertices))

# Deploy everything: a collapsed quad has no area, and therefore no normal. The deployed position of each vertex is
# computed from the key data (`basis + sum(value * (key - basis))`) rather than read off an evaluated mesh, because a
# decal cannot be found by material on this file's evaluated meshes at all - `to_mesh()` reports one `material_index`
# for every polygon (measured earlier, 17718 polygons, one index), and the material slots are the only thing that says
# which polygons are a decal.
keys = getattr(mesh.data, "shape_keys", None)
deployed_keys = []
positions = []
if keys is not None:
    basis = keys.key_blocks["Basis"].data
    positions = [list(vertex.co) for vertex in basis]
    for block in keys.key_blocks:
        if not (block.name.startswith(("Manga_", "Tear_")) or block.name == "Cheek_Blush"):
            continue
        block.value = 1.0
        deployed_keys.append(block.name)
        for index, point in enumerate(block.data):
            delta = point.co - basis[index].co
            positions[index][0] += delta.x
            positions[index][1] += delta.y
            positions[index][2] += delta.z
positions = [Vector(point) for point in positions]
say("%s - deployed %d key(s)" % (pmx, len(deployed_keys)))
say()

head = None
for obj in bpy.context.scene.objects:
    if obj.type == 'ARMATURE':
        bone = obj.data.bones.get("Head")
        if bone is not None:
            head = obj.matrix_world @ bone.head_local
            break
if head is None:
    head = Vector((0.0, 0.0, 0.0))
    say("no Head bone found: distances are measured from the origin")
else:
    say("Head bone at %s" % (tuple(round(value, 3) for value in head),))
say()

world = mesh.matrix_world
decal_names = MorphDrivers.decal_materials()


def deployed_corners(polygon):
    """The polygon's corners in *deployed* positions - Newell's normal, which is right for a non-planar n-gon too."""
    return [positions[vertex] for vertex in polygon.vertices]


def newell_normal(points):
    normal = Vector((0.0, 0.0, 0.0))
    for index, point in enumerate(points):
        following = points[(index + 1) % len(points)]
        normal.x += (point.y - following.y) * (point.z + following.z)
        normal.y += (point.z - following.z) * (point.x + following.x)
        normal.z += (point.x - following.x) * (point.y + following.y)
    return normal


def report(material, index):
    """(polygons, centre, average normal, dot with the direction away from the head) for one material slot."""
    polygons = [polygon for polygon in mesh.data.polygons if polygon.material_index == index]
    if not polygons:
        return polygons, None, None, None
    corner_sets = [deployed_corners(polygon) for polygon in polygons]
    centre = sum((world @ (sum(points, Vector((0.0, 0.0, 0.0))) / len(points))
                  for points in corner_sets), Vector((0.0, 0.0, 0.0))) / len(corner_sets)
    normals = Vector((0.0, 0.0, 0.0))
    for points in corner_sets:
        normals += world.to_3x3() @ newell_normal(points)
    normal = normals.normalized() if normals.length > 1e-9 else Vector((0.0, 0.0, 0.0))
    outward = centre - head
    outward = outward.normalized() if outward.length > 1e-9 else Vector((0.0, 0.0, 0.0))
    return polygons, centre, normal, normal.dot(outward)

say("%-34s %5s  %-28s %-28s %6s %s" % ("material", "polys", "centre", "avg normal", "dot", "verdict"))
for index, slot in enumerate(mesh.material_slots):
    material = slot.material
    if material is None:
        continue
    if not any(material.name == name or material.name.startswith(name + ".") for name in decal_names):
        continue
    polygons, centre, normal, dot = report(material, index)
    if not polygons:
        say("%-34s %5d  (no polygons on this mesh)" % (material.name, 0))
        continue
    verdict = "faces out" if dot > 0.0 else "FACES IN (behind the face)"
    say("%-34s %5d  %-28s %-28s %6.3f %s"
        % (material.name, len(polygons),
           tuple(round(value, 3) for value in centre), tuple(round(value, 3) for value in normal), dot, verdict))

say()
say("pairing, so the two eyes can be compared:")
for index, slot in enumerate(mesh.material_slots):
    material = slot.material
    if material is None or "emo_eye" not in material.name and "_mayu" not in material.name:
        continue
    polygons, centre, normal, dot = report(material, index)
    if not polygons:
        continue
    say("  %-32s x=%+.3f  normal=%s  dot=%+.3f" % (material.name, centre.x,
                                                   tuple(round(value, 3) for value in normal), dot))

with open(out, "w", encoding="utf-8") as handle:
    handle.write("\n".join(lines) + "\n")
print("wrote %s" % out)
