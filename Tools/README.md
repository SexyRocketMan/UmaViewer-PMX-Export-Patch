# Tools

Scripts for exporting and verifying models **without the GUI**, so export bugs can be reproduced and
regression tested from a terminal (and from CI).

| File | What it does |
|---|---|
| `headless_export.ps1` | Runs Unity in batch mode, boots the viewer scene, loads a character/costume and writes a `.pmx` - the same code path the "Export Model" button uses. |
| `verify_export.ps1` | Runs headless Blender + `mmd_tools` + `uma_addon` and checks that the export still imports and rigs correctly. |
| `blender_verify_pmx.py` | The Blender side of `verify_export.ps1`. |
| `pmx_inspect.py` | Blender-free PMX inspector/differ (`summary`, `bones`, `tails`, `weights`, `morphs`, `diff`, `check`). |
| `vmd_inspect.py` | Blender-free VMD inspector and loop validator (`summary`, `loop`, `motion`). |
| `morph_name_table.py` | Prints what every morph is called in each naming mode, checks the rules against four exports (one per mode) and can write [`../docs/MORPH_NAMES.md`](../docs/MORPH_NAMES.md). |
| `blender_verify_vmd.py` | Imports a PMX + VMD in Blender and checks the motion's morph keyframes survive. |
| `blender_render_motion.py` | Imports a PMX (+VMD) and renders the upper body / full body as a coloured PNG sequence. |
| `render_stats.py` | Checks rendered frames numerically: not black, not empty, not flat white, motion plays, loop closes. |
| `run_workflow.ps1` | The whole chain in one command: export -> record -> check -> render -> encode -> check. |

## The whole chain in one command

```powershell
# model + one loop of a running motion + a coloured mp4, every step verified
./Tools/run_workflow.ps1 -Char 1001 -Costume 00 -Motion anm_rac_type01_run02_stride -View full

# just a model and a motion, no render
./Tools/run_workflow.ps1 -Char 1002 -Costume 00 -SkipRender
```

It stops on the first failing step with a non zero exit code, so it doubles as a regression gate for
the export path. Outputs land in `HeadlessExports/` (`<name>.pmx`, `<name>.vmd`, `<name>.mp4`,
`<name>_frames/`, `<name>_still.png`, plus the `Texture2D/` folder the pmx refers to).

**Pick a moving motion.** The clip the viewer loads by default is an idle whose bone rotations are
essentially zero - `anm_eve_chr1001_00_idle01_loop` measured 0 of 52 bones moving, so it makes a
useless test and a dull video. `-Motion anm_rac_type01_run02_stride` (or any other substring of a
motion asset path) is a much better baseline; `vmd_inspect.py motion` fails loudly on a static clip.

`render_stats.py` is what replaces "just look at it", since a headless agent cannot see the frames:
it reports subject coverage, distinct colours, saturation and background, and compares first/middle/
last frames for motion and loop closure. Measured on the stride render: 12.7k distinct colours,
first vs middle 0.038 mean (motion plays), first vs last **0.0000** (loops exactly).

## Quick start

```powershell
# list characters and their costume ids
./Tools/headless_export.ps1 -ListChars

# export Special Week, costume "00"
./Tools/headless_export.ps1 -Char 1001 -Costume 00 -Out D:/out/1001_00.pmx

# verify the export the way Blender sees it
./Tools/verify_export.ps1 -Pmx D:/out/1001_00.pmx
```

`verify_export.ps1` exits non-zero on failure and prints `PASS` / `FAIL` per file, so it can be used
as a regression gate. `pmx_inspect.py` needs no Blender or Unity:

```powershell
uv run Tools/pmx_inspect.py check D:/out/1001_00.pmx
uv run Tools/pmx_inspect.py diff a.pmx b.pmx
```

## Props and scenes

Props/scenes export through the same CLI (`-umaProp` takes an asset path or a unique substring), and
`-umaDumpMaterials` reports what their materials resolved to:

