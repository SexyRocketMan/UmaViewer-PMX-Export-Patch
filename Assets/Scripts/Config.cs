using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

public class Config
{
    public static string configPath = Application.dataPath + "/../Config.json";
    public static Config Instance;
    public string Version = "";

    public string DBBaseKeyTip = "Base key to read game database files";
    public string DBBaseKeyText;

    public string DBKeyTip = "Key to read game database files";
    public string DBKeyText;

    public string GlobalDBKeyTip = "Key to read game database files (for Global)";
    public string GlobalDBKeyText;

    public string ABKeyTip = "Key to read asset bundles";
    public string ABKeyText;

    public string LanguageTip = "Affects Uma names on the list. Language options: 0 - En, 1 - Jp";
    public Language Language = Language.En;

    public string RegionTip = "Game region. Region options: 0 - Global, 1 - Japan";
    public Region Region = Region.Jp;

    public string DownloadMissingResourcesTip = "true/false. automatically download missing files, which may require a VPN.";
    public bool DownloadMissingResources = true;

    public string MainPathTip = "Path to game folder, eg. D:/Backup/Cygames/umamusume";
    public string MainPath = "";

    public string WorkModeTip = "Affects how application work, options: 0 - work with game client, 1 - work without game client(slow), Database needs to be updated manually";
    public WorkMode WorkMode = WorkMode.Default;

    public string VmdKeyReductionLevelTip = "Affects the recording quality: 1 = record every frame, 2 = record every two frames, and so on.";
    public int VmdKeyReductionLevel = 1;

    public string LastModelFolderTip = "Folder the PMX export dialog opens in. Updated after every export, so the next one starts where the last one went.";
    public string LastModelFolder = "";

    public string LastMotionFolderTip = "Folder the VMD save dialogs open in. Updated after every save, so the next one starts where the last one went.";
    public string LastMotionFolder = "";

    public string PmxMorphNameModeTip = "Naming of morphs in exported .pmx models and in the vmd motions recorded beside them - one setting for both, because a motion can only drive a model whose morphs are spelled the same way, and the two used to be independently selectable. 0 = Blender/uma_addon compatible, keeps the \"(Tag)[Mesh]\" suffix the stock addon matches on (Eye_2_L(CloseA)[M_Face]) but that is 27-30 bytes and a vmd morph name field holds 15, so a model exported this way cannot be driven by any motion; 1 = short english names (Eye_2_L), fits a motion but says nothing about what the morph does; 2 = unified, descriptive and vmd sized (Brow_WaraiA_R). Bone names have no switch at all: a recorded vmd always carries the rig's own names, which are the names the exported pmx carries.";
    public PmxMorphNameMode PmxMorphNameMode = PmxMorphNameMode.Unified;

    public string PmxAPoseRestPoseTip = "true/false. Exports the model with both upper arms rotated 38.5 degrees into the A-pose that motion recording is relative to, so a recorded vmd lines up in Blender on its own - no posing the model and re-importing the motion with \"Use current pose as rest pose\". true for MMD style use, false (default) keeps the plain T-pose rest that rigging/retargeting expects.";
    public bool PmxAPoseRestPose = true;

    public string PmxUmaMaterialFieldsTip = "true/false. Writes the uma material data an MMD material has no field for into the exported materials: the shader settings (shade threshold, rim, specular, outline, map names) as one line in the material comment, which the Blender uma addon reads back, and the material's specular and outline values. false exports plain MMD materials (white diffuse, no specular, black outline at 0.4) for models meant to be used without that addon.";
    public bool PmxUmaMaterialFields = true;

