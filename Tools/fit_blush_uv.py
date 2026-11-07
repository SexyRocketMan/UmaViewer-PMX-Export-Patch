"""Is there one static UV transform from the face atlas to the blush map?

    blender --background --factory-startup --python Tools/fit_blush_uv.py -- <out.txt> [<file.pmx>]

The user's idea: every uma shares the face model, so rather than baking a blush layer per character, ship *one*
transform with the add-on and let the face material sample the blush map through it. If that transform is a rotation,
a uniform scale and an offset, a Blender `Mapping` node can express it exactly and no bake is needed at all.

So this measures the mapping instead of assuming it: for every face vertex whose projection lands on the blush quad,
it pairs (face atlas UV) with (blush map UV), fits the best affine map between them, and reports

* the residual of the full affine fit, in UV units and in blush texels;
* the residual of the **conformal** fit (rotation + uniform scale + offset), which is the largest class of transform a
  `Mapping` node can express - if that is close enough, the node can do it and nothing needs baking;
* how much of the affine's stretch is shear, i.e. how much is lost by dropping it.
"""
import sys

import bpy
from mathutils import Matrix, Vector

ADDON = "bl_ext.user_default.uma_addon"

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
bpy.ops.preferences.addon_enable(module=ADDON)
bpy.ops.mmd_tools.import_model(filepath=pmx)

body = max([obj for obj in bpy.context.scene.objects if obj.type == 'MESH'],
           key=lambda item: len(item.data.vertices))
cheek_slots = [index for index, slot in enumerate(body.material_slots)
               if slot.material is not None and "cheek" in slot.material.name.lower()]
face_slots = [index for index, slot in enumerate(body.material_slots)
              if slot.material is not None and "face" in slot.material.name.lower()]
positions = deployed_positions(body)
uv_layer = body.data.uv_layers.active
blush_image = next((node.image for slot in body.material_slots if slot.material is not None
                    and "cheek" in slot.material.name.lower()
                    for node in slot.material.node_tree.nodes if node.type == 'TEX_IMAGE' and node.image), None)
say("%s - blush map %r (%dx%d)" % (pmx, blush_image.name, blush_image.size[0], blush_image.size[1]))

triangles = []
for polygon in body.data.polygons:
    if polygon.material_index not in cheek_slots:
        continue
    loops = [uv_layer.data[index].uv.copy() for index in polygon.loop_indices]
    points = [positions[index] for index in polygon.vertices]
    for offset in range(1, len(points) - 1):
        triangles.append(((points[0], points[offset], points[offset + 1]),
                          (loops[0], loops[offset], loops[offset + 1])))


def project(point):
    """`(blush uv, distance)` for the closest point on the blush triangles, or `(None, distance)`."""
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


pairs = []
rejected = 0
# The quad is lifted ~0.06 off the face (the exporter's own clearance), so a *true* pair is a vertex about that far
# from its plane. Everything else that projects inside the triangle is on the far side of the head - pairing those
# wrecked the first fit (residual 32 UV units, which is nonsense on a 0..1 map).
NEAREST, FARTHEST = 0.01, 0.14
for polygon in body.data.polygons:
    if polygon.material_index not in face_slots:
        continue
    for loop_index in polygon.loop_indices:
        vertex = body.data.loops[loop_index].vertex_index
        blush_uv, distance = project(positions[vertex])
        if blush_uv is None:
            continue
        if not (NEAREST <= distance <= FARTHEST):
            rejected += 1
            continue
        face_uv = uv_layer.data[loop_index].uv
        pairs.append((Vector(face_uv), Vector(blush_uv), distance))
say("pairs (face-atlas UV -> blush UV) at clearance distance: %d (%d rejected as too far or too near)"
    % (len(pairs), rejected))
if len(pairs) < 8:
    raise SystemExit("not enough pairs to fit anything")


