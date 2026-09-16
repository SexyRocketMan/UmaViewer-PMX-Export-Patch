# Tools

Scripts for exporting and verifying models **without the GUI**, so export bugs can be reproduced and
regression tested from a terminal (and from CI).

| File | What it does |
|---|---|
| `headless_export.ps1` | Runs Unity in batch mode, boots the viewer scene, loads a character/costume and writes a `.pmx` - the same code path the "Export Model" button uses. |
| `verify_export.ps1` | Runs headless Blender + `mmd_tools` + `uma_addon` and checks that the export still imports and rigs correctly. |
| `blender_verify_pmx.py` | The Blender side of `verify_export.ps1`. |
| `pmx_inspect.py` | Blender-free PMX inspector/differ (`summary`, `bones`, `weights`, `morphs`, `diff`, `check`). |
| `vmd_inspect.py` | Blender-free VMD inspector and loop validator (`summary`, `loop`). |

## Quick start

```powershell
# list characters and their costume ids
./Tools/headless_export.ps1 -ListChars

# export Special Week, costume "00"
./Tools/headless_export.ps1 -Char 1001 -Costume 00 -Out D:/out/1001_00.pmx

# verify the export the way Blender sees it
./Tools/verify_export.ps1 -Pmx D:/out/1001_00.pmx
```

`verify_export.ps1` exits non-zero on failure and prints `PASS` / `FAIL` per file, so it can be used
as a regression gate. `pmx_inspect.py` needs no Blender or Unity:

```powershell
uv run Tools/pmx_inspect.py check D:/out/1001_00.pmx
uv run Tools/pmx_inspect.py diff a.pmx b.pmx
```

## Props and scenes

Props/scenes export through the same CLI (`-umaProp` takes an asset path or a unique substring), and
`-umaDumpMaterials` reports what their materials resolved to:

```powershell
./Tools/headless_export.ps1 -ListProps home10001        # find scene assets
./Tools/headless_export.ps1 -Prop "3d/env/home/home10001/main/pfb_env_home10001_main000_000" `
    -DumpMaterials -Variant 214 -Out D:/out/home.pmx
```

## Motion recording (one button, no trimming)

`UnityHumanoidVMDRecorder.RecordClipLoop` / `RecordCurrentLoop` record exactly one loop of a clip by
stepping the animation to exact normalized times (`frame / totalFrames`), pinning
`Time.captureDeltaTime` to the frame length so cloth/hair keep advancing one step per frame, and
sampling one vmd frame per step through `SampleFrame()`. That gives:

* a frame count of exactly `clip.length * fps + 1`,
* a last frame that repeats the first pose, so the motion loops without a visible jump.

Sampling in real time (one `FixedUpdate` per frame) drifts against the animator, which is what made
the start and end frames disagree. `SaveVMD` also used to skip the final frame whenever the key
reduction did not divide it; the last frame is now always keyed.

Record and validate headlessly:

```powershell
./Tools/headless_export.ps1 -Char 1001 -Costume 00 -RecordVmd D:/out/loop.vmd -RecordFps 30
uv run Tools/vmd_inspect.py summary D:/out/loop.vmd
uv run Tools/vmd_inspect.py loop    D:/out/loop.vmd     # frame 0 == last frame for every bone
```

Only physics driven bones (cloth, hair) can still differ slightly between the first and last frame -
`loop` reports the worst deviation so that stays visible (`--rotation-tolerance` defaults to 1e-3).



Environment materials are frequently serialized with **no texture at all**, because the game assigns
one of several texture sets at runtime (time of day, weather, event banner). The home screen is the
clearest example:

```
mtl_env_home10001_main000_000_base01   <- prefab bundle: _MainTex = null (12 of 20 material slots!)
tex_env_home10001_main000_212_base01   <- 2048x2048 DXT1, separate bundle
tex_env_home10001_main000_214_base01   <- 2048x2048 DXT1, separate bundle
```

`UmaContainerProp` only instantiated the prefab, so those materials rendered flat white and exported
with whatever texture happened to be first in the texture list. `UmaEnvTextureSet` now resolves the
sets that exist for every textureless material, assigns one (default: the variant covering the most
materials, lowest code first) and exposes `Variants` / `CurrentVariant` / `SetVariant()` so
`UISettingsModel.LoadTextureSetPanel` can offer the choice in the materials panel.

Verified with `-umaDumpMaterials`: NULL `_MainTex` count drops 12 -> 1 (the remaining one is a
runtime mirror reflection that has no texture set), and the exported PMX switches from
`tex_..._000_base00` to the correct `tex_..._212_base01` for all `base01` materials.

## Why the verification exists: the uma_addon morph-name contract

The Blender `uma_addon`'s **Refine Structure** operator
(`uma_addon/.../operators/AddonOperators.py` -> `RefineBoneStructure.fix_eye_shapekeys`) rebuilds the
eye rig: it separates the eyeball geometry, deletes the `Eye_L`/`Eye_R` vertex groups (folding their
weights into `Head`) and replaces them with shape keys plus drivers,
`Eye_L(L)/Eye_L(R)/Eye_L(U)/Eye_L(D)` and `Eye_R(...)`.

Those replacement shape keys are built from *four morphs it matches by exact name*:

```
Eye_20_R(XRange)[M_Face]   Eye_20_L(XRange)[M_Face]
Eye_21_R(YRange)[M_Face]   Eye_21_L(YRange)[M_Face]
```

`AddonOperators2.py` similarly matches `Eye_{n}_{L|R}({tag})[M_Face]`,
`EyeBrow_{n}_{L|R}({tag})[M_Face]` and `Ear_{n}_{L|R}({tag})[M_Hair]`.

So the `(Tag)[Mesh]` suffix that `ModelExporter.AddBlendShape` appends to every baked morph is **part
of the exported file's contract with Blender**, not cosmetic. If a morph is exported without the
suffix the addon finds nothing, creates no eye controls, and the eye bones silently stop deforming
the mesh - while a raw import still looks fine.

That is why `PmxMorphNameMode` defaults to `BlenderCompatible` (keep the suffix) and why shortening
morph names is opt-in:

| `PmxMorphNameMode` (Config.json) | Morph names | Blender/uma_addon | vmd morph matching |
|---|---|---|---|
| `0` BlenderCompatible *(default)* | `Eye_20_R(XRange)[M_Face]` | works | no (vmd names are short) |
| `1` ShortEnglish | `Eye_20_R` | **eye controls break** | yes |
| `2` Both | both of the above | works | yes (larger file) |

`verify_export.ps1 -Mode short|both|blender` asserts the matching expectations.

## Notes / gotchas

* **One Unity per project.** Batch mode refuses to start if an editor already has the project open
  ("project already open in another instance") and the failed run can leave a stale
  `Temp/UnityLockfile`. Close the editor first, or point `-ProjectPath` at a copy.
* The first batch run compiles scripts and reimports assets, so it is much slower than later runs.
* `UmaHeadlessExport` enters play mode, which triggers a script domain reload; it keeps its progress
  in `SessionState` and re-arms itself from `[InitializeOnLoadMethod]`. Without that the runner
  silently hangs - which is what `-umaTimeout` guards against.
* Unity's launcher process can exit before the editor child it spawns, so a zero process exit code
  is not proof of success: check for `[UmaHeadlessExport] OK` / `FAILED` in the log, or the output
  file itself.