```powershell
./Tools/headless_export.ps1 -ListProps home10001        # find scene assets
./Tools/headless_export.ps1 -Prop "3d/env/home/home10001/main/pfb_env_home10001_main000_000" `
    -DumpMaterials -Variant 214 -Out D:/out/home.pmx
```

### Which scenes are actually affected

`UmaContainerProp` applies no material fixes at all, so a scene renders with whatever its bundle
ships. `-umaScanProps` loads a set of scenes in turn and prints one line of material health each, so
the broken ones can be found instead of guessed:

```powershell
./Tools/headless_export.ps1 -ScanProps "3d/env/home" -ScanCount 12
```

Each line has `renderers`, material `slots`, how many are `textureless` (no `_MainTex`), how many
materials have no `_MainTex` property at all, how many have a missing shader, and what the texture set
resolver did (`textureSet=<variant> of [...] fixed=<slots>`). Measured so far:

| scene | slots | textureless before | after the resolver |
|---|---|---|---|
| `home10001/main/pfb_env_home10001_main000_000` | 20 | **12** (every `base01` surface) | 1 (a runtime mirror) |
| `home10001/main/pfb_env_home10001_main002_000` | 23 | **12** | 1 |
| `home10001/main/pfb_env_home10001_main003_000` | 31 | **12** | 1 |
| `home10001/main/pfb_env_home10001_main004_000` | 23 | **12** | 1 |
| `home10001/main/pfb_env_home10001_main005_000` | 57 | **12** | 1 |
| `home10001/main/pfb_env_home10001_main006_000` | 30 | **12** | 1 |
| `cutin1049_00/pfb_env_cutin1049_00_00_room00` | 20 | 0 | nothing to do |
| `race00000/race00000_8000/pfb_env_race00000_8000_000` | 1 | 0 | nothing to do |

So this is specific to scenes where the game assigns a texture set at runtime, not universal, and the
resolver correctly does nothing elsewhere. Note that all the home scenes **share** the same material
(`mtl_env_home10001_main000_000_base01`), so the resolver keys textures off the scene named *in the
material*, not the prefab being loaded - keying it off the prefab left main002 upwards white.

A sweep over every environment family (8 props each, `-ScanProps`) puts the scope of the problem:

| family | slots sampled | textureless | props affected |
|---|---|---|---|
| `3d/env/home` | 20-57 per scene | 12 each | all, fixed by the resolver (12 -> 1) |
| `3d/env/live` (common: confetti, cyalume, billboards) | 42 | **27** | 8/8 - runtime driven, see below |
| `3d/env/race` (common props) | 8 | 1 | 1 |
| `3d/env/set` | 282 | 0 | 0 |
| `3d/env/gacha` | 52 | 0 | 0 |
| `3d/env/mini` | 46 | 0 | 0 |
| `3d/env/cutin` | 40 | 0 | 0 |

The `live/common` and `race/common` cases are **not fixable by a static resolver**: no texture assets
exist for them at all (`3d/env/race/common` has zero `tex_` entries, and nothing matches
`tex_env_live_cmn_*` for confetti or the cyalume controllers). Their appearance is set at runtime - the
cyalume controllers get their penlight colours per song, confetti and billboards come from live/effect
data - so loading one as a standalone prop showing untextured is expected. The resolver correctly
leaves them alone rather than guessing a texture.


### T-pose frame and rest pose

Two things to know when comparing a render against what you see in Blender by hand:

* The recorder used to emit one frame at the start that was a rest pose (a T-pose with the arm offset
  stacked on it). `RecordClipLoop` now rewinds the clip to its first frame and forces an animator
  evaluation before the first sample, so frame 0 is the clip's own first pose. `vmd_inspect.py motion`
  fails on that artefact ("the first frame is Nx the typical step"), and `render_stats.py frames` does the
  same at the pixel level: it compares the step from the first to the second frame against the median step
  of the same sequence (2.5x by default) rather than against a fixed number, because a fast animation
  legitimately steps a long way between its first and second frame (measured: 0.0208 against a 0.0233
  median step). `--max-first-frame-jump 0.02` restores the old absolute check for cases where that is what
  you want.
* The recorder captures its reference pose with the arms already rotated into an **A-pose** (38.5
  degrees, `UmaAPose.Degrees`). A motion therefore only lands correctly if that is the rest pose:
  `blender_render_motion.py` poses the arms into an A-pose and imports the vmd with `use_pose_mode`
  ("Treat Current Pose as Rest Pose"), which is the same recipe as doing it by hand in Blender. Use
  `--apose <degrees>` if you use a different angle.

### Exporting the A-pose rest pose instead (option)

`Config.PmxAPoseRestPose = true` (or `headless_export.ps1 -APose`, `run_workflow.ps1 -APose`) makes the
exporter hand the user the model that recipe produces: `UmaAPose.Enter(container)` freezes the animator,
restores the rig's own rest pose, rotates both upper arms 38.5 degrees with the same helper the recorder
uses, and puts everything back on dispose. Because `ReadVerticesAndTriangles` reads `SkinnedMeshRenderer.
BakeMesh`, the vertices follow the arms, so both the mesh and the bone rest pose come out A-posed -
exactly what `use_pose_mode` would have done in Blender.

Imported that way the motion needs no posing and no `use_pose_mode`:

```powershell
./Tools/headless_export.ps1 -Char 1001 -Costume 00 -Out D:/out/a.pmx -APose
./Tools/headless_export.ps1 -Char 1001 -Costume 00 -RecordVmd D:/out/a.vmd -Motion <motion>
uv run Tools/blender_render_motion.py --pmx D:/out/a.pmx --vmd D:/out/a.vmd `
    --apose 0 --use-pose-mode 0 --frames-dir D:/out/frames --view full
```

