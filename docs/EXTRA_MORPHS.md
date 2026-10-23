# The extra morphs: the full list, and what the export does with each

The game's face rig has two kinds of morph. The ordinary one (`FacialMorph`) is vertex offsets and exports as an
MMD vertex morph. The other kind (`FacialOtherMorph`) drives *material properties*, *textures*, *object
visibility* and an *animated tear system*, through a list of `BindProperty` entries. This is the complete
inventory of those, read from the code (`Assets/Scripts/umamusume/Gallop/FacialMorph.cs`,
`FaceDrivenKeyTarget.InitializeExtra()`, `TearController.cs`), and what the exporter does about each.

| morph | driven by (game) | what it does in the game | export |
|---|---|---|---|
| `Cheek_Ctrl` | locator scale X/Y/Z | `Texture` + `Shader` binds on the `M_Cheek` decal: `mainTexture = cheek0/1/2`, `_CheekRate` | **deploy morph `Blush`** + a baked blush map; the map pair stays in `Texture2D/` and the binding is in the material comment |
| `Shade_Ctrl` | locator scale X | `Shader` bind `_faceShadowAlpha` on the face material | nothing yet - an MMD material has no such field; the addon would have to drive the uma shader's face region |
| `Manga_Ctrl` | locator scale Y (select), scale X (enable) | `EyeSelect` + `Enable` over four manga prefabs, once per eye | **eight deploy morphs**, one per eye and expression (below) |
| `StaticTear_Ctrl` | locator position X (L), Y (R) | `Enable` on `tearmesh_l` / `tearmesh_r`, drawn with `tex_chr_tear00` | **deploy morphs `Tear_L`, `Tear_R`** |
| `Tear0_Ctrl` … `Tear3_Ctrl` | scale X (`TearWeight`), position Y (`TearSide`), scale Y (`TearSelect`), scale Z (`TearSpeed`) | the animated tears - see below | **deploy morphs `TearDrop1_L` … `TearDrop4b_L`** plus the sprite-sheet `_scrN` UV morphs of each (left side: the viewer never places the right one); the control tokens stay in the model comment |

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

MMD can hold the mesh but not the scroll or the stencil, so the export turns the scroll into a stack of **UV
morphs**: one per sprite-sheet frame, each shifting every UV by that frame's step, capped at 8 steps so a large
sheet does not fill the morph list (a 66-vertex tear gets `TearDrop1_L` and `TearDrop1_L_scr0` … `_scr6`). Frame
0 is the rest state, so a running tear is the deploy morph at 1 with the scroll morphs cycled through - the rate
is how fast the weights fade. The stencil has no counterpart and is not needed: the tear is its own decal
geometry, drawn in front of the eye by the same deploy lift every decal gets. Both sides are in the export: the four controllers are not all on
the same eye - `TearDrop1_*` and `TearDrop3_*` come out `_L`, `TearDrop2_*` and `TearDrop4_*` come out
`_R` - because the viewer places each controller on the side its own `SetDir` chose. A controller that is
mirrored at runtime rather than placed would need its right-side instance created at export time.

The sprite sheets themselves are inside the tear prefabs' asset tables, not in `Persistent/textures`, which is
why the viewer has never listed them.

### The sign of a UV morph, which cost a bug

The pmx stores uv **top-down**. That is why vertex uvs are written `1 - v`, and mmd_tools flips them back on
import (`pmx/importer.py: flipUV_V`), so static uvs round-trip unchanged. A uv *morph* is a delta, and the
importer **negates** a delta's v rather than flipping it (`(x, -y, z, -w)`), so a delta has to be stored with the
sign the game does *not* use. The tear scroll had this wrong: its base cell sits at v `[0, 1/rows]`, and the seven
steps of `1/rows` land the uvs at exactly 1.0 - the far edge of the sheet - when stored `+v`, and at **-0.875**,
off the sheet entirely, when stored `-v`, which is what shipped. A stack that walks the uvs off the sheet cannot
scroll through it, so the drip was never going to run the right way. The `@_@` rotation writes its deltas the same
way and has not been re-measured: a mirrored rotation looks plausible on a symmetrical decal, so if the dizzy face
turns the wrong way, suspect the same thing.

### What actually poses the card: nothing in the assets

`headless_export.ps1 -ProbeTears` dumps the runtime objects (see the handoff), and it answers "why does the tear
card stand up over the eye instead of running down the cheek" with: no shipped data gives it a pose.

| what could pose it | measured |
|---|---|
| the clip | `anm_chr_tear000` **and** the base `anm_tear000_00` both have **0 curve bindings** — they are timers for the sprite scroll (4.267 s and 1.0 s long) |
| a spring | no `CySpring`, `CySpringDataContainer` or `DynamicBone` on either prefab, and no spring container lists a `joint*` bone name |
| the attach bones | `Eye_tear_attach_01..03_L/R` all carry local rotation 0; their up vector is world +Y |
| the placement | `SetDir` sets `transform.position`, and `localScale = (-1,1,1)` for the right eye — it never sets a rotation |
| the animator running | `BakeMesh` is identical at t = 0, 0.25, 0.5, 0.75, 1 and after 45 played frames |

