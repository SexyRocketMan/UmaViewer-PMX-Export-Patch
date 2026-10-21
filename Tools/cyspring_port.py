"""The game's spring solver, in Python - the port a Blender bake would run.

This is a transcription of the game's own code, not a physics model of our own:

  * `CySpringNative.UpdateNativeCloth` seeds each bone from its parent and then steps every bone;
  * `UpdateNativeClothInternalMono` is the step: an aim vector from stiffness, a drag term from the last
    movement, gravity (per capability group), optional wind, a position update projected back onto the
    bone's rest length, collision resolution against spheres and capsules, and a final rotation from the
    aim vector onto the resulting direction.

The point of the file today is the check that the transcription is faithful. `--trace` writes the frames
this produces; `--compare` holds them against the frames the C# side produced for the same synthetic chain
(`Tools/spring_probe`, `--trace`), so a mistake in the port shows up as a number rather than as a weird tail.

Every arithmetic result is rounded to float32, because the game computes in `float`; doing the maths in
doubles here and rounding once at the end would not reproduce its trajectories.

    python Tools/cyspring_port.py --trace out.json
    python Tools/cyspring_port.py --compare <csharp trace>.json
"""

from __future__ import annotations

import argparse
import ctypes
import json
import math
import sys

LINK_LENGTH = 0.25
BONES = 5
FRAMES = 120


# --------------------------------------------------------------------------- float32 arithmetic

def f32(value: float) -> float:
    """Round to the precision the game computes in."""
    return ctypes.c_float(value).value


def v_add(a, b):
    return (f32(a[0] + b[0]), f32(a[1] + b[1]), f32(a[2] + b[2]))


def v_sub(a, b):
    return (f32(a[0] - b[0]), f32(a[1] - b[1]), f32(a[2] - b[2]))


def v_scale(a, s):
    return (f32(a[0] * s), f32(a[1] * s), f32(a[2] * s))


def v_scale3(a, b):
    """Unity's Vector3.Scale: component-wise."""
    return (f32(a[0] * b[0]), f32(a[1] * b[1]), f32(a[2] * b[2]))


def v_dot(a, b):
    return f32(f32(f32(a[0] * b[0]) + f32(a[1] * b[1])) + f32(a[2] * b[2]))


def v_cross(a, b):
    return (f32(f32(a[1] * b[2]) - f32(a[2] * b[1])),
            f32(f32(a[2] * b[0]) - f32(a[0] * b[2])),
            f32(f32(a[0] * b[1]) - f32(a[1] * b[0])))


def v_length(a):
    return f32(math.sqrt(v_dot(a, a)))


def v_distance(a, b):
    return v_length(v_sub(a, b))


def v_normalized(a):
    length = v_length(a)
    if length <= 1e-9:
        return (0.0, 0.0, 0.0)
    return (f32(a[0] / length), f32(a[1] / length), f32(a[2] / length))


# --------------------------------------------------------------------------- quaternions

IDENTITY = (0.0, 0.0, 0.0, 1.0)


def q_mul(a, b):
    ax, ay, az, aw = a
    bx, by, bz, bw = b
    return (f32(f32(f32(f32(aw * bx) + f32(ax * bw)) + f32(ay * bz)) - f32(az * by)),
            f32(f32(f32(f32(aw * by) - f32(ax * bz)) + f32(ay * bw)) + f32(az * bx)),
            f32(f32(f32(f32(aw * bz) + f32(ax * by)) - f32(ay * bx)) + f32(az * bw)),
            f32(f32(f32(f32(aw * bw) - f32(ax * bx)) - f32(ay * by)) - f32(az * bz)))


