"""Render a model from a set of camera angles, framed like the game screenshots, for shading comparison.

`headless_export.ps1 -ShotYaws` writes what the game draws at those angles; this renders the same view of the
same model from Blender, so the two can be put side by side. The framing is matched on purpose: the camera
sits in front of the head at 15% of the model's height with a 50 mm lens, which is what the viewer's `face`
view uses, and the angles orbit the camera around a fixed scene, which is what the game does when its camera
moves. The *lighting* cannot be matched exactly - the game has its own sun and tone mapping - so the point is
the shape of the shading, not the exact colour.

    blender --background --factory-startup --python blender_render_angles.py -- \
        --pmx model.pmx --out-dir DIR [--yaws 0,45,-45] [--compare DIR] [--apply-shader]

With `--compare` it also writes `sheet.png`: the game's shot next to Blender's for every angle.
"""

import argparse
import math
import os
import sys

import addon_utils
import bpy
from mathutils import Matrix, Vector

MMD_TOOLS = "bl_ext.blender_org.mmd_tools"
UMA_ADDON = "bl_ext.user_default.uma_addon"
# The viewer's own face view sits at 0.15 of the model height, but its screenshot renders through the
# editor's projection matrix, which is built for the screen's aspect rather than the square target, so its
# frame comes out about twice as wide. These factors are what makes the two frames line up.
VIEW_DISTANCE = {"face": 0.30, "head": 0.45, "upper": 1.1, "full": 2.2}
VIEW_LENS = {"face": 50.0, "head": 50.0, "upper": 50.0, "full": 50.0}


def enable(name):
    try:
        addon_utils.enable(name, default_set=True)
    except Exception as exc:  # noqa: BLE001
        print(f"  !! could not enable {name}: {exc}")


def facing_direction(armature, mesh):
    """The direction the model faces, in world space: the feet give it on any humanoid rig."""
    for side in ("L", "R"):
        toe = armature.data.bones.get("Toe_" + side) if armature else None
        ankle = armature.data.bones.get("Ankle_" + side) if armature else None
        if toe is None or ankle is None:
            continue
        delta = (armature.matrix_world @ toe.head_local) - (armature.matrix_world @ ankle.head_local)
        delta.z = 0.0
        if delta.length > 1e-6:
            return delta.normalized()
    return Vector((0.0, -1.0, 0.0))


def scene_setup(args, mesh, armature):
    points = [mesh.matrix_world @ vertex.co for vertex in mesh.data.vertices]
    lowest = min(point.z for point in points)
    highest = max(point.z for point in points)
    height = highest - lowest

    head_bone = armature.data.bones.get("Head") if armature else None
    head = (armature.matrix_world @ head_bone.head_local) if head_bone else Vector(
        ((min(p.x for p in points) + max(p.x for p in points)) / 2,
         (min(p.y for p in points) + max(p.y for p in points)) / 2, highest - height * 0.08))
    if args.view == "full":
        target = Vector(((min(p.x for p in points) + max(p.x for p in points)) / 2,
                         (min(p.y for p in points) + max(p.y for p in points)) / 2,
                         (lowest + highest) / 2))
    elif args.view == "upper":
        target = Vector((head.x, head.y, lowest + height * 0.82))
    else:
        target = head

    distance = height * VIEW_DISTANCE[args.view]
    camera_data = bpy.data.cameras.new("AngleCam")
    camera_data.lens = VIEW_LENS[args.view]
    camera = bpy.data.objects.new("AngleCam", camera_data)
    bpy.context.scene.collection.objects.link(camera)
    bpy.context.scene.camera = camera

    world = bpy.data.worlds.new("AngleWorld")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (args.world,) * 3 + (1.0,)
    bpy.context.scene.world = world

    facing = facing_direction(armature, mesh)
    if args.light_from == "front":
        light_direction = facing
    elif args.light_from == "upper-left":
        light_direction = (facing + Vector((0.7, 0.0, 0.7))).normalized()
    else:
        light_direction = (facing + Vector((0.0, 0.0, 1.0))).normalized()

    light_data = bpy.data.lights.new("AngleKey", type="AREA")
    light_data.energy = (distance * 1.2) ** 2 * 90
    light_data.size = distance * 0.6
    light = bpy.data.objects.new("AngleKey", light_data)
    light.location = target + light_direction * distance
    light.rotation_euler = (target - light.location).to_track_quat("-Z", "Y").to_euler()
    bpy.context.scene.collection.objects.link(light)
    print(f"   light from {tuple(round(v, 2) for v in light_direction)} "
          f"({args.light_from}), camera distance {distance:.3f}")

    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE"
    scene.eevee.taa_render_samples = args.samples
    scene.render.resolution_x = scene.render.resolution_y = args.size
    scene.render.image_settings.file_format = "PNG"
    return camera, target, distance, facing


