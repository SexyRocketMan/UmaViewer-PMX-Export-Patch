# Handoff: the advanced morphs work

Everything this session built, what is settled, what is open, and how to pick it up cold. Two repositories are
involved:

| repository | branch | what it is |
|---|---|---|
| `C:\Users\user\Documents\dev\UmaViewer-PMX-Export-Patch` | `feat/extra-morphs` | the Unity viewer fork and its PMX exporter |
| `C:\Users\user\AppData\Roaming\Blender Foundation\Blender\5.2\extensions\user_default\uma_addon` | `feat/morph-drivers` | the Blender addon |

Current export: `C:\Users\user\Documents\umaviewer_exports\morphs\1001_morphs23.pmx` (281 morphs, 27 materials).
A shaded character for inspecting the eye material lives beside it as `Special_Week_Looper.blend`.

## What "advanced morphs" are, and where each one stands

The game drives five facial effects outside its vertex morphs (`docs/EXTRA_MORPHS.md`). The mechanism that makes
them portable is the **collapse + deploy morph**: every decal part is written collapsed onto one point inside the
head (zero-area triangles, invisible) and one vertex morph per part puts it back, so MMD rests hidden and weight
1 is the deployed part. Three UV-morph stacks carry the animated ones.

| effect | MMD / pmx | Blender (addon) |
|---|---|---|
| cheek blush | `Blush` deploy morph, baked blush maps `<map>_blush.png` | decal material blended, no shadow, maps clipped |
| eye manga ×4 ×2 eyes | `@_@_L` … `o_o_R` | decal material rules |
| `@_@` spin | `@_@_L_spin1..7` UV morphs | `Spin_Speed` (turns/s) + `Spin` offset |
| animated tears ×8 | `TearDrop1_L` … `TearDrop4b_R`, each with `_scr0..6` | `Tear_Scroll` (sheet frames/s), texture scroll |
| static tears | `Tear_L`, `Tear_R` | decal material rules |
| tear-eye highlights | **not in the pmx** (see below) | **in the shader library** + properties |

Advanced morphs have their own opt-out (`Advanced morphs` scene switch, default on) and their own operator,
`Set Up Advanced Morphs`, which runs the whole step without a Shading pass.

## The highlights: what is settled, what is not

**Settled** (documented in `docs/UMA_EYE_SHADER.md`, which is the document to read first):

- the game composites **three** highlight maps (`eyehi00/01/02`) over the eye; the viewer's own eye shader is
  static, the game's is not
- the decompiled game eye shader (`croakfang/UmaMusumeMME`, `UMA_Eye.fx`) shows the highlight UV is rotated per
  layer about a centre (`uv - 0.5 + param.xy`, `sincos(param.z)`), and each layer is **thresholded**
  (`tex.r * intensity - _Limit`, `saturate`) and added — not alpha blended
- in Blender the threshold is two `GREATER_THAN` nodes at **0.5** fed by `eyehi00`/`eyehi01`; the third layer has
  no threshold (matching `_HighParam1[2].w = 0`)
- **in-engine footage says the teary state is that threshold dropping** (highlights grow), possibly with a
  wobble on the value. There is no intro swirl. The rotation machinery exists but is not what the teary state
  does.

**Not settled** — the two numbers the game keeps in its own code: how far `_Limit` drops, and the wobble's rate
and amplitude. Also unknown: whether the shipped `charactertooneyet.shader` differs from the MME port.

**Where the controls are**: `MorphDrivers.build_highlights()` creates `Hi_Limit` (0.5), `Hi_Throb` (0),
`Hi_Jitter_Rate`, plus the rotation path (`Hi_Swirl`, `Hi_Jitter`, defaulted to 0) on the character root empty,
and drives the threshold nodes and the `UMA Hi N` rotate nodes.

**The shader libraries are patched, deliberately**: `operators/Umashaders-0.2.6.blend` (`Uma Eyes`, three
`UMA Hi 0..2` rotate nodes) and `operators/SporkShaders.blend` (`Spork Eye`, two). They carry **no drivers** —
the motion is the addon's. `Tools/patch_eye_masters.py` re-applies the nodes (backs each library up as `.bak`
first, skips an already-patched file). If a shader update ever lands, re-run it.

## The shipped shader's values: measured, and what is left

