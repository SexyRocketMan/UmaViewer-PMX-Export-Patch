"""Import a PMX and a VMD with mmd_tools and check that the motion's morph keyframes survive.

mmd_tools matches vmd morph keyframes against the imported model's shape keys **by name**. A name
that exists in only one of the two files is dropped without a warning, which is why an exported
motion used to import as bone keyframes only. This script is the check for that, and it is the
regression test for the Umaviewer/uma_addon naming contract.

  blender --background --factory-startup --python Tools/blender_verify_vmd.py -- \
      --pmx model.pmx --vmd motion.vmd [--json report.json]

Exit code 0 when every morph name in the vmd resolved to a shape key of the model, 1 otherwise.
"""

import addon_utils
import argparse
import json
import os
import re
import sys

import bpy

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from vmd_inspect import read_vmd  # noqa: E402

MMD_TOOLS = "bl_ext.blender_org.mmd_tools"
KEY_BLOCK_RE = re.compile(r'key_blocks\["([^"]+)"\]\.value')


def main():
    argv = sys.argv
    argv = argv[argv.index("--") + 1:] if "--" in argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("--pmx", required=True)
    parser.add_argument("--vmd", required=True)
    parser.add_argument("--scale", type=float, default=0.08)
    parser.add_argument("--json")
    args = parser.parse_args(argv)

    bpy.ops.wm.read_factory_settings(use_empty=True)
    addon_utils.enable(MMD_TOOLS, default_set=True)

    motion = read_vmd(args.vmd)
    vmd_morphs = sorted({k.name for k in motion.morph_keys})
    print(f"== pmx {args.pmx}")
    print(f"== vmd {args.vmd}")
    print(f"   vmd has {len(vmd_morphs)} distinct morph name(s), {len(motion.bone_keys)} bone key(s)")

    bpy.ops.mmd_tools.import_model(
        filepath=args.pmx,
        types={"MESH", "ARMATURE", "MORPHS", "DISPLAY"},
        scale=args.scale,
        clean_model=False,
        rename_bones=False,
        fix_bone_order=True,
        apply_bone_fixed_axis=False,
    )

    meshes = [o for o in bpy.data.objects if o.type == "MESH" and o.data.shape_keys]
    if not meshes:
        print("   !! imported model has no shape keys")
        return 1

    shape_keys = set()
    for obj in meshes:
        shape_keys.update(obj.data.shape_keys.key_blocks.keys())
    print(f"   model has {len(shape_keys)} shape key(s)")

    missing = [name for name in vmd_morphs if name not in shape_keys]
    matched = [name for name in vmd_morphs if name in shape_keys]
    print(f"   morph names resolved: {len(matched)}/{len(vmd_morphs)}")
    if missing:
        print(f"   !! {len(missing)} morph name(s) in the motion do not exist in the model "
              f"(mmd_tools drops these silently): {missing[:10]}")

    bpy.ops.mmd_tools.import_vmd(filepath=args.vmd, scale=args.scale)

    animated = set()
    for obj in meshes:
        keys = obj.data.shape_keys
        if not keys.animation_data or not keys.animation_data.action:
            continue
        for curve in keys.animation_data.action.fcurves:
            found = KEY_BLOCK_RE.search(curve.data_path)
            if found:
                animated.add(found.group(1))
    print(f"   shape keys animated after the vmd import: {len(animated)}")

    missing_curves = [name for name in matched if name not in animated]
    passed = bool(vmd_morphs) and not missing and not missing_curves
    if missing_curves:
        print(f"   !! {len(missing_curves)} matched morph(s) got no animation curve: {missing_curves[:10]}")
    if not vmd_morphs:
        print("   !! the vmd has no morph keyframes at all")
        passed = False

    print("PASS" if passed else "FAIL")
    if args.json:
        with open(args.json, "w", encoding="utf-8") as handle:
            json.dump({"pmx": args.pmx, "vmd": args.vmd, "vmd_morphs": len(vmd_morphs),
                       "resolved": len(matched), "animated": len(animated),
                       "missing": missing[:50], "passed": passed}, handle, indent=1)
    print("@@RESULT@@ " + json.dumps({"passed": passed, "resolved": len(matched), "of": len(vmd_morphs)}))
    return 0 if passed else 1


sys.exit(main())
