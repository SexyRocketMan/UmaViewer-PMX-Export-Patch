using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

/// <summary>
/// Which manifest entries count as props, and what to call each one on a card.
///
/// The list the prop panel shows and the list the grid browser shows must be the same list, so the rule lives
/// here rather than in either of them: a prefab of one of the three prop families, and not its `clothes`
/// variant. `docs/ASSET_FAMILIES.md` has the measured counts behind those three names, and
/// `docs/PROP_LABELS.md` is why a card needs a name of its own: the game ships none, so the browser has to
/// show the id - and the id is what a user reports a mistake with, so it is never abbreviated away entirely.
/// </summary>
public static class PropCatalog
{
    /// <summary>
    /// The prefab name prefixes that are props.
    ///
    /// The game names each `3d/chara` family its own way, so the single `pfb_chr_prop` substring the prop panel
    /// used to filter on reached exactly one family and left others unreachable even though their bundles are in
    /// the manifest: `toonprop` (held/effect props, one per character) and `richprop` (three live-show props).
    ///
    /// `attach` is deliberately absent and `sweat` always was: an attach prefab is not a prop but a second skin
    /// bound to the character's own skeleton (`UISettingsModel.LoadAttachPanel`,
    /// `UmaContainerCharacter.AttachAccessory`), and a sweat prefab is a `..._sweatlocator` with no geometry.
    /// </summary>
    public static readonly string[] Prefixes = { "pfb_chr_prop", "pfb_toon_prop", "pfb_rich_prop" };

    /// <summary>True for a manifest path the prop panel and the grid should offer.</summary>
    public static bool IsProp(string path)
    {
        if (string.IsNullOrEmpty(path) || path.Contains("clothes")) return false;
        string name = Path.GetFileName(path);
        return Prefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal));
    }

    /// <summary>The `3d/chara` family a manifest path belongs to ("attach", "prop", ...), or "" outside it.</summary>
    public static string Family(string path)
    {
        const string root = "3d/chara/";
        if (string.IsNullOrEmpty(path) || !path.StartsWith(root, StringComparison.Ordinal)) return "";
        string rest = path.Substring(root.Length);
        int slash = rest.IndexOf('/');
        return slash > 0 ? rest.Substring(0, slash) : rest;
    }

    /// <summary>Every prop in the manifest, in manifest order, which is id order within a family.</summary>
    public static List<UmaDatabaseEntry> Entries()
    {
        var main = UmaViewerMain.Instance;
        if (main == null || main.AbList == null) return new List<UmaDatabaseEntry>();
        return main.AbList
            .Where(pair => IsProp(pair.Key))
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => pair.Value)
            .ToList();
    }

    /// <summary>
    /// What a card says: the family, abbreviated, and the id without its prefix - "prop 1003_10" rather than
    /// "pfb_chr_prop1003_10". The family has to be on the card because the same id exists in more than one of
    /// them, and the full prefab name is still available as the search text.
    /// </summary>
    public static string Label(UmaDatabaseEntry entry)
    {
        if (entry == null) return "";
        string name = Path.GetFileName(entry.Name);
        string family = Family(entry.Name);
        string shortFamily = family switch
        {
            "prop" => "prop",
            "toonprop" => "toon",
            "richprop" => "rich",
            _ => family,
        };
        foreach (string prefix in Prefixes)
        {
            if (name.StartsWith(prefix, StringComparison.Ordinal))
            {
                return $"{shortFamily} {name.Substring(prefix.Length)}";
            }
        }
        return name;
    }
}
