"""Add `Uma Brows` to a shader library: the eyebrow surfaces drawn in front of the hair.

The trick is the user's own, measured in their character file (`spe_configured.blend`, material
`mtl_chr1001_00_mayu [face]`) and read straight out of it rather than remembered:

    Geometry.Incoming -> Vector Math (SCALE, Scale 0.05) -> Material Output.Displacement
    material.displacement_method = 'BOTH'

`Incoming` is the view vector, so a brow vertex is nudged 0.05 towards the camera at draw time and the depth test
lets the brows win against the fringe in front of them. It is **vertex** displacement - a 4-vertex plane shows
nothing, a subdivided one moves - and it is view-dependent by construction, so it moves with the camera instead of
with the model. Measured on the user's file with flat materials: 106 brow pixels without it, 970 with it.

It has to be a material of its own. The brows match the `face` rule, so before this they took the shared `default`
template (`Uma Shader`) - the same material every body and hair slot wears - and displacing *that* would push the
whole character towards the camera. So the library gets a `Uma Brows` copy, the manifest gets a `brows` role and a
`brows` part rule **before** `face` (a part rule is the first substring that matches, and the `face` rule lists
`mayu` itself), and everything else about the brows stays the face's own shading.

The copy is a copy of `Uma Shader` on purpose: it keeps the `Uma Shader` group node and the five image node names the
`default` texture recipe addresses, so `Shading` binds the brow textures exactly as it binds a face's.

The material goes into a slot on the library's carrier sphere as well, because that sphere's *slots* are where
`Umashader.library_masters` reads a role's master material from - a material in the file but in no slot is invisible
to the apply step.

Run by hand, like `patch_eye_masters.py`: it edits the library, so every copy shaded afterwards inherits it. Backs
the library up as `.bak` and re-asserts the graph on a library that already has it (idempotent, so a re-run after a
shader update puts it back).
"""
import os
import shutil

import bpy

BASE = r"C:\Users\user\AppData\Roaming\Blender Foundation\Blender\5.2\extensions\user_default\uma_addon\uma_addon\addons\uma_addon\operators\shaders"

#: The library to patch, the master the brows copy, and the name the manifest's `brows` role will name.
LIBRARY = "Umashaders-0.2.6.blend"
SOURCE = "Uma Shader"
TARGET = "Uma Brows"

#: How far along the view vector a brow vertex is pushed, in the file's own units. The user's number, unchanged: the
#: export has no physical unit (`docs/HANDOFF_ADVANCED_MORPHS.md`), so this is "far enough to beat the fringe and not
#: far enough to look detached", and it was tuned against the same character this add-on shades.
OFFSET = 0.05

#: The frame the user put the nodes in, kept as a label so the graph explains itself in the editor.
LABEL = "Eyebrows in front"


def _ensure_node(tree, bl_idname, name, location, **attributes):
    """One node, by name - created when the library does not have it yet, re-asserted when it does."""
    node = tree.nodes.get(name)
    if node is None or node.bl_idname != bl_idname:
        node = tree.nodes.new(bl_idname)
        node.name = name
        node.label = name
    node.location = location
    for attribute, value in attributes.items():
        setattr(node, attribute, value)
    return node


def patch(path):
    """Give `path` its `Uma Brows` material, and return what was done. Idempotent."""
    backup = path + ".bak"
    if not os.path.exists(backup):
        shutil.copy2(path, backup)
    bpy.ops.wm.open_mainfile(filepath=path)

    source = bpy.data.materials.get(SOURCE)
    if source is None or source.node_tree is None:
        return "%s: no %r material to copy from" % (path, SOURCE)
    brows = bpy.data.materials.get(TARGET)
    created = brows is None
    if created:
        brows = source.copy()
        brows.name = TARGET

    tree = brows.node_tree
    # the same two nodes the user made, in the same order, with the frame that says what they are for
    geometry = _ensure_node(tree, "ShaderNodeNewGeometry", "Geometry", (-420, -320))
    scale = _ensure_node(tree, "ShaderNodeVectorMath", "Vector Math", (-200, -320))
    scale.operation = 'SCALE'
    scale.inputs["Scale"].default_value = OFFSET
    output = next((node for node in tree.nodes if node.bl_idname == "ShaderNodeOutputMaterial"), None)
    if output is None:
        return "%s: %r has no material output" % (path, TARGET)

    frame = next((node for node in tree.nodes if node.bl_idname == "NodeFrame" and node.label == LABEL), None)
    if frame is None:
        frame = tree.nodes.new("NodeFrame")
        frame.label = LABEL
    for node in (geometry, scale):
        node.parent = frame

    # `Incoming` is the view vector - `Normal` would push the brows out along the face instead, which does not move
    # them against the hair at all. Re-linked every run so a half-linked copy is repaired rather than trusted.
    for link in [link for link in scale.inputs["Vector"].links]:
        tree.links.remove(link)
    for link in [link for link in output.inputs["Displacement"].links]:
        tree.links.remove(link)
    tree.links.new(geometry.outputs["Incoming"], scale.inputs["Vector"])
    tree.links.new(scale.outputs["Vector"], output.inputs["Displacement"])
    brows.displacement_method = 'BOTH'

    # a library material is copied *from*, never worn: it carries no shader tag and no texture of its own
    if "uma_shader_source" in brows.keys():
        del brows["uma_shader_source"]
    for node in tree.nodes:
        if node.type == 'TEX_IMAGE' and node.image is not None:
            node.image = None

    # the sphere's slots are where `Umashader.library_masters` looks a role up
    carriers = [obj for obj in bpy.data.objects
                if obj.type == 'MESH' and any(SOURCE in slot.material.name for slot in obj.material_slots
                                              if slot.material is not None)]
    slotted = False
    for carrier in carriers:
        if any(slot.material is brows for slot in carrier.material_slots):
            continue
        carrier.data.materials.append(brows)
        slotted = True

    bpy.ops.wm.save_mainfile(filepath=path)
    return ("%s: %s %r (%d nodes, displacement %s at %.3f, %s)"
            % (os.path.basename(path), "added" if created else "re-asserted", TARGET, len(tree.nodes),
               "linked" if output.inputs["Displacement"].links else "UNLINKED", OFFSET,
               "in %d slot(s)" % sum(len([slot for slot in obj.material_slots if slot.material is brows])
                                     for obj in carriers) if slotted or carriers else "no carrier sphere"))


print(patch(os.path.join(BASE, LIBRARY)))
