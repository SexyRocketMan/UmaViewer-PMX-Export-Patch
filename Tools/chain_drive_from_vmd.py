"""Take a chain spec and drive it with a real animation's hip, instead of a scripted wag.

    python Tools/chain_drive_from_vmd.py <spec.json> <motion.vmd> [-o out.json] [--bone Hip]
                                         [--rate 1.0] [--loops 2] [--no-translation] [--scale 12.5]

`motion.vmd` is a recording from the viewer (`-RecordVmd`, see docs/CLI.md). The tail has no animation of its
own: it hangs off the hip, and what makes it move is the hip - mostly its *translation*, which is why a drive
that only rotates the parent makes the chain look like a stick. The hip's rotation and travel are both written
into the chain spec's motion, the chain is re-anchored on the hip, and `cyspring_metrics.py` and
`spring_plot.py` run the result as it stands.

Three details are easy to get wrong, and each of them showed up as a tail that shook instead of followed:

* A VMD records a key every *second* frame. Holding the last key - which the first version of this did - makes
  the hip step rather than travel, and every step is a yank on the chain. Keys are interpolated here: positions
  linearly, rotations by slerp.
* The recorder writes MMD coordinates, which are the game's turned 180 degrees about the vertical: positions
  come out as `(-x, y, -z) * 12.5` and rotations as `(-x, y, -z, w)`. Both are undone, so the drive points
  where the animation pointed.
* The hip's travel is written as a delta from the clip's first key, so the chain starts where the prefab rests
  instead of being dragged the clip's opening offset in one step.
"""

from __future__ import annotations

import argparse
import json
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import vmd_inspect as vmd  # noqa: E402


def slerp(a: list[float], b: list[float], t: float) -> list[float]:
    """Shortest way round, then normalized: with a key every second frame these are big rotations."""
    dot = sum(x * y for x, y in zip(a, b))
    if dot < 0.0:
        b = [-value for value in b]
        dot = -dot
    if dot > 0.9995:
        out = [x + (y - x) * t for x, y in zip(a, b)]
    else:
        theta = math.acos(max(-1.0, min(1.0, dot)))
        sine = math.sin(theta)
        out = [(x * math.sin((1.0 - t) * theta) + y * math.sin(t * theta)) / sine for x, y in zip(a, b)]
    length = math.sqrt(sum(value * value for value in out)) or 1.0
    return [value / length for value in out]


def to_game_space(position: list[float], rotation: list[float]) -> tuple[list[float], list[float]]:
    """Undo the recorder's conversion, which is a turn of 180 degrees about the vertical."""
    return ([-position[0], position[1], -position[2]],
            [-rotation[0], rotation[1], -rotation[2], rotation[3]])


def timeline(keys: list, loops: int, rate: float) -> list[tuple[int, tuple, tuple]]:
    """Every key of `loops` passes over the clip, in frames at `rate`."""
    period = max(1, keys[-1].frame - keys[0].frame)
    entries = []
    for loop in range(max(1, loops)):
        for key in keys:
            entries.append((int(round((key.frame + loop * period) * rate)),
                            tuple(key.position), tuple(key.rotation)))
    entries.sort(key=lambda entry: entry[0])
    return entries


def resample(keys: list, loops: int, rate: float, scale: float) -> list[dict]:
    """One motion key per frame, interpolated between the VMD's sparse ones."""
    entries = timeline(keys, loops, rate)
    first_position, _ = to_game_space(list(entries[0][1]), list(entries[0][2]))
    motion = []
    cursor = 0
    for frame in range(entries[-1][0] + 1):
        while cursor + 1 < len(entries) and entries[cursor + 1][0] <= frame:
            cursor += 1
        start_frame, start_position, start_rotation = entries[cursor]
        if cursor + 1 < len(entries):
            end_frame, end_position, end_rotation = entries[cursor + 1]
        else:
            end_frame, end_position, end_rotation = start_frame, start_position, start_rotation
        t = 0.0 if end_frame == start_frame else (frame - start_frame) / (end_frame - start_frame)
        position = [x + (y - x) * t for x, y in zip(start_position, end_position)]
        rotation = slerp(list(start_rotation), list(end_rotation), t)
        position, rotation = to_game_space(position, rotation)
        motion.append({"frame": frame, "rotation": rotation,
                       "offset": [(position[axis] - first_position[axis]) / scale for axis in range(3)]})
    return motion


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("spec")
    parser.add_argument("motion")
    parser.add_argument("-o", "--out", default="", help="default: the spec's name with _vmd appended")
    parser.add_argument("--bone", default="Hip", help="the bone the chain hangs from")
    parser.add_argument("--scale", type=float, default=12.5, help="MMD units per game unit")
    parser.add_argument("--rate", type=float, default=1.0, help="playback speed, 1 is the clip's own")
    parser.add_argument("--loops", type=int, default=2, help="passes over the clip")
    parser.add_argument("--fps", type=float, default=0.0,
                        help="the rate the clip was recorded at, stored in the spec; 0 keeps the spec's")
    parser.add_argument("--no-translation", action="store_true", help="rotation only, which is the stick case")
    args = parser.parse_args(argv)

    with open(args.spec, encoding="utf-8") as handle:
        spec = json.load(handle)
    recording = vmd.read_vmd(args.motion)

    keys = sorted((key for key in recording.bone_keys if key.name == args.bone), key=lambda key: key.frame)
    if not keys:
        names = sorted({key.name for key in recording.bone_keys})
        raise SystemExit(f"'{args.bone}' is not in {args.motion}; it has {', '.join(names[:12])}...")

    # the chain hangs from the hip, so it turns about the hip and travels with it
    hip = next((parent for parent in spec.get("parents", []) if parent["name"] == args.bone), None)
    if hip is None:
        raise SystemExit(f"the spec's parents do not include '{args.bone}': "
                         f"{[parent['name'] for parent in spec.get('parents', [])]}")
    spec = dict(spec)
    spec["pivot"] = hip["position"]

    motion = resample(keys, args.loops, args.rate, args.scale)
    if args.no_translation:
        for entry in motion:
            entry["offset"] = [0.0, 0.0, 0.0]

    spec["motion"] = motion
    spec["frames"] = len(spec["motion"]) - 1
    if args.fps > 0:
        spec["fps"] = args.fps
    spec["source"] = dict(spec.get("source") or {}, drive=os.path.basename(args.motion),
                          bone=args.bone, translation=not args.no_translation, loops=args.loops)

    offsets = [entry["offset"] for entry in spec["motion"]]
    travel = max(max(abs(value) for value in offset) for offset in offsets)
    steps = [math.dist(a, b) for a, b in zip(offsets, offsets[1:])]
    links = [bone["distance"] for bone in spec["bones"]]
    out = args.out or os.path.splitext(args.spec)[0] + "_vmd.json"
    with open(out, "w", encoding="utf-8") as handle:
        json.dump(spec, handle, ensure_ascii=False, separators=(",", ":"))
    print(f"{out}: {len(spec['motion'])} frame(s), {args.loops} loop(s) of '{args.bone}' from "
          f"{os.path.basename(args.motion)}; hip travels {travel:.4f} game units, largest step between "
          f"frames {max(steps):.4f} (links are {min(links):.4f}-{max(links):.4f})")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
