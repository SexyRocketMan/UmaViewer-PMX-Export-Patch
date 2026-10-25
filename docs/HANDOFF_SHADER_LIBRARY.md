# Handoff — shader library work

Continues from `HANDOFF_ADVANCED_MORPHS.md` (which owns the morph/export side). This one is the **shading library**
side: what shipped, what to build next, and the measurements that keep the next session from re-deriving them.

## Where things are

| what | where |
|---|---|
| export repo | `C:\Users\user\Documents\dev\UmaViewer-PMX-Export-Patch` (branch `feat/extra-morphs`) |
| add-on repo | `C:\Users\user\AppData\Roaming\Blender Foundation\Blender\5.2\extensions\user_default\uma_addon` (branch `feat/morph-drivers`) |
| manifests | `uma_addon/addons/uma_addon/operators/shaders/*.json` (`spork-1.0`, `umashader-0.2.6`) |
| libraries | **the same folder** — `Umashaders.blend`, `Umashaders-0.2.6.blend`, `SporkShaders.blend` |
| settings engine | `operators/UmaShaderControl.py` (`SETTING_BY_KEY`, `_sockets_for`, `_own_driver_paths`, `hook`, `status`) |
| manifest loader | `operators/ShaderManifest.py` — `DIRECTORY`, `path_for()`, **`blend_path()`** (one resolver for every library) |
| apply pass | `operators/Umashader.py` (`Shading`; reuses a slot already tagged `uma_shader_source == manifest id`) |
| morph/decals | `operators/MorphDrivers.py` (`decal_materials`, `build_spin`, `build_tear_scroll`, `setup_advanced_morphs`) |
| panels | `panels/AddonPanels.py` (`MorphPanel`, `SCENE_PT_umaaddonpanel_morphs`) |

`MMR_Rig.blend` deliberately stays in `operators/` — it is the rig metarig template, not a shader library. The two
`.blend.bak` files there are the user's own editor backups, untracked; leave them.

## What shipped this session

| commit | repo | what |
|---|---|---|
| `257ca7b` | add-on | **looper lib**: `Uma Eyes` carries the wet-eye highlight system; `build_highlights`, its five `Hi_*` properties, its call, the highlight block inside `build_tear_scroll` and the panel's "Eye highlights" box are removed |
| `e82b79a` | add-on | `CrySwitch` 1 → 0, so a fresh file rests **not** crying |
| `908ab68` | add-on | library blends moved beside their manifests, read through `ShaderManifest.blend_path()` (also `Motion`/`Properties`, which read `Umashaders.blend` for the action library) |
| `2f5a00a`, `a485693` | add-on | dead pre-4.2 driver builder removed; docstrings corrected |
| `4f26331` | export | the nodes-vs-drivers measurement and the state of the library port, in `HANDOFF_ADVANCED_MORPHS.md` |
| `ab21b98`, `0f552a5`, `7fe85c7`, `67c7536`, `70d0c28` | export | uv-morph stacks dropped, decal clearance along the normal, rolling tear card left out, normal/naming fixes |

**Verified, not assumed:** a fresh Shading pass on `1001_nostream.pmx` yields an eye material with both node groups,
both rotators linked to the chain, all four images bound and the two colour nodes present; node-tree drivers went
300 → 297 (the three dead highlight ones); manifests still load/validate and name files that exist; a full Shading
run reports "Uma shader applied".

## The highlight graph, as it now ships

`Uma Eyes` in `Umashaders-0.2.6.blend`, 39 nodes. Names matter — the manifest and the panel address them:

- `UMA Hi 0`, `UMA Hi 1` — `VECTOR_ROTATE`, `Z_AXIS`, centre `(0.5, 0.5)`. **`UMA Hi 2` does not exist**: the third
  highlight (`eyehi02`) is parked, its overlay mix factor is the texture's own black colour.
- angles ← `Scene Time` → `NoiseGenerator` (`TimeScale` 2, `Gain` 0.15, `Offset` 0) → `Mix.003` (label
  **`CryThreshold`**) and `Mix.005` (label **`UvTremble`**) → `Clamp 0..1` → angle. Both mixes interpose the
  `Value` node labelled **`CrySwitch`** (0 at rest, 1 = crying) via a link, so their `Factor` defaults are inert.