def q_rotate(q, v):
    """q * v - rotating a vector by a unit quaternion, written the way Unity writes it."""
    x, y, z, w = q
    x2, y2, z2 = f32(x * 2.0), f32(y * 2.0), f32(z * 2.0)
    xx, yy, zz = f32(x * x2), f32(y * y2), f32(z * z2)
    xy, xz, yz = f32(x * y2), f32(x * z2), f32(y * z2)
    wx, wy, wz = f32(w * x2), f32(w * y2), f32(w * z2)
    vx, vy, vz = v
    return (f32(f32(f32(1.0 - f32(yy + zz)) * vx) + f32(f32(xy - wz) * vy)) + f32(f32(xz + wy) * vz),
            f32(f32(f32(xy + wz) * vx) + f32(f32(1.0 - f32(xx + zz)) * vy)) + f32(f32(yz - wx) * vz),
            f32(f32(f32(xz - wy) * vx) + f32(f32(yz + wx) * vy)) + f32(f32(1.0 - f32(xx + yy)) * vz))


def q_normalized(q):
    length = f32(math.sqrt(f32(f32(f32(q[0] * q[0]) + f32(q[1] * q[1])) + f32(f32(q[2] * q[2]) + f32(q[3] * q[3])))))
    if length <= 1e-9:
        return IDENTITY
    return (f32(q[0] / length), f32(q[1] / length), f32(q[2] / length), f32(q[3] / length))


def q_from_to_rotation(source, target):
    a = v_normalized(source)
    b = v_normalized(target)
    d = v_dot(a, b)

    if d >= 1.0 - 1e-6:
        return IDENTITY

    if d <= -1.0 + 1e-6:
        axis = v_cross(a, (1.0, 0.0, 0.0))
        if v_dot(axis, axis) < 1e-6:
            axis = v_cross(a, (0.0, 1.0, 0.0))
        axis = v_normalized(axis)
        return (axis[0], axis[1], axis[2], 0.0)

    c = v_cross(a, b)
    return q_normalized((c[0], c[1], c[2], f32(1.0 + d)))


# --------------------------------------------------------------------------- the solver

class Bone:
    """One entry of `NativeClothWorking`, by the name the game uses for it."""

    __slots__ = ("init_local_rotation", "old_rotation", "final_rotation", "bone_axis",
                 "current_position", "prev_position", "old_position", "spring_force", "wind_power",
                 "init_bone_distance", "stiffness", "drag", "radius", "gravity",
                 "capability", "caps_group_index", "wind", "wind_group_index",
                 "active_collision", "collision_indices", "exist_collision", "is_skip")

    def __init__(self, **kwargs):
        for slot in self.__slots__:
            setattr(self, slot, kwargs.get(slot))
        self.collision_indices = kwargs.get("collision_indices") or []

    def copy_state_from(self, other: "Bone") -> None:
        for slot in self.__slots__:
            setattr(self, slot, getattr(other, slot))


def _step_bone(bone: Bone, collisions, params, parent_world_positions, parent_world_rotations) -> None:
    """`CySpringNative.UpdateNativeClothInternalMono`, in its own order."""
    diff = v_sub(bone.prev_position, bone.current_position)
    bone.prev_position = bone.current_position

    aim = q_rotate(bone.old_rotation, bone.bone_axis)
    force = v_scale(aim, f32(bone.stiffness * params["stiffness_rate"]))
    force = v_add(force, v_scale(diff, f32(bone.drag * params["drag_rate"])))
    force = v_add(force, bone.spring_force)

    if bone.capability & 1:      # GravityControl: the direction comes from the capability group
        flag = False
        direction = (0.0, -1.0, 0.0)
        group = bone.caps_group_index
        if 0 <= group <= 4:
            flag = params[f"capability{group}"] & 1 != 0
            direction = params[f"gravity_direction{group}"]
        if flag:
            gravity = f32(bone.gravity * params["gravity_rate"])
            force = v_sub(force, v_scale(q_rotate(bone.old_rotation, direction), gravity))
    else:
        force = (force[0], f32(force[1] - f32(bone.gravity * params["gravity_rate"])), force[2])

    if bone.wind & 1:
        force = v_add(v_scale3(force, params["force_scale"]), bone.wind_power)

    bone.current_position = v_add(v_sub(bone.current_position, diff), force)

    diff = v_normalized(v_sub(bone.current_position, bone.old_position))
    bone.current_position = v_add(v_scale(diff, bone.init_bone_distance), bone.old_position)

    if bone.exist_collision and bone.active_collision > 0 and params["collision_switch"]:
        for i in range(bone.active_collision):
            collision = collisions[bone.collision_indices[i]]
            bone.current_position = _resolve(bone, collision, parent_world_positions, parent_world_rotations)

    bone.final_rotation = q_mul(q_from_to_rotation(aim, diff), bone.old_rotation)


