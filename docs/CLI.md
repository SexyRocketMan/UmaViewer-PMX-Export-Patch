# Command line export

UmaViewer can do its whole export job from a terminal: write a `.pmx` model, record the playing motion
into a `.vmd`, and render the game's own view to a PNG. It drives the same code paths as the buttons in
the UI - *Export Model*, *Record VMD*, the screenshot button - so what comes out is what the buttons
produce, only without the clicking.

Everything goes through one script, `Tools/headless_export.ps1`. It builds a Unity batch-mode command
line, runs it, and prints the interesting lines of the log afterwards.

> ### Status: this runs the Unity Editor, not the released build
> The CLI is an editor entry point - `Assets/Editor/UmaHeadlessExport.cs`, which Unity executes with
> `-batchmode -executeMethod UmaHeadlessExport.Run` - and `Tools/headless_export.ps1` is the wrapper that
> assembles that command line for you. **Using it today needs the project source and the matching Unity
> editor.** The `UmaViewer.exe` from a release has no argument handling at all, so none of this works from
> a release build yet. If the CLI is meant to be a feature of the shipped viewer, read
> [What a shipped CLI would need](#what-a-shipped-cli-would-need) at the end - that section is the
> starting point for reworking this interface.

---

## What you can do

| You want to | Command |
|---|---|
| See which characters and costumes exist | `./Tools/headless_export.ps1 -ListChars` |
| Export a model | `./Tools/headless_export.ps1 -Char 1001 -Costume 00 -Out D:/out/special_week.pmx` |
| Record one loop of a motion | `./Tools/headless_export.ps1 -Char 1001 -Motion anm_rac_type01_run02_stride -RecordVmd D:/out/run.vmd` |
| Do both in one run | `./Tools/headless_export.ps1 -Char 1001 -Costume 00 -Motion <motion> -Out m.pmx -RecordVmd m.vmd` |
| Screenshot the game's own render | `./Tools/headless_export.ps1 -Char 1001 -Costume 00 -ShotView face -Screenshot D:/out/face.png -ShotOnly` |
| Export a prop or a scene | `./Tools/headless_export.ps1 -ListProps home10001` then `-Prop <path> -Out D:/out/home.pmx` |
| Find out why a scene renders white | `./Tools/headless_export.ps1 -ScanProps "3d/env/home" -ScanCount 12` |
| Check what you exported | `uv run Tools/pmx_inspect.py summary <file.pmx>`, `uv run Tools/vmd_inspect.py loop <file.vmd>` |

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
./Tools/headless_export.ps1 -ListProps home10001        # find scene assets (substring filter, optional)
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

### 7. Change what the exported file contains

| Option | Effect |
|---|---|
| `-MorphNameMode 0\|1\|2\|3` | Morph and shape-key spelling: `0` tagged (`Eye_20_R(XRange)[M_Face]`, what the **stock** Blender addon matches), `1` short (`Eye_20_R`), `2` both, `3` unified (`Eye_XRange_R`) - the default, and what the patched addon in this fork resolves. Models and motions must use the same mode or the motion's morph tracks land on nothing. |
| `-APose` | Exports the model with both upper arms rotated 38.5° into the A-pose that recorded motions are relative to, so a model and a motion line up in Blender without posing anything by hand. **Off by default** - rigging and retargeting tools expect a T-pose rest. |
| `-PlainMaterials` | Writes plain MMD materials: no uma shader settings in the material comment, plain diffuse/specular/outline values. Use it for a model meant to be used *without* the uma addon. |
| `-DumpMaterials` | Before exporting, logs what every material resolved to - textureless slots, missing shaders, and every property of every shader the model uses. Use it when a model comes out white or oddly shaded. |

### 8. Check what you got

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
| `-MorphNameMode -1\|0\|1\|2\|3` | -1 (use `Config.json`, i.e. unified) | Morph-name spelling, see [above](#7-change-what-the-exported-file-contains) |
| `-APose` | off | Export the A-pose rest pose |
| `-PlainMaterials` | off | Export plain MMD materials |
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
| `-ScanProps <filter>` | - | Load every matching `3d/env` scene in turn and report its material health |
| `-ScanCount <n>` | 10 | How many scenes `-ScanProps` covers |
| `-ListChars` / `-ListCostumes` / `-ListProps [filter]` | - | Print and exit without loading |

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
| Screenshots | `-Screenshot`, with `_yaw+45` / `_az+60_yaw0` inserted before the extension when there are several angles |
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
| `-RecordFps` / `-RecordReduction` / `-RecordMode` | `-umaRecordFps` / `-umaRecordReduction` / `-umaRecordMode` |
| `-PlaySeconds` / `-ExtraFrames` / `-Timeout` | `-umaPlaySeconds` / `-umaExtraFrames` / `-umaTimeout` |
| `-ListChars` / `-ListCostumes` / `-ListProps [filter]` | `-umaListChars` / `-umaListCostumes` / `-umaListProps [filter]` |
| `-DumpMaterials` / `-DumpFace` / `-ScanProps` / `-ScanCount` | `-umaDumpMaterials` / `-umaDumpFace` / `-umaScanProps` / `-umaScanCount` |
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
