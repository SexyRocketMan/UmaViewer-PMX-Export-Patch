"""Put the Spork masters' *render settings* on the same footing as the Looper masters', which is what stops the
see-through.

`spork-1.0` shaded characters came out looking inside-out: parts behind a surface were painted over the front of it,
and unplugging one of a body's two coplanar material slots made it go away. This is a draw-order artefact, not
geometry: the masters were copied from his own wrapper material in 2.34 and kept its settings - `blend_method` BLEND,
`surface_render_method` BLENDED, `show_transparent_back` True - so every copy of the shader (body, face, hair, tail,
brows) is drawn in EEVEE's **blended pass**, which does not write depth. Two coplanar body slots then composite in
whatever order EEVEE picks, and the one whose triangles were drawn last appears on top, which reads as inverted
normals. Measured: switching the three masters to the Looper masters' settings changes **30.9% of the pixels inside
the silhouette** on the 1001 export (+27.8/255 brighter), and the Looper shader - which has never shown the artefact -
ships exactly these settings.

The Looper masters are `HASHED` / `DITHERED` / `show_transparent_back` False / no backface culling, so the surface
writes depth and the cutout is resolved per pixel instead of by draw order. This script flips the three Spork masters
(`Spork Shader`, `Spork Shader Face`, `Uma Brows`) to the same, and leaves the eyes, decals and outline alone: those
are single-layer overlays where blending is the correct answer and the Looper library blends them too.

    blender --background --factory-startup --python Tools/spork_render_settings.py -- [<spork library>]
"""
import os
import shutil
import sys

import bpy

BASE = (r"C:\Users\user\AppData\Roaming\Blender Foundation\Blender\5.2\extensions\user_default"
        r"\uma_addon\uma_addon\addons\uma_addon\operators\shaders")

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
LIBRARY = argv[0] if argv else os.path.join(BASE, "SporkShaders.blend")

#: The masters that shade a solid surface, and the settings the Looper library's `Uma Shader`/`Uma Brows` carry.
SOLID = ("Spork Shader", "Spork Shader Face", "Uma Brows")
SETTINGS = {"blend_method": 'HASHED', "surface_render_method": 'DITHERED',
            "show_transparent_back": False, "use_backface_culling": False}


def main():
    if not os.path.isfile(LIBRARY):
        raise SystemExit("no file at %r" % LIBRARY)
    backup = LIBRARY + ".bak"
    if not os.path.exists(backup):
        shutil.copy2(LIBRARY, backup)

    bpy.ops.wm.open_mainfile(filepath=LIBRARY)
    print("%-22s %-9s %-10s %-7s %s" % ("material", "blend", "render", "back", "cull"))
    for name in SOLID:
        material = bpy.data.materials.get(name)
        if material is None:
            raise SystemExit("no %r in the library" % name)
        before = (material.blend_method, getattr(material, "surface_render_method", "-"),
                  getattr(material, "show_transparent_back", "-"), material.use_backface_culling)
        for attribute, value in SETTINGS.items():
            setattr(material, attribute, value)
        after = (material.blend_method, getattr(material, "surface_render_method", "-"),
                 getattr(material, "show_transparent_back", "-"), material.use_backface_culling)
        print("%-22s %s -> %s" % (name, before, after))

    print()
    print("left as they were (single-layer overlays, blended in the Looper library too):")
    for name in ("Spork Eye", "Spork Eye Inner", "Uma Decal", "Uma Outline Material"):
        material = bpy.data.materials.get(name)
        if material is None:
            continue
        print("  %-22s blend=%s render=%s back=%s"
              % (name, material.blend_method, getattr(material, "surface_render_method", "-"),
                 getattr(material, "show_transparent_back", "-")))

    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_mainfile(filepath=LIBRARY)
    if os.path.exists(LIBRARY + "1"):
        os.remove(LIBRARY + "1")
    print()
    print("wrote %s (%d byte(s))" % (LIBRARY, os.path.getsize(LIBRARY)))


main()
