"""Numbers that describe how a CySpring chain moves, so "looks right" can be checked against values.

    python Tools/cyspring_metrics.py <spec.json> [--motion spec|impulse|wag] [--swing DEG] [--json out.json]

The chain is the game's own (positions, axes, lengths, stiffness, drag, gravity from `uma_spring_dump.py`),
the solver is `cyspring_port.py`, and this file only measures what comes out of it. What it measures:

  rest error   each link's length against the prefab's, in game units. A chain keeps its lengths when it is
               seeded right; anything else means the geometry the solver was handed does not match the data.
  dev deg      the angle between a link and the direction a *rigid* chain would point at that frame (the
               parent's rotation applied to the rest axis). Zero is a welded rod, and this is the number that
               says whether a bake will read as a tail: it has to be large, and it has to grow down the chain.
  lag fr       frames by which one link's deviation trails the link above it, from the cross-correlation of
               the two. A spring chain delays more the further down it goes; equal lags everywhere means the
               links move together.
  bend deg     the angle between consecutive links, at rest and while moving: how much the chain curves.
  over deg     how far past its settled angle a link swings after the parent stops - a spring overshoots,
               a rod does not.
  settle fr    frames from the parent stopping until the link's deviation stops changing. None means it was
               still moving at the end of the run.
  ring         zero crossings of the deviation after the parent stops: how many times it swings back and
               forth, which is the difference between a spring and a slow sag.
  step deg     the frame-to-frame change of a link's direction. Spikes are the flipping and stretching that
               make a bake look wrong.

`dev atRest` is the static droop under gravity once everything has settled, which is the only number that is
about the chain and not about the drive.

`--motion impulse` steps the parent once and holds it: the cleanest read of a spring's response. `--motion
wag` drives the fast flick with still stretches that a tail wag is, and that is the dump's own script.
"""

from __future__ import annotations

import argparse
import json
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import cyspring_port as port  # noqa: E402


def drive(spec: dict, mode: str, swing: float, axis: str) -> list[dict]:
    """The parent's rotation per frame: the spec's own script, or a test drive of this file's making."""
    if mode == "idle":
        return [{"frame": f, "rotation": list(port.IDENTITY), "offset": [0.0, 0.0, 0.0]}
                for f in range(spec["frames"] + 1)]
    if mode == "spec" and spec.get("motion"):
        return spec["motion"]
    if mode == "spec":
        return [{"frame": f, "rotation": list(port.IDENTITY)} for f in range(spec["frames"] + 1)]

    direction = {"x": (1.0, 0.0, 0.0), "y": (0.0, 1.0, 0.0), "z": (0.0, 0.0, 1.0)}[axis]

    def keyed(angle: float) -> list[float]:
        half = math.radians(angle) / 2.0
        return [direction[0] * math.sin(half), direction[1] * math.sin(half),
                direction[2] * math.sin(half), math.cos(half)]

    if mode == "impulse":
        # a step over two frames at frame 5, then held: whatever happens after is the chain's own ringing
        frames = max(spec["frames"], 72)
        angles = [0.0] * 5 + [swing * 0.5, swing] + [swing] * (frames - 6)
        return [{"frame": f, "rotation": keyed(angles[f])} for f in range(frames + 1)]

    # wag: the same shape `uma_spring_dump.wag_motion` writes - flicks with still stretches in between
    rise, hold = 3, 10
    keys = [(0.0, 0.0), (rise, swing), (rise + hold, swing), (2 * rise + hold, -swing),
            (2 * rise + 2 * hold, -swing), (3 * rise + 2 * hold, 0.0)]
    motion = []
    for frame in range(spec["frames"] + 1):
        angle = 0.0
        for (start, begin), (end, finish) in zip(keys, keys[1:]):
            if start <= frame <= end:
                angle = begin + (finish - begin) * ((frame - start) / (end - start))
                break
        motion.append({"frame": frame, "rotation": keyed(angle)})
    return motion


def swing_axis(motion: list[dict]) -> tuple[float, float, float]:
    """The axis the drive turns about, so a deviation can be signed instead of just a magnitude."""
    key = min(motion, key=lambda entry: abs(entry["rotation"][3]))
    length = port.v_length(tuple(key["rotation"][:3]))
    if length < 1e-9:
        return (0.0, 1.0, 0.0)
    return (key["rotation"][0] / length, key["rotation"][1] / length, key["rotation"][2] / length)


def signed_angle(actual, driven, axis) -> float:
    """The angle from `driven` to `actual`, positive in the direction the drive turns."""
    cross = port.v_cross(driven, actual)
    return math.degrees(math.atan2(port.v_dot(cross, axis), port.v_dot(driven, actual)))


