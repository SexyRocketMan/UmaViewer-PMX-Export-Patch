using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>
/// Default file names for the save dialogs. The container is named after its database id
/// ("Chara_1001_00"), which says nothing to a human, so files are named after the uma plus the tail of the
/// animation: "anm_rac_type01_run02_stride" for Special Week becomes "special_week_stride.vmd".
/// </summary>
public static class ExportNaming
{
    /// <summary>
    /// Everything a file name cannot hold becomes an underscore, runs of them collapse and the result is
    /// lower case, so "Special Week" is "special_week". Letters that are not ASCII survive (uma names in
    /// japanese are still usable as a file name).
    /// </summary>
    public static string Sanitize(string value, string fallback)
    {
        if (string.IsNullOrEmpty(value)) return fallback;

        var builder = new StringBuilder(value.Length);
        bool lastWasSeparator = false;
        foreach (char c in value.Trim())
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(char.ToLowerInvariant(c));
                lastWasSeparator = false;
                continue;
            }
            if (lastWasSeparator) continue;
            builder.Append('_');
            lastWasSeparator = true;
        }

        string result = builder.ToString().Trim('_');
        return string.IsNullOrEmpty(result) ? fallback : result;
    }

    /// <summary>The uma's own name, falling back to the container name when there is no database entry.</summary>
    public static string CharacterName(UmaContainerCharacter container)
    {
        if (container == null) return "uma";
        var entry = container.CharaEntry;
        string name = entry != null ? entry.GetName() : null;
        if (string.IsNullOrEmpty(name)) name = container.name;
        return Sanitize(name, "uma");
    }

    /// <summary>
    /// The costume the container was loaded with: the long form when there is one ("0001_00_00" for the
    /// generic costumes), otherwise the id the container is named after ("Chara_1001_00" is "00").
    /// </summary>
    public static string CostumeId(UmaContainerCharacter container)
    {
        if (container == null) return "";
        if (!string.IsNullOrEmpty(container.VarCostumeIdLong)) return container.VarCostumeIdLong;
        string[] parts = container.name.Split('_');
        return parts.Length >= 3 ? parts[parts.Length - 1] : "";
    }

    /// <summary>
    /// The costume's name, the way the costume list shows it ("Race Shorts", "Default"), so two exports of
    /// the same uma in different outfits do not suggest the same file name.
    /// </summary>
    public static string CostumeName(UmaContainerCharacter container)
    {
        string costumeId = CostumeId(container);
        if (string.IsNullOrEmpty(costumeId)) return "";

        string dressName = null;
        var main = UmaViewerMain.Instance;
        var entry = container.CharaEntry;
        if (main != null && main.Costumes != null && entry != null && int.TryParse(costumeId, out int sub))
        {
            var dress = main.Costumes.FirstOrDefault(c => c.CharaId == entry.Id && c.BodyTypeSub == sub);
            if (dress != null) dressName = dress.DressName;
        }

        // GetCostumeName turns the id into a readable name and falls back to what it is given
        return Sanitize(UmaViewerUI.GetCostumeName(costumeId, string.IsNullOrEmpty(dressName) ? costumeId : dressName), "");
    }

    /// <summary>The whole default name for a model export: "special_week_default".</summary>
    public static string ModelFile(UmaContainerCharacter container)
    {
        string name = CharacterName(container);
        string costume = CostumeName(container);
        return string.IsNullOrEmpty(costume) ? name : $"{name}_{costume}";
    }

    /// <summary>
    /// A part of an asset name that says nothing about the motion: the "anm" prefix, the character the clip
    /// belongs to ("chr1001") and empty parts.
    /// </summary>
    static bool IsNoiseToken(string token, string characterIdToken)
    {
        if (string.IsNullOrEmpty(token)) return true;
        if (token == "anm") return true;
        if (token.Length > 3 && token.StartsWith("chr") && token.Substring(3).All(char.IsDigit)) return true;
        return !string.IsNullOrEmpty(characterIdToken) && token == characterIdToken;
    }

    /// <summary>
    /// The tail of an animation asset name: "anm_rac_type01_run02_stride" is "stride". Asset names are
    /// "<prefix>_<group>_<character id>_<variation>_<what it is>", so the last part that is not a number
    /// and not the character id is what identifies the motion for a human.
    /// </summary>
    public static string MotionTag(AnimationClip clip, string characterIdToken = null)
    {
        if (clip == null) return "motion";
        // clip names are asset paths ("3d/motion/racemain/body/type01/anm_rac_type01_run02_stride")
        string file = Path.GetFileName(clip.name);
        if (string.IsNullOrEmpty(file)) file = clip.name;

        string[] parts = file.Split('_');
        for (int i = parts.Length - 1; i >= 0; i--)
        {
            string part = parts[i];
            if (IsNoiseToken(part, characterIdToken)) continue;
            if (part.All(char.IsDigit)) continue;

            // keep what follows it, so "anm_res_chr1001_001" is "res_001" rather than "001"
            string tail = part;
            for (int j = i + 1; j < parts.Length; j++)
            {
                if (IsNoiseToken(parts[j], characterIdToken)) continue;
                tail += "_" + parts[j];
            }
            return Sanitize(tail, "motion");
        }
        return Sanitize(file, "motion");
    }

    /// <summary>The whole default name for a recorded motion: "special_week_stride".</summary>
    public static string MotionFile(UmaContainerCharacter container, AnimationClip clip)
    {
        string characterId = container != null && container.CharaEntry != null
            ? "chr" + container.CharaEntry.Id
            : null;
        return $"{CharacterName(container)}_{MotionTag(clip, characterId)}";
    }
}
