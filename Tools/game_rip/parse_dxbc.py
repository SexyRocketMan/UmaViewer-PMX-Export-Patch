"""Parse the DXBC containers in the blob region and name their resources and constants.

A DXBC file is a header, a chunk table, and chunks: RDEF (resource definition: the names of every constant and
texture the program binds), ISGN/OSGN (inputs and outputs), SHEX or SHDR (the actual bytecode). The names in RDEF
are plain ASCII, which is enough to say which program is the face fragment shader without disassembling anything.
"""

import struct
from pathlib import Path

OUT = Path(r"C:\Users\user\Documents\dev\_uma_scratch\game_shaders")
raw = (OUT / "face_blob_region.bin").read_bytes()


def find_containers(data):
    containers = []
    start = 0
    while True:
        offset = data.find(b"DXBC", start)
        if offset < 0:
            break
        start = offset + 4
        if offset + 32 > len(data):
            continue
        # header: magic, 16-byte hash, version, length, chunk count
        try:
            length, chunks = struct.unpack_from("<II", data, offset + 24)
        except struct.error:
            continue
        if 32 < length < 100000 and 0 < chunks < 32 and offset + length <= len(data):
            table = [struct.unpack_from("<4sII", data, offset + 32 + 16 * i) for i in range(chunks)]
            if all(o + s <= length for _, o, s in table):
                containers.append((offset, length, table))
    return containers


def parse_rdef(data, offset, size):
    """Resource definition chunk: constant buffers with variables, and the bound resources."""
    names = []
    try:
        constant_buffers, constant_count, _, _, _, resource_count = struct.unpack_from("<IIIIII", data, offset + 24)
        base = offset + 28
        for i in range(constant_count):
            name_offset, variable_count = struct.unpack_from("<II", data, base + 24 * i)
            end = data.index(b"\0", offset + name_offset)
            names.append(data[offset + name_offset:end].decode("ascii", "replace"))
            for v in range(variable_count):
                slot, var_off, var_size, flags, _, _, var_name = struct.unpack_from(
                    "<IIIIIII", data, base + constant_count * 24 + 40 * (v + sum(
                        1 for j in range(i))))  # not exact for multiple buffers; fine for naming
        resource_base = base + constant_count * 24
        # walk resources: each is 32 bytes with a name offset
        for i in range(resource_count):
            entry = resource_base + 32 * i
            if entry + 32 > offset + size:
                break
            name_offset = struct.unpack_from("<I", data, entry)[0]
            if 0 < name_offset < size:
                end = data.index(b"\0", offset + name_offset)
                text = data[offset + name_offset:end].decode("ascii", "replace")
                if text.isprintable():
                    names.append(text)
    except Exception as exc:
        names.append(f"<rdef parse fell short: {exc}>")
    return names


containers = find_containers(raw)
print(f"{len(containers)} DXBC containers\n")
for offset, length, table in containers:
    chunks = {fourcc.decode("ascii", "replace"): (o, s) for fourcc, o, s in table}
    names = []
    if "RDEF" in chunks:
        o, s = chunks["RDEF"]
        try:
            names = parse_rdef(raw, offset + o, s)
        except Exception as exc:
            names = [f"<{exc}>"]
    interesting = [n for n in names if n.startswith("_") or "Toon" in n or "Triple" in n or "Face" in n]
    stage = ",".join(k for k in chunks.keys() if k.isprintable())
    print(f"offset {offset:>7} length {length:>6} chunks {stage}")
    print("   interesting names: " + str([n for n in interesting[:14] if n.isprintable()]))
