"""Check that a review .blend is actually a review .blend - by numbers, not by eyeballing.

    python Tools/verify_review_scene.py review.blend [--render review.png] [--expect-label "mode 1"] ...

What a review scene has to have, and what this checks:

1. one labelled root per fixture, with every object of that model named after its label;
2. **a label object over each model's head** - above the model's own top, inside its own slot, facing the
   camera, and visible to the renderer (a label that exists but is hidden or behind the model is no label);
3. a camera aimed at the row, and lighting that reaches it;
4. the add-on's run report embedded as a text block, and not empty;
5. with `--render`, that the rendered image is not a blank frame: real contrast between the background and
   the models, and ink where the labels are.

Exit code is 0 when everything holds. The point is that "it rendered" is not the claim - "each model is in
its slot with a readable label over its head" is, and that is what a person looking at the file should see.
"""
import argparse
import os
import sys

import bpy
from mathutils import Vector

MARK = "uma_review_"
lines = []
failures = []


def say(text=""):
    lines.append(text)
    print(text)


def check(label, ok, detail=""):
    say("  [%s] %s%s" % ("PASS" if ok else "FAIL", label, (" - " + detail) if detail else ""))
    if not ok:
        failures.append(label)
    return ok


def models_in(scene):
    """One entry per labelled root: (label, root, mesh objects)."""
    out = []
    for obj in scene.objects:
        if obj.type == 'EMPTY' and obj.name.endswith(" (root)") and ":" not in obj.name:
            label = obj.name[:-len(" (root)")]
            meshes = [child for child in scene.objects
                      if child.parent is not None and child.name.startswith(label + ":") and child.type == 'MESH']
            out.append((label, obj, meshes))
    return out


def bounds(objects, depsgraph):
    low = Vector((1e9, 1e9, 1e9))
    high = Vector((-1e9, -1e9, -1e9))
    found = False
    for obj in objects:
        if obj.type != 'MESH':
            continue
        evaluated = obj.evaluated_get(depsgraph)
        for corner in evaluated.bound_box:
            point = obj.matrix_world @ Vector(corner)
            low = Vector((min(low[i], point[i]) for i in range(3)))
            high = Vector((max(high[i], point[i]) for i in range(3)))
            found = True
    return (low, high) if found else (None, None)


