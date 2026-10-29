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
| `22b7fc4` | add-on | **unlit decals**: `Uma Decal` in the library, `materials.decal` + `textures.decal` + two rows (`Decal opacity`, `Decal tint`), and `Umashader.apply_decal_material` copying it per decal slot, keeping the name and the mmd comment |
| `73e8aec` | export | `Tools/patch_decal_material.py`, `Tools/verify_decals.py` (a two-run lamp comparison), the two decal probes - and the two tests whose row/driver counts were literals now derive them from the manifest |
| `d7712db` | add-on | **face donor**: the static copy keeps `Eye_EyeHideA_L/_R`, driven from the face's own `Key` datablock; `copy_topology_mismatch` allows them; `refresh_copy_data` re-asserts |
| `3f642cb` | export | `Tools/verify_face_donor.py` - the donor moves by the face's own offsets (0.07479, exact to 0.0000000) and by nothing else |

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

**DONE in 2.35.18** (the user spotted it: *"his shaded + diffuse group seems to be much more complex than what we
ship"*): his **51-node / 8-socket** `Simple Diff + Shaded` is ported - `Base`, `Highlight Color`, `Light Intensity`,
`Colored Light`, `Shadow Output`, transparency inside the group off the base mask's blue channel - together with
`Simple Diff + Shaded FACE` (34 nodes) as a `face` role of its own. `Tools/port_spork_group.py` does it and is
re-runnable; section 7 below records what it measured. The four new sockets are **not** rows, by the same test the old
ones failed: `Base`, `Highlight Color` and the two light sockets are either fed by links in his materials or left at
his own defaults, so a slider on them would fight the link.

**Verified** (`Tools/port_spork_eye.py`, `Tools/verify_spork_library.py`): the library checks above, the matcher
accepts `Simple Diff + Shaded`/`.001` and rejects `Simple Diff + Shaded FACE`, and a character shaded with
`spork-1.0` gets an `[eye]` copy of the ported master with `eye0` on the main node and `eyehi00-02` on `.001`-`.003`,
all five rows resolving, **11 drivers on the eye**, nothing missing, `verify()` clean. **No Spork `.pmx` exists on
this machine** (his project file is a `.blend`, and no export names 1063), so that last half ran on the reference
export with his manifest - the apply path is what it tests, and the images were 1001's.

### 4. DONE — the decals are painted, not lit

**Shipped**: a library `Uma Decal` (10 nodes - map × `UMA Decal Tint`, alpha × `UMA Decal Opacity`, Transparent/Emission
mixed by that alpha, `BLENDED`), `materials.decal` + `textures.decal`, two settings rows, and
`Umashader.apply_decal_material`, which copies it per decal slot, binds that slot's own map, keeps the decal's **name**
and its `mmd_material.comment` (the tear scroll and the @_@ spin read the comment, and they run *after* this), tags the
copy `uma_shader_role = "decal"`, and is idempotent. 15 decal copies on 1001, 5 drivers each (4 tint + 1 opacity).

**Measured before designing it, and one measurement was thrown away**: `image.pixels[:4000]` is the top few rows of a
256×256 map - transparent margin for every decal - so it reports "alpha is zero everywhere" about maps that are
43-84% transparent. Whole-image: cheek 76% transparent / mean alpha 0.06, tear sheet 50% / 0.14, manga 44-84% /
0.15-0.56, all 32-bit, and `mmd_material.alpha` 1.0 on every one. Transparency is per pixel and comes from the map.

**The measurement is a two-run comparison, because a single run cannot answer it.** An alpha-blended decal is
`decal*alpha + face*(1-alpha)`, so it inherits whatever the face underneath does - changing the body's lighting moved
the decal *pixels* more than the rest of the frame, which is a true reading of a composite and a false reading of the
decal. So: the same scene shaded once **with** the `decal` role and once **without** it, and a real 20 W sun lamp
switched on in both. Without the role the lamp changes the decal pixels by **0.03286**; with it, **0.00000**. The lamp
has to be real: relocating the `Uma Sun` empty 1.5 units behind the head changed **not a single pixel**, so that empty
is not how this shader's direction is taken (which also means the "tilt the sun" idea for the unlit proof would have
measured nothing at all and been believed).

