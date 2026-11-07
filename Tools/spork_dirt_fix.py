"""Two dirt fixes: multiply instead of mix, and put the face group's dirt where its output actually is.

**Multiply.** `Umashader.bind_dirt_maps` documents the look as `colour_out = colour * lerp(1, Dirt tint, mask * Dirt)`,
which is what a Mix node with `blend_type` MULTIPLY and that factor computes. The ported Spork groups were built with
the default MIX, i.e. `lerp(colour, tint, f)` - a fade *towards* the tint rather than a multiplication *by* it, which
washes the surface out (a light tint brightens it) instead of dirtying it. The Looper library's own fallback group uses
MULTIPLY; the two taps in each Spork group are switched to match.

**The face's dirt was on a branch the face group does not always output.** In `Simple Diff + Shaded` the colour taps
feed `Mix.001`, which reaches the output through a shader mix - the tap placement the Looper group uses. In
`Simple Diff + Shaded FACE` the output is a plain colour (`Mix.004.Result`), and that node picks between the tap-mixed
colour and an `RGB Curves` light ramp by `Colored Light`, so the dirt sits on whichever branch the factor happens to
pick. The fix moves the face group's dirt to the **end of its colour path**: one multiply on whatever `Mix.004` chose,
with the two taps removed so it is applied exactly once. The main group keeps its taps (that is where its colour comes
from, and it is measured working there).

    blender --background --factory-startup --python Tools/spork_dirt_fix.py -- [<spork library>]
"""
import os
import shutil
import sys

import bpy

BASE = (r"C:\Users\user\AppData\Roaming\Blender Foundation\Blender\5.2\extensions\user_default"
        r"\uma_addon\uma_addon\addons\uma_addon\operators\shaders")

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
LIBRARY = argv[0] if argv else os.path.join(BASE, "SporkShaders.blend")

MAIN = "Simple Diff + Shaded"
FACE = "Simple Diff + Shaded FACE"


def multiply_taps(group):
    """The two tap mixes, multiplied into the colour instead of faded towards the tint."""
    changed = []
    for name in ("Dirt Diffuse", "Dirt Shaded"):
        node = group.nodes.get(name)
        if node is None or node.type != 'MIX':
            continue
        if node.blend_type != 'MULTIPLY':
            node.blend_type = 'MULTIPLY'
            changed.append(name)
    return changed


def move_face_dirt_to_output(group):
    """One multiply on the face group's final colour, and the two taps removed (so it lands once)."""
    output = next(node for node in group.nodes if node.type == 'GROUP_OUTPUT')
    colour_socket = output.inputs["Color"]
    if not colour_socket.links:
        raise SystemExit("the face group's Color output is not linked")
    source = colour_socket.links[0].from_socket
    source_node = colour_socket.links[0].from_node

    taps = [group.nodes.get(name) for name in ("Dirt Diffuse", "Dirt Shaded")]
    taps = [node for node in taps if node is not None]
    # the taps' consumers go back to the raw group sockets they were inserted in front of
    for tap in taps:
        tap_input = next(socket for socket in tap.inputs if socket.type == 'RGBA' and socket.is_linked)
        raw = tap_input.links[0].from_socket
        result = next(socket for socket in tap.outputs if socket.type == 'RGBA')
        for link in list(result.links):
            target = link.to_socket
            group.links.remove(link)
            group.links.new(raw, target)

    node = group.nodes.get("Dirt Colour")
    if node is None:
        node = group.nodes.new('ShaderNodeMix')
        node.name = node.label = "Dirt Colour"
    node.data_type = 'RGBA'
    node.blend_type = 'MULTIPLY'
    node.clamp_factor = True
    node.location = (source_node.location.x + 200.0, source_node.location.y - 320.0)
    colours = [socket for socket in node.inputs if socket.type == 'RGBA']
    factor = next(socket for socket in node.inputs if socket.name == "Factor" and socket.type == 'VALUE')
    result = next(socket for socket in node.outputs if socket.type == 'RGBA')
    for socket in (colours[0], colours[1], factor):
        for link in list(socket.links):
            group.links.remove(link)
    group.links.new(source, colours[0])
    group_input = next(node for node in group.nodes if node.type == 'GROUP_INPUT')
    group.links.new(group_input.outputs["Dirt Tint"], colours[1])
    group.links.new(group.nodes["Dirt Amount"].outputs["Value"], factor)
    for link in list(colour_socket.links):
        group.links.remove(link)
    group.links.new(result, colour_socket)

    for tap in taps:
        group.nodes.remove(tap)
    frame = group.nodes.get("Dirt Frame")
    if frame is not None:
        node.parent = frame
    return source_node.name


def main():
    if not os.path.isfile(LIBRARY):
        raise SystemExit("no file at %r" % LIBRARY)
    backup = LIBRARY + ".bak"
    if not os.path.exists(backup):
        shutil.copy2(LIBRARY, backup)

    bpy.ops.wm.open_mainfile(filepath=LIBRARY)
    main_group = bpy.data.node_groups.get(MAIN)
    face_group = bpy.data.node_groups.get(FACE)
    if main_group is None or face_group is None:
        raise SystemExit("the library is missing one of its two groups")

    print("main group taps switched to MULTIPLY: %s" % multiply_taps(main_group))
    print("face group: the source of its Color output is %s" % move_face_dirt_to_output(face_group))

    for group, expected in ((main_group, "Simple Diff + Shaded"), (face_group, "Simple Diff + Shaded FACE")):
        taps = [node.blend_type for node in group.nodes
                if node.name.startswith("Dirt ") and node.type == 'MIX']
        print("%-26s %2d node(s), dirt mixes: %s" % (group.name, len(group.nodes), taps))

    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_mainfile(filepath=LIBRARY)
    if os.path.exists(LIBRARY + "1"):
        os.remove(LIBRARY + "1")
    print("wrote %s (%d byte(s))" % (LIBRARY, os.path.getsize(LIBRARY)))


main()
