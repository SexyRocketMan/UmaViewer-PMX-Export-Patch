#!/usr/bin/env python3
"""Report how far a model dump has got, and what it is costing.

A dump writes its index after every model, so the index is the honest progress record - no log parsing, no guessing
from file counts. This prints how many of the planned models are written, the rate and an estimate of what is left:

    python Tools/dump_status.py C:/dump
    python Tools/dump_status.py C:/dump --follow

`--follow` re-reads twice a second and redraws one line, which is what to run beside a dump that takes half an hour.
Exit code 1 when the dump has failures, so it can end a watch.
"""

import argparse
import json
import os
import sys
import time

SCHEMA = "umaviewer-models/1"


def read(root):
    path = os.path.join(root, "index.json")
    if not os.path.isfile(path):
        return None, f"no index.json in {root}"
    try:
        with open(path, encoding="utf-8") as handle:
            return json.load(handle), None
    except (OSError, json.JSONDecodeError) as error:
        return None, f"{path} is not readable yet ({type(error).__name__})"


def moment(root, entry):
    """When a model was written, from the file it named - the index carries no timestamp per model."""
    for key in ("model", "pack", "thumb"):
        relative = entry.get(key) or ""
        if not relative:
            continue
        path = os.path.join(root, relative.replace("/", os.sep))
        try:
            return os.path.getmtime(path)
        except OSError:
            continue
    return None


def describe(root, index):
    models = index.get("models") or []
    counted = index.get("counted") or 0
    written = index.get("written") if index.get("written") is not None else len(models)
    failed = index.get("failed") or 0
    # an index written by an older runner, or one about to be replaced, may not carry the plan size yet
    share = f" ({100.0 * written / counted:.1f}%)" if counted else " (plan size not in the index yet)"
    of = f"/{counted}" if counted else ""

    stamps = sorted(ts for ts in (moment(root, entry) for entry in models) if ts)
    rate = ""
    if len(stamps) >= 2:
        span = stamps[-1] - stamps[0]
        if span > 0:
            per = span / (len(stamps) - 1)
            left = max(0, counted - written) * per if counted else 0
            rate = (f"  {per:.1f} s each, {left / 60:.0f} min left"
                    if left > 0 else f"  {per:.1f} s each")

    line = (f"{written}{of} written{share}"
            + (f", {failed} failed" if failed else "")
            + f", {index.get('bytes', 0) / (1024 * 1024):.0f} MB of models"
            + rate)
    return line, failed


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("root", help="the dump folder holding index.json")
    ap.add_argument("--follow", action="store_true", help="watch until the dump stops changing")
    args = ap.parse_args()

    root = os.path.abspath(args.root)
    if not args.follow:
        index, failure = read(root)
        if failure:
            print(failure, file=sys.stderr)
            return 1
        if index.get("schema") != SCHEMA:
            print(f"unexpected schema {index.get('schema')}", file=sys.stderr)
            return 1
        line, failed = describe(root, index)
        print(f"{index.get('root') or root}: {line}")
        return 1 if failed else 0

    last = None
    still = 0
    while True:
        index, failure = read(root)
        if failure:
            sys.stdout.write(f"\r{failure}")
            sys.stdout.flush()
            time.sleep(0.5)
            continue
        line, failed = describe(root, index)
        if line != last:
            sys.stdout.write(f"\r{line}")
            sys.stdout.flush()
            last = line
            still = 0
        else:
            still += 1
            if still > 600:                     # five minutes without a new model: the dump is over or stuck
                print()
                return 1 if failed else 0
        time.sleep(0.5)


if __name__ == "__main__":
    sys.exit(main())
