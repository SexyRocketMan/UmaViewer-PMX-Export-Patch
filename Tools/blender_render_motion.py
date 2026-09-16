"""Render a PMX (+ optional VMD) with mmd_tools, headless, as a frame sequence plus a still.

Turns "the export works" into something you can look at: imports the model, optionally applies a
motion, frames the upper body, lights it, and renders every frame as a PNG (plus one still for
eyeballing). Tools/run_workflow.ps1 encodes the sequence into a video with ffmpeg - this script
renders images because that also gives you individual frames to check, and because a Blender build
without ffmpeg output support can still render.

  blender --background --factory-startup --python Tools/blender_render_motion.py -- \
      --pmx D:/out/1001_00.pmx --vmd D:/out/loop.vmd --frames-dir D:/out/verify_frames --still D:/out/verify.png

Exit code 0 when the promised files exist, 1 otherwise. The last stdout line is
"@@RESULT@@ <json>" so wrappers can parse the outcome.
"""

import addon_utils
import argparse
import json
import math
import os
import shutil
import sys

import bpy
from mathutils import Matrix, Vector

MMD_TOOLS = "bl_ext.blender_org.mmd_tools"


def parse_args():
    argv = sys.argv
    argv = argv[argv.index("--") + 1:] if "--" in argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("--pmx", required=True)
    parser.add_argument("--vmd", default="")
    parser.add_argument("--frames-dir", required=True, help="directory the PNG sequence is written to")
    parser.add_argument("--still", default="", help="single png copy of one frame, for eyeballing")
    parser.add_argument("--still-frame", type=int, default=-1, help="frame to save as a still (default: middle)")
    parser.add_argument("--engine", default="eevee", choices=["eevee", "workbench", "cycles"])
    parser.add_argument("--width", type=int, default=640)
    parser.add_argument("--height", type=int, default=640)
    parser.add_argument("--samples", type=int, default=32)
    parser.add_argument("--fps", type=int, default=30)
    parser.add_argument("--scale", type=float, default=0.08, help="mmd -> blender scale used by mmd_tools")
    parser.add_argument("--view", default="upper", choices=["upper", "head", "full"],
                        help="what to frame the camera on")
    parser.add_argument("--yaw", type=float, default=18.0, help="camera rotation around the model, degrees")
    parser.add_argument("--apose", type=float, default=38.5,
                        help="rotate the arms from the model's T-pose rest into the A-pose the recorder "
                             "captures against (UnityHumanoidVMDRecorder uses 38.5); 0 disables it")
    parser.add_argument("--json", default="")
    return parser.parse_args(argv)


def enable_mmd_tools():
    addon_utils.enable(MMD_TOOLS, default_set=True)


def import_model(path, scale):
    before = set(bpy.data.objects)
    bpy.ops.mmd_tools.import_model(
        filepath=path,
        types={"MESH", "ARMATURE", "MORPHS", "DISPLAY"},
        scale=scale,
        clean_model=False,
        remove_doubles=False,
        rename_bones=False,
        fix_bone_order=True,
        apply_bone_fixed_axis=False,
    )
    return [o for o in bpy.data.objects if o not in before]


def import_motion(path, scale, armature):
    for obj in bpy.data.objects:
        obj.select_set(False)
    armature.select_set(True)
    bpy.context.view_layer.objects.active = armature
    # "Treat Current Pose as Rest Pose": the recorder captures its reference pose with the arms already
    # rotated into an A-pose, so the motion only lands correctly if that pose is the rest pose.
    bpy.ops.mmd_tools.import_vmd(filepath=path, scale=scale, use_pose_mode=True)


def rotate_about_armature_axis(pose_bone, degrees, axis="Y"):
    """Rotate a pose bone in place, around an armature-space axis through its own head."""
    matrix = pose_bone.matrix.copy()
    head = matrix.translation.copy()
    rotation = (Matrix.Translation(head)
                @ Matrix.Rotation(math.radians(degrees), 4, axis)
                @ Matrix.Translation(-head))
    pose_bone.matrix = rotation @ matrix


