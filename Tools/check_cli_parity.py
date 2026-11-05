#!/usr/bin/env python3
"""Check that the PowerShell wrapper can pass every flag the CLI knows.

The CLI lives in `Assets/Scripts/Cli/UmaHeadlessExport.cs` as a table of flags, and `Tools/headless_export.ps1`
maps its own `-Style` switches onto those `-uma*` names. The two drift: a flag added to the runner and not to the
wrapper is invisible to anyone using the wrapper, and a wrapper switch that maps to nothing fails at run time with
"nothing to do". A parameter the wrapper declares but never forwards is worse than a missing one, because the run
looks like it worked. This reads both and reports the difference; exit code 1 when they disagree.

    python Tools/check_cli_parity.py            # report
    python Tools/check_cli_parity.py --verbose  # also list the flags that do match
"""

import argparse
import re
import sys

RUNNER = r"Assets/Scripts/Cli/UmaHeadlessExport.cs"
WRAPPER = r"Tools/headless_export.ps1"

# The runner's table entry looks like:  new Flag { Long = "--char", Internal = "-umaChar", ...
FLAG = re.compile(r'new Flag\s*\{\s*Long\s*=\s*"(--[^"]+)"(?:\s*,\s*Internal\s*=\s*"(-uma\w*)")?')


def runner_flags(path):
    """{long name: internal name or None} for every row of the CLI's flag table."""
    source = open(path, encoding="utf-8").read()
    table = source[source.index("private static readonly Flag[] Flags"):]
    table = table[: table.index("private static readonly Dictionary")]
    flags = {}
    for long_name, internal in FLAG.findall(table):
        flags[long_name] = internal or ""
    # the flags the runner answers itself, which have no -uma* spelling
    for handled in ("--help", "--about", "--json", "--doctor", "--verb"):
        flags.setdefault(handled, "")
    return flags


def wrapper_passes(path):
    """Every -uma* name the wrapper puts on Unity's command line, plus its own switches."""
    source = open(path, encoding="utf-8").read()
    body = source[source.index("$arguments = @("):]
    passed = set(re.findall(r'"(-uma\w+)"', body))
    switches = set(re.findall(r'\[(?:string|int|double|switch)\]\$(\w+)\s*(?:=|,)', source))
    return passed, switches


PARAM = re.compile(r"\[(?:switch|string|int|double|bool)\]\s*\$(\w+)", re.I)


def wrapper_parameters(path):
    """(declared names, body) for the wrapper's param block: what it accepts, and what it actually uses."""
    source = open(path, encoding="utf-8").read()
    start = source.lower().find("param(")
    if start < 0:
        return set(), source
    depth = 0
    end = start
    for index in range(start, len(source)):
        char = source[index]
        if char == "(":
            depth += 1
        elif char == ")":
            depth -= 1
            if depth == 0:
                end = index + 1
                break
    block = source[start:end]
    return set(PARAM.findall(block)), source[end:]


def main(argv):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--verbose", action="store_true", help="list the flags that do match as well")
    args = parser.parse_args(argv)

    flags = runner_flags(RUNNER)
    passed, switches = wrapper_passes(WRAPPER)
    declared, body = wrapper_parameters(WRAPPER)

    internal = {name for name in flags.values() if name}
    unreachable = sorted(name for name in internal if name not in passed)
    orphaned = sorted(name for name in passed if name not in internal)

    print(f"runner:  {len(flags)} flag(s) in the table, {len(internal)} with a -uma* spelling")
    print(f"wrapper: {len(passed)} -uma* name(s) passed, {len(switches)} switch(es), "
          f"{len(declared)} parameter(s) declared")

    ok = True
    if unreachable:
        ok = False
        print(f"\n{len(unreachable)} flag(s) the wrapper cannot pass:")
        for name in unreachable:
            long_name = next((long for long, internal_name in flags.items() if internal_name == name), "?")
            print(f"  {long_name:<24} {name}")
    if orphaned:
        ok = False
        print(f"\n{len(orphaned)} name(s) the wrapper passes that the runner no longer has:")
        for name in orphaned:
            print(f"  {name}")

    ignored = sorted(name for name in declared if f"${name}" not in body)
    if ignored:
        ok = False
        print(f"\n{len(ignored)} wrapper parameter(s) accepted and never forwarded:")
        for name in ignored:
            print(f"  -{name}")

    if args.verbose:
        print("\nmatching:")
        for long_name, internal_name in sorted(flags.items()):
            if internal_name and internal_name in passed:
                print(f"  {long_name:<24} {internal_name}")

    if ok:
        print("\nevery flag the runner knows can be passed through the wrapper")
    return 0 if ok else 1


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
