"""Port his current `Simple Diff + Shaded` (and its FACE sibling) into our Spork library, with our weather on top.

The library we ship was built from an *older* revision of his shader: 26 nodes, four sockets, one colour path. His
file today (`SporksShader.blend`, the one he sent) carries **51 nodes / 8+3 sockets**: a `Base` mask input, a
`Highlight Color` diffuse pass, `Light Intensity` and `Colored Light`, a `Shadow Output`, and transparency handled
*inside* the group off the base map's blue channel instead of by a Mix Shader in the wrapper. A `Simple Diff +
Shaded FACE` variant (34 nodes) is what his face material wears. So the shipped graph is a revision behind, which is
what the user spotted.

What this script does, in the order it has to happen:

1. appends both groups from his file into `SporkShaders.blend`;
2. injects our weather math into each - the same two-tap dirt the Looper library carries (coverage = max of the
   mask's three channels x `Dirt`, tinted by `Dirt Tint`), wired so *every* consumer of `Diffuse` / `Shaded` reads
   the dirt-mixed colour (his group feeds its emission and both shading chains from those sockets, so one tap would
   have left the shaded side clean);
3. rewires the masters (`Spork Shader`, `Uma Brows`, `Spork Eye Inner`) onto the new group: the wet pairs feed
   `Diffuse`/`Shaded`/`Base`, the `Dirt Map` image feeds the group's `Dirt Map`, and the now-redundant wrapper-side
   transparency chain (`Separate Color` -> `Mix Shader`) is removed - the group does that itself now;
4. adds `Spork Shader Face` (a copy of `Spork Shader` minus the base-map chain) on the FACE group, and puts it in a
   slot on the carrier sphere where `Umashader.library_masters` looks roles up.

The socket names the add-on's apply step and settings rows rely on are unchanged: `Diffuse`, `Shaded`,
`Shadow Softness`, `Emission Strength`, `Dirt Map`, `Dirt`, `Dirt Tint` (plus his new `Base`, `Highlight Color`,
`Light Intensity`, `Colored Light`). `Dirt Map` as an *image node* on the masters is what `_bind_one_dirt` fills.

    blender --background --factory-startup --python Tools/port_spork_group.py -- [<library.blend> [<his.blend>]]
"""
import os
import shutil
import sys

import bpy

BASE = r"C:\Users\user\AppData\Roaming\Blender Foundation\Blender\5.2\extensions\user_default\uma_addon\uma_addon\addons\uma_addon\operators\shaders"

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
LIBRARY = argv[0] if argv else os.path.join(BASE, "SporkShaders.blend")
HIS = argv[1] if len(argv) > 1 else r"C:\Users\user\Downloads\SporksShader.blend"

MAIN = "Simple Diff + Shaded"
FACE = "Simple Diff + Shaded FACE"

#: The weather contract, identical to `Umashaders-0.2.6.blend`: the pass fills a `Dirt Map` image node, the settings
#: rows drive `Dirt` and `Dirt Tint`, and the maths lives in the group.
DIRT_COLOUR = (0.72, 0.66, 0.60, 1.0)

#: His own defaults, measured from his file rather than guessed (they are what every one of his wrappers carries).
HIGHLIGHT_COLOUR = (0.6386, 0.4212, 0.4204, 1.0)
SHADOW_SOFTNESS = 0.15

#: The masters that wear the general group, the one that wears the FACE variant, and the new role's name.
GENERAL_MASTERS = ("Spork Shader", "Uma Brows")
FACE_MASTER = "Spork Shader Face"
EYE_INNER = "Spork Eye Inner"
CARRIER = "Icosphere of Materials"

report = []


def say(text=""):
    report.append(text)
    print("[spork-port] " + text if text else "")


def sockets(tree, in_out="INPUT"):
    return [item.name for item in tree.interface.items_tree
            if getattr(item, "item_type", "SOCKET") == "SOCKET" and item.in_out == in_out]


