<!-- superseded -->
> **Note.** Its account of the custom split normals is right and worth reading. Its _CylinderBlend value is wrong: it records 0.25 for ToonFace/ToonEye/ToonHair, but the shipped material asset stores **0.0** for the face and the disassembly agrees, so the weight on this material is the vertex colour's blue channel alone. It was also written before the game's own face shader was read properly; docs/GROUND_TRUTH.md and docs/VERIFICATION_WORKFLOW.md in the Blender addon repository are current.

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

### Turning the extras off again

Nothing here changes what MMD or any other PMX tool renders: the comment is a comment, and the only real MMD
field involved is the specular, scaled by the uma power. A viewer that knows nothing about any of this -
including a Blender session without the addon - shows the same matte model it always did: rendering the same
export with and without the extras differs by a mean of 0.0002 per channel, with 0.2% of pixels changed at all.

For anyone who wants the literal pre-2.6 materials anyway, `Config.PmxUmaMaterialFields` (in `Assets/Scripts/Config.cs`)
turns the extra fields off, and the headless runner exposes it as `-umaPlainMaterials`:

```
./Tools/headless_export.ps1 -Char 1001 -Costume 00 -PlainMaterials -Out D:/out/plain.pmx
```

A plain export has no comment and no uma-derived specular or edge values, so the Shading operator applies no
per-material settings to it and falls back to the shipped group's own defaults. Everything else is untouched:
same vertices, bones, morphs and textures (22 on character 1001 costume 00, the uma maps included, since they
are exported as textures regardless), and the same empty comment the exporter wrote before this feature existed.

## Face shading, morphs and custom split normals

Three things decide how the face looks, and the game does all three.

**Keep the normals the game authored.** The models ship custom split normals, and on the face those are what
makes the shading clean: the face is low-poly, so normals recomputed from the surface move the toon step onto
triangle edges and the cheeks and jaw come out in hard angular patches - clearly worse than anything the
authored normals do. An earlier version of the Shading operator cleared them (`fix_morph_shading`); that is now
off by default, with the checkbox kept for the rare model that still shows a seam.

**Blend a cylinder normal into them.** The game's face, eye and hair shaders do not light the surface with the
mesh normal. Decompiled community sources (see below) agree on

```
radial   = P - (C + dot(U, P - C) * U)          # the component perpendicular to the head's up axis
w        = vertexColour.blue * (1 - _CylinderBlend)
shadingN = normalize(w * N + (1 - w) * normalize(radial))
```

and the viewer's own code sets `_CylinderBlend = 0.25` for exactly `Gallop/3D/Chara/ToonFace`, `ToonEye` and
`ToonHair`, never for the body, whose shader uses the plain mesh normal (`Assets/Scripts/UmaContainerCharacter.cs`).
The weight is the Unity vertex colour, which the exporter writes into the `.pmx`'s last extra UV slot and
`mmd_tools` imports as the `UV3` (r,g) and `_UV3` (b,a) layers - so the blue channel is `_UV3.x`, and no extra
data has to be exported for this. The Shading operator applies the same blend to the face, eyebrow, eye and
hair materials (`Face shading normals`, on by default) and writes it as custom split normals: measured on
character 1001 costume 00 that turns 4133 vertices by 20.8 degrees on average.

**Switch the group's face branch on.** The shipped `Uma Shader` group has an input `Toggle If Face [0=Off,1=On]`,
and the operator sets it for the `face` and `mayu` materials. That branch is the game's own face shading, and it
is also what keeps the face clean *while a morph deforms it*. Blender stores the imported custom normals in the
corner fan space, so they do follow shape keys and posing (measured: a shape key that rotates the mesh rotates
the normals with it, by exactly the same angle) - but they follow the *deformation*, so a mouth morph still
moves the boundary, and the face branch is what keeps that from tearing.

Measured on character 1001 costume 00, face close-up, the same camera and a dim world, in a 2x2 of the two
switches with the two largest mouth morphs at 1.0:

| setup | at rest | with the mouth morphs |
|---|---|---|
| face branch on, authored normals (the defaults now) | clean | clean |
| face branch on, normals cleared | hard angular patches on the cheeks and jaw | nearly clean, a spike near the nose |
| face branch off, authored normals | clean | hard seam and dark spikes around the lips |
| face branch off, normals cleared | hard angular patches | hard seam |

The scripts used for that were scratch tooling (`face_toggle_matrix.py`, `cylinder_normals.py`), kept outside
this repository along with the rest of the development harness, and the
images are in `umaviewer_exports\shadercheck\facetoggle\` and `...\face_light\`.

### Where that came from

* `croakfang/UmaMusumeMME` - decompiled HLSL of the game's face shader (`UMA_Face.fx`), which contains the
  cylinder blend and the cheek/nose mask tests.
* `Elysia-simp/Honse-Shader` - an independent hand rewrite from an OpenGL decompile (`face_shader.fxsub`),
  agreeing on the same maths.
* Cygames Tech Conference 2021, `ウマ娘 プリティーダービー 3DCGキャラクター事例` - first-party: the head uses a
  detail mask for cheeks and nose, and before it "the shadow looked distorted and unnatural".
* **The detail mask is now reproduced** (the addon's `Face shading from the base map's detail mask`): the base
  texture's green channel sits at 0.5 over the face and at 0 on the hair and body, so it picks the face out,
  and the face and eyebrow vertices' shading normals are pointed where the character is looking. Measured at a
  grazing light with two mouth morphs at 1.0: the face is flat and clean, where without it a gradient and
  patches cross the cheeks (28% of the pixels differ at rest, 32% with the morphs) - the game's flat face in
  `game_face.png` is now what Blender renders too. The direction lives in the mesh's custom normals, which
  Blender keeps in the corner fan space and the armature deforms, so **the face follows the head bone**: a 45
  degree head turn turns the sampled face normals 45.0 degrees, with no driver in the file to keep alive.
* **The red channel measured as a no-op**: the game biases its shade step per texel with it
  (`base.red * halfLambert` against the toon step), but on character 1001 that channel is a binary mask and the
  shipped `Uma Shader` group already gates its shading with it, so implementing the bias changes 0.0% of the
  pixels. It is available as an option, off by default, for a model where the boundary really does cross it.
* Still open: the game's per-texel cheek/nose *colour* override (a half-plane test against the light direction
  in the head's frame) rather than a replaced normal, and the second normal set the outlines read.

## What still cannot be matched, and why

* **Per-instance tinting.** `_MaskColor*`/`_MaskToonColor*` and the live rim values depend on the costume
  and the scene. They can be exported as they were at export time (the comment carries the ones the shader
  reads), but a model exported in one state cannot know about another.
* **The renderer.** The game shades deferred with its own shadow maps, SSAO, bloom and tone mapping; the
  `.pmx` in EEVEE does not. Matching lights and framing gets much closer than tweaking material values.
* **Uv-driven detail** (`_DirtTex`, `_TexScrollParam`, emission scroll) has no MMD equivalent at all.


## Checking it

`blender_render_motion.py` (in the companion `UmaViewer-DevTools` tooling) renders an exported model,
optionally with a recorded motion, headlessly, and `render_stats.py` checks the result numerically. For shader
work the useful check is a comparison
against the viewer's own look at the same camera angle, which is a job for the eye rather than a script -
but the shading A/B harness there (`blender_render_angles.py`, `shading_ab.ps1`) renders the same model with
legacy and new shading and tiles the pairs, so a regression shows up as a visible difference rather than a
changed list.
