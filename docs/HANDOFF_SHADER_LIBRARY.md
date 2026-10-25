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
| `df8fac2` | add-on | **brows**: `Uma Brows` in `Umashaders-0.2.6.blend` (the user's view-vector displacement, 0.05, `BOTH`) + manifest `materials.brows`, `parts.brows` (`mayu`, **before** `face`), `textures.brows`, `part_inputs.brows` |
| `1d3b896` | export | `Tools/patch_brows_material.py` (adds/re-asserts it in a library), `Tools/verify_brows.py` (structural proof through a real `uma.apply_shader`), `Tools/measure_brows.py` (the pixel measurement) |
| `6ad6c4e` | add-on | **eye knobs**: a settings row may name a node *inside* a material (`target`), the five highlight rows, `uma_shader_role` written on every copy, and `character_materials` counting tagged copies | 
| `d45443e` | export | `Tools/verify_eye_controls.py` (11 drivers on the eye, 298 in the scene, knobs move sockets, bake/re-hook) and `Tools/verify_eye_panel.py` (the panel draws all five rows, checked by recording layout calls) |
| `40c9105` | add-on | **Spork**: his `Spork Eye` is Looper's `Uma Eyes` graph (his own kept as `Spork Eye (Spork's own)`), `textures.eye` rewritten to our node names, five `spork_eye_*` rows, and `_hint_matches` made **exact** so `Simple Diff + Shaded FACE` stops counting as a shader group |
| `8d79fac` | export | `Tools/port_spork_eye.py` (the port, idempotent) and `Tools/verify_spork_library.py` (library + a character shaded with his manifest) |

## What shipped before it (the session this handoff continues from)

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

### 1. DONE — `Uma Brows` + the `brows` role

**Shipped as described below, and it is the user's own fix read out of their file rather than remembered.** Their
brow material (`spe_configured.blend`, `mtl_chr1001_00_mayu [face]`, 23 nodes) is `Geometry.Incoming → Vector Math
(SCALE, 0.05) → Material Output.Displacement` with `displacement_method = BOTH`, in a frame labelled `Eyebrows in
front`. Two details the plan below did not have:

- it is **`Incoming`**, not `Normal` - verified from the link itself (`('Geometry', 'Incoming', 'Vector')`). `Normal`
  pushes the brows out along the face and does not move them against the hair;
- the brow slot also carries the face's **`Toggle If Face` = 1.0** (measured beside `_face` = 1.0 and `_hair` = 0.0),
  so the brows stay a *face* part; the port adds a `part_inputs` row for it, or the brows would silently shade as a
  non-face surface.

The rest as planned, and both halves of it were checked rather than assumed: `mtl_chr1001_00_mayu` is the only
`mayu` material on 1001 (there is no `mayu` texture anywhere - the brows bind the **face** atlas
`tex_chr1001_00_face_diff/ctrl/shad_c/base`, which is why `textures.brows` is the `default` recipe rule for rule),
and the `face` rule lists `mayu` itself, so `brows` has to come first in `parts`.

`Uma Brows` is a copy of `Uma Shader` plus the two nodes and the frame, in a slot on the carrier sphere
(`library_masters` reads roles from those slots), with no images and no `uma_shader_source`. `Tools/patch_brows_material.py`
adds it and re-asserts the graph, so a shader update is repaired by re-running it.

**Verified two ways, on a freshly shaded 1001 (`Tools/verify_brows.py`, `Tools/measure_brows.py`):**

- structurally, through the real `uma.apply_shader`: the brow slot becomes `mtl_chr1001_00_mayu [brows]` with 23
  nodes, displacement linked at 0.050, `BOTH`, the face atlas bound, `Toggle If Face` 1, the shader tag intact, and
  every other slot unchanged (17 checks, all pass);
- by measurement: brows flattened red, everything else black, camera on the brow faces along their own average
  normal, 640² EEVEE, two warm-up renders per configuration - **261 brow pixels without the displacement, 1657
  with it (+535%)**. The user's own file measured 106 → 970 (+815%); the absolute numbers differ because the framing
  differs, the direction does not.

**Two traps met on the way**, both worth not repeating:

- the test has to run the *real* operator: `uma.apply_shader` polls on exactly one selected mesh, and its face step
  **separates the face into its own object**, so the brow slot afterwards belongs to `..._mesh_face`, not to the mesh
  the operator was handed (the first measurement failed with "the brow slot has no faces" for exactly that reason);
- Blender 5.2's engine id is `BLENDER_EEVEE` again, not `BLENDER_EEVEE_NEXT` (that was 4.2-4.4), so a timing or
  render script should pick the id from `scene.render.bl_rna.properties["engine"].enum_items`.

**Why it needed a material of its own** (the reasoning that shaped the change, as it was put before the work): the
brows matched the `face` rule and so wore the shared `default` template - `Uma Shader`, the material every body, hair
and tail slot wears - so putting the displacement there would push the whole character towards the camera.

The checklist it shipped as: `Uma Brows` in `shaders/Umashaders-0.2.6.blend` (a copy of `Uma Shader`'s tree plus
`Geometry.Incoming → Vector Math (SCALE 0.05) → Material Output.Displacement`, `displacement_method = BOTH`);
`materials.brows`, a `parts` rule for `mayu` placed **before** `face` (first substring wins), `textures.brows` copied
from `default`, and `part_inputs.brows` for the face's `Toggle If Face`; verified by measurement with
`Render Result.save_render` (the Synth Data add-on hijacks `scene.render.filepath`).

### 2. DONE — panel rows for the highlight controls

**Shipped**, with the schema exactly as sketched, plus the five rows: `Crying` (the library's `CrySwitch`),
`Tremble rate` (`NoiseGenerator` `TimeScale`), `Tremble amount` (`Gain`), `Highlight colour` (`Color`) and `Second
highlight colour` (`Color.001`), under one new section, `Eye highlights`. `Offset`, both thresholds and `Smothness`
stay internal, as you decided. The table is 25 rows now.

Four things the sketch did not know, all of them load-bearing:

- **a `Value` or `RGB` node is driven through its output**, not an input - `nodes["Value"].outputs[0].default_value`.
  `CrySwitch` and both highlight colours are those node types, so an input-only resolver would have found nothing;
- **groups are addressed by their node tree**, `{"group": "NoiseGenerator"}`, because the library calls its group
  nodes `Group`, `Group.001`, `Group.002` - positional, and they would move under any node added above them;
- **the eye copy was invisible to the control.** `character_materials` asked for a shader *group* node, and `Uma
  Eyes` has no `Uma Shader` group - it is an Emission graph with its own subgroups. So no driver, no verification and
  a `status()` that said "baked" while the graph animated itself. It now counts a copy that wears the shader by its
  tag, which is why `Shading` writes `uma_shader_role` (`eye`, `default`, `brows`, ...) onto every copy - and writes
  it on the *reuse* path too, so a copy shaded before the tag existed picks it up on its next run;
- **a targeted row applies to its own role only**, or the control would report the design as 20 missing settings on
  every eye and 5 missing ones on every body copy.

`setting_targets(material, setting)` is now the one place that knows where a row's value lives; `hook`, `verify`,
`bake`, `_own_driver_paths` and `seed_global` all read it, so they cannot disagree. `seed_global` also seeds per
shader across *all* of a character's copies now, so an eye knob is seeded from the eye.

**Measured** (`Tools/verify_eye_controls.py`, `Tools/verify_eye_panel.py`), on a freshly shaded 1001: the five rows
resolve to `nodes["Value"].outputs[0]`, `nodes["Group"].inputs[0]`, `nodes["Group"].inputs[1]`,
`nodes["Color"].outputs[0]`, `nodes["Color.001"].outputs[0]`; **11 drivers on the eye** (1+1+1+4+4) and
**298 in the scene over 8 copies** (41 per body copy, as measured before, unchanged); 0 rows reported missing;
global `Crying` 1 reaches the socket, `Tremble rate` 5 reaches `TimeScale`, a colour reaches the RGB node, a
character override beats the global and inheriting returns to it; `verify()` 0 of 145 wrong; `bake()` writes 145
sockets in and removes 298 drivers, and re-hooking restores them. The panel draws all five rows in the global block
and in a character's own block - checked by recording the layout calls, because a background Blender has no UI.

### 3. DONE — Spork's eye, and the `group_hints` trap

**The eye is ported**: `SporkShaders.blend`'s `Spork Eye` is now Looper's `Uma Eyes` graph, 39 nodes, with the
`NoiseGenerator` and two `SmoothThreshold` groups travelling with it; `CrySwitch` 0, no images, no shader tag, and the
carrier sphere's slot points at it. His own eye is kept as `Spork Eye (Spork's own)` (22 nodes, `mmd_tex_uv` + two
`AOV Output`s that our Emission graph does not have) **with a fake user** - moving the sphere's slot takes away its
last real user, and Blender drops a zero-user datablock on save (measured: the first run left it in the session and
not in the file).