def package():
    """Append his groups, and return them by their canonical names (a same-named group from an older build is
    renamed out of the way first, so Blender does not suffix the new one)."""
    old_main = bpy.data.node_groups.get(MAIN)
    old_face = bpy.data.node_groups.get(FACE)
    # names, not datablocks: appending invalidates the wrappers held across the load
    stale = []
    if old_main is not None:
        old_main.name = MAIN + " (shipped before the port)"
        stale.append(old_main.name)
    if old_face is not None:
        old_face.name = FACE + " (shipped before the port)"
        stale.append(old_face.name)

    with bpy.data.libraries.load(HIS, link=False) as (data_from, data_to):
        for name in (MAIN, FACE):
            if name not in data_from.node_groups:
                raise SystemExit("his file has no node group %r (has %s)" % (name, list(data_from.node_groups)))
        data_to.node_groups = [MAIN, FACE]

    loaded = [group for group in data_to.node_groups if group is not None]
    if len(loaded) != 2:
        raise SystemExit("appended %d group(s), expected two" % len(loaded))
    for group, name in zip(loaded, (MAIN, FACE)):
        group.name = name
    return loaded[0], loaded[1], stale


def inject_dirt(group):
    """The weather maths, inside the group, on both colour taps.

    His group reads `Diffuse` for the emission and for the lit/shaded mix, and `Shaded` for the shadow side and the
    highlight pass, so the dirt has to stand *between the sockets and every consumer*: one Mix per tap, retargeting
    each link that started at the group's own input socket. Coverage is the union of the mask's three channels (the
    maps are three independent masks, see Umashader.bind_dirt_maps), scaled by the `Dirt` row.
    """
    tree = group
    for name, socket_type, default in (("Dirt Map", 'NodeSocketColor', (0.0, 0.0, 0.0, 1.0)),
                                       ("Dirt", 'NodeSocketFloat', 0.0),
                                       ("Dirt Tint", 'NodeSocketColor', DIRT_COLOUR)):
        if name in sockets(tree):
            continue
        tree.interface.new_socket(name=name, in_out='INPUT', socket_type=socket_type).default_value = default

    group_input = next(node for node in tree.nodes if node.type == 'GROUP_INPUT')
    # a free corner: his layout is untouched, ours goes below it
    y = min((node.location.y for node in tree.nodes), default=0.0) - 400.0

    def make(bl_idname, name, x, yy, **attributes):
        node = tree.nodes.new(bl_idname)
        node.name = node.label = name
        node.location = (x, yy)
        for attribute, value in attributes.items():
            setattr(node, attribute, value)
        return node

    def math(name, operation, x, yy, clamp=False):
        node = make('ShaderNodeMath', name, x, yy)
        node.operation = operation
        node.use_clamp = clamp
        return node

    split = make('ShaderNodeSeparateColor', 'Dirt Channels', -1100.0, y)
    split.mode = 'RGB'
    tree.links.new(group_input.outputs["Dirt Map"], split.inputs["Color"])
    max_rg = math('Dirt Coverage R G', 'MAXIMUM', -900.0, y)
    tree.links.new(split.outputs["Red"], max_rg.inputs[0])
    tree.links.new(split.outputs["Green"], max_rg.inputs[1])
    max_rgb = math('Dirt Coverage RGB', 'MAXIMUM', -760.0, y)
    tree.links.new(max_rg.outputs["Value"], max_rgb.inputs[0])
    tree.links.new(split.outputs["Blue"], max_rgb.inputs[1])
    amount = math('Dirt Amount', 'MULTIPLY', -620.0, y, clamp=True)
    tree.links.new(max_rgb.outputs["Value"], amount.inputs[0])
    tree.links.new(group_input.outputs["Dirt"], amount.inputs[1])

    taps = {}
    made = set()
    for socket_name in ("Diffuse", "Shaded"):
        mix = make('ShaderNodeMix', "Dirt " + socket_name, -420.0, y - 160.0 * len(taps))
        mix.data_type = 'RGBA'
        mix.blend_type = 'MIX'
        colours = [socket for socket in mix.inputs if socket.type == 'RGBA']
        factor = next(socket for socket in mix.inputs if socket.name == "Factor" and socket.type == 'VALUE')
        tree.links.new(group_input.outputs[socket_name], colours[0])
        tree.links.new(group_input.outputs["Dirt Tint"], colours[1])
        tree.links.new(amount.outputs["Value"], factor)
        result = next(socket for socket in mix.outputs if socket.type == 'RGBA')
        taps[socket_name] = result
        made.add(mix.name)

    # now that every mix exists, move the consumers over to them - excluding the mixes themselves, whose `A` input
    # is the very socket being retargeted (doing it inline in the loop above made them read their own result)
    for socket_name, result in taps.items():
        for link in [link for link in tree.links
                     if link.from_node.type == 'GROUP_INPUT' and link.from_socket.name == socket_name
                     and link.to_node.name not in made]:
            target = link.to_socket
            tree.links.remove(link)
            tree.links.new(result, target)

    frame = make('NodeFrame', 'Dirt Frame', -1140.0, y - 200.0)
    frame.label = "Weather: dirt"
    for node in tree.nodes:
        if node.name.startswith("Dirt "):
            node.parent = frame
    return tree


