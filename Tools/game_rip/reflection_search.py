"""Ask whether the game's shipped DXBC carries reflection data anywhere, not just in the face program.

Every conclusion this project has about "which cb1 slot is which property" depends on knowing whether the
programs name their own constants. The face pixel container has no RDEF chunk; this checks the other 27
containers in the same blob region, and prints each container's chunk list, so the answer is a property of
the bundle rather than of one program.

Run:  python reflection_search.py
"""

import struct
import sys
from pathlib import Path

sys.path.insert(0, r"C:\Users\user\Documents\dev\UmaViewer-PMX-Export-Patch\Tools\game_rip")
from walk_blobs import containers                                    # noqa: E402

REGION = Path(r"C:\Users\user\Documents\dev\_uma_scratch\game_shaders\face_blob_region.bin")
data = REGION.read_bytes()
found = containers(REGION)
print(f"{len(found)} DXBC containers in {REGION.name}")
with_reflection = 0
for offset, length, chunks in found:
    blob = data[offset:offset + length]
    tags = []
    for index in range(chunks):
        tag = blob[28 + 4 * index:32 + 4 * index]
        tags.append(tag.decode("ascii", "replace") if all(32 <= b < 127 for b in tag) else f"<{tag.hex()}>")
    rdef = blob.find(b"RDEF")
    if rdef >= 0:
        with_reflection += 1
    kind = "pixel" if "RDEF" in tags or length > 6000 else "?"
    print(f"  offset {offset:7d} length {length:6d} chunks {chunks}  {tags}  RDEF@{rdef}")
print(f"\n{with_reflection} of {len(found)} containers carry an RDEF chunk")
