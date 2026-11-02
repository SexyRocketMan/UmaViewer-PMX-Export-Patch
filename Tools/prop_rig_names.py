#!/usr/bin/env python3
"""Tally what the bone names inside a prefab family say about the prefab.

`-ScanProps <family>` prints one line per prop: how many renderers it has, how many bone
references its skin carries, and up to eight of the hand bones and eight of the other bones it
names. That is enough to answer three questions at once, and this script separates them:

  * is it HELD - the skin names a wrist or a finger of the character armature, and few other
    bones, so the prop hangs off a hand;
  * where does it SIT - the other bones it names are the character's own bones (Hip, Chest,
    Shoulder_R, Head...), so the set of them says which part of the body it is worn on;
  * does it NAME ITSELF - a prop that ships bones of its own called "Fan", "Rope" or "Pizza"
    carries a word describing it, and that is the only descriptive text the game ships for
    props at all (see docs/PROP_LABELS.md).

A hand bone (`Wrist_L`, `Thumb_01_L`) names the hand rather than the prop, and a body bone
names the body rather than the prop, so neither is evidence of what the prop looks like. A
prop skinned to the *whole* character (fingers and hips and toes together) is not held - it is
worn - which is why the hand and body counts are reported separately rather than as one
"names hand bones" number.

Usage
    python Tools/prop_rig_names.py <log> [<log> ...]
    python Tools/prop_rig_names.py --family mini <log>     # one family out of a mixed scan
    python Tools/prop_rig_names.py --not-family mini <log>
    python Tools/prop_rig_names.py --vocab <log>           # every bone name, by frequency
    python Tools/prop_rig_names.py --props <log>           # one line per prop, bucketed

The log is the one the wrapper names on stdout ("Log    : ...\\Logs\\headless_export_*.log"),
or any file holding the `... N bone ref(s) ...` lines.
"""

import argparse
import collections
import re
import sys

LINE = re.compile(r"\[(\d+)/(\d+)\]\s+(\S+)\s+(.*)$")
SKIN = re.compile(r"skinned (\d+) of (\d+) renderer\(s\), (\d+) bone ref\(s\), ")
HAND = re.compile(r"HAND bones (\d+): ([^|]*?)(?=\s*\||\s*$)")
OTHER = re.compile(r"\|\s*other (\d+): (.*?), size \(")
SIZE = re.compile(r"size \((.*?)\)\s*$")

# Words that name a place on the character rather than a thing. A prop skinned to these is
# worn at that place; the words still say something useful - just not what the prop is.
BODY = {
    "hip", "waist", "spine", "chest", "neck", "head", "shoulder", "arm", "elbow", "armroll",
    "shoulderroll", "roll", "rol", "wrist", "hand", "thumb", "index", "middle", "ring", "pinky",
    "finger", "thigh", "knee", "ankle", "foot", "toe", "skirt", "tail", "nose", "chin", "cheek",
    "eye", "face", "mouth", "tooth", "teeth", "breast", "bust", "hair", "upbody", "position",
    "body",
}

# Those same words grouped by the part of the character they move with, so a prop's mount can be
# read off its bone names: "Chest,Shoulder_R,Arm_R" is the right arm, "Hip,Thigh_L,Knee_L" the
# left leg, and any hand bone means it follows a hand.
PLACES = {
    "head": {"head", "face", "nose", "chin", "cheek", "eye", "hair", "mouth", "tooth", "teeth"},
    "arm": {"shoulder", "arm", "elbow", "armroll", "shoulderroll", "roll", "rol"},
    "torso": {"hip", "waist", "spine", "chest", "neck", "upbody", "body", "breast", "bust"},
    "leg": {"thigh", "knee", "ankle", "foot", "toe"},
    "tail": {"tail"},
    "skirt": {"skirt"},
}
PLACE_OF = {word: place for place, words in PLACES.items() for word in words}

# Words that say "a slot in a rig", not "a thing": a name made only of these plus numbers is as
# opaque as the id it replaces.
GENERIC = {
    "root", "offset", "bone", "bones", "bn", "bip", "bip01", "joint", "joints", "nub", "node",
    "null", "empty", "dummy", "obj", "object", "grp", "group", "mesh", "geo", "model", "poly",
    "polysurface", "surface", "plane", "cube", "sphere", "cylinder", "box", "loc", "locator",
    "lct", "bo", "c", "cst", "ctrl", "control", "top", "all", "base", "def", "jnt", "sk",
    "skl", "skeleton", "armature", "ref", "temp", "wgt", "rig", "setup", "l", "r", "fl", "fr",
    "bl", "br", "b", "f", "u", "d", "cb", "cu", "cd", "cl", "cr", "in", "out", "sp", "ro",
    "hi", "he", "lo", "mid", "sub", "main", "add", "ex", "no", "var",
    # corner and side suffixes: Mant_FLL is the mantle's front-left-lower end, not a word
    "fll", "flr", "bll", "blr", "ull", "ulr", "dll", "dlr", "ul", "ur", "dl", "dr", "lu", "ru",
    "ld", "rd", "scl", "efc", "bld",
    # a name that grows out of a rig word is a rig word: OffsetL, Root_R, LineA
    "line", "linea", "lineb", "frag", "piece", "seg", "div",
    # category words: they say a prop is an accessory or a cloth, not which one
    "acc", "accessory", "cloth", "clothes", "under",
}

# Three-letter words that are the abbreviation of an object rather than a rig slot. A short token
# is kept only when it is listed here, so `l`, `c` and `fl` stay rig noise while `mic`, `rod`,
# `net` and `pnk` survive as the hints they are.
SHORT_WORDS = {
    "mic", "fan", "net", "rod", "tow", "cap", "bag", "cup", "pen", "key", "hat", "bow", "tie",
    "pot", "can", "pnk", "gad", "ball", "jug", "mat", "jar", "lid",
}


