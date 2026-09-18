"""Walk from a decrypted bundle to disassembled DXBC programs.

    python extract_blob.py     find the shader by name and report its blob layout
    python decompress_blob.py  LZ4-decompress the blob region (writes face_blob_region.bin)
    dotnet run --project dxbc_disasm -- <region.bin> <outDir>
    python parse_dxbc.py       optional: list the containers

Two traps, both hit while building this:

* The DXBC container declares its own length at offset 24. Slicing to the *next* DXBC magic silently truncates
  any container that has a "DXBC" string inside it, and the disassembler then fails with E_FAIL for every blob.
* `ID3DBlob` must be read through its vtable. Slots 0-2 are QueryInterface/AddRef/Release, so the buffer
  accessors are 3 and 4; going through COM interop landed on the wrong slot.
"""

import struct
from pathlib import Path

REGION = Path(__file__).with_name("face_blob_region.bin")


def containers(region_path=REGION):
    """(offset, length, chunk_count) for every DXBC container in a decompressed blob region."""
    data = region_path.read_bytes()
    found = []
    start = 0
    while True:
        offset = data.find(b"DXBC", start)
        if offset < 0:
            return found
        start = offset + 4
        if offset + 32 > len(data):
            continue
        length, chunks = struct.unpack_from("<II", data, offset + 24)
        if 64 < length <= 200000 and 0 < chunks <= 32 and offset + length <= len(data):
            found.append((offset, length, chunks))


if __name__ == "__main__":
    for offset, length, chunks in containers():
        print(f"offset {offset:7d}  length {length:6d}  chunks {chunks}")
