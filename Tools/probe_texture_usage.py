"""Which files in Texture2D does the exported pmx actually reference?

    blender --background --factory-startup --python Tools/probe_texture_usage.py -- <out.txt> <file-or-folder>

The pmx keeps its own texture table, so "is this file used" is a lookup rather than a guess: every name in the table is
referenced by the model, and anything in the folder that no table entry names is a leftover - from an earlier export,
an older bake, or a setting that has since changed. Those are the only files that are safe to delete.
"""
import os
import sys

sys.path.insert(0, r"C:\Users\user\AppData\Roaming\Blender Foundation\Blender\5.2\extensions\blender_org")
from mmd_tools.core import pmx

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]
out, target = argv[0], argv[1]
if os.path.isdir(target):
    pmx_path = next((os.path.join(target, name) for name in sorted(os.listdir(target))
                     if name.lower().endswith(".pmx")), None)
else:
    pmx_path = target
if pmx_path is None:
    raise SystemExit("no pmx found in %s" % target)

model = pmx.load(pmx_path)
folder = os.path.join(os.path.dirname(pmx_path), "Texture2D")
referenced = [str(name) for name in (model.textures or [])]
def normalise(name):
    """The pmx stores a path; `mmd_tools` hands it back with a trailing `>` on some builds, so both ends are cleaned."""
    text = str(name).strip().rstrip(">").strip().strip("'").strip('"')
    return os.path.basename(text.replace("\\", "/"))


referenced_basenames = {normalise(name) for name in referenced if name}

lines = []
lines.append("pmx: %s" % pmx_path)
lines.append("texture table entries: %d" % len(referenced))
lines.append("")
lines.append("referenced by the model (%d unique file names):" % len(referenced_basenames))
for name in sorted(referenced_basenames):
    lines.append("  %s" % name)

present = []
if os.path.isdir(folder):
    present = sorted(name for name in os.listdir(folder) if name.lower().endswith(".png"))
unused = [name for name in present if name not in referenced_basenames]
missing = sorted(name for name in referenced_basenames if name not in set(present))
lines.append("")
lines.append("files in Texture2D: %d" % len(present))
lines.append("")
lines.append("NOT referenced by the pmx (%d) - the only safe deletions:" % len(unused))
for name in unused:
    size = os.path.getsize(os.path.join(folder, name))
    lines.append("  %-44s %8d bytes" % (name, size))
if not unused:
    lines.append("  none - every file in the folder is referenced")
lines.append("")
lines.append("referenced but MISSING from the folder (%d):" % len(missing))
for name in missing:
    lines.append("  %s" % name)
if not missing:
    lines.append("  none - nothing the model points at is absent")

with open(out, "w", encoding="utf-8") as handle:
    handle.write("\n".join(lines) + "\n")
print("\n".join(lines))
