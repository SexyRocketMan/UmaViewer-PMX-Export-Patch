"""Bake a CySpring chain onto a real model in Blender and render it.

    blender -b -P Tools/blender_cyspring_demo.py -- <spec.json> <out.blend> --model <model.pmx> \
        [--mp4 out.mp4] [--frames N] [--prefix Sp_Hi_Tail0] [--colliders]

`spec.json` is one real chain from `uma_spring_dump.py 1001 00 --chain <root> --swing 35`: the game's own
parameters, its bone positions in game units, and a scripted parent wag. The solver is `cyspring_port.py`,
the port that is bit-exact against the game's own C#, and `cyspring_metrics.py` reads the same run as
numbers (droop, lag, overshoot, settle) - worth running before baking, since a chain that does not lag in
the numbers will not lag on the model either.

What the bake has to get right, and did not at first: the solver's rotations are in the *prefab's* frame
(Unity, Y up) while the imported armature is in Blender's (Z up, and its own rest shape), so a rotation
crosses over as `M @ R @ M^-1`, with `M` taken per bone from that bone's own rest direction. Applied
directly, the tail turned about axes about 87 degrees away from the ones the solver used.

The PMX is imported with mmd_tools (meshes and armature only - MMD's own rigid-body physics is left out,
since the bake *is* the motion) and the swing is written onto the model's own spring bones by name. What is
baked is a **world rotation per bone, applied on top of the model's rest orientation**: the solver's
rotations are rest-relative, and the model's bones have their own rest frames, so
`matrix = translation(head) @ (solver_delta @ model_rest)` carries the motion across whatever scale or
orientation the import produced. Keeping each head where the parent's pose put it means the links stay
rigid - only rotations are keyed.

Colliders are off by default; `--colliders` turns them on for the run (their authored radius is larger than
the tail it guards and their scale is unverified - see uma_spring_dump.py).
"""

from __future__ import annotations

import argparse
import json
import math
import os
import shutil
import sys

import bpy
from mathutils import Matrix, Quaternion, Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import cyspring_port as port  # noqa: E402


def parse_args() -> argparse.Namespace:
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("spec")
    parser.add_argument("blend")
    parser.add_argument("--model", default="")
    parser.add_argument("--mp4", default="")
    parser.add_argument("--keep-frames", action="store_true", help="keep the intermediate PNG frames")
    parser.add_argument("--frames", type=int, default=0)
    parser.add_argument("--colliders", action="store_true")
    parser.add_argument("--prefix", default="Sp_Hi_Tail0", help="bake bones whose name starts with this")
    return parser.parse_args(argv)


def reset_scene() -> None:
    """Empty the scene without reloading the file: `read_factory_settings` also disables the extensions,
    and mmd_tools has to be loaded for the import."""
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    bpy.context.scene.render.fps = 24
    bpy.context.scene.frame_start = 0


def import_model(path: str):
    """The PMX, meshes and armature only: the rigid bodies would fight the bake."""
    before = set(bpy.data.objects)
    bpy.ops.mmd_tools.import_model(filepath=path, scale=1.0, clean_model=True,
                                   types={"MESH", "ARMATURE"})
    for obj in bpy.data.objects:
        if obj.type == "ARMATURE" and obj not in before:
            return obj
    raise SystemExit(f"{path} imported without an armature")


def model_frame(obj: bpy.types.Object, spec: dict, baked: list[str]) -> dict[str, Quaternion]:
    """For each bone, the rotation carrying the prefab's frame onto the armature's.

    The spec's axes come from the prefab, the bones come from the PMX, and the importer puts the model in
    Blender's Z-up frame: the two are the same tail seen from two origins, so a solver rotation has to cross
    as `M @ R @ M^-1`. Applying it directly (which this file did first) leaves the baked chain ten degrees
    away from the solver's shape on every joint.

    `M` is taken per bone, from that bone's own rest direction, not once for the whole chain: the model's
    rest shape is a little different from the prefab's positions (they agree to about seven degrees), and a
    chain-wide `M` would smear that difference into the motion. Per bone, the bake reproduces the solver's
    rotation *relative to each bone's own rest*, which is what a tail looks like on this model.
    """
    rest = {name: obj.data.bones[name].matrix_local.to_quaternion() for name in baked}
    frames = {}
    for bone in spec["bones"]:
        if bone["name"] not in rest:
            continue
        spec_axis = Vector(bone["axis"]).normalized()
        model_axis = (rest[bone["name"]] @ Vector((0.0, 1.0, 0.0))).normalized()
        frames[bone["name"]] = spec_axis.rotation_difference(model_axis)
    return frames




