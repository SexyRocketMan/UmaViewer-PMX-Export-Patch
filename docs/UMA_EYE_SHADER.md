# The game's eye shader: what the highlights actually are

The teary-eye highlights are the one facial effect the viewer has no code for, and for a long time nothing in
this repository said what they *do*. This is what the decompiled shader says, so the guesswork stops here.

**Source:** `UMA_Eye.fx` in [croakfang/UmaMusumeMME](https://github.com/croakfang/UmaMusumeMME) (decompiled HLSL
of the game's eye shader; the same repository `UMA_Face.fx` came from, see `UMA_SHADER.md`). It is an MMD effect
file that reads the textures a UmaViewer export writes, so the maths in it is the game's, not a rewrite's.

## The three layers, and the fifth parameter

The eye material carries **four** texture inputs: `_MainTex` (the iris plate, `tex_*_eye0_all`) and three
highlight maps `_High0Tex`/`_High1Tex`/`_High2Tex` = `tex_*_eyehi00/01/02`. A single `_High0Tex` *sampler* is
shared by all three, but each is fetched with **its own UV**, computed in the vertex shader.

The parameters are what matters:

| uniform | layout | meaning |
|---|---|---|
| `_HighParam1[0..2]` | `float4(x, y, z, w)` | **left** eye: upper highlight, lower highlight, blink layer. `.xy` = rotation centre, `.z` = rotation **angle**, `.w` = intensity (default 0.22, 0.22, 0) |
| `_HighParam2[0..1]` | `float4(x, y, z, w)` | **right** eye: upper and lower highlight, same layout |
| `_MainParam[0..1]` | `float4(x, y, z, w)` | the iris plate: `.xy` = per-eye UV offset, `.z` = rotation angle, `.w` = eye type 0–3 |
| `_Offset` | `float4` | per-eye UV offset for the highlight sampling |
| `_High0Tex_ST` | `float4(1, 1, 0, 0)` | highlight UV scale — **1:1, no tiling** |
| `_Limit` | `float` | the threshold the highlights are cut at (default 0.1) |
| `_HighParam1[2].w` | `float` | intensity of the blink layer |

## The motion, per vertex

For each highlight layer the vertex shader does exactly this (`sincos(a, s, c)` = sin/cos of the angle):

```hlsl
uv = inUv - 0.5 + param.xy;                      // from the layer's own centre
rotated.x = uv.x * s - uv.y * c;
rotated.y = uv.x * c + uv.y * s;
uv = 0.5 + rotated;                              // back into 0..1
uv = uv * _High0Tex_ST + _High0Tex_ST.zw;
```

So the highlight is **rotated about a per-layer centre in UV space, by an animated angle** — nothing else moves.
The "jitter" is that angle wobbling; a "swirl" is that angle sweeping. The left/right pair is chosen in the
*vertex* shader (`v.v2.y >= 0.5` picks the eye), which is why one material serves both eyes.

The iris plate rotates the same way (`_MainParam[n].z`), and `_EyePupliScale` scales the whole vertex position —
so the pupil and the highlights can be animated independently.

## How the highlights are drawn

In the fragment shader each layer is thresholded and added, not alpha-blended:

```hlsl
intensity = tex(_High1Tex, uv).r * param.w - _Limit;
colour += max(0, saturate(intensity));           // per layer, three times
```

i.e. **luminance above a threshold, scaled by the layer's own intensity, added to the eye**. That is why the
maps are black-background rather than alpha-masked, and why an MMD-style alpha blend of them draws black
(`export` notes: `TextureExporter.BakeHighlight` exists for exactly that reason).

## What this implies for Blender and MMD

* **Blender:** a **rotate-about-a-centre** node per highlight map, with the angle driven by the frame — not the
  translation I first patched in. `ShaderNodeVectorRotate` (`Z_AXIS`, `Center = (0.5, 0.5, 0)`) is the exact
  equivalent of the vertex shader's maths in one node.
* **The rate and the amplitude are not in the shader** — they are uniforms the game's own code animates, so the
  only remaining unknowns are two numbers, and both are exposed as constants in
  `Tools/patch_eye_masters.py` (`ROTATE_RATE`, `ROTATE_AMPLITUDE`).
* **MMD:** the same motion is a **UV-morph stack** (a rotation of the highlight vertices' UVs per step), which is
  how the `@_@` spin and the tear scroll are already carried. That is the route if the highlights ever need to
  work without the addon.
