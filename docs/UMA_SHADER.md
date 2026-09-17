# The uma shader: what Blender gets, what the game does, and where the gap is

Two separate things are involved when an exported model is shaded:

* the **viewer** (this repository) uses the game's own shader; materials come from the game's asset bundles
  and are lit by the game's renderer,
* **Blender** gets a `.pmx` file, and a `.pmx` material is an MMD material: one base colour, one texture,
  one sphere map, one toon ramp, plus specular and outline values. Everything else about the uma shading -
  the shade/toon texture, the mask channels, the rim and highlight parameters - is **not** in the file.

The `uma_addon`'s **Shading** operator rebuilds the uma look inside Blender from the model plus the
textures that sit next to it. This document records exactly what it builds, what the game sets that it
cannot see, and what could be closed.

## What the Shading operator builds

Sources: `uma_addon/addons/uma_addon/operators/Umashader.py` and the `Umashaders.blend` it ships
(probed from Blender 5.2, so the numbers below are the shipped defaults).

For every material slot of the selected mesh it:

1. picks the part type from the material name - `bdy`/`body`, `hair`, `face`/`mayu`, `tail`, `eye`,
2. finds the imported MMD texture node (`mmd_base_tex`) to learn the texture folder and file name,
3. copies `Uma Shader` (or `Uma Eyes` for `eye`) out of `Umashaders.blend` and replaces the slot with it,
4. wires the uma texture set into the copy by file name:

   | node | file name | wired to the group's socket | colourspace |
   |---|---|---|---|
   | `Image Texture` | `..._diff.png` | `Diffuse Texture` | sRGB |
   | `Image Texture.001` | `..._ctrl.png` | `Ctrl Texture` | Non-Color |
   | `Image Texture.002` | `..._shad_c.png` | `Shaded Texture` | sRGB |
   | `Image Texture.003` | `..._base.png` | `Base Texture` | Non-Color |
   | `Image Texture.004` | `..._emi.png` | `Emmission Texture` (and switches `Emmission Toggle` on) | Non-Color |

5. for the eye material, loads `eye0` as the base and `eyehi00/01/02` as the highlight layers,
6. switches `Toggle If Face [0=Off,1=On]` on for `face`/`mayu`,
7. adds a **`Uma Outlines`** geometry nodes modifier and sets its `Depth Offset` input to `0.1`.

The `Uma Shader` group itself is the uma material model in node form. Its inputs, with the defaults the
operator leaves in place for every material:

| socket | identifier | default | what it is |
|---|---|---|---|
| `Base Texture` | `Input_3` | - | the `base` map (MMD's spherical/additive map) |
| ` Ctrl Texture` | `Input_4` | - | the mask/control map (note the leading space in the name) |
| `Shaded Texture` | `Input_1` | - | the shade/toon ramp |
| `Diffuse Texture` | `Input_2` | - | the albedo |
| `Emmission Texture` | `Input_30` | - | the emissive map |
| `Toggle If Face [0=Off,1=On]` | `Input_11` | 0 | face-specific behaviour |
| `Light Threshold` | `Input_35` | **0.2** | where the shade ramp kicks in |
| `Diffuse Intensity` / `Saturation` / `Color` | `Input_25` / `Input_20` / `Input_32` | 1 / 1 / white | albedo response |
| `Shaded Intensity` / `Saturation` / `Color` | `Input_26` / `Input_19` / `Input_33` | 1 / 1 / white | shade response |
| `Metallic Intensity` / `Color` | `Input_18` / `Input_21` | 1 / white | metal/reflection response |
| `Highlight Intensity` / `Color` | `Input_24` / `Input_22` | 1 / white | specular highlight |
| `Ambient Rimlight Size` / `Intensity` / `Color` | `Input_27` / ... | 0.4 / 1 / white | ambient rim |
| `Rimlight Size` / `Intensity` / `Color` | ... | 0.1 / 1 / white | rim light |
| `Emmission Toggle` / `Emission Intensity` | ... | 0 / 1 | emission |

`Uma Outlines` inputs: `Thickness`, `Camera`, `Depth Offset`, `Outline Material`,
`Enable Distance Scaling`, `Enable Distance Clamp`, `Min Distance Size`, `Max Distance Size`,
`Only Depth Offset`, `Face`, `Value`.

### Writing those inputs from an addon (Blender version note)

Blender 4.0 removed the old geometry-nodes-modifier-as-ID-properties access; reading it raises
`this type doesn't support IDProperties` and writing raises `id properties not supported for this type`.
The working call in 4.x/5.x is by the interface's socket identifier, and each entry is an ID property group:

```python
modifier.properties.inputs["Input_15"]["value"] = 0.1     # Depth Offset of the shipped Uma Outlines
```

`Umashader.set_geometry_modifier_input()` does that, preferring the socket's readable name over the
identifier and falling back to the old ID property form on Blender 3.x.

## What the game sets that the `.pmx` cannot carry

From the viewer's own material code (`UmaContainerCharacter`, `Live/Director`):

| game property | what it does | in the export |
|---|---|---|
| `_MainTex` | albedo | yes, as the material's texture |
| `_MaskColorR1/R2/G1/G2/B1/B2`, `_MaskToonColor*` | per-channel tinting driven by the `ctrl` map (hair/skin/cloth variants) | **no** |
| `_CylinderBlend`, `_RimColor`, `_RimStep`, `_RimFeather`, `_RimSpecRate`, `_RimShadowRate`, `_RimHorizonOffset`, `_RimVerticalOffset` (+ the `2` variants) | the live rim lighting (two layers, six parameters each) | **no** |
| `_CharaColor`, `_ToonDarkColor`, `_ToonBrightColor`, `_OutlineColor`, `_Saturation` | live colour grading of the whole character | **no** |
| `_Cutoff`, `_StencilMask`, `_StencilComp`, `_StencilOp` | alpha cutout and rendering order | **no** (the PMX has one alpha value per material) |
| `_UseOriginalDirectionalLight` | which light drives the toon ramp | **no** |

The exporter writes fixed MMD material values instead (`ModelExporter.ReadPartMaterials`): white diffuse,
no specular, ambient 0.5, shininess 5, black outline at 0.4, and the first texture of the model as the
base texture. The material's comment field is written empty.

That is the gap you are seeing: in Blender **every material gets the same `Light Threshold`, the same
intensities and the same single-colour diffuse/shaded response**, while the game varies those per
character, per costume and per part - and on top of that the game's renderer does deferred shading with
its own shadow maps, SSAO, bloom and tone mapping, which nothing in Blender reproduces by accident.

## What the exporter now carries

The gap above is closed as far as a `.pmx` allows, in one way: **the parameters an MMD material has no field
for go into the material comment.** The comment is free text that `mmd_tools` keeps on the imported material
as `material.mmd_material.comment`, and it is written as one line of `key=value` pairs:

```
uma1 toon_step=0.4 toon_feather=0.001 specular_power=0.15 specular=1,0.905,0.59,1 env_rate=0.4 env_bias=5
rim_step=0.15 rim_feather=0.001 rim=1,1,1,0.392 rim_spec_rate=1 rim_shadow=2 outline_width=0.325
outline=0.125,0.047,0,0.098 chara=1,1,1,1 saturation=1 toon_bright=1,1,1,0 toon_dark=1,1,1,0
emissive_intensity=1 emissive_rim_power=1 emissive_rim_intensity=1 use_option_mask=1
triple=tex_bdy1001_00_base option=tex_bdy1001_00_ctrl toon=tex_bdy1001_00_shad_c env=tex_chr_env000
```

Numbers use the invariant culture (dots for decimals, commas only between colour components), the tag
`uma1` marks the format, and a property the game renames is skipped rather than breaking the export - which
is also how a rename gets noticed. Anything that does not understand the comment simply sees a comment.

### Which MMD slots the uma maps go into, and which they do not

The obvious idea is to put the uma maps into MMD's sphere and toon slots, since those carry a texture each.
**Both were tried and both are wrong**, measured by rendering the same model with and without them (Blender
5.2, EEVEE, identical camera and lights):

| what was tried | what it rendered |
|---|---|
| `_EnvMap` in the sphere slot, mode add | the whole model washed out and glossy: the game adds that map at the material's `_EnvRate` (0.4), MMD's sphere add has no rate and adds at full strength |
| `_ToonMap` (`shad_c`) in the toon slot | harsh dark steps and black hair: that map is the *colour* of the shaded part of a surface, not a ramp, and MMD's toon slot is a ramp |
| neither (current) | the pre-change look, mean pixel difference **0.0002** against the baseline export |

The maps are still exported as textures and named in the comment, so a shader that knows what they are (the
uma addon's) can apply them with the right maths - `env` with a rate, `toon` as a shade colour. Only the
specular is carried into a real MMD field, scaled by the uma power (`_SpecularColor * _SpecularPower`),
because using the colour as it is made every part look polished.

The Shading operator reads the comment back and maps it by meaning onto the shipped group:

| comment key | shader group socket | why |
|---|---|---|
| `toon_step` | `Light Threshold` | the game's shade threshold |
| `chara` / `saturation` | `Diffuse Color` / `Diffuse Saturation` | the character colour grade |
| `specular` | `Highlight Color` | the highlight tint |
| `env_rate` | `Metallic Intensity` | how much the environment map contributes |
| `emissive_intensity` | `Emission Intensity` | emission strength |
| `rim` (rgb + a) | `Rimlight Color` + `Rimlight Intensity` | the uma rim colour's alpha is its strength |
| `rim_step` | `Rimlight Size` | rim width |
| `toon_dark` / `toon_bright` | `Shaded Color` | only when the game actually applied the live grade (alpha > 0) |
| `outline_width` | `Uma Outlines` → `Thickness` | same scale: 0.35 in the shipped group, 0.325 in the material this was measured on. The most common value across materials wins, so one part wanting a thick outline does not drag the model's outline with it |
| `triple`, `option`, `toon`, `env`, `emissive` | the texture nodes | the file each map was exported as, so no name guessing |

Verified on a real export (character 1001, costume 00): the body material takes `Light Threshold 0.4`, the
hair `0.3` and the mayu `0.5` - the per-material variation that used to be flattened into one default - the
face gets `Rimlight Intensity 0` because its rim colour is transparent, and the outline thickness lands on
`0.325`. An export made before this existed still imports and shades: no comment means no settings applied,
the textures are found by file name as before, and the outline keeps the group's own 0.35.

## What still cannot be matched, and why

* **Per-instance tinting.** `_MaskColor*`/`_MaskToonColor*` and the live rim values depend on the costume
  and the scene. They can be exported as they were at export time (the comment carries the ones the shader
  reads), but a model exported in one state cannot know about another.
* **The renderer.** The game shades deferred with its own shadow maps, SSAO, bloom and tone mapping; the
  `.pmx` in EEVEE does not. Matching lights and framing gets much closer than tweaking material values.
* **Uv-driven detail** (`_DirtTex`, `_TexScrollParam`, emission scroll) has no MMD equivalent at all.


## Checking it

`Tools/blender_render_motion.py` renders an exported model (optionally with a recorded motion) headlessly,
and `Tools/render_stats.py` checks the result numerically. For shader work the useful check is a comparison
against the viewer's own look at the same camera angle, which is a job for the eye rather than a script -
but `umashader_test.py` in the scratch tooling reports what the operator wired up (materials, textures and
their colourspaces, the outline modifier and its inputs) so a regression shows up as a changed list.
