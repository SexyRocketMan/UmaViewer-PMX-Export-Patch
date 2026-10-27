# Handoff — options, naming and the release

Written at the end of a long session. `HANDOFF_SHADER_LIBRARY.md` owns the shading library work and is still accurate;
this one is the **exporter/add-on options** side and the state of the release. Read the "Open work, in order" section
first — it is the queue, with the anchors checked against the code.

## Where things are

| what | where |
|---|---|
| export repo | `C:\Users\user\Documents\dev\UmaViewer-PMX-Export-Patch` (branch `feat/extra-morphs`) |
| add-on repo | `C:\Users\user\AppData\Roaming\Blender Foundation\Blender\5.2\extensions\user_default\uma_addon` (branch `feat/morph-drivers`) |
| add-on version | **2.34.0** (`blender_manifest.toml`) |
| exporter CLI | `Tools/headless_export.ps1` + `docs/CLI.md` — **runs from here**, see "How to verify" |
| shader libraries | `uma_addon/addons/uma_addon/operators/shaders/*.blend` beside their manifests |

Both trees are clean apart from the user's own dirty set in the fork (`VMDRecorder/UnityHumanoidVMDRecorder.cs`,
`Settings/UISettingsModel.cs`, `UI/UIShortcuts.cs`, `UmaContainerCharacter.cs`, `UmaViewerBuilder.cs`) and the
untracked `.blend.bak` files the earlier handoff says to leave.

## What shipped this session (2.34.0)

Brows (`Uma Brows` in both libraries — the user's own view-vector displacement, measured 261 → 1657 brow pixels), the
five eye knobs (`CrySwitch`, tremble rate/amount, two highlight colours, as node-targeted settings rows), Spork's eye
ported with an **exact** group matcher (`Simple Diff + Shaded FACE` no longer counts as a shader), the unlit
`Uma Decal` with opacity/tint rows, the face donor following `Eye_EyeHideA_L/_R`, **every material named by role**
(`[face]`, `[decal]`, `[stock]` — `MorphDrivers.base_material_name` sees past the suffix), the face step producing one
donor per character in a multi-character file, and the panel's advanced-morphs box as **checkboxes** with the headings
"Advanced morphs" / "Advanced morphs setup" and a one-way pregnancy row.

**Verified, not assumed:** six verifiers pass (`verify_brows`, `verify_eye_controls`, `verify_eye_panel`,
`verify_spork_library`, `verify_decals`, `verify_face_donor`, `verify_two_copies`), plus `measure_decal_key` and a
`dotnet build umamusume.csproj` at 0 errors.

## Shelved, with the reasons

- **Blush into the face texture.** Tried three ways: a UV transform from the face atlas to the blush map (impossible —
  `Tools/fit_blush_uv.py`, ~42 UV units of residual on a 0..1 map), a bake (works, aligns, but ugly: a 128² multiply
  sprite magnified to 512²), and the clean texture (wraps wrong). The shared-layer idea was right in principle — the
  *layer* is static even though no UV transform is — but the art does not survive the magnification. Libraries and the
  exporter were reverted (`8ff2fb2`, `658140b`, `93c0528`); the attempt is in history (`47df680`…`da3afdd`).
- **The decal black key** (`UMA Decal Key`, keyed alpha by the map's brightest channel). Shipped on by default, then off
  by default when the manga came out pure white — and *removed entirely* in `a14ad5b`, because a default only applies
  when a property is **created**, so a file shaded in between kept `uma_shader_decal_key = 1.0` on its `Uma Sun` and the
  driver kept forcing it. Two regressions from one idea the manga cannot use: their art is black line work.
- **The eye-highlight `_mask` bake** (exporter). Dead end, measured: no material names a `_mask` file in any slot
  (`tex` = 10 = `eye0`, `sphere` −1, `toon` −1) and the add-on thresholds the raw `eyehi00/01/02` in `Uma Eyes`. They
  were in the pmx's texture table only because the bake registered them as dependencies. Removed (`920843e`); a fresh
  export is 35 files with none of them.

## Open work, in order

### 1. `MorphNaming`: drop `Both`, add `Japanese` — **one question first**

`Assets/Scripts/Exporters/MorphNaming.cs`:

- `NamesFor` (line 183) has `case PmxMorphNameMode.Both:` yielding `rawName` then `ShortName(rawName)`;
- `VmdName` (line 211) prefers `UnifiedName` when it fits, else `ShortName`;
- `FitsVmd`/`ByteLength` and `VmdNameByteLimit = 15` are already there (line 24-25, 171, 236) — the Japanese overflow
  rule should reuse them;
- `Config.cs:347` declares `BlenderCompatible = 0, ShortEnglish = 1, Both = 2, Unified = 3` — **explicitly numbered**,
  so `PmxMorphNameMode` persists into `Config.json` as a number.

**The question that decides the size of the job:** is `rawName` already the game's Japanese name (so `Japanese` mode is
literally `yield return rawName;`), or has Unity romanised it before `MorphNaming` sees it (so a Japanese mode needs a
name table that does not exist in the repo)? Settle it by exporting with `PmxMorphNameMode = Blend` and reading one
morph name out of the pmx. Everything below assumes the first answer.

Plan, agreed with the user: **replace `Both` in slot 2 with `Japanese`** rather than appending — slot 2 was unusable for
motion, so an old config that selected it gains a working mode instead of an error, and no other choice shifts. Then:
`VmdName` gains the Japanese pass-through + overflow rule; the four mode descriptions in
`Settings/UISettingsModel.cs:180-212` lose the `Both` text and gain a `Japanese` entry that states the 15-byte /
five-character limit.

### 2. VMD and PMX naming must not diverge

`Exporters/VMDRecorder/UnityHumanoidVMDRecorder.cs`: `VmdUseEnglishMorphNames` is read at 1531, 1536 and 1566, and
`MorphNaming.ModelAndMotionNamesAgree(mode)` exists only to **warn** about the combination of it with
`PmxMorphNameMode.BlenderCompatible` (a model carrying names too long for a vmd field). A motion whose names do not
match the model is silently useless, so: drop the toggle, always
`MorphNaming.VmdName(name, tag, Config.Instance.PmxMorphNameMode)`, and delete the warning path. `Config.cs:51`
`VmdUseEnglishBoneNames` **stays** (nothing else governs bone names and it is what keeps a vmd in step with its pmx) —
relabel it `Bone names: English`.

### 3. Expose `PmxExtraMorphs` in the model panel — the user's on/off test

The field exists (`Config.cs`, read through `Config.Instance.PmxExtraMorphs`) and the exporter guards on it
(`ExtraMorphBlend`, `DeployMorphs`, `ModelExporter` around the morph passes), and the CLI has `-NoExtraMorphs`. What is
missing is the **tickbox** in the model panel, plus its tip string (every other option has a `…Tip` field beside it —
follow that shape). Without it the only way to test advanced morphs on/off is editing `Config.json`.

### 4. APose and other booleans

Agreed position: a checkbox for a boolean, a dropdown for an enumeration. `Export using APose` stays a checkbox unless
it grows a *choice of poses* (A / T / leave rest), in which case promote it. The bone-naming dropdown the user floated
is the same call: two options, so a checkbox labelled `Bone names: English` is clearer than a two-item menu — promote it
if a third naming option ever appears.

### 5. The decal winding defect (shader side, still open)

Right-eye instances of two of the four manga prefabs (`mtl_eff_chr_emo_eye_002.001` Shut, `…_003.001` Wide) and one tear
sheet (`mtl_chr_tear001.001`) have polygons facing **into** the head — `normal · outward = −0.705` / `−0.793` where
their left twins read `+0.705` / `+0.793` — and the exporter lifts every decal **along its own normal**, so those parts
sit behind the face. That is the user's "most manga invisible" and "Wide lost its black rim" (the rim went with the
black key, now removed). Agreed fix, unwritten: flip the winding (and with it the lift direction) for a decal part whose
normal opposes the outward direction; the addon-side stopgap would be the same test at shading time.

