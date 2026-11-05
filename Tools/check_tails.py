#!/usr/bin/env python3
"""Check that every joint points at the joint MMD points it at.

A PMX stores no bone axis, so the tail is the orientation: MMD, Blender and every tool that plays a motion read a
bone's frame from head -> tail. Nothing in an export fails loudly when a tail is wrong - the file opens, the
vertices are right, the motions play - the bone just rotates around a skewed axis, which shows up as a limb that
leans the wrong way once it is animated.

The convention is not a matter of taste: MMD's own rigs point the thigh at the knee, the elbow at the wrist, the
wrist at the middle finger, the chest at the neck and the hip up the body. This reads the tails out of the file and
checks each joint against that, so a change to the exporter's tail rule can be measured instead of eyeballed:

    python Tools/check_tails.py C:/dump/characters
    python Tools/check_tails.py C:/dump/characters --min 0.99 --worst 10

The rest pose is not checked here (Tools/dump_report.py does that) and neither is the eye rule: an eye bone points
out of the face on purpose, so it is not in the table.

Exit code 1 when fewer than --min of the joints land on their joint, so it can gate a dump.
"""

import argparse
import collections
import glob
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import pmx_inspect

#: each joint and the bone(s) MMD points it at. The head has no joint below it to name: it is correct when its tail
#: points up, which is checked separately.
EXPECTED = {
    "Hip": {"UpBody_Ctrl", "Waist", "Spine"},
    "Waist": {"Spine"},
    "Spine": {"Chest"},
    "Chest": {"Neck"},
    "Neck": {"Head"},
    "Shoulder_L": {"Arm_L"},
    "Arm_L": {"Elbow_L"},
    "Elbow_L": {"Wrist_L"},
    "Wrist_L": {"Middle_01_L"},
    "Thigh_L": {"Knee_L"},
    "Knee_L": {"Ankle_L"},
    "Ankle_L": {"Toe_L", "Toe_offset_L"},
    "Shoulder_R": {"Arm_R"},
    "Arm_R": {"Elbow_R"},
    "Elbow_R": {"Wrist_R"},
    "Wrist_R": {"Middle_01_R"},
    "Thigh_R": {"Knee_R"},
    "Knee_R": {"Ankle_R"},
    "Ankle_R": {"Toe_R", "Toe_offset_R"},
}

#: bones whose tail is correct when it points up, within this many degrees
UP_JOINTS = {"Head": 5.0}
UP = [0.0, 1.0, 0.0]

#: how far a tail may sit from the direction of the joint it should point at, in degrees
TOLERANCE = 5.0


def subtract(a, b):
    return [a[i] - b[i] for i in range(3)]


def length(vector):
    return math.sqrt(sum(component * component for component in vector))


def dot(a, b):
    la, lb = length(a), length(b)
    return 0.0 if la < 1e-9 or lb < 1e-9 else sum(a[i] * b[i] for i in range(3)) / (la * lb)


def tail_of(model, bone):
    """(kind, vector from the head, the child it names or None)"""
    bones = model.bones
    if bone.tail_bone is not None and 0 <= bone.tail_bone < len(bones):
        child = bones[bone.tail_bone]
        return "child", subtract(child.pos, bone.pos), child.name
    if bone.tail_pos is not None:
        return "offset", list(bone.tail_pos), None
    return None, None, None


def check_file(path):
    """-> (the joints of this model that are wrong, the joints it has at all)

    A tail is judged by the direction it points, not by how it is stored: naming the right child and writing an
    offset that points the same way are the same orientation, and the exporter uses both.
    """
    model = pmx_inspect.read_model(path)
    by_name = {bone.name: bone for bone in model.bones}
    wrong = []
    present = set()
    for name, expected in EXPECTED.items():
        bone = by_name.get(name)
        if bone is None:
            continue
        present.add(name)
        kind, vector, target = tail_of(model, bone)
        expected_directions = [subtract(by_name[joint].pos, bone.pos)
                               for joint in expected if joint in by_name]
        if vector is None or length(vector) < 1e-6:
            wrong.append((name, sorted(expected), "no tail"))
            continue
        if any(length(direction) > 1e-6
               and dot(vector, direction) > math.cos(math.radians(TOLERANCE))
               for direction in expected_directions):
            continue
        wrong.append((name, sorted(expected), target if target else "an offset elsewhere"))
    for name, tolerance in UP_JOINTS.items():
        bone = by_name.get(name)
        if bone is None:
            continue
        present.add(name)
        kind, vector, target = tail_of(model, bone)
        if vector is None or length(vector) < 1e-6:
            wrong.append((name, ["up"], "no tail"))
        elif dot(vector, UP) <= math.cos(math.radians(tolerance)):
            wrong.append((name, ["up"],
                          target if target else f"that way ({vector[0]:+.2f},{vector[1]:+.2f},{vector[2]:+.2f})"))
    return wrong, present


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("paths", nargs="+", help="PMX files, or folders searched for *.pmx")
    ap.add_argument("--min", type=float, default=0.95,
                    help="how many of the joints must land on their joint, 0..1 (default 0.95)")
    ap.add_argument("--worst", type=int, default=6, help="how many of the failures to print")
    args = ap.parse_args()

    files = []
    for path in args.paths:
        if os.path.isdir(path):
            files.extend(sorted(glob.glob(os.path.join(path, "**", "*.pmx"), recursive=True)))
        else:
            files.append(path)
    if not files:
        print("no PMX files found", file=sys.stderr)
        return 1

    good = 0
    failures = []
    per_joint = collections.Counter()
    per_joint_wrong = collections.Counter()
    for path in files:
        try:
            wrong, present = check_file(path)
        except Exception as error:                       # noqa: BLE001 - a broken file is a failure of its own
            print(f"  could not read {os.path.basename(path)}: {type(error).__name__}: {error}", file=sys.stderr)
            return 1
        for name in present:
            per_joint[name] += 1
        for name, expected, got in wrong:
            per_joint_wrong[name] += 1
            failures.append((os.path.basename(path), name, expected, got))
        good += len(present) - len(wrong)

    total = sum(per_joint.values())
    share = good / total if total else 0.0
    print(f"{len(files)} model(s), {total} joint(s) checked, {good} on their joint ({100.0 * share:.1f}%)")
    if failures:
        print("off their joint:")
        for model, name, expected, got in failures[:args.worst]:
            print(f"   {model:<24} {name:<12} expected {', '.join(expected):<26} points at {got}")
        if len(failures) > args.worst:
            print(f"   ... {len(failures) - args.worst} more")
        print("per joint:")
        for name in list(EXPECTED) + list(UP_JOINTS):
            if per_joint_wrong.get(name):
                print(f"   {name:<12} {per_joint_wrong[name]:>5} of {per_joint[name]} wrong")
    if share < args.min:
        print(f"FAIL: {100.0 * share:.1f}% is below the {100.0 * args.min:.0f}% floor")
        return 1
    print(f"ok: at or above the {100.0 * args.min:.0f}% floor")
    return 0


if __name__ == "__main__":
    sys.exit(main())
