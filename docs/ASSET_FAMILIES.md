# The game's `3d/chara` families: what is loaded and what is never touched

Ten families sit under `Root/3d/chara`: `attach`, `body`, `common`, `head`, `mini`, `prop`, `richprop`, `sweat`,
`tail`, `toonprop`. All ten are loaded into the viewer at startup; what differs is which of them any code path
reads. One family - `sweat` - is read by nothing at all.

## What the viewer has in memory

`UmaViewerMain.cs` (`LoadDatabase`) builds the character list as "every `_3d_cutt` manifest entry whose name starts
with `3d/chara/`":

```csharp
var chara_3d = AbList.Where(ab => ab.Value.Type == UmaFileType._3d_cutt).Select(ab => ab.Value).ToList();
AbChara = chara_3d.FindAll(ab => ab.Name.StartsWith(UmaDatabaseController.CharaPath));   // "3d/chara/"
```

`AbList` is the whole manifest, so `AbChara` holds **all ten families** - the prefix is the only filter.

## Per family

| family | what the code does with it | where |
|---|---|---|
| `body` | the main character prefab (`pfb_bdy{id}_{costume}`) | `UmaDatabaseController.BodyPath`, `UmaViewerBuilder` |
| `head` | head/face prefabs (`pfb_chr{id}_{costume}…`) | `UmaDatabaseController.HeadPath` |
| `tail` | tail prefabs and their textures | `UmaViewerBuilder` (`3d/chara/tail/{name}/`) |
| `mini` | the chibi build: `mini/body`, `mini/head`, `mini/tail` | `MiniBodyPath`, `UmaViewerBuilder` |
| `common` | **partly**: `common/textures/tex_chr_tear00` and `common/tear/tear000|001/pfb_chr_tear000|001` | `UmaContainerCharacter` (the tear texture, and the tear prefab list) |
| `prop` | the prop panel, for prefabs whose file name starts `pfb_chr_prop`; the CLI's `-umaProp <substring>` resolves *any* prefab name in the whole `AbList` | `UmaViewerUI.cs` (`IsPropAsset`, `LoadPropPanel`), `Editor/UmaHeadlessExport.cs` (`FindPropEntry`) |
| `attach` | worn on the character: the prefab's skins are retargeted onto the character's bones by name, one toggle per prefab in the materials panel | `UmaContainerCharacter.AttachAccessory`, `UmaViewerBuilder.LoadAttach`, `UISettingsModel.LoadAttachPanel`, `-umaAttach <pattern>` |
| `richprop` | the prop panel, for `pfb_rich_prop*` | `UmaViewerUI.cs` (`PropPrefixes`) |
| `toonprop` | the prop panel, for `pfb_toon_prop*`; not an attachment | `UmaViewerUI.cs` (`PropPrefixes`) |
| `sweat` | nothing. `sweat` appears in the code only as `LiveTimelineDefine.SweatLocator = 59` (a live-camera locator enum) and in the comments that keep the family off the prop panel | — |

## Measured: the gap is a filter per family, not one substring

`headless_export.ps1 -ListFamilies` prints, from the manifest alone, what each family holds and what its prefabs
are called (digit runs masked, so 22 857 body entries collapse to the handful of forms a family is really built
from):

| family | entries | prefabs | naming forms (representative) | prop panel shows |
|---|---|---|---|---|
| `body` | 22 857 | 8 179 | `pfb_bdy####_##…`, `ast_bdy…_skirt##`, `ast_bdy…_ikcol…` | 0 |
| `head` | 8 896 | 3 168 | `pfb_chr####_##_hair###_cloth##`, `anm_chr####_##_face###_facial_eye#` | 0 |
| `mini` | 2 897 | 1 287 | `pfb_mbdy####_##`, `pfb_mattach####_##_####_##`, `ast_mbdy…_skirt…` | 0 |
| `prop` | 1 231 | 1 229 | `pfb_chr_prop####_##`, `…_cloth##`, `…_variant` | **1 194** |
| `tail` | 711 | 42 | `pfb_tail####_##`, `…_cloth##`, `tex_tail…_area…` | 0 |
| `sweat` | 503 | 503 | `pfb_chr####_##_sweatlocator`, `pfb_chr####_##_face###_sweatlocator` | 0 |
| `attach` | 321 | 267 | `pfb_attach####_##_####_##`, `tex_attach…_{base,diff,shad_c}_{wet,dirt}` | 0 |
| `toonprop` | 153 | 145 | `pfb_toon_prop####_##`, `…_cloth##`, `ast_toon_prop…_shader_effect_data` | 0 |
| `common` | 7 | 2 | `pfb_chr_tear###`, `tex_chr_{tear,env,dither}#` | 0 |
| `richprop` | 3 | 3 | `pfb_rich_prop####_##` | 0 |

