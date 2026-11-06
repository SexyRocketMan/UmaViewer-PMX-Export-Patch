"""Build a review .blend: the fixtures in one row, a label over each head, and the run's own log inside the file.

    blender -b --factory-startup -P Tools/build_review_scene.py -- --out review.blend [options] label=path.pmx ...

Options
    --out PATH        where to save the .blend (required)
    --render PATH     also render a preview PNG of the row
    --refine          run `Refine Structure` on each model
    --shade           run `Shading` on each model
    --normals         run the face-normal transfer on each model
    --morphs          run the advanced-morphs step on each model
    --spacing FLOAT   gap between models, as a fraction of the model's own width (default 0.18)
    --proportion      how many screen widths one model's height may use for the preview render (default 0.75)
    --fixtures PATH   a JSON file {"fixtures": [{"label": ..., "pmx": ...}, ...]} instead of label=path pairs
    --title TEXT      a line above the row

Why
---
A pipeline that raises nothing has not necessarily done its job: the eye controls can build themselves from the
wrong morph, the donor can lose the keys it follows, a decal can stay lit, and every one of those is silent in the
console and obvious in a picture. This writes the *state* after a run - models side by side, each labelled with what
was run on it and the numbers that decide whether it worked - so the result can be judged by looking at it, and
compared against last week's by opening the other file.

The .blend also carries the add-on's run report as a text block (`uma_run_report`), so the file explains itself
without the log it was built from.
"""
import json
import os
import sys

import bpy
from mathutils import Vector

ADDON = "bl_ext.user_default.uma_addon"

#: prefix for every object this tool creates, so a review scene can be emptied again
MARK = "uma_review_"


def parse_args(argv):
    options = {"out": None, "render": None, "refine": False, "shade": False, "normals": False,
               "morphs": False, "spacing": 0.18, "proportion": 0.75, "fixtures": None, "title": None,
               "opaque": False, "rig": False, "all": False}
    fixtures = []
    index = 0
    while index < len(argv):
        token = argv[index]
        if token == "--out":
            index += 1
            options["out"] = argv[index]
        elif token == "--render":
            index += 1
            options["render"] = argv[index]
        elif token == "--spacing":
            index += 1
            options["spacing"] = float(argv[index])
        elif token == "--proportion":
            index += 1
            options["proportion"] = float(argv[index])
        elif token == "--fixtures":
            index += 1
            options["fixtures"] = argv[index]
        elif token == "--title":
            index += 1
            options["title"] = argv[index]
        elif token == "--rig":
            options["rig"] = True
        elif token == "--all":
            options["all"] = True
        elif token == "--opaque":
            options["opaque"] = True
        elif token in ("--refine", "--shade", "--normals", "--morphs"):
            options[token[2:]] = True
        elif token.startswith("--"):
            raise SystemExit("unknown option %s" % token)
        else:
            label, _, path = token.partition("=")
            if not path:
                raise SystemExit("fixtures are given as label=path.pmx (got %r)" % token)
            fixtures.append((label, path))
        index += 1

    if options["all"]:
        for flag in ("refine", "shade", "normals", "morphs"):
            options[flag] = True
    if options["fixtures"]:
        with open(options["fixtures"], encoding="utf-8") as handle:
            data = json.load(handle)
        for entry in data.get("fixtures", []):
            fixtures.append((entry.get("label") or os.path.basename(entry["pmx"]), entry["pmx"]))
        options["title"] = options["title"] or data.get("title")
    if not options["out"]:
        raise SystemExit("--out is required")
    if not fixtures:
        raise SystemExit("no fixtures: pass label=path.pmx pairs or --fixtures manifest.json")
    return options, fixtures


def enable_addons():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.preferences.addon_enable(module="bl_ext.blender_org.mmd_tools")
    try:
        bpy.ops.preferences.addon_enable(module=ADDON)
    except Exception as exc:                     # noqa: BLE001 - a layout-only review needs no add-on
        print("[review] the uma add-on did not load (%s); steps that need it will be skipped" % exc)


def scene_objects():
    return set(bpy.context.scene.objects)


def import_model(path, label):
    """Import one pmx, rename what it brought in after `label`, and return its root empty and mesh."""
    before = scene_objects()
    bpy.ops.mmd_tools.import_model(filepath=path, scale=0.08, types={'MESH', 'ARMATURE', 'MORPHS'})
    added = [obj for obj in bpy.context.scene.objects if obj not in before]
    roots = [obj for obj in added if obj.parent is None]
    meshes = [obj for obj in added if obj.type == 'MESH']
    for obj in added:
        obj.name = "%s:%s" % (label, obj.name)
    root = roots[0] if roots else added[0]
    root.name = "%s (root)" % label
    for mesh in meshes:
        mesh.data.name = "%s mesh" % label
    return added, root, meshes


