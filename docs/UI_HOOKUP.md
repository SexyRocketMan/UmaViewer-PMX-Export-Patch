# Hooking the export/recording options up in the UI

Every option below has a working `Config.json` key, so the features work without any of this; the rows exist so
`Config.json` need not be hand-edited. The C# side is in place. What remains is scene work in
`Assets/Scenes/Version2.unity`.

## Already wired - nothing to do

| Feature | Wired through |
|---|---|
| `Record VMD` recording one clean loop | the existing button calls `UmaViewerUI.AutoRecordVMD` |
| Texture set rows for props/scenes | built in code by `UISettingsModel.LoadTextureSetPanel` from `UmaViewerUI.UmaContainerTogglePrefab`, then cleared again on unload. No scene object needed - only keep `UISettingsModel.MaterialsList` assigned. |

## One scene edit the naming change needs

In `Assets/Scenes/Version2.unity`, so editor work like the rows further down.

**Delete the "English morph names" toggle.** Its callback `UISettingsAnimation.EnableEnglishVmdMorphNames` is gone:
morph names follow the model tab's naming dropdown, because a motion and its model choosing their spellings
independently is how they stop matching. The row's `onValueChanged` reference is dangling - Unity logs
`Failed to call function EnableEnglishVmdMorphNames` on every click - so clear that call list or delete the row.
Nothing in `Config.json` depends on it (`VmdUseEnglishMorphNames` is gone; a stale key is ignored).

The **bone-names switch went the same way** and its row is already deleted: there is no `VmdUseEnglishBoneNames`,
no `EnableEnglishVmdBoneNames`, and a recorded vmd carries the rig's own bone names.

## The rows to add

| # | Option | Control to add | Where | Method to bind | Serialized field to assign | Default |
|---|---|---|---|---|---|---|
| 1 | A-pose rest pose for exported models | `Toggle` | Model tab, next to "Open with T-Pose" | `UISettingsModel.EnableAPoseRestPose` | `_aPoseRestPose` | on |
| 2 | Advanced morphs (decals as vertex morphs) | `Toggle` | Model tab, beside the naming dropdown | `UISettingsModel.EnablePmxExtraMorphs` | `_extraMorphs` | **on** |
| 3 | Morph naming mode | `TMP_Dropdown` | Model tab, near the export button | `UISettingsModel.ChangeMorphNameMode` | `_morphNameMode` | Unified (index 2) |
| 4 | VMD key reduction | `Slider` + label | Animation tab, under the record button | `UISettingsAnimation.ChangeVmdKeyReduction` | `_keyReduction`, `_keyReductionText` | 1 |
| 5 | Export the game's wet and dirt maps | `Toggle` | Model tab, under "Advanced morphs" | `UISettingsModel.EnablePmxTextureVariants` | `_textureVariants` | off |

Defaults are the `Config.cs` field values, written into the rows by `ApplySettings` at startup. The serialized
fields are `[SerializeField] private`, i.e. `A Pose Rest Pose`, `Extra Morphs`, `Texture Variants`,
`Morph Name Mode`, `Key Reduction`, `Key Reduction Text` in the inspector. All are **optional**: an unassigned one
only means that row does not show `Config.json`'s value at startup, and nothing throws.

Row 5 is **placed and wired**: the `ExportTextureVariants` toggle's `onValueChanged` calls
`EnablePmxTextureVariants` and `UISettingsModel._textureVariants` holds that toggle, so `ApplySettings` shows the
key at startup and a click writes it back. Both ends are required: a toggle whose call list has no *target* only
fires on a click, and the row can read on while `Config.json` stays false and no variant files are written.

An optional `_textureSetRowPrefab` (`UmaUIContainer`) restyles the texture set rows; empty uses the shared
container toggle prefab, as the materials list does.

### Suggested labels and dropdown options

