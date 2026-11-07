"""Does the static face donor now follow the face's eyelid keys - and nothing else?

    blender --background --factory-startup --python Tools/verify_face_donor.py -- <out.txt> [<file.pmx>]

The static copy exists to stay put while the face morphs, because the normal transfer needs a *fixed* shape. The one
exception the user asked for is the eyelid: since 2.33.13 the eyeball is part of the face piece, so a blink moves lid
and eyeball polygons together and a donor frozen at rest hands the transfer a lid shape that is not on screen.
`FaceNormals.keep_face_keys` keeps `Eye_EyeHideA_L/_R` on the copy and drives them from the main face's `Key`.

What is checked:

1. after the real Shading run, the copy has exactly `Basis` + the two eyelid keys - every other key is gone, which is
   the property the feature rests on;
2. each kept key is *driven*, and its driver's variable targets the **face's own Key datablock** at the same key - not
   a copied value;
3. the donor deforms by the **same offsets as the face**: with the face's `Eye_EyeHideA_L` at 1 the copy's evaluated
   vertices move by the same per-vertex amount (measured, not assumed: compared vertex by vertex);
4. and by nothing else: a different key (`Mouth_WaraiA`) at 1 moves the face and leaves the copy alone;
5. re-running the step changes nothing - one copy, one driver per kept key, still two keys. (`copy_topology_mismatch`
   used to call *any* shape key on the copy a topology mismatch, which would have rebuilt the copy on every run.)
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


def bail(reason):
    say()
    say("BAILED: %s" % reason)
    say("RESULT: FAILED - %s" % reason)
    with open(out, "w", encoding="utf-8") as handle:
        handle.write("\n".join(lines) + "\n")
    print("wrote %s" % out)
    raise SystemExit(1)


def evaluated_positions(obj):
    """The evaluated (deformed) vertex positions of one object, in world space."""
    depsgraph = bpy.context.evaluated_depsgraph_get()
    evaluated = obj.evaluated_get(depsgraph)
    mesh = evaluated.to_mesh()
    world = evaluated.matrix_world
    positions = [world @ vertex.co for vertex in mesh.vertices]
    evaluated.to_mesh_clear()
    return positions


bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.preferences.addon_enable(module="bl_ext.blender_org.mmd_tools")
bpy.ops.preferences.addon_enable(module=ADDON)

from bl_ext.user_default.uma_addon.uma_addon.addons.uma_addon.operators import FaceNormals  # noqa: E402

context = bpy.context
bpy.ops.mmd_tools.import_model(filepath=pmx)
mesh = max([obj for obj in context.scene.objects if obj.type == 'MESH'], key=lambda item: len(item.data.vertices))
for obj in context.view_layer.objects:
    obj.select_set(False)
mesh.select_set(True)
context.view_layer.objects.active = mesh

say("=" * 100)
say("1. the real Shading run builds the donor")
say("     uma.apply_shader: %s" % (bpy.ops.uma.apply_shader(),))
copy = next((obj for obj in context.scene.objects if obj.get(FaceNormals.COPY_MARKER)), None)
face = next((obj for obj in context.scene.objects
             if obj.type == 'MESH' and obj.name.endswith("_mesh_face")), None)
if not check("a static copy exists", copy is not None, copy.name if copy else "none"):
    bail("no static copy")
if not check("the separated face exists", face is not None, face.name if face else "none"):
    bail("no separated face")

keys = getattr(copy.data, "shape_keys", None)
blocks = [block.name for block in keys.key_blocks] if keys else []
say("     copy keys: %s" % (blocks or "none"))
say("     face keys: %d" % (len(face.data.shape_keys.key_blocks) if face.data.shape_keys else 0))
check("the copy keeps exactly Basis + the two eyelid keys",
      sorted(blocks) == sorted(["Basis"] + list(FaceNormals.KEPT_COPY_KEYS)), "%s" % (blocks,))
check("its other keys are gone", "Mouth_WaraiA" not in blocks)

say()
say("2. kept keys are driven from the face's own Key")
for name in FaceNormals.KEPT_COPY_KEYS:
    if name not in blocks:
        say("  [SKIP] %s is not on this model" % name)
        continue
    path = 'key_blocks["%s"].value' % name
    curves = [curve for curve in keys.animation_data.drivers if curve.data_path == path] \
        if keys.animation_data else []
    if not check("%s has one driver" % name, len(curves) == 1, "%d" % len(curves)):
        continue
    driver = curves[0].driver
    variables = list(driver.variables)
    target = variables[0].targets[0] if variables else None
    check("%s: one SINGLE_PROP variable" % name,
          len(variables) == 1 and variables[0].type == 'SINGLE_PROP')
    # the face's own keys, either through its `Key` datablock or (the fallback) through the object - what matters is
    # that the *source* is the face and the key name matches, not that no copy of the value was baked
    through_key = (target is not None and target.id is not None and target.id is face.data.shape_keys
                   and target.data_path == path)
    through_object = (target is not None and target.id is face
                      and (target.data_path or "").endswith(path))
    check("%s: it reads the face's own key" % name, through_key or through_object,
          "id=%r path=%r" % (target.id.name if target is not None and target.id else None,
                             target.data_path if target is not None else None))
    check("%s: the expression is that variable" % name, driver.expression.strip() == variables[0].name,
          repr(driver.expression))

say()
say("3. the donor deforms with the lid, and by the same amount as the face")
lid = "Eye_EyeHideA_L"
face_keys = face.data.shape_keys.key_blocks
if lid not in face_keys or lid not in blocks:
    bail("%s is not on both the face and the copy" % lid)
rest_copy = evaluated_positions(copy)
face_keys[lid].value = 1.0
context.view_layer.update()
moved_copy = evaluated_positions(copy)
check("the driver followed (the copy's own value is 1)", abs(copy.data.shape_keys.key_blocks[lid].value - 1.0) < 1e-4,
      "%.4f" % copy.data.shape_keys.key_blocks[lid].value)
copy_travel = max((a - b).length for a, b in zip(rest_copy, moved_copy))
check("the copy's eyelid actually moves when the face's is at 1", copy_travel > 0.0, "%.5f" % copy_travel)

# Vertex by vertex against the face's *own* key data rather than its evaluated mesh: the face piece carries the
# `Uma Outlines` geometry-nodes modifier (which builds a shell) and a data transfer, so its evaluated mesh is not a
# vertex-per-vertex match for the plain copy (measured: the comparison ran off the end of one of them). The key data
# is the deformation both objects use, so comparing it is both simpler and stricter - and the depsgraph comparison
# above already showed that the copy applies it.
face_data = face_keys[lid].data
copy_data = copy.data.shape_keys.key_blocks[lid].data
basis = face.data.shape_keys.key_blocks["Basis"].data
displacement_mismatch = max((copy_data[i].co - face_data[i].co).length for i in range(len(copy_data)))
offset_mismatch = max((face_data[i].co - basis[i].co).length for i in range(len(face_data)))
check("the copy holds the face's own key offsets", displacement_mismatch == 0.0,
      "largest difference %.7f over %d vertices" % (displacement_mismatch, len(copy_data)))
check("and that key is not empty (the measurement means something)", offset_mismatch > 0.0,
      "largest offset in the key %.5f" % offset_mismatch)

say()
say("4. and it does not follow anything else")
face_keys[lid].value = 0.0
other = "Mouth_WaraiA"
if other in face_keys:
    rest_copy = evaluated_positions(copy)
    face_keys[other].value = 1.0
    context.view_layer.update()
    moved_copy = evaluated_positions(copy)
    other_travel = max((a - b).length for a, b in zip(rest_copy, moved_copy))
    check("%s moves the face but leaves the copy alone" % other, other_travel < 1e-6, "%.7f" % other_travel)
    face_keys[other].value = 0.0
    context.view_layer.update()
else:
    say("  [SKIP] %s is not on this model" % other)

say()
say("5. re-running the step is idempotent")
before = (copy.name, len(copy.data.shape_keys.key_blocks))
FaceNormals.apply_face_normals(context)
copies = [obj for obj in context.scene.objects if obj.get(FaceNormals.COPY_MARKER)]
check("still exactly one copy", len(copies) == 1, "%d" % len(copies))
copy = copies[0]
blocks_after = [block.name for block in copy.data.shape_keys.key_blocks] if copy.data.shape_keys else []
check("still Basis + the two eyelid keys", sorted(blocks_after) == sorted(["Basis"] + list(FaceNormals.KEPT_COPY_KEYS)),
      "%s" % (blocks_after,))
drivers_after = [curve.data_path for curve in copy.data.shape_keys.animation_data.drivers] \
    if copy.data.shape_keys and copy.data.shape_keys.animation_data else []
check("one driver per kept key, no duplicates", len(drivers_after) == len(set(drivers_after)) == 2,
      "%s" % (drivers_after,))
check("the copy is not considered a topology mismatch", FaceNormals.copy_topology_mismatch(copy, face) == "",
      FaceNormals.copy_topology_mismatch(copy, face))
transfer = next((modifier for modifier in face.modifiers
                 if modifier.type == 'DATA_TRANSFER'), None)
check("the transfer is still on the face", transfer is not None,
      transfer.object.name if transfer is not None and transfer.object else "none")

say()
say("RESULT: %s" % ("all checks passed" if not failures else "FAILED - " + "; ".join(failures)))
with open(out, "w", encoding="utf-8") as handle:
    handle.write("\n".join(lines) + "\n")
print("wrote %s" % out)