    public string PmxExtraMorphsTip = "true/false. The facial morphs an MMD morph cannot express (the cheek blush, the eye-manga and the tear objects) are exported as extra MMD vertex morphs instead of being lost: each decal part is collapsed onto a point inside the head and a morph of its own (Cheek_Blush, one per eye manga expression, Tear_Static_L and Tear_Static_R, Tear_Pool_L and Tear_Pool_R) puts it back, so the model rests hidden and the morph at weight 1 is the deployed decal - the idiom MMD models use for a hidden accessory, and it needs nothing but MMD itself. The tall rolling tear card is left out on purpose: it has no way to roll, and a tear that cannot move reads as a mistake, so it stays collapsed and is simply not in the model. What a morph cannot hold (the cheek texture pair and _CheekRate, the face shadow alpha) is described in the comments instead: a morph= line in the material comments and extra morphs / object morphs lines in the model comment, which the Blender uma addon reads back. The two *animated* decals (the @_@ turn and the tear sprite-sheet scroll) are not morphs at all any more: their data is in those comment tokens and the addon drives them on the material. false leaves the decals where the game draws them (blush and pool tears visible at rest) and writes no comment lines. See docs/PMX_EXTENSIONS.md.";
    public bool PmxExtraMorphs = true;

    public string FbxApplyUmaRefinementTip = "true/false. FBX export only, and optional on purpose: aims the eye bones the way the Blender uma addon's \"Refine Structure\" aims them (Y out of the face along the gaze, roll so Z points down) and keeps the eyeball skinned to Eye_L/Eye_R, so rotating an eye bone moves the gaze without any driver - which is what FBX cannot carry. true (default) exports the rig as close to an addon-refined model as the format allows; false exports the rig exactly as the viewer holds it.";
    public bool FbxApplyUmaRefinement = true;

    public string FbxEmbedTexturesTip = "true/false. FBX export only. Carries the PNGs inside the .fbx as well as writing the Texture2D folder next to it, so the model stays textured when the file is copied somewhere on its own (Blender packs the embedded copy into the .blend). Costs the size of the textures. false writes the relative paths only, which is what the .pmx does.";
    public bool FbxEmbedTextures = true;

    public string AntiAliasingTip = "Display, screenshot antialiasing level. 0 - no AA, 1 - 2x MSAA, 2 - 4x MSAA, 3 - 8x MSAA";
    public int AntiAliasing = 2;

    public bool RegionDetectionPassed = false;

