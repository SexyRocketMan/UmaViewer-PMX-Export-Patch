# The game's eye shader: what the highlights actually are

The teary-eye highlights are the one facial effect the viewer has no code for. This file states what the
decompiled shader does with them, so a shader port does not have to guess.

**Source:** `UMA_Eye.fx` in [croakfang/UmaMusumeMME](https://github.com/croakfang/UmaMusumeMME) (decompiled HLSL
of the game's eye shader; the same repository `UMA_Face.fx` came from, see `archive/UMA_SHADER.md`). It is an MMD
effect file that reads the textures a UmaViewer export writes, so the maths in it is the game's.

## The three layers, and the fifth parameter

The eye material carries **four** texture inputs: `_MainTex` (the iris plate, `tex_*_eye0` - the *left quarter* of
the game's four-plate atlas, see below) and three highlight maps `_High0Tex`/`_High1Tex`/`_High2Tex` =
`tex_*_eyehi00/01/02`. A single `_High0Tex` *sampler* is shared by all three, but each is fetched with **its own
UV**, computed in the vertex shader.

The parameters are what matters:

| uniform | layout | meaning |
|---|---|---|
| `_HighParam1[0..2]` | `float4(x, y, z, w)` | **left** eye: upper highlight, lower highlight, blink layer. `.xy` = rotation centre, `.z` = rotation **angle**, `.w` = intensity (default 0.22, 0.22, 0) |
| `_HighParam2[0..1]` | `float4(x, y, z, w)` | **right** eye: upper and lower highlight, same layout |
| `_MainParam[0..1]` | `float4(x, y, z, w)` | the iris plate: `.xy` = per-eye UV offset, `.z` = rotation angle, `.w` = eye type 0–3 |
| `_Offset` | `float4` | per-eye UV offset for the highlight sampling |
| `_High0Tex_ST` | `float4(1, 1, 0, 0)` | highlight UV scale - **1:1, no tiling** |
| `_Limit` | `float` | the threshold the highlights are cut at (declared 0.1 in the port; the shipped material carries **0.5**, see below) |
| `_HighParam1[2].w` | `float` | intensity of the blink layer |

The `_HighParam*` and `_MainParam*` rows are uniforms the MME *port* declares so an MMD effect can be driven from
outside. The shipped shader has 46 properties and none of those names (see "Measured"), so the port reproduces the
*maths* while inventing the *interface*: a value read from the port says nothing about the game's material.

**The four-plate atlas.** `.w` of `_MainParam` as the port declares it is the eye *type*, 0–3, and that matches
the asset: `tex_*_eye0` is 512×256 on 1001, four 128×256 plates side by side, with **plate-local** mesh UVs
(measured on an export: `u 0.135..0.865`, `v 0.025..0.975`). The exporter writes the left plate as
`tex_*_eye0.png` and keeps the UVs, which draws type 0 - what a pmx material can name, since it has one texture
slot and no place for a per-material UV offset.

**The whole sheet ships beside it as `tex_*_eye0_all.png`**: the four plates are the character's eye options,
including a variant with the highlights baked out. To draw another, point the image node at `_all` and put a
**Mapping** (or Vector Math scale+add) in front of its Vector input: **x scale 0.25**, **x offset = plate × 0.25**
- 0, 0.25, 0.5, 0.75 pick the four plates in order, y untouched. That is what the game's `_MainParam.w` does with
a uniform, by hand, because a pmx cannot carry it.

## The motion, per vertex

For each highlight layer the vertex shader does exactly this (`sincos(a, s, c)` = sin/cos of the angle):

```hlsl
uv = inUv - 0.5 + param.xy;                      // from the layer's own centre
rotated.x = uv.x * s - uv.y * c;
rotated.y = uv.x * c + uv.y * s;
uv = 0.5 + rotated;                              // back into 0..1
uv = uv * _High0Tex_ST + _High0Tex_ST.zw;
```

The highlight is **rotated about a per-layer centre in UV space by an animated angle**, and nothing else moves: a
"jitter" is that angle wobbling, a "swirl" is that angle sweeping. The left/right pair is chosen in the *vertex*
shader (`v.v2.y >= 0.5` picks the eye), which is why one material serves both eyes.

The iris plate rotates the same way (`_MainParam[n].z`), and `_EyePupliScale` scales the whole vertex position, so
pupil and highlights animate independently.

## How the highlights are drawn

In the fragment shader each layer is thresholded and added, not alpha-blended:

```hlsl
intensity = tex(_High1Tex, uv).r * param.w - _Limit;
colour += max(0, saturate(intensity));           // per layer, three times
```

**Luminance above a threshold, scaled by the layer's own intensity, added to the eye.** That is why the maps are
black-background rather than alpha-masked, and why an MMD-style alpha blend of them draws black. The exporter bakes
no `<map>_mask.png` (`TextureExporter`, 2.34): no material names one, so a consumer thresholds the raw maps.

## Measured: what the shipped material exposes

`Tools/headless_export.ps1 -Char 1001 -Costume 00 -DumpShaders -Out <path>` prints the property list of the eye
and face materials as loaded. These are the game's own names, ranges and values: `UmaContainerCharacter` swaps the
material's shader for the game's *compiled* one out of `shader.a` (`UmaAssetManager.EyeShader` =
`charactertooneyet.shader`).

The eye material (`mtl_chr1001_00_eye`, shader `Gallop/3D/Chara/ToonEye/T`) has 46 properties. The ones that
matter, with the shipped values:

| property | type | shipped value | note |
|---|---|---|---|
| `_High0Tex` / `_High1Tex` / `_High2Tex` | texture | `tex_chr1001_00_eyehi00/01/02` | the three layers, exactly as the port assumes |
| `_Limit` | Range | **0.5** | one threshold for all three layers, not one per layer |
| `_Offset` | Vector | (0, 0, 0, 0) | the port's per-eye UV offset |
| `_WetTex` | texture | `<none>` | **not in the port** - a wet/teary overlay slot |
| `_WetRate` | Range | 1.0 | strength of the above |
| `_EyePupliScale` | Float | 1.0 | pupil scale, as the port assumes |
| `_NormalizeNormal` | Range | 0 | matches `archive/UMA_SHADER.md`'s note on the face |

What this settles and what it does not:

* **Settled:** `_Limit` is real, single and **0.5**, not the port's 0.1, so thresholding the highlights at 0.5 in
  Blender is right. The three-layer split with per-layer intensity (`_HighParam*`) does not exist on the shipped
  shader, so the "third layer has no threshold" reading of `_HighParam1[2].w` described the port.
* **Not settled:** the teary values. A material asset carries only static numbers and nothing in it drops
  `_Limit`, so the teary state has to be driven by code or animation; `-DumpShaders` also scans motion bundles for
  material animation curves and found none in 8 `campaign/bg` bundles (7 clips).
* **Open:** `_WetTex`/`_WetRate`. Every character texture ships a wet variant - 8128 asset names contain `wet`
  (`tex_*_diff_wet`, `tex_*_shad_c_wet`, …) - so this is plausibly the rain/wet system rather than a tear overlay.
  Separate the two before assuming it explains the tears.

## What this implies for Blender and MMD

* **Blender:** a **rotate-about-a-centre** node per highlight map, with the angle driven by the frame.
  `ShaderNodeVectorRotate` (`Z_AXIS`, `Center = (0.5, 0.5, 0)`) is the exact equivalent of the vertex shader's
  maths in one node.
* **The rate and the amplitude are not in the shader** - the game's own code animates them, so the remaining
  unknowns are two numbers, exposed as `ROTATE_RATE` and `ROTATE_AMPLITUDE` in `Tools/patch_eye_masters.py`.
* **MMD:** no route. A UV-morph stack cannot carry the motion - nothing in the pipeline reads a uv morph and
  Blender cannot animate one - so a highlight motion outside the add-on needs a carrier MMD can play, such as
  material animation or geometry.
