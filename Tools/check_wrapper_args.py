#!/usr/bin/env python3
"""Check what the PowerShell wrapper actually passes to the runner.

`check_cli_parity.py` compares the two flag lists, which catches a flag that exists on one side and not the other.
It cannot catch a wrapper switch that is accepted and then not forwarded - the switch is declared, the run says
OK, and the run did something else. That is not hypothetical: `-DumpModels ... -DumpPropsFilter rich_prop` was
accepted, ignored, and dumped the first four props of the whole install; three runs over three families produced
the same four models, and each said OK.

So this drives the wrapper for real with `-DryRun`, which prints the argument vector it would hand to Unity
without launching anything, and asserts on it. Exit code 1 on any mismatch.

    python Tools/check_wrapper_args.py
    python Tools/check_wrapper_args.py --verbose
"""

import argparse
import pathlib
import subprocess
import sys

WRAPPER = pathlib.Path(__file__).resolve().parent / "headless_export.ps1"

# (label, wrapper switches, flags that must appear, values that must follow a flag, flags that must not appear)
CASES = [
    (
        "a model dump with a props filter",
        ["-DumpModels", "C:/tmp/cli/probe", "-DumpModelsProps", "-DumpPropsFilter", "rich_prop",
         "-DumpModelsCount", "4", "-Packs", "-Thumbs"],
        ["-umaDumpModels", "-umaDumpModelsProps", "-umaPacks", "-umaThumbs", "-umaDumpModelsCount"],
        {"-umaDumpPropsFilter": "rich_prop", "-umaDumpModelsCount": "4", "-umaDumpModels": "C:/tmp/cli/probe"},
        [],
    ),
    (
        "the older prop dump keeps its own filter",
        ["-DumpProps", "C:/tmp/cli/probe", "-DumpPropsFilter", "toon_prop", "-DumpPropsCount", "3"],
        ["-umaDumpProps", "-umaDumpPropsFilter", "-umaDumpPropsCount"],
        {"-umaDumpPropsFilter": "toon_prop", "-umaDumpPropsCount": "3"},
        [],
    ),
    (
        "a character export with a screenshot",
        ["-Char", "1001", "-Costume", "00", "-Out", "C:/tmp/cli/probe/x.pmx",
         "-Screenshot", "C:/tmp/cli/probe/x.png", "-ShotTransparent", "-ShotView", "face"],
        ["-umaChar", "-umaCostume", "-umaScreenshot", "-umaShotTransparent"],
        {"-umaChar": "1001", "-umaCostume": "00", "-umaShotView": "face"},
        [],
    ),
    (
        "listing costs no boot",
        ["-ListFamilies", "-ListFamiliesFilter", "prop"],
        ["-umaListFamilies"],
        {},
        ["-umaChar"],
    ),
    (
        "help is asked for directly",
        ["-Help"],
        ["--help"],
        {},
        ["-umaDumpModels"],
    ),
]


def dry_run(switches):
    """The argument vector the wrapper would pass, as a list of tokens."""
    command = ["pwsh", "-NoProfile", "-File", str(WRAPPER), "-DryRun"] + switches
    result = subprocess.run(command, capture_output=True, text=True, timeout=300)
    if result.returncode != 0:
        raise SystemExit(f"  FAIL the wrapper exited {result.returncode}: {result.stderr.strip()[:400]}")
    return [line.strip() for line in result.stdout.splitlines() if line.strip()]


def main(argv):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--verbose", action="store_true", help="print the argument vector of every case")
    args = parser.parse_args(argv)

    problems = 0
    for label, switches, expected, values, forbidden in CASES:
        try:
            passed = dry_run(switches)
        except SystemExit as error:
            print(f"  {label}: {error}")
            problems += 1
            continue

        # a flag named only in `values` is a required flag too - listing it under "and its value must be this"
        # once let a switch that was never forwarded count as fine, which is exactly the bug this file is for
        required = list(expected) + [flag for flag in values if flag not in expected]
        missing = [flag for flag in required if flag not in passed]
        extra = [flag for flag in forbidden if flag in passed]
        wrong = {
            flag: (want, passed[passed.index(flag) + 1] if passed.index(flag) + 1 < len(passed) else None)
            for flag, want in values.items()
            if flag in passed and (passed.index(flag) + 1 >= len(passed) or passed[passed.index(flag) + 1] != want)
        }
        # the value of a switch must be exactly the next token, never empty - an empty one swallows the flag after it
        empty = [flag for flag in passed if flag.startswith("-uma") and flag in values
                 and passed.index(flag) + 1 < len(passed) and not passed[passed.index(flag) + 1]]

        if args.verbose:
            print(f"  {label}: {' '.join(passed)}")

        if missing or extra or wrong or empty:
            problems += 1
            print(f"  {label}:")
            for flag in missing:
                print(f"    missing {flag}")
            for flag in extra:
                print(f"    unexpected {flag}")
            for flag, (want, got) in wrong.items():
                print(f"    {flag} is {got!r}, expected {want!r}")
            for flag in empty:
                print(f"    {flag} has an empty value")
        else:
            print(f"  ok   {label}")

    if problems:
        print(f"\n  FAIL {problems} of {len(CASES)} case(s)")
        return 1
    print(f"\nthe wrapper forwards what it is given ({len(CASES)} case(s))")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
