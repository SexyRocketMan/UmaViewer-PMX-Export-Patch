"""Dump a character's CySpring rig - the chains, their parameters and their colliders - as JSON.

The game's wobble physics (tails, hair, skirts, ribbons) is data plus a solver: `CySpringDataContainer`
components inside the model's costume prefabs hold one entry per chain, each with per-bone stiffness, drag,
gravity, collision radius, angle limits and a list of colliders, plus the colliders themselves (sphere,
capsule or plane, attached to a bone). This reads that data out of the installed game, so the same numbers
can drive a solver anywhere - the Blender bake, the viewer, a comparison against the game's own plugin.

Read-only. Needs `UnityPy` (`pip install UnityPy`) and the game installed at GAME below.

    python Tools/uma_spring_dump.py 1001 00 -o out/1001_00_spring.json

The output is what the viewer packs into an exported model's English comment, so the two must agree:

    {"format":"UMA-SPRING","version":1,"viewer":"3.3","character":{...},
     "containers":[{"asset":"...","flags":{...},"chains":[{"bones":[...]}],"colliders":[...]}]}

`python Tools/uma_spring_dump.py 1001 00 --compact` prints the single-line form instead.

## Where the geometry comes from, and what is still open

`--chain` builds a solver run from the prefabs, not from an exported model: same units as the collider data,
and the bone order `Initialize` walks. Doing that settled several things by measurement:

* the tail's bones live in `3d/chara/tail/tail0001_00/pfb_tail0001_00` in game units - Hip at the origin, the
  chain hanging ~0.5 units down - and `Tail_Ctrl` sits on top of `Sp_Hi_Tail0_B_00`, which is the zero-length
  segment the game answers with `Exist = false`;
* `Sp_Hi_Tail0_B_03` is in no prefab at all, so `B_04` hangs off a bone the models do not have - both are
  dropped, and 3 of the 5 bones are what can actually be simulated for this tail;
* the colliders a chain tests against are defined in another container (the tail's two live in the body's
  clothing prefab), so they have to be looked up across the whole dump;
* **the colliders are not in the prefabs.** There is no `CySpringCollisionComponent` in the body or tail
  prefab: they are created at run time from the container's `collisionParam`, and the code that does it is
  not in the decompiled subset this repository carries. So the radii in the dump are the authored numbers,
  but whether the game scales them on the way to the solver is unverified - and it matters, because
  `Col_B_Hip_Tail` carries a radius of 1.0 against tail links of 0.05..0.16, which would swallow the whole
  tail if taken literally. The viewer takes them literally (with +-14% fudges), which may be part of why its
  tail physics does not look right.

That last point is the next thing to settle, and it needs evidence this repository does not have: either the
missing factory code (a disassembly job) or a measurement against the game's own tail.
"""
from __future__ import annotations

import argparse
import json
import math
import os
import sqlite3
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
GAME = r"C:\Users\user\Umamusume\umamusume_Data\Persistent"
META = os.path.join(ROOT, "meta_plain.sqlite")
MDB = os.path.join(GAME, "master", "master.mdb")
FORMAT = "UMA-SPRING"
VERSION = 1

# CySpringCollisionData.CollisionType: Sphere, None, Capsule, Plane - in that order, so the numbers are 0..3
COLLISION_TYPES = {0: "sphere", 1: "none", 2: "capsule", 3: "plane"}
COLLISION_TYPES_REV = {0: "sphere", 1: "none", 2: "capsule", 3: "plane"}
COLLISION_TYPES_REV = {v: k for k, v in COLLISION_TYPES.items()}
CAPABILITY_GRAVITY = 1          # CySpring.eCapability.GravityControl


def _unitypy():
    try:
        import UnityPy  # noqa: F401
        return UnityPy
    except ImportError:
        raise SystemExit("this tool needs UnityPy: pip install UnityPy")


def _decrypt(data: bytes, key: int) -> bytes:
    """The viewer's UmaAssetBundleStream: everything past the 256-byte header XORed with a 88-byte key."""
    if not key or len(data) <= 256:
        return data
    ab_key = bytes.fromhex("532B4631E4A7B9473E7CFB")      # Config.json -> ABKeyText
    key_bytes = (key & 0xFFFFFFFFFFFFFFFF).to_bytes(8, "little")
    stream = bytearray()
    for base in ab_key:
        for j in range(8):
            stream.append(base ^ key_bytes[j])
    out = bytearray(data)
    n = len(stream)
    for i in range(256, len(out)):
        out[i] ^= stream[i % n]
    return bytes(out)