No unused family is named `pfb_chr_prop*`. Three of them *are* prefab families a prop panel can reasonably show,
under their own prefixes:

* **`attach`** (321 entries) - real models that ship their own `_wet`/`_dirt` texture sets exactly like body
  textures do, i.e. shaded parts of a character, not metadata. `pfb_attach####_##_…`.
* **`toonprop`** (153) - `pfb_toon_prop####_##`, plus `ast_toon_prop…_shader_effect_data` siblings, which is
  shader-driven prop data of the kind the outline/effect work cares about.
* **`richprop`** (3) - `rich_prop1001_00`, `rich_prop1002_00`, `rich_prop1005_00` and nothing else. Small enough to
  be one panel line, not a family worth building for.

**`sweat` is not a prop family at all**: all 503 entries are `…_sweatlocator` (or `…_face###_sweatlocator`)
prefabs - locators, the same notion as `LiveTimelineDefine.SweatLocator = 59`, i.e. points the sweat effect is
placed at. They hold no geometry and do not belong on the prop panel.

A prop filter therefore has to know the prefix per family (`pfb_chr_prop`, `pfb_toon_prop`, `pfb_rich_prop` - and
deliberately not `sweatlocator`), not one wider substring: the family *folder* name is not part of the prefab name
(`richprop/rich_prop1001_00/pfb_rich_prop1001_00`), so a filter derived from folders misses every family it is
meant to reach. Widening the predicate costs one predicate and, at most, a question about opening bundles - no new
loader and no new manifest stage.

## Checking a family

* `-umaListFamilies [filter]` in `Editor/UmaHeadlessExport.cs`, `-ListFamilies` / `-ListFamiliesFilter` on
  `Tools/headless_export.ps1`: groups `AbChara` by the path's second segment and prints, per family, entry count,
  prefab count, the naming forms (digit runs masked) and three samples. Metadata only - it reads the manifest and
  opens no bundle, so `Config.DownloadMissingResources` cannot fire.
* `-umaProp pfb_attach1048_00_0001_00`, `-umaProp pfb_toon_prop1001_00`, `-umaProp pfb_rich_prop1001_00`: the CLI
  path that resolves an arbitrary prefab name. `FindPropEntry` searches the whole `AbList` by substring and prints
  sibling names on an ambiguous match. It *loads* what it finds, and `Config.DownloadMissingResources` is `true`,
  so an absent bundle can trigger a download (the VPN caveat in `Config.json`).

## What the families are, and where each lives

| family | what it actually is | where it lives |
|---|---|---|
| `prop` | 1 194 standalone props | prop panel (unchanged) |
| `toonprop` | **one per character** (140 folders, 145 prefabs, `toon_prop{id}_{n}`), a partial rig - `Root` → `ArmRoll_R` → `Wrist_R` → fingers, 21 bones, local coordinates. A held/effect object | prop panel |
| `richprop` | three assets (`pfb_rich_prop1001/1002/1005_00`), character-sized | prop panel |
| `attach` | **not props at all** - see below | character attachments |
| `sweat` | 503 `…_sweatlocator` prefabs, no geometry | nowhere |

`UmaViewerUI.LoadPropPanel` matches one prefix per listed family (`pfb_chr_prop`, `pfb_toon_prop`,
`pfb_rich_prop`), skips `clothes`, sorts by path so a family's entries sit together, and puts the family name in
each entry's search keywords (`richprop`'s prefabs are `pfb_rich_prop*`). The panel goes from 1 194 entries to
**1 342**; `attach` and `sweat` are deliberately not in it.

### `attach` is a character accessory, and it says so itself

`pfb_attach1048_00_0001_00` exports as **830 verts / 2 928 indices / 76 bones**, and the bone list is the answer:
`Position` → `Hip` → `UpBody_Ctrl`/`Waist` → `Spine`/`Chest`/`Neck`/`Head`, both arms with all fifteen finger bones,
`Hand_Attach_L/R`, `Sp_Ch_Bust0_L/R`, plus animator leftovers (`M_Attach`, `Wrist_*_Pole`, `Wrist_*_Target`) - a
**copy of a character rig**, at that character's rest pose, with the accessory geometry skinned to it. The game's
job when spawning one is a name-based retarget onto the character's own bones, which is what
`UmaContainerCharacter.MergeBone` already does for hair; `AttachAccessory` does it for accessories,
`UmaViewerBuilder.LoadAttach` loads and wears one, `UISettingsModel.LoadAttachPanel` offers them as toggles in the
materials panel (28 for character 1048, the deepest set), and `-umaAttach <pattern>` does it headlessly.

