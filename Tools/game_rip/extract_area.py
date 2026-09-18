"""Extract the *_area* texture - the mask-colour region map - and look at what it contains."""

import struct
from pathlib import Path

import UnityPy

OUT = Path(r"C:\Users\user\Documents\dev\_uma_scratch\game_shaders")
DAT = Path(r"C:\Users\user\Umamusume\umamusume_Data\Persistent\dat")
ABKEY = bytes([0x53, 0x2B, 0x46, 0x31, 0xE4, 0xA7, 0xB9, 0x47, 0x3E, 0x7C, 0xFB])


def keystream(entry_key):
    key_bytes = struct.pack("<q", entry_key)
    keys = bytearray(len(ABKEY) * 8)
    for i, base in enumerate(ABKEY):
        for j in range(8):
            keys[i * 8 + j] = base ^ key_bytes[j]
    return bytes(keys)


def load(bundle_hash, entry_key, label):
    data = bytearray((DAT / bundle_hash[:2] / bundle_hash).read_bytes())
    keys = keystream(entry_key)
    for position in range(256, len(data)):
        data[position] ^= keys[position % len(keys)]
    (OUT / f"{label}.unity3d").write_bytes(bytes(data))
    return UnityPy.load(bytes(data))


for bundle_hash, entry_key, label in (
    ("6MQFGZXC62KGRJM4XDQVETKUJLTT6QFK", -614143197183374756, "area_face000"),
    ("ZKVOWNLO2E2RUQFBFE45TE3OK3LEUUK5", 2350644591724972638, "area_face000_wet"),
):
    print(f"--- {label}: {bundle_hash[:8]}")
    env = load(bundle_hash, entry_key, label)
    kinds = {}
    for obj in env.objects:
        kinds[obj.type.name] = kinds.get(obj.type.name, 0) + 1
    print("   objects:", kinds)
    for obj in env.objects:
        if obj.type.name != "Texture2D":
            continue
        data = obj.read()
        name = getattr(data, "m_Name", "") or f"texture_{obj.path_id}"
        image = data.image
        target = OUT / f"{name}.png"
        image.save(target)
        rgb = image.convert("RGB")
        pixels = list(rgb.getdata())
        count = len(pixels)
        print(f"   {name}: {image.size[0]}x{image.size[1]} -> {target.name}")
        for channel, label_c in ((0, "r"), (1, "g"), (2, "b")):
            values = [p[channel] for p in pixels]
            low = 100.0 * sum(1 for v in values if v < 125) / count
            high = 100.0 * sum(1 for v in values if v > 130) / count
            print(f"      {label_c}: mean {sum(values)/count:6.1f}   {low:5.1f}% below 0.49   {high:5.1f}% above 0.51")