- `SmoothThreshold` ×2 (inputs `Value`, `Threshold`, `Smothness` 0.001) gate `eyehi00`/`eyehi01`.
- `Color`, `Color.001` — RGB nodes (white) tinting the main eye texture and the first highlight.
- image nodes `Image Texture` (eye0), `.001` (eyehi00), `.002` (eyehi01), `.003` (eyehi02) — **unbound** in the
  library, names fixed by the manifest's `eye` recipe.
- `NoiseGenerator` and `SmoothThreshold` are node groups that travel with the material when appended.

## Measurements worth not repeating

- **Drivers are not the framerate problem.** 305 drivers on material node trees cost **0.27 ms/frame** (frame
  stepping 2.11 → 1.84 ms). Render at 640²/8 samples: noise-in-shader 179.7, driver-driven angles 181.5, static
  184.8 ms/frame — indistinguishable. The node route moves work to the per-pixel path; the driver route to the
  per-frame path. **Convert drivers to nodes for self-containment (a library that animates itself, no auto-run), not
  for speed.**
- **What the frame costs:** each material ≈ 2 ms/frame (27 materials → one flat emission: 189 → 130 ms), the
  modifier stack ≈ 39 ms (outline GN shell + data transfer), shape keys ≈ 3 ms; 320→1280 px is only +34 ms, so it is
  a per-material/per-object constant, not fill rate. Levers for the user's 60 fps complaint: fewer material
  instances, the outline shell, viewport TAA (16 by default in that file).
- **The eyebrow trick works**, measured on the user's file with flat materials: brow visible pixels **106 → 970
  (+815%)** with the displacement connected, in EEVEE, `Material.displacement_method = BOTH`. It is *vertex*
  displacement — a 4-vertex plane showed nothing, a 40×40 grid moved — and it is view-dependent by construction.
- **A linked socket wins over a driver on its `default_value`.** That is why `build_highlights` was inert: it drove
  the exact sockets the graph had begun driving by link.
- EEVEE Next compiles a material's shaders lazily, so any timing must warm up (two discarded renders) per
  configuration or it measures compilation.

## Next work, in the order I'd take it

### 1. `Uma Brows` + the `brows` role (the user's brow fix did not appear — this is why)

The trick lives only in their character file. The brows currently match the `face` rule → the shared `default`
template (`Uma Shader`), so putting the displacement there would push the whole body toward the camera. Do:

- in `shaders/Umashaders-0.2.6.blend`: new material `Uma Brows` = a copy of `Uma Shader`'s tree plus
  `Geometry.Incoming → Vector Math (SCALE 0.05) → Material Output.Displacement`, `displacement_method = BOTH`
- manifest `umashader-0.2.6`: `materials.brows`, a `parts` rule `{"id": "brows", "match": ["mayu"], "material":
  "brows"}` placed **before** the `face` rule (first substring wins), and `textures.brows` copied from `default`
- verify by measurement, not by eye: shade a fresh character, flatten every material (brows red, rest opaque black),
  count red pixels with and without the displacement link (`Render Result.save_render`; the Synth Data add-on
  hijacks `scene.render.filepath`)

### 2. Panel rows for the highlight controls (user said go)

A `settings` row addresses a **group socket** today (`_sockets_for` matches `node.inputs[i].name`). The knobs needed
are nodes *inside* the material, so extend the row schema:

```json
{"key": "uma_shader_cry", "kind": "number", "material": "eye",
 "target": {"node": "Value", "socket": "Value"}, "default": 0.0, ...}
```

Rows wanted: `CrySwitch` (0/1 crying), `NoiseGenerator.TimeScale` (tremble rate), `NoiseGenerator.Gain` (amount),
`Color` and `Color.001` (colours). Deliberately *not* exposed: `Offset`, the thresholds, `Smothness`.

Touch points: `_sockets_for()` (return `(path, index)` pairs, not socket indices), `_own_driver_paths()`,
`hook()`, `status()`, the property operators (`INHERIT` seeding/clearing), and the panel loop that draws rows from
`KEYS`/`SECTION_ORDER`/`NUMERIC_KEYS`/`COLOUR_KEYS`. Keep `{"input": …}` working as the shorthand for group sockets.

### 3. Spork's new version

His file is `C:\Users\user\Downloads\SporksShader.blend` — a **character** file (Ikuno Dictus, 1063), not a library.
What matters in it:

