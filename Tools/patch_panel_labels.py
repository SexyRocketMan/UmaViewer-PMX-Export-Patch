"""The panel changes the user asked for, applied with an assert on every anchor.

    python Tools/patch_panel_labels.py [--check]

1. the "Deploy morphs" heading becomes "Advanced morphs";
2. the deploy rows become checkboxes: a deploy shape key is 0 (collapsed, the model's rest state) or 1 (deployed), so a
   slider offers a range the morph does not have;
3. the scene switch that runs the step is labelled "Advanced morphs setup", which is what it is - the heading above the
   rows is the state, the switch is the button;
4. the joke `Pregnancy toggle` cannot be switched back off from the panel once on: while it is off it draws as a normal
   checkbox, and once on there is nothing left to click. (Blender has no latching property; the row *is* the latch.)

Every anchor is matched without its leading whitespace and re-indented from the line it matched, so the file's own
indentation and its CRLF endings are preserved rather than assumed.
"""
import io
import sys

PANEL = (r"C:\Users\user\AppData\Roaming\Blender Foundation\Blender\5.2\extensions\user_default"
         r"\uma_addon\uma_addon\addons\uma_addon\panels\AddonPanels.py")

SIMPLE = [
    ('box.label(text="Deploy morphs", icon=\'SHAPEKEY_DATA\')',
     'box.label(text="Advanced morphs", icon=\'SHAPEKEY_DATA\')'),
    ('box.prop(block, "value", text=label, slider=True)',
     'box.prop(block, "value", text=label, toggle=True)'),
    ('layout.prop(scene_props, "advanced_morphs")',
     'layout.prop(scene_props, "advanced_morphs", text="Advanced morphs setup")'),
]

PREGNANCY = 'layout.prop(scene_props, "pregnancy_toggle")'


def indent_of(text, needle):
    """The whitespace the line containing `needle` starts with."""
    index = text.index(needle)
    start = text.rfind("\n", 0, index) + 1
    line_start = text[start:index]
    assert line_start.strip() == "", "the anchor is not at the start of its line: %r" % line_start
    return line_start


def apply(check=False):
    text = io.open(PANEL, encoding="utf-8", newline="").read()
    for old, new in SIMPLE:
        count = text.count(old)
        assert count == 1, "anchor appears %d time(s), expected 1: %r" % (count, old[:70])
        text = text.replace(old, new, 1)

    assert text.count(PREGNANCY) == 1, "the pregnancy row was not found exactly once"
    pad = indent_of(text, PREGNANCY)
    latched = "\n".join([
        "# a joke row the user asked to keep, and to make one-way: Blender has no latching property, so the row",
        "# itself is the latch - while it is off it is a checkbox, and once it is on there is nothing to click.",
        "if scene_props.pregnancy_toggle:",
        "    layout.row(align=True).label(text=\"Pregnancy toggle: on, and it stays on\", icon='CHECKMARK')",
        "else:",
        "    " + PREGNANCY,
    ])
    text = text.replace(PREGNANCY, latched.replace("\n", "\n" + pad), 1)

    if check:
        print("all %d anchors found; nothing written" % (len(SIMPLE) + 1))
        return
    io.open(PANEL, "w", encoding="utf-8", newline="").write(text)
    print("patched %s (%d labels/toggles + the pregnancy latch)" % (PANEL.rsplit("\\", 1)[-1], len(SIMPLE)))


apply("--check" in sys.argv)
