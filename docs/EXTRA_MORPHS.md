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
