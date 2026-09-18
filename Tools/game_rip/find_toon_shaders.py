"""Find the character toon shaders, and read the real face material's parameters.

The shader bundle holds 499 shaders with names and property lists but only compiled code blobs, so the algorithm
has to come from the bytecode. The material, though, carries the actual values - which texture is bound where, and
what the numbers are - and that is what has been guessed at all session.
"""

import json
from pathlib import Path

import UnityPy

OUT = Path(r"game_shaders")
env = UnityPy.load(str(OUT / "shader_bundle.unity3d"))

rows = []
for obj in env.objects:
    if obj.type.name != "Shader":
        continue
    tree = obj.read_typetree()
    parsed = tree.get("m_ParsedForm", {})
    name = parsed.get("m_Name", "")
    props = [p.get("m_Name", "") for p in parsed.get("m_PropInfo", {}).get("m_Props", [])]
    rows.append((name, props, obj.path_id))

print(f"{len(rows)} shaders")
print("\n--- names containing Chara / Toon / Face")
for name, props, path_id in sorted(rows):
    if any(word in name for word in ("Toon", "Chara", "Face")):
        print(f"  {name!r} ({len(props)} props) id={path_id}")
        print(f"      {props}")

print("\n--- which shaders carry the properties we have been guessing at")
for name, props, path_id in sorted(rows):
    interesting = [p for p in props if p in ("_TripleMaskMap", "_ToonMap", "_ShadowStrength",
                                             "_ViewDirX", "_ViewDirY", "_MainTex", "_ToonMap_ST")]
    if interesting:
        print(f"  {name!r}: {interesting}")

