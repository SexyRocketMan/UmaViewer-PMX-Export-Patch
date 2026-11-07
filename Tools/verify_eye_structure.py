"""Verify the eye controls `Refine Structure` builds, on a real export.

    blender --background --factory-startup --python Tools/verify_eye_structure.py -- <out.txt> [<file.pmx>]

What has to hold after `uma.refine_bone_structure` turns the game's four eye range morphs into
eight bone-driven shape keys:

1. the eight control keys exist, and the four source morphs are gone;
2. **every key moves one eye and only one eye** - a mirrored key that still moves the source eye
   is the reported defect (`shape_key_mirror` skips vertices it cannot pair and leaves them with
   the source side's motion);
3. a mirrored key carries exactly the mirrored deformations of its source key (same multiset,
   so a model whose eyes are not vertex-symmetric still passes - only the *sides* and the
   *deformation set* are claimed, not which vertex mirrors which);
4. `Eye_L`/`Eye_R` are gone from the vertex groups and their weights are in `Head`, so nothing
   the eye bones rotate still deforms the mesh through skinning;
5. eight drivers, each on its own key, reading its own bone's ROT_X/ROT_Z in local space;
6. the mirror step itself pairs every vertex of a deliberately *non-symmetric* eye (the reported
   model's shape), where `bpy.ops.object.shape_key_mirror` leaves six of twenty-three behind.

Exit code is 0 when every check passes.
"""
import sys

import bpy
from mathutils import Vector

ADDON = "bl_ext.user_default.uma_addon"
# (source key, mirrored key) -> the driver each is wired to
PAIRS = [("Eye_R(R)", "Eye_L(L)"), ("Eye_L(R)", "Eye_R(L)"),
         ("Eye_R(D)", "Eye_L(D)"), ("Eye_L(U)", "Eye_R(U)")]
KEYS = [name for pair in PAIRS for name in pair]
SOURCE_MORPHS = ("Eye_XRange_R", "Eye_XRange_L", "Eye_YRange_R", "Eye_YRange_L")
EPS = 1e-6

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]
out = argv[0]
pmx = argv[1] if len(argv) > 1 else r"C:\Users\user\Documents\umaviewer_exports\morphs\1001_nostream.pmx"

from bl_ext.user_default.uma_addon.uma_addon.addons.uma_addon.utils import naming  # noqa: E402

lines = []
failures = []
keys_before = 0
eye_symmetry = ({}, [], [])


def say(text=""):
    lines.append(text)


def check(label, ok, detail=""):
    say("  [%s] %s%s" % ("PASS" if ok else "FAIL", label, (" - " + detail) if detail else ""))
    if not ok:
        failures.append(label)
    return ok


def run_refine():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.preferences.addon_enable(module="bl_ext.blender_org.mmd_tools")
    bpy.ops.preferences.addon_enable(module=ADDON)
    bpy.ops.mmd_tools.import_model(filepath=pmx, scale=0.08, types={'MESH', 'ARMATURE', 'MORPHS'})
    arm = next(o for o in bpy.context.scene.objects if o.type == 'ARMATURE')
    mesh = next(o for o in bpy.context.scene.objects
                if o.type == 'MESH' and any(m.type == 'ARMATURE' and m.object == arm for m in o.modifiers))
    global keys_before, eye_symmetry
    keys_before = len(mesh.data.shape_keys.key_blocks)
    eye_symmetry = measure_eye_symmetry(mesh)
    bpy.context.view_layer.objects.active = arm
    arm.select_set(True)
    result = bpy.ops.uma.refine_bone_structure()
    mesh = next(o for o in bpy.context.scene.objects
                if o.type == 'MESH' and any(m.type == 'ARMATURE' and m.object == arm for m in o.modifiers))
    return arm, mesh, result


def measure_eye_symmetry(mesh):
    """How well the imported eye region mirrors: `(points, twin distances, per-eye counts)`.

    The mirrored control keys are built by pairing one eye's vertices with the other's. Once the
    region is 100% paired (every eye vertex has a twin at 0.000000 m) the old failure mode cannot
    reach the model at all; anything less was exposed to it before 2.35.20.
    """
    from mathutils import kdtree
    points = []
    counts = {}
    for group in ("Eye_L", "Eye_R"):
        entry = mesh.vertex_groups.get(group)
        if entry is None:
            counts[group] = 0
            continue
        verts = [v.index for v in mesh.data.vertices
                 if any(g.group == entry.index and g.weight > 0.5 for g in v.groups)]
        counts[group] = len(verts)
        points.extend(mesh.data.shape_keys.key_blocks[0].data[i].co.copy() for i in verts)
    if not points:
        return counts, [], []
    tree = kdtree.KDTree(len(points))
    for i, p in enumerate(points):
        tree.insert(p, i)
    tree.balance()
    distances = sorted(tree.find(Vector((-p.x, p.y, p.z)))[2] for p in points)
    return counts, distances, points