Traps this cost: `to_mesh()` on these shaded meshes reports **one** `material_index` for every polygon (measured: 17718
polygons, one index), so material lookup has to read `obj.data.polygons`; the brows - and the decal faces - live on the
**separated face** object, not on the mesh the operator was handed; and the average normal of a whole face mesh points
*into* the head (+Y where the brows read −Y), which puts a camera behind the character and renders two identical empty
frames.

### 5. DONE — the static face donor follows the eyelid

**Shipped as designed**: the copy keeps `Eye_EyeHideA_L/_R` and nothing else, and the two are driven from the main
face's own `Key` datablock (`SINGLE_PROP` at `key_blocks["…"].value`), so the donor deforms like the surface its
normals are transferred onto. `FaceNormals.keep_face_keys` does the pruning and the driving; `refresh_copy_data`
re-asserts it, because a refresh brings the face's whole key set back with the mesh data.

Three things it cost, two of them wrong on the first attempt:

- **the variable's id type must be set before its id.** Assigning a Key datablock to a target still typed `OBJECT` is
  refused, `target.id` stays `None`, and a variable with no target evaluates to 0 - so the keys were kept, driven and
  never followed (measured: the copy read 0.0000 while the face was at 1). There is a fallback that reads through the
  face object for a Blender that refuses the `KEY` type;
- **`copy_topology_mismatch` treated any shape key on the copy as a topology mismatch**, so a copy that keeps two on
  purpose would have been rebuilt on every run. It now allows `Basis` plus the kept two - and its own membership test
  had a case bug (`{"Basis"}` compared against lowercased names);
- the verification cannot compare evaluated *meshes* vertex for vertex: the face piece carries the `Uma Outlines`
  geometry-nodes shell and a data transfer, so its evaluated mesh is not a match for the plain copy (measured: the
  comparison ran off the end). Comparing the **key offsets** is both simpler and stricter, and the depsgraph
  comparison against the copy's own rest shape still shows the deformation happening.

**Measured** (`Tools/verify_face_donor.py`, freshly shaded 1001): the copy ends with exactly `Basis`,
`Eye_EyeHideA_R`, `Eye_EyeHideA_L`; each has one driver reading `Key.001` at the same key; with the face's key at 1 the
copy's own value reads **1.0000** and its eyelid moves **0.07479**; it holds the face's offsets **exactly**
(0.0000000 over 1468 vertices); `Mouth_WaraiA` at 1 moves the face and leaves the copy at **0.0000000**; a second run
leaves one copy, two keys, one driver each, no mismatch, transfer intact.

The deeper check the plan asked for - the angle between the transferred normal and the donor's *deformed* normal at the
eye region - is still not automated: the offset identity plus the driven deformation establish that the donor's lid is
the face's lid, but nothing yet measures the shading that comes out the other end.

### 6. Optional, from the measurements

Convert `Spin` (Vector Rotate angle) and `Tear_Scroll` (Mapping location) to `Scene Time` chains so the library
animates itself, and stop driving the 20 appearance settings every frame (write them when the panel changes; keep
drivers only while a knob is being animated). Self-containment, not speed.

### 7. DONE (2.35.18) — his current shader, ported; and the decal role Spork never had

Two gaps of the same kind, both found by the user: the Spork side of the add-on was a revision behind the Looper side.

**The graph.** `SporkShaders.blend` carried `Simple Diff + Shaded` = his **26 nodes / 4 sockets** (+ our 6 dirt nodes).
His file today (`SporksShader.blend`, which matches `test.blend` group for group) is **51 nodes / 8 sockets**: `Base`,
`Highlight Color`, `Light Intensity`, `Colored Light`, a `Shadow Output`, and transparency handled *inside* the group
off the base mask's blue channel. `Tools/port_spork_group.py` appends his group and its `FACE` sibling (34 nodes),
injects our weather maths into both and rewires the masters:

- dirt sits between each colour socket and *every* consumer (`Dirt <tap>` mixes on `Diffuse` and `Shaded`) - his group
  feeds the emission and both shading chains from those sockets, so one tap would have left the shaded side clean;
- the wrapper-side `Separate Color` -> `Mix Shader` transparency pair is gone, because his group reads the base mask
  itself; `Spork Eye Inner` sets `Base` to white, since black there mixes the inner eye fully transparent;
