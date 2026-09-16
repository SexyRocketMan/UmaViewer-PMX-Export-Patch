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

## What can be done about it

1. **Carry the parameters in the material comment.** A `.pmx` material has a free-text comment field that
   the exporter writes empty today (`MMDMaterial.MetaInfo`). Writing something like
   `uma light_threshold=0.2 diffuse=1,1,1 shaded=0.9,0.85,0.8 rim_size=0.1 ...` would let the Shading
   operator set the group's sockets per material instead of using the defaults for all of them. Small,
   verifiable, no format breakage - any other viewer just ignores the comment.
2. **Export the extra textures into the MMD slots they belong in.** The `shad_c` map is what MMD calls the
   toon/ramp texture and the `base` map is the sphere map, but the export only references the albedo. With
   `shad_c` as the material's toon texture and `base` as the sphere map, a plain `mmd_tools` import would
   shade far closer to the game even without the addon, and the addon could stop guessing the file names.
3. **Tint and rim values are per-instance.** `_MaskColor*` and the live rim values depend on the costume and
   the scene, so they can be carried (same comment trick) but they will only be right for the state the
   model was exported in; the honest options are to export them as they were at export time, or to leave
   them and set them by hand.
4. **Match the light, not just the material.** The group is built around one directional light and a
   threshold; games look different because of the surrounding renderer. A Blender scene with a sun of about
   the game's intensity, a modest ambient world colour and the same camera framing will land much closer
   than tweaking material values ever will.

Nothing here can make a `.pmx` in EEVEE identical to the game - the rasteriser, the shadow maps and the
post-processing differ. What is achievable is that each material reacts to light the way the game intends:
right ramp, right threshold, right tint, right rim.

## Checking it

`Tools/blender_render_motion.py` renders an exported model (optionally with a recorded motion) headlessly,
and `Tools/render_stats.py` checks the result numerically. For shader work the useful check is a comparison
against the viewer's own look at the same camera angle, which is a job for the eye rather than a script -
but `umashader_test.py` in the scratch tooling reports what the operator wired up (materials, textures and
their colourspaces, the outline modifier and its inputs) so a regression shows up as a changed list.
