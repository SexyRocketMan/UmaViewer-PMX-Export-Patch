"""Bake a CySpring chain in Blender and render it, so a human can judge the motion.

This runs *inside* Blender:

    blender -b -P Tools/blender_cyspring_demo.py -- <spec.json> <out.blend> [--mp4 out.mp4]
            [--frames N] [--scale S] [--no-colliders]

`spec.json` is what `uma_spring_dump.py 1001 00 --chain <root>` writes: one real chain, its parameters,
its colliders (optional here) and a scripted parent swing. The solver is `cyspring_port.py` - the port that
is bit-exact against the game's own C# (`--compare`), so what is rendered is the game's algorithm, not an
approximation of it.

Why the bones are driven by matrices rather than by pose-bone rotations: the solver produces a *world*
rotation for every bone, and a world rotation is only what an animator sees if the parent's motion is
already accounted for. Setting `pose_bone.matrix` lets Blender do that subtraction, in hierarchy order, and
then `keyframe_insert` stores whatever basis it worked out - so the baked animation is the solver's motion
and nothing else.

Colliders are off by default (`--colliders` turns them on): their radius in the game's data is larger than
the tail it is supposed to keep out of the hip, and the code that builds them is not in the decompiled
subset, so their scale is unverified. Without them the motion is stiffness, drag and gravity only.
"""

from __future__ import annotations

import argparse
import json
import math
import os
import sys

import bpy
from mathutils import Matrix, Quaternion, Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import cyspring_port as port  # noqa: E402  (the solver, shared with the CLI)


def parse_args() -> argparse.Namespace:
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("spec")
    parser.add_argument("blend")
    parser.add_argument("--mp4", default="")
    parser.add_argument("--frames", type=int, default=0, help="override the spec's frame count")
    parser.add_argument("--scale", type=float, default=4.0, help="model units to Blender units")
    parser.add_argument("--colliders", action="store_true", help="test against the chain's colliders")
    return parser.parse_args(argv)


def reset_scene() -> None:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.render.fps = 24
    scene.frame_start = 0


def build_armature(spec: dict, scale: float) -> bpy.types.Object:
    armature = bpy.data.armatures.new("cyspring")
    obj = bpy.data.objects.new("CySpring", armature)
    bpy.context.collection.objects.link(obj)

    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode="EDIT")
    previous = None
    for bone in spec["bones"]:
        edit_bone = armature.edit_bones.new(bone["name"])
        head = Vector(bone["position"]) * scale
        axis = Vector(bone["axis"])
        edit_bone.head = head
        edit_bone.tail = head + axis * max(bone["distance"], 1e-3) * scale
        # roll along a stable reference so the rest matrices are reproducible
        edit_bone.align_roll(Vector((0.0, 0.0, 1.0)))
        if previous is not None:
            edit_bone.parent = previous
            edit_bone.use_connect = False
        previous = edit_bone
    bpy.ops.object.mode_set(mode="OBJECT")
    return obj


def bake(obj: bpy.types.Object, spec: dict, frames: list, scale: float) -> None:
    """One keyframe per frame: the solver's world transform, turned into a pose by setting the matrix."""
    scene = bpy.context.scene
    names = [bone["name"] for bone in spec["bones"]]

    for frame_index, frame in enumerate(frames):
        scene.frame_set(frame_index)
        for index, name in enumerate(names):
            pose_bone = obj.pose.bones.get(name)
            if pose_bone is None:
                continue
            x, y, z, qx, qy, qz, qw = frame[index * 7:index * 7 + 7]
            rotation = Quaternion((qw, qx, qy, qz)).to_matrix().to_4x4()
            position = Matrix.Translation(Vector((x, y, z)) * scale)
            pose_bone.matrix = position @ rotation
            bpy.context.view_layer.update()
            pose_bone.keyframe_insert("location", frame=frame_index)
            pose_bone.keyframe_insert("rotation_quaternion", frame=frame_index)


def build_scene_extras(spec: dict, scale: float) -> None:
    scene = bpy.context.scene
    scene.frame_end = max(1, len(spec.get("motion") or []) - 1)

    # a small block at the pivot, so the parent's motion and the chain's lag are both readable
    pivot = Vector(spec["pivot"]) * scale
    bpy.ops.mesh.primitive_cube_add(size=0.12, location=pivot + Vector((0, 0, -0.06)))
    pivot_block = bpy.context.object
    pivot_block.name = "pivot"

    # the ground the tail hangs over, for a horizon line
    bpy.ops.mesh.primitive_plane_add(size=6, location=(0, 0, pivot.z - 1.4))
    ground = bpy.context.object
    ground.name = "ground"

    bpy.ops.object.camera_add(location=(2.6, -3.4, pivot.z + 0.4))
    camera = bpy.context.object
    direction = (pivot - camera.location)
    camera.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
    camera.data.lens = 50
    scene.camera = camera

    bpy.ops.object.light_add(type="SUN", location=(3, -3, 6))
    bpy.context.object.data.energy = 3.0


def configure_render(mp4: str) -> None:
    scene = bpy.context.scene
    scene.render.resolution_x = 640
    scene.render.resolution_y = 480
    scene.render.resolution_percentage = 100
    # Blender 5 splits image and video settings: the media type has to say "video" before a video format
    # is offered at all, and 5.2 no longer lists FFMPEG under the image formats.
    settings = scene.render.image_settings
    if hasattr(settings, "media_type"):
        settings.media_type = "VIDEO"
    settings.file_format = "FFMPEG"
    scene.render.ffmpeg.format = "MPEG4"
    scene.render.ffmpeg.codec = "H264"
    scene.render.ffmpeg.constant_rate_factor = "HIGH"
    scene.render.filepath = mp4
    # Workbench draws with the viewport's studio light and needs no shader compilation: the point of the
    # render is to read the motion, not the shading.
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "OBJECT"
    scene.display.shading.show_shadows = True


def main() -> int:
    args = parse_args()
    with open(args.spec, encoding="utf-8") as handle:
        spec = json.load(handle)
    if args.frames:
        spec["frames"] = args.frames
    if not args.colliders:
        # drop the colliders from the run rather than the data: what is baked is dynamics only
        spec = dict(spec)
        spec["colliders"] = []
        for bone in spec["bones"]:
            bone["colliders"] = []

    reset_scene()
    frames, diagnostics = port.simulate_spec(spec, trace=True)
    if frames is None:
        print("the solver produced nothing")
        return 1

    obj = build_armature(spec, args.scale)
    build_scene_extras(spec, args.scale)
    bake(obj, spec, frames, args.scale)

    bpy.context.scene.frame_set(0)
    os.makedirs(os.path.dirname(os.path.abspath(args.blend)), exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.abspath(args.blend))
    print(f"saved {args.blend} ({len(frames)} frames, {len(spec['bones'])} bones, "
          f"{len(spec['colliders'])} collider(s))")

    if args.mp4:
        configure_render(os.path.abspath(args.mp4))
        bpy.ops.render.render(animation=True)
        print(f"rendered {args.mp4}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
