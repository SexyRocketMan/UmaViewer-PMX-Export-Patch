# Tools

The viewer's command line interface: export models, motions and screenshots without clicking, and check
what came out.

**A human's guide is [`../docs/CLI.md`](../docs/CLI.md)** - prerequisites, task-by-task recipes, every
argument, and what the errors mean. This file is only the map.

| File | What it does |
|---|---|
| `headless_export.ps1` | The CLI. Runs Unity in batch mode, boots the viewer scene, loads a character, costume or prop, and then writes a `.pmx` or an `.fbx`, records one loop of a motion into a `.vmd`, and/or renders the game's own view to a PNG - the same code paths the in-app buttons use. The model's extension picks the writer. It also prints a sound sheet's transcript (`-Transcript`) and writes a voiced clip to a tagged mp3 (`-ExportAudio`). |
| `pmx_inspect.py` | Blender-free PMX inspector and differ: `summary`, `bones`, `tails`, `weights`, `morphs`, `names`, `integrity`, `check`, `diff`. |
| `vmd_inspect.py` | Blender-free VMD inspector and loop validator: `summary`, `loop`, `motion`. |
| `prop_rig_names.py` | Reads `-ScanProps` output and tallies what a family's prefabs are: held in a hand, worn on a body part, or self-rigged, plus every bone name that carries a word - the only description the game ships for a prop. `python Tools/prop_rig_names.py Logs/headless_export_*.log --vocab`. |
| `headless_export.ps1 -PropGridCheck` | Not a tool of its own: the prop grid is a view of the props panel, so the CLI opens the panel, switches it, and reports what it built. `-PropAtlas <png>` writes a page of props as one contact sheet with per-cell coverage in the log. See [`../docs/PROP_BROWSER.md`](../docs/PROP_BROWSER.md). |
| `mp3_transcript.py` | Reads the transcript an exported mp3 carries in its ID3 tag - title, speaker, story, full text - and exits non-zero when a file has none. Standard library only, so it doubles as the check that an export was tagged. |

Both Python tools are PEP-723 scripts with no dependencies, so `uv run` needs nothing installed:

```powershell
uv run Tools/pmx_inspect.py summary D:/out/1001_00.pmx
uv run Tools/vmd_inspect.py loop     D:/out/1001_00.vmd
```

`mp3_transcript.py` needs nothing either:

```powershell
python Tools/mp3_transcript.py D:/out/line.mp3            # what the exported clip says
python Tools/mp3_transcript.py "D:/rips/*.mp3" --quiet     # "12/12 file(s) carry a transcript"
```

### Game data probes

These read the installed game's files directly, and they are how [`../docs/TRANSCRIPTS.md`](../docs/TRANSCRIPTS.md)
was measured. They are research tools, not part of any export; all of them are read-only.

| File | What it does |
|---|---|
| `uma_master_probe.py` | Runs one SQL query against `master/master.mdb` (a plain SQLite file) or lists its tables. `python Tools/uma_master_probe.py "select * from character_system_text limit 5"`. |
| `uma_meta_probe.py` | The same for `meta_plain.sqlite`, the decrypted asset index: where an asset's file is, what it is called, whether it is encrypted. |
| `uma_transcript_survey.py` | Walks every sound asset, resolves each clip's cue name through the sibling `.acb`, and reports how many clips the master data can put a line of text on. Also holds the acb cue walk (`Acb`) the survey and the story probe share. |
| `uma_story_dump.py` | Opens one story timeline bundle and prints the lines it carries. Needs `UnityPy` (`pip install UnityPy`); the viewer itself needs nothing extra, it reads these with the game's own classes. |
| `cri_utf.py` | Minimal CRI `@UTF` table reader, used by the two CRI probes above. |

```powershell
python Tools/uma_transcript_survey.py           # coverage per sound family
python Tools/uma_master_probe.py                # list the master data's tables
python Tools/uma_story_dump.py "story/data/04/1047/storytimeline_041047007"
```

### Spring (CySpring) probes

