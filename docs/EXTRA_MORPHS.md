# The extra morphs: the full list, and what the export does with each

The game's face rig has two kinds of morph. The ordinary one (`FacialMorph`) is vertex offsets and exports as an
MMD vertex morph. The other kind (`FacialOtherMorph`) drives *material properties*, *textures*, *object
visibility* and an *animated tear system*, through a list of `BindProperty` entries. This is the complete
inventory of those, read from the code (`Assets/Scripts/umamusume/Gallop/FacialMorph.cs`,
`FaceDrivenKeyTarget.InitializeExtra()`, `TearController.cs`), and what the exporter does about each.

| morph | driven by (game) | what it does in the game | export |
|---|---|---|---|
| `Cheek_Ctrl` | locator scale X/Y/Z | `Texture` + `Shader` binds on the `M_Cheek` decal: `mainTexture = cheek0/1/2`, `_CheekRate` | **deploy morph `Cheek_Blush`** + a baked blush map; the map pair stays in `Texture2D/` and the binding is in the material comment |
| `Shade_Ctrl` | locator scale X | `Shader` bind `_faceShadowAlpha` on the face material | nothing yet - an MMD material has no such field; the addon would have to drive the uma shader's face region |
| `Manga_Ctrl` | locator scale Y (select), scale X (enable) | `EyeSelect` + `Enable` over four manga prefabs, once per eye | **eight deploy morphs**, one per eye and expression (below) |
| `StaticTear_Ctrl` | locator position X (L), Y (R) | `Enable` on `tearmesh_l` / `tearmesh_r`, drawn with `tex_chr_tear00` | **deploy morphs `Tear_Static_L`, `Tear_Static_R`** |
| `Tear0_Ctrl` … `Tear3_Ctrl` | scale X (`TearWeight`), position Y (`TearSide`), scale Y (`TearSelect`), scale Z (`TearSpeed`) | the animated tears - see below | **deploy morphs `Tear_Stream_*` / `Tear_Pool_*`** (deduplicated, one per distinct card) plus the `scroll=<cols>x<rows>` grid in the material comment (left side: the viewer never places the right one); the control tokens stay in the model comment |

## The eye manga

Four prefabs, `pfb_eff_chr_emo_eye_000` … `_003`, instanced twice - once for each eye. The four draw these
faces, in the order the game selects them:

| index | face | morph names |
|---|---|---|
| 0 | `@_@` | `@_@_L`, `@_@_R` |
| 1 | `✦_✦` | `✦_✦_L`, `✦_✦_R` |
| 2 | `>_<` | `>_<_L`, `>_<_R` |
| 3 | `o_o` | `o_o_L`, `o_o_R` |

Each is a quad over one eye, collapsed at rest and deployed by its morph, so `@_@_L` at weight 1 puts the `@_@`
eye on the left eye and nothing else moves.

## The animated tears

`TearController.cs` keeps two tear prefabs per controller - `pfb_chr_tear000`, `pfb_chr_tear001` - each a
skinned mesh with an `Animator` whose clip is the asset's `default_animation` (the `anm_tear000_00` state is
the handle that gets overridden). What makes them no ordinary decal:

* **they scroll a sprite sheet**: `UpdateOffset()` sets `_TexScrollParam = (offsetX, -offsetY, 0, Weight)`,
  where the offset walks the sheet's columns and rows - `texture_width`, `texture_height`,
  `texture_part_width`, `texture_part_height` come from the prefab's own asset table, and the frame index is
  `normalizedTime * columns * rows`
* **they are stencil-masked**: `_StencilMask = id`, `_StencilComp = Equal`, so the tear only draws where the
  eye it belongs to was drawn
* **they are mirrored, not mirrored geometry**: `SetDir` moves the objects to `Eye_tear_attach_03_L/R` and
  `Eye_tear_attach_02_L/R` and sets `localScale = (-1, 1, 1)` for the right eye
* **the four controls** pick which of the two objects, the side, the weight and the animation speed

