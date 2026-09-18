"""Render a model from a set of camera angles, framed like the game screenshots, for shading comparison.

`headless_export.ps1 -ShotYaws` writes what the game draws at those angles; this renders the same view of the
same model from Blender and can put the two side by side. The camera orbits a fixed scene exactly as the
viewer's does, and the distance factors are tuned so the two frames line up - the viewer's shot renders
through the editor's projection matrix, which is built for the screen's aspect rather than the square target
it writes, so it comes out about twice as wide as the same numbers suggest.

    blender --background --factory-startup --python blender_render_angles.py -- \
        --pmx model.pmx --out-dir DIR --yaws 0,45,-45 --apply-shader \
        --compare GAME_SHOT_DIR --name game_face [--window 0.66,0.90]

`--window lo,hi` writes a second pair of images per angle with that luminance window stretched to full range,
which is how faint shading structure is compared: on skin tones a difference of a couple of levels is invisible
until it is stretched.

The lighting cannot be matched exactly - the game has its own sun and tone mapping - so compare the *shape* of
the shading, not the exact colour.
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

# The face factor is measured against the viewer's own face view, not tuned by eye: rendered at 0.15,
# 0.30, 0.60 and 1.00 and tiled against the viewer's shot, 0.60 is the one that frames the same way - a
# whole head with shoulders, not a face filling the frame. It matters more than it looks: at 0.30 the
# comparison crop is mostly background, and the face crop's mean was 201 against the viewer's 150, while at
# 0.60 it is 140 against 150. That 51-level gap is what both handoff documents called the largest
# unexplained difference in this project; it was the framing.
# The other three views have not been checked this way.
# Calibrated against the viewer's own view distances. Matching the visible extent at the subject's depth is
#     ours = viewer_nominal * (H_viewer / H_blender) * tan(fov_viewer / 2) / tan(19.8deg)
# and the viewer's shot renders through the editor's projection rather than the 39.6 degrees it sets - the
# note above calls it "about twice as wide" - while its model measures 2.92 units tall against this tool's
# 1.5994. The product is 4.19, which predicts 0.63 for the face against the 0.60 that was measured by
# rendering four candidate distances and tiling them against the viewer's shot. The face value is measured;
# the other three follow from the same camera and projection and have not been checked individually.
VIEW_DISTANCE = {"face": 0.60, "head": 1.17, "upper": 3.35, "full": 7.96}
VIEW_LENS = 50.0


def enable(name):
    try:
        addon_utils.enable(name, default_set=True)
    except Exception as exc:  # noqa: BLE001
        print(f"  !! could not enable {name}: {exc}")


def facing_direction(armature):
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
    """Camera, light and render settings, with the framing the viewer's own views use."""
    points = [mesh.matrix_world @ vertex.co for vertex in mesh.data.vertices]
    lowest = min(point.z for point in points)
    highest = max(point.z for point in points)
    height = highest - lowest
    centre = Vector(((min(point.x for point in points) + max(point.x for point in points)) / 2,
                     (min(point.y for point in points) + max(point.y for point in points)) / 2,
                     (lowest + highest) / 2))

    head_bone = armature.data.bones.get("Head") if armature else None
    head = (armature.matrix_world @ head_bone.head_local) if head_bone else Vector(
        (centre.x, centre.y, highest - height * 0.08))
    if args.view == "full":
        target = centre
    elif args.view == "upper":
        target = Vector((head.x, head.y, lowest + height * 0.82))
    else:
        target = head

    factor = args.view_distance if getattr(args, "view_distance", None) else VIEW_DISTANCE[args.view]
    distance = height * factor
    print(f"   camera distance factor {factor:g} of a model {height:.4f} high "
          + ("(overridden)" if getattr(args, "view_distance", None) else f"(the {args.view} default)"))
    camera_data = bpy.data.cameras.new("AngleCam")
    camera_data.lens = VIEW_LENS
    camera = bpy.data.objects.new("AngleCam", camera_data)
    bpy.context.scene.collection.objects.link(camera)
    bpy.context.scene.camera = camera

    world = bpy.data.worlds.new("AngleWorld")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (args.world,) * 3 + (1.0,)
    bpy.context.scene.world = world

    facing = facing_direction(armature)
    if args.light_from == "front":
        light_direction = facing
    elif args.light_from == "upper-left":
        light_direction = (facing + Vector((0.7, 0.0, 0.7))).normalized()
    elif args.light_from == "upper-right":
        light_direction = (facing + Vector((-0.7, 0.0, 0.7))).normalized()
    else:
        light_direction = (facing + Vector((0.0, 0.0, 1.0))).normalized()
    # A SUN, not an area light: the addon's cheek test reads the scene's sun lamp for its light direction (a
    # shader cannot be handed one), so the shading and the cheek band agree on where the light is.
    light_data = bpy.data.lights.new("AngleKey", type="SUN")
    light_data.energy = args.light_energy
    light_data.angle = 0.02
    light = bpy.data.objects.new("AngleKey", light_data)
    light.location = target + light_direction * distance
    light.rotation_euler = (target - light.location).to_track_quat("-Z", "Y").to_euler()
    bpy.context.scene.collection.objects.link(light)
    print(f"   light from {tuple(round(v, 2) for v in light_direction)} ({args.light_from}), "
          f"camera distance {distance:.3f}, headed at {tuple(round(v, 3) for v in target)}")

    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE"
    scene.eevee.taa_render_samples = args.samples
    scene.render.resolution_x = scene.render.resolution_y = args.size
    scene.render.image_settings.file_format = "PNG"
    # The viewer is a plain linear-ish pipeline; Blender's default AgX view transform desaturates and lifts the
    # midtones, which made every comparison look pale and washed out against it. Standard is the honest match.
    scene.view_settings.view_transform = "Standard"
    scene.view_settings.look = "None"
    return camera, target, distance, facing, light


