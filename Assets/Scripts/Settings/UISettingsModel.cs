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
    /// Export options. Both are optional: an unassigned control only means the row is not in the scene,
    /// <see cref="Config.json"/> still works and the exporter still follows it.
    /// </summary>
    [SerializeField] private Toggle _aPoseRestPose;
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
        _enableFaceOverride = true;

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
    /// Naming of morphs in exported models and motions. Dropdown order matches
    /// <see cref="PmxMorphNameMode"/>: 0 tagged (Blender addon), 1 short english, 2 both, 3 unified.
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
    /// 192 of them in all four spellings.
    /// </summary>
    static string MorphNameModeLabel(PmxMorphNameMode mode)
    {
        switch (mode)
        {
            case PmxMorphNameMode.BlenderCompatible:
                return "Tagged: EyeBrow_1_R(WaraiA)[M_Face]\n"
                       + "The stock Blender addon finds these, but the name is 27 bytes and a motion can only "
                       + "hold 15 - so a recorded vmd cannot drive these morphs.";
            case PmxMorphNameMode.ShortEnglish:
                return "Short english: EyeBrow_1_R\n"
                       + "Fits a motion, but the name says nothing about what the morph does.";
            case PmxMorphNameMode.Both:
                return "Both: EyeBrow_1_R(WaraiA)[M_Face] + EyeBrow_1_R\n"
                       + "The addon stays happy and motions still land, at the cost of twice as many morphs.";
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
            var entry = container.CharaEntry;
            var path = StandaloneFileBrowser.SaveFilePanel("Save PMX File", Config.Instance.MainPath, $"{entry.Id}_{entry.GetName()}", "pmx");
            if (!string.IsNullOrEmpty(path))
            {
                ModelExporter.ExportModel(container, path);
            }
        }

        var prop_container = Builder.CurrentOtherContainer;
        if (prop_container)
        {
            var path = StandaloneFileBrowser.SaveFilePanel("Save PMX File", Config.Instance.MainPath, $"{prop_container}", "pmx");
            if (!string.IsNullOrEmpty(path))
            {
                ModelExporter.ExportModel(prop_container, path);
            }
        }

#else
        UmaViewerUI.Instance.ShowMessage("Not supported on this platform", UIMessageType.Warning);
#endif
    }
}