MMD cannot hold the scroll or the stencil. The scroll used to become a stack of UV morphs (one per sprite-sheet
frame, capped at 8 steps); that stack was removed - what carries the scroll now is the `scroll=<cols>x<rows>` grid in
the material comment, which the add-on steps a Mapping node over, so a running tear is the deploy morph at 1 with its
texture walking the sheet. The stencil has no counterpart and is not needed: the tear is its own decal
geometry, drawn in front of the eye by the same deploy lift every decal gets. Both sides are in the export: the four
controllers are not all on the same eye - the cards come out as `Tear_Stream_L/R` and `Tear_Pool_L/R` because the
viewer places each controller on the side its own `SetDir` chose, and the four controllers deduplicate onto those
four cards. A controller that is mirrored at runtime rather than placed would need its right-side instance created
at export time.

The sprite sheets themselves are inside the tear prefabs' asset tables, not in `Persistent/textures`, which is
why the viewer has never listed them.

### Why the export carries no UV morphs

Both animated decals used to travel as **stacks of uv morphs** - seven rotations for the `@_@` turn, seven
sprite-sheet steps per tear card - and both stacks were built, measured and then **removed**: the pmx no longer
carries a single uv morph, and character 1001 went from 249 morphs to 207.

The reason is that nothing in the pipeline could use them. Blender cannot animate a uv layer - a shape key moves
vertices, not texture coordinates - so the add-on animates both effects in the *material*: a Vector Rotate node
driven by `Spin_Speed`/`Spin` for the turn, a Mapping node stepped by `Tear_Scroll` over the `scroll=<cols>x<rows>`
grid for the tear sheet. The morphs were therefore 42 sliders sitting in every character's morph list that no tool of
ours reads, on effects that were already animated by other means. They also carried a live hazard: `mmd_tools` imports
them as `mmd_root.uv_morphs` groups, so pressing *Apply UV Morph* on one bakes that rotation into the decal's uv
coordinates for good.

What that costs: a consumer **without** the add-on no longer gets these two effects. In plain MMD a material
animation can only *translate* uvs (no rotation) and a comment is not readable, so the uv-morph route was the only
way they existed outside this pipeline. The pipeline's whole point is the add-on, which drives them from the tokens,
so that trade was taken deliberately.

#### The sign trap, which is worth keeping even though the code is gone

The pmx stores uv **top-down**. That is why vertex uvs are written `1 - v`, and mmd_tools flips them back on
import (`pmx/importer.py: flipUV_V`), so static uvs round-trip unchanged. A uv *morph* is a delta, and the
importer **negates** a delta's v rather than flipping it (`(x, -y, z, -w)`), so a delta has to be stored with the
sign the game does *not* use. The tear scroll had this wrong while it existed: its base cell sits at v
`[0, 1/rows]`, and seven steps of `1/rows` land the uvs at exactly 1.0 - the far edge of the sheet - when stored
`+v`, and at **-0.875**, off the sheet entirely, when stored `-v`, which is what shipped. A stack that walks the uvs
off the sheet cannot scroll through it. Anyone who ever writes a uv morph again should start from this paragraph.

For the record, the removed rotation stack was exact: every `Manga_Spin_*_TurnN` was a pure rotation about the centre
of 0..1 uv space, walking `0, 45, 90, 135, 180, -135, -90, -45` degrees - one direction, 45° per step, wrapping
through ±180 so that `Turn7` plus one more step returned to the identity, every vertex agreeing to 0.00°, and both
eyes carrying the same angles. The turn is now whatever the add-on's driver does, in one direction only; the
`Spin_Speed` knob scales it and 0 stops it.

### The skeleton animates: the card rolls down the cheek and off the chin

The card is not a static mesh and it is not sprite-only: it is a small **skeleton animation**, and the whole
motion is in the data. What the setup is:

| part of the setup | what it is |
|---|---|
| the mesh | `SkinnedMeshRenderer`, 66 verts on an 11-joint chain (`joint000`…`joint010`, 5 mm apart) for `tear000`; 36 verts on 3 joints for `tear001` |
| the animator | present and enabled, with its own generic avatar `mdl_chr_tear000Avatar` (`valid=True`, `human=False`) |
| the controller | created at run time: `TearController` builds an `AnimatorOverrideController` over `anm_chara_tear_animator` and overrides the state `anm_tear000_00` with the prefab's `default_animation` |
| the clips | 4.267 s, looping, **and `AnimationUtility.GetCurveBindings` reports 0 curves for them** - which says nothing about the animation, because that API only lists generic transform curves |

