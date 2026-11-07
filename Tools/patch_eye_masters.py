"""Patch the eye masters in the shader libraries with the game's highlight motion.

The game's own eye shader (decompiled: `UMA_Eye.fx`, see docs/UMA_EYE_SHADER.md in the exporter repository) does
one thing to the highlights: it rotates the UV they are sampled at, per layer, about a centre in UV space, by an
animated angle. So the patch is one rotate-about-a-centre node per highlight map, its Z angle driven from the
frame - no helper nodes, no translations.

Rotate about a centre rather than the origin is what `ShaderNodeVectorRotate` is (a Mapping node rotates about
the origin and would need the centre faked with a location offset, which only works for a fixed angle).

The rates are the one thing the shader does not tell us - they are uniforms the game's code animates - so
ROTATE_RATE / ROTATE_AMPLITUDE below are the two numbers to tune, and the defaults are the smallest reasonable
tremble: about 20 degrees at a roughly 8 frame period, with a faster small wobble on top so it never reads as a
loop.

Run by hand; it edits the shader source rather than the shaded copies, so every copy inherits the motion and a
Shading run has nothing to do. Backs the library up as .bak before writing and skips a library already patched.
"""
import os
import shutil

import bpy

BASE = r"C:\Users\user\AppData\Roaming\Blender Foundation\Blender\5.2\extensions\user_default\uma_addon\uma_addon\addons\uma_addon\operators"

#: Library file -> the eye master's material name in it.
LIBRARIES = {
    "Umashaders-0.2.6.blend": "Uma Eyes",
    "SporkShaders.blend": "Spork Eye",
}

#: Radians per frame for the slow sweep, and how much the two layers swing around it.
ROTATE_RATE = 0.16
ROTATE_AMPLITUDE = 0.35

#: Radians per frame for the small extra wobble, and its amplitude.
WOBBLE_RATE = 1.3
WOBBLE_AMPLITUDE = 0.04


def _highlight_nodes(tree):
    """The eye master's highlight image nodes: every `Image Texture.NNN` (the diffuse is `Image Texture`)."""
    return [node for node in tree.nodes
            if node.type == "TEX_IMAGE" and node.name.startswith("Image Texture")
            and node.name != "Image Texture"]


def patch(path, master):
    backup = path + ".bak"
    if not os.path.exists(backup):
        shutil.copy2(path, backup)
    bpy.ops.wm.open_mainfile(filepath=path)
    material = bpy.data.materials.get(master)
    if material is None or material.node_tree is None:
        return master + ": not found"
    tree = material.node_tree
    if any(node.label and node.label.startswith("UMA Hi") for node in tree.nodes):
        return master + ": already patched"

    patched = 0
    for index, node in enumerate(_highlight_nodes(tree)):
        rotate = tree.nodes.new("ShaderNodeVectorRotate")
        rotate.name = "UMA Hi %d" % index
        rotate.label = "UMA Hi %d" % index
        rotate.rotation_type = "Z_AXIS"
        rotate.location = (node.location.x - 200, node.location.y)
        # the game's own centre: `uv - 0.5 + param.xy`, back into 0..1, i.e. a rotation about the middle of the
        # map; `_Offset` is the per-eye shift and is left at zero here
        rotate.inputs["Center"].default_value = (0.5, 0.5, 0.0)
        source = node.inputs["Vector"]
        if source.links:
            tree.links.new(source.links[0].from_socket, rotate.inputs["Vector"])
        else:
            uv = tree.nodes.new("ShaderNodeUVMap")
            uv.location = (rotate.location.x - 180, rotate.location.y)
            uv.uv_map = "UVMap"
            tree.links.new(uv.outputs["UV"], rotate.inputs["Vector"])
        tree.links.new(rotate.outputs["Vector"], source)
        driver = rotate.inputs["Angle"].driver_add("default_value").driver
        driver.type = "SCRIPTED"
        # the sweep the game does with `param.z`, and a small faster wobble on top: both driven by the frame, so
        # the highlights are never still
        driver.expression = "frame * %.4f + sin(frame * %.4f) * %.4f + sin(frame * %.4f) * %.4f" % (
            ROTATE_RATE, ROTATE_RATE * 4.0, ROTATE_AMPLITUDE, WOBBLE_RATE, WOBBLE_AMPLITUDE)
        patched += 1
    bpy.ops.wm.save_mainfile(filepath=path)
    return "%s: %d highlight node(s) patched" % (master, patched)


for library, master in LIBRARIES.items():
    print(patch(os.path.join(BASE, library), master))
