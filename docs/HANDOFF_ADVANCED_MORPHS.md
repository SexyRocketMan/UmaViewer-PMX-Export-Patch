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

## The open lead: real values from the shipped shader

`Tools/headless_export.ps1 -DumpShaders` was added for exactly this and **is not wired yet** — the flag parses
and `DumpShaderFacts()` exists and compiles (`Assets/Editor/UmaHeadlessExport.cs`, ~line 1487), but nothing calls
it, so a run does a plain export with no `[shaders]` lines. Fix: insert

```csharp
                    if (options.DumpShaders)
                    {
                        DumpShaderFacts(container);
                    }
```

immediately after the `ProbeMorphs` block (around line 801, after its closing brace), then run:

```powershell
.\Tools\headless_export.ps1 -Char 1001 -Costume 00 -DumpShaders -Out C:\tmp\x.pmx
```

and read `[shaders]` lines. It prints eye/face material properties (`_High*`, `_Limit`, `_Offset`,
`_Highlight*`, `_Show*`, `_Eye*`) with the values the game's own material carries, then scans motion bundles
(capped at 8) for clips that animate a material property — where a keyframed teary threshold would be.

If that returns only defaults, the values are written by IL2CPP code (`MaterialPropertyBlock`, the pattern the
viewer's own `Director.cs` uses for rim), and decompiling that is blocked here (no `global-metadata.dat`). The
next tool to reach for is **AssetStudio/AssetRipper** over the material and animation bundles — not installed.

A reminder on what a shipped shader can and cannot give: the game's shaders are *compiled* assets in `shader.a`;
properties and defaults are readable, **source is not**. The decompile remains the source of truth for logic.

## Known gaps and next steps, in order

1. **Wire the `-DumpShaders` call site** (above) and run it. Decide from its output whether AssetStudio is needed.
2. **Morphs panel rows**: `AddonPanels.py` has a `Morphs` panel listing `Spin_Speed`, `Spin`, `Tear_Scroll` and
   the deploy keys, but not the `Hi_*` properties — a few lines, same pattern.
3. **Normals donor retained morphs** (parked by the user, design agreed): the static face copy used for the
   normal transfer should *keep* selected shape keys (`Eye_EyeHideA_L/_R` first) and drive them from the real
   face's keys, instead of masking the transfer. The Vertex Group field of the Data Transfer modifier does not
   mask normal transfers. Hooks: `FaceNormals.split_face_into_object`, `prune_dead_shape_keys`,
   `remove_shape_keys`.
4. **Parked deliberately**: `Shade_Ctrl` (`_faceShadowAlpha`; needs the face-region port to have an input),
   vmd/recording of the advanced morphs (the sampler design is in `EXTRA_MORPHS.md`, judged not worth it yet),
   eye-highlight UV morphs in the pmx (superseded by the shader route).
5. **Merges**: `feat/morph-drivers` → addon `main`, `feat/extra-morphs` → integration. Two unrelated dirty
   file sets in the fork (rain/wet look, VMD loop lead-in) belong to the user.

## Pitfalls that cost time this session

* **Panel `bl_idname` collision**: a new panel taking an existing `SCENE_PT_umaaddonpanel5` silently ate the
  shading panel. New panels use their own id (`SCENE_PT_umaaddonpanel_morphs`).
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
