"""Drop the black key from the decal material and its manifest row.

    python Tools/drop_decal_key.py [--check]

The key multiplied the decal's alpha by the map's brightest channel. It was shipped on by default, then turned off by
default when the manga came out pure white - but a default only applies when a property is *created*, so a file shaded
in between kept `uma_shader_decal_key = 1.0` on its `Uma Sun` and the driver kept forcing it. Two regressions from one
idea that the manga cannot use at all (their art is black line work), so it goes: the node leaves the library material
and the row leaves the manifest, rather than leaving a knob whose stale value is invisible.

The library change is a `git checkout` of the blend to the commit before the key; this script only handles the manifest
row, and asserts that it removes exactly one row and leaves the file hand-formatted.
"""
import io
import sys

MANIFEST = (r"C:\Users\user\AppData\Roaming\Blender Foundation\Blender\5.2\extensions\user_default"
            r"\uma_addon\uma_addon\addons\uma_addon\operators\shaders\umashader-0.2.6.json")

START = '    {"key": "uma_shader_decal_key"'


def apply(check=False):
    lines = io.open(MANIFEST, encoding="utf-8", newline="").read().splitlines(keepends=True)
    starts = [index for index, line in enumerate(lines) if line.startswith(START)]
    assert len(starts) == 1, "expected exactly one decal-key row, found %d" % len(starts)
    start = starts[0]
    # the row is hand-formatted over three lines: the key, the name/range, and the description that closes the object
    end = start + 2
    assert lines[end].rstrip().endswith("},"), "the third line of the row does not close it: %r" % lines[end]
    assert lines[end + 1].lstrip().startswith('{"key"'), "the row is not followed by another row: %r" % lines[end + 1]
    removed = "".join(lines[start:end + 1])
    assert "Key out black" in removed, "the block removed is not the decal-key row:\n%s" % removed
    if check:
        print("would remove %d line(s):\n%s" % (end - start + 1, removed.strip()))
        return
    del lines[start:end + 1]
    io.open(MANIFEST, "w", encoding="utf-8", newline="").write("".join(lines))
    print("removed the decal-key row from the manifest")


apply("--check" in sys.argv)