def outcome(result):
    """An operator's return, as a word: `FINISHED` is ok, `CANCELLED` is "it said why in a report"."""
    if result == {"FINISHED"}:
        return "ok"
    if result == {"CANCELLED"}:
        return "cancelled"
    return str(result)


def run_steps(label, added, meshes, options):
    """Run the requested pipeline steps on one model and return a list of result strings for the label."""
    results = []
    if not meshes:
        return ["no mesh"]
    mesh = max(meshes, key=lambda obj: len(obj.data.vertices))
    armature = next((obj for obj in added if obj.type == 'ARMATURE'), None)

    def focus(obj):
        for other in bpy.context.scene.objects:
            other.select_set(False)
        obj.select_set(True)
        bpy.context.view_layer.objects.active = obj
        if bpy.context.mode != 'OBJECT':
            bpy.ops.object.mode_set(mode='OBJECT')

    if options["refine"] and armature is not None:
        focus(armature)
        try:
            result = bpy.ops.uma.refine_bone_structure()
            keys = [k.name for k in mesh.data.shape_keys.key_blocks if k.name.startswith("Eye_") and k.name.endswith(")")]
            eye_l = mesh.vertex_groups.get("Eye_L")
            symmetry = ""
            if eye_l is not None and mesh.data.shape_keys:
                basis = mesh.data.shape_keys.key_blocks[0]
                points = [basis.data[v.index].co.copy() for v in mesh.data.vertices
                          if any(g.group == eye_l.index and g.weight > 0.5 for g in v.groups)]
                if points:
                    paired = 0
                    for point in points:
                        target = Vector((-point.x, point.y, point.z))
                        if min((target - other).length for other in points) < 1e-4:
                            paired += 1
                    symmetry = "eye symmetry %d/%d" % (2 * paired if paired * 2 <= len(points) * 2 else paired,
                                                       len(points) * 2)
            results.append("refine %s, %d eye keys%s" % (outcome(result),
                                                         len(keys) // 2, (", " + symmetry) if symmetry else ""))
        except Exception as exc:                 # noqa: BLE001 - the label has to show the failure
            results.append("refine FAILED: %s" % exc)
    if options["shade"]:
        focus(mesh)
        try:
            result = bpy.ops.uma.apply_shader()
            roles = sorted({material.get("uma_shader_role") for material in bpy.data.materials
                            if material.get("uma_shader_role")})
            results.append("shade %s (%s)" % (outcome(result),
                                              ", ".join(str(role) for role in roles) or "no roles"))
        except Exception as exc:                 # noqa: BLE001
            results.append("shade FAILED: %s" % exc)
    if options["normals"]:
        focus(mesh)
        try:
            result = bpy.ops.uma.face_normals_apply()
            donors = [obj for obj in bpy.context.scene.objects if obj.get("uma_face_normals_copy")]
            kept = []
            for donor in donors:
                keys = getattr(donor.data, "shape_keys", None)
                kept = [k.name for k in keys.key_blocks if k.name != "Basis"] if keys else []
            results.append("normals %s, donor keys %s"
                           % (outcome(result),
                              ", ".join(kept) or "none"))
        except Exception as exc:                 # noqa: BLE001
            results.append("normals FAILED: %s" % exc)
    if options["morphs"]:
        focus(mesh)
        try:
            result = bpy.ops.uma.build_morph_drivers()
            deploy = [k.name for k in mesh.data.shape_keys.key_blocks
                      if k.name.startswith(("Cheek_", "Manga_", "Tear_"))] if mesh.data.shape_keys else []
            # `uma.build_morph_drivers` returns CANCELLED whenever it has *notes* (dead morph names after the
            # face split), not when it failed - so a label calling that "cancelled" would misread
            word = "ok (notes)" if result == {"CANCELLED"} else outcome(result)
            results.append("morphs %s, %d deploy key(s)" % (word, len(deploy)))
        except Exception as exc:                 # noqa: BLE001
            results.append("morphs FAILED: %s" % exc)
    if options["rig"]:
        focus(mesh)
        try:
            result = bpy.ops.uma.mmr_rig()
            results.append("rig %s" % outcome(result))
        except Exception as exc:                 # noqa: BLE001
            results.append("rig FAILED: %s" % exc)
    # A label is a record, so a step that did not run says so instead of being absent: "not run" and "ran"
    # must not look the same in a file somebody opens a week later.
    for flag, name in (("refine", "refine"), ("shade", "shade"), ("normals", "normals"),
                       ("morphs", "morphs"), ("rig", "rig")):
        if not options[flag] and not any(line.startswith(name + " ") for line in results):
            results.append("%s not run" % name)
    if not results:
        results.append("imported only")
    return results


def world_bounds(objects):
    """The box of the models' geometry in world space, from the evaluated meshes."""
    depsgraph = bpy.context.evaluated_depsgraph_get()
    low = Vector((1e9, 1e9, 1e9))
    high = Vector((-1e9, -1e9, -1e9))
    found = False
    for obj in objects:
        if obj.type != 'MESH' or obj.hide_render:
            continue
        evaluated = obj.evaluated_get(depsgraph)
        for corner in evaluated.bound_box:
            point = obj.matrix_world @ Vector(corner)
            low = Vector((min(low[i], point[i]) for i in range(3)))
            high = Vector((max(high[i], point[i]) for i in range(3)))
            found = True
    if not found:
        return None, None
    return low, high


def wrap(text, width=30, max_lines=6):
    """Break a label into short lines, so it stays inside its model's slot instead of running into the next.

    A result line like "normals ok, donor keys Eye_22_R(EyeHideA)[M_Face], ..." is 60-odd characters; at the
    label's size that is metres wide, which is how a review row turns into unreadable overlapping text.
    """
    lines = []
    for paragraph in text.split("\n"):
        words = paragraph.split(" ")
        current = ""
        for word in words:
            candidate = (current + " " + word).strip()
            if current and len(candidate) > width:
                lines.append(current)
                current = word
            else:
                current = candidate
        if current:
            lines.append(current)
    if len(lines) > max_lines:
        lines = lines[:max_lines - 1] + ["..."]
    return "\n".join(lines)


def make_text(body, location, size=0.08, align='CENTER'):
    """A flat text object standing in the XY plane, readable from the front (-Y)."""
    curve = bpy.data.curves.new(name=MARK + "label", type='FONT')
    curve.body = wrap(body)
    curve.size = size
    curve.align_x = align
    curve.align_y = 'TOP'
    obj = bpy.data.objects.new(MARK + "label", curve)
    bpy.context.scene.collection.objects.link(obj)
    obj.location = location
    obj.rotation_euler = (1.5707963, 0.0, 0.0)      # face -Y, the way the models face
    # **no shadow from a label.** A label is a viewport annotation, not a prop: a text object standing over the
    # head at label size throws a band of shade across the face of the model behind it, which is exactly the part
    # of the render a reviewer is looking at.
    obj.visible_shadow = False
    material = bpy.data.materials.get(MARK + "label")
    if material is None:
        material = bpy.data.materials.new(MARK + "label")
        material.use_nodes = True
        nodes = material.node_tree.nodes
        for node in list(nodes):
            if node.type != 'OUTPUT_MATERIAL':
                nodes.remove(node)
        emission = nodes.new("ShaderNodeEmission")
        emission.inputs["Color"].default_value = (0.02, 0.02, 0.02, 1.0)
        emission.inputs["Strength"].default_value = 1.0
        material.node_tree.links.new(emission.outputs["Emission"],
                                     nodes["Material Output"].inputs["Surface"])
    obj.data.materials.append(material)
    return obj


def lights_and_camera(low, high, options):
    """A camera framing the row, and lighting that shows a shaded uma the way it is meant to be seen."""
    centre = (low + high) * 0.5
    width = max(high.x - low.x, 0.5)
    height = high.z - low.z

    world = bpy.data.worlds.get(MARK + "world") or bpy.data.worlds.new(MARK + "world")
    world.use_nodes = True
    background = world.node_tree.nodes.get("Background")
    if background is not None:
        background.inputs["Color"].default_value = (0.012, 0.012, 0.016, 1.0)
        background.inputs["Strength"].default_value = 1.0
    bpy.context.scene.world = world

    for name, energy, rotation in ((MARK + "key", 4.0, (1.0, 0.0, -0.6)),
                                   (MARK + "fill", 1.5, (1.2, 0.0, 2.4))):
        if bpy.data.objects.get(name) is None:
            light_data = bpy.data.lights.new(name, type='SUN')
            light_data.energy = energy
            light = bpy.data.objects.new(name, light_data)
            bpy.context.scene.collection.objects.link(light)
            light.rotation_euler = rotation

    camera = bpy.data.objects.get(MARK + "camera")
    if camera is None:
        camera_data = bpy.data.cameras.new(MARK + "camera")
        camera = bpy.data.objects.new(MARK + "camera", camera_data)
        bpy.context.scene.collection.objects.link(camera)
    camera.data.lens = 60.0
    # how far back the row fits: the horizontal field of view of a 60 mm lens on a 36 mm sensor
    import math
    fov = 2.0 * math.atan(36.0 / (2.0 * camera.data.lens))
    distance = (width * 0.5) / math.tan(fov * 0.5) * 1.25 + 1.2
    camera.location = (centre.x, centre.y - distance, centre.z + height * 0.18)
    direction = centre - camera.location
    camera.rotation_euler = direction.to_track_quat('-Z', 'Y').to_euler()
    bpy.context.scene.camera = camera
    return camera


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]
    options, fixtures = parse_args(argv)
    enable_addons()

    placed = []
    report_lines = ["review scene", ""]
    for label, path in fixtures:
        if not os.path.exists(path):
            report_lines.append("%s: MISSING %s" % (label, path))
            continue
        added, root, meshes = import_model(path, label)
        results = run_steps(label, added, meshes, options)
        low, high = world_bounds(meshes)
        if low is None:
            report_lines.append("%s: imported, no geometry" % label)
            continue
        placed.append({"label": label, "path": path, "root": root, "meshes": meshes,
                       "low": low, "high": high, "results": results})
        report_lines.append("%s: %s" % (label, "; ".join(results)))

    if not placed:
        raise SystemExit("nothing imported")

    # --- the row: each model centred on its own slot, spaced by what the previous one occupies -----------
    cursor = 0.0
    for entry in placed:
        width = entry["high"].x - entry["low"].x
        slot = width * (1.0 + options["spacing"])
        entry["centre_x"] = cursor + slot * 0.5
        cursor += slot
    row_width = cursor
    for entry in placed:
        shift = entry["centre_x"] - (entry["low"].x + entry["high"].x) * 0.5
        entry["root"].location.x += shift

    # --- labels: one over each head, saying what was run and with which numbers --------------------------
    labels = []
    for entry in placed:
        bpy.context.view_layer.update()
        low, high = world_bounds(entry["meshes"])
        entry["low"], entry["high"] = low, high
        head_top = high.z
        centre_x = entry["centre_x"]
        title = entry["label"]
        body = title + "\n" + "\n".join(entry["results"])
        labels.append(make_text(body, (centre_x, (low.y + high.y) * 0.5 - 0.35, head_top + 0.30), size=0.075))

    if options["title"]:
        low_all = min(entry["low"].y for entry in placed)
        labels.append(make_text(options["title"], (row_width * 0.5, low_all - 0.6,
                                                   max(entry["high"].z for entry in placed) + 0.75),
                                size=0.11))

    low_all = Vector((0.0, min(entry["low"].y for entry in placed), min(entry["low"].z for entry in placed)))
    high_all = Vector((row_width, max(entry["high"].y for entry in placed),
                       max(entry["high"].z for entry in placed) + (0.55 if options["title"] else 0.30)))
    camera = lights_and_camera(low_all, high_all, options)

    # --- the run's own log, inside the file --------------------------------------------------------------
    try:
        from bl_ext.user_default.uma_addon.uma_addon.addons.uma_addon.utils import audit
        text = bpy.data.texts.get("uma_run_report") or bpy.data.texts.new("uma_run_report")
        text.clear()
        text.write(audit.report_text())
        report_lines.append("")
        report_lines.append("run report embedded as the text block 'uma_run_report'")
    except Exception as exc:                     # noqa: BLE001 - a layout-only review has no report
        report_lines.append("no run report (%s)" % exc)

    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_EEVEE' if hasattr(bpy.types, "SceneEEVEE") else scene.render.engine
    scene.render.resolution_x = max(1280, min(3840, int(round(row_width * 900 / max(1.0, high_all.z - low_all.z)))))
    scene.render.resolution_y = max(480, min(1600, int(round(scene.render.resolution_x * 0.55))))
    # A review render is a record of models, not of a scene: a transparent backdrop composites over anything,
    # and it turns "did this model actually draw" into coverage that can be measured instead of contrast that
    # has to be guessed at. `--opaque` gives the dark backdrop back for a human looking at a single frame.
    scene.render.film_transparent = not options["opaque"]

    out_dir = os.path.dirname(os.path.abspath(options["out"]))
    os.makedirs(out_dir, exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.abspath(options["out"]))
    print("[review] saved %s (%d model(s), %d label(s))" % (options["out"], len(placed), len(labels)))

    if options["render"]:
        scene.render.filepath = os.path.abspath(options["render"])
        scene.render.image_settings.file_format = 'PNG'
        bpy.ops.render.render(write_still=True)
        print("[review] rendered %s" % options["render"])

    summary_path = os.path.splitext(os.path.abspath(options["out"]))[0] + ".txt"
    with open(summary_path, "w", encoding="utf-8") as handle:
        handle.write("\n".join(report_lines) + "\n")
    print("[review] summary %s" % summary_path)


if __name__ == "__main__":
    main()





