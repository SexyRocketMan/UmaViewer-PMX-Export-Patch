"""Are the decals actually unlit now - measured against what they were before?

    blender --background --factory-startup --python Tools/verify_decals.py -- <out.txt> [<file.pmx>]

Structural checks first (every decal slot wears the library's `Uma Decal`, keeps its name and its own map, still gets
the spin and the tear scroll, and the two new rows drive it), then the comparison the whole change exists for.

**Why it is a two-run comparison.** The first attempt measured "does the decal change when the lighting changes" by
recolouring the *body*, and got a bigger change on the decal's pixels than on the rest of the frame - because an
alpha-blended decal is `decal*alpha + face*(1-alpha)`, so it inherits whatever the face underneath it does. No
single-run control can separate those. What can is the same scene shaded twice, once with the manifest declaring a
`decal` role and once without it:

* **without** the role, the decals keep mmd_tools' own material, which lights from the **scene's lights** - so a real
  lamp switched on changes their pixels;
* **with** the role, they are the library's emission graph - so the same lamp changes nothing.

The lamp has to be a real light: the `Uma Sun` empty is not one. (Measured the hard way: relocating that empty 1.5
units behind the head changed not a single pixel, so it is not how this shader's direction is taken either.)

The decal region is the pixels the `Decal opacity` knob changes, taken from the run that has the role.
"""
import sys

import bpy
from mathutils import Vector

ADDON = "bl_ext.user_default.uma_addon"
PMX_DEFAULT = r"C:\Users\user\Documents\umaviewer_exports\morphs\1001_nostream.pmx"

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]
out = argv[0]
pmx = argv[1] if len(argv) > 1 else PMX_DEFAULT
png_dir = out.rsplit("\\", 1)[0]

RESOLUTION = 480
SAMPLES = 8

lines = []
failures = []


def say(text=""):
    lines.append(text)


def check(label, ok, detail=""):
    say("  [%s] %s%s" % ("PASS" if ok else "FAIL", label, (" - " + detail) if detail else ""))
    if not ok:
        failures.append(label)
    return ok


def bail(reason):
    """Write what is known and stop - a report written only at the end is no report when the run dies."""
    say()
    say("BAILED: %s" % reason)
    say("RESULT: FAILED - %s" % reason)
    with open(out, "w", encoding="utf-8") as handle:
        handle.write("\n".join(lines) + "\n")
    print("wrote %s" % out)
    raise SystemExit(1)


def enable():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.preferences.addon_enable(module="bl_ext.blender_org.mmd_tools")
    bpy.ops.preferences.addon_enable(module=ADDON)


def render(path):
    """Render after two discarded warm-ups (EEVEE compiles lazily), saved through `Render Result`."""
    for _ in range(2):
        bpy.ops.render.render(write_still=False)
    bpy.ops.render.render(write_still=False)
    bpy.data.images["Render Result"].save_render(path)
    image = bpy.data.images.load(path, check_existing=False)
    pixels = [0.0] * (image.size[0] * image.size[1] * 4)
    image.pixels.foreach_get(pixels)
    size = (image.size[0], image.size[1])
    bpy.data.images.remove(image)
    return pixels, size


def difference(a, b, indices):
    """Mean per-pixel |a-b| over the given pixel indices."""
    if not indices:
        return 0.0
    total = 0.0
    for index in indices:
        base = index * 4
        total += (abs(a[base] - b[base]) + abs(a[base + 1] - b[base + 1]) + abs(a[base + 2] - b[base + 2])) / 3.0
    return total / len(indices)


# --- 1. the manifest -------------------------------------------------------------------------------------
enable()
from bl_ext.user_default.uma_addon.uma_addon.addons.uma_addon.operators import (  # noqa: E402
    MorphDrivers, ShaderManifest, UmaShaderControl as Control)

context = bpy.context
manifest, problems = ShaderManifest.load(ShaderManifest.DEFAULT_ID)