def _asset_index():
    return sqlite3.connect(f"file:{META}?mode=ro", uri=True)


def _master():
    return sqlite3.connect(f"file:{MDB}?mode=ro", uri=True)


def _tail_model_id(chara_id: int) -> int:
    row = _master().execute("select tail_model_id from chara_data where id = ?", (chara_id,)).fetchone()
    return int(row[0]) if row else 0


def candidate_assets(chara_id: int, costume: str, head_costume: str = None) -> list[str]:
    """The costume prefabs the viewer loads physics from, in the same order it does."""
    head_costume = head_costume or costume
    body = f"bdy{chara_id}_{costume}"
    head = f"chr{chara_id}_{head_costume}"
    candidates = [
        f"3d/chara/body/{body}/clothes/pfb_{body}_cloth00",
        f"3d/chara/body/{body}/clothes/pfb_{body}_bust_cloth00",
        f"3d/chara/head/{head}/clothes/pfb_{head}_cloth00",
    ]
    exclusive = f"tail{chara_id}_{costume}"
    candidates.append(f"3d/chara/tail/{exclusive}/clothes/pfb_{exclusive}_cloth00")

    tail_id = _tail_model_id(chara_id)
    if tail_id > 0:
        tail = f"tail{tail_id:04d}_00"
        candidates.append(f"3d/chara/tail/{tail}/clothes/pfb_{tail}_cloth00")
    return candidates


def _vec(v, default=(0.0, 0.0, 0.0)):
    if isinstance(v, dict):
        return [float(v.get("x", 0)), float(v.get("y", 0)), float(v.get("z", 0))]
    if isinstance(v, (list, tuple)) and len(v) >= 3:
        return [float(v[0]), float(v[1]), float(v[2])]
    return list(default)


def bone_entry(element: dict) -> dict:
    """One bone of a chain: the numbers the solver needs, with the game's field names dropped."""
    return {
        "name": element.get("_boneName") or element.get("_collisionName") or "",
        "stiffness": float(element.get("_stiffnessForce", 0)),
        "drag": float(element.get("_dragForce", 0)),
        "gravity": float(element.get("_gravity", 0)),
        "springForce": _vec(element.get("_springForce")),
        "radius": float(element.get("_collisionRadius", 0)),
        "limit": bool(element.get("_isLimit", False)),
        "limitMin": _vec(element.get("_limitAngleMin")),
        "limitMax": _vec(element.get("_limitAngleMax")),
        "moveApplyRate": float(element.get("MoveSpringApplyRate", 1)),
        "capability": int(element.get("_capability", 0)),
        "capabilityGroup": int(element.get("_capsGroupIndex", 0)),
        "colliders": list(element.get("_collisionNameList") or []),
        "wind": {
            "verticalSlow": float(element.get("_verticalWindRateSlow", 0)),
            "horizontalSlow": float(element.get("_horizontalWindRateSlow", 0)),
            "verticalFast": float(element.get("_verticalWindRateFast", 0)),
            "horizontalFast": float(element.get("_horizontalWindRateFast", 0)),
        },
        "simulateEndBone": bool(element.get("_needSimulateEndBone", False)),
    }


def collider_entry(collision: dict) -> dict:
    return {
        "name": collision.get("_collisionName", ""),
        "type": COLLISION_TYPES.get(int(collision.get("_type", 3)), "none"),
        "targetBone": collision.get("_targetObjectName", ""),
        "inner": bool(collision.get("_isInner", False)),
        "radius": float(collision.get("_radius", 0)),
        "distance": float(collision.get("_distance", 0)),
        "offset": _vec(collision.get("_offset")),
        "offset2": _vec(collision.get("_offset2")),
        "normal": _vec(collision.get("_normal"), (0.0, 1.0, 0.0)),
    }


