# The extra morphs: the full list, and what the export does with each

The game's face rig has two kinds of morph. The ordinary one (`FacialMorph`) is vertex offsets and exports as an
MMD vertex morph. The other kind (`FacialOtherMorph`) drives *material properties*, *textures*, *object
visibility* and an *animated tear system*, through a list of `BindProperty` entries. This is the complete
inventory of those, read from the code (`Assets/Scripts/umamusume/Gallop/FacialMorph.cs`,
`FaceDrivenKeyTarget.InitializeExtra()`, `TearController.cs`), and what the exporter does about each.

| morph | driven by (game) | what it does in the game | export |
|---|---|---|---|
| `Cheek_Ctrl` | locator scale X/Y/Z | `Texture` + `Shader` binds on the `M_Cheek` decal: `mainTexture = cheek0/1/2`, `_CheekRate` | **deploy morph `Cheek_Blush`**; the map set stays in `Texture2D/` and the binding is in the material comment (no baked copy is written) |
| `Shade_Ctrl` | locator scale X | `Shader` bind `_faceShadowAlpha` on the face material | nothing yet - an MMD material has no such field; the addon would have to drive the uma shader's face region |
| `Manga_Ctrl` | locator scale Y (select), scale X (enable) | `EyeSelect` + `Enable` over four manga prefabs, once per eye | **eight deploy morphs**, one per eye and expression (below) |
| `StaticTear_Ctrl` | locator position X (L), Y (R) | `Enable` on `tearmesh_l` / `tearmesh_r`, drawn with `tex_chr_tear00` | **deploy morphs `Tear_Static_L`, `Tear_Static_R`** |
| `Tear0_Ctrl` … `Tear3_Ctrl` | scale X (`TearWeight`), position Y (`TearSide`), scale Y (`TearSelect`), scale Z (`TearSpeed`) | the animated tears - see below | **deploy morphs `Tear_Pool_L/R`** (deduplicated, one per distinct card) plus the `scroll=<cols>x<rows>` grid in the material comment; **the rolling `Tear_Stream_*` card is not exported** (see "The rolling card is not exported") |

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
eye on the left eye and nothing else moves. The names the export uses are `Manga_Spin_L/R` (`@_@`),
`Manga_Star_L/R` (`✦_✦`), `Manga_Shut_L/R` (`>_<`) and `Manga_Wide_L/R` (`o_o`).

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

MMD can hold neither the scroll nor the stencil. The scroll travels as the `scroll=<cols>x<rows>` grid in the
material comment, which the add-on steps a Mapping node over, so a running tear is the deploy morph at 1 with its
texture walking the sheet. The stencil has no counterpart and is not needed: the tear is its own decal geometry,
drawn in front of the eye by the same deploy clearance every decal gets (a fixed fraction of the part's distance
from the head bone, along its own normal - see "The deploy lift is a normal, not a radius" below).

Only the **wide** card is exported. Both sides are in the file: the four controllers are not all on the same eye,
and the cards come out as `Tear_Pool_L/R` because the viewer places each controller on the side its own `SetDir`
chose, with the four controllers deduplicating onto those two. The tall rolling card is a separate decision,
below.

### The rolling card is not exported

`TearObject_0` - the tall `tear000` stream that rolls from the eye corner down the cheek and off the chin - gets
**no deploy morph** and **no scroll token**. Its vertices stay on the collapse point, so it is not in the model.

The whole effect is the roll, the roll lives in the card's skeleton animation, and exporting that is unsolved
("Shelved: exporting the roll" below), so a tear that cannot move is left out rather than shipped standing still
at the eye corner. To bring it back as a static tear, run `TearMorphs` over `TearObject_0` as well and allow
`tear.TearObject_0` in `ExtraMorphGrid`.

What is left of the tear system in the pmx is: a deploy morph each for `Tear_Pool_L/R` and `Tear_Static_L/R`, the
`scroll=` grid on the pool card's materials, and the tear control tokens in the model comment. The sprite sheets
themselves live inside the tear prefabs' asset tables, not in `Persistent/textures`, which is why the viewer has
never listed them.

### Why the export carries no UV morphs

Both animated decals travelled as **stacks of uv morphs** - seven rotations for the `@_@` turn, seven
sprite-sheet steps per tear card - and both stacks were removed. The pmx carries no uv morph at all, and
character 1001 went from 249 morphs to 207.

