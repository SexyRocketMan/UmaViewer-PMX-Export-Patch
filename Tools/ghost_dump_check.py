# /// script
# requires-python = ">=3.11"
# dependencies = []
# ///
"""Read the vmd recorder's ghost dump and say what the recorder actually wrote, bone by bone.

    UMA_GHOST_DUMP=1 ./Tools/headless_export.ps1 -Char 1001 -Motion <motion> -RecordVmd out.vmd
    uv run ghost_dump_check.py %TEMP%\\ghost_dump.jsonl

The recorder writes `ghost.localRotation` for every bone that has a ghost rather than the live bone's own
rotation, because the ghost chain mirrors the MMD hierarchy instead of the viewer's. That substitution is
deliberate, and it means a recording that comes out wrong cannot be argued about from the vmd alone: you have
to know whether a value came from the ghost's frame or from the pose the viewer was showing. The dump holds
both, per bone and per frame, and this reports:

  local delta   angle between the recorded value and the live bone's rotation. Zero for a bone that is
                recorded exactly as the viewer shows it. A *constant* non-zero delta is a frame conversion
                (which is what the ghost is for); a delta that moves with the animation is the recording
                following something the animation did not ask for - the look-at solve was one of those, and
                is now suspended for the duration of a take.
  world delta   angle between the ghost's world rotation and the live bone's, which is the conversion the
                ghost applies end to end.
  parent delta  angle between the live parent's world rotation and the ghost parent's. A ghost chain that
                reproduces the live hierarchy reports 0 here, and the difference is what the local rotation
                ends up absorbing.

Judging whether a frame conversion is *correct* needs the model: apply the recorded vmd to the exported pmx's
rest skeleton and compare against the same dump. That is the next step for a reported "the recording twists
the upper body" bug, and this file is the viewer-side half of it.
"""

from __future__ import annotations

import json
import math
import sys
from collections import defaultdict

# a windows console is not utf-8 by default, and the bone names are japanese
try:
    sys.stdout.reconfigure(encoding="utf-8")
except AttributeError:
    pass


def q_angle_degrees(a, b) -> float:
    """The rotation between two quaternions, in degrees, with the sign ambiguity folded away."""
    dot = abs(sum(x * y for x, y in zip(a, b)))
    return math.degrees(2.0 * math.acos(min(1.0, max(0.0, dot))))


def main() -> int:
    if len(sys.argv) < 2:
        print(__doc__)
        return 2
    per_bone = defaultdict(list)
    with open(sys.argv[1], encoding="utf-8") as handle:
        for line in handle:
            line = line.strip()
            if line:
                row = json.loads(line)
                per_bone[row["bone"]].append(row)

    print(f"{'bone':12} {'n':>3} {'local d':>8} {'spread':>7} {'world d':>8} {'parent d':>8}  note")
    rows = []
    for bone, entries in per_bone.items():
        local = [q_angle_degrees(tuple(e["ghostLocal"]), tuple(e["liveLocal"])) for e in entries]
        world = [q_angle_degrees(tuple(e["ghostWorld"]), tuple(e["liveWorld"])) for e in entries]
        parent = [q_angle_degrees(tuple(e["liveParentWorld"]), tuple(e["ghostParentWorld"])) for e in entries]
        spread = max(local) - min(local)
        note = ""
        if max(local) > 1.0:
            note = "constant frame conversion" if spread < 2.0 else "moves with the animation"
        rows.append((max(local), bone, len(entries), spread, max(world), max(parent), note))

    for local_delta, bone, count, spread, world_delta, parent_delta, note in sorted(rows, reverse=True):
        print(f"{bone:12} {count:>3} {local_delta:>8.2f} {spread:>7.2f} {world_delta:>8.2f} "
              f"{parent_delta:>8.2f}  {note}")

    if not rows:
        print("no rows in the dump")
        return 1
    converting = [r for r in rows if r[0] > 1.0]
    moving = [r for r in converting if r[3] >= 2.0]
    print(f"\n{len(rows)} bone(s): {len(converting)} recorded through a frame conversion, "
          f"{len(moving)} of those following something that moves with the animation")
    if moving:
        print("worth looking at first: " + ", ".join(r[1] for r in sorted(moving, reverse=True)[:6]))
    else:
        print("every conversion is constant: nothing about the animation is being recorded through a "
              "frame that changes over the take")
    return 0


if __name__ == "__main__":
    sys.exit(main())
