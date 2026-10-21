"""Turn a character's real tail (or any spring chain) into the input a solver run needs.

`uma_spring_dump.py` gives the numbers the game ships - stiffness, drag, gravity, radii, colliders. The
solver also needs the *geometry*: where the bones sit at rest, how far apart they are, which bone each
collider hangs from, and what the chain hangs from. That geometry is in the exported model, so this reads
the spring data and a `.pmx` and writes one JSON both the port (`cyspring_port.py --spec`) and the Blender
side can consume.

    python Tools/uma_spring_chain.py spring.json --pmx model.pmx --root Sp_Hi_Tail0_B_00 -o tail.json
    python Tools/uma_spring_chain.py spring.json --pmx model.pmx --list           # what chains exist
    python Tools/cyspring_port.py --spec tail.json                                # run it

Coordinates are the model's own (MMD space, Y down, as mmd_tools imports it). The port does not care about
the convention - it moves points around - but a bake does, which is why nothing here converts anything.

**Status: experimental, and the last run says why.** Two things are still open, both about the *binding*
rather than the solver (the solver is proven against the game's C#, see `cyspring_port.py --compare`):

* **Space.** The exported model is in MMD units, roughly eight times the game's: a tail link measures ~2.0
  here and ~0.25 in the game, while the collider radii come from the dump in game units (0.85, 1.0). Mixing
  the two is why the collider clearance comes out nonsensical. Either read the bones from the game prefab
  (same space as the rest of the data) or scale the collider data by the model's factor.
* **Which segment belongs to which bone.** `CySpringBoneBase.Initialize` stores a bone's distance and axis
  on its *parent*, and a zero-length segment makes the game drop both bones (`Exist = false`) - which the
  tail has: `Tail_Ctrl` and `Sp_Hi_Tail0_B_00` sit at the same position. Reading the transforms out of the
  prefab, in the order `Initialize` sees them, settles it; guessing from the PMX does not.
"""
from __future__ import annotations

import argparse
import json
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pmx_inspect import read_model  # noqa: E402  (the repo's own PMX reader)

# CySpringCollisionData.CollisionType
COLLISION_TYPE = {"sphere": 0, "none": 1, "capsule": 2, "plane": 3}


def load_dump(path: str) -> dict:
    with open(path, encoding="utf-8") as handle:
        return json.load(handle)


def all_chains(dump: dict):
    for container in dump.get("containers", []):
        for chain in container.get("chains", []):
            yield container, chain


def find_chain(dump: dict, root: str):
    for container, chain in all_chains(dump):
        if chain["root"] == root or any(bone["name"] == root for bone in chain["bones"]):
            return container, chain
    return None, None


def sub(a, b):
    return [a[0] - b[0], a[1] - b[1], a[2] - b[2]]


def length(a):
    return math.sqrt(a[0] * a[0] + a[1] * a[1] + a[2] * a[2])


def normalized(a):
    n = length(a)
    return [0.0, 0.0, 0.0] if n < 1e-9 else [a[0] / n, a[1] / n, a[2] / n]