- masters now: `Spork Shader`/`Uma Brows` (22/24 nodes) on the general group, `Spork Shader Face` (19) on the FACE
  variant for a new `face` role, `Spork Eye Inner` (4) on the general one with `Base` white.

**Measured, not eyeballed.** His group and the ported one, driven by identical values with `Dirt` 0, render to
**zero differing pixels** (the dirt chain is a Mix at factor 0 there, so the port is provably his maths). On the 1063
export (`C:\tmp\matrix\current\1063_00`), a bust view differs on **83% of pixels** from the pre-port library - mean
0.495/0.413/0.431 against 0.566/0.518/0.521, i.e. his real light-response model is darker than our flat one - and the
face region comes out at (0.682, 0.405, 0.359) on the FACE variant, against (0.688, 0.410, 0.352) for the old library
and (0.771, 0.474, 0.439) for the general group, so his face variant preserves the look our faces had. Weather still
works: Rain 1 moves 59% of the bust, Dirt 1 31%, 15 dirt maps bound, 57 wet slots filled on 1063 / 45 on 1001.
`Tools/verify_spork_library.py` passes every check (it was out of date with our own node renames and now derives its
expectations from the current names).

**The decals.** `spork-1.0` named no `decal` master, so `apply_decal_material` had nothing to paint a decal with and
every decal kept mmd_tools' lit material: wrong brightness beside a character lighting from the `Uma Sun`, and none of
the spin/extension handling. Measured: **45 decal copies under `umashader-0.2.6`, 0 under `spork-1.0`**; both are 45
now (`Tools/add_spork_decal.py` appends the Looper library's `Uma Decal`, which is shader-independent by construction,
and the manifest gains `materials.decal`, `textures.decal`, the same `shipped_textures.blush` and the same
`uma_shader_decal_opacity`/`_tint` rows - the master itself rests at opacity 1.0 and a white tint, because the Looper
library leaves those at 0.5/black knowing its manifest drives them, and a manifest without the rows would have painted
every decal at half strength).

One measurement to keep: with the decal copies' opacity set to 0 and back in the render harness, **the image does not
change at all** - for the Looper library too. So this harness cannot see decals (the quads are coplanar with the face
and the alpha-blended emission does not survive `--background`), and "the decals look right" has to be judged in the
GUI. What the checks prove is what can be proven headlessly: 45 copies, each bound to its own map, the blush on the
multiply decal, CLIP on the spinning eyes, 125 drivers over 23 copies.

Both libraries' manifests still expose the same `uma_shader_wet`/`uma_shader_dirt`/`uma_shader_dirt_colour` keys, so a
scene wearing both shaders keeps one set of weather globals.

## Reports from the user, with what has been measured

Four things came back after all five items shipped. Two are measured; two need a file or a decision.

**A. Three donor objects after shading two models in one file, one a copy of a whole mesh.** Not reproducible here
(there is no two-model file on this machine) so this is the one that needs the file. Two leads worth checking first:
`FaceNormals.faces_in` accepts an object as a face when *all* its polygons are face materials or when its **name**
says face, and the static copy carries every material slot of the piece it copies - so a whole-mesh copy means a
whole mesh answered "yes, I am the face". `find_copy` is supposed to prevent a second copy per face; the marker it
looks for is `COPY_MARKER` on the object.

**B. `Manga_Wide_R` darker than the left one, and it disappears in edit mode.** Measured, and it is a data defect in
the export: on 1001 the **right-eye instances of two of the four manga prefabs** (`mtl_eff_chr_emo_eye_002.001` =
Shut, `mtl_eff_chr_emo_eye_003.001` = **Wide**) and **one tear sheet** (`mtl_chr_tear001.001`) have their polygons
facing **into** the head - average normal · outward = **−0.705** and **−0.793**, where their left twins read **+0.705**
and **+0.793**. The exporter lifts every decal off the face *along its own normal*, so those parts were lifted
**behind** the face instead of off it, which is both the "darker" and the "vanishes in edit mode" (solid shading does
not draw a face whose normal points away). Also measured: `mtl_chr_tear001.002`/`.003` read 0.000 because they are
still collapsed - those are the rolling cards that were deliberately left out of the export.
(`Tools/probe_decal_normals.py`.)

**C. The brows' render-in-front is not in Spork's shader.** Shipped (`2548f6a`): `Uma Brows` in `SporkShaders.blend`
from his own `Spork Shader`, the `brows` role and rule and texture recipe in `spork-1.0`, measured at the same
261 → 1657 pixels as under Looper.

