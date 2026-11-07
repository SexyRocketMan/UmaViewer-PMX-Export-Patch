"""Verify the Spork library: his ported eye, the ported group(s), and that shading a character reaches all of them.

    blender --background --factory-startup --python Tools/verify_spork_library.py -- <out.txt> [<file.pmx>]

There is no Spork `.pmx` anywhere on this machine (checked: his project files are `.blend`s), so the *character* half
is exercised on a reference export shaded with `spork-1.0`: the part rules, the texture recipes, the copies and the
drivers are the code under test, and the images happen to be 1001's. The library half is checked on the file itself.

What it has to prove, as of 2.35.18 (his current group ported by `Tools/port_spork_group.py`):

1. the manifest validates, carries his five eye rows *and* the weather rows, and its textures address node names the
   ported materials actually have;
2. `SporkShaders.blend` holds the ported graphs: `Simple Diff + Shaded` = his 51 nodes + our dirt chain (58), and
   `Simple Diff + Shaded FACE` = his 34 + dirt (41); both offer the weather sockets; `Spork Eye` is still the 39-node
   highlight graph with `CrySwitch` at 0 and no images; his own eye is still kept;
3. the masters are the ones the roles name: `Spork Shader` and `Uma Brows` on the general group with the base-map
   chain, `Spork Shader Face` on the FACE variant *without* one, `Spork Eye Inner` on the general group with `Base`
   resting at white (black there would mix the inner eye fully transparent);
4. shading a character with `spork-1.0` gives the three roles their own copies, the five rows resolve on the eye, and
   `hook` puts 11 drivers on the eye with none missing.
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


def group_of(material):
    return next((node.node_tree.name for node in material.node_tree.nodes
                 if node.type == 'GROUP' and node.node_tree is not None), None)


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
rows = manifest["settings"]
check("has the five eye rows", len([row for row in rows if row.get("material") == "eye"]) == 5,
      "%d" % len([row for row in rows if row.get("material") == "eye"]))
check("has the weather rows", {"uma_shader_wet", "uma_shader_dirt", "uma_shader_dirt_colour"}
      <= {row["key"] for row in rows}, "%s" % [row["key"] for row in rows])
check("exposes his three light sockets",
      {"spork_highlight_color", "spork_light_intensity", "spork_colored_light"} <= {row["key"] for row in rows},
      "%s" % [row["key"] for row in rows])
check("15 rows in total", len(rows) == 15, "%d" % len(rows))
eye_rules = [rule["node"] for rule in manifest["textures"]["eye"]]
check("textures.eye addresses our node names",
      eye_rules == ["Iris Map", "Highlight Map 0", "Highlight Map 1", "Highlight Map 2"], "%s" % (eye_rules,))
check("group_hints names both of his groups, exactly",
      manifest["group_hints"] == ["Simple Diff + Shaded", "Simple Diff + Shaded FACE"],
      "%s" % (manifest["group_hints"],))
say()
say("   the roles the port added")
check("declares a 'face' role on his FACE variant",
      (manifest.get("materials") or {}).get("face") == "Spork Shader Face",
      repr((manifest.get("materials") or {}).get("face")))
order = [rule.get("id") for rule in manifest["parts"]]
check("the face rule maps to that role", any(rule["id"] == "face" and rule.get("material") == "face"
                                             for rule in manifest["parts"]), "%s" % (order,))
check("a 'brows' rule for mayu, before 'face'",
      "brows" in order and order.index("brows") < order.index("face"), "%s" % (order,))
check("has a texture recipe for the face", "face" in (manifest.get("textures") or {}))
say()
say("   the decal role, which the Spork manifest went without until 2.35.18")
check("declares 'decal'", (manifest.get("materials") or {}).get("decal") == "Uma Decal",
      repr((manifest.get("materials") or {}).get("decal")))
check("has a decal texture recipe naming its own node",
      [rule.get("node") for rule in (manifest.get("textures") or {}).get("decal") or []] == ["Decal Map"],
      "%s" % ([rule.get("node") for rule in (manifest.get("textures") or {}).get("decal") or []],))
check("ships the blush for multiply decals",
      (manifest.get("shipped_textures") or {}).get("blush") == "shipped_textures/blush.png",
      repr((manifest.get("shipped_textures") or {}).get("blush")))
decal_rows = [row for row in rows if row.get("material") == "decal"]
check("drives the decal's opacity and tint, at the Looper manifest's keys and defaults",
      sorted(row["key"] for row in decal_rows) == ["uma_shader_decal_opacity", "uma_shader_decal_tint"]
      and [row["default"] for row in decal_rows if row["key"] == "uma_shader_decal_opacity"] == [1.0],
      "%s" % ([(row["key"], row["default"]) for row in decal_rows],))

say()
say("2. the library file")
bpy.ops.wm.open_mainfile(filepath=SHADERS + r"\SporkShaders.blend")
main_group = bpy.data.node_groups.get("Simple Diff + Shaded")
face_group = bpy.data.node_groups.get("Simple Diff + Shaded FACE")
if check("the general group is there", main_group is not None):
    # counting *every* node made this check fail on a cosmetic edit: the user added a second `Group Input` node to
    # lay the dirt chain out tidily, and a group may hold as many of those (and as many frames and reroutes) as its
    # author likes. The maths is what has to be his, so the UI nodes are excluded.
    ui_only = {'FRAME', 'REROUTE', 'GROUP_INPUT'}
    maths = [node for node in main_group.nodes if node.type not in ui_only]
    check("it is his maths plus the dirt chain (33 non-UI node(s))", len(maths) == 33,
          "%d of %d node(s)" % (len(maths), len(main_group.nodes)))
    names = {item.name for item in main_group.interface.items_tree
             if getattr(item, "in_out", None) == 'INPUT'}
    check("it offers his eight sockets plus the weather three",
          names == {"Diffuse", "Shaded", "Base", "Shadow Softness", "Emission Strength", "Highlight Color",
                    "Light Intensity", "Colored Light", "Dirt Map", "Dirt", "Dirt Tint"}, "%s" % (sorted(names),))
    dirt = [node for node in main_group.nodes if node.name.startswith("Dirt ")]
    check("the dirt chain is in it", len(dirt) == 7, "%s" % [node.name for node in dirt])
    # a Mix with the default MIX blend fades the surface towards the tint instead of multiplying by it
    taps = [main_group.nodes.get(name) for name in ("Dirt Diffuse", "Dirt Shaded")]
    check("its dirt multiplies rather than fades",
          all(node is not None and node.blend_type == 'MULTIPLY' for node in taps),
          "%s" % [getattr(node, "blend_type", None) for node in taps])
if check("the FACE variant is there", face_group is not None):
    ui_only = {'FRAME', 'REROUTE', 'GROUP_INPUT'}
    check("it is his maths plus the dirt chain (23 non-UI node(s))",
          len([node for node in face_group.nodes if node.type not in ui_only]) == 23,
          "%d of %d node(s)" % (len([node for node in face_group.nodes if node.type not in ui_only]),
                                len(face_group.nodes)))
    names = {item.name for item in face_group.interface.items_tree
             if getattr(item, "in_out", None) == 'INPUT'}
    check("it has no Base or Highlight Color, as his does",
          "Base" not in names and "Highlight Color" not in names, "%s" % (sorted(names),))
    # his FACE group's output is a plain colour, and `Colored Light` can route round the colour taps - so the dirt
    # has to sit on whatever the output actually is (see Tools/spork_dirt_fix.py)
    dirt_out = face_group.nodes.get("Dirt Colour")
    output = next((node for node in face_group.nodes if node.type == 'GROUP_OUTPUT'), None)
    check("its dirt multiplies the group's own output colour",
          dirt_out is not None and dirt_out.blend_type == 'MULTIPLY' and output is not None
          and bool(output.inputs["Color"].links)
          and output.inputs["Color"].links[0].from_node.name == "Dirt Colour",
          "%s" % (output.inputs["Color"].links[0].from_node.name if output is not None
                  and output.inputs["Color"].links else "unlinked",))
    check("and the old colour taps are gone, so it lands once",
          face_group.nodes.get("Dirt Diffuse") is None and face_group.nodes.get("Dirt Shaded") is None)

eye = bpy.data.materials.get("Spork Eye")
kept = bpy.data.materials.get("Spork Eye (Spork's own)")
if check("Spork Eye is there", eye is not None and eye.node_tree is not None):
    nodes = eye.node_tree.nodes
    check("it is the highlight graph (39 nodes)", len(nodes) == 39, "%d" % len(nodes))
    groups = sorted({node.node_tree.name for node in nodes
                     if node.type == 'GROUP' and node.node_tree is not None})
    check("the two highlight groups came with it",
          "NoiseGenerator" in groups and "SmoothThreshold" in groups, "%s" % (groups,))
    cry = nodes.get("Cry Switch")
    check("CrySwitch rests at 0", cry is not None and float(cry.outputs[0].default_value) == 0.0,
          "%r" % (float(cry.outputs[0].default_value) if cry else None,))
    check("no image is bound", not [node for node in nodes if node.type == 'TEX_IMAGE' and node.image])
    check("no shader tag", eye.get(ShaderManifest.SOURCE_PROPERTY) is None)
    check("the four image nodes the recipe names exist",
          all(nodes.get(name) is not None for name in ("Iris Map", "Highlight Map 0", "Highlight Map 1",
                                                       "Highlight Map 2")))
check("his own eye was kept, not thrown away", kept is not None,
      "%d node(s)" % len(kept.node_tree.nodes) if kept else "missing")

say()
say("   the masters, which is where the port lands")
for name, wanted_group, wanted_nodes in (("Spork Shader", "Simple Diff + Shaded", 22),
                                         ("Uma Brows", "Simple Diff + Shaded", 24),
                                         ("Spork Shader Face", "Simple Diff + Shaded FACE", 19),
                                         ("Spork Eye Inner", "Simple Diff + Shaded", 4)):
    material = bpy.data.materials.get(name)
    if not check("%s is in the library" % name, material is not None and material.node_tree is not None):
        continue
    check("  it wears %s" % wanted_group, group_of(material) == wanted_group, repr(group_of(material)))
    check("  %d node(s)" % wanted_nodes, len(material.node_tree.nodes) == wanted_nodes,
          "%d" % len(material.node_tree.nodes))
    if name != "Spork Eye Inner":        # the inner eye binds no texture map but its two, so it has no Dirt slot
        check("  it carries the Dirt Map slot", material.node_tree.nodes.get("Dirt Map") is not None)
    base = material.node_tree.nodes.get("Base Map")
    if wanted_group == "Simple Diff + Shaded FACE":
        check("  and no base-map chain", base is None, "Base Map present")
    elif name == "Spork Eye Inner":
        check("  and no base map, as the eye binds none", base is None, "Base Map present")
    else:
        check("  and its base-map chain", base is not None, "Base Map missing")
    if name != "Spork Eye Inner":
        # The see-through fix (2.35.18): a blended master puts every copy of the shader in EEVEE's blended pass,
        # which does not write depth, and a character's coplanar body slots then composite over each other in
        # whatever order EEVEE picks. Measured against the same materials forced OPAQUE, the blended setting is off
        # on 57.7% of the silhouette (44/255 darker); this setting is off on 0.
        check("  renders dithered-opaque like the Looper masters",
              (material.blend_method, material.surface_render_method, material.show_transparent_back)
              == ('HASHED', 'DITHERED', False),
              "%s" % ((material.blend_method, material.surface_render_method,
                       material.show_transparent_back),))
inner = bpy.data.materials.get("Spork Eye Inner")
if inner is not None:
    node = next(node for node in inner.node_tree.nodes if node.type == 'GROUP' and node.node_tree is not None)
    base = node.inputs.get("Base")
    check("the inner eye's Base rests at white",
          base is not None and tuple(round(value, 3) for value in base.default_value) == (1.0, 1.0, 1.0, 1.0),
          "%r" % (tuple(round(value, 3) for value in base.default_value) if base else None,))

slots = [slot.material.name if slot.material else None
         for obj in bpy.data.objects if obj.type == 'MESH' for slot in obj.material_slots]
for name in ("Spork Shader", "Spork Shader Face", "Uma Brows", "Spork Eye", "Spork Eye Inner",
             "Uma Decal", "Uma Outline Material"):
    check("the carrier sphere has a %r slot" % name, name in slots, "%s" % (slots,))

brows = bpy.data.materials.get("Uma Brows")
if check("Uma Brows is in his library", brows is not None and brows.node_tree is not None):
    output = next((node for node in brows.node_tree.nodes
                   if node.bl_idname == "ShaderNodeOutputMaterial"), None)
    scale = brows.node_tree.nodes.get("Vector Math")
    check("it displaces the brows in front", bool(output.inputs["Displacement"].links)
          and scale is not None and abs(scale.inputs["Scale"].default_value - 0.05) < 1e-6,
          "%s from %s" % (["Vector Math" if scale is None else "Vector Math at %r"
                           % scale.inputs["Scale"].default_value],
                          [link.from_node.name for link in output.inputs["Displacement"].links]))
    check("displacement_method is BOTH", brows.displacement_method == 'BOTH', brows.displacement_method)

say()
say("3. the hint matcher (exact names, not substrings)")
plain = ("Simple Diff + Shaded",)
declared = tuple(manifest["group_hints"])
for name, hints, wanted in (("Simple Diff + Shaded", plain, True),
                            ("Simple Diff + Shaded.001", plain, True),
                            ("Simple Diff + Shaded FACE", plain, False),
                            ("Simple Diff + Shaded FACE", declared, True),
                            ("Simple Diff + Shaded FACE.001", declared, True),
                            ("Simple Diff + Shaded FACEoff", declared, False),
                            ("Umaviewer Outline material", declared, False)):
    check("_hint_matches(%r, %s) is %s" % (name, "declared" if hints == declared else "plain", wanted),
          Control._hint_matches(name, hints) is wanted)

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

roles = {}
for slot in mesh.material_slots:
    material = slot.material
    if material is not None and material.node_tree is not None and material.get(ShaderManifest.SOURCE_PROPERTY):
        roles.setdefault(material.get(ShaderManifest.ROLE_PROPERTY), []).append(material)
say("     copies by role: %s" % {role: len(copies) for role, copies in sorted(roles.items())})
check("the body/hair/tail copies are 'default'", len(roles.get("default") or []) > 0)
if roles.get("face"):
    check("the face copy wears 19 nodes of the FACE variant",
          all(len(copy.node_tree.nodes) == 19 for copy in roles["face"]),
          "%s" % [len(copy.node_tree.nodes) for copy in roles["face"]])
    check("the face copy has no Base Map", all(copy.node_tree.nodes.get("Base Map") is None
                                               for copy in roles["face"]))
    # the control finds a copy's shader group by name, so a group the manifest does not declare is a copy with no
    # drivers at all - which is what the face silently was until 2.35.18 (`shader_nodes` matched `Simple Diff +
    # Shaded` exactly, and the FACE variant is a different name)
    face = roles["face"][0]
    check("the control sees the FACE group as a shader group",
          [node.node_tree.name for node in Control.shader_nodes(face)] == ["Simple Diff + Shaded FACE"],
          "%s" % ([node.node_tree.name for node in Control.shader_nodes(face)],))
    driven = {setting["key"] for setting in Control.SETTINGS if Control.setting_targets(face, setting)}
    say("     the face is driven for: %s" % sorted(driven))
    for key in ("spork_shadow_softness", "spork_emission_strength", "spork_light_intensity",
                "spork_colored_light", "uma_shader_wet", "uma_shader_dirt", "uma_shader_dirt_colour"):
        check("  %s reaches the face" % key, key in driven)
    check("  and the highlight colour does not, because his FACE group has no such socket",
          "spork_highlight_color" not in driven)

copy = next((slot.material for slot in mesh.material_slots
             if slot.material and slot.material.name.endswith("[eye]")), None)
if check("an [eye] copy exists", copy is not None, copy.name if copy else "none"):
    # the copy is renamed `<model material> [part]` by the apply step, so "it wears the ported graph" is a question
    # about the material it was copied from, not about the copy's own name
    master = Control.master_material(manifest, "eye", context)
    check("the master it was copied from is Spork Eye", master is not None and master.name.startswith("Spork Eye"),
          master.name if master else "none")
    check("39 nodes, i.e. the ported graph", len(copy.node_tree.nodes) == 39, "%d" % len(copy.node_tree.nodes))
    check("its role tag says eye", Control.material_role(copy) == "eye", repr(copy.get(ShaderManifest.ROLE_PROPERTY)))
    images = {node.name: (node.image.name if node.image else None)
              for node in copy.node_tree.nodes if node.type == 'TEX_IMAGE'}
    say("     bound images: %s" % images)
    check("the main eye texture is on `Iris Map`",
          (images.get("Iris Map") or "").endswith("eye0.png"), images.get("Iris Map"))
    check("the first highlight is on `Highlight Map 0`",
          (images.get("Highlight Map 0") or "").endswith("eyehi00.png"), images.get("Highlight Map 0"))
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
say("   the weather, which is the part the port had to keep")
body = (roles.get("default") or [None])[0]
if body is not None:
    images = {node.name: (node.image.name if node.image else None)
              for node in body.node_tree.nodes if node.type == 'TEX_IMAGE'}
    say("     body copy images: %s" % images)
    # whether they are *bound* depends on the export (only a texture-variant export writes the rain set); what the
    # library owes is the pair of slots beside every dry map, which the pass fills
    check("the rain slots are there", all(name in images for name in
                                          ("Wet Diffuse Map", "Wet Shaded Map", "Wet Base Map")),
          "%s" % (sorted(images),))
    check("and the Dirt Map slot is there", "Dirt Map" in images, "%s" % (sorted(images),))
    check("its group offers `Dirt` and `Dirt Map`",
          all(socket in [item.name for item in body.node_tree.nodes["Group"].inputs]
              for socket in ("Dirt", "Dirt Map")), "%s" % ([socket.name for socket in
                                                             body.node_tree.nodes["Group"].inputs],))
    knob = body.node_tree.nodes.get("Dry/Wet")
    check("the Dry/Wet knob is there", knob is not None and knob.type == 'VALUE')

say()
say("   the brow slot under his shader")
brow_copy = next((slot.material for slot in mesh.material_slots
                  if slot.material and slot.material.name.endswith("[brows]")), None)
if check("the brow slot is [brows]", brow_copy is not None, brow_copy.name if brow_copy else "none"):
    output = next((node for node in brow_copy.node_tree.nodes
                   if node.bl_idname == "ShaderNodeOutputMaterial"), None)
    check("its displacement is linked", bool(output.inputs["Displacement"].links),
          "%s" % [link.from_node.name for link in output.inputs["Displacement"].links])
    check("with method BOTH", brow_copy.displacement_method == 'BOTH', brow_copy.displacement_method)
    images = {node.name: (node.image.name if node.image else None)
              for node in brow_copy.node_tree.nodes if node.type == 'TEX_IMAGE'}
    say("     bound images: %s" % images)
    check("it binds the face atlas", (images.get("Diffuse Map") or "").endswith("face_diff.png"),
          images.get("Diffuse Map"))

say()
say("RESULT: %s" % ("all checks passed" if not failures else "FAILED - " + "; ".join(failures)))
with open(out, "w", encoding="utf-8") as handle:
    handle.write("\n".join(lines) + "\n")
print("wrote %s" % out)