def container_entry(asset: str, tree: dict) -> dict:
    chains = []
    for spring in tree.get("springParam") or []:
        bones = [bone_entry(spring)]
        for child in spring.get("_childElements") or []:
            bones.append(bone_entry(child))
        chains.append({"root": bones[0]["name"], "bones": bones})

    return {
        "asset": asset,
        "flags": {
            "verticalWind": bool(tree.get("enableVerticalWind", False)),
            "horizontalWind": bool(tree.get("enableHorizontalWind", False)),
            "hipMove": bool(tree.get("IsEnableHipMoveParam", False)),
            "correctScale": bool(tree.get("UseCorrectScaleCalc", False)),
            "hipMoveInfluenceDistance": float(tree.get("HipMoveInfluenceDistance", 0)),
            "hipMoveInfluenceMaxDistance": float(tree.get("HipMoveInfluenceMaxDistance", 0)),
        },
        "wind": {
            "centerAngleSlow": float(tree.get("centerWindAngleSlow", 0)),
            "centerAngleFast": float(tree.get("centerWindAngleFast", 0)),
            "verticalCycleSlow": float(tree.get("verticalCycleSlow", 0)),
            "horizontalCycleSlow": float(tree.get("horizontalCycleSlow", 0)),
            "verticalAngleWidthSlow": float(tree.get("verticalAngleWidthSlow", 0)),
            "horizontalAngleWidthSlow": float(tree.get("horizontalAngleWidthSlow", 0)),
            "verticalCycleFast": float(tree.get("verticalCycleFast", 0)),
            "horizontalCycleFast": float(tree.get("horizontalCycleFast", 0)),
            "verticalAngleWidthFast": float(tree.get("verticalAngleWidthFast", 0)),
            "horizontalAngleWidthFast": float(tree.get("horizontalAngleWidthFast", 0)),
        },
        "chains": chains,
        "colliders": [collider_entry(c) for c in (tree.get("collisionParam") or [])],
    }


def read_container(UnityPy, con, asset: str):
    row = con.execute("select h, e from a where n = ?", (asset,)).fetchone()
    if not row:
        return "not in the asset index"
    path = os.path.join(GAME, "dat", row[0][:2], row[0])
    if not os.path.exists(path):
        return "not downloaded"
    try:
        env = UnityPy.load(__import__("io").BytesIO(_decrypt(open(path, "rb").read(), row[1])))
    except Exception as error:                       # noqa: BLE001 - a tool: report and keep going
        return f"unreadable ({error})"

    scripts = {}
    for obj in env.objects:
        if obj.type.name == "MonoScript":
            try:
                scripts[obj.path_id] = obj.read().m_ClassName
            except Exception:                        # noqa: BLE001
                pass

    found = []
    for obj in env.objects:
        if obj.type.name != "MonoBehaviour":
            continue
        try:
            tree = obj.read_typetree()
        except Exception:                            # noqa: BLE001
            continue
        if scripts.get(tree.get("m_Script", {}).get("m_PathID")) == "CySpringDataContainer":
            found.append(tree)
    return found


def dump(chara_id: int, costume: str, head_costume: str = None) -> dict:
    UnityPy = _unitypy()
    con = _asset_index()

    character = {
        "id": chara_id,
        "costume": costume,
        "headCostume": head_costume or costume,
        "tailModelId": _tail_model_id(chara_id),
    }
    containers, notes = [], []
    for asset in candidate_assets(chara_id, costume, head_costume):
        result = read_container(UnityPy, con, asset)
        if isinstance(result, str):
            if result != "not in the asset index":
                notes.append(f"{asset}: {result}")
            continue
        if not result:
            continue
        if len(result) == 1:
            containers.append(container_entry(asset, result[0]))
        else:
            # a prefab can hold one container per part; keep them apart so a reader can tell them apart
            for index, tree in enumerate(result):
                entry = container_entry(asset, tree)
                entry["part"] = index
                containers.append(entry)

    return {
        "format": FORMAT,
        "version": VERSION,
        "character": character,
        "containers": containers,
        "notes": notes,
    }


# --------------------------------------------------------------------------- a chain, in the game's own space