def series(spec: dict, frames: list, motion: list[dict]) -> dict:
    """Per frame, per link: the solver's link, the rigid one, and the gap between them."""
    n = len(spec["bones"])
    root_head = tuple(spec["bones"][0]["position"])
    pivot = tuple(spec.get("pivot") or root_head)
    axes = [tuple(bone["axis"]) for bone in spec["bones"]]
    rotations = [port._motion_at({"motion": motion}, f) for f in range(len(frames))]
    direction_axis = swing_axis(motion)

    tails = [[tuple(row[i * 7:i * 7 + 3]) for i in range(n)] for row in frames]
    anchors = [port.v_add(port.v_add(pivot, port.q_rotate(rotations[f], port.v_sub(root_head, pivot))),
                          port._offset_at({"motion": motion}, f))
               for f in range(len(frames))]
    # A child's head is the parent's tail *from the previous frame*: `update()` seeds `old_position` before
    # stepping anything, so with the parent's tail from the same frame the link lengths come out short and
    # the lag vanishes. At frame 0 every head is the rest head.
    heads = []
    for f in range(len(frames)):
        if f == 0:
            row_heads = [tuple(anchors[0])] + [tuple(tails[0][i - 1]) for i in range(1, n)]
        else:
            row_heads = [anchors[f]] + [tails[f - 1][i - 1] for i in range(1, n)]
        heads.append(row_heads)
    links = [[port.v_normalized(port.v_sub(tails[f][i], heads[f][i])) for i in range(n)]
             for f in range(len(frames))]
    driven = [[port.q_rotate(rotations[f], axes[i]) for i in range(n)] for f in range(len(frames))]
    # Two gaps per link: the whole angle, which also sees a droop under gravity (that turns the link about
    # an axis across the drive, so an in-plane angle would miss it), and the in-plane one, which is the
    # motion the drive asked for and is what lag and overshoot are measured on.
    deviation_abs = [[_angle(links[f][i], driven[f][i]) for f in range(len(frames))] for i in range(n)]
    deviation = [[signed_angle(links[f][i], driven[f][i], direction_axis) for f in range(len(frames))]
                 for i in range(n)]
    return {"tails": tails, "heads": heads, "links": links, "driven": driven, "deviations": deviation,
            "deviationAbs": deviation_abs, "rotations": rotations, "pivot": pivot, "root_head": root_head}


def _angle(a, b) -> float:
    return math.degrees(math.acos(max(-1.0, min(1.0, port.v_dot(a, b)))))


def link_metrics(spec: dict, index: int, data: dict, settle_from: int) -> dict:
    """Every measure for one link."""
    n_frames = len(data["tails"])
    rest_length = spec["bones"][index]["distance"]
    lengths = [port.v_length(port.v_sub(data["tails"][f][index], data["heads"][f][index]))
               for f in range(n_frames)]
    deviation = data["deviations"][index]
    absolute = data["deviationAbs"][index]
    steps = [abs(deviation[f] - deviation[f - 1]) for f in range(1, n_frames)]

    bend = []
    for f in range(n_frames):
        for j in range(1, len(spec["bones"])):
            if j == index:
                bend.append(port.v_dot(data["links"][f][j], data["links"][f][j - 1]))
    bends = [math.degrees(math.acos(max(-1.0, min(1.0, value)))) for value in bend]

    lag = 0
    if index > 0:
        above = data["deviations"][index - 1]
        best = -1e18
        for shift in range(0, min(13, n_frames)):
            a = above[:n_frames - shift]
            b = deviation[shift:]
            mean_a = sum(a) / len(a)
            mean_b = sum(b) / len(b)
            score = sum((x - mean_a) * (y - mean_b) for x, y in zip(a, b))
            if score > best:
                best, lag = score, shift

    settled = deviation[-1]
    overshoot = 0.0
    settle_frames = None
    crossings = 0
    if settle_from < n_frames:
        window = deviation[settle_from:]
        peak = max(window, key=abs)
        overshoot = abs(peak) - abs(settled)
        for offset in range(len(window)):
            if all(abs(window[min(offset + k, len(window) - 1)] - settled) < 0.02 * max(0.5, abs(peak))
                   for k in range(4)):
                settle_frames = offset
                break
        for f in range(1, len(window)):
            if (window[f] - settled) * (window[f - 1] - settled) < 0 and abs(window[f] - settled) > 0.05:
                crossings += 1

    return {"name": spec["bones"][index]["name"], "restLength": rest_length,
            "lengthError": max(abs(length - rest_length) for length in lengths),
            "deviationMean": sum(absolute) / len(absolute), "deviationMax": max(absolute),
            "deviationAtRest": absolute[-1], "lagBehindParent": lag, "overshoot": overshoot,
            "settleFrames": settle_frames, "ringCrossings": crossings,
            "bendMean": sum(bends) / len(bends) if bends else 0.0, "bendMax": max(bends) if bends else 0.0,
            "stepMean": sum(steps) / len(steps) if steps else 0.0, "stepMax": max(steps) if steps else 0.0,
            "deviation": deviation}