Sampling the animator through the clip (`animator.Play(0, 0, t)` then `BakeMesh`) gives the real motion. Positions
are local to the attach point, so negative Y is down the face; the tall card, controller 2:

| t | extents | what it is doing |
|---|---|---|
| 0.0–0.2 | min Y −0.002, max Y +0.043 | standing at the eye corner |
| 0.3 | Y −0.025 … +0.021 | tipping over the lid |
| 0.4 | Y −0.055 … −0.012 | clear of the eye, running down |
| 0.5 | Y −0.073 … −0.041, X 0.003 … 0.043 | down the cheek, drifting outboard |
| 0.6–0.7 | Y −0.083 … −0.053, X 0.017 … 0.063 | mid-cheek |
| 0.8 | Y −0.101 … −0.064, narrowing | the drop thinning |
| 0.9 | Y −0.118 … −0.072, 0.012 wide | at the chin, nearly gone |
| 1.0 | back to the start | loops |

Controller 3 is the same motion mirrored in X, which is the `localScale = (-1,1,1)` of the right eye. The wide
`tear001` card does **not** move at all (0 of 10 steps) - only the tall one animates.

An alarming amount of time went into concluding the opposite, so the trap is worth recording: **an `Animator` in
its default culling mode does not write bone transforms while the renderer is not being drawn**, and batch mode
draws nothing. That is why the card measured perfectly rigid here - `BakeMesh` kept returning the rest pose -
while it visibly rolls in the editor, where the scene view is always drawing it. Setting
`animator.cullingMode = AnimatorCullingMode.AlwaysAnimate` before sampling is what made the animation visible to
the probe. The same reason is why sampling over wall-clock seconds was useless in batch: two seconds advance the
state by about 1% of the clip. Stepping `Play(0, 0, t)` through normalised time is the way to see it.

The card's rest pose is a stub pointing up from the eye corner, and it measures 0.603 pmx units from Y 17.495
to 18.098 while the eye corner (`Eye_tear_attach_03_L`) is at 17.455 and the `Chin` bone at 16.466 - which is why
an export that copies the rest pose reads as a small quad above the eye.

### Shelved: exporting the roll

The motion above *was* exported, and then taken back out. It is worth writing down what it did and why it went,
because the measurement above is the expensive part and it stands.

**What it was.** `TearRollTimes = {0.2, 0.5, 0.9}` - the corner, mid-cheek, the chin - baked as three more vertex
morphs per animated card, each offset measured from the deployed pose:

```
TearDrop1_L            deploy, the card at the eye corner
TearDrop1_L_roll1..3   the three sampled poses
```