Both paths are checked against each other numerically: `blender_render_motion.py` writes a `pose`
fingerprint (world space elbow/wrist/ankle/head bone heads relative to the hip, plus the evaluated mesh
bounding box at the first frame) into its `--json`, and the two imports have to agree on it. Measured on
the stride motion of character 1001:

| bone head | T-pose model + hand posed + `use_pose_mode` | A-pose export, nothing posed | T-pose model, nothing posed (control) |
|---|---|---|---|
| `Wrist_L` | `[0.1224, -0.32156, 0.13291]` | `[0.12219, -0.32184, 0.13312]` | `[0.32835, -0.39139, 0.25597]` |
| `Wrist_R` | `[-0.23653, 0.07898, 0.10227]` | `[-0.23674, 0.07848, 0.102]` | `[-0.40393, 0.04334, 0.2928]` |
| bbox centre | `[-0.05309, 0.0964, 0.74273]` | `[-0.05323, 0.09633, 0.74273]` | `[-0.08909, 0.06333, 0.74273]` |

The export path agrees with the hand recipe to 6e-4 blender units and the rendered frames differ by
0.32/255 on average, while skipping the pose (the control) is off by 0.2 units and 6.1/255 - so the check
bites. Over the pmx files themselves the difference is confined to the arms: `Ankle_L`, `Hip`, `Head`,
`Spine` and `Shoulder_L` are identical to 0.00000, 11762 of 13968 vertices are unchanged, and the 2206
that move are the arms (the wrists drop 3.24 units in y, which is the A-pose direction).

Default is still `false`, since a T-pose rest is what rigging and retargeting tools expect.

### Exporting plain MMD materials instead (option)

`Config.PmxUmaMaterialFields = false` (or `headless_export.ps1 -PlainMaterials`, `run_workflow.ps1
-PlainMaterials`) writes the pre-2.6 materials: an empty material comment and no uma-derived specular or
outline values, which is what an export from before that feature existed looks like. Useful for a model that
is meant to be used without the uma addon. The difference is small either way - the same model rendered with
and without the extras differs by a mean of 0.0002 per channel, with 0.2% of pixels changed at all - and
nothing else about the export changes: same vertices, bones, morphs and 22 textures. See
[docs/UMA_SHADER.md](../docs/UMA_SHADER.md).

