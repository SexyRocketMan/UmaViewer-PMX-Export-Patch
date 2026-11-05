# The extra morphs: everything that is not a vertex blend shape

The game's face rig has two kinds of morph. `FacialMorph` is the familiar one — vertex/TRS offsets that become a shape key and export as a PMX vertex morph. `FacialOtherMorph` is the other kind, and it is what this document is about: it does not touch vertices at all, it drives *material properties*, *textures*, *object visibility* and a *particle system*, through a list of `BindProperty` entries.

All of it is in `Assets/Scripts/umamusume/Gallop/FacialMorph.cs` (the classes) and `Assets/Scripts/umamusume/Gallop/FaceDrivenKeyTarget.cs` (what builds and applies them).

## The inventory

The set is not data — it is built in code, in `FaceDrivenKeyTarget.InitializeExtra()` (`FaceDrivenKeyTarget.cs:209`), from whatever the character's head actually has. On a normal character that is these five, and nothing else:

| morph | locator | what its binds do |
|---|---|---|
| `Cheek_Ctrl` | `Cheek_Ctrl` | up to three `Texture` binds on the `M_Cheek` decal: `mainTexture = CheekTex_0/1/2`, `_CheekRate` = value. Driven by the locator's scale **X**, **Y**, **Z** — one axis per cheek texture. |
| `Shade_Ctrl` | `Shade_Ctrl` | one `Shader` bind: `_faceShadowAlpha` on the `M_Face` material, driven by scale **X**. |
| `Manga_Ctrl` | `Manga_Ctrl` | `EyeSelect` over the left and right eye-manga objects (scale **Y**) and an `Enable` over the same set (scale **X**). |
| `StaticTear_Ctrl` | `Cheek_Ctrl` | two `Enable` binds, `StaticTear_L` and `StaticTear_R`, driven by position **X** and **Y**. |
| `Tear{index}_Ctrl` | `Tear{index}_Ctrl` | per tear controller: `TearWeight` (scale **X**), `TearSide` (position **Y**), `TearSelect` (scale **Y**), `TearSpeed` (scale **Z**). |

The important structural point: **the animation does not move the morph, it moves the locator.** `Cheek_Ctrl`, `Shade_Ctrl`, `Manga_Ctrl` and the tear locators are ordinary transforms in the head rig, and the game's driven-key clips animate them (`anm_*_face_driven`, `anm_*_ear_driven`, `anm_chr*_tear*_driven` in `3d/motion/.../facial/`). A recorded motion therefore already carries the *input* to these morphs; an export has to carry the *response*.

What the export carries of that response, in outline: the parts these morphs show and hide (the cheek blush, the four eye manga, the static tears, the wide animated tear card) are collapsed onto a point inside the head at rest and one vertex morph per part deploys it — `Cheek_Blush`, `Manga_Star_L` … `Manga_Wide_R`, `Tear_Static_L/R`, `Tear_Pool_L/R` — so a plain MMD viewer can show them without any addon. What a vertex morph still cannot hold is the cheek's *texture pair and rate* and the face shadow's `_faceShadowAlpha`, which stay as `morph=` tokens in the material comments; the two *animated* decals (the `@_@` turn and the tear sheet scroll) are driven by the add-on from those tokens and ship with no uv morphs at all, and the tall rolling tear card is not exported. The contract is [`PMX_EXTENSIONS.md`](PMX_EXTENSIONS.md).

## The bind types and exactly what they do

`FacialMorph.cs:65` lists nine; `FaceDrivenKeyTarget.ProcessExtraMorph` (`FaceDrivenKeyTarget.cs:504`) applies them:

| bind type | what it does |
|---|---|
| `Texture` | with `Value > 0.01`: activate the bound object, set the material's `mainTexture` to the bound texture and `SetFloat(PropertyName, Value)`. With no property left above zero, deactivate the object. |
| `Shader` | `SetFloat(PropertyName, Value)` on a material. |
| `Select` | activate the prefab whose index equals `Value`, deactivate the rest. |
| `EyeSelect` | activate prefab `i` and prefab `i + count/2` for `Value == i`: the left/right pair of one variant. |
| `Enable` | activate when `Value > 0` (one prefab), otherwise deactivate all (several prefabs). |
| `TearSide` | `BindTearController.SetDir((int)Value)`. |
| `TearWeight` | `BindTearController.SetWegiht(Value)`. |
| `TearSelect` | `BindTearController.SetObject((int)Value)`. |
| `TearSpeed` | `BindTearController.SetSpeed(Value)`. |

## What an export can carry, per morph

"PMX" is what a `.pmx` can express as a morph; "Blender" is what an addon can drive on the imported model. PMX morphs cover vertices, UVs, materials (diffuse/specular/ambient/edge colours, specular power, texture and sphere/toon tints), bones and groups — there is no object visibility, no texture swapping and no custom shader properties.

| morph | PMX | Blender | verdict |
|---|---|---|---|
| `Cheek_Ctrl` | A decal material's tint/alpha can be animated by a material morph; the *texture swap* cannot. | Swap the image on the decal material and drive `_CheekRate` from the locator's scale. | **deploy morph plus tokens**: `Cheek_Blush` shows the decal; the game's own map is exported unchanged and the `blend=multiply` / `morph=` tokens tell the add-on to swap the image and set the rate. |
| `Shade_Ctrl` | Nothing — no shader property in MMD. | The uma shader node group already has `_faceShadowAlpha` as an input; a driver from `Shade_Ctrl`'s scale X closes the loop. | **data only**: `morph=Shade_Ctrl:Shader:ScaX:_faceShadowAlpha` in the face material comment. No geometry, so no morph, and the add-on side is not built. |
| `Manga_Ctrl` | Nothing — MMD cannot show/hide objects. | Object visibility, driven from the locator. | **eight deploy morphs**, one per expression and eye, plus the add-on's driver for the turn. |
| `StaticTear_Ctrl` | Nothing. | Object visibility. | **deploy morphs** `Tear_Static_L/R`. |
| `Tear{n}_Ctrl` | Nothing. | Nothing sane — tears are a particle system with weight/side/object/speed. | **partly**: `Tear_Pool_L/R` deploy morphs and the `scroll=<cols>x<rows>` grid. The rolling card's skeleton animation is the one part of the feature still unexported. |

## What the exporter does

* `FacialMorph`s (eyebrow, eye, mouth, ear) are exported as PMX vertex morphs, per mesh, through
  `ModelExporter.ReadMorph` and `addBlendShapePart`.
* The uma material comment (`uma1 key=value`) carries the *rest* values of the shader properties these morphs
  drive, including the face shadow and cheek thresholds (`ModelExporter.UmaMaterialComment`), because the
  material needs them for a still image.
* The *bindings* - which locator axis drives which property - travel as `morph=` tokens in the material
  comments, and the object-level morphs as the deploy morphs above.
* [`EXTRA_MORPHS.md`](EXTRA_MORPHS.md) audits each of the five against the current export, morph by morph.
