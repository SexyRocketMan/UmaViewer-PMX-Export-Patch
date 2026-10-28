# Handoff — options, naming and the release

Written at the end of a long session. `HANDOFF_SHADER_LIBRARY.md` owns the shading library work and is still
accurate; this one is the **exporter/add-on options** side and the state of the release. Read the "Open work,
in order" section first — it is the queue.

## Where things are

| what | where |
|---|---|
| export repo | `C:\Users\user\Documents\dev\UmaViewer-PMX-Export-Patch` (branch `feat/extra-morphs`) |
| add-on repo | `C:\Users\user\AppData\Roaming\Blender Foundation\Blender\5.2\extensions\user_default\uma_addon` (branch `feat/morph-drivers`) |
| add-on version | **2.34.0** (`blender_manifest.toml`) — nothing changed it this session |
| exporter CLI | `Tools/headless_export.ps1` + `docs/CLI.md` — **runs from here**, see "How to verify" |
| shader libraries | `uma_addon/addons/uma_addon/operators/shaders/*.blend` beside their manifests |

The add-on tree is untouched this session and still has only the four `.blend.bak` files plus
`shaders/uma_face_blush_fixed.png` untracked.

**This session's changes are committed (`1eaeff5`), and the tree is still dirty with the user's own work in
progress.** The two could only be separated at hunk level, because they interleave in five files; anything
further must be staged the same way. Still uncommitted, and not this session's: the wet/rain look
(`UmaContainerCharacter.cs`, `UIShortcuts.cs`, `UISettingsModel.cs`), the export failure report
(`UISettingsModel.cs`), the CLI and wrapper work (`UmaHeadlessExport.cs`, `Tools/headless_export.ps1`,
`docs/CLI.md`), plus `ModelExporter.cs`, `TextureExporter.cs` and `UnityHumanoidVMDRecorder.cs` — the same ten
files the previous handoff listed. **That WIP is a single cross-file unit**: `UIShortcuts.cs` and
`UmaViewerBuilder.cs` call `ToggleWetLook` / `IsWetLook`, which live in the user's hunks of
`UISettingsModel.cs`, so committing any part of it without the rest does not compile. If a future split is
needed, materialise the index and build it before trusting it (`git show :<path>` per file, `dotnet build`,
restore) — that check caught two bad staging attempts this session.

## What shipped this session

**The naming question is answered, and the answer is "no Japanese mode".** The previous handoff left this
open with a question that decides the size of the job. It is settled, from the code, and it goes the other
way from what that handoff assumed:

- `rawName` is **never** Japanese, so `Japanese` as `yield return rawName` was never possible. The game ships
  morph *indices*; the viewer builds the name itself — `FaceDrivenKeyTarget.cs:81,115`:
  `morph.name = "EyeBrow_" + i + "_L"`, `morph.tag = Enum.GetName(typeof(FacialEyebrowId), i)` = `WaraiA`.
  The romaji lives in the decompiled enum at `LiveTimelineDefine.cs:161-221`, not in the data.
- A Japanese table *does* exist (`Config.VmdMorphConvertSetting`, `Config.cs:78-138`): 57 MMD-standard
  expression names over 94 UMA morphs. Measured against the model it is a clean subset — 94 of 192, zero
  stale names — but it **merges** (`困る右` = `EyeBrow_6_R` + `EyeBrow_12_R`) and leaves the other 98 morphs
  nameless, including *all 32* ear morphs. That is a different, incomplete morph set, not a spelling of this
  one, so it cannot be a naming mode.
- The user's call: **drop it** rather than ship a mode that names half a model and merges the other half,
  and revisit only if a real schema turns up. The table is left in `Config.cs` untouched and dormant, as
  agreed — but be aware its last consumer is gone too (below), so **nothing reads it now**.

What changed in code:

