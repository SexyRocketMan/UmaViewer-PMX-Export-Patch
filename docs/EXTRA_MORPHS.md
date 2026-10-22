# The extra morphs: the full list, and what the export does with each

The game's face rig has two kinds of morph. The ordinary one (`FacialMorph`) is vertex offsets and exports as an
MMD vertex morph. The other kind (`FacialOtherMorph`) drives *material properties*, *textures*, *object
visibility* and an *animated tear system*, through a list of `BindProperty` entries. This is the complete
inventory of those, read from the code (`Assets/Scripts/umamusume/Gallop/FacialMorph.cs`,
`FaceDrivenKeyTarget.InitializeExtra()`, `TearController.cs`), and what the exporter does about each.

| morph | driven by (game) | what it does in the game | export |
|---|---|---|---|
| `Cheek_Ctrl` | locator scale X/Y/Z | `Texture` + `Shader` binds on the `M_Cheek` decal: `mainTexture = cheek0/1/2`, `_CheekRate` | **deploy morph `Cheek_Show`** + a baked blush map; the map pair stays in `Texture2D/` and the binding is in the material comment |
| `Shade_Ctrl` | locator scale X | `Shader` bind `_faceShadowAlpha` on the face material | nothing yet - an MMD material has no such field; the addon would have to drive the uma shader's face region |
| `Manga_Ctrl` | locator scale Y (select), scale X (enable) | `EyeSelect` + `Enable` over four manga prefabs, once per eye | **eight deploy morphs**, one per eye and expression (below) |
| `StaticTear_Ctrl` | locator position X (L), Y (R) | `Enable` on `tearmesh_l` / `tearmesh_r`, drawn with `tex_chr_tear00` | **deploy morphs `Tear_L`, `Tear_R`** |
| `Tear0_Ctrl` … `Tear3_Ctrl` | scale X (`TearWeight`), position Y (`TearSide`), scale Y (`TearSelect`), scale Z (`TearSpeed`) | the animated tears - see below | not exported; the tokens are in the model comment, the parts are not in the file |

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

## The animated tears (not exported)

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

MMD can hold the mesh and the attach bones but not the scroll or the stencil. A faithful port would animate the
scroll as a stack of **UV morphs** - one per sprite-sheet step, each shifting every UV by the same delta, with
the weight animated over time - which is a real experiment and not a guess; the stencil would have to become
geometry (the tear drawn slightly in front of the eye, which is what the static tears are already).

The sprite sheets themselves are inside the tear prefabs' asset tables, not in `Persistent/textures`, which is
why the viewer has never listed them.

## The teary-eye effect that the viewer cannot show

`tex_chr_tear00` is not only the animated tears' texture: `UmaContainerCharacter.cs:927` loads it and assigns it
as the *main texture* of the two static tear meshes (`tearmesh_l`, `tearmesh_r`), which is the shine that sits
on the lower lid when a character is about to cry. Those two meshes are in the export (as `Tear_L`/`Tear_R`
above), so the effect *is* reachable - what is not reachable from the viewer is the animated version, because
the viewer has no tear UI at all (the `Tear0`…`Tear3` controls are only ever written by the game's own face
clips). Settling whether there is a *third* mechanism - a shader input for wetness on the eye material - needs
a material probe: every material property whose name contains "tear"/"wet". No such property exists in the
shader set the viewer knows, but the probe is the evidence, not the absence.

## What the export carries today

* `Cheek_Show`, `Tear_L`, `Tear_R` - one deploy morph per decal part.
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

Animations on the deploy morphs (and on such UV morphs) are the other half of this: a pmx carries keys only in a
**vmd**, and the exporter's motion side (`UnityHumanoidVMDRecorder`) writes bone and the standard morph channels
but has no export for these extra morph names yet. That is what "animations on the morphs" means here - the
morphs exist and can be posed by hand, but nothing samples a game animation onto them yet.
