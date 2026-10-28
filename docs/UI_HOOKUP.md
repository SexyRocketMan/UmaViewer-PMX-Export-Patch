# Hooking the new export/recording options up in the UI

Everything below has a working `Config.json` key already, so nothing here is required for the features to
work - these rows are about not having to hand-edit `Config.json`. The C# side is done and committed; what
is left is scene work in `Assets/Scenes/Version2.unity`.

## Already wired - nothing to do

| Feature | Wired through |
|---|---|
| `Record VMD` recording one clean loop | the existing button calls `UmaViewerUI.AutoRecordVMD` |
| Texture set rows for props/scenes | built in code by `UISettingsModel.LoadTextureSetPanel` from `UmaViewerUI.UmaContainerTogglePrefab`, then cleared again on unload. No scene object needed - only keep `UISettingsModel.MaterialsList` assigned. |

## One scene edit the naming change needs

In `Assets/Scenes/Version2.unity`, so editor work like the rows further down.

**Delete the "English morph names" toggle.** Its callback was
`UISettingsAnimation.EnableEnglishVmdMorphNames`, which no longer exists: morph names now follow the
model tab's naming dropdown, because a motion and its model choosing their spellings independently is
how they silently stopped matching. The row's `onValueChanged` reference is now dangling - Unity logs
`Failed to call function EnableEnglishVmdMorphNames` when the toggle is clicked - so clear that call
list or delete the row. Nothing in `Config.json` depends on it any more (`VmdUseEnglishMorphNames` is
gone; a stale key in an old `Config.json` is simply ignored).

The **bone-names switch went the same way**, and its row is already deleted from the scene: there is no
`VmdUseEnglishBoneNames`, no `EnableEnglishVmdBoneNames`, and a recorded vmd always carries the rig's own
bone names. Nothing to do, but do not go looking for a missing row.


## The rows to add

| # | Option | Control to add | Where | Method to bind | Serialized field to assign | Default |
|---|---|---|---|---|---|---|
| 1 | A-pose rest pose for exported models | `Toggle` | Model tab, next to "Open with T-Pose" | `UISettingsModel.EnableAPoseRestPose` | `_aPoseRestPose` | off |
| 2 | Advanced morphs (decals as vertex morphs) | `Toggle` | Model tab, beside the naming dropdown | `UISettingsModel.EnablePmxExtraMorphs` | `_extraMorphs` | **on** |
| 3 | Morph naming mode | `TMP_Dropdown` | Model tab, near the export button | `UISettingsModel.ChangeMorphNameMode` | `_morphNameMode` | Unified (index 2) |
| 4 | VMD key reduction | `Slider` + label | Animation tab, under the record button | `UISettingsAnimation.ChangeVmdKeyReduction` | `_keyReduction`, `_keyReductionText` | 1 |
| 5 | Export the game's wet and dirt maps | `Toggle` | Model tab, under "Advanced morphs" | `UISettingsModel.EnablePmxTextureVariants` | `_textureVariants` | off |

The serialized fields are `[SerializeField] private`, so they show up in the inspector as `A Pose Rest
Pose`, `Extra Morphs`, `Texture Variants`, `Morph Name Mode`, `Key Reduction`, `Key Reduction Text`. They are
all **optional**: an unassigned one only means that row does not reflect `Config.json` at startup, and nothing
throws.

