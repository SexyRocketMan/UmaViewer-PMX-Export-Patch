"""How many donors does a workflow produce, and are they all legitimate?

    blender --background --factory-startup --python Tools/verify_donor_count.py -- <out.txt> [<file.pmx>]

The user reported a third donor in a fresh file after fixing the split-selection bug, so this runs the two workflows
that can produce one, on the model they used, and reports every donor with its target:

* **flow A**: import the model twice, then one Shading run per copy (the flow the earlier reproduction used);
* **flow B**: import once, shade it, then **duplicate the whole character** and shade the duplicate - the duplicate
  arrives carrying its own copy of the donor, whose marker still names the *original* face, so the duplicate's own
  face gets a second donor and the stale one is left behind.

A donor is legitimate when its target exists, is not itself a donor, and (for the copy that the transfer uses) is
pointed at by a Data Transfer modifier. Anything else is counted and named.
"""
import sys

import bpy

ADDON = "bl_ext.user_default.uma_addon"
PMX_DEFAULT = r"C:\Users\user\Documents\umaviewer_exports\morphs\1001_nostream.pmx"

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]
out = argv[0]
pmx = argv[1] if len(argv) > 1 else PMX_DEFAULT

lines = []
failures = []


def say(text=""):
    lines.append(text)


def check(label, ok, detail=""):
    say("  [%s] %s%s" % ("PASS" if ok else "FAIL", label, (" - " + detail) if detail else ""))
    if not ok:
        failures.append(label)
    return ok


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.preferences.addon_enable(module="bl_ext.blender_org.mmd_tools")
    bpy.ops.preferences.addon_enable(module=ADDON)


def shade(obj):
    context = bpy.context
    for item in context.view_layer.objects:
        item.select_set(False)
    obj.select_set(True)
    context.view_layer.objects.active = obj
    return bpy.ops.uma.apply_shader()


def report(title):
    from bl_ext.user_default.uma_addon.uma_addon.addons.uma_addon.operators import FaceNormals
    donors = [obj for obj in bpy.data.objects if obj.get(FaceNormals.COPY_MARKER)]
    targets = {}
    for donor in donors:
        name = donor.get("uma_face_normals_for")
        targets.setdefault(name, []).append(donor)
    say()
    say("%s: %d donor(s)" % (title, len(donors)))
    for donor in donors:
        name = donor.get("uma_face_normals_for")
        target = bpy.data.objects.get(name) if name else None
        used_by = [obj.name for obj in bpy.data.objects
                   if obj.type == 'MESH' and any(modifier.type == 'DATA_TRANSFER'
                                                 and modifier.object is donor for modifier in obj.modifiers)]
        say("     %-52s target=%-46r exists=%-5s polygons=%5d used_by=%s"
            % (donor.name, name, target is not None, len(donor.data.polygons), used_by or "nobody"))
    duplicate_claims = {name: [donor.name for donor in group] for name, group in targets.items()
                        if len(group) > 1}
    unowned = [donor.name for donor in donors
               if not any(obj.type == 'MESH' and any(modifier.type == 'DATA_TRANSFER'
                                                     and modifier.object is donor for modifier in obj.modifiers)
                          for obj in bpy.data.objects)]
    whole_meshes = [(donor.name, len(donor.data.polygons)) for donor in donors
                    if "face" not in (donor.get("uma_face_normals_for") or "").lower()]
    check("%s: no two donors claim the same face" % title, not duplicate_claims, "%s" % duplicate_claims)
    check("%s: every donor is used by a transfer" % title, not unowned, "%s" % unowned)
    check("%s: no donor is a copy of a whole mesh" % title, not whole_meshes, "%s" % whole_meshes)
    return donors


say("=" * 100)
say("model: %s" % pmx)
say()
say("flow A: the model imported twice, one Shading run each")
reset()
bpy.ops.mmd_tools.import_model(filepath=pmx)
first = max([obj for obj in bpy.context.scene.objects if obj.type == 'MESH'], key=lambda item: len(item.data.vertices))
bpy.ops.mmd_tools.import_model(filepath=pmx)
bodies = sorted([obj for obj in bpy.context.scene.objects if obj.type == 'MESH'], key=lambda item: -len(item.data.vertices))
second = next(body for body in bodies if body.name != first.name)
say("     %r and %r" % (first.name, second.name))
shade(first)
shade(second)
report("flow A")

say()
say("flow B: shade one, duplicate the whole character, shade the duplicate")
reset()
bpy.ops.mmd_tools.import_model(filepath=pmx)
body = max([obj for obj in bpy.context.scene.objects if obj.type == 'MESH'], key=lambda item: len(item.data.vertices))
shade(body)
report("flow B, after the first run")
body = next(obj for obj in bpy.context.scene.objects if obj.name.endswith("_mesh"))
root = body
while root.parent is not None:
    root = root.parent
family = [obj for obj in bpy.context.scene.objects
          if obj is root or (obj.parent is not None and obj.parent.name == root.name)]
for obj in bpy.context.view_layer.objects:
    obj.select_set(obj in family)
bpy.context.view_layer.objects.active = root
before = {obj.name for obj in bpy.context.scene.objects}
result = bpy.ops.object.duplicate(linked=False)
say("     duplicated %d object(s): %s" % (len(family), result))
# by name difference, not by `select_get`: the duplicates are selected, but a wrapper re-fetched from the scene is not
# the same Python object and the selection flags are easier to get wrong than a set difference
fresh = [obj for obj in bpy.context.scene.objects if obj.name not in before]
copies = [obj for obj in fresh if obj.type == 'MESH']
say("     the duplicate's new objects: %s" % [obj.name for obj in fresh])
say("     the duplicate's meshes: %s" % [obj.name for obj in copies])
shade(max(copies, key=lambda item: len(item.data.vertices)))
report("flow B, after shading the duplicate")

say()
say("RESULT: %s" % ("all checks passed" if not failures else "FAILED - " + "; ".join(failures)))
with open(out, "w", encoding="utf-8") as handle:
    handle.write("\n".join(lines) + "\n")
print("wrote %s" % out)