`Tools/headless_export.ps1 -DumpShaders` **is wired and has been run** (the call sits in the Export stage right
after the `ProbeMorphs` block; the prompt for it is:

```powershell
.\Tools\headless_export.ps1 -Char 1001 -Costume 00 -DumpShaders -Out C:\tmp\x.pmx
```

A run prints `[shaders]` lines to the log and, since the display filter includes them, to the console. It prints
the **whole** property list of the loaded eye/face/cheek materials rather than a guessed subset, then
`chara_shader_effect`, then a scan for material animation curves.

This is the game's own data, not a port's: `UmaContainerCharacter` swaps the material's shader for the shipped
compiled shader out of `shader.a` (`UmaAssetManager.EyeShader`), so names, ranges and values are real. What was
learned is written up in `docs/UMA_EYE_SHADER.md` under "Measured". In short:

* `_Limit` is real, is **one** scalar, and the normal value is **0.5** (the MME port declares 0.1).
* The shipped shader has **no** `_HighParam*`/`_MainParam*` — those are the port's own interface. So "the third
  layer has no threshold" was describing the port, and the 0.5 the addon thresholds at was right.
* There is a `_WetTex`/`_WetRate` pair on the eye that nothing in this repository uses, and 8128 asset names
  contain `wet` (every texture has a wet variant — the rain/wet look). Separating "wet look" from "tears" is
  the first thing to settle before using this as the tear lead.
* `chara_shader_effect` is `none` on both head and body for 1001, so `CharaShaderEffectData` (the
  `_UVEmissiveScroll` driver, the animated-tear mechanism) is not where the highlight numbers live for her.
* The first motion scan sampled 8 `campaign/bg` bundles (7 clips, 0 material curves) — a useless sample, because
  alphabetical order starts at campaign backgrounds. It now selects character motions (`chara` in the name) and
  logs each bundle's clip count, so the next run actually tests the character side.

**The decision this was meant to inform:** the material has now given everything a material can give. A material
asset carries static values only, so nothing in it drops `_Limit` in the teary state — that is code
(`MaterialPropertyBlock`, the pattern the viewer's own `Director.cs` uses for rim) or a keyed animation. And the
decompile route is confirmed blocked: there is **no `global-metadata.dat`** anywhere under
`C:\Users\user\Umamusume` (only `GameAssembly.dll`, 179 MB, and `il2cpp_data\Resources`), so the IL2CPP metadata
needed to name the method that sets `_Limit` is simply absent. AssetStudio/AssetRipper over the material and
animation bundles stays the next tool — still not installed.

A reminder on what a shipped shader can and cannot give: the game's shaders are *compiled* assets in `shader.a`;
properties and defaults are readable, **source is not**. The decompile remains the source of truth for logic.

## Known gaps and next steps, in order

1. **DONE — the `-DumpShaders` call site is wired and run.** See "The shipped shader's values" above for what it
   found. The AssetStudio question is answered as far as this tool can answer it: the material holds nothing
   more, and the IL2CPP route is confirmed blocked (no `global-metadata.dat`).
2. **DONE — the `Hi_*` rows are in the Morphs panel** (`Morphs` panel, an "Eye highlights" box: `Hi_Limit`,
   `Hi_Throb`, `Hi_Swirl`, `Hi_Jitter`, `Hi_Jitter_Rate`, with a single info row when the step has not been run).
3. **Next real question: what sets `_Limit` in the teary state?** Three cheap candidates, cheapest first:
   (a) a facial morph binding the property — `-ProbeMorphs` prints every `OtherMorphs` bind with its
   `property=` name, so grep that log for `_Limit`/`_Wet`/`_High`; (b) a material animation curve — the motion
   scan says no for 8 character motions, so this needs a wider or targeted sample before it is called dead;
   (c) code. Also worth a look, independent of tears: **`_WetTex` has no matching texture anywhere** — there are
   0 asset names containing both `wet` and `eye`, while face/hair/body have thousands — so whatever the game
   assigns to it is not part of the wet-texture-variant system and is worth identifying on its own.
4. **Normals donor retained morphs** (parked by the user, design agreed): the static face copy used for the
   normal transfer should *keep* selected shape keys (`Eye_EyeHideA_L/_R` first) and drive them from the real
   face's keys, instead of masking the transfer. The Vertex Group field of the Data Transfer modifier does not
   mask normal transfers. Hooks: `FaceNormals.split_face_into_object`, `prune_dead_shape_keys`,
   `remove_shape_keys`.
5. **Parked deliberately**: `Shade_Ctrl` (`_faceShadowAlpha`; needs the face-region port to have an input),
   vmd/recording of the advanced morphs (the sampler design is in `EXTRA_MORPHS.md`, judged not worth it yet),
   eye-highlight UV morphs in the pmx (superseded by the shader route), and **the tear card's roll** - it was
   built, verified and then taken back out: twelve morphs for one effect, animated only by an addon-side crossfade,
   so in plain `mmd_tools` it is dead weight. The measurement and the reason are in `EXTRA_MORPHS.md` under
   "Shelved: exporting the roll"; if it is ever revisited, the carrier to use is bones plus a VMD, not morphs.
6. **Robustness pass, in progress.** A reviewer's audit of both repositories is in
   `C:\tmp\pipeline_robustness_review.md` (not in the repo - copy it in if it is still wanted after a reboot). What
   it found and what has been fixed since:
   * **fixed** - the two export switches now each govern their own feature: `PmxExtraMorphs = false` stops the tears
     being written at all (they used to ship collapsed and undeployable) and `PmxUmaMaterialFields = false` no longer
     drops the `morph=`/`blend=`/`scroll=` tokens, which belong to the morphs and not to the shader values;
     `-NoExtraMorphs` exposes the switch in the headless runner;
   * **fixed** - decals did not follow the head at all (a decal is a *child* of a bone, so `bones.IndexOf(...)` was
     -1, and the tear card's joints are not pmx bones so its skin indices resolved to 0); morphs renamed
     `<feature>_<what it does>_<side>` with the old spelling still accepted; duplicate tear cards deduplicated on
     geometry; the panel finds the character from any selected part (root, mesh, armature, the separated face);
     `report_export` and the deploy rows look at every mesh rather than the first; the advanced-morph step runs
     *after* the materials exist, so eye highlights work on the first Shading run; each character's drivers are
     scoped to her own materials and claimed once; the pmx index widths are computed instead of hardcoded to 2
     bytes; `FindHeadBone` matches loosely and warns; a failed export puts the model back (try/finally); a Shading
     run covers every mesh of the character, skips a bad slot instead of aborting mid-way, and re-verifies the
     shader control afterwards.
   * **investigated, not a bug on 5.2** - "a driven shape key loses its driver when the face is separated". Measured
     two ways: a plain `frame * 0.01` driver on an eye key survives the split and still evaluates (0.370 at frame
     37, before and after), and a driver whose variable reads a key of its *own* Key follows the *piece's* key, not
     the other piece's - so `mesh.separate` carries and re-points driver data correctly on Blender 5.2. The add-on
     keeps a cheap check instead (report any driver that does not resolve, right after the split); a repair was
     written, A/B-tested, found to change nothing, and taken back out.
   * **still open** - the static face copy is keyed on the face object's *name*, so a rename multiplies copies;
     `Alt+D` duplicates share one set of drivers; the exporter's
     unreadable-mesh recovery double-counts `baseShift`; normals are never transformed while coordinates are;
     `build`/`build_material_visibility`/`build_cheek`/`verify` in `MorphDrivers.py` are dead code (and the only
     pre-4.2 `material.blend_method` uses); the `@_@` spin's UV-morph V-sign is still unmeasured.
   * **the pooled tear card "moving forward"** - checked and not reproducible in the data: the card's absolute
     coordinates are identical in every export from `morphs13` (when the tears appeared) through `morphs33`
     (`Y 17.405…17.587`, `Z −0.468…−0.455`), and its drawing state is identical too (BLENDED, no culling, repeat,
     same texture). `DecalLift = 0.06` is proportional to the distance from the head bone, but the pool card gets
     0.086 of it against the manga's 0.097 - it is not the outlier. The card that used to be weighted to bone 0 now
     follows the head, so a decal that was previously stuck in the wrong place is visible where it belongs; that is
     the most likely explanation for the change in appearance.
7. **Merges**: `feat/morph-drivers` → addon `main`, `feat/extra-morphs` → integration. Two unrelated dirty
   file sets in the fork (rain/wet look, VMD loop lead-in) belong to the user.

## Pitfalls that cost time this session

* **Panel `bl_idname` collision**: a new panel taking an existing `SCENE_PT_umaaddonpanel5` silently ate the
  shading panel. New panels use their own id (`SCENE_PT_umaaddonpanel_morphs`).
* **`Tools/headless_export.ps1` echoes only what its allowlist matches.** New `Debug.Log` prefixes are invisible
  on the console even when the run succeeds — the `[shaders]` lines were there in the log all along. Add the
  prefix to the `Select-String` pattern at the bottom of the script, or grep the log file.
* **PowerShell here-string replacements fail silently** on these files (line endings), and one of them truncated
  `ModelExporter.cs` to zero bytes. Prefer a small Python script with `assert` on every anchor, or the `edit`
  tool; check `Get-Item … Length` after a scripted write.
* **Scripted insertion inside a method body** (inserting a method after a signature line, or a call between an
  `if` and its `{`) compiles into nonsense — insert *before* the doc comment, or after the whole block.
* **The stale-bake trap**: derived textures (`<map>_blush.png`, `<map>_mask.png`) are written into the shared
  `Texture2D/` folder and must be **rewritten every export**, or an older bake ships.
* **`bmesh.ops.duplicate`** with faces+verts+edges in `geom` throws; pass faces only.
* **mmd_tools and UV morphs**: they import (`mmd_root.uv_morphs`, as vertex groups) and can be viewed/applied by
  the UV Morph operators, but they never deform on their own — Blender cannot animate a UV layer.

## Fast orientation

* `docs/EXTRA_MORPHS.md` — the game-side inventory, the deploy-morph idiom, the tear scroll, the open questions
* `docs/PMX_EXTENSIONS.md` — the comment grammar and the deploy/UV morph tables
* `docs/UMA_EYE_SHADER.md` — the eye shader ground truth, uniforms and maths
* `docs/MORPHS.md` — the original analysis of the five non-vertex morphs
* `ModelExporter.cs`: `DeployMorphs`, `SpinMorphs`, `TearMorphs`, `MorphPartRenderers`, `MorphPartTextures`,
  `BakeBlush`, `BakeHighlight`
* addon `MorphDrivers.py`: `decal_materials`, `build_spin`, `build_tear_scroll`, `build_highlights`,
  `setup_advanced_morphs`, `report_export`
