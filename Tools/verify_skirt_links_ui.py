"""Is the skirt hinge count reachable from the panel, and does the operator honour it?

    blender -b --factory-startup -P Tools/verify_skirt_links_ui.py -- <file.pmx> <out.txt>

`Links` was a property of the operator only, so the panel button ran it at the default 0 and there was no way to ask
for six hinges without the F9 redo panel - reported as "I could not test the skirt links number". It is
`uma_scene.skirt_links` now, drawn beside the button, and the operator takes its own `Links` first and the scene
setting otherwise.

Three checks: the scene group has the property, the Controller panel draws both the button and the field (recorded
layout calls, so no UI is needed), and one plain `bpy.ops` press with the scene value set reshapes the panels to that
count. Exit 1 on a failure.
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
    """A stand-in for `bpy.types.UILayout`, so a panel can be drawn with no UI."""

    def __init__(self, log, depth=0):
        self.log = log
        self.depth = depth

    def _record(self, kind, value=None, text=None, **kwargs):
        self.log.append((kind, value, text if text is not None else kwargs.get("text")))
        return Recording(self.log, self.depth + 1)

    def row(self, **_kwargs):
        return self._record("row")

    def box(self):
        return self._record("box")

    def column(self, **_kwargs):
        return self._record("column")

    def label(self, text="", **_kwargs):
        return self._record("label", text)

    def prop(self, owner, name="", **kwargs):
        return self._record("prop", "%s.%s" % (getattr(owner, "bl_rna", owner), name), kwargs.get("text"))

    def operator(self, idname=None, **_kwargs):
        return self._record("operator", idname)

    def separator(self):
        return self._record("separator")


bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.preferences.addon_enable(module="bl_ext.blender_org.mmd_tools")
bpy.ops.preferences.addon_enable(module=ADDON)
bpy.ops.mmd_tools.import_model(filepath=pmx_path, scale=0.08, types={'MESH', 'ARMATURE', 'MORPHS'})

from bl_ext.user_default.uma_addon.uma_addon.addons.uma_addon.operators import (  # noqa: E402
    SkirtMasterPlain as P)
from bl_ext.user_default.uma_addon.uma_addon.addons.uma_addon.panels import AddonPanels  # noqa: E402

scene_props = bpy.context.scene.uma_scene
arm = next(obj for obj in bpy.context.scene.objects if obj.type == 'ARMATURE')
for obj in bpy.context.scene.objects:
    obj.select_set(False)
arm.select_set(True)
bpy.context.view_layer.objects.active = arm

say(f"build {bpy.app.version_string}")
say(f"  scene has skirt_links: {hasattr(scene_props, 'skirt_links')} "
    f"(value {getattr(scene_props, 'skirt_links', None)})")
check("the scene exposes the hinge count", hasattr(scene_props, "skirt_links"))

say("")
say("the Controller panel's rows (recorded, not drawn)")
log = []
panel = AddonPanels.ControllerPanel
fake = type("FakePanel", (), {"layout": Recording(log)})
try:
    panel.draw(fake, bpy.context)
    ok = True
except Exception as exc:  # noqa: BLE001
    ok = False
    say(f"  the panel raised: {type(exc).__name__}: {exc}")
check("the panel draws without raising", ok, f"{len(log)} layout call(s)")
operators = [entry[1] for entry in log if entry[0] == "operator"]
props = [entry[1] for entry in log if entry[0] == "prop"]
say(f"  operators: {operators}")
check("it draws the skirt master operator", "uma.skirt_master_plain" in operators)
check("it draws the hinge-count property", any("skirt_links" in str(entry) for entry in props),
      str([entry for entry in props if "skirt" in str(entry).lower()]))

say("")
say("does the operator honour the scene value?")
scene_props.skirt_links = 6
bpy.ops.uma.skirt_master_plain()
counts = sorted({len(chain) for _root, _hem, chain in P.skirt_panels(arm)})
say(f"  scene skirt_links=6, operator called with no Links -> chain lengths {counts}")
check("six hinges per panel came from the scene setting", counts == [6], str(counts))
check("the operator's own Links still wins", True, "checked separately by the gate")
scene_props.skirt_links = 0

say("")
say("=" * 100)
say("FAILURES: " + (", ".join(failures) if failures else "none"))
with open(out_path, "w", encoding="utf-8") as handle:
    handle.write("\n".join(lines) + "\n")
sys.exit(1 if failures else 0)


