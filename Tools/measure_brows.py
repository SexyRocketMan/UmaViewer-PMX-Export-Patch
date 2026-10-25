"""The brows measurement: how many brow pixels are *visible* with the displacement, and without it.

    blender --background --factory-startup --python Tools/measure_brows.py -- <out.txt> [<file.pmx>]

The handoff's rule is "verify by measurement, not by eye", and the claim being verified is the user's own: with
`Geometry.Incoming -> Vector Math (SCALE 0.05) -> Displacement` and `displacement_method = BOTH`, the brows win the
depth test against the fringe in front of them. Measured on their file with flat materials: 106 brow pixels without
it, 970 with it (+815%).

So this replicates that measurement on a **freshly shaded** character, which is the thing the manifest change is
supposed to produce:

1. import and shade 1001 with `uma.apply_shader` - the real operator, the real manifest;
2. flatten every material to flat emission - the brow slot red, everything else black - so the only thing in the
   frame that can be red is a brow surface that survived the depth test. The brow material's tree is left alone
   except for its Surface link, so its displacement chain is still the shipped one;
3. drop the outline shell: it is a geometry-nodes shell around the whole body and it would paint over the brows for
   reasons that have nothing to do with this measurement;
4. put the camera on the brow region, along the brows' own average normal, framed to their bounding box - so the view
   is "in front of the face", which is where the fringe occludes them;
5. render 640x640 in EEVEE twice per configuration (EEVEE compiles lazily, so the first render of a configuration
   measures compilation - measured in this project before) and count red pixels.

The two configurations are the same scene, differing only in the brow material's displacement: linked + `BOTH`, and
unlinked + `BUMP`. That is the A/B; nothing else moves.
"""
import sys

import bpy
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]
out = argv[0]
pmx = argv[1] if len(argv) > 1 else r"C:\Users\user\Documents\umaviewer_exports\morphs\1001_nostream.pmx"
png_dir = out.rsplit("\\", 1)[0]

RESOLUTION = 640
SAMPLES = 8

lines = []


def say(text=""):
    lines.append(text)


def flatten(material, colour):
    """Flat emission of one colour, whatever the material was. Returns the nodes it added."""
    tree = material.node_tree
    output = next((node for node in tree.nodes if node.bl_idname == "ShaderNodeOutputMaterial"), None)
    emission = next((node for node in tree.nodes if node.bl_idname == "ShaderNodeEmission"
                     and node.name.startswith("UMA Measure")), None)
    if emission is None:
        emission = tree.nodes.new("ShaderNodeEmission")
        emission.name = "UMA Measure"
        emission.location = (600, 300)
    emission.inputs["Color"].default_value = (colour[0], colour[1], colour[2], 1.0)
    emission.inputs["Strength"].default_value = 1.0
    for link in list(output.inputs["Surface"].links):
        tree.links.remove(link)
    tree.links.new(emission.outputs["Emission"], output.inputs["Surface"])
    return output


def render(path):
    """Render, warm up twice (lazy shader compilation), then save through `Render Result`.

    The Synth Data add-on overrides `scene.render.filepath`, so `save_render` on the result image is the only path
    that is actually this render's (a trap recorded in the handoff).
    """
    scene = bpy.context.scene
    for _ in range(2):
        bpy.ops.render.render(write_still=False)
    bpy.ops.render.render(write_still=False)
    bpy.data.images["Render Result"].save_render(path)
    image = bpy.data.images.load(path, check_existing=False)
    pixels = list(image.pixels)
    counts = {"red": 0, "bright": 0, "total": 0}
    for index in range(0, len(pixels), 4):
        red, green, blue = pixels[index], pixels[index + 1], pixels[index + 2]
        counts["total"] += 1
        if red > 0.5 and green < 0.3 and blue < 0.3:
            counts["red"] += 1
        if red + green + blue > 0.05:
            counts["bright"] += 1
    bpy.data.images.remove(image)
    return counts


bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.preferences.addon_enable(module="bl_ext.blender_org.mmd_tools")
bpy.ops.preferences.addon_enable(module="bl_ext.user_default.uma_addon")
bpy.ops.mmd_tools.import_model(filepath=pmx)

meshes = [obj for obj in bpy.context.scene.objects if obj.type == 'MESH']
mesh = max(meshes, key=lambda item: len(item.data.vertices))
for obj in bpy.context.view_layer.objects:
    obj.select_set(False)
mesh.select_set(True)
bpy.context.view_layer.objects.active = mesh
say("shading %r (%d vertices)" % (mesh.name, len(mesh.data.vertices)))
result = bpy.ops.uma.apply_shader()
say("operator result: %s" % (result,))

brow = next((slot.material for slot in mesh.material_slots
             if slot.material and "[brows]" in slot.material.name), None)
if brow is None:
    say("FAILED: no [brows] slot after shading")
    with open(out, "w", encoding="utf-8") as handle:
        handle.write("\n".join(lines) + "\n")
    raise SystemExit(1)
say("brow material: %r" % brow.name)

