#!/usr/bin/env python3
"""Summarise a `dump models` dump: what it holds, how big it is, and whether it looks right.

`check_dump.py` answers "is this dump self-consistent" - every model present, every map on disk, every pack containing
what it should. This answers the questions that come after a long run, where the failures are quieter:

  * **the shape of it** - models per kind and per prop family, bytes per kind, so 400 costumes and 1377 props can be
    seen at a glance rather than inferred from a folder listing;
  * **sizes** - the box of each model's vertices, in centimetres, with the range per kind. A character that is 20 m
    tall or 2 cm tall is a bug, and at 1777 models nobody will notice it by looking;
  * **costume names** - how many characters have one. They come from the game's data, so a missing name means the
    dump could not read it, not that the costume has none;
  * **preview framing** - the share of each preview the model actually covers, sampled. `dump models --thumbs`
    converges on about 84% by measuring rendered pixels, so anything near 0 is an empty picture and anything at 100 is
    a cropped one.

    python Tools/dump_report.py C:/tmp/dump/full
    python Tools/dump_report.py C:/tmp/dump/full --sample 60

Pillow is used for the preview numbers when it is installed and those lines are skipped when it is not.
"""

import argparse
import collections
import json
import os
import random
import sys

try:
    from PIL import Image
    HAVE_PIL = True
except ImportError:                     # the preview section is skipped rather than the report failing
    HAVE_PIL = False


def size_of(root, relative):
    if not relative:
        return 0
    path = os.path.join(root, relative.replace("/", os.sep))
    return os.path.getsize(path) if os.path.isfile(path) else 0


def centimetres(size):
    """'20.206,12.709,6.072' -> (162, 102, 49): PMX units are 8 cm each, the convention the rest of the project uses."""
    try:
        parts = [float(part) for part in str(size).split(",")]
    except ValueError:
        return None
    if len(parts) != 3:
        return None
    return tuple(round(part * 0.08 * 100) for part in parts)


def median(values):
    ordered = sorted(values)
    if not ordered:
        return 0
    middle = len(ordered) // 2
    return ordered[middle] if len(ordered) % 2 else (ordered[middle - 1] + ordered[middle]) / 2


def preview_fill(path):
    """How much of a 256 px square the model covers, from the alpha channel."""
    with Image.open(path) as image:
        alpha = image.convert("RGBA").getchannel("A")
        box = alpha.getbbox()
        if box is None:
            return 0.0
        return max(box[2] - box[0], box[3] - box[1]) / float(alpha.size[0])


def main(argv):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("root", help="the folder `dump models` wrote")
    parser.add_argument("--sample", type=int, default=24, help="how many previews to measure (default 24)")
    parser.add_argument("--json", action="store_true", help="print the summary as JSON")
    args = parser.parse_args(argv)

    index_path = os.path.join(args.root, "index.json")
    if not os.path.isfile(index_path):
        print(f"{args.root} has no index.json", file=sys.stderr)
        return 1
    with open(index_path, encoding="utf-8-sig") as handle:
        index = json.load(handle)

    models = [model for model in index.get("models", []) if not model.get("failure")]
    failed = [model for model in index.get("models", []) if model.get("failure")]

    by_kind = collections.Counter(model["kind"] for model in models)
    by_family = collections.Counter(model.get("family") or "?" for model in models if model["kind"] == "prop")
    bytes_by_kind = collections.defaultdict(int)
    for model in models:
        maps = sum(size_of(args.root, f"{model['textures']}/{name}") for name in model.get("maps") or [])
        bytes_by_kind[model["kind"]] += size_of(args.root, model["model"]) + maps

    summary = {
        "root": os.path.abspath(args.root),
        "schema": index.get("schema"),
        "counted": index.get("counted"),
        "models": len(models),
        "failed": len(failed),
        "packs": sum(1 for model in models if model.get("pack")),
        "previews": sum(1 for model in models if model.get("thumb")),
        "bytes": bytes_by_kind,
    }

    print(f"{summary['root']}")
    print(f"  schema {summary['schema']}, planned {summary['counted']}, written {summary['models']}, "
          f"failed {summary['failed']}")
    for kind, count in sorted(by_kind.items()):
        print(f"  {kind:<10} {count:>5} model(s), {bytes_by_kind[kind] / 2**30:6.2f} GB of model and maps")
    if by_family:
        families = ", ".join(f"{name} {count}" for name, count in by_family.most_common())
        print(f"  prop families: {families}")
    print(f"  {summary['packs']} pack(s), {summary['previews']} preview(s)")

    # sizes, per kind: a character much shorter or taller than a person is a measurement bug
    for kind in sorted(by_kind):
        heights = [centimetres(model.get("size")) for model in models
                   if model["kind"] == kind and centimetres(model.get("size"))]
        if not heights:
            print(f"  {kind:<10} no sizes recorded")
            continue
        tall = [height[1] for height in heights]
        shortest = min(tall)
        tallest = max(tall)
        outliers = [model["name"] for model in models if model["kind"] == kind
                    and centimetres(model.get("size"))
                    and (centimetres(model.get("size"))[1] < 1 or centimetres(model.get("size"))[1] > 2000)]
        print(f"  {kind:<10} height: min {shortest} cm, median {median(tall):.0f} cm, max {tallest} cm"
              + (f", {len(outliers)} implausible e.g. {outliers[0]}" if outliers else ""))
        summary.setdefault("height", {})[kind] = {"min": shortest, "median": median(tall), "max": tallest}

    if by_kind.get("character"):
        characters = [model for model in models if model["kind"] == "character"]
        named = sum(1 for model in characters if model.get("costumeName"))
        print(f"  costume names: {named} of {len(characters)} character model(s)"
              + ("" if named == len(characters) else "  <- run `dump models <root> --refresh` to fill the rest"))

    if HAVE_PIL and args.sample > 0:
        previews = [model for model in models if model.get("thumb")]
        chosen = random.Random(1).sample(previews, min(args.sample, len(previews)))
        fills = []
        empty = []
        for model in chosen:
            path = os.path.join(args.root, model["thumb"].replace("/", os.sep))
            try:
                fill = preview_fill(path)
            except OSError:
                continue
            fills.append(fill)
            if fill < 0.2:
                empty.append(model["name"])
        if fills:
            print(f"  previews sampled {len(fills)}: fill min {min(fills) * 100:.0f}%, "
                  f"median {median(fills) * 100:.0f}%, max {max(fills) * 100:.0f}%"
                  + (f", {len(empty)} nearly empty e.g. {empty[0]}" if empty else ""))
            summary["previewFill"] = {"min": min(fills), "median": median(fills), "max": max(fills),
                                      "empty": empty}
    elif not HAVE_PIL:
        print("  (Pillow is not installed, so the previews were not measured)")

    if failed:
        print(f"\n  {len(failed)} failure(s):")
        for model in failed[:10]:
            print(f"    {model['name']}: {model['failure']}")
        if len(failed) > 10:
            print(f"    ... and {len(failed) - 10} more")
        summary["failures"] = [{"name": model["name"], "failure": model["failure"]} for model in failed]

    if args.json:
        print(json.dumps(summary, indent=2))
    return 1 if failed else 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