- **`Both` is gone and `Unified` took slot 2** (`PmxMorphNameMode` = 0 tagged, 1 short english,
  2 unified). `Both` wrote 432 morph entries for what `Unified` does in 192, and nothing needed the tagged
  spelling in the file that carries a motion. No migration: each build ships its own `Config.json` (the
  user's call), so the numbers are free to move; a stale value above 2 is not rejected by the enum but is
  caught by the CLI and falls back safely in code.
- **`NamesFor`/`VmdName` fail safe towards `Unified`.** `BlenderCompatible` is now an explicit `case` and
  `default` yields the unified name, because an unknown mode should not silently land on the one spelling no
  motion can drive. `VmdName` follows the same rule (`mode != ShortEnglish` → try unified first), which also
  means mode 0 motions now get the unified spelling rather than the short one — equally undrivable before,
  but the right side to fail towards.
- **`VmdUseEnglishMorphNames` is deleted** (`Config.cs`) and with it
  `UISettingsAnimation.EnableEnglishVmdMorphNames`, the recorder's Japanese branch and its warning. A motion
  and its model can no longer be spelled differently: `ConvertMorphName` always calls
  `MorphNaming.VmdName(name, tag, Config.Instance.PmxMorphNameMode)`.
  **This is also what orphaned `VmdMorphConvertSetting`** — that Japanese branch was its only reader.
- **`VmdUseEnglishBoneNames` is deleted too, later in the same session** — the previous handoff said to keep
  it; the user overruled that after seeing the argument below. `UISettingsAnimation.EnableEnglishVmdBoneNames`
  goes with it, `GetBoneNameForExport` loses its japanese branch (and the `センター`/`グルーブ` naming
  substitution, which only applied to japanese names; `UseCenterAsParentOfAll` stays, it also drives the IK
  recording maths), and the two `const`s it used are gone. A missing map entry is now a `LogError` rather than
  a silent japanese name — the table is complete (52 of 52 recordable bones), so it should be unreachable.

  Why this is *not* the same argument as the morph one, which matters for anyone tempted to re-litigate:
  the bone side is complete. `BoneNames` is the full MMD standard set and `EnglishBoneNameMap` maps every
  member. The argument is divergence, not data: `ModelExporter.cs:581` writes
  `pmxbone.Name = pmxbone.NameEn = bone.name` — the rig's english names, with no option for anything else — so
  a japanese-spelled vmd could not drive any pmx this project exports. It was the last remaining way to
  produce the silently-useless motion we had just removed from morphs.

  **The one thing lost:** a recorded motion can no longer be played on a *stock* MMD model (Miku and friends),
  which is what the japanese names were for. If that ever comes back, the honest fix is japanese bone names on
  the **model** side too — PMX carries a display name and an english name per bone, so the exporter could
  write `Name` japanese and `NameEn` english and the japanese path would drive our own pmx. Do not resurrect a
  vmd-only switch; that is the bug, not the feature.
- **The mode-0 warning survives** at record time (`MorphNaming.ModelAndMotionNamesAgree`), which the old
  queue said to delete along with the toggle path. Kept deliberately: it is the one place that tells a user,
  at the moment they waste an hour, that a mode-0 model cannot be driven. Delete it if you disagree.
- **`PmxExtraMorphs` has a control** (`UISettingsModel.EnablePmxExtraMorphs` + `_extraMorphs` toggle, wired
  into `ApplySettings`) — the on/off test the user asked for. The `…Tip` string it was supposed to need
  already existed in `Config.cs`; `…Tip` fields are JSON documentation only, no UI code reads them.
- **`-umaMorphNameMode` validates** instead of casting any integer through: `3` is now a clear failure
  naming the three modes, not a silent fourth spelling.
- **`_blush.png` naming fixed at the source**: `BakeBlush` takes the name the export writes the texture
  under and refuses to bake at all when neither that nor the source has a name. Both call sites pass
  `tex_name`.

**Verified, with real exports, after the editor closed:**

| run | result |
|---|---|
| `dotnet build umamusume.csproj` | 0 errors, 22 pre-existing warnings, none from the files touched |
| default (`C:\tmp\name_on`) | 205 morphs, 35 files, 34 textures, 5,798,573 B; unified names, `maxBytes=15`, all unique; the 13 advanced morphs present (`Cheek_Blush`, 8 × `Manga_*`, 4 × `Tear_*`) |
| `-NoExtraMorphs` (`C:\tmp\name_off`) | 192 morphs, 24 files, 23 textures, 5,733,691 B; zero advanced morphs |
| `-MorphNameMode 3` | exit 1: `FAILED: -umaMorphNameMode takes 0 (tagged), 1 (short english) or 2 (unified), not 3` |
| `-MorphNameMode 0` (`C:\tmp\mode0`) | 253 morphs, tagged spelling (`EyeBrow_1_R(WaraiA)[M_Face]`), and the inspector flags 240 of them as >15 bytes — model-only, exactly as documented |
| after the scene edit | 5,798,573 B, byte-identical to the pre-edit default run; log has no `Failed to call`, no `NullReference`, no exception |
| bone-switch retirement | `dotnet build` 0 errors; a parse of the file confirms `EnglishBoneNameMap` covers **52 of 52** recordable `BoneNames` members (only `None` is unmapped, and it never reaches the recorder), so the new `LogError` fallback is unreachable. No reference to the switch survives in any `.cs` |

**`_blush.png` is gone and the bakes are named**: both bakes are on disk as
`tex_chr1001_00_cheek0_blush.png` / `…cheek1_blush.png` and both are named by the pmx's texture table. A
folder scan (pmx texture table vs. the files) reports exactly one file written and never referenced, in both
exports: **`tex_chr1001_00_eye0_all.png`** — the pre-existing item below, not a regression. No `eyehi*_mask.png`
anywhere, and nothing the pmx names is missing from disk.

## Keybinds and the modes they collide with

The shortcuts (`docs/KEYBINDS.md` has the audit table; do not add a binding without reading it) **now
live in `Config.json`** as `ViewerShortcuts: [{ Name, Key, Alt }]`, parsed by name so `"R"` reads as `R`
rather than `114`. `UIShortcuts` keeps the action list and the context rules; an unknown name or key is
logged and skipped. Startup logs every binding it made.

What the audit found, and what changed:

- **The pose-mode conflict was much smaller than it looked.** Every `TransformGizmo` key needs LeftAlt
  (`Alt+W/E/R/Y/Z/C/X/S`), and `UIShortcuts` fires a plain binding only when Alt is *not* held — so plain
  `W`/`E`/`R` never fought the gizmo. What did need gating in pose mode: the **list walk**, because it
  loads whatever it lands on and would replace the model mid-pose, and **recording**, because
  `EnablePoseMode` disables the animator the recorder samples.
- **The free-camera conflict is real and total.** `CameraOrbit` mode 1 moves through the `Horizontal`/
  `Vertical` axes, i.e. WASD *and* the arrows, so every navigation shortcut fought it. Any binding whose
  key is one of those eight is now suspended while the camera flies; `E` and `R` do not collide and keep
  working. Matched against the bound key, not the action, so a rebound shortcut works while flying.
- **Two leaks fixed in the pose system itself**: `H` (help panel) and `Space` (IK toggle) fired in *every*
  mode, flipping state for a solver that does not exist outside pose mode. Both are gated on `PoseModeOn`.
- **`FreeCam.cs` is dead code** — no scene reference, no `AddComponent`. The live free camera is
  `CameraOrbit`. Left in place; it is on the delete-me list, because if it were ever wired up it would
  collide with nearly every shortcut.
- **Rebindable in `Config.json`, not in a panel**: there is no shortcuts UI, and `ShortcutConfig` is a
  plain list, so a panel row is not required to add one later.

Not verified: the suspensions were reasoned from the code and the build is clean, but no one has flown
the camera with a nav key held since the change — the headless runs only prove the nine default bindings
initialise (`[Shortcuts] 9 binding(s): ...`) and reach `Config.json`.

## The wet and dirt sets: exported, not yet shaded

The user's call: instead of a viewer toggle for the looks, **export the maps with the model** so the addon can
wire them into its shaders and hand the control to the user. The export half is built and verified; the addon
half is the open work (`docs/PMX_EXTENSIONS.md#the-texture-variants` is the contract to implement).

What the investigation established first, because it decides the design:

- **`_dirt` assets exist** — 33 in one alphabetical slice, e.g. `tex_bdy1001_00_dirt` beside
  `tex_bdy1001_00_diff`/`_base`/`_shad_c`, plus per-attachment (`tex_attach1048_00_dirt`), offline and
  zekken variants. Found with the new `-ListTextures` probe, not by reading a prefab.
- **The dry maps are not assets at all**: they live inside the part prefabs, while the variants are separate
  DB entries. That is why the export can pair them by name (`<dry>_wet`, `_diff`→`_dirt`) with no table —
  and why `AbChara` filtered to `/textures/` is exactly the variant list.
- **Dirt is not a wet-style swap**: the shader draws it from its own `_DirtTex` slot (unbound,
  `_DirtScale`, `_DirtRate` vector), and there is a *separate* global `_DirtRate` (float array of 3) plus
  three dirt tint colours on `UmaViewerGlobalShader`. `CharaShaderEffectData.DATA_SET_DIRT = 11` is a dead
  constant from the decompile. So the wet maps replace their dry counterparts, the dirt maps are an overlay.

**`Config.PmxTextureVariants`** (off by default, `-umaTextureVariants` / `-TextureVariants`) writes the variant
files under the game's own names and carries the contract in two places:

- model comment, one line: `texture variants: wet 11, dirt 2 - …`. Three states, and the addon must handle
  all three: **no line** = not requested (no files), **`texture variants: none (requested, …)`** = requested
  and this model ships none, **a count** = requested and these are the family sizes.
- material comment: `wet=<file>,<file> dirt=<file>`, because a pmx material has one texture slot and a second
  map can only be named in text — the same reason `morph=`/`blend=` exist. `wet=` files replace the dry map
  named by the token minus `_wet`; `dirt=` is the overlay. Written in both the uma-fields and plain-MMD
  paths, since it describes files rather than shader values.

**Measured on character 1001 costume 00** (the numbers the default-off decision rests on): 34 textures /
2.79 MB → **47 textures / 5.01 MB**, i.e. **13 files, +2.22 MB (+80%)** — 11 wet and 2 dirt. Three of the wet
ones are *tail* maps that a `1001_00` name filter misses, which is why the lookup runs at export time against
the whole asset list. The pmx grew 2,474 bytes (the tokens).

**Verified:** export with the option on (13 files, 7 materials with `wet=`, 5 with `dirt=`, model line
`wet 11, dirt 2`); the same export with it off is **byte-identical** to the pre-option baseline
(5,798,573 B, zero tokens, no model line); a **prop** with the option on gives `texture variants: none
(requested, …)`. `Tools/probe_texture_usage.py` was taught about the token and now reports the 13 files as
"referenced by a comment token, not by the table", leaving only the pre-existing `eye0_all` as unreferenced —
without that it called all 13 leftovers, which is exactly the trap this repo documents.

**What is left, in order:**

1. **The addon half.** `MorphDrivers.py:155` already collects every material's comment for `morph=`/`blend=`;
   a `wet=`/`dirt=` reader joins it, then a shading pass binds the files to the uma shader's wet and dirt
   inputs and drives them from a custom property. Nothing reads the tokens today, so MMD users see files
   nothing uses — acceptable only because the option is off by default.
2. **The dirt look's values are still unmeasured.** `DirtRates` (default `D`) steps the *global* `_DirtRate`
   so the shaders' response can be watched; once it is known, that knob becomes a real `DirtyLook` action
   (and the per-material `_DirtTex`/`_DirtScale` numbers get picked rather than guessed).
3. **No panel row** for `PmxTextureVariants` — `Config.json` and the CLI only, for now. Same shape as
   `PmxExtraMorphs` had before its row (see `UI_HOOKUP.md`).

## The export settings line, and where the contract lives

Every export now also states the switches that shaped it, on one model-comment line:

```
export settings: morph_names=unified extra_morphs=on texture_variants=off a_pose=on uma_material_fields=on
```

Why it earns its place: the metadata *version* describes the text format and nothing else, so it cannot say
why two exports of one character differ. `morph_names` renames every morph, `extra_morphs` and
`texture_variants` decide whether those features exist at all, `a_pose` changes the rest pose, and
`uma_material_fields` decides whether the material comments carry shader values. The values are **words, not
numbers**, because the enum's numbering has already moved once (`Both` retired, `Unified` 3 → 2).

**The contract now has two halves, one per repository, and they must be kept in step:**

| side | file | owns |
|---|---|---|
| written | this repo, `docs/PMX_EXTENSIONS.md` | what the exporter writes: the version rule, every line and token, the changelog, and where the textures live |
| relied on | `uma_addon/docs/PMX_METADATA_CONTRACT.md` | what a reader may assume, the three unversioned content contracts, what to ignore, and what to do when the texture folder did not travel |

The addon's `MorphDrivers.py` docstring points at both. Change one side and update the other in the same
breath - that is the whole reason the second file exists.

**The texture caveat is documented, not solved.** A pmx never embeds its textures, and the `wet=`/`dirt=`
files are named *only* by comment tokens, so a "delete what nothing references" pass - or a user copying the
`.pmx` alone - takes them. A model can therefore claim `texture variants: wet 11, dirt 2` with none of them
beside it. What the docs say a reader must do: verify each file it is about to use, name each missing one, and
degrade to "no variant wiring" rather than failing an import. `Tools/probe_texture_usage.py` already knows a
token is a reference; the addon side of that check is part of the queued compatibility report.

## The metadata format now has a version

`ModelExporter.MetadataVersion` (currently **2**) is written as `metadata format: 2` on its own model-comment
line, and `docs/PMX_EXTENSIONS.md` gained the section that makes the number mean something:
**a missing line is version 1**, so every legacy export is a supported input rather than a special case;
readers ignore unknown keys/tokens/lines and never gate on `Application.version`; a writer bumps only when an
older reader would get a *wrong answer* rather than no answer, and a bump obliges it to keep writing the old
spelling for a release.

Two findings from the audit that produced it, both worth remembering:

- **The markers we already had are dead.** `uma1`, the per-material prefix of the shader-key line, is written
  by the exporter and **read by nothing** — grepping the whole addon repo for it matches only its own
  `OUTLINES.md`. `Generated by UmaViewer <version>` is the *application* version and is also unread, which is
  lucky: gating on it would couple every reader to build numbers. The doc now says both things explicitly.
- **The format version is not what breaks users.** Morph names (mode 0 vs 2), deploy shape-key names
  (`Manga_*` vs the pre-2.34 `@_@`/`✦_✦`/`Tear_L`) and material-property names (`_CheekRate`, `Tear_Scroll`)
  all change without a metadata bump, and they are where an old export meets a new addon. `PMX_EXTENSIONS.md`
  now has a table for those three contracts, and `MorphDrivers.report_export` already reads both deploy-name
  spellings on purpose ("those are still in people's projects").

Also established: the addon's readers are presence-checked, so a legacy export **works** — the failure mode is
*silence* (no token → nothing wired; a token naming a property that no longer exists → Blender creates a
custom property and nothing happens). That silence is what produces the "older export" reports.

**Deferred, and the next step on this thread:** the **addon-side compatibility report** — print the format
version found, the tokens understood and ignored, and anything a token names that is missing (property,
material, image, shape key). It generalises `report_export` and is the change that turns a silent no-op into a
sentence a user can act on. It is addon-side work, so it belongs with the addon's own queue.

## Scene work — done, with two leftovers fixed

The user wired the panel in the editor; four things are worth knowing about the result.

- The **"Extra morphs export"** toggle exists, is bound to `UISettingsModel.EnablePmxExtraMorphs` with
  `m_Mode: 0` (the dynamic entry, which is the one that sends the tick's value) and starts on.
- **`_extraMorphs` was left unassigned** (`{fileID: 0}`), so `ApplySettings` could not show `Config.json`'s
  value in it — the row worked but could disagree with the config at startup. Assigned to the toggle
  component (`fileID: 344244284`) in the scene file, editor closed. `m_Value: 3` on the naming dropdown was
  likewise out of range for a three-member enum and is now `2`; `ApplySettings` overwrote it at runtime
  anyway, but a saved value with no matching option is a trap for the next reader.
- The **morph-names row is gone** (`EnableEnglishVmdMorphNames` no longer appears in the scene at all) and the
  dropdown carries the three current options. `_morphNameModeText` is still unassigned, so the description
  label under the dropdown stays empty — optional, see `UI_HOOKUP.md`.
- The bone-names control was **not relabelled**. It is a pre-existing row (the upstream viewer's own), so
  leaving its label alone costs nothing; the `Config.json` tip already reads `Bone names: English`.

## Open work, in order

### 1. The decal winding defect (shader side, still open)

Right-eye instances of two of the four manga prefabs (`mtl_eff_chr_emo_eye_002.001` Shut, `…_003.001` Wide)
and one tear sheet (`mtl_chr_tear001.001`) have polygons facing **into** the head —
`normal · outward = −0.705` / `−0.793` where their left twins read `+0.705` / `+0.793` — and the exporter
lifts every decal **along its own normal**, so those parts sit behind the face. That is the user's "most
manga invisible" and "Wide lost its black rim". Agreed fix, unwritten: flip the winding (and with it the
lift direction) for a decal part whose normal opposes the outward direction; the addon-side stopgap would be
the same test at shading time.

### 2. Small ones

- **A multiply material would bake a blush per textured slot.** `AddRenderer` walks every texture property
  and calls `BakeBlush` for each one on a multiply material, while `ModelExporter` only ever references
  `BlushName(mainTexture.name)`. Measured on 1001 the two bakes are `cheek0_blush` and `cheek1_blush` and
  **both** are named by the pmx, so it orphans nothing today — but the code is slot-agnostic while the
  reference is `mainTexture` only, so a multiply material with two textured slots would leave second-class
  files behind. Bake from `_MainTex` only if it ever bites.
- **`BakeHighlight` is now uncalled** (the mask bake was removed in `920843e` but the function stayed) and
  carries the same unnamed-artifact bug the new `BakeBlush` parameter fixes. Delete it, or give it the same
  `name` parameter, before anything calls it again. `docs/UMA_EYE_SHADER.md:64` still refers to it.
- **`tex_chr1001_00_eye0_all.png`** is written and referenced by nothing (42 KB/character). The user said to
  keep the four eye textures; this fifth one is a separate question.
- **Morph-setup guard**: `setup_advanced_morphs` reports `"<root>: the mesh carries no deploy morphs"` for a
  model that predates the advanced morphs — it does not crash, but "old model" and "setup failed" read
  identically.
- **Decals stay on the body** (the user's call): `split_face_into_object` only moves face+eye polygons, so
  the cheek and tear quads and their morphs remain on the body. They are bone-parented, so they follow the
  head — accepted.

## How to verify — the loop that makes this repo workable

```powershell
# 1. compile C# without Unity's UI (this works and caught two bugs)
dotnet build umamusume.csproj -v q --nologo          # expect 0 Errors

# 2. export for real, headless, ~90 s per run
.\Tools\headless_export.ps1 -Char 1001 -Costume 00 -Out C:\tmp\somewhere\x.pmx
#    log: Logs\headless_export_*.log  — the console only echoes an allowlist, so grep the log
#    requires: Unity 2022.3.62f1 (installed), the editor closed, Config.json pointing at the game data

# 3. Blender-side verifiers (all read-only, all report PASS/FAIL to a text file)
blender --background --factory-startup --python Tools\verify_decals.py -- <out.txt>
#    verify_brows, verify_eye_controls, verify_eye_panel, verify_spork_library, verify_decals,
#    verify_face_donor, verify_two_copies, measure_decal_key, measure_brows, probe_*.py
```

`Tools\probe_texture_usage.py <out.txt> <folder-or-pmx>` answers "is this file used" from the pmx's own
texture table; `Tools\probe_decal_normals.py` measures which way each decal part faces.

## Traps that cost time

- **Naming a derived file after `texture.name` instead of the exporter's own `tex_name`** is how `_blush.png`
  appeared: a `Texture2D` copy carries the source's name and `new Texture2D` starts out unnamed, so the
  bake inherited nothing. Pass the exporter's name through (see `BakeBlush(source, name)`).
- **Removing a UI method leaves its scene binding dangling** — Unity only complains at click time
  (`Failed to call function …`), so a compile-clean change still has a broken row.
- **Do not hand-edit `Assets/Scenes/Version2.unity` while the editor is open.** The editor holds the scene
  in memory and overwrites the file; a YAML edit only survives with the editor closed.
- **Renumbering a persisted enum is safe here only because each build ships its own `Config.json`.** Do not
  generalise that to a build that shares one.
- **PowerShell `.Replace` on source files fails silently** when the anchor's whitespace differs — it bit four
  times in an earlier session. Use the `edit` tool, or a Python script with `assert` per anchor and an
  explicit EOL. `pwsh` has no heredoc.
- **`AddonPanels.py` is CRLF**; an inserted block arrives LF-only and leaves mixed endings. Normalise after
  inserting, and check the byte counts. (`edit` preserves whatever the file already uses — verify with a
  CR-count if a file was touched by both.)
- **`sys.argv` slicing after `--`** must degrade to *empty*, not to `sys.argv[1:]`, or Blender's own flags
  become the script's arguments (`open_mainfile('--background')`).
- **Custom properties are never re-defaulted.** Changing a manifest `default` does not touch a property that
  already exists, so a file shaded under the old default keeps it — that is what made the decal key a second
  regression.
- **Unity `Debug.Log` from the exporter is invisible** on the headless console unless the wrapper's allowlist
  matches it; everything is in the log file.
- **The parts are `SkinnedMeshRenderer`s.** `GetComponent<MeshFilter>()` returns null for them; read
  `((SkinnedMeshRenderer)renderer).sharedMesh`.
- **`ShaderNodeMix` indexes sockets across all data types** (0 = float factor, 2 = float A, 3 = float B);
  linking by index wrongly is silent. And `vec_a*(1-F) + vec_b*F` means A is the "off" side.
- **`mmd_tools` hands texture names back with a trailing `>`** — normalise before comparing them to file names.
- **The export folder is not cleaned.** A fresh test needs a fresh folder, or leftovers from older settings
  look like current output (the "masks are still there" false alarm).

## Decisions the user has made (do not re-litigate)

Advanced morphs on/off must be testable from the panel; decals stay on the body; the blush is shelved; the
black key is gone; masks are gone; morph naming is **one dropdown with three modes** (`Both` retired,
`Unified` in slot 2); a japanese mode is **dropped, not deferred** — the table stays but nothing reads it,
and it comes back only if a real schema appears; **naming is english-only on both sides — the bone-names
switch is retired too**, accepting that a recorded motion no longer plays on a stock MMD model; vmd and pmx
naming must not be independently selectable; booleans stay checkboxes unless they grow variants; enum
renumbering needs no `Config.json` migration because each build carries its own.
