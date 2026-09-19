  # A clean toon terminator on a low-poly face — techniques and sources

Research note. Two things are kept apart throughout:

* **[established]** — I found it described in a source I actually read (URL given).
* **[inference]** — my own reasoning, or a claim I could only partly ground.

Sources are listed at the end; inline links point at the specific page.

---

## 0. Context found in this repository (not the web)

This matters more than anything else here, because the game's own face shader is already
vendored in the workspace and it does **not** let `dot(N, L)` alone place the face terminator.

**`Assets/Resources/NarsShader/Shaders/UmaMusume Face.shader`** (decompiled Amplify shader,
material name `Nars/UmaMusume/Face`). It has a `_TripleMaskMap("HardShadows")` and the
terminator is:

```hlsl
float NDL91 = dot(lightDir, worldNormal);
// mask.g == _TripleMaskMap green channel
float s = smoothstep(0.0, mask.g * (1.0 - NDL91), (NDL91 * _ShadowStrength) * mask.g);
float t = s - 0.26;                                  // _LightSmoothness
float shade = saturate(t / fwidth(t));               // "Step Antialiasing"
float4 Shadow64 = lerp(ShadowTexture147, BaseTexture158, shade);
```

Two separate mechanisms, both relevant:

1. a **mask texture channel** multiplies into the threshold, and
2. the final hard step is anti-aliased **in screen space with `fwidth`** (the Amplify "Step
   Antialiasing" node — the HLSL `saturate((x - t) / fwidth(x))` idiom).

Note that in this particular decompile the mask cancels out of the smoothstep ratio
(`3·NDL·m / (m·(1-NDL))`), so `mask.g` only acts through the degenerate `m == 0` case. That is
almost certainly an artefact of the decompilation, not the game's real maths. **[inference]**

**`Assets/Resources/Materials/UmaShaderFace.shader`** (hand-written reimplementation of the same
material) does the same job differently:

```hlsl
float3 lightDirection = float3(0, -1, 0);                   // fixed light, not the scene light
float halfLambert = 0.5 * dot(i.normalDir, lightDirection) + 0.5;
float shadowLerp  = saturate(1 + ((base.r * 2 * halfLambert - 0.3) * 50));
float4 shadedDiff = lerp(shad, diff, shadowLerp) * lightColor;
```

Again a texture channel (`base.r`) biases the threshold, and `* 50` makes the step hard.

**`docs/UMA_SHADER.md`** already records the decisive facts:

* "The models ship custom split normals, and on the face those are what makes the shading clean:
  the face is low-poly, so normals recomputed from the surface move the toon step onto triangle
  edges and the cheeks and jaw come out in hard angular patches."
* The game's shading normal is not the mesh normal:
  `radial = P - (C + dot(U, P-C)·U)`; `w = vertexColour.blue * (1 - _CylinderBlend)`;
  `shadingN = normalize(w·N + (1-w)·normalize(radial))`, with `_CylinderBlend = 0.25` for
  `ToonFace`, `ToonEye` and `ToonHair` (never for the body).
* On top of that the game uses a per-texel **detail mask** (the base map's green channel sits at
  0.5 over the face and 0 elsewhere); the face and eyebrow shading normals are then pointed where
  the character is looking, and the face renders flat and clean.
* Named provenance: `croakfang/UmaMusumeMME` (decompiled `UMA_Face.fx`),
  `Elysia-simp/Honse-Shader` (independent rewrite from an OpenGL decompile), and Cygames Tech
  Conference 2021, `ウマ娘 プリティーダービー 3DCGキャラクター事例`.

**The design consequence:** on Uma's face the terminator is not a `N·L` iso-contour at all. The
normals are redirected so that `N·L` barely varies across the face, and where the line falls is
decided by a mask. That is the same conclusion the wider industry sources below reach
independently.

### 0.1 Confirmed against the decompile, and one correction worth having

A focused follow-up pass read `croakfang/UmaMusumeMME`'s `UMA_Face.fx` and
`Elysia-simp/Honse-Shader`'s `face_shader.fxsub` directly, and located a full Chinese translation of
the Cygames session. Findings:

* **The face basis is the head *bone*, not the object.** `UMA_Face.fx` declares
  `_faceShadowHeadMat : CONTROLOBJECT < string item="head"; >` with `_LocalFaceUp=(0,1,0)` and
  `_LocalFaceForward=(0,0,-1)`. Face shading therefore follows head rotation.
  ([UMA_Face.fx](https://raw.githubusercontent.com/croakfang/UmaMusumeMME/master/UMA_Face.fx))
* **The cylinder/flattened normal is confirmed in the vertex shader**, as approximately
  `shadingN = lerp(-horizDir, N - horizDir, vertexColor.z * (1 - _CylinderBlend))` — the same idea
  as the workspace doc's `radial`/`w` formula, written as a lerp. The per-vertex weight is authored
  in vertex colour.
* **The "detail mask" is the face mask texture's green channel**, with the cheek class at
  `y > 0.51` and the nose class at `y <= 0.49`; cheek and nose shadows are gated by
  `_CheekPretenseThreshold` / `_NosePretenseThreshold` (default 0.775) against an angular term built
  from face-forward·light, and scaled by `_NoseVisibility`. In other words the cheek and nose
  shadows are **authored per texel and fire at artist-chosen angles, independent of the normals.**
* **The Cygames Tech Conference 2021 talk is confirmed to exist** — session
  「ウマ娘 プリティーダービー 3DCGキャラクター事例 ～基本設計とウマ娘ならではの表現について～」, Day 1 (2021-11-13)
  ([game.watch pre-announcement](https://game.watch.impress.co.jp/docs/news/1364580.html)) — and a
  Chinese translation of the character-modelling portion (《赛马娘 3DCG 角色制作案例》,
  [uisdc.com/pretty-derby-3d](https://www.uisdc.com/pretty-derby-3d)) states that the head uses a
  **Detail Mask** ("细节遮罩") mainly to control **cheek and nose** shading, controllable
  independently of the normal shading path, and that **before this the shadow looked
  "歪曲和不自然" (distorted and unnatural) and afterwards it was cleaner and more anime-like.**
  Same session also gives the model spec (6 materials, <20 000 tris, ~160 bones):
  two colour maps (Diffuse + Shadow Color) and a control set whose five channels are ShadowMask,
  SpecularMask, CutoffMask, Environment mask and a rim/backlight mask — and it says **"the majority
  of the quality-affecting adjustments are done in the shadow mask."**
  **This confirms the workspace doc's account and confirms the defect's reported cause.**
* **Correction: Uma Musume's face is *not* an SDF / angle-threshold-map system.** Neither
  decompile uses one; the mechanism is flattened/cylinder-blended normals plus a painted per-texel
  detail mask. If the project's mental model is "Uma = Genshin-style SDF face", that is wrong, and
  the fix for this case is not an SDF port. (Genshin/Honkai/Hi-Fi RUSH/Project Sekai *are* SDF; see
  §1.13.) **[established]**
* Prior art worth knowing: `LooperHonstropy/BLENDER-Uma-Musume-Pretty-Derby-Shaders` is a Blender
  port of the Uma shaders whose README states in its Known Bugs: *"The shader currently does not
  have the face triangles when rotating the sun, as I haven't found the solution to use the texture
  yet… There IS a node inside of the main node group that aims to apply the face triangle lighting,
  but right now It's super clunky, and is at best a hacky way of solving that problem."* That is
  the same problem, unsolved, by someone else. **[established]**
* The earlier Cygames HDR talk (Unite Beijing 2018, He Jia / 贺甲) uses a **different, weaker**
  technique for Honkai 3rd's face: a **vertex-colour channel as a mask** controlling the strength of
  the face's shading layers — no threshold texture, no SDF, no 180° bakes
  ([gameres transcript](https://www.gameres.com/807345.html)). So "HI3 uses an SDF face shadow" is
  **folklore** unless you find HI3's own face texture. **[established negative]**

---

## 1. Techniques

### 1. Keep the authored shading normals; never recompute them for the face

*What it does:* leaves the custom split normals the model shipped with in place, so the shader
reads the artist's normal field rather than one derived from the triangle layout.
*Why the terminator is smooth:* the terminator is the level set of `dot(N, L)`. If `N` is a
hand-shaped field that varies smoothly across the face, the level set is a smooth curve. If `N` is
recomputed from geometry, its variation is a function of the triangle layout, so the level set
inherits the triangle layout.
*In Blender:* `custom_normal` is an attribute on the **face corner** domain; importing preserves
it (FBX and Alembic importers are the only documented importers of custom normals, but
`mmd_tools`/PMX import and Blender's own file format keep it too). **Do not** run
`Shade Auto Smooth` / `Smooth by Angle` with a low angle on the face, do not clear custom split
normals, and do not apply a modifier that discards them. [Blender manual, Mesh Structure →
Custom Split Normals](https://projects.blender.org/blender/blender-manual/raw/branch/main/manual/modeling/meshes/structure.rst) —
"Custom split normals are a way to tweak/fake shading by pointing normals towards other directions
than the default, auto-computed ones… helps counterbalance some issues generated by low-poly
objects". **[established]** (plus the workspace doc's measured result above).

### 2. Redirect the face normals — the three documented shapes: "Flat", "Cylinder", "Two Halves"

*What it does:* replaces the face's normal field with a deliberately simple one.
*Why the terminator is smooth:*

* **Flat** — every face normal is aligned to one axis, so `N` is effectively constant and `N·L`
  is constant: there is no iso-contour on the face at all, so nothing can follow triangles. The
  face flips lit↔shaded as a unit and the cheek/nose/chin shading must come from a mask.
* **Cylinder** — normals are spherised and then flattened on one axis, giving the face the
  shading of a vertical cylinder: the terminator becomes a clean vertical curve that sweeps
  across the face as the light turns.
* **Two Halves** — the face is split in half and each half's normals aligned/rotated, then
  smoothed, giving two planes with a soft crease.

*In Blender:* all three are done by writing **custom split normals** on the face. Natively:
`Mesh → Normals → Point to Target` with the **Align** option (all normals point in one direction —
this is "Flat"), `Rotate`, `Average`, `Merge`, `Split`, `Smooth Vectors`, `Copy/Paste Vector`,
plus the **Normal Edit** and **Weighted Normal** modifiers.
[Blender manual, Editing Normals](https://projects.blender.org/blender/blender-manual/raw/branch/main/manual/modeling/meshes/editing/mesh/normals.rst)
*"Two Halves"* is exactly `Point to Target → Align`, select half, rotate, then
`Smooth Vectors` ([VRCLibrary, Custom Normals 3](https://vrclibrary.com/wiki/books/applying-custom-normals-to-avatars/page/custom-normals-3-two-halves-face)).
The three-way taxonomy with per-technique tool steps is [VRCLibrary, "Applying Custom Normals to
Avatars"](https://vrclibrary.com/wiki/books/applying-custom-normals-to-avatars/page/introduction) —
its introduction names the problem as the "**blob face**": "Cartoon and anime faces were never
meant to be lit the way they're shaped, so ugly shadows from the nose, lips and recesses of the
eyes give away the fact that you're looking at a 3D model and not an illustration." **[established]**
(VRChat-community documentation rather than vendor/publisher documentation.)

### 3. Normal Edit modifier — Radial / Directional / Parallel Normals

*What it does:* generates a parametric normal field and mixes it into the existing normals.
*Why it can help:* it lets you replace or partially bend the normal field toward a shape you
choose, so the terminator stops being a function of the triangle layout.
*In Blender:* `Normal Edit` modifier, **Radial** (normals radiate from a target's origin, "as if
they were emitted from an ellipsoid surface") or **Directional** (normals converge on / run
parallel to a target), with **Mix Factor**, **Vertex Group**, **Max Angle** and
**Lock Polygon Normals**.

The official manual page states the toon use case outright:
> "This modifier can be used to quickly generate radial normals for low-poly tree foliage or
> **'fix' shading of toon-like rendering by partially bending default normals**…"
> "…a *Normal Edit* modifier is used to bend them towards the camera. This shading trick is often
> used in games to fake scattering in trees and other vegetation."

[Blender manual, Normal Edit Modifier](https://projects.blender.org/blender/blender-manual/raw/branch/main/manual/modeling/modifiers/normals/normal_edit.rst) **[established]** — this is the single most
authoritative confirmation that bending normals to fix toon-like shading is documented practice,
not folklore.

### 4. Weighted Normal modifier

*What it does:* recomputes custom normals from face-area / corner-angle weights.
*Why it matters here:* it changes how the normal field is distributed, which changes where the
`N·L` iso-contours run; the manual notes it "can be useful to make some faces appear very flat
during shading". It does **not** smooth a terminator by itself — it re-weights, it does not
smooth the field.
*In Blender:* `Weighted Normal` modifier, with *Weighting Mode* (Face Area / Corner Angle / both),
*Weight*, *Threshold*, *Keep Sharp*, *Face Influence*, *Vertex Group*.
[Blender manual, Weighted Normal Modifier](https://projects.blender.org/blender/blender-manual/raw/branch/main/manual/modeling/modifiers/normals/weighted_normal.rst) **[established]**. Note the honest caveat from the
vendor docs in §7: face-area weighting is "counterproductive in areas that are supposed to be
smooth, clean surfaces" — the *inverse* of face-area weighting is what smooths. **[established]**
[Fondant, Laplacian Smooth explanation](https://tools.fondant.gg/custom-normals/laplacian-technical/)

### 5. Data Transfer of custom normals from a smoothed / subdivided copy

*What it does:* builds a proxy whose normal field is what you want, then copies its normals onto
the real (low-poly) mesh as custom split normals.
*Why the terminator is smooth:* the destination mesh keeps its geometry but inherits the proxy's
smooth field, so `dot(N,L)` varies smoothly and its level set is a clean curve.
*In Blender:* `Data Transfer` modifier → **Face Corner Data → Custom Normals**, with a mapping
(*Nearest Corner and Best Matching Normal*, *Nearest Face Interpolated*, *Projected Face
Interpolated*), *Max Distance*, *Ray Radius* and *Islands Precision*.
[Blender manual, Data Transfer Modifier](https://projects.blender.org/blender/blender-manual/raw/branch/main/manual/modeling/modifiers/modify/data_transfer.rst);
the manual's own Normals page points at it: "Mesh Data Transfer … for copying normals from
another mesh." **[established]**

### 6. Proxy mesh normals ("anime face proxy")

*What it does:* a dedicated simple-shaped proxy object (a plane-ish face shell, a cylinder, an
ellipsoid) supplies the normals; a modifier transfers them onto the real face.
*Why the terminator is smooth:* same as §5, but the proxy's shape is chosen for the *drawing*, not
for the mesh. The vendor documentation is explicit that the goal is not to preserve detail but to
*reduce* it: "Stylized characters don't just need their shading to be clean, they need it to be
the right stylistic shape. This often means that **the shading shapes you need are not actually
those your geometry will produce**. In places like anime character faces, the shading needs to be
that of a much simpler surface. The solution is to use a separate proxy mesh that is the correct
shape and transfer Normals from it."
*In Blender:* hand-built proxy + `Data Transfer`, or a purpose-built addon. The two I found:

* **Fondant "Easy Custom Normals"** — free tier (Weighted Normals + Laplacian Smooth modifiers),
  Pro tier (8–10 USD) adds the **Anime Face Proxy** system: a rigged Bézier curve set fitted to
  chin/ears/brow, a `Face Proxy Normals` modifier transferring curve normals to face geometry, an
  auto boundary mask so only the face is affected, and MeshRig/shape-key integration.
  [official docs](https://tools.fondant.gg/custom-normals/), [Face Proxy FAQ](https://tools.fondant.gg/custom-normals/face-proxy-tips-faq/),
  [CGWorld news writeup](https://cgworld.jp/flashnews/01-202507-EasyCustomNormals-v2.html).
* **ASP / Anime Shading Plus** calls the same category "**proxy normal**" and lists it as one of
  the common solutions alongside the SDF face shadow map.
  [ASP docs, Face Shadow Map](https://erichu33.github.io/ASPDocs/en/articles/face-shadow-map-creation-and-baking-workflow.html)

**[established]** for the technique existing and being purpose-built and sold; the exact tool
internals are vendor-described, not independently verified by me.

A caveat worth flagging: **I could not find a canonical "transfer normals from a sphere/capsule"
tutorial.** The *concept* is richly documented (radial normals, sphere/cylinder normals, proxy
normals, "Point to Target → Spherize"), and one Japanese Blender write-up is titled
"VRoid 顔の影をきれいなアニメ系イラスト調に直す 法線転写 Blender" (fixing VRoid face shadows into a clean
anime style by **normal transfer**) — but I could not read its body (JS-rendered page), so I am not
counting it as evidence for a specific workflow. Treat the literal "sphere/capsule transfer" framing
as **folklore/dialect** rather than a named standard technique.

### 7. Smooth the normal field itself (Laplacian smoothing), not the mesh

*What it does:* iteratively smooths the *normal vectors* across connected topology, weighting each
neighbour by edge length / face size.
*Why the terminator is smooth:* plain smoothing (Blur) is not sensitive to edge length or face
size, so on an uneven triangulated mesh it does not remove the topology signal from the field;
Laplacian smoothing is, so it does. The vendor docs claim: "**No more worrying about triangles
messing up the shading**" and "make topology not matter for shading … You get the same results as
a clean quad mesh on uneven triangulated meshes".
*In Blender:* `Mesh → Normals → Smooth Vectors` (averages custom normals with neighbouring
vertices' normals, `Factor` controls strength)
([manual](https://projects.blender.org/blender/blender-manual/raw/branch/main/manual/modeling/meshes/editing/mesh/normals.rst)); or the Fondant addon's Geometry-Nodes Laplacian Smooth Normals
modifier, which is **slower but topology-independent** and can be driven by any attribute (vertex
groups, vertex colours, other GN attributes).
[Fondant Laplacian explanation](https://tools.fondant.gg/custom-normals/laplacian-technical/) **[established]**

Two documented limitations from the same page, both directly relevant to the reported defect:
* "This is greatly reducing **Linear Interpolation issues at the vertex level, but does not help
  with those in the shader itself (such as on very long edges)**."
* "Smoothing does not necessarily get you to the stylistically correct shading shape for any given
  object. For more artistic control, the proxy mesh workflow is still best."

### 8. Geometry Nodes: `Set Mesh Normal`, and the `custom_normal` attribute

*What it does:* writes normals procedurally.
*In Blender:* the **Set Mesh Normal** node (`Free` / `Tangent Space` / `Sharpness` modes, domain
Point / Face / Face Corner) writes the `custom_normal` attribute; `custom_normal` lives on the
**face corner** domain. **Free** normals are plain object-space vectors — fast, but **static: they
do not follow deformation**; **Tangent Space** normals are slower but deformation-dependent, which
is what you want on a rigged face. The manual even names the use case: the older
"custom split normals" concept exists "mostly … in game development, where it helps counterbalance
some issues generated by low-poly objects".
[Set Mesh Normal node](https://projects.blender.org/blender/blender-manual/raw/branch/main/manual/modeling/geometry_nodes/mesh/write/set_mesh_normal.rst);
[Mesh Structure → Custom Split Normals / Free Normals](https://projects.blender.org/blender/blender-manual/raw/branch/main/manual/modeling/meshes/structure.rst) **[established]**

Version caveat: the Fondant docs (targeting Blender 4.3/4.4) state "Geometry Nodes has no way to
**input** existing Custom Vertex Normals until 4.5". Blender 5.x has `Set Mesh Normal`, so on 5.2
this path is open. **[established for the versions quoted]**

### 9. What Blender's `Geometry` node actually outputs for a mesh with custom normals

* `Normal` — "Shading normal at the surface (includes smooth normals and bump mapping)". This is
  the one a toon shader wants, and it **includes custom split normals**.
* `True Normal` — "Geometry or flat normal of the surface". This is *supposed* to ignore custom
  split normals; it is documented nowhere in that sense, and there is an EEVEE known issue where it
  does not ignore them: *"EEVEE: Geometry Shader Node's 'True Normal' not ignoring Custom Split
  normals"*, blender#136677 (reported 4.4, Status/Archived). The reporter also notes Cycles and
  EEVEE behave differently, and that no manual text mentions custom split normals for this node.
[Geometry node manual](https://projects.blender.org/blender/blender-manual/raw/branch/main/manual/render/shader_nodes/input/geometry.rst),
[issue #136677 via the Gitea API](https://projects.blender.org/blender/blender/issues/136677) **[established]**

Practical reading: hook your toon threshold to `Geometry → Normal`, and be aware that if you ever
need the *geometric* normal you cannot fully trust `True Normal` on a mesh with custom normals.

### 10. Increase the effective vertex density — subdivision, and Phong tessellation

*What it does:* adds vertices so the normal field (or the interpolated `N·L`) has enough resolution
to describe a clean curve.
*Why it helps:* more triangles means the level set of `N·L` is a polyline with many more, much
shorter segments — and more importantly, the *field gradient direction changes less abruptly* at
each edge, so the kinks become invisible.
*In Blender:* a `Subdivision Surface` / `Subdivide` copy used only as a normal source (§5), or
actual density on the face.
*Sources:* the Unity-Chan Toon Shader manual's own troubleshooting for artefacts at the edges of
each colour band lists "**Increase mesh density** — By doing this, most of the artifacts will
disappear", and its Phong Tessellation section says "Phong Tessellation … is an **effective method
for smoothing low-poly meshes**"
([UTS2 manual](https://raw.githubusercontent.com/unity3d-jp/UnityChanToonShaderProject/master/Manual/UTS2_Manual_en.md) —
see the URL list at the end for the exact link). PotaToon's best-practice page puts it bluntly:
"**Subdivide the facial mesh as much as possible.**"
([PotaToon, Best Practices](https://potatoon.dev/en/best-practice)) **[established]**

Note what this implies: the recommended remedy for a colour-edge artefact in the most widely used
anime toon shader family is *more geometry*, not more anti-aliasing. That is strong evidence that
the class of artefact is geometric.

### 11. Fix the topology, and orient the triangles along the terminator

*What it does:* adds support loops where the terminator has to bend sharply, and chooses the
diagonal direction of triangles/quad splits so it does not cut across the terminator.
*Source — this is the most on-point text I found anywhere, from the Face Proxy FAQ:*
> "**I can't get the area around the jaw line to look good. How do I get a smooth shape without
> jaggedness in the shading?** … This area is very difficult to get right because the amount of
> curvature amplifies problems related to vertex density, face shape, and triangulation. Generally,
> it can be improved by **adding more loops along the jawline (like a bevel) to support the
> curvature**. **Make sure triangles are crossing quad faces in the correct direction: towards the
> center and down, matching the likely angle of the shading terminator.** Laplacian Smooth also
> helps. Or remove the jawline curvature entirely by masking to Radial Normals or making your own
> Boundary Mask."

[Fondant, Face Proxy Tips & FAQs](https://tools.fondant.gg/custom-normals/face-proxy-tips-faq/) **[established]**
— "jaggedness in the shading" on a low-poly anime face, caused by triangulation, fixed by geometry.

### 12. Put the terminator in a texture — the mask / shading-grade-map approach

*What it does:* a texture channel decides where the shaded region starts, so the contour is a
texture contour and has no relationship to the mesh's triangle edges.
*Why the terminator is smooth:* the mask is sampled by UV (or by a light-angle index, §13), both of
which are independent of the normal field. The Unity-Chan Toon Shader manual states the property
directly for its Shading Grade Map: "These maps allow you to **set shadows of any shape and in any
place you like, regardless of geometry or vectors**."
*In Blender:* sample a mask texture (or a vertex-colour / attribute) and use it to bias the
threshold: `step = base_threshold + mask * bias`. That is precisely what the game's own shaders in
this repository do (`base.r` in `UmaShaderFace.shader`, `_TripleMaskMap.g` in
`UmaMusashi Face.shader`).
*Sampling so the edge does not betray the mask's own resolution:* UTS2 documents "**Blur Level of
ShadingGradeMap** — Blur the Shading Grade Map **using the Mip Map function**" (default 0)
([UTS2 manual](https://raw.githubusercontent.com/unity3d-jp/UnityChanToonShaderVer2_Project/master/Manual/UTS2_Manual_en.md)).
The two documented mask flavours in UTS2 are the **Position Map** ("designates shadows that you
want to cast regardless of the lighting… Indicates areas that must have a shadow in black") and the
**Shading Grade Map** ("control the sharpness and intensity of shadows in relation to the
lighting"). Authoring advice is to paint them in a 3D painter such as Substance Painter.
**[established]**

*Important nuance* **[inference, from reading the vendored shaders]**: masking alone does **not**
guarantee a polygon-free line. If the mask is *multiplied into* an `N·L` term (as both Uma shaders
do), the `N·L` term still imposes its own structure wherever it dominates. For the mask to place
the line by itself it has to be the dominant or sole input — which is what the SDF scheme in §13
does.

### 13. Angle-keyed **SDF face shadow map** — the industry answer

*What it does:* stores, per texel, the light angle at which that texel crosses from shaded to lit.
The lookup index is an **object-level angle**, not `N·L`, so the terminator's *shape* is entirely
authored in UV space and the whole face is shaded as if it were a plane.
*Why the terminator is smooth and polygon-free:* the shading result "depends on and only on the
light angle", which the Blender reimplementation describes as "equivalent to shading a *plane*
with a uniform normal" ([mos9527, PJSK Blender pipeline part 3](https://mos9527.com/posts/pjsk/shading-reverse-part-3/)).
Nothing in the computation reads the per-triangle normal, so nothing can follow a triangle edge.
*How it is authored:* hand-paint N threshold images for light angles across 0–180° on one side of
the face, then bake them into one SDF texture with a distance-transform tool. ASP documents the
concrete workflow: 9 hand-painted textures, then `Tools → ASP → SDFGenerator → Create SDFs &
Merge`
([ASP docs](https://erichu33.github.io/ASPDocs/en/articles/face-shadow-map-creation-and-baking-workflow.html)).
Distance-transform tooling: Valve's SIGGRAPH 2007 alpha-tested magnification and the 8SSEDT
algorithm ([Lisapple/8SSEDT](https://github.com/Lisapple/8SSEDT),
[Yu-ki016/SDFTool](https://github.com/Yu-ki016/SDFTool) — generate SDF and SDF atlases from
black-and-white images).
*Industry provenance:* Tango Gameworks' GDC 2024 talk **"Toon Rendering in Hi-Fi RUSH"** covers
"**toon face shadow implementation**", and a slide is indexed as *"The shadow shapes are determined
by NdotL **'except for the face'**"*
([GDC Vault](https://gdcvault.com/play/1034330/3D-Toon-Rendering-in-Hi),
[80.lv summary](https://80.lv/articles/the-making-of-hi-fi-rush-s-3d-toon-rendering-style)).
*Blender implementation, concretely:*
* [mos9527, part 3](https://mos9527.com/posts/pjsk/shading-reverse-part-3/) — builds the face
  basis from the **Head bone's world-space Euler via a driver** (so it follows animation), derives
  the orthonormal basis with Frisvad's method (no cross product/normalisation), computes
  `cosθ = L·N` and `cosφ = L·T`, sign-selects the side with `b = n × t`, and mirrors `u = 1-u` for
  the [−90°,0°] half (which requires a symmetric face or two SDF textures). It explicitly warns
  that many Blender SDF tutorials wrongly assume the shaded face's normal is the +Y axis.
* The Blender addon **AnimeFaceShadow** (Blender 5.1) auto-bakes the SDF: it builds a **proxy
  (cage) mesh** from the selected mesh, then **Voxel Remesh → Smooth** to get a smooth outer shell,
  and generates the gradient texture at **32-bit float to prevent banding**; at runtime a driver
  follows the Sun Light and blends the SDF on the X and Z axes with a Y-offset correction, plus an
  RGB detail mask for direction-dependent highlights
  ([BOOTH listing](https://booth.pm/en/items/8223563)).
* Note the ASP remark that the choice usually comes down to work: "Common solutions include using
  proxy normal or manually modifying the model normal in third-party DCC, and using SDF-based face
  shadow map. Face shadow map is the most efficient method in my opinion. It has high
  controllability for artists and does not take as much time as modifying normals manually."
**[established]**

**The primary description of the technique (GDC 2024, first-party).** Tango Gameworks' talk, read via
a slide-by-slide transcription:

> "**Except for the face**, all other parts of the character are shaded by thresholding the dot of
> normal and light."
>
> Face was first done by editing normals; three listed failures: (1) hard to generate smooth curves,
> (2) **facial animation destroys the shadow silhouette**, (3) artists cannot fix it by model
> settings alone.
>
> Solution: "a texture similar to a **heightmap**. Internally we call this a **threshold map**."
>
> Sampling: "compute the **ANGLE** between the horizontal component of the face-facing direction and
> the light direction. 'Horizontal component' means the corresponding horizontal component in the
> **face BONE's coordinate system**. The result is the angle divided by Pi, normalized. (**Note: this
> is NOT the dot-product result**)". Then sample the threshold map and compare.
>
> "This texture has a left/right direction." / "When light comes from the left, you must horizontally
> flip the threshold map."
>
> Baking: bake 180° in a DCC → the artist manually adjusts the edge parts → "merge the adjusted bakes
> by interpolating them in a **distance-field-like** way."

([transcription of the GDC talk](https://game.3loumao.org/382235095); the
[GDC Vault page](https://gdcvault.com/play/1034330/3D-Toon-Rendering-in-Hi) itself 403s to
automated fetches) **[established]**. Note the three failure modes of the *normal-editing* route —
they are exactly the objections that apply to any normals-only fix, including §1.2 and §1.6, and the
second one matters here because our face is morph-driven.

**One nuance that costs people time:** the stored value is a **light-angle threshold, not a
Euclidean distance**; only the *merge* step is distance-field-like. Calling the texel "the distance
to the terminator" is loose usage. **[established]**

**Confirmed in real shader source** (three independent implementations, all agreeing):

* [Knosiz/URPSimpleGenshinShaders](https://github.com/Knosiz/URPSimpleGenshinShaders) —
  `step(normalizedFdotL, faceShadowMap)` on the **red** channel, i.e. the texel *is* the threshold;
  two L/R maps or a flipped sample. Its shipped textures are `Avatar_{Boy,Girl,Lady,Loli,Male}_
  Tex_FaceLightmap.png` — **five standardised body-type maps, not per-character**. Its FAQ documents
  the two practical traps: "For smooth shadow edges, set shadow gradient texture's **compression
  quality to high**", and "Shadow coverage doesn't change based on head facing — … set the
  character's **head bone** as the character head mesh's skinned mesh root."
* [ashyukiha/GenshinCharacterShaderZhihuVer](https://github.com/ashyukiha/GenshinCharacterShaderZhihuVer)
  — uses `acos(RightL)/PI - 0.5`, i.e. **linear in angle**, matching Hi-Fi RUSH; samples the *same*
  texture at `uv.x` **and** `-uv.x` and selects with `min(step(RightL, a), step(-RightL, b))`
  (explicit mirroring); `_FaceShadowMapPow` (0.2) is applied to the texel "to flatten the middle so
  most of the change is gradual".
* **lilToon 1.8.0+** (`lil_common_frag.hlsl`, `_ShadowMaskType == 2`) — an **RG** texture (R one
  side, G the mirrored side) selected by `sign(dot(L.xz, objectRight.xz))`; `_ShadowFlatBlur`
  multiplies the **Y** component of both forward and light before normalising, i.e. it deliberately
  suppresses the vertical light component so shading follows the horizontal angle; and the texel is
  added as a **bias** to `N·L` rather than compared with `step` — the same threshold semantics
  expressed as an offset. Note `aastrencth = 0`: AA is switched off for the face mask because the
  authored texture already supplies the soft edge.
  ([lilToon](https://github.com/lilxyzw/lilToon))

**Hand-painting the RG texture, documented step by step:** paint **one** grayscale gradient in face
UV space (white on the face's left, black on the right, the terminator drawn as a *gradient*, plus
extra layers for the nose side, cheek highlight, mouth/nose and ears); duplicate and horizontally
flip it; channels-compose **R = original, G = flipped, B = 0** — which is exactly lilToon's expected
texture ([メタカル最前線 interview with modeller アノマロカリス](https://metacul-frontier.com/?p=20546)) **[established]**.
The same source's mirror-image in Blender is [puppaxlyu's write-up](https://puppaxlyu.blogspot.com/2025/11/sdf.html),
which drives the Mapping node's rotation from the Z-rotation of a single "light" empty via a driver
on a Value node — the Blender analogue of the runtime head/light basis. **[established]**

**The authoring rule that actually produces the smooth edge** — worth quoting because it is the part
people get wrong: if the black/white boundary lands on the *same* spot in several of the successive
angle images, the shadow "pops" abruptly at that angle; **deliberately staggering the boundary between
successive images** gives the gradual change
([onigiri-tiken memo on lilToon SDF face shadow](https://scrapbox.io/onigiri-tiken/%E3%80%90Unity%E3%80%91%E3%82%B7%E3%82%A7%E3%83%BC%E3%83%80%E3%83%BC%EF%BC%9AlilToon%E3%81%A7SDF%E3%82%92%E4%BD%BF%E3%81%A3%E3%81%9F%E9%A1%94%E5%BD%B1)) **[established]**.
The community tool that does the merge (`sdf_tool`, by AnimeXD) also smooths the seams automatically
("なじませも自動でやってくれる") and requires 8-bit RGB with **no alpha** and contiguous filenames.

**Blender-native generators (the practical answer if this has to be done in Blender):**

* [EmuMan/npr-face-shader](https://github.com/EmuMan/npr-face-shader) — **free and open source**, and
  the most complete Blender answer I found. You draw grease-pencil strokes directly on the face
  surface (vertical strokes = light-range segments, shapes = regions that stay shaded longer, e.g.
  the nose; or stay lit longer); the addon projects them through a **second UV map laid out as a
  front-horizontal projection** of the face, rasterises into an image, applies a **box blur**
  ("Blur Size … a larger value means smoother transitions and less exact line following"), and
  **auto-builds the material and node group with drivers bound to the Z-rotation of a Sun object and
  a Head object**. Stated limitation: horizontal lighting only. **[established]**
* `shop_yamanobu` **AnimeFaceShadow** (BOOTH, ¥300) — computes the field on a **proxy (cage) mesh**
  built from the selection and smoothed with **Voxel Remesh → Smooth**, then bakes the gradient from
  that shell at 32-bit float to avoid banding. This is the cleanest available answer to "why does the
  generated field not follow the polygons": **the field is computed on a smoothed proxy shell, so the
  render mesh's own faceting never enters it.** Sun-driven, horizontal X + vertical Z blending plus a
  Y offset, and an RGB detail mask (R = highlight when lit from the right, G = from the left).
  ([BOOTH](https://booth.pm/ja/items/8223563)) **[established]**
* **Calappa Lab CL SDF Tools** (BOOTH, ¥500) — Blender-only painting at 15°/22.5°/30° steps with
  mirror-symmetrise, converts to one SDF texture, PNG 8/16-bit, up to 4096 gradations, and writes the
  horizontally mirrored version as well. Its own copy states the premise: *"if you leave the face
  shadow to the light, the nose and cheek relief shows through as-is and looks wrong as a picture."*
  ([BOOTH](https://booth.pm/ja/items/8779114)) **[established]**

**Negative result worth having:** I could not find an SDF face shadow, a "Face Shadow" feature or a
"high-precision face" mode in UTS2/UTS. Its artist-placed-shadow answer is the **Position Map** — a
UV-space offset of the shadow border — not a light-angle-keyed threshold. **[established negative]**

**What I did *not* find:** no one documenting an object-space **Gradient Texture** as a face
terminator. It would give a terminator that sweeps with object rotation, but as a *linear* gradient
across the whole face it cannot say where the nose or cheek shadows belong — which is exactly the
value a per-texel-authoritative mask has. A static UV-sampled mask is just the degenerate
single-angle case and never moves with the light; UTS's Position Map is the shipped version of that
idea. **[inference]**

### 14. Screen-space derivative anti-aliasing of the step (`fwidth`) — absent in Blender

*What it does:* `saturate((x - threshold) / fwidth(x))` makes the transition about one pixel wide
everywhere, whatever the gradient is, which removes the stair-stepping *of a hard edge*.
*Why it is listed here:* it is what the game's own shader does, and it is the reason the game's
edge does not show pixel staircase on top of whatever shape the terminator has — it does **not**
fix the terminator's shape.
*In Blender:* **[established — established by absence, from the node indexes]** Blender exposes no
screen-space derivatives to shader nodes. The Utilities node category is Math, Vector, implicit
conversion, Repeat, Closure/Evaluate Closure, Bundle, Switch, Script — no derivative node; the Shader
Nodes index has no derivatives category; EEVEE's "Supported Nodes" list contains none; and a Stack
Exchange API search for `fwidth` / "screen space derivative" on blender.stackexchange returns **zero**
questions. The only route is **OSL through the Script node**, which is marked Cycles-only and needs
OSL enabled on CPU. ([Utilities index](https://projects.blender.org/blender/blender-manual/raw/branch/main/manual/render/shader_nodes/utilities/index.rst),
[Shader Nodes index](https://projects.blender.org/blender/blender-manual/raw/branch/main/manual/render/shader_nodes/index.rst),
[EEVEE supported nodes](https://projects.blender.org/blender/blender-manual/raw/branch/main/manual/render/eevee/limitations/nodes_support.rst),
[Script node](https://projects.blender.org/blender/blender-manual/raw/branch/main/manual/render/shader_nodes/utilities/script.rst)).
The canonical Unity form, for reference, is
`float t = dot(N, L); float w = fwidth(t); smoothstep(0, w, t);`
([ronja-tutorials SteppedToonLighting.shader](https://github.com/ronja-tutorials/ShaderTutorials/blob/master/Assets/031_StepToon/SteppedToonLighting.shader),
[generic UV form](https://github.com/DeGGeD/ShaderStory/blob/main/Chapters/Derivatives/EdgeTransition.md)).
Blender's substitutes — a wider ColorRamp band, or relying on bilinear/mip filtering of a ramp
texture — are **fixed width in `dot` units, not screen-space-derivative-driven**, so their edge width
does not track the object's on-screen size, which is precisely what `fwidth` solves. **[inference]**
And note the limit that matters here: `fwidth` fixes the *staircase*, never the *polyline* — the kink
where two triangles meet is still there, merely antialiased. **[inference]**

### 15. Render-level anti-aliasing (only ever a partial remedy)

*What it does:* reduces pixel staircase at the edge.
*In Blender:* EEVEE uses TAA, "sample based so the more samples the more aliasing is reduced";
Film → **Filter Size** "controls how much the image is softened; lower values give more crisp
renders, higher values are softer and reduce aliasing". Cycles: Film → Pixel Filter (*Box* /
*Gaussian* / *Blackman-Harris*) and *Width*.
[EEVEE Sampling](https://projects.blender.org/blender/blender-manual/raw/branch/main/manual/render/eevee/render_settings/sampling.rst),
[EEVEE Film](https://projects.blender.org/blender/blender-manual/raw/branch/main/manual/render/eevee/render_settings/film.rst),
[Cycles Film](https://projects.blender.org/blender/blender-manual/raw/branch/main/manual/render/cycles/render_settings/film.rst) **[established]**

**These cannot fix a geometric terminator.** They blur the edge by roughly a pixel; they do not
move or reshape the contour. **[inference — well supported:** the remedies the sources above give
for colour-edge artefacts are mesh density and normals, never AA; and the Fondant docs describe the
problem as normals/topology, "make triangles and topology spacing not matter for shading".**]**

---

### 16. Softening the transition — a real workaround with a real cost

*What it does:* widens the gradient band around the threshold (a wider smoothstep / ColorRamp band),
so the boundary is no longer a hard cut.
*Why it helps:* it lowers the contrast across the transition so the eye cannot localise the corner.
*Cost, and this is the part usually left out:* it does **not** move the contour. The centreline is the
same polyline; each tone level of a multi-step ramp is a *different* level set, producing several
nested polylines whose kinks do **not** coincide — which is exactly where banding along triangle edges
comes from. And because the gradient's slope jumps at every triangle edge, a **wide smooth** gradient
can make triangle-edge Mach bands *more* visible, while a hard cut collapses almost the whole gradient
into one discontinuity and hides them. You are trading a polygonal terminator for edge banding.
**[inference, built on the gltut citations in §3]**

It is nonetheless a shipped, standard workaround: Godot's built-in Toon diffuse mode is documented as
"Provides a hard cut for lighting, with smoothing affected by roughness"
([Godot standard material docs](https://raw.githubusercontent.com/godotengine/godot-docs/master/tutorials/3d/standard_material_3d.rst)) **[established]**.

**Blender's own "terminator" settings — know what they target.** Blender documents low-poly terminator
artefacts as a shading-normal-vs-geometry problem and offers a *bias* rather than more samples:

* EEVEE → Object Properties → Shading → **Shadow Terminator**: "help reduce artifacts that appear
  along the edges of low-poly or smoothly shaded objects, especially when using bump mapping. These
  artifacts occur when **shading normals deviate from the actual geometry**, causing abrupt shadow
  breaks."
* Cycles → Object Properties → **Geometry Offset** ("Offset rays from the surface to reduce shadow
  terminator artifacts on low-poly geometry") and **Shading Offset** ("Pushes the shadow terminator
  (the line that divides the light and dark) towards the light to hide artifacts on low-poly
  geometry"), with the documented caveat that Shading Offset "**artificially alters the scene's
  lighting and is not energy conserving** and consequently not physically accurate".

([EEVEE object data](https://projects.blender.org/blender/blender-manual/raw/branch/main/manual/render/eevee/object_settings/object_data.rst),
[Cycles object data](https://projects.blender.org/blender/blender-manual/raw/branch/main/manual/render/cycles/object_settings/object_data.rst)) **[established]**
**But** these act on the shadow-ray / BSDF terminator, so they will **not** necessarily fix an
arbitrary per-pixel threshold you apply to `dot(N, L)` yourself via a shader-node graph.
**[inference]** A renderer vendor documents the same root cause independently — MoonRay: shading
normal `Ns` deviating from geometric normal `Ng` causes harsh terminators, and its remedy is a
terminator *softening* shader, citing Deshmukh & Green and Chiang et al., "Taming the Shadow
Terminator" ([MoonRay shadow terminators](https://docs.openmoonray.org/user-reference/how-to-guides/shadow-terminators/)) **[established]**.

One more established trap, specific to Blender exports: **Blender does not document Output → Format →
Resolution % as a supersampling control** — only as a size slider "useful for small test renders".
Rendering at 200 % and downscaling *does* give SSAA, but that is community practice, not documented
Blender behaviour ([Output format](https://projects.blender.org/blender/blender-manual/raw/branch/main/manual/render/output/properties/format.rst)) **[established]**.

---

## 2. The most likely fix for this specific case

Reasoning first, then the recommendation.

**The game does not get its clean face line from `N·L`.** Per §0 it redirects the shading normals
(cylinder blend, and a per-texel detail mask that points the face normals along the look
direction) and biases the step with a mask channel. A low-poly face with a `N·L`-only threshold
and normals that actually follow the surface *cannot* produce a smooth anatomical terminator —
that is the defect, and it is geometric (§1.1, §1.7, §1.11, §1.13).

**First, split the diagnosis, because the two candidates have different fixes:**

| what you see | cause | fix |
|---|---|---|
| boundary is **exactly** the polygon edges; rotating the light makes the shaded area change in whole-triangle jumps | the mesh is being shaded **flat**: per-face normals, i.e. `Shade Flat`, a `Smooth by Angle` threshold, or custom split normals that were dropped/cleared on import | restore/author the custom split normals (§1.1, §1.2) |
| boundary is a **polyline with kinks** that cuts through triangle interiors and does not coincide with edges | the mesh is smooth-shaded, but the normal field is too coarse / not shaped for the terminator | §1.2, §1.6, §1.7, or move the line into a mask (§1.12/§1.13) |

The second row is what the workspace doc already measured: clearing the custom normals produced
"hard angular patches on the cheeks and jaw". **[established for the repo's own measurement]**

**Then, in order of cost:**

1. **Make sure the authored face normals survive the round trip** and are not being recomputed.
   This is the cheapest check and the repo doc says it is the difference between clean and
   angular. Also confirm nothing is running `Shade Auto Smooth`/`Smooth by Angle` on the face, and
   that no modifier in the stack discards custom normals.
2. **Apply the game's own shading-normal formula** — the cylinder blend with `_CylinderBlend`
   driven by the vertex-colour blue channel (which the exporter already writes into the `.pmx`'s
   extra UV slot and which `mmd_tools` imports as `_UV3.x`). This reproduces the reference instead
   of approximating it, and it is the technique the model was authored against. **[established via
   the workspace doc's decompile comparison]**
3. **Match the game's flat-face behaviour**: with the base map's detail mask selecting the face,
   point the face and eyebrow shading normals along the look direction. This removes the
   polygon-following terminator entirely — the face is shaded as a plane and the cheek/nose
   shadow comes from the mask, which is exactly what the game renders. This is the technique most
   likely to make Blender agree with the game, and per the workspace doc it is already implemented
   in the addon (`Face shading from the base map's detail mask`, on by default). If the reported
   defect is still present, the most likely explanations are that the face branch is off for the
   material in question, that this particular model's base map has no usable G channel, or that a
   morph is deforming the normals — all checkable. **[inference]**
4. **If a partial, moving terminator on the face is genuinely wanted** (rather than a whole-face
   flip), use an angle-keyed SDF face shadow map (§1.13). This is the only documented approach
   that is *both* smooth *and* light-reactive on the face, and it is what Project Sekai, Hi-Fi
   Rush and the Genshin-style Unity shaders all do. Blender-side the documented end-to-end option is
   [`EmuMan/npr-face-shader`](https://github.com/EmuMan/npr-face-shader): it writes the texture *and*
   builds the node group with Sun/Head drivers; the commercial Blender generators do the same job.
   **But note the correction in §0.1: Uma Musume does not use an SDF face shadow**, so this step
   diverges from the reference rather than reproducing it. It is the right move if the goal is "a
   clean, light-reactive face line", the wrong move if the goal is "match this game's renderer".
   Cheaper middle ground, and closer to the reference: bias the threshold with the available mask
   (the game's `base.r` / `HardShadows` behaviour) and blur the mask (UTS2's mip-blur) so its own
   resolution does not show.
5. **Only then** spend effort on AA: enough EEVEE TAA samples, a sensible Filter Size, and render
   at a higher resolution if the edge still stair-steps. Do not expect this to move the line.

**What I would *not* do:** chase MSAA/supersampling as the fix, or rely on `Weighted Normal` alone
(it re-weights rather than smooths), or subdivide the real mesh if the export must stay low-poly
(use a subdivided copy as a normal source via `Data Transfer` instead, §1.5).

**A conclusion worth stating plainly, because it changes what "fix" means.** Every one of the
industry sources that solves this problem *stops deriving the face terminator from the interpolated
face normals.* Hi-Fi RUSH tried normals first and abandoned it for a threshold map, listing "facial
animation destroys the shadow silhouette" among the reasons; Cygames used flattened normals plus a
detail mask; Genshin/Honkai/Project Sekai use an angle-keyed SDF; lilToon biases the terminator with
an authored mask. So a low-poly face + `dot(N,L)` threshold is not a configuration that has a clean
fix *within* itself — one of the three inputs (the normal field, the threshold's position, or the
threshold's sharpness) has to stop being a function of the mesh.

---

## 3. How to tell aliasing from geometry — a procedure

The discriminator is that **aliasing is a property of the pixel grid and a geometric terminator is
a property of the object.** One depends on resolution; the other does not.

**Why the contour is a polyline in the first place [established].** The normal is interpolated
linearly across each triangle — *Learning Modern 3D Graphics Programming* ("gltut"), Fragment
Lighting: "The normal is being interpolated linearly across the surface… The edge between two
triangles changes how the light interacts. On one side, the nearly-linear gradient has one slope, and
on the other side, it has a different one. That is, **the rate at which the gradients change abruptly
changes**… the real source of the problem is that the normal is being linearly interpolated."
([gltut, Fragment Lighting](https://web.archive.org/web/20150225192608/http://www.arcsynthesis.org/gltut/Illumination/Tut10%20Fragment%20Lighting.html))
And the contour of a linear interpolant on a triangle is a straight line — SIGGRAPH's *Line Based
Contouring*: "A linear interpolant can be fitted over each triangle — and happily has **straight lines
as contours**… Piecewise linear models over each triangle can then be used, with the associated
straight line contour segments."
([SIGGRAPH, Line Based Contouring](http://education.siggraph.org/static/HyperVis/vised/VisTech/Techniques/s2dalinecontour.html))
So: `N` is affine within a triangle and `L` is constant for a directional light ⇒ `dot(N,L)` is
affine ⇒ `{dot(N,L) = t}` is a **straight segment** inside each triangle. Across a shared edge the
field is C⁰-continuous but its cross-edge derivative jumps, so there is a **corner exactly on the mesh
edge**. It is a topological feature of the shading function, not a sampling error. **[inference,
built on the two citations above]**

gltut also names the visibility mechanism, which is why this reads as "jagged" rather than as a
polyline: "human vision really wants to find sharp edges in smooth gradients … if there is a shape to
the gradient intersection, such as a line, we tend to see that intersection 'pop' out at us."

**[inference — the checklist below is my synthesis; each step's rationale is grounded in documented
mechanisms, but I could not find a single source that spells out this exact test for this exact
problem. The MSAA/SSAA step and the outline-shell step are the two exceptions: both are grounded in
sources, cited inline.]**

0. **Look at the wireframe through it.** Turn on the Wireframe overlay (or a wireframe-only
   material) and view the face straight on.
   * Steps that coincide *exactly* with triangle edges, edge for edge, at every zoom → **geometry.
     Specifically flat shading / per-face normals.**
   * Steps that are ~1 px and do not line up with edges → **aliasing.**
   * A boundary that cuts through triangle interiors in straight-ish segments with a kink at each
     edge it crosses → **geometry. Specifically a hard threshold on a too-coarse interpolated
     normal field.**
1. **Measure the step length in pixels.** Zoom to 100 % and count: a 1-pixel staircase is
   aliasing; a staircase 20–100 px wide is the object's own contour.
2. **Render at 2× and 4× and compare downsampled.** This is the resolution-scaling law: a 1-px tooth
   stays ~1 px as resolution rises (aliasing); a geometric corner's *position in mesh terms* does not
   move while its pixel height grows (geometry).
   * Contour unchanged in object space, staircase gone → the staircase was aliasing.
   * Contour still polygonal and now *longer* in pixels, still hugging the same edges → geometry.
3. **Sweep EEVEE's TAA render samples** (e.g. 1 → 16 → 64 → 256) and, separately, **Film → Filter
   Size** up and down.
   * Steps soften/disappear → aliasing.
   * Steps blur by about a pixel but the corner positions and the polygonal shape are unchanged →
     geometry. Filter Size is literally "slightly blurring the image to soften edges"; it cannot
     relocate a contour.
4. **The MSAA / SSAA split — the sharpest established discriminator.** MSAA adds *coverage* samples
   but not *colour* samples, so the fragment shader still runs once per pixel and a 1-bit
   shader-computed boundary is **not** antialiased by it: "MSAA increases the number of coverage
   samples, but not the number of color samples. However, since the number of color samples did not
   increase, fragment shaders are still run for each pixel only once."
   ([Godot 3D antialiasing](https://docs.godotengine.org/en/stable/tutorials/3d/3d_antialiasing.html);
   [Unity URP antialiasing](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@12.1/manual/anti-aliasing.html)
   — "does not fix shader aliasing issues".) SSAA/TAA, by contrast, *do* reduce shader aliasing.
   * Raising MSAA changes nothing, while SSAA/TAA smooths the staircase ⇒ **shader-computed
     threshold**, i.e. our case: the boundary is not a coverage edge at all.
   * SSAA at 400 % still leaves the **same polyline** ⇒ geometry confirmed.
5. **Toggle the shading mode on the face** (`Shade Flat` ↔ `Shade Smooth`). Blender documents the
   distinction as the interpolation of normals: Shade Smooth "does not actually modify the object's
   geometry; it changes the way the shading is calculated across the surfaces (**normals will be
   interpolated**)"
   ([Blender manual, Shading](https://projects.blender.org/blender/blender-manual/raw/branch/main/manual/scene_layout/object/editing/shading.rst)).
   * The boundary jumps to a completely different place / the whole face flips at once → normals are
     in play (geometry).
   * Nothing about the jaggedness changes → it was never a normal problem.
6. **Rotate the light in small steps and watch the boundary.**
   * The boundary moves *continuously* but keeps a polygonal shape whose segments are tied to the
     mesh → interpolated normals + hard threshold.
   * The shaded region *pops* in whole triangles → flat shading / per-face normals.
   * The staircase stays at the pixel level while the contour sweeps smoothly → aliasing.
7. **Rule out the outline shell first if the model has one.** A practitioner report on Blender Stack
   Exchange had exactly the symptom in the question title and the accepted answer attributed it to
   the inverted-hull outline mesh (Solidify + flipped normals + backface cull) still using shadow mode
   *Opaque*; setting it to *None* fixed it
   ([BSE 323205, "Eevee Toon Shader Shadows Looking Jagged and Choppy"](https://blender.stackexchange.com/questions/323205/eevee-toon-shader-shadows-looking-jagged-and-choppy-everything-is-set-to-shade)).
   This patch's models do ship an outline geometry-nodes modifier, so it is worth a minute.
   **[established, practitioner report]**
8. **Inspect the value being thresholded.** Wire `Geometry → Normal` into `dot(·, L)` and output
   it raw as Emission with a black-to-white ramp *without* the threshold. If the underlying
   gradient already shows straight bands with hard kinks at triangle edges, the problem is upstream
   of the threshold. `Geometry → True Normal` (the flat normal) shows you the pure triangle
   structure for comparison — but note issue #136677: on a mesh with custom split normals, EEVEE's
   `True Normal` may not ignore them.
9. **Check the whole-frame budget.** If MSAA/TAA is off or at 1 sample, you are seeing both
   problems at once. Fix the render-level AA first *only so you can see the geometric contour
   clearly* — then fix the geometry.

A useful sanity check on any "it's aliasing" hypothesis: **aliasing cannot survive a 4× supersample
with a proper filter, and cannot follow a light rotation while retaining a fixed object-space
polygon shape.** If the artefact has object-space identity, it is geometry.

---

## 4. Sources

Blender documentation (fetched as raw `.rst` from the manual repo, because the rendered pages are
nav-dominated and truncate):

* [Blender manual — Normal Edit Modifier](https://projects.blender.org/blender/blender-manual/raw/branch/main/manual/modeling/modifiers/normals/normal_edit.rst)
  — the authoritative statement that bending normals is used to "fix shading of toon-like
  rendering", plus Radial/Directional/Parallel Normals/Offset/Max Angle semantics.
* [Blender manual — Weighted Normal Modifier](https://projects.blender.org/blender/blender-manual/raw/branch/main/manual/modeling/modifiers/normals/weighted_normal.rst)
  — how face-area / corner-angle weighting changes custom normals.
* [Blender manual — Data Transfer Modifier](https://projects.blender.org/blender/blender-manual/raw/branch/main/manual/modeling/modifiers/modify/data_transfer.rst)
  — copying custom normals from another mesh, and the face-corner mapping modes.
* [Blender manual — Editing Normals](https://projects.blender.org/blender/blender-manual/raw/branch/main/manual/modeling/meshes/editing/mesh/normals.rst)
  — `Set from Faces`, `Point to Target` (Align/Spherize), `Average`, `Merge`, `Split`,
  `Smooth Vectors`, `Copy/Paste Vector` — the native tools for building a redirected normal field.
* [Blender manual — Mesh Structure → Normals / Custom Split Normals / Free Normals](https://projects.blender.org/blender/blender-manual/raw/branch/main/manual/modeling/meshes/structure.rst)
  — "helps counterbalance some issues generated by low-poly objects"; `custom_normal` lives on the
  face-corner domain; Free vs Tangent Space.
* [Blender manual — Set Mesh Normal node](https://projects.blender.org/blender/blender-manual/raw/branch/main/manual/modeling/geometry_nodes/mesh/write/set_mesh_normal.rst)
  — writing normals in Geometry Nodes (Sharpness / Free / Tangent Space).
* [Blender manual — Smooth By Angle modifier](https://projects.blender.org/blender/blender-manual/raw/branch/main/manual/modeling/modifiers/normals/smooth_by_angle.rst)
  — what `Shade Auto Smooth` actually adds in 4.1+.
* [Blender manual — Geometry (shader) node](https://projects.blender.org/blender/blender-manual/raw/branch/main/manual/render/shader_nodes/input/geometry.rst)
  — `Normal` = shading normal (smooth normals + bump); `True Normal` = geometry/flat normal.
* [Blender issue #136677](https://projects.blender.org/blender/blender/issues/136677) — EEVEE's
  `True Normal` does not ignore custom split normals; Cycles/EEVEE disagree.
* [Blender manual — EEVEE Sampling](https://projects.blender.org/blender/blender-manual/raw/branch/main/manual/render/eevee/render_settings/sampling.rst)
  and [EEVEE Film](https://projects.blender.org/blender/blender-manual/raw/branch/main/manual/render/eevee/render_settings/film.rst)
  and [Cycles Film](https://projects.blender.org/blender/blender-manual/raw/branch/main/manual/render/cycles/render_settings/film.rst)
  — TAA sample count, Filter Size, Cycles pixel filter.

Normals for toon/anime shading:

* [VRCLibrary — Applying Custom Normals to Avatars](https://vrclibrary.com/wiki/books/applying-custom-normals-to-avatars/page/introduction)
  (plus [Flat](https://vrclibrary.com/wiki/books/applying-custom-normals-to-avatars/page/custom-normals-1-flat-face),
  [Cylinder](https://vrclibrary.com/wiki/books/applying-custom-normals-to-avatars/page/custom-normals-2-cylinder-face),
  [Two Halves](https://vrclibrary.com/wiki/books/applying-custom-normals-to-avatars/page/custom-normals-3-two-halves-face))
  — the "blob face" problem statement and a three-way taxonomy of face-normal redirection with exact
  Blender tool steps.
* [Fondant — Custom Normals addon, overview](https://tools.fondant.gg/custom-normals/) — vendor
  documentation of the category: author normals for clean toon/cel shading; the anime face proxy
  mesh ("the shading shapes you need are not actually those your geometry will produce").
* [Fondant — Laplacian Smooth explanation](https://tools.fondant.gg/custom-normals/laplacian-technical/)
  — why toon shading is "faceted or jagged unless the mesh topology is very clean"; Blur vs
  Laplacian; the explicit limitation about shader-side linear-interpolation issues on long edges.
* [Fondant — Face Proxy Tips & FAQs](https://tools.fondant.gg/custom-normals/face-proxy-tips-faq/)
  — the jawline "jaggedness in the shading" answer: add loops, and orient triangles to cross quads
  along the terminator's direction.
* [CGWorld — Easy Custom Normals v2.0.0 release](https://cgworld.jp/flashnews/01-202507-EasyCustomNormals-v2.html)
  — independent trade-press confirmation of the addon and its Pro face-normal control system.
* [note.com — VRoid 顔の影を…法線転写 Blender](https://note.com/maasa_ym/n/ncd3a8512bc13) — found,
  cited by title only: a Blender normal-transfer write-up for exactly this problem. **I could not
  read the body** (JS-rendered), so I claim nothing about its contents.
* [blender2ogre — CustomSplitNormals.md](https://github.com/MichelePastena/blender2ogre/blob/master/CustomSplitNormals.md)
  — practical note that custom normals "can be useful … in anime characters that usually need
  specific shading profiles to look good with the toon shader".

Texture-mask terminator and SDF face shadow:

* [UTS2 manual (English)](https://raw.githubusercontent.com/unity3d-jp/UnityChanToonShaderVer2_Project/master/Manual/UTS2_Manual_en.md)
  — the most useful single document I found: **Position Map** and **Shading Grade Map** ("set
  shadows of any shape and in any place you like, **regardless of geometry or vectors**");
  mip-map blurring of the shading grade map; the colour-edge artefact troubleshooting that
  recommends **increasing mesh density**; Phong Tessellation as "an effective method for smoothing
  low-poly meshes".
* [PotaToon — Best Practices](https://potatoon.dev/en/best-practice) — "Subdivide the facial mesh
  as much as possible"; face SDF mask recommended; links two Blender face-shadow tutorials.
* [ASP (Anime Shading Plus) — Face Shadow Map: creation & baking workflow](https://erichu33.github.io/ASPDocs/en/articles/face-shadow-map-creation-and-baking-workflow.html)
  — states the problem ("broken light and shadow changes on the face"), names the two families of
  fix (proxy normal / edited normals, and SDF face shadow map), and gives the full authoring
  recipe: 9 hand-painted angle thresholds → SDF generator → merged texture → head-bone transform.
* [mos9527 — PJSK Blender cel-shading pipeline part 3: SDF face rendering](https://mos9527.com/posts/pjsk/shading-reverse-part-3/)
  — a **Blender-specific** implementation: Head-bone driver, Frisvad orthonormal basis, `cosθ = L·N`
  / `cosφ = L·T` with sign selection, `u = 1-u` mirroring; and the key conceptual claim that SDF
  turns a per-fragment problem into a per-object one, equivalent to shading a plane.
* [BOOTH — AnimeFaceShadow Blender addon](https://booth.pm/en/items/8223563) — auto-bakes an SDF
  face shadow in Blender from a **proxy/cage mesh → Voxel Remesh → Smooth** shell at 32-bit float
  to avoid banding; driver-linked to the Sun Light, X/Z blending plus Y offset.
* [Lisapple/8SSEDT](https://github.com/Lisapple/8SSEDT) — the signed distance transform used to
  turn an angle-threshold image into an SDF texture, with the Valve SIGGRAPH 2007 reference.
* [Yu-ki016/SDFTool](https://github.com/Yu-ki016/SDFTool) — a tool that generates SDF images and
  SDF atlases from black-and-white inputs.
* [GDC Vault — Toon Rendering in Hi-Fi RUSH (GDC 2024, Tango Gameworks)](https://gdcvault.com/play/1034330/3D-Toon-Rendering-in-Hi)
  and [80.lv's summary](https://80.lv/articles/the-making-of-hi-fi-rush-s-3d-toon-rendering-style)
  — a shipped, deferred toon renderer that explicitly has a "toon face shadow implementation"; an
  indexed slide reads "The shadow shapes are determined by NdotL **'except for the face'**".
* [Gaolingx/GenshinCelShaderURP README](https://raw.githubusercontent.com/Gaolingx/GenshinCelShaderURP/main/README.md)
  — the Genshin-style texture set as the community understands it, including a "面部阴影SDF阈值图"
  (face shadow SDF threshold map) and a "面部阴影Mask" (face shadow mask) as **separate** assets from
  the base/ILM/shadow-ramp maps; also the ramp-sampling/shadow-ramp structure and the vertex-colour
  ramp-offset channel.

This repository:

* `Assets/Resources/NarsShader/Shaders/UmaMusume Face.shader` — the game face shader: mask-modulated
  smoothstep plus `fwidth` step anti-aliasing.
* `Assets/Resources/Materials/UmaShaderFace.shader` — the reimplementation: `base.r`-biased,
  `*50` hard step.
* `docs/UMA_SHADER.md` — the repo's own record of the cylinder-normal blend, the detail mask, the
  measured effect of clearing normals, and the provenance list.

Confirmed implementations and first-party descriptions of the face-shadow technique:

* [Transcription of the GDC 2024 talk "3D Toon Rendering in 'Hi-Fi RUSH'"](https://game.3loumao.org/382235095)
  — the definitive description: "Except for the face, all other parts … are shaded by thresholding
  the dot of normal and light"; the three failure modes of the normals route; the threshold map keyed
  to the face-forward↔light **angle** in the **face bone's** frame, "not the dot-product result";
  left/right flipping; the bake→hand-fix→distance-field-like-merge workflow.
* [Knosiz/URPSimpleGenshinShaders](https://github.com/Knosiz/URPSimpleGenshinShaders) — working code
  (`step(angleIndex, texel)` on the red channel), the five shared body-type face lightmaps, and the
  two FAQ traps (texture compression quality, head-bone root).
* [ashyukiha/GenshinCharacterShaderZhihuVer](https://github.com/ashyukiha/GenshinCharacterShaderZhihuVer)
  — the `acos`-based (linear-in-angle) variant with explicit `uv.x` / `-uv.x` mirrored sampling.
* [lilToon](https://github.com/lilxyzw/lilToon) — the RG-texture convention, `_ShadowFlatBlur`
  suppressing vertical light, mask-as-bias instead of `step`, and `aastrencth = 0`.
* [メタカル最前線: hand-painting the SDF face shadow (interview with アノマロカリス)](https://metacul-frontier.com/?p=20546)
  — the full GIMP workflow (paint one gradient, flip, pack R/G), and the warning about neck seams.
* [onigiri-tiken memo on lilToon SDF face shadows](https://scrapbox.io/onigiri-tiken/%E3%80%90Unity%E3%80%91%E3%82%B7%E3%82%A7%E3%83%BC%E3%83%80%E3%83%BC%EF%BC%9AlilToon%E3%81%A7SDF%E3%82%92%E4%BD%BF%E3%81%A3%E3%81%9F%E9%A1%94%E5%BD%B1)
  — the `sdf_tool` workflow and the crucial authoring rule that the boundary must be staggered across
  successive angle masks or the shadow pops.
* [alwei (Indie-us Games): UE5 SDF face shadow mapping](https://unrealengine.hatenablog.com/entry/2024/02/28/222220)
  — the Unreal implementation, the 9-angle set at 22.5° steps, and the claim that it is cheaper and
  more reusable than normal transfer.
* [puppaxlyu: SDF face shadow in Blender](https://puppaxlyu.blogspot.com/2025/11/sdf.html) — the
  Blender driver setup that makes the shadow follow a "light" empty.
* [EmuMan/npr-face-shader](https://github.com/EmuMan/npr-face-shader) — free/open-source Blender
  addon: grease-pencil drawing on the surface, a second front-projected UV map, a box blur, and
  auto-built material with Sun/Head drivers.
* [BOOTH: AnimeFaceShadow](https://booth.pm/ja/items/8223563) — bakes the field from a
  **Voxel-Remesh-smoothed proxy shell** at 32-bit float; and
  [BOOTH: CL SDF Tools](https://booth.pm/ja/items/8779114) — in-Blender per-angle painting that also
  writes the mirrored map.

Uma Musume first-party and decompiled sources:

* [game.watch: pre-announcement of the Cygames Tech Conference 2021 session](https://game.watch.impress.co.jp/docs/news/1364580.html)
  — confirms the talk's existence, title and date.
* [uisdc: Chinese translation of the character-modelling session](https://www.uisdc.com/pretty-derby-3d)
  — the model spec, the five control-mask channels, the **Detail Mask** for cheek/nose shading, and
  the "shadow looked distorted and unnatural before this" statement.
* [croakfang/UmaMusumeMME `UMA_Face.fx`](https://raw.githubusercontent.com/croakfang/UmaMusumeMME/master/UMA_Face.fx)
  — head-bone-bound face basis (`_faceShadowHeadMat`), the vertex-shader cylinder normal blend, and
  the green-channel cheek/nose detail-mask classes and thresholds.
* [Elysia-simp/Honse-Shader `face_shader.fxsub`](https://github.com/Elysia-simp/Honse-Shader) — a
  readable reimplementation of the same cheek/nose gating (without the normal flattening).
* [LooperHonstropy/BLENDER-Uma-Musume-Pretty-Derby-Shaders](https://github.com/LooperHonstropy/BLENDER-Uma-Musume-Pretty-Derby-Shaders)
  — a Blender port whose README admits the face lighting under sun rotation is unsolved.
* [gameres: He Jia / Unite Beijing 2018 transcript](https://www.gameres.com/807345.html) — the
  *different*, weaker Honkai 3rd face technique (a vertex-colour channel as a mask), which is why
  "HI3 uses an SDF face shadow" should be treated as folklore.

Aliasing vs geometry:

* [gltut, "Fragment Lighting"](https://web.archive.org/web/20150225192608/http://www.arcsynthesis.org/gltut/Illumination/Tut10%20Fragment%20Lighting.html)
  — the citable statement that the normal is linearly interpolated, that the gradient's *slope*
  changes abruptly at each triangle edge, and the Mach-band visibility mechanism.
* [SIGGRAPH, "Line Based Contouring"](http://education.siggraph.org/static/HyperVis/vised/VisTech/Techniques/s2dalinecontour.html)
  — the contour of a linear interpolant on a triangle is a straight line, hence a polyline overall.
* [Godot: 3D antialiasing](https://docs.godotengine.org/en/stable/tutorials/3d/3d_antialiasing.html)
  and [Unity URP: antialiasing](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@12.1/manual/anti-aliasing.html)
  — MSAA adds coverage samples but not colour samples, so it cannot antialias a shader-computed
  1-bit boundary; that is the discriminator.
* [blender.stackexchange 323205](https://blender.stackexchange.com/questions/323205/eevee-toon-shader-shadows-looking-jagged-and-choppy-everything-is-set-to-shade)
  — jagged toon shading traced to the inverted-hull outline mesh's shadow mode, not to the terminator.
* [MoonRay: shadow terminators](https://docs.openmoonray.org/user-reference/how-to-guides/shadow-terminators/)
  — vendor-side statement of the same root cause (`Ns` vs `Ng`) and its softening remedies.
* [Blender manual: EEVEE object data](https://projects.blender.org/blender/blender-manual/raw/branch/main/manual/render/eevee/object_settings/object_data.rst) /
  [Cycles object data](https://projects.blender.org/blender/blender-manual/raw/branch/main/manual/render/cycles/object_settings/object_data.rst)
  — the Shadow Terminator / Geometry Offset / Shading Offset controls, and their caveats.
* [ronja-tutorials SteppedToonLighting.shader](https://github.com/ronja-tutorials/ShaderTutorials/blob/master/Assets/031_StepToon/SteppedToonLighting.shader)
  and [ShaderStory: EdgeTransition](https://github.com/DeGGeD/ShaderStory/blob/main/Chapters/Derivatives/EdgeTransition.md)
  — the canonical `fwidth` step-AA code.

---

## 5. What I could not find, and what I am not claiming

* **I could not read the two Zhihu articles that the Blender SDF write-up cites as prerequisites**:
  `二次元角色卡通渲染—面部篇` (zhuanlan.zhihu.com/p/411188212) and
  `卡通渲染——360度脸部SDF光照方案` (zhuanlan.zhihu.com/p/670837192). Zhihu returns HTTP 403 to me. I
  cite them by reference only, not by content.
* **I could not read the GDC Vault page for the Hi-Fi Rush talk** (403). My evidence for its face
  shadow content is the indexed snippet *"The shadow shapes are determined by NdotL 'except for the
  face'"* plus 80.lv's summary that the talk covers "toon face shadow implementation".
* **Blender StackExchange is 403 to me (Cloudflare)**, so I could not read any BSE thread — including
  one promising hit, *"How Do I Get Crisp Shadows With Toon Shaders?"*. I am therefore **not**
  claiming what the Blender community's most-upvoted answer is; I can only say which mechanisms the
  official documentation and the vendor/community documentation endorse.
* **Bilibili articles are captcha-blocked to me**, including one whose title is exactly on point:
  *"Blender 在不使用第三方插件的情况下利用SDF贴图渲染出正确的二次元角色面部阴影"* (rendering correct anime face
  shadows in Blender with an SDF texture, without third-party plugins). I cite the title as
  existence evidence only.
* **polycount's "A short explanation about custom vertex normals" (403)** and the ArtStation product
  page for the Genshin Blender shader (403) — found, not read.
* **I did not verify the Cygames Tech Conference 2021 talk content first-hand from the original
  slides.** I confirmed the session's existence, title and date from the pre-announcement, and read a
  full Chinese translation of the character-modelling portion, which independently corroborates the
  Detail Mask / cheek-and-nose / "distorted and unnatural" account in this repo's `docs/UMA_SHADER.md`.
  **I could not locate the original Japanese slide deck or video**, and the translation promises a
  second instalment covering 「ウマ娘独特表現力」 which I could not find — so there may be further face
  detail I have not seen.
* **The primary GDC 2024 Hi-Fi RUSH slide PDF is not fetchable** (gdcvault.com returns 403 to
  automated fetches). I read the talk's content through a detailed slide-by-slide transcription.
* **He Jia's (贺甲, miHoYo) "Unity Seoul 2018" talk — not found.** The Hi-Fi RUSH slides reportedly cite
  it as the origin of the technique, but I could only locate the transcript of his Unite **Beijing**
  2018 talk, which does **not** contain the threshold-texture/SDF technique — its face statement is the
  weaker "one vertex-colour channel as a mask". So either the attribution points at a
  different/unpublished deck or the transcription's attribution is imprecise. I could not resolve this.
* **"Honkai Impact 3rd uses an SDF face shadow" is folklore as far as I can establish.** I found no
  first-party or technically detailed source. The only evidence is second-hand (an Unreal write-up
  saying the technique "seems to have started with 崩壊3rd and 原神", and a shader README distinguishing
  HI3 old vs new characters). **Genshin Impact has no first-party documentation of it either** —
  everything is reverse-engineered or datamined texture names.
* **No SDF face shadow, "Face Shadow" feature or "high-precision face" mode in UTS2/UTS.** I grepped
  the README for 顔/Face/SDF/影 and read the official Unity Toon Shader basic docs. UTS's
  artist-placed-shadow answer is the **Position Map** — a UV-space offset of the shadow border — not a
  light-angle-keyed threshold. This is a negative result, not an absence of searching.
* **The stored value is a light-angle threshold, not a Euclidean distance to the terminator.** Only
  the *merge* step is described as "distance-field-like". Sources that call the texel "the distance to
  the terminator" are using the term loosely.
* **No `fwidth` equivalent in Blender's shader nodes.** This is now established **by absence** rather
  than by inference: the Utilities node index, the Shader Nodes index and EEVEE's supported-nodes list
  contain no derivative node, and there are zero blender.stackexchange questions about it. The only
  route is OSL via the Script node (Cycles-only). I did not find a *positive* statement from Blender
  saying "there is no derivative node", so treat it as very well-supported absence, not a documented
  negative.
* **No authoritative, citable "aliasing vs geometry" diagnostic checklist.** The procedure in §3 is a
  synthesis; its *rationale* pieces are individually sourced (gltut, the SIGGRAPH contouring page, the
  MSAA/SSAA distinction, the outline-shell report), but no single source spells out this checklist.
  Likewise I found no source that documents the wireframe-overlay test, the count-pixels-per-step test
  or the resolution-scaling-law test as "the standard tests".
* **No source quantifies how many angle bakes are needed before the shadow pops** beyond the
  qualitative "stagger the boundary" rule.
* **Nothing explaining why a specific game's renderer is clean on the same mesh, before I read the
  game's own shader.** The Chinese write-ups on Uma Musume face rendering (zhuanlan.zhihu.com/p/660281558,
  /p/658955827) both returned 403 to me; the decompiles and the Cygames translation are what settled it.
* **No canonical "transfer normals from a sphere/capsule" technique.** I found the *concept*
  documented under other names (radial normals, proxy normals, sphere/cylinder normals, `Point to
  Target → Spherize`). If someone describes the fix to you as "the sphere-normal trick", that is
  community dialect for the proxy/simplified-shape family, not a named published technique.
* **Folklore to treat with care:** a very common piece of advice in toon-shading threads is "just
  soften the shadow edge / widen the feather". Widening the transition does hide the polygonal
  structure, but it does not change the contour: the gradient under a wide smoothstep is still C¹
  only piecewise, so the kinks at triangle edges remain as faint Mach-band creases, and the *shape*
  of the terminator is still the mesh's. I found no source that claims feathering fixes a geometric
  terminator, and I would not present it as a fix.
* I found **no single authoritative "toon shading on low-poly faces: the canonical fix" article**
  in the Blender ecosystem. The best material is spread across vendor documentation (UTS2, Fondant,
  ASP, PotaToon), community wikis (VRCLibrary), game-industry talks (Hi-Fi Rush, Cygames), and
  individual Blender write-ups (mos9527). The convergence across those independent sources is
  strong, which is the main reason I am confident in §2.
