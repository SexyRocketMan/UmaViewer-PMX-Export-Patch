"""Bake the blush into the face atlas's UV space once, as a file the add-on can ship.

    blender --background --factory-startup --python Tools/bake_face_blush_layer.py -- <out.png> [<file.pmx>]

The static-mapping question, answered the way it actually is static. A *UV transform* from the face atlas to the blush
map does not exist (measured: `Tools/fit_blush_uv.py`, residual ~42 UV units) - the two maps are unrelated
parameterisations of different geometry. But the **baked layer** is a different object: it depends only on the face
mesh's UVs and on where the blush quad sits, and every uma shares that face model. So the layer is the same for every
character, which means it can be baked **once, here** and shipped with the add-on: no per-export bake, no Unity change,
no UV guessing at shade time.

This writes exactly what the face material wants to multiply: white where there is no blush, `lerp(white, blush,
blushAlpha)` where there is, at the face atlas's resolution (512x512 for 1001 - the other face maps are the same size).
"""
import sys

import bpy
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]
out = argv[0]
pmx = argv[1] if len(argv) > 1 else r"C:\Users\user\Documents\umaviewer_exports\morphs\1001_nostream.pmx"

lines = []


def say(text=""):
    lines.append(text)


def deployed_positions(mesh):
    keys = getattr(mesh.data, "shape_keys", None)
    if keys is None:
        return [vertex.co.copy() for vertex in mesh.data.vertices]
    basis = keys.key_blocks["Basis"].data
    positions = [point.co.copy() for point in basis]
    for block in keys.key_blocks:
        if not (block.name.startswith(("Manga_", "Tear_")) or block.name == "Cheek_Blush"):
            continue
        block.value = 1.0
        for index, point in enumerate(block.data):
            positions[index] += point.co - basis[index].co
    return positions


bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.preferences.addon_enable(module="bl_ext.blender_org.mmd_tools")
bpy.ops.mmd_tools.import_model(filepath=pmx)

body = max([obj for obj in bpy.context.scene.objects if obj.type == 'MESH'],
           key=lambda item: len(item.data.vertices))
cheek_slots = [i for i, s in enumerate(body.material_slots)
               if s.material is not None and "cheek" in s.material.name.lower()]
face_slots = [i for i, s in enumerate(body.material_slots)
              if s.material is not None and "face" in s.material.name.lower()
              and "mayu" not in s.material.name.lower()]
positions = deployed_positions(body)
uv_layer = body.data.uv_layers.active

blush_image = next(node.image for slot in body.material_slots if slot.material is not None
                   and "cheek" in slot.material.name.lower()
                   for node in slot.material.node_tree.nodes if node.type == 'TEX_IMAGE' and node.image)
face_image = next(node.image for slot in body.material_slots if slot.material is not None
                  and "face" in slot.material.name.lower() and "mayu" not in slot.material.name.lower()
                  for node in slot.material.node_tree.nodes if node.type == 'TEX_IMAGE' and node.image)
width, height = face_image.size
say("%s" % pmx)
say("face atlas %r %dx%d, blush map %r %dx%d" % (face_image.name, width, height,
                                                 blush_image.name, blush_image.size[0], blush_image.size[1]))

blush_pixels = list(blush_image.pixels)
blush_width, blush_height = blush_image.size


def sample(u, v):
    x = min(max(u, 0.0), 0.9999) * blush_width
    y = min(max(v, 0.0), 0.9999) * blush_height
    x0, y0 = int(x), int(y)
    fx, fy = x - x0, y - y0

    def at(ix, iy):
        offset = (min(iy, blush_height - 1) * blush_width + min(ix, blush_width - 1)) * 4
        return blush_pixels[offset:offset + 4]
    a, b = at(x0, y0), at(x0 + 1, y0)
    c, d = at(x0, y0 + 1), at(x0 + 1, y0 + 1)
    return tuple(a[i] * (1 - fx) * (1 - fy) + b[i] * fx * (1 - fy)
                 + c[i] * (1 - fx) * fy + d[i] * fx * fy for i in range(4))


triangles = []           # ((p0, p1, p2), (uv0, uv1, uv2))
for polygon in body.data.polygons:
    if polygon.material_index not in cheek_slots:
        continue
    loops = [uv_layer.data[index].uv.copy() for index in polygon.loop_indices]
    points = [positions[index] for index in polygon.vertices]
    for offset in range(1, len(points) - 1):
        triangles.append(((points[0], points[offset], points[offset + 1]),
                          (loops[0], loops[offset], loops[offset + 1])))


