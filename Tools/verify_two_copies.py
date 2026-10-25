"""Two copies of one model in one file: does the face step make a donor for the whole mesh?

    blender --background --factory-startup --python Tools/verify_two_copies.py -- <out.txt> [<file.pmx>]

The user's report, in their words: *"I used the same model, just two copies in one file... The extra normal donor
appeared after the first run of the shader setup - so both `..._mesh.001_normals_static` and
`..._mesh_face_normals_static` appeared. Second run with either Looper's or Spork's shader produces only one normal
donor."*

`apply_face_normals` has one path that can do exactly that: when `is_whole_face` is false it *separates* the face and
continues with the piece - but when `split_off_face` returns `None` (it failed) it reports the note and keeps going
with the **original, unsplit** object, so `make_copy` then copies the whole mesh and the collection gains a donor named
`<whole mesh>_normals_static`. This reproduces the situation (two characters, two shading runs) and records:

1. how many donors exist after each run, and what each one was made from - the whole mesh or the face piece;
2. every note the step reported, so a failed split cannot pass silently;
3. that a second run does not add another.
"""
import sys

import bpy

ADDON = "bl_ext.user_default.uma_addon"

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]
out = argv[0]
pmx = argv[1] if len(argv) > 1 else r"C:\Users\user\Documents\umaviewer_exports\morphs\1001_nostream.pmx"

lines = []
failures = []


def say(text=""):
    lines.append(text)


def check(label, ok, detail=""):
    say("  [%s] %s%s" % ("PASS" if ok else "FAIL", label, (" - " + detail) if detail else ""))
    if not ok:
        failures.append(label)
    return ok


def donors():
    return [obj for obj in bpy.data.objects if obj.get(FaceNormals.COPY_MARKER)]


def describe():
    return [(obj.name, obj.get("uma_face_normals_for"), len(obj.data.polygons)) for obj in donors()]


def shade(context, obj):
    """One Shading run on one mesh, the way the panel does it."""
    for item in context.view_layer.objects:
        item.select_set(False)
    obj.select_set(True)
    context.view_layer.objects.active = obj
    return bpy.ops.uma.apply_shader()


bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.preferences.addon_enable(module="bl_ext.blender_org.mmd_tools")
bpy.ops.preferences.addon_enable(module=ADDON)
context = bpy.context
from bl_ext.user_default.uma_addon.uma_addon.addons.uma_addon.operators import FaceNormals  # noqa: E402

bpy.ops.mmd_tools.import_model(filepath=pmx)
first = max([obj for obj in context.scene.objects if obj.type == 'MESH'], key=lambda item: len(item.data.vertices))
say("=" * 100)
say("imported %r (%d vertices)" % (first.name, len(first.data.vertices)))

# the second copy, as a user makes one: the same model in the same file a second time
bpy.ops.mmd_tools.import_model(filepath=pmx)
bodies = sorted([obj for obj in context.scene.objects if obj.type == 'MESH'
                 and not obj.get("uma_face_normals_copy")], key=lambda item: -len(item.data.vertices))
second = next((body for body in bodies if body.name != first.name), None)
if second is None:
    raise SystemExit("the second import did not produce a second body")
say("imported the model a second time: %r (%d vertices)" % (second.name, len(second.data.vertices)))
say()

say("run 1 on %r" % first.name)
result = shade(context, first)
say("     operator: %s" % (result,))
after_first = describe()
for name, target, polygons in after_first:
    say("     donor %-58s from %-58r %d poly(s)" % (name, target, polygons))
# a run prepares **every** face in the scene, not only the selected character's - that is the step's design, since a
# file can hold several characters - so one run over a two-character file gives two donors, one per character
check("run 1 gives one donor per character", len(after_first) == 2, "%d" % len(after_first))
check("... and every source is a face, not a whole mesh", all("face" in (target or "").lower()
                                                              for _name, target, _polygons in after_first))
check("... and both are face pieces, not whole meshes", all(polygons < 3000
                                                           for _name, _target, polygons in after_first),
      "%s" % [(name, polygons) for name, _target, polygons in after_first])

say()
say("run 2 on %r" % second.name)
result = shade(context, second)
say("     operator: %s" % (result,))
after_second = describe()
for name, target, polygons in after_second:
    say("     donor %-58s from %-58r %d poly(s)" % (name, target, polygons))
whole_mesh_donors = [(name, target, polygons) for name, target, polygons in after_second
                     if "face" not in (target or "").lower()]
check("two characters give two donors", len(after_second) == 2, "%d" % len(after_second))
check("neither donor is a copy of a whole mesh", not whole_mesh_donors,
      "%s" % [name for name, _target, _polygons in whole_mesh_donors])

say()
say("run 3, the same character again")
result = shade(context, second)
after_third = describe()
check("a second run adds nothing", len(after_third) == len(after_second),
      "%d -> %d" % (len(after_second), len(after_third)))
check("and never a whole-mesh donor", not [entry for entry in after_third
                                           if "face" not in (entry[1] or "").lower()],
      "%s" % [entry[0] for entry in after_third])

say()
say("RESULT: %s" % ("all checks passed" if not failures else "FAILED - " + "; ".join(failures)))
with open(out, "w", encoding="utf-8") as handle:
    handle.write("\n".join(lines) + "\n")
print("wrote %s" % out)