say("=" * 100)
say("1. the manifest")
check("loads with no problems", not problems, "; ".join(problems) or "clean")
check("declares a 'decal' role", (manifest.get("materials") or {}).get("decal") == "Uma Decal",
      repr((manifest.get("materials") or {}).get("decal")))
check("has a 'decal' texture recipe", "decal" in (manifest.get("textures") or {}))
rows = [row for row in manifest["settings"] if row.get("material") == "decal"]
check("two decal rows", len(rows) >= 2, "%s" % [row["key"] for row in rows])


# --- the scene a decal is measured in: imported, shaded, cheek deployed, camera on the face -----------------
def prepare(with_decal_role, label):
    """Import, shade (with or without the decal role), deploy the cheek, frame it. Returns what was found."""
    enable()
    local_manifest, _problems = ShaderManifest.load(ShaderManifest.DEFAULT_ID)
    if not with_decal_role:
        # the apply step looks the decal master up by role, so a manifest without the role leaves the decals to
        # mmd_tools - which is exactly the "before" of this comparison
        local_manifest["materials"].pop("decal", None)
    context = bpy.context
    bpy.ops.mmd_tools.import_model(filepath=pmx)
    mesh = max([obj for obj in context.scene.objects if obj.type == 'MESH'],
               key=lambda item: len(item.data.vertices))
    for obj in context.view_layer.objects:
        obj.select_set(False)
    mesh.select_set(True)
    context.view_layer.objects.active = mesh
    bpy.ops.uma.apply_shader()

    decal_names = MorphDrivers.decal_materials()
    copies = []
    for obj in [item for item in context.scene.objects if item.type == 'MESH']:
        for slot in obj.material_slots:
            material = slot.material
            if material is None:
                continue
            if any(material.name == name or material.name.startswith(name + ".")
                   for name in decal_names) and material not in copies:
                copies.append(material)

    # deploy the cheek: the decal quads are collapsed onto a point until the morph is at 1
    key = None
    for obj in [item for item in context.scene.objects if item.type == 'MESH']:
        blocks = getattr(obj.data.shape_keys, "key_blocks", None) if obj.data.shape_keys else None
        if blocks and "Cheek_Blush" in blocks:
            key = blocks["Cheek_Blush"]
            break
    if key is None:
        bail("%s: no Cheek_Blush deploy morph" % label)
    key.value = 1.0
    context.view_layer.update()

    # The view comes from the **brows**: a small patch on the front of the face, so its average normal is the one
    # thing here that reliably points at the camera. The average normal of the whole face mesh points *into* the head
    # (measured: +Y where the brows read -Y), and a camera placed on it renders the back of the head - which is how an
    # earlier version of this "measured" two identical empty frames.
    #
    # Every mesh, and the base mesh: the shading run separates the face into its own object, so the brows live on the
    # face piece, and `to_mesh()` on these meshes reports one `material_index` for every polygon (measured: 17718
    # polygons, one index), so material lookup has to read `obj.data.polygons`.
    def slots_named(obj, suffix):
        return [index for index, slot in enumerate(obj.material_slots)
                if slot.material is not None and slot.material.name.endswith(suffix)]

    positions = []
    normals = Vector((0.0, 0.0, 0.0))
    for obj in [item for item in context.scene.objects if item.type == 'MESH']:
        slots = slots_named(obj, "[brows]") or slots_named(obj, "[eye]")
        if not slots:
            continue
        world = obj.matrix_world
        for polygon in obj.data.polygons:
            if polygon.material_index in slots:
                positions.append(world @ polygon.center)
                normals += world.to_3x3() @ polygon.normal
    if not positions:
        bail("%s: no brows or eye faces to take the view from" % label)
    centre = sum(positions, Vector((0.0, 0.0, 0.0))) / len(positions)
    normal = normals.normalized()
    extent = max((mesh.matrix_world @ Vector(corner) - centre).length for corner in mesh.bound_box)

    camera_data = bpy.data.cameras.new("Decal Measure")
    camera = bpy.data.objects.new("Decal Measure", camera_data)
    context.scene.collection.objects.link(camera)
    camera_data.lens = 50.0
    camera.location = centre + normal * max(extent * 0.35, 0.08)
    camera.rotation_mode = 'QUATERNION'
    camera.rotation_quaternion = (centre - camera.location).to_track_quat('-Z', 'Y')
    context.scene.camera = camera

    scene = context.scene
    engines = [item.identifier for item in scene.render.bl_rna.properties["engine"].enum_items]
    scene.render.engine = next((name for name in ("BLENDER_EEVEE", "BLENDER_EEVEE_NEXT") if name in engines),
                               engines[0])
    try:
        scene.eevee.taa_render_samples = SAMPLES
    except AttributeError:
        scene.eevee.taa_samples = SAMPLES
    scene.render.resolution_x = RESOLUTION
    scene.render.resolution_y = RESOLUTION
    scene.render.resolution_percentage = 100
    scene.render.film_transparent = False

    # a real lamp, because only a real lamp can light an mmd_tools material
    light_data = bpy.data.lights.new("Decal Probe", 'SUN')
    light_data.energy = 20.0
    light = bpy.data.objects.new("Decal Probe", light_data)
    context.scene.collection.objects.link(light)
    light.rotation_mode = 'QUATERNION'
    light.rotation_quaternion = (centre - (centre + normal * 2.0)).to_track_quat('-Z', 'Y')

    sun = Control.sun_object(context)
    root = Control.root_object(mesh)
    return {"context": context, "copies": copies, "sun": sun, "root": root, "light": light_data,
            "centre": centre, "normal": normal, "extent": extent}


