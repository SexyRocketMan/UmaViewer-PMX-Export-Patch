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