**Shelved.** The port is exact and the bakes are faithful, but without a numeric reference for the game's
tail there is no way to tell whether a chain looks right — see [`../docs/CYSPRING.md`](../docs/CYSPRING.md)
for what was found, what was ruled out and what would unblock it.

How the game's wiggle-bone solver works, and how to watch it move. The solver is ported to Python and held
bit-exact against the game's own C# by `spring_probe/` (a small .NET project that compiles the repo's
`CySpringNative.cs`); the chain spec and the Blender bake are what turned that into something visible.

| File | What it does |
|---|---|
| `uma_spring_dump.py` | Reads a costume prefab, finds the CySpring chains in it, and writes one chain as a JSON spec: the game's parameters, bone positions in game units, and a scripted parent wag (`--swing`, `--axis`). A link's distance and axis always run from a bone down to its child, which is what `CySpringBoneBase.Initialize` measures. |
| `cyspring_port.py` | The solver itself: a float32 transcription of `CySpringNative.UpdateNativeCloth`, with `--compare` against the C# trace, `--trace`, and `--no-colliders`. Also the module the Blender bake imports. |
| `cyspring_metrics.py` | The same run as numbers: per link the rest length against the prefab's, droop under gravity, how far a link hangs behind its parent, its lag in frames, overshoot, settle time, ringing, and frame-to-frame step - plus the tip's path against a rigid chain's. `--motion impulse` steps the parent once and holds it, which is the cleanest spring read. |
| `spring_probe/` | The .NET harness that runs the game's own structs and solver and dumps a trace, so the Python port can be compared frame by frame (`dotnet run -- --trace out.txt`). |
| `blender_cyspring_demo.py` | Imports a PMX with mmd_tools, bakes a spec's motion onto the model's own spring bones as world rotations, and can render an mp4. Needs `--model`; MMD's rigid-body physics is left out because the bake *is* the motion. |
| `spring_plot.py` | Draws the chain in 2D - an HTML page with playback and a deviation strip, plus an optional animated GIF. The grey ghost is where a rigid chain would be, so the gap is the lag. This is the way to look at a chain without Blender in the way. |
| `chain_drive_from_vmd.py` | Drives a chain with a real animation's hip instead of a scripted wag: rotation *and* travel, taken from a VMD the viewer recorded. A drive that only rotates the parent makes every chain a stick - the travel is what a tail trails behind. |

```powershell
python Tools/uma_spring_dump.py 1001 00 --chain Sp_Hi_Tail0 --segments child > tail.json
& "C:\Program Files\Blender Foundation\Blender 5.2\blender.exe" -b -P Tools/blender_cyspring_demo.py -- `
    tail.json tail.blend --model model.pmx --mp4 tail.mp4
```

Two traps worth knowing. `scene.render.filepath` is not respected while rendering if the user's startup
add-ons rewrite it (a dataset add-on that hooks `render_init` will send the frames into its own folder);
the demo renders without `write_still` and calls `Image.save_render()` instead, which writes where it is
told. And the solver's rotations live in the prefab's frame, not the armature's: a bake that applies them
directly turns the tail about axes about 87 degrees from the solver's. The demo carries them over per bone
(`M @ R @ M^-1`) and verifies every frame against the solver afterwards, so a bad bake shows as a number.

## Quick start

```powershell
# list characters and their costume ids
./Tools/headless_export.ps1 -ListChars

# one model; use an .fbx path for FBX instead of PMX
./Tools/headless_export.ps1 -Char 1001 -Costume 00 -Out D:/out/1001_00.pmx
./Tools/headless_export.ps1 -Char 1001 -Costume 00 -Out D:/out/1001_00.fbx

# FBX without the uma refinement step (eye bones left as the viewer holds them)
./Tools/headless_export.ps1 -Char 1001 -Costume 00 -Refine off -Out D:/out/1001_00.fbx

# FBX referencing its textures instead of carrying them (smaller file)
./Tools/headless_export.ps1 -Char 1001 -Costume 00 -EmbedTextures off -Out D:/out/1001_00.fbx

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
