using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using System.IO;
using Newtonsoft.Json.Linq;
using System;

public class TextureExporter
{
    /// <summary>Whether a material is one the game draws by multiplying - the blush decal is.</summary>
    static public bool IsMultiply(Material material)
    {
        return material != null && material.shader != null
               && (material.shader.name ?? string.Empty).IndexOf("Multiply", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>
    /// A readable copy of a texture, written as PNG by the export. It keeps the source's name on purpose: every caller writes
    /// the copy under the name it was handed, so a nameless copy lands in the pmx under another name.
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
    /// The variant files the last <see cref="ExportAllTexture"/> wrote, keyed by dry texture name. Per-export state on purpose:
    /// the material comment naming them is built later in the same export (<see cref="ModelExporter"/>).
    /// </summary>
    static public IReadOnlyDictionary<string, VariantFiles> WrittenVariants => writtenVariants;

    static private readonly Dictionary<string, VariantFiles> writtenVariants =
        new Dictionary<string, VariantFiles>(StringComparer.Ordinal);

    /// <summary>
    /// Every texture the model's materials use, written to `Texture2D/` next to the export. `extra` covers what the usual walk
    /// misses: facial parts a morph drives stay inactive until a key raises their rate, and an inactive renderer is not in
    /// <see cref="GameObject.GetComponentsInChildren{T}()"/>.
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
                            // the game's wet and dirt twins, written beside the dry map. Outside the File.Exists block on purpose: a repeat export
                            // skips the write but still has to record the pairing, or the material comments would lose their tokens
                            if (variantAssets != null) WriteVariants(savePath, tex_name, variantAssets);
                            if (!File.Exists(tex_path))
                            {

                                mat_texture = duplicateTexture(mat_texture);

                                // eye0 is a four-plate atlas whose mesh UVs are plate-local, so crop to the plate _Offset selects (shipped (0,0,0,0) =
                                // the left one). The whole sheet also ships as `<name>_all.png`: to reach another plate, scale image x to 0.25 and offset
                                // by plate * 0.25. A pmx cannot name it (one texture slot per material), so it is an extra rather than the bound map.
                                if (tex_name.EndsWith("eye0"))
                                {
                                    var clip_texture = duplicateTexture(mat_texture);
                                    var pixels = clip_texture.GetPixels(0, 0, clip_texture.width / 4, clip_texture.height);
                                    clip_texture.Reinitialize(clip_texture.width / 4, clip_texture.height);
                                    clip_texture.SetPixels(pixels);
                                    File.WriteAllBytes(tex_path, clip_texture.EncodeToPNG());
                                    tex_path = savePath + tex_name + "_all" + ".png";
                                }

                                byte[] bytes = mat_texture.EncodeToPNG();
                                File.WriteAllBytes(tex_path, bytes);
                            }
                            // a multiply decal (the cheek) needs no baked copy: the game's own map is written as it is, and the addon derives the
                            // alpha at shade time with a multiply node and luminance ramp - blend=multiply says so. No `_mask` bake: no material names one.
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
        // textures a morph binds but no material uses: the second cheek map, without which the addon cannot swap to it
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
                // the cheek pair is a multiply map either way, both written as the game ships them - no alpha-only bake of either
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
    /// Every character texture asset, by name. The dry maps are not here - they live inside the part prefabs, and only the
    /// variants shipped beside them are assets of their own, which is what a variant lookup needs.
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
    /// Writes the twins a dry map has and records them for the material comment. The names come from the game's own
    /// convention - rain appends "_wet", the dirt map replaces the albedo token ("..._diff" -> "..._dirt") - no table.
    /// </summary>
    static private void WriteVariants(string savePath, string texName, Dictionary<string, UmaDatabaseEntry> assets)
    {
        if (string.IsNullOrEmpty(texName)) return;

        var files = new VariantFiles();
        files.Wet = WriteVariantFile(savePath, texName + WetSuffix, assets);
        if (texName.EndsWith(AlbedoSuffix, StringComparison.Ordinal))
        {
            // the dirt map is the coarse asset (256x256 body, 128x128 face), so it is resampled on the way out; see UpscaleBicubic
            files.Dirt = WriteVariantFile(savePath, texName.Substring(0, texName.Length - AlbedoSuffix.Length) + DirtSuffix,
                                          assets, upscale: DirtUpscaleFactor);
        }

        if (string.IsNullOrEmpty(files.Wet) && string.IsNullOrEmpty(files.Dirt)) return;
        writtenVariants[texName] = files;
    }

    /// <summary>
    /// One variant file, under the game's own name, and only when that asset exists. Returns the name written, or null - which
    /// is how a material with no twin is told apart from one with a twin.
    /// </summary>
    static private string WriteVariantFile(string savePath, string name, Dictionary<string, UmaDatabaseEntry> assets,
                                          int upscale = 1)
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

        Texture2D output = duplicateTexture(texture);
        if (upscale > 1) output = UpscaleBicubic(output, upscale);
        File.WriteAllBytes(path, output.EncodeToPNG());
        return name;
    }

    /// <summary>
    /// How far the dirt maps are resampled on the way out, 1 meaning "as the game ships it". The dirt set is the coarse one
    /// (256x256 body, 128x128 face, against 1024-2048 around them) and four is where the resample stops helping at normal
    /// viewing distance: 256 -> 1024, 128 -> 512. It cannot add detail; it removes the blockiness of a magnified map.
    /// </summary>
    public const int DirtUpscaleFactor = 4;

    /// <summary>The largest side the resample may reach, so a future coarse-but-huge map cannot balloon an export.</summary>
    const int DirtUpscaleLimit = 2048;

    /// <summary>
    /// A texture resampled by a whole factor with Catmull-Rom weights, one horizontal pass and one vertical, edges clamped.
    /// Catmull-Rom rather than bilinear because the maps are near-binary masks, and not Lanczos because its ringing halos dots.
    /// </summary>
    static private Texture2D UpscaleBicubic(Texture2D source, int factor)
    {
        int width = Mathf.Min(source.width * factor, DirtUpscaleLimit);
        int height = Mathf.Min(source.height * factor, DirtUpscaleLimit);
        Color[] pixels = source.GetPixels();

        Color[] rows = new Color[width * source.height];
        for (int y = 0; y < source.height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float fx = (x + 0.5f) * source.width / width - 0.5f;
                int x0 = Mathf.FloorToInt(fx);
                float t = fx - x0;
                Color sum = Color.black;
                for (int k = -1; k <= 2; k++)
                {
                    int index = Mathf.Clamp(x0 + k, 0, source.width - 1);
                    sum += pixels[y * source.width + index] * CatmullRom(t - k);
                }
                rows[y * width + x] = new Color(Mathf.Clamp01(sum.r), Mathf.Clamp01(sum.g), Mathf.Clamp01(sum.b), 1f);
            }
        }

        Color[] result = new Color[width * height];
        for (int y = 0; y < height; y++)
        {
            float fy = (y + 0.5f) * source.height / height - 0.5f;
            int y0 = Mathf.FloorToInt(fy);
            float t = fy - y0;
            for (int x = 0; x < width; x++)
            {
                Color sum = Color.black;
                for (int k = -1; k <= 2; k++)
                {
                    int index = Mathf.Clamp(y0 + k, 0, source.height - 1);
                    sum += rows[index * width + x] * CatmullRom(t - k);
                }
                result[y * width + x] = new Color(Mathf.Clamp01(sum.r), Mathf.Clamp01(sum.g), Mathf.Clamp01(sum.b), 1f);
            }
        }

        Texture2D upscaled = new Texture2D(width, height, TextureFormat.RGBA32, false);
        upscaled.name = source.name;
        upscaled.SetPixels(result);
        upscaled.Apply();
        return upscaled;
    }

    /// <summary>Catmull-Rom weight for the sample at integer offset `offset` from the one below `t`.</summary>
    static private float CatmullRom(float offset)
    {
        float x = Mathf.Abs(offset);
        if (x < 1f) return 1.5f * x * x * x - 2.5f * x * x + 1f;
        if (x < 2f) return -0.5f * x * x * x + 2.5f * x * x - 4f * x + 2f;
        return 0f;
    }
}