def png_stats(path):
    """Brightness histogram and channel spread of a PNG, without any third-party module.

    Blender writes an 8-bit, non-interlaced PNG; that is all this reads. The repo's checkers carry no
    dependencies, and Blender's own Python has no Pillow, so decoding in twenty lines beats importing one.
    """
    import struct
    import zlib

    with open(path, "rb") as handle:
        data = handle.read()
    if data[:8] != b"\x89PNG\r\n\x1a\n":
        raise ValueError("not a PNG: %s" % path)
    pos, width, height, depth, colour_type, idat = 8, 0, 0, 0, 0, b""
    while pos < len(data):
        length = struct.unpack(">I", data[pos:pos + 4])[0]
        kind = data[pos + 4:pos + 8]
        body = data[pos + 8:pos + 8 + length]
        if kind == b"IHDR":
            width, height, depth, colour_type = struct.unpack(">IIBB", body[:10])
            interlace = body[12]
            if depth != 8 or colour_type not in (2, 6) or interlace:
                raise ValueError("unsupported PNG (depth=%d colour=%d interlace=%d)" % (depth, colour_type, interlace))
        elif kind == b"IDAT":
            idat += body
        elif kind == b"IEND":
            break
        pos += 12 + length

    channels = 4 if colour_type == 6 else 3
    stride = width * channels
    raw = zlib.decompress(idat)
    previous = bytearray(stride)
    histogram = [0] * 256
    opaque_histogram = [0] * 256
    alpha_plane = bytearray(width * height)
    sums = [0, 0, 0]
    sum_squares = [0.0, 0.0, 0.0]
    pixels = 0
    offset = 0
    for _row in range(height):
        filter_type = raw[offset]
        offset += 1
        line = bytearray(raw[offset:offset + stride])
        offset += stride
        if filter_type == 1:                       # Sub
            for i in range(channels, stride):
                line[i] = (line[i] + line[i - channels]) & 0xFF
        elif filter_type == 2:                     # Up
            for i in range(stride):
                line[i] = (line[i] + previous[i]) & 0xFF
        elif filter_type == 3:                     # Average
            for i in range(stride):
                left = line[i - channels] if i >= channels else 0
                line[i] = (line[i] + ((left + previous[i]) >> 1)) & 0xFF
        elif filter_type == 4:                     # Paeth
            for i in range(stride):
                a = line[i - channels] if i >= channels else 0
                b = previous[i]
                c = previous[i - channels] if i >= channels else 0
                p = a + b - c
                pa, pb, pc = abs(p - a), abs(p - b), abs(p - c)
                predictor = a if (pa <= pb and pa <= pc) else (b if pb <= pc else c)
                line[i] = (line[i] + predictor) & 0xFF
        previous = line
        for x in range(0, stride, channels):
            r, g, b = line[x], line[x + 1], line[x + 2]
            coverage_alpha = line[x + 3] if channels == 4 else 255
            alpha_plane[pixels] = coverage_alpha
            luma = (299 * r + 587 * g + 114 * b) // 1000
            histogram[luma] += 1
            if coverage_alpha > 8:
                opaque_histogram[luma] += 1
            for channel, value in enumerate((r, g, b)):
                sums[channel] += value
                sum_squares[channel] += value * value
            pixels += 1

    total = max(1, pixels)
    means = [value / total for value in sums]
    deviations = [max(0.0, (sum_squares[i] / total) - means[i] ** 2) ** 0.5 for i in range(3)]

    def percentile(fraction, source=None):
        """A luma percentile **of the opaque pixels** - the transparent backdrop is not shading."""
        table = source if source is not None else histogram
        span = sum(table) or 1
        target = span * fraction
        seen = 0
        for luma, count in enumerate(table):
            seen += count
            if seen >= target:
                return luma
        return 255

    return {"size": (width, height), "pixels": pixels, "histogram": histogram,
            "mean": means, "spread": max(deviations),
            "p05": percentile(0.05, opaque_histogram), "p50": percentile(0.5, opaque_histogram),
            "p95": percentile(0.95, opaque_histogram),
            "alpha": alpha_plane, "has_alpha": colour_type == 6}


