# UmaViewer 3.5 + UMA Addon 2.35.18

Two downloads, nothing else needed:

| file | what it is | size |
|---|---|---|
| `AgemaViewer-3.5-win64.zip` | the viewer, standalone Windows build | 47.2 MB |
| `uma_addon-2.35.18.zip` | the Blender add-on, with both shaders | 6.7 MB |

SHA256: `4ADC62393B3A0ED85A62A93CE62C37C6D1A71B2B84DC33AC519A7164C9339CF1` (viewer),
`7D6DBE8B410AE03719C2679A3EDB9F7340D97055F7CADD4F0B33074026C871F7` (add-on).
Both were built from their repositories' `HEAD` and install-tested; the hashes are of the files as shipped, so a
re-download can be checked against them.

## The viewer

Unzip anywhere and run `UmaViewer.exe`. It is a plain standalone build - no Unity install, no Godot, no config wizard.
`Config.json` next to the exe is the settings file and it ships already set for the game's own data:

* `PmxAPoseRestPose` on - exported models rest in A-pose, which is what the viewer wants;
* `PmxExtraMorphs` on - the exporter's extra morphs (decals, tear scroll, @_@ spin) ride along;
* `PmxMorphNameMode: Unified` - morph names use the unified spelling, so both the viewer and Blender agree;
* `PmxTextureVariants` off - the rain/dirt map variants are not written unless you ask for them.

Keybinds are in the same file under `ViewerShortcuts` (each entry is a name and a key): `PanelPrev`/`PanelNext`
(Left/Right), `ListUp`/`ListDown` (Up/W and Down/S), `ExportModel` (E), `AutoRecord` (A), `RainLook` (R). Nothing is
hard-coded, so a different keyboard layout or a different game pad does not need a rebuild.

## The add-on

Install it the way Blender does it:

* **Blender 5.x**: *Preferences → Get Extensions → Install from Disk…* and pick the zip.
* **Blender 4.2 - 4.4**: *Preferences → Add-ons → Install…* and pick the zip (it still carries `bl_info`, so the legacy
  installer accepts it).

Then it is under the **UMA** tab. The pieces that come from this fork:

* **one place to build the rig** - *Pumpkin Rig Setup* runs the MikuMikuRig pipeline and adds rigify chains for hair,
  ears, skirt, chest, tail and ribbons;
* **one place to set the look** - *Shading* wraps every part in one of the two shipped shaders, and the
  **Shading Control** panel drives every copy of every uma from one place, with per-character overrides;
* **face normals that survive morphs**, and an optional face donor.

The shipped shaders, and what each bag of settings does:

| | Looper (`umashader-0.2.6`, the default) | Spork (`spork-1.0`) |
|---|---|---|
| graph | upstream's, plus our wet/dirt work | his current `Simple Diff + Shaded` (51 nodes) and its `FACE` variant, ported |
| rows | 30 | 15 |
| sections | look, eyes, decals, wet & dirt | shadow softness, emission, light, eye highlights, wet & dirt, decals |

Both shaders get, from the exporter's metadata: unified morph names, the eye highlights with their own knobs, the
hand-drawn blush shipped beside the add-on, the tear scroll and the @_@ spin, the outline shell, rain maps and dirt
masks (dial the **Rain** and **Dirt** rows; **Dirt colour** is the tint), and an unlit decal material so a blush or a
tear sheet stops being shaded like skin.

## What this pair adds

The exporter writes a `metadata format: 2` block into the pmx comment, and the add-on reads it. That is the whole
reason the two are released together:

* **morph names** unified to one spelling (`EyeBrow_1_R(WaraiA)[M_Face]`, `EyeBrow_1_R` and `Brow_WaraiA_R` all
  resolve), with the original names still in the file;
* **the extra morphs** - decals, tear scroll, spin, cheek blush - no longer depend on guessing a name;
* **weather**: with texture variants on, the game's rain and dirt sets are written beside the dry maps and bound by
  Blender's Shading run, driven by rows rather than baked in;
* **A-pose rest**, which is what the viewer and the shaders' own orientation expect.

## What to expect, and what not to

* **Models exported by the old viewer** still load and shade, but they carry no metadata: no decals, no spin, no tear
  scroll, no rain or dirt, and they rest in T-pose. Body, eyes and outline work. Re-export from this viewer with
  `PmxExtraMorphs` on to get the rest.
* **Spork's shader is his look, not the viewer's.** The viewer draws its own Unity shaders; a Spork-shaded uma in
  Blender is not pixel-comparable with the same uma in the viewer. The two are independent choices.
* **Dirt is our maths, not the game's.** The game's shader would use `_GlobalDirtColor` and a per-material rate; the
  mask is the game's, the multiplication is ours (colour × tint by the map's max channel). The face's mask is nearly
  empty in the game data, so faces barely show any - that is the data, not the wiring.
