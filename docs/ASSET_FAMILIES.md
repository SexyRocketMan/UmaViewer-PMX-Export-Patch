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

## The likely real gap: one substring

The prop panel filters on **`pfb_chr_prop`**, not on the folder. If the four unused families' prefabs are named
`pfb_chr_richprop*`, `pfb_chr_toonprop*`, `pfb_chr_sweat*`, `pfb_chr_attach*` — plausible, and unverified — then they
never match that substring, so the panel shows nothing for them while the bundles sit loaded in `AbList`.

That would make the fix a widened filter rather than a new loader, which is worth knowing before anyone writes one.

## Verification first (cheap, no new loaders)

1. **Enumerate, do not guess.** A `-umaListChara <prefix>` switch in `Editor/UmaHeadlessExport.cs` that groups `AbChara`
   by the path's second segment and prints count + three samples per family, for all ten at once. Metadata only — no
   bundle is opened, nothing is downloaded. ~15 lines, next to the existing `-umaListCostumes`/`-umaListProps`.
2. **Sample with what exists today.** `-umaProp 3d/chara/richprop`, `…/toonprop`, `…/sweat`, `…/attach`: `FindPropEntry`
   searches the whole `AbList` by substring, and its ambiguity warning prints sibling names, so this is already a way in.
   It *loads* what it finds, though, and `Config.DownloadMissingResources` is `true` — an absent bundle can trigger a
   download (the VPN caveat in `Config.json`). Prefer the metadata dump first.
3. **Then decide per family.** `sweat` and `attach` may not be character models at all (a sweat/effect prefab set, and
   attachment data/points), which would change whether they belong on the prop panel, in the scene list, or nowhere.

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
