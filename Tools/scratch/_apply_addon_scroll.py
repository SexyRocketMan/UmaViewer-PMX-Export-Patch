import io

path = r"C:\Users\user\AppData\Roaming\Blender Foundation\Blender\5.2\extensions\user_default\uma_addon\uma_addon\addons\uma_addon\operators\MorphDrivers.py"
with io.open(path, "r", encoding="utf-8", newline="") as f:
    text = f.read()

assert "def build_tear_scroll" not in text, "already added"

block = '''#: The property the tear scroll is driven by: sheet frames per second, 0 stops it. The sheet grid is the
#: material's `scroll=<cols>x<rows>` token, written by the exporter from the tear prefab's asset table.
TEAR_SCROLL_PROPERTY = "Tear_Scroll"


def _grid_from_comment(comment):
    """The `scroll=<cols>x<rows>` token as (cols, rows), or None."""
    for token in (comment or "").split():
        if not token.startswith("scroll="):
            continue
        match = re.match(r"scroll=(\\d+)x(\\d+)$", token)
        if match:
            return int(match.group(1)), int(match.group(2))
    return None


def build_tear_scroll(roots=None):
    """Roll the tear sprite sheets the way the game does: the texture moves a sheet frame at a time.

    The pmx carries the roll as UV morphs (`_scrN`), which Blender cannot play, so the material gets a Mapping
    node whose location steps over the sheet instead: `k = floor(frame * Tear_Scroll)` picks the sheet frame,
    the column moves x by one cell and the row moves y by one row - a grid step, not a smooth slide, because a
    smooth slide would show the seams between frames.

    Only materials whose comment carries the grid are touched; the static tear decal has no grid and stays put.
    """
    report = {"tag": TAG, "materials": 0, "drivers": 0, "notes": []}
    for root in _roots(roots):
        materials = []
        for material, comment in _material_comments().items():
            grid = _grid_from_comment(comment)
            if grid is not None:
                materials.append((material, grid))
        if not materials:
            continue
        _ensure_property(root, TEAR_SCROLL_PROPERTY, 12.0, 0.0, 60.0,
                         "Tear scroll rate, in sprite-sheet frames per second. 0 holds the sheet still")
        for material, (columns, rows) in materials:
            tree = material.node_tree
            if tree is None:
                continue
            mapping = next((node for node in tree.nodes
                            if node.type == "MAPPING" and node.label == "Tear Scroll"), None)
            if mapping is None:
                mapping = tree.nodes.new("ShaderNodeMapping")
                mapping.name = "UMA Tear Scroll"
                mapping.label = "Tear Scroll"
                mapping.location = (-600, -200)
                for node in _texture_vector_inputs(material):
                    source = node.inputs["Vector"]
                    if source.links:
                        tree.links.new(source.links[0].from_socket, mapping.inputs["Vector"])
                    else:
                        uv = next((n for n in tree.nodes if n.type == "UVMAP"), None)
                        if uv is None:
                            uv = tree.nodes.new("ShaderNodeUVMap")
                            uv.location = (-800, -200)
                            uv.uv_map = "UVMap"
                        tree.links.new(uv.outputs["UV"], mapping.inputs["Vector"])
                    tree.links.new(mapping.outputs["Vector"], source)
            path = 'nodes["%s"].inputs["Location"].default_value' % mapping.name
            if tree.animation_data is not None:
                for curve in [c for c in tree.animation_data.drivers if c.data_path == path]:
                    tree.animation_data.drivers.remove(curve)
            for axis, expression in (
                (0, "(floor(frame * var) %% %d) / %.1f" % (columns, columns)),
                (1, "-(floor(floor(frame * var) / %.1f) / %.1f)" % (columns, rows)),
            ):
                driver = mapping.inputs["Location"].driver_add("default_value", axis).driver
                driver.type = "SCRIPTED"
                driver.expression = expression
                variable = driver.variables.new()
                variable.name = "var"
                variable.type = "SINGLE_PROP"
                variable.targets[0].id_type = "OBJECT"
                variable.targets[0].id = root
                variable.targets[0].data_path = '["%s"]' % TEAR_SCROLL_PROPERTY
                report["drivers"] += 1
            report["materials"] += 1
        root.update_tag()
        if armature := _armature_of(root):
            armature.update_tag()
        bpy.context.view_layer.update()
    return report


def report_export(roots=None):'''

anchor = "def report_export(roots=None):"
assert text.count(anchor) == 1
text = text.replace(anchor, block, 1)
if "import re" not in text.split("def ")[0]:
    text = text.replace("import bpy", "import re\n\nimport bpy", 1)
with io.open(path, "w", encoding="utf-8", newline="") as f:
    f.write(text)
print("ok")