### 6. Small ones

- **`_blush.png` still appears.** The guard added checks `tex_name`, but `BakeBlush` names itself from
  `mat_texture.name`, and `duplicateTexture` (line ~209) can leave that empty. Fix: derive the baked name from
  `tex_name` at both `BakeBlush` call sites (`TextureExporter.cs` ~244 and ~291).
- **`tex_chr1001_00_eye0_all.png`** is written and referenced by nothing (42 KB/character). The user said to keep the
  four eye textures; this fifth one is a separate question.
- **Morph-setup guard**: `setup_advanced_morphs` reports `"<root>: the mesh carries no deploy morphs"` for a model that
  predates the advanced morphs — it does not crash, but "old model" and "setup failed" read identically.
- **Decals stay on the body** (the user's call): `split_face_into_object` only moves face+eye polygons, so the cheek and
  tear quads and their morphs remain on the body. They are bone-parented, so they follow the head — that was accepted.

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

`Tools\probe_texture_usage.py <out.txt> <folder-or-pmx>` answers "is this file used" from the pmx's own texture table;
`Tools\probe_decal_normals.py` measures which way each decal part faces.

## Traps that cost time (add to the ones in the shader handoff)

- **PowerShell `.Replace` on source files fails silently** when the anchor's whitespace differs — it bit four times
  today. Use the `edit` tool, or a Python script with `assert` per anchor and an explicit EOL. `pwsh` has no heredoc.
- **`AddonPanels.py` is CRLF**; an inserted block arrives LF-only and leaves mixed endings (569 CRLF vs 575 LF). Normalise
  after inserting, and check the byte counts.
- **`sys.argv` slicing after `--`** must degrade to *empty*, not to `sys.argv[1:]`, or Blender's own flags become the
  script's arguments (`open_mainfile('--background')`).
- **Custom properties are never re-defaulted.** Changing a manifest `default` does not touch a property that already
  exists, so a file shaded under the old default keeps it — that is what made the decal key a second regression.
- **Unity `Debug.Log` from the exporter is invisible** on the headless console unless the wrapper's allowlist matches it;
  everything is in the log file.
- **The parts are `SkinnedMeshRenderer`s.** `GetComponent<MeshFilter>()` returns null for them; read
  `((SkinnedMeshRenderer)renderer).sharedMesh`. This is why the first face-blush bake silently did nothing.
- **`ShaderNodeMix` indexes sockets across all data types** (0 = float factor, 2 = float A, 3 = float B); linking by
  index wrongly is silent. And `vec_a*(1-F) + vec_b*F` means A is the "off" side.
- **`mmd_tools` hands texture names back with a trailing `>`** — normalise before comparing them to file names.
- **The export folder is not cleaned.** A fresh test needs a fresh folder, or leftovers from older settings look like
  current output (the "masks are still there" false alarm).

## Decisions the user has made (do not re-litigate)

Advanced morphs on/off must be testable from the panel; decals stay on the body; the blush is shelved; the black key is
gone; masks are gone; morph naming should be one dropdown including Japanese, with `Both` removed; vmd and pmx naming
must not be independently selectable; booleans stay checkboxes unless they grow variants.
