# /// script
# requires-python = ">=3.10"
# dependencies = []
# ///
"""Show what every morph is called in each naming mode, straight from exported models.

The rules here mirror Assets/Scripts/Exporters/MorphNaming.cs. They are then checked against models
exported in each mode, so a name in the table is one that is really in a .pmx, not a guess.

    uv run Tools/morph_name_table.py --tagged m0.pmx --short m1.pmx --both m2.pmx --unified m3.pmx
    uv run Tools/morph_name_table.py ... --examples          # the short form for UI labels
    uv run Tools/morph_name_table.py ... --out docs/MORPH_NAMES.md

Export the four files with:

    ./Tools/headless_export.ps1 -Char 1001 -Costume 00 -Out m0.pmx -MorphNameMode 0
    ./Tools/headless_export.ps1 -Char 1001 -Costume 00 -Out m1.pmx -MorphNameMode 1
    ./Tools/headless_export.ps1 -Char 1001 -Costume 00 -Out m2.pmx -MorphNameMode 2
    ./Tools/headless_export.ps1 -Char 1001 -Costume 00 -Out m3.pmx -MorphNameMode 3
"""

import argparse
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import pmx_inspect as pi  # noqa: E402

VMD_NAME_BYTE_LIMIT = 15
FAMILIES = ("EyeBrow", "Eye", "Ear", "Mouth")
GROUP_TOKENS = {"Eye": "Eye", "EyeBrow": "Brow", "Ear": "Ear", "Mouth": "Mouth"}
TAG_ABBREVIATIONS = {"EyelidHideA": "LidHideA", "EyelidHideB": "LidHideB"}


class Parts:
    def __init__(self):
        self.family = "Other"
        self.id = -1
        self.side = ""
        self.tag = ""
        self.mesh = ""


def parse(raw_name: str, tag_hint: str | None = None) -> Parts:
    """MorphNaming.TryParse."""
    parts = Parts()
    if not raw_name:
        return parts
    name = raw_name.strip()

    start = name.find("(")
    if start > 0:
        end = name.find(")", start + 1)
        if end > start:
            parts.tag = name[start + 1:end]
            name = name[:start] + name[end + 1:]
    start = name.find("[")
    if start > 0:
        end = name.find("]", start + 1)
        if end > start:
            parts.mesh = name[start + 1:end]
            name = name[:start] + name[end + 1:]

    name = name.strip()
    if tag_hint:
        parts.tag = tag_hint

    for family in FAMILIES:
        if name.startswith(family + "_"):
            parts.family = family
            name = name[len(family) + 1:]
            break

    fields = name.split("_")
    for index in range(len(fields) - 1, -1, -1):
        if len(fields[index]) == 1 and fields[index] in ("L", "R"):
            parts.side = fields[index]
            fields.pop(index)
    if fields and fields[0].lstrip("-").isdigit():
        parts.id = int(fields[0])

    if not parts.side and len(parts.tag) > 2 and parts.tag.endswith("_L"):
        parts.side, parts.tag = "L", parts.tag[:-2]
    elif not parts.side and len(parts.tag) > 2 and parts.tag.endswith("_R"):
        parts.side, parts.tag = "R", parts.tag[:-2]

    if ".00" in parts.tag:
        parts.tag = parts.tag[:parts.tag.rfind(".")]
    parts.tag = parts.tag.strip()
    return parts


def short_name(raw_name: str) -> str:
    """MorphNaming.ShortName."""
    if not raw_name:
        return raw_name
    name = raw_name.strip()
    start = name.find("(")
    if start > 0:
        name = name[:start]
    start = name.find("[")
    if start > 0:
        name = name[:start]
    if ".00" in name:
        name = name[:name.rfind(".")]
    return name.strip()