def bake(obj: bpy.types.Object, spec: dict, frames: list, prefix: str) -> list[str]:
    """Key the solver's world rotations onto the model's bones, on top of each bone's rest orientation."""
    scene = bpy.context.scene
    baked = []
    for bone in spec["bones"]:
        if prefix and not bone["name"].startswith(prefix):
            continue
        if obj.pose.bones.get(bone["name"]) is None:
            print(f"note: {bone['name']} is not in this model")
            continue
        baked.append(bone["name"])
    if not baked:
        raise SystemExit("none of the chain's bones are in this model")

    rest_rotation = {name: obj.data.bones[name].matrix_local.to_quaternion() for name in baked}
    frame_to_model = model_frame(obj, spec, baked)
    angles = [math.degrees(rotation.angle) for rotation in frame_to_model.values()]
    print(f"  prefab frame -> model frame: {min(angles):.1f} to {max(angles):.1f} deg per bone "
          f"(the importer's Y-up to Z-up turn, plus the tail's own rest bend)")

    for frame_index, frame in enumerate(frames):
        scene.frame_set(frame_index)
        for index, bone in enumerate(spec["bones"]):
            name = bone["name"]
            if name not in baked:
                continue
            x, y, z, qx, qy, qz, qw = frame[index * 7:index * 7 + 7]
            delta = Quaternion((qw, qx, qy, qz))          # the world rotation the solver produced
            carry = frame_to_model[name]
            applied = carry @ delta @ carry.inverted()
            bpy.context.view_layer.update()
            pose_bone = obj.pose.bones[name]
            head = pose_bone.head.copy()                   # where the parent's pose put this bone
            pose_bone.matrix = Matrix.Translation(head) @ (applied @ rest_rotation[name]).to_matrix().to_4x4()
            pose_bone.keyframe_insert("rotation_quaternion", frame=frame_index)
            pose_bone.keyframe_insert("location", frame=frame_index)

    scene.frame_end = max(1, len(frames) - 1)
    return baked


def verify_bake(obj: bpy.types.Object, spec: dict, frames: list, baked: list[str]) -> None:
    """Read the keyed animation back and say numerically what it does.

    A bake is only as good as what it keyed, and the failure that matters - every link welded to its parent -
    looks like a very smooth, very quiet set of numbers. So this walks the scene again and reports, per bone,
    how far it turns per frame and how far it sits from the bone above it: a tail with no lag has the second
    column at zero, and a tail that jumps shows up in the maximum.
    """
    scene = bpy.context.scene
    names = [name for name in baked]
    directions: list[dict[str, Vector]] = []
    for frame_index in range(len(frames)):
        scene.frame_set(frame_index)
        row = {}
        for name in names:
            pose = obj.pose.bones[name]
            row[name] = (obj.matrix_world.to_quaternion() @ (pose.matrix.to_quaternion()
                                                             @ Vector((0.0, 1.0, 0.0)))).normalized()
        directions.append(row)

    print("  read back from the armature:")
    print(f"    {'bone':<22}{'turn mean°':>11}{'turn max°':>10}{'from above mean°':>18}{'max°':>7}"
          f"{'vs solver°':>12}")
    for index, name in enumerate(names):
        turns = [math.degrees(directions[f][name].angle(directions[f - 1][name]))
                 for f in range(1, len(directions))]
        if index == 0:
            above = [0.0, 0.0]
        else:
            gaps = [math.degrees(directions[f][name].angle(directions[f][names[index - 1]]))
                    for f in range(len(directions))]
            above = [sum(gaps) / len(gaps), max(gaps)]
        # the bake is exact if every baked direction is the solver's link direction carried into the model
        carry = model_frame(obj, spec, baked)[name]
        residuals = []
        bone_index = [entry["name"] for entry in spec["bones"]].index(name)
        for frame_index, frame in enumerate(frames):
            _, _, _, qx, qy, qz, qw = frame[bone_index * 7:bone_index * 7 + 7]
            delta = Quaternion((qw, qx, qy, qz))
            expected = (carry @ (delta @ Vector(spec["bones"][bone_index]["axis"]))).normalized()
            residuals.append(math.degrees(directions[frame_index][name].angle(expected)))
        print(f"    {name:<22}{sum(turns) / len(turns):11.3f}{max(turns):10.3f}"
              f"{above[0]:18.3f}{above[1]:7.3f}{max(residuals):12.4f}")


