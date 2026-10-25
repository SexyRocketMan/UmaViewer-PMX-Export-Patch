"""Verify the Spork port: his eye is our highlight graph, and the matcher no longer confuses his two groups.

    blender --background --factory-startup --python Tools/verify_spork_library.py -- <out.txt> [<file.pmx>]

There is no Spork character in `.pmx` anywhere on this machine (checked: his project file is a `.blend`, and no
export names 1063), so the *character* half is exercised on the reference export shaded with `spork-1.0`: the part
rules, the texture recipe, the copies and the drivers are the code under test, and the images happen to be 1001's.
The library half is checked on the file itself.

What it has to prove:

1. `spork-1.0` validates with the five new eye rows, and its `textures.eye` names nodes the ported material has;
2. `SporkShaders.blend` holds `Spork Eye` = the 39-node highlight graph with `NoiseGenerator`/`SmoothThreshold`
   alongside it, `CrySwitch` 0, no images, no shader tag, and his own eye kept under its own name;
3. `_hint_matches` is exact: `Simple Diff + Shaded` and its `.001` rename match, `Simple Diff + Shaded FACE` does not
   - the trap in the handoff, decided by measurement rather than by eye (the FACE variant really does carry both of
   the sockets the panel drives, so a substring match would have failed quietly);
4. shading a character with `spork-1.0` gives an `[eye]` copy wearing `Spork Eye`, the five rows resolve on it, and
   `hook` puts 11 drivers on the eye and none of the Looper rows.
"""
import sys

import bpy

ADDON = "bl_ext.user_default.uma_addon"
SHADERS = (r"C:\Users\user\AppData\Roaming\Blender Foundation\Blender\5.2\extensions\user_default"
           r"\uma_addon\uma_addon\addons\uma_addon\operators\shaders")

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
from bl_ext.user_default.uma_addon.uma_addon.addons.uma_addon.operators import Umashader  # noqa: E402

context = bpy.context

say("=" * 100)
say("1. the manifest")
manifest, problems = ShaderManifest.load("spork-1.0")
check("validates with no problems", not problems, "; ".join(problems) or "clean")
check("has the five eye rows", len([row for row in manifest["settings"] if row.get("material") == "eye"]) == 5,
      "%d" % len([row for row in manifest["settings"] if row.get("material") == "eye"]))
check("7 rows in total", len(manifest["settings"]) == 7, "%d" % len(manifest["settings"]))
eye_rules = [rule["node"] for rule in manifest["textures"]["eye"]]
check("textures.eye addresses our node names",
      eye_rules == ["Image Texture", "Image Texture.001", "Image Texture.002", "Image Texture.003"],
      "%s" % (eye_rules,))
check("group_hints is still just his shader", manifest["group_hints"] == ["Simple Diff + Shaded"],
      "%s" % (manifest["group_hints"],))

say()
say("2. the library file")
bpy.ops.wm.open_mainfile(filepath=SHADERS + r"\SporkShaders.blend")
eye = bpy.data.materials.get("Spork Eye")
kept = bpy.data.materials.get("Spork Eye (Spork's own)")
if check("Spork Eye is there", eye is not None and eye.node_tree is not None):
    nodes = eye.node_tree.nodes
    check("it is the highlight graph (39 nodes)", len(nodes) == 39, "%d" % len(nodes))
    groups = sorted({node.node_tree.name for node in nodes
                     if node.type == 'GROUP' and node.node_tree is not None})
    check("the two highlight groups came with it",
          "NoiseGenerator" in groups and "SmoothThreshold" in groups, "%s" % (groups,))
    cry = nodes.get("Value")
    check("CrySwitch rests at 0", cry is not None and float(cry.outputs[0].default_value) == 0.0,
          "%r" % (float(cry.outputs[0].default_value) if cry else None,))
    check("no image is bound", not [node for node in nodes if node.type == 'TEX_IMAGE' and node.image])
    check("no shader tag", eye.get(ShaderManifest.SOURCE_PROPERTY) is None)
    check("the four image nodes the recipe names exist",
          all(nodes.get(name) is not None for name in ("Image Texture", "Image Texture.001",
                                                       "Image Texture.002", "Image Texture.003")))