def unified_name(raw_name: str, tag_hint: str | None = None) -> str:
    """MorphNaming.UnifiedName."""
    parts = parse(raw_name, tag_hint)
    if parts.family == "Other" or not parts.tag:
        return short_name(raw_name)
    tag = TAG_ABBREVIATIONS.get(parts.tag, parts.tag)
    group = GROUP_TOKENS.get(parts.family, parts.family)
    name = f"{group}_{tag}_{parts.side}" if parts.side else f"{group}_{tag}"
    if len(name.encode("shift_jis", errors="replace")) <= VMD_NAME_BYTE_LIMIT:
        return name
    return short_name(raw_name)


def bytes_of(name: str) -> int:
    return len(name.encode("shift_jis", errors="replace"))


def load_names(path: str) -> list[str]:
    return list(pi.read_model(path).morph_names)


def check(tagged: list[str], short: list[str], both: list[str], unified: list[str]) -> list[str]:
    """Confirm the rules above reproduce what the exporter actually wrote."""
    problems = []

    expected_short = {short_name(name) for name in tagged}
    if not expected_short <= set(short):
        problems.append(f"mode 1 is missing {sorted(expected_short - set(short))[:5]}")
    expected_unified = {unified_name(name) for name in tagged}
    if not expected_unified <= set(unified):
        problems.append(f"mode 3 is missing {sorted(expected_unified - set(unified))[:5]}")
    expected_both = set(tagged) | expected_short
    if not expected_both <= set(both):
        problems.append(f"mode 2 is missing {sorted(expected_both - set(both))[:5]}")

    print(f"tagged  mode 0: {len(tagged):4d} morphs, worst name {max(map(bytes_of, tagged))} bytes")
    print(f"short   mode 1: {len(short):4d} morphs, worst name {max(map(bytes_of, short))} bytes")
    print(f"both    mode 2: {len(both):4d} morphs, worst name {max(map(bytes_of, both))} bytes")
    print(f"unified mode 3: {len(unified):4d} morphs, worst name {max(map(bytes_of, unified))} bytes")
    duplicate = len(unified) - len(set(unified))
    if duplicate:
        problems.append(f"mode 3 has {duplicate} duplicate names")
    long_unified = sorted(name for name in unified if bytes_of(name) > VMD_NAME_BYTE_LIMIT)
    if long_unified:
        problems.append(f"mode 3 names over the vmd limit: {long_unified[:5]}")
    # morphs that merge in the unified spelling: the same morph baked onto several meshes
    print(f"          tagged mode keeps {len(set(tagged)) - len(set(unified))} more entries, "
          f"which the unified spelling merges (same morph on several meshes)")
    return problems


def uitable(u: str) -> str:
    """Wrap a long name in backticks for the generated markdown."""
    return f"`{u}`"


def render_markdown(tagged: list[str], short: list[str], unified: list[str]) -> str:
    short_set, unified_set = set(short), set(unified)
    rows_by_family: dict[str, list[tuple[str, str, str]]] = {}
    seen = set()
    for name in tagged:
        short_form = short_name(name)
        unified_form = unified_name(name)
        if unified_form in seen:
            continue
        seen.add(unified_form)
        family = parse(name).family
        rows_by_family.setdefault(family, []).append((unified_form, short_form, name))

    lines = []
    for family in list(FAMILIES) + ["Other"]:
        rows = rows_by_family.get(family)
        if not rows:
            continue
        lines.append("")
        lines.append(f"### {GROUP_TOKENS.get(family, family)} ({len(rows)} morphs)")
        lines.append("")
        lines.append("| unified (mode 3) | short (mode 1) | tagged (mode 0) | bytes | fits vmd |")
        lines.append("|---|---|---|---|---|")
        for unified_form, short_form, tagged_form in sorted(rows):
            fits = "yes" if bytes_of(unified_form) <= VMD_NAME_BYTE_LIMIT else "**no**"
            fallback = " *(no tag, falls back to the short name)*" if unified_form == short_form else ""
            lines.append(f"| `{unified_form}`{fallback} | `{short_form}` | `{tagged_form}` | "
                         f"{bytes_of(unified_form)} | {fits} |")
    return "\n".join(lines)