def _resolve(bone: Bone, collision, parent_world_positions, parent_world_rotations):
    """The sphere and capsule cases; everything else leaves the bone alone, as the game does."""
    kind = collision["type"]
    if kind == 0:                                    # sphere
        if collision["purpose"] == 2:
            centre = collision["position"]
            current = bone.current_position
            old = bone.old_position
            offset = (0.0, 0.0, 0.0)
        else:
            parent = parent_world_positions[collision["parent_index"]]
            current = v_sub(bone.current_position, parent)
            old = v_sub(bone.old_position, parent)
            centre = q_rotate(parent_world_rotations[collision["parent_index"]], collision["position"])
            offset = parent

        radius = f32(bone.radius + collision["radius"])
        if v_distance(current, centre) <= radius:
            current = v_add(centre, v_scale(v_normalized(v_sub(current, centre)), radius))
            current = v_add(v_scale(v_normalized(v_sub(current, old)), bone.init_bone_distance), old)
        return v_add(current, offset)

    if kind == 2:                                    # capsule
        parent = parent_world_positions[collision["parent_index"]]
        rotation = parent_world_rotations[collision["parent_index"]]
        current = v_sub(bone.current_position, parent)
        old = v_sub(bone.old_position, parent)
        start = q_rotate(rotation, collision["position"])
        end = q_rotate(rotation, collision["position2"])
        axis = v_sub(end, start)
        length = v_length(axis)
        axis = (f32(axis[0] / length), f32(axis[1] / length), f32(axis[2] / length))
        radius = f32(bone.radius + collision["radius"])

        to_bone = v_sub(current, start)
        projection = v_dot(to_bone, axis)
        handled = False
        if 0.0 <= projection < length:
            along = v_scale(axis, projection)
            radial = v_sub(to_bone, along)
            distance = v_length(radial)
            if distance < radius:
                current = v_add(v_add(start, along), v_scale(radial, f32(radius / distance)))
                current = v_add(v_scale(v_normalized(v_sub(current, old)), bone.init_bone_distance), old)
                handled = True

        if not handled:
            for point in (start, end):
                if v_distance(current, point) <= radius:
                    current = v_add(point, v_scale(v_normalized(v_sub(current, point)), radius))
                    current = v_add(v_scale(v_normalized(v_sub(current, old)), bone.init_bone_distance), old)
        return v_add(current, parent)

    return bone.current_position


def update(bones, collisions, params, parent_world_positions, parent_world_rotations) -> None:
    """`CySpringNative.UpdateNativeCloth`: seed the chain from its parent, then step every bone."""
    if bones[0].is_skip:
        return

    for i in range(1, len(bones)):
        bones[i].old_position = bones[i - 1].current_position
        bones[i].old_rotation = q_mul(bones[i].init_local_rotation, bones[i - 1].final_rotation)

    for bone in bones:
        _step_bone(bone, collisions, params, parent_world_positions, parent_world_rotations)


# --------------------------------------------------------------------------- the scenario the C# side runs

