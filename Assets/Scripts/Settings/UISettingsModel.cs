#if !UNITY_ANDROID || UNITY_EDITOR
using SFB;
#endif

using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class UISettingsModel : MonoBehaviour
{
    static UmaViewerBuilder Builder => UmaViewerBuilder.Instance;

    [SerializeField] private Toggle _lockCharacter;
    [SerializeField] private Toggle _openWithTPose;
    [SerializeField] private Toggle _enablePhysics;
    [SerializeField] private Toggle _lookAtCamera;
    [SerializeField] private Toggle _faceOverride;
    [SerializeField] private Slider _outlineWidthSlider;

    /// <summary>
    /// Export options. All are optional: an unassigned control only means the row is not in the scene,
    /// <see cref="Config.json"/> still works and the exporter still follows it.
    /// </summary>
    [SerializeField] private Toggle _aPoseRestPose;
    [SerializeField] private Toggle _extraMorphs;
    [SerializeField] private Toggle _textureVariants;
    [SerializeField] private TMP_Dropdown _morphNameMode;

    /// <summary>
    /// Optional description label under the naming dropdown. It spells out what the selected mode does to
    /// one morph, so the dropdown itself can stay short
    /// (see <see cref="MorphNameModeLabel"/> and docs/MORPH_NAMES.md).
    /// </summary>
    [SerializeField] private TextMeshProUGUI _morphNameModeText;

    /// <summary>
    /// Optional dedicated prefab for the texture set rows in the materials panel; falls back to the
    /// container toggle prefab the character/material lists use when it is not assigned.
    /// </summary>
    [SerializeField] private UmaUIContainer _textureSetRowPrefab;

    public ScrollRect MaterialsList;

    private bool
        _isHeadFix,
        _isTPose,
        _dynamicBoneEnable = true,
        _enableEyeTracking = true,
        _enableFaceOverride = true,
        _isWetLook;

    private float _outlineWidth;

    public bool IsHeadFix
    {
        get { return _isHeadFix; }
        set { _lockCharacter.SetIsOnWithoutNotify(value); SetHeadFix(value); }
    }

    public bool IsTPose
    {
        get { return _isTPose; }
        set { _openWithTPose.SetIsOnWithoutNotify(value); SetTPose(value); }
    }

    public bool DynamicBoneEnable
    {
        get { return _dynamicBoneEnable; }
        set { _enablePhysics.SetIsOnWithoutNotify(value); SetDynamicBoneEnable(value); }
    }

    public bool EnableEyeTracking
    {
        get { return _enableEyeTracking; }
        set { _lookAtCamera.SetIsOnWithoutNotify(value); SetEyeTrackingEnable(value); }
    }

    public bool EnableFaceOverride
    {
        get { return _enableFaceOverride; }
        set { _faceOverride.SetIsOnWithoutNotify(value); SetFaceOverrideEnable(value); }
    }

    public float OutlineWidth
    {
        get { return _outlineWidth; }
        set { _outlineWidthSlider.value = value; }
    }

    public void SetHeadFix(bool value)
    {
        _isHeadFix = value;
    }

    public void SetTPose(bool value)
    {
        _isTPose = value;
    }

    public void SetDynamicBoneEnable(bool isOn)
    {
        _dynamicBoneEnable = isOn;
        Builder.CurrentUMAContainer?.SetDynamicBoneEnable(isOn);
    }

    public void SetEyeTrackingEnable(bool isOn)
    {
        _enableEyeTracking = isOn;
        Builder.CurrentUMAContainer?.SetEyeTracking(isOn);
    }

    public void SetFaceOverrideEnable(bool isOn)
    {
        _enableFaceOverride = isOn;
        Builder.CurrentUMAContainer?.SetFaceOverrideData(isOn);
    }

    /// <summary>
    /// Whether the loaded character wears the game's rain (wet) texture set. Session only, like the other
    /// view toggles here (T-pose, eye tracking, face override): the game decides this from the race's
    /// weather, so there is nothing to write to Config.json.
    /// </summary>
    public bool IsWetLook
    {
        get { return _isWetLook; }
    }

    /// <summary>Rain look on or off for the character that is loaded (bound to R in <see cref="UIShortcuts"/>).</summary>
    public void ToggleWetLook()
    {
        SetWetLook(!_isWetLook);
    }

    public void SetWetLook(bool wet)
    {
        var container = Builder.CurrentUMAContainer;
        if (container == null)
        {
            UmaViewerUI.Instance?.ShowMessage("Load a character first", UIMessageType.Warning);
            return;
        }

        // the container answers with what it could really do: a model whose parts ship no wet maps stays dry
        _isWetLook = container.SetWetLook(wet);
        UmaViewerUI.Instance?.ShowMessage(_isWetLook
            ? "Rain look on (the game's wet textures)"
            : "Rain look off", UIMessageType.Default);
    }

    public void ChangeOutlineWidth(float val)
    {
        _outlineWidth = val;
        Shader.SetGlobalFloat("_GlobalOutlineWidth", val);
    }

    /// <summary>
    /// Shows what <see cref="Config.json"/> currently says in the export controls, so a row can never
    /// disagree with what the exporter will actually do. Called from <see cref="UmaViewerUI.Start"/>;
    /// every control is optional.
    /// </summary>
    public void ApplySettings()
    {
        if (_aPoseRestPose != null) _aPoseRestPose.SetIsOnWithoutNotify(Config.Instance.PmxAPoseRestPose);
        if (_extraMorphs != null) _extraMorphs.SetIsOnWithoutNotify(Config.Instance.PmxExtraMorphs);
        if (_textureVariants != null) _textureVariants.SetIsOnWithoutNotify(Config.Instance.PmxTextureVariants);
        if (_morphNameMode != null) _morphNameMode.SetValueWithoutNotify((int)Config.Instance.PmxMorphNameMode);
        if (_morphNameModeText != null) _morphNameModeText.text = MorphNameModeLabel(Config.Instance.PmxMorphNameMode);
    }

    /// <summary>
    /// Export models with the arms in the A-pose that recorded motions are relative to, so a recorded
    /// vmd lines up without posing the model in Blender first - see <see cref="UmaAPose"/>.
    /// </summary>
    public void EnableAPoseRestPose(bool enable)
    {
        if (Config.Instance.PmxAPoseRestPose == enable) return;
        Config.Instance.PmxAPoseRestPose = enable;
        Debug.Log($"[Export] exported models will {(enable ? "use the A-pose" : "keep the T-pose")} rest pose");
        Config.Instance.UpdateConfig(false);
    }

    /// <summary>
    /// Export the facial morphs an MMD morph cannot express as extra vertex morphs instead of losing
    /// them - the cheek blush, the eye manga expressions, the tears. What each one is called, which
    /// decals stay out and what a morph cannot carry at all is written up in the
    /// <see cref="Config.PmxExtraMorphsTip"/> row of <see cref="Config.json"/> and in
    /// docs/PMX_EXTENSIONS.md.
    ///
    /// This row is the on/off test for the whole feature: the option existed in Config.json and on the
    /// command line (-umaNoExtraMorphs) before it had a control, so comparing the two exports meant
    /// editing Config.json by hand.
    /// </summary>
    public void EnablePmxExtraMorphs(bool enable)
    {
        if (Config.Instance.PmxExtraMorphs == enable) return;
        Config.Instance.PmxExtraMorphs = enable;
        Debug.Log($"[Export] advanced morphs are {(enable ? "on, decals export as vertex morphs" : "off, decals stay where the game draws them")}");
        Config.Instance.UpdateConfig(false);
    }

    /// <summary>
    /// Also export the game's wet and dirt maps beside the dry ones, named per material by `wet=`/`dirt=`
    /// tokens in the material comments. Off by default because it is the one option that roughly doubles the
    /// size of an export (measured on 1001: 13 files, +2.22 MB, +80%) and nothing in MMD reads the extra
    /// files - they are for a shader that knows what they are, which is the uma addon.
    ///
    /// The option existed in <see cref="Config.json"/> and on the command line
    /// (`-umaTextureVariants`/`-TextureVariants`) before it had a control, exactly like the advanced morphs
    /// did; the row is the on/off test for it.
    /// </summary>
    public void EnablePmxTextureVariants(bool enable)
    {
        if (Config.Instance.PmxTextureVariants == enable) return;
        Config.Instance.PmxTextureVariants = enable;
        Debug.Log($"[Export] the game's wet and dirt maps are {(enable ? "exported beside the dry ones" : "left out")}");
        Config.Instance.UpdateConfig(false);
    }

    /// <summary>
    /// Naming of morphs in exported models and recorded motions. Dropdown order matches
    /// <see cref="PmxMorphNameMode"/>: 0 tagged (Blender addon), 1 short english, 2 unified.
    /// </summary>
    public void ChangeMorphNameMode(int mode)
    {
        var wanted = (PmxMorphNameMode)Mathf.Clamp(mode, 0, (int)PmxMorphNameMode.Unified);
        if (_morphNameModeText != null) _morphNameModeText.text = MorphNameModeLabel(wanted);
        if (Config.Instance.PmxMorphNameMode == wanted) return;
        Config.Instance.PmxMorphNameMode = wanted;
        Debug.Log($"[Export] morph names are now '{wanted}' ({(int)wanted}); "
                  + "models and motions use the same spelling, so re-export models after changing this");
        Config.Instance.UpdateConfig(false);
    }

    /// <summary>
    /// What each naming mode does, spelled out on one morph - the smiling right eyebrow of a character
    /// ("EyeBrow_1_R(WaraiA)[M_Face]"). Every morph follows the same rules; docs/MORPH_NAMES.md lists all
    /// 192 of them in all three spellings.
    /// </summary>
    static string MorphNameModeLabel(PmxMorphNameMode mode)
    {
        switch (mode)
        {
            case PmxMorphNameMode.BlenderCompatible:
                return "Tagged: EyeBrow_1_R(WaraiA)[M_Face]\n"
                       + "Original UmaViewer naming schema: stock Uma Blender Addon compatible. Too long for the VMD 15-byte limit, morphs are not resolved in Blender.";
            case PmxMorphNameMode.ShortEnglish:
                return "Short english: EyeBrow_1_R. Fits a motion, but the name says nothing about what the morph does.";
            default:
                return "Unified: Brow_WaraiA_R\n"
                       + "English group, romaji tag and side, inside the 15 byte vmd limit - one descriptive "
                       + "name for the model and the motion.";
        }
    }

    /// <summary>Prefix of the generated texture set rows, so they can be cleared again.</summary>
    public const string TextureSetRowPrefix = "TextureSetRow_";

    /// <summary>
    /// Lists the texture sets of the loaded prop/scene in the materials panel.
    ///
    /// Environment materials are usually shipped without any texture: the game picks one of several
    /// texture sets at runtime (time of day, weather, event banner), see
    /// <see cref="UmaEnvTextureSet"/>. Those sets live in separate bundles, so this panel is what lets
    /// the user switch between them.
    /// </summary>
    public void LoadTextureSetPanel(UmaEnvTextureSet textureSet)
    {
        ClearTextureSetPanel();
        if (textureSet == null) return;
        if (MaterialsList == null) return;
        // nothing was resolved (or the scene has no alternative sets and needs none): stay out of the way
        if (textureSet.Variants.Count == 0 || textureSet.AppliedSlots == 0) return;

        bool switchable = textureSet.Variants.Count > 1;
        Debug.Log($"[UISettingsModel] {(switchable ? "offering" : "applied")} {textureSet.Variants.Count} texture set(s) "
                  + $"for {textureSet.name}: {string.Join(", ", textureSet.Variants)} "
                  + $"(current '{textureSet.CurrentVariant}', {textureSet.AppliedSlots} material slots)");

        foreach (string variant in textureSet.Variants)
        {
            bool isCurrent = variant == textureSet.CurrentVariant;
            var prefab = _textureSetRowPrefab != null ? _textureSetRowPrefab : UmaViewerUI.Instance.UmaContainerTogglePrefab;
            var row = Instantiate(prefab, MaterialsList.content);
            row.name = TextureSetRowPrefix + variant;
            row.Name = $"Texture set {variant}"
                       + (isCurrent ? $" - active ({textureSet.AppliedSlots} slots)" : "")
                       + (switchable ? "" : " (only set available)");
            string captured = variant;
            row.Toggle.SetIsOnWithoutNotify(isCurrent);
            if (!switchable)
            {
                // a single set was found and already applied: show it, but do not pretend it is a choice
                row.Toggle.interactable = false;
                continue;
            }
            row.Toggle.onValueChanged.AddListener(value =>
            {
                if (!value) return;
                if (!textureSet.SetVariant(captured)) return;
                LoadTextureSetPanel(textureSet);
                Debug.Log($"[UISettingsModel] texture set {captured} applied to {textureSet.name}");
            });
        }
    }

    public void ClearTextureSetPanel()
    {
        if (MaterialsList == null) return;
        for (int i = MaterialsList.content.childCount - 1; i >= 0; i--)
        {
            var child = MaterialsList.content.GetChild(i);
            if (child.name.StartsWith(TextureSetRowPrefix)) Destroy(child.gameObject);
        }
    }

    public void ExportModel()
    {
#if !UNITY_ANDROID || UNITY_EDITOR
        var container = Builder.CurrentUMAContainer;
        if (container)
        {
            var path = StandaloneFileBrowser.SaveFilePanel("Save Model File",
                Config.Instance.ModelSaveFolder(Config.Instance.MainPath),
                ExportNaming.ModelFile(container), ModelExportFilters);
            if (!string.IsNullOrEmpty(path))
            {
                ExportWithReport(() => ModelExporter.ExportModel(container, path), path);
                Config.Instance.RememberModelSave(path);
            }
        }

        var prop_container = Builder.CurrentOtherContainer;
        if (prop_container)
        {
            var path = StandaloneFileBrowser.SaveFilePanel("Save Model File",
                Config.Instance.ModelSaveFolder(Config.Instance.MainPath), $"{prop_container}",
                ModelExportFilters);
            if (!string.IsNullOrEmpty(path))
            {
                ExportWithReport(() => ModelExporter.ExportModel(prop_container, path), path);
                Config.Instance.RememberModelSave(path);
            }
        }

#else
        UmaViewerUI.Instance.ShowMessage("Not supported on this platform", UIMessageType.Warning);
#endif
    }

    /// <summary>
    /// Runs one export and puts its failure in front of the user.
    ///
    /// The exporter writes the `Texture2D/` folder before it reads any geometry, so a scene it cannot read
    /// used to leave the folder on disk, no model, and nothing on screen saying why - the exception went to
    /// the log and `Export Model` looked as if it had simply done nothing. Scenes are where that happened:
    /// their meshes are whatever the game shipped, and two of the shapes that broke the export (a mesh with
    /// no normals, a scene with no textures at all) are fixed, but a third will show up eventually, and it
    /// should name itself. The message is short on purpose - the exception with its stack is in the log,
    /// which is what a bug report needs.
    /// </summary>
    private static void ExportWithReport(System.Action export, string path)
    {
        // File.Exists alone is not the question: a file left by an earlier export makes it true, and the
        // export-failed-fix-export-again loop is exactly when the hint matters. Snapshot what is there
        // before the write and compare after, which holds whether the path was empty, stale or fresh.
        System.DateTime? before = System.IO.File.Exists(path)
            ? System.IO.File.GetLastWriteTimeUtc(path)
            : (System.DateTime?)null;
        try
        {
            export();
        }
        catch (System.Exception e)
        {
            bool wroteModel = System.IO.File.Exists(path)
                              && (before == null || System.IO.File.GetLastWriteTimeUtc(path) != before.Value);
            UnityEngine.Debug.LogError($"[UISettingsModel] export to {path} failed: {e}");
            UmaViewerUI.Instance?.ShowMessage($"Export failed ({e.GetType().Name}: {e.Message})."
                                              + (wroteModel
                                                  ? ""
                                                  : " No model was written, though its textures may be.")
                                              + " See the log for the full trace.", UIMessageType.Error);
        }
    }

    /// <summary>
    /// What the save dialog offers. The extension the name ends up with decides which writer runs
    /// (<see cref="ModelExporter.ExportModel"/>), so both formats are reachable from this one dialog.
    /// </summary>
    private static readonly ExtensionFilter[] ModelExportFilters =
    {
        new ExtensionFilter("PMX model", "pmx"),
        new ExtensionFilter("FBX model", "fbx")
    };
}