    public string VmdMorphConvertSettingTip = "The mapping of MMD mprphs to UMA mprphs during VMD recording, multiple UMA expression weights will be combined (not exceeding 1)";
    public List<MorphConvertConfig> VmdMorphConvertSetting = new List<MorphConvertConfig>
    {
        new MorphConvertConfig("にこり右", new string[] { "EyeBrow_1_R" }),
        new MorphConvertConfig("にこり左", new string[] { "EyeBrow_1_L" }),
        new MorphConvertConfig("真面目右", new string[] { "EyeBrow_17_R" }),
        new MorphConvertConfig("真面目左", new string[] { "EyeBrow_17_L" }),
        new MorphConvertConfig("困る右", new string[] { "EyeBrow_6_R", "EyeBrow_12_R" }),
        new MorphConvertConfig("困る左", new string[] { "EyeBrow_6_L" }),
        new MorphConvertConfig("困る２", new string[] { "EyeBrow_13_L", "EyeBrow_20_L", "EyeBrow_10_L" }),
        new MorphConvertConfig("怒り右", new string[] { "EyeBrow_5_R", "EyeBrow_15_R", "EyeBrow_16_R", "EyeBrow_19_R", "EyeBrow_7_R" }),
        new MorphConvertConfig("怒り左", new string[] { "EyeBrow_5_L", "EyeBrow_15_L", "EyeBrow_16_L", "EyeBrow_19_L", "EyeBrow_7_L" }),
        new MorphConvertConfig("怒り２", new string[] { "EyeBrow_18_L" }),
        new MorphConvertConfig("下右", new string[] { "EyeBrow_14_R", "EyeBrow_22_R", "EyeBrow_11_R" }),
        new MorphConvertConfig("下左", new string[] { "EyeBrow_14_L", "EyeBrow_22_L", "EyeBrow_11_L" }),
        new MorphConvertConfig("上右", new string[] { "EyeBrow_21_R" }),
        new MorphConvertConfig("上左", new string[] { "EyeBrow_21_L" }),
        new MorphConvertConfig("眉左", new string[] { "EyeBrow_23_L", "EyeBrow_23_R" }),
        new MorphConvertConfig("眉右", new string[] { "EyeBrow_24_L", "EyeBrow_24_R" }),
        new MorphConvertConfig("ｳｨﾝｸ２右", new string[] { "Eye_2_R" }),
        new MorphConvertConfig("ウィンク２", new string[] { "Eye_2_L" }),
        new MorphConvertConfig("ウィンク右", new string[] { "Eye_5_R", "Eye_8_R" }),
        new MorphConvertConfig("ウィンク", new string[] { "Eye_5_L", "Eye_8_L" }),
        new MorphConvertConfig("ｷﾘｯ", new string[] { "Eye_9_L", "Eye_10_L", "Eye_11_L", "Eye_18_L" }),
        new MorphConvertConfig("なごみ", new string[] { "Eye_16_L" }),
        new MorphConvertConfig("びっくり", new string[] { "Eye_12_L" }),
        new MorphConvertConfig("じと目", new string[] { "Eye_15_L" }),
        new MorphConvertConfig("瞳小", new string[] { "Eye_14_L" }),
        new MorphConvertConfig("笑い目", new string[] { "Eye_19_L" }),
        new MorphConvertConfig("笑い目２", new string[] { "Eye_23_L" }),
        new MorphConvertConfig("ぷく～_左", new string[] { "Mouth_2_0" }),
        new MorphConvertConfig("ぷく～_右", new string[] { "Mouth_3_0" }),
        new MorphConvertConfig("ん", new string[] { "Mouth_9_0" }),
        new MorphConvertConfig("~~", new string[] { "Mouth_44_0", "Mouth_11_0" }),
        new MorphConvertConfig("にっこり", new string[] { "Mouth_12_0" }),
        new MorphConvertConfig("口角上げ", new string[] { "Mouth_13_0" }),
        new MorphConvertConfig("□", new string[] { "Mouth_15_0" }),
        new MorphConvertConfig("▲", new string[] { "Mouth_16_0" }),
        new MorphConvertConfig("ω□", new string[] { "Mouth_17_0" }),
        new MorphConvertConfig("にやり２", new string[] { "Mouth_18_0" }),
        new MorphConvertConfig("にやり3", new string[] { "Mouth_19_0" }),
        new MorphConvertConfig("にやり", new string[] { "Mouth_1_0", "Mouth_4_0", "Mouth_22_0" }),
        new MorphConvertConfig("にぃー", new string[] { "Mouth_26_0" }),
        new MorphConvertConfig("あ", new string[] { "Mouth_5_0", "Mouth_6_0", "Mouth_7_0", "Mouth_23_0" }),
        new MorphConvertConfig("い", new string[] { "Mouth_25_0", "Mouth_35_0", "Mouth_36_0", "Mouth_8_0" }),
        new MorphConvertConfig("う", new string[] { "Mouth_28_0", "Mouth_27_0" }),
        new MorphConvertConfig("お", new string[] { "Mouth_31_0", "Mouth_10_0", "Mouth_14_0" }),
        new MorphConvertConfig("え", new string[] { "Mouth_29_0", "Mouth_30_0", "Mouth_37_0", "Mouth_33_0" }),
        new MorphConvertConfig("口角下げ", new string[] { "Mouth_41_0" }),
        new MorphConvertConfig("ぺろっ", new string[] { "Mouth_45_0" }),
        new MorphConvertConfig("てへぺろ", new string[] { "Mouth_46_0" }),
        new MorphConvertConfig("ぺろっ2", new string[] { "Mouth_47_0" }),
        new MorphConvertConfig("てへぺろ2", new string[] { "Mouth_48_0" }),
        new MorphConvertConfig("ぺろっ3", new string[] { "Mouth_49_0" }),
        new MorphConvertConfig("口上", new string[] { "Mouth_50_0" }),
        new MorphConvertConfig("口下", new string[] { "Mouth_51_0" }),
        new MorphConvertConfig("口左", new string[] { "Mouth_52_0" }),
        new MorphConvertConfig("口右", new string[] { "Mouth_53_0" }),
        new MorphConvertConfig("口横広げ", new string[] { "Mouth_54_0" }),
        new MorphConvertConfig("口横狭い", new string[] { "Mouth_55_0" })
    };

