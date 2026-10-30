# The game's `3d/chara` families: what we load, what we never touch

Research note, no implementation. Written because a member of the project pointed at **Umamusume Explorer** and said it
exposes more assets under `Root/3d/chara` than our viewer does — `attach`, `body`, `common`, `head`, `mini`, `prop`,
`richprop`, `sweat`, `tail`, `toonprop` — and asked whether we are missing anything.

Short answer: **we are not missing the assets, we are missing the use of four of them.** Every one of those names is
already in the viewer's hands at startup; nothing in our code ever asks for four of the families.

## What the viewer actually has in memory

`UmaViewerMain.cs` (`LoadDatabase`) builds the character list as "every `_3d_cutt` manifest entry whose name starts with
`3d/chara/`":

```csharp
var chara_3d = AbList.Where(ab => ab.Value.Type == UmaFileType._3d_cutt).Select(ab => ab.Value).ToList();
AbChara = chara_3d.FindAll(ab => ab.Name.StartsWith(UmaDatabaseController.CharaPath));   // "3d/chara/"
```

`AbList` is the whole manifest, so `AbChara` holds **all ten families** — the prefix is the only filter. What differs
is which of them any code path reads.

## Per family

| family | what our code does with it | where |
|---|---|---|
| `body` | the main character prefab (`pfb_bdy{id}_{costume}`) | `UmaDatabaseController.BodyPath`, `UmaViewerBuilder` |
| `head` | head/face prefabs (`pfb_chr{id}_{costume}…`) | `UmaDatabaseController.HeadPath` |
| `tail` | tail prefabs and their textures | `UmaViewerBuilder` (`3d/chara/tail/{name}/`) |
| `mini` | the chibi build: `mini/body`, `mini/head`, `mini/tail` | `MiniBodyPath`, `UmaViewerBuilder` |
| `common` | **partly**: `common/textures/tex_chr_tear00` and `common/tear/tear000|001/pfb_chr_tear000|001` | `UmaContainerCharacter.cs:946`, `:1642-1643` |
| `prop` | **exposed by a name guess**: the UI's prop panel lists `Main.AbList.Where(a => a.Key.Contains("pfb_chr_prop") && !a.Key.Contains("clothes"))`; the CLI's `-umaProp <substring>` resolves *any* prefab name in the whole `AbList` | `UmaViewerUI.cs` (`LoadPropPanel`), `Editor/UmaHeadlessExport.cs` (`FindPropEntry`) |
| `attach` | nothing. (The 113 `attach` hits in `Assets/` are the verb and the tear bones `Eye_tear_attach_01_L`…) | — |
| `richprop` | nothing. `grep` across `Assets/**.cs`: **0** references | — |
| `toonprop` | nothing. **0** references | — |
| `sweat` | nothing. The one `sweat` hit is `LiveTimelineDefine.SweatLocator = 59`, a live-camera locator enum | — |

## Measured: the gap is a filter per family, not one substring

The guess below - "they are probably called `pfb_chr_<family>*`, so the fix is a wider substring" - was worth
forming and is **wrong in its detail**. `headless_export.ps1 -ListFamilies` now prints, from the manifest alone,
what each family holds and what its prefabs are called (digit runs masked, so 22 857 body entries collapse to
the handful of forms a family is really built from):

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

Not one of the four unused families is named `pfb_chr_prop*`. Three of them *are* prefab families a prop panel
could reasonably show, under their own prefixes:

* **`attach`** (321 entries) - real models, and the richest find: they ship their own `_wet`/`_dirt` texture sets
  exactly like body textures do, i.e. they are shaded parts of a character, not metadata. `pfb_attach####_##_…`.
* **`toonprop`** (153) - `pfb_toon_prop####_##`, plus `ast_toon_prop…_shader_effect_data` siblings, which is
  shader-driven prop data of the kind the outline/effect work cares about.
* **`richprop`** (3) - `rich_prop1001_00`, `rich_prop1002_00`, `rich_prop1005_00` and nothing else. Three assets
  is not a family worth a panel section, but it is worth knowing the family is that small before building for it.

**`sweat` is not a prop family at all**: all 503 entries are `…_sweatlocator` (or `…_face###_sweatlocator`)
prefabs - locators, the same notion as `LiveTimelineDefine.SweatLocator = 59`, i.e. points the sweat effect is
placed at. They are empty-of-geometry by name, and they do not belong on the prop panel.

