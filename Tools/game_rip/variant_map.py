"""Recover which compiled program is which shader variant, and the texture-register order.

Two things the shipped bytecode does not tell you by itself:

* **Which program is which variant.** Unity's serialized `Shader` keeps compiled subprograms in each
  pass's `m_SubPrograms`, but for these baked build shaders that array is empty - the mapping only
  exists in the decompressed blob region, as a small record before each DXBC container that carries
  the keywords selecting it in plain ASCII. This reads that record.
* **Which texture register is which property.** The containers have **no RDEF chunk**, so the
  `t0..tN` in a disassembly have no names in them at all. The order comes from the shader's own
  property table instead: `m_ParsedForm.m_PropInfo` lists the properties in declaration order, and
  Unity assigns sampler units to the texture-typed ones in that order. The variant register counts
  are the check - a variant that omits a texture declares one fewer, contiguous, trailing register.

Usage:

    python variant_map.py <bundle.unity3d> "<shader name>" [scratch dir]

The region is written to the scratch directory, never into this repository: the extracted programs
are the game's assets. What belongs in the repository is the reading of them.
"""

import json
import re
import struct
import sys
from pathlib import Path

import UnityPy

IDENTIFIER = re.compile(rb"[A-Za-z_][A-Za-z0-9_]{2,}")
TEXTURE_PROPERTY = 4


def containers(raw):
    """Every DXBC container: the header declares its own length at offset 24."""
    found, start = [], 0
    while True:
        offset = raw.find(b"DXBC", start)
        if offset < 0:
            return found
        start = offset + 4
        if offset + 32 > len(raw):
            continue
        length = struct.unpack_from("<I", raw, offset + 24)[0]
        if 32 < length < 200000 and offset + length <= len(raw):
            found.append((offset, length))


def keywords_before(raw, offset, floor, known):
    """The keyword record immediately before a container, kept to the shader's own keyword names.

    The record holds more than keywords - it also names the constant buffers the variant binds
    (`Globals`, `UnityPerDraw`, `UnityPerMaterial`) and sometimes material constants - so what
    survives is filtered against m_KeywordNames. That filter is what makes the reading exact: without
    it, a variant with no keywords at all looks like it has ten.
    """
    block = raw[max(floor, offset - 512):offset]
    words = []
    for match in IDENTIFIER.finditer(block):
        text = match.group().decode("ascii")
        if text in known and text not in words:
            words.append(text)
    return words


def main():
    bundle_path = Path(sys.argv[1])
    wanted = sys.argv[2] if len(sys.argv) > 2 else "Gallop/3D/Chara/ToonFace/TSER"
    scratch = Path(sys.argv[3]) if len(sys.argv) > 3 else Path("game_shaders")

    env = UnityPy.load(str(bundle_path))
    tree = None
    for obj in env.objects:
        if obj.type.name != "Shader":
            continue
        candidate = obj.read_typetree()
        if candidate.get("m_ParsedForm", {}).get("m_Name", "") == wanted:
            tree = candidate
            break
    if tree is None:
        raise SystemExit(f"no shader named {wanted!r} in {bundle_path}")

    form = tree["m_ParsedForm"]
    print(f"shader: {wanted}")
    print(f"  baked: {tree.get('m_ShaderIsBaked')}   platforms: {tree.get('platforms')}")

    keywords = form.get("m_KeywordNames") or []
    print(f"  keywords ({len(keywords)}): {keywords}")

    textures = [p.get("m_Name") for p in form.get("m_PropInfo", {}).get("m_Props", [])
                if p.get("m_Type") == TEXTURE_PROPERTY]
    print(f"  texture properties, in declaration order (this is the register map):")
    for register, name in enumerate(textures):
        print(f"    t{register} = {name}")

    compressed = bytes(tree["compressedBlob"])
    size = tree["decompressedLengths"][0][0]
    from UnityPy.helpers import CompressionHelper
    raw = CompressionHelper.decompress_lz4(compressed, size)
    scratch.mkdir(parents=True, exist_ok=True)
    slug = wanted.replace("/", "_")
    region = scratch / f"{slug}_region.bin"
    region.write_bytes(raw)
    print(f"\n  blob region {len(compressed)} -> {len(raw)} bytes, written to {region}")

    found = containers(raw)
    print(f"  {len(found)} DXBC containers")
    print("  (the compiler's own stage token - ps_/vs_ - and the declared texture count only exist in "
          "the disassembly, not in the raw chunk bytes; run dxbc_disasm on this region for those)\n")
    previous_end = 0
    for index, (offset, length) in enumerate(found):
        words = keywords_before(raw, offset, previous_end, set(keywords))
        label = ", ".join(words) if words else "<base: no keywords>"
        print(f"  [{index:>2}] offset {offset:>7} length {length:>6}  [{label}]")
        previous_end = offset + length

    print("\nTo disassemble these: dxbc_disasm <region.bin> <out dir> writes one file per container, "
          "named by its offset.")


main()
