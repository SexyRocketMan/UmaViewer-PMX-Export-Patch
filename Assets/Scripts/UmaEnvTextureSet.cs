using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

/// <summary>
/// Resolves the texture sets of environment props/scenes.
///
/// Environment materials are frequently serialized with no texture at all, because the game assigns
/// one of several texture sets at runtime (time of day / weather / event banner). The home screen is
/// a good example:
///
///   mtl_env_home10001_main000_000_base01      &lt;- no texture in the prefab's bundle
///   tex_env_home10001_main000_212_base01      &lt;- 2048x2048, one texture set
///   tex_env_home10001_main000_214_base01      &lt;- 2048x2048, another texture set
///
/// <see cref="UmaContainerProp"/> only instantiated the prefab, so those materials stayed
/// untextured: they render flat white in the viewer and export with the wrong (or first) texture.
///
/// Attaching this component to a loaded prop/scene finds the texture sets that exist for every
/// textureless material, picks one (or the requested variant) and assigns it. The picked variant is
/// exposed through <see cref="CurrentVariant"/>/<see cref="Variants"/> so the UI can offer a choice.
/// </summary>
[DisallowMultipleComponent]
public class UmaEnvTextureSet : MonoBehaviour
{
    /// <summary>Asset-name stem shared by the prefab, its materials and its texture sets.</summary>
    public string Stem { get; private set; } = "";

    /// <summary>Texture set variants available for this scene, ordered by preference.</summary>
    public List<string> Variants { get; private set; } = new List<string>();

    /// <summary>Currently applied variant, empty when nothing could be resolved.</summary>
    public string CurrentVariant { get; private set; } = "";

    /// <summary>Number of material slots whose texture was resolved from a variant set.</summary>
    public int AppliedSlots { get; private set; }

    /// <summary>Material slots (and the texture-set suffix each one needs) that were left empty.</summary>
    public List<string> UnresolvedSlots { get; private set; } = new List<string>();

    private class Slot
    {
        public Material Material;
        public string Suffix;
    }

    private readonly List<Slot> _slots = new List<Slot>();
    private readonly Dictionary<string, Dictionary<string, UmaDatabaseEntry>> _variants =
        new Dictionary<string, Dictionary<string, UmaDatabaseEntry>>();
    private readonly Dictionary<string, Texture2D> _textureCache = new Dictionary<string, Texture2D>();

    /// <summary>
    /// Finds the texture sets of a loaded prop/scene and applies one.
    /// </summary>
    /// <param name="root">Instantiated prop/scene.</param>
    /// <param name="assetName">Database name of the prefab, e.g. "3d/env/home/home10001/main/pfb_env_home10001_main000_000".</param>
    /// <param name="preferredVariant">Variant to apply, or null for the best default.</param>
    public static UmaEnvTextureSet Attach(GameObject root, string assetName, string preferredVariant = null)
    {
        if (root == null) return null;
        var set = root.GetComponent<UmaEnvTextureSet>();
        if (set == null) set = root.AddComponent<UmaEnvTextureSet>();
        set.Resolve(assetName, preferredVariant);
        return set;
    }

    /// <summary>Switches to another variant, e.g. from a UI dropdown.</summary>
    public bool SetVariant(string variant)
    {
        if (string.IsNullOrEmpty(variant)) return false;
        if (!_variants.ContainsKey(variant)) return false;
        ApplyVariant(variant);
        return true;
    }

    private void Resolve(string assetName, string preferredVariant)
    {
        _slots.Clear();
        _variants.Clear();
        _textureCache.Clear();
        UnresolvedSlots.Clear();
        AppliedSlots = 0;
        CurrentVariant = "";

        var main = UmaViewerMain.Instance;
        if (main == null || string.IsNullOrEmpty(assetName))
        {
            enabled = false;
            return;
        }

        // materials of this prefab that have a main texture slot but nothing in it
        var textureless = new Dictionary<string, string>(); // material name -> suffix
        var stuckWithoutTextureSlot = new List<string>();
        foreach (var renderer in GetComponentsInChildren<Renderer>(true))
        {
            foreach (var material in renderer.sharedMaterials)
            {
                if (material == null) continue;
                if (!material.HasProperty("_MainTex")) continue;
                if (material.GetTexture("_MainTex") != null) continue;
                if (textureless.ContainsKey(material.name)) continue;
                textureless[material.name] = null; // suffix filled in once the stem is known
                if (!stuckWithoutTextureSlot.Contains(material.name)) stuckWithoutTextureSlot.Add(material.name);
            }
        }

        if (stuckWithoutTextureSlot.Count == 0)
        {
            // nothing to do, every material already has its texture
            enabled = false;
            return;
        }

        // The stem is derived from the asset name, but the variant token of the name is unreliable
        // across scene families, so try both readings and keep whichever resolves something.
        foreach (string stem in CandidateStems(assetName))
        {
            var resolved = BuildVariants(main, stem, stuckWithoutTextureSlot);
            if (resolved.Count == 0) continue;

            Stem = stem;
            foreach (var pair in resolved) _variants[pair.Key] = pair.Value;

            foreach (string materialName in stuckWithoutTextureSlot)
            {
                string suffix = MaterialSuffix(materialName, stem);
                if (string.IsNullOrEmpty(suffix)) continue;
                if (!_variants.Values.Any(v => v.ContainsKey(suffix)))
                {
                    UnresolvedSlots.Add($"{materialName} (no texture set named '..._{suffix}')");
                    continue;
                }
                foreach (var renderer in GetComponentsInChildren<Renderer>(true))
                {
                    foreach (var material in renderer.sharedMaterials)
                    {
                        if (material == null || material.name != materialName) continue;
                        if (material.GetTexture("_MainTex") != null) continue;
                        _slots.Add(new Slot { Material = material, Suffix = suffix });
                    }
                }
            }

            if (_slots.Count == 0) continue;

            Variants = _variants.Keys.ToList();
            // prefer the variant that fills the most slots, then the lowest code (212 before 214)
            Variants = Variants
                .OrderByDescending(v => _variants[v].Count(k => _slots.Any(s => s.Suffix == k.Key)))
                .ThenBy(v => v, StringComparer.Ordinal)
                .ToList();

            if (!string.IsNullOrEmpty(preferredVariant) && _variants.ContainsKey(preferredVariant))
            {
                ApplyVariant(preferredVariant);
            }
            else
            {
                if (!string.IsNullOrEmpty(preferredVariant))
                    Debug.LogWarning($"[UmaEnvTextureSet] variant '{preferredVariant}' does not exist for {Stem}, available: {string.Join(", ", Variants)}");
                ApplyVariant(Variants[0]);
            }

            Debug.Log($"[UmaEnvTextureSet] {name}: resolved {AppliedSlots}/{_slots.Count} textureless material slot(s) "
                      + $"to variant '{CurrentVariant}' of [{string.Join(", ", Variants)}]");
            if (UnresolvedSlots.Count > 0)
                Debug.Log($"[UmaEnvTextureSet] {name}: no texture set for {string.Join("; ", UnresolvedSlots)}");
            enabled = false;
            return;
        }

        Debug.Log($"[UmaEnvTextureSet] {name}: no texture sets found for {stuckWithoutTextureSlot.Count} textureless material(s) "
                  + $"({string.Join(", ", stuckWithoutTextureSlot.Take(6))})");
        enabled = false;
    }