So the real shape of the fix is **a filter that knows the prefix per family** (`pfb_attach`, `pfb_toon_prop`,
`pfb_rich_prop` - and deliberately not `sweatlocator`), not one wider substring. That still means the work is a
panel predicate plus, at most, a cost question about opening bundles - no new loader and no new manifest stage.

## The guess that preceded the measurement

Kept because the way it failed is the useful part: the family *folder* name is not part of the prefab name
(`richprop/rich_prop1001_00/pfb_rich_prop1001_00`), so a filter derived from folders would have missed all
three families it was meant to reach.

The prop panel filters on **`pfb_chr_prop`**, not on the folder. If the four unused families' prefabs had been
named `pfb_chr_richprop*`, `pfb_chr_toonprop*`, `pfb_chr_sweat*`, `pfb_chr_attach*` - plausible, and unverified -
then they would never match that substring, so the panel would show nothing for them while the bundles sit
loaded in `AbList`.

## Verification first (cheap, no new loaders)

1. **DONE — enumerate, do not guess.** `-umaListFamilies [filter]` in `Editor/UmaHeadlessExport.cs`, with
   `-ListFamilies` / `-ListFamiliesFilter` on `Tools/headless_export.ps1`, groups `AbChara` by the path's second
   segment and prints, per family: entry count, prefab count, the naming forms (digit runs masked) and three samples.
   Metadata only — it reads the manifest and opens no bundle, so `Config.DownloadMissingResources` cannot fire. This
   is where the table above comes from; it runs in about the time any headless run takes to boot (the run itself is
   milliseconds). Naming note: `-umaListFamilies` rather than the `-umaListChara` first written down, because
   `-umaListChars` two lines above it lists *characters* and the two would have been confused for each other forever.
2. **Sample with what exists today.** `-umaProp pfb_attach1048_00_0001_00`, `-umaProp pfb_toon_prop1001_00`,
   `-umaProp pfb_rich_prop1001_00`: `FindPropEntry` searches the whole `AbList` by substring, and its ambiguity
   warning prints sibling names, so this is already a way in. It *loads* what it finds, though, and
   `Config.DownloadMissingResources` is `true` — an absent bundle can trigger a download (the VPN caveat in
   `Config.json`). **Not run yet**; it is the check that says whether these three families load at all, and it is the
   one that would make a widened panel predicate safe to trust. Deliberately not run in the session that wrote the
   dump, because the dump had already answered the question it was for.
3. **DONE — decide per family.** `sweat` is settled by name: 503 `…_sweatlocator` prefabs, no geometry, so it
   stays out. `attach` is settled the other way: 321 entries with their own `_wet`/`_dirt` texture sets means real
   shaded models — confirmed by loading one (below). `toonprop` and `richprop` load too; `richprop` is three
   assets, so it is on the panel because it costs one line, not because anyone will browse it.

## What the families turned out to be, and where each one lives now

Loading them changed the plan twice, which is the point of loading them.

| family | what it actually is | where it lives now |
|---|---|---|
| `prop` | 1 194 standalone props | prop panel (unchanged) |
| `toonprop` | **one per character** (140 folders, 145 prefabs, `toon_prop{id}_{n}`), a partial rig - `Root` → `ArmRoll_R` → `Wrist_R` → fingers, 21 bones, local coordinates. A held/effect object | prop panel |
| `richprop` | three assets (`pfb_rich_prop1001/1002/1005_00`), character-sized | prop panel |
| `attach` | **not props at all** - see below | character attachments |
| `sweat` | 503 `…_sweatlocator` prefabs, no geometry | nowhere |

`UmaViewerUI.LoadPropPanel` matched the literal `pfb_chr_prop`; it now matches one prefix per listed family
(`pfb_chr_prop`, `pfb_toon_prop`, `pfb_rich_prop`), skips `clothes`, sorts by path so a family's entries sit
together, and puts the family name in each entry's search keywords (`richprop`'s prefabs are `pfb_rich_prop*`).
The panel goes from 1 194 entries to **1 342**; `attach` and `sweat` are deliberately not in it.

### `attach` is a character accessory, and it says so itself

