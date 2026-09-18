"""Decompress the shader's blob region and find the individual program blobs inside it."""

from pathlib import Path

import UnityPy

OUT = Path(r"C:\Users\user\Documents\dev\_uma_scratch\game_shaders")
env = UnityPy.load(str(OUT / "shader_bundle.unity3d"))

tree = None
for obj in env.objects:
    if obj.type.name != "Shader":
        continue
    candidate = obj.read_typetree()
    if candidate.get("m_ParsedForm", {}).get("m_Name", "") == "Gallop/3D/Chara/DitherToonFace/TSER":
        tree = candidate
        break
assert tree is not None

compressed = bytes(tree["compressedBlob"])
print(f"compressed {len(compressed)} bytes")

try:
    from UnityPy.helpers import CompressionHelper
    print("CompressionHelper:", [n for n in dir(CompressionHelper) if "decomp" in n.lower() or "lz4" in n.lower()][:6])
except Exception as exc:
    CompressionHelper = None
    print("no CompressionHelper:", exc)

raw = None
for attempt in ("unitypy", "lz4"):
    try:
        if attempt == "unitypy" and CompressionHelper is not None:
            raw = CompressionHelper.decompress_lz4(compressed, 139660)
        elif attempt == "lz4":
            import lz4.block
            raw = lz4.block.decompress(compressed, uncompressed_size=139660)
        print(f"{attempt}: decompressed {len(raw)} bytes")
        break
    except Exception as exc:
        print(f"{attempt} failed: {type(exc).__name__}: {exc}")

if raw is None:
    raise SystemExit("could not decompress")

(OUT / "face_blob_region.bin").write_bytes(raw)

# find the program headers inside the region
for magic in (b"DXBC", b"DXIL", b"SPIR", b"\x03\x02\x23\x07", b"!!ts", b"GLCN", b"GLEX"):
    offset = raw.find(magic)
    if offset >= 0:
        print(f"found {magic!r} at offset {offset}")

print("\nfirst 64 bytes:", raw[:64])
offsets = []
for magic in (b"DXBC", b"DXIL"):
    start = 0
    while True:
        found = raw.find(magic, start)
        if found < 0:
            break
        offsets.append(found)
        start = found + 4
print(f"{len(offsets)} DXBC/DXIL headers at {offsets[:12]}")
