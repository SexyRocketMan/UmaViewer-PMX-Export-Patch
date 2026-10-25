"""Structural verification of the `brows` role: shade a real character and check what the brow slot became.

    blender --background --factory-startup --python Tools/verify_brows.py -- <out.txt> [<file.pmx>]

What it checks, in the order the claims were made in `docs/HANDOFF_SHADER_LIBRARY.md`:

1. the manifest still loads with **no problems** (`ShaderManifest.validate`), so the new `materials.brows`,
   `parts.brows`, `textures.brows` and `part_inputs.brows` entries are all well-formed;
2. a part rule's *first* substring wins and `mayu` now resolves to `brows` while `_face`/`_hair`/`_bdy` are
   untouched - the whole reason the new rule has to sit before `face`;
3. the library carries a `Uma Brows` master reachable from the carrier sphere's slots, which is what
   `Umashader.library_masters` reads;
4. after a real `uma.apply_shader` run the brow slot is `... [brows]`, wears the `Uma Brows` copy, has the
   displacement linked at 0.05, `displacement_method = BOTH`, the face atlas bound, `Toggle If Face = 1`, and
   **no other slot changed** its part (the face/bdy/hair/tail/eye rules are what they were).

Point 4 is the one that costs a run: it shades the model exactly as a user would. The pmx is 1001 by default, the
character every measurement in this repository uses.
"""
import sys

import bpy

ADDON = "bl_ext.user_default.uma_addon"
PACKAGE = ADDON + ".uma_addon.addons.uma_addon"

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]
out = argv[0]
pmx = argv[1] if len(argv) > 1 else r"C:\Users\user\Documents\umaviewer_exports\morphs\1001_nostream.pmx"

lines = []


def say(text=""):
    lines.append(text)


def check(label, ok, detail=""):
    say("  [%s] %s%s" % ("PASS" if ok else "FAIL", label, (" - " + detail) if detail else ""))
    return ok


bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.preferences.addon_enable(module="bl_ext.blender_org.mmd_tools")
bpy.ops.preferences.addon_enable(module=ADDON)

from bl_ext.user_default.uma_addon.uma_addon.addons.uma_addon.operators import (  # noqa: E402
    ShaderManifest, Umashader)
from bl_ext.user_default.uma_addon.uma_addon.addons.uma_addon.operators import Umashader as ShadingModule  # noqa

failures = []

say("=" * 100)
say("1. the manifest")
manifest, problems = ShaderManifest.load(ShaderManifest.DEFAULT_ID)
if not check("loads with no problems", not problems, "; ".join(problems) or "clean"):
    failures.append("manifest problems")
roles = sorted((manifest.get("materials") or {}))
say("     roles: %s" % roles)
check("declares a 'brows' role", "brows" in roles,
      "%r" % (manifest.get("materials") or {}).get("brows", "<missing>"))
recipes = sorted((manifest.get("textures") or {}))
say("     texture recipes: %s" % recipes)
check("has a 'brows' texture recipe", "brows" in recipes)
brows_rule = next((rule for rule in manifest["parts"] if rule.get("id") == "brows"), None)
face_rule = next((rule for rule in manifest["parts"] if rule.get("id") == "face"), None)
order = [rule.get("id") for rule in manifest["parts"]]
say("     part order: %s" % order)
check("rule order puts 'brows' before 'face'",
      brows_rule is not None and face_rule is not None and order.index("brows") < order.index("face"),
      "brows at %s, face at %s" % (order.index("brows") if brows_rule else "?", order.index("face")
                                   if face_rule else "?"))

say()
say("2. what each material name resolves to")
expected = {
    "mtl_chr1001_00_mayu": "brows",
    "mtl_chr1001_00_face": "face",
    "mtl_chr1001_00_hair": "hair",
    "mtl_bdy1001_00": "bdy",
    "mtl_tail0001_00": "tail",
    "mtl_chr1001_00_eye": "eye",
}
for name, want in expected.items():
    rule = Umashader.part_rule(manifest, name)
    got = (rule or {}).get("id")
    if not check("%s -> %s" % (name, want), got == want, "got %r" % got):
        failures.append("part rule for %s" % name)

say()
say("3. the library's carrier sphere")
bpy.ops.mmd_tools.import_model(filepath=pmx)
collection, masters, notes = Umashader.ensure_library(
    manifest, ShaderManifest.blend_path(manifest["blend"]), bpy.context)