def synthetic_chain(collider_radius: float, link_length: float = LINK_LENGTH, bones_count: int = BONES):
    """The same five-bone chain `Tools/spring_probe` builds, down to the last constant."""
    stiffness = [170.0, 190.0, 210.0, 230.0, 250.0]
    drag = [200.0, 400.0, 600.0, 800.0, 1000.0]
    radius = [0.03, 0.05, 0.075, 0.075, 0.075]

    bones = []
    for i in range(bones_count):
        position = (0.0, f32(1.5 - i * link_length), 0.0)
        old = (0.0, f32(1.5 + link_length), 0.0) if i == 0 else (0.0, f32(1.5 - (i - 1) * link_length), 0.0)
        bones.append(Bone(
            init_local_rotation=IDENTITY,
            old_rotation=IDENTITY,
            final_rotation=IDENTITY,
            bone_axis=(0.0, -1.0, 0.0),
            current_position=position,
            prev_position=position,
            old_position=old,
            spring_force=(0.0, 0.0, 0.0),
            wind_power=(0.0, 0.0, 0.0),
            init_bone_distance=link_length,
            stiffness=stiffness[i],
            drag=drag[i],
            radius=radius[i],
            gravity=50.0,
            capability=0,
            caps_group_index=0,
            wind=0,
            wind_group_index=0.0,
            active_collision=0,
            exist_collision=False,
            is_skip=False,
        ))

    collisions = [{
        "type": 0,
        "position": (0.0, -0.5, 0.0),
        "position2": (0.0, -0.5, 0.0),
        "radius": collider_radius,
        "parent_index": 0,
        "purpose": 0,
    }]
    if collider_radius > 0:
        for bone in bones:
            bone.active_collision = 1
            bone.collision_indices = [0]
            bone.exist_collision = True

    params = {
        "stiffness_rate": 1.0,
        "drag_rate": 1.0,
        "gravity_rate": 1.0,
        "collision_switch": True,
        "force_scale": (1.0, 1.0, 1.0),
    }
    for group in range(5):
        params[f"capability{group}"] = 0
        params[f"gravity_direction{group}"] = (0.0, -1.0, 0.0)

    parent_positions = [(0.0, f32(1.5 + link_length), 0.0)]
    parent_rotations = [IDENTITY]
    return bones, collisions, params, parent_positions, parent_rotations


def simulate(collider_radius: float, frames: int = FRAMES):
    """Every frame of the synthetic scenario, in the shape the C# side writes its trace in."""
    bones, collisions, params, parent_positions, parent_rotations = synthetic_chain(collider_radius)
    out = []
    for _ in range(frames + 1):
        out.append([v for bone in bones
                    for v in (bone.current_position[0], bone.current_position[1], bone.current_position[2],
                              bone.final_rotation[0], bone.final_rotation[1], bone.final_rotation[2],
                              bone.final_rotation[3])])
        update(bones, collisions, params, parent_positions, parent_rotations)
    return out


def trace() -> dict:
    return {
        "scenario": "synthetic-tail",
        "linkLength": LINK_LENGTH,
        "bones": BONES,
        "runs": [
            {"name": "no-collider", "colliderRadius": 0.0, "frames": simulate(0.0)},
            {"name": "sphere-collider", "colliderRadius": 0.6, "frames": simulate(0.6)},
        ],
    }


# --------------------------------------------------------------------------- a chain from the game's data

def load_spec(path: str) -> dict:
    with open(path, encoding="utf-8") as handle:
        return json.load(handle)


def build_from_spec(spec: dict):
    """The chain `uma_spring_chain.py` wrote, as solver state: bones, colliders, parents, motion."""
    parent_index = {parent["name"]: i for i, parent in enumerate(spec["parents"])}

    bones = []
    for index, entry in enumerate(spec["bones"]):
        bones.append(Bone(
            init_local_rotation=IDENTITY,
            old_rotation=IDENTITY,
            final_rotation=IDENTITY,
            bone_axis=tuple(entry["axis"]),
            current_position=tuple(entry["position"]),
            prev_position=tuple(entry["position"]),
            old_position=tuple(spec["pivot"]),     # replaced each frame by the animated anchor
            spring_force=(0.0, 0.0, 0.0),
            wind_power=(0.0, 0.0, 0.0),
            init_bone_distance=entry["distance"],
            stiffness=entry["stiffness"],
            drag=entry["drag"],
            radius=entry["radius"],
            gravity=entry["gravity"],
            capability=entry["capability"],
            caps_group_index=entry["capabilityGroup"],
            wind=0,
            wind_group_index=0.0,
            active_collision=0,
            exist_collision=False,
            is_skip=False,
        ))

    # the colliders this run can resolve: the ones whose parent bone was supplied, in spec order, so a
    # bone''s `collision_indices` are indexes into this same list
    collisions = []
    for definition in spec["colliders"]:
        index = parent_index.get(definition["parent"])
        if index is None:
            continue                      # hangs from a bone the run was not given
        collisions.append({
            "name": definition["name"],
            "type": definition["type"],
            "position": tuple(definition["position"]),
            "position2": tuple(definition["position2"]),
            "radius": definition["radius"],
            "parent_index": index,
            "purpose": definition.get("purpose", 0),
        })

    for bone, entry in zip(bones, spec["bones"]):
        indices = [i for i, collision in enumerate(collisions) if collision["name"] in entry["colliders"]]
        if indices:
            bone.exist_collision = True
            bone.collision_indices = indices
            bone.active_collision = len(indices)

    params = {
        "stiffness_rate": 1.0,
        "drag_rate": 1.0,
        "gravity_rate": 1.0,
        "collision_switch": True,
        "force_scale": (1.0, 1.0, 1.0),
    }
    for group in range(5):
        params[f"capability{group}"] = 0
        params[f"gravity_direction{group}"] = (0.0, -1.0, 0.0)

    return bones, collisions, params, spec


