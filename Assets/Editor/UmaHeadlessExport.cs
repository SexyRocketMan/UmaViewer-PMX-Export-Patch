#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Headless (batch mode) model export, so exports can be produced and regression tested without
/// clicking through the UI.
///
/// It opens the normal viewer scene, lets it boot in play mode, then drives the same code path the
/// UI's "Export Model" button uses (<see cref="UmaViewerBuilder.LoadUma"/> +
/// <see cref="ModelExporter.ExportModel"/>).
///
/// Usage (all arguments are optional except the ones producing an export):
///
///   Unity.exe -batchmode -projectPath &lt;project&gt; -executeMethod UmaHeadlessExport.Run ^
///       -umaChar 1001 -umaCostume 00 -umaOut "D:/out/1001.pmx"
///
///   -umaChar &lt;id&gt;        character id to load (see -umaListChars)
///   -umaCostume &lt;id&gt;     costume id, e.g. "00". Defaults to the first costume of the character
///   -umaProp &lt;path&gt;      instead of a character, load and export a prop / scene asset path or name
///                        (see -umaListProps)
///   -umaMotion &lt;path&gt;    load a specific motion onto the character before exporting/recording
///                        (e.g. 3d/motion/racemain/body/type01/anm_rac_type01_run02_stride)
///   -umaOut &lt;path&gt;       .pmx file to write. Defaults to &lt;project&gt;/HeadlessExports/&lt;char&gt;_&lt;costume&gt;.pmx
///   -umaScene &lt;path&gt;     scene to boot, default Assets/Scenes/Version2.unity
///   -umaListChars        print every character id together with its costume ids, then exit
///   -umaListCostumes     print the costume ids of -umaChar, then exit
///   -umaListProps &lt;f&gt;    print prop/scene assets whose name contains &lt;f&gt; (empty = all), then exit
///   -umaScanProps &lt;f&gt;    load every prop/scene matching &lt;f&gt; in turn and report its material health
///                        (textureless slots, missing shaders, resolved texture set), then exit
///   -umaScanCount &lt;n&gt;    how many props to scan, default 10
///   -umaDumpMaterials    log material diagnostics for the loaded model before exporting
///   -umaVariant &lt;code&gt;   pick an environment texture set variant (e.g. 212 or 214) before exporting
///   -umaMorphNameMode &lt;n&gt; override Config.PmxMorphNameMode (0 tagged, 1 short, 2 both, 3 unified)
///   -umaAPose            export models with the arms in the A-pose that recorded motions are relative to
///   -umaRecordVmd &lt;path&gt; record one loop of the playing animation to a .vmd, then exit (character only)
///   -umaRecordFps &lt;n&gt;    frame rate of that recording, default 30
///   -umaRecordMode &lt;m&gt;   deterministic (default, one pinned frame per sample) or realtime (the legacy
///                        FixedUpdate sampler, useful to compare behaviour)
///   -umaTimeout &lt;sec&gt;    abort after this many seconds, default 600
///   -umaExtraFrames &lt;n&gt;  frames to let the model settle before exporting, default 30
///   -umaPlaySeconds &lt;s&gt; let the loaded motion play for this much wall clock time before exporting or
///                        recording (default 0). Batch mode burns through editor frames in milliseconds,
///                        so without this a one shot animation never reaches its end - which is the state
///                        the viewer is in when a user records after the animation finished.
///
/// Exit code is 0 on success, non zero on failure (the log line prefixed with [UmaHeadlessExport]
/// explains why).
///
/// NOTE: entering play mode triggers a script domain reload, which clears static fields and drops
/// EditorApplication.update subscriptions. All progress state therefore lives in
/// <see cref="SessionState"/> and the update loop is re-armed from an [InitializeOnLoadMethod].
/// </summary>
public static class UmaHeadlessExport
{
    private const string Tag = "[UmaHeadlessExport]";
    private const string DefaultScene = "Assets/Scenes/Version2.unity";

    // SessionState keys (survive the play mode domain reload)
    private const string KeyOptions = "UmaHeadlessExport.Options";
    private const string KeyStage = "UmaHeadlessExport.Stage";
    private const string KeyDeadline = "UmaHeadlessExport.Deadline";
    private const string KeyOutPath = "UmaHeadlessExport.OutPath";
    private const string KeyIsProp = "UmaHeadlessExport.IsProp";
    private const string KeyPropName = "UmaHeadlessExport.PropName";
    private const string KeyVmdPath = "UmaHeadlessExport.VmdPath";
    private const string KeyVmdStarted = "UmaHeadlessExport.VmdStarted";
    private const string KeyVmdDone = "UmaHeadlessExport.VmdDone";
    private const string KeyVmdFrames = "UmaHeadlessExport.VmdFrames";
    private const string KeyMotionLoaded = "UmaHeadlessExport.MotionLoaded";
    private const string KeyScanIndex = "UmaHeadlessExport.ScanIndex";

    private enum Stage
    {
        Idle = 0,
        WaitPlayMode,
        WaitStartup,
        WaitModel,
        Settle,
        RecordVmd,
        Export,
        Done,
        Screenshot
    }

    [Serializable]
    private class Options
    {
        public string ScenePath = DefaultScene;
        public int CharId = -1;
        public string CostumeId = "";
        public string OutPath = "";
        public string Prop = "";
        public string Motion = "";
        public bool ListChars;
        public bool ListCostumes;
        public bool ListProps;
        public string ListPropsFilter = "";
        public string ScanProps = "";
        public int ScanCount = 10;
        public bool DumpMaterials;
        public bool DumpFace;
        public string Variant = "";
        public int MorphNameMode = -1;
        public bool APoseRestPose;
        public bool PlainMaterials;
        public string RecordVmd = "";
        public int RecordFps = 30;
        public int RecordReduction = 0;
        public string RecordMode = "deterministic";
        public double TimeoutSeconds = 600;
        public double BootTimeoutSeconds = 120;
        public int ExtraFrames = 30;
        public double PlaySeconds = 0;
        public string Screenshot = "";
        public string ShotView = "full";
        public int ShotWidth = 0;
        public int ShotHeight = 0;
        public double ShotYaw = 0;
        // several yaws in one run: "-umaShotYaw 0,45,-45" writes one png per angle, which is what shading work
        // is judged on - the same view under different light directions
        public string ShotYaws = "";
        // azimuths for the face light, in degrees: the Nars face shader shades with _ViewDirX/_ViewDirY
        public string ShotAzimuths = "";

        // for probing which texture a term of the game's compiled shader reads: "Property=r,g,b" or
        // "Property=null", applied before the shots and undone after them
        public List<string> ShotTextures = new List<string>();

        // for probing what a shader *global* does to the face - the viewer's own global buffer carries
        // the fog and lightmap colours, so the absolute brightness of its render is scene state
        public List<string> ShotGlobals = new List<string>();

        // for probing a material float: _faceShadowAlpha is driven by the Shade_Ctrl morph rather than
        // being a material constant, so its rest value is 0 and the cheek and nose regions never show
        public List<string> ShotFloats = new List<string>();

        // freezes the model at this normalised time before the shots, because an A/B pair is only a
        // comparison if the pose is the same in both. Negative leaves the animation alone.
        public double PinPose = -1;

        // limits -umaShotTexture / -umaShotFloat / -umaShotGlobal's face work to materials whose name
        // contains this. Without it a property that exists on both the face and the body changes both, and
        // the result is not comparable with a port that only touches the face.
        public string ShotMaterial = "";
        // -1 keeps the scene's own elevation; anything else rebuilds the light direction at that height
        public double ShotLightElevation = -1.0;
        public bool ShotTransparent;
        public bool ShotOnly;

        public static Options Parse(string[] argv)
        {
            var options = new Options();
            for (int i = 0; i < argv.Length; i++)
            {
                string key = argv[i];
                string Next() => i + 1 < argv.Length ? argv[++i] : throw new Exception($"missing value for {key}");

                switch (key)
                {
                    case "-umaChar": options.CharId = int.Parse(Next()); break;
                    case "-umaCostume": options.CostumeId = Next(); break;
                    case "-umaOut": options.OutPath = Next(); break;
                    case "-umaScene": options.ScenePath = Next(); break;
                    case "-umaProp": options.Prop = Next(); break;
                    case "-umaMotion": options.Motion = Next(); break;
                    case "-umaListChars": options.ListChars = true; break;
                    case "-umaListCostumes": options.ListCostumes = true; break;
                    case "-umaListProps":
                        options.ListProps = true;
                        // the filter is optional: only consume the next argument if it is a value,
                        // not another -umaX switch
                        if (i + 1 < argv.Length && !argv[i + 1].StartsWith("-uma")) options.ListPropsFilter = argv[++i];
                        break;
                    case "-umaScanProps": options.ScanProps = Next(); break;
                    case "-umaScanCount": options.ScanCount = int.Parse(Next()); break;
                    case "-umaDumpMaterials": options.DumpMaterials = true; break;
                    case "-umaDumpFace": options.DumpFace = true; break;
                    case "-umaShotTexture": options.ShotTextures.Add(Next()); break;
                    case "-umaShotGlobal": options.ShotGlobals.Add(Next()); break;
                    case "-umaShotFloat": options.ShotFloats.Add(Next()); break;
                    case "-umaPinPose": options.PinPose = double.Parse(Next()); break;
                    case "-umaShotMaterial": options.ShotMaterial = Next(); break;
                    case "-umaVariant": options.Variant = Next(); break;
                    case "-umaMorphNameMode": options.MorphNameMode = int.Parse(Next()); break;
                    case "-umaAPose": options.APoseRestPose = true; break;
                    case "-umaPlainMaterials": options.PlainMaterials = true; break;
                    case "-umaRecordVmd": options.RecordVmd = Next(); break;
                    case "-umaRecordFps": options.RecordFps = int.Parse(Next()); break;
                    case "-umaRecordReduction": options.RecordReduction = int.Parse(Next()); break;
                    case "-umaRecordMode": options.RecordMode = Next(); break;
                    case "-umaTimeout": options.TimeoutSeconds = double.Parse(Next()); break;
                    case "-umaBootTimeout": options.BootTimeoutSeconds = double.Parse(Next()); break;
                    case "-umaExtraFrames": options.ExtraFrames = int.Parse(Next()); break;
                    case "-umaPlaySeconds": options.PlaySeconds = double.Parse(Next()); break;
                    case "-umaScreenshot": options.Screenshot = Next(); break;
                    case "-umaShotView": options.ShotView = Next().ToLowerInvariant(); break;
                    case "-umaShotWidth": options.ShotWidth = int.Parse(Next()); break;
                    case "-umaShotHeight": options.ShotHeight = int.Parse(Next()); break;
                    case "-umaShotYaw": options.ShotYaw = double.Parse(Next()); break;
                    case "-umaShotYaws": options.ShotYaws = Next(); break;
                    case "-umaShotAzimuths": options.ShotAzimuths = Next(); break;
                    case "-umaShotLightElevation": options.ShotLightElevation = double.Parse(Next()); break;
                    case "-umaShotTransparent": options.ShotTransparent = true; break;
                    case "-umaShotOnly": options.ShotOnly = true; break;
                    default: break; // ignore everything else Unity/the shell passes through
                }
            }
            return options;
        }
    }