Nothing in the pipeline could use them. Blender cannot animate a uv layer - a shape key moves vertices, not
texture coordinates - so the add-on animates both effects in the *material*: a Vector Rotate node driven by
`Spin_Speed`/`Spin` for the turn, a Mapping node stepped by `Tear_Scroll` over the `scroll=<cols>x<rows>` grid
for the tear sheet. The morphs were 42 sliders in every character's morph list that no tool in this pipeline
reads, on effects already animated by other means. They also carried a live hazard: `mmd_tools` imports them as
`mmd_root.uv_morphs` groups, so pressing *Apply UV Morph* on one bakes that rotation into the decal's uv
coordinates for good.

What that costs: a consumer **without** the add-on gets neither effect. In plain MMD a material animation can
only *translate* uvs (no rotation) and a comment is not readable, so the uv-morph route was the only way they
existed outside this pipeline; the add-on drives them from the tokens instead.

#### The sign trap, which is worth keeping even though the code is gone

The pmx stores uv **top-down**. That is why vertex uvs are written `1 - v`, and mmd_tools flips them back on
import (`pmx/importer.py: flipUV_V`), so static uvs round-trip unchanged. A uv *morph* is a delta, and the
importer **negates** a delta's v rather than flipping it (`(x, -y, z, -w)`), so a delta has to be stored with the
sign the game does *not* use. The tear scroll had this wrong: its base cell sits at v `[0, 1/rows]`, and seven
steps of `1/rows` land the uvs at exactly 1.0 - the far edge of the sheet - when stored `+v`, and at **-0.875**,
off the sheet entirely, when stored `-v`, which is what shipped. A stack that walks the uvs off the sheet cannot
scroll through it. Anyone who writes a uv morph again should start from this paragraph.

The removed rotation stack was exact: every `Manga_Spin_*_TurnN` was a pure rotation about the centre of 0..1 uv
space, walking `0, 45, 90, 135, 180, -135, -90, -45` degrees - one direction, 45° per step, wrapping through ±180
so that `Turn7` plus one more step returned to the identity, both eyes carrying the same angles. The turn is the
add-on's driver, in one direction only; `Spin_Speed` scales it and 0 stops it.

### The deploy lift is a normal, not a radius

An offset is only *clearance* along the normal of the surface a decal is painted on. An offset along the
**radius from the head bone** instead - 6% of the distance from that bone - is wrong twice over: the radius is not
the surface normal (the eye manga sat 25° off their own normal, the left static tear 48°), so most of the offset
slides the decal *across* the face instead of off it, and the clearance that comes out depends on the part's angle
rather than being a constant:

| decal | clearance before | sideways travel before |
|---|---|---|
| `Cheek_Blush` | 0.0618 | 0.0300 |
| `Manga_*` | 0.0629 | 0.0610 |
| `Tear_Static_L` / `_R` | 0.0389 / 0.0599 | 0.0737 / 0.0579 |
| `Tear_Stream_*` | 0.0585 | 0.0715 |
| `Tear_Pool_*` | 0.0625 | 0.0509 |

(The `Tear_Stream_*` row was measured while that card was still exported; it is not any more, see "The rolling
card is not exported".)

The offset is along each part's own normal (taken from the mesh, flipped to point away from the head bone so a
mirrored instance cannot invert it), and the clearance is `0.046 × the part's mean distance from the head bone`:

| decal | clearance | sideways travel |
|---|---|---|
| `Cheek_Blush` | 0.0612 | 0.000000 |
| `Manga_*` | 0.0686 … 0.0703 | 0.000000 |
| `Tear_Static_*` | 0.0641 | 0.000000 |
| `Tear_Pool_*` | 0.0622 | 0.000000 |

**It has to stay a fraction.** The export has no physical unit: the coordinates are the character container's,
and the pmx round trip multiplies them again - the head bone reads `1.34` inside the exporter and `16.79` in the
file, 12.5×. An absolute clearance is therefore a different physical distance in every export: a "constant"
`0.063` comes out as `0.79` in the file and lifts every decal most of a unit clear of the face.

### The card is a skeleton animation

The card is not a static mesh and it is not sprite-only: it is a small **skeleton animation**, and the whole
motion is in the data. What the setup is:

| part of the setup | what it is |
|---|---|
| the mesh | `SkinnedMeshRenderer`, 66 verts on an 11-joint chain (`joint000`…`joint010`, 5 mm apart) for `tear000`; 36 verts on 3 joints for `tear001` |
| the animator | present and enabled, with its own generic avatar `mdl_chr_tear000Avatar` (`valid=True`, `human=False`) |
| the controller | created at run time: `TearController` builds an `AnimatorOverrideController` over `anm_chara_tear_animator` and overrides the state `anm_tear000_00` with the prefab's `default_animation` |
| the clips | 4.267 s, looping, **and `AnimationUtility.GetCurveBindings` reports 0 curves for them** - which says nothing about the animation, because that API only lists generic transform curves |

