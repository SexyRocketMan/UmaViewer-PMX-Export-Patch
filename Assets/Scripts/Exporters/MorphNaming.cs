using System.Collections.Generic;
using System.Text;

/// <summary>
/// The one place that decides what a morph is called, shared by the PMX exporter
/// (<see cref="ModelExporter"/>) and the VMD recorder (<see cref="UnityHumanoidVMDRecorder"/>).
///
/// Those two used to derive names independently, which is how exported models and exported motions
/// ended up disagreeing: Blender's mmd_tools imports vmd morph keyframes by matching the morph name
/// against the shape keys of the imported model, so a name that exists in only one of the two files
/// is silently dropped on import.
///
/// Naming modes (see <see cref="PmxMorphNameMode"/>):
///   BlenderCompatible  "(Tag)[Mesh]" suffix kept, matches the stock uma_addon but is far too long
///                      for a vmd morph name (the limit is 15 shift-jis bytes), so motions cannot
///                      drive those morphs.
///   ShortEnglish       "Eye_2_L" - fits a vmd but says nothing about what the morph does.
///   Both               emits the tagged morph plus a short alias.
///   Unified            "Brow_WaraiA_R" - descriptive, unique and within the vmd byte limit, so one
///                      name serves the model and the motion. Default.
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
    /// Tags that do not fit the byte limit with their group ("Eye_EyelidHideA_L" is 17 bytes).
    /// Everything else in the game's morph set fits as-is.
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
    /// "EyeBrow_1_R" / "Mouth_2_0". <paramref name="tagHint"/> (FacialMorph.tag) supplies the tag for
    /// names that do not carry one.
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
    /// The unified spelling: english group + romaji tag + side, e.g. "Brow_WaraiA_R", "Eye_XRange_L",
    /// "Mouth_TalkA_O_S". Falls back to the short name for morphs without a usable tag or family, and
    /// for anything that would not fit a vmd name field.
    /// </summary>
    public static string UnifiedName(string rawName, string tagHint)
    {
        if (!TryParse(rawName, tagHint, out Parts parts)) return ShortName(rawName);

        if (parts.Family == "Other" || string.IsNullOrEmpty(parts.Tag)) return ShortName(rawName);

        string tag = TagAbbreviations.TryGetValue(parts.Tag, out string abbrev) ? abbrev : parts.Tag;
        string group = GroupTokens.TryGetValue(parts.Family, out string token) ? token : parts.Family;

        string name = string.IsNullOrEmpty(parts.Side) ? $"{group}_{tag}" : $"{group}_{tag}_{parts.Side}";
        if (ByteLength(name) <= VmdNameByteLimit) return name;

        // a tag that is not in the abbreviation table and does not fit: keep the name valid and
        // unique instead of truncating it into something ambiguous
        return ShortName(rawName);
    }

    /// <summary>
    /// Every morph name a single morph is exported under, for the active mode. Only the exporter uses
    /// this, and its input is already the tagged spelling written by
    /// <c>ModelExporter.AddBlendShape</c> ("Eye_2_L(CloseA)[M_Face]").
    /// </summary>
    public static IEnumerable<string> NamesFor(string rawName, string tagHint, PmxMorphNameMode mode)
    {
        switch (mode)
        {
            case PmxMorphNameMode.ShortEnglish:
                yield return ShortName(rawName);
                yield break;

            case PmxMorphNameMode.Unified:
                yield return UnifiedName(rawName, tagHint);
                yield break;

            case PmxMorphNameMode.Both:
                yield return rawName;
                string shortName = ShortName(rawName);
                if (shortName != rawName) yield return shortName;
                yield break;

            default:
                yield return rawName;
                yield break;
        }
    }

    /// <summary>
    /// The name a vmd uses for this morph. It has to be a name the exported model actually carries
    /// and it has to fit the 15 byte field, otherwise Blender silently drops the morph keyframes.
    /// </summary>
    public static string VmdName(string rawName, string tagHint, PmxMorphNameMode mode)
    {
        if (mode == PmxMorphNameMode.Unified)
        {
            string unified = UnifiedName(rawName, tagHint);
            if (FitsVmd(unified)) return unified;
        }

        // the short spelling is what "ShortEnglish" exports and what "Both" emits as an alias
        string shortName = ShortName(rawName);
        return FitsVmd(shortName) ? shortName : UnifiedName(rawName, tagHint);
    }

    /// <summary>
    /// False when the exported model and the exported motion cannot agree on this morph's name, i.e.
    /// in "BlenderCompatible" mode where the model carries a spelling no vmd field can hold.
    /// </summary>
    public static bool ModelAndMotionNamesAgree(PmxMorphNameMode mode) => mode != PmxMorphNameMode.BlenderCompatible;

    public static int ByteLength(string value)
    {
        if (string.IsNullOrEmpty(value)) return 0;
        return ShiftJis.GetByteCount(value);
    }

    public static bool FitsVmd(string value) => ByteLength(value) <= VmdNameByteLimit;
}