def build(dump: dict, model, root: str, frames: int, swing_degrees: float) -> dict:
    container, chain = find_chain(dump, root)
    if chain is None:
        raise SystemExit(f"no chain rooted at {root} in this dump; --list shows what is there")

    by_name = {bone.name: bone for bone in model.bones}
    missing = [bone["name"] for bone in chain["bones"] if bone["name"] not in by_name]
    if missing:
        raise SystemExit(f"the model has no bone(s) {missing} - is this the model the dump came from?")

    # the chain, in the order the game stores it: parent first
    bones = []
    # The solver constrains each bone to `init_bone_distance` from the *previous* bone's position - it
    # seeds `old_position` from the parent and then projects the bone back onto that sphere. So both the
    # axis and the distance are measured from the parent to the bone, and the root's parent is the bone it
    # hangs from, not the bone below it. Measuring the other way round shifts the whole chain by one link.
    root_parent_pos = list(model.bones[by_name[chain["bones"][0]["name"]].parent].pos)

    for index, entry in enumerate(chain["bones"]):
        pmx_bone = by_name[entry["name"]]
        parent_pos = root_parent_pos if index == 0 else by_name[chain["bones"][index - 1]["name"]].pos
        offset = sub(pmx_bone.pos, parent_pos)
        axis = normalized(offset)
        if axis == [0.0, 0.0, 0.0] and bones:
            axis = bones[-1]["axis"]

        bones.append({
            "name": entry["name"],
            "position": list(pmx_bone.pos),
            "axis": axis,
            "distance": length(offset),
            "stiffness": entry["stiffness"],
            "drag": entry["drag"],
            "gravity": entry["gravity"],
            "radius": entry["radius"],
            "capability": entry["capability"],
            "capabilityGroup": entry["capabilityGroup"],
            "colliders": list(entry["colliders"]),
        })

    # The colliders the chain names, and the bones they hang from. They are looked up across every
    # container in the dump, not just this chain's own: the tail's two colliders live in the body's
    # clothing prefab, and the viewer merges the collider sets for exactly that reason.
    referenced = {name for bone in chain["bones"] for name in bone["colliders"]}
    collider_defs = {}
    for other in dump.get("containers", []):
        for definition in other.get("colliders", []):
            collider_defs.setdefault(definition["name"], definition)
    colliders = []
    parent_names = []
    for name in sorted(referenced):
        definition = collider_defs.get(name)
        if definition is None:
            print(f"note: {name} is named by the chain but not defined in {container['asset']}", file=sys.stderr)
            continue
        if definition["targetBone"] not in by_name:
            print(f"note: {name} hangs from {definition['targetBone']}, which is not in this model",
                  file=sys.stderr)
            continue
        if definition["targetBone"] not in parent_names:
            parent_names.append(definition["targetBone"])
        colliders.append({
            "name": name,
            "type": COLLISION_TYPE.get(definition["type"], 1),
            "position": definition["offset"],
            "position2": definition["offset2"],
            "radius": definition["radius"],
            "purpose": 0,
            "parent": definition["targetBone"],
        })

    parents = [{
        "name": name,
        "position": list(by_name[name].pos),
        "rotation": [0.0, 0.0, 0.0, 1.0],
    } for name in parent_names]

    # the chain hangs from its root's parent bone, which is also the pivot an animated swing turns around
    root_parent = model.bones[by_name[chain["bones"][0]["name"]].parent]
    pivot = list(root_parent.pos)

    motion = []
    if swing_degrees:
        half = max(1, frames // 4)
        for frame in range(frames + 1):
            if frame <= half:
                angle = swing_degrees * (frame / half)
            elif frame <= 2 * half:
                angle = swing_degrees * (1.0 - (frame - half) / half)
            else:
                angle = 0.0
            radians = math.radians(angle) / 2.0
            # a rotation about X, i.e. the chain swinging forward and back
            motion.append({"frame": frame,
                           "rotation": [math.sin(radians), 0.0, 0.0, math.cos(radians)]})

    return {
        "format": "UMA-SPRING-CHAIN",
        "version": 1,
        "name": root,
        "source": {"dump": os.path.basename(container["asset"]), "model": os.path.basename(model.name)},
        "frames": frames,
        "bones": bones,
        "colliders": colliders,
        "parents": parents,
        "pivot": pivot,
        "motion": motion,
    }


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description="Build a solver run from a spring dump and an exported model.")
    parser.add_argument("dump", help="JSON written by uma_spring_dump.py")
    parser.add_argument("--pmx", required=False, help="the exported model the chain lives in")
    parser.add_argument("--root", default="", help="the chain's root bone, e.g. Sp_Hi_Tail0_B_00")
    parser.add_argument("--list", action="store_true", help="print the chains the dump has, then exit")
    parser.add_argument("--frames", type=int, default=120)
    parser.add_argument("--swing", type=float, default=0.0, help="swing the chain's parent by this many degrees")
    parser.add_argument("-o", "--out", default="", help="write here instead of stdout")
    args = parser.parse_args(argv)

    dump = load_dump(args.dump)

    if args.list:
        for container, chain in all_chains(dump):
            print(f"{chain['root']:<24} {len(chain['bones'])} bones   "
                  f"{len(chain['bones'][0]['colliders'])} collider(s)   {container['asset']}")
        return 0

    if not args.pmx or not args.root:
        parser.error("--pmx and --root are needed to build a chain (or use --list)")

    model = read_model(args.pmx)
    spec = build(dump, model, args.root, args.frames, args.swing)
    text = json.dumps(spec, ensure_ascii=False, separators=(",", ":"))

    if args.out:
        with open(args.out, "w", encoding="utf-8") as handle:
            handle.write(text)
        print(f"{args.out}: {len(spec['bones'])} bone(s), {len(spec['colliders'])} collider(s), "
              f"{len(spec['parents'])} parent bone(s), {len(spec['motion'])} motion key(s)")
    else:
        print(text)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