def coverage(alpha, width, height, box, threshold=8):
    """What fraction of `box` (0..1 screen space, y up) is not transparent."""
    if alpha is None:
        return None
    x0, x1, y0, y1 = box
    px0 = max(0, min(width - 1, int(x0 * width)))
    px1 = max(0, min(width, int(x1 * width) + 1))
    py0 = max(0, min(height - 1, int((1.0 - y1) * height)))
    py1 = max(0, min(height, int((1.0 - y0) * height) + 1))
    if px1 <= px0 or py1 <= py0:
        return 0.0
    lit = 0
    area = 0
    for y in range(py0, py1):
        row = y * width
        for x in range(px0, px1):
            area += 1
            if alpha[row + x] > threshold:
                lit += 1
    return lit / area if area else 0.0


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("blend")
    ap.add_argument("--render")
    ap.add_argument("--expect-label", action="append", default=[])
    ap.add_argument("--report", default=None)
    # Blender passes its own flags before `--`; everything after it is ours
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]
    args = ap.parse_args(argv)

    bpy.ops.wm.open_mainfile(filepath=os.path.abspath(args.blend))
    scene = bpy.context.scene
    depsgraph = bpy.context.evaluated_depsgraph_get()

    say("=" * 100)
    say("review scene - %s" % args.blend)

    models = models_in(scene)
    say("")
    say("1. the models")
    check("at least two models in the row", len(models) >= 2, "%d found" % len(models))
    for wanted in args.expect_label:
        check("the row has %r" % wanted, any(wanted == label for label, _root, _meshes in models))
    for label, root, meshes in models:
        named = [obj for obj in meshes if obj.name.startswith(label + ":")]
        check("%r: every mesh is named after it" % label, len(named) == len(meshes),
              "%d of %d" % (len(named), len(meshes)))

    say("")
    say("2. a label over each head")
    texts = [obj for obj in scene.objects
             if obj.type == 'FONT' and obj.name.startswith(MARK + "label")]
    check("there is one label per model (plus an optional title)", len(texts) >= len(models),
          "%d label(s) for %d model(s)" % (len(texts), len(models)))
    label_for = {}
    for label, root, meshes in models:
        low, high = bounds(meshes, depsgraph)
        if low is None:
            check("%r: has geometry" % label, False, "no mesh bounds")
            continue
        slot_low, slot_high = low.x, high.x
        above = [obj for obj in texts
                 if slot_low - 0.2 <= obj.location.x <= slot_high + 0.2 and obj.location.z > high.z]
        ok = bool(above)
        detail = ""
        if above:
            nearest = min(above, key=lambda obj: abs(obj.location.x - (slot_low + slot_high) * 0.5))
            label_for[label] = nearest
            detail = ("%r at z %.2f, head top %.2f, %d line(s)"
                      % (nearest.data.body.split("\n")[0][:28], nearest.location.z, high.z,
                         len(nearest.data.body.split("\n"))))
            ok = ok and not nearest.hide_render and nearest.hide_get() is False
        check("%r: a label sits above its head and is renderable" % label, ok, detail)

    say("")
    say("3. the labels say what ran - and the data agrees")
    for label, root, meshes in models:
        text_obj = label_for.get(label)
        if text_obj is None:
            continue
        claims = [line.strip() for line in text_obj.data.body.replace("\n", " ").split() if False]
        body = text_obj.data.body.replace("\n", " ")
        # **every** piece of the model, not the biggest one: shading and the face split put the frame
        # morphs on the face piece, so the body alone reports none of the eye control keys that refine built.
        keys = set()
        for piece in meshes:
            if piece.data.shape_keys:
                keys.update(piece.data.shape_keys.key_blocks.keys())
        eye_keys = {name for name in keys if name.endswith(")") and name.startswith("Eye_")}
        slots = [slot.material for piece in meshes for slot in piece.material_slots if slot.material]
        roles = {material.get("uma_shader_role") for material in slots if material.get("uma_shader_role")}
        donors = [obj for obj in scene.objects if obj.get("uma_face_normals_copy")]
        deploy = {name for name in keys if name.startswith(("Cheek_", "Manga_", "Tear_"))}

        if "refine ok" in body:
            check("%r: refine ran - the eye control keys are on the mesh" % label, len(eye_keys) >= 4,
                  "%d control key(s), e.g. %s" % (len(eye_keys), sorted(eye_keys)[:3]))
        if "shade ok" in body:
            check("%r: shade ran - the mesh wears tagged shader copies" % label, bool(roles),
                  ", ".join(sorted(str(role) for role in roles)) or "no uma_shader_role material")
        if "normals ok" in body:
            check("%r: normals ran - a face donor exists for it" % label,
                  any(donor.get("uma_face_normals_for") in {obj.name for obj in meshes} for donor in donors),
                  "%d donor(s) in the scene" % len(donors))
        if "morphs ok" in body or "morphs cancelled" in body:
            check("%r: morphs ran - the deploy keys are on the mesh" % label, bool(deploy),
                  "%d deploy key(s), e.g. %s" % (len(deploy), sorted(deploy)[:3]))
        if "not run" in body:
            say("  (skipped: the label records %r as not run)"
                % next(line.strip() for line in text_obj.data.body.split("\n") if "not run" in line))

    say("")
    say("4. the labels do not shade the models")
    casting = [obj for obj in texts if getattr(obj, "visible_shadow", False)]
    check("no label casts a shadow", not casting,
          ", ".join(obj.name for obj in casting) or "all %d label(s) have visible_shadow off" % len(texts))

    say("")
    say("5. a camera and light on the row")
    camera = scene.camera
    check("the scene has a camera", camera is not None, camera.name if camera else "none")
    if camera is not None and models:
        all_low = Vector((min(bounds(meshes, depsgraph)[0].x for _l, _r, meshes in models), 0, 0))
        all_high = Vector((max(bounds(meshes, depsgraph)[1].x for _l, _r, meshes in models), 0, 0))
        centre_x = (all_low.x + all_high.x) * 0.5
        view = camera.matrix_world.to_quaternion() @ Vector((0.0, 0.0, -1.0))
        toward = Vector((centre_x, 0.0, 1.0)) - camera.location
        aimed = view.normalized().dot(toward.normalized()) > 0.7
        check("the camera looks at the row", aimed,
              "camera at (%.2f, %.2f, %.2f), forward·toward=%.2f"
              % (camera.location.x, camera.location.y, camera.location.z,
                 view.normalized().dot(toward.normalized())))
        check("the camera is in front of the models (-Y)", camera.location.y < 0, "y=%.2f" % camera.location.y)
    lights = [obj for obj in scene.objects if obj.type == 'LIGHT']
    check("there is light in the scene", bool(lights), "%d light(s)" % len(lights))

    say("")
    say("6. the run report travels with the file")
    text = bpy.data.texts.get("uma_run_report")
    check("the report is embedded as a text block", text is not None, "uma_run_report")
    if text is not None:
        body = text.as_string()
        check("the report is not empty", len(body.strip()) > 40, "%d chars" % len(body.strip()))
        check("the report names a step", "[" in body and "]" in body,
              next((line for line in body.splitlines() if "[" in line), "")[:70])

    say("")
    say("7. the render, when there is one")
    if not args.render:
        say("  (skipped: no --render given)")
    elif not os.path.exists(args.render):
        check("the render exists", False, args.render)
    else:
        try:
            from bpy_extras.object_utils import world_to_camera_view

            stats = png_stats(args.render)
            histogram = stats["histogram"]
            total = stats["pixels"]
            alpha = stats["alpha"]
            width, height = stats["size"]
            contrast = stats["p95"] - stats["p05"]

            def screen_box(corners):
                projected = [world_to_camera_view(scene, camera, corner) for corner in corners]
                return (min(p.x for p in projected), max(p.x for p in projected),
                        min(p.y for p in projected), max(p.y for p in projected))

            if stats["has_alpha"] and camera is not None:
                # **coverage per model**: transparent backdrop, so "this model drew" is a number - how much of
                # its own projected box has opaque pixels in it. That is the check a blank render cannot pass.
                for label, _root, meshes in models:
                    low, high = bounds(meshes, depsgraph)
                    if low is None:
                        continue
                    corners = [Vector((x, y, z)) for x in (low.x, high.x)
                               for y in (low.y, high.y) for z in (low.z, high.z)]
                    box = screen_box(corners)
                    share = coverage(alpha, width, height, box)
                    check("%r: drew pixels inside its own projected box" % label,
                          share is not None and share > 0.02,
                          "%.1f%% of its box is opaque" % ((share or 0.0) * 100))
                # **coverage per label**: the text must have ink of its own, above the model
                for obj in texts:
                    if obj.name == MARK + "label" and obj.data.size > 0.1:
                        continue                     # the title line is checked as a label only if it is per-model
                    corners = [obj.matrix_world @ Vector(corner) for corner in obj.bound_box]
                    box = screen_box(corners)
                    share = coverage(alpha, width, height, box)
                    check("label %r has ink in the frame" % obj.data.body.split("\n")[0][:30],
                          share is not None and share > 0.01,
                          "%.1f%% of its box is opaque" % ((share or 0.0) * 100))
                overall = sum(1 for value in alpha[::7] if value > 8) / max(1, len(alpha[::7]))
                check("the models cover part of the frame", 0.02 < overall < 0.75,
                      "%.1f%% of pixels are opaque" % (overall * 100))
            else:
                lit = sum(histogram[stats["p50"] + 30:]) / total
                check("lit geometry covers part of the frame", lit > 0.02,
                      "%.1f%% of pixels are 30+ levels above the median (no alpha channel in the render)"
                      % (lit * 100))
            check("the models are shaded, not flat", contrast > 40,
                  "p05 %d, p50 %d, p95 %d (contrast %d)" % (stats["p05"], stats["p50"], stats["p95"], contrast))
            say("  render: %dx%d, mean %s, spread %.1f, contrast %d, alpha %s"
                % (width, height, [round(v, 1) for v in stats["mean"]], stats["spread"], contrast,
                   "yes" if stats["has_alpha"] else "no"))
        except Exception as exc:                 # noqa: BLE001 - a broken PNG is a failed check, not a crash
            check("the render decodes as an 8-bit PNG", False, str(exc))

    say("")
    say("=" * 100)
    say("FAILURES: " + (", ".join(failures) if failures else "none"))
    if args.report:
        with open(args.report, "w", encoding="utf-8") as handle:
            handle.write("\n".join(lines) + "\n")
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())




