"""Report what a shader library actually holds, so a plan about it rests on the file rather than on the handoff.

    blender --background --factory-startup --python Tools/probe_shader_library.py -- <library.blend> <out.txt> [<material name> ...]

Prints the collections, the carrier objects and their material slots (the sphere is what holds a role's master
material - `Umashader.library_masters`), then for each named material its node count, node names, bound images,
node-tree drivers and the output's displacement method. Names passed after the output path are the materials
reported in full; with none, every material in the file is reported in brief.
"""
import sys

import bpy

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]
if len(argv) < 2:
    raise SystemExit("usage: <library.blend> <out.txt> [<material> ...]")
path, out, wanted = argv[0], argv[1], argv[2:]

bpy.ops.wm.open_mainfile(filepath=path)

lines = []


def say(text=""):
    lines.append(text)


say("file: %s" % path)
say("collections:")
for collection in bpy.data.collections:
    say("  %-45s objects=%d tagged=%r" % (collection.name, len(collection.all_objects),
                                          collection.get("uma_shader_source")))
say()
say("carrier objects and their material slots:")
for obj in bpy.data.objects:
    if obj.type != 'MESH':
        continue
    say("  %-30s slots=%d modifiers=%s" % (obj.name, len(obj.material_slots),
                                          [m.type for m in obj.modifiers]))
    for slot in obj.material_slots:
        say("      %r" % (slot.material.name if slot.material else None,))
say()
say("materials in file: %d" % len(bpy.data.materials))
for material in bpy.data.materials:
    tree = material.node_tree
    say("  %-38s nodes=%s users=%d" % (material.name, len(tree.nodes) if tree else 0, material.users))
say()
say("node groups in file: %d" % len(bpy.data.node_groups))
for group in sorted(bpy.data.node_groups, key=lambda item: item.name):
    interface = [(socket.name, socket.in_out) for socket in group.interface.items_tree
                 if getattr(socket, "in_out", None) == 'INPUT']
    say("  %-38s nodes=%3d inputs=%s" % (group.name, len(group.nodes), [name for name, _ in interface]))
say()

report = wanted or [material.name for material in bpy.data.materials]
for name in report:
    material = bpy.data.materials.get(name)
    say("=" * 90)
    if material is None:
        say("MISSING material %r" % name)
        continue
    tree = material.node_tree
    say("material %r: nodes=%d" % (material.name, len(tree.nodes) if tree else 0))
    say("  displacement_method = %r" % (getattr(material, "displacement_method", "n/a"),))
    if tree is None:
        continue
    say("  nodes:")
    for node in tree.nodes:
        extra = ""
        if node.type == 'TEX_IMAGE':
            extra = " image=%r" % (node.image.name if node.image else None,)
        elif node.type == 'GROUP':
            extra = " tree=%r" % (node.node_tree.name if node.node_tree else None,)
        label = " label=%r" % node.label if node.label else ""
        say("    %-26s %-18s%s%s" % (node.name, node.bl_idname, label, extra))
    say("  group nodes' sockets:")
    for node in tree.nodes:
        if node.type == 'GROUP' and node.node_tree is not None:
            say("    group %r (%s)" % (node.name, node.node_tree.name))
            for index, socket in enumerate(node.inputs):
                say("      in[%d] %-42s %s default=%r" % (index, socket.name, socket.type,
                                                          getattr(socket, "default_value", None)))
    animations = tree.animation_data
    drivers = list(animations.drivers) if animations else []
    say("  node-tree drivers: %d" % len(drivers))
    for curve in drivers:
        say("    %s index=%d vars=%d expr=%r" % (curve.data_path, curve.array_index,
                                                 len(curve.driver.variables), curve.driver.expression))
    output = next((node for node in tree.nodes if node.bl_idname == "ShaderNodeOutputMaterial"), None)
    if output is not None:
        for index, socket in enumerate(output.inputs):
            say("  output in[%d] %-20s linked=%s" % (index, socket.name,
                                                     [link.from_node.name for link in socket.links]))
    say("  images bound: %s" % sorted({node.image.name for node in tree.nodes
                                       if node.type == 'TEX_IMAGE' and node.image}))

text = "\n".join(lines)
with open(out, "w", encoding="utf-8") as handle:
    handle.write(text + "\n")
print("wrote %s (%d lines)" % (out, len(lines)))