* Toggle: `A-pose rest pose (ready for recorded motions)`
* Slider label text: `Key reduction: every frame` (the code rewrites this label as `every N frames`)
* Dropdown options in this exact order - the index is the value written to `Config.PmxMorphNameMode`:

  | Index | Option text | Morph names look like | Use when |
  |---|---|---|---|
  | 0 | `Tagged (stock Blender addon)` | `EyeBrow_1_R(WaraiA)[M_Face]` | you use the unpatched `uma_addon` and never import vmds |
  | 1 | `Short english` | `EyeBrow_1_R` | the old fork behaviour |
  | 2 | `Unified (recommended)` | `Brow_WaraiA_R` | normal use; the only mode that works for model **and** motion |

  A fourth mode (`Both`: the tagged name plus a short alias, index 2 in the retired numbering) is gone - 432 morph
  entries for what `Unified` does in 192, and nothing needed the tagged spelling in the file that carries a motion.
  `Unified` sits in slot 2, so **a saved dropdown value of 3 means nothing**: the exporter falls back to
  `Unified`, and `-MorphNameMode 3` is rejected on the command line. An option list with four entries must lose the
  old index 2.

* Optional description label: assign a `TextMeshProUGUI` to `_morphNameModeText` and the code writes the
  explanation for the selected mode, example included, so the options can stay short. The text it writes, checked
  against real exports:

  | Mode | Label text |
  |---|---|
  | 0 | `Tagged: EyeBrow_1_R(WaraiA)[M_Face]` / `The stock Blender addon finds these, but the name is 27 bytes and a motion can only hold 15 - so a recorded vmd cannot drive these morphs.` |
  | 1 | `Short english: EyeBrow_1_R` / `Fits a motion, but the name says nothing about what the morph does.` |
  | 2 | `Unified: Brow_WaraiA_R` / `English group, romaji tag and side, inside the 15 byte vmd limit - one descriptive name for the model and the motion.` |

  Leave `_morphNameModeText` empty to put the examples in the option text instead, e.g.
  `Tagged - EyeBrow_1_R(WaraiA)[M_Face]`.

### All three modes on one morph

The smiling right eyebrow of character 1001, from real exports (they predate the retirement of `Both`, which is why
`unified` reads one index higher there):

| Mode | This morph is called | Bytes | A vmd can drive it |
|---|---|---|---|
| 0 tagged | `EyeBrow_1_R(WaraiA)[M_Face]` | 27 | no |
| 1 short english | `EyeBrow_1_R` | 11 | yes |
| 2 unified | `Brow_WaraiA_R` | 13 | yes |

Two more that show the other rules:

| Rule | tagged | short | unified |
|---|---|---|---|
| the tag says what the morph does | `Eye_2_L(CloseA)[M_Face]` | `Eye_2_L` | `Eye_CloseA_L` |
| the side moves out of the tag into the name | `Mouth_2_0(CheekA_L)[M_Face]` | `Mouth_2_0` | `Mouth_CheekA_L` |
| a tag too long with its group gets abbreviated | `Eye_27_L(EyelidHideA)[M_Face]` | `Eye_27_L` | `Eye_LidHideA_L` |
| the morphs the Blender add-on builds eye controls from | `Eye_20_R(XRange)[M_Face]` | `Eye_20_R` | `Eye_XRange_R` |

Over the whole character, mode 0 writes **240** morph entries (the same morph on `M_Face` and `M_Mayu` kept apart)
and modes 1 and 2 write **192** (those 48 duplicates merge). The complete mapping, all 192 morphs in all three
spellings, is in [`MORPH_NAMES.md`](MORPH_NAMES.md); `morph_name_table.py` in the companion `UmaViewer-DevTools`
tooling regenerates it from one export per mode and fails if a name in it is not in the corresponding `.pmx`.

## Step by step (by duplicating an existing row)

1. Open `Assets/Scenes/Version2.unity`.
2. Find the object owning `UISettingsModel`: select a wired control (the A-pose toggle, whose `onValueChanged` calls
   `EnableAPoseRestPose`) and read its call's **target**. `UISettingsAnimation` is found the same way through the
   key-reduction slider.
3. Duplicate that row (`Ctrl+D`), rename the GameObject, change the label text, and clear the duplicated toggle's
   `onValueChanged` list.
