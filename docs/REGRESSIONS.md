# Failure points, and what guards each one

Every defect this project has found the hard way, what would have caught it, and whether anything does today.
The rule the list follows: **a failure that can be found again by a command belongs in the list with that
command**; one that cannot belongs in "open" with what it would take.

Run these before shipping anything that touches the exporter, the add-on's operators or the shader libraries.

## Guarded (a command finds it again)

| failure point | what happened | guard |
|---|---|---|
| **index width vs. count** — a pmx that outgrew its 2-byte indices | written without an error, corrupt downstream | `pmx_inspect.py integrity` |
| **morph names longer than a vmd's 15 bytes** | morph tracks silently dropped by mmd_tools / MMD | `pmx_inspect.py names`, and `check` for the eye contract |
| **bone tails not pointing at the MMD joint** | the whole motion rotated about the wrong axis | `check_tails.py` (99.9% of joint selections) |
| **an eye control key that moves the other eye** (add-on ≤ 2.35.19) | `shape_key_mirror` skips vertices it cannot pair, leaving the *source* eye's motion in the mirrored key; rotating one eye bone dragged part of the other (6 of 23 vertices per key) | `Tools/verify_eye_structure.py` — every key moves one eye, each mirror is exact |
| **`symmetry_snap` deforming the eye** (removed in 2.35.20) | moved all 46 eye vertices (median 1.6 mm) to prepare a mirror that then still failed | same gate (`verify_eye_structure.py`) |
| **the normal donor losing the eyelid keys** (2.35.21) | the keys were asked for by the *unified* spelling only, so a mode 0/1 export kept none and the blink artefact returned silently | the gate's donor check, and the review scene's label for every fixture |
| **a label that hides a step** | a review row that said nothing about a step read as "it never ran" | `build_review_scene.py` prints `not run` per step; `verify_review_scene.py` cross-checks each claim against the data |
| **a review render that proves nothing** | the first row ran refine + normals only, so nothing looked shaded | the verifier's per-model opaque coverage in its own projected box |
| **eye control keys "missing" after shading** | the face split moves the frame morphs onto the face piece; a check on the body finds none | the verifier checks every piece of the model |
| **a damped-track toggle that reads off when it is on** | the getter returned from inside its `for bone_name` loop, so only the first bone in the list was ever looked at, and a second press stacked a second constraint | `Tools/verify_physics_panel.py` (both directions, no stacking, missing-first-bone case) |
| **a skirt master that needs rigify** | the master lived inside `rigify_chains` and attached to rigify FK bones, so a rig keeping its skirt family plain (the damped-track case) had no master at all | `Tools/verify_skirt_master_plain.py` (panels found on plain bones, one bone added, no rigify bones, the master turns and flares a panel, idempotent) |
| **a pinned family that cannot be set to plain bones** | the ears are pinned to `spines.basic_tail` and their row was a locked label, so `none` (the answer a damped track needs) was unreachable for the one family that needs it | `Tools/verify_rig_plain_bones.py` (default unchanged; `none` really leaves the bones plain; the pin still types them) |
| **an operator no panel draws** | `uma.build_morph_drivers` was reachable only from F3 while the Morphs panel told the user to run it | the same script records the panel's layout calls and requires the row |

## Open (no automated guard yet)

| failure point | how it shows | what a test would do |
|---|---|---|
| **orphaned drivers after a library rename** (fixed in 2.35.22, unguarded) | a build that renames a node orphans the drivers written by the previous one: `status()` undercounts, `unhook()` leaves them, each re-hook adds another | hook a model, rename a node a row addresses, `unhook()`, assert zero owned drivers left and no duplicates after re-hook |
| **a renamed setting key** | the user's value is silently ignored and the row shows its default | shade a model, rename a key in the manifest, assert the report says "N settings this add-on does not know" |
| **a renamed role / source tag** | the copies stop being found and `Shading` builds a second set beside the first | assert one copy per material after two Shading runs, with the tag renamed between them |
| **a shader id that disappears** | copies keep `uma_shader_source` naming a manifest that no longer exists | assert the report names it |
| **`uma.build_morph_drivers` returning CANCELLED on notes** | the label says `cancelled` for a step that worked (dead morph names after the face split) | decide the contract: `FINISHED` + a warning report, or document CANCELLED as "ran with notes" |
| **mouth morphs unresolvable in modes 0/1** | latent: nothing looks a mouth morph up today | `naming.parse` must classify `Mouth_2_0`-style names, or the lookup must accept a tag-embedded side |
| **`naming.parse` misclassifying short names as unified** | `convention()` returns `unified` on a pure mode-1 file, so `target_name()` would write `Ear_Kanasi_L` there | a resolver test over all three spellings (the audit measured the current behaviour) |
| **texture variants / wet-dirt wiring** | `PmxTextureVariants` is off by default, so a shaded model never exercises `wet=`/`dirt=` binding | export one fixture with variants on, assert the add-on binds the maps and the report has no failed check |
| **both shaders** | only one shader is exercised per run | a two-fixture review scene (done: `--fixtures` with a `shader` per fixture), extended into the automated runner |

## What a review row is for, and what it is not

A review scene records a *state* a person can judge: models side by side, one line per step per model, and the
add-on's run report embedded in the file. It is not the regression net — the lists above are — but it is how a
green run gets checked against what the picture actually shows, which is the one thing assertions cannot do.

## The test dataset (the axis list this implies)

Ordered by what has actually broken, not by what is easy:

1. **morph naming 0/1/2** — only matters for the *non-shading* steps (eye controls, donor keys, resolver), which
   is exactly why the mode row looks identical when shaded. Keep it for those assertions, not for looks.
2. **shader** — `umashader-0.2.6` against `spork-1.0`; the look is the thing under test.
3. **texture variants on/off** — wet/dirt wiring is only exercised when they are on.
4. **face normals on/off** — the donor and the split change the object graph.
5. **A-pose on/off, extra morphs on/off** — motion compatibility and the deploy keys.
6. **a second rig** (1087 next to 1001) — bone counts and layouts differ (430 vs 437).

Not yet in a runner: only the fixture list exists (`--fixtures manifest.json`), because the automated assertions
want the same fixtures the review scene uses.



