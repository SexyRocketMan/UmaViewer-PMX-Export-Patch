using System.Collections.Generic;
using System.Text;

/// <summary>
/// Decides what a morph is called, shared by the PMX exporter (<see cref="ModelExporter"/>) and the VMD recorder
/// (<see cref="UnityHumanoidVMDRecorder"/>): mmd_tools matches vmd morph keys to shape keys by name, so a name carried by
/// only one of the two files is silently dropped on import. BlenderCompatible "(Tag)[Mesh]" is model-only and too long for a
/// vmd, ShortEnglish is "Eye_2_L", Unified "Brow_WaraiA_R" (default, fits both).
/// </summary>
public static class MorphNaming
{
    /// <summary>vmd stores morph names in a fixed 15 byte shift-jis field.</summary>
    public const int VmdNameByteLimit = 15;

    private static readonly Encoding ShiftJis = Encoding.GetEncoding("shift_jis");

    /// <summary>Groups are written in english, but "EyeBrow" does not leave room for the tag.</summary>
    private static readonly Dictionary<string, string> GroupTokens = new Dictionary<string, string>
    {
        { "Eye", "Eye" },
        { "EyeBrow", "Brow" },
        { "Ear", "Ear" },
        { "Mouth", "Mouth" },
    };

    /// <summary>
    /// Tags that overflow the vmd byte limit with their group ("Eye_EyelidHideA_L" is 17 bytes); everything else fits as-is.
    /// </summary>
    private static readonly Dictionary<string, string> TagAbbreviations = new Dictionary<string, string>
    {
        { "EyelidHideA", "LidHideA" },
        { "EyelidHideB", "LidHideB" },
    };

    private static readonly string[] Families = { "EyeBrow", "Eye", "Ear", "Mouth" };

    /// <summary>The pieces of a morph name, in either the legacy or the unified spelling.</summary>
    public class Parts
    {
        /// <summary>Eye, EyeBrow, Ear, Mouth or Other.</summary>
        public string Family = "Other";
        /// <summary>Index within the family, -1 when the name does not carry one.</summary>
        public int Id = -1;
        /// <summary>"L", "R" or "" (morphs that are not sided, e.g. most mouth shapes).</summary>
        public string Side = "";
        /// <summary>Romaji action tag, e.g. "WaraiA".</summary>
        public string Tag = "";
        /// <summary>Source mesh of the legacy spelling, e.g. "M_Face".</summary>
        public string Mesh = "";
    }

    /// <summary>
    /// Parses "EyeBrow_1_R(WaraiA)[M_Face]", "Mouth_2_0(CheekA_L)[M_Face]" and the short forms
    /// "EyeBrow_1_R" / "Mouth_2_0". <paramref name="tagHint"/> (FacialMorph.tag) supplies a missing tag.
    /// </summary>
    public static bool TryParse(string rawName, string tagHint, out Parts parts)
    {
        parts = new Parts();
        if (string.IsNullOrEmpty(rawName)) return false;

        string name = rawName.Trim();

        // "(tag)" and "[mesh]" only exist in the tagged spelling
        int parenStart = name.IndexOf('(');
        if (parenStart > 0)
        {
            int parenEnd = name.IndexOf(')', parenStart + 1);
            if (parenEnd > parenStart)
            {
                parts.Tag = name.Substring(parenStart + 1, parenEnd - parenStart - 1);
                name = name.Substring(0, parenStart) + name.Substring(parenEnd + 1);
            }
        }
        int bracketStart = name.IndexOf('[');
        if (bracketStart > 0)
        {
            int bracketEnd = name.IndexOf(']', bracketStart + 1);
            if (bracketEnd > bracketStart)
            {
                parts.Mesh = name.Substring(bracketStart + 1, bracketEnd - bracketStart - 1);
                name = name.Substring(0, bracketStart) + name.Substring(bracketEnd + 1);
            }
        }

        name = name.Trim();
        if (!string.IsNullOrEmpty(tagHint)) parts.Tag = tagHint;

        foreach (string family in Families)
        {
            if (!name.StartsWith(family + "_")) continue;
            parts.Family = family;
            name = name.Substring(family.Length + 1);
            break;
        }

        // remaining fields: {id}[_{index}][_{side}]
        var fields = new List<string>(name.Split('_'));
        for (int i = fields.Count - 1; i >= 0; i--)
        {
            string field = fields[i];
            if (field.Length == 1 && (field == "L" || field == "R"))
            {
                parts.Side = field;
                fields.RemoveAt(i);
            }
        }
        if (fields.Count > 0 && int.TryParse(fields[0], out int id)) parts.Id = id;

        // some tags carry the side themselves ("CheekA_L", "KusyoA_R", "TanC_L")
        if (string.IsNullOrEmpty(parts.Side) && parts.Tag.Length > 2 && parts.Tag.EndsWith("_L"))
        {
            parts.Side = "L";
            parts.Tag = parts.Tag.Substring(0, parts.Tag.Length - 2);
        }
        else if (string.IsNullOrEmpty(parts.Side) && parts.Tag.Length > 2 && parts.Tag.EndsWith("_R"))
        {
            parts.Side = "R";
            parts.Tag = parts.Tag.Substring(0, parts.Tag.Length - 2);
        }

        if (parts.Tag.Contains(".00")) parts.Tag = parts.Tag.Substring(0, parts.Tag.LastIndexOf('.'));
        parts.Tag = parts.Tag.Trim();

        return parts.Family != "Other" || parts.Id >= 0;
    }

