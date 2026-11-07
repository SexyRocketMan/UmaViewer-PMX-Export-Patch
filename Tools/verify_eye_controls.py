"""Verify the node-targeted settings rows (`target`), end to end, on a real character.

    blender --background --factory-startup --python Tools/verify_eye_controls.py -- <out.txt> [<file.pmx>]

The rows added in 2.34 address nodes *inside* the eye copy (the library's `Value` node labelled `CrySwitch`, the
`NoiseGenerator` group's `TimeScale`/`Gain`, and the two `RGB` nodes) rather than sockets on a shader group. The
claims that has to earn:

1. the manifest still validates - 25 rows, five of them targeted;
2. `setting_targets` lands on the right sockets: `nodes["Value"].outputs[0].default_value` (a `Value` node is driven
   through its **output**), `nodes["Group"].inputs[0].default_value` for `TimeScale`, and the RGB outputs;
3. a targeted row applies to its own role only - the eye resolves it, a body copy does not, and neither reports the
   other's absence as a missing setting;
4. copies carry `uma_shader_role` (`Shading` writes it), and the eye copy is now a material the control *sees*;
5. `hook` adds exactly 11 drivers for the five rows (1 + 1 + 1 + 4 + 4) on the eye and leaves the 41 group rows on a
   body copy untouched - 41 per body copy is what this project measured before, so 7 copies + the eye = 298;
6. the knobs *work*: setting the global moves the socket, a character override beats the global, `verify()` reports
   nothing wrong, and `bake()` writes the values in and takes the drivers out - with `hook` putting them back.
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


bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.preferences.addon_enable(module="bl_ext.blender_org.mmd_tools")
bpy.ops.preferences.addon_enable(module=ADDON)

from bl_ext.user_default.uma_addon.uma_addon.addons.uma_addon.operators import (  # noqa: E402
    ShaderManifest, UmaShaderControl as Control)

context = bpy.context
manifest, problems = ShaderManifest.load(ShaderManifest.DEFAULT_ID)
Control.use_table(context, manifest=manifest)

say("=" * 100)
say("1. the manifest")
check("loads with no problems", not problems, "; ".join(problems) or "clean")
targeted = [row for row in manifest["settings"] if row.get("target")]
eye_keys = ("uma_shader_eye_cry", "uma_shader_eye_tremble_rate", "uma_shader_eye_tremble_amount",
            "uma_shader_eye_color_hi0", "uma_shader_eye_color_hi1")
check("has the five eye rows", all(Control.SETTING_BY_KEY.get(key) for key in eye_keys),
      "%s" % [key for key in eye_keys if not Control.SETTING_BY_KEY.get(key)])
check("all five are targeted rows",
      all(Control.SETTING_BY_KEY[key].get("target") for key in eye_keys))
check("the eye section is in the table", "Eye highlights" in Control.SECTION_ORDER,
      "%s" % (Control.SECTION_ORDER,))
for row in targeted:
    say("     %-28s %-8s %r" % (row["key"], row["kind"], row.get("target")))

say()
say("2. shade the character, then look at what it wears")
bpy.ops.mmd_tools.import_model(filepath=pmx)
meshes = [obj for obj in context.scene.objects if obj.type == 'MESH']
mesh = max(meshes, key=lambda item: len(item.data.vertices))
for obj in context.view_layer.objects:
    obj.select_set(False)
mesh.select_set(True)
context.view_layer.objects.active = mesh
say("     uma.apply_shader: %s" % (bpy.ops.uma.apply_shader(),))

eye = next((slot.material for slot in mesh.material_slots
            if slot.material and slot.material.name.endswith("[eye]")), None)
body = next((slot.material for slot in mesh.material_slots
             if slot.material and slot.material.name.endswith("[bdy]")), None)
if not check("an [eye] copy exists", eye is not None, eye.name if eye else "none"):
    with open(out, "w", encoding="utf-8") as handle:
        handle.write("\n".join(lines) + "\n")
    raise SystemExit(1)
check("it carries the role tag", Control.material_role(eye) == "eye", repr(eye.get(ShaderManifest.ROLE_PROPERTY)))
check("a body copy is 'default'", Control.material_role(body) == "default", Control.material_role(body))

materials = Control.character_materials([obj for obj in context.scene.objects if obj.type == 'MESH'])
check("the control now sees the eye copy", eye in materials, "%d material(s) in the character" % len(materials))

say()
say("3. where each row lands")
expectations = {
    "uma_shader_eye_cry": 'nodes["Value"].outputs[0].default_value',
    "uma_shader_eye_tremble_rate": 'nodes["Group"].inputs[0].default_value',
    "uma_shader_eye_tremble_amount": 'nodes["Group"].inputs[1].default_value',
    "uma_shader_eye_color_hi0": 'nodes["Color"].outputs[0].default_value',
    "uma_shader_eye_color_hi1": 'nodes["Color.001"].outputs[0].default_value',
}
for key, wanted in expectations.items():
    setting = Control.SETTING_BY_KEY[key]
    found = Control.setting_targets(eye, setting)
    paths = [path for path, _socket in found]
    check("%s -> %s" % (key, wanted), paths == [wanted], "%s" % (paths,))
    other = Control.setting_targets(body, setting)
    check("   ... and not on a body copy", not other, "%s" % ([path for path, _ in other],))
group_row = Control.SETTING_BY_KEY["uma_shader_light_threshold"]
check("a group row still resolves on a body copy", len(Control.setting_targets(body, group_row)) == 1)
check("a group row resolves nowhere on the eye", not Control.setting_targets(eye, group_row))
check("_should_report is quiet about the eye's group rows", not Control._should_report(group_row, eye))
check("_should_report is quiet about the body's eye rows",
      not Control._should_report(Control.SETTING_BY_KEY["uma_shader_eye_cry"], body))

say()
say("4. hook the drivers up")


def expected_drivers(materials):
    """What `hook` should make, derived from the table rather than written down: one curve per number target and four
    per colour target, over every row each copy actually owns. A literal here went stale the moment the decal rows
    were added, which is exactly what a test must not do."""
    total = 0
    for material in materials:
        for setting in Control.SETTINGS:
            total += len(Control.setting_targets(material, setting)) * (4 if setting["kind"] == "colour" else 1)
    return total


expected = expected_drivers(materials)
report = Control.hook(context)
say("     %d driver(s) over %d cop(y/ies)" % (report["drivers"], report["copies"]))
say("     notes: %s" % (report["notes"] or "none",))
for entry in report["characters"]:
    say("     %s: %d driver(s), missing %d" % (entry["root"], entry["drivers"], len(entry["missing"])))
    for missing in entry["missing"]:
        say("       missing: %s" % missing)
check("no missing settings reported", not any(entry["missing"] for entry in report["characters"]),
      "%d reported" % sum(len(entry["missing"]) for entry in report["characters"]))
tree = eye.node_tree
drivers = list(tree.animation_data.drivers) if tree.animation_data else []
mine = [curve for curve in drivers if curve.data_path in Control._own_driver_paths(eye)]
check("11 drivers on the eye (1 + 1 + 1 + 4 + 4)", len(mine) == 11, "%d" % len(mine))
check("the driver total is what the table implies", report["drivers"] == expected, "%d, expected %d" % (report["drivers"], expected))
check("hook's own verification is clean", report["verified"]["wrong_count"] == 0,
      "%d wrong of %d checked" % (report["verified"]["wrong_count"], report["verified"]["checked"]))

say()
say("5. the knobs move the sockets")
sun = Control.sun_object(context)
root = Control.root_object(mesh)
value_node = eye.node_tree.nodes["Value"]
noise = eye.node_tree.nodes["Group"]
hi0 = eye.node_tree.nodes["Color"]


def read_after_setting(obj, key, value):
    Control.write_value(obj, key, value)
    Control._settle(context, sun, root)
    return value_node.outputs[0].default_value, noise.inputs["TimeScale"].default_value, \
        tuple(round(component, 3) for component in hi0.outputs[0].default_value)


cry, rate, colour = read_after_setting(sun, "uma_shader_eye_cry", 1.0)
check("global Crying 1 reaches the library's CrySwitch", abs(cry - 1.0) < 1e-4, "%.4f" % cry)
check("... and the colour is still the seeded white", colour == (1.0, 1.0, 1.0, 1.0), "%s" % (colour,))
cry, rate, colour = read_after_setting(sun, "uma_shader_eye_tremble_rate", 5.0)
check("global Tremble rate 5 reaches TimeScale", abs(rate - 5.0) < 1e-4, "%.4f" % rate)
cry, rate, colour = read_after_setting(sun, "uma_shader_eye_color_hi0", (0.25, 0.5, 0.75, 1.0))
check("global highlight colour reaches the RGB node",
      tuple(round(component, 3) for component in colour) == (0.25, 0.5, 0.75, 1.0), "%s" % (colour,))

# per-character: her own value wins over the global, and going back to inherit returns to it
Control.write_value(root, "uma_shader_eye_cry", 0.25)
Control._settle(context, sun, root)
check("her override beats the global", abs(float(value_node.outputs[0].default_value) - 0.25) < 1e-4,
      "%.4f" % value_node.outputs[0].default_value)
Control.write_value(root, "uma_shader_eye_cry", Control.INHERIT)
Control._settle(context, sun, root)
check("and inheriting again returns to the global", abs(float(value_node.outputs[0].default_value) - 1.0) < 1e-4,
      "%.4f" % value_node.outputs[0].default_value)
check("verify() finds nothing wrong", Control.verify(context)["wrong_count"] == 0)

say()
say("6. bake, then hook again")
baked = Control.bake(context)
check("bake wrote the values", baked["written"] > 0, "%d socket(s)" % baked["written"])
check("bake removed the drivers", baked["drivers_removed"] > 0, "%d" % baked["drivers_removed"])
check("the value survived the bake", abs(float(value_node.outputs[0].default_value) - 1.0) < 1e-4,
      "%.4f" % value_node.outputs[0].default_value)
check("status() says baked", Control.status(context)[0] == "baked", Control.status(context)[0])
again = Control.hook(context)
check("re-hooking restores the same total", again["drivers"] == expected, "%d, expected %d" % (again["drivers"], expected))
check("status() says driven again", Control.status(context)[0] == "driven", Control.status(context)[0])

say()
say("RESULT: %s" % ("all checks passed" if not failures else "FAILED - " + "; ".join(failures)))
with open(out, "w", encoding="utf-8") as handle:
    handle.write("\n".join(lines) + "\n")
print("wrote %s" % out)