def solve_affine(items):
    """Least-squares 2x3 affine `face uv -> blush uv`, via the normal equations."""
    ata = Matrix(((0.0,) * 3,) * 3)
    atb = [Vector((0.0, 0.0, 0.0)) for _ in range(3)]        # 3D, so a 3x3 inverse can be applied to it
    for face_uv, blush_uv, _distance in items:
        row = Vector((face_uv.x, face_uv.y, 1.0))
        for i in range(3):
            for j in range(3):
                ata[i][j] += row[i] * row[j]
            atb[i] += row[i] * Vector((blush_uv.x, blush_uv.y, 0.0))
    inverse = ata.inverted()
    solution = [inverse @ atb[i] for i in range(3)]
    return solution        # [ (a, b) per input axis ] -> blush = a*u + b*v + c


def apply_affine(solution, face_uv):
    return Vector((solution[0].x * face_uv.x + solution[1].x * face_uv.y + solution[2].x,
                   solution[0].y * face_uv.x + solution[1].y * face_uv.y + solution[2].y))


solution = solve_affine(pairs)
errors = [(apply_affine(solution, face_uv) - blush_uv).length for face_uv, blush_uv, _d in pairs]
rms = (sum(error * error for error in errors) / len(errors)) ** 0.5
say()
say("full affine fit")
say("     blush_uv = M * face_uv + t,  M = [[%+.4f, %+.4f], [%+.4f, %+.4f]], t = (%+.4f, %+.4f)"
    % (solution[0].x, solution[1].x, solution[0].y, solution[1].y, solution[2].x, solution[2].y))
say("     residual: rms %.5f, worst %.5f UV units (%d x %d map: 1 texel = %.5f UV)"
    % (rms, max(errors), blush_image.size[0], blush_image.size[1],
       1.0 / max(blush_image.size[0], blush_image.size[1])))
say("     worst place: a vertex %.1f texels off" % (max(errors) * max(blush_image.size[0], blush_image.size[1])))

# the conformal fit a `Mapping` node can express: one rotation, one uniform scale, one offset
linear = Matrix(((solution[0].x, solution[1].x), (solution[0].y, solution[1].y)))
determinant = linear.determinant()
if determinant < 0:
    say("     note: the affine MIRRORS (determinant %.4f) - a Mapping node cannot mirror on its own" % determinant)
angle = linear[0][0] ** 2 + linear[1][0] ** 2
scale = (abs(determinant)) ** 0.5
say()
say("what a Mapping node can express")
say("     mean scale %.4f, area-preserving (determinant %.4f)" % (scale, determinant))
conformal = None
try:
    u, _s, v = linear.svd()
    rotation = u @ v.transposed()
    conformal = Matrix(((rotation[0][0] * scale, rotation[0][1] * scale),
                        (rotation[1][0] * scale, rotation[1][1] * scale)))
    say("     rotation %.2f degrees" % (rotation.to_euler().z * 57.29577951308232))
except (ValueError, AttributeError):
    say("     svd unavailable in this Blender; shear not decomposed")

if conformal is not None:
    # offset from the centroids, so the fit is centred rather than anchored at the origin
    face_centre = sum((face_uv for face_uv, _b, _d in pairs), Vector((0.0, 0.0))) / len(pairs)
    blush_centre = sum((blush_uv for _f, blush_uv, _d in pairs), Vector((0.0, 0.0))) / len(pairs)
    offset = blush_centre - conformal @ face_centre
    conformal_errors = [((conformal @ face_uv + offset) - blush_uv).length
                        for face_uv, blush_uv, _d in pairs]
    conformal_rms = (sum(error * error for error in conformal_errors) / len(conformal_errors)) ** 0.5
    say("     scale/rotate/offset only: offset (%+.4f, %+.4f), residual rms %.5f, worst %.5f UV"
        % (offset.x, offset.y, conformal_rms, max(conformal_errors)))
    say("     worst place: %.1f texels off"
        % (max(conformal_errors) * max(blush_image.size[0], blush_image.size[1])))
    verdict = ("a Mapping node is enough" if max(conformal_errors) * max(blush_image.size[0],
                                                                         blush_image.size[1]) < 3
               else "a Mapping node is not enough - the mapping needs shear or a bake")
    say()
    say("VERDICT: %s" % verdict)

with open(out, "w", encoding="utf-8") as handle:
    handle.write("\n".join(lines) + "\n")
print("wrote %s" % out)