* **Weather needs texture variants.** `PmxTextureVariants` is off in the shipped `Config.json` because it roughly
  doubles the texture count. Turn it on in the exporter (or set it in the viewer's scene) to get the rain and dirt
  sets, then shade in Blender.

## Where the details are

Everything above is measured and written down in the exporter repository's `docs/`:

* `TEST_MATRIX.md` - what each export setting produces, old viewer against this one, and what each shading setting
  moves;
* `HANDOFF_SHADER_LIBRARY.md` - the shader libraries, both ports, the weather pass, the decal role, and every fix
  since (including the see-through render settings and the face's driver wiring);
* `PMX_EXTENSIONS.md` - the metadata contract itself;
* `KEYBINDS.md`, `MORPH_NAMES.md`, `OUTLINES.md`, `CLI.md` - the viewer-side documents.

## Hot patch, 5 November 2026

Shipped on top of 3.5 without a version bump, as `UmaViewer-3.5-hotfix-2026-11-05.zip` (48 MB, sha256 starts
`D59DEB86F068E7D9`). The repository has the changelog that went inside it at `CHANGELOG.txt` in the package; what it
fixes, and what a future session needs to know about each:

| fix | commit | why it mattered |
|---|---|---|
| waist stretch on every export | `78878ba` | the rest pose was restored from a world-space table captured before `SetHeight`, so unskinned control bones rose by `P0 x (1 - BodyScale)`. **Every export between 2 and 5 November is wrong and must be re-exported** - the file cannot be repaired |
| bone orientation | `9317c40` | a bone's tail now points at the joint MMD points it at (99.9% of joint selections over 368 exports, from 71.8%); `Tools/check_tails.py` gates it |
| dump cost and survivability | `26bd4a2` | a 600 s deadline for the whole run killed long dumps, the plan was rebuilt per model (~3 s), and the asset cache was never released (6 GB per instance). A dump now runs 1777 models in ~25 minutes across four viewers and stays under 3 GB |
| prop previews | `2fd9e7e` | near-plane, one-sided flats, and empty-frame retries; `check_dump.py` measures the drawn bounding box rather than pixel coverage, because a thin prop that fills the frame is a good picture |
| standalone build | `10274b6` | the runner looked for `Assets/Scenes/Version2.unity` on disk, so a built player only worked when started from the project directory |

The build is reproduced with `Unity.exe -batchmode -quit -projectPath . -executeMethod UmaPlayerBuild.BuildWindows`,
which writes `BuildDev/uma-cli/UmaViewer.exe` (ignored by git); packaging is that folder plus `README.txt` and
`CHANGELOG.txt`, minus `Config.json`, which would otherwise overwrite a user's settings.

## Hot patch, 7 November 2026 - keybinds

Shipped on top of 3.5 (and of the 5 November hot patch) as `UmaViewer-3.5-hotfix-2026-11-07.zip` (47.8 MB, sha256
`ACFC56229AFB0F6BF3E28A7B72BE21EEB25D3DC85B81FDB30135467A65536EBA`). A user reported two things about the 3.5 keybind
work, and both were real:

| report | what it actually was |
|---|---|
| "the keybindings in the config file have no effect - E still exports" | exact. `UIShortcuts` was built in `UmaViewerUI.Awake` and read `Config.Instance`; the config is created in `UmaViewerMain.Awake`, and Unity does not order two `Awake`s. The UI's ran first, so the shortcuts bound `Config.DefaultViewerShortcuts()` **silently** - and the startup log printed that default list, which is why nobody noticed: the shipped `Config.json` binds the same keys. `Config.Ensure()` now creates the config on first use, so no order matters, and a missing list is a warning naming the file instead of a silent fallback |
| "pose mode is still affected by the hotkeys" | the pose mask suspended only navigation and recording, on the reasoning that `E` and `R` do not collide with the gizmo. True, and beside the point: a posed user pressing `E` gets an export of the model mid-pose. Pose mode now suspends **every** binding, which costs the pose tool nothing - its own keys are `H`, `Space` and the Alt/Shift/Ctrl combinations `TransformGizmo` and `HandleManager` read |

`Tools/verify_keybinds.ps1` is the gate both defects would have failed. It writes a probe `Config.json` beside
the player (rebound keys, one unknown action, one unknown key), runs it with `UMA_SHORTCUT_PROBE=1`, and asserts
the binding log and the context decisions; `UMA_SHORTCUT_PROBE=1` prints one line per binding per context, so the
suspension rules are checkable without flying the camera or posing a model by hand.

Verified: `dotnet build` 0 errors, and `./Tools/verify_keybinds.ps1` green against the rebuilt player - 10 checks.
The same gate against the 5 November binary fails 9 of them, and fails them for the right reason: it logs
`9 binding(s): ... E -> ExportModel, A -> AutoRecord, R -> RainLook` while the probe file beside it asks for
`Keypad1`/`Keypad2`/`W`/`Keypad3`.

Packaging is the 5 November recipe: `BuildDev/uma-cli` (minus `AgemaViewer_BurstDebugInformation_DoNotShip`) plus
`README.txt` and `CHANGELOG.txt`, with `Config.json` keeping the shipped values but its `LastModelFolder` /
`LastMotionFolder` blanked so one machine's dialog history does not travel. The zip holds a
`UmaViewer-3.5-win64/` folder, as before.


