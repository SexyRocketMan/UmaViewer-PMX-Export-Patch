"""The face shader's property list, and the real face material's parameter values."""

import hashlib
import json
import struct
from pathlib import Path

import UnityPy

OUT = Path(r"game_shaders")
DAT = Path(r"C:\Users\user\Umamusume\umamusume_Data\Persistent\dat")
ABKEY = bytes([0x53, 0x2B, 0x46, 0x31, 0xE4, 0xA7, 0xB9, 0x47, 0x3E, 0x7C, 0xFB])


def keystream(entry_key: int) -> bytes:
    key_bytes = struct.pack("<q", entry_key)
    keys = bytearray(len(ABKEY) * 8)
    for i, base in enumerate(ABKEY):
        for j in range(8):
            keys[i * 8 + j] = base ^ key_bytes[j]
    return bytes(keys)


def load_bundle(bundle_hash: str, entry_key: int, label: str):
    data = bytearray((DAT / bundle_hash[:2] / bundle_hash).read_bytes())
    keys = keystream(entry_key)
    for position in range(256, len(data)):
        data[position] ^= keys[position % len(keys)]
    (OUT / f"{label}.unity3d").write_bytes(bytes(data))
    return UnityPy.load(bytes(data))


# --- 1. the face shaders' properties -------------------------------------------------------------
env = UnityPy.load(str(OUT / "shader_bundle.unity3d"))
faces = []
for obj in env.objects:
    if obj.type.name != "Shader":
        continue
    parsed = obj.read_typetree().get("m_ParsedForm", {})
    name = parsed.get("m_Name", "")
    if "Face" in name or "Toon" in name and "Noline" in name and "Hair" not in name:
        props = [p.get("m_Name", "") for p in parsed.get("m_PropInfo", {}).get("m_Props", [])]
        faces.append((name, props))

print(f"--- {len(faces)} candidate shaders")
for name, props in sorted(faces):
    mask = [p for p in props if "Mask" in p or "Toon" in p or "Cylinder" in p or "Face" in p]
    print(f"\n{name} ({len(props)} props)")
    print(f"   mask/toon-related: {mask}")

# --- 2. the real face material -------------------------------------------------------------------
print("\n=== the game's own face material ===")
bundle = load_bundle("3GQR2HQJSCOKRZXBD7UY5OJARUQRDIL4", 6614454798242685316, "face_material_bundle")
for obj in bundle.objects:
    if obj.type.name != "Material":
        continue
    tree = obj.read_typetree()
    name = tree.get("m_Name", "")
    if "face" not in name:
        continue
    print(f"\n{name}")
    saved = tree.get("m_SavedProperties", {})
    print(f"  floats: {len(saved.get('m_Floats', []))}, colors: {len(saved.get('m_Colors', []))}, "
          f"textures: {len(saved.get('m_TexEnvs', []))}")
    def pair(entry):
        """UnityPy hands these as [key, value] or as a dict, depending on version."""
        if isinstance(entry, dict):
            return entry.get("first", entry.get("key", "")), entry.get("second", entry.get("value"))
        return entry[0], entry[1]

    def scalar(value):
        if isinstance(value, dict):
            return value.get("value", value.get("rgba", value))
        return value

    print("   floats:")
    for entry in saved.get("m_Floats", []):
        key, value = pair(entry)
        if any(word in str(key) for word in ("Toon", "Shadow", "Cylinder", "Face", "Option", "Satur",
                                             "Normalize", "VertexColor", "Cutoff")):
            print(f"     {key} = {scalar(value)}")
    print("   textures:")
    for entry in saved.get("m_TexEnvs", []):
        key, value = pair(entry)
        if any(word in str(key) for word in ("Triple", "Toon", "Mask", "Main", "Option")):
            texture = value.get("m_Texture", {}) if isinstance(value, dict) else {}
            print(f"     {key} -> {texture}")
    print("   colours:")
    for entry in saved.get("m_Colors", []):
        key, value = pair(entry)
        if any(word in str(key) for word in ("Toon", "Mask", "Chara")):
            print(f"     {key} = {scalar(value)}")