`pfb_attach1048_00_0001_00` exports as **830 verts / 2 928 indices / 76 bones**, and the bone list is the answer:
`Position` → `Hip` → `UpBody_Ctrl`/`Waist` → `Spine`/`Chest`/`Neck`/`Head`, both arms with all fifteen finger bones,
`Hand_Attach_L/R`, `Sp_Ch_Bust0_L/R`, plus animator leftovers (`M_Attach`, `Wrist_*_Pole`, `Wrist_*_Target`) - a
**copy of a character rig**, at that character's rest pose, with the accessory geometry skinned to it. So the game's
job when spawning one is a name-based retarget onto the character's own bones, which is exactly what
`UmaContainerCharacter.MergeBone` already does for hair.

`UmaContainerCharacter.AttachAccessory` does that, `UmaViewerBuilder.LoadAttach` loads and wears one,
`UISettingsModel.LoadAttachPanel` offers them as toggles in the materials panel (28 for character 1048, which is
the deepest set), and `-umaAttach <pattern>` does it headlessly. Only **ten characters** ship the family
(`attach{id}_00/`: 1048, 1081, 1112, 1113, 1128, 1132, 1140, 2002, 2003, 2007), even though the id is a character
id - 1132 and 1140 have no costume 00 in the costume list, so the `_00` is the set index, not the costume.

Measured on 1048 + `pfb_attach1048_00_0001_00` against the same character bare:

| | bare | worn | difference |
|---|---|---|---|
| vertices | 15 767 | 16 597 | **+830** - the prefab's own count, exactly |
| indices | 89 022 | 91 950 | +2 928 |
| materials | 27 | 28 | +1 (`mtl_attach1048_00`) |
| textures | 31 | 35 | +4, and `Texture2D/` gains the wet/dirt twins |
| bones | 425 | 427 | **+2** - the accessory's own holder nodes (`pfb_attach1048_00_0001_00`, `M_Attach`); every rig bone it carries was retargeted, none came along |
| rendered pixels | - | - | **inside the noise**: 628 px change by more than 12/255 against a 746 px floor from two *identical* bare runs. The user's own look settled it: the attach set repeats geometry the body already draws, so it makes no visual difference |

The export is the evidence here, not the image. Two things had to be learned the hard way to say that honestly:
a **render diff below the noise floor is not evidence** (measure two identical runs before believing any diff on
this harness), and **`Renderer.bounds` is stale in the frame a hierarchy changes** - the numbers it reported for a
just-parented accessory were the pre-move ones, which is why the geometry was checked through the export
(`pmx_inspect.py bounds`) instead.

Three traps this cost, all worth keeping:

* **`Destroy` is too late.** The exporter counts every transform under `Position`, and a deferred `Destroy` leaves
  the released prefab bones alive until the end of the frame - so an attach applied during the settle stage exported
  **500 bones instead of 425**, the prefab's whole rig coming along as duplicate names. Releasing them with
  `DestroyImmediate` fixes it; the same trap applies to anything that alters a hierarchy and then measures it in
  the same frame.
* **Prune before the retarget captures nothing, prune after it misses the point.** The retarget reparents the
  prefab's bones onto the character, so a walk of the prefab's tree afterwards no longer sees the leftovers it
  should remove (measured: 9 extra bones, then 4). The working order is: capture the prefab's nodes and what
  carries renderers *before* the retarget, retarget, then remove the captured nodes that are neither a renderer
  holder nor still pointed at by a skin.
* **Retargeting is not pruning.** Prefab nodes nothing points at any more (the bust chains, the IK pole/target
  nodes) are dead weight that the export would report as extra bones, which is why the capture above exists.

## `toonprop` is two things in one family

Loading two of them settled why the family looked inconsistent: they are not one kind of asset, and one placement
rule cannot serve both.

| prefab | what its weights say | what it is |
|---|---|---|
| `pfb_toon_prop1048_00` | `Waist` 384.9, `Chest` 338.1, `Hip` 276.3, `Thigh_R/L` ~150, knees, elbows, 2 126 verts | a **full-body toon costume** |
| `pfb_toon_prop1001_00` | `Root` 102.3, `Wrist_R` 52.7, `ArmRoll_R` 36.9, every right-hand finger, 489 verts | a **hand-held object** |

The signature that separates them is measurable before anything is retargeted: **how far the prefab's bones sit from
the character's bones of the same name**, in model units.

