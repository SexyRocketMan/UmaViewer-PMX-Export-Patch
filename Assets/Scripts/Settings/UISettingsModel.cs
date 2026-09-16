#if !UNITY_ANDROID || UNITY_EDITOR
using SFB;
#endif

using UnityEngine;
using UnityEngine.UI;

public class UISettingsModel : MonoBehaviour
{
    static UmaViewerBuilder Builder => UmaViewerBuilder.Instance;

    [SerializeField] private Toggle _lockCharacter;
    [SerializeField] private Toggle _openWithTPose;
    [SerializeField] private Toggle _enablePhysics;
    [SerializeField] private Toggle _lookAtCamera;
    [SerializeField] private Toggle _faceOverride;
    [SerializeField] private Slider _outlineWidthSlider;

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
        if (textureSet == null || textureSet.Variants.Count < 2) return;
        if (MaterialsList == null) return;

        Debug.Log($"[UISettingsModel] offering {textureSet.Variants.Count} texture sets for {textureSet.name}: "
                  + $"{string.Join(", ", textureSet.Variants)} (current '{textureSet.CurrentVariant}')");

        foreach (string variant in textureSet.Variants)
        {
            var row = Instantiate(UmaViewerUI.Instance.UmaContainerTogglePrefab, MaterialsList.content);
            row.name = TextureSetRowPrefix + variant;
            row.Name = variant == textureSet.CurrentVariant ? $"Texture set {variant} (current)" : $"Texture set {variant}";
            string captured = variant;
            row.Toggle.SetIsOnWithoutNotify(variant == textureSet.CurrentVariant);
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