Row 5 is **placed and wired**: the `ExportTextureVariants` toggle's `onValueChanged` calls
`EnablePmxTextureVariants`, and `UISettingsModel._textureVariants` is assigned to that toggle, so `ApplySettings`
shows `Config.json`'s value at startup and a click writes the key back. That assignment was the missing half for
a while - a toggle whose call list is empty of a *target* only fires on a click, so the row could look on while
`Config.json` stayed false and the export wrote no variant files (measured, exactly how the "I cannot see the dirt
textures" report came in). A row like this needs both ends; the inspector shows the field as `Texture Variants`.

There is also an optional `_textureSetRowPrefab` (`UmaUIContainer`) on `UISettingsModel` if you want the
texture set rows to look different from the character/material toggle rows - leave it empty and they use
the shared container toggle prefab, which is what the materials list already uses.

### Suggested labels and dropdown options

* Toggle: `A-pose rest pose (ready for recorded motions)`
* Slider label text: `Key reduction: every frame` (the code rewrites this label as `every N frames`)
* Dropdown options, in this exact order (the index is the value written to `Config.PmxMorphNameMode`):

  | Index | Option text | Morph names look like | Use when |
  |---|---|---|---|
  | 0 | `Tagged (stock Blender addon)` | `EyeBrow_1_R(WaraiA)[M_Face]` | you use the unpatched `uma_addon` and never import vmds |
  | 1 | `Short english` | `EyeBrow_1_R` | the old fork behaviour |
  | 2 | `Unified (recommended)` | `Brow_WaraiA_R` | normal use; the only mode that works for model **and** motion |

  There used to be a fourth (`Both`, index 2: the tagged name plus a short alias). It is gone - 432 morph
  entries for what `Unified` does in 192, and nothing needed the tagged spelling in the file that carries a
  motion. `Unified` moved into slot 2, so **a saved dropdown value of 3 no longer means anything**: the
  exporter falls back to `Unified` rather than to the model-only tagged spelling, and `-MorphNameMode 3` is
  rejected on the command line. An option list that still has four entries has to lose the old index 2.

* Optional description label under the dropdown: assign a `TextMeshProUGUI` to `_morphNameModeText` and the
  code writes the explanation for whichever mode is selected, including the example, so the options
  themselves can stay short. The text it writes, verified against real exports (see below):

  | Mode | Label text |
  |---|---|
  | 0 | `Tagged: EyeBrow_1_R(WaraiA)[M_Face]` / `The stock Blender addon finds these, but the name is 27 bytes and a motion can only hold 15 - so a recorded vmd cannot drive these morphs.` |
  | 1 | `Short english: EyeBrow_1_R` / `Fits a motion, but the name says nothing about what the morph does.` |
  | 2 | `Unified: Brow_WaraiA_R` / `English group, romaji tag and side, inside the 15 byte vmd limit - one descriptive name for the model and the motion.` |

  Leave `_morphNameModeText` empty if you would rather put the examples in the option text itself; then
  append them to the option strings, e.g. `Tagged - EyeBrow_1_R(WaraiA)[M_Face]`.

### All three modes on one morph

Everything below is the smiling right eyebrow of character 1001, taken from real exports rather than typed
by hand (they were made while the retired `Both` mode still existed, which is why the numbering in the old
files reads one higher for `unified`):

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
| the morphs the Blender addon builds eye controls from | `Eye_20_R(XRange)[M_Face]` | `Eye_20_R` | `Eye_XRange_R` |

Over the whole character, mode 0 writes **240** morph entries (same morph on `M_Face` and `M_Mayu` kept
apart) and modes 1 and 2 write **192** (those 48 duplicates merge). The complete mapping, all 192 morphs in
all three spellings, is in [`MORPH_NAMES.md`](MORPH_NAMES.md), and `morph_name_table.py` in the companion
`UmaViewer-DevTools` tooling regenerates it from one export per mode and fails if a name in it is not
actually in the corresponding `.pmx`.

## Step by step (by duplicating an existing row)

1. Open `Assets/Scenes/Version2.unity`.
2. Find the settings panel that owns `UISettingsModel`: select any scene control that is already wired, e.g.
   the A-pose toggle, whose `onValueChanged` calls `EnableAPoseRestPose`, and look at the **target** of that
   call - that object is the `UISettingsModel` object. The `UISettingsAnimation` object is found the same way
   through the key-reduction slider.
3. Duplicate that row (`Ctrl+D`), rename the GameObject, change its label text, and clear the duplicated
   toggle's `onValueChanged` list before adding your own.
4. On the new control, add the callback: `onValueChanged` (`+`) → drag the `UISettingsModel` /
   `UISettingsAnimation` object into the object slot → pick the method **from the "Dynamic" section**
   (`EnableAPoseRestPose` / `EnablePmxExtraMorphs` / `ChangeVmdKeyReduction`). For the dropdown it is under
   `ChangeMorphNameMode` (int). Picking the static entry instead would send a fixed value and the control
   would do nothing.
5. Select the settings object and drag the new control into the matching serialized field listed above.
6. For the dropdown, set the option list exactly as in the table above (three entries), and set `Value` to 2
   (or leave it - `ApplySettings` overwrites it from `Config.json` at startup anyway).
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
  A-pose. Or headless: `Tools\headless_export.ps1 -Char 1001 -Costume 00 -APose -Out x.pmx`, then import
  `x.pmx` together with a recorded motion and pose nothing by hand.
* **Morph naming**: `Tools\headless_export.ps1 -Char 1001 -Costume 00 -Out x.pmx` then
  `uv run Tools/pmx_inspect.py names x.pmx --require Eye_XRange_L,Eye_XRange_R` for mode 2, plus
  `uv run Tools/pmx_inspect.py names x.pmx` to see every name and its byte length.
* **Advanced morphs**: the same export twice, once with the row on and once with it off (or `-NoExtraMorphs`),
  into **fresh folders** - the export folder is not cleaned, so leftovers from the previous run read as
  current output. With it on the pmx carries the `Cheek_Blush` / eye-manga / `Tear_*` morphs and the
  `morph=` tokens; with it off, none, and the decals sit where the game draws them.
* **Key reduction**: record, then `uv run Tools/vmd_inspect.py summary x.vmd` - the bone key count drops
  by roughly the reduction level, and `loop` still has to pass.
* **Texture sets**: load a home scene; the materials panel lists `Texture set <variant>` rows, the active
  one ticked. The headless equivalent is
  `Tools\headless_export.ps1 -Prop pfb_env_home10001_main000_000 -Variant 214 -Out x.pmx`.

## Gotchas worth knowing

* **Mode 0 is a dead end for motions.** Those names are 27-30 bytes and a vmd morph name field holds 15, so
  the keyframes are dropped on import. The recorder logs a warning when it records under mode 0, and
  `-MorphNameMode 3` - `Unified` under the retired numbering - is now rejected instead of silently
  exporting something else.
* **There is no japanese morph-name mode, on purpose.** The game ships no japanese morph names: the viewer
  builds `EyeBrow_1_L` itself from an index into an english enum (`Gallop.FaceDrivenKeyTarget`), so there is
  nothing to fall back on. The one japanese table in the repo, `Config.VmdMorphConvertSetting`, names 94 of
  the 192 morphs and merges several of them into one, so it is a *different, incomplete* morph set rather
  than a spelling of this one - see `HANDOFF_OPTIONS_AND_RELEASE.md`. The table is left in place, unused.
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
| Command line equivalents of every option | `Assets/Editor/UmaHeadlessExport.cs`, documented in [`CLI.md`](CLI.md) |
