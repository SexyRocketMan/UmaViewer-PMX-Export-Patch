# Hooking the new export/recording options up in the UI

Everything below has a working `Config.json` key already, so nothing here is required for the features to
work - these rows are about not having to hand-edit `Config.json`. The C# side is done and committed; what
is left is scene work in `Assets/Scenes/Version2.unity`.

## Already wired - nothing to do

| Feature | Wired through |
|---|---|
| English bone names in vmds | `UISettingsAnimation.EnableEnglishVmdBoneNames` (already called by a scene toggle) |
| English morph names in vmds | `UISettingsAnimation.EnableEnglishVmdMorphNames` (already called by a scene toggle) |
| `Record VMD` recording one clean loop | the existing button calls `UmaViewerUI.AutoRecordVMD` |
| Texture set rows for props/scenes | built in code by `UISettingsModel.LoadTextureSetPanel` from `UmaViewerUI.UmaContainerTogglePrefab`, then cleared again on unload. No scene object needed - only keep `UISettingsModel.MaterialsList` assigned. |

## The three rows to add

| # | Option | Control to add | Where | Method to bind | Serialized field to assign | Default |
|---|---|---|---|---|---|---|
| 1 | A-pose rest pose for exported models | `Toggle` | Model tab, next to "Open with T-Pose" | `UISettingsModel.EnableAPoseRestPose` | `_aPoseRestPose` | off |
| 2 | Morph naming mode | `TMP_Dropdown` | Model tab, near the export button | `UISettingsModel.ChangeMorphNameMode` | `_morphNameMode` | Unified (index 3) |
| 3 | VMD key reduction | `Slider` + label | Animation tab, under the record button | `UISettingsAnimation.ChangeVmdKeyReduction` | `_keyReduction`, `_keyReductionText` | 1 |

The serialized fields are `[SerializeField] private`, so they show up in the inspector as `A Pose Rest
Pose`, `Morph Name Mode`, `Key Reduction`, `Key Reduction Text`. They are all **optional**: an unassigned
one only means that row does not reflect `Config.json` at startup, and nothing throws.

There is also an optional `_textureSetRowPrefab` (`UmaUIContainer`) on `UISettingsModel` if you want the
texture set rows to look different from the character/material toggle rows - leave it empty and they use
the shared container toggle prefab, which is what the materials list already uses.

### Suggested labels and dropdown options

* Toggle: `A-pose rest pose (ready for recorded motions)`
* Slider label text: `Key reduction: every frame` (the code rewrites this label as `every N frames`)
* Dropdown options, in this exact order (the index is the value written to `Config.PmxMorphNameMode`):

  | Index | Option text | Morph names look like | Use when |
  |---|---|---|---|
  | 0 | `Tagged (stock Blender addon)` | `Eye_2_L(CloseA)[M_Face]` | you use the unpatched `uma_addon` and never import vmds |
  | 1 | `Short english` | `Eye_2_L` | the old fork behaviour |
  | 2 | `Both (tagged + short)` | both of the above | bridging: addon and motion both work |
  | 3 | `Unified (recommended)` | `Brow_WaraiA_R`, `Eye_XRange_L` | normal use; the only mode that works for model **and** motion |

## Step by step (by duplicating an existing row)

1. Open `Assets/Scenes/Version2.unity`.
2. Find the settings panel that owns `UISettingsModel`: select any scene toggle that is already wired, e.g.
   the one whose `onValueChanged` calls `EnableEnglishVmdMorphNames`, and look at the **target** of that
   call - that object is the `UISettingsAnimation` object. The `UISettingsModel` object is found the same
   way through one of its own toggles (the "Open with T-Pose" row).
3. Duplicate that row (`Ctrl+D`), rename the GameObject, change its label text, and clear the duplicated
   toggle's `onValueChanged` list before adding your own.
4. On the new control, add the callback: `onValueChanged` (`+`) → drag the `UISettingsModel` /
   `UISettingsAnimation` object into the object slot → pick the method **from the "Dynamic" section**
   (`EnableAPoseRestPose` / `ChangeVmdKeyReduction`). For the dropdown it is under `ChangeMorphNameMode`
   (int). Picking the static entry instead would send a fixed value and the control would do nothing.