**The plan was wrong in one place, and measuring caught it:** `textures.eye` did *not* already match node for node.
His recipe bound the **main** eye texture to `mmd_base_tex` and the first highlight to `Image Texture`; ours are
`Image Texture` and `.001`-`.003`. Dropping the graph in unchanged would have bound the eye texture into the first
*highlight* slot. Rewritten, and the five `spork_eye_*` rows added - the same five nodes, because it is the same graph.

**The trap, settled by measuring before deciding.** `Simple Diff + Shaded FACE` (34 nodes, new in his project file)
offers `Diffuse`, `Shaded`, **`Shadow Softness`**, **`Emission Strength`**, `Light Intensity`, `Colored Light` - so it
*does* have both sockets the panel drives, which means the substring match would have failed **silently** (the same
shape as the 0.2.6 outline bug). `_hint_matches` is now exact: a name, or that name plus a Blender `.001` suffix (the
case the substring test existed for, since appending a second library renames a node group the way it renames a
material). The hint list stays `["Simple Diff + Shaded"]`; the FACE variant is deliberately not listed.

**Not done, and now a decision of its own:** his *new* `Simple Diff + Shaded` is **51 nodes with 8 sockets** -
`Base`, `Highlight Color`, `Light Intensity`, `Colored Light` are the four the library's 26-node/4-socket copy does
not have. Porting it means rebuilding the library from his file and re-measuring which new sockets are worth rows
(the same test that kept `Diffuse`/`Shaded` out: is the socket fed by a *link* in his materials?).

