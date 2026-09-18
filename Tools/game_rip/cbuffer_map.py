"""Map a DXBC program's constant buffer slots to the property names behind them.

The RDEF chunk holds the name table and the descriptors. Layout of the chunk data:

    uint32 constant_buffer_count, uint32 constant_buffer_offset
    uint32 bound_resource_count,  uint32 bound_resource_offset
    uint8 minor, uint8 major, uint16 program_type, uint32 flags, uint32 creator_offset

constant buffer descriptors follow the header at constant_buffer_offset, 24 bytes each:
    uint32 name_offset, uint32 variable_count, uint32 size, uint32 flags, uint32 type, uint32 pad
variable descriptors follow the descriptors, 40 bytes each, in the order the buffers declare them:
    uint32 name_offset, uint32 start_offset, uint32 size, uint32 flags, uint32 default_offset, ...

Offsets are relative to the start of the RDEF chunk data. This is what turns cb1[3].w into _ToonStep.
"""

import struct
from pathlib import Path

PATH = Path(r"C:\Users\user\Documents\dev\_uma_scratch\game_shaders")
raw = (PATH / "face_blob_region.bin").read_bytes()

CONTAINER = 13066                      # the first pixel program
declared = struct.unpack_from("<I", raw, CONTAINER + 24)[0]
blob = raw[CONTAINER:CONTAINER + declared]
print(f"pixel blob: {declared} bytes")

position = blob.find(b"RDEF")
if position < 0:
    raise SystemExit("no RDEF chunk")
chunk_offset, chunk_size = struct.unpack_from("<II", blob, position + 4)
rdef = chunk_offset
print(f"RDEF at {rdef}, {chunk_size} bytes")

constant_count, constant_offset, resource_count, resource_offset = struct.unpack_from("<IIII", blob, rdef)


def name_at(offset):
    end = blob.index(b"\0", rdef + offset)
    return blob[rdef + offset:end].decode("ascii", "replace")


print(f"{constant_count} constant buffers, {resource_count} resources\n")

variable_base = rdef + constant_offset + 24 * constant_count
cursor = variable_base
for buffer_index in range(constant_count):
    descriptor = rdef + constant_offset + 24 * buffer_index
    buffer_name_offset, variable_count, buffer_size, flags = struct.unpack_from("<IIII", blob, descriptor)
    print(f"--- cb{buffer_index}: {name_at(buffer_name_offset)!r}, {buffer_size} bytes, {variable_count} variables")
    for _ in range(variable_count):
        try:
            name_offset, start, size, vflags, default_offset = struct.unpack_from("<IIIII", blob, cursor)
        except struct.error:
            break
        if not 0 < name_offset < chunk_size:
            cursor += 40
            continue
        name = name_at(name_offset)
        slot = start // 16
        lane = (start % 16) // 4
        print(f"      cb{buffer_index}[{slot}].{'xyzw'[lane]}  {name}"
              + (f"   (+{size} bytes)" if size > 4 else ""))
        cursor += 40

print("\n--- bound resources")
for i in range(resource_count):
    entry = rdef + resource_offset + 32 * i
    name_offset, type_id, return_type, dimension, samples, bind_point, bind_count, flags = struct.unpack_from(
        "<IIIIIIII", blob, entry)
    print(f"      t{bind_point or flags} {name_at(name_offset)!r} type {type_id} dim {dimension}")
