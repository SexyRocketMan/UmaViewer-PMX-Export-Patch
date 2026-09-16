# Uma Viewer — Ultimate Agemasen Edition

A fork of [katboi01/UmaViewer](https://github.com/katboi01/UmaViewer) that focuses on **PMX model and VMD
motion export that you can actually open in Blender**. Upstream can export models and animations, but the
results regularly need surgery: corrupted geometry, missing materials, motions whose morph/expression
tracks land on nothing. This fork ships those fixes and leaves the viewer itself alone.

Maintained on a best-effort basis.

Tutorial video: https://www.youtube.com/watch?v=zbzfF3pubjQ

Jump to: [What each release fixed](#what-each-release-fixed) - [Fork vs og UmaViewer](#fork-vs-og-umaviewer) -
[Installation](#requirementsinstallation) - [Build it yourself](#for-developerscontributors)

## What each release fixed

### Agemasen 2 — *Ultimate Agemasen Edition (global db key updated)*
[release](https://github.com/SexyRocketMan/UmaViewer-PMX-Export-Patch/releases/tag/Agemasen2) · 2026-07-26

- **`Database not found` / viewer won't start**: the database key was refreshed, so this build works like the
  current og UmaViewer again instead of failing on a new game version.

### Agemasen 1 — *The Ultimate Agemasen Edition*
[release](https://github.com/SexyRocketMan/UmaViewer-PMX-Export-Patch/releases/tag/Agemasen1) · 2026-06-29

- **Short english names for exported bones and morphs**, selectable in the settings tab. The og naming is
  Japanese/verbose, which no longer fits the VMD format's 15-byte name field - so morph (expression) tracks
  were dropped or landed on nothing when you imported the motion next to a model. With short names an
  exported motion maps onto an exported model in Blender without any custom translation dictionary.
- **Mini-uma (chibi) motion export fixed properly** - the neck and shoulder bones are no longer broken in the
  exported VMD. A couple of finger mappings are still off.
- New app icon.
- Note: models exported with an older version don't carry the new names - **re-export your models with this
  version** or the morphs of a short-named motion won't match them.

### patch_3
tag `patch_3` · 2026-05-17

- **Mini-uma motion export** got a first, hacky bypass so a recorded chibi motion could be exported at all.
- **Settings could not be changed after quitting on mobile** - fixed (desktop was unaffected).

### patch_2
tag `patch_2` · 2026-03-31

- **Some scenes could not be exported at all**: a failed texture-list name lookup aborted the export. There is
  now a fallback texture assignment, so those scenes export.

### patch — *PMX Export Patch*
tag `patch` · 2026-03-30 — the first release of this fork

- **Broken geometry on models/scenes with more than 65535 vertices**: the exporter wrote 2-byte vertex indices,
  so everything past the limit turned into a mess of faces that cannot be repaired after the fact. Now 4-byte
  indices are used when needed. No character or prop hits the limit, but several scenes do.
- **Missing materials in exported models** - fixed.
- New app icon and build settings.

### Unreleased — branch `fix/eye-bone-export`

- **Eye bones keep working after Blender's `Refine Structure`**: the name shortener used to strip the
  `(Tag)[Mesh]` suffix that Blender's `uma_addon` matches on, so the addon deleted the `Eye_L`/`Eye_R` vertex
  groups and never built the replacement eye controls - the eye bones went dead while a plain import still
  looked fine. Morph naming is now a single shared implementation with a selectable scheme (see
  [Tools/README.md](Tools/README.md)).
- **One naming scheme for models and motions**: descriptive, romaji tags + english groups, always within the
  VMD 15-byte limit, and identical for the exported model and the exported motion - so morph mapping just
  works. The old short-english and Blender-compatible spellings are still selectable in `Config.json`
  (`PmxMorphNameMode`).
- **Props and scenes stop rendering white**: environment texture sets are resolved from the asset database and
  can be switched from a row in the materials panel, so a home/live/race scene shows its real textures instead
  of flat white. Exports now also warn about every material slot they could not resolve.
- **One clean loop, recorded by a button**: `Record VMD` records exactly one pass over the playing
  animation, rewinding to its first frame first (frame count = length × fps + 1). A looping clip closes on
  itself, so the motion loops without a jump; a one shot keeps its own ending. No trimming or retiming in
  Blender afterwards, and no T-pose frame sneaking in at the start.
- **One shot animations record properly**: a clip that has already finished is parked on its last frame and
  Unity never advances a finished state, so recording after the animation ended used to write that single
  pose for every frame - a motion that does not move at all. That is fixed by the rewind, which keeps the
  pose the viewer is showing for every bone the clip does not animate.
- **The camera motion lands next to the motion**: one save dialog, `<name>.vmd` plus `<name>_cam.vmd`.
- **The file dialogs remember where you last saved** - models and motions separately, and across restarts
  (`Config.json` → `LastModelFolder` / `LastMotionFolder`).
- **Sensible default file names**: the dialogs suggest the uma's own name instead of the container id, plus
  the costume for models and the tail of the animation for motions - `special_week_stride.vmd` for Special
  Week running `anm_rac_type01_run02_stride`, `special_week_res_001.vmd` for a race result animation,
  `special_week.pmx` for the model and `special_week_<costume>.pmx` when the costume has a name (the game's
  own costume titles, so the wording follows the database language; the upgraded costume, for instance,
  comes out as `special_week_upgraded.pmx`).
- **Optional A-pose rest pose**: an exported model can be written with both upper arms rotated into the
  38.5° A-pose that recorded motions are relative to, so model and motion line up in Blender without posing
  anything by hand and without importing the motion with *Use current pose as rest pose*. It is **off by
  default** - a T-pose rest is what rigging and retargeting tools expect - and can be turned on with
  `"PmxAPoseRestPose": true` in `Config.json`, or `-APose` on the command line tools. The result was checked
  against the hand recipe down to 6e-4 blender units, see [Tools/README.md](Tools/README.md).
- **VMD key reduction is applied** (it was silently ignored because the save method shadowed the setting).
- **Command line export**: models, motions, props and scene material tables can be exported and verified
  without clicking, and the whole chain (export → record → check → render → encode) has a one-command
  workflow. See [Tools/README.md](Tools/README.md).

## Fork vs og UmaViewer

| Problem in og UmaViewer | In this fork |
| --- | --- |
| Geometry corrupt on models/scenes over 65535 vertices (2-byte indices) | 4-byte indices where needed *(patch)* |
| Exported models missing materials | Fixed *(patch)* |
| Some scenes abort export on a texture-list lookup failure | Fallback texture assignment *(patch_2)* |
| Mini-uma motions export with broken neck/shoulder bones | Bypass *(patch_3)*, then properly fixed *(Agemasen 1)* |
| Japanese/verbose bone and morph names - morph tracks don't fit the VMD 15-byte name field and need a translation dictionary | Short english names *(Agemasen 1)*, now one descriptive unified scheme used by both models and motions *(unreleased)* |
| `Database not found` on a newer game version | Refreshed database key *(Agemasen 2)* |
| Blender `Refine Structure` kills the eye bones | Fixed - eye controls are built *(unreleased)* |
| Scenes/props render and export with untextured (white) materials | Environment texture sets resolved + switchable *(unreleased)* |
| Recorded motions need trimming, retiming, or a manual T→A rest pose | Button records one clean loop, one shots included; optional A-pose rest pose *(unreleased)* |
| Exporting means clicking through the UI | Headless CLI + end-to-end workflow script *(unreleased)* |

Known upstream behaviour that is **not** a bug: after importing a motion you may see
`not found bone Ankle_L_IK` - exported PMX models have no IK-constrained bones, so the VMD's IK track is
inert and can be ignored.

# Original readme follows:
Unity application that makes it easy to view assets from Uma Musume: Pretty Derby.

| Version   | Supported |
|-----------|-----------|
| JP (DMM)  | ✅        |
| JP (Steam)| ✅        |
| KR        | ✅        |
| Global    | ✅        |

------------

## ⚠️ 🌍 EN/Global users ⚠️

In UmaViewer, set **WorkMode** to **Default** and **Region** to **Global** in the **'Other'** Settings tab.

Currently only the default work mode is supported - you need to download assets using the Download All button in the game's settings for the viewer to work.

------------

### Requirements/Installation
1. [Uma Musume: Pretty Derby](https://dmg.umamusume.jp/) with full data download is required to run the viewer.
2. Depending on your version and update status, the game stores its data in **different locations**
 - **DMM/Steam Older installations :** C:\Users\*your_username*\AppData\LocalLow\Cygames\umamusume(?)\
 - **DMM/Steam Fresh installations :** ...\*Umamusume installation directory*\Umamusume_Data\Persistent(?)\
 - In any case，confirm your file listing in target folder looks like this
   * Target Folder\
     * **meta**
     * master\
       * **master.mdb**
     * dat\
       - 2A\...
       - 2B\...
       - ...\...
3. Download the most recent UmaViewer.zip file from [Releases](https://github.com/katboi01/UmaViewer/releases/) tab.
4. Extract the archive anywhere, can be extracted over previous version.
5. Run the UmaViewer.exe. 
6. UmaViewer will try to automatically detect the game data folder.  
   - If it fails or shows an error, go to **Settings → Other → Change DataPath** and manually select target folder.

------------

- For Developers/Contributors
1. [Unity Hub](https://unity3d.com/get-unity/download) with [Unity Engine Version 2022.3.62f1](https://unity.com/releases/editor/archive) is recommended. It should be possible to run it on newer 2022.3.X versions.
1. Clone or download and extract this repository.
1. Import and Open the project in Unity Hub, missing files should be automatically repaired.
1. Open the Assets/Scenes/Version2 scene.
   - note: If there are errors in the console, [JSON .NET For Unity](https://assetstore.unity.com/packages/tools/input-management/json-net-for-unity-11347) may be required

### Features

||||
| ------------ | ------------ | ------------ |
| ✓ - Working | / - Incomplete  | x - Unsupported  |

|||
| ------------ | ------------ |
| Viewing main character models/animations | ✓  |
| Swapping costumes/animations between characters | ✓  |
| Viewing chibi models/animations | ✓  |
| Viewing mob (NPC) models | ✓ |
| Playing facial animations, custom sliders | ✓  |
| Cloth/Hair physics | ✓  |
| Playing Live Audio with Lyrics | ✓  |
| Exporting animations to MMD | ✓  |
| Recording animations (.gif), screenshots | ✓  |
| Viewing Props, Scenery, Live scenes | /  |
| Exporting models | /  |


All characters and animations are supported

<img src="https://user-images.githubusercontent.com/59540382/222418271-a6e4ce82-b3a5-47ba-9fc9-4d85120218ec.png" height="350" />

Mobs / background characters as well

<img src="https://user-images.githubusercontent.com/32562737/219174232-7d0a0eec-8b1c-4571-9c08-8474e06dd3a8.png" height="350" />

Mixing outfits and animations

<img src="https://user-images.githubusercontent.com/59540382/222420757-609e1f77-d762-4b39-a7d0-d1fb2d3b79a3.png" height="350" />

Screenshot and .gif recording

<img src="https://user-images.githubusercontent.com/59540382/222421579-582be5db-5839-4f7c-bf1b-80efc812c4e0.gif" height="350" />

and more

<img src="https://user-images.githubusercontent.com/59540382/222422871-12e80e0b-778b-4f42-b581-5e4af5cd6df9.png" height="350" />

### Also check out:
[UmaChat by kagari](https://github.com/kagari-bi/UmaChat) - model viewer fork that lets you chat with Umas using AI + TTS

### Special Thank to:
MarshmallowAndroid: [UmaMusumeExplorer](https://github.com/MarshmallowAndroid/UmaMusumeExplorer) for acb/awb decoder.
