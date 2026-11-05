"""Why a whole mesh became a face object: every mesh in a file, against the tests `faces_in`/`is_whole_face` use.

    blender --background --factory-startup --python Tools/probe_face_targets.py -- <file.blend> <out.txt>

The report is a file with two copies of one model in it, where the face step produced a donor from the **whole mesh**
of one copy (`..._mesh.001_normals_static`) as well as the correct one. `FaceNormals.faces_in` accepts an object when
its name says face *or* when every polygon it has belongs to a face material, so a whole mesh only gets in on the
second test - and the point of this probe is to see which slots and polygons it actually had.
"""
import sys

import bpy

ADDON = "bl_ext.user_default.uma_addon"

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]
path, out = argv[0], argv[1]

lines = []


def say(text=""):
    lines.append(text)


bpy.ops.preferences.addon_enable(module=ADDON)
bpy.ops.wm.open_mainfile(filepath=path)

from bl_ext.user_default.uma_addon.uma_addon.addons.uma_addon.operators import FaceNormals  # noqa: E402

say("file: %s" % path)
say()
say("objects:")
for obj in bpy.data.objects:
    if obj.type != 'MESH':
        continue
    polygons = len(obj.data.polygons)
    face_polygons = FaceNormals.face_polygon_count(obj)
    slots = [(index, slot.material.name if slot.material else None) for index, slot in enumerate(obj.material_slots)]
    face_slots = [entry for entry in slots if entry[1] and FaceNormals.is_face_material(
        bpy.data.materials.get(entry[1]))]
    say("  %-58s polys=%5d face_polys=%5d donor=%s"
        % (obj.name, polygons, face_polygons, bool(obj.get(FaceNormals.COPY_MARKER))))
    say("       is_whole_face=%-5s named_face=%-5s parent=%r"
        % (FaceNormals.is_whole_face(obj),
           ("face" in obj.name.lower() or "mayu" in obj.name.lower()),
           obj.parent.name if obj.parent else None))
    say("       face slots: %s" % (face_slots or "none"))
    say("       all slots: %s" % [entry[1] for entry in slots][:10])
    say("       markers: %s" % sorted(key for key in obj.keys() if "uma" in key.lower() or "normal" in key.lower()))
    say()

say("what `faces_in` would pick, with its reasons:")
for obj, reason in FaceNormals.faces_in(bpy.context):
    say("  %-58s %s" % (obj.name, reason))

say()
say("existing donors and what each was made for:")
for obj in bpy.data.objects:
    if obj.get(FaceNormals.COPY_MARKER):
        target = obj.get("uma_face_normals_for")
        say("  %-58s for %r (exists: %s)" % (obj.name, target, target in bpy.data.objects))
        say("       modifiers: %s, transfer on it: %s"
            % ([modifier.type for modifier in obj.modifiers], obj.data.shape_keys is not None))

with open(out, "w", encoding="utf-8") as handle:
    handle.write("\n".join(lines) + "\n")
print("wrote %s (%d lines)" % (out, len(lines)))
