"""Does the panel *draw* the node-targeted rows? A recording layout, so no UI is needed.

    blender --background --factory-startup --python Tools/verify_eye_panel.py -- <out.txt> [<file.pmx>]

`draw_shading_control` is the panel's whole body; it reaches into `layout` with `row`, `box`, `label`, `prop`,
`operator`, `menu`. Recording those calls is enough to prove the rows are drawn, in the right section, with the
labels the manifest gives them - and that nothing in the drawing path assumed every row has an `input`, which is the
specific way this change could break the panel (`setting["input"]` was the row's label before 2.34).

Two blocks are drawn and both are checked: the global block (no selection) and one character's own block (a selection
with an override, which is the only state in which her rows appear).
"""
import sys

import bpy

ADDON = "bl_ext.user_default.uma_addon"

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]
out = argv[0]
pmx = argv[1] if len(argv) > 1 else r"C:\Users\user\Documents\umaviewer_exports\morphs\1001_nostream.pmx"

lines = []
failures = []


def say(text=""):
    lines.append(text)


def check(label, ok, detail=""):
    say("  [%s] %s%s" % ("PASS" if ok else "FAIL", label, (" - " + detail) if detail else ""))
    if not ok:
        failures.append(label)
    return ok


class Recording:
    """Stands in for `bpy.types.UILayout`: every call is recorded and returns another recorder.

    Blender's layout methods return either a sub-layout (`row()`, `box()`, `column()`) or None (`label()`, `prop()`),
    so returning `self` everywhere is what a chained call expects.
    """

    def __init__(self, log, depth=0):
        self.log = log
        self.depth = depth

    def _record(self, kind, text=None, **_kwargs):
        self.log.append((kind, text, self.depth))
        return Recording(self.log, self.depth + 1)

    def row(self, **_kwargs):
        return self._record("row")

    def box(self):
        return self._record("box")

    def column(self, **_kwargs):
        return self._record("column")

    def label(self, text="", **_kwargs):
        return self._record("label", text)

    def prop(self, _owner, name="", **kwargs):
        return self._record("prop", kwargs.get("text") or name)

    def operator(self, _idname, **_kwargs):
        return self._record("operator", _kwargs.get("text"))

    def menu(self, _idname, text="", **_kwargs):
        return self._record("menu", text)

    def separator(self, **_kwargs):
        return self._record("separator")

    def split(self, **_kwargs):
        return self._record("split")


bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.preferences.addon_enable(module="bl_ext.blender_org.mmd_tools")
bpy.ops.preferences.addon_enable(module=ADDON)

from bl_ext.user_default.uma_addon.uma_addon.addons.uma_addon.operators import (  # noqa: E402
    ShaderManifest, UmaShaderControl as Control)

context = bpy.context

bpy.ops.mmd_tools.import_model(filepath=pmx)
mesh = max([obj for obj in context.scene.objects if obj.type == 'MESH'], key=lambda item: len(item.data.vertices))
for obj in context.view_layer.objects:
    obj.select_set(False)
mesh.select_set(True)
context.view_layer.objects.active = mesh
bpy.ops.uma.apply_shader()
Control.hook(context)

# the panel with nothing selected: one block per shader a character in the scene wears
for obj in context.view_layer.objects:
    obj.select_set(False)
context.view_layer.objects.active = None
log = []
Control.draw_shading_control(Recording(log), context)
labels = [text for kind, text, _depth in log if kind in ("prop", "label") and text]
say("=" * 100)
say("global block: %d call(s), %d label/prop text(s)" % (len(log), len(labels)))
for want in ("Crying", "Tremble rate", "Tremble amount", "Highlight colour", "Second highlight colour"):
    check("draws %r" % want, want in labels)
check("also draws the old rows", "Diffuse Intensity" in labels and "Emission Intensity" in labels)
check("shows the row count of the table", any("25 setting(s)" in (text or "") for _kind, text, _d in log),
      "%s" % [text for _kind, text, _d in log if text and "setting" in str(text)][:3])
check("no empty label was drawn", "" not in [text for kind, text, _d in log if kind in ("prop", "label")])

# and with a character selected who has an override: her block is the one that shows the padlocks
character = Control.root_object(mesh)
for obj in context.view_layer.objects:
    obj.select_set(obj is mesh)
context.view_layer.objects.active = mesh
Control.write_value(character, "uma_shader_eye_cry", 1.0)
log = []
Control.draw_shading_control(Recording(log), context)
texts = [text for kind, text, _depth in log if kind in ("label", "prop") and text]
say()
say("character block: %d call(s)" % len(log))
say("  mentions: %s" % [text for text in texts if "shading for" in str(text).lower() or "%" in str(text)][:4])
check("draws her own Crying row", "Crying" in texts)
check("and her other own rows", "Second highlight colour" in texts)
check("says how many of her settings she has set",
      any("of 25 set" in str(text) for text in texts), "%s" % [t for t in texts if "set" in str(t)][:3])

say()
say("RESULT: %s" % ("all checks passed" if not failures else "FAILED - " + "; ".join(failures)))
with open(out, "w", encoding="utf-8") as handle:
    handle.write("\n".join(lines) + "\n")
print("wrote %s" % out)
