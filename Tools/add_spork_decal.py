"""Give the Spork library the unlit decal material every other shader has, so decals stop being lit body parts.

`Umashader.apply_decal_material` skips the shading loop for a decal and paints it with the library's `decal` role
instead - unlit, map x tint into an Emission, alpha-blended, which is what the game does. The Looper library has had
`Uma Decal` since 2.24; the Spork manifest never named one, so with `spork-1.0` the blush, the tear sheet and the
emoji-eye decals kept mmd_tools' own materials: lit by the scene's lights while everything around them lights from the
`Uma Sun`, i.e. always the wrong brightness, and none of them got the spin/extension handling the shaded copies do
(measured on 1001: 45 decal copies under `umashader-0.2.6`, 0 under `spork-1.0`).

The material is shader-independent by construction - no group, no lit path - so it is appended from the Looper library
rather than rebuilt, and it keeps its node names (`Decal Map`, `UMA Decal Opacity`, `UMA Decal Tint`), which is what
the decal recipe addresses. Its slot goes on the carrier sphere because that is where `Umashader.library_masters`
reads a role's master from.

Idempotent: a library that already carries `Uma Decal` is left alone.

    blender --background --factory-startup --python Tools/add_spork_decal.py -- [<spork library> [<looper library>]]
"""
import os
import shutil
import sys

import bpy

BASE = (r"C:\Users\user\AppData\Roaming\Blender Foundation\Blender\5.2\extensions\user_default"
        r"\uma_addon\uma_addon\addons\uma_addon\operators\shaders")

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
LIBRARY = argv[0] if argv else os.path.join(BASE, "SporkShaders.blend")
SOURCE = argv[1] if len(argv) > 1 else os.path.join(BASE, "Umashaders-0.2.6.blend")

DECAL = "Uma Decal"
CARRIER = "Icosphere of Materials"


def main():
    for path in (LIBRARY, SOURCE):
        if not os.path.isfile(path):
            raise SystemExit("no file at %r" % path)
    backup = LIBRARY + ".bak"
    if not os.path.exists(backup):
        shutil.copy2(LIBRARY, backup)

    bpy.ops.wm.open_mainfile(filepath=LIBRARY)
    if bpy.data.materials.get(DECAL) is not None:
        print("%s: already carries %r, nothing to do" % (os.path.basename(LIBRARY), DECAL))
        return

    with bpy.data.libraries.load(SOURCE, link=False) as (data_from, data_to):
        if DECAL not in data_from.materials:
            raise SystemExit("%r has no %r material" % (SOURCE, DECAL))
        data_to.materials = [DECAL]
    material = bpy.data.materials[DECAL]
    if material is None:
        raise SystemExit("the append produced no material")

    # a library is copied *from*: no tag that says a scene is shaded with it, and no images
    if "uma_shader_source" in material.keys():
        del material["uma_shader_source"]
    unbound = 0
    for node in material.node_tree.nodes:
        if node.type == 'TEX_IMAGE' and node.image is not None:
            node.image = None
            unbound += 1

    carrier = bpy.data.objects.get(CARRIER)
    if carrier is None:
        raise SystemExit("no %r object to hang the master on" % CARRIER)
    slotted = any(slot.material is material for slot in carrier.material_slots)
    if not slotted:
        carrier.data.materials.append(material)

    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_mainfile(filepath=LIBRARY)
    if os.path.exists(LIBRARY + "1"):
        os.remove(LIBRARY + "1")
    print("%s: appended %r (%d node(s), %d image node(s) unbound, slotted=%s), now %d byte(s)"
          % (os.path.basename(LIBRARY), DECAL, len(material.node_tree.nodes), unbound, slotted,
             os.path.getsize(LIBRARY)))
    print("carrier slots: %s" % [slot.material.name for slot in carrier.material_slots if slot.material])


main()