    private static Options CurrentOptions =>
        JsonUtility.FromJson<Options>(SessionState.GetString(KeyOptions, "{}"));

    private static Stage CurrentStage
    {
        get => (Stage)SessionState.GetInt(KeyStage, (int)Stage.Idle);
        set => SessionState.SetInt(KeyStage, (int)value);
    }

    private static int _settleFramesLeft;
    private static double _playDeadline;
    private static DateTime _playModeEnteredUtc;
    private static DateTime _runStartedUtc;
    private static int _playModeRetries;

    /// <summary>Entry point for -executeMethod.</summary>
    public static void Run()
    {
        Options options;
        try
        {
            options = Options.Parse(Environment.GetCommandLineArgs());
        }
        catch (Exception ex)
        {
            Debug.LogError($"{Tag} invalid arguments: {ex.Message}");
            EditorApplication.Exit(2);
            return;
        }

        if (!options.ListChars && !options.ListCostumes && !options.ListProps
            && string.IsNullOrEmpty(options.ScanProps)
            && options.CharId < 0 && string.IsNullOrEmpty(options.Prop))
        {
            Debug.LogError($"{Tag} nothing to do: pass -umaChar <id> or -umaProp <path> (optionally -umaOut), "
                           + "or -umaListChars / -umaListCostumes / -umaListProps / -umaScanProps");
            EditorApplication.Exit(2);
            return;
        }

        if (!File.Exists(options.ScenePath))
        {
            Debug.LogError($"{Tag} scene not found: {options.ScenePath}");
            EditorApplication.Exit(2);
            return;
        }

        SessionState.SetString(KeyOptions, JsonUtility.ToJson(options));
        SessionState.SetString(KeyDeadline, DateTime.UtcNow.AddSeconds(options.TimeoutSeconds).Ticks.ToString());
        SessionState.SetString(KeyOutPath, "");
        _runStartedUtc = DateTime.UtcNow;
        _playModeRetries = 0;
        _faceDumped = false;
        CurrentStage = Stage.WaitPlayMode;

        Debug.Log($"{Tag} opening scene {options.ScenePath}");
        EditorSceneManager.OpenScene(options.ScenePath, OpenSceneMode.Single);

        Arm();
        EditorApplication.EnterPlaymode();
    }

    /// <summary>
    /// Runs after every domain reload (including the one caused by entering play mode), which is
    /// where the update subscription made by <see cref="Run"/> gets lost.
    /// </summary>
    [InitializeOnLoadMethod]
    private static void Bootstrap()
    {
        if (CurrentStage != Stage.Idle) Arm();
    }

    private static void Arm()
    {
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
    }