def apply_apose(armature, degrees):
    """Pose the arms from the T-pose rest into an A-pose, picking the direction that lowers the hands.

    The recorder rotates the upper arms by 38.5 degrees before it captures its ghosts, so a motion
    recorded from the viewer is authored against an A-pose. Which way to rotate depends on the bone
    axes the importer produced, so this measures instead of guessing.
    """
    if degrees <= 0:
        return 0.0
    view_layer = bpy.context.view_layer
    for index, arm_name in enumerate(("Arm_L", "Arm_R")):
        arm = armature.pose.bones.get(arm_name)
        if arm is None:
            continue
        wrist = armature.pose.bones.get(arm_name.replace("Arm", "Wrist"))
        if wrist is None:
            continue

        arm.rotation_mode = "QUATERNION"
        before = (armature.matrix_world @ wrist.head).z
        rotate_about_armature_axis(arm, degrees)
        view_layer.update()
        after = (armature.matrix_world @ wrist.head).z
        if after > before:  # wrong way, go twice the other way round
            rotate_about_armature_axis(arm, -2 * degrees)
            view_layer.update()
    return degrees


def world_bounds(objects):
    minimum = Vector((math.inf,) * 3)
    maximum = Vector((-math.inf,) * 3)
    for obj in objects:
        if obj.type != "MESH":
            continue
        for corner in obj.bound_box:
            point = obj.matrix_world @ Vector(corner)
            for axis in range(3):
                minimum[axis] = min(minimum[axis], point[axis])
                maximum[axis] = max(maximum[axis], point[axis])
    return minimum, maximum


def find_head(armature, bounds_min, bounds_max):
    """Prefer the head bone, fall back to the top of the bounding box."""
    if armature is not None:
        for name in ("頭", "Head", "head"):
            bone = armature.pose.bones.get(name)
            if bone is not None:
                return armature.matrix_world @ bone.head
    return Vector(((bounds_min.x + bounds_max.x) / 2, (bounds_min.y + bounds_max.y) / 2, bounds_max.z))


def add_light(name, kind, location, energy, size=1.0, color=(1.0, 1.0, 1.0), rotation=(0.0, 0.0, 0.0)):
    data = bpy.data.lights.new(name, type=kind)
    data.energy = energy
    data.color = color
    if kind == "AREA":
        data.size = size
    obj = bpy.data.objects.new(name, data)
    obj.location = location
    obj.rotation_euler = rotation
    bpy.context.scene.collection.objects.link(obj)
    return obj


def look_at(obj, target):
    direction = target - obj.location
    obj.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()


def setup_scene(args, meshes, armature):
    scene = bpy.context.scene
    bounds_min, bounds_max = world_bounds(meshes)
    head = find_head(armature, bounds_min, bounds_max)
    center = (bounds_min + bounds_max) / 2
    height = max(bounds_max.z - bounds_min.z, 0.1)

    if args.view == "head":
        target = head
        distance = height * 0.55
    elif args.view == "full":
        target = center
        distance = height * 1.9
    else:  # upper body
        target = Vector((head.x, head.y, bounds_min.z + height * 0.82))
        distance = height * 1.15

    yaw = math.radians(args.yaw)
    camera_data = bpy.data.cameras.new("VerifyCamera")
    camera_data.lens = 50
    camera = bpy.data.objects.new("VerifyCamera", camera_data)
    camera.location = Vector((math.sin(yaw) * distance, -math.cos(yaw) * distance, target.z + height * 0.06))
    bpy.context.scene.collection.objects.link(camera)
    look_at(camera, target)
    scene.camera = camera

    # three point lighting, plus a soft world so nothing renders black
    key = add_light("Key", "AREA", (distance * 0.7, -distance * 0.9, target.z + height * 0.55),
                    energy=distance * distance * 90, size=distance * 0.6)
    look_at(key, target)
    fill = add_light("Fill", "AREA", (-distance * 0.9, -distance * 0.5, target.z + height * 0.1),
                     energy=distance * distance * 30, size=distance * 0.8,
                     color=(0.85, 0.9, 1.0))
    look_at(fill, target)
    rim = add_light("Rim", "AREA", (-distance * 0.35, distance * 0.9, target.z + height * 0.5),
                    energy=distance * distance * 55, size=distance * 0.5,
                    color=(1.0, 0.93, 0.85))
    look_at(rim, target)

    world = bpy.data.worlds.new("VerifyWorld")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.18, 0.2, 0.24, 1.0)
    world.node_tree.nodes["Background"].inputs[1].default_value = 0.6
    scene.world = world
    return bounds_min, bounds_max