    [NonSerialized]
    public byte[] DBBaseKey = new byte[]
    {
        0xF1, 0x70, 0xCE, 0xA4, 0xDF, 0xCE, 0xA3, 0xE1,
        0xA5, 0xD8, 0xC7, 0x0B, 0xD1, 0x00, 0x00, 0x00
    };

    [NonSerialized]
    public byte[] DBKey = new byte[]
    {
        0x6D, 0x5B, 0x65, 0x33, 0x63, 0x36,
        0x63, 0x25, 0x54, 0x71, 0x2D, 0x73,
        0x50, 0x53, 0x63, 0x38, 0x6D, 0x34,
        0x37, 0x7B, 0x35, 0x63, 0x70, 0x23,
        0x37, 0x34, 0x53, 0x29, 0x73, 0x43,
        0x36, 0x33
    };

    [NonSerialized]
    public byte[] GlobalDBKey = new byte[]
    {
        0x36, 0x23, 0x6b, 0x4c, 0x2a, 0x39,
        0x21, 0x75, 0x52, 0x26, 0x32, 0x76,
        0x25, 0x50, 0x3f, 0x35, 0x5d, 0x77,
        0x58, 0x6d, 0x40, 0x71, 0x38, 0x5e,
        0x4c, 0x31, 0x28, 0x74, 0x29, 0x59,
        0x37, 0x24, 0x53
    };

    [NonSerialized]
    public byte[] ABKey = new byte[]
    {
        0x53, 0x2B, 0x46, 0x31, 0xE4, 0xA7, 0xB9, 0x47, 0x3E, 0x7C, 0xFB
    };


    public Config()
    {
        Version = Application.version;

        MainPath = $@"{Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "Low"}\Cygames\umamusume";
        if (Application.isMobilePlatform)
        {
            WorkMode = WorkMode.Standalone;
            DownloadMissingResources = true;
            MainPath = Application.persistentDataPath;
            Instance = this;
            return;
        }

        if (!File.Exists(configPath))
        {
            DBBaseKeyText = ByteArrayToHex(DBBaseKey);
            DBKeyText = ByteArrayToHex(DBKey);
            ABKeyText = ByteArrayToHex(ABKey);
            File.WriteAllText(configPath, JsonUtility.ToJson(this, true));
        }
        else
        {
            try
            {
                JsonUtility.FromJsonOverwrite(File.ReadAllText(configPath), this);

                if (string.IsNullOrEmpty(DBBaseKeyText))
                {
                    DBBaseKeyText = ByteArrayToHex(DBBaseKey);
                }
                else
                {
                    DBBaseKey = HexToByteArray(DBBaseKeyText);
                }
                if (string.IsNullOrEmpty(DBKeyText))
                {
                    DBKeyText = ByteArrayToHex(DBKey);
                }
                else
                {
                    DBKey = HexToByteArray(DBKeyText);
                }
                if (string.IsNullOrEmpty(ABKeyText))
                {
                    ABKeyText = ByteArrayToHex(ABKey);
                }
                else
                {
                    ABKey = HexToByteArray(ABKeyText);
                }
                if (string.IsNullOrEmpty(GlobalDBKeyText))
                {
                    GlobalDBKeyText = ByteArrayToHex(GlobalDBKey);
                } else
                {
                    GlobalDBKey = HexToByteArray(GlobalDBKeyText);
                }
                File.WriteAllText(configPath, JsonUtility.ToJson(this, true));
            }
            catch (Exception ex)
            {
                UmaViewerUI.Instance.ShowMessage("Config load error. Using default. " + ex.Message, UIMessageType.Error);
                MainPath = $@"{Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "Low"}\Cygames\umamusume";
            }
        }
        Instance = this;
    }

    public void UpdateConfig(bool requireRestart)
    {
        File.WriteAllText(configPath, JsonUtility.ToJson(this, true));
        if (requireRestart)
        {
            UmaViewerUI.Instance.ShowMessage("The configuration has changed. Please restart the application.", UIMessageType.Default);
        }
    }

    /// <summary>
    /// Folder a model export dialog should open in: the one the last export went to, so the dialog comes
    /// back to the same place both within a session and after a restart. Falls back to
    /// <paramref name="fallback"/> the first time (or if that folder has since been deleted).
    /// </summary>
    public string ModelSaveFolder(string fallback)
    {
        return Directory.Exists(LastModelFolder) ? LastModelFolder : fallback;
    }