    private void ApplyVariant(string variant)
    {
        var sets = _variants[variant];
        AppliedSlots = 0;
        foreach (var slot in _slots)
        {
            if (!sets.TryGetValue(slot.Suffix, out var entry)) continue;
            var texture = LoadTexture(variant, slot.Suffix, entry);
            if (texture == null) continue;
            slot.Material.SetTexture("_MainTex", texture);
            AppliedSlots++;
        }
        CurrentVariant = variant;
    }

    private Texture2D LoadTexture(string variant, string suffix, UmaDatabaseEntry entry)
    {
        string key = $"{variant}|{suffix}";
        if (_textureCache.TryGetValue(key, out var cached)) return cached;

        Texture2D texture = null;
        if (!File.Exists(entry.Path))
        {
            Debug.LogWarning($"[UmaEnvTextureSet] {entry.Name} is not on disk, skipping");
        }
        else
        {
            try
            {
                texture = entry.Get<Texture2D>();
                if (texture == null) Debug.LogWarning($"[UmaEnvTextureSet] {entry.Name} contains no Texture2D");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[UmaEnvTextureSet] loading {entry.Name} failed: {ex.Message}");
            }
        }
        _textureCache[key] = texture;
        return texture;
    }

    /// <summary>All texture sets that exist for the given textureless materials, by variant then suffix.</summary>
    private static Dictionary<string, Dictionary<string, UmaDatabaseEntry>> BuildVariants(
        UmaViewerMain main, string stem, List<string> materialNames)
    {
        var suffixes = materialNames
            .Select(n => MaterialSuffix(n, stem))
            .Where(s => !string.IsNullOrEmpty(s))
            .Distinct()
            .ToList();

        var result = new Dictionary<string, Dictionary<string, UmaDatabaseEntry>>();
        if (suffixes.Count == 0) return result;

        string prefix = $"tex_{stem}_";
        foreach (var entry in main.AbList.Values)
        {
            if (!entry.Name.StartsWith("3d/env", StringComparison.OrdinalIgnoreCase)) continue;
            string file = Path.GetFileName(entry.Name);
            if (!file.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;

            string rest = file.Substring(prefix.Length);
            foreach (string suffix in suffixes)
            {
                string postfix = $"_{suffix}";
                if (!rest.EndsWith(postfix, StringComparison.OrdinalIgnoreCase)) continue;
                if (rest.Length <= postfix.Length) continue;

                string variant = rest.Substring(0, rest.Length - postfix.Length);
                if (!result.TryGetValue(variant, out var bySuffix))
                {
                    bySuffix = new Dictionary<string, UmaDatabaseEntry>();
                    result[variant] = bySuffix;
                }
                bySuffix[suffix] = entry;
                break;
            }
        }
        return result;
    }

    /// <summary>
    /// "pfb_env_home10001_main000_000" -&gt; "env_home10001_main000".
    /// Both readings (with and without the trailing index) are returned because the index is part of
    /// the stem in some scene families.
    /// </summary>
    private static IEnumerable<string> CandidateStems(string assetName)
    {
        string name = Path.GetFileName(assetName);
        if (name.StartsWith("pfb_", StringComparison.OrdinalIgnoreCase)) name = name.Substring(4);
        yield return name;

        var parts = name.Split('_');
        if (parts.Length > 1 && parts[parts.Length - 1].Length <= 4 && parts[parts.Length - 1].All(char.IsDigit))
        {
            yield return string.Join("_", parts.Take(parts.Length - 1));
        }
    }

    /// <summary>"mtl_&lt;stem&gt;_000_base01" -&gt; "base01" (drops the variant token).</summary>
    private static string MaterialSuffix(string materialName, string stem)
    {
        string prefix = $"mtl_{stem}_";
        if (!materialName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        var parts = materialName.Substring(prefix.Length).Split('_');
        return parts.Length > 1 ? string.Join("_", parts.Skip(1)) : null;
    }
}