def load_scaled(path, height_limit=700):
    """A Blender image, scaled to the comparison height."""
    image = bpy.data.images.load(path)
    height = min(image.size[1], height_limit)
    width = max(1, int(image.size[0] * height / image.size[1]))
    image.scale(width, height)
    return image, width, height


def stretch_luminance(image, low, high):
    """Window the image: below 'low' goes black, above 'high' white, the rest spread over the full range."""
    pixels = list(image.pixels)
    for index in range(0, len(pixels), 4):
        for channel in range(3):
            value = (pixels[index + channel] - low) / (high - low)
            pixels[index + channel] = 0.0 if value < 0.0 else (1.0 if value > 1.0 else value)
    image.pixels = pixels
    return image


def compose(left_path, right_path, out_path, window=None):
    """The game's shot next to Blender's, optionally with both windowed."""
    images = []
    for source in (left_path, right_path):
        image, width, height = load_scaled(source)
        if window:
            stretch_luminance(image, window[0], window[1])
        images.append((image, width, height))
    height = images[0][2]
    width = sum(entry[1] for entry in images) + 8
    row = bpy.data.images.new(os.path.basename(out_path), width, height, alpha=False)
    buffer = [0.03] * (width * height * 4)
    offset = 0
    for image, image_width, image_height in images:
        source_pixels = list(image.pixels)
        for y in range(min(image_height, height)):
            source_row = source_pixels[y * image_width * 4:(y + 1) * image_width * 4]
            start = (y * width + offset) * 4
            buffer[start:start + len(source_row)] = source_row
        offset += image_width + 8
    row.pixels = buffer
    row.filepath_raw = out_path
    row.file_format = 'PNG'
    row.save()
    print(f"   wrote {out_path}: the game's shot on the left, Blender's on the right")


def montage(paths, columns, out_path):
    """Tile rendered images into one picture: rows of `columns`, in the order given."""
    loaded = [load_scaled(path, height_limit=460) for path in paths]
    cell_width = max(entry[1] for entry in loaded)
    cell_height = max(entry[2] for entry in loaded)
    rows = (len(loaded) + columns - 1) // columns
    width = cell_width * columns + 4 * (columns - 1)
    height = cell_height * rows + 4 * (rows - 1)
    sheet = bpy.data.images.new(os.path.basename(out_path), width, height, alpha=False)
    buffer = [0.05] * (width * height * 4)
    for index, (image, image_width, image_height) in enumerate(loaded):
        column, row = index % columns, index // columns
        offset_x = column * (cell_width + 4)
        offset_y = (rows - 1 - row) * (cell_height + 4)
        source = list(image.pixels)
        for y in range(image_height):
            source_row = source[y * image_width * 4:(y + 1) * image_width * 4]
            start = ((offset_y + y) * width + offset_x) * 4
            buffer[start:start + len(source_row)] = source_row
    sheet.pixels = buffer
    sheet.filepath_raw = out_path
    sheet.file_format = 'PNG'
    sheet.save()
    print(f"   wrote {out_path}: {columns}x{rows} montage")


