"""Find which object in a story timeline bundle holds the dialogue, and what it carries besides.

Read-only research tool: opens one story timeline bundle, walks every MonoBehaviour's typetree, and
reports the fields whose value looks like Japanese text - with the sibling fields at the same level, so
the layout around a line (speaker, voice id, lipsync, ...) becomes visible.
"""
import io
import os
import re
import sqlite3
import sys

import UnityPy

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
GAME = r"C:\Users\user\Umamusume\umamusume_Data\Persistent"
META = os.path.join(ROOT, "meta_plain.sqlite")
AB_KEY = bytes.fromhex("532B4631E4A7B9473E7CFB")
JAPANESE = re.compile(r"[\u3000-\u30ff\u4e00-\u9fff\uff00-\uffef]")


def file_key(key):
    keys = bytearray()
    kb = (key & 0xFFFFFFFFFFFFFFFF).to_bytes(8, "little")
    for b in AB_KEY:
        for j in range(8):
            keys.append(b ^ kb[j])
    return bytes(keys)


def decrypt(data, key):
    data = bytearray(data)
    if key and len(data) > 256:
        keys = file_key(key)
        for i in range(256, len(data)):
            data[i] ^= keys[i % len(keys)]
    return bytes(data)


def load(name):
    con = sqlite3.connect(f"file:{META}?mode=ro", uri=True)
    row = con.execute("select h, e from a where n = ?", (name,)).fetchone()
    con.close()
    if not row:
        raise SystemExit(f"{name} is not in the asset index")
    h, key = row
    path = os.path.join(GAME, "dat", h[:2], h)
    if not os.path.exists(path):
        raise SystemExit(f"{path} is not downloaded")
    return UnityPy.load(io.BytesIO(decrypt(open(path, "rb").read(), key)))


def walk(value, path=""):
    """Yield (path, value) for every leaf, so a line can be printed with its neighbours."""
    if isinstance(value, dict):
        for k, v in value.items():
            yield from walk(v, f"{path}.{k}" if path else k)
    elif isinstance(value, (list, tuple)):
        for i, v in enumerate(value[:4]):
            yield from walk(v, f"{path}[{i}]")
    else:
        yield path, value


def main():
    name = sys.argv[1] if len(sys.argv) > 1 else "story/data/40/0000/storytimeline_400000001"
    env = load(name)
    print(name)

    classes = {}
    for obj in env.objects:
        if obj.type.name == "MonoScript":
            try:
                data = obj.read()
                classes[obj.path_id] = data.m_ClassName
            except Exception:
                pass
    print(f"scripts: {sorted(set(classes.values()))}")

    hits = 0
    for obj in env.objects:
        if obj.type.name != "MonoBehaviour":
            continue
        try:
            tree = obj.read_typetree()
        except Exception as exc:  # noqa: BLE001
            continue
        leaves = list(walk(tree))
        texty = [(p, v) for p, v in leaves if isinstance(v, str) and JAPANESE.search(v)]
        if not texty:
            continue
        hits += 1
        if hits > 3:
            continue
        print(f"\n== pathid={obj.path_id} ({len(leaves)} leaves)")
        for path, value in texty[:4]:
            print(f"   TEXT {path} = {value[:80]!r}")
        print("   neighbours:")
        for path, value in leaves[:40]:
            print(f"     {path} = {value!r}"[:160])
    print(f"\n{hits} objects carry japanese text")


if __name__ == "__main__":
    main()
