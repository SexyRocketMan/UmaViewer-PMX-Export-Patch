# Command line export

UmaViewer can do its whole export job from a terminal: write a `.pmx` model, record the playing motion
into a `.vmd`, and render the game's own view to a PNG. It drives the same code paths as the buttons in
the UI - *Export Model*, *Record VMD*, the screenshot button - so what comes out is what the buttons
produce, only without the clicking.

Everything goes through one script, `Tools/headless_export.ps1`. It builds a Unity batch-mode command
line, runs it, and prints the interesting lines of the log afterwards.

> ### Status: the editor and a built viewer
> The runner lives in `Assets/Scripts/Cli/`, which is runtime code: the editor reaches it with
> `-batchmode -executeMethod UmaHeadlessExport.Run` (and `Tools/headless_export.ps1` assembles that command
> line for you), and **a built viewer is itself the CLI** - `UmaViewer.exe --char 1001 --out x.pmx` - with no
> Unity installed at all. See [Running from the built viewer](#running-from-the-built-viewer).
>
> Every flag has a double-dash name (`--char`) and its original single-dash one (`-umaChar`), `--help` lists
> them, and `--json` gives a machine readable result. The flags that read editor-only data -
> `--probe-morphs`, `--probe-tears`, `--dump-shaders`, `--dump-face` - still need the editor, because a runtime
> assembly cannot reference `UnityEditor`; in a player they say so and the run carries on.

### Two spellings, one parser

The harness behind the wrapper takes two names for every flag: the double-dash one to type, and the
single-dash one that has always worked. They are the same switch - `--out` and `-umaOut` are one case in one
parser - so scripts written years ago keep working, and nobody has to read a table to guess a flag name.

```powershell
./Tools/headless_export.ps1 -Char 1001 -Out D:/out/1001.pmx        # the wrapper's own switches
```

The wrapper is a PowerShell script, so **its** parameters are `-Char` (one dash, no `uma`). The double-dash
names are the harness's, which is what you use when calling Unity yourself:

```powershell
& "C:\Program Files\Unity\Hub\Editor\2022.3.62f1\Editor\Unity.exe" `
    -batchmode -projectPath . -executeMethod UmaHeadlessExport.Run `
    --char 1001 --costume 00 --out D:\out\1001.pmx -logFile D:\out\run.log
```

That needs no wrapper at all. Its exit code is the run's (0 ok, 1 the run failed, 2 bad arguments), and the
log says `[UmaHeadlessExport] OK` or `FAILED: <why>` either way. What the wrapper still does for you: finds
Unity, clears a `Temp/UnityLockfile` left by a crashed run, waits out Unity's launcher process, and filters
the log down to the lines worth reading.

### Running from the built viewer

Build one to `BuildDev/uma-cli/UmaViewer.exe` (ignored by git, 124 MB):

```powershell
& "C:\Program Files\Unity\Hub\Editor\2022.3.62f1\Editor\Unity.exe" -batchmode -projectPath . `
    -executeMethod UmaPlayerBuild.BuildWindows -logFile build.log
```

Then the binary *is* the CLI - no Unity, no project, no PowerShell:

```powershell
.\BuildDev\uma-cli\UmaViewer.exe --char 1001 --costume 00 --out D:\out\1001.pmx --json
.\BuildDev\uma-cli\UmaViewer.exe --dump-props D:\props --dump-props-filter toon_prop
.\BuildDev\uma-cli\UmaViewer.exe --help
```

The exit code is the run's (0 ok, 1 failed, 2 bad arguments), the log says the same as the editor's, and
`--json` gives the same line. A window does open - the viewer is a game, not a console app - so pass
`-screen-width 640 -screen-height 360 -screen-fullscreen 0` if it is in the way; the UI is hidden as the run
starts and the cursor is released.

Measured, same machine: a character export took **5.3 s** in the player against **10-11 s** in the editor, for
a PMX of the same 14 492 vertices, 82 074 indices, 437 bones and 205 morphs. Two honest differences:

* **The editor-only probes.** `--probe-morphs`, `--probe-tears`, `--dump-shaders` and `--dump-face` read
  animation curves and serialized properties with `UnityEditor`, which a player assembly cannot reference at
  all. In a player they say so - `DumpTearBundles needs the editor: ...` - and the run carries on.
* **The model's English name.** The editor writes the localised name (`Special Week`); the player writes the
  Japanese one (`スペシャルウィーク`), because the English names arrive from a translation download the
  editor's cache already has. Everything else in the file is identical.

### Commands

A verb in front of the flags says what the run *is*, fills in the flag that choice implies, and refuses to start
when the run could not produce what was asked for:

```powershell
UmaViewer.exe export model --char 1001 --costume 00 --out D:/out/1001.pmx
UmaViewer.exe export motion --char 1001 --motion anm_rac_type01_run02_stride --record-vmd D:/out/run.vmd
UmaViewer.exe export screenshot --char 1001 --screenshot D:/out/shot.png
UmaViewer.exe export audio --export-audio snd_voi_story_041047007 --export-clip 3 --out D:/out/line.mp3
UmaViewer.exe list chars | list costumes --char 1001 | list props book | list textures prop | list families
UmaViewer.exe export model --char 1001 --all-costumes --out D:/out/costumes
UmaViewer.exe dump props D:/props --dump-props-filter toon_prop
UmaViewer.exe dump models D:/web --packs
UmaViewer.exe scan props pfb_chr_prop --scan-count 60
UmaViewer.exe doctor
```

`dump props`, `scan props` and the `list` commands take a **bare word** as the thing they act on, so
`dump props D:/props` and `--dump-props D:/props` are the same run. Everything else is a flag, exactly as
before: the flags alone (`--char 1001 --out x.pmx`) are still a complete command, and every existing script
keeps working - a verb is sugar, not a new parser.

What a verb adds is the *checking*. `export model` without `--out` says

```
export model needs --out <file> to write
run it as: export model --char 1001 --costume 00 --out D:/out/1001.pmx
```

and exits 2, instead of writing to the default folder and leaving you to work out where. `list costumes`
without `--char`, `export motion` without `--record-vmd`, and a verb with no noun (`export`) say the same kind
of thing. `--help` lists every command with an example.

Verbs and the flags work in both hosts; through the PowerShell wrapper only the flags do, because its
parameters are `-Char`-style and PowerShell binds a bare word to nothing. The batches are the exception worth
knowing: `./Tools/headless_export.ps1 -Char 1001 -AllCostumes -Out D:/out/costumes` is the same run the verb
form gives you, and `Tools/check_cli_parity.py` is what keeps the wrapper able to pass every flag the runner
knows.

### Batches: one boot, many models

Every run pays for the viewer booting (about eight seconds). For more than one model that is the whole cost, so
`--all-costumes` and `--all-chars` walk a list in a single run:

```powershell
# every costume of one character: 1001_00.pmx, 1001_02.pmx, ... into the folder
UmaViewer.exe export model --char 1001 --all-costumes --out D:/out/costumes

# every character, at the costume named (or 00 for the ones without it), or every model in the game
UmaViewer.exe export model --all-chars --costume 00 --out D:/out/chars
UmaViewer.exe export model --all-chars --all-costumes --out D:/out/all
```

`--out` is a **folder** here, and giving it a file name is an error rather than a directory called
`1001_00.pmx`. Each model is written as `<id>_<costume>.pmx`, one `LoadUma` + `ModelExporter.ExportModel` at a
time - the same path a single export takes - so nothing about the output differs from exporting that model on its
own.

It **resumes**: any `.pmx` already in the folder is skipped without loading its model, so an interrupted batch
carries on where it stopped, and running it again after a costume is added only writes the new one. The run
reports what it wrote, what it skipped and what failed:

```
batch: 4 model(s) into C:/tmp/batch
[1/4] exporting 1001_00 (スペシャルウィーク)
...
batch: 4 model(s) written, 0 failed, 23 MB
```

Measured, both hosts: four costumes in **one** run took 18.8 s of work and 27 s including the boot, against
4 × 11 s as separate runs; the built player did the same four in 22 s. A failure is per model - the batch
records it and carries on - and the run only fails outright when nothing at all could be written.
`--json` reports it as `"mode":"batch"` with `jobs` and `written`.

How long a full sweep takes, from that rate: ~5 s a model, so 169 characters at one costume each is about
fifteen minutes, and every costume in the game (about 1 700 models, ~10 GB) is a couple of hours and a resumable
one at that.

### The dump a browser reads: `dump models`

`dump props` writes the prop families for a picker. `dump models` is the one for a web service: characters **and**
props under one root, one `index.json`, and a shared texture folder per kind.

```powershell
UmaViewer.exe dump models D:/web --packs --thumbs         # everything, with a zip and a preview per model
UmaViewer.exe dump models D:/web --dump-models-count 8     # try it on a few first
UmaViewer.exe dump models D:/web --dump-models-props       # props only
python Tools/check_dump.py D:/web --packs --thumbs         # then check it
```

```
D:/web/index.json
D:/web/characters/1001_00.pmx      D:/web/characters/Texture2D/*.png
D:/web/props/pfb_chr_prop1003_10.pmx   D:/web/props/Texture2D/*.png
D:/web/packs/character/1001_00.zip
D:/web/thumbs/character/1001_00.png
```

**Why the textures are shared per kind.** A PMX names its maps as relative paths - `Texture2D/tex_x_diff.png` -
which are resolved next to the model. So the models and their maps have to sit side by side, and putting every
character's models in one folder lets them share one `Texture2D/`: four costumes of character 1001 share 48 of
their 180 maps. Everything that opens a PMX (MMD, Blender, a browser) then resolves it with no help at all.

**One model on its own.** `--packs` writes a zip per model with the PMX at the root and only *its* maps in
`Texture2D/` inside - 32 entries for `1001_00` (the model and its 31 maps) - so a user can download one
character and open it without fetching the rest of the dump. The pack is stored, not deflated: a PMX and PNGs
are already compressed, and the extra pass would cost minutes across a thousand models for a few per cent.

**A picture of each model.** `--thumbs` renders every model to a square PNG (256 px unless `--thumb-size`
says otherwise) while it is still loaded, and records it as `thumb` in the index, so a browser can show what a
model looks like without loading it. It is rendered by the same scene that has just exported the model, so it
costs one camera render per model, not a second load; the picture is a third of the way round from the front and
slightly above, with a transparent background so a page can put it on its own panel.

Framing is measured rather than computed, and that is the whole trick: `Renderer.bounds` is not the shape that
gets drawn. A skinned renderer reports the box of its bones, and character 1001 reports 16.7 x 6.2 x 25.9 units
while drawing a person - framed from that box, she came out 85 px wide in a 256 px square. So each pass renders,
reads back the alpha channel, and corrects from what appeared: the size sets the distance, the offset sets where
to aim. Two passes reach 86% of the square and centre within 3 px; a third is only needed when the centring move
was larger than a pixel or two, because a correction is applied by the *next* render, not the one it was measured
on.

**Resume that repairs.** A model already on disk is skipped - but only if it is *complete*: the file, every map
its PMX names, its pack when `--packs` was asked for, and its preview when `--thumbs` was. Reading the PMX back
is what lets a resumed dump fix itself instead of skipping past a hole; deleting one texture from a finished dump
and re-running re-exports exactly that model, and so does re-running an older dump with `--thumbs` added.

**index.json** (schema `umaviewer-models/1`) is the whole dump in one file: per model the kind, id, costume,
name (japanese and english), the prop family, the model, texture and preview paths relative to the root, the list
of map files it needs, its byte size, bounds, bone and hand-bone counts, its pack, and any failure. Plus the
totals a browser wants before loading anything: written, failed, bytes, texture files and their bytes, packs,
previews.

`Tools/check_dump.py` is the other half: it reads a dump and its index and reports missing models, missing maps,
size disagreements, truncated files and packs that do not contain what they should. With `--thumbs` it also opens
the previews and asks how much of each square was actually drawn, because a camera pointed at nothing still writes
a valid PNG. It exits 1, so it can gate a deploy - and it is how the resume check above was found to be necessary
in the first place.

### `--help`, `--about`, `--json`

* `--help` prints every flag, grouped by what it is for, with the old name next to the new one. It is the
  table below, generated from the parser, so it cannot drift.
* `--about` prints the build, the Unity version and the flag count. It is not `--version`: Unity answers that
  one itself and exits before this code runs, with no log and no explanation.
* `--doctor` (or the `doctor` command) prints what a run would use and exits: the build, the scene, the data
  folder and its work mode, whether `meta`, `master.mdb` and `dat/` are actually there, then the manifest -
  assets, characters, shaders, how many english names arrived, how many props - and it runs in the editor and in
  a player alike. It is the answer to every "why did nothing happen" that is really an environment question.
* `--json` (`-Json` on the wrapper) finishes a run with one machine readable line:

  ```
  [UmaHeadlessExport] JSON {"ok":true,"mode":"export","path":"D:/out/1001.pmx","bytes":5801991,
                            "seconds":10.2,"failures":0,"error":""}
  ```

  `mode` is what ran (`export`, `export-prop`, `vmd`, `batch`, `screenshot`, `scan`, `scan-export`, `dump`,
  `list`, `doctor`, `transcript`, `audio`), `failures` counts models that failed inside a sweep or a dump where
  the run itself still succeeded, and the line is written on failure too - so a caller never has to tell
  "no JSON" from "the run died before it could write one".

  `list` and `doctor` answer with their own payload instead:

  ```json
  {"ok":true,"mode":"list","list":"costumes","count":4,"truncated":false,
   "items":[{"id":"00","asset":"3d/chara/body/bdy1001_00/pfb_bdy1001_00"}, ...]}
  {"ok":true,"mode":"doctor","host":"editor","assets":360456,"charaAssets":37579,
   "motions":93122,"characters":169,"shaders":497,"translated":174,"props":1337,
   "meta":true,"masterMdb":true,"dat":true, ...}
  ```

  `--out <file>` on a `list` writes the *whole* list there (a `file` field names it), which is what to use for
  the big ones: `list textures --json --out D:/textures.json` is 15 717 items, and only the first 2 000 are put
  in the log line, with `"truncated":true` to say so.

---

## What you can do

| You want to | Command |
|---|---|
| See which characters and costumes exist | `./Tools/headless_export.ps1 -ListChars` |
| Export a model | `./Tools/headless_export.ps1 -Char 1001 -Costume 00 -Out D:/out/special_week.pmx` |
| Export it as FBX instead | `./Tools/headless_export.ps1 -Char 1001 -Costume 00 -Out D:/out/special_week.fbx` |
| Record one loop of a motion | `./Tools/headless_export.ps1 -Char 1001 -Motion anm_rac_type01_run02_stride -RecordVmd D:/out/run.vmd` |
| Do both in one run | `./Tools/headless_export.ps1 -Char 1001 -Costume 00 -Motion <motion> -Out m.pmx -RecordVmd m.vmd` |
| Screenshot the game's own render | `./Tools/headless_export.ps1 -Char 1001 -Costume 00 -ShotView face -Screenshot D:/out/face.png -ShotOnly` |
| Export a prop or a scene | `./Tools/headless_export.ps1 -ListProps -ListPropsFilter home10001` then `-Prop <path> -Out D:/out/home.pmx` |
| See what the ten `3d/chara` families hold | `./Tools/headless_export.ps1 -ListFamilies` (names only, nothing downloaded) |
| Export a character wearing its attachment set | `./Tools/headless_export.ps1 -Char 1048 -Costume 00 -Attach pfb_attach1048_00_0001_00 -Out D:/out/worn.pmx` |
| Find out why a scene renders white | `./Tools/headless_export.ps1 -ScanProps "3d/env/home" -ScanCount 12` |
| Find every scene that fails to export | `./Tools/headless_export.ps1 -ScanProps "3d/env" -ScanCount 60 -ScanExport` |
| Dump every prop as a browsable model pack | `./Tools/headless_export.ps1 -DumpProps D:/umaprops` (one folder per prop plus `index.json` - see [PROP_DUMP](PROP_DUMP.md)) |
| See what a sound clip says | `./Tools/headless_export.ps1 -Transcript snd_voi_home_100100` |
| Export a voice clip to mp3 with its line in the tag | `./Tools/headless_export.ps1 -ExportAudio snd_voi_story_041047007 -ExportClip 3 -Out D:/out/line.mp3` |
| Check what you exported | `uv run Tools/pmx_inspect.py summary <file.pmx>`, `uv run Tools/vmd_inspect.py loop <file.vmd>` |
| See where one material's geometry ended up | `uv run Tools/pmx_inspect.py bounds <file.pmx> --material attach` (a merged part that landed in the wrong place exports without an error, so this is the check) |

---

## Before the first run

| You need | Why | How to tell it is there |
|---|---|---|
| **Unity 2022.3.62f1** (or another 2022.3.x) | today's CLI *is* the editor in batch mode | Installed through Unity Hub. The wrapper reads `ProjectSettings/ProjectVersion.txt` and looks in `C:\Program Files\Unity\Hub\Editor\<version>\Editor\Unity.exe`, then falls back to the newest editor installed there. `-UnityExe <path>` overrides it. |
| **The project source** | `Assets/Editor/UmaHeadlessExport.cs` lives in it | `ProjectSettings/ProjectVersion.txt` next to `Tools/` |
| **The game data folder** | the same folder the viewer itself needs - nothing loads without it | Same as the GUI: if the viewer cannot find it, set it in *Settings → Other → Change DataPath*. `Config.json` sits next to the project and holds the result. |
| **`uv`** *(only for the checkers)* | `pmx_inspect.py` / `vmd_inspect.py` are PEP-723 scripts with no dependencies | `uv --version`; `winget install astral-sh.uv` or see the uv docs |
| **The editor closed** | batch mode refuses to start while any Unity process holds the project | Task Manager: no `Unity.exe` with this project in its command line |

Two things that are normal and not errors:

* **The first run is slow** (several minutes). Unity compiles the scripts and reimports assets. Later runs
  are much faster.
* **Only one Unity per project.** If the editor (or another batch run) has the project open you get
  *"project already open in another instance"*. The wrapper removes a stale `Temp/UnityLockfile` when no
  Unity process is holding the project, but it will not fight a live editor.

---

## Recipes

### 1. Find a character and its costume ids

```powershell
./Tools/headless_export.ps1 -ListChars
```

Prints one line per character - id, name, available costume ids - and exits without loading anything:

```
[UmaHeadlessExport]   1001    Special Week    [00,01,02,...]
```

`-ListCostumes` (with `-Char`) prints the full costume asset paths instead.

### 2. Export a model

```powershell
./Tools/headless_export.ps1 -Char 1001 -Costume 00 -Out D:/out/special_week.pmx
```

`-Costume` may be left off: the first costume of the character is used and its id is printed. Writes the
`.pmx` **and a `Texture2D/` folder next to it** - the model refers to those files, so keep them together.

Without `-Out` the file lands in `<project>/HeadlessExports/<charid>_<costume>.pmx`.

**FBX instead of PMX:** give `-Out` an `.fbx` path. Same model, same code path that read it - only the
writer differs - and the `Texture2D/` folder next to it works the same way. The two formats differ in what
they can carry, which is worth knowing before choosing:

| | `.pmx` | `.fbx` |
|---|---|---|
| who imports it | mmd_tools in Blender, MMD itself | Blender, Unity, Unreal, anything with an FBX importer |
| bones | all 437, with the tails the exporter derives | all 437, same names, same positions and orientations; FBX stores no bone length |
| morphs | all, as MMD vertex morphs | all, as blend shapes with the same names |
| materials | MMD materials, with the uma shader settings in the material comment | Phong materials; the uma settings travel as a `UmaMaterial` user property instead |
| extra UV channels | the 3 PMX extra UVs (mmd_tools shows them as UV1..UV3, _UV2, _UV3) | the same six layers, same names |
| eye rig | the Blender add-on's `Refine Structure` turns the gaze morphs into eight driven shape keys | the eye bones are aimed so rotating them moves the gaze, with no driver (`-Refine`) |
| bone collections, connected bones, hidden bones | the add-on's `Set Bone Collections` / `Refine Structure` | **not in the format** - see [FBX_EXPORT.md](FBX_EXPORT.md) §7 |

The one option of its own is `-Refine on|off` (default: `Config.json`'s `FbxApplyUmaRefinement`, which is
on): `off` exports the rig exactly as the viewer holds it, all 437 bones; `on` leaves out the **167 bones
the Blender add-on hides** - its `Handle` collection and *Refine Structure*'s own hidden list - and aims the
eye bones the way the add-on aims them, keeping the eyeball skinned to them so rotating `Eye_L`/`Eye_R`
moves the gaze. An FBX has no visibility flag, so omitting is the only way a file can look like the rig the
add-on shows; any weight an omitted bone carried moves to the bone it belongs to. Only affects FBX.

Two things about the FBX worth knowing before comparing it with the PMX:

* **The textures are inside it.** The PNGs are written to `Texture2D/` *and* embedded in the file, so the
  model stays textured when the `.fbx` is copied somewhere on its own (Blender packs the embedded copy).
  `-EmbedTextures off` writes the relative paths only, like the PMX. Either way the file is about the size
  of the `.pmx`.
* **Import it with *Ignore Leaf Bones* ticked.** An FBX carries no bone length, so a reader infers one from
  the distance to a bone's children; the writer adds a stub at the tip of every chain end - the same trick
  Blender's own exporter has as `add_leaf_bones` - so that the tips have a length at all. Ticking the box
  drops the 160 stubs and leaves the 270 bones the refined rig has, lengths intact. Without it you get 430
  bones, the extra ones being the stubs.
  [FBX_EXPORT.md](FBX_EXPORT.md#8-bone-lengths-in-an-fbx) has the measured detail and what stays
  approximate either way.

### 3. Record a motion

```powershell
# one clean loop of a running cycle
./Tools/headless_export.ps1 -Char 1001 -Motion anm_rac_type01_run02_stride -RecordVmd D:/out/run.vmd

# a one shot: let it play to its end first, or the recording is one repeated pose
./Tools/headless_export.ps1 -Char 1001 -Motion anm_res_chr1001_001 -PlaySeconds 8 -RecordVmd D:/out/res.vmd
```

* `-Motion` takes a **substring of the motion's asset path** (`run02_stride` is enough). It is matched
  exactly first, then case-insensitively, and the match is printed.
* The recording is exactly one pass over the clip: **`clip length × fps + 1` frames** at `-RecordFps`
  (30 by default). A looping clip closes on itself, a one shot keeps its own ending.
* **Pick a motion that moves.** The clip the viewer loads on its own is an idle whose bone rotations are
  nearly zero, which makes a useless test and a dull video. `anm_rac_type01_run02_stride` is a good
  baseline.
* `-PlaySeconds <s>` lets the motion play for that much wall-clock time *before* recording. Batch mode
  burns through frames in milliseconds, so a one shot never reaches its end without it - and a clip that
  has already finished is exactly the state the viewer is in when a user hits record after the animation
  ended.
* `-RecordMode realtime` keeps the legacy sampler (one wall-clock `FixedUpdate` per frame). It drifts
  against the animator; `deterministic` is the default and the one to use unless you are comparing.
* `-RecordReduction <n>` keeps every `n`th key. `0` (the default) uses `Config.json`'s
  `VmdKeyReductionLevel`, which is every frame.
* **The viewer's look-at solve is suspended for the whole recording session**, and it is **off by default** in the
  viewer. It bends the spine and head towards the camera on top of the clip, and the sampler reads the very bones
  it drives, so its contribution could be written into the motion as though the animation had asked for it.
  `UnityHumanoidVMDRecorder.StartRecording`/`StopRecording` hold `UmaContainerCharacter.SuspendLookAt()`, so **both**
  record buttons - the manual one and the auto-record one - sample the same way the reference was captured; a
  suspension placed inside only one of those paths is what left the manual button sampling a solved pose against an
  unsolved reference for several builds. Whether the setting still changes a recording now that the reference pose
  is also reset correctly (`docs/VMD_RECORDING.md`) has **not been measured** - the last takes that showed the
  toggle mattering predate those fixes. Viewing with it on is unaffected.
* **`UMA_GHOST_DUMP=1` records what the sampler actually used** to `ghost_dump.jsonl` in the temp directory:
  per bone and per frame, the value written (the ghost's local rotation), the live bone's rotation, and both
  parent frames; plus one reference row per bone (`role: "reference"`) holding the pose the take is a delta
  against, and one row per control bone the ghost chain skips (`role: "skipped"`). A recording that comes out wrong
  cannot be argued about from the vmd alone, because the value in the file came from the ghost's frame rather than
  the bone's own. `Tools\ghost_dump_check.py` reads the dump and separates a constant frame conversion (what the
  ghost is for) from a difference that moves with the animation. Off unless the variable is set, and it does not
  change what is recorded. Flushed from `StopRecording`, so either button produces it.
  See `docs/VMD_RECORDING.md` for what these three rows have caught.

### 4. Model and motion in one run

```powershell
./Tools/headless_export.ps1 -Char 1001 -Costume 00 -Motion anm_rac_type01_run02_stride `
    -Out D:/out/special_week.pmx -RecordVmd D:/out/special_week.vmd
```

One Unity launch, both files, and - the point of doing it in one run - the model and the motion are
recorded from the same loaded character, so the morph names match by construction.

### 5. Screenshot the game's own render

This is the baseline for judging a Blender material or shading setup: what the viewer actually draws,
with the game's own materials, lighting and post processing.

```powershell
# the face, nothing exported
./Tools/headless_export.ps1 -Char 1001 -Costume 00 -ShotView face `
    -Screenshot D:/out/game_face.png -ShotOnly

# the same view from several angles in one run
./Tools/headless_export.ps1 -Char 1001 -Costume 00 -ShotView face -ShotYaws 0,45,-45,90 `
    -Screenshot D:/out/game_face.png -ShotOnly

# a model and a picture of it side by side
./Tools/headless_export.ps1 -Char 1001 -Costume 00 -Out D:/out/m.pmx -ShotView upper `
    -Screenshot D:/out/game_upper.png
```

| `-ShotView` | Framing |
|---|---|
| `face` | the head nearly fills the frame (distance = 15% of the model's height) |
| `head` | head and shoulders (28%) |
| `upper` | upper body (80%) |
| `full` | the whole model (190%) - the default |

`-ShotYaws 0,45,-45` writes `game_face.png`, `game_face_yaw+45.png`, `game_face_yaw-45.png`. `-ShotYaw
<deg>` (singular) is the same thing for one angle and orbits the camera around the model's own facing
direction. `-ShotWidth` / `-ShotHeight` default to 1280×720, `-ShotTransparent` drops the background, and
`-ShotOnly` skips the export so the run is just the picture.

The camera is put back afterwards and the UI layer is excluded the same way the in-app screenshot button
excludes it, so taking a shot does not disturb an export that happens in the same run.

### 6. Props, scenes and their textures

```powershell
./Tools/headless_export.ps1 -ListProps -ListPropsFilter home10001   # find scene assets (filter optional)
./Tools/headless_export.ps1 -Prop pfb_env_home10001_main000_000 -Out D:/out/home.pmx
./Tools/headless_export.ps1 -Prop pfb_env_home10001_main000_000 -Variant 214 -Out D:/out/home_214.pmx
```

A scene often ships **no textures at all**, because the game assigns one of several texture sets at
runtime (time of day, weather, event banner). The viewer resolves a set for each textureless material and
`-Variant <code>` picks which one - the codes are the ones in the materials panel of the GUI, e.g. `212`
or `214`. Without `-Variant` the set covering the most materials is used.

`-ScanProps "<filter>" -ScanCount <n>` loads each matching scene in turn and prints one line of material
health per scene, which is how you find the ones that would render white instead of guessing:

```
[UmaHeadlessExport] [3/12] 3d/env/home/home10001/main/pfb_env_home10001_main000_000
    renderers=3 slots=20 textureless=1 noMainTexProp=0 missingShader=0 shaders=2 textureSet=212 of [212,214] fixed=11
```

`textureless` after the resolver is the number of slots that still have no `_MainTex`. A few scenes
(`3d/env/live/common`, `3d/env/race/common`) have no texture assets anywhere in the game data - the game
colours them at runtime - so showing untextured there is expected, not a bug.

### 6b. Export a whole batch of scenes, and find the ones that throw

`-ScanProps` only reports; `-ScanExport` *exports* every matched scene and keeps going when one throws:

```powershell
# every downloaded scene under 3d/env/race, one editor launch for the whole batch
./Tools/headless_export.ps1 -ScanProps "3d/env/race" -ScanCount 40 -ScanExport -ScanOut D:/scan
```

Each scene gets a line saying whether it produced a file:

```
[UmaHeadlessExport] [7/40] exporting 3d/env/race/race00000/race00000_8001_000
[UmaHeadlessExport] [7] 3d/env/race/race00000/race00000_8001_000: exported (4595 bytes)
[UmaHeadlessExport] [8] 3d/env/race/race00002/.../pfb_env_race00002_cutt000_002: FAILED: IndexOutOfRangeException: ...
[UmaHeadlessExport] sweep: 35 of 40 prop(s) exported, 5 failed - the failures are named above
```

and a summary at the end; the run exits non-zero if any scene failed, so it works as a gate in a script.
The `.pmx` files land in `-ScanOut` (default `<project>/HeadlessExports/scan`) and share one `Texture2D/`
folder, since a scene's textures are written beside its model.

This is the cheap way to answer "which scenes are broken": one editor launch for the batch instead of one
per scene, which is where nearly all of a run's time goes. The sweep is how the `IndexOutOfRangeException`
that made `pfb_env_race10201_cutt000_002` come out as a texture folder with no model was found to affect
whole families of scenes (the race scenes with billboard trees, and every scene that ships an environment
prop) - see [the failure modes](#6c-why-a-scene-can-export-its-textures-and-no-model) below.

### 6c. Why a scene can export its textures and no model

The exporter writes the `Texture2D/` folder *before* it reads the geometry, so a model that fails is always
a folder of PNGs and nothing else. Two things that used to throw there are the reason: both are scene-only,
because a character's meshes and materials are authored for the export while a scene's are whatever the
game shipped.

| Cause | What the scene looks like, measured |
|---|---|
| a mesh with no normal channel | the exporter walked `vertices.Length` and indexed `mesh.normals`. The race scenes' tree billboards have 4 to 70 vertices and no normals at all: `pfb_env_race10201_cutt000_002` alone has **688** such meshes, and the very first one aborted the export. Normals are now rebuilt from the mesh's own triangles, since a zero normal shades as black in MMD |
| a model with no textures at all | the textureless-material fallback read `TextureList[0]` on an empty list. `3d/env/race/common/prop/prop0000/pfb_env_cmn_prop0000` and its four siblings carry no texture assets anywhere (the game colours them at runtime), so they threw `ArgumentOutOfRangeException`. Such a part is now written with PMX's own "no texture" |

Two more guards went in beside them, in the same shape, and neither has been seen to fire yet: a renderer
with no material at all (`materials[Count - 1]` on an empty list - such a submesh is now exported as an
untextured part named after its object), and a mesh whose uv channel is missing (`vertices.Length` walk
against `uv` - a missing uv is now zeros, so the surface loses its texture and the geometry stays right).

Both of the real ones announce themselves in the log, so a scene that needed the repair is visible rather
than silently patched: `holds N vertices, 0 normals and M uvs` per mesh (the first six by name, then a
count), and `no main texture at all, and the model has no textures at all`.

### 6d. A file that is written, but with corrupt indices

The sweep above is also how the third one surfaced, and it is not visible from the shell: the export
succeeds and the file is the right size, but its indices point at the wrong things.

A pmx index is **signed** - -1 is how the format says "none", for a parent bone or a texture - so one byte
addresses 128 entries, not 256. (The vertex index is the one the specification calls unsigned; the note at
the end of this section says why it takes the signed width here anyway.) The exporter picked its widths with
unsigned thresholds (`> 255` before widening), which is invisible on a character's skeleton (437 bones, 2
bytes either way), shows up only in its morph list (205 morphs, 1 byte), and is flat wrong on exactly the
scenes whose skeleton or material list lands in 129..255:

| Scene | Counts | What the file said |
|---|---|---|
| `pfb_env_race10006_cutt000_002` | 230 bones, 212 materials | 1-byte bone and material indices, so every index past 127 came out negative on the way out (`(sbyte)213` is -43). `pmx_inspect integrity` reported vertices "skinned to bone -27" |
| character 1001 | 205 morphs | 1-byte morph indices - harmless today only because nothing in that file references a morph by index (a group morph would), and enough to fail `integrity` |

Widths are now chosen by what the width can *address*: 128 entries in one byte, 32768 in two, otherwise four
(`ModelExporter.IndexSize`, used for every index including the vertex one - the pmx spec calls that one
unsigned, and the signed rule is what the **one-byte** width needs, because 129 to 255 entries used to wrap to
negative indices there: `MMDReaderWriteUtil.ReadIndex` reads one byte signed, two unsigned and four signed, so
the sign bit only costs capacity at one byte). Any export older than that is worth a second look:

```powershell
uv run Tools/pmx_inspect.py integrity <file.pmx>   # exits non-zero on either failure
```


### 7. Change what the exported file contains

| Option | Effect |
|---|---|
| `-MorphNameMode 0\|1\|2` | Morph and shape-key spelling, for the model **and** the motion it is recorded with: `0` tagged (`Eye_20_R(XRange)[M_Face]`, what the **stock** Blender addon matches, but 27-30 bytes - too long for a vmd morph name field, so no motion can drive a model exported this way), `1` short (`Eye_20_R`), `2` unified (`Eye_XRange_R`) - the default, and what the patched addon in this fork resolves. Models and motions must use the same mode or the motion's morph tracks land on nothing, which is why there is no separate motion-side switch. `3` used to be a fourth mode ("both", tagged plus a short alias) and is rejected now. |
| `-APose` | Exports the model with both upper arms rotated 38.5° into the A-pose that recorded motions are relative to, so a model and a motion line up in Blender without posing anything by hand. **Off by default** - rigging and retargeting tools expect a T-pose rest. |
| `-PlainMaterials` | Writes plain MMD materials: no uma shader settings in the material comment, plain diffuse/specular/outline values. Use it for a model meant to be used *without* the uma addon. |
| `-TextureVariants` | Also writes the game's **wet** and **dirt** maps beside the dry ones, under the game's own names (`<map>_wet.png`, `<map>_dirt.png`), and names them per material with `wet=` / `dirt=` comment tokens plus one `texture variants:` line in the model comment. Only a shader that knows what those maps are (the uma addon) uses them; MMD itself ignores them entirely. **Off by default** because it is not free: on character 1001 costume 00, 8 of 34 maps have a wet twin and 2 have a dirt map, about +2.1 MB on a 2.7 MB texture set. See [PMX_EXTENSIONS.md](PMX_EXTENSIONS.md#the-texture-variants) for the contract. |
| `-NoExtraMorphs` | Writes no advanced morphs at all: no deploy or tear morphs, and none of the `morph=` / `blend=` / `scroll=` tokens that describe them. The morph *parts* then stay wherever the game draws them, so the decals are permanently visible - that is what `Config.PmxExtraMorphs = false` does, and it is the opposite of `-PlainMaterials`, which keeps the morphs and drops the shader values. |
| `-DumpMaterials` | Before exporting, logs what every material resolved to - textureless slots, missing shaders, and every property of every shader the model uses. Use it when a model comes out white or oddly shaded. |

### 8. Voices, and what they say

The Sound page in the GUI shows a clip's line and puts it in the tag of an exported mp3; the CLI can do the
same, and can print the text without exporting anything:

```powershell
./Tools/headless_export.ps1 -Transcript snd_voi_home_100100     # a character's recorded lines
./Tools/headless_export.ps1 -Transcript 041047007               # a story's dialogue (also accepts snd_voi_story_041047007)

./Tools/headless_export.ps1 -ExportAudio snd_voi_story_041047007 -ExportClip 3 -Out D:/out/line.mp3
./Tools/headless_export.ps1 -ExportAudio snd_voi_home_100100 -ExportClip 1 -Out D:/out/home.mp3
```

`-Transcript` prints what the sheet resolves to and then, clip by clip, what the viewer would label that
clip with; `-ExportAudio` writes the mp3 and reads the ID3 tag back out of it, which is how the tag writer
is checked. The text comes from the game's own data - the master database for recorded character voices,
the story timeline assets for dialogue - and never from the network. [TRANSCRIPTS.md](TRANSCRIPTS.md) has
the details, the coverage, and the one family of sheets where a clip answers for several lines.

### 9. Check what you got

Nothing here needs Unity, Blender or the game:

```powershell
uv run Tools/pmx_inspect.py summary   D:/out/m.pmx        # counts, bounding box, what is in the file
uv run Tools/pmx_inspect.py check     D:/out/m.pmx        # the eye-morph / eye-skinning contract
uv run Tools/pmx_inspect.py names     D:/out/m.pmx        # morph names unique and within a vmd's 15 bytes
uv run Tools/pmx_inspect.py integrity D:/out/m.pmx        # declared index sizes vs the counts they hold
uv run Tools/pmx_inspect.py tails     D:/out/m.pmx --pairs   # where the bones point, left/right mirroring
uv run Tools/pmx_inspect.py diff      a.pmx b.pmx         # what changed between two exports

uv run Tools/vmd_inspect.py summary   D:/out/m.vmd        # bones, frames, morph tracks
uv run Tools/vmd_inspect.py loop      D:/out/m.vmd        # does frame 0 == last frame for every bone
uv run Tools/vmd_inspect.py motion    D:/out/m.vmd        # does the motion actually move?
```

All of them exit non-zero when the check fails, so they can be used as a gate in a script. `loop` is
*expected* to fail on a one-shot recording - its last frame is the animation's ending, not a repeat of
the first pose.

---

## Every argument

Run `Get-Help ./Tools/headless_export.ps1 -Detailed` for the same list from PowerShell.

### What to load

| Argument | Default | Meaning |
|---|---|---|
| `-Char <id>` | - | Character id to load (`-ListChars` shows them) |
| `-Costume <id>` | first costume | Costume id, e.g. `00` |
| `-Prop <path>` | - | Load a prop/scene instead of a character. Asset path, file name, or any unique substring of either |
| `-Motion <substring>` | the viewer's default idle | Motion to load onto the character before exporting or recording |
| `-Scene <path>` | `Assets/Scenes/Version2.unity` | The scene to boot. Only change this if you know why |
| `-Variant <code>` | the widest set | Environment texture set to apply to a prop/scene (e.g. `212`) |

### What to produce

| Argument | Default | Meaning |
|---|---|---|
| `-Out <file.pmx>` | `HeadlessExports\<id>_<costume>.pmx` | Where to write the model |
| `-RecordVmd <file.vmd>` | - | Record one pass over the playing motion to this file (characters only) |
| `-Screenshot <file.png>` | - | Write the game's own render of the loaded model to this file |
| `-ShotOnly` | off | Take the screenshot and stop, do not export a model |

### How to produce it

| Argument | Default | Meaning |
|---|---|---|
| `-RecordFps <n>` | 30 | Frame rate of the recording |
| `-RecordReduction <n>` | 0 (use `Config.json`) | Keep every `n`th key; `0` means the configured value, normally 1 |
| `-RecordMode deterministic\|realtime` | `deterministic` | `deterministic` pins one frame per sample; `realtime` is the legacy sampler |
| `-MorphNameMode -1\|0\|1\|2` | -1 (use `Config.json`, i.e. unified) | Morph-name spelling, see [above](#7-change-what-the-exported-file-contains) |
| `-APose` | off | Export the A-pose rest pose |
| `-PlainMaterials` | off | Export plain MMD materials |
| `-NoExtraMorphs` | off | Export without the advanced morphs and their tokens |
| `-TextureVariants` | off | Export the game's wet and dirt maps and name them per material |
| `-PlaySeconds <s>` | 0 | Let the motion play this long (wall clock) before exporting/recording |
| `-ExtraFrames <n>` | 30 | Frames to let the model settle before the export or recording starts |
| `-DumpMaterials` | off | Log material and shader diagnostics first |
| `-DumpFace` | off | Log the face pipeline, and write a `<model>.pmx.vertices.txt` dump of every vertex the exporter wrote. A diagnostics probe, not something a normal export needs |
| `-ShotView face\|head\|upper\|full` | `full` | Screenshot framing |
| `-ShotWidth` / `-ShotHeight` | 1280 / 720 | Screenshot size |
| `-ShotYaw <deg>` | 0 | Camera angle around the model's facing direction |
| `-ShotYaws <list>` | - | Several camera angles in one run, e.g. `0,45,-45` |
| `-ShotAzimuths <list>` | - | Light angles for the face shading, one image per angle; combine with `-ShotYaws` for a grid |
| `-ShotLightElevation <deg>` | scene's own | Rebuild the key light at this height instead of the scene's |
| `-ShotTransparent` | off | Screenshot without the background |
| `-ScanProps <filter>` | - | Load every matching `3d/env` scene **or `3d/chara` prop** in turn and report its material health plus how it is rigged: skinned renderers, bone count, and up to eight of the hand bones and eight of the other bone names it carries, which is the only description the game ships for a prop (see [PROP_LABELS](PROP_LABELS.md)). One editor boot answers a whole family's question - `-ScanProps pfb_chr_prop -ScanCount 60` reports sixty character props as single-`Root` meshes, and `Tools/prop_rig_names.py <log>` tallies a whole family's names |
| `-ScanCount <n>` | 10 | How many entries `-ScanProps` covers |
| `-ScanExport` | off | With `-ScanProps`: export each scene instead of reporting it, and continue past the ones that throw |
| `-DumpProps <dir>` | - | Export **every prop** into its own folder - `&lt;family&gt;/&lt;prop folder&gt;/&lt;prefab&gt;.pmx` with its own `Texture2D/` - and write an `index.json` describing all of them: name, family, manifest path, relative paths, bytes, bone count, hand bone count and bounds. Re-running resumes: a model already on disk is skipped and the index keeps what was written. About 1 377 models, roughly 180 MB and 15-25 minutes. See [PROP_DUMP](PROP_DUMP.md) |
| `-DumpPropsFilter <substring>` | - | Only dump props whose manifest path contains this: `rich_prop`, `toon_prop`, `chr_prop`, or a single prop's folder |
| `-DumpPropsCount <n>` | 0 | How many props to dump; 0 means all of them |
| `-ScanOut <dir>` | `<project>/HeadlessExports/scan` | Where `-ScanExport` writes its `.pmx` files and the shared `Texture2D/` folder |
| `-Transcript <sheet>` | - | Print the dialogue a story voice sheet carries, and what the viewer would label each clip of that asset, then exit |
| `-ExportAudio <sheet>` | - | Write a sound asset to an mp3 (with `-Out`) carrying its transcript in the ID3 tag |
| `-ExportClip <n>` | 0 | Which clip of that asset; `-1` mixes the whole asset |
| `-ListChars` / `-ListCostumes` / `-ListProps [filter]` | - | Print and exit without loading |
| `-ListTextures [filter]` | - | Print the character *texture* assets matching a substring and exit. Use a character id (`-ListTextures -ListTexturesFilter bdy1001_00`) to see what sets a character ships: the wet maps are the `_wet` twins of the dry ones, and any other set - the `_dirt` maps, for example - shows up here as its own name |
| `-ListFamilies` / `-ListFamiliesFilter <text>` | - | Print the families under `3d/chara/` (body, head, tail, mini, common, prop, attach, richprop, sweat, toonprop) matching a substring and exit: how many manifest entries each holds, how many are prefabs, the naming forms the family is built from (digit runs masked, e.g. `pfb_toon_prop####_##`), and three samples. Metadata only - it reads the manifest and opens no bundle, so unlike `-Prop` it cannot trigger a download. A filter that selects one family also lists that family's folders (`attach1048_00`) with entry counts |
| `-Attach <pattern>` | - | Wear the `3d/chara/attach` prefab(s) whose path contains `<pattern>` on the loaded character; it is an array, so several go `-Attach a,b` (`-Attach a -Attach b` is a binding error). Applied after the settle frames and before any screenshot or export, so the run measures the character as worn. An attach prefab carries a copy of the character rig and is retargeted onto the character's bones by name, so it follows the animation. Ten characters ship the family; `-ListFamilies attach` lists their sets. Toon props are *not* character attachments - see `docs/ASSET_FAMILIES.md` |

### Listing and plumbing

| Argument | Default | Meaning |
|---|---|---|
| `-Timeout <s>` | 600 | Abort a run after this long |
| `-ProjectPath <dir>` | the script's parent | The Unity project to run |
| `-UnityExe <path>` | auto-detected | The editor to run |
| `-LogPath <file>` | `Logs\headless_export_<timestamp>.log` | Where Unity's log goes |

> **Diagnostics probes.** `-ShotTexture`, `-ShotGlobal`, `-ShotFloat`, `-ShotMaterial` and `-PinPose`
> exist to interrogate the game's compiled shader during shading work - they override a texture, a shader
> global, a material float, or freeze the pose at a normalized time before the screenshot. They are
> internal research switches (several `;`-separated values each), they are not part of any normal export,
> and a user-facing CLI should not expose them. See the rework notes at the end.

---

## Where the files land

| What | Where |
|---|---|
| The model | `-Out`, else `<project>/HeadlessExports/<charid>_<costume>.pmx` |
| Its textures | a `Texture2D/` folder next to the `.pmx` - move them together |
| The motion | `-RecordVmd` (no default) |
| The mp3 | `-Out`, else `<project>/HeadlessExports/<asset name>.mp3` |
| Screenshots | `-Screenshot`, with `_yaw+45` / `_az+60_yaw0` inserted before the extension when there are several angles |
| A `-ScanExport` sweep | `-ScanOut`, else `<project>/HeadlessExports/scan`, one `.pmx` per scene plus one shared `Texture2D/` |
| The run's log | `-LogPath`, else `<project>/Logs/headless_export_<timestamp>.log` |

---

## Reading what happened

The wrapper prints the interesting log lines and exits with a code you can test:

| Exit code | Meaning |
|---|---|
| `0` | The run reported success (`[UmaHeadlessExport] OK` in the log) |
| `1` | The run reported a failure (`[UmaHeadlessExport] FAILED: <reason>`), or no result marker appeared within `-Timeout` + 120 s |
| non-zero, no log | The wrapper itself refused: nothing to do, no Unity found, a parameter it could not pass on |

**Unity's own process exit code means nothing here.** Its launcher process can exit before the editor it
spawned has finished, so the wrapper waits for the runner's own marker in the log instead. That is also
why a failed run is diagnosed from the log, not from the shell's exit status alone.

Useful lines in the log:

| Line | What it tells you |
|---|---|
| `[UmaHeadlessExport] viewer booted: ... characters, ... shaders` | the viewer came up; the counts should look sane |
| `[UmaHeadlessExport] loading character 1001 (Name) costume 00` | what was actually loaded |
| `[UmaHeadlessExport] the save dialog would suggest 'special_week.pmx'` | the name the GUI would have offered |
| `[UmaHeadlessExport] recording one loop of '<clip>' (1.200s) at 30fps` | which clip was recorded and how long it is |
| `[UmaHeadlessExport] OK <path> (<bytes> bytes)` | the file was written |
| `[UmaHeadlessExport] FAILED: <message>` | what went wrong; the message is specific |

### Known harmless warning

Importing a recorded motion in Blender prints `WARNING: not found bone Ankle_L_IK` (and `Ankle_R_IK`).
The recorder names the foot IK bones `Ankle_L_IK` while the model's own bones are `Ankle_L_IK_Handle`,
which does not fit a vmd's 15-byte name field - and the exported model contains no IK-constrained bones
at all, so the track has nothing to drive. Dropping it changes nothing.

---

## When something goes wrong

| Symptom | Cause and fix |
|---|---|
| `project already open in another instance` | The editor (or another batch run) holds the project. **Close Unity first.** A crashed run can leave a stale `Temp/UnityLockfile`; the wrapper removes it when no Unity process is using the project, otherwise delete it by hand. |
| `Could not find Unity.exe` | No editor in `C:\Program Files\Unity\Hub\Editor` matching the project's version. Pass `-UnityExe "C:\path\to\Unity.exe"`. |
| `Nothing to do: pass -Char <id> ...` | The wrapper refuses to launch Unity with nothing to do. Add `-Char`, `-Prop`, `-RecordVmd` or a `-List*` switch. |
| `No result marker in the log (timeout after ...)` | The run never finished. Open the log - the last `[UmaHeadlessExport]` line says which stage it was in. A first run that is still compiling, or a blocked network on startup, is the usual cause; `-Timeout <s>` raises the limit. |
| `FAILED: motion '...' not found` | `-Motion` matched nothing. Try a shorter substring of the asset path. |
| `FAILED: character 1001 not found` | Wrong id, or the game data is not loaded. Check `-ListChars` first, and the viewer's data path setting. |
| `FAILED: -umaRecordVmd needs a character with an animator` | `-RecordVmd` with `-Prop`; motions belong to characters. |
| The recording does not move (`vmd_inspect.py motion` fails) | The clip is an idle, or a one shot that was already over. Use `-PlaySeconds <s>` and a motion that actually moves. |
| The model is white in Blender | The textures did not travel with it, or the materials were never resolved. Keep the `Texture2D/` folder next to the `.pmx`, and run with `-DumpMaterials` to see what resolved. |
| A `Texture2D/` folder appeared but no model | The export threw after writing the textures and before opening the `.pmx`; the `FAILED:` line above names the exception (`IndexOutOfRangeException` on a scene's mesh or texture list is the historical pair - see [6c](#6c-why-a-scene-can-export-its-textures-and-no-model)). `-ScanProps "<filter>" -ScanExport` finds every scene in a family that does it in one run. |
| The model imports but its skinning or materials point at the wrong thing | A pmx index that outgrew its width - see [6d](#6d-a-file-that-is-written-but-with-corrupt-indices). `uv run Tools/pmx_inspect.py integrity <file>` names it. |
| The same model exports a different file when exported again | It did, for the cheek blush: the old alpha-only bake named its output after the texture it was handed, and the first export into a folder handed it an *unnamed* copy (`new Texture2D`), so it wrote and listed `_blush.png`; a re-export into the same folder skipped that copy and named the bake `tex_..._cheek0_blush.png`. That bake is gone now - the game's cheek map is exported as it is and the addon derives the alpha at shade time - so this no longer happens. The rule that fixed it still holds for any future derived file: name it after the name the export writes, never after the texture object. |
| `uv: command not found` | Only the checkers need it; the export itself does not. |
| A run that used to work now times out | Check for a leftover Unity process holding the project, and whether an editor window opened on the same project. |

---

## Under the hood

`headless_export.ps1` is a thin wrapper. What it actually runs is:

```
Unity.exe -batchmode -projectPath <project> -executeMethod UmaHeadlessExport.Run ^
    -umaChar 1001 -umaCostume 00 -umaOut D:/out/m.pmx -umaTimeout 600 -umaExtraFrames 30 ^
    -logFile <project>/Logs/headless_export_<timestamp>.log
```

Everything after the wrapper's own concerns (`-ProjectPath`, `-UnityExe`, `-LogPath`) maps to one `-uma*`
argument. The wrapper exists because those Unity-side arguments are an internal interface: they are
positional, unvalidated, and Unity swallows anything it does not recognise.

| Wrapper | Unity-side |
|---|---|
| `-Char` / `-Costume` / `-Prop` / `-Motion` / `-Scene` / `-Variant` | `-umaChar` / `-umaCostume` / `-umaProp` / `-umaMotion` / `-umaScene` / `-umaVariant` |
| `-Out` / `-RecordVmd` / `-Screenshot` | `-umaOut` / `-umaRecordVmd` / `-umaScreenshot` |
| `-MorphNameMode` / `-APose` / `-PlainMaterials` | `-umaMorphNameMode` / `-umaAPose` / `-umaPlainMaterials` |
| `-NoExtraMorphs` | `-umaNoExtraMorphs` |
| `-TextureVariants` | `-umaTextureVariants` |
| `-RecordFps` / `-RecordReduction` / `-RecordMode` | `-umaRecordFps` / `-umaRecordReduction` / `-umaRecordMode` |
| `-PlaySeconds` / `-ExtraFrames` / `-Timeout` | `-umaPlaySeconds` / `-umaExtraFrames` / `-umaTimeout` |
| `-ListChars` / `-ListCostumes` / `-ListProps [filter]` | `-umaListChars` / `-umaListCostumes` / `-umaListProps [filter]` |
| `-ListTextures [filter]` | `-umaListTextures [filter]` |
| `-ListFamilies` / `-ListFamiliesFilter <text>` | `-umaListFamilies [filter]` |
| `-Attach <pattern>` | `-umaAttach <pattern>` |
| `-DumpMaterials` / `-DumpFace` / `-ScanProps` / `-ScanCount` | `-umaDumpMaterials` / `-umaDumpFace` / `-umaScanProps` / `-umaScanCount` |
| `-ScanExport` / `-ScanOut` | `-umaScanExport` / `-umaScanOut` |
| `-DumpProps` / `-DumpPropsFilter` / `-DumpPropsCount` | `-umaDumpProps` / `-umaDumpPropsFilter` / `-umaDumpPropsCount` |
| `-Transcript` / `-ExportAudio` / `-ExportClip` | `-umaTranscript` / `-umaExportAudio` / `-umaExportClip` |
| `-ShotView` / `-ShotWidth` / `-ShotHeight` / `-ShotYaw` / `-ShotYaws` | `-umaShotView` / `-umaShotWidth` / `-umaShotHeight` / `-umaShotYaw` / `-umaShotYaws` |
| `-ShotAzimuths` / `-ShotLightElevation` / `-ShotTransparent` / `-ShotOnly` | `-umaShotAzimuths` / `-umaShotLightElevation` / `-umaShotTransparent` / `-umaShotOnly` |
| `-ShotTexture` / `-ShotGlobal` / `-ShotFloat` / `-ShotMaterial` / `-PinPose` | `-umaShotTexture` / `-umaShotGlobal` / `-umaShotFloat` / `-umaShotMaterial` / `-umaPinPose` |

Inside the editor, `Options.Parse` reads those arguments, `Options` is serialized into `SessionState` so
it survives the script-domain reload that entering play mode triggers, and an `EditorApplication.update`
loop re-armed from `[InitializeOnLoadMethod]` walks the run through its stages: boot the scene, wait for
the viewer, load the model, settle, optionally screenshot, optionally record the motion, export, exit.

---

## What a shipped CLI would need

This is the part that matters if the interface is going to be reworked. Today's CLI is a good harness and
a poor user interface, for reasons that are all in the code:

**Why it cannot ship as it is**

* `Assets/Editor/UmaHeadlessExport.cs` is wrapped in `#if UNITY_EDITOR` and lives under `Assets/Editor/`,
  so Unity never compiles it into a player. A released `UmaViewer.exe` has no argument handling at all -
  not even `--help`.
* It leans on editor-only APIs that do not exist in a player: `EditorApplication.Exit`,
  `EditorApplication.EnterPlaymode`, `EditorApplication.update`, `EditorApplication.timeSinceStartup`,
  `EditorSceneManager.OpenScene`, `SessionState` (which is what carries the run across the play-mode
  domain reload), and `[InitializeOnLoadMethod]`.
* It assumes it is the only thing running: the run is a state machine advanced by editor frames, keyed by
  `SessionState` strings, and it terminates the process (`EditorApplication.Exit`) rather than returning.
* The wrapper is PowerShell, i.e. Windows-only, and its only progress channel is scraping the log.

**What has been done**

The runner moved out of `Assets/Editor/` into `Assets/Scripts/Cli/`, so it is ordinary runtime code and is
compiled into the player:

* `CliHost` is the four things that were editor-shaped - the frame loop, exiting, entering play mode, opening
  the scene - each with an editor branch and a player branch.
* `CliState` is `SessionState` in the editor (where entering play mode clears every static field) and a plain
  dictionary in a player (where nothing reloads).
* The five diagnostic probes that read animation data - `DumpTearBundles`, `DumpTearAnimator`,
  `DumpClipSerialized`, `DumpShaderFacts`, `DumpShaderEffectData`, `DumpFacePipeline` - moved to
  `Assets/Editor/CliEditorProbes.cs` and install themselves into `CliEditorProbeHooks` when the editor loads,
  including after the domain reload that play mode causes. In a player the hooks stay null and the flag says so.
* `UmaCliBootstrap` is the player's entry point: a `[RuntimeInitializeOnLoadMethod]` that notices CLI flags,
  hides the UI, adds a driver object that delivers frames, and starts the same stage machine.
* `UmaPlayerBuild.BuildWindows` builds the player, because the only way to know any of this stays true is to
  build one and run a job through it.

Verified both hosts on the same jobs: a character export (identical structure, 5.3 s in the player against
10-11 s in the editor), a prop dump with its `index.json`, `--help`, `--json`, and the editor-only probe
reporting itself as unavailable in the player.

The interface on top of that is now in place as well: a table of commands (`export model`, `list props`,
`dump props`, `doctor`, ...) that validates what it can before the run starts, `--doctor` for the environment,
and english names that survive a failed download - the tables are cached beside `Config.json`, and a run waits a
few seconds for them instead of silently exporting the japanese name in the english name field. That last one
was found the honest way: the same model exported from the editor and from the player came out 18 bytes apart,
and the difference was the name.

Batches (`--all-costumes`, `--all-chars`) are in as well: one boot for a whole character or the whole game,
resumable by skipping what is already on disk.

`dump models` is the last piece the web side needed from us: one index for characters and props, textures shared
per kind so a PMX resolves them with no help, a zip per model for a single download, and a validator that says
whether a dump is complete. Browser rendering is the plan there; prerendered previews stay the fallback.

**What is still open**

The first step of that rework is in: the parser is table-driven, every flag has a `--long` name with the old
`-uma*` one as an alias, `--help` prints the table it is parsed from, `--about` names the build, `--json` gives
a result line, and Unity can be driven directly without the wrapper (see *Two spellings, one parser* above).
That buys discoverability and a machine-readable result without moving any code out of the editor.

The rest is still open, and the reasoning above is unchanged: the CLI cannot be a feature of the shipped
viewer until the driver lives in runtime code, and the 15 second editor boot per invocation is the floor for
anything editor-hosted. Deciding whether to spend that is a product question, not a technical one.

**To make it a real feature of the shipped viewer**

1. **Move the driver into runtime code** (something like `Assets/Scripts/Cli/`), compiled into the player,
   entered from a bootstrap scene when `Environment.GetCommandLineArgs()` carries the CLI's own flag.
   `Application.Quit(exitCode)` replaces `EditorApplication.Exit`; `SceneManager.LoadScene` replaces
   `EditorSceneManager.OpenScene`; a normal `MonoBehaviour` update loop replaces the
   `EditorApplication.update` subscription, and the state no longer has to be smuggled through
   `SessionState` at all.
2. **Drop the play-mode dance.** The whole `WaitPlayMode` / `WaitStartup` / re-arm complexity exists only
   because the editor has to enter play mode to reach the viewer's runtime load path. In a player that is
   simply "start up".
3. **Resolve the game data folder itself.** A user CLI has to do what *Settings → Other → Change DataPath*
   does - read `Config.json` next to the executable, auto-detect, and fail with a message that says what
   to do rather than an exception.
4. **Give it a real interface.** Subcommands and validated options instead of positional `-uma*` flags:

   ```
   umaviewer list characters
   umaviewer list costumes 1001
   umaviewer list props home
   umaviewer export model  --char 1001 --costume 00 --out special_week.pmx
   umaviewer export motion --char 1001 --motion run02_stride --out run.vmd --fps 30
   umaviewer screenshot    --char 1001 --view face --yaws 0,45,-45 --out face.png
   umaviewer check <file.pmx|file.vmd>
   ```

   with `--help` on every command, `--version`, a `--dry-run` that prints what would be loaded, and
   `--json <path>` so CI does not have to parse log lines.
5. **Leave the research probes behind.** `-umaShotTexture`, `-umaShotGlobal`, `-umaShotFloat`,
   `-umaShotMaterial`, `-umaPinPose` and `-umaDumpFace` exist to interrogate the game's compiled shader
   while porting its shading. They are not export features and should stay in the development harness.
   (Those live in the companion `UmaViewer-DevTools` directory now, which is also where the Blender-side
   verification and rendering tools went.)
6. **Tidy what a user does see.** Concretely, in the current interface: `-umaShotYaw` (one angle) versus
   `-umaShotYaws` (a list) is a distinction nobody wants to know; `-umaListProps` has an optional value,
   which is why the wrapper has a heuristic for telling a filter from the next switch; `-umaTimeout`
   exists editor-side *and* as a wrapper wait; there is no way to ask for a checksum, no progress output
   during a multi-minute run, and the naming mode is a bare integer (`0..3`) that maps to a
   `Config.json` enum.

The pieces that are already right and worth keeping: the CLI reuses the UI's own code paths rather than
reimplementing them, it records reproducible motions (deterministic sampling, a rewound clip, one pass
per recording), the two Blender-free checkers verify the output without Unity or Blender, and every mode
emits a single unambiguous `OK` / `FAILED` marker plus a specific reason.