Sampling the animator through the clip (`animator.Play(0, 0, t)` then `BakeMesh`, with
`animator.cullingMode = AnimatorCullingMode.AlwaysAnimate` set first) gives the real motion. Positions are local
to the attach point, so negative Y is down the face; the tall card, controller 2:

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
`tear001` card does not move at all (0 of 10 steps) - only the tall one animates.

The card's rest pose is a stub pointing up from the eye corner, and it measures 0.603 pmx units from Y 17.495
to 18.098 while the eye corner (`Eye_tear_attach_03_L`) is at 17.455 and the `Chin` bone at 16.466 - which is why
an export that copies the rest pose reads as a small quad above the eye.

### Shelved: exporting the roll

The motion above is not exported: the measurement stands, and the carrier was what went wrong.

**What it was.** `TearRollTimes = {0.2, 0.5, 0.9}` - the corner, mid-cheek, the chin - baked as three more vertex
morphs per animated card, each offset measured from the deployed pose:

```
TearDrop1_L            deploy, the card at the eye corner
TearDrop1_L_roll1..3   the three sampled poses
```

and a `roll=<seconds>:<t1>,<t2>,t3>` material token so a consumer knew the loop and where the poses sit in it. In
the addon, `build_tear_roll` drove the three weights piecewise-linearly from a `Tear_Roll` property (and a
`Tear_Roll_Phase` property holding the loop position, because inlining the phase into the weight expressions made
one of them 267 characters and Blender's driver parser rejected it). Measured working: the card travelled from Z
1.442 at frame 1 to 1.334 at frame 90, the eye corner to the chin, weights crossfading 1.00/0 → 0.69/0.31 →
0/0.79/0.21 → 0/0.05/0.95.

**Why it is shelved.** It cost 12 of 293 morphs (four cards × three poses) for one small card, where the whole
rest of the advanced set costs 89. A pmx morph cannot hold a curve, so the animation *is* the crossfade between
stored poses: in plain `mmd_tools` the three morphs are inert keys, unlike every other advanced morph, whose deploy
keys still work as shape keys with no addon. And piecewise-linear between three poses is visible on a flat quad,
while the sprite scroll underneath already carries most of the "tear running down" reading.

**What was kept:** the tear scroll's V-sign fix, the `headless_export.ps1 -ProbeTears` probe,
`Assets/Editor/UmaTearWatch.cs`, and every measurement on this page.

**The revisit route is the carrier, not the sampler.** The card is a *skinned mesh on an 11-joint chain*, so the
honest export is **bones plus a VMD**: the joints as pmx bones with per-vertex weights and the sampled pose baked
into bone curves. That costs no morphs, works in plain `mmd_tools` and MMD, and is what the game itself uses.

### Traps worth not rediscovering

* **`Animator.cullingMode`** - in the default mode an animator does not write bone transforms while its renderer
  is not being drawn, which in a headless run is *always*. `BakeMesh` then returns the rest pose and the clip looks
  like it animates nothing. Set `AlwaysAnimate` before sampling. The same reason makes wall-clock sampling
  useless in batch: two seconds advance the state by about 1% of the clip, so step `Play(0, 0, t)` through
  normalised time instead.
* **`AnimationUtility.GetCurveBindings` reports generic curves only**, so a clip that animates perfectly can report
  `bindings=0`. Judge a clip by posing through it, not by counting its curves.
* For drivers to survive at all: a face separation gives the new object a **copy** of the shape-key datablock, so
  any driver on the original key does not follow it.

### The four controllers are not four positions

`SetDir` puts *both* of a controller's prefabs on the same eye - `TearObject_0` at `Eye_tear_attach_03_L/R`,
`TearObject_1` at `attach_02_L/R` - and the per-controller spacing in the game comes from `TearSide`, a
position-Y offset the face clips drive and this viewer never does. So controllers 1 and 3 land on the left eye at
the same place and 2 and 4 on the right: measured, the controller-1 and controller-3 cards hold **identical**
offsets (same shape, same position, different vertices), as do the two on the right. The exporter therefore
deduplicates on the placed geometry and writes one morph per distinct card, `Tear_Pool_L/R`. Two stacked decals
are the price of exporting the viewer's default placement rather than the game's runtime one.

## The teary-eye effect that the viewer cannot show

`tex_chr_tear00` is not only the animated tears' texture: `UmaContainerCharacter` loads it and assigns it as the
*main texture* of the two static tear meshes (`tearmesh_l`, `tearmesh_r`), which is the shine that sits on the
lower lid when a character is about to cry. Those two meshes are in the export (as `Tear_Static_L/R`), so the
effect *is* reachable. The animated version is reachable too: the viewer's **Other** morph list builds a slider
for every `Tear0_Ctrl`…`Tear3_Ctrl` bind (`UmaViewerUI.cs`, the `TearWeight`/`TearSide`/`TearSelect`/`TearSpeed`
cases), and what that shows is the sprite rolling with the card staying where it is - `UpdateOffset` writes
`_TexScrollParam` and nothing else moves.

Whether a *third* mechanism exists - a shader input for wetness on the eye material - is open. No material
property whose name contains "tear"/"wet" appears in the shader set the viewer knows, but that is an absence of
names, not a probe.

## What the export carries today

Every advanced morph is named `<feature>_<what it does>_<side>`, so the set groups together in a morph list:
`Cheek_Blush`, `Manga_Spin_L`, `Tear_Static_L`, `Tear_Pool_L`. Before 2.34 the names were the game's own
spelling - the emoticons `@_@_L`, `✦_✦_L`, `>_<_L`, `o_o_L`, then `Tear_L`/`Tear_R` and `TearDrop1_L` …
`TearDrop4b_R` - and models exported then keep working: the add-on matches both spellings everywhere.

* `Cheek_Blush`, `Tear_Static_L`, `Tear_Static_R` - one deploy morph per static decal part.
* `Tear_Pool_L/R` - one deploy morph per *distinct* wide card (`tear001`). The tall rolling `tear000` card is not
  exported at all; see "The rolling card is not exported".
* `Manga_Spin_L/R`, `Manga_Star_L/R`, `Manga_Shut_L/R`, `Manga_Wide_L/R` - the eye manga, one per expression
  (`@_@`, `✦_✦`, `>_<`, `o_o`).
* The four controllers do not produce four cards a side - controllers 1 and 3 carry the same card, and they are
  deduplicated on the placed geometry (see "The four controllers are not four positions"), which keeps two deploy
  morphs out of the file and stops the decal being drawn twice.
* The five `morph=` / `object morphs:` token groups in the comments, for the parts a vertex morph cannot hold.
* The cheek's maps exactly as the game ships them. There is no alpha-only bake beside them: the addon multiplies
  the game's own map and derives the transparency from it at shade time, the same recipe its "Fix mini umamusume
  model" operator uses for minis. See the `blend=multiply` section of [`PMX_EXTENSIONS.md`](PMX_EXTENSIONS.md).

**There are no UV morphs**, and character 1001 exports **205** morphs, all of them vertex morphs: 192 ordinary
facial ones and 13 deploy morphs. Two uv-morph stacks rode along before - 14 rotation steps for the `@_@` turn
and 28 sprite-sheet steps for the tear scroll, 42 of what were then 249 morphs - and both were removed on purpose
("Why the export carries no UV morphs" above); the rolling tear card's deploy morphs went with it, which took the
deploy count from 15 to 13. The deploy-morph mechanism is described in `PMX_EXTENSIONS.md`; the game-side
inventory is `MORPHS.md`.

### The audit: every extra morph, and where it stands

Counted against the current export of `1001` (205 morphs, **all vertex**: 192 ordinary + 13 deploy) and against
`FaceDrivenKeyTarget.InitializeExtra`, which is the complete list of what the game builds:

| game morph (`OtherMorphs`, 8 for 1001) | mechanism | in the pmx today | addon today |
|---|---|---|---|
| `Cheek_Ctrl` | up to 3 `_CheekRate` textures, locator axes ScaX/Y/Z | **yes** - `Cheek_Blush` deploy + `morph=Cheek_Ctrl:Texture:ScaX:_CheekRate,ScaY:_CheekRate,ScaZ:_CheekRate` on the cheek material | yes |
| `Shade_Ctrl` | shader `_faceShadowAlpha` | **yes, as data only** - `morph=Shade_Ctrl:Shader:ScaX:_faceShadowAlpha` on the face material; no geometry, so no morph | **no** - parked, waiting for the face-region port to have an input |
| `Manga_Ctrl` | EyeSelect + Enable over 8 prefabs | **yes** - `Manga_*_L/R` deploy morphs, and the turn is the add-on's driver (no uv morphs) | yes |
| `StaticTear_Ctrl` | Enable over `tearmesh_l/r` | **yes** - `Tear_Static_L/R` | yes |
| `Tear0_Ctrl`…`Tear3_Ctrl` | TearWeight/Side/Select/Speed on a `TearController` | **partly** - 2 deploy morphs (`Tear_Pool_L/R`) + `scroll=<cols>x<rows>`; **the skeleton animation is not exported** | yes for the scroll, no for the roll |

The 192 remaining morphs are the ordinary facial ones (eye, brow, mouth, ear), which come from the meshes' own
blend shapes (`ReadMorph` walks `mesh.blendShapeCount`): whatever shape keys the game's meshes carry is what gets
exported. The audit therefore finds one gap in the model data - the tear card's skeleton animation, which is also
why the rolling card is not exported at all - and one piece of data exported but unused (the face shadow).
Everything else the game builds is either a deploy morph or a slot the add-on drives from the comment tokens.

## The teary eye highlights

The effect that makes a character look like they are about to cry is not one of the five morphs but the **eye
highlights**, and this viewer has no code for it, which is why nothing in the UI reaches it.
`Assets/Resources/Materials/UmaShaderEye.shader` draws three highlight maps over the eye - `_Highlight00`,
`_Highlight01`, `_Highlight02` (`tex_chr*_eyehi00/01/02`) - sampled at a *different* UV from the eye itself
(`altUv = (uv.x * 4, uv.y * 2)`), gated by two thresholds (`_Show00`, `_Show01`, through `step()`) and placed by
material properties (`_Highlight00_ST` …), i.e. animatable per material instance. `Director.cs` writes none of
them, so the animation is the game's own: the highlight texture transform swings around the eye (the "swirl") and
then oscillates at a few hertz (the jitter). The effect is a texture-transform animation on the highlight layers,
not new geometry, so it reaches MMD only through their own quads (as the eye manga already have) carrying **UV
morphs** - a swirl is a rotation of a quad's UVs - or, insufficiently, through material morphs, which can change a
texture *index* but not a texture transform.

## Animated morphs: where the recorder stands

A pmx carries no keys - a motion file (vmd) does. The standard 192 face morphs animate: the recorder builds one
driver per morph name from the character's `FaceDrivenKeyTarget` morph lists (`MorphRecorder` constructor,
`UnityHumanoidVMDRecorder.cs`), samples `morph.weight` every recorded frame (`MorphDriver.RecordMorph`) and drops
frames that do not change (`DisableIntron`).

