"""Does the plain-bone skirt master work, on a rig with no rigify in it?

    blender -b --factory-startup -P Tools/verify_skirt_master_plain.py -- <file.pmx> <out.txt>

The reference rig this feature comes from has no rigify at all: ordinary deform bones per panel, a `COPY_ROTATION`
cascade, and the panel root reading `skirt_master`. This checks that the add-on's plain path reproduces the
mechanism on a model that never had a chain built:

1. the operator finds skirt panels among the model's bones (through the rules table's skirt-family rule, so 1001's
   `Sp_Hi_MSkirt0_*` naming works as well as the reference's `Skirt_*`);
2. it adds **no rigify bones** - no `DEF-`/`MCH-`/`ORG-`, no rigify per-panel `_master`;
3. every panel root carries the two channels (link + lift), and the master carries the travel limit;
4. **the master actually moves the panels**: rotating it turns a panel root, and lifting it in Z turns one further;
5. running it twice changes nothing (idempotent).

Exit 1 on a failure.
"""
import math
import sys

import bpy
from mathutils import Vector

ADDON = "bl_ext.user_default.uma_addon"
argv = sys.argv[sys.argv.index("--") + 1:]
pmx_path, out_path = argv[0], argv[1]
lines, failures = [], []


def say(text=""):
    lines.append(text)
    print(text)


def check(label, ok, detail=""):
    say("  [%s] %s%s" % ("PASS" if ok else "FAIL", label, (" - " + detail) if detail else ""))
    if not ok:
        failures.append(label)
    return ok


bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.preferences.addon_enable(module="bl_ext.blender_org.mmd_tools")
bpy.ops.preferences.addon_enable(module=ADDON)
bpy.ops.mmd_tools.import_model(filepath=pmx_path, scale=0.08, types={'MESH', 'ARMATURE', 'MORPHS'})

from bl_ext.user_default.uma_addon.uma_addon.addons.uma_addon.operators.RigifyChains import (  # noqa: E402
    skirt_family, physics_families)
from bl_ext.user_default.uma_addon.uma_addon.addons.uma_addon.operators import (  # noqa: E402
    SkirtMasterPlain as P)

arm = next(obj for obj in bpy.context.scene.objects if obj.type == 'ARMATURE')
for obj in bpy.context.scene.objects:
    obj.select_set(False)
arm.select_set(True)
bpy.context.view_layer.objects.active = arm

say("=" * 100)
say(f"plain skirt master - {pmx_path}")
say(f"  armature {arm.name!r}, {len(arm.data.bones)} bones")
present = physics_families(arm)
say(f"  skirt families the rules table recognises: "
    f"{[family for family in present if family and skirt_family(family)]}")

panels = P.skirt_panels(arm)
say(f"  panels found among the model's own bones: {len(panels)}")
for root, hem, chain in panels:
    say(f"    {root} .. {hem}  ({len(chain)} bones)")
check("at least two panels to make a ring", len(panels) >= 2, f"{len(panels)} found")

before_bones = {bone.name for bone in arm.data.bones}
result = bpy.ops.uma.skirt_master_plain()
say(f"  uma.skirt_master_plain -> {result}")
check("the operator works on a rig with no rigify", result == {'FINISHED'}, str(result))

after_bones = {bone.name for bone in arm.data.bones}
added = sorted(after_bones - before_bones)
say(f"  bones it added: {added}")
# two by design since the Offset channel: the master, and the small bone whose travel carries the bias
check("it added the master and nothing else", set(added) <= {"UMA_skirt_master", "UMA_skirt_offset"}
      and "UMA_skirt_master" in added, ", ".join(added))
check("no rigify bones exist in the rig",
      not [bone for bone in after_bones if bone.startswith(("DEF-", "MCH-", "ORG-")) or
           (bone.endswith("_master") and bone != "UMA_skirt_master")],
      ", ".join(sorted(bone for bone in after_bones
                       if bone.startswith(("DEF-", "MCH-", "ORG-")))) or "none")

