"""List a pmx's material names, which is what the shader manifest's `parts` rules are matched against.

The `brows` plan in `docs/HANDOFF_SHADER_LIBRARY.md` rests on one claim: a character's eyebrow material has a
name the rule `{"match": ["mayu"]}` finds. `UmaContainerCharacter.cs:897` recognises it as
`m.name.EndsWith("mayu")` on the Unity side, so this prints the exported names with exactly that test applied.

    blender --background --python Tools/probe_pmx_material_names.py -- <file.pmx> [<out.txt>]
"""
import sys

sys.path.insert(0, r"C:\Users\user\AppData\Roaming\Blender Foundation\Blender\5.2\extensions\blender_org")
from mmd_tools.core import pmx

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]
path = argv[0] if argv else r"C:\Users\user\Documents\umaviewer_exports\morphs\1001_nostream.pmx"
out = argv[1] if len(argv) > 1 else None

model = pmx.load(path)
lines = ["%s - %d materials" % (path, len(model.materials))]
hits = []
for index, material in enumerate(model.materials):
    name = material.name
    marks = []
    lowered = name.lower()
    if lowered.endswith("mayu"):
        marks.append("ENDS-WITH-mayu")
        hits.append(name)
    for token in ("mayu", "brow", "face", "eye", "hair"):
        if token in lowered:
            marks.append(token)
    lines.append("%2d  %-40s %s" % (index, name, " ".join(marks)))
lines.append("")
lines.append("materials a 'mayu' substring rule would catch (%d):" % len(hits))
for name in hits:
    lines.append("  " + name)

text = "\n".join(lines)
if out:
    with open(out, "w", encoding="utf-8") as handle:
        handle.write(text + "\n")
    print("wrote %s" % out)
else:
    print(text)