So the card is a **rigid ribbon in its rest pose**, pointing straight up along world +Y from whichever attach bone
placed it. For 1001 that is `pfb_chr_tear000`: 66 vertices skinned to an 11-joint chain, 0.010 wide by 0.046
tall, which lands in the pmx as **0.603 units from Y 17.495 to 18.098** while the eye corner
(`Eye_tear_attach_03_L`) is at 17.455 and the `Chin` bone at 16.466. It is roughly the right length for the trip
to the chin but pointing the wrong way, and about 40% short of actually reaching it.

The game must pose those joints in code — IL2CPP, and there is no `global-metadata.dat` to decompile (see the
handoff). Reconstructing the pose is therefore a choice rather than an extraction. The defensible one uses the
rig's own landmarks instead of an invented angle: aim the ribbon from `Eye_tear_attach_03_*` at the `Chin` bone,
and scale its length to the distance between them (1.225 units against the ribbon's 0.603, so about 2x on Y).

### The four controllers are not four positions

Worth knowing before reading the morph list. `SetDir` puts *both* of a controller's prefabs on the same eye -
`TearObject_0` at `Eye_tear_attach_03_L/R`, `TearObject_1` at `attach_02_L/R` - and the per-controller spacing in
the game comes from `TearSide`, a position-Y offset that the face clips drive. Nothing drives it by default, so
controllers 1 and 3 land on the left eye at the same place and 2 and 4 on the right: measured,
`TearDrop1_L` and `TearDrop3_L` hold **identical** offsets (same shape, same position, different vertices), as do
`TearDrop2_R`/`TearDrop4_R` and the `b` pair. That is also why there is no `TearDrop3_R` or `TearDrop4_L`: the
number is the *controller*, and a controller only ever sits on one eye. Two stacked decals and four redundant
morphs are the price of exporting the viewer's default placement rather than the game's runtime one.

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

* `Blush`, `Tear_L`, `Tear_R` - one deploy morph per static decal part.
* `TearDrop1_L` … `TearDrop4b_L` - one deploy morph per animated tear object, each with its `_scr0` … `_scr6`
  sprite-sheet scroll steps.
* `@_@_L_spin1` … `@_@_R_spin7` - the dizzy face's turn, seven UV morphs per eye at 45° steps (203 vertex
  morphs and 14 UV morphs on character 1001). You spin it by cross-fading neighbours: `spin1` → `spin2` → … →
  `spin7` → back to none, which is the 8th step and lands on the rest orientation.
* `@_@_L`, `@_@_R`, `✦_✦_L`, `✦_✦_R`, `>_<_L`, `>_<_R`, `o_o_L`, `o_o_R` - the eye manga.
* The five `morph=` / `object morphs:` token groups in the comments, for the parts a vertex morph cannot hold.
* `Texture2D/tex_chr1001_00_cheek0_blush.png` and `…cheek1_blush.png` - the blush baked for alpha blending,
  beside the game's own maps.

The deploy-morph mechanism is described in `PMX_EXTENSIONS.md`; the game-side inventory is `MORPHS.md`.

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

What is missing is the other half of this document - the deploy morphs (`Blush`, the eight eye manga,
`Tear_L`/`Tear_R`) and any UV morphs. They have no `FacialMorph` to read: the game shows those parts by moving a
locator and enabling an object, so nothing in the sampled data is named `Blush`. The design that fits the
existing code:

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

* **the `@_@` eye manga spins.** The decal is a texture on its own quad; the spin is a rotation of the sampled
  UV, so a port is a **stack of UV morphs**: `K` morphs each rotating the quad's UVs about their own centre by
  `360/K`, and a continuous spin is an animation that cross-fades consecutive pairs (`w_k` down while `w_{k+1}`
  goes up) - linear weights over a linear UV delta add up to a linear rotation, so the cross-fade is exact.
  LibMMD already reads and writes UV morphs (`Morph.cs:54`, `PMXWriter.cs:188`), so the exporter side is a
  function that emits them for a part whose face is `@_@`, and the vmd side is a UV morph section.
* **some tear variants scroll a sprite sheet** (`TearController.UpdateOffset`) - same UV morph stack, stepped
  through the sheet's rows and columns instead of rotated.
* **the eye highlights swirl and jitter** (see above) - the same stack again, on highlight quads.

They all reduce to one mechanism: *UV morphs with animated weights*. A vmd carries UV morph keys by morph
**name** in its own section, next to the vertex-morph section, which is why the extra morph names have to exist
in the pmx first - which they now do for the deploy parts, and would need to for the spin/scroll morphs.