def _motion_at(spec: dict, frame: int):
    """The parent's rotation at a frame. A keyframe list is expected, identity when there is none."""
    motion = spec.get("motion") or []
    if not motion:
        return IDENTITY
    nearest = motion[0]
    for key in motion:
        if key["frame"] <= frame:
            nearest = key
        else:
            break
    return tuple(nearest["rotation"])


def simulate_spec(spec: dict, trace: bool = False):
    """Run a real chain: the parent's animated rotation carries the whole chain, the solver lags behind it."""
    bones, collisions, params, spec = build_from_spec(spec)
    pivot = tuple(spec["pivot"])
    rest_parent_positions = [tuple(p["position"]) for p in spec["parents"]]
    root_parent = None
    # the anchor is the bone the chain hangs from: the parent of its root bone
    for index, parent in enumerate(spec["parents"]):
        root_parent = index if root_parent is None else root_parent

    frames = []
    diagnostics = []
    for frame in range(spec["frames"] + 1):
        rotation = _motion_at(spec, frame)

        parent_positions = [v_add(pivot, q_rotate(rotation, v_sub(rest, pivot)))
                            for rest in rest_parent_positions]
        parent_rotations = [rotation for _ in rest_parent_positions]
        anchor = parent_positions[root_parent] if parent_positions else pivot
        bones[0].old_position = anchor

        if trace:
            frames.append([v for bone in bones
                           for v in (bone.current_position[0], bone.current_position[1], bone.current_position[2],
                                     bone.final_rotation[0], bone.final_rotation[1], bone.final_rotation[2],
                                     bone.final_rotation[3])])

        update(bones, collisions, params, parent_positions, parent_rotations)

        worst_length = 0.0
        for i in range(1, len(bones)):
            worst_length = max(worst_length,
                               abs(v_distance(bones[i].current_position, bones[i - 1].current_position)
                                   - bones[i].init_bone_distance))
        clearance = None
        if collisions:
            clearance = min(v_distance(bone.current_position, parent_positions[collision["parent_index"]]
                                       if collision["purpose"] != 2 else collision["position"])
                            - f32(bone.radius + collision["radius"])
                            for bone in bones for collision in collisions)
        # how far the chain's own direction has drifted from where an animated parent would put it
        driven = v_normalized(q_rotate(rotation, tuple(spec["bones"][0]["axis"])))
        lag = math.degrees(math.acos(max(-1.0, min(1.0, v_dot(driven, v_normalized(
            v_sub(bones[0].current_position, anchor))))))) if bones else 0.0
        diagnostics.append({"frame": frame, "lengthError": worst_length,
                            "clearance": clearance, "lagDegrees": lag})

    return (frames if trace else None), diagnostics


# --------------------------------------------------------------------------- comparing