    private static void Tick()
    {
        var stage = CurrentStage;
        if (stage == Stage.Idle || stage == Stage.Done) return;

        var options = CurrentOptions;
        if (DateTime.UtcNow.Ticks > long.Parse(SessionState.GetString(KeyDeadline, "0")))
        {
            Fail($"timed out after {options.TimeoutSeconds}s in stage {stage}");
            return;
        }

        try
        {
            switch (stage)
            {
                case Stage.WaitPlayMode:
                    if (EditorApplication.isPlaying)
                    {
                        _playModeEnteredUtc = DateTime.UtcNow;
                        Debug.Log($"{Tag} play mode entered");
                        CurrentStage = Stage.WaitStartup;
                        break;
                    }

                    // Entering play mode is cancelled by a script recompile while the editor is starting,
                    // which otherwise leaves the run sitting here until the timeout.
                    if ((DateTime.UtcNow - _runStartedUtc).TotalSeconds > 20)
                    {
                        _playModeRetries++;
                        if (_playModeRetries > 6)
                        {
                            Fail("play mode never started; a script change while the editor was starting cancels it");
                            return;
                        }
                        Debug.Log($"{Tag} play mode has not started yet, asking again ({_playModeRetries})");
                        EditorApplication.EnterPlaymode();
                    }
                    return;

                case Stage.WaitStartup:
                {
                    var main = UmaViewerMain.Instance;
                    var builder = UmaViewerBuilder.Instance;
                    if (main == null || builder == null) return;

                    // UmaViewerMain.Start() ends by loading the shader list, but it also downloads the
                    // english translations from GitHub first - on a slow or blocked network that never
                    // completes, so fall through after a while instead of hanging forever.
                    bool fullyBooted = builder.ShaderList != null && builder.ShaderList.Count > 0;
                    bool usable = main.AbList != null && main.AbList.Count > 0;
                    double bootSeconds = (DateTime.UtcNow - _playModeEnteredUtc).TotalSeconds;

                    if (!usable || (!fullyBooted && bootSeconds < options.BootTimeoutSeconds)) return;

                    Debug.Log($"{Tag} viewer booted: {main.Characters.Count} characters, {main.AbChara.Count} chara assets, "
                              + $"{builder.ShaderList.Count} shaders"
                              + (fullyBooted ? "" : $" (after {bootSeconds:F0}s without the startup download finishing)"));

                    if (options.MorphNameMode >= 0)
                    {
                        Config.Instance.PmxMorphNameMode = (PmxMorphNameMode)options.MorphNameMode;
                        Debug.Log($"{Tag} morph naming mode overridden to {Config.Instance.PmxMorphNameMode} "
                                  + $"({(int)Config.Instance.PmxMorphNameMode})");
                    }

                    if (options.APoseRestPose)
                    {
                        Config.Instance.PmxAPoseRestPose = true;
                        Debug.Log($"{Tag} exporting models in the A-pose rest pose "
                                  + $"(upper arms rotated {UmaAPose.Degrees} degrees down)");
                    }

                    if (options.PlainMaterials)
                    {
                        Config.Instance.PmxUmaMaterialFields = false;
                        Debug.Log($"{Tag} exporting plain MMD materials: no uma shader settings in the material "
                                  + "comment, and the plain diffuse/specular/outline values");
                    }

                    if (options.ListChars)
                    {
                        PrintCharacters();
                        Succeed();
                        return;
                    }

                    if (options.ListProps)
                    {
                        PrintProps(options.ListPropsFilter);
                        Succeed();
                        return;
                    }

                    if (!string.IsNullOrEmpty(options.ScanProps))
                    {
                        int index = SessionState.GetInt(KeyScanIndex, 0);
                        var candidates = ScanCandidates(options.ScanProps, options.ScanCount);
                        if (candidates.Count == 0)
                        {
                            Fail($"no props match '{options.ScanProps}'");
                            return;
                        }
                        if (index >= candidates.Count)
                        {
                            Debug.Log($"{Tag} scanned {candidates.Count} prop(s)");
                            Succeed();
                            return;
                        }

                        var entry = candidates[index];
                        SessionState.SetInt(KeyScanIndex, index + 1);
                        builder.UnloadProp();
                        builder.LoadProp(entry);
                        var container = builder.CurrentOtherContainer;
                        Debug.Log($"{Tag} [{index + 1}/{candidates.Count}] {entry.Name}  {SummarizeMaterials(container)}");
                        return;
                    }

                    if (!string.IsNullOrEmpty(options.Prop))
                    {
                        var propEntry = FindPropEntry(options.Prop);
                        if (propEntry == null)
                        {
                            Fail($"prop '{options.Prop}' not found; use -umaListProps to search");
                            return;
                        }

                        string propExportPath = string.IsNullOrEmpty(options.OutPath)
                            ? Path.Combine(Path.GetDirectoryName(Application.dataPath), "HeadlessExports",
                                           Path.GetFileName(propEntry.Name) + ".pmx")
                            : options.OutPath;
                        propExportPath = Path.GetFullPath(propExportPath);
                        Directory.CreateDirectory(Path.GetDirectoryName(propExportPath));
                        SessionState.SetString(KeyOutPath, propExportPath);
                        SessionState.SetInt(KeyIsProp, 1);
                        SessionState.SetString(KeyPropName, propEntry.Name);

                        Debug.Log($"{Tag} loading prop {propEntry.Name}");
                        builder.UnloadProp();
                        builder.LoadProp(propEntry);
                        CurrentStage = Stage.WaitModel;
                        break;
                    }

                    var chara = FindCharacter(options.CharId);
                    if (chara == null)
                    {
                        Fail($"character {options.CharId} not found");
                        return;
                    }

                    if (options.ListCostumes)
                    {
                        PrintCostumes(chara);
                        Succeed();
                        return;
                    }

                    if (!TryResolveCostume(chara, options.CostumeId, out string costumeId, out string failure))
                    {
                        Fail(failure);
                        return;
                    }

                    string exportPath = string.IsNullOrEmpty(options.OutPath)
                        ? Path.Combine(Path.GetDirectoryName(Application.dataPath), "HeadlessExports", $"{chara.Id}_{costumeId}.pmx")
                        : options.OutPath;
                    exportPath = Path.GetFullPath(exportPath);
                    Directory.CreateDirectory(Path.GetDirectoryName(exportPath));
                    SessionState.SetString(KeyOutPath, exportPath);
                    SessionState.SetInt(KeyIsProp, 0);

                    Debug.Log($"{Tag} loading character {chara.Id} ({chara.Name}) costume {costumeId}");
                    StartLoad(chara, costumeId);
                    CurrentStage = Stage.WaitModel;
                    break;
                }

                case Stage.WaitModel:
                {
                    var container = CurrentContainer();
                    if (container == null) return;
                    _settleFramesLeft = options.ExtraFrames;
                    CurrentStage = Stage.Settle;
                    break;
                }

                case Stage.Settle:
                    if (_settleFramesLeft-- > 0) return;

                    if (!string.IsNullOrEmpty(options.Motion) && SessionState.GetInt(KeyMotionLoaded, 0) == 0)
                    {
                        var target = CurrentContainer() as UmaContainerCharacter;
                        if (target == null)
                        {
                            Fail("-umaMotion needs a character");
                            return;
                        }
                        var main = UmaViewerMain.Instance;
                        if (!main.AbList.TryGetValue(options.Motion, out var motionEntry))
                        {
                            motionEntry = main.AbMotions.FirstOrDefault(e =>
                                e.Name.IndexOf(options.Motion, StringComparison.OrdinalIgnoreCase) >= 0);
                        }
                        if (motionEntry == null)
                        {
                            Fail($"motion '{options.Motion}' not found; try a substring of the asset path");
                            return;
                        }
                        Debug.Log($"{Tag} loading motion {motionEntry.Name}");
                        target.LoadAnimation(motionEntry);
                        Debug.Log($"{Tag} now playing '{CurrentClip(target.UmaAnimator)?.name}'");
                        SessionState.SetInt(KeyMotionLoaded, 1);
                        _settleFramesLeft = options.ExtraFrames; // let the new clip settle before sampling
                        // batch mode runs editor updates back to back, so frames alone are no time at all:
                        // PlaySeconds is what lets a one shot animation actually reach its end
                        _playDeadline = EditorApplication.timeSinceStartup + options.PlaySeconds;
                        return;
                    }

                    if (options.PlaySeconds > 0 && EditorApplication.timeSinceStartup < _playDeadline)
                    {
                        return;
                    }

                    CurrentStage = !string.IsNullOrEmpty(options.Screenshot)
                        ? Stage.Screenshot
                        : (string.IsNullOrEmpty(options.RecordVmd) ? Stage.Export : Stage.RecordVmd);
                    break;

                case Stage.Screenshot:
                {
                    // the game's own render of the loaded character, for comparing a Blender shading setup
                    // against what the game actually looks like. Several yaws in one run give the same view
                    // under different light directions, which is what shading work is judged on.
                    string shotPath = Path.GetFullPath(options.Screenshot);
                    Directory.CreateDirectory(Path.GetDirectoryName(shotPath));
                    var container = CurrentContainer();
                    if (options.PinPose >= 0.0 && container != null) PinContainerPose(container, options.PinPose);
                    List<ShotTextureOverride> overrides = container == null
                        ? new List<ShotTextureOverride>()
                        : ApplyShotTextureOverrides(container.gameObject, options.ShotTextures,
                                                    options.ShotMaterial);
                    List<GlobalOverride> globals = ApplyShotGlobalOverrides(options.ShotGlobals);
                    List<FloatOverride> floats = container == null
                        ? new List<FloatOverride>()
                        : ApplyShotFloatOverrides(container.gameObject, options.ShotFloats,
                                                  options.ShotMaterial);
                    double[] yaws = ParseShotYaws(options.ShotYaws, options.ShotYaw);
                    // the viewer's face shading reads its light direction from the materials' _ViewDirX/_ViewDirY,
                    // so a grid steps those instead of moving a scene light
                    double[] azimuths = ParseShotYaws(options.ShotAzimuths, 0.0);
                    string baseName = Path.GetFileNameWithoutExtension(shotPath);
                    string extension = Path.GetExtension(shotPath);
                    string folder = Path.GetDirectoryName(shotPath);
                    foreach (double azimuth in azimuths)
                    {
                        if (azimuths.Length > 1)
                            ApplyLightAzimuth(azimuth, options.ShotLightElevation);
                        foreach (double yaw in yaws)
                        {
                            bool grid = yaws.Length * azimuths.Length > 1;
                            string name = grid
                                ? $"{baseName}_az{azimuth:+0;-0;0}_yaw{yaw:+0;-0;0}{extension}"
                                : (yaws.Length == 1 ? shotPath
                                                    : Path.Combine(folder, $"{baseName}_yaw{yaw:+0;-0;0}{extension}"));
                            string path = grid ? Path.Combine(folder, name) : name;
                            options.ShotYaw = yaw;
                            string failure = CaptureGameScreenshot(options, path);
                            if (failure != null)
                            {
                                Fail(failure);
                                return;
                            }
                            Debug.Log($"{Tag} wrote {path} ({options.ShotView} view, {yaw:+0;-0;0} degrees"
                                      + (azimuths.Length > 1 ? $", light {azimuth:+0;-0;0}" : "")
                                      + (options.ShotTransparent ? ", transparent" : "") + ")");
                        }
                    }
                    RestoreShotTextureOverrides(overrides);
                    RestoreShotGlobalOverrides(globals);
                    RestoreShotFloatOverrides(floats);
                    if (options.ShotOnly)
                    {
                        Succeed();
                        return;
                    }
                    CurrentStage = string.IsNullOrEmpty(options.RecordVmd) ? Stage.Export : Stage.RecordVmd;
                    break;
                }

                case Stage.RecordVmd:
                {
                    var container = CurrentContainer();
                    if (container == null)
                    {
                        Fail("model container disappeared before recording");
                        return;
                    }
                    if (!(container is UmaContainerCharacter character) || character.UmaAnimator == null)
                    {
                        Fail("-umaRecordVmd needs a character with an animator");
                        return;
                    }

                    var positionBone = container.transform.Find("Position");
                    if (positionBone == null)
                    {
                        Fail("character has no 'Position' bone to attach the recorder to");
                        return;
                    }

                    var recorder = positionBone.GetComponent<UnityHumanoidVMDRecorder>();
                    if (recorder == null)
                    {
                        recorder = positionBone.gameObject.AddComponent<UnityHumanoidVMDRecorder>();
                        recorder.KeyReductionLevel = Config.Instance.VmdKeyReductionLevel;
                        recorder.Initialize();
                    }

                    string vmdPath = Path.GetFullPath(options.RecordVmd);
                    Directory.CreateDirectory(Path.GetDirectoryName(vmdPath));
                    SessionState.SetString(KeyVmdPath, vmdPath);

                    if (SessionState.GetInt(KeyVmdStarted, 0) == 0)
                    {
                        var clip = CurrentClip(character.UmaAnimator);
                        if (clip == null)
                        {
                            Fail("no animation clip is playing on the character");
                            return;
                        }
                        SessionState.SetInt(KeyVmdStarted, 1);
                        Debug.Log($"{Tag} recording one loop of '{clip.name}' ({clip.length:F3}s) at {options.RecordFps}fps "
                                  + $"in {options.RecordMode} mode; the save dialog would suggest "
                                  + $"'{ExportNaming.MotionFile(character, clip)}.vmd'");

                        if (options.RecordMode == "realtime")
                        {
                            // the legacy path: let the animator play normally and let FixedUpdate sample
                            int frames = Mathf.Max(1, Mathf.RoundToInt(clip.length * options.RecordFps));
                            SessionState.SetInt(KeyVmdFrames, frames);
                            Time.fixedDeltaTime = 1f / options.RecordFps;
                            character.UmaAnimator.speed = 1f;
                            recorder.StartRecording();
                        }
                        else
                        {
                            recorder.StartCoroutine(recorder.RecordCurrentLoop(clip, options.RecordFps,
                                () => SessionState.SetInt(KeyVmdDone, 1)));
                        }
                        return;
                    }

                    if (options.RecordMode == "realtime")
                    {
                        int wanted = SessionState.GetInt(KeyVmdFrames, 30) + 1;
                        if (recorder.FrameNumber < wanted) return; // still sampling
                        recorder.StopRecording();
                        SessionState.SetInt(KeyVmdDone, 1);
                    }

                    if (SessionState.GetInt(KeyVmdDone, 0) == 0) return; // still recording

                    // The component defaults to 2 (its interactive default); recordings from here should be
                    // full fidelity unless asked otherwise, which is also what Config.VmdKeyReductionLevel
                    // means in the UI.
                    recorder.KeyReductionLevel = options.RecordReduction > 0
                        ? options.RecordReduction
                        : Mathf.Max(1, Config.Instance.VmdKeyReductionLevel);
                    recorder.SaveVMD(container.name, vmdPath);
                    if (!File.Exists(vmdPath) || new FileInfo(vmdPath).Length == 0)
                    {
                        Fail($"recording did not write {vmdPath}");
                        return;
                    }
                    Debug.Log($"{Tag} OK {vmdPath} ({new FileInfo(vmdPath).Length} bytes)");
                    Succeed();
                    return;
                }

                case Stage.Export:
                {
                    var container = CurrentContainer();
                    if (container == null)
                    {
                        Fail("model container disappeared before export");
                        return;
                    }
                    string exportPath = SessionState.GetString(KeyOutPath, "");

                    if (!string.IsNullOrEmpty(options.Variant) && container is UmaContainerProp propContainer
                        && propContainer.TextureSet != null)
                    {
                        // drive the actual UI row when it exists, so this also covers the dropdown path
                        bool appliedViaUi = TryClickTextureSetRow(options.Variant, out string rowName);
                        bool applied = appliedViaUi || propContainer.TextureSet.SetVariant(options.Variant);
                        Debug.Log($"{Tag} texture set variant '{options.Variant}' "
                                  + (applied
                                      ? $"applied via {(appliedViaUi ? $"UI row '{rowName}'" : "API")} "
                                        + $"(current {propContainer.TextureSet.CurrentVariant})"
                                      : $"not available, current is '{propContainer.TextureSet.CurrentVariant}'"));
                    }

                    if (options.DumpFace || options.DumpMaterials)
                    {
                        string stem = SessionState.GetInt(KeyIsProp, 0) == 1
                            ? SceneStem(SessionState.GetString(KeyPropName, ""))
                            : "";
                        if (options.DumpFace) DumpFacePipeline(container.gameObject, stem);
                        if (options.DumpMaterials)
                        {
                            DumpMaterials(container.gameObject, stem);
                            DumpShaderProperties(container.gameObject, stem);
                        }
                    }

                    Debug.Log($"{Tag} exporting to {exportPath}");
                    // the exporter records what it writes, so the pmx can be compared against the game's
                    // own data vertex by vertex rather than by matching positions after the fact
                    ModelExporter.VertexDumpPath = options.DumpFace ? exportPath + ".vertices.txt" : null;
                    if (container is UmaContainerCharacter character)
                    {
                        Debug.Log($"{Tag} the save dialog would suggest '{ExportNaming.ModelFile(character)}.pmx'");
                        ModelExporter.ExportModel(character, exportPath);
                    }
                    else
                    {
                        ModelExporter.ExportModel(container, exportPath);
                    }
                    if (!File.Exists(exportPath) || new FileInfo(exportPath).Length == 0)
                    {
                        Fail($"export did not write {exportPath}");
                        return;
                    }
                    Debug.Log($"{Tag} OK {exportPath} ({new FileInfo(exportPath).Length} bytes)");
                    Succeed();
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            Fail($"{ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
        }
    }

    /// <summary>
    /// Sets a texture set row of the materials panel to on, which is exactly what clicking the
    /// dropdown/toggle does. Returns true when a matching row was found.
    /// </summary>
    private static bool TryClickTextureSetRow(string variant, out string rowName)
    {
        rowName = null;
        var list = UmaViewerUI.Instance != null && UmaViewerUI.Instance.ModelSettings != null
            ? UmaViewerUI.Instance.ModelSettings.MaterialsList
            : null;
        if (list == null) return false;

        string expected = UISettingsModel.TextureSetRowPrefix + variant;
        foreach (Transform child in list.content)
        {
            if (child.name != expected) continue;
            var toggle = child.GetComponentInChildren<UnityEngine.UI.Toggle>(true);
            if (toggle == null) continue;
            rowName = child.name;
            toggle.isOn = true; // fires the panel's onValueChanged listener
            return true;
        }
        return false;
    }

    /// <summary>The clip currently playing on the animator (first non-empty layer).</summary>
    private static AnimationClip CurrentClip(Animator animator)
    {
        if (animator == null) return null;
        for (int layer = 0; layer < animator.layerCount; layer++)
        {
            var clips = animator.GetCurrentAnimatorClipInfo(layer);
            if (clips != null && clips.Length > 0 && clips[0].clip != null) return clips[0].clip;
        }
        return null;
    }

    /// <summary>
    /// A character entry by id, taking it from the already built list when the viewer finished its
    /// startup, and otherwise building the minimal entry from the database so a headless run does not
    /// depend on the startup download completing.
    /// </summary>
    private static CharaEntry FindCharacter(int id)
    {
        var main = UmaViewerMain.Instance;
        var known = main.Characters.FirstOrDefault(c => c.Id == id);
        if (known != null) return known;

        var row = UmaDatabaseController.Instance?.CharaData?.FirstOrDefault(item => Convert.ToInt32(item["id"]) == id);
        if (row == null) return null;
        return new CharaEntry { Id = id, Name = row["charaname"].ToString(), EnName = "" };
    }

    /// <summary>Props/scenes to scan, most useful ones first, capped at <paramref name="count"/>.</summary>
    private static List<UmaDatabaseEntry> ScanCandidates(string filter, int count)
    {
        return UmaViewerMain.Instance.AbList.Values
            .Where(e => e.Name.StartsWith("3d/env", StringComparison.OrdinalIgnoreCase))
            .Where(e => Path.GetFileName(e.Name).StartsWith("pfb_", StringComparison.OrdinalIgnoreCase))
            .Where(e => e.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
            .Where(e => File.Exists(e.Path)) // do not spend the scan on assets that were never downloaded
            .OrderBy(e => e.Name)
            .Take(Mathf.Max(1, count))
            .ToList();
    }

    /// <summary>One line describing whether a loaded prop/scene will render with its textures.</summary>
    private static string SummarizeMaterials(UmaContainer container)
    {
        if (container == null) return "!! no container";
        var renderers = container.GetComponentsInChildren<Renderer>(true);
        int slots = 0, textureless = 0, noMainTexProperty = 0, missingShader = 0;
        var shaders = new HashSet<string>();
        foreach (var renderer in renderers)
        {
            foreach (var material in renderer.sharedMaterials)
            {
                slots++;
                if (material == null) { textureless++; continue; }
                string shaderName = material.shader != null ? material.shader.name : "<none>";
                shaders.Add(shaderName);
                if (material.shader == null || shaderName == "Hidden/InternalErrorShader") { missingShader++; continue; }
                if (!material.HasProperty("_MainTex")) { noMainTexProperty++; continue; }
                if (material.GetTexture("_MainTex") == null) textureless++;
            }
        }

        var textureSet = (container as UmaContainerProp)?.TextureSet;
        string resolved = textureSet != null && textureSet.Variants.Count > 0
            ? $"textureSet={textureSet.CurrentVariant} of [{string.Join(",", textureSet.Variants)}] fixed={textureSet.AppliedSlots}"
            : "textureSet=none";
        return $"renderers={renderers.Length} slots={slots} textureless={textureless} "
               + $"noMainTexProp={noMainTexProperty} missingShader={missingShader} shaders={shaders.Count} {resolved}";
    }

    /// <summary>
    /// Points the character's materials at a light azimuth, for the grid: the Nars face shader shades with its
    /// view-rotated light direction, and _ViewDirX/_ViewDirY are what rotate it. Half a turn either way covers
    /// the face from both sides.
    /// </summary>
    private static Quaternion s_lightRestRotation;
    private static bool s_lightRestCaptured;
    private static double s_lightElevation = -1.0;
    private static float s_lightRestYaw = 0f;

    private static void ApplyLightAzimuth(double azimuth, double elevation)
    {
        s_lightElevation = elevation;
        // The scene's directional light is what the character's shading follows, so that is what turns. The
        // first call remembers where it started, and every azimuth is measured from there - so azimuth 0 gives
        // back exactly the viewer's own default lighting.
        int turned = 0;
        foreach (Light light in UnityEngine.Object.FindObjectsByType<Light>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (light.type != LightType.Directional) continue;
            if (!s_lightRestCaptured)
            {
                s_lightRestRotation = light.transform.rotation;
                // the scene's own azimuth is the zero: it is the direction the viewer lights the character from,
                // and rebuilding the direction without it put the sun behind the model
                s_lightRestYaw = light.transform.eulerAngles.y;
                s_lightRestCaptured = true;
            }
            light.transform.rotation = s_lightElevation >= 0.0
                ? Quaternion.Euler((float)s_lightElevation, s_lightRestYaw + (float)azimuth, 0f)
                : Quaternion.Euler(0f, (float)azimuth, 0f) * s_lightRestRotation;
            turned++;
            Debug.Log($"{Tag} turned the directional light '{light.name}' to azimuth {azimuth:+0;-0;0} degrees "
                      + $"(elevation {light.transform.eulerAngles.x:F0}, at {light.transform.eulerAngles.y:F0})"
                      // the vector itself, so the Blender tool can be given the same light: Unity's directional
                      // light travels along its own forward
                      + $", travelling along ({light.transform.forward.x:F4}, "
                      + $"{light.transform.forward.y:F4}, {light.transform.forward.z:F4})"
                      // the colour and intensity too: the game's pixel program multiplies the diffuse by a
                      // global that carries the light colour, so a warm scene light tints the face warm
                      + $", colour ({light.color.r:F3}, {light.color.g:F3}, {light.color.b:F3}) "
                      + $"intensity {light.intensity:F3}");
        }

        // and the Nars face shader's own control, for a model that does use it
        int applied = 0;
        foreach (Renderer renderer in UnityEngine.Object.FindObjectsByType<Renderer>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            foreach (Material material in renderer.materials)
            {
                if (material == null || !material.HasProperty("_ViewDirX")) continue;
                material.SetFloat("_ViewDirX", (float)azimuth);
                applied++;
            }
        }
        if (turned == 0)
            Debug.LogWarning($"{Tag} no directional light in the scene to turn; _ViewDirX set on {applied} "
                             + "material(s) instead");
        else if (applied > 0)
            Debug.Log($"{Tag} and _ViewDirX on {applied} material(s)");
    }

    /// <summary>
    /// The camera angles to shoot: the comma separated list when one was given, otherwise the single -umaShotYaw
    /// value. One angle writes the file as named, several write "&lt;name&gt;_yaw+45.png" and so on.
    /// </summary>
    private static double[] ParseShotYaws(string list, double single)
    {
        if (string.IsNullOrWhiteSpace(list)) return new[] { single };
        var parsed = new List<double>();
        foreach (string part in list.Split(','))
        {
            string trimmed = part.Trim();
            if (trimmed.Length == 0) continue;
            if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                parsed.Add(value);
            else
                Debug.Log($"{Tag} ignoring unreadable camera angle '{trimmed}'");
        }
        return parsed.Count > 0 ? parsed.ToArray() : new[] { single };
    }

    /// <summary>
    /// Renders the game's own view of the loaded character to a PNG, so a Blender shading setup can be
    /// compared against what the game actually looks like. The camera is moved for the shot - the game
    /// keeps its post processing, so this is the tone mapped result, not a raw buffer - and put back
    /// afterwards, which leaves the export path untouched.
    ///
    /// Returns null on success, or a message describing what was missing.
    /// </summary>
    private static string CaptureGameScreenshot(Options options, string path)
    {
        var builder = UmaViewerBuilder.Instance;
        var container = CurrentContainer();
        if (container == null) return "no model loaded to screenshot";

        var animationCamera = builder != null ? builder.AnimationCamera : null;
        Camera camera = animationCamera != null && animationCamera.isActiveAndEnabled ? animationCamera : Camera.main;
        if (camera == null) return "the viewer scene has no active camera";

        Bounds bounds = new Bounds(container.transform.position, Vector3.zero);
        bool any = false;
        foreach (var renderer in container.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null || !renderer.enabled) continue;
            if (!any) { bounds = renderer.bounds; any = true; }
            else bounds.Encapsulate(renderer.bounds);
        }
        if (!any) return "the loaded model has no renderers";

        var character = container as UmaContainerCharacter;
        Transform head = character != null && character.HeadBone != null ? character.HeadBone.transform : null;
        // the game's bone axes point where the character looks, so the head's own forward is the way the face
        // is turned - the same axis the exporter uses to aim the eye bones
        Vector3 facing = head != null ? head.forward : container.transform.forward;
        facing.y = 0f;
        if (facing.sqrMagnitude < 1e-6f) facing = Vector3.forward;
        facing.Normalize();

        float height = Mathf.Max(bounds.size.y, 0.01f);
        Vector3 headPoint = head != null
            ? head.position
            : new Vector3(bounds.center.x, bounds.max.y - height * 0.08f, bounds.center.z);
        Vector3 target;
        float distance;
        switch (options.ShotView)
        {
            case "face":
                target = headPoint;
                distance = height * 0.15f;      // the head nearly fills the frame, for comparing shading
                break;
            case "head":
                target = headPoint;
                distance = height * 0.28f;
                break;
            case "upper":
                target = new Vector3(headPoint.x, bounds.min.y + height * 0.82f, headPoint.z);
                distance = height * 0.8f;
                break;
            default:
                target = bounds.center;
                distance = height * 1.9f;
                break;
        }
        Debug.Log($"{Tag} screenshot view '{options.ShotView}': bounds {bounds.size} centre {bounds.center} "
                  + $"head {headPoint} facing {facing} target {target} distance {distance:F3}");

        Vector3 direction = Quaternion.AngleAxis((float)options.ShotYaw, Vector3.up) * facing;
        Transform camTransform = camera.transform;
        Vector3 oldPosition = camTransform.position;
        Quaternion oldRotation = camTransform.rotation;
        float oldFov = camera.fieldOfView;
        try
        {
            camTransform.position = target + direction * distance;
            camTransform.rotation = Quaternion.LookRotation(-direction, Vector3.up);
            camera.fieldOfView = 39.6f;     // what a 50 mm lens on a 36 mm sensor sees, like the Blender tool
            camera.ResetProjectionMatrix();

            var image = Screenshot.GrabFrame(camera, options.ShotWidth, options.ShotHeight,
                                             options.ShotTransparent);
            File.WriteAllBytes(path, ImageConversion.EncodeToPNG(image));
            UnityEngine.Object.Destroy(image);
        }
        finally
        {
            camTransform.position = oldPosition;
            camTransform.rotation = oldRotation;
            camera.fieldOfView = oldFov;
            camera.ResetProjectionMatrix();
        }
        return null;
    }

    /// <summary>The container the current run is operating on (character or prop/scene).</summary>
    private static UmaContainer CurrentContainer()
    {
        var builder = UmaViewerBuilder.Instance;
        if (builder == null) return null;
        return SessionState.GetInt(KeyIsProp, 0) == 1 ? (UmaContainer)builder.CurrentOtherContainer : builder.CurrentUMAContainer;
    }

    /// <summary>
    /// Finds a prop/scene entry by exact asset path, or by a case insensitive substring of the path
    /// or of the file name.
    /// </summary>
    private static UmaDatabaseEntry FindPropEntry(string query)
    {
        var candidates = UmaViewerMain.Instance.AbList.Values.ToList();

        var exact = candidates.FirstOrDefault(e => string.Equals(e.Name, query, StringComparison.OrdinalIgnoreCase));
        if (exact != null) return exact;

        var matches = candidates.Where(e => e.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        if (matches.Count > 1)
        {
            Debug.LogWarning($"{Tag} '{query}' is ambiguous ({matches.Count} matches), using {matches[0].Name}; "
                             + $"others: {string.Join(", ", matches.Skip(1).Take(8).Select(m => m.Name))}");
        }
        return matches.FirstOrDefault();
    }

    private static void PrintProps(string filter)
    {
        var props = UmaViewerMain.Instance.AbList.Values
            .Where(e => e.Name.StartsWith("3d/env") && Path.GetFileName(e.Name).StartsWith("pfb_")
                        || (e.Name.StartsWith("cutt/cutt_son") && Path.GetFileName(e.Name).StartsWith("cutt_son")))
            .Where(e => string.IsNullOrEmpty(filter) || e.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
            .OrderBy(e => e.Name)
            .ToList();
        Debug.Log($"{Tag} props/scenes ({props.Count} matching '{filter}'):");
        foreach (var entry in props.Take(2000)) Debug.Log($"{Tag}   {entry.Name}");
        if (props.Count > 2000) Debug.Log($"{Tag}   ... and {props.Count - 2000} more");
    }

    /// <summary>
    /// Every property of every shader the model uses, with the value it has on the first material seen.
    /// This is how the parameters worth carrying into an export were identified (the ones the uma shader
    /// actually drives), and it is how a game update that renames them will be noticed.
    /// </summary>
    private static void DumpShaderProperties(GameObject root, string stem)
    {
        var seen = new HashSet<string>();
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            foreach (var material in renderer.sharedMaterials)
            {
                if (material == null || material.shader == null) continue;
                var shader = material.shader;
                if (!seen.Add(shader.name)) continue;

                Debug.Log($"{Tag} SHADER '{shader.name}' ({shader.GetPropertyCount()} properties), "
                          + $"values from material '{material.name}'");
                for (int i = 0; i < shader.GetPropertyCount(); i++)
                {
                    string name = shader.GetPropertyName(i);
                    var type = shader.GetPropertyType(i);
                    string value;
                    switch (type)
                    {
                        case ShaderPropertyType.Float:
                        case ShaderPropertyType.Range:
                            value = material.GetFloat(name).ToString("F4");
                            break;
                        case ShaderPropertyType.Color:
                            var colour = material.GetColor(name);
                            value = $"({colour.r:F3},{colour.g:F3},{colour.b:F3},{colour.a:F3})";
                            break;
                        case ShaderPropertyType.Vector:
                            var vector = material.GetVector(name);
                            value = $"({vector.x:F3},{vector.y:F3},{vector.z:F3},{vector.w:F3})";
                            break;
                        case ShaderPropertyType.Texture:
                            var texture = material.GetTexture(name);
                            value = texture != null ? texture.name : "<none>";
                            break;
                        default:
                            value = material.GetInt(name).ToString();
                            break;
                    }
                    Debug.Log($"{Tag}   SHADERPROP {name} | {type} | {value}");
                }
            }
        }
    }

    /// <summary>
    /// Reports what the loaded model's materials actually resolved to. Props and scenes get no
    /// material post-processing at all in UmaContainerProp, so materials the game assigns at runtime
    /// (weather/banner texture sets) stay empty and render flat white.
    /// </summary>
    /// <summary>
    /// Freezes every animator under the container at a normalised time. Two shots of the same model are only
    /// comparable if the pose matches, and this viewer's model animates: a baseline against a perturbed
    /// render otherwise shows the pose moving as well as whatever was perturbed.
    /// </summary>
    private static void PinContainerPose(UmaContainer container, double normalisedTime)
    {
        int pinned = 0;
        foreach (var animator in container.GetComponentsInChildren<Animator>(true))
        {
            if (animator.runtimeAnimatorController == null) continue;
            animator.speed = 0f;
            animator.Play(0, 0, (float)normalisedTime);
            animator.Update(0f);
            pinned++;
        }
        Debug.Log($"{Tag} -umaPinPose: froze {pinned} animator(s) at normalised time {normalisedTime:g}");
    }

    private class FloatOverride
    {
        public Material Material;
        public string Property;
        public float Original;
    }

    /// <summary>
    /// Sets named material floats for the duration of a shot. `_faceShadowAlpha` is the reason this exists:
    /// it is a *driven* property of the game's facial driven-key system (`Gallop/FaceDrivenKeyTarget` binds it
    /// to a `Shade_Ctrl` morph), so its value in the material is only its rest value of 0, and the cheek and
    /// nose regions are inert. Forcing it is how to see what those regions do.
    /// </summary>
    private static List<FloatOverride> ApplyShotFloatOverrides(GameObject root, List<string> specs,
                                                              string only = "")
    {
        var applied = new List<FloatOverride>();
        if (specs == null || specs.Count == 0) return applied;

        foreach (string raw in specs)
        {
            string spec = raw.Trim().Trim('\'', '"');
            int equals = spec.IndexOf('=');
            if (equals < 0)
            {
                Debug.LogWarning($"{Tag} ignoring -umaShotFloat '{spec}': expected Property=value");
                continue;
            }
            string property = spec.Substring(0, equals).Trim().Trim('\'', '"');
            string value = spec.Substring(equals + 1).Trim().Trim('\'', '"');
            if (!float.TryParse(value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float number))
            {
                Debug.LogWarning($"{Tag} ignoring -umaShotFloat '{spec}': bad number");
                continue;
            }

            int count = 0;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                foreach (var material in renderer.materials)
                {
                    if (material == null || !material.HasProperty(property)) continue;
                    if (only.Length > 0 && !material.name.ToLowerInvariant().Contains(only.ToLowerInvariant()))
                        continue;
                    applied.Add(new FloatOverride
                    {
                        Material = material,
                        Property = property,
                        Original = material.GetFloat(property)
                    });
                    material.SetFloat(property, number);
                    count++;
                }
            }
            Debug.Log($"{Tag} -umaShotFloat: '{property}' set to {number} on {count} material slot(s)");
        }
        return applied;
    }

    private static void RestoreShotFloatOverrides(List<FloatOverride> applied)
    {
        foreach (var entry in applied)
        {
            if (entry.Material != null) entry.Material.SetFloat(entry.Property, entry.Original);
        }
        applied.Clear();
    }

    private class GlobalOverride
    {
        public string Name;
        public bool IsColour;
        public Color Colour;
        public float Value;
    }

    /// <summary>
    /// Sets named shader globals for the duration of a shot. The face program ends by blending its colour
    /// toward a global by a per-vertex factor, and that global lives in the viewer's own buffer
    /// (UmaViewerGlobalShader), so overriding it is how to tell whether the viewer's render is fogged or
    /// lightmapped in a way the game's material knows nothing about.
    /// </summary>
    private static List<GlobalOverride> ApplyShotGlobalOverrides(List<string> specs)
    {
        var applied = new List<GlobalOverride>();
        if (specs == null || specs.Count == 0) return applied;

        foreach (string raw in specs)
        {
            string spec = raw.Trim().Trim('\'', '"');
            int equals = spec.IndexOf('=');
            if (equals < 0)
            {
                Debug.LogWarning($"{Tag} ignoring -umaShotGlobal '{spec}': expected Name=value");
                continue;
            }
            string name = spec.Substring(0, equals).Trim();
            string value = spec.Substring(equals + 1).Trim().Trim('\'', '"');
            string[] parts = value.Split(',');

            if (parts.Length >= 2)
            {
                var components = new float[4] { 0f, 0f, 0f, 1f };
                bool parsed = true;
                for (int i = 0; parsed && i < parts.Length && i < 4; i++)
                {
                    parsed = float.TryParse(parts[i].Trim(), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out components[i]);
                }
                if (!parsed)
                {
                    Debug.LogWarning($"{Tag} ignoring -umaShotGlobal '{spec}': bad colour");
                    continue;
                }
                var colour = new Color(components[0], components[1], components[2], components[3]);
                Color previous = Shader.GetGlobalColor(name);
                applied.Add(new GlobalOverride
                {
                    Name = name, IsColour = true, Colour = previous, Value = 0f
                });
                Shader.SetGlobalColor(name, colour);
                Debug.Log($"{Tag} -umaShotGlobal: '{name}' colour set to "
                          + $"{colour.r:F3},{colour.g:F3},{colour.b:F3},{colour.a:F3}"
                          + $" (was {previous.r:F3},{previous.g:F3},{previous.b:F3},{previous.a:F3})");
            }
            else
            {
                if (!float.TryParse(value, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float number))
                {
                    Debug.LogWarning($"{Tag} ignoring -umaShotGlobal '{spec}': bad number");
                    continue;
                }
                float original = Shader.GetGlobalFloat(name);
                applied.Add(new GlobalOverride { Name = name, IsColour = false, Value = original });
                Shader.SetGlobalFloat(name, number);
                Debug.Log($"{Tag} -umaShotGlobal: '{name}' float set to {number} (was {original})");
            }
        }
        return applied;
    }

    private static void RestoreShotGlobalOverrides(List<GlobalOverride> applied)
    {
        foreach (var entry in applied)
        {
            if (entry.IsColour) Shader.SetGlobalColor(entry.Name, entry.Colour);
            else Shader.SetGlobalFloat(entry.Name, entry.Value);
        }
        applied.Clear();
    }

    private class ShotTextureOverride
    {
        public Material Material;
        public string Property;
        public Texture Original;
    }

    /// <summary>
    /// Replaces named texture properties with a solid colour, so a term of the game's compiled shader can be
    /// asked what it reads: hand the term a colour it could not otherwise produce and see whether it turns up
    /// in the render. This is the only way to name a texture register - the shipped containers have no RDEF
    /// chunk, and the per-container records in the blob region are not in layout order.
    /// </summary>
    private static List<ShotTextureOverride> ApplyShotTextureOverrides(GameObject root, List<string> specs,
                                                                     string only = "")
    {
        var applied = new List<ShotTextureOverride>();
        if (specs == null || specs.Count == 0) return applied;

        foreach (string raw in specs)
        {
            // quoted forms turn up when a caller passes an array through a shell, so strip them rather
            // than throw on them
            string spec = raw.Trim().Trim('\'', '"');
            int equals = spec.IndexOf('=');
            if (equals < 0)
            {
                Debug.LogWarning($"{Tag} ignoring -umaShotTexture '{spec}': expected Property=r,g,b or Property=null");
                continue;
            }
            string property = spec.Substring(0, equals).Trim().Trim('\'', '"');
            string value = spec.Substring(equals + 1).Trim().Trim('\'', '"');

            Texture replacement = null;
            if (!value.Equals("null", StringComparison.OrdinalIgnoreCase))
            {
                string[] parts = value.Split(',');
                var components = new float[3];
                bool parsed = parts.Length >= 3;
                for (int i = 0; parsed && i < 3; i++)
                {
                    parsed = float.TryParse(parts[i].Trim().Trim('\'', '"'),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out components[i]);
                }
                if (!parsed)
                {
                    Debug.LogWarning($"{Tag} ignoring -umaShotTexture '{spec}': expected three numbers, "
                                     + $"got '{value}'");
                    continue;
                }
                var colour = new Color(components[0], components[1], components[2], 1f);
                var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false);
                var pixels = new Color[16];
                for (int i = 0; i < pixels.Length; i++) pixels[i] = colour;
                texture.SetPixels(pixels);
                texture.Apply();
                replacement = texture;
            }

            int count = 0;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                foreach (var material in renderer.materials)
                {
                    if (material == null || !material.HasProperty(property)) continue;
                    if (only.Length > 0 && !material.name.ToLowerInvariant().Contains(only.ToLowerInvariant()))
                        continue;
                    applied.Add(new ShotTextureOverride
                    {
                        Material = material,
                        Property = property,
                        Original = material.GetTexture(property)
                    });
                    material.SetTexture(property, replacement);
                    count++;
                }
            }
            Debug.Log($"{Tag} -umaShotTexture: '{property}' set to "
                      + (replacement == null ? "<null>" : value) + $" on {count} material slot(s)");
        }
        return applied;
    }

    private static void RestoreShotTextureOverrides(List<ShotTextureOverride> applied)
    {
        foreach (var entry in applied)
        {
            if (entry.Material != null) entry.Material.SetTexture(entry.Property, entry.Original);
        }
        applied.Clear();
    }

    /// <summary>
    /// Everything after the meta database: the material the game's shader actually receives, the textures bound to
    /// it, and the mesh facts the shader depends on. The stored bundle material is not necessarily the runtime one -
    /// the viewer overwrites textures and properties when it loads a character - and the face shader's
    /// _NormalizeNormal is 0, so the length of its vertex normals matters.
    /// </summary>
    private static bool _faceDumped;

    private static void DumpFacePipeline(GameObject root, string stem)
    {
        // once only: this sits in the stage loop, and without the guard it wrote a four gigabyte log
        if (_faceDumped) return;
        _faceDumped = true;

        foreach (var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            string lower = renderer.name.ToLowerInvariant();
            if (!lower.Contains("face") && !lower.Contains("head")) continue;

            Debug.Log($"{Tag} [face] {stem} renderer '{renderer.name}' mesh '{renderer.sharedMesh?.name}'");

            var mesh = renderer.sharedMesh;
            if (mesh != null)
            {
                var normals = mesh.normals;
                if (normals != null && normals.Length > 0)
                {
                    float min = float.MaxValue, max = 0f, total = 0f;
                    foreach (var n in normals)
                    {
                        float length = n.magnitude;
                        if (length < min) min = length;
                        if (length > max) max = length;
                        total += length;
                    }
                    float mean = total / normals.Length;
                    Debug.Log($"{Tag} [face] normals: {normals.Length} entries, length min {min:F6} mean {mean:F6} "
                              + $"max {max:F6}");
                }

                var colours = mesh.colors;
                if (colours == null || colours.Length == 0)
                {
                    Debug.Log($"{Tag} [face] vertex colours: none on the mesh");
                }
                else
                {
                    Vector4 min = new Vector4(float.MaxValue, float.MaxValue, float.MaxValue, float.MaxValue);
                    Vector4 max = new Vector4(float.MinValue, float.MinValue, float.MinValue, float.MinValue);
                    foreach (var c in colours)
                    {
                        min = Vector4.Min(min, c);
                        max = Vector4.Max(max, c);
                    }
                if (normals != null && normals.Length > 0)
                {
                    Vector3 sum = Vector3.zero;
                    foreach (var n in normals) sum += n;
                    Vector3 mean = (sum / normals.Length).normalized;
                    var bins = new int[19];
                    foreach (var n in normals)
                        bins[Mathf.Clamp((int)(Vector3.Angle(n, mean) / 5f), 0, 18)]++;
                    var parts = new List<string>();
                    for (int i = 0; i < bins.Length; i++)
                        if (bins[i] > 0) parts.Add($"{i * 5}-{i * 5 + 5}:{bins[i]}");
                    Debug.Log($"{Tag} [face] normal fingerprint: mean "
                              + $"({mean.x:F4},{mean.y:F4},{mean.z:F4}) angles to mean -> "
                              + string.Join(" ", parts));
                }
                    Vector4 colourTotal = Vector4.zero;
                    foreach (var c in colours) colourTotal += (Vector4)c;
                    Vector4 meanColour = colourTotal / colours.Length;
                    Debug.Log($"{Tag} [face] vertex colours: {colours.Length} entries, "
                              + $"r {min.x:F3}-{max.x:F3} g {min.y:F3}-{max.y:F3} "
                              + $"b {min.z:F3}-{max.z:F3} a {min.w:F3}-{max.w:F3}");
                    // the four means separately: the export packs this colour into the pmx's third
                    // extra uv as (r,g,b,a), so which channel is the blue is a question of order,
                    // and r and b measure close enough on this model that blue's mean alone cannot
                    // tell them apart
                    Debug.Log($"{Tag} [face] colour channel means: r {meanColour.x:F4} g {meanColour.y:F4} "
                              + $"b {meanColour.z:F4} a {meanColour.w:F4}");
                }
            }

            DumpMeshArrays(renderer, mesh, root);

            foreach (var material in renderer.sharedMaterials)
            {
                if (material == null) continue;
                Debug.Log($"{Tag} [face] material '{material.name}' shader "
                          + $"'{(material.shader != null ? material.shader.name : "<none>")}'");
                var keywords = material.shaderKeywords;
                Debug.Log($"{Tag} [face]   enabled keywords: "
                          + (keywords == null || keywords.Length == 0
                              ? "<none>"
                              : string.Join(", ", keywords)));

                foreach (string property in material.GetTexturePropertyNames())
                {
                    var texture = material.GetTexture(property);
                    Debug.Log($"{Tag} [face]   texture {property} = "
                              + $"{(texture == null ? "<null>" : $"'{texture.name}' {texture.width}x{texture.height}")}");
                }

                var shader = material.shader;
                if (shader == null) continue;
                int count = ShaderUtil.GetPropertyCount(shader);
                for (int i = 0; i < count; i++)
                {
                    var type = ShaderUtil.GetPropertyType(shader, i);
                    string property = ShaderUtil.GetPropertyName(shader, i);
                    if (type == ShaderUtil.ShaderPropertyType.Float
                        || type == ShaderUtil.ShaderPropertyType.Range)
                    {
                        Debug.Log($"{Tag} [face]   float {property} = {material.GetFloat(property):F6}");
                    }
                    else if (type == ShaderUtil.ShaderPropertyType.Color)
                    {
                        var colour = material.GetColor(property);
                        Debug.Log($"{Tag} [face]   colour {property} = "
                                  + $"{colour.r:F3},{colour.g:F3},{colour.b:F3},{colour.a:F3}");
                    }
                }
            }
        }
    }

    /// <summary>
    /// Writes the arrays the exporter reads for one renderer, so the pmx it produced can be compared
    /// against the game's own data vertex by vertex rather than through a histogram. The positions are
    /// written in exactly the space the exporter writes them in
    /// (root.InverseTransformPoint(renderer.transform.TransformPoint(baked.vertices[i]))), which makes
    /// the pmx's own order a valid alignment for the rest.
    /// </summary>
    private static void DumpMeshArrays(SkinnedMeshRenderer renderer, Mesh mesh, GameObject root)
    {
        string exportPath = SessionState.GetString(KeyOutPath, "");
        if (string.IsNullOrEmpty(exportPath)) return;

        var baked = new Mesh();
        renderer.BakeMesh(baked, true);
        var lines = new List<string>
        {
            $"renderer {renderer.name}",
            $"mesh {mesh.name}",
            $"isReadable {mesh.isReadable}",
            $"vertexCount {mesh.vertexCount}",
            $"bakedVertexCount {baked.vertexCount}",
            $"subMeshCount {mesh.subMeshCount}",
            $"rootWorldToLocal {MatrixText(root.transform.worldToLocalMatrix)}",
            $"rendererLocalToWorld {MatrixText(renderer.transform.localToWorldMatrix)}",
        };

        var normals = mesh.normals;
        var bakedNormals = baked.normals;
        var colours = mesh.colors;
        var uv = mesh.uv;
        var uv2 = mesh.uv2;
        var uv3 = mesh.uv3;

        lines.Add($"positions {mesh.vertexCount}");
        for (int i = 0; i < mesh.vertexCount; i++)
        {
            var p = root.transform.InverseTransformPoint(renderer.transform.TransformPoint(baked.vertices[i]));
            lines.Add($"{p.x:R} {p.y:R} {p.z:R}");
        }

        lines.Add($"normals {normals.Length}");
        foreach (var n in normals) lines.Add($"{n.x:R} {n.y:R} {n.z:R}");
        lines.Add($"bakedNormals {bakedNormals.Length}");
        foreach (var n in bakedNormals) lines.Add($"{n.x:R} {n.y:R} {n.z:R}");

        lines.Add($"colours {colours.Length}");
        foreach (var c in colours) lines.Add($"{c.r:R} {c.g:R} {c.b:R} {c.a:R}");
        lines.Add($"uv {uv.Length}");
        foreach (var t in uv) lines.Add($"{t.x:R} {t.y:R}");
        lines.Add($"uv2 {uv2.Length}");
        foreach (var t in uv2) lines.Add($"{t.x:R} {t.y:R}");
        lines.Add($"uv3 {uv3.Length}");
        foreach (var t in uv3) lines.Add($"{t.x:R} {t.y:R}");

        string dumpPath = exportPath + ".mesh.txt";
        File.AppendAllLines(dumpPath, lines);
        Debug.Log($"{Tag} [face] wrote {lines.Count} lines of mesh arrays to {dumpPath}");
    }

    private static string MatrixText(Matrix4x4 m)
    {
        var parts = new List<string>(16);
        for (int row = 0; row < 4; row++)
            for (int column = 0; column < 4; column++)
                parts.Add(m[row, column].ToString("R"));
        return string.Join(" ", parts);
    }

    private static void DumpMaterials(GameObject root, string stem)
    {
        var renderers = root.GetComponentsInChildren<Renderer>(true);
        var shaderCounts = new Dictionary<string, int>();
        var problemMaterials = new List<string>();
        var textures = new HashSet<string>();
        int materialCount = 0;
        int noMainTexProperty = 0;
        int nullMainTex = 0;
        int missingShader = 0;

        foreach (var renderer in renderers)
        {
            foreach (var material in renderer.sharedMaterials)
            {
                materialCount++;
                if (material == null)
                {
                    problemMaterials.Add($"{renderer.name}: <null material slot>");
                    continue;
                }

                string shaderName = material.shader != null ? material.shader.name : "<no shader>";
                shaderCounts[shaderName] = shaderCounts.TryGetValue(shaderName, out int count) ? count + 1 : 1;

                if (material.shader == null || shaderName == "Hidden/InternalErrorShader") missingShader++;

                bool hasMainTex = material.HasProperty("_MainTex");
                if (!hasMainTex)
                {
                    noMainTexProperty++;
                    Color color = material.HasProperty("_Color") ? material.color : Color.white;
                    problemMaterials.Add($"{renderer.name} / {material.name}: shader '{shaderName}' has no _MainTex, "
                                         + $"color {color} -> renders flat {(color == Color.white ? "WHITE" : "colour")}");
                    continue;
                }

                var mainTex = material.mainTexture;
                if (mainTex == null)
                {
                    nullMainTex++;
                    string properties = string.Join(", ", material.GetTexturePropertyNames()
                        .Select(p => $"{p}={(material.GetTexture(p) == null ? "null" : material.GetTexture(p).name)}"));
                    problemMaterials.Add($"{renderer.name} / {material.name}: _MainTex is NULL (shader '{shaderName}') "
                                         + $"texture properties: [{properties}]");
                }
                else
                {
                    textures.Add(mainTex.name);
                }
            }
        }

        Debug.Log($"{Tag} material dump for {root.name}: {renderers.Length} renderers, {materialCount} material slots");
        Debug.Log($"{Tag}   distinct shaders: {shaderCounts.Count}");
        foreach (var pair in shaderCounts.OrderByDescending(p => p.Value).Take(25))
        {
            Debug.Log($"{Tag}     {pair.Value,4}  {pair.Key}");
        }
        Debug.Log($"{Tag}   materials with no _MainTex property: {noMainTexProperty}");
        Debug.Log($"{Tag}   materials with a NULL _MainTex  : {nullMainTex}");
        Debug.Log($"{Tag}   materials with a missing shader : {missingShader}");
        Debug.Log($"{Tag}   distinct main textures used      : {textures.Count}");
        foreach (string problem in problemMaterials.Take(60)) Debug.Log($"{Tag}   ! {problem}");
        if (problemMaterials.Count > 60) Debug.Log($"{Tag}   ! ... and {problemMaterials.Count - 60} more suspicious materials");

        // components that might be assigning textures/banners at runtime
        var behaviours = root.GetComponentsInChildren<MonoBehaviour>(true)
            .Where(b => b != null)
            .GroupBy(b => b.GetType().Name)
            .OrderByDescending(g => g.Count())
            .ToList();
        Debug.Log($"{Tag}   components on the prefab: {string.Join(", ", behaviours.Select(g => $"{g.Key}x{g.Count()}").Take(30))}");

        if (string.IsNullOrEmpty(stem)) return;

        // everything the loaded bundles brought into memory that belongs to this scene - this is both
        // the evidence that the prefab's materials are intentionally textureless and the candidate
        // list a texture-set dropdown would offer
        var materialAssets = Resources.FindObjectsOfTypeAll<Material>()
            .Where(m => m.name.IndexOf(stem, StringComparison.OrdinalIgnoreCase) >= 0)
            .OrderBy(m => m.name)
            .ToList();
        Debug.Log($"{Tag}   material assets matching '{stem}': {materialAssets.Count}");
        foreach (var material in materialAssets.Take(40))
        {
            string properties = string.Join(", ", material.GetTexturePropertyNames()
                .Select(p => $"{p}={(material.GetTexture(p) == null ? "null" : material.GetTexture(p).name)}"));
            Debug.Log($"{Tag}     {material.name}: shader={material.shader?.name} [{properties}]");
        }

        var textureAssets = Resources.FindObjectsOfTypeAll<Texture2D>()
            .Where(t => t.name.IndexOf(stem, StringComparison.OrdinalIgnoreCase) >= 0)
            .Select(t => t.name)
            .Distinct()
            .OrderBy(n => n)
            .ToList();
        Debug.Log($"{Tag}   texture assets matching '{stem}': {textureAssets.Count}");
        foreach (string texture in textureAssets.Take(80)) Debug.Log($"{Tag}     {texture}");

        // The interesting question: for a material like "mtl_<stem>_000_<suffix>" that has no texture,
        // does a "tex_<stem>_<variant>_<suffix>" texture set exist as a separate loadable asset?
        var suffixes = Resources.FindObjectsOfTypeAll<Material>()
            .Where(m => m.name.StartsWith($"mtl_{stem}_", StringComparison.OrdinalIgnoreCase))
            .Where(m => m.HasProperty("_MainTex") && m.GetTexture("_MainTex") == null)
            .Select(m => MaterialSuffix(m.name, stem))
            .Where(s => !string.IsNullOrEmpty(s))
            .Distinct()
            .ToList();
        if (suffixes.Count > 0)
        {
            Debug.Log($"{Tag}   probing texture sets for textureless suffixes: {string.Join(", ", suffixes)}");
            ProbeTextureVariants(stem, suffixes);
        }
    }

    /// <summary>"mtl_&lt;stem&gt;_000_base01" -> "base01" (drops the 3 digit variant token).</summary>
    private static string MaterialSuffix(string materialName, string stem)
    {
        string prefix = $"mtl_{stem}_";
        if (!materialName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        var parts = materialName.Substring(prefix.Length).Split('_');
        return parts.Length > 1 ? string.Join("_", parts.Skip(1)) : null;
    }

    /// <summary>Enumerates the alternative texture sets for a suffix and tries to load each one.</summary>
    private static void ProbeTextureVariants(string stem, List<string> suffixes)
    {
        var main = UmaViewerMain.Instance;
        foreach (string suffix in suffixes)
        {
            string needle = $"tex_{stem}_";
            var candidates = main.AbList.Values
                .Where(e => e.Name.StartsWith("3d/env", StringComparison.OrdinalIgnoreCase))
                .Where(e =>
                {
                    string file = Path.GetFileName(e.Name);
                    return file.StartsWith(needle, StringComparison.OrdinalIgnoreCase)
                           && file.EndsWith($"_{suffix}", StringComparison.OrdinalIgnoreCase);
                })
                .OrderBy(e => e.Name)
                .ToList();

            Debug.Log($"{Tag}     suffix '{suffix}': {candidates.Count} texture set(s) in the database");
            foreach (var entry in candidates.Take(8))
            {
                if (!File.Exists(entry.Path))
                {
                    Debug.Log($"{Tag}       MISSING ON DISK {entry.Name}");
                    continue;
                }
                try
                {
                    var texture = entry.Get<Texture2D>();
                    Debug.Log(texture != null
                        ? $"{Tag}       OK    {entry.Name} -> {texture.width}x{texture.height} ({texture.format})"
                        : $"{Tag}       EMPTY {entry.Name} -> bundle has no Texture2D");
                }
                catch (Exception ex)
                {
                    Debug.Log($"{Tag}       FAIL  {entry.Name}: {ex.GetType().Name} {ex.Message}");
                }
            }
        }
    }

    /// <summary>
    /// "pfb_env_home10001_main000_000" -> "env_home10001_main000": the part shared by the prefab, its
    /// materials and every texture variant set that belongs to it.
    /// </summary>
    private static string SceneStem(string entryName)
    {
        string name = Path.GetFileName(entryName);
        if (name.StartsWith("pfb_", StringComparison.OrdinalIgnoreCase)) name = name.Substring(4);
        var parts = name.Split('_');
        if (parts.Length > 1 && parts[parts.Length - 1].Length <= 4 && parts[parts.Length - 1].All(char.IsDigit))
        {
            name = string.Join("_", parts.Take(parts.Length - 1));
        }
        return name;
    }

    private static void StartLoad(CharaEntry chara, string costumeId)
    {
        var main = UmaViewerMain.Instance;
        var builder = UmaViewerBuilder.Instance;

        // Mirrors UmaViewerUI's character selection: same asset set, same load entry point.
        var list = new List<UmaDatabaseEntry>();
        list.AddRange(main.AbChara.Where(a => a.Name.StartsWith(UmaDatabaseController.BodyPath) && a.Name.Contains(chara.Id.ToString())));
        list.AddRange(main.AbChara.Where(a => a.Name.StartsWith(UmaDatabaseController.HeadPath) && a.Name.Contains(chara.Id.ToString())));

        var tailPath = UmaViewerUI.Instance != null ? UmaViewerUI.Instance.getCharaTailPath(chara) : null;
        if (!string.IsNullOrEmpty(tailPath))
        {
            list.AddRange(main.AbChara.Where(a => a.Name.StartsWith(tailPath)));
        }

        if (main.AbList.TryGetValue("3d/animator/drivenkeylocator", out var drivenKeyEntry)) list.Add(drivenKeyEntry);
        string motionPath = $"3d/motion/event/body/chara/chr{chara.Id}_00/anm_eve_chr{chara.Id}_00_idle01_loop";
        if (main.AbList.TryGetValue(motionPath, out var motionEntry)) list.Add(motionEntry);

        builder.UnloadUma();
        UmaAssetManager.PreLoadAndRun(list, delegate
        {
            try
            {
                builder.StartCoroutine(builder.LoadUma(chara, costumeId, false, string.Empty));
            }
            catch (Exception ex)
            {
                Fail($"loading character failed: {ex.Message}");
            }
        });
    }

    private static bool TryResolveCostume(CharaEntry chara, string requested, out string costumeId, out string failure)
    {
        costumeId = "";
        failure = null;

        var bodies = UmaViewerMain.Instance.AbChara
            .Where(a => a.Name.StartsWith(UmaDatabaseController.BodyPath) && !a.Name.Contains("clothes") && a.Name.Contains($"pfb_bdy{chara.Id}"))
            .OrderBy(a => a.Name)
            .ToList();

        if (bodies.Count == 0)
        {
            failure = $"no body assets found for character {chara.Id}";
            return false;
        }

        if (string.IsNullOrEmpty(requested))
        {
            costumeId = bodies[0].Name.Split('_').Last();
            return true;
        }

        var match = bodies.FirstOrDefault(b => b.Name.Split('_').Last() == requested);
        if (match == null)
        {
            failure = $"costume '{requested}' not found for character {chara.Id}; available: "
                      + string.Join(", ", bodies.Select(b => b.Name.Split('_').Last()));
            return false;
        }

        costumeId = requested;
        return true;
    }

    private static void PrintCharacters()
    {
        var main = UmaViewerMain.Instance;
        Debug.Log($"{Tag} characters:");
        foreach (var chara in main.Characters.OrderBy(c => c.Id))
        {
            string costumes = string.Join(",", main.AbChara
                .Where(a => a.Name.StartsWith(UmaDatabaseController.BodyPath) && !a.Name.Contains("clothes") && a.Name.Contains($"pfb_bdy{chara.Id}"))
                .Select(a => a.Name.Split('_').Last())
                .Distinct()
                .OrderBy(x => x));
            Debug.Log($"{Tag}   {chara.Id}\t{chara.Name}\t[{costumes}]");
        }
    }

    private static void PrintCostumes(CharaEntry chara)
    {
        var costumes = UmaViewerMain.Instance.AbChara
            .Where(a => a.Name.StartsWith(UmaDatabaseController.BodyPath) && !a.Name.Contains("clothes") && a.Name.Contains($"pfb_bdy{chara.Id}"))
            .Select(a => a.Name)
            .OrderBy(a => a)
            .ToList();
        Debug.Log($"{Tag} costumes for {chara.Id} ({chara.Name}):");
        foreach (var name in costumes) Debug.Log($"{Tag}   {name.Split('_').Last()}\t{name}");
    }

    private static void Succeed()
    {
        CurrentStage = Stage.Done;
        SessionState.SetString(KeyOptions, "");
        EditorApplication.update -= Tick;
        // every mode has to emit this marker: the wrappers wait for it, and a mode that only logs its
        // own summary looks like a hang to them
        Debug.Log($"{Tag} OK");
        EditorApplication.Exit(0);
    }

    private static void Fail(string message)
    {
        CurrentStage = Stage.Done;
        SessionState.SetString(KeyOptions, "");
        EditorApplication.update -= Tick;
        Debug.LogError($"{Tag} FAILED: {message}");
        EditorApplication.Exit(1);
    }
}
#endif