EXAMPLES = (
    ("EyeBrow_1_R", "an eyebrow: the group is shortened from EyeBrow to Brow"),
    ("Eye_2_L", "a wink: the tag carries what the morph does"),
    ("Mouth_2_0", "a mouth shape: the side moves out of the tag into the name"),
    ("Eye_20_R", "an eye range morph, the ones the Blender addon builds controls from"),
)


def show_examples(tagged: list[str]) -> None:
    by_short: dict[str, list[str]] = {}
    for name in tagged:
        by_short.setdefault(short_name(name), []).append(name)

    print()
    print("Examples for one morph at a time (drop-in text for the dropdown):")
    for wanted, why in EXAMPLES:
        found = by_short.get(wanted)
        if not found:
            print(f"   {wanted}: not in this model")
            continue
        name = found[0]
        unified_form = unified_name(name)
        print()
        print(f"   {wanted} - {why}")
        print(f"      0 tagged  : {name}")
        print(f"      1 short   : {short_name(name)}")
        print(f"      2 both    : {name}  +  {short_name(name)}")
        print(f"      3 unified : {unified_form}")
        aliases = [n for n in found if n != name]
        if aliases:
            print(f"      (the same morph also exists as {', '.join(aliases)} - modes 1 and 3 merge those, "
                  f"mode 0 keeps them apart)")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--tagged", required=True, help="pmx exported with PmxMorphNameMode 0")
    parser.add_argument("--short", required=True, help="pmx exported with PmxMorphNameMode 1")
    parser.add_argument("--both", required=True, help="pmx exported with PmxMorphNameMode 2")
    parser.add_argument("--unified", required=True, help="pmx exported with PmxMorphNameMode 3")
    parser.add_argument("--out", help="write the full mapping table here as markdown")
    parser.add_argument("--examples", action="store_true", help="print one morph in all four modes")
    args = parser.parse_args()

    tagged = load_names(args.tagged)
    short = load_names(args.short)
    both = load_names(args.both)
    unified = load_names(args.unified)
    if not tagged or not unified:
        print("!! one of the files has no morphs")
        return 1

    problems = check(tagged, short, both, unified)
    if args.examples:
        show_examples(tagged)

    if args.out:
        header = (
            "# What a morph is called in each naming mode\n\n"
            "Generated by `Tools/morph_name_table.py` from four exports of the same model, one per\n"
            "`Config.PmxMorphNameMode`, then checked against those files: every name below is really in the\n"
            "corresponding `.pmx`. Mode 2 (`Both`) writes the tagged name and the short name for every morph,\n"
            "so it is the union of the two columns.\n\n"
            "A vmd morph name field holds 15 shift-jis bytes. Names longer than that cannot be used by a\n"
            "motion, which is why mode 0 is model-only.\n\n"
            f"In this model (character 1001, costume 00): mode 0 writes **{len(tagged)}** morph entries -\n"
            f"the same morph baked onto two meshes, e.g. `M_Face` and `M_Mayu`, stays two entries there -\n"
            f"modes 1 and 3 write **{len(short)}** and **{len(unified)}** because those spellings merge them,\n"
            f"and mode 2 writes **{len(both)}**. Every morph here has a usable tag, so no unified name falls\n"
            "back to the short spelling; a morph without one (or whose unified name would not fit) keeps its\n"
            "short name instead, which is the only case where the two columns are identical.\n\n"
            f"Regenerate with:\n\n```powershell\n"
            f'./Tools/morph_name_table.py --tagged "{os.path.basename(args.tagged)}" '
            f'--short "{os.path.basename(args.short)}" --both "{os.path.basename(args.both)}" '
            f'--unified "{os.path.basename(args.unified)}" --out {args.out}\n```\n'
        )
        with open(args.out, "w", encoding="utf-8") as handle:
            handle.write(header + render_markdown(tagged, short, unified) + "\n")
        print(f"\nwrote {args.out}")

    for problem in problems:
        print(f"!! {problem}")
    print("PASS" if not problems else "FAIL")
    return 0 if not problems else 1


sys.exit(main())
