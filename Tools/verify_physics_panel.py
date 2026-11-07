"""The physics toggles, and that the Morphs panel draws the operator its rows need.

    blender -b --factory-startup -P Tools/verify_physics_panel.py -- <file.pmx> <out.txt>

Two defects the rig audit found, both silent:

* the damped-track toggles' getter returned from *inside* the `for bone_name` loop, so it only ever looked at the
  first bone in the list - a costume with no tail read the toggle as off, and a rig whose first bone had no
  constraint read it as off even when the rest carried one. `set_enable(True)` also added a DAMPED_TRACK without
  checking, so pressing a toggle twice stacked two.
* `uma.build_morph_drivers` ("Set Up Advanced Morphs") was drawn by no panel at all, while the Morphs panel's own
  error text told the user to run it. It was reachable only from F3.

Exit code 0 when every check passes.

1. The three damped-track toggles: on a rig that has ears/bust/tail bones, turning one on adds exactly one
   constraint per bone that has a child, the toggle reads back True, a second press adds nothing, turning it off
   removes every one of them. Then the getter is checked against a bone list whose **first** bone is missing -
   the case the early `return False` used to get wrong (a costume with no tail).
2. The Morphs panel is drawn into a recording layout (the pattern `Tools/verify_eye_panel.py` uses), and the
   `uma.build_morph_drivers` row has to be there.
"""
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


class Recording:
    """A stand-in for `bpy.types.UILayout`: records what a panel draws, and chains like the real thing."""

    def __init__(self, log, depth=0):
        self.log = log
        self.depth = depth

    def _record(self, kind, text=None, **kwargs):
        self.log.append((kind, text, kwargs.get("icon")))
        return Recording(self.log, self.depth + 1)

    def row(self, **_kwargs):
        return self._record("row")

    def box(self):
        return self._record("box")

    def column(self, **_kwargs):
        return self._record("column")

    def label(self, text="", **_kwargs):
        return self._record("label", text)

    def prop(self, _owner, name="", **kwargs):
        return self._record("prop", kwargs.get("text") or name)

    def operator(self, idname=None, **_kwargs):
        return self._record("operator", idname)


bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.preferences.addon_enable(module="bl_ext.blender_org.mmd_tools")
bpy.ops.preferences.addon_enable(module=ADDON)
bpy.ops.mmd_tools.import_model(filepath=pmx_path, scale=0.08, types={'MESH', 'ARMATURE', 'MORPHS'})

from bl_ext.user_default.uma_addon.uma_addon.addons.uma_addon.operators import Physics  # noqa: E402
from bl_ext.user_default.uma_addon.uma_addon.addons.uma_addon.panels import AddonPanels  # noqa: E402

arm = next(obj for obj in bpy.context.scene.objects if obj.type == 'ARMATURE')
for obj in bpy.context.scene.objects:
    obj.select_set(False)
arm.select_set(True)
bpy.context.view_layer.objects.active = arm

scene_props = bpy.context.scene.damped_track


def tagged(bone_name):
    pbone = arm.pose.bones.get(bone_name)
    if pbone is None:
        return []
    return [c.name for c in pbone.constraints if c.name.endswith(ADDON)]


say("=" * 100)
say(f"physics toggles - {pmx_path}")
say(f"  bones present: ears {[b for b in Physics.EAR_BONES if b in arm.pose.bones]}")
say(f"                 bust {[b for b in Physics.BUST_BONES if b in arm.pose.bones]}")
say(f"                 tail {[b for b in Physics.TAIL_BONES if b in arm.pose.bones]}")

for prop, bones in (("ear_enable", Physics.EAR_BONES), ("bust_enable", Physics.BUST_BONES),
                    ("tail_enable", Physics.TAIL_BONES)):
    say("")
    say(f"{prop}:")
    present = [b for b in bones if b in arm.pose.bones]
    if not present:
        say("  (this rig has none of those bones - the toggle should stay off and do nothing)")
        setattr(scene_props, prop, True)
        check(f"{prop} stays off on a rig without the bones", getattr(scene_props, prop) is False)
        check(f"{prop} added no constraint", not any(tagged(b) for b in bones if b in arm.pose.bones))
        continue

    setattr(scene_props, prop, True)
    after_one = {bone: len(tagged(bone)) for bone in present}
    with_child = [bone for bone in present if arm.pose.bones[bone].children]
    check(f"{prop} reads back True", getattr(scene_props, prop) is True, str(after_one))
    check(f"{prop} added one constraint per bone that has a child", all(count <= 1 for count in after_one.values()),
          str(after_one))
    check(f"{prop} covered every bone with a child",
          all(after_one[bone] == 1 for bone in with_child), f"{len(with_child)} bone(s)")

    setattr(scene_props, prop, True)              # a second press must not stack
    after_two = {bone: len(tagged(bone)) for bone in present}
    check(f"{prop} a second press adds nothing", after_two == after_one, str(after_two))

    setattr(scene_props, prop, False)
    check(f"{prop} turning it off removes all of them", not any(tagged(bone) for bone in present),
          str({bone: len(tagged(bone)) for bone in present}))
    check(f"{prop} reads back False", getattr(scene_props, prop) is False)

say("")
say("the getter's early-return case: a bone list whose first entry is absent")
missing_first = ("Sp_Hi_Tail0_B_00_does_not_exist", "Ear_01_L")
getter = Physics.DampedTrackProperties.get_enable(missing_first)
setattr(scene_props, "ear_enable", True)          # put a real constraint on Ear_01_L
wrapped = type("Probe", (), {"get": getter})
say(f"  constraint on Ear_01_L: {tagged('Ear_01_L')}")
check("a missing first bone does not hide a constraint further down the list", getter(None) is True,
      "the old getter returned False here")
setattr(scene_props, "ear_enable", False)

say("")
say("the Morphs panel's rows")
log = []
panel = AddonPanels.MorphPanel
fake = type("FakePanel", (), {"layout": Recording(log)})
try:
    panel.draw(fake, bpy.context)
except Exception as exc:                          # noqa: BLE001 - the panel's own error is a failed check
    check("the panel draws without raising", False, str(exc))
else:
    check("the panel draws without raising", True, f"{len(log)} layout call(s)")
    operators = [entry[1] for entry in log if entry[0] == "operator"]
    check("it draws the morph-setup operator", "uma.build_morph_drivers" in operators, str(operators))
    labels = [entry[1] for entry in log if entry[0] == "label"]
    say(f"  operator rows: {operators}")
    say(f"  labels: {labels}")

say("")
say("=" * 100)
say("FAILURES: " + (", ".join(failures) if failures else "none"))
with open(out_path, "w", encoding="utf-8") as handle:
    handle.write("\n".join(lines) + "\n")
sys.exit(1 if failures else 0)

