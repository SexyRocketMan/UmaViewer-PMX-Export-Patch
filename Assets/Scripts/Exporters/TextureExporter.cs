using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using Newtonsoft.Json.Linq;
using System;

public class TextureExporter
{
    /// <summary>
    /// A multiply overlay turned into one an MMD material can draw: the same blush, as a colour and an alpha.
    ///
    /// The game's cheek map is white where the face shows through and only drops the green and blue channels
    /// where the blush is - it is *multiplied* into the face. An MMD material can only alpha blend, so drawn
    /// as it is the decal paints a white shell over the cheeks.
    ///
    /// The map's channel drop is the blush's *shape*, and it is a very small number: the strongest texel of the
    /// reference cheek map drops under 6%, which as an alpha is invisible. Solving `s(1-a) + c*a = s*t` per texel
    /// reproduces the game exactly but gives every texel that has any drop at all its own fully saturated colour
    /// (`c = (t - 1 + a) / a` divides by that small alpha), and the result is a garish red where the blush is
    /// faint. So the solve is used only for the colour's *hue*, and the texture is rebuilt as one soft rose at a
    /// strength a viewer actually sees: the drop normalised to the map's own maximum, raised to a curve so the
    /// middle of the blush does not collapse, and an alpha ceiling the blush never exceeds.
    ///
    /// All three numbers are conversion choices, not game data, so they are named here.
    /// </summary>
    //: the blush colour the overlay fades to - a muted rose, dark enough to read as blush over skin
    static readonly Color BlushTint = new Color(0.86f, 0.44f, 0.46f, 1f);
    //: the strongest alpha the blush reaches, at the map's own strongest texel
    const float BlushMaxAlpha = 0.55f;
    //: how the drop maps onto alpha; below 1 the faint outer edge of the blush reaches further
    const float BlushCurve = 0.7f;