**Verified** (`Tools/port_spork_eye.py`, `Tools/verify_spork_library.py`): the library checks above, the matcher
accepts `Simple Diff + Shaded`/`.001` and rejects `Simple Diff + Shaded FACE`, and a character shaded with
`spork-1.0` gets an `[eye]` copy of the ported master with `eye0` on the main node and `eyehi00-02` on `.001`-`.003`,
all five rows resolving, **11 drivers on the eye**, nothing missing, `verify()` clean. **No Spork `.pmx` exists on
this machine** (his project file is a `.blend`, and no export names 1063), so that last half ran on the reference
export with his manifest - the apply path is what it tests, and the images were 1001's.

### 4. Decal morphs react to world light (decided: unlit `Uma Decal`, not built yet)

Decals keep mmd_tools' `MMDShaderDev`, which lights from the **scene's lights**, while Looper lights from the
`Uma Sun` empty with a threshold mask — different lighting models, so decals always look darker than the face under
them. The user picked option 1: a library **`Uma Decal`** material — texture × alpha into emission, alpha-blended, no
lighting (what the game does, and it gives honest tint/opacity knobs). `decal_materials()` already knows exactly which
materials are decals, so the work is a library material plus the decal pass replacing each decal's material with a
copy of it, and the two rejected options stay rejected: flattening the MMD material has no knobs, and shading decals
with the face's shader is the route that wrapped manga in outlines.

### 5. Static face donor and `Eye_EyeHideA_*` (confirmed, not built yet)

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

Decided by the user at the start of this session, and worked in this order: **brows → panel rows → Spork**, all three
now shipped and verified.

- decals: **unlit `Uma Decal` library material** — decided, **not built** (item 4)
- donor: **confirmed**, drive `Eye_EyeHideA_L/_R` on the static copy from the main face's keys — **not built** (item 5)
- tremble knobs: **rate + amount only** plus `CrySwitch` and the two colours — shipped
- **new, from item 3**: port Spork's *new* 51-node `Simple Diff + Shaded` (8 sockets, 4 of them new) into the library,
  or leave the 26-node one? Needs a measurement first: which of the new sockets are fed by a link in his materials
  (those cannot be panel rows, the same reason `Diffuse`/`Shaded` are not)
- still undecided: whether `Spin`/`Tear_Scroll` should become node-driven (item 6, deliberately last)

## Traps that cost time

- `git commit -m` from `pwsh` breaks on quotes/backticks — write the message to a file and use `-F`
- `MorphDrivers.py` is LF, `panels/AddonPanels.py` is CRLF: line-based edits with an explicit EOL, not literal
  multi-line replacements
- the Synth Data add-on overrides the render output path; save through `bpy.data.images["Render Result"].save_render`
- a python reference to a link/node goes stale across a render — look it up again before removing it
- a `bpy_struct` **comparison** (`node is other_node`) is not an identity test: Blender hands back a fresh Python
  wrapper per lookup, so a check written that way fails on a node that is present. Compare `name`s.
- Blender 5.2's EEVEE is `BLENDER_EEVEE` again (`BLENDER_EEVEE_NEXT` was 4.2-4.4), so a script that sets the engine
  by name has to pick it from `scene.render.bl_rna.properties["engine"].enum_items`
- `bpy.data.libraries.load` appending a material does bring its node groups (verified), but the **material** keeps
  `uma_shader_source` and bound images: strip both for a library
- EEVEE timings need a warm-up render per configuration
