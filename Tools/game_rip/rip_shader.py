"""Decrypt a game bundle and pull the shader source out of it.

Decryption, from the viewer's UmaAssetBundleStream and UmaDatabaseEntry:

    keys[i*8 + j] = ABKey[i] ^ little_endian_int64(entry_key)[j]     for i in 0..10, j in 0..7   (88 bytes)
    byte[p] ^= keys[p % 88]                                          for p >= 256

The first 256 bytes are in the clear, which is why the bundles start with a readable UnityFS header.
"""

import json
import struct
import sys
from pathlib import Path

import UnityPy

ABKEY = bytes([0x53, 0x2B, 0x46, 0x31, 0xE4, 0xA7, 0xB9, 0x47, 0x3E, 0x7C, 0xFB])
DAT = Path(r"C:\Users\user\Umamusume\umamusume_Data\Persistent\dat")
OUT = Path(r"game_shaders")


def keystream(entry_key: int) -> bytes:
    key_bytes = struct.pack("<q", entry_key)
    keys = bytearray(len(ABKEY) * 8)
    for i, base in enumerate(ABKEY):
        for j in range(8):
            keys[i * 8 + j] = base ^ key_bytes[j]
    return bytes(keys)


def decrypt(bundle_hash: str, entry_key: int) -> bytes:
    path = DAT / bundle_hash[:2] / bundle_hash
    data = bytearray(path.read_bytes())
    keys = keystream(entry_key)
    for position in range(256, len(data)):
        data[position] ^= keys[position % len(keys)]
    return bytes(data), path


def main():
    bundle_hash, entry_key = sys.argv[1], int(sys.argv[2])
    label = sys.argv[3] if len(sys.argv) > 3 else bundle_hash[:8]
    OUT.mkdir(parents=True, exist_ok=True)

    data, path = decrypt(bundle_hash, entry_key)
    print(f"{path.name}: {len(data)/1e6:.1f} MB, first 16 bytes {data[:16]!r}")
    (OUT / f"{label}.unity3d").write_bytes(data)

    env = UnityPy.load(data)
    kinds = {}
    for obj in env.objects:
        kinds[obj.type.name] = kinds.get(obj.type.name, 0) + 1
    print("objects:", sorted(kinds.items(), key=lambda kv: -kv[1])[:12])

    for obj in env.objects:
        if obj.type.name != "Shader":
            continue
        tree = obj.read_typetree()
        props = [p.get("m_Name", "") for p in tree.get("m_PropInfo", {}).get("m_Props", [])]
        codes = []
        for sub in tree.get("m_SubShaders", []):
            for index, p in enumerate(sub.get("m_Passes", [])):
                for kind in ("Vertex", "Fragment", "Geometry", "Hull", "Domain"):
                    code = p.get(f"m_Prog{kind}", {}).get("m_Code", "")
                    if code:
                        codes.append((p.get("m_Name", f"pass{index}"), kind, code))
        name = f"{label}_{obj.path_id}"
        (OUT / f"{name}.json").write_text(json.dumps(tree, indent=1)[:800000], encoding="utf-8")
        for index, (pass_name, kind, code) in enumerate(codes):
            (OUT / f"{name}_{index}_{kind}.hlsl").write_text(code, encoding="utf-8")
        interesting = [p for p in props if p.startswith(("_Triple", "_Toon", "_Shadow", "_ViewDir", "_Main"))]
        print(f"  shader {obj.path_id}: {len(props)} properties, {len(codes)} code blocks"
              f"{', interesting: ' + str(interesting) if interesting else ''}")


main()