def tip_travel(spec: dict, data: dict) -> dict:
    """How far the tip swings on its own path, against the path a rigid chain would have taken."""
    pivot = data["pivot"]
    last = len(spec["bones"]) - 1
    actual = [data["tails"][f][last] for f in range(len(data["tails"]))]
    rest_tip = actual[0]
    rigid = [port.v_add(pivot, port.q_rotate(data["rotations"][f], port.v_sub(rest_tip, pivot)))
             for f in range(len(actual))]
    actual_path = sum(port.v_length(port.v_sub(actual[f], actual[f - 1])) for f in range(1, len(actual)))
    rigid_path = sum(port.v_length(port.v_sub(rigid[f], rigid[f - 1])) for f in range(1, len(rigid)))
    sag = max(port.v_length(port.v_sub(actual[f], rigid[f])) for f in range(len(actual)))
    # a still drive has no rigid path at all, which is the idle case: the wind is then the whole motion
    return {"pathRatio": actual_path / rigid_path if rigid_path > 1e-9 else float("inf"),
            "sag": sag}


def report(spec: dict, motion: list[dict], metrics: list[dict], travel: dict, settle_from: int) -> None:
    print(f"{spec['name']}: {len(spec['bones'])} link(s), {len(motion)} frame(s), drive settles from frame "
          f"{settle_from}")
    header = (f"  {'link':<22}{'len':>7}{'err':>9}{'dev':>7}{'devMax':>8}{'atRest':>8}"
              f"{'lag fr':>7}{'over°':>7}{'settle':>7}{'ring':>5}{'bend':>7}{'step°':>7}{'stepMax':>8}")
    print(header)
    for m in metrics:
        print(f"  {m['name']:<22}{m['restLength']:7.3f}{m['lengthError']:9.1e}{m['deviationMean']:7.2f}"
              f"{m['deviationMax']:8.2f}{m['deviationAtRest']:8.2f}{m['lagBehindParent']:7d}"
              f"{m['overshoot']:7.2f}{str(m['settleFrames']):>7}{m['ringCrossings']:5d}"
              f"{m['bendMean']:7.2f}{m['stepMean']:7.2f}{m['stepMax']:8.2f}")
    ratio = travel["pathRatio"]
    shown = "no rigid drive at all (the wind is the whole motion)" if ratio == float("inf") else f"{ratio:.2f}x"
    print(f"  tip: path {shown}, sag {travel['sag']:.3f} game units")
    print("  reading: dev is how far a link hangs behind its parent - 0 is the welded rod, and it should")
    print("  grow down the chain; lag fr should too. over/ring/settle are the spring's own answer after the")
    print("  parent stops; atRest is the static droop, the only number that is about the chain alone.")


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("spec")
    parser.add_argument("--motion", choices=("spec", "impulse", "wag", "idle"), default="spec")
    parser.add_argument("--swing", type=float, default=35.0)
    parser.add_argument("--axis", choices=("x", "y", "z"), default="y")
    parser.add_argument("--wind", type=float, default=None,
                        help="0 turns the wind off; None uses the spec's own 0.35")
    parser.add_argument("--no-colliders", action="store_true",
                        help="drop the chain's colliders (the default in the Blender bake, see FORMAT.md)")
    parser.add_argument("--json", default="", help="write every series here")
    args = parser.parse_args(argv)

    spec = port.load_spec(args.spec)
    if args.no_colliders:
        spec = dict(spec)
        spec["colliders"] = []
        spec["bones"] = [dict(bone, colliders=[]) for bone in spec["bones"]]
    motion = drive(spec, args.motion, args.swing, args.axis)
    spec = dict(spec)
    spec["frames"] = len(motion) - 1
    spec["motion"] = motion

    frames, _ = port.simulate_spec(spec, trace=True, warmup=24, wind_scale=args.wind)
    if frames is None:
        print("the solver produced nothing")
        return 1
    data = series(spec, frames, motion)

    # where the drive stops moving: one frame after the last frame whose rotation differs from the one
    # before it. An impulse holds its step, so "last non-identity frame" would never fire.
    settle_from = 0
    for index in range(1, len(motion)):
        if port.v_distance(tuple(motion[index]["rotation"]), tuple(motion[index - 1]["rotation"])) > 1e-6:
            settle_from = index + 1

    metrics = [link_metrics(spec, index, data, settle_from) for index in range(len(spec["bones"]))]
    travel = tip_travel(spec, data)
    report(spec, motion, metrics, travel, settle_from)

    if args.json:
        with open(args.json, "w", encoding="utf-8") as handle:
            json.dump({"spec": spec["name"], "motion": args.motion, "tip": travel,
                       "links": [{key: value for key, value in m.items() if key != "deviation"}
                                 for m in metrics],
                       "deviation": {m["name"]: [round(v, 4) for v in m["deviation"]] for m in metrics}},
                      handle, indent=1)
        print(f"{args.json} written")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
