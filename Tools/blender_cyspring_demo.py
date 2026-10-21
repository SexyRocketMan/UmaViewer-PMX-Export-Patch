"""Bake a CySpring chain onto a real model in Blender and render it.

    blender -b -P Tools/blender_cyspring_demo.py -- <spec.json> <out.blend> --model <model.pmx> \
        [--mp4 out.mp4] [--frames N] [--prefix Sp_Hi_Tail0] [--colliders]

`spec.json` is one real chain from `uma_spring_dump.py 1001 00 --chain <root> --segments child`: the game's
own parameters, its bone positions in game units, and a scripted parent swing. The solver is
`cyspring_port.py`, the port that is bit-exact against the game's own C#.

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

    for frame_index, frame in enumerate(frames):
        scene.frame_set(frame_index)
        for index, bone in enumerate(spec["bones"]):
            name = bone["name"]
            if name not in baked:
                continue
            x, y, z, qx, qy, qz, qw = frame[index * 7:index * 7 + 7]
            delta = Quaternion((qw, qx, qy, qz))          # the world rotation the solver produced
            bpy.context.view_layer.update()
            pose_bone = obj.pose.bones[name]
            head = pose_bone.head.copy()                   # where the parent's pose put this bone
            pose_bone.matrix = Matrix.Translation(head) @ (delta @ rest_rotation[name]).to_matrix().to_4x4()
            pose_bone.keyframe_insert("rotation_quaternion", frame=frame_index)
            pose_bone.keyframe_insert("location", frame=frame_index)

    scene.frame_end = max(1, len(frames) - 1)
    return baked


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