def frame_chain(obj: bpy.types.Object, baked: list[str]) -> None:
    """Point the camera at the chain, wherever the model put it."""
    heads = [obj.matrix_world @ obj.pose.bones[name].head for name in baked]
    centre = sum(heads, Vector((0.0, 0.0, 0.0))) / len(heads)
    radius = max((head - centre).length for head in heads) or 1.0

    bpy.ops.object.camera_add(location=centre + Vector((radius * 3.0, -radius * 3.0, radius * 1.0)))
    camera = bpy.context.object
    camera.rotation_euler = (centre - camera.location).to_track_quat("-Z", "Y").to_euler()
    camera.data.lens = 50
    bpy.context.scene.camera = camera

    bpy.ops.object.light_add(type="SUN", location=centre + Vector((3, -3, 5)))
    bpy.context.object.data.energy = 3.0


def configure_render(frames_dir: str) -> None:
    """Stills, assembled afterwards by the ffmpeg on the path.

    Blender's own FFMPEG output wrote nothing in a session with the extensions loaded, without reporting an
    error, so the animation is rendered as a PNG sequence and muxed outside Blender - which also leaves the
    frames behind, handy for picking a specific moment to look at.
    """
    scene = bpy.context.scene
    scene.render.resolution_x = 720
    scene.render.resolution_y = 540
    scene.render.resolution_percentage = 100
    settings = scene.render.image_settings
    if hasattr(settings, "media_type"):
        settings.media_type = "IMAGE"
    settings.file_format = "PNG"
    settings.color_mode = "RGB"
    # scene.render.filepath is not honoured here: the user's startup add-ons rewrite it around a render, so
    # frames silently landed in another project's dataset folder. Rendering without write_still and calling
    # Image.save_render() writes to the path we pass, whatever the handlers do.
    scene.render.filepath = os.path.join(frames_dir, "frame_")
    synth = getattr(scene, "synth_settings", None)
    if synth is not None:
        try:
            synth.auto_generate_on_render = False
        except AttributeError:
            pass
    # Workbench with the model's own textures: the question is the motion, and this needs no shader setup
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "TEXTURE"
    scene.display.shading.show_shadows = True


def assemble(frames_dir: str, mp4: str) -> None:
    import glob
    import subprocess
    frames = sorted(glob.glob(os.path.join(frames_dir, "frame_*.png")))
    if not frames:
        print("no frames were rendered, so there is no video to assemble")
        return
    pattern = os.path.join(frames_dir, "frame_%04d.png")
    subprocess.run(["ffmpeg", "-y", "-v", "error", "-framerate", "24", "-i", pattern,
                    "-c:v", "libx264", "-pix_fmt", "yuv420p", "-crf", "20", mp4], check=False)
    print(f"{len(frames)} frames -> {mp4}")


def main() -> int:
    args = parse_args()
    with open(args.spec, encoding="utf-8") as handle:
        spec = json.load(handle)
    if args.frames:
        spec["frames"] = args.frames
    if not args.colliders:
        spec = dict(spec)
        spec["colliders"] = []
        spec["bones"] = [dict(bone, colliders=[]) for bone in spec["bones"]]

    reset_scene()
    frames, _ = port.simulate_spec(spec, trace=True)
    if frames is None:
        print("the solver produced nothing")
        return 1

    if not args.model:
        raise SystemExit("--model is needed: an armature alone renders as nothing")
    armature = import_model(args.model)
    baked = bake(armature, spec, frames, args.prefix)
    verify_bake(armature, spec, frames, baked)
    frame_chain(armature, baked)

    os.makedirs(os.path.dirname(os.path.abspath(args.blend)), exist_ok=True)
    bpy.context.scene.frame_set(0)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.abspath(args.blend))
    print(f"saved {args.blend}: {len(frames)} frames baked onto {baked}")

    if args.mp4:
        frames_dir = os.path.join(os.path.dirname(os.path.abspath(args.blend)), "frames")
        os.makedirs(frames_dir, exist_ok=True)
        configure_render(frames_dir)
        scene = bpy.context.scene
        for frame_index in range(len(frames)):
            scene.frame_set(frame_index)
            bpy.ops.render.render(write_still=False)
            result = bpy.data.images.get("Render Result")
            if result is None:
                raise RuntimeError("render produced no Render Result image")
            result.save_render(os.path.join(frames_dir, f"frame_{frame_index:04d}.png"))
        assemble(frames_dir, os.path.abspath(args.mp4))
        if not args.keep_frames:
            shutil.rmtree(frames_dir, ignore_errors=True)
    return 0


if __name__ == "__main__":
    sys.exit(main())