    /// <summary>The legacy short spelling: "EyeBrow_1_R(WaraiA)[M_Face]" -> "EyeBrow_1_R".</summary>
    public static string ShortName(string rawName, string tagHint = null)
    {
        if (string.IsNullOrEmpty(rawName)) return rawName;
        string name = rawName.Trim();

        int parenStart = name.IndexOf('(');
        if (parenStart > 0) name = name.Substring(0, parenStart);
        int bracketStart = name.IndexOf('[');
        if (bracketStart > 0) name = name.Substring(0, bracketStart);

        if (name.Contains(".00")) name = name.Substring(0, name.LastIndexOf('.'));
        return name.Trim();
    }

    /// <summary>
    /// The unified spelling: english group + romaji tag + side, e.g. "Brow_WaraiA_R". Falls back to the short name without a
    /// usable tag or family, or when the result would not fit a vmd name field.
    /// </summary>
    public static string UnifiedName(string rawName, string tagHint)
    {
        if (!TryParse(rawName, tagHint, out Parts parts)) return ShortName(rawName);

        if (parts.Family == "Other" || string.IsNullOrEmpty(parts.Tag)) return ShortName(rawName);

        string tag = TagAbbreviations.TryGetValue(parts.Tag, out string abbrev) ? abbrev : parts.Tag;
        string group = GroupTokens.TryGetValue(parts.Family, out string token) ? token : parts.Family;

        string name = string.IsNullOrEmpty(parts.Side) ? $"{group}_{tag}" : $"{group}_{tag}_{parts.Side}";
        if (ByteLength(name) <= VmdNameByteLimit) return name;

        // keep the name valid and unique rather than truncating it into something ambiguous
        return ShortName(rawName);
    }

    /// <summary>
    /// Every morph name one morph is exported under, for the active mode. Its input is already the tagged spelling written by
    /// <c>ModelExporter.AddBlendShape</c> ("Eye_2_L(CloseA)[M_Face]").
    /// </summary>
    public static IEnumerable<string> NamesFor(string rawName, string tagHint, PmxMorphNameMode mode)
    {
        switch (mode)
        {
            case PmxMorphNameMode.BlenderCompatible:
                yield return rawName;
                yield break;

            case PmxMorphNameMode.ShortEnglish:
                yield return ShortName(rawName);
                yield break;

            // unified is also the fallback for a mode value this build does not know: the only spelling that works for both files
            default:
                yield return UnifiedName(rawName, tagHint);
                yield break;
        }
    }

    /// <summary>
    /// The name a vmd uses for this morph: a name the exported model carries, within the 15 byte field, or Blender silently
    /// drops the morph keyframes.
    /// </summary>
    public static string VmdName(string rawName, string tagHint, PmxMorphNameMode mode)
    {
        // unified is the only spelling both files can carry, hence also the fail-safe for an unknown mode
        if (mode != PmxMorphNameMode.ShortEnglish)
        {
            string unified = UnifiedName(rawName, tagHint);
            if (FitsVmd(unified)) return unified;
        }

        // what ShortEnglish exports, and the last resort for a morph whose unified name would overflow
        string shortName = ShortName(rawName);
        return FitsVmd(shortName) ? shortName : UnifiedName(rawName, tagHint);
    }

    /// <summary>
    /// False when model and motion cannot agree on this morph's name: in BlenderCompatible mode the model carries a spelling no
    /// vmd field can hold.
    /// </summary>
    public static bool ModelAndMotionNamesAgree(PmxMorphNameMode mode) => mode != PmxMorphNameMode.BlenderCompatible;

    public static int ByteLength(string value)
    {
        if (string.IsNullOrEmpty(value)) return 0;
        return ShiftJis.GetByteCount(value);
    }

    public static bool FitsVmd(string value) => ByteLength(value) <= VmdNameByteLimit;
}