Only **ten characters** ship the family (`attach{id}_00/`: 1048, 1081, 1112, 1113, 1128, 1132, 1140, 2002, 2003,
2007), even though the id is a character id - 1132 and 1140 have no costume 00 in the costume list, so the `_00`
is the set index, not the costume.

Measured on 1048 + `pfb_attach1048_00_0001_00` against the same character bare:

| | bare | worn | difference |
|---|---|---|---|
| vertices | 15 767 | 16 597 | **+830** - the prefab's own count, exactly |
| indices | 89 022 | 91 950 | +2 928 |
| materials | 27 | 28 | +1 (`mtl_attach1048_00`) |
| textures | 31 | 35 | +4, and `Texture2D/` gains the wet/dirt twins |
| bones | 425 | 427 | **+2** - the accessory's own holder nodes (`pfb_attach1048_00_0001_00`, `M_Attach`); every rig bone it carries was retargeted, none came along |
| rendered pixels | - | - | **inside the noise**: 628 px change by more than 12/255 against a 746 px floor from two *identical* bare runs. The attach set repeats geometry the body already draws, so it makes no visual difference |

Three rules this leaves, each cheap to violate:

* **`Renderer.bounds` is stale in the frame a hierarchy changes** - the numbers it reports for a just-parented
  accessory are the pre-move ones. Read geometry back from the export (`pmx_inspect.py bounds`) instead.
* **`Destroy` is too late.** The exporter counts every transform under `Position`, and a deferred `Destroy` leaves
  the released prefab bones alive until the end of the frame: an attach applied during the settle stage exports
  **500 bones instead of 425**, the prefab's whole rig coming along as duplicate names. Release them with
  `DestroyImmediate`.
* **Prune from a capture taken before the retarget.** The retarget reparents the prefab's bones onto the
  character, so a walk of the prefab's tree afterwards no longer sees the leftovers it should remove. Capture the
  prefab's nodes and what carries renderers *before* the retarget, retarget, then remove the captured nodes that
  are neither a renderer holder nor still pointed at by a skin. Nodes nothing points at any more (the bust chains,
  the IK pole/target nodes) are dead weight the export reports as extra bones.

## `toonprop`: not character props, whatever the numbers suggest

The ids line up (`toon_prop1001_00` beside `bdy1001_00`) and both prefabs examined carry bone names a character
also has, but that is a coincidence: the game numbers things per character, not per owner, and none of them
aligned when placed.

| prefab | what its weights say | what it actually is |
|---|---|---|
| `pfb_toon_prop1001_00` | `Root` 102.3, `Wrist_R` 52.7, `ArmRoll_R` 36.9, every right-hand finger, 489 verts | **a model of a hand**, fingers to elbow. Its purpose is not established |
| `pfb_toon_prop1048_00` | `Waist` 384.9, `Chest` 338.1, `Hip` 276.3, `Thigh_R/L` ~150, knees, elbows, 2 126 verts | a body-shaped **prop that reuses the uma armature** - its bones *do* line up with a character's |

So `toonprop` is a family of prop meshes authored on uma-shaped rigs, not a per-character attachment. It is not in
the attachment list (it was for one revision, `1e171ff`..`54bec20`), it stays browsable as a prop, and what holds
one is an open question.

## The `prop` family does not answer "which of these is held" either

`-umaScanProps` loads `3d/env` scenes and `3d/chara` props, and reports, per prop, whether it is skinned and which
bones it names. Sixty `pfb_chr_prop*` prefabs scanned in one editor boot:

* **none of them names a hand bone.** Not one `Wrist_*`, `Hand_Attach_*`, or finger.
* every one is a single skinned renderer on **its own little rig**, named for what it is: `Page0_0`/`Page1_0`
  (a book), `Tea0_C_00`/`Saucer_C_00` (a tea set), `Tail0_B_00`, `Chain0_5`, `Bamboo_Root`, `Wing0_L_00`,
  `Frag_Root`, `Cover_L/R`, `Cap0_0` - props with bones of their own, not props bound to a character.
