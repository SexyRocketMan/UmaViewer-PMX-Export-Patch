"""Take a chain spec and drive it with a real animation's hip, instead of a scripted wag.

    python Tools/chain_drive_from_vmd.py <spec.json> <motion.vmd> [-o out.json] [--bone Hip]
                                         [--rate 1.0] [--no-translation] [--scale 12.5]

`motion.vmd` is a recording from the viewer (`-RecordVmd`, see docs/CLI.md). The tail does not have its own
animation: it hangs off the hip, and what makes it move is the hip - mostly its *translation*, which is why a
drive that only rotates the parent makes the chain look like a stick. The hip's rotation and its travel are
both written into the chain spec's motion, the chain is re-anchored on the hip, and the result is a spec that
`cyspring_metrics.py` and `spring_plot.py` can run as they stand.

Units: the recorder writes MMD units, 12.5 to the game unit (`DefaultBoneAmplifier`), hence `--scale`.
The rotation keys are local to the hip and the hip's parent never moves in these recordings, so they are the
hip's world rotation as far as the chain is concerned.
"""

from __future__ import annotations

import argparse
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import vmd_inspect as vmd  # noqa: E402


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("spec")
    parser.add_argument("motion")
    parser.add_argument("-o", "--out", default="", help="default: the spec's name with _vmd appended")
    parser.add_argument("--bone", default="Hip", help="the bone the chain hangs from")
    parser.add_argument("--scale", type=float, default=12.5, help="MMD units per game unit")
    parser.add_argument("--rate", type=float, default=1.0, help="playback speed, 1 is the clip's own")
    parser.add_argument("--no-translation", action="store_true", help="rotation only, which is the stick case")
    args = parser.parse_args(argv)

    with open(args.spec, encoding="utf-8") as handle:
        spec = json.load(handle)
    motion = vmd.read_vmd(args.motion)

    keys = sorted((key for key in motion.bone_keys if key.name == args.bone), key=lambda key: key.frame)
    if not keys:
        names = sorted({key.name for key in motion.bone_keys})
        raise SystemExit(f"'{args.bone}' is not in {args.motion}; it has {', '.join(names[:12])}...")

    # the chain hangs from the hip, so it turns about the hip and travels with it
    hip = next((parent for parent in spec.get("parents", []) if parent["name"] == args.bone), None)
    if hip is None:
        raise SystemExit(f"the spec's parents do not include '{args.bone}': "
                         f"{[parent['name'] for parent in spec.get('parents', [])]}")
    spec = dict(spec)
    spec["pivot"] = hip["position"]

    frames = []
    by_frame = {}
    for key in keys:
        by_frame[int(round(key.frame * args.rate))] = key
    # the clip's own first frame is generally not the prefab's rest pose, so the hip's travel is written as a
    # delta from where the clip starts: otherwise the chain opens by being dragged that whole distance at once
    start = keys[0].position
    current = keys[0]
    for frame in range(max(by_frame) + 1):
        if frame in by_frame:
            current = by_frame[frame]
        offset = [0.0, 0.0, 0.0] if args.no_translation \
            else [(current.position[axis] - start[axis]) / args.scale for axis in range(3)]
        # a VMD key is (x, y, z, w) with y up and z forward: the same handedness as the prefab's space
        frames.append({"frame": frame, "rotation": list(current.rotation), "offset": offset})

    spec["motion"] = frames
    spec["frames"] = len(spec["motion"]) - 1
    spec["source"] = dict(spec.get("source") or {}, drive=os.path.basename(args.motion),
                          bone=args.bone, translation=not args.no_translation)

    travel = max(max(abs(value) for value in entry["offset"]) for entry in spec["motion"]) \
        if not args.no_translation else 0.0
    if not args.no_translation:
        travel = max(travel, max(max(abs(value) for value in key.position) for key in keys) / args.scale)
    out = args.out or os.path.splitext(args.spec)[0] + "_vmd.json"
    with open(out, "w", encoding="utf-8") as handle:
        json.dump(spec, handle, ensure_ascii=False, separators=(",", ":"))
    print(f"{out}: {len(spec['motion'])} frame(s) driven by '{args.bone}' of {os.path.basename(args.motion)}; "
          f"hip travels up to {travel:.4f} game units, links are "
          f"{min(bone['distance'] for bone in spec['bones']):.4f}-{max(bone['distance'] for bone in spec['bones']):.4f}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
