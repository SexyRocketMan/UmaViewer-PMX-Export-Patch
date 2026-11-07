"""Port our wet-eye highlight eye into Spork's library, as his `Spork Eye`.

Spork's eye material is the one thing in his file that needs somebody else's nodes, and it is the *same* job ours
already does - so the eye is ours, dropped in unchanged, rather than a second highlight system written against his
graph:

    Umashaders-0.2.6.blend : "Uma Eyes"          -> SporkShaders.blend : "Spork Eye"
    his old "Spork Eye"    -> renamed, kept in the file as "... (Spork's own)"

What comes with it (measured before: appending a material appends the node groups it uses): `NoiseGenerator` and the
two `SmoothThreshold` groups, which are what a Spork character never had.

Three things are asserted rather than assumed, because a library material is copied *from* and a wrong assumption
here is invisible until somebody shades:

- **no images and no `uma_shader_source`**: a library carries slots, not textures, and the apply step loads the
  images from the model's folder;
- **`CrySwitch` 0**: a fresh file rests dry (the state 0.2.6 ships in, see `e82b79a`);
- **the carrier sphere's slot points at it**, because that sphere's *slots* are where `Umashader.library_masters`
  reads a role's master from - a material in the file but in no slot is invisible to the apply step.

His own eye is kept, not deleted: it carries `mmd_tex_uv` and two `AOV Output`s that our graph does not (ours is a
self-contained Emission graph), so throwing it away would throw away the only copy of that setup in the library. It
is renamed so nothing can mistake it for the one the manifest names.

**Also measured, and this one contradicts the handoff:** his `textures.eye` recipe binds the *main* eye texture to a
node called `mmd_base_tex` and the first highlight to `Image Texture`, while ours are `Image Texture` and
`Image Texture.001..003`. So dropping our graph in means the recipe has to be rewritten as well, or the main eye
texture would be bound *into the first highlight slot*.
"""
import os
import shutil

import bpy

BASE = r"C:\Users\user\AppData\Roaming\Blender Foundation\Blender\5.2\extensions\user_default\uma_addon\uma_addon\addons\uma_addon\operators\shaders"
SOURCE_BLEND = "Umashaders-0.2.6.blend"
SOURCE_MATERIAL = "Uma Eyes"
TARGET_BLEND = "SporkShaders.blend"
TARGET_MATERIAL = "Spork Eye"
KEPT_NAME = "Spork Eye (Spork's own)"


def _is_ported(material):
    """True when this material is already the highlight graph, so a second run does not stack two of them."""
    if material is None or material.node_tree is None:
        return False
    nodes = material.node_tree.nodes
    return (nodes.get("Value") is not None and nodes.get("Color.001") is not None
            and any(node.type == 'GROUP' and node.node_tree is not None
                    and node.node_tree.name == "NoiseGenerator" for node in nodes))


def port():
    target_path = os.path.join(BASE, TARGET_BLEND)
    backup = target_path + ".bak"
    if not os.path.exists(backup):
        shutil.copy2(target_path, backup)
    bpy.ops.wm.open_mainfile(filepath=target_path)

    current = bpy.data.materials.get(TARGET_MATERIAL)
    if _is_ported(current):
        return "%s: %r is already the highlight graph (%d nodes)" % (
            TARGET_BLEND, TARGET_MATERIAL, len(current.node_tree.nodes))

    before = set(bpy.data.materials)
    with bpy.data.libraries.load(os.path.join(BASE, SOURCE_BLEND), link=False) as (data_from, data_to):
        data_to.materials = [SOURCE_MATERIAL]
    appended = [material for material in bpy.data.materials if material not in before]
    if not appended:
        return "%s: %r did not append from %s" % (TARGET_BLEND, SOURCE_MATERIAL, SOURCE_BLEND)
    eye = appended[0]
    groups = sorted({node.node_tree.name for node in eye.node_tree.nodes
                     if node.type == 'GROUP' and node.node_tree is not None})

    # his own eye first, so the name is free - and kept, under a name that says whose it is. The fake user is not
    # decoration: moving the carrier slot to the ported material takes away the old one's last real user, and Blender
    # drops a zero-user datablock when it saves - measured, the first run of this script left the material in the
    # session and *not* in the file.
    if current is not None:
        current.name = KEPT_NAME
        current.use_fake_user = True
    eye.name = TARGET_MATERIAL
    for node in eye.node_tree.nodes:
        if node.type == 'TEX_IMAGE':
            node.image = None
    if "uma_shader_source" in eye.keys():
        del eye["uma_shader_source"]
    if "uma_shader_role" in eye.keys():
        del eye["uma_shader_role"]

    # a fresh library rests dry: `CrySwitch` 0 turns the threshold drop and the tremble off
    cry = eye.node_tree.nodes.get("Value")
    if cry is not None:
        cry.outputs[0].default_value = 0.0
    eye.displacement_method = 'BUMP'

    # the carrier sphere's slots are where a role's master is read from
    slotted = 0
    for obj in bpy.data.objects:
        if obj.type != 'MESH':
            continue
        for slot in obj.material_slots:
            if slot.material is current:
                slot.material = eye
                slotted += 1
    if not slotted:
        for obj in bpy.data.objects:
            if obj.type == 'MESH' and any(slot.material is not None and
                                          slot.material.name.startswith("Spork ")
                                          for slot in obj.material_slots):
                obj.data.materials.append(eye)
                slotted += 1

    bpy.ops.wm.save_mainfile(filepath=target_path)
    return ("%s: %r -> %r (%d nodes, groups %s, CrySwitch %.1f, %d carrier slot(s)); his own kept as %r"
            % (TARGET_BLEND, SOURCE_MATERIAL, eye.name, len(eye.node_tree.nodes), groups,
               cry.outputs[0].default_value if cry is not None else -1.0, slotted, KEPT_NAME))


print(port())