say("")
say("the wiring, per panel root")
for root, hem, _chain in panels:
    holder = arm.pose.bones[root]
    link = holder.constraints.get(P.NUMBERS.SKIRT_MASTER_CONSTRAINT)
    lift = holder.constraints.get(P.NUMBERS.SKIRT_LIFT_CONSTRAINT)
    ok = (link is not None and link.type == 'COPY_ROTATION' and link.subtarget == "UMA_skirt_master"
          and lift is not None and lift.type == 'TRANSFORM')
    check(f"{root}: link + lift on the panel root", ok,
          "%s / %s" % (link.type if link else "missing", lift.type if lift else "missing"))
master = arm.pose.bones.get("UMA_skirt_master")
travel = master.constraints.get(P.NUMBERS.SKIRT_TRAVEL_CONSTRAINT) if master else None
check("the master carries the travel limit", travel is not None and travel.type == 'LIMIT_LOCATION',
      travel.type if travel else "missing")
check("the master is locked in X and Y", master is not None and tuple(master.lock_location)[:2] == (True, True),
      str(tuple(master.lock_location)) if master else "no master")
check("the travel window is the reference's: -5 mm..+5 cm",
      travel is not None and abs(travel.min_z + 0.005) < 1e-6 and abs(travel.max_z - 0.05) < 1e-6,
      "z %.4f..%.4f" % (travel.min_z, travel.max_z) if travel else "missing")

say("")
say("the cascade: every link of every panel follows the one above it")
links = P.skirt_links(arm)
uncovered = [(child, parent) for child, parent in links
             if arm.pose.bones[child].constraints.get(P.CHAIN_CONSTRAINT) is None]
check("every parent/child link inside the skirt carries a chain constraint", not uncovered,
      "%d links, %d without: %s" % (len(links), len(uncovered), uncovered[:3]))
roots = {root for root, _hem, _chain in panels}
check("and no panel root is driven twice", all(
    all(c.subtarget == "UMA_skirt_master" for c in arm.pose.bones[root].constraints
        if c.name == P.CHAIN_CONSTRAINT) for root in roots),
      "roots carry the master link only")

say("")
say("the mechanism: the master has to move the panels")


def panel_rotation(root, master_rotation=None, lift=0.0):
    """The panel root's world rotation with the master at the given rotation and lift."""
    pb = arm.pose.bones["UMA_skirt_master"]
    pb.rotation_mode = 'XYZ'
    pb.rotation_euler = (0.0, 0.0, 0.0)
    pb.location = (0.0, 0.0, 0.0)
    if master_rotation:
        pb.rotation_euler = master_rotation
    if lift:
        pb.location = (0.0, 0.0, lift)
    bpy.context.view_layer.update()
    return arm.pose.bones[root].matrix.to_quaternion()


root = panels[0][0]
rest = panel_rotation(root)
turned = panel_rotation(root, master_rotation=(0.0, 0.0, math.radians(30.0)))
angle = math.degrees(rest.rotation_difference(turned).angle)
check("turning the master turns a panel", angle > 1.0, f"{angle:.1f} deg at 30 deg of master")

rest = panel_rotation(root)
lifted = panel_rotation(root, lift=0.05)
angle = math.degrees(rest.rotation_difference(lifted).angle)
check("lifting the master flares a panel", angle > 1.0, f"{angle:.1f} deg at +5 cm")

say("")
say("idempotency")
snapshot = sorted((root, len(arm.pose.bones[root].constraints)) for root, _hem, _chain in panels)
again = bpy.ops.uma.skirt_master_plain()
after = sorted((root, len(arm.pose.bones[root].constraints)) for root, _hem, _chain in panels)
check("a second run adds no constraints", snapshot == after and again == {'FINISHED'},
      f"{snapshot} -> {after}")

say("")
say("=" * 100)
say("FAILURES: " + (", ".join(failures) if failures else "none"))
with open(out_path, "w", encoding="utf-8") as handle:
    handle.write("\n".join(lines) + "\n")
sys.exit(1 if failures else 0)

