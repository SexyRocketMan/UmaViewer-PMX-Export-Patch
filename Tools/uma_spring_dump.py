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
"""
from __future__ import annotations

import argparse
import json
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


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description="Dump a character's CySpring chains, parameters and colliders.")
    parser.add_argument("chara", type=int, help="character id, e.g. 1001")
    parser.add_argument("costume", nargs="?", default="00", help="costume id, default 00")
    parser.add_argument("--head", default=None, help="head costume id, default the body costume")
    parser.add_argument("--viewer", default="3.3", help="viewer version to stamp into the dump")
    parser.add_argument("-o", "--out", default="", help="write the JSON here instead of stdout")
    parser.add_argument("--compact", action="store_true", help="one line, as it goes into the model comment")
    args = parser.parse_args(argv)

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
