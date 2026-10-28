using System.Collections;
using System.Collections.Generic;
using System.Linq;
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

    /// <summary>
    /// The bake: the game's multiply cheek map turned into the alpha-blended rose an MMD material can draw
    /// (the numbers behind it, and why they are conversion choices rather than game data, are documented on
    /// <see cref="BlushTint"/> above).
    ///
    /// <paramref name="name"/> overrides what the bake is called. Pass the name the export already writes the
    /// source under: a texture object can reach a call site unnamed - <see cref="duplicateTexture"/> copies
    /// the source's name, and the source may have none to copy - and naming the bake after that produces
    /// `_blush.png`, a file the pmx then lists under a name that says nothing about what it is. That is the
    /// defect this parameter fixes. When neither name is usable the bake is skipped rather than written: an
    /// unnamed artifact is worse than no artifact, which is why the highlight bake is gone from the export.
    /// </summary>
    static public Texture2D BakeBlush(Texture2D source, string name = null)
    {
        if (source == null) return null;
        string sourceName = !string.IsNullOrWhiteSpace(name) ? name : source.name;
        if (string.IsNullOrWhiteSpace(sourceName)) return null;
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
        baked.name = BlushName(sourceName);
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

    /// <summary>
    /// A readable copy of a texture, written as PNG by the export.
    ///
    /// The copy keeps the source's name: the cheek bake names its output after the texture it was handed
    /// (<see cref="BlushName"/>), and `new Texture2D(...)` starts out unnamed. Without the transfer, the first
    /// export into a folder wrote the bake as `_blush.png` and listed `Texture2D/_blush.png` in the pmx, while
    /// a second export into the same folder - which skips the write because the file is already there -
    /// produced `tex_chr1001_00_cheek0_blush.png` instead. Same model, two different texture lists, one of
    /// them pointing at a file named after nothing.
    /// </summary>
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
        readableText.name = source.name;
        readableText.ReadPixels(new Rect(0, 0, renderTex.width, renderTex.height), 0, 0);
        readableText.Apply();
        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(renderTex);
        return readableText;
    }

    /// <summary>The game's variant maps are named by changing the dry map's name, not by a table.</summary>
    private const string WetSuffix = "_wet";

    private const string DirtSuffix = "_dirt";

    /// <summary>The albedo map is the only one the game ships a dirt version of.</summary>
    private const string AlbedoSuffix = "_diff";

    /// <summary>What variant files were written for one dry texture.</summary>
    public struct VariantFiles
    {
        /// <summary>The file the rain look uses: the dry name plus "_wet", or null when the game ships none.</summary>
        public string Wet;

        /// <summary>The game's dirt overlay for this map, or null. Only albedo maps have one.</summary>
        public string Dirt;
    }

    /// <summary>
    /// The variant files the last <see cref="ExportAllTexture"/> wrote, keyed by the dry texture's name.
    ///
    /// Per-export state on purpose: the files are written while walking the materials, and the comment that
    /// has to name them is built later in the same export (see <see cref="ModelExporter"/>), which cannot
    /// re-derive them without walking the game's asset list again.
    /// </summary>
    static public IReadOnlyDictionary<string, VariantFiles> WrittenVariants => writtenVariants;

    static private readonly Dictionary<string, VariantFiles> writtenVariants =
        new Dictionary<string, VariantFiles>(StringComparer.Ordinal);

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

        // the variants are a separate pass over the same names, and only when they were asked for
        writtenVariants.Clear();
        Dictionary<string, UmaDatabaseEntry> variantAssets = null;
        if (Config.Instance != null && Config.Instance.PmxTextureVariants) variantAssets = CharacterTextureAssets();

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
                            // the game's wet and dirt twins, written beside the dry map whenever the export
                            // was asked for them. Outside the File.Exists block on purpose: a second export
                            // into the same folder skips the write but still has to record the pairing, or
                            // the material comments would lose their tokens
                            if (variantAssets != null) WriteVariants(savePath, tex_name, variantAssets);
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
                            // A texture with no name bakes to "_mask.png"/"_blush.png" - artifacts beside every export
                            // that nothing references (measured: 3 unreferenced files in a 40-file folder, two of them
                            // these). Skip the bake instead of naming it after nothing.
                            bool named = !string.IsNullOrWhiteSpace(tex_name);
                            // **The highlight mask bake is gone** (2.34), after measuring what wanted it. It wrote
                            // `eyehi00_mask.png` and its siblings for a consumer that thresholds *outside* a shader,
                            // so a plain MMD material could draw the game's colour-masked highlights without painting
                            // black over the eye. On an export of 1001: no material names any `_mask` file in any
                            // slot (`tex` = 10 = `eye0`, `sphere` -1, `toon` -1), and the add-on does not want them
                            // either - it binds the raw `eyehi00`/`01`/`02` and thresholds in `Uma Eyes` with its two
                            // `SmoothThreshold` groups. They were in the pmx's texture table only because this block
                            // registered them as dependencies. A dead end: kept in history, not in every user's folder.
                            if (named && IsMultiply(mat))
                            {
                                // named after `tex_name`, not after the texture object: the bake used to name
                                // itself from its source, which is empty often enough to have shipped `_blush.png`
                                Texture2D blush = BakeBlush(mat_texture, tex_name);
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
                if (variantAssets != null) WriteVariants(savePath, tex_name, variantAssets);
                if (!File.Exists(tex_path))
                {
                    var copy = duplicateTexture(texture);
                    File.WriteAllBytes(tex_path, copy.EncodeToPNG());
                }
                // the cheek pair is a multiply map either way, so both get the MMD bake
                if (tex_name.IndexOf("cheek", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    Texture2D blush = BakeBlush(texture, tex_name);
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
        if (variantAssets != null)
        {
            int wet = 0, dirt = 0;
            foreach (var pair in writtenVariants)
            {
                if (!string.IsNullOrEmpty(pair.Value.Wet)) wet++;
                if (!string.IsNullOrEmpty(pair.Value.Dirt)) dirt++;
            }
            Debug.Log($"[Variants] {wet} wet and {dirt} dirt map(s) written for {writtenVariants.Count} dry "
                      + $"texture(s); named per material by 'wet='/'dirt=' tokens");
        }
        return textureNames.ToArray();
    }

    /// <summary>
    /// Every character texture asset, by name. The dry maps are not in here: they live inside the part
    /// prefabs, and only the variants the game ships beside them are assets of their own - which is why this
    /// list is exactly what a variant lookup needs and a dry-map lookup would not.
    /// </summary>
    static private Dictionary<string, UmaDatabaseEntry> CharacterTextureAssets()
    {
        var assets = new Dictionary<string, UmaDatabaseEntry>(StringComparer.Ordinal);
        var main = UmaViewerMain.Instance;
        if (main == null || main.AbChara == null) return assets;

        foreach (var entry in main.AbChara)
        {
            if (entry == null || !entry.Name.Contains("/textures/")) continue;
            assets[entry.Name.Substring(entry.Name.LastIndexOf('/') + 1)] = entry;
        }
        return assets;
    }

    /// <summary>
    /// Writes the twins a dry map has, if the game ships any, and records them for the material comment.
    /// The names come from the game's own convention - the rain set appends "_wet", the dirt map replaces
    /// the albedo token ("..._diff" -> "..._dirt") - so nothing here needs a table of its own.
    /// </summary>
    static private void WriteVariants(string savePath, string texName, Dictionary<string, UmaDatabaseEntry> assets)
    {
        if (string.IsNullOrEmpty(texName)) return;

        var files = new VariantFiles();
        files.Wet = WriteVariantFile(savePath, texName + WetSuffix, assets);
        if (texName.EndsWith(AlbedoSuffix, StringComparison.Ordinal))
        {
            files.Dirt = WriteVariantFile(savePath, texName.Substring(0, texName.Length - AlbedoSuffix.Length) + DirtSuffix, assets);
        }

        if (string.IsNullOrEmpty(files.Wet) && string.IsNullOrEmpty(files.Dirt)) return;
        writtenVariants[texName] = files;
    }

    /// <summary>
    /// One variant file, under the game's own name, and only when that asset exists. Returns the name it
    /// was written as, or null - which is how a material with no twin is told apart from one with a twin.
    /// </summary>
    static private string WriteVariantFile(string savePath, string name, Dictionary<string, UmaDatabaseEntry> assets)
    {
        if (string.IsNullOrEmpty(name) || !assets.TryGetValue(name, out UmaDatabaseEntry entry)) return null;

        string path = savePath + name + ".png";
        if (File.Exists(path)) return name;

        var texture = entry.GetAll<Texture2D>().FirstOrDefault();
        if (texture == null)
        {
            Debug.LogWarning($"[Variants] {name} is listed but carries no readable texture; skipped");
            return null;
        }

        File.WriteAllBytes(path, duplicateTexture(texture).EncodeToPNG());
        return name;
    }
}
