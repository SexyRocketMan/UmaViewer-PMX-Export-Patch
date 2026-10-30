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
3. **Partly answered — decide per family.** `sweat` is settled by name: 503 `…_sweatlocator` prefabs, no geometry, so
   it stays out. `attach` is settled the other way: 321 entries with their own `_wet`/`_dirt` texture sets means real
   shaded models. `toonprop` (153, plus `ast_…_shader_effect_data`) still needs one load to know what it looks like,
   and `richprop` is three assets — cheap to check, hard to care about.

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