| prefab | rig offset from the character | body height | verdict |
|---|---|---|---|
| `pfb_toon_prop1048_00` | **0.03** | 6.3 | same space - it was authored on the character, so retarget it |
| `pfb_toon_prop1001_00` | **1.26** | 6.3 | its own space - its rig is a *flattened* arm around its own `Root`, so hang it off the hand |

`UmaContainerCharacter.AttachAccessory` measures that and does the matching thing (`<= 5% of body height` means
retarget, otherwise rigid anchor to `Hand_Attach_R`/`L`, picking the side from the bones the skin names). Both
outcomes were measured through `pmx_inspect.py bounds` (per-material world box), against the alternative:

| case | placement | material box (model units) | verdict |
|---|---|---|---|
| 1048 costume, retargeted | wraps the body | centre (−0.07, 9.79, 0.56), size 12.34 × 19.49 × 3.13, body is 12.75 × 16.82 × 3.04 | **right** |
| 1048 costume, rigid at the hand | hangs off to the side, above the head | centre (−11.24, **19.21**, 1.48), y 10.97..27.46 | wrong |
| 1001 hand object, rigid at `Hand_Attach_R` | compact, next to the hand | centre (−7.01, 10.58, 0.75), size 3.41 × 3.08 × 1.35, hand bone at (−5.49, 11.90, 0.87) | **right** |
| 1001 hand object, retargeted | stretched from the hand to the floor | size 6.48 × **12.96**, y −0.36..12.61 | wrong - Root's weights stay at the prefab's origin while the hand's weights follow the hand |

Rendered, against the harness's own noise floor (two identical bare runs differ by **746 px** at >12/255):

| change | pixels changed | reading |
|---|---|---|
| 1048 + attach only | 1 101 | 1.5x noise - and the user confirmed the attach set repeats geometry the body already draws |
| 1048 + toon costume | **5 092** | 6.8x noise, clearly visible |
| 1001 + toon hand prop, full-body view | 571 | **below** the noise floor; the prop is ~3.4 model units on a 6.3-unit body |
| 1001 + toon hand prop, `-ShotView upper` | 2 314 vs a 1 458 floor | 1.6x, and the changed region reaches left of the noise box (x 250..912 against 366..912) - the hand end, which is where the anchor puts it |

Both toonprops are offered in the attachment panel alongside the attach sets
(`UmaViewerBuilder.AttachEntriesFor` covers `attach{id}_*` and `toon_prop{id}_*`), and `-umaAttach <pattern>` takes
either. A character with several toonprops can toggle more than one on; nothing prevents overlap, which is worth
knowing before reporting "two props in the hand" as a bug.

## Umamusume Explorer: what it is, and what there is to lift

[Repo](https://github.com/MarshmallowAndroid/UmamusumeExplorer) · [README](https://github.com/MarshmallowAndroid/UmamusumeExplorer/blob/master/README.md)

A WinForms **browser**, not a 3D tool: "File browser with search and export", an audio player with export, a live-music
player with export, a race-music simulator, and character info (stats/skills read from `master.mdb`). It reads bundles
with **AssetsTools.NET** and CRIWARE audio with **vgmstream**. There is no model loading, no shader work, no rigging,
and no Unity in it.

That explains the observation exactly: its file browser lists **the whole manifest tree**, so every path in the game
appears there — including the ones we never fetch. It is not showing assets our database lacks; it is showing names our
UI never asks for.

**Nothing worth lifting for our side.** Its manifest layer (`UmamusumeData/ManifestEntry.cs`, `ManifestEntryKind.cs`,
`AssetBundleGroup.cs`, `DataDirectories/{Dmm,Steam,Default}DataDirectory.cs`, `Assets/EncryptedAssetStream.cs`) is a
second implementation of what we already have (`Scripts/UmaDatabase/*`, `Scripts/AssetBundleDecryptor.cs`), and ours is
the one wired into a renderer. `ManifestEntryKind` is only `Default/AssetManifest/PlatformManifest/RootManifest/Master/
Sound/Movie/Font`, so there is no richer taxonomy to borrow either.

If the repo is ever wanted anyway, the only file that could pay off is `UmamusumeExplorer/Pages/FileBrowserControl.cs`
— to see whether it groups paths by prefix in a nicer way than flattening them.
