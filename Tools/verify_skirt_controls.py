"""Do the two skirt controls do what their names say?

    blender -b --factory-startup -P Tools/verify_skirt_controls.py -- <file.pmx> <out.txt>

`Skirt_Amount` and `Skirt_Offset` are custom properties on the master bone, driven onto the constraints rather than
baked into them. This measures the *result*, on the hem's world position, not the wiring:

    Amount 1.0 / 0.5 / 0.0 with the master turned    full bend, about half, and nothing at all
    Amount 0.0 with the master lifted                the lift channel silenced too
    Offset +45 / -45 / 0 with the master at rest     the ring flares out, tucks in, and does nothing

The flare direction is measured as the hem's horizontal radius from the master, which is the one number that
distinguishes "flares" from "tucks" - displacement magnitude alone cannot.

Exit 1 on a failure.
"""
import math
import sys

import bpy

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

from bl_ext.user_default.uma_addon.uma_addon.addons.uma_addon.operators import (  # noqa: E402
    SkirtMasterPlain as P)

arm = next(obj for obj in bpy.context.scene.objects if obj.type == 'ARMATURE')
for obj in bpy.context.scene.objects:
    obj.select_set(False)
arm.select_set(True)
bpy.context.view_layer.objects.active = arm
result = bpy.ops.uma.skirt_master_plain()
panels = P.skirt_panels(arm)
master = arm.pose.bones[P.NUMBERS.SKIRT_MASTER_NAME]
centre = arm.data.bones[P.NUMBERS.SKIRT_MASTER_NAME].head_local
hem = panels[0][1]
drivers = len(arm.animation_data.drivers) if arm.animation_data else 0

say("=" * 100)
say(f"skirt controls - {pmx_path}")
say(f"  operator {result}; {len(panels)} panel(s); controls {[k for k in master.keys() if k.startswith('Skirt_')]}; "
    f"{drivers} driver(s)")
check("both controls are declared", P.AMOUNT_PROP in master.keys() and P.OFFSET_PROP in master.keys())
check("the offset bone adds no deform", P.OFFSET_BONE in arm.data.bones
      and not arm.data.bones[P.OFFSET_BONE].use_deform, P.OFFSET_BONE)
check("the channels are driven, not baked", drivers >= 2 * len(panels), f"{drivers} drivers for {len(panels)} panels")


def pose(amount=1.0, offset=0.0, turn=0.0, lift=0.0):
    master[P.AMOUNT_PROP] = float(amount)
    master[P.OFFSET_PROP] = float(offset)
    master.rotation_mode = 'XYZ'
    master.rotation_euler = (0.0, 0.0, math.radians(turn))
    master.location = (0.0, 0.0, lift)
    arm.update_tag()
    bpy.context.view_layer.update()
    return arm.pose.bones[hem].tail.copy()


def radius(bone_name, offset):
    # the master back to its rest pose **and** Amount back to 1: measuring the offset while the previous case left
    # the master silenced measured 0 for every offset, which is what made this check fail the first time
    master[P.AMOUNT_PROP] = 1.0
    master.rotation_mode = 'XYZ'
    master.rotation_euler = (0.0, 0.0, 0.0)
    master.location = (0.0, 0.0, 0.0)
    master[P.OFFSET_PROP] = float(offset)
    arm.update_tag()
    bpy.context.view_layer.update()
    delta = arm.pose.bones[bone_name].tail - centre
    delta.z = 0.0
    return delta.length


rest = pose()
say("")
say("Amount - the hem's displacement, master turned 10 deg")
full = (pose(turn=10.0) - rest).length * 1000
half = (pose(amount=0.5, turn=10.0) - rest).length * 1000
none = (pose(amount=0.0, turn=10.0) - rest).length * 1000
lifted = (pose(lift=0.05) - rest).length * 1000
lifted_off = (pose(amount=0.0, lift=0.05) - rest).length * 1000
doubled = (pose(amount=2.0, turn=10.0) - rest).length * 1000
say("  Amount 1.0: %6.1f mm   Amount 0.5: %6.1f mm   Amount 0.0: %5.2f mm   Amount 2.0: %6.1f mm"
    % (full, half, none, doubled))
say("  lift +5 cm: Amount 1.0 %6.1f mm, Amount 0.0 %5.2f mm" % (lifted, lifted_off))
check("Amount 1.0 bends the panel", full > 5.0, f"{full:.1f} mm")
check("Amount 0.0 silences the master", none < 0.5 and lifted_off < 0.5, f"{none:.2f} / {lifted_off:.2f} mm")
check("Amount 0.5 lands between", 0.15 * full < half < 0.9 * full, f"{half:.1f} mm of {full:.1f}")
check("Amount 2.0 goes past the 0..1 cap (the extra copy slot)", doubled > 1.4 * full,
      f"{doubled:.1f} mm against {full:.1f} at 1.0")

say("")
say("Offset - the hem's horizontal radius from the master, master at rest")
radii = {value: radius(hem, value) * 1000 for value in (-45.0, 0.0, 45.0)}
say("  -45: %6.1f mm   0: %6.1f mm   +45: %6.1f mm" % (radii[-45.0], radii[0.0], radii[45.0]))
check("Offset 0 leaves the panel alone", abs(radii[0.0] - radius(hem, 0.0) * 1000) < 0.5)
outward = [radius(entry[1], 45.0) * 1000 - radii[0.0] for entry in panels]
inward = [radius(entry[1], -45.0) * 1000 - radii[0.0] for entry in panels]
say("  per panel: outward %+.0f..%+.0f mm, inward %+.0f..%+.0f mm"
    % (min(outward), max(outward), min(inward), max(inward)))
bias_only = radius(hem, 45.0) * 1000 - radius(hem, 0.0) * 1000
master[P.AMOUNT_PROP] = 0.0
arm.update_tag()
bpy.context.view_layer.update()
silenced_radius = arm.pose.bones[hem].tail.copy()
say("  Offset +45 with Amount 0 still flares: %+.0f mm of the bias channel alone"
    % ((silenced_radius - centre).length * 1000 - radii[0.0]))
check("the bias does not depend on Amount", (silenced_radius - centre).length * 1000 - radii[0.0] > 5.0,
      "measured at Amount 0")
check("Offset +45 flares every panel outward", all(delta > 5.0 for delta in outward),
      "%d of %d panels" % (sum(1 for delta in outward if delta > 5.0), len(outward)))
check("Offset -45 tucks every panel inward", all(delta < -5.0 for delta in inward),
      "%d of %d panels" % (sum(1 for delta in inward if delta < -5.0), len(inward)))

say("")
say("=" * 100)
say("FAILURES: " + (", ".join(failures) if failures else "none"))
with open(out_path, "w", encoding="utf-8") as handle:
    handle.write("\n".join(lines) + "\n")
sys.exit(1 if failures else 0)