### A baseline from the game itself

Shading work needs something to compare against, and memory is a poor reference: `-Screenshot` writes what the
viewer draws, with the game's own materials, lighting and post processing, from a camera the tool places
itself.

```powershell
# the game's own render of the face, no model exported
./Tools/headless_export.ps1 -Char 1001 -Costume 00 -ShotView face -Screenshot D:/out/game_face.png -ShotOnly

# same run as an export, for a model and a picture of it side by side
./Tools/headless_export.ps1 -Char 1001 -Costume 00 -Out D:/out/1001_00.pmx -ShotView upper `
    -Screenshot D:/out/game_upper.png
```

`-ShotView` is `face`, `head`, `upper` or `full` (distance as a fraction of the model's height), `-ShotWidth`
/ `-ShotHeight` size the image (1280x720 by default), `-ShotYaw` orbits the camera, `-ShotTransparent` drops
the background and `-ShotOnly` skips the export. The camera is put back afterwards, so the export that
follows is unaffected, and the UI layer is excluded the same way the in-app screenshot button does it.

### Checking where the bones point

A PMX bone's tail is either a child index or an offset, and head -> tail is what Blender draws. The uma rig
has no tails, so the exporter derives them: a bone points at the child that continues its chain, and a bone
without one - finger tips, hair ends, and the roll helpers, whose only child sits on top of themselves -
continues the segment leading into it. That is why the head bone points up out of the neck and both roll
helpers mirror each other, and `pmx_inspect.py tails` checks it without Blender:

```powershell
uv run Tools/pmx_inspect.py tails D:/out/1001_00.pmx
```

It prints the direction of the bones a user notices (`--named`), each left/right pair's mirroring
(`--pairs`, tolerated to `--mirror-tolerance` degrees, 5 by default) and exits non-zero when a pair is not
mirrored, plus how many bones turn away from the segment leading into them - strands of hair and costume
ribbons are expected there, a body chain bone is not. Measured on character 1001: 79 of 253 bones turn more
than 5 degrees, and 73 of those are hair, mantle, ribbon, skirt and tail strands - the other six are the eye
and eye-locator bones, which deliberately point out of the face instead of along the chain that arrives at
them (see below).

The eye bones are the one place where the chain is the wrong answer. `Eye_L`/`Eye_R` and the locators that
drive them hang off the head in the middle of the skull, so the segment leading into them runs up and out
towards the temple: a tail along it makes a gaze rotation read as a roll, and the eyes look inward. MMD points
an eye bone at the viewer, so those six bones take the head's own forward axis instead - measured on character
1001 they come out as `(0.00, 0.00, -0.73)`, straight along the direction the model faces.

### Known harmless warning

Importing a recorded motion prints `WARNING: not found bone Ankle_L_IK` (and `Ankle_R_IK`). The
recorder calls the foot IK bones `Ankle_L_IK`/`Ankle_R_IK`, while the model's own bones are
`Ankle_L_IK_Handle` (17 bytes, which does not fit a vmd's 15 byte name field). The exported PMX
contains **no IK constrained bones at all** (`pmx_inspect.py bones` prints no `IK->`), so that track
has nothing to drive and dropping it changes nothing.

## Morph naming (one name for the model and the motion)

`MorphNaming` is the single source of truth for morph names, used by both `ModelExporter` (the .pmx)
and `UnityHumanoidVMDRecorder` (the .vmd). Blender's mmd_tools matches vmd morph keyframes to the
model's shape keys **by name**, so a name that exists in only one of the two files is dropped on
import - which is why exported motions used to import as bone keyframes only.

A vmd morph name field is 15 bytes of shift-jis. The stock spelling
(`Eye_2_L(CloseA)[M_Face]`, 27-30 bytes) can never be stored in one, so `PmxMorphNameMode` chooses:

| mode | example | fits a vmd | notes |
|---|---|---|---|
| `0` BlenderCompatible | `Eye_2_L(CloseA)[M_Face]` | no | stock `uma_addon` compatibility only |
| `1` ShortEnglish | `Eye_2_L` | yes | the fork's old behaviour |
| `2` Both | both of the above | yes | bridge: addon + motion both work |
| `3` Unified *(default)* | `Brow_WaraiA_R`, `Eye_XRange_L` | yes | english group + romaji tag + side |

The unified spelling drops the numeric id and the `[M_Face]` mesh suffix, and abbreviates the two
tags that would not fit (`EyelidHideA/B` -> `LidHideA/B`). Over the full 240 morph set of a character
it produces 192 unique names with a worst case of exactly 15 bytes, verified with:

```powershell
uv run Tools/pmx_inspect.py names D:/out/1001_00.pmx --require Eye_XRange_L,Eye_XRange_R,Eye_YRange_L,Eye_YRange_R
```

`pmx_inspect.py names` fails on names longer than 15 bytes or on duplicates (Blender renames
duplicates to `.001`, which breaks vmd import again).

### Round-trip check: does the motion drive the model?

```powershell
blender --background --factory-startup --python Tools/blender_verify_vmd.py -- \
    --pmx D:/out/1001_00.pmx --vmd D:/out/1001_00.vmd
