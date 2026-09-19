# Tools

The viewer's command line interface: export models, motions and screenshots without clicking, and check
what came out.

**A human's guide is [`../docs/CLI.md`](../docs/CLI.md)** - prerequisites, task-by-task recipes, every
argument, and what the errors mean. This file is only the map.

| File | What it does |
|---|---|
| `headless_export.ps1` | The CLI. Runs Unity in batch mode, boots the viewer scene, loads a character, costume or prop, and then writes a `.pmx`, records one loop of a motion into a `.vmd`, and/or renders the game's own view to a PNG - the same code paths the in-app buttons use. |
| `pmx_inspect.py` | Blender-free PMX inspector and differ: `summary`, `bones`, `tails`, `weights`, `morphs`, `names`, `integrity`, `check`, `diff`. |
| `vmd_inspect.py` | Blender-free VMD inspector and loop validator: `summary`, `loop`, `motion`. |

Both Python tools are PEP-723 scripts with no dependencies, so `uv run` needs nothing installed:

```powershell
uv run Tools/pmx_inspect.py summary D:/out/1001_00.pmx
uv run Tools/vmd_inspect.py loop     D:/out/1001_00.vmd
```

## Quick start

```powershell
# list characters and their costume ids
./Tools/headless_export.ps1 -ListChars

# one model
./Tools/headless_export.ps1 -Char 1001 -Costume 00 -Out D:/out/1001_00.pmx

# one model and one loop of a motion
./Tools/headless_export.ps1 -Char 1001 -Costume 00 -Motion anm_rac_type01_run02_stride `
    -Out D:/out/1001_00.pmx -RecordVmd D:/out/1001_00.vmd
```

## What this needs

A **Unity editor** (2022.3.62f1 or another 2022.3.x) and **the project source**: the CLI is an editor
entry point (`Assets/Editor/UmaHeadlessExport.cs`) that Unity runs with `-batchmode -executeMethod`, so
the released `UmaViewer.exe` cannot do it yet. Close the editor before a run - batch mode refuses to
start while the project is open anywhere else. [`../docs/CLI.md`](../docs/CLI.md) has the details and
what a user-facing version of this would need.

## Not in here any more

The verification, rendering and game-data-research scripts that were used to build this fork - the
Blender checks, the shading A/B renders, the end-to-end workflow script, the game's-own-shader
reverse-engineering workbench - live outside the project, in the companion `UmaViewer-DevTools`
directory next to it, so a shipped checkout does not carry them. Its `README.md` is where the
measurements behind the claims in [`../README.md`](../README.md) and [`../docs`](../docs) are written
down; the viewer repository's git history has the earlier layout.