**D. Tears leak their background over the manga faces.** Not fixed, and two candidate fixes have each been checked
and ruled out or confirmed:

- **the render-in-front trick cannot work on them as they are**: the four eye manga are **2 polygons / 4 vertices**
  each, the static tears 16/15 and the sheets 48/36 (`Tools/probe_decal_geometry.py`), and the brow trick is *vertex*
  displacement - measured doing nothing at all on a 4-vertex plane. It would need subdivided decal quads, which would
  also change the mesh topology the normal transfer pairs against.
- **additive blending is not available in EEVEE Next**, which the user asked for and which has now been measured:
  `Uma Decal` was rebuilt with a soft/additive switch (`Add Shader(Emission x alpha, Transparent)` behind a `Mix
  Shader`), and on a 1001 with the manga deployed **1937 of 1937** decal pixels came out *darker* than with the decal
  hidden - because an Emission closure carries alpha 1, so `Add Shader` occludes instead of adding. EEVEE Next's
  blended materials do alpha blending only (`surface_render_method` has `DITHERED`/`BLENDED` and no additive mode), so
  the switch was reverted rather than shipped reading "additive" and behaving opaque.

  **Shipped instead: the black key** (`a7916e2`). `Uma Decal` keys the drawn alpha by the map's own brightest channel -
  `alpha = map.alpha x lerp(1, max(r, g, b), key) x opacity` - so a sprite's black background contributes nothing and
  the art is left alone, with `Key out black` as a row in the Decals section for a decal whose own art is dark enough
  that keying would thin it. Measured on 1001 (`Tools/measure_decal_key.py`, the cheek blush): the painted area falls
  **2981 -> 2784** pixels (the background it dropped), the worst darkening of any pixel falls **0.0614 -> 0.0405**, and
  the darkened-pixel count falls with it. Two wiring mistakes were caught by measuring rather than by reading the graph:
  `ShaderNodeMix` indexes its sockets across *all* data types (0 is the float factor, 2 the float A, 3 the float B), so
  linking A at index 0 put the map where the knob goes and key 0/key 1 rendered identically; and A/B the other way
  round made the knob do the opposite of its name.

  **Still open on this front**: the backdrop problem proper is the manga and the tears. `Manga_Wide_R` being darker and
  vanishing in edit mode was the **winding** defect, which the exporter now fixes (see
  `HANDOFF_OPTIONS_AND_RELEASE.md`, "The decal winding defect"); re-measuring these from a front camera on a fresh
  export is the next step, and should be done before assuming the black key is the remaining cause.

**E. The black key blanked the eye manga** (reported right after it shipped, fixed in `69342f4`). The manga's art *is*
black line work, so keying the black out deletes the drawing and leaves the light background: the wide-eye sprite came
out pure white with its rim gone. The key is still in `Uma Decal` - it is right for a sprite whose art sits on a black
*background* - but its default is now **0**, it is a manual knob, and its description says in the panel what it does to
black art. **The verification is what failed here, not the idea**: it measured the cheek blush, whose art is
red-on-transparent (where keying only thins the haze), and the one decal class the key breaks - the manga - was the one
the measurement could not see, because they draw behind the eyeball from a front camera (defect B).

**F. A Shading run prepares the faces of *every* character in the file, not just the selected one.** Reported as
unexpected and it is by design, not an accident: `apply_face_normals` calls `faces_in(context)` with no `objects`, so
every mesh with face polygons is a target - which is what made the second character's face piece and donor on the first
run. Whether that is the *right* design is a decision: scoping it to the run's own character means passing the run's
meshes (`character_meshes` in `Umashader._apply`, which is already computed there) instead of `None`, and a second
character then keeps its face on the body until it is shaded itself.

**G. The tear and cheek morphs stay on the body after the face is separated.** Their geometry moves into the face piece,
the shape keys do not follow, so moving `Cheek_Blush` / `Tear_*` after a Shading run does nothing visible. The split
copies every key onto both pieces and `prune_split_morphs` prunes per piece, so the next step is a measurement of which
keys ended up with no offsets on the piece that holds their polygons - not a guess at the prune's logic.