- `Simple Diff + Shaded` (51 nodes) and **`Simple Diff + Shaded FACE` (34 nodes, new)**; dependencies
  `ProjectOnPlane`, `Dot Creation`, `Mask for Face`
- new material `mtl_chr1063_00_hair_Alpha0` (transparent hair variant)
- **his eye material `mtl_chr1063_00_eye` is 18 nodes, Emission-only, with exactly our four image node names** — the
  same structure we had before the highlight port, which is why our graph can be dropped in unchanged (user's plan:
  "he uses the same shader for the eyes, so we can copy ours with the new highlight system")

Port: append our highlight eye material (`NoiseGenerator`/`SmoothThreshold` come with it) into `SporkShaders.blend`,
rename it to what the manifest names for `eye`, unbind its four images, `CrySwitch` 0; point `materials.eye` at it;
`textures.eye` already matches node-for-node.

**Trap first:** `spork-1.0`'s `group_hints` is `["Simple Diff + Shaded"]` and shader discovery is a *substring* match,
so `Simple Diff + Shaded FACE` also matches and the panel would drive `Shadow Softness`/`Emission Strength` into a
group that may not have them (the same shape as the 0.2.6 outline-socket bug: it reports, so nothing looks broken).
Make the hint exact, or list the FACE variant explicitly once its sockets are measured.

### 4. Decal morphs react to world light (needs the user's decision)

Decals keep mmd_tools' `MMDShaderDev`, which lights from the **scene's lights**, while Looper lights from the
`Uma Sun` empty with a threshold mask — different lighting models, so decals always look darker than the face under
them. Options put to the user:

1. **Unlit decals (recommended)** — a library `Uma Decal` material: texture × alpha into emission, alpha-blended, no
   lighting. Also what the game does (decals are painted overlays). Gives honest tint/opacity knobs.
2. **Stopgap, no new material** — flatten the decal MMD material in the decal pass (diffuse white, ambient 1, sphere
   off). Looks close to 1, one line of code, no knobs.
3. Shade decals with the face's shader — rejected: it is the route that wrapped manga in outlines (the outline shell
   is a modifier on the mesh, so a decal cannot be excluded per material).

Waiting on the user's pick; `decal_materials()` already knows exactly which materials are decals.

### 5. Static face donor and `Eye_EyeHideA_*` (needs the user's confirmation)

The user's proposal is the design we parked earlier: keep **only** `Eye_EyeHideA_L/_R` on the static copy, prune the
rest, and drive those two from the main face's keys — then the donor deforms like the surface its normals are
transferred onto. Agreed. Notes:

- drive via `SINGLE_PROP` variables targeting the **main face's Key datablock** (`key_blocks["…"].value`), not by
  copying values — measured earlier that this mechanism works across `mesh.separate`
- the donor must keep the same rest topology the Data Transfer pairs against; the keys are identical offsets, so the
  lid deformation matches
- re-assert the prune on every run (a re-shading must not leave a second key behind)
- verify with a measurement, not by eye: angle between the transferred normal and the donor's deformed normal at the
  eye region with the morph at 1

### 6. Optional, from the measurements

Convert `Spin` (Vector Rotate angle) and `Tear_Scroll` (Mapping location) to `Scene Time` chains so the library
animates itself, and stop driving the 20 appearance settings every frame (write them when the panel changes; keep
drivers only while a knob is being animated). Self-containment, not speed.

## Decisions still owed by the user

- decal direction: unlit `Uma Decal` vs flattening the MMD material
- donor: confirm drive-from-the-face
- tremble knobs: rate + amount only (my proposal), or the whole set
- whether `Spin`/`Tear_Scroll` should become node-driven

## Traps that cost time

- `git commit -m` from `pwsh` breaks on quotes/backticks — write the message to a file and use `-F`
- `MorphDrivers.py` is LF, `panels/AddonPanels.py` is CRLF: line-based edits with an explicit EOL, not literal
  multi-line replacements
- the Synth Data add-on overrides the render output path; save through `bpy.data.images["Render Result"].save_render`
- a python reference to a link/node goes stale across a render — look it up again before removing it
- `bpy.data.libraries.load` appending a material does bring its node groups (verified), but the **material** keeps
  `uma_shader_source` and bound images: strip both for a library
- EEVEE timings need a warm-up render per configuration