```

It imports both, then asserts every morph name in the vmd resolved to a shape key *and* got an
animation curve. Measured results:

| model | motion | morph names resolved |
|---|---|---|
| unified (mode 3) | unified | **160 / 160** ✅ |
| og tagged (stock 2.1.8) | unified | **0 / 160** ❌ (the mismatch this test exists to catch) |
| short (mode 1) | short | **160 / 160** ✅ |

### The addon side

`blender_verify_pmx.py` also runs the addon's **Refine Structure**, which is the operator that used to
kill the eye bones, so it covers both halves of the contract:

```powershell
blender --background --factory-startup --python Tools/blender_verify_pmx.py -- --mode unified D:/out/1001_00.pmx
```

`--mode` is `unified` / `tagged` (`blender`) / `short` / `both`, and selects the eye range morph
spelling the model is expected to expose. The stock addon only resolves the tagged spelling, so the
fork of it (see below) is what makes `unified` and `short` pass:

| addon | model spelling | eye controls built | rotating `Eye_L` |
|---|---|---|---|
| stock | unified | **0 / 8** | **0 verts** |
| patched | unified | 8 / 8 | 23 verts |
| patched | tagged | 8 / 8 | 23 verts |
| patched | short | 8 / 8 | 23 verts |

The patched addon lives in its own repository (`Blender-Uma-Addon`, branch `unified-morph-naming`):
one new `utils/naming.py` resolves a shape key by `(group, tag, side)` in whichever spelling the model
uses, plus optional `naming_overrides.json`, and the three operators that matched morph names by
literal string now go through it. Its `NAMING.md` has the details.

"Refine Structure" builds the eye controls from the four eye range morphs and then merges the
`Eye_L`/`Eye_R` weights into `Head`, which is why the eyes are driven by the control shape keys and their
driver bones afterwards rather than by the eye bones alone. The fork resolves those morphs before it
touches anything and **refuses the whole eye fix if any of the four is missing**, leaving the vertex
groups alone: the stock operator deleted them either way, so a model whose morphs could not be found (an
unknown naming spelling, a model imported without morphs, or a stale addon) ended up with eye bones that
rotate and move nothing. Verified both ways on a real export - with the morphs renamed away, the eyes keep
working (`Eye_L` still moves 22 vertices) and the operator reports which names it was looking for, while a
normal model still gets all 8 control keys.



`UnityHumanoidVMDRecorder.RecordClipLoop` / `RecordCurrentLoop` record exactly one pass over a clip. The
clip is rewound to its start first, then left playing at normal speed, `Time.captureDeltaTime` is pinned
to the frame length so the animation - and cloth/hair - advance by exactly one frame per sample, and one
vmd frame is sampled per `WaitForFixedUpdate` through `SampleFrame()`. That gives:

* a frame count of exactly `clip.length * fps + 1`,
* for a looping clip, a last frame that repeats the first pose, so the motion loops without a jump,
* for a one shot clip, a last frame that is the clip's end pose instead.

Rewinding is what makes a one shot animation recordable: a clip that has already finished is parked on its
last frame and Unity does not advance a finished state, so a recording started there (the state the viewer
is in when a user hits record after the animation ended) repeated one pose for every frame. Seeking with
`Animator.Play(hash, layer, 0f)` enters the state again, and a state with "write default values" resets
every bone its clip does not animate - which is what wrecked an earlier seek attempt (measured: 21 of 52
bones rotating instead of 49). The pose is therefore snapshotted before the seek and put back right after
it, and one animator evaluation lays the clip's own curves on top: the clip decides the bones it animates,
everything else keeps the pose the viewer was showing.

Whether a recording closes on itself is decided by the clip (`AnimationClip.isLooping`, or a `_loop` name
the viewer itself goes by) and confirmed against the data: the last frame is forced onto the first only if
the clip declares a loop or the sampled motion ends where it started (within 0.05 quaternion / 0.5 units).
Measured on character 1001: the race result one shot ends 0.82 from its start so it keeps its own ending,
the running cycle sits at 0.23 but is declared a loop, the idles at 0.10. `AnimatorStateInfo.loop` cannot
be used for this - it reports true for every state in this rig.

Because of the rewind a recording is reproducible: two runs of the stride cycle produced identical bone
tracks (every shared key compared to 0.000000), and two runs of a one shot identical except the very last
frame, which sits on the clip's end boundary. `-umaPlaySeconds` therefore no longer changes what gets
recorded, it only changes when the recording starts.

`-umaPlaySeconds <s>` lets the loaded motion play for that much wall clock time before the export or
recording starts. Batch mode burns through editor frames in milliseconds, so without it a one shot
animation never reaches its end and the parked-state bug cannot be reproduced - with it, the recording
above is the regression test for exactly that, and `vmd_inspect.py motion` fails when a recording only
moves a bone or two (it used to pass as long as *something* twitched).

`-umaRecordMode realtime` keeps the legacy path (one wall-clock `FixedUpdate` per frame, no pinned step);
that one drifts against the animator, which is what made the first and last frames disagree. `SaveVMD`
also used to skip the final frame whenever the key reduction did not divide it, and the level parameter
used to shadow the field, so every save used level 1 - both are fixed, and key reduction is verified by
checking that the reduced file is an identical subset of the full one.

Record and validate headlessly:

```powershell
./Tools/headless_export.ps1 -Char 1001 -Costume 00 -RecordVmd D:/out/loop.vmd -RecordFps 30
uv run Tools/vmd_inspect.py summary D:/out/loop.vmd
uv run Tools/vmd_inspect.py loop    D:/out/loop.vmd     # frame 0 == last frame for every bone