def _link(tree, source_socket, node, name):
    """Point one group-node input at a socket, replacing whatever was there."""
    socket = node.inputs.get(name)
    if socket is None:
        return False
    for link in list(socket.links):
        tree.links.remove(link)
    tree.links.new(source_socket, socket)
    return True


def _default(node, name, value):
    socket = node.inputs.get(name)
    if socket is not None:
        socket.default_value = value
    return socket is not None


def rewire(material, group, base_chain, wet=True):
    """Put `material` on `group`, feeding it from the master's own wet pairs and its `Dirt Map` image node."""
    tree = material.node_tree
    node = next((node for node in tree.nodes if node.type == 'GROUP' and node.node_tree is not None
                 and node.node_tree.name.startswith(MAIN)), None)
    if node is None:
        raise SystemExit("%s has no Simple Diff group node" % material.name)
    for link in [link for link in tree.links if link.to_node == node or link.from_node == node]:
        tree.links.remove(link)
    node.node_tree = group
    node.location = (-200.0, 0.0)

    def tex(name):
        found = tree.nodes.get(name)
        return found.outputs["Color"] if found is not None and found.type == 'TEX_IMAGE' else None

    for tap, wet_mix in (("Diffuse", "Dry/Wet Diffuse"), ("Shaded", "Dry/Wet Shaded")):
        mix = tree.nodes.get(wet_mix)
        if mix is not None:
            _link(tree, next(socket for socket in mix.outputs if socket.type == 'RGBA'), node, tap)
        elif tex(tap + " Map"):
            _link(tree, tex(tap + " Map"), node, tap)
    if base_chain:
        mix = tree.nodes.get("Dry/Wet Base")
        if mix is not None:
            _link(tree, next(socket for socket in mix.outputs if socket.type == 'RGBA'), node, "Base")
        elif tex("Base Map"):
            _link(tree, tex("Base Map"), node, "Base")
    if tex("Dirt Map"):
        _link(tree, tex("Dirt Map"), node, "Dirt Map")

    # his own defaults, so a master nobody has driven looks exactly like one of his wrappers
    for name, value in (("Shadow Softness", SHADOW_SOFTNESS), ("Emission Strength", 0.0),
                        ("Highlight Color", HIGHLIGHT_COLOUR), ("Light Intensity", 0.0), ("Colored Light", 1.0),
                        ("Dirt", 0.0), ("Dirt Tint", DIRT_COLOUR)):
        _default(node, name, value)

    output = next(node for node in tree.nodes if node.bl_idname == 'ShaderNodeOutputMaterial')
    for link in list(output.inputs["Surface"].links):
        tree.links.remove(link)
    tree.links.new(next(socket for socket in node.outputs if socket.name == "Color"), output.inputs["Surface"])

    # the wrapper-side transparency chain the old group needed: his group reads the base mask itself
    for name in ("Separate Color", "Mix Shader"):
        dead = tree.nodes.get(name)
        if dead is not None:
            tree.nodes.remove(dead)
    for name in ("Reroute.004", "Reroute.005"):
        reroute = tree.nodes.get(name)
        if reroute is not None and not any(socket.links for socket in reroute.outputs):
            tree.nodes.remove(reroute)
    return node


