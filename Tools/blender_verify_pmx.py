"""Verify a PMX export the way a Blender + mmd_tools + uma_addon artist uses it.

Run headlessly:

  blender --background --factory-startup --python Tools/blender_verify_pmx.py -- \
      --mode blender --json report.json path/to/model.pmx

What it checks per file:

  1. morph naming contract - the eth four eye-range morphs the uma_addon "Refine Structure"
     operator looks up by exact name ("Eye_20_R(XRange)[M_Face]", ...) survive the export.
     When they do not, the operator deletes the Eye_L/Eye_R vertex groups but never builds the
     replacement eye controls, so the eye bones stop moving the mesh.
  2. raw import - rotating the eye bone deforms the eyeball.
  3. after "Refine Structure" - the eight eye control shape keys exist and rotating the eye bone
     still deforms the mesh (through the shape keys + drivers the operator installs).

Exit code is 0 when every enabled assertion passes, 1 otherwise. The last stdout line is
"@@RESULT@@ <json>" so wrappers can parse the outcome.
"""

import addon_utils
import argparse
import json
import sys

import bpy

MMD_TOOLS = "bl_ext.blender_org.mmd_tools"
UMA_ADDON = "bl_ext.user_default.uma_addon"

EYE_CONTROL_KEYS = ["Eye_L(L)", "Eye_L(R)", "Eye_L(U)", "Eye_L(D)",
                    "Eye_R(L)", "Eye_R(R)", "Eye_R(U)", "Eye_R(D)"]

TAGGED_EYE_MORPHS = ["Eye_20_R(XRange)[M_Face]", "Eye_20_L(XRange)[M_Face]",
                     "Eye_21_R(YRange)[M_Face]", "Eye_21_L(YRange)[M_Face]"]
SHORT_EYE_MORPHS = ["Eye_20_R", "Eye_20_L", "Eye_21_R", "Eye_21_L"]

# vertex groups mmd_tools always adds; they intentionally have no matching bone
KNOWN_ORPHAN_GROUPS = {"mmd_edge_scale", "mmd_vertex_order", "mmd_uv1", "mmd_uv2"}


def enable_addons(need_uma):
    names = [MMD_TOOLS] + ([UMA_ADDON] if need_uma else [])
    for name in names:
        try:
            addon_utils.enable(name, default_set=True)
        except Exception as exc:  # noqa: BLE001
            print(f"  !! could not enable {name}: {exc}")


def reset_scene(need_uma):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    enable_addons(need_uma)


def import_pmx(path):
    bpy.ops.mmd_tools.import_model(
        filepath=path,
        types={"MESH", "ARMATURE", "MORPHS", "DISPLAY"},
        scale=0.08,
        clean_model=False,
        rename_bones=False,
        fix_bone_order=True,
        apply_bone_fixed_axis=False,
    )


def eval_coords(obj):
    depsgraph = bpy.context.evaluated_depsgraph_get()
    evaluated = obj.evaluated_get(depsgraph)
    mesh = evaluated.to_mesh()
    coords = [v.co.copy() for v in mesh.vertices]
    evaluated.to_mesh_clear()
    return coords


def rotation_test(arm, bone_name, angle=0.6):
    """Rotate a pose bone and report how many vertices moved, per mesh."""
    if bone_name not in arm.pose.bones:
        return None
    results = {}
    for obj in [o for o in bpy.data.objects if o.type == "MESH"]:
        before = eval_coords(obj)
        pose_bone = arm.pose.bones[bone_name]
        pose_bone.rotation_mode = "XYZ"
        previous = tuple(pose_bone.rotation_euler)
        pose_bone.rotation_euler = (0.0, 0.0, angle)
        bpy.context.view_layer.update()
        after = eval_coords(obj)
        moved = sum(1 for a, b in zip(before, after) if (a - b).length > 1e-5)
        max_distance = max(((a - b).length for a, b in zip(before, after)), default=0.0)
        pose_bone.rotation_euler = previous
        bpy.context.view_layer.update()
        results[obj.name] = {"moved": moved, "max_cm": round(max_distance * 100, 4)}
    return results


def scene_facts():
    armatures = [o for o in bpy.data.objects if o.type == "ARMATURE"]
    meshes = [o for o in bpy.data.objects if o.type == "MESH"]
    facts = {"armature": armatures[0].name if armatures else None, "meshes": {}}
    for obj in meshes:
        shape_keys = list(obj.data.shape_keys.key_blocks.keys()) if obj.data.shape_keys else []
        groups = {g.name for g in obj.vertex_groups}
        bone_names = {b.name for b in armatures[0].data.bones} if armatures else set()
        facts["meshes"][obj.name] = {
            "vertices": len(obj.data.vertices),
            "shape_keys": len(shape_keys),
            "tagged_shape_keys": sum(1 for k in shape_keys if "(" in k and ")" in k and "[" in k),
            "eye_control_keys": [k for k in shape_keys if k in EYE_CONTROL_KEYS],
            "has_eye_vertex_groups": sorted(groups & {"Eye_L", "Eye_R"}),
            "orphan_vertex_groups": sorted(groups - bone_names - KNOWN_ORPHAN_GROUPS),
            "all_shape_keys": shape_keys,
        }
    return facts


