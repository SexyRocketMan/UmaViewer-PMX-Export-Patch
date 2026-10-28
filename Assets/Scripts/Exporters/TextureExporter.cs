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
    /// A readable copy of a texture, written as PNG by the export.
    ///
    /// The copy keeps the source's name, because `new Texture2D(...)` starts out unnamed and every caller
    /// writes the copy under the name it was handed. That transfer is not cosmetic: before it, a derived file
    /// was written as `_blush.png` and listed in the pmx under that name, while a second export into the same
    /// folder - which skips the write because the file already exists - produced
    /// `tex_chr1001_00_cheek0_blush.png` instead. Same model, two texture lists, one of them pointing at a
    /// file named after nothing. (Nothing is derived any more - the cheek keeps the game's own map - but the
    /// rule holds for whatever comes next.)
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

                                // The eye's `_MainTex` is a four-plate atlas (512x256 on 1001) and the mesh UVs are
                                // plate-local: `_Offset` selects the plate and the shipped value is (0,0,0,0), so the
                                // left plate is the one this export draws, and cropping to it keeps the UVs valid as
                                // they stand (measured: the eye samples u 0.135..0.865, v 0.025..0.975 - a plate-local
                                // range, not a whole-sheet one). The full sheet used to be written beside it as
                                // `<name>_all.png`; nothing named it - not the pmx's texture table, not a material, not
                                // the add-on - and it cost 42 KB per character, so it is not written any more.
                                if (tex_name.EndsWith("eye0"))
                                {
                                    var clip_texture = duplicateTexture(mat_texture);
                                    var pixels = clip_texture.GetPixels(0, 0, clip_texture.width / 4, clip_texture.height);
                                    clip_texture.Reinitialize(clip_texture.width / 4, clip_texture.height);
                                    clip_texture.SetPixels(pixels);
                                    File.WriteAllBytes(tex_path, clip_texture.EncodeToPNG());
                                    continue;
                                }

                                byte[] bytes = mat_texture.EncodeToPNG();
                                File.WriteAllBytes(tex_path, bytes);
                            }
                            // A multiply decal (the cheek) needs no baked copy any more: the game's own map is written
                            // as it is, and the addon derives the alpha from it at shade time with the multiply node and
                            // luminance ramp its "Fix mini umamusume model" operator already uses. lend=multiply says so.
                            // **The highlight mask bake is gone** (2.34), after measuring what wanted it. It wrote
                            // `eyehi00_mask.png` and its siblings for a consumer that thresholds *outside* a shader,
                            // so a plain MMD material could draw the game's colour-masked highlights without painting
                            // black over the eye. On an export of 1001: no material names any `_mask` file in any
                            // slot (`tex` = 10 = `eye0`, `sphere` -1, `toon` -1), and the add-on does not want them
                            // either - it binds the raw `eyehi00`/`01`/`02` and thresholds in `Uma Eyes` with its two
                            // `SmoothThreshold` groups. They were in the pmx's texture table only because this block
                            // registered them as dependencies. A dead end: kept in history, not in every user's folder.
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
                // the cheek pair is a multiply map either way, and both are written as the game ships them:
                // no alpha-only bake of either (see the note beside the decal material in ModelExporter)
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