5. Select the settings object and drag the new control into the matching serialized field listed above.
6. For the dropdown, set the option list exactly as in the table above, and set `Value` to 3 (or leave it
   - `ApplySettings` overwrites it from `Config.json` at startup anyway).
7. For the slider, set `Whole Numbers` on, `Min Value` 1, `Max Value` 4, and put the label
   `TextMeshProUGUI` into `_keyReductionText`.
8. Save the scene and commit the scene changes. The `.cs` files are already committed, so your commit
   should only contain the scene/prefab diffs.

`UmaViewerUI.Start` now calls `ModelSettings.ApplySettings()` and `AnimationSettings.ApplySettings()`, so
assigned controls show what `Config.json` says instead of whatever the row was saved with. Before that,
the scene's saved tick was the only thing the panel showed until you clicked it.

## Verifying each row

* **Any of them**: click the control, then open `Config.json` next to the exe - the value is written
  immediately (`UpdateConfig(false)`, no restart prompt; unlike Language/Region/WorkMode).
* **A-pose rest pose**: export a model and import it in Blender - the arms should already hang in the
  A-pose. Or headless:
  `Tools\run_workflow.ps1 -Char 1001 -Costume 00 -Motion anm_rac_type01_run02_stride -APose`
  (renders a clip without posing anything by hand).
* **Morph naming**: `Tools\headless_export.ps1 -Char 1001 -Costume 00 -Out x.pmx` then
  `uv run Tools/pmx_inspect.py names x.pmx --require Eye_XRange_L,Eye_XRange_R` for mode 3, plus
  `uv run Tools/pmx_inspect.py names x.pmx` to see every name and its byte length.
* **Key reduction**: record, then `uv run Tools/vmd_inspect.py summary x.vmd` - the bone key count drops
  by roughly the reduction level, and `loop` still has to pass.
* **Texture sets**: load a home scene; the materials panel lists `Texture set <variant>` rows, the active
  one ticked. The headless equivalent is
  `Tools\headless_export.ps1 -Prop pfb_env_home10001_main000_000 -Variant 214 -Out x.pmx`.

## Gotchas worth knowing

* **Mode 0 with english morph names is a dead end for motions.** Those names are 27-30 bytes and a vmd
  morph name field holds 15, so the keyframes are dropped on import. The recorder logs a warning when it
  sees that combination, and another one if japanese morph names are selected, because no exported model
  can carry those either.
* **Changing the naming mode is a re-export.** Models and motions have to be spelled the same way, so after
  switching the dropdown, export the model again before using an old motion with it.
* **The A-pose row only affects exports.** Recordings are always relative to the A-pose whatever the row
  says; the row decides whether the exported model's rest pose matches that.
* **T-pose vs A-pose**: leave it off if you retarget in Blender or use Rigify, since those expect a T-pose
  rest pose. Turn it on if you drop recorded motions straight onto an exported model.
* **Scene defaults**: if you would rather not assign the serialized field, set the toggle's saved `Is On`
  state to the default you want - it just will not follow `Config.json` after that.

## Where it lives in code

| What | File |
|---|---|
| A-pose implementation both the exporter and the recorder use | `Assets/Scripts/Exporters/UmaAPose.cs` |
| Export options, their callbacks and `ApplySettings` | `Assets/Scripts/Settings/UISettingsModel.cs` |
| Recording options, their callbacks and `ApplySettings` | `Assets/Scripts/Settings/UISettingsAnimation.cs` |
| `Config.json` keys and their tooltips | `Assets/Scripts/Config.cs` |
| Auto-record button | `UmaViewerUI.AutoRecordVMD` in `Assets/Scripts/UmaViewerUI.cs` |
| Texture set rows | `UISettingsModel.LoadTextureSetPanel` + `Assets/Scripts/UmaEnvTextureSet.cs` |
| Command line equivalents of every option | `Assets/Editor/UmaHeadlessExport.cs` |
