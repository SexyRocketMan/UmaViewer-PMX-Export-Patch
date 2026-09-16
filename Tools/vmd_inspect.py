# /// script
# requires-python = ">=3.11"
# dependencies = []
# ///
"""Inspect and validate exported VMD motions.

  uv run vmd_inspect.py summary <file.vmd> [more.vmd ...]
  uv run vmd_inspect.py loop    <file.vmd> [--tolerance 1e-4]     # does frame 0 == last frame?

`loop` is the check for "one button, no trimming in Blender": every bone has to be keyed on the last
frame and that key has to repeat the first frame's pose, otherwise the motion jumps when it loops.
"""

from __future__ import annotations

import argparse
import math
import struct
import sys
from dataclasses import dataclass, field

SHIFT_JIS = "shift_jis"


@dataclass
class BoneKey:
    name: str
    frame: int
    position: tuple[float, float, float]
    rotation: tuple[float, float, float, float]


@dataclass
class MorphKey:
    name: str
    frame: int
    weight: float


@dataclass
class Motion:
    model_name: str
    bone_keys: list[BoneKey] = field(default_factory=list)
    morph_keys: list[MorphKey] = field(default_factory=list)
    camera_frames: list[int] = field(default_factory=list)
    light_frames: list[int] = field(default_factory=list)
    shadow_frames: list[int] = field(default_factory=list)
    ik_keys: list[tuple[int, str, bool]] = field(default_factory=list)


def _text(blob: bytes) -> str:
    return blob.split(b"\0")[0].decode(SHIFT_JIS, errors="replace")


def read_vmd(path: str) -> Motion:
    with open(path, "rb") as handle:
        data = handle.read()

    if not data.startswith(b"Vocaloid Motion Data"):
        raise ValueError(f"not a vmd file: {path}")

    offset = 0
    offset += 30                       # signature
    model = _text(data[offset:offset + 20]); offset += 20

    motion = Motion(model_name=model)

    count = struct.unpack_from("<I", data, offset)[0]; offset += 4
    for _ in range(count):
        name = _text(data[offset:offset + 15]); offset += 15
        frame = struct.unpack_from("<I", data, offset)[0]; offset += 4
        position = struct.unpack_from("<3f", data, offset); offset += 12
        rotation = struct.unpack_from("<4f", data, offset); offset += 16
        offset += 64                   # interpolation
        motion.bone_keys.append(BoneKey(name, frame, position, rotation))

    count = struct.unpack_from("<I", data, offset)[0]; offset += 4
    for _ in range(count):
        name = _text(data[offset:offset + 15]); offset += 15
        frame = struct.unpack_from("<I", data, offset)[0]; offset += 4
        weight = struct.unpack_from("<f", data, offset)[0]; offset += 4
        motion.morph_keys.append(MorphKey(name, frame, weight))

    count = struct.unpack_from("<I", data, offset)[0]; offset += 4
    for _ in range(count):
        frame = struct.unpack_from("<I", data, offset)[0]; offset += 4
        offset += 4 + 12 + 12 + 24 + 4 + 1
        motion.camera_frames.append(frame)

    count = struct.unpack_from("<I", data, offset)[0]; offset += 4
    for _ in range(count):
        frame = struct.unpack_from("<I", data, offset)[0]; offset += 4 + 12 + 12
        motion.light_frames.append(frame)

    if offset < len(data):             # self shadow
        count = struct.unpack_from("<I", data, offset)[0]; offset += 4
        for _ in range(count):
            frame = struct.unpack_from("<I", data, offset)[0]; offset += 4 + 1 + 4
            motion.shadow_frames.append(frame)

    if offset < len(data):             # ik / visibility
        count = struct.unpack_from("<I", data, offset)[0]; offset += 4
        for _ in range(count):
            frame = struct.unpack_from("<I", data, offset)[0]; offset += 4
            visible = data[offset]; offset += 1
            ik_count = struct.unpack_from("<I", data, offset)[0]; offset += 4
            for _ in range(ik_count):
                name = _text(data[offset:offset + 20]); offset += 20
                enabled = data[offset]; offset += 1
                motion.ik_keys.append((frame, name, bool(enabled)))
            motion.ik_keys.append((frame, "<visible>", bool(visible)))

    return motion


