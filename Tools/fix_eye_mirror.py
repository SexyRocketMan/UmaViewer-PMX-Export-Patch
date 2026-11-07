"""Repair the eye control keys of a model refined by a buggy add-on version.

    blender -b --factory-startup -P Tools/fix_eye_mirror.py -- <in.blend> <out.blend> [--dry-run]

What is wrong in such a file
-----------------------------
`Refine Structure` builds four pairs of eye control keys: the game's own eye range morph (which
moves one eye) and a mirrored copy (which should move the *other* eye). The mirrored copy was
made with `bpy.ops.object.shape_key_mirror()`, which wants an exact mirrored twin for every
vertex and silently skips the ones it cannot pair - a skipped vertex keeps the *source* eye's
motion. On a model whose two eyes are not vertex-symmetric that leaves a handful of vertices of
the wrong eye in every mirrored key, so rotating one eye bone drags a piece of the other one.

What this does
--------------
Nothing is re-imported and the mesh is not split again: the file still has four intact source
keys, and the vertices they move are the two eyes. Each mirrored key is rebuilt from its source
key with `mirror_eye_key`, which pairs every vertex of one eye with the other one.

The eye `Eye_L`/`Eye_R` vertex groups are gone in such a file (the operator merged their weights
into `Head`), so the two eyes are taken from the four source keys: each source key moves exactly
one eye's vertices, and all four move the same set per side.
"""
import sys

import bpy

ADDON = "bl_ext.user_default.uma_addon"

# (source key, mirrored key) - the pairing `fix_eye_shapekeys` installs
PAIRS = [("Eye_R(R)", "Eye_L(L)"), ("Eye_L(R)", "Eye_R(L)"),
         ("Eye_R(D)", "Eye_L(D)"), ("Eye_L(U)", "Eye_R(U)")]
EPS = 1e-9

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]
in_path, out_path = argv[0], argv[1]
dry_run = "--dry-run" in argv

lines = []


def say(s=""):
    lines.append(s)
    print(s)


bpy.ops.wm.open_mainfile(filepath=in_path)

try:
    bpy.ops.preferences.addon_enable(module=ADDON)
except Exception as exc:  # noqa: BLE001
    raise SystemExit(f"the uma add-on {ADDON} has to be enabled (it provides mirror_eye_key): {exc}")

from bl_ext.user_default.uma_addon.uma_addon.addons.uma_addon.operators.AddonOperators import (  # noqa: E402
    mirror_eye_key)

host = None
for ob in bpy.context.scene.objects:
    if ob.type == 'MESH' and ob.data.shape_keys:
        names = ob.data.shape_keys.key_blocks.keys()
        if all(name in names for pair in PAIRS for name in pair):
            host = ob
            break
if host is None:
    raise SystemExit("no mesh carries the eight eye control keys (was this model refined at all?)")

kb = host.data.shape_keys.key_blocks
basis = kb[0]
verts = host.data.vertices
say(f"file    {in_path}")
say(f"mesh    {host.name!r}  vertices={len(verts)}  keys={len(kb)}  (written by Blender {tuple(bpy.data.version)})")


def moved(name):
    key = kb[name]
    return {i for i in range(len(verts)) if (key.data[i].co - basis.data[i].co).length > EPS}


def split(indices):
    """(left count, right count) of a vertex set, by the sign of x."""
    left = sum(1 for i in indices if verts[i].co.x > 0)
    return left, len(indices) - left


def one_sided(indices):
    left, right = split(indices)
    return bool(indices) and min(left, right) == 0


own = {name: moved(name) for pair in PAIRS for name in pair}
say("")
for pair in PAIRS:
    for name in pair:
        left, right = split(own[name])
        state = "one eye" if one_sided(own[name]) else "MIXED UP - both eyes"
        say(f"  {name:10} moves {len(own[name]):3d} verts (left {left:2d} / right {right:2d})  {state}")

# The two eyes, from the four source keys (each one is the whole of its own eye).
left_set, right_set = set(), set()
for source, _ in PAIRS:
    if not one_sided(own[source]):
        continue
    left, right = split(own[source])
    (left_set if left else right_set).update(own[source])
say("")
say(f"eye vertex sets from the source keys: left={len(left_set)} right={len(right_set)}")

rebuilt, already, skipped = [], [], []
for source, mirrored in PAIRS:
    source_left, _ = split(own[source])
    target_left, _ = split(own[mirrored])
    if not one_sided(own[source]):
        skipped.append(f"{mirrored}: its source {source} is itself mixed up")
        continue
    if not own[mirrored]:
        skipped.append(f"{mirrored}: key not found")
        continue
    opposite = (source_left > 0) != (target_left > 0)
    if one_sided(own[mirrored]) and opposite:
        already.append(mirrored)
        continue
    if dry_run:
        rebuilt.append(f"{mirrored} from {source}")
        continue
    # Seed the target with the source's shape - the state `fix_eye_shapekeys` hands to
    # `mirror_eye_key` - so the function mirrors the source eye and not the target's own
    # (mixed up) content.
    for i in range(len(verts)):
        kb[mirrored].data[i].co = kb[source].data[i].co
    pairs, considered, unpaired = mirror_eye_key(host, kb[mirrored], basis, left_set, right_set)
    note = f"{mirrored} from {source}: {len(pairs)} of {considered} vertices paired"
    if unpaired:
        note += f", {unpaired} had no partner on the other eye"
    rebuilt.append(note)

say("")
for note in rebuilt:
    say(f"  {'WOULD REBUILD' if dry_run else 'REBUILT'}  {note}")
for name in already:
    say(f"  OK          {name} already moves the other eye alone")
for note in skipped:
    say(f"  SKIPPED     {note}")

report_path = (in_path if dry_run else out_path) + ".fix-eye-mirror.txt"
if dry_run:
    say("")
    say("dry run: nothing written")
else:
    bpy.ops.wm.save_as_mainfile(filepath=out_path)
    say("")
    say(f"saved to {out_path}")

with open(report_path, "w", encoding="utf-8") as f:
    f.write("\n".join(lines) + "\n")
print(f"report: {report_path}")