def project(point):
    best = (None, 1e9)
    for points, uvs in triangles:
        a, b, c = points
        normal = (b - a).cross(c - a)
        length = normal.length
        if length < 1e-12:
            continue
        normal = normal / length
        distance = abs((point - a).dot(normal))
        projected = point - normal * (point - a).dot(normal)
        v0, v1, v2 = b - a, c - a, projected - a
        d00, d01, d11 = v0.dot(v0), v0.dot(v1), v1.dot(v1)
        d20, d21 = v2.dot(v0), v2.dot(v1)
        denominator = d00 * d11 - d01 * d01
        if abs(denominator) < 1e-12:
            continue
        v = (d11 * d20 - d01 * d21) / denominator
        w = (d00 * d21 - d01 * d20) / denominator
        u = 1.0 - v - w
        if min(u, v, w) < -0.001:
            continue
        uv = uvs[0] * u + uvs[1] * v + uvs[2] * w
        if distance < best[1]:
            best = ((uv.x, uv.y), distance)
    return best


def barycentric(uv, uv_a, uv_b, uv_c):
    denominator = (uv_b.y - uv_c.y) * (uv_a.x - uv_c.x) + (uv_c.x - uv_b.x) * (uv_a.y - uv_c.y)
    if abs(denominator) < 1e-12:
        return None
    wa = ((uv_b.y - uv_c.y) * (uv.x - uv_c.x) + (uv_c.x - uv_b.x) * (uv.y - uv_c.y)) / denominator
    wb = ((uv_c.y - uv_a.y) * (uv.x - uv_c.x) + (uv_a.x - uv_c.x) * (uv.y - uv_c.y)) / denominator
    wc = 1.0 - wa - wb
    if min(wa, wb, wc) < -0.001:
        return None
    return wa, wb, wc


layer = [1.0] * (width * height * 4)
covered = 0
painted = 0
strongest = 0.0
for polygon in body.data.polygons:
    if polygon.material_index not in face_slots:
        continue
    loops = [uv_layer.data[index].uv.copy() for index in polygon.loop_indices]
    vertices = list(polygon.vertices)
    for offset in range(1, len(vertices) - 1):
        corners = [vertices[0], vertices[offset], vertices[offset + 1]]
        uvs = [loops[0], loops[offset], loops[offset + 1]]
        points = [positions[index] for index in corners]
        min_x = max(0, int(min(uv.x for uv in uvs) * width) - 1)
        max_x = min(width - 1, int(max(uv.x for uv in uvs) * width) + 1)
        min_y = max(0, int(min(uv.y for uv in uvs) * height) - 1)
        max_y = min(height - 1, int(max(uv.y for uv in uvs) * height) + 1)
        for y in range(min_y, max_y + 1):
            for x in range(min_x, max_x + 1):
                uv = Vector(((x + 0.5) / width, (y + 0.5) / height))
                weights = barycentric(uv, uvs[0], uvs[1], uvs[2])
                if weights is None:
                    continue
                covered += 1
                point = points[0] * weights[0] + points[1] * weights[1] + points[2] * weights[2]
                blush_uv, _distance = project(point)
                if blush_uv is None:
                    continue
                colour = sample(blush_uv[0], blush_uv[1])
                alpha = colour[3]
                if alpha <= 0.002:
                    continue
                painted += 1
                strongest = max(strongest, alpha)
                index = (y * width + x) * 4
                for channel in range(3):
                    layer[index + channel] = 1.0 - alpha * (1.0 - colour[channel])
                layer[index + 3] = 1.0

image = bpy.data.images.new("UMA Face Blush", width=width, height=height, alpha=True)
image.pixels = layer
image.filepath_raw = out
image.file_format = 'PNG'
image.save()
say("texels covered by the face: %d, of which painted with blush: %d (%.2f%%), strongest alpha %.3f"
    % (covered, painted, 100.0 * painted / max(1, covered), strongest))
say("wrote %s (%dx%d)" % (out, width, height))

with open(out + ".txt", "w", encoding="utf-8") as handle:
    handle.write("\n".join(lines) + "\n")
print("\n".join(lines))