say()
say("2. shade it, and look at what a decal slot wears")
scene_with = prepare(True, "with the decal role")
copies = scene_with["copies"]
say("     decal materials after Shading: %d" % len(copies))
unlit = 0
for material in copies:
    tree = material.node_tree
    group = any(node.type == 'GROUP' and node.node_tree is not None
                and node.node_tree.name == "Uma Shader" for node in tree.nodes)
    images = [node.image.name for node in tree.nodes if node.type == 'TEX_IMAGE' and node.image]
    alpha = any(link.from_node.type == 'TEX_IMAGE' and link.from_socket.name == "Alpha"
                for node in tree.nodes if node.bl_idname == "ShaderNodeMath"
                for link in node.inputs[0].links)
    emission = any(node.bl_idname == "ShaderNodeEmission" for node in tree.nodes)
    ok = (not group) and emission and bool(images) and alpha and material.surface_render_method == 'BLENDED'
    unlit += 1 if ok else 0
    say("     %-32s role=%-6r %-8s nodes=%2d map=%s unlit=%s alpha=%s"
        % (material.name, material.get(ShaderManifest.ROLE_PROPERTY), material.surface_render_method,
           len(tree.nodes), images[0] if images else None, not group, alpha))
check("every decal slot is the library's unlit copy", unlit == len(copies), "%d of %d" % (unlit, len(copies)))
check("the @_@ spin still landed on its copy",
      any(node.label == "Spin" for material in copies for node in material.node_tree.nodes))
check("the tear scroll still landed on the sheets",
      any(node.label == "Tear Scroll" for material in copies for node in material.node_tree.nodes))

say()
say("3. the knobs")
Control.use_table(context, manifest=ShaderManifest.load(ShaderManifest.DEFAULT_ID)[0])
report = Control.hook(context)
wrong_counts = []
for material in copies:
    drivers = material.node_tree.animation_data.drivers if material.node_tree.animation_data else []
    mine = [curve for curve in drivers if curve.data_path in Control._own_driver_paths(material)]
    # derived from the table, not written down: the count went stale the moment the black-key row was added
    expected = sum(4 if row["kind"] == "colour" else 1 for row in rows)
    if len(mine) != expected:
        wrong_counts.append((material.name, len(mine), expected))
