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
    /// as it is the decal paints a white shell over the cheeks. Alpha blending `s(1-a) + c*a` reproduces the
    /// multiply `s*t` when `a = 1 - min(g, b)` and `c = (t - 1 + a) / a`: the map's channel drop becomes the
    /// alpha, and the colour comes out as the saturated pink that drop implies, fully transparent wherever the
    /// map is white. The whole blush is subtle by nature - the reference cheek map drops fewer than 6% on its
    /// strongest texel - so the result reads as a soft tint, which is what the multiply does.
    /// </summary>
    static public Texture2D BakeBlush(Texture2D source)
    {
        if (source == null) return null;
        Texture2D readable = duplicateTexture(source);
        Color[] pixels = readable.GetPixels();
        for (int i = 0; i < pixels.Length; i++)
        {
            Color p = pixels[i];
            float drop = 1f - Mathf.Min(p.g, p.b);
            if (drop <= 0.002f)
            {
                pixels[i] = new Color(p.r, p.g, p.b, 0f);
                continue;
            }
            float r = (p.r - 1f + drop) / drop;
            float g = (p.g - 1f + drop) / drop;
            float b = (p.b - 1f + drop) / drop;
            pixels[i] = new Color(Mathf.Clamp01(r), Mathf.Clamp01(g), Mathf.Clamp01(b), Mathf.Clamp01(drop));
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
                                    if (!File.Exists(blushPath)) File.WriteAllBytes(blushPath, blush.EncodeToPNG());
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
                        if (!File.Exists(blushPath)) File.WriteAllBytes(blushPath, blush.EncodeToPNG());
                    }
                }
            }
        }
        Debug.Log($"Exported textures: {textureNames.ToArray()}");
        return textureNames.ToArray();
    }
}
