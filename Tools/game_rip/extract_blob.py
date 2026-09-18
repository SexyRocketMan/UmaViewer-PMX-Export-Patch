"""Pull the face shader's compiled program blobs out of the bundle and identify their format."""

import struct
from pathlib import Path

import UnityPy

OUT = Path(r"C:\Users\user\Documents\dev\_uma_scratch\game_shaders")
env = UnityPy.load(str(OUT / "shader_bundle.unity3d"))

target = None
for obj in env.objects:
    if obj.type.name != "Shader":
        continue
    tree = obj.read_typetree()
    if tree.get("m_ParsedForm", {}).get("m_Name", "") == "Gallop/3D/Chara/DitherToonFace/TSER":
        target = (obj, tree)
        break

if target is None:
    raise SystemExit("face shader not found in this bundle")

obj, tree = target
print("shader:", tree["m_ParsedForm"]["m_Name"])
print("m_ShaderIsBaked:", tree.get("m_ShaderIsBaked"))
print("platforms:", len(tree.get("platforms", [])))
for index, platform in enumerate(tree.get("platforms", [])):
    print(f"  platform {index}: {platform}")
print("offsets:", tree.get("offsets"), "compressedLengths:", tree.get("compressedLengths"),
      "decompressedLengths:", tree.get("decompressedLengths"))
print("stageCounts:", tree.get("stageCounts"))
print("compressedBlob length:", len(tree.get("compressedBlob", [])))

passes = tree["m_ParsedForm"]["m_SubShaders"][0]["m_Passes"]
for pindex, p in enumerate(passes):
    print(f"\npass {pindex}: {p.get('m_Name')!r}")
    for stage in ("progVertex", "progFragment", "progGeometry"):
        program = p.get(stage, {})
        subs = program.get("m_SubPrograms", [])
        player = program.get("m_PlayerSubPrograms", [])
        print(f"   {stage}: {len(subs)} subprograms, {len(player)} player entries")
        for index, sub in enumerate(subs[:4]):
            print(f"      {index}: {sub}")

# UnityPy can decompress the blobs for us
try:
    from UnityPy.helpers import ShaderHelper
    print("\nShaderHelper functions:", [n for n in dir(ShaderHelper) if not n.startswith("_")])
except Exception as exc:
    print("no ShaderHelper:", exc)

first_platform = tree["platforms"][0]
try:
    blob = first_platform.get("m_CompressedBlob") or tree["compressedBlob"]
except Exception:
    blob = tree["compressedBlob"]
print("\nfirst bytes of the compressed blob:", bytes(blob[:32]))