def verify(path, args):
    result = {"file": path, "mode": args.mode, "failures": []}
    reset_scene(need_uma=True)
    print(f"== {path}")
    import_pmx(path)

    armatures = [o for o in bpy.data.objects if o.type == "ARMATURE"]
    if not armatures:
        result["failures"].append("no armature imported")
        return result
    arm = armatures[0]

    raw = scene_facts()
    result["raw"] = raw
    mesh_name = next(iter(raw["meshes"]))
    keys = set(raw["meshes"][mesh_name]["all_shape_keys"])

    tagged_present = [k for k in TAGGED_EYE_MORPHS if k in keys]
    short_present = [k for k in SHORT_EYE_MORPHS if k in keys]
    result["tagged_eye_morphs"] = tagged_present
    result["short_eye_morphs"] = short_present
    print(f"   eye-range morphs: tagged={len(tagged_present)}/{len(TAGGED_EYE_MORPHS)} short={short_present}")

    expects_tagged = args.mode in ("blender", "both")
    if expects_tagged and len(tagged_present) != len(TAGGED_EYE_MORPHS):
        result["failures"].append(
            f"uma_addon eye-range morphs missing (found {tagged_present}, need {TAGGED_EYE_MORPHS}) -> "
            f"Refine Structure will not build the eye controls")
    if args.mode == "short" and tagged_present:
        result["failures"].append(f"expected short morph names only, but tagged morphs are present: {tagged_present}")

    result["raw_rotation"] = rotation_test(arm, args.bone)
    raw_moved = sum(v["moved"] for v in (result["raw_rotation"] or {}).values())
    print(f"   raw import: rotating {args.bone} moves {raw_moved} verts")
    if raw_moved == 0:
        result["failures"].append(f"rotating {args.bone} moves nothing on a raw import")

    if args.skip_refine:
        return result

    # --- uma_addon Refine Structure
    for obj in bpy.data.objects:
        obj.select_set(False)
    arm.select_set(True)
    bpy.context.view_layer.objects.active = arm
    try:
        status = bpy.ops.uma.refine_bone_structure()
        print(f"   refine_bone_structure -> {status}")
    except Exception as exc:  # noqa: BLE001
        result["failures"].append(f"refine_bone_structure raised {type(exc).__name__}: {exc}")
        return result

    refined = scene_facts()
    result["refined"] = refined
    refined_mesh = next(iter(refined["meshes"]))
    control_keys = refined["meshes"][refined_mesh]["eye_control_keys"]
    result["refined_eye_controls"] = control_keys
    print(f"   after refine: eye control keys={len(control_keys)}/{len(EYE_CONTROL_KEYS)} "
          f"eye vertex groups={refined['meshes'][refined_mesh]['has_eye_vertex_groups']}")

    if expects_tagged:
        missing = [k for k in EYE_CONTROL_KEYS if k not in control_keys]
        if missing:
            result["failures"].append(f"Refine Structure did not build eye controls: missing {missing}")

    result["refined_rotation"] = rotation_test(arm, args.bone)
    refined_moved = sum(v["moved"] for v in (result["refined_rotation"] or {}).values())
    result["refined_moved"] = refined_moved
    print(f"   after refine: rotating {args.bone} moves {refined_moved} verts")

    if expects_tagged and refined_moved == 0:
        result["failures"].append(
            f"after Refine Structure the eye bone {args.bone} no longer deforms the mesh "
            f"(its vertex groups are folded into Head and no eye controls were built)")
    return result


def main():
    argv = sys.argv
    argv = argv[argv.index("--") + 1:] if "--" in argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("files", nargs="+")
    parser.add_argument("--mode", choices=["blender", "short", "both"], default="blender")
    parser.add_argument("--bone", default="Eye_L")
    parser.add_argument("--json")
    parser.add_argument("--skip-refine", action="store_true")
    args = parser.parse_args(argv)

    results = [verify(path, args) for path in args.files]
    failed = [r for r in results if r["failures"]]

    for r in failed:
        print(f"FAIL {r['file']}")
        for f in r["failures"]:
            print(f"     - {f}")
    for r in results:
        if not r["failures"]:
            print(f"PASS {r['file']}")

    payload = {"mode": args.mode,
               "results": [{k: v for k, v in r.items() if k != "raw" and k != "refined"} | {
                   "raw_vertices": r.get("raw", {}).get("meshes", {}),
                   "refined_vertices": r.get("refined", {}).get("meshes", {}),
               } for r in results],
               "passed": not failed}
    if args.json:
        with open(args.json, "w", encoding="utf-8") as handle:
            json.dump(payload, handle, indent=1, ensure_ascii=False)
    print("@@RESULT@@ " + json.dumps({"passed": not failed, "failed_files": [r["file"] for r in failed]}))
    sys.exit(1 if failed else 0)


main()
