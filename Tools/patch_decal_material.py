"""Add `Uma Decal` to a shader library: a decal that is painted, not lit.

The problem it solves: decals keep mmd_tools' own material, whose lighting comes from the *scene's* lights, while the
shaded body lights from the `Uma Sun` empty with a threshold mask. Two lighting models, so a decal is always darker or
lighter than the face it sits on, no matter how the scene is lit. The game does not light its decals at all - they are
painted overlays - and neither does this: texture x tint into an Emission, alpha-blended.

Measured on 1001 before writing it (the probe that failed first is worth the note: `image.pixels[:4000]` reads a few
rows of a 256x256 map, which for a decal is transparent margin, so it reports "alpha is zero everywhere" about maps
that are 43-84% transparent):

    material                   map                          alpha min/mean/max   transparent%
    cheek (blend=multiply)     tex_chr1001_00_cheek0_blush   0.000/0.060/0.553       76.4%
    static tear                tex_chr_tear00                0.000/0.109/1.000       84.4%
    tear sheet (scroll=8x16)   tex_chr_tear001               0.000/0.142/1.000       50.3%
    manga eyes x4              tex/mtl_eff_chr_emo_eye_00N   0.000/0.15-0.56/1.000   43-84%

and every one of them is 32-bit with `mmd_material.alpha = 1.0` - the transparency is per pixel in the map, not a
uniform on the material. So the graph takes the alpha from the map, which is what mmd_tools' own shader does too.

Two knobs, named so a settings row can address them (`target: {"node": "UMA Decal Opacity", "socket": "Value"}` and
`{"node": "UMA Decal Tint", "socket": "Color"}`): opacity multiplies the map's alpha, tint multiplies its colour.
Nothing else is exposed - the map is the look.

The material carries no image and is copied *from*: the apply step copies it per decal slot and binds that slot's own
map, exactly as the shaded parts do.
"""
import os
import shutil

import bpy

BASE = r"C:\Users\user\AppData\Roaming\Blender Foundation\Blender\5.2\extensions\user_default\uma_addon\uma_addon\addons\uma_addon\operators\shaders"
LIBRARY = "Umashaders-0.2.6.blend"
TARGET = "Uma Decal"


def _node(tree, bl_idname, name, location, label=None):
    node = tree.nodes.get(name)
    if node is None or node.bl_idname != bl_idname:
        node = tree.nodes.new(bl_idname)
        node.name = name
    node.label = label or name
    node.location = location
    return node


def patch(path):
    backup = path + ".bak"
    if not os.path.exists(backup):
        shutil.copy2(path, backup)
    bpy.ops.wm.open_mainfile(filepath=path)

    created = bpy.data.materials.get(TARGET) is None
    decal = bpy.data.materials.get(TARGET) or bpy.data.materials.new(TARGET)
    if decal.name != TARGET:
        decal.name = TARGET
    decal.use_nodes = True
    tree = decal.node_tree
    for node in list(tree.nodes):
        # rebuilt from scratch every run: this graph is ours, and a half-edited one is worse than a fresh one
        tree.nodes.remove(node)

    output = _node(tree, "ShaderNodeOutputMaterial", "Material Output", (400, 0), "Material Output")
    mix_shader = _node(tree, "ShaderNodeMixShader", "UMA Decal Mix", (200, 0), "Decal alpha")
    emission = _node(tree, "ShaderNodeEmission", "UMA Decal Emission", (0, 140), "Unlit")
    transparent = _node(tree, "ShaderNodeBsdfTransparent", "UMA Decal Clear", (0, -60), "Clear")
    multiply = _node(tree, "ShaderNodeMixRGB", "UMA Decal Colour", (-240, 240), "Map x tint")
    tint = _node(tree, "ShaderNodeRGB", "UMA Decal Tint", (-480, 160), "UMA Decal Tint")
    alpha = _node(tree, "ShaderNodeMath", "UMA Decal Alpha", (-240, -40), "Map alpha x opacity")
    opacity = _node(tree, "ShaderNodeValue", "UMA Decal Opacity", (-480, -120), "UMA Decal Opacity")
    image = _node(tree, "ShaderNodeTexImage", "Image Texture", (-700, 120), "Image Texture")
    uv = _node(tree, "ShaderNodeUVMap", "UV Map", (-900, 120), "UV Map")

    multiply.blend_type = 'MULTIPLY'
    multiply.inputs["Fac"].default_value = 1.0
    alpha.operation = 'MULTIPLY'
    tint.outputs[0].default_value = (1.0, 1.0, 1.0, 1.0)
    opacity.outputs[0].default_value = 1.0
    emission.inputs["Strength"].default_value = 1.0
    image.image = None
    try:
        image.image_user.frame_duration = 1
    except AttributeError:
        pass

    uv.uv_map = "UVMap"
    for link in list(tree.links):
        tree.links.remove(link)
    tree.links.new(uv.outputs["UV"], image.inputs["Vector"])
    tree.links.new(image.outputs["Color"], multiply.inputs["Color1"])
    tree.links.new(tint.outputs[0], multiply.inputs["Color2"])
    tree.links.new(multiply.outputs["Color"], emission.inputs["Color"])
    tree.links.new(image.outputs["Alpha"], alpha.inputs[0])
    tree.links.new(opacity.outputs[0], alpha.inputs[1])
    tree.links.new(alpha.outputs[0], mix_shader.inputs["Fac"])
    tree.links.new(transparent.outputs[0], mix_shader.inputs[1])
    tree.links.new(emission.outputs[0], mix_shader.inputs[2])
    tree.links.new(mix_shader.outputs[0], output.inputs["Surface"])

    # a decal is drawn over what is behind it, is seen from both sides (the right-eye copies are mirrored, which
    # reverses their winding), and must not cast a solid shadow
    decal.surface_render_method = 'BLENDED'
    decal.use_backface_culling = False
    decal.use_transparent_shadow = True
    for stray in ("uma_shader_source", "uma_shader_role"):
        if stray in decal.keys():
            del decal[stray]

    # a slot on the carrier sphere, which is where `Umashader.library_masters` reads a role from
    slotted = 0
    for obj in bpy.data.objects:
        if obj.type != 'MESH' or not obj.material_slots:
            continue
        if any(slot.material is decal for slot in obj.material_slots):
            slotted += 1
            continue
        if any(slot.material is not None and slot.material.name.startswith(("Uma Shader", "Uma Eyes"))
               for slot in obj.material_slots):
            obj.data.materials.append(decal)
            slotted += 1

    bpy.ops.wm.save_mainfile(filepath=path)
    return ("%s: %s %r (%d nodes, %s, tint+opacity knobs, %d carrier slot(s))"
            % (os.path.basename(path), "added" if created else "rebuilt", TARGET, len(tree.nodes),
               decal.surface_render_method, slotted))


print(patch(os.path.join(BASE, LIBRARY)))