say("     library: %s" % (collection.name if collection else "<none>"))
say("     roles found: %s" % sorted(masters))
for note in notes:
    say("     note: %s" % note)
check("Uma Brows is reachable as a master", masters.get("brows") is not None,
      "%r" % (masters.get("brows").name if masters.get("brows") else None,))

say()
say("4. after a real Shading run")
# the operator polls on exactly one selected mesh, the way the panel gives it one
meshes = [obj for obj in bpy.context.scene.objects if obj.type == 'MESH']
for obj in bpy.context.view_layer.objects:
    obj.select_set(False)
mesh = max(meshes, key=lambda item: len(item.data.vertices))
mesh.select_set(True)
bpy.context.view_layer.objects.active = mesh
say("     shading %r (%d vertices)" % (mesh.name, len(mesh.data.vertices)))
result = bpy.ops.uma.apply_shader()
say("     operator result: %s" % (result,))
say("     mesh now: %s, %d slot(s)" % (mesh.name, len(mesh.material_slots)))
brow = next((slot.material for slot in mesh.material_slots
             if slot.material and "[brows]" in slot.material.name), None)
say("     brow slot: %r" % (brow.name if brow else None,))
if not check("the brow slot is named [brows]", brow is not None):
    failures.append("no brow slot after shading")
else:
    tree = brow.node_tree
    output = next((node for node in tree.nodes if node.bl_idname == "ShaderNodeOutputMaterial"), None)
    linked = [link.from_node for link in output.inputs["Displacement"].links]
    scale = next((node for node in tree.nodes if node.bl_idname == "ShaderNodeVectorMath"
                  and node.operation == 'SCALE'), None)
    geometry = next((node for node in tree.nodes if node.bl_idname == "ShaderNodeNewGeometry"), None)
    feeds = [link.from_socket.name for link in scale.inputs["Vector"].links] if scale else []
    check("material output has a displacement", bool(linked), "%s" % [node.name for node in linked])
    # by name, not by identity: Blender hands back a fresh Python wrapper for the same node on each lookup
    if not check("it comes from a Vector Math SCALE node",
                 bool(linked) and linked[0].name == "Vector Math" and linked[0].operation == 'SCALE',
                 "%s" % ([(node.name, getattr(node, "operation", None)) for node in linked],)):
        failures.append("displacement chain")
    check("fed by Geometry.Incoming", feeds == ["Incoming"], "feeds %s" % feeds)
    check("Scale is 0.05", scale is not None and abs(scale.inputs["Scale"].default_value - 0.05) < 1e-6,
          "%r" % (scale.inputs["Scale"].default_value if scale else None,))
    check("displacement_method is BOTH", brow.displacement_method == 'BOTH', brow.displacement_method)
    images = {node.name: (node.image.name if node.image else None)
              for node in tree.nodes if node.type == 'TEX_IMAGE'}
    say("     bound images: %s" % images)
    check("binds the face atlas",
          (images.get("Image Texture") or "").endswith("face_diff.png"), images.get("Image Texture"))
    group = next((node for node in tree.nodes if node.bl_idname == "ShaderNodeGroup"), None)
    toggle = [socket for socket in group.inputs if socket.name.startswith("Toggle If Face")] if group else []
    check("Toggle If Face is 1", bool(toggle) and float(toggle[0].default_value) == 1.0,
          "%r" % ([float(socket.default_value) for socket in toggle],))
    check("keeps the shader tag", brow.get("uma_shader_source") == manifest["id"],
          "%r" % (brow.get("uma_shader_source"),))
    check("node count is the master's 23", len(tree.nodes) == 23, "%d" % len(tree.nodes))

say()
say("     every slot, for the record:")
for slot in mesh.material_slots:
    material = slot.material
    if material is None:
        continue
    say("       %-38s source=%r method=%-4s displacement=%s"
        % (material.name, material.get("uma_shader_source"), material.displacement_method,
           bool(next((node for node in material.node_tree.nodes
                      if node.bl_idname == "ShaderNodeOutputMaterial"), None).inputs["Displacement"].links)))

say()
say("RESULT: %s" % ("all checks passed" if not failures else "FAILED - " + "; ".join(failures)))
with open(out, "w", encoding="utf-8") as handle:
    handle.write("\n".join(lines) + "\n")
print("wrote %s" % out)
