"""Bake the deployed blush onto the face, as a vertex colour - the alignment question, answered by measuring.

    blender --background --factory-startup --python Tools/bake_blush_vertices.py -- <out.txt> [<file.pmx>]

The idea the user asked for: instead of a decal quad composited over the face, put the blush *into* the face material
with a Mix Color multiply - "closest to the game". A mix node in the face material can only read the face mesh's own
UVs, and the blush map is in its own UV square, so there is no UV mapping between them to pick; guessing an offset
would be right for one character and wrong for the next.

The mapping that is right by construction is **geometry**: the blush quad sits a hair in front of the cheek, so
projecting each face vertex onto the quad and sampling the blush map there puts the art exactly where the quad is. The
result is stored as a colour attribute on the face mesh (`UMA_Blush`, white = no blush), which a Mix Color node can
multiply into the face texture - no second texture, no bake pass, no UV guessing.

This script is the measurement, not the pipeline: it bakes, then reports how much of the face the blush lands on and
how strong it comes out, so the look can be judged before anything is wired into Shading.
"""
import sys

import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree

ADDON = "bl_ext.user_default.uma_addon"

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]
out = argv[0]
pmx = argv[1] if len(argv) > 1 else r"C:\Users\user\Documents\umaviewer_exports\morphs\1001_nostream.pmx"

lines = []


def say(text=""):
    lines.append(text)


def deployed_positions(mesh):
    """Vertex positions with every deploy key at 1, computed from the key data (the quads are collapsed at rest)."""
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


def sample(image, u, v):
    """Bilinear sample of `image` at `u, v` (both 0..1), as a 4-tuple."""
    width, height = image.size
    x = min(max(u, 0.0), 0.9999) * width
    y = min(max(v, 0.0), 0.9999) * height
    x0, y0 = int(x), int(y)
    fx, fy = x - x0, y - y0
    pixels = image.pixels
    def at(ix, iy):
        offset = (min(iy, height - 1) * width + min(ix, width - 1)) * 4
        return (pixels[offset], pixels[offset + 1], pixels[offset + 2], pixels[offset + 3])
    a, b, c, d = at(x0, y0), at(x0 + 1, y0), at(x0, y0 + 1), at(x0 + 1, y0 + 1)
    return tuple((a[i] * (1 - fx) * (1 - fy) + b[i] * fx * (1 - fy)
                  + c[i] * (1 - fx) * fy + d[i] * fx * fy) for i in range(4))


bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.preferences.addon_enable(module="bl_ext.blender_org.mmd_tools")
bpy.ops.preferences.addon_enable(module=ADDON)
bpy.ops.mmd_tools.import_model(filepath=pmx)

body = max([obj for obj in bpy.context.scene.objects if obj.type == 'MESH'],
           key=lambda item: len(item.data.vertices))
say("%s - body mesh %r, %d vertices" % (pmx, body.name, len(body.data.vertices)))

cheek_slots = [index for index, slot in enumerate(body.material_slots)
               if slot.material is not None and "cheek" in slot.material.name.lower()]
face_slots = [index for index, slot in enumerate(body.material_slots)
              if slot.material is not None and "face" in slot.material.name.lower()]
say("cheek slot(s): %s, face slot(s): %s" % (cheek_slots, face_slots))
if not cheek_slots or not face_slots:
    raise SystemExit("this model has no cheek decal or no face material")

positions = deployed_positions(body)
uv_layer = body.data.uv_layers.active
say("uv layer: %r" % (uv_layer.name if uv_layer else None))

# the blush quad, as triangles with their blush-map UVs
cheek_source = next(slot.material for slot in body.material_slots if slot.material is not None
                    and "cheek" in slot.material.name.lower())
blush_image = next((node.image for node in cheek_source.node_tree.nodes
                    if node.type == 'TEX_IMAGE' and node.image), None)
say("blush map: %r (%dx%d)" % (blush_image.name if blush_image else None,
                               blush_image.size[0] if blush_image else 0,
                               blush_image.size[1] if blush_image else 0))

triangles = []          # ((p0, p1, p2), (uv0, uv1, uv2))
for polygon in body.data.polygons:
    if polygon.material_index not in cheek_slots:
        continue
    loops = [uv_layer.data[index].uv.copy() for index in polygon.loop_indices]
    points = [positions[index] for index in polygon.vertices]
    for offset in range(1, len(points) - 1):
        triangles.append(((points[0], points[offset], points[offset + 1]),
                          (loops[0], loops[offset], loops[offset + 1])))
say("blush geometry: %d triangle(s), %d face vertices" % (len(triangles), len(positions)))


def project(point):
    """`(uv, distance)` for the closest point on the blush triangles to `point`, or `(None, distance)`."""
    best = (None, 1e9)
    for points, uvs in triangles:
        a, b, c = points
        # barycentric of the projection of `point` on the triangle's plane
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


attribute = body.data.color_attributes.get("UMA_Blush")
if attribute is None:
    attribute = body.data.color_attributes.new(name="UMA_Blush", type='BYTE_COLOR', domain='POINT')
say("attribute: %r on the %r domain" % (attribute.name, attribute.domain))

inside = 0
distances = []
colours = []
for vertex in body.data.vertices:
    colour = (1.0, 1.0, 1.0, 1.0)                 # white multiplies to "no blush"
    uv, distance = project(positions[vertex.index])
    if uv is not None:
        sampled = sample(blush_image, uv[0], uv[1])
        # the blush map is art on transparency: its alpha is the mask, its colour the tint
        strength = sampled[3]
        colour = (1.0 - strength * (1.0 - sampled[0]),
                  1.0 - strength * (1.0 - sampled[1]),
                  1.0 - strength * (1.0 - sampled[2]),
                  1.0)
        inside += 1
        distances.append(distance)
        colours.append(strength)
    attribute.data[vertex.index].color = colour

face_vertices = {index for polygon in body.data.polygons if polygon.material_index in face_slots
                 for index in polygon.vertices}
face_inside = sum(1 for index in face_vertices
                  if any(True for _ in [0]) and attribute.data[index].color[0] < 0.999)
say()
say("result")
say("     vertices whose projection landed on the blush: %d of %d (%.1f%%)"
    % (inside, len(body.data.vertices), 100.0 * inside / max(1, len(body.data.vertices))))
say("     face vertices painted: %d of %d" % (face_inside, len(face_vertices)))
if distances:
    say("     distance to the blush plane: %.4f .. %.4f (mean %.4f)"
        % (min(distances), max(distances), sum(distances) / len(distances)))
if colours:
    say("     blush strength (map alpha): %.3f .. %.3f, mean %.3f"
        % (min(colours), max(colours), sum(colours) / len(colours)))
say("     attribute is white everywhere else, so a multiply mixes it in and leaves the rest alone")

with open(out, "w", encoding="utf-8") as handle:
    handle.write("\n".join(lines) + "\n")
print("wrote %s" % out)