check("one driver per decal knob per copy", not wrong_counts, "%s" % wrong_counts[:3])
check("nothing reported missing", not any(entry["missing"] for entry in report["characters"]),
      "%s" % [entry["missing"] for entry in report["characters"] if entry["missing"]][:2])
check("verify() is clean", report["verified"]["wrong_count"] == 0,
      "%d wrong of %d checked" % (report["verified"]["wrong_count"], report["verified"]["checked"]))

say()
say("4. the measurement: the same lamp, with the decal role and without it")
sun, root = scene_with["sun"], scene_with["root"]
Control.write_value(sun, "uma_shader_decal_opacity", 1.0)
Control._settle(context, sun, root)
scene_with["light"].energy = 0.0
opaque, size = render(png_dir + "\\_decal_lamp_off.png")
Control.write_value(sun, "uma_shader_decal_opacity", 0.0)
Control._settle(context, sun, root)
hidden, _size = render(png_dir + "\\_decal_hidden.png")
pixels = size[0] * size[1]
decal_area = [index for index in range(pixels)
              if (abs(opaque[index * 4] - hidden[index * 4])
                  + abs(opaque[index * 4 + 1] - hidden[index * 4 + 1])
                  + abs(opaque[index * 4 + 2] - hidden[index * 4 + 2])) > 0.02]
check("the opacity knob changes a real area of the frame", len(decal_area) > pixels * 0.002,
      "%d of %d pixel(s) (%.2f%%)" % (len(decal_area), pixels, 100.0 * len(decal_area) / pixels))
Control.write_value(sun, "uma_shader_decal_opacity", 1.0)
Control._settle(context, sun, root)
scene_with["light"].energy = 20.0
lit_with, _size = render(png_dir + "\\_decal_lamp_on_with_role.png")
after = difference(opaque, lit_with, decal_area)
after_frame = difference(opaque, lit_with, [i for i in range(pixels) if i not in set(decal_area)])
say("     with the role:    decal pixels change %.5f, the rest of the frame %.5f" % (after, after_frame))

scene_without = prepare(False, "without the decal role")
say("     decals in the 'without' run: %d material(s), first method %s"
    % (len(scene_without["copies"]),
       scene_without["copies"][0].surface_render_method if scene_without["copies"] else "none"))
not_unlit = [material.name for material in scene_without["copies"]
             if not any(node.bl_idname == "ShaderNodeEmission" for node in material.node_tree.nodes)]
check("without the role the decals are mmd_tools' materials again", bool(not_unlit),
      "%d of %d are not emission graphs" % (len(not_unlit), len(scene_without["copies"])))
scene_without["light"].energy = 0.0
before_pixels, _size = render(png_dir + "\\_decal_before_lamp_off.png")
scene_without["light"].energy = 20.0
before_lit, _size = render(png_dir + "\\_decal_before_lamp_on.png")
before = difference(before_pixels, before_lit, decal_area)
before_frame = difference(before_pixels, before_lit, [i for i in range(pixels) if i not in set(decal_area)])
say("     without the role: decal pixels change %.5f, the rest of the frame %.5f" % (before, before_frame))

check("before: the lamp changes the decals (they were lit)", before > 0.01, "%.5f" % before)
check("after: the same lamp changes them by nothing", after < 0.01, "%.5f" % after)
check("the change is the fix, not a coincidence", before > 5 * max(after, 1e-5),
      "%.5f before vs %.5f after" % (before, after))

say()
say("RESULT: %s" % ("all checks passed" if not failures else "FAILED - " + "; ".join(failures)))
with open(out, "w", encoding="utf-8") as handle:
    handle.write("\n".join(lines) + "\n")
print("wrote %s" % out)