    /// <summary>Same for the vmd save dialogs.</summary>
    public string MotionSaveFolder(string fallback)
    {
        return Directory.Exists(LastMotionFolder) ? LastMotionFolder : fallback;
    }

    /// <summary>Remembers where a model was exported to, for the next dialog.</summary>
    public void RememberModelSave(string filePath)
    {
        RememberFolder(ref LastModelFolder, filePath);
    }

    /// <summary>Remembers where a motion was saved, for the next dialog.</summary>
    public void RememberMotionSave(string filePath)
    {
        RememberFolder(ref LastMotionFolder, filePath);
    }

    private void RememberFolder(ref string field, string filePath)
    {
        if (string.IsNullOrEmpty(filePath)) return;
        string folder;
        try
        {
            folder = Path.GetDirectoryName(filePath);
        }
        catch (Exception)
        {
            return; // a path the runtime refuses to parse is not worth a crash on the way out of a save
        }
        if (string.IsNullOrEmpty(folder) || folder == field) return;
        field = folder;
        UpdateConfig(false);
    }

    private string ByteArrayToHex(byte[] byteArray)
    {
        if (byteArray == null) return string.Empty;

        StringBuilder sb = new StringBuilder(byteArray.Length * 2);
        foreach (byte b in byteArray)
        {
            sb.AppendFormat("{0:X2}", b);
        }
        return sb.ToString();
    }

    private byte[] HexToByteArray(string hex)
    {
        if (string.IsNullOrEmpty(hex) || hex.Length % 2 != 0)
            return new byte[0];

        byte[] result = new byte[hex.Length / 2];
        for (int i = 0; i < result.Length; i++)
        {
            string byteString = hex.Substring(i * 2, 2);
            result[i] = byte.Parse(byteString, System.Globalization.NumberStyles.HexNumber);
        }
        return result;
    }
}

public enum Language
{
    En,
    Jp
}

public enum Region
{
    Global,
    Jp
}

public enum WorkMode
{
    Default,
    Standalone
}

/// <summary>
/// How vertex morphs are named in exported .pmx models and in the vmds recorded beside them.
///
/// The values are numbered explicitly because this enum is persisted into Config.json as a number. The
/// numbering changed once: 2 used to be <c>Both</c> (tagged plus a short alias) and <c>Unified</c> was 3.
/// <c>Both</c> is gone - it wrote 432 morph entries for what <c>Unified</c> does in 192, and nothing
/// needed the tagged spelling in the same file as a motion - so <c>Unified</c> took slot 2. Every build
/// ships its own Config.json, so no migration is needed; a stale value of 3 simply lands on the
/// fail-safe in <see cref="MorphNaming.NamesFor"/>.
/// </summary>
public enum PmxMorphNameMode
{
    /// <summary>
    /// Keeps the "(Tag)[Mesh]" suffix produced for every morph (e.g. "Eye_20_R(XRange)[M_Face]").
    /// This is what the original UmaViewer exported and what the stock Blender uma_addon looks up by
    /// exact name, but it is 27-30 bytes and a vmd morph name field only holds 15, so motions cannot
    /// drive these morphs. Kept for compatibility with the stock addon.
    /// </summary>
    BlenderCompatible = 0,

    /// <summary>
    /// Short names only (e.g. "Eye_20_R"). Fits a vmd, but the name says nothing about what the morph
    /// does - which is what <see cref="Unified"/> exists to fix.
    /// </summary>
    ShortEnglish = 1,

    /// <summary>
    /// Descriptive, unique and within the vmd byte limit: english group + romaji tag + side
    /// (e.g. "Brow_WaraiA_R", "Eye_XRange_L"). One name for the model and the motion, see
    /// <see cref="MorphNaming"/>. Default.
    /// </summary>
    Unified = 2
}

[Serializable]
public class MorphConvertConfig
{
    public string MMDMorph;
    public List<string> UMAMorph;

    public MorphConvertConfig(string mmdMorph, string[] umaMorphs)
    {
        UMAMorph = new List<string>(umaMorphs);
        MMDMorph = mmdMorph;
    }
}