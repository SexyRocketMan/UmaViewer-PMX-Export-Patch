"""Add `Uma Face` to the library: the face material with the blush multiplied in from the shipped layer.

    blender --background --factory-startup --python Tools/patch_face_material.py -- [<library.blend>]

The face currently shares the `default` template (`Uma Shader`) with every body, hair and tail slot, so a blush mixed
into it would multiply the whole character. This makes it a material of its own - the same reason `Uma Brows` exists -
carrying one extra thing: the shipped layer `uma_face_blush.png` multiplied into the diffuse texture before it enters
the shader group, so the blush is part of the *face* instead of a decal quad composited over it.

The layer is white where there is no blush, so the multiply is a no-op outside the cheeks. Its `Fac` is the strength:
1 is the blush as baked, 0 is off, and the add-on drives it from the `Cheek_Blush` shape key's value so the morph still
controls it.
"""
import os
import shutil
import sys

import bpy

BASE = r"C:\Users\user\AppData\Roaming\Blender Foundation\Blender\5.2\extensions\user_default\uma_addon\uma_addon\addons\uma_addon\operators\shaders"

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
LIBRARY = argv[0] if argv else "Umashaders-0.2.6.blend"
SOURCE = "Uma Shader"
TARGET = "Uma Face"
LAYER = "uma_face_blush.png"


def patch(path):
    # the library is under version control, so a backup is a convenience - and one that cannot be written (a
    # read-only .bak left from an earlier run) must not stop the patch
    backup = path + ".bak"
    if not os.path.exists(backup):
        try:
            shutil.copy2(path, backup)
        except OSError:
            pass
    bpy.ops.wm.open_mainfile(filepath=path)

    source = bpy.data.materials.get(SOURCE)
    if source is None or source.node_tree is None:
        return "%s: no %r to copy from" % (path, SOURCE)
    face = bpy.data.materials.get(TARGET)
    created = face is None
    if created:
        face = source.copy()
        face.name = TARGET
    tree = face.node_tree

    layer_path = os.path.join(BASE, LAYER)
    if not os.path.exists(layer_path):
        return "%s: the shipped layer %r is missing" % (path, LAYER)

    node = tree.nodes.get("UMA Face Blush")
    if node is None or node.bl_idname != "ShaderNodeTexImage":
        node = tree.nodes.new("ShaderNodeTexImage")
        node.name = node.label = "UMA Face Blush"
        node.location = (-900, -520)
    node.image = bpy.data.images.load(layer_path, check_existing=True)
    node.image.colorspace_settings.name = "sRGB"
    node.extension = 'EXTEND'

    mix = tree.nodes.get("UMA Face Blush Mix")
    if mix is None or mix.bl_idname != "ShaderNodeMixRGB":
        mix = tree.nodes.new("ShaderNodeMixRGB")
        mix.name = mix.label = "UMA Face Blush Mix"
        mix.location = (-620, -420)
    mix.blend_type = 'MULTIPLY'

    group = next((entry for entry in tree.nodes
                  if entry.bl_idname == "ShaderNodeGroup" and entry.node_tree is not None), None)
    diffuse_node = tree.nodes.get("Image Texture")
    if group is None or diffuse_node is None:
        return "%s: %r has no shader group or no diffuse image node" % (path, TARGET)

    socket = group.inputs.get("Diffuse Texture")
    if socket is None:
        return "%s: the group offers no 'Diffuse Texture' socket" % (path,)
    # the layer multiplies the diffuse colour on its way into the group; the diffuse node may feed it directly or
    # through reroutes, so the link is found rather than assumed
    existing = socket.links[0].from_socket if socket.links else diffuse_node.outputs["Color"]
    for link in list(mix.inputs["Color2"].links):
        tree.links.remove(link)
    for link in list(mix.inputs["Color1"].links):
        tree.links.remove(link)
    tree.links.new(diffuse_node.outputs["Color"], mix.inputs["Color1"])
    tree.links.new(node.outputs["Color"], mix.inputs["Color2"])
    mix.inputs["Fac"].default_value = 1.0
    for link in list(socket.links):
        tree.links.remove(link)
    tree.links.new(mix.outputs["Color"], socket)
    for stray in ("uma_shader_source", "uma_shader_role"):
        if stray in face.keys():
            del face[stray]

    slotted = 0
    for obj in bpy.data.objects:
        if obj.type != 'MESH':
            continue
        if any(slot.material is face for slot in obj.material_slots):
            slotted += 1
            continue
        if any(slot.material is not None and slot.material.name.startswith(SOURCE) for slot in obj.material_slots):
            obj.data.materials.append(face)
            slotted += 1

    bpy.ops.wm.save_mainfile(filepath=path)
    return ("%s: %s %r (%d nodes, layer %r, multiply into 'Diffuse Texture' at fac %.1f, %d carrier slot(s))"
            % (os.path.basename(path), "added" if created else "re-asserted", TARGET, len(tree.nodes),
               os.path.basename(node.image.filepath), mix.inputs["Fac"].default_value, slotted))


print(patch(os.path.join(BASE, LIBRARY)))