def configure_render(args, scene):
    scene.render.resolution_x = args.width
    scene.render.resolution_y = args.height
    scene.render.resolution_percentage = 100
    scene.render.fps = args.fps
    scene.render.film_transparent = False
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGB"
    scene.render.filepath = os.path.join(os.path.abspath(args.frames_dir), "frame_")

    if args.engine == "eevee":
        scene.render.engine = "BLENDER_EEVEE"
        scene.eevee.taa_render_samples = args.samples
    elif args.engine == "workbench":
        scene.render.engine = "BLENDER_WORKBENCH"
        scene.display.shading.color_type = "TEXTURE"
        scene.display.shading.light = "STUDIO"
    else:
        scene.render.engine = "CYCLES"
        scene.cycles.device = "CPU"
        scene.cycles.samples = args.samples


def main():
    args = parse_args()
    result = {"pmx": args.pmx, "vmd": args.vmd, "frames_dir": None, "frame_count": 0,
              "still": None, "frames": None, "engine": args.engine, "seconds": None,
              "failures": []}

    bpy.ops.wm.read_factory_settings(use_empty=True)
    enable_mmd_tools()

    objects = import_model(args.pmx, args.scale)
    meshes = [o for o in objects if o.type == "MESH"]
    armatures = [o for o in objects if o.type == "ARMATURE"]
    armature = armatures[0] if armatures else None
    result["vertices"] = sum(len(o.data.vertices) for o in meshes)
    result["shape_keys"] = max((len(o.data.shape_keys.key_blocks) for o in meshes if o.data.shape_keys),
                               default=0)
    print(f"   imported {len(meshes)} mesh(es), {len(armatures)} armature(s), "
          f"{result['vertices']} vertices, {result['shape_keys']} shape keys")
    if not meshes:
        result["failures"].append("nothing imported")
        return report(result, args)

    scene = bpy.context.scene
    if args.vmd:
        try:
            if args.apose > 0 and armature is not None:
                apply_apose(armature, args.apose)
                result["apose_degrees"] = args.apose
                print(f"   arms posed to A-pose by {args.apose} degrees before the motion import")
            import_motion(args.vmd, args.scale, armature)
            print(f"   motion applied, scene frames {scene.frame_start}..{scene.frame_end}")
        except Exception as exc:  # noqa: BLE001
            result["failures"].append(f"motion import failed: {type(exc).__name__}: {exc}")
    if scene.frame_end <= scene.frame_start:
        scene.frame_start, scene.frame_end = 1, 30
    result["frames"] = [scene.frame_start, scene.frame_end]
    result["seconds"] = round((scene.frame_end - scene.frame_start + 1) / float(args.fps), 3)

    setup_scene(args, meshes, armature)
    configure_render(args, scene)

    frames_dir = os.path.abspath(args.frames_dir)
    if os.path.isdir(frames_dir):
        shutil.rmtree(frames_dir)
    os.makedirs(frames_dir, exist_ok=True)
    print(f"   rendering frames {scene.frame_start}..{scene.frame_end} at "
          f"{args.width}x{args.height} with {args.engine} -> {frames_dir}")
    bpy.ops.render.render(animation=True)

    rendered = sorted(f for f in os.listdir(frames_dir) if f.lower().endswith(".png"))
    result["frames_dir"] = frames_dir
    result["frame_count"] = len(rendered)
    expected = scene.frame_end - scene.frame_start + 1
    print(f"   rendered {len(rendered)}/{expected} frames")
    if len(rendered) != expected:
        result["failures"].append(f"expected {expected} frames, got {len(rendered)}")

    if args.still:
        frame = args.still_frame if args.still_frame >= 0 else (scene.frame_start + scene.frame_end) // 2
        source = os.path.join(frames_dir, f"frame_{frame:04d}.png")
        if not os.path.isfile(source):
            candidates = [f for f in rendered if f"_{frame:04d}" in f]
            source = os.path.join(frames_dir, candidates[0]) if candidates else ""
        if source and os.path.isfile(source):
            os.makedirs(os.path.dirname(os.path.abspath(args.still)) or ".", exist_ok=True)
            shutil.copyfile(source, args.still)
            result["still"] = args.still
            result["still_frame"] = frame
        else:
            result["failures"].append(f"frame {frame} not found in {frames_dir}")

    return report(result, args)


def report(result, args):
    for failure in result["failures"]:
        print(f"   !! {failure}")
    passed = not result["failures"]
    print("PASS" if passed else "FAIL")
    if args.json:
        with open(args.json, "w", encoding="utf-8") as handle:
            json.dump(result, handle, indent=1)
    print("@@RESULT@@ " + json.dumps({"passed": passed, "frames_dir": result["frames_dir"],
                                      "frame_count": result["frame_count"], "still": result["still"]}))
    return 0 if passed else 1


sys.exit(main())