def build_face_master():
    """`Spork Shader Face`: his copy minus the base-map chain, on the FACE group."""
    source = bpy.data.materials["Spork Shader"]
    face = bpy.data.materials.get(FACE_MASTER)
    if face is None:
        face = source.copy()
        face.name = FACE_MASTER
    else:
        face.node_tree = source.node_tree.copy()
    tree = face.node_tree
    group = bpy.data.node_groups[FACE]
    node = rewire(face, group, base_chain=False)
    for name in ("Base Map", "Wet Base Map", "Dry/Wet Base"):
        dead = tree.nodes.get(name)
        if dead is not None:
            tree.nodes.remove(dead)
    # the FACE group has no `Base`, so nothing links it; the wet pairs it does have stay
    return face, node


def main():
    if not os.path.isfile(LIBRARY) or not os.path.isfile(HIS):
        raise SystemExit("need both files: %r %r" % (LIBRARY, HIS))
    backup = LIBRARY + ".bak"
    if not os.path.exists(backup):
        shutil.copy2(LIBRARY, backup)

    bpy.ops.wm.open_mainfile(filepath=LIBRARY)
    main_group, face_group, old = package()
    say("appended %s (%d node(s), %d socket(s)) and %s (%d node(s), %d socket(s))"
        % (main_group.name, len(main_group.nodes), len(sockets(main_group)),
           face_group.name, len(face_group.nodes), len(sockets(face_group))))
    say("their sockets: %s" % sockets(main_group))

    for group in (main_group, face_group):
        inject_dirt(group)
        say("%s now offers %s" % (group.name, sockets(group)))

    for name in GENERAL_MASTERS:
        material = bpy.data.materials.get(name)
        if material is None:
            raise SystemExit("no %r in the library" % name)
        rewire(material, main_group, base_chain=True)
        say("%s: %d node(s), on %s" % (name, len(material.node_tree.nodes), main_group.name))

    inner = bpy.data.materials.get(EYE_INNER)
    if inner is not None:
        node = rewire(inner, main_group, base_chain=False)
        # the inner eye binds no base map, and a black `Base` would mix it fully transparent; white is "no mask"
        _default(node, "Base", (1.0, 1.0, 1.0, 1.0))
        say("%s: %d node(s), Base white" % (EYE_INNER, len(inner.node_tree.nodes)))

    face, face_node = build_face_master()
    say("%s: %d node(s), on %s, base-chain %s"
        % (FACE_MASTER, len(face.node_tree.nodes), face_group.name,
           "no" if face.node_tree.nodes.get("Base Map") is None else "yes"))

    carrier = bpy.data.objects.get(CARRIER)
    if carrier is None:
        raise SystemExit("no %r object to hang the masters on" % CARRIER)
    if not any(slot.material is face for slot in carrier.material_slots):
        carrier.data.materials.append(face)
    say("carrier slots: %s" % [slot.material.name for slot in carrier.material_slots if slot.material])

    for name in old:
        group = bpy.data.node_groups.get(name)
        if group is not None:
            bpy.data.node_groups.remove(group)
            say("removed the stale %r" % name)

    # a library carries slots, not textures
    unbound = 0
    for material in bpy.data.materials:
        if material.node_tree is None:
            continue
        for node in material.node_tree.nodes:
            if node.type == 'TEX_IMAGE' and node.image is not None:
                node.image = None
                unbound += 1
    say("unbound %d image node(s); %d image(s) in the file" % (unbound, len(bpy.data.images)))
    say("groups in the file: %s" % sorted((group.name, len(group.nodes)) for group in bpy.data.node_groups))

    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_mainfile(filepath=LIBRARY)
    say("wrote %s (%d byte(s))" % (LIBRARY, os.path.getsize(LIBRARY)))
    if os.path.exists(LIBRARY + "1"):
        os.remove(LIBRARY + "1")


main()
