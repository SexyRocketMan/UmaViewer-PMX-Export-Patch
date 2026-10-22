# The extra morphs: everything that is not a vertex blend shape

The game's face rig has two kinds of morph. `FacialMorph` is the familiar one — vertex/TRS offsets that
become a shape key and export today as a PMX vertex morph. `FacialOtherMorph` is the other kind, and it is
what this document is about: it does not touch vertices at all, it drives *material properties*, *textures*,
*object visibility* and a *particle system*, through a list of `BindProperty` entries.

All of it is in `Assets/Scripts/umamusume/Gallop/FacialMorph.cs` (the classes) and
`Assets/Scripts/umamusume/Gallop/FaceDrivenKeyTarget.cs` (what builds and applies them).

## The inventory

The set is not data — it is built in code, in `FaceDrivenKeyTarget.InitializeExtra()`
(`FaceDrivenKeyTarget.cs:209`), from whatever the character's head actually has. On a normal character that is
these five, and nothing else:

| morph | locator | what its binds do |
|---|---|---|
| `Cheek_Ctrl` | `Cheek_Ctrl` | up to three `Texture` binds on the `M_Cheek` decal: `mainTexture = CheekTex_0/1/2`, `_CheekRate` = value. Driven by the locator's scale **X**, **Y**, **Z** — one axis per cheek texture. |
| `Shade_Ctrl` | `Shade_Ctrl` | one `Shader` bind: `_faceShadowAlpha` on the `M_Face` material, driven by scale **X**. |
| `Manga_Ctrl` | `Manga_Ctrl` | `EyeSelect` over the left and right eye-manga objects (scale **Y**) and an `Enable` over the same set (scale **X**). |
| `StaticTear_Ctrl` | `Cheek_Ctrl` | two `Enable` binds, `StaticTear_L` and `StaticTear_R`, driven by position **X** and **Y**. |
| `Tear{index}_Ctrl` | `Tear{index}_Ctrl` | per tear controller: `TearWeight` (scale **X**), `TearSide` (position **Y**), `TearSelect` (scale **Y**), `TearSpeed` (scale **Z**). |

The important structural point: **the animation does not move the morph, it moves the locator.** `Cheek_Ctrl`,
`Shade_Ctrl`, `Manga_Ctrl` and the tear locators are ordinary transforms in the head rig, and the game's
driven-key clips animate them (`anm_*_face_driven`, `anm_*_ear_driven`, `anm_chr*_tear*_driven` in
`3d/motion/.../facial/`). So a recorded motion already carries the *input* to these morphs; what an export
does not carry is the *response*.

What the export does carry now is in `PMX_EXTENSIONS.md`: the parts these morphs show and hide (the cheek
blush, the four eye manga, the two static tears) are collapsed onto a point inside the head at rest and one
vertex morph per part deploys it - `Cheek_Show`, `Manga_000`..`Manga_003`, `Tear_L`, `Tear_R` - so a plain MMD
viewer can show them without any addon. What a vertex morph still cannot hold is the cheek's *texture pair and
rate* and the face shadow's `_faceShadowAlpha`, which stay as `morph=` tokens in the material comments.

## The bind types and exactly what they do

`FacialMorph.cs:65` lists nine; `FaceDrivenKeyTarget.ProcessExtraMorph` (`FaceDrivenKeyTarget.cs:504`)
applies them:

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

"PMX" is what a `.pmx` can express as a morph; "Blender" is what an addon can drive on the imported model.
PMX morphs cover vertices, UVs, materials (diffuse/specular/ambient/edge colours, specular power, texture and
sphere/toon tints), bones and groups — there is no object visibility, no texture swapping and no custom
shader properties.

| morph | PMX | Blender | verdict |
|---|---|---|---|
| `Cheek_Ctrl` | A decal material's tint/alpha can be animated by a material morph; the *texture swap* cannot. | Swap the image on the decal material and drive `_CheekRate` from the locator's scale. | **mesh-able**: the decal is its own mesh (`M_Cheek`) with its own material — export it separately, and either bake one material per cheek texture or let the addon pick by locator scale. |
| `Shade_Ctrl` | Nothing — no shader property in MMD. | The uma shader node group already has `_faceShadowAlpha` as an input; a driver from `Shade_Ctrl`'s scale X closes the loop. | **Blender-only**: carry the binding in the material comment, build the driver in the addon. |
| `Manga_Ctrl` | Nothing — MMD cannot show/hide objects. | Object visibility, driven from the locator. | **Blender-only**, and only if the manga objects are exported as separate objects rather than merged into the face. |
| `StaticTear_Ctrl` | Nothing. | Object visibility. | **Blender-only**; the static tears are simple meshes, so this is cheap. |
| `Tear{n}_Ctrl` | Nothing. | Nothing sane — tears are a particle system with weight/side/object/speed. | **out of scope**; document it, do not fake it. |

## What the exporter does today

* `FacialMorph`s (eyebrow, eye, mouth, ear) are exported as PMX vertex morphs, per mesh, through
  `ModelExporter.ReadMorph` and `addBlendShapePart`.
* The uma material comment (`uma1 key=value`) already carries the *rest* values of the shader properties these
  morphs drive, including the face shadow and cheek thresholds (`ModelExporter.cs:713`–`746`), because the
  material needs them for a still image. Nothing carries the *binding* (which locator axis drives which
  property) or the object-level morphs.

## What building this would look like

1. **Plumbing first**: expose the `OtherMorphs` list (name, locator, binds with their type and target) to the
   exporters. Today it exists only in the viewer's runtime object.
2. **Separate objects**: keep `M_Cheek`, the manga objects and the static tears as their own meshes and
   materials rather than merging them into the head, so anything can be shown or hidden.
3. **Cheek**: export the decal and either three materials (one per cheek texture) or one material plus the
   addon's image swap. Material morphs give a tunable blush strength in MMD for free.
4. **Shade / manga / static tears**: write the locator binding into the material comment (or a text block) and
   have the umamusume addon build a driver — locator scale/position axis → node group value or object
   visibility. This matches how the addon already reads `uma1` material fields.
5. **Tears**: document as unsupported.

## Open questions

* Which of these are worth building? The cheek blush is the one that shows on almost every expression; static
  tears are cheap; manga is a style feature; the animated tear system is a lot of work for something MMD/Blender
  cannot express anyway.
* Drivers in the addon, or separate materials/objects per state in the export? The first keeps the file small
  and stays live; the second needs no addon but cannot express `Enable`/`EyeSelect` at all.