# --- the view: along the brows' own average normal ------------------------------------------------------
# The shading run separates the face into its own object, and the brow faces go with it, so the brow slot is looked
# for on *every* mesh: measuring the one object the operator was handed found no brow faces at all.
positions = []
normals = Vector((0.0, 0.0, 0.0))
holders = []
for obj in [item for item in bpy.context.scene.objects if item.type == 'MESH']:
    slots = [index for index, slot in enumerate(obj.material_slots) if slot.material is brow]
    if not slots:
        continue
    holders.append("%s(%.2f)" % (obj.name, obj.matrix_world.to_scale().x))
    world = obj.matrix_world
    for polygon in obj.data.polygons:
        if polygon.material_index not in slots:
            continue
        positions.append(world @ polygon.center)
        normals += world.to_3x3() @ polygon.normal
say("meshes holding brow faces: %s" % holders)
if not positions:
    say("FAILED: the brow slot has no faces on any mesh")
    with open(out, "w", encoding="utf-8") as handle:
        handle.write("\n".join(lines) + "\n")
    raise SystemExit(1)
centre = sum(positions, Vector((0.0, 0.0, 0.0))) / len(positions)
normal = normals.normalized()
radius = max((position - centre).length for position in positions)
say("brow faces: %d, centre %s, average normal %s, radius %.4f"
    % (len(positions), tuple(round(value, 4) for value in centre),
       tuple(round(value, 3) for value in normal), radius))

camera_data = bpy.data.cameras.new("Brow Measure")
camera = bpy.data.objects.new("Brow Measure", camera_data)
bpy.context.scene.collection.objects.link(camera)
camera_data.lens = 50.0
distance = max(radius * 4.0, 0.05)
camera.location = centre + normal * distance
camera.rotation_mode = 'QUATERNION'
camera.rotation_quaternion = (centre - camera.location).to_track_quat('-Z', 'Y')
bpy.context.scene.camera = camera
say("camera at %s, distance %.4f" % (tuple(round(value, 4) for value in camera.location), distance))

# --- a scene where only colour can come from a material ---------------------------------------------------
scene = bpy.context.scene
# 5.2 renamed the engine back: EEVEE Next was `BLENDER_EEVEE_NEXT` in 4.2-4.4 and is `BLENDER_EEVEE` again, so the
# id is picked from what this Blender offers rather than hardcoded
engines = [item.identifier for item in scene.render.bl_rna.properties["engine"].enum_items]
scene.render.engine = next((name for name in ("BLENDER_EEVEE", "BLENDER_EEVEE_NEXT") if name in engines), engines[0])
try:
    scene.eevee.taa_render_samples = SAMPLES
except AttributeError:
    scene.eevee.taa_samples = SAMPLES
scene.render.resolution_x = RESOLUTION
scene.render.resolution_y = RESOLUTION
scene.render.resolution_percentage = 100
scene.render.film_transparent = False
world_data = bpy.data.worlds.new("Measure")
world_data.use_nodes = True
background = world_data.node_tree.nodes["Background"]
background.inputs["Color"].default_value = (0.0, 0.0, 0.0, 1.0)
background.inputs["Strength"].default_value = 0.0
scene.world = world_data

# the outline shell is a geometry-nodes shell around the whole body: it is not part of this question, and it would
# sit between the camera and the brows
removed = 0
for obj in bpy.context.scene.objects:
    for modifier in list(obj.modifiers):
        if modifier.type == 'NODES' and "Uma Outlines" in modifier.node_group.name:
            obj.modifiers.remove(modifier)
            removed += 1
say("outline shell modifiers removed: %d" % removed)

# every mesh's materials are flattened: one brow colour, everything else black. Shared materials are visited twice
# and that is harmless - the same emission node is re-used by name.
flattened = set()
for obj in [item for item in bpy.context.scene.objects if item.type == 'MESH']:
    for slot in obj.material_slots:
        if slot.material is None or slot.material.name in flattened:
            continue
        flattened.add(slot.material.name)
        flatten(slot.material, (1.0, 0.0, 0.0) if slot.material is brow else (0.0, 0.0, 0.0))
say("materials flattened: %d" % len(flattened))

tree = brow.node_tree
output = next(node for node in tree.nodes if node.bl_idname == "ShaderNodeOutputMaterial")
scale = tree.nodes["Vector Math"]

# --- A: as shipped ----------------------------------------------------------------------------------------
brow.displacement_method = 'BOTH'
counts_with = render(png_dir + "\\_brows_with_displacement.png")
say("with displacement:    %d red pixel(s) of %d (%d lit)" % (counts_with["red"], counts_with["total"],
                                                              counts_with["bright"]))

# --- B: the same scene with the displacement taken out ----------------------------------------------------
for link in list(output.inputs["Displacement"].links):
    tree.links.remove(link)
brow.displacement_method = 'BUMP'
counts_without = render(png_dir + "\\_brows_without_displacement.png")
say("without displacement: %d red pixel(s) of %d (%d lit)" % (counts_without["red"], counts_without["total"],
                                                              counts_without["bright"]))

say()
if counts_without["red"] > 0:
    change = 100.0 * (counts_with["red"] - counts_without["red"]) / counts_without["red"]
    say("RESULT: %d -> %d brow pixels (%+.1f%%), scale %.2f"
        % (counts_without["red"], counts_with["red"], change, scale.inputs["Scale"].default_value))
else:
    say("RESULT: %d -> %d brow pixels (the brows were not visible at all without it, so the ratio is undefined)"
        % (counts_without["red"], counts_with["red"]))

with open(out, "w", encoding="utf-8") as handle:
    handle.write("\n".join(lines) + "\n")
print("wrote %s" % out)