def prefab_world_positions(UnityPy, con, asset: str):
    """Every transform in one prefab, positioned in that prefab's own space (`{name: (x, y, z)}`).

    This is where the geometry comes from and the exported model is not: the prefab is in the game's units,
    the same ones the collider radii and offsets are in, and its bone order is the one `Initialize` walks.
    """
    row = con.execute("select h, e from a where n = ?", (asset,)).fetchone()
    if not row:
        return None
    path = os.path.join(GAME, "dat", row[0][:2], row[0])
    if not os.path.exists(path):
        return None

    env = UnityPy.load(__import__("io").BytesIO(_decrypt(open(path, "rb").read(), row[1])))
    names, transforms = {}, {}
    for obj in env.objects:
        try:
            data = obj.read()
        except Exception:                            # noqa: BLE001
            continue
        if obj.type.name == "GameObject":
            names[obj.path_id] = data.m_Name
        elif obj.type.name == "Transform":
            position = data.m_LocalPosition
            rotation = data.m_LocalRotation
            transforms[obj.path_id] = {
                "name": names.get(data.m_GameObject.m_PathID, "?"),
                "father": data.m_Father.m_PathID,
                "local": (position.x, position.y, position.z),
                "rotation": (rotation.x, rotation.y, rotation.z, rotation.w),
            }
    if not transforms:
        return None

    def place(path_id, parent_name, parent_rotation, parent_position, out, depth=0):
        entry = transforms[path_id]
        local = entry["local"]
        rotation = entry["rotation"]
        # local position rotated by the parent's rotation, then offset - the usual transform composition
        moved = _quat_rotate((parent_rotation[0], parent_rotation[1], parent_rotation[2], parent_rotation[3]),
                             local)
        position = (parent_position[0] + moved[0], parent_position[1] + moved[1], parent_position[2] + moved[2])
        out[entry["name"]] = (position, parent_name)
        composed = _quat_mul((parent_rotation[0], parent_rotation[1], parent_rotation[2], parent_rotation[3]),
                             (rotation[0], rotation[1], rotation[2], rotation[3]))
        for child, value in transforms.items():
            if value["father"] == path_id:
                place(child, entry["name"], composed, position, out, depth + 1)

    children = {path_id for entry in transforms.values() for path_id in [entry["father"]]}
    out = {}
    for path_id, entry in transforms.items():
        if entry["father"] == 0 or entry["father"] not in transforms:
            place(path_id, "", (0.0, 0.0, 0.0, 1.0), (0.0, 0.0, 0.0), out)
    return out


def _quat_mul(a, b):
    ax, ay, az, aw = a
    bx, by, bz, bw = b
    return (aw * bx + ax * bw + ay * bz - az * by,
            aw * by - ax * bz + ay * bw + az * bx,
            aw * bz + ax * by - ay * bx + az * bw,
            aw * bw - ax * bx - ay * by - az * bz)


def _quat_rotate(q, v):
    x, y, z, w = q
    x2, y2, z2 = x * 2.0, y * 2.0, z * 2.0
    xx, yy, zz = x * x2, y * y2, z * z2
    xy, xz, yz = x * y2, x * z2, y * z2
    wx, wy, wz = w * x2, w * y2, w * z2
    vx, vy, vz = v
    return ((1.0 - (yy + zz)) * vx + (xy - wz) * vy + (xz + wy) * vz,
            (xy + wz) * vx + (1.0 - (xx + zz)) * vy + (yz - wx) * vz,
            (xz - wy) * vx + (yz + wx) * vy + (1.0 - (xx + yy)) * vz)