def main():
    argv = sys.argv
    argv = argv[argv.index("--") + 1:] if "--" in argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("--pmx", required=True)
    parser.add_argument("--out-dir", required=True)
    parser.add_argument("--yaws", default="0,45,-45", help="comma separated camera angles")
    parser.add_argument("--view", default="face", choices=list(VIEW_DISTANCE))
    parser.add_argument("--apply-shader", action="store_true")
    parser.add_argument("--cheek-strength", type=float, default=None,
                        help="passed to the Shading operator when --apply-shader is used")
    parser.add_argument("--light-from", default="upper-left", choices=["front", "upper", "upper-left"])
    parser.add_argument("--world", type=float, default=0.16)
    parser.add_argument("--size", type=int, default=900)
    parser.add_argument("--samples", type=int, default=32)
    parser.add_argument("--compare", default="", help="directory of game shots named '<name>_yaw<N>.png'")
    parser.add_argument("--name", default="blender_face", help="file name stem, also the game shots' stem")
    args = parser.parse_args(argv)
    os.makedirs(args.out_dir, exist_ok=True)

    yaws = [float(part) for part in args.yaws.split(",") if part.strip()]

    bpy.ops.wm.read_factory_settings(use_empty=True)
    enable(MMD_TOOLS)
    enable(UMA_ADDON)
    bpy.ops.mmd_tools.import_model(filepath=args.pmx, types={"MESH", "ARMATURE", "MORPHS", "DISPLAY"},
                                   scale=0.08, clean_model=False, rename_bones=False,
                                   fix_bone_order=True, apply_bone_fixed_axis=False)
    mesh = next((o for o in bpy.data.objects if o.type == "MESH"), None)
    armature = next((o for o in bpy.data.objects if o.type == "ARMATURE"), None)
    if mesh is None:
        print("!! no mesh in that model")
        return

    if args.apply_shader:
        for other in bpy.data.objects:
            other.select_set(False)
        mesh.select_set(True)
        bpy.context.view_layer.objects.active = mesh
        kwargs = {}
        if args.cheek_strength is not None:
            kwargs["cheek_shading_strength"] = args.cheek_strength
        print(f"== apply_shader({kwargs}) -> {bpy.ops.uma.apply_shader(**kwargs)}")

    camera, target, distance, facing = scene_setup(args, mesh, armature)
    written = {}
    for yaw in yaws:
        # the game orbits its camera around the model: the same thing here is the facing vector turned about
        # the world's up axis, with the camera at the target's height
        # negated: the viewer's yaw turns its camera the other way round from a Blender Z rotation, and
        # the comparison is much easier to read when the two frames show the same side
        direction = (Matrix.Rotation(math.radians(-yaw), 4, 'Z') @ facing).normalized()
        direction.z = 0.0
        direction.normalize()
        camera.location = target + direction * distance
        camera.rotation_euler = (target - camera.location).to_track_quat("-Z", "Y").to_euler()
        label = f"{yaw:+0.0f}" if yaw else "0"
        path = os.path.join(args.out_dir, f"{args.name}_yaw{label}.png")
        bpy.context.scene.render.filepath = path
        bpy.ops.render.render(write_still=True)
        written[yaw] = path
        print(f"   rendered {path}")

    if args.compare:
        # Blender's own image API, so the comparison needs nothing installed
        made = 0
        for yaw, path in written.items():
            label = f"{yaw:+0.0f}" if yaw else "0"
            game = os.path.join(args.compare, f"{args.name}_yaw{label}.png")
            if not os.path.exists(game):
                print(f"   no game shot at {game}")
                continue
            images = []
            for source in (game, path):
                image = bpy.data.images.load(source)
                height = min(image.size[1], 700)
                width = max(1, int(image.size[0] * height / image.size[1]))
                image.scale(width, height)
                images.append((image, width, height))
            height = images[0][2]
            width = sum(entry[1] for entry in images) + 8
            row = bpy.data.images.new(f"compare_yaw{label}", width, height, alpha=False)
            buffer = [0.03] * (width * height * 4)
            offset = 0
            for image, image_width, image_height in images:
                source = list(image.pixels)
                for y in range(min(image_height, height)):
                    source_row = source[y * image_width * 4:(y + 1) * image_width * 4]
                    start = (y * width + offset) * 4
                    buffer[start:start + len(source_row)] = source_row
                offset += image_width + 8
            row.pixels = buffer
            row.filepath_raw = os.path.join(args.out_dir, f"compare_yaw{label}.png")
            row.file_format = 'PNG'
            row.save()
            made += 1
            print(f"   wrote {row.filepath_raw}: the game's shot on the left, Blender's on the right")
        if made == 0:
            print(f"   no game shots found in {args.compare} (they are written by "
                  f"headless_export.ps1 -ShotYaws with the same -Screenshot name stem)")


main()