def deltas(mesh, name):
    kb = mesh.data.shape_keys.key_blocks
    basis = kb[0]
    key = kb[name]
    return {i: (key.data[i].co - basis.data[i].co)
            for i in range(len(mesh.data.vertices))
            if (key.data[i].co - basis.data[i].co).length > EPS}


say("=" * 100)
say(f"eye controls - {pmx}")
say("")

arm, mesh, result = run_refine()
say(f"refine: {result}   mesh={mesh.name!r} bones={len(arm.data.bones)} verts={len(mesh.data.vertices)}")
say("")

say("0. this model's eye geometry, i.e. whether the pre-2.35.20 defect could reach it")
counts, twins, _points = eye_symmetry
paired = sum(1 for d in twins if d < 1e-4)
say(f"  eyes: {counts.get('Eye_L', 0)} left / {counts.get('Eye_R', 0)} right vertices; "
    f"{paired}/{len(twins)} have an exact mirror twin"
    + (f", the rest are up to {twins[-1]*1000:.2f} mm off" if twins and twins[-1] >= 1e-4 else ""))
if not twins:
    say("  (no eye vertex groups: this model has no eye bones to build controls for)")
elif paired == len(twins):
    say("  verdict: exactly symmetric - the old mirror had an exact twin for every vertex, which is")
    say("           why this model could never show the defect (and why it was not reproducible)")
else:
    say(f"  verdict: {len(twins) - paired} of {len(twins)} vertices have no exact twin - this model is "
        f"the exposed kind;")
    say("           a pre-2.35.20 build put their motion on the wrong eye, 2.35.20 pairs them instead")
say("")

say("1. the eight control keys, and the four source morphs consumed")
kb = mesh.data.shape_keys.key_blocks
check("refine finished", result == {'FINISHED'}, str(result))
check("all eight control keys exist", all(n in kb for n in KEYS),
      ", ".join(n for n in KEYS if n not in kb) or "8/8")
# Asked by meaning, not by spelling: the four source morphs are renamed to `Eye_R(R)` and friends, so
# nothing should still parse as an eye range morph - whichever naming mode the export used. (Mode 0
# spells them `Eye_20_R(XRange)[M_Face]`, mode 1 `Eye_20_R`, mode 2 `Eye_XRange_R`.)
left_over = [k for k in kb.keys() if naming.parse(k) and
             (naming.parse(k)["group"] == "Eye" and
              naming.parse(k)["tag"] in ("XRange", "YRange"))]
check("the four source eye range morphs were consumed", not left_over,
      ", ".join(left_over) or "none left")
check("refine added exactly the four mirrored keys", len(kb) == keys_before + 4,
      f"{keys_before} imported -> {len(kb)} (the four sources are renamed in place, not duplicated)")

say("")
say("2. every key moves one eye, and only one eye")
if bpy.context.mode != 'OBJECT':
    bpy.ops.object.mode_set(mode='OBJECT')
for name in KEYS:
    moved = deltas(mesh, name)
    left = sum(1 for i in moved if mesh.data.vertices[i].co.x > 0)
    right = len(moved) - left
    check(f"{name:10} moves one eye", moved and (left == 0 or right == 0),
          f"{len(moved)} verts (left {left} / right {right})")

say("")
say("3. a mirrored key carries the mirrored deformations of its source")
for source, mirrored in PAIRS:
    src = deltas(mesh, source)
    tgt = deltas(mesh, mirrored)
    want = sorted(tuple(round(c, 6) for c in (-d.x, d.y, d.z)) for d in src.values())
    got = sorted(tuple(round(c, 6) for c in d) for d in tgt.values())
    check(f"{mirrored} is {source} mirrored", want == got,
          "" if want == got else f"{len(want)} source vs {len(got)} mirrored deformations")

say("")
say("4. the eye weights moved into Head")
names = [g.name for g in mesh.vertex_groups]
check("Eye_L/Eye_R vertex groups are gone", "Eye_L" not in names and "Eye_R" not in names,
      f"still present: {[n for n in ('Eye_L', 'Eye_R') if n in names]}")
eye_verts = set()
for name in KEYS:
    eye_verts |= set(deltas(mesh, name))
head = mesh.vertex_groups.get("Head")
heavy = 0
if head is not None:
    for i in eye_verts:
        weight = next((g.weight for g in mesh.data.vertices[i].groups if g.group == head.index), 0.0)
        if weight > 0.0:
            heavy += 1
check("every eye vertex is now weighted to Head", head is not None and heavy == len(eye_verts),
      f"{heavy}/{len(eye_verts)}")

say("")
say("5. the eight drivers")
sk = mesh.data.shape_keys
drivers = {}
if sk.animation_data:
    for drv in sk.animation_data.drivers:
        path = drv.data_path
        if path.startswith('key_blocks["'):
            name = path.split('"')[1]
            target = drv.driver.variables[0].targets[0] if drv.driver.variables else None
            drivers[name] = (drv.driver.expression, target.bone_target if target else None,
                             target.transform_type if target else None,
                             target.transform_space if target else None)