**H. The blush into the face material: the projection works, vertex colours are too coarse** (user's idea, then
approved: *"add the blush to the face texture with a mix color node"*). A mix node in the face material can only read
the face mesh's own UVs, and the blush map is in its own UV square, so there is no UV mapping to pick between them -
any offset would be right for one character and wrong for the next. The mapping that is right by construction is
**geometry**, and `Tools/bake_blush_vertices.py` measures it: project every mesh vertex onto the deployed blush
triangle, check the barycentric, sample the blush map. Measured on 1001: **255 of 14492 vertices land on the blush**
(map alpha 0.000-0.552, mean 0.146), but only **30 of the face material's 1328 vertices** fall inside its silhouette -
so a *vertex* colour layer (`UMA_Blush`, white = none) is too coarse to carry a soft blush: it would come out blocky at
30 points. The route that matches the wanted quality is a **texture bake onto the face atlas's UV layout**, which is a
real bake pass (and a texture per character) rather than an attribute write. Alignment is not the problem in either
case - it is solved by projection - the resolution is.

### A. The blush into the face texture - what to build, in order

Decided: the blush stays a *separate layer* (so the morph can still turn it off), the texture is baked **exporter
side**, and the face material multiplies it in. Two halves.

**Unity side (`ModelExporter`/`TextureExporter`)** - needs a build, so it is written down rather than guessed at:

1. a `TextureExporter.BakeFaceBlush(...)` that renders the **deployed** cheek geometry into a texture the size of the
   face atlas and returns it, plus a `FaceBlushName(sourceName)` next to the existing `BlushName`. The algorithm is the
   one `Tools/bake_blush_vertices.py` validated, at texel resolution instead of vertex resolution: for every texel of
   the face atlas, take the face surface point it belongs to (walk the face polygons, interpolate position from the
   UVs), project it onto the cheek quad, and if it lands inside, write the blush sample into that texel; white (=
   multiply by 1) everywhere else. The paired data is exactly what `Tools/fit_blush_uv.py` collects, and that tool's
   failure is why this is a *bake* and not a UV transform: no single affine map relates the two UV spaces (residual
   ~42 UV units on a 0-1 map).
2. it has to be written into `Texture2D/` and **rewritten on every export** (the stale-bake trap: an older bake
   silently shipping is already a known failure mode for `<map>_blush`/`<map>_mask`).
3. the face material's own texture must stay the thing the pmx points at; the blush is an extra file the add-on binds.

**Add-on side (`Uma Face`)** - not built yet, and it is the half that can be verified here:

1. a library material `Uma Face` = a copy of `Uma Shader` with one `Image Texture` node (name `face_blush`, unbound) and
   a `MixRGB` set to MULTIPLY inserted between the diffuse texture node and the group socket it feeds;
2. `materials.face` + a `parts` rule bump (`face` -> `face`) so the face slot takes that material instead of `default`,
   `textures.face` = the `default` recipe plus the `face_blush` rule (bound from the model's folder, `optional: true`
   so a model without a bake still shades);
3. the multiply's **factor driven from the `Cheek_Blush` shape key** - `SINGLE_PROP` on the key's `value`, exactly like
   the face donor's eyelid keys - so the morph slider still controls the blush while the drawing is the face
   material's own multiply, and no decal quad is needed for the cheek;
4. rows for the blush tint/strength if the factor turns out to want a manual override.

Until the bake exists, the cheek keeps the `[decal]` quad, so nothing here is a regression: the manifest role can be
added and left unused, or not added at all.

## Decisions still owed by the user

Everything decided at the start of this session is now **shipped and verified**: brows, the eye knobs, Spork's eye,
the unlit decals, the face donor, and (2.35.18) his current 51-node shader plus the decal role Spork never had. What is
left:

- whether `Spin`/`Tear_Scroll` should become node-driven (item 6, deliberately last).
- the viewer side of Spork: a Spork character shaded in Blender looks different from the *viewer's* Spork look, and
  nobody has compared the two - the viewer ships its own Unity shaders, and 1063 is the only Spork export there is.
- the eyes' shading path after the port: `Spork Eye` is now our graph, so a Spork character's eyes animate themselves
  but no longer carry his own eye's `mmd_tex_uv`/AOV setup. Nobody has looked at a shaded Spork character yet - there
  is no Spork `.pmx` on this machine - so the first person who shades one should compare it against his render.

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