check("his own eye was kept, not thrown away", kept is not None,
      "%d node(s)" % len(kept.node_tree.nodes) if kept else "missing")
slots = [slot.material.name if slot.material else None
         for obj in bpy.data.objects if obj.type == 'MESH' for slot in obj.material_slots]
check("the carrier sphere points at it", "Spork Eye" in slots, "%s" % (slots,))

say()
say("3. the hint matcher (the trap)")
for name, wanted in (("Simple Diff + Shaded", True), ("Simple Diff + Shaded.001", True),
                     ("Simple Diff + Shaded FACE", False), ("Umaviewer Outline material", False)):
    check("_hint_matches(%r) is %s" % (name, wanted),
          Control._hint_matches(name, ("Simple Diff + Shaded",)) is wanted)

say()
say("4. shading a character with spork-1.0")
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.preferences.addon_enable(module="bl_ext.blender_org.mmd_tools")
bpy.ops.preferences.addon_enable(module=ADDON)
context.scene.uma_scene.shader = "spork-1.0"
bpy.ops.mmd_tools.import_model(filepath=pmx)
mesh = max([obj for obj in context.scene.objects if obj.type == 'MESH'], key=lambda item: len(item.data.vertices))
for obj in context.view_layer.objects:
    obj.select_set(False)
mesh.select_set(True)
context.view_layer.objects.active = mesh
say("     uma.apply_shader: %s" % (bpy.ops.uma.apply_shader(),))
copy = next((slot.material for slot in mesh.material_slots
             if slot.material and slot.material.name.endswith("[eye]")), None)
if check("an [eye] copy exists", copy is not None, copy.name if copy else "none"):
    # the copy is renamed `<model material> [part]` by the apply step, so "it wears Spork Eye" is a question about
    # the *master* it was made from, not about the copy's own name
    master = Control.master_material(manifest, "eye", context)
    check("the master it was copied from is Spork Eye", master is not None and master.name.startswith("Spork Eye"),
          master.name if master else "none")
    check("39 nodes, i.e. the ported graph", len(copy.node_tree.nodes) == 39, "%d" % len(copy.node_tree.nodes))
    check("its role tag says eye", Control.material_role(copy) == "eye", repr(copy.get(ShaderManifest.ROLE_PROPERTY)))
    images = {node.name: (node.image.name if node.image else None)
              for node in copy.node_tree.nodes if node.type == 'TEX_IMAGE'}
    say("     bound images: %s" % images)
    check("the main eye texture is on the main node",
          (images.get("Image Texture") or "").endswith("eye0.png"), images.get("Image Texture"))
    check("the first highlight is on .001", (images.get("Image Texture.001") or "").endswith("eyehi00.png"),
          images.get("Image Texture.001"))
    Control.use_table(context, manifest=manifest)
    resolved = {key: Control.setting_targets(copy, Control.SETTING_BY_KEY[key]) for key in
                ("spork_eye_cry", "spork_eye_tremble_rate", "spork_eye_tremble_amount",
                 "spork_eye_color_hi0", "spork_eye_color_hi1")}
    for key, found in resolved.items():
        check("%s resolves on the eye" % key, len(found) == 1, "%s" % ([path for path, _s in found],))
    report = Control.hook(context)
    mine = [curve for curve in (copy.node_tree.animation_data.drivers if copy.node_tree.animation_data else [])
            if curve.data_path in Control._own_driver_paths(copy)]
    say("     hook: %d driver(s) over %d cop(y/ies); %s"
        % (report["drivers"], report["copies"], report["notes"] or "no notes"))
    check("11 drivers on the eye", len(mine) == 11, "%d" % len(mine))
    check("no missing settings", not any(entry["missing"] for entry in report["characters"]),
          "%s" % [entry["missing"] for entry in report["characters"] if entry["missing"]][:2])
    check("verify() is clean", report["verified"]["wrong_count"] == 0,
          "%d wrong of %d checked" % (report["verified"]["wrong_count"], report["verified"]["checked"]))

say()
say("RESULT: %s" % ("all checks passed" if not failures else "FAILED - " + "; ".join(failures)))
with open(out, "w", encoding="utf-8") as handle:
    handle.write("\n".join(lines) + "\n")
print("wrote %s" % out)
