"""What a shaded character file actually holds: the characters, their shader copies, and any displacement trick.

    blender --background --factory-startup --python Tools/probe_character_blend.py -- <file.blend> <out.txt>

Written to answer one question the handoff only describes: **what does the brow fix in the user's own character file
consist of?** It reports, for every material, the shader it came from, the part suffix `Shading` wrote into its name,
`displacement_method`, and - where the Material Output's Displacement is linked - the chain feeding it, with node
types, values and whether anything is driven. A material whose displacement is linked is the eyebrow trick; nothing
else in these files is expected to have one.
"""
import sys

import bpy

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]
path, out = argv[0], argv[1]

bpy.ops.preferences.addon_enable(module="bl_ext.user_default.uma_addon")
bpy.ops.wm.open_mainfile(filepath=path)

lines = []


def say(text=""):
    lines.append(text)


def describe_chain(tree, socket, depth=0, seen=None):
    """Walk back from a socket to the nodes that feed it."""
    seen = seen if seen is not None else set()
    for link in socket.links:
        node = link.from_node
        if node.name in seen or depth > 6:
            continue
        seen.add(node.name)
        extra = ""
        if node.type == 'VECT_MATH':
            extra = " operation=%r" % (node.operation,)
        elif node.type == 'MATH':
            extra = " operation=%r" % (node.operation,)
        elif node.type == 'VALUE':
            extra = " value=%r" % (node.outputs[0].default_value,)
        elif node.type == 'MAP_RANGE':
            extra = " from=%r to=%r" % (tuple(node.inputs["From Min"].default_value),
                                        tuple(node.inputs["To Min"].default_value))
        label = " label=%r" % node.label if node.label else ""
        say("      %s%s (%s)%s%s -> %s" % ("  " * depth, node.name, node.bl_idname, label, extra, socket.name))
        # the constants the chain turns on: an unlinked input is a number the trick reads as a setting
        for child in node.inputs:
            if not child.links and child.enabled:
                value = getattr(child, "default_value", None)
                try:
                    value = tuple(round(float(component), 6) for component in value)
                except TypeError:
                    try:
                        value = round(float(value), 6)
                    except (TypeError, ValueError):
                        value = None
                if value not in (None, 0):
                    say("      %s  .%s = %r" % ("  " * depth, child.name, value))
        for child in node.inputs:
            describe_chain(tree, child, depth + 1, seen)


say("file: %s" % path)
say("objects: %d, materials: %d" % (len(bpy.data.objects), len(bpy.data.materials)))
say()

say("characters (top-level empties):")
for obj in bpy.data.objects:
    if obj.type != 'EMPTY' or obj.parent is not None:
        continue
    children = [child.name for child in obj.children]
    props = sorted(key for key in obj.keys() if not key.startswith("_"))
    say("  %-32s children=%s" % (obj.name, children[:6]))
    if props:
        say("     properties: %s" % (props,))
say()

say("materials:")
displaced = []
for material in sorted(bpy.data.materials, key=lambda item: item.name):
    tree = getattr(material, "node_tree", None)
    if tree is None:
        continue
    output = next((node for node in tree.nodes if node.bl_idname == "ShaderNodeOutputMaterial"), None)
    linked = bool(output is not None and output.inputs["Displacement"].links)
    method = getattr(material, "displacement_method", None)
    say("  %-42s nodes=%3d source=%r method=%r displacement_linked=%s"
        % (material.name, len(tree.nodes), material.get("uma_shader_source"), method, linked))
    if linked:
        displaced.append(material.name)
        say("      every node in this material:")
        for node in tree.nodes:
            say("        %-26s %-24s label=%r" % (node.name, node.bl_idname, node.label))
        describe_chain(tree, output.inputs["Displacement"])
        drivers = list(tree.animation_data.drivers) if tree.animation_data else []
        say("      drivers on this tree: %d" % len(drivers))
        for curve in drivers:
            say("        %s[%d] expr=%r" % (curve.data_path, curve.array_index, curve.driver.expression))

say()
say("materials with a linked Displacement (%d): %s" % (len(displaced), displaced))
say()
say("material slots per mesh:")
for obj in bpy.data.objects:
    if obj.type != 'MESH':
        continue
    say("  %-30s %s" % (obj.name, [slot.material.name if slot.material else None
                                   for slot in obj.material_slots]))

text = "\n".join(lines)
with open(out, "w", encoding="utf-8") as handle:
    handle.write(text + "\n")
print("wrote %s (%d lines)" % (out, len(lines)))
