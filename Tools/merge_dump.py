#!/usr/bin/env python3
"""Join several dumps of the same plan back into one.

A dump is a single-threaded export, so the way to finish faster is to run several viewers over disjoint slices of the
same plan - `--dump-models-skip N --dump-models-count M` - each into its own folder, then join them here. The slices
must come from the same plan (same manifest, same flags), which is what makes the union a complete dump:

    viewer.exe --dump-models C:/dump/a --dump-models-count 600 --packs --thumbs
    viewer.exe --dump-models C:/dump/b --dump-models-skip 600 --dump-models-count 600 --packs --thumbs
    python Tools/merge_dump.py --into C:/dump --from C:/dump/a C:/dump/b

Models are keyed by their index entry, so a model written twice (a resumed or overlapping slice) is kept once. Files
that two slices both wrote are compared by size and only kept when they agree; the shared texture folders are merged
the same way, which is safe because the PNGs are deterministic for a given map. The index is rewritten with its totals
recalculated, so `check_dump.py` and `dump_report.py` see one dump rather than a pile of slices.

Exit code 1 when a slice is missing its index, when two slices disagree about a file, or when the models that arrived
are fewer than the plan the slices report.
"""

import argparse
import datetime
import json
import os
import shutil
import sys

SCHEMA = "umaviewer-models/1"


def load_index(root):
    path = os.path.join(root, "index.json")
    with open(path, encoding="utf-8") as handle:
        index = json.load(handle)
    if index.get("schema") != SCHEMA:
        sys.exit(f"{path}: unexpected schema {index.get('schema')}")
    return index


def copy_file(source, target, problems):
    """Copy unless the target is already there, in which case the two must agree."""
    if not os.path.isfile(source):
        problems.append(f"missing file {source}")
        return
    if os.path.isfile(target):
        if os.path.getsize(source) != os.path.getsize(target):
            problems.append(f"two slices disagree about {target}")
        return
    os.makedirs(os.path.dirname(target), exist_ok=True)
    shutil.copy2(source, target)


def merge(into, slices):
    problems = []
    entries = {}
    counted = 0
    for slice_root in slices:
        index = load_index(slice_root)
        counted = max(counted, index.get("counted") or 0)
        for entry in index.get("models") or []:
            key = entry.get("model") or ""
            if not key:
                problems.append(f"{slice_root}: an entry has no model")
                continue
            entries.setdefault(key, entry)
            for field in ("model", "pack", "thumb"):
                relative = entry.get(field) or ""
                if relative:
                    copy_file(os.path.join(slice_root, relative.replace("/", os.sep)),
                              os.path.join(into, relative.replace("/", os.sep)), problems)
            for kind in ("characters", "props"):
                textures = os.path.join(slice_root, kind, "Texture2D")
                if not os.path.isdir(textures):
                    continue
                for name in entry.get("maps") or []:
                    copy_file(os.path.join(textures, name),
                              os.path.join(into, kind, "Texture2D", name), problems)
    return entries, counted, problems


def totals(into, entries):
    texture_files = 0
    texture_bytes = 0
    for kind in ("characters", "props"):
        folder = os.path.join(into, kind, "Texture2D")
        if not os.path.isdir(folder):
            continue
        for name in os.listdir(folder):
            if not name.lower().endswith(".png"):
                continue
            texture_files += 1
            texture_bytes += os.path.getsize(os.path.join(folder, name))
    models = list(entries.values())
    return {
        "written": sum(1 for entry in models if not entry.get("failure")),
        "failed": sum(1 for entry in models if entry.get("failure")),
        "bytes": sum(entry.get("bytes") or 0 for entry in models),
        "textureFiles": texture_files,
        "textureBytes": texture_bytes,
        "packs": sum(1 for entry in models if entry.get("pack")),
        "thumbs": sum(1 for entry in models if entry.get("thumb")),
    }


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--into", required=True, help="the dump folder to build: the union of the slices")
    ap.add_argument("--from", nargs="+", required=True, dest="slices", help="the slice folders, each with an index.json")
    ap.add_argument("--quiet", action="store_true")
    args = ap.parse_args()

    into = os.path.abspath(args.into)
    slices = [os.path.abspath(path) for path in args.slices]
    for slice_root in slices:
        if not os.path.isfile(os.path.join(slice_root, "index.json")):
            sys.exit(f"no index.json in {slice_root}")

    os.makedirs(into, exist_ok=True)
    entries, counted, problems = merge(into, slices)

    index = {
        "schema": SCHEMA,
        "generated": datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%S.%fZ"),
        "root": os.path.basename(into.rstrip("\\/")),
        "counted": max(counted, len(entries)),
    }
    index.update(totals(into, entries))
    index["models"] = sorted(entries.values(),
                             key=lambda entry: (entry.get("kind") or "", entry.get("name") or ""))
    with open(os.path.join(into, "index.json"), "w", encoding="utf-8") as handle:
        json.dump(index, handle, indent=4, ensure_ascii=False)
        handle.write("\n")

    if not args.quiet or problems:
        print(f"{len(slices)} slice(s) -> {into}: {len(entries)} model(s), "
              f"{index['bytes'] / (1024 * 1024):.0f} MB of models, {index['packs']} pack(s), "
              f"{index['thumbs']} preview(s), {index['textureFiles']} map(s)")
    for problem in problems[:20]:
        print(f"   {problem}", file=sys.stderr)
    if len(problems) > 20:
        print(f"   ... {len(problems) - 20} more", file=sys.stderr)
    if index["failed"]:
        print(f"   {index['failed']} model(s) failed in the slices", file=sys.stderr)
    return 1 if problems or index["failed"] else 0


if __name__ == "__main__":
    sys.exit(main())