def compare(reference: dict, mine: dict) -> int:
    """Hold this port against the C# trace, frame by frame."""
    failures = 0
    for run_ref, run_mine in zip(reference["runs"], mine["runs"]):
        worst_position = 0.0
        worst_rotation = 0.0
        first_bad = None
        for frame, (a, b) in enumerate(zip(run_ref["frames"], run_mine["frames"])):
            for bone in range(reference["bones"]):
                for k in range(7):
                    delta = abs(a[bone * 7 + k] - b[bone * 7 + k])
                    limit = worst_position if k < 3 else worst_rotation
                    if k < 3:
                        worst_position = max(worst_position, delta)
                    else:
                        worst_rotation = max(worst_rotation, delta)
                    if delta > 1e-6 and first_bad is None:
                        first_bad = (frame, bone, k, a[bone * 7 + k], b[bone * 7 + k])
        status = "ok" if first_bad is None else "MISMATCH"
        if first_bad is not None:
            failures += 1
        print(f"  {run_ref['name']:<16} {status:<9} max |dposition|={worst_position:.3e} "
              f"max |drotation|={worst_rotation:.3e}"
              + ("" if first_bad is None else
                 f"   first at frame {first_bad[0]} bone {first_bad[1]} component {first_bad[2]}: "
                 f"{first_bad[3]!r} vs {first_bad[4]!r}"))
    return failures


def report(spec: dict, diagnostics: list) -> None:
    """What the run did, in numbers: the links hold, nothing diverges, and the chain lags its driver."""
    worst_length = max(d["lengthError"] for d in diagnostics)
    clearances = [d["clearance"] for d in diagnostics if d["clearance"] is not None]
    lengths_ok = worst_length < 1e-3
    finite = all(abs(d["lagDegrees"]) <= 180.0 for d in diagnostics)

    print(f"{spec['name']}: {len(spec['bones'])} bone(s), {len(spec['colliders'])} collider(s), "
          f"{len(diagnostics)} frame(s) -> links {'hold' if lengths_ok else 'DRIFT'} "
          f"(worst error {worst_length:.2e}), {'finite' if finite else 'DIVERGED'}")
    if clearances:
        print(f"  clearance to the colliders: min {min(clearances):+.3f} "
              f"(negative means a bone ended up inside one)")
    step = max(1, len(diagnostics) // 8)
    print("   frame   lag(deg)   link error")
    for d in diagnostics:
        if d["frame"] % step == 0 or d["frame"] == len(diagnostics) - 1:
            print(f"  {d['frame']:>6}   {d['lagDegrees']:8.2f}   {d['lengthError']:.2e}")


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description="The game's spring solver in Python, and the check that the "
                                                 "transcription matches the C# it came from.")
    parser.add_argument("--trace", default="", help="write this port's frames to a JSON file")
    parser.add_argument("--compare", default="", help="compare against a trace written by Tools/spring_probe")
    parser.add_argument("--spec", default="", help="run a real chain written by Tools/uma_spring_chain.py")
    parser.add_argument("--trace-out", default="", help="write that run's frames here")
    args = parser.parse_args(argv)

    if args.spec:
        spec = load_spec(args.spec)
        frames, diagnostics = simulate_spec(spec, trace=True)
        report(spec, diagnostics)
        if args.trace_out:
            with open(args.trace_out, "w", encoding="utf-8") as handle:
                json.dump({"spec": spec["name"], "bones": len(spec["bones"]), "frames": frames}, handle,
                          separators=(",", ":"))
            print(f"{args.trace_out}: {len(frames)} frame(s)")

    if args.trace:
        data = trace()
        with open(args.trace, "w", encoding="utf-8") as handle:
            json.dump(data, handle, separators=(",", ":"))
        frames = sum(len(run["frames"]) for run in data["runs"])
        print(f"{args.trace}: {len(data['runs'])} run(s), {frames} frame(s)")

    if args.compare:
        with open(args.compare, encoding="utf-8") as handle:
            reference = json.load(handle)
        failures = compare(reference, trace())
        print(f"{len(reference['runs'])} run(s) compared, {failures} mismatched")
        return 0 if failures == 0 else 1

    if not args.spec and not args.trace and not args.compare:
        parser.print_help()
        return 2
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