    static public Texture2D BakeBlush(Texture2D source)
    {
        if (source == null) return null;
        Texture2D readable = duplicateTexture(source);
        Color[] pixels = readable.GetPixels();

        float strongest = 0f;
        for (int i = 0; i < pixels.Length; i++)
        {
            strongest = Mathf.Max(strongest, 1f - Mathf.Min(pixels[i].g, pixels[i].b));
        }
        // a map with no drop at all is not a blush; leave it alone rather than write a fully transparent copy
        if (strongest <= 0.004f) return null;

        // the map is small and its gradient is quantised: the raw channel drop comes in a few dozen steps, which an
        // alpha of 0.55 turns into a few dozen visible rings. Ring the field with a light 3x3 average first - the
        // blush has no detail worth keeping at that scale, only its shape.
        int width = readable.width, height = readable.height;
        float[] drop = new float[pixels.Length];
        for (int i = 0; i < pixels.Length; i++) drop[i] = 1f - Mathf.Min(pixels[i].g, pixels[i].b);
        float[] smoothed = new float[pixels.Length];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float sum = 0f;
                int count = 0;
                for (int dy = -1; dy <= 1; dy++)
                {
                    int yy = y + dy;
                    if (yy < 0 || yy >= height) continue;
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int xx = x + dx;
                        if (xx < 0 || xx >= width) continue;
                        sum += drop[yy * width + xx];
                        count++;
                    }
                }
                smoothed[y * width + x] = sum / count;
            }
        }
        for (int i = 0; i < pixels.Length; i++)
        {
            Color p = pixels[i];
            float strength = smoothed[i] / strongest;
            float alpha = Mathf.Pow(Mathf.Clamp01(strength), BlushCurve) * BlushMaxAlpha;
            // an 8 bit alpha has 256 steps, and a blush that spans a few centimetres of face across 0.55 of them
            // shows every step as a ring. A fixed per texel offset of a fraction of a step, hashed so it does not
            // line up with the texture's own grid, turns the rings into grain.
            uint hash = (uint)(i * 2654435761u);
            float dither = ((hash >> 8) % 256u) / 255f - 0.5f;
            alpha = Mathf.Clamp01(alpha + dither / 255f * 1.5f);
            pixels[i] = new Color(BlushTint.r, BlushTint.g, BlushTint.b, alpha);
        }
        Texture2D baked = new Texture2D(readable.width, readable.height, TextureFormat.RGBA32, false);
        baked.name = BlushName(source.name);
        baked.SetPixels(pixels);
        baked.Apply();
        return baked;
    }

    /// <summary>
    /// A highlight map turned into one an MMD material can draw: white where the highlight is, transparent
    /// everywhere else.
    ///
    /// The eye shader does not carry the highlight in the map's alpha - it carries it in the *colour* and masks
    /// it with `step(_Show, texture)`, so drawn as it is the layer paints black over the eye wherever there is no
    /// highlight. The mask is that step, softened a little at the edge so the shape is not aliased: alpha from
    /// the luminance, thresholded, and the colour forced to white because a highlight is light.
    /// </summary>
    static public Texture2D BakeHighlight(Texture2D source)
    {
        if (source == null) return null;
        Texture2D readable = duplicateTexture(source);
        Color[] pixels = readable.GetPixels();
        for (int i = 0; i < pixels.Length; i++)
        {
            Color p = pixels[i];
            float lum = Mathf.Max(p.r, Mathf.Max(p.g, p.b));
            float alpha = Mathf.Clamp01((lum - 0.18f) / (0.45f - 0.18f));
            pixels[i] = new Color(1f, 1f, 1f, alpha);
        }
        Texture2D baked = new Texture2D(readable.width, readable.height, TextureFormat.RGBA32, false);
        baked.name = HighlightName(source.name);
        baked.SetPixels(pixels);
        baked.Apply();
        return baked;
    }

    /// <summary>The name the baked highlight is written under.</summary>
    static public string HighlightName(string sourceName)
    {
        return sourceName + "_mask";
    }

    /// <summary>The name the baked copy is written under; the exporter looks it up by this.</summary>
    static public string BlushName(string sourceName)
    {
        return sourceName + "_blush";
    }

    /// <summary>Whether a material is one the game draws by multiplying - the blush decal is.</summary>
    static public bool IsMultiply(Material material)
    {
        return material != null && material.shader != null
               && (material.shader.name ?? string.Empty).IndexOf("Multiply", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>The name the baked face-blush layer is written under.</summary>
    static public string FaceBlushName(string sourceName)
    {
        return sourceName + "_faceblush";
    }

    /// <summary>
    /// The cheek blush baked into the **face atlas's own UV space**, so a face material can multiply it in with a Mix
    /// Color node instead of drawing a decal quad over the face.
    ///
    /// Why a bake and not a UV transform: the blush map lives in its own UV square and the face in the face atlas, and
    /// nothing affine relates them - measured in the exporter repository (`Tools/fit_blush_uv.py`), the best
    /// face-UV -> blush-UV affine fit leaves a residual of ~42 UV units on a 0..1 map, i.e. it explains nothing. What
    /// *is* exact is geometry: the blush quad sits a hair in front of the cheek, so projecting a point of the face
    /// surface onto the quad says which blush texel belongs there. That is what this does, at texel resolution - the
    /// same projection `Tools/bake_blush_vertices.py` validated, which was too coarse at vertex resolution (30 of the
    /// face's 1328 vertices fell inside the blush) and is fine here.
    ///
    /// **The output is a multiply layer**: white wherever there is no blush, and `lerp(white, blushColour, blushAlpha)`
    /// where there is, so `face * layer` leaves the face alone outside the blush and darkens/tints it inside. White is
    /// also why a material that never gets the file still looks right.
    ///
    /// All the geometry is passed in rather than looked up, so this stays a pure function of its arguments: the caller
    /// knows which renderer is the face and which are the cheek decals, and this does not. Triangles are index triples
    /// into the matching arrays, and every array is in the same space (the character's) - which is where `DeployMorphs`
    /// and the clearance lift already work, so the caller just has to pass the deployed positions.
    /// </summary>
    static public Texture2D BakeFaceBlush(Texture2D faceAtlas,
                                          Vector3[] facePoints, Vector2[] faceUvs, int[] faceTriangles,
                                          Vector3[] blushPoints, Vector2[] blushUvs, int[] blushTriangles,
                                          Texture2D blushMap)
    {
        if (faceAtlas == null || blushMap == null || facePoints == null || blushPoints == null) return null;
        if (faceTriangles == null || blushTriangles == null) return null;
        if (faceTriangles.Length < 3 || blushTriangles.Length < 3) return null;

        Texture2D readableBlush = duplicateTexture(blushMap);
        if (readableBlush == null) return null;
        Color[] blushPixels = readableBlush.GetPixels();

        int width = faceAtlas.width;
        int height = faceAtlas.height;
        Texture2D baked = new Texture2D(width, height, TextureFormat.RGBA32, false);
        baked.name = FaceBlushName(faceAtlas.name);
        Color[] layer = new Color[width * height];
        for (int i = 0; i < layer.Length; i++) layer[i] = new Color(1f, 1f, 1f, 1f);   // multiply by 1 = leave alone

        // every face triangle, rasterised in *UV* space: a texel inside the triangle's UV triangle belongs to the
        // surface point the barycentric says, and that point is projected onto the blush to find its texel there
        for (int t = 0; t + 2 < faceTriangles.Length; t += 3)
        {
            Vector2 uvA = faceUvs[faceTriangles[t]];
            Vector2 uvB = faceUvs[faceTriangles[t + 1]];
            Vector2 uvC = faceUvs[faceTriangles[t + 2]];
            Vector2 min = Vector2.Min(uvA, Vector2.Min(uvB, uvC));
            Vector2 max = Vector2.Max(uvA, Vector2.Max(uvB, uvC));
            int x0 = Mathf.Clamp(Mathf.FloorToInt(min.x * width), 0, width - 1);
            int x1 = Mathf.Clamp(Mathf.CeilToInt(max.x * width), 0, width - 1);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(min.y * height), 0, height - 1);
            int y1 = Mathf.Clamp(Mathf.CeilToInt(max.y * height), 0, height - 1);
            float denominator = (uvB.y - uvC.y) * (uvA.x - uvC.x) + (uvC.x - uvB.x) * (uvA.y - uvC.y);
            if (Mathf.Abs(denominator) < 1e-8f) continue;

            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    Vector2 uv = new Vector2((x + 0.5f) / width, (y + 0.5f) / height);
                    float wa = ((uvB.y - uvC.y) * (uv.x - uvC.x) + (uvC.x - uvB.x) * (uv.y - uvC.y)) / denominator;
                    float wb = ((uvC.y - uvA.y) * (uv.x - uvC.x) + (uvA.x - uvC.x) * (uv.y - uvC.y)) / denominator;
                    float wc = 1f - wa - wb;
                    if (wa < -0.001f || wb < -0.001f || wc < -0.001f) continue;

                    Vector3 point = facePoints[faceTriangles[t]] * wa
                                    + facePoints[faceTriangles[t + 1]] * wb
                                    + facePoints[faceTriangles[t + 2]] * wc;
                    Vector2 blushUv;
                    if (ProjectOntoBlush(point, blushPoints, blushUvs, blushTriangles, out blushUv) == false) continue;

                    Color sample = SampleBilinear(blushPixels, readableBlush.width, readableBlush.height,
                                                  blushUv.x, blushUv.y);
                    layer[y * width + x] = new Color(Mathf.Lerp(1f, sample.r, sample.a),
                                                     Mathf.Lerp(1f, sample.g, sample.a),
                                                     Mathf.Lerp(1f, sample.b, sample.a),
                                                     1f);
                }
            }
        }

        baked.SetPixels(layer);
        baked.Apply();
        return baked;
    }

    /// <summary>The blush UV of the closest point on the blush triangles that `point` projects into, or false.</summary>
    static bool ProjectOntoBlush(Vector3 point, Vector3[] points, Vector2[] uvs, int[] triangles, out Vector2 uv)
    {
        uv = Vector2.zero;
        bool found = false;
        float bestDistance = float.MaxValue;
        for (int t = 0; t + 2 < triangles.Length; t += 3)
        {
            Vector3 a = points[triangles[t]];
            Vector3 b = points[triangles[t + 1]];
            Vector3 c = points[triangles[t + 2]];
            Vector3 normal = Vector3.Cross(b - a, c - a);
            float length = normal.magnitude;
            if (length < 1e-9f) continue;                       // a collapsed decal triangle (the rest state)
            normal /= length;
            float distance = Mathf.Abs(Vector3.Dot(point - a, normal));
            Vector3 projected = point - normal * Vector3.Dot(point - a, normal);

            Vector3 v0 = b - a, v1 = c - a, v2 = projected - a;
            float d00 = Vector3.Dot(v0, v0), d01 = Vector3.Dot(v0, v1), d11 = Vector3.Dot(v1, v1);
            float d20 = Vector3.Dot(v2, v0), d21 = Vector3.Dot(v2, v1);
            float denominator = d00 * d11 - d01 * d01;
            if (Mathf.Abs(denominator) < 1e-12f) continue;
            float v = (d11 * d20 - d01 * d21) / denominator;
            float w = (d00 * d21 - d01 * d20) / denominator;
            float u = 1f - v - w;
            if (u < -0.001f || v < -0.001f || w < -0.001f) continue;
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            uv = uvs[triangles[t]] * u + uvs[triangles[t + 1]] * v + uvs[triangles[t + 2]] * w;
            found = true;
        }
        return found;
    }

    /// <summary>Bilinear sample of a pixel array at `u, v` in 0..1, clamped to the edges.</summary>
    static Color SampleBilinear(Color[] pixels, int width, int height, float u, float v)
    {
        float x = Mathf.Clamp(u, 0f, 0.9999f) * width;
        float y = Mathf.Clamp(v, 0f, 0.9999f) * height;
        int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
        float fx = x - x0, fy = y - y0;
        int ym = Mathf.Clamp(y0, 0, height - 1) * width;
        int yn = Mathf.Clamp(y0 + 1, 0, height - 1) * width;
        Color a = pixels[ym + Mathf.Clamp(x0, 0, width - 1)];
        Color b = pixels[ym + Mathf.Clamp(x0 + 1, 0, width - 1)];
        Color c = pixels[yn + Mathf.Clamp(x0, 0, width - 1)];
        Color d = pixels[yn + Mathf.Clamp(x0 + 1, 0, width - 1)];
        return Color.Lerp(Color.Lerp(a, b, fx), Color.Lerp(c, d, fx), fy);
    }

    /// <summary>
    /// The glue `BakeFaceBlush` needs: find the face renderer and the cheek renderer(s) among the character's, pull
    /// their mesh data into one space, and bake.
    ///
    /// The face is the renderer with a material that says "face" and is not a multiply decal (the cheek material also
    /// mentions the face in the game's naming, which is why `IsMultiply` excludes it); the cheeks are every submesh
    /// whose material is a multiply one - `MorphPartRenderers` already hands those over, so `extra` is where they come
    /// from in practice. Positions are taken in world space through each renderer's own transform, which is the one
    /// space both sides share while the rig is at rest.
    ///
    /// Returns null - rather than throwing - for a character with no cheek decal, no face mesh, or a mesh without
    /// UVs, because a model that cannot be baked is not an export failure: the cheek keeps shipping as its own part.
    /// </summary>
    static Texture2D BakeFaceBlushLayer(GameObject gameobj, IEnumerable<Renderer> extra, string savePath)
    {
        if (gameobj == null) return null;
        List<Renderer> renderers = new List<Renderer>(gameobj.transform.GetComponentsInChildren<Renderer>());
        if (extra != null) renderers.AddRange(extra);

        Renderer faceRenderer = null;
        Mesh faceMesh = null;
        Material faceMaterial = null;
        int faceSubmesh = 0;
        foreach (Renderer renderer in renderers)
        {
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) continue;
            Material[] materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] == null || IsMultiply(materials[i])) continue;
                if ((materials[i].name ?? string.Empty).IndexOf("face", StringComparison.OrdinalIgnoreCase) < 0) continue;
                faceRenderer = renderer;
                faceMesh = filter.sharedMesh;
                faceMaterial = materials[i];
                faceSubmesh = i;              // the face is one submesh of its renderer, not necessarily submesh 0
                break;
            }
            if (faceRenderer != null) break;
        }
        if (faceRenderer == null || faceMesh == null || faceMaterial == null)
        {
            // said out loud: a silent null looks exactly like a bake that worked until the folder has no file in it,
            // which is how this cost a round trip
            Debug.Log("[exporter] face-blush layer skipped: no face material among " + renderers.Count
                      + " renderer(s) - looking for a material whose name contains 'face' and is not a multiply decal");
            return null;
        }
        if (faceMesh.uv == null || faceMesh.uv.Length != faceMesh.vertexCount || faceMesh.subMeshCount < 1) return null;
        Texture2D faceAtlas = faceMaterial.mainTexture as Texture2D;
        if (faceAtlas == null)
        {
            Debug.Log("[exporter] face-blush layer skipped: " + faceMaterial.name
                      + " has no main texture to size the layer against");
            return null;
        }
        int[] faceTriangles = faceMesh.GetTriangles(faceSubmesh);
        if (faceTriangles.Length < 3) return null;

        List<Vector3> blushPoints = new List<Vector3>();
        List<Vector2> blushUvs = new List<Vector2>();
        List<int> blushTriangles = new List<int>();
        Texture2D blushMap = null;
        foreach (Renderer renderer in renderers)
        {
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) continue;
            Mesh mesh = filter.sharedMesh;
            if (mesh.uv == null || mesh.uv.Length != mesh.vertexCount) continue;
            Material[] materials = renderer.sharedMaterials;
            for (int submesh = 0; submesh < mesh.subMeshCount && submesh < materials.Length; submesh++)
            {
                if (!IsMultiply(materials[submesh])) continue;
                int offset = blushPoints.Count;
                Vector3[] vertices = mesh.vertices;
                for (int i = 0; i < vertices.Length; i++) blushPoints.Add(renderer.transform.TransformPoint(vertices[i]));
                blushUvs.AddRange(mesh.uv);
                foreach (int index in mesh.GetTriangles(submesh)) blushTriangles.Add(offset + index);
                if (blushMap == null) blushMap = materials[submesh].mainTexture as Texture2D;
            }
        }
        if (blushMap == null || blushTriangles.Count < 3) return null;

        Vector3[] facePoints = new Vector3[faceMesh.vertexCount];
        Vector3[] source = faceMesh.vertices;
        for (int i = 0; i < facePoints.Length; i++) facePoints[i] = faceRenderer.transform.TransformPoint(source[i]);

        return BakeFaceBlush(faceAtlas, facePoints, faceMesh.uv, faceTriangles,
                             blushPoints.ToArray(), blushUvs.ToArray(), blushTriangles.ToArray(), blushMap);
    }

    static public Texture2D duplicateTexture(Texture2D source)
    {
        RenderTexture renderTex = RenderTexture.GetTemporary(
                    source.width,
                    source.height,
                    0,
                    RenderTextureFormat.Default,
                    RenderTextureReadWrite.Linear);

        Graphics.Blit(source, renderTex);
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = renderTex;
        Texture2D readableText = new Texture2D(source.width, source.height);
        readableText.ReadPixels(new Rect(0, 0, renderTex.width, renderTex.height), 0, 0);
        readableText.Apply();
        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(renderTex);
        return readableText;
    }

    /// <summary>
    /// Every texture the model's materials use, written to `Texture2D/` next to the export.
    ///
    /// `extra` is for renderers that are not part of the usual walk - the facial parts a morph drives are
    /// switched off in the viewer until a driven key raises their rate, and an inactive renderer is not in
    /// <see cref="GameObject.GetComponentsInChildren{T}()"/>, so the cheek texture would never be written even
    /// though the material that uses it is exported.
    /// </summary>
    static public string[] ExportAllTexture(string path, GameObject gameobj, IEnumerable<Renderer> extra = null,
                                            IEnumerable<Texture2D> extraTextures = null)
    {
        string savePath = path + "/Texture2D/";
        List<string> textureNames = new List<string>();
        if (!Directory.Exists(savePath))
        {
            Directory.CreateDirectory(savePath);
        }

        void AddRenderer(Renderer renderer)
        {
            if (renderer == null) return;
            foreach (Material mat in renderer.sharedMaterials)
            {
                if (mat == null) continue;
                //»ñµÃ²ÄÖÊµÄËùÓÐÊôÐÔÃû£¬Èç¹û¸ÄÊôÐÔÃû¶ÔÓ¦µÄÊÇÌùÍ¼£¬ÌáÈ¡¸ÃÌùÍ¼
                Shader mat_shader = mat.shader;
                int p_num = mat_shader.GetPropertyCount();
                for (int i = 0; i < p_num; i++)
                {
                    string p_name = mat_shader.GetPropertyName(i);
                    if (mat_shader.GetPropertyType(i) == UnityEngine.Rendering.ShaderPropertyType.Texture)
                    {
                        Texture2D mat_texture = (Texture2D)mat.GetTexture(p_name);

                        if (mat_texture)
                        {
                            string tex_name = mat_texture.name;
                            string tex_path = savePath + tex_name + ".png";
                            var dpath = "Texture2D/" + tex_name + ".png";
                            if (!textureNames.Contains(dpath))
                            {
                                textureNames.Add(dpath);
                            }
                            if (!File.Exists(tex_path))
                            {

                                mat_texture = duplicateTexture(mat_texture);

                                if (tex_name.EndsWith("eye0"))
                                {
                                    var clip_texture = duplicateTexture(mat_texture);
                                    var pixels = clip_texture.GetPixels(0, 0, clip_texture.width / 4, clip_texture.height);
                                    clip_texture.Reinitialize(clip_texture.width / 4, clip_texture.height);
                                    clip_texture.SetPixels(pixels);


                                    byte[] clip = clip_texture.EncodeToPNG();
                                    File.WriteAllBytes(tex_path, clip);
                                    tex_path = savePath + tex_name + "_all" + ".png";
                                }

                                byte[] bytes = mat_texture.EncodeToPNG();
                                File.WriteAllBytes(tex_path, bytes);
                            }
                            // a multiply decal also gets the bake that lets an MMD material draw it (see
                            // BakeBlush); written beside the original, which stays for anyone who wants the
                            // game's own map
                            // the eye highlight maps are colour-masked in the game's shader, not alpha-masked:
                            // they need the baked copy that carries the mask as alpha, or the layer paints black
                            if (tex_name.IndexOf("eyehi", System.StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                Texture2D highlight = BakeHighlight(mat_texture);
                                if (highlight != null)
                                {
                                    string highlightPath = savePath + highlight.name + ".png";
                                    string highlightDep = "Texture2D/" + highlight.name + ".png";
                                    if (!textureNames.Contains(highlightDep)) textureNames.Add(highlightDep);
                                    File.WriteAllBytes(highlightPath, highlight.EncodeToPNG());
                                }
                            }
                            if (IsMultiply(mat))
                            {
                                Texture2D blush = BakeBlush(mat_texture);
                                if (blush != null)
                                {
                                    string blushPath = savePath + blush.name + ".png";
                                    string blushDep = "Texture2D/" + blush.name + ".png";
                                    if (!textureNames.Contains(blushDep)) textureNames.Add(blushDep);
                                    // always rewritten: it is derived, and a batch of blushes written by an older bake would
                                    // otherwise stay on disk and be what the export ships
                                    File.WriteAllBytes(blushPath, blush.EncodeToPNG());
                                }
                            }
                        }
                    }
                }
            }
        }

        // the cheek blush baked into the face atlas's own UV space, so a face material can multiply it in with a Mix
        // Color node instead of a decal quad over the face (`BakeFaceBlush` has the reasoning). Written last, because
        // it needs the face's exported texture and the cheek renderers, and both are known by here.
        try
        {
            Texture2D faceBlush = BakeFaceBlushLayer(gameobj, extra, savePath);
            if (faceBlush != null)
            {
                string faceBlushDep = "Texture2D/" + faceBlush.name + ".png";
                if (!textureNames.Contains(faceBlushDep)) textureNames.Add(faceBlushDep);
                File.WriteAllBytes(savePath + faceBlush.name + ".png", faceBlush.EncodeToPNG());
            }
        }
        catch (Exception error)
        {
            Debug.LogWarning("[exporter] the face-blush layer was not baked (" + error.GetType().Name + ": "
                             + error.Message + "); the cheek decal still ships as its own part");
        }

        foreach (Renderer renderer in gameobj.transform.GetComponentsInChildren<Renderer>())
        {
            AddRenderer(renderer);
        }
        if (extra != null)
        {
            foreach (Renderer renderer in extra)
            {
                AddRenderer(renderer);
            }
        }
        // textures a morph binds but no material uses: the second cheek map is one, and without it in the folder
        // the addon cannot swap to it when the locator raises that axis
        if (extraTextures != null)
        {
            foreach (Texture2D texture in extraTextures)
            {
                if (texture == null) continue;
                string tex_name = texture.name;
                string tex_path = savePath + tex_name + ".png";
                var dpath = "Texture2D/" + tex_name + ".png";
                if (!textureNames.Contains(dpath)) textureNames.Add(dpath);
                if (!File.Exists(tex_path))
                {
                    var copy = duplicateTexture(texture);
                    File.WriteAllBytes(tex_path, copy.EncodeToPNG());
                }
                // the cheek pair is a multiply map either way, so both get the MMD bake
                if (tex_name.IndexOf("cheek", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    Texture2D blush = BakeBlush(texture);
                    if (blush != null)
                    {
                        string blushPath = savePath + blush.name + ".png";
                        string blushDep = "Texture2D/" + blush.name + ".png";
                        if (!textureNames.Contains(blushDep)) textureNames.Add(blushDep);
                        // always rewritten: it is derived (see above)
                        File.WriteAllBytes(blushPath, blush.EncodeToPNG());
                    }
                }
            }
        }
        Debug.Log($"Exported textures: {textureNames.ToArray()}");
        return textureNames.ToArray();
    }
}