def chain_geometry(UnityPy, con, dump: dict, root: str):
    """One chain, with its bones, colliders and collider parents positioned in a single space.

    The bones and the colliders they test against can live in different prefabs (the tail's bones are in the
    tail prefab, its two colliders hang from `Hip` and `Chest` in the body's), so both are placed relative to
    one reference bone: whichever collider parent turns up first, else the chain root's own parent.
    """
    container = chain = None
    for candidate in dump.get("containers", []):
        for entry in candidate.get("chains", []):
            if entry["root"] == root or any(b["name"] == root for b in entry["bones"]):
                container, chain = candidate, entry
                break
        if chain:
            break
    if chain is None:
        raise SystemExit(f"no chain rooted at {root} in this dump")

    # every prefab that could hold the bones: the chain's own container, the model it dresses, and the body
    # and head models that carry the collider bones
    assets = [container["asset"]]
    # the prefab a costume piece dresses: .../<part>/clothes/pfb_<part>_cloth00 -> .../<part>/pfb_<part>
    part_dir = os.path.dirname(os.path.dirname(container["asset"]))
    if part_dir:
        assets.append(f"{part_dir}/pfb_{os.path.basename(part_dir)}")
    character = dump.get("character", {})
    body = f"bdy{character.get('id')}_{character.get('costume')}"
    head = f"chr{character.get('id')}_{character.get('headCostume')}"
    assets += [f"3d/chara/body/{body}/pfb_{body}", f"3d/chara/head/{head}/pfb_{head}"]
    # a body model is often split into variants (pfb_bdy1001_00_01_0_0_0); take whatever the index has
    for (name,) in con.execute("select n from a where n like ? and n not like '%clothes%' order by n limit 1",
                               (f"3d/chara/body/{body}/pfb_%",)):
        assets.append(name)
    for (name,) in con.execute("select n from a where n like ? and n not like '%clothes%' order by n limit 1",
                               (f"3d/chara/head/{head}/pfb_%",)):
        assets.append(name)
    assets = [asset for asset in dict.fromkeys(assets) if asset]

    positions = {}
    for asset in assets:
        found = prefab_world_positions(UnityPy, con, asset)
        if found:
            positions[asset] = found

    # colliders are looked up across every container: a chain tests against colliders that are defined
    # in another container (the tail's two live in the body's clothing prefab)
    referenced = {}
    for other in dump.get("containers", []):
        for definition in other.get("colliders", []):
            referenced.setdefault(definition["name"], definition)
    chain_names = [bone["name"] for bone in chain["bones"]]
    collider_names = {name for bone in chain["bones"] for name in bone["colliders"]}

    # the reference bone: a collider's parent if one is available, else the chain's own parent
    reference = None
    for name in sorted(collider_names):
        definition = referenced.get(name)
        if definition and any(definition["targetBone"] in table for table in positions.values()):
            reference = definition["targetBone"]
            break
    if reference is None:
        for table in positions.values():
            if chain_names[0] in table:
                reference = chain_names[0]
                break
    if reference is None:
        raise SystemExit("none of the prefabs carry this chain's bones")

    # The prefabs are separate models that the game composes at runtime, and one chain can span them (the
    # tail's bones are split between the tail model and the head's). So they are aligned by whatever bone
    # they share with what is already merged, rather than by all containing one particular bone.
    merged = {}
    parent_of = {}
    pending = [table for table in positions.values() if reference in table]
    for table in pending:
        origin = table[reference][0]
        for name, (position, parent) in table.items():
            merged.setdefault(name, (position[0] - origin[0], position[1] - origin[1], position[2] - origin[2]))
            parent_of.setdefault(name, parent)

    remaining = [table for table in positions.values() if table not in pending]
    progress = True
    while remaining and progress:
        progress = False
        for table in list(remaining):
            shared = next((name for name in table if name in merged), None)
            if shared is None:
                continue
            here = table[shared][0]
            there = merged[shared]
            for name, (position, parent) in table.items():
                merged.setdefault(name, (position[0] - here[0] + there[0],
                                         position[1] - here[1] + there[1],
                                         position[2] - here[2] + there[2]))
                parent_of.setdefault(name, parent)
            remaining.remove(table)
            progress = True

    # a bone the models do not have cannot be simulated - the game skips it the same way
    absent = [name for name in chain_names if name not in merged]

    return container, chain, merged, parent_of, referenced, reference, absent