4. Add the callback: `onValueChanged` (`+`) → drag the settings object into the object slot → pick the method
   **from the "Dynamic" section** (`EnableAPoseRestPose` / `EnablePmxExtraMorphs` / `ChangeVmdKeyReduction`; the
   dropdown's is `ChangeMorphNameMode`, int). The static entry would send a fixed value and do nothing.
5. Drag the new control into the matching serialized field above.
6. For the dropdown, set the three options exactly as above and set `Value` to 2 (or leave it - `ApplySettings`
   overwrites it from `Config.json` at startup).
7. For the slider, set `Whole Numbers` on, `Min Value` 1, `Max Value` 4, and put the label `TextMeshProUGUI` into
   `_keyReductionText`.
8. Save the scene and commit the scene changes only; the `.cs` files are already committed.

`UmaViewerUI.Start` calls `ModelSettings.ApplySettings()` and `AnimationSettings.ApplySettings()`, so assigned
controls show `Config.json` instead of the saved tick; an unassigned control keeps its saved state until clicked.

## Verifying each row

* **Any of them**: click the control, then open `Config.json` next to the exe - the value is written immediately
  (`UpdateConfig(false)`, no restart prompt; unlike Language/Region/WorkMode).
* **A-pose rest pose**: export and import in Blender - the arms hang in the A-pose. Headless:
  `Tools\headless_export.ps1 -Char 1001 -Costume 00 -APose -Out x.pmx`, then import `x.pmx` with a recorded motion
  and pose nothing by hand.
* **Morph naming**: `Tools\headless_export.ps1 -Char 1001 -Costume 00 -Out x.pmx`, then
  `uv run Tools/pmx_inspect.py names x.pmx --require Eye_XRange_L,Eye_XRange_R` for mode 2, and
  `uv run Tools/pmx_inspect.py names x.pmx` for every name with its byte length.
* **Advanced morphs**: the same export twice, row on and off (or `-NoExtraMorphs`), into **fresh folders** - the
  export folder is not cleaned, so leftovers read as current output. On, the pmx carries the `Cheek_Blush` /
  eye-manga / `Tear_*` morphs and the `morph=` tokens; off, none, and the decals sit where the game draws them.
* **Key reduction**: record, then `uv run Tools/vmd_inspect.py summary x.vmd` - the bone key count drops by
  roughly the reduction level, and `loop` still has to pass.
* **Texture sets**: load a home scene; the materials panel lists `Texture set <variant>` rows, the active one
  ticked. Headless: `Tools\headless_export.ps1 -Prop pfb_env_home10001_main000_000 -Variant 214 -Out x.pmx`.

## Gotchas worth knowing

* **Mode 0 is a dead end for motions.** Those names are 27-30 bytes and a vmd morph name field holds 15, so the
  keyframes are dropped on import. The recorder logs a warning under mode 0, and `-MorphNameMode 3` is rejected
  rather than silently exporting something else.
* **There is no japanese morph-name mode, deliberately.** The game ships no japanese morph names: the viewer builds
  `EyeBrow_1_L` itself from an index into an english enum (`Gallop.FaceDrivenKeyTarget`). The one japanese table in
  the repo, `Config.VmdMorphConvertSetting`, names 94 of the 192 morphs and merges several into one, so it is a
  *different, incomplete* morph set - see `archive/HANDOFF_OPTIONS_AND_RELEASE.md`. It is left unused.
* **Changing the naming mode is a re-export.** After switching the dropdown, export the model again before using an
  old motion with it; models and motions must be spelled alike.
* **The A-pose row only affects exports.** Recordings are always relative to the A-pose; the row decides whether
  the exported model's rest pose matches that.
* **T-pose vs A-pose**: leave it off for retargeting or Rigify, which expect a T-pose rest. Turn it on if you drop
  recorded motions straight onto an exported model.
* **Scene defaults**: instead of assigning the serialized field, set the toggle's saved `Is On` state - it just
  will not follow `Config.json` after that.

## Where it lives in code

| What | File |
|---|---|
| A-pose implementation both the exporter and the recorder use | `Assets/Scripts/Exporters/UmaAPose.cs` |
| Export options, their callbacks and `ApplySettings` | `Assets/Scripts/Settings/UISettingsModel.cs` |
| Recording options, their callbacks and `ApplySettings` | `Assets/Scripts/Settings/UISettingsAnimation.cs` |
| `Config.json` keys and their tooltips | `Assets/Scripts/Config.cs` |
| Auto-record button | `UmaViewerUI.AutoRecordVMD` in `Assets/Scripts/UmaViewerUI.cs` |
| Texture set rows | `UISettingsModel.LoadTextureSetPanel` + `Assets/Scripts/UmaEnvTextureSet.cs` |
| Command line equivalents of every option | `Assets/Scripts/Cli/UmaHeadlessExport.cs`, documented in [`CLI.md`](CLI.md) |