def tokens(name):
    """The words in a bone name, lower-cased, with numbers and separators dropped."""
    return [p.lower() for p in re.split(r"[^A-Za-z]+", name) if p]


def is_word(token):
    """Whether one token of a bone name describes a thing (as opposed to a rig slot)."""
    if token in SHORT_WORDS:
        return True
    if any(token.startswith(rig) for rig in ("offset", "root", "bone", "joint", "def_", "sk_")):
        return False
    return len(token) >= 3 and token not in GENERIC and token not in BODY


def classify(name):
    """'body' for a character bone, 'rig' for a slot name, 'word' for something descriptive."""
    words = tokens(name)
    if any(w in BODY for w in words):
        return "body"
    if any(is_word(w) for w in words):
        return "word"
    return "rig"


def parse(path, family=None, not_family=None):
    rows = []
    for raw in open(path, encoding="utf-8", errors="replace"):
        line = raw.rstrip("\r\n")
        if " bone ref(s)" not in line or "skinned " not in line:
            continue
        m = LINE.search(line)
        skin = SKIN.search(line)
        if not m or not skin:
            continue
        _index, _total, name, _rest = m.groups()
        segments = name.split("/")
        if family and not any(s.startswith(family) for s in segments):
            continue
        if not_family and any(s.startswith(not_family) for s in segments):
            continue

        skinned, renderers, bone_refs = skin.groups()
        tail = line[skin.end():]
        hm = HAND.search(tail)
        hand = [b.strip() for b in hm.group(2).split(",")] if hm else []
        hand = [b for b in hand if b]
        om = OTHER.search(tail)
        other = [b.strip() for b in om.group(2).split(",")] if om else []
        other = [b for b in other if b]
        sm = SIZE.search(tail)

        words = sorted({w for b in other for w in tokens(b) if is_word(w)})
        places = sorted({PLACE_OF[w] for b in other for w in tokens(b) if w in PLACE_OF})
        rows.append(
            {
                "path": name,
                "prefab": name.rsplit("/", 1)[-1],
                "skinned": int(skinned),
                "renderers": int(renderers),
                "bone_refs": int(bone_refs),
                "hand": hand,
                "hand_n": int(hm.group(1)) if hm else 0,
                "other": other,
                "other_n": int(om.group(1)) if om else 0,
                "size": sm.group(1) if sm else "-",
                "words": words,
                "places": places,
            }
        )
    return rows


def bucket(r):
    """How the prop is attached, as far as its own bone names can say.

    A whole-body skin is recognised by size as well as by names: an `attach` prefab is bound to
    sixty-six bones of the character, and the eight names the scan prints are only the first
    eight of them, so their places say where the *list* starts rather than where the prop sits.
    """
    if r["renderers"] == 0:
        return "empty (no renderer)"
    whole = len(r["places"]) >= 3 or (r["hand_n"] >= 8 and r["other_n"] >= 8)
    if whole:
        return f"worn on the whole body ({r['bone_refs']} bones)"
    if r["hand"] and r["places"]:
        return "held, plus " + ",".join(r["places"])
    if r["hand"]:
        return "held in a hand"
    if r["places"]:
        return "worn on the " + ",".join(r["places"])
    if r["other_n"] >= 2:
        return "own rig, body not named"
    if r["other_n"] == 1:
        return "one bone: " + (r["other"][0] if r["other"] else "?")
    return "no bones"


def summary(rows, vocab, props):
    groups = collections.defaultdict(list)
    for r in rows:
        groups[re.sub(r" \(\d+ bones\)$", "", bucket(r))].append(r)
    live = [r for r in rows if r["renderers"] > 0]
    with_words = [r for r in live if r["words"]]

    print(f"props scanned                {len(rows)}")
    for name, members in sorted(groups.items(), key=lambda kv: -len(kv[1])):
        bones = [m["bone_refs"] for m in members]
        spread = f"  skin {min(bones)}-{max(bones)} bones" if max(bones) != min(bones) else (
            f"  skin {bones[0]} bones" if max(bones) > 8 else "")
        print(f"  {name:<34} {len(members):>4}{spread}")
    print(f"a bone name carries a word   {len(with_words)} of {len(live)} "
          f"({100 * len(with_words) // max(1, len(live))}%)")

    if with_words:
        print("\nvocabulary:")
        for word, count in collections.Counter(w for r in with_words for w in r["words"]).most_common():
            print(f"  {count:>4}  {word}")

    if vocab:
        print("\nnon-hand bone names, by frequency:")
        for name, count in collections.Counter(b for r in rows for b in r["other"]).most_common():
            print(f"  {count:>4}  {name:<28} {classify(name)}")

    if props:
        print("\neach prop: how attached / words / the bones it names")
        for r in rows:
            print(f"  {r['prefab']:<48} {bucket(r):<28} {'/'.join(r['words']) or '-':<16} "
                  f"[{','.join(r['other'][:8])}]")


def main(argv):
    parser = argparse.ArgumentParser(
        description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter
    )
    parser.add_argument("logs", nargs="+")
    parser.add_argument("--family", help="only props whose path has a segment starting with this")
    parser.add_argument("--not-family", help="skip props whose path has a segment starting with this")
    parser.add_argument("--vocab", action="store_true", help="dump the bone-name vocabulary")
    parser.add_argument("--props", action="store_true", help="dump one line per prop")
    args = parser.parse_args(argv)

    rows = []
    for path in args.logs:
        rows.extend(parse(path, args.family, args.not_family))
    if not rows:
        print("no scan lines found", file=sys.stderr)
        return 1
    summary(rows, args.vocab, args.props)
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
