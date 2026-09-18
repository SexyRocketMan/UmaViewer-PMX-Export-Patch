"""Which texture files does the game's face material actually bind?

The material's m_TexEnvs entries are (m_FileID, m_PathID) references. FileID 0 means an object inside the same
bundle; anything else is an external bundle listed in the file's m_Externals. Resolving them says what
_TripleMaskMap, _ToonMap and _MainTex really point at - which is the question this project has been guessing at.
"""

import json
from pathlib import Path

import UnityPy

OUT = Path(r"C:\Users\user\Documents\dev\_uma_scratch\game_shaders")
env = UnityPy.load(str(OUT / "face_material_bundle.unity3d"))

# name everything the bundle itself contains, so internal references can be resolved
by_path_id = {}
by_name = {}
for obj in env.objects:
    try:
        data = obj.read()
    except Exception:
        continue
    name = getattr(data, "m_Name", "") or ""
    by_path_id[(0, obj.path_id)] = (obj.type.name, name)
    if name:
        by_name.setdefault(name, []).append(obj.type.name)

print(f"bundle contains {len(env.objects)} objects, {sum(1 for v in by_name.values() if v)} named")
print("names:", sorted(by_name)[:24])

# what the file references that it does not contain
for obj in env.objects:
    tree = obj.read_typetree()
    externals = tree.get("m_Externals")
    if externals:
        print("\nexternal references:")
        for entry in externals:
            if isinstance(entry, dict):
                print("   ", entry.get("fileName") or entry.get("pathName") or entry)
            else:
                print("   ", entry)

print("\n=== the face material's bindings ===")
for obj in env.objects:
    if obj.type.name != "Material":
        continue
    tree = obj.read_typetree()
    if "face" not in tree.get("m_Name", ""):
        continue
    print(f"material {tree.get('m_Name')} shader ref {tree.get('m_Shader')}")
    for entry in tree.get("m_SavedProperties", {}).get("m_TexEnvs", []):
        key, value = (entry[0], entry[1]) if not isinstance(entry, dict) else (entry.get("first"), entry.get("second"))
        if not isinstance(value, dict):
            continue
        file_id = value.get("m_FileID", 0)
        path_id = value.get("m_PathID", 0)
        target = by_path_id.get((0, path_id))
        if file_id == 0 and target:
            print(f"   {key:<18} -> {target[0]} {target[1]!r} (inside this bundle)")
        else:
            print(f"   {key:<18} -> external file {file_id}, pathID {path_id}")