and a `roll=<seconds>:<t1>,<t2>,t3>` material token so a consumer knew the loop and where the poses sit in it. In
the addon, `build_tear_roll` drove the three weights piecewise-linearly from a `Tear_Roll` property (and a
`Tear_Roll_Phase` property holding the loop position, because inlining the phase into the weight expressions made
one of them 267 characters and Blender's driver parser rejected it). Verified working: the card travelled from Z
1.442 at frame 1 to 1.334 at frame 90, the eye corner to the chin, weights crossfading 1.00/0 → 0.69/0.31 → 0/0.79/0.21
→ 0/0.05/0.95.

**Why it was shelved** - the user's call, and the reasoning is worth keeping:

* **Twelve morphs for one effect.** The effect is real but it is one small card, and it cost 12 of 293 morphs
  (four cards × three poses) where the whole rest of the advanced set costs 89.
* **It only works with the addon.** A pmx morph cannot hold a curve, so the animation *is* the crossfade between
  stored poses. Opened in plain `mmd_tools` the three morphs are inert keys that do nothing on their own - the
  exact opposite of how the other advanced morphs behave, which degrade gracefully (the deploy keys still work as
  shape keys even with no addon).
* **It still looked rough.** Piecewise-linear between three poses is a visible approximation on a flat quad,
  and the sprite scroll underneath is already carrying most of the "tear running down" reading.

**What was kept.** The tear scroll's V-sign fix (that one was a bug in shipped code, not a feature), the
`headless_export.ps1 -ProbeTears` probe, `Assets/Editor/UmaTearWatch.cs`, and every
measurement on this page.

**What would make it worth revisiting** - the carrier, not the sampler. The card is a *skinned mesh on an
11-joint chain*, so the honest export is **bones plus a VMD**: the joints as pmx bones with per-vertex weights,
and the sampled pose baked into bone curves. That costs no morphs at all, works in plain `mmd_tools` and MMD, and
is what the game itself uses. The sampling code that produced the sweep is the part to keep for that route; the
pose-stack-and-crossfade layer is what was wrong.

### Traps worth not rediscovering

Three of these cost time here, and none is obvious from the API:

* **`Animator.cullingMode`** - in the default mode an animator does not write bone transforms while its renderer is
  not being drawn, which in a headless run is *always*. `BakeMesh` then returns the rest pose and the clip looks
  like it animates nothing. Set `AlwaysAnimate` before sampling.
* **`AnimationUtility.GetCurveBindings` reports generic curves only**, so a clip that animates perfectly can report
  `bindings=0`. Judge a clip by posing through it, not by counting its curves.
* Also, for drivers to survive here at all: a face separation gives the new object a **copy** of the shape-key
  datablock, so any driver on the original key does not follow it.

### The four controllers are not four positions

Worth knowing before reading the morph list. `SetDir` puts *both* of a controller's prefabs on the same eye -
`TearObject_0` at `Eye_tear_attach_03_L/R`, `TearObject_1` at `attach_02_L/R` - and the per-controller spacing in
the game comes from `TearSide`, a position-Y offset that the face clips drive. Nothing drives it by default, so
controllers 1 and 3 land on the left eye at the same place and 2 and 4 on the right: measured, the controller-1 and
controller-3 cards hold **identical** offsets (same shape, same position, different vertices), as do the two on the
right. That is why the exporter deduplicates them on the placed geometry and writes four cards
(`Tear_Stream_L/R`, `Tear_Pool_L/R`) instead of eight. Two stacked decals are the price of exporting the viewer's
default placement rather than the game's runtime one.

## The teary-eye effect that the viewer cannot show

`tex_chr_tear00` is not only the animated tears' texture: `UmaContainerCharacter.cs:927` loads it and assigns it
as the *main texture* of the two static tear meshes (`tearmesh_l`, `tearmesh_r`), which is the shine that sits
on the lower lid when a character is about to cry. Those two meshes are in the export (as `Tear_L`/`Tear_R`
above), so the effect *is* reachable.

The animated version is reachable too, which this document used to deny: the viewer's **Other** morph list builds
a slider for every `Tear0_Ctrl`…`Tear3_Ctrl` bind (`UmaViewerUI.cs`, the `TearWeight`/`TearSide`/`TearSelect`/
`TearSpeed` cases), so the tears can be switched on by hand. What that shows is the sprite rolling and the card
staying where it is - `UpdateOffset` writes `_TexScrollParam` and nothing else moves. See "What actually poses the
card" above for the measurement.

Settling whether there is a *third* mechanism - a shader input for wetness on the eye material - needs
a material probe: every material property whose name contains "tear"/"wet". No such property exists in the
shader set the viewer knows, but the probe is the evidence, not the absence.

## What the export carries today

Every advanced morph is named `<feature>_<what it does>_<side>`, so the set groups together in a morph list:
`Cheek_Blush`, `Manga_Spin_L`, `Tear_Static_L`, `Tear_Stream_L`. Before 2.34 the names were the game's own
spelling - the emoticons `@_@_L`, `✦_✦_L`, `>_<_L`, `o_o_L`, then `Tear_L`/`Tear_R` and `TearDrop1_L` … `TearDrop4b_R`
- and models exported then keep working: the add-on matches both spellings everywhere.

* `Cheek_Blush`, `Tear_Static_L`, `Tear_Static_R` - one deploy morph per static decal part.
* `Tear_Stream_L/R` and `Tear_Pool_L/R` - one deploy morph per *distinct* animated tear card. Two kinds because
  there are two prefabs: `tear000` is the tall card that rolls down the cheek, `tear001` the wide one.
* The four controllers used to produce four cards a side - controllers 1 and 3 carry the same card. They are
  deduplicated on the placed geometry now, because the four controllers differ only by `TearSide`, a position offset
  the face clips drive and this viewer never does. That removed two deploy morphs, and stopped the decal being drawn
  twice.
* `Manga_Spin_L/R`, `Manga_Star_L/R`, `Manga_Shut_L/R`, `Manga_Wide_L/R` - the eye manga, one per expression
  (`@_@`, `✦_✦`, `>_<`, `o_o`).
* The five `morph=` / `object morphs:` token groups in the comments, for the parts a vertex morph cannot hold.
* `Texture2D/tex_chr1001_00_cheek0_blush.png` and `…cheek1_blush.png` - the blush baked for alpha blending,
  beside the game's own maps.

**There are no UV morphs any more**, and character 1001 exports 207 morphs, all of them vertex morphs. Two stacks
used to ride along - 14 rotation steps for the `@_@` turn and 28 sprite-sheet steps for the tear scroll, 42 of the
249 morphs - and both were removed on purpose, see "Why the export carries no UV morphs" above.

The deploy-morph mechanism is described in `PMX_EXTENSIONS.md`; the game-side inventory is `MORPHS.md`.

### The audit: every extra morph, and where it stands

Re-counted against the current export of `1001` (207 morphs, **all vertex**) and against
`FaceDrivenKeyTarget.InitializeExtra`, which is the complete list of what the game builds:

| game morph (`OtherMorphs`, 8 for 1001) | mechanism | in the pmx today | addon today |
|---|---|---|---|
| `Cheek_Ctrl` | up to 3 `_CheekRate` textures, locator axes ScaX/Y/Z | **yes** - `Cheek_Blush` deploy + `morph=Cheek_Ctrl:Texture:ScaX:_CheekRate,ScaY:_CheekRate` on the cheek material + the baked `_blush` maps | yes |
| `Shade_Ctrl` | shader `_faceShadowAlpha` | **yes, as data only** - `morph=Shade_Ctrl:Shader:ScaX:_faceShadowAlpha` on the face material; no geometry, so no morph | **no** - parked, waiting for the face-region port to have an input |
| `Manga_Ctrl` | EyeSelect + Enable over 8 prefabs | **yes** - `Manga_*_L/R` deploy morphs, and the turn is the add-on's driver (no uv morphs) | yes |
| `StaticTear_Ctrl` | Enable over `tearmesh_l/r` | **yes** - `Tear_Static_L/R` | yes |
| `Tear0_Ctrl`…`Tear3_Ctrl` | TearWeight/Side/Select/Speed on a `TearController` | **partly** - 4 deploy morphs + `scroll=<cols>x<rows>` (the sheet uv morphs are gone); **the skeleton animation is not exported** | yes for the scroll, no for the roll |

The 192 remaining morphs are the ordinary facial ones (eye, brow, mouth, ear). Those come from the meshes' own
blend shapes (`ReadMorph` walks `mesh.blendShapeCount`), so they cannot be "missed" - whatever shape keys the
game's meshes carry is what gets exported.

So the audit finds exactly one true gap in the model data - the tear card's skeleton animation - and one piece of
data that is exported but unused (the face shadow). Everything else the game builds is either a deploy morph or a
slot the add-on drives from the comment tokens; no uv morphs are needed for any of it.


## The teary eye highlights

The effect that makes a character look like they are about to cry is not one of the five morphs: it is the
**eye highlights**, and it has no code in this viewer at all, which is why nothing in the UI reaches it.

What the evidence is:

* `Assets/Resources/Materials/UmaShaderEye.shader` draws three separate highlight maps over the eye -
  `_Highlight00`, `_Highlight01`, `_Highlight02` (`tex_chr*_eyehi00/01/02`) - sampled at a *different* UV from the
  eye itself (`altUv = (uv.x * 4, uv.y * 2)`) and gated by two thresholds, `_Show00` and `_Show01`, through
  `step()`. Their texture transforms are material properties (`_Highlight00_ST` …), i.e. animatable per material
  instance.
* The viewer never writes any of those: `Director.cs` only drives rim and chara colours. So the animation is the
  game's own: an intro where the highlight texture transform (and/or `_Show`) swings around the eye - the
  "swirl" - and then a small oscillation at a few hertz, the jitter that sells the wet look.
* The umamusume eye shader in the game is the same three-input shape as the one rebuilt here, so the effect is a
  *texture-transform animation on the highlight layers*, not new geometry.

Two ways this could come to MMD, both of them the same tool as the animated tears:

1. **Split the highlights into their own quads** (as the eye manga already are) and give each one a **stack of UV
   morphs** - a swirl is a rotation of the highlight quad's UVs, a jitter is a tiny oscillation - with the weights
   animated over time. This is the faithful route: MMD morphs then carry the whole effect and nothing needs the
   addon.
2. **Material morphs** on the eye material can change a *texture index* but not a texture transform, so an MMD
   material morph can switch which highlight map is shown but cannot scroll it. Not enough on its own.

## Animated morphs: where the recorder stands

A pmx carries no keys - a motion file (vmd) does. The standard 192 face morphs already animate: the recorder
builds one driver per morph name from the character's `FaceDrivenKeyTarget` morph lists
(`MorphRecorder` constructor, `UnityHumanoidVMDRecorder.cs:1497`), samples `morph.weight` every recorded frame
(`MorphDriver.RecordMorph`, line 1659) and drops frames that do not change (`DisableIntron`, line 1612).

What is missing is the other half of this document - the deploy morphs (`Cheek_Blush`, the eight eye manga,
`Tear_Static_*`, `Tear_Stream_*`/`Tear_Pool_*`). They have no `FacialMorph` to read: the game shows those parts by
moving a locator and enabling an object, so nothing in the sampled data is named `Cheek_Blush`. The design that fits
the existing code:

1. a second, small driver type next to `MorphDriver` that holds a *name* and a *sampler* instead of a list of
   `FacialMorph`s, with the same `ValueList` shape so `DisableIntron` and the writer treat it identically;
2. the sampler reads the game's own state for that part - `FacialOtherMorph.GetLocatorValue(part)`
   (`FacialMorph.cs:36`) against the bind's compare value, for a switch; for the eye manga, the `EyeSelect`
   value picks one of four variants per eye, so both `_L` and `_R` of the selected face go to 1;
3. `MorphRecorder` fills those drivers from `FaceDrivenKeyTarget.OtherMorphs` in its constructor
   (`OtherMorphs` is the same list the exporter reads for the deploy morphs) and `RecrodAllMorph` samples them
   alongside the face morphs;
4. the vmd writer's morph-section assembly needs to know about the extra names - that is the one piece not yet
   read, and the first thing to look at before writing code.

### The animated decals themselves

Three of the parts move *on their own*, independent of any morph weight, and none of that is a vertex morph:

* **the `@_@` eye manga spins.** The decal is a texture on its own quad; the spin is a rotation of the sampled UV.
  The port is the add-on's Vector Rotate node driven by `Spin_Speed`/`Spin`, and the uv-morph stack that was built
  for the same purpose has since been removed (see "Why the export carries no UV morphs").
* **some tear variants scroll a sprite sheet** (`TearController.UpdateOffset`) - the add-on's Mapping node steps
  over the grid the `scroll=<cols>x<rows>` token carries.
* **the eye highlights swirl and jitter** (see above) - driven on the material by `Hi_Swirl`/`Hi_Jitter`.

So all three are *material* animation keyed to the timeline, and none of them needs a morph in the pmx. If a vmd for
them is ever wanted, the carrier to reach for is the material animation MMD supports (uv *translation* only, which
covers the tear scroll but not the rotation) or bones, not uv morphs - the uv-morph route was tried, measured and
dropped.