def cmd_motion(paths: list[str], args) -> int:
    """Does the motion actually move? A frozen clip still has the right frame count and still loops."""
    ok = True
    for path in paths:
        motion = read_vmd(path)
        print(f"== {path} (model {motion.model_name!r})")
        if not motion.bone_keys:
            print("   !! no bone keyframes")
            ok = False
            continue

        per_bone: dict[str, list[BoneKey]] = {}
        for key in motion.bone_keys:
            per_bone.setdefault(key.name, []).append(key)

        worst_pos = worst_rot = 0.0
        moving_pos = moving_rot = 0
        for name, keys in per_bone.items():
            keys.sort(key=lambda k: k.frame)
            base_pos, base_rot = keys[0].position, keys[0].rotation
            pos = max(math.dist(base_pos, k.position) for k in keys)
            rot = max(min(max(abs(a - b) for a, b in zip(base_rot, k.rotation)),
                          max(abs(a + b) for a, b in zip(base_rot, k.rotation))) for k in keys)
            worst_pos = max(worst_pos, pos)
            worst_rot = max(worst_rot, rot)
            if pos > args.position_tolerance:
                moving_pos += 1
            if rot > args.rotation_tolerance:
                moving_rot += 1

        morph_spans = {}
        for key in motion.morph_keys:
            low, high = morph_spans.get(key.name, (key.weight, key.weight))
            morph_spans[key.name] = (min(low, key.weight), max(high, key.weight))
        moving_morphs = {n: span for n, span in morph_spans.items() if span[1] - span[0] > 1e-4}

        print(f"   bones: {moving_pos}/{len(per_bone)} move positionally (worst {worst_pos:.5f}), "
              f"{moving_rot}/{len(per_bone)} rotate (worst {worst_rot:.5f})")
        print(f"   morphs: {len(moving_morphs)}/{len(morph_spans)} change")
        # A frozen recording is not necessarily one where *nothing* moves: a physics driven bone or two
        # drift even when the sampled pose is the same every frame, so count how much of the rig moves.
        moving = max(moving_pos, moving_rot)
        if moving < args.min_moving_bones:
            print(f"   !! only {moving}/{len(per_bone)} bones move: the recording sampled a frozen pose. A one "
                  f"shot animation that had already finished is parked on its last frame, which is what a "
                  f"recording started after the animation ended used to capture. Pass "
                  f"--min-moving-bones 0 if the clip really is static.")
            ok = False
        elif moving_pos == 0:
            print(f"   note: no bone translates, only {moving_rot} rotate")

        # A recording whose first frame is an outlier pose (a T-pose or a rest pose captured before the
        # animation started) shows up as one huge step out of frame 0 and a matching one at the loop seam.
        frames = sorted({k.frame for k in motion.bone_keys})
        if len(frames) >= 4:
            steps = []
            for index in range(len(frames) - 1):
                worst = 0.0
                for keys in per_bone.values():
                    by_frame = {k.frame: k.rotation for k in keys}
                    a, b = by_frame.get(frames[index]), by_frame.get(frames[index + 1])
                    if a is None or b is None:
                        continue
                    worst = max(worst, min(max(abs(x - y) for x, y in zip(a, b)),
                                           max(abs(x + y) for x, y in zip(a, b))))
                steps.append(worst)
            typical = sorted(steps[1:])[len(steps[1:]) // 2]
            print(f"   first step {steps[0]:.4f}, seam step {steps[-1]:.4f}, typical step {typical:.4f}")
            if steps[0] > args.first_step_factor * typical and steps[0] - typical > 0.02:
                print(f"   !! the first frame is {steps[0] / typical:.1f}x the typical step: it looks like a "
                      f"pose the animation never had (a T-pose or rest pose captured before playback)")
                ok = False

    print("PASS" if ok else "FAIL")
    return 0 if ok else 1


def cmd_summary(paths: list[str], args) -> int:
    for path in paths:
        motion = read_vmd(path)
        frames = [k.frame for k in motion.bone_keys]
        morph_frames = [k.frame for k in motion.morph_keys]
        print(f"== {path}")
        print(f"   model name     : {motion.model_name!r}")
        print(f"   bone keys      : {len(motion.bone_keys)} over {len(set(frames))} frames "
              f"[{min(frames) if frames else '-'}..{max(frames) if frames else '-'}]")
        print(f"   bones animated : {len({k.name for k in motion.bone_keys})}")
        print(f"   morph keys     : {len(motion.morph_keys)} over "
              f"{len(set(morph_frames))} frames "
              f"[{min(morph_frames) if morph_frames else '-'}..{max(morph_frames) if morph_frames else '-'}]")
        print(f"   morphs animated: {len({k.name for k in motion.morph_keys})}")
        print(f"   camera keys    : {len(motion.camera_frames)}")
        print(f"   light/shadow/ik: {len(motion.light_frames)}/{len(motion.shadow_frames)}/{len(motion.ik_keys)}")
    return 0


def _pose_map(keys: list) -> dict[str, list]:
    poses: dict[str, list] = {}
    for key in keys:
        poses.setdefault(key.name, []).append(key)
    return poses


def cmd_loop(paths: list[str], args) -> int:
    ok = True
    for path in paths:
        motion = read_vmd(path)
        print(f"== {path} (model {motion.model_name!r})")
        if not motion.bone_keys:
            print("   !! no bone keyframes")
            ok = False
            continue

        last_frame = max(k.frame for k in motion.bone_keys)
        first_frame = min(k.frame for k in motion.bone_keys)
        poses = _pose_map(motion.bone_keys)

        missing_last = []
        deviations = []
        for name, keys in sorted(poses.items()):
            by_frame = {k.frame: k for k in keys}
            start = by_frame.get(first_frame)
            end = by_frame.get(last_frame)
            if start is None or end is None:
                missing_last.append(name)
                continue
            pos_delta = max(abs(a - b) for a, b in zip(start.position, end.position))
            # quaternions are sign ambiguous: q and -q are the same rotation
            rot_delta = min(
                max(abs(a - b) for a, b in zip(start.rotation, end.rotation)),
                max(abs(a + b) for a, b in zip(start.rotation, end.rotation)),
            )
            deviations.append((name, pos_delta, rot_delta))

        print(f"   frame range {first_frame}..{last_frame}, {len(poses)} bones")
        deviations.sort(key=lambda t: -max(t[1], t[2]))
        worst = deviations[0] if deviations else None
        if worst:
            print(f"   worst return-to-start: {worst[0]} dpos={worst[1]:.6f} drot={worst[2]:.6f} "
                  f"(tolerance pos {args.tolerance}, rot {args.rotation_tolerance})")

        mismatched = [d for d in deviations
                      if d[1] > args.tolerance or d[2] > args.rotation_tolerance]
        if missing_last:
            print(f"   !! {len(missing_last)} bone(s) have no key on the last frame: {missing_last[:8]}")
            ok = False
        if mismatched:
            print(f"   !! {len(mismatched)} bone(s) do not return to their first pose:")
            for name, pos_delta, rot_delta in mismatched[:10]:
                print(f"        {name:20} dpos={pos_delta:.5f} drot={rot_delta:.5f}")
            ok = False
        if not missing_last and not mismatched:
            print("   loop OK: every bone is keyed on the last frame and repeats the first pose")

        if motion.morph_keys:
            morph_poses = _pose_map(motion.morph_keys)
            morph_last = max(k.frame for k in motion.morph_keys)
            morph_missing = [n for n, keys in morph_poses.items() if morph_last not in {k.frame for k in keys}]
            if morph_missing:
                print(f"   !! {len(morph_missing)} morph(s) have no key on frame {morph_last}: {morph_missing[:8]}")

    print("PASS" if ok else "FAIL")
    return 0 if ok else 1


def main() -> int:
    parser = argparse.ArgumentParser()
    sub = parser.add_subparsers(dest="cmd", required=True)
    summary = sub.add_parser("summary"); summary.add_argument("paths", nargs="+")
    motion = sub.add_parser("motion",
                            help="check that the motion actually moves and is not a frozen pose")
    motion.add_argument("paths", nargs="+")
    motion.add_argument("--position-tolerance", type=float, default=1e-4)
    motion.add_argument("--rotation-tolerance", type=float, default=1e-4)
    motion.add_argument("--min-moving-bones", type=int, default=3,
                        help="how many bones have to move before the recording counts as animated; a "
                             "frozen recording still drifts a physics bone or two")
    motion.add_argument("--first-step-factor", type=float, default=2.5,
                        help="how much bigger than a typical step the first frame may be before it "
                             "counts as a pose pop")
    loop = sub.add_parser("loop")
    loop.add_argument("paths", nargs="+")
    loop.add_argument("--tolerance", type=float, default=1e-4,
                      help="max position deviation between the first and last frame")
    loop.add_argument("--rotation-tolerance", type=float, default=1e-3,
                      help="max quaternion deviation between the first and last frame "
                           "(1e-3 ~ 0.1 degrees; physics driven bones never close exactly)")
    args = parser.parse_args()
    return {"summary": cmd_summary, "loop": cmd_loop, "motion": cmd_motion}[args.cmd](args.paths, args)


if __name__ == "__main__":
    sys.exit(main())