def main():
    argv = sys.argv
    argv = argv[argv.index("--") + 1:] if "--" in argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("--pmx", required=True)
    parser.add_argument("--out-dir", required=True)
    parser.add_argument("--yaws", default="0,45,-45", help="comma separated camera angles")
    parser.add_argument("--view", default="face", choices=list(VIEW_DISTANCE))
    parser.add_argument("--view-distance", type=float, default=None,
                        help="override the camera distance as a fraction of the model's height, to match the "
                             "viewer's framing by measurement instead of by argument (the viewer's own face "
                             "view is 0.15 of its model's height)")
    parser.add_argument("--apply-shader", action="store_true")
    parser.add_argument("--no-cylinder-blend", action="store_true",
                        help="apply the game shading without the cylinder normal blend, to isolate its effect")
    parser.add_argument("--face-shadow-alpha", type=float, default=None,
                        help="override the alpha that gates the addon's cheek and nose regions, so they can "
                             "be rendered and compared against the same regions forced on in the viewer. "
                             "The exported value is their rest value of 0, which leaves them inert")
    parser.add_argument("--face-mask", default="", choices=["", "triple", "toon"],
                        help="which texture the game's face step reads its mask from: 'triple' is "
                             "_TripleMaskMap (*_base), the register the game's pixel program actually reads, "
                             "and 'toon' is _ToonMap (*_shad_c), the earlier reading. Empty uses the addon's "
                             "own default")
    parser.add_argument("--isolate-game-step", action="store_true",
                        help="zero the shipped node group's own metallic, highlight, rim, ambient and "
                             "emission terms, leaving only the game's lit/shaded step - which is all the "
                             "port reproduces. For measuring how much of the difference the group accounts for")
    parser.add_argument("--legacy", action="store_true",
                        help="apply the shader with the fork's face work switched off, i.e. as upstream shades it")
    parser.add_argument("--subdivide", type=int, default=0,
                        help="SIMPLE subdivision level for shading: no vertex moves, the boundary just gets more triangles to cross")
    parser.add_argument("--new-face", action="store_true",
                        help="apply the fork's face shading (legacy is the addon's default now, so this is what "
                             "has to be asked for explicitly)")
    parser.add_argument("--cheek-strength", type=float, default=None,
                        help="passed to the Shading operator when --apply-shader is used")
    parser.add_argument("--light-from", default="upper-right",
                        choices=["front", "upper", "upper-left", "upper-right"],
                        help="where the key light comes from; upper-right puts the cheek shadow on the side the "
                             "+45 and -45 orbits look at, which is what the game's own shots show")
    parser.add_argument("--world", type=float, default=0.16)
    parser.add_argument("--light-energy", type=float, default=3.0,
                        help="sun lamp strength; the game's key light is brighter than a default scene light")
    parser.add_argument("--size", type=int, default=900)
    parser.add_argument("--samples", type=int, default=32)
    parser.add_argument("--compare", default="", help="directory of game shots named '<name>_yaw<N>.png'")
    parser.add_argument("--window", default="",
                        help="'lo,hi': also write both sides with that luminance window stretched to full "
                             "range, which is how faint shading structure is compared")
    parser.add_argument("--name", default="game_face", help="file name stem, also the game shots' stem")
    parser.add_argument("--morph", action="append", default=[],
                        help="NAME=VALUE, repeatable: set every shape key whose name contains NAME (case "
                             "insensitive) to VALUE, e.g. --morph Mouth=1.0 --morph Eye=0")
    parser.add_argument("--list-morphs", action="store_true", help="print the model's shape keys and stop")
    parser.add_argument("--light-elevation", type=float, default=35.0,
                        help="degrees above the horizon for the grid's sun. The viewer's own light sits at 70, "
                             "and comparing the two at different elevations compares two different pictures: at a "
                             "high elevation the face's N.L barely varies, so the toon step never crosses it")
    parser.add_argument("--grid", default="",
                        help="'azimuths': render the camera yaws against each sun azimuth and tile them into "
                             "grid.png, which is how the diff/shad transition becomes visible")
    args = parser.parse_args(argv)
    os.makedirs(args.out_dir, exist_ok=True)
    yaws = [float(part) for part in args.yaws.split(",") if part.strip()]

    window = None
    if args.window:
        parts = [float(part) for part in args.window.split(",") if part.strip()]
        if len(parts) == 2 and parts[1] > parts[0]:
            window = (parts[0], parts[1])
            print(f"   also writing both sides with the luminance window {window[0]}-{window[1]} stretched")

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

    camera, target, distance, facing, light = scene_setup(args, mesh, armature)

    if args.list_morphs:
        for key in mesh.data.shape_keys.key_blocks if mesh.data.shape_keys else []:
            print(f"   morph {key.name!r} = {key.value:g}")
        return

    for entry in args.morph:
        name, _, raw = entry.partition("=")
        try:
            value = float(raw)
        except ValueError:
            print(f"   !! --morph {entry}: {raw!r} is not a number")
            continue
        needle = name.strip().lower()
        touched = 0
        for key in (mesh.data.shape_keys.key_blocks if mesh.data.shape_keys else []):
            if needle in key.name.lower():
                key.value = value
                touched += 1
                print(f"   morph {key.name!r} set to {value:g}")
        if touched == 0:
            print(f"   !! no shape key matched {name!r} - use --list-morphs to see them")

    if args.apply_shader:
        # after the scene is built: the addon's cheek test reads the scene's sun lamp for its light direction,
        # so the light has to exist before Shading runs, otherwise the test falls back to its own guess
        for other in bpy.data.objects:
            other.select_set(False)
        mesh.select_set(True)
        bpy.context.view_layer.objects.active = mesh
        kwargs = {}
        if args.subdivide:
            kwargs["shade_subdivide"] = args.subdivide
        if args.legacy:
            kwargs["legacy_shading"] = True
        elif args.new_face:
            kwargs["legacy_shading"] = False
        if getattr(args, "no_cylinder_blend", False):
            kwargs["game_cylinder_blend"] = False
        if getattr(args, "face_mask", None):
            kwargs["game_face_mask"] = args.face_mask
        if getattr(args, "face_shadow_alpha", None) is not None:
            kwargs["game_face_shadow_alpha"] = args.face_shadow_alpha
        if args.cheek_strength is not None:
            kwargs["cheek_shading_strength"] = args.cheek_strength
        print(f"== apply_shader({kwargs}) -> {bpy.ops.uma.apply_shader(**kwargs)}")

        if getattr(args, "isolate_game_step", False):
            # The game's face program does its own specular, rim and environment terms, and the port
            # implements the lit/shaded step only and hands the result to the shipped node group, which
            # then adds its own metallic, highlight, rim and ambient lighting on top. Zeroing those
            # leaves the step's colour alone, which is what the port actually reproduces - so this shows
            # how much of the difference between our face and the game's is the group rather than the port.
            zeroed = []
            for material in bpy.data.materials:
                if not material.use_nodes or "face" not in material.name.lower():
                    continue
                for node in material.node_tree.nodes:
                    if node.type != "GROUP" or not node.node_tree:
                        continue
                    for socket_name in ("Metallic Intensity", "Highlight Intensity",
                                        "Rimlight Intensity", "Ambient Rimlight Intensity",
                                        "Emission Intensity"):
                        socket = node.inputs.get(socket_name)
                        if socket is not None and socket.default_value:
                            socket.default_value = 0.0
                            zeroed.append(f"{material.name}:{socket_name}")
            print(f"== isolated the game's step: zeroed {len(zeroed)} group input(s)"
                  + (f", e.g. {zeroed[:3]}" if zeroed else ""))
    written = {}
    for yaw in yaws:
        # negated: the viewer's yaw turns its camera the other way round from a Blender Z rotation, and the
        # comparison is much easier to read when the two frames show the same side
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

    if args.grid:
        azimuths = [float(part) for part in args.grid.split(",") if part.strip()]
        paths = []
        for azimuth in azimuths:
            # swing the sun around the model: the camera stays put, which is what makes the transition visible
            direction = (Matrix.Rotation(math.radians(azimuth), 4, 'Z') @ facing).normalized()
            direction.z = 0.0
            direction.normalize()
            elevation = math.radians(args.light_elevation)
            light_direction = (direction * math.cos(elevation)
                               + Vector((0.0, 0.0, 1.0)) * math.sin(elevation)).normalized()
            light.location = target + light_direction * distance
            light.rotation_euler = (target - light.location).to_track_quat("-Z", "Y").to_euler()
            print(f"   sun at azimuth {azimuth:+.0f}, elevation {args.light_elevation:.0f} "
                  f"-> direction {tuple(round(v, 3) for v in light_direction)}")
            for yaw in yaws:
                camera_direction = (Matrix.Rotation(math.radians(-yaw), 4, 'Z') @ facing).normalized()
                camera_direction.z = 0.0
                camera_direction.normalize()
                camera.location = target + camera_direction * distance
                camera.rotation_euler = (target - camera.location).to_track_quat("-Z", "Y").to_euler()
                label = f"{yaw:+0.0f}" if yaw else "0"
                path = os.path.join(args.out_dir, f"grid_az{azimuth:+0.0f}_yaw{label}.png")
                bpy.context.scene.render.filepath = path
                bpy.ops.render.render(write_still=True)
                paths.append(path)
                print(f"   rendered {path}")
        if paths:
            montage(paths, len(yaws), os.path.join(args.out_dir, "grid.png"))

    if not args.compare:
        return
    made = 0
    for yaw, path in written.items():
        label = f"{yaw:+0.0f}" if yaw else "0"
        game = os.path.join(args.compare, f"{args.name}_yaw{label}.png")
        if not os.path.exists(game):
            print(f"   no game shot at {game}")
            continue
        compose(game, path, os.path.join(args.out_dir, f"compare_yaw{label}.png"))
        made += 1
        if window:
            compose(game, path, os.path.join(args.out_dir, f"compare_yaw{label}_window.png"), window)
    if made == 0:
        print(f"   no game shots found in {args.compare} (headless_export.ps1 -ShotYaws writes them with the "
              f"same -Screenshot name stem)")


main()