* sizes run 0.17 to 15.08 model units, i.e. from a wing ornament to something taller than the character.

A held prop is not *skinned* to a hand, it is *placed* at one, and that placement is not in the prefab. The signal
has to be the game's own attachment data - the animator/locator assets (`3d/animator/*`, the same
`drivenkeylocator` idea the exporter already knows about) and the motions that reference them - which is the next
thing to enumerate, not the prop bundles. Two prefabs look like counter-examples until measured:
`pfb_chr_prop1003_10` names `Cover_L`/`Cover_R` (a book with covers) and `pfb_chr_prop1019_00` names
`Sp_Ro_cloth2_*` (cloth chains) - prop-internal rigs again, nothing to do with a character's arm.

## What placement can be measured

| case | placement | material box (model units) | reading |
|---|---|---|---|
| 1048 prop, retargeted | wraps the body | centre (−0.07, 9.79, 0.56), size 12.34 × 19.49 × 3.13, body is 12.75 × 16.82 × 3.04 | it lines up because it *is* body-shaped, which is why it reads as a costume |
| 1048 prop, rigid at the hand | off to the side, above the head | centre (−11.24, **19.21**, 1.48), y 10.97..27.46 | wrong |
| 1001 prop, rigid at `Hand_Attach_R` | compact, beside the hand | centre (−7.01, 10.58, 0.75), size 3.41 × 3.08 × 1.35, hand at (−5.49, 11.90, 0.87) | the hand model sitting next to a hand, which reads as "correct" |
| 1001 prop, retargeted | stretched from the hand to the floor | size 6.48 × **12.96**, y −0.36..12.61 | wrong - Root's weights stay at the prefab's origin while the hand's weights follow the hand |

Rendered, against the harness's own noise floor (two identical bare runs differ by **746 px** at >12/255):

| change | pixels changed | reading |
|---|---|---|
| 1048 + attach only | 1 101 | 1.5x noise - and the attach set repeats geometry the body already draws |
| 1048 + its toon prop | **5 092** | 6.8x noise, clearly visible (it is body-sized) |
| 1001 + its toon prop, full-body view | 571 | **below** the noise floor; the prop is ~3.4 model units on a 6.3-unit body |
| 1001 + its toon prop, `-ShotView upper` | 2 314 vs a 1 458 floor | 1.6x, reaching left of the noise box - the hand end |

A render diff below the noise floor is not evidence: measure two identical runs before believing any diff on this
harness.

### Classifying nothing from live bone positions

`AttachAccessory` retargets every skin it can by name and falls back to a hand anchor only when a prefab shares
*no* bone name with the character at all. The reason is that the viewer's character is always animated, so its
bones sit wherever the idle put them while a prefab's are at rest, and any measurement of "own space" against live
bone positions flips as soon as the clip moves an arm. Anything that compares a prefab to a character does it in
rest pose, or not at all.

## Umamusume Explorer: what it is, and what there is to lift

[Repo](https://github.com/MarshmallowAndroid/UmamusumeExplorer) ·
[README](https://github.com/MarshmallowAndroid/UmamusumeExplorer/blob/master/README.md)

A WinForms **browser**, not a 3D tool: file browser with search and export, an audio player with export, a
live-music player with export, a race-music simulator, and character info (stats/skills read from `master.mdb`).
It reads bundles with **AssetsTools.NET** and CRIWARE audio with **vgmstream**. There is no model loading, no
shader work, no rigging, and no Unity in it. Its file browser lists **the whole manifest tree**, so every path in
the game appears there, including the ones this viewer never fetches: it shows names the UI never asks for, not
assets the database lacks.

**Nothing worth lifting.** Its manifest layer (`UmamusumeData/ManifestEntry.cs`, `ManifestEntryKind.cs`,
`AssetBundleGroup.cs`, `DataDirectories/{Dmm,Steam,Default}DataDirectory.cs`, `Assets/EncryptedAssetStream.cs`) is a
second implementation of what this repo already has (`Scripts/UmaDatabase/*`, `Scripts/AssetBundleDecryptor.cs`),
and this repo's is the one wired into a renderer. `ManifestEntryKind` is only
`Default/AssetManifest/PlatformManifest/RootManifest/Master/Sound/Movie/Font`, so there is no richer taxonomy to
borrow either. The only file that could pay off is `UmamusumeExplorer/Pages/FileBrowserControl.cs` - whether it
groups paths by prefix in a nicer way than flattening them.