What is missing is the other half of this document - the deploy morphs (`Cheek_Blush`, the eight eye manga,
`Tear_Static_*`, `Tear_Pool_*`). They have no `FacialMorph` to read: the game shows those parts by moving a
locator and enabling an object, so nothing in the sampled data is named `Cheek_Blush`. The design that fits the
existing code:

1. a second driver type next to `MorphDriver` holding a *name* and a *sampler* instead of a list of
   `FacialMorph`s, with the same `ValueList` shape so `DisableIntron` and the writer treat it identically;
2. a sampler that reads the game's own state - `FacialOtherMorph.GetLocatorValue(part)` (`FacialMorph.cs:36`)
   against the bind's compare value for a switch, and the `EyeSelect` value for the manga, where both `_L` and
   `_R` of the selected face go to 1;
3. `MorphRecorder` filling those drivers from `FaceDrivenKeyTarget.OtherMorphs` in its constructor (the same list
   the exporter reads for the deploy morphs), with `RecrodAllMorph` sampling them alongside the face morphs;
4. the vmd writer's morph-section assembly, which has to know about the extra names - the one piece not yet read.

### The animated decals themselves

Three of the parts move *on their own*, independent of any morph weight, and none of that is a vertex morph:

* **the `@_@` eye manga spins.** The decal is a texture on its own quad; the spin is a rotation of the sampled UV.
  The carrier is the add-on's Vector Rotate node driven by `Spin_Speed`/`Spin`; there is no uv-morph stack (see
  "Why the export carries no UV morphs").
* **some tear variants scroll a sprite sheet** (`TearController.UpdateOffset`) - the add-on's Mapping node steps
  over the grid the `scroll=<cols>x<rows>` token carries.
* **the eye highlights swirl and jitter** (see above) - driven on the material by `Hi_Swirl`/`Hi_Jitter`.

So all three are *material* animation keyed to the timeline, and none of them needs a morph in the pmx. If a vmd
for them is ever wanted, the carrier to reach for is the material animation MMD supports (uv *translation* only,
which covers the tear scroll but not the rotation) or bones, not uv morphs.