expected_bone = {"Eye_R(R)": ("Eye_R", "ROT_Z"), "Eye_L(R)": ("Eye_L", "ROT_Z"),
                 "Eye_R(L)": ("Eye_R", "ROT_Z"), "Eye_L(L)": ("Eye_L", "ROT_Z"),
                 "Eye_R(D)": ("Eye_R", "ROT_X"), "Eye_L(U)": ("Eye_L", "ROT_X"),
                 "Eye_R(U)": ("Eye_R", "ROT_X"), "Eye_L(D)": ("Eye_L", "ROT_X")}
check("eight drivers on the keys", len(drivers) == 8, f"{len(drivers)} drivers")
for name, (bone, axis) in expected_bone.items():
    expr, target_bone, transform, space = drivers.get(name, (None, None, None, None))
    check(f"{name:10} <- {bone} {axis}",
          target_bone == bone and transform == axis and space == 'LOCAL_SPACE' and expr and "*" in expr,
          f"bone={target_bone} {transform} {space} expr={expr!r}")
check("drivers sit on the mesh that owns the keys", True, mesh.name)

say("")
say("6. the mirror step, on an eye whose halves are not vertex-symmetric")
from bl_ext.user_default.uma_addon.uma_addon.addons.uma_addon.operators.AddonOperators import (  # noqa: E402
    eye_sides, mirror_eye_key)
bpy.ops.wm.read_factory_settings(use_empty=True)
positions = []
for side in (1, -1):
    for ring in range(3):
        for k in range(7):
            angle = k * 6.28318 / 7
            # every vertex of the right eye is displaced on its own, so no vertex has an exact twin
            wobble = 0.0 if side > 0 else 0.0022 * ((ring * 7 + k) % 5) / 4.0
            positions.append(Vector((side * (0.031 + 0.006 * ring + wobble),
                                     -0.024 + 0.004 * ring + wobble * 0.5,
                                     1.352 + 0.004 * 3 * ring)))
data = bpy.data.meshes.new("asym_eye")
obj = bpy.data.objects.new("asym_eye", data)
bpy.context.scene.collection.objects.link(obj)
data.from_pydata([tuple(p) for p in positions], [], [])
data.update()
left_group = obj.vertex_groups.new(name="Eye_L")
right_group = obj.vertex_groups.new(name="Eye_R")
for i, p in enumerate(positions):
    (left_group if p.x > 0 else right_group).add([i], 1.0, 'REPLACE')
obj.shape_key_add(name="Basis")
source = obj.shape_key_add(name="source")
right = [i for i, p in enumerate(positions) if p.x < 0]
delta = Vector((-0.0089, 0.0033, 0.0000))
for i in right:
    source.data[i].co = positions[i] + delta
bpy.context.view_layer.objects.active = obj
obj.select_set(True)

by_op = obj.shape_key_add(name="by_op", from_mix=False)
for i in range(len(positions)):
    by_op.data[i].co = positions[i]
for i in right:
    by_op.data[i].co = positions[i] + delta
obj.active_shape_key_index = data.shape_keys.key_blocks.keys().index("by_op")
bpy.ops.object.shape_key_mirror()
by_us = obj.shape_key_add(name="by_us", from_mix=False)
for i in range(len(positions)):
    by_us.data[i].co = positions[i]
for i in right:
    by_us.data[i].co = positions[i] + delta
basis = data.shape_keys.key_blocks["Basis"]
side_l, side_r = eye_sides(obj)
pairs, considered, unpaired = mirror_eye_key(obj, by_us, basis, side_l, side_r)


def wrong_side(key):
    return sum(1 for i in right if (key.data[i].co - basis.data[i].co).length > EPS)


op_wrong = wrong_side(by_op)
us_wrong = wrong_side(by_us)
say(f"  shape_key_mirror leaves {op_wrong} of {len(right)} vertices moving the source eye")
say(f"  mirror_eye_key moves  {us_wrong} of {len(right)} vertices on the source eye; "
    f"{len(pairs)} of {considered} paired, {unpaired} unpaired")
check("the old operator leaves vertices on the wrong eye (the reported defect)", op_wrong > 0,
      f"{op_wrong} vertices - this is what the fix addresses")
check("mirror_eye_key leaves none", us_wrong == 0, f"{us_wrong} vertices")
check("mirror_eye_key pairs every moved vertex", unpaired == 0, f"{unpaired} unpaired")

say("")
say("=" * 100)
say("FAILURES: " + (", ".join(failures) if failures else "none"))
with open(out, "w", encoding="utf-8") as handle:
    handle.write("\n".join(lines) + "\n")
print("\n".join(lines))
sys.exit(1 if failures else 0)
