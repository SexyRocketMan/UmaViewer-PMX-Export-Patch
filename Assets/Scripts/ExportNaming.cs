using System.IO;
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
    /// The tail of an animation asset name: "anm_rac_type01_run02_stride" is "stride". The viewer names
    /// clips after their asset path, so the last underscore separated part is what identifies the motion.
    /// </summary>
    public static string MotionTag(AnimationClip clip)
    {
        if (clip == null) return "motion";
        // clip names are asset paths ("3d/motion/racemain/body/type01/anm_rac_type01_run02_stride")
        string file = Path.GetFileName(clip.name);
        if (string.IsNullOrEmpty(file)) file = clip.name;

        string[] parts = file.Split('_');
        for (int i = parts.Length - 1; i >= 0; i--)
        {
            if (!string.IsNullOrEmpty(parts[i])) return Sanitize(parts[i], "motion");
        }
        return Sanitize(file, "motion");
    }

    /// <summary>The whole default name for a recorded motion: "special_week_stride".</summary>
    public static string MotionFile(UmaContainerCharacter container, AnimationClip clip)
    {
        return $"{CharacterName(container)}_{MotionTag(clip)}";
    }
}