# a one shot: let it finish first, then check that the recording still animates
./Tools/headless_export.ps1 -Char 1001 -Motion anm_res_chr1001_001 -PlaySeconds 8 -RecordVmd D:/out/res.vmd
uv run Tools/vmd_inspect.py motion  D:/out/res.vmd
```

Only physics driven bones (cloth, hair) can still differ slightly between the first and last frame -
`loop` reports the worst deviation so that stays visible (`--rotation-tolerance` defaults to 1e-3).
`vmd_inspect.py loop` is expected to fail on a one shot recording: its last frame is the animation's
ending, not a repeat of the first pose, which is exactly what the check reports.



Environment materials are frequently serialized with **no texture at all**, because the game assigns
one of several texture sets at runtime (time of day, weather, event banner). The home screen is the
clearest example:

```
mtl_env_home10001_main000_000_base01   <- prefab bundle: _MainTex = null (12 of 20 material slots!)
tex_env_home10001_main000_212_base01   <- 2048x2048 DXT1, separate bundle
tex_env_home10001_main000_214_base01   <- 2048x2048 DXT1, separate bundle
```

`UmaContainerProp` only instantiated the prefab, so those materials rendered flat white and exported
with whatever texture happened to be first in the texture list. `UmaEnvTextureSet` now resolves the
sets that exist for every textureless material, assigns one (default: the variant covering the most
materials, lowest code first) and exposes `Variants` / `CurrentVariant` / `SetVariant()` so
`UISettingsModel.LoadTextureSetPanel` can offer the choice in the materials panel.

Verified with `-umaDumpMaterials`: NULL `_MainTex` count drops 12 -> 1 (the remaining one is a
runtime mirror reflection that has no texture set), and the exported PMX switches from
`tex_..._000_base00` to the correct `tex_..._212_base01` for all `base01` materials.

## Why the verification exists: the uma_addon morph-name contract

The Blender `uma_addon`'s **Refine Structure** operator
(`uma_addon/.../operators/AddonOperators.py` -> `RefineBoneStructure.fix_eye_shapekeys`) rebuilds the
eye rig: it separates the eyeball geometry, deletes the `Eye_L`/`Eye_R` vertex groups (folding their
weights into `Head`) and replaces them with shape keys plus drivers,
`Eye_L(L)/Eye_L(R)/Eye_L(U)/Eye_L(D)` and `Eye_R(...)`.

Those replacement shape keys are built from *four morphs it matches by exact name*:

```
Eye_20_R(XRange)[M_Face]   Eye_20_L(XRange)[M_Face]
Eye_21_R(YRange)[M_Face]   Eye_21_L(YRange)[M_Face]
```

`AddonOperators2.py` similarly matches `Eye_{n}_{L|R}({tag})[M_Face]`,
`EyeBrow_{n}_{L|R}({tag})[M_Face]` and `Ear_{n}_{L|R}({tag})[M_Hair]`.

So the `(Tag)[Mesh]` suffix that `ModelExporter.AddBlendShape` appends to every baked morph is **part
of the exported file's contract with Blender**, not cosmetic. If a morph is exported without the
suffix the addon finds nothing, creates no eye controls, and the eye bones silently stop deforming
the mesh - while a raw import still looks fine.

That is why the suffix is still available (`PmxMorphNameMode 0`), but it is no longer the default: the uma
addon fork that ships with this project resolves morph names by meaning, so the descriptive names fit in a vmd
keyframe too and both formats work.

| `PmxMorphNameMode` (Config.json) | Morph names | Blender/uma_addon | vmd morph matching |
|---|---|---|---|
| `3` Unified *(default)* | `Brow_WaraiA_R` | fork build: yes (matched by meaning); stock addon: **eye controls break** | yes |
| `0` BlenderCompatible | `Eye_20_R(XRange)[M_Face]` | yes | no (vmd names are short) |
| `1` ShortEnglish | `Eye_20_R` | fork build: yes; stock addon: **eye controls break** | yes |
| `2` Both | both spellings | yes | yes (larger file) |

`verify_export.ps1 -Mode unified|blender|tagged|short|both` asserts the matching expectations; `unified` is
the default there too, so a checker run on a plain default export no longer reports the tagged spellings as
missing while the addon has in fact built all eight eye controls.

## Notes / gotchas

* **One Unity per project.** Batch mode refuses to start if an editor already has the project open
  ("project already open in another instance") and the failed run can leave a stale
  `Temp/UnityLockfile`. Close the editor first, or point `-ProjectPath` at a copy.
* The first batch run compiles scripts and reimports assets, so it is much slower than later runs.
* `UmaHeadlessExport` enters play mode, which triggers a script domain reload; it keeps its progress
  in `SessionState` and re-arms itself from `[InitializeOnLoadMethod]`. Without that the runner
  silently hangs - which is what `-umaTimeout` guards against.
* Unity's launcher process can exit before the editor child it spawns, so a zero process exit code
  is not proof of success: check for `[UmaHeadlessExport] OK` / `FAILED` in the log, or the output
  file itself.