def chain_spec(UnityPy, con, dump: dict, root: str, frames: int, swing: float) -> dict:
    container, chain, merged, parent_of, referenced, reference, absent = chain_geometry(UnityPy, con, dump, root)

    bones = []
    dropped = list(absent)
    for index, entry in enumerate(chain["bones"]):
        if entry["name"] in absent:
            continue
        position = merged[entry["name"]]
        parent_name = chain["bones"][index - 1]["name"] if index > 0 else None
        if parent_name in absent:
            skip = True
            dropped.append(entry["name"])
            continue
        if index == 0:
            # the chain hangs from whatever its first bone's parent is in the prefab hierarchy - `Tail_Ctrl`,
            # usually, which sits on top of the root bone. A zero-length segment is what the game drops
            # (`Exist = false`), so that case stays a drop rather than being papered over.
            parent_position = merged.get(parent_of.get(entry["name"]), merged.get(reference, position))
        else:
            parent_position = merged[parent_name]

        offset = (position[0] - parent_position[0],
                  position[1] - parent_position[1],
                  position[2] - parent_position[2])
        distance = math.sqrt(offset[0] ** 2 + offset[1] ** 2 + offset[2] ** 2)
        if distance == 0.0:
            dropped.append(entry["name"])
            continue
        axis = (offset[0] / distance, offset[1] / distance, offset[2] / distance)

        bones.append({
            "name": entry["name"],
            "position": position,
            "axis": axis,
            "distance": distance,
            "stiffness": entry["stiffness"],
            "drag": entry["drag"],
            "gravity": entry["gravity"],
            "radius": entry["radius"],
            "capability": entry["capability"],
            "capabilityGroup": entry["capabilityGroup"],
            "colliders": [name for name in entry["colliders"] if name in referenced],
        })
    if not bones:
        raise SystemExit("every bone of this chain was dropped as zero length")

    parents = {}
    colliders = []
    for name in sorted({c for bone in bones for c in bone["colliders"]}):
        definition = referenced[name]
        target = definition["targetBone"]
        if target not in merged:
            continue
        if target not in parents:
            parents[target] = merged[target]
        colliders.append({
            "name": name,
            "type": COLLISION_TYPES_REV.get(definition["type"], 1),
            "position": definition["offset"],
            "position2": definition["offset2"],
            "radius": definition["radius"],
            "purpose": 0,
            "parent": target,
        })

    pivot = merged.get(reference, bones[0]["position"])
    motion = []
    if swing:
        half = max(1, frames // 4)
        for frame in range(frames + 1):
            if frame <= half:
                angle = swing * (frame / half)
            elif frame <= 2 * half:
                angle = swing * (1.0 - (frame - half) / half)
            else:
                angle = 0.0
            radians = math.radians(angle) / 2.0
            motion.append({"frame": frame, "rotation": [math.sin(radians), 0.0, 0.0, math.cos(radians)]})

    return {
        "format": "UMA-SPRING-CHAIN",
        "version": 1,
        "name": root,
        "source": {"dump": os.path.basename(container["asset"]), "bones": "prefab", "reference": reference},
        "frames": frames,
        "bones": bones,
        "colliders": colliders,
        "parents": [{"name": name, "position": position, "rotation": [0.0, 0.0, 0.0, 1.0]}
                    for name, position in parents.items()],
        "pivot": pivot,
        "motion": motion,
        "dropped": dropped,
    }


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description="Dump a character's CySpring chains, parameters and colliders.")
    parser.add_argument("chara", type=int, help="character id, e.g. 1001")
    parser.add_argument("costume", nargs="?", default="00", help="costume id, default 00")
    parser.add_argument("--head", default=None, help="head costume id, default the body costume")
    parser.add_argument("--viewer", default="3.3", help="viewer version to stamp into the dump")
    parser.add_argument("-o", "--out", default="", help="write the JSON here instead of stdout")
    parser.add_argument("--compact", action="store_true", help="one line, as it goes into the model comment")
    parser.add_argument("--chain", default="", help="write one chain as a solver run instead of the whole dump")
    parser.add_argument("--frames", type=int, default=120, help="frames for --chain")
    parser.add_argument("--swing", type=float, default=0.0, help="swing the chain's parent by this many degrees")
    args = parser.parse_args(argv)

    if args.chain:
        UnityPy = _unitypy()
        data = dump(args.chara, args.costume, args.head)
        spec = chain_spec(UnityPy, _asset_index(), data, args.chain, args.frames, args.swing)
        text = json.dumps(spec, ensure_ascii=False, separators=(",", ":"))
        if args.out:
            with open(args.out, "w", encoding="utf-8") as handle:
                handle.write(text)
            print(f"{args.out}: {len(spec['bones'])} bone(s), {len(spec['colliders'])} collider(s), "
                  f"{len(spec['parents'])} collider parent(s), {len(spec['motion'])} motion key(s)"
                  + (f", dropped zero-length: {spec['dropped']}" if spec["dropped"] else ""))
        else:
            print(text)
        return 0

    data = dump(args.chara, args.costume, args.head)
    data["viewer"] = args.viewer

    chains = sum(len(c["chains"]) for c in data["containers"])
    bones = sum(len(chain["bones"]) for c in data["containers"] for chain in c["chains"])
    colliders = sum(len(c["colliders"]) for c in data["containers"])

    text = json.dumps(data, ensure_ascii=False, separators=(",", ":")) if args.compact \
        else json.dumps(data, ensure_ascii=False, indent=2)

    if args.out:
        os.makedirs(os.path.dirname(os.path.abspath(args.out)), exist_ok=True)
        with open(args.out, "w", encoding="utf-8") as handle:
            handle.write(text)
        print(f"{args.out}: {len(text)} bytes, {len(data['containers'])} container(s), "
              f"{chains} chain(s), {bones} bone(s), {colliders} collider(s)")
    else:
        print(text)

    for note in data["notes"]:
        print(f"note: {note}", file=sys.stderr)
    if not data["containers"]:
        print("no CySpring data found - is the costume downloaded?", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
