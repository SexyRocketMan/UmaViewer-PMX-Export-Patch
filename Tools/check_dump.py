#!/usr/bin/env python3
"""Check that a `dump models` dump is complete and self-consistent.

A dump is what a web service or a pack ships, and every way it can be wrong is quiet: a model whose file is
missing, a PMX that names a texture nobody wrote, an index that disagrees with the folder, a truncated file. This
reads the result instead of trusting the run.

    python Tools/check_dump.py C:/dump
    python Tools/check_dump.py C:/dump --packs --thumbs --quiet

It checks that a dump agrees with itself - presence, bytes, packs and previews that contain what they should - but
cannot judge whether the values are plausible: a model recorded as 0 x 0 x 0 cm passes here and is caught by
Tools/dump_report.py. Exit code 1 when anything is wrong, so it can gate a deploy.
"""

import argparse
import json
import os
import sys
import zipfile


def fail(problems, message):
    problems.append(message)


def check(root, want_packs, want_thumbs, quiet):
    index_path = os.path.join(root, "index.json")
    if not os.path.isfile(index_path):
        print(f"no index.json in {root}", file=sys.stderr)
        return 1

    with open(index_path, encoding="utf-8") as handle:
        index = json.load(handle)

    models = index.get("models") or []
    problems = []
    reported = 0
    previewless = set()

    print(f"{root}")
    print(f"  schema {index.get('schema')}  generated {index.get('generated')}")
    print(f"  {len(models)} model(s): "
          f"{sum(1 for m in models if not m.get('failure'))} written, "
          f"{sum(1 for m in models if m.get('failure'))} failed")

    # `counted` is how many models the run that finished meant to write, so it says whether the dump is complete -
    # 900 listed of 1377 planned is an interrupted dump. More listed than counted is not a problem: that is a later
    # run over a subset (`--dump-models-count 1` on an older dump), and the index keeps every model it has.
    if index.get("counted") and index["counted"] > len(models):
        fail(problems, f"the run planned {index['counted']} model(s), the index lists {len(models)}: incomplete")

    kinds = {}
    maps_referenced = set()
    previews = []
    total_bytes = 0

    for model in models:
        name = model.get("name") or model.get("model") or "?"
        kind = model.get("kind") or "?"
        kinds[kind] = kinds.get(kind, 0) + 1

        if model.get("failure"):
            fail(problems, f"{name}: the dump recorded a failure: {model['failure']}")
            continue

        relative = model.get("model") or ""
        path = os.path.join(root, relative.replace("/", os.sep))
        if not os.path.isfile(path):
            fail(problems, f"{name}: no file at {relative}")
            continue

        size = os.path.getsize(path)
        total_bytes += size
        if size == 0:
            fail(problems, f"{name}: {relative} is empty")
        if model.get("bytes") and abs(model["bytes"] - size) > 0:
            fail(problems, f"{name}: index says {model['bytes']} bytes, file is {size}")

        texture_folder = model.get("textures") or ""
        maps = model.get("maps") or []
        # a marker prop has no vertices: it names no textures and nothing can be photographed, so both are expected
        flat = (model.get("size") or "").replace(" ", "") in ("", "0,0,0", "0.000,0.000,0.000")
        if not maps and not flat:
            # a marker with no geometry names no textures either, and that is not a defect
            fail(problems, f"{name}: names no textures at all")
        if texture_folder:
            for map_name in maps:
                maps_referenced.add(f"{texture_folder}/{map_name}")
                target = os.path.join(root, texture_folder.replace("/", os.sep), map_name)
                if not os.path.isfile(target):
                    fail(problems, f"{name}: missing texture {texture_folder}/{map_name}")
                elif os.path.getsize(target) == 0:
                    # an interrupted copy leaves a name with no bytes, and the model then loads without that map -
                    # which looks like a material bug rather than a broken dump
                    fail(problems, f"{name}: texture {texture_folder}/{map_name} is empty")

        if want_thumbs:
            thumb = model.get("thumb") or ""
            if not thumb:
                if not flat:
                    # a handful of props have nothing a camera can catch - an additive-effect banner leaves the alpha
                    # channel empty, and a marker has no geometry - so they are counted and reported rather than
                    # failing one at a time; a real regression takes out more than a percent
                    previewless.add(name)
            else:
                thumb_path = os.path.join(root, thumb.replace("/", os.sep))
                if not os.path.isfile(thumb_path):
                    fail(problems, f"{name}: no preview at {thumb}")
                elif os.path.getsize(thumb_path) == 0:
                    fail(problems, f"{name}: {thumb} is empty")
                elif not quiet:
                    previews.append(thumb_path)

        if want_packs:
            pack = model.get("pack") or ""
            if not pack:
                fail(problems, f"{name}: no pack recorded")
                continue
            pack_path = os.path.join(root, pack.replace("/", os.sep))
            if not os.path.isfile(pack_path):
                fail(problems, f"{name}: no pack at {pack}")
                continue
            try:
                with zipfile.ZipFile(pack_path) as archive:
                    names = set(archive.namelist())
                    model_inside = os.path.basename(relative)
                    if model_inside not in names:
                        fail(problems, f"{name}: {pack} does not contain {model_inside}")
                    missing = [m for m in maps if f"Texture2D/{m}" not in names]
                    if missing:
                        fail(problems, f"{name}: {pack} is missing {len(missing)} of its {len(maps)} map(s), "
                                        f"e.g. {missing[0]}")
            except zipfile.BadZipFile:
                fail(problems, f"{name}: {pack} is not a readable zip")

    for kind, count in sorted(kinds.items()):
        models_of_kind = [m for m in models if (m.get("kind") or "?") == kind and not m.get("failure")]
        size = sum(m.get("bytes") or 0 for m in models_of_kind)
        folders = {m.get("textures") for m in models_of_kind if m.get("textures")}
        texture_files = set()
        for folder in folders:
            absolute = os.path.join(root, folder.replace("/", os.sep))
            if os.path.isdir(absolute):
                texture_files.update(f"{folder}/{entry}" for entry in os.listdir(absolute))
        print(f"  {kind:<10} {count:>5} model(s), {size / (1024 * 1024):8.1f} MB, "
              f"{len(texture_files):5} texture file(s) in {', '.join(sorted(folders)) or 'no folder'}")

        # a texture folder that no model references is either shared with another kind or left over
        unused = texture_files - maps_referenced
        if unused and not quiet:
            print(f"             {len(unused)} texture file(s) not named by any model "
                  f"(shared, or left from an earlier dump)")

    packs = index.get("packs") or 0
    thumbs = index.get("thumbs") or 0
    print(f"  {total_bytes / (1024 * 1024):.1f} MB of models, {packs} pack(s), {thumbs} preview(s)")
    if previews:
        # a preview can exist and still be blank - a camera pointing at nothing, or a model that did not load,
        # writes a valid PNG - so a sample is opened and asked how much of the square the model *spans*. What is
        # drawn is not the same as how much is covered: a thin prop (a sheet two centimetres thick) fills the frame
        # across its length and still covers a few percent of the pixels, and that is a perfectly good picture.
        try:
            from PIL import Image
        except ImportError:
            Image = None

        blank = []
        for path in previews:
            if Image is None:
                if os.path.getsize(path) < 512:
                    blank.append(f"{os.path.basename(path)} (only {os.path.getsize(path)} bytes)")
                continue
            try:
                with Image.open(path) as image:
                    alpha = image.convert("RGBA").getchannel("A")
                    width, height = alpha.size
                    drawn = alpha.getbbox()
                    extent = 0.0
                    if drawn is not None:
                        extent = max(drawn[2] - drawn[0], drawn[3] - drawn[1]) / float(max(width, height))
                    if width != height:
                        blank.append(f"{os.path.basename(path)} is {width}x{height}, not square")
                    elif extent < 0.25:
                        blank.append(f"{os.path.basename(path)} (spans {extent:.0%} of the frame)")
            except Exception as error:                     # a preview that will not open is a problem too
                blank.append(f"{os.path.basename(path)} ({type(error).__name__}: {error})")
        if blank:
            fail(problems, f"{len(blank)} preview(s) are not pictures of anything, e.g. {blank[0]}")

    print(f"  {sum(1 for m in models if m.get('thumb'))} of {len(models)} model(s) have a preview")
    if previewless:
        share = len(previewless) / float(len(models) or 1)
        print(f"  {len(previewless)} model(s) have no preview ({share:.1%}): "
              + ", ".join(sorted(previewless)[:6])
              + (" ..." if len(previewless) > 6 else ""))
        if share > 0.01:
            fail(problems, f"{len(previewless)} model(s) have no preview, more than the 1% a few effect props explain")

    if problems:
        print(f"\n{len(problems)} problem(s):")
        for problem in problems[:reported or 40]:
            print(f"  {problem}")
        if len(problems) > 40:
            print(f"  ... and {len(problems) - 40} more")
        return 1

    print("\nthe dump agrees with its index")
    return 0


def main(argv):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("root", help="the folder --dump-models wrote")
    parser.add_argument("--packs", action="store_true", help="also check the per-model zips")
    parser.add_argument("--thumbs", action="store_true", help="also check the per-model previews")
    parser.add_argument("--quiet", action="store_true", help="do not mention unreferenced textures")
    args = parser.parse_args(argv)
    return check(args.root, args.packs, args.thumbs, args.quiet)


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
