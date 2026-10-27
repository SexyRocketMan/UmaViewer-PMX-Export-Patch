#if UNITY_EDITOR
using Gallop;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UmaMusumeAudio;
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
///   -umaOut &lt;path&gt;       .pmx or .fbx file to write (the extension picks the writer). Defaults to
///                          &lt;project&gt;/HeadlessExports/&lt;char&gt;_&lt;costume&gt;.pmx
///   -umaRefine &lt;bool&gt;    FBX only: the optional uma refinement step (eye bones aimed as the Blender
///                          addon aims them). Defaults to Config.json's FbxApplyUmaRefinement
///   -umaEmbedTextures &lt;bool&gt;  FBX only: carry the PNGs inside the file as well as next to it.
///                          Defaults to Config.json's FbxEmbedTextures
///   -umaScene &lt;path&gt;     scene to boot, default Assets/Scenes/Version2.unity
///   -umaListChars        print every character id together with its costume ids, then exit
///   -umaListCostumes     print the costume ids of -umaChar, then exit
///   -umaListProps &lt;f&gt;    print prop/scene assets whose name contains &lt;f&gt; (empty = all), then exit
///   -umaScanProps &lt;f&gt;    load every prop/scene matching &lt;f&gt; in turn and report its material health
///                        (textureless slots, missing shaders, resolved texture set), then exit
///   -umaScanCount &lt;n&gt;    how many props to scan, default 10
///   -umaExportAudio &lt;sheet&gt;  write a sound asset to an mp3 (with -umaOut) carrying its transcript in
///                          the tag: -umaExportAudio snd_voi_story_041047007 -umaExportClip 3 -umaOut x.mp3
///   -umaExportClip &lt;n&gt;     which clip of that asset, default 0; -1 mixes the whole asset
///   -umaTranscript &lt;sheet&gt;  print the lines a story voice sheet is used for (e.g. 041047007, or
///                          snd_voi_story_041047007), together with what the viewer would put on each
///                          clip of that sound asset, then exit
///   -umaDumpMaterials    log material diagnostics for the loaded model before exporting
///   -umaVariant &lt;code&gt;   pick an environment texture set variant (e.g. 212 or 214) before exporting
///   -umaMorphNameMode &lt;n&gt; override Config.PmxMorphNameMode (0 tagged, 1 short english, 2 unified)
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
        Screenshot,
        ExportAudio
    }

    [Serializable]
    private class Options
    {
        public string ScenePath = DefaultScene;
        public int CharId = -1;
        public string CostumeId = "";
        public string OutPath = "";
        /// <summary>
        /// FBX only: -1 keeps Config.json's FbxApplyUmaRefinement, 0 turns the optional uma refinement off
        /// and 1 turns it on. An int rather than a bool?, because Options travels through SessionState as
        /// JsonUtility JSON and JsonUtility drops nullable fields.
        /// </summary>
        public int Refine = -1;
        /// <summary>FBX only: -1 keeps Config.json's FbxEmbedTextures, 0 off, 1 on.</summary>
        public int EmbedTextures = -1;
        public string Prop = "";
        public string Motion = "";
        public bool ListChars;
        public bool ListCostumes;
        public bool ListProps;
        public string ListPropsFilter = "";
        public string ScanProps = "";
        public int ScanCount = 10;
        public bool DumpMaterials;
        // a sound asset to write to an mp3, with the transcript in its tag
        public string ExportAudio = "";
        public int ExportClip = 0;
        // a story voice sheet to print the transcript of, without loading or exporting anything
        public string Transcript = "";
        public bool DumpFace;
        // what the facial morph system actually holds at runtime: the extra morphs, their locators and
        // their binds, and whether the cheek/texture plumbing behind them got filled in
        public bool ProbeMorphs;
        // how the animated tears are built: they are skinned meshes posed by an Animator, so the mesh the exporter
        // reads is a bind pose and not necessarily the shape the game draws
        public bool ProbeTears;
        // how long -ProbeTears watches the tears with the animator actually running, in wall clock seconds
        public double TearSampleSeconds = 2.0;
        public bool DumpShaders;
        public string Variant = "";
        public int MorphNameMode = -1;
        public bool APoseRestPose;
        public bool PlainMaterials;
        // the other opt-out: no deploy/tear morphs and none of the tokens that explain them
        public bool NoExtraMorphs;
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
                    case "-umaRefine": options.Refine = bool.Parse(Next()) ? 1 : 0; break;
                    case "-umaEmbedTextures": options.EmbedTextures = bool.Parse(Next()) ? 1 : 0; break;
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
                    case "-umaTranscript": options.Transcript = Next(); break;
                    case "-umaExportAudio": options.ExportAudio = Next(); break;
                    case "-umaExportClip": options.ExportClip = int.Parse(Next()); break;
                    case "-umaDumpMaterials": options.DumpMaterials = true; break;
                    case "-umaDumpFace": options.DumpFace = true; break;
            case "-umaProbeMorphs": options.ProbeMorphs = true; break;
            case "-umaProbeTears": options.ProbeTears = true; break;
            case "-umaDumpShaders": options.DumpShaders = true; break;
                    case "-umaShotTexture": options.ShotTextures.Add(Next()); break;
                    case "-umaShotGlobal": options.ShotGlobals.Add(Next()); break;
                    case "-umaShotFloat": options.ShotFloats.Add(Next()); break;
                    case "-umaPinPose": options.PinPose = double.Parse(Next()); break;
                    case "-umaShotMaterial": options.ShotMaterial = Next(); break;
                    case "-umaVariant": options.Variant = Next(); break;
                    case "-umaMorphNameMode": options.MorphNameMode = int.Parse(Next()); break;
                    case "-umaAPose": options.APoseRestPose = true; break;
                    case "-umaPlainMaterials": options.PlainMaterials = true; break;
                    case "-umaNoExtraMorphs": options.NoExtraMorphs = true; break;
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
            && string.IsNullOrEmpty(options.ScanProps) && string.IsNullOrEmpty(options.Transcript)
            && string.IsNullOrEmpty(options.ExportAudio)
            && options.CharId < 0 && string.IsNullOrEmpty(options.Prop))
        {
            Debug.LogError($"{Tag} nothing to do: pass -umaChar <id> or -umaProp <path> (optionally -umaOut), "
                           + "or -umaListChars / -umaListCostumes / -umaListProps / -umaScanProps / -umaTranscript");
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

                case Stage.ExportAudio:
                    if (!_audioExported) return;
                    ReportTag(SessionState.GetString(KeyOutPath, ""));
                    Succeed();
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
                        // the enum no longer has a slot 3 (it used to be "both"), and an unknown value would
                        // otherwise be cast straight through and only show up as the wrong spelling in the file
                        if (options.MorphNameMode > (int)PmxMorphNameMode.Unified)
                        {
                            Fail($"-umaMorphNameMode takes 0 (tagged), 1 (short english) or 2 (unified), not {options.MorphNameMode}");
                            return;
                        }

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

                    if (options.NoExtraMorphs)
                    {
                        Config.Instance.PmxExtraMorphs = false;
                        Debug.Log($"{Tag} exporting without the extra morphs: no deploy or tear morphs, and none "
                                  + "of the morph tokens that describe them");
                    }

                    if (options.Refine >= 0)
                    {
                        Config.Instance.FbxApplyUmaRefinement = options.Refine == 1;
                        Debug.Log($"{Tag} FBX uma refinement "
                                  + (Config.Instance.FbxApplyUmaRefinement ? "on" : "off")
                                  + (Config.Instance.FbxApplyUmaRefinement
                                      ? " (eye bones aimed at the gaze as the Blender addon aims them)"
                                      : " (the rig is exported exactly as the viewer holds it)"));
                    }

                    if (options.EmbedTextures >= 0)
                    {
                        Config.Instance.FbxEmbedTextures = options.EmbedTextures == 1;
                        Debug.Log($"{Tag} FBX textures "
                                  + (Config.Instance.FbxEmbedTextures
                                      ? "embedded (the file stays textured when moved on its own)"
                                      : "referenced only (keep the Texture2D folder next to the file)"));
                    }

                    if (!string.IsNullOrEmpty(options.ExportAudio))
                    {
                        if (!StartAudioExport(options)) return;
                        CurrentStage = Stage.ExportAudio;
                        return;
                    }

                    if (!string.IsNullOrEmpty(options.Transcript))
                    {
                        PrintTranscript(options.Transcript);
                        Succeed();
                        return;
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

                    // the tears last: they have to be watched with the animator running, which needs wall clock
                    // time rather than frames (see the note by _playDeadline)
                    if (options.ProbeTears)
                    {
                        if (!_tearSamplingStarted)
                        {
                            _tearSamplingStarted = true;
                            _tearSampleSeconds = options.TearSampleSeconds;
                            BeginTearSampling();
                        }
                        if (EditorApplication.timeSinceStartup < _tearSampleDeadline)
                        {
                            SampleTearFrame();
                            return;
                        }
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
                        // the light is applied for every shot, not only when there are several azimuths. A
                        // single azimuth is a legitimate request - "render this view with the light at 90
                        // degrees" - and skipping it silently renders the scene's own light instead, which
                        // makes a one-value-per-run light sweep look as though the light does not matter.
                        // Found by sweeping the light through six azimuths and measuring a 0.98 overlap
                        // between two opposite ones.
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
                    if (options.ProbeMorphs && container is UmaContainerCharacter probed)
                    {
                        DumpMorphState(probed);
                    }
                    if (options.ProbeTears && container is UmaContainerCharacter tearProbe)
                    {
                        DumpTearState(tearProbe);
                    }
                    if (options.DumpShaders)
                    {
                        DumpShaderFacts(container);
                    }

                    Debug.Log($"{Tag} exporting to {exportPath}");
                    // the exporter records what it writes, so the pmx can be compared against the game's
                    // own data vertex by vertex rather than by matching positions after the fact
                    ModelExporter.VertexDumpPath = options.DumpFace ? exportPath + ".vertices.txt" : null;
                    bool fbx = FbxExporter.IsFbxPath(exportPath);
                    Debug.Log($"{Tag} format: {(fbx ? "FBX" : "PMX")}"
                              + (fbx
                                  ? ", uma refinement " + (Config.Instance.FbxApplyUmaRefinement ? "on" : "off")
                                  : ""));
                    if (container is UmaContainerCharacter character)
                    {
                        Debug.Log($"{Tag} the save dialog would suggest "
                                  + $"'{ExportNaming.ModelFile(character)}{(fbx ? ".fbx" : ".pmx")}'");
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

    /// <summary>
    /// What the facial morph system holds at runtime, for a character that was set up but not exported.
    ///
    /// The extra morphs are built in code from the character's own parts (FaceDrivenKeyTarget.InitializeExtra),
    /// so a morph being absent means one of those parts was missing when the target initialised. This prints the
    /// parts themselves as well, so the report says *why* rather than only what.
    /// </summary>
    /// <summary>Watchers for -ProbeTears: the tears are sampled over wall clock time, not frames.</summary>
    private static bool _tearSamplingStarted;
    private static double _tearSampleDeadline;
    private static int _tearSampleCount;

    /// <summary>
    /// Switches the tears on the way the UI does - every `Tear*_Ctrl` weight to 1 - and marks the animation as
    /// running. Note the objects only animate once they are active, and the animator then needs real time.
    /// </summary>
    private static void BeginTearSampling()
    {
        var character = CurrentContainer() as UmaContainerCharacter;
        _tearSampleCount = 0;
        _tearSampleDeadline = EditorApplication.timeSinceStartup + Math.Max(0.1, _tearSampleSeconds);
        if (character == null || character.FaceDrivenKeyTarget == null)
        {
            Debug.Log($"{Tag} [tears] no facial target, nothing to sample");
            return;
        }
        int driven = 0;
        foreach (FacialOtherMorph morph in character.FaceDrivenKeyTarget.OtherMorphs)
        {
            if (morph == null || !morph.name.StartsWith("Tear")) continue;
            foreach (BindProperty property in morph.BindProperties)
            {
                if (property == null || property.Type != BindProperty.BindType.TearWeight) continue;
                character.FaceDrivenKeyTarget.ChangeMorphWeight(property, 1f, morph, false);
                driven++;
            }
        }
        Debug.Log($"{Tag} [tears] sampling '{character.name}' for {_tearSampleSeconds:F1}s, {driven} weight(s) raised");

        // and sweep the clip from end to end, because wall clock is far too slow in batch mode to walk 4.267s of
        // animation (two seconds of it advance the state by about 1%)
        for (int index = 0; index < character.TearControllers.Count; index++)
        {
            TearController controller = character.TearControllers[index];
            if (controller == null) continue;
            GameObject part = controller.CurrenObject < 1 ? controller.TearObject_0 : controller.TearObject_1;
            if (part == null) continue;
            SweepTearClip($"[tears]   sweep ctrl{index}", part);
        }
    }

    /// <summary>
    /// Steps the animator through the clip by normalised time and reports the posed extent each step. This is the
    /// routine an export would use to bake the roll into morphs, so it is the same code path either way.
    /// </summary>
    private static void SweepTearClip(string label, GameObject part)
    {
        Animator animator = part.GetComponent<Animator>();
        if (animator == null || animator.runtimeAnimatorController == null)
        {
            Debug.Log($"{Tag} {label} no animator to sweep");
            return;
        }
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.enabled = true;
        animator.Rebind();
        animator.speed = 0f; // Play() sets the time; speed only decides whether it drifts between calls
        int movedSteps = 0;
        Vector3 first = Vector3.zero;
        for (int step = 0; step <= 10; step++)
        {
            float time = step / 10f;
            animator.Play(0, 0, time);
            animator.Update(0.0001f);
            foreach (SkinnedMeshRenderer renderer in part.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var baked = new Mesh();
                try
                {
                    renderer.BakeMesh(baked, false);
                    Vector3[] vertices = baked.vertices;
                    if (vertices.Length == 0) continue;
                    Vector3 min = vertices[0], max = vertices[0];
                    foreach (Vector3 vertex in vertices)
                    {
                        min = Vector3.Min(min, vertex);
                        max = Vector3.Max(max, vertex);
                    }
                    Vector3 size = max - min;
                    if (step == 0) first = size;
                    else if ((size - first).sqrMagnitude > 1e-8f) movedSteps++;
                    Debug.Log($"{Tag} {label} t={time:F1} size={VectorText(size)} min={VectorText(min)} max={VectorText(max)}");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(baked);
                }
            }
        }
        animator.speed = 1f;
        Debug.Log($"{Tag} {label} steps that differ from t=0: {movedSteps} of 10");
    }

    private static double _tearSampleSeconds = 2.0;

    /// <summary>One sample per editor frame: where the animator is, and the posed extent of every active tear.</summary>
    private static void SampleTearFrame()
    {
        var character = CurrentContainer() as UmaContainerCharacter;
        if (character == null || character.TearControllers == null) return;
        _tearSampleCount++;
        for (int index = 0; index < character.TearControllers.Count; index++)
        {
            TearController controller = character.TearControllers[index];
            if (controller == null) continue;
            // SetWegiht shows CurrenObject and hides the other one, so that is the part that is really on screen
            GameObject part = controller.CurrenObject < 1 ? controller.TearObject_0 : controller.TearObject_1;
            if (part == null) continue;
            Animator animator = part.GetComponent<Animator>();
            // An animator in the default culling mode does not write its bone transforms when the renderer is not
            // being drawn, and batch mode draws nothing at all - which is exactly how a card that visibly rolls in
            // the editor measured rigid here. AlwaysAnimate is what makes the pose real without a camera.
            if (animator != null && animator.cullingMode != AnimatorCullingMode.AlwaysAnimate)
            {
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            }
            string state = "no animator";
            if (animator != null && animator.isActiveAndEnabled)
            {
                AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(0);
                state = $"norm={info.normalizedTime:F3} len={info.length:F3} "
                        + $"intransition={(animator.IsInTransition(0) ? 1 : 0)} speed={animator.speed:F2} "
                        + $"culling={animator.cullingMode}";
            }
            foreach (SkinnedMeshRenderer renderer in part.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var baked = new Mesh();
                try
                {
                    renderer.BakeMesh(baked, false);
                    Vector3[] vertices = baked.vertices;
                    if (vertices.Length == 0) continue;
                    Vector3 min = vertices[0], max = vertices[0];
                    foreach (Vector3 vertex in vertices)
                    {
                        min = Vector3.Min(min, vertex);
                        max = Vector3.Max(max, vertex);
                    }
                    Transform[] bones = renderer.bones;
                    string last = bones.Length > 0 && bones[bones.Length - 1] != null
                        ? $"{bones[bones.Length - 1].name}{VectorText(bones[bones.Length - 1].position)}"
                        : "none";
                    Debug.Log($"{Tag} [tears] frame {_tearSampleCount} ctrl{index} '{part.name}' "
                              + $"active={part.activeSelf} {state} size={VectorText(max - min)} "
                              + $"min={VectorText(min)} tip={last}");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(baked);
                }
            }
        }
    }

    /// <summary>
    /// How the animated tears are actually built, because the exporter only ever sees half of it.
    ///
    /// A tear is a <see cref="SkinnedMeshRenderer"/> posed by an <see cref="Animator"/> whose clip is the prefab's
    /// own `default_animation`. `ModelExporter` reads `mesh.vertices`, which for a skinned mesh is the *bind*
    /// pose - if the clip scales a bone to stretch the card from the eye corner down the cheek, the exported card
    /// is only the bind-pose stub. This prints the bind-pose extent, the posed extent (through BakeMesh, which
    /// runs the skinning for real), the skeleton, and the clip's own curves, so an authored-long card and a
    /// card-that-only-becomes-long-when-posed can be told apart.
    /// </summary>
    private static void DumpTearState(UmaContainerCharacter character)
    {
        if (character == null || character.TearControllers == null)
        {
            Debug.Log($"{Tag} [tears] no tear controllers");
            return;
        }
        DumpTearBundles();
        Debug.Log($"{Tag} [tears] {character.TearControllers.Count} controller(s), "
                  + $"FaceDrivenKeyTarget={(character.FaceDrivenKeyTarget != null)}, "
                  + $"spring containers on the character={character.cySpringDataContainers.Count}");

        // springs bind by bone *name* into one dictionary built from every transform on the character, so a
        // container that lists joint000..joint010 is what makes a tear ribbon hang down the cheek
        int springHits = 0;
        foreach (CySpringDataContainer spring in character.cySpringDataContainers)
        {
            if (spring == null) continue;
            foreach (CySpringParamDataElement element in spring.springParam)
            {
                if (element == null || string.IsNullOrEmpty(element._boneName)) continue;
                if (element._boneName.IndexOf("joint", StringComparison.OrdinalIgnoreCase) < 0) continue;
                springHits++;
                Debug.Log($"{Tag} [tears] spring '{spring.name}' binds '{element._boneName}' "
                          + $"gravity={element._gravity:F3} stiffness={element._stiffnessForce:F5} "
                          + $"drag={element._dragForce:F3} springForce={VectorText(element._springForce)} "
                          + $"children={element._childElements.Count}");
            }
        }
        Debug.Log($"{Tag} [tears] spring params naming a 'joint': {springHits}");

        // every asset the prefab carries, because the body's chara_shader_effect was exactly the kind of thing
        // that hid in a table we had not enumerated yet
        for (int index = 0; index < character.TearControllers.Count; index++)
        {
            TearController controller = character.TearControllers[index];
            if (controller == null) continue;
            foreach (GameObject part in new[] { controller.TearObject_0, controller.TearObject_1 })
            {
                if (part == null) continue;
                AssetHolder holder = part.GetComponent<AssetHolder>();
                if (holder == null || holder._assetTable == null || holder._assetTable.list == null) continue;
                var entries = new List<string>();
                foreach (StringObjectPair pair in holder._assetTable.list)
                {
                    if (pair == null) continue;
                    entries.Add($"{pair.Key}={pair.Value.GetType().Name}:{pair.Value.name}");
                }
                Debug.Log($"{Tag} [tears] '{part.name}' asset table ({entries.Count}): {string.Join(", ", entries)}");
            }
        }

        // and the same path the UI slider takes, in case the pose is hung off the weight rather than off frames
        FaceDrivenKeyTarget facial = character.FaceDrivenKeyTarget;
        if (facial != null && facial.OtherMorphs != null)
        {
            foreach (FacialOtherMorph morph in facial.OtherMorphs)
            {
                if (morph == null || !morph.name.StartsWith("Tear")) continue;
                foreach (BindProperty property in morph.BindProperties)
                {
                    if (property == null || property.Type != BindProperty.BindType.TearWeight) continue;
                    Debug.Log($"{Tag} [tears] driving '{morph.name}' TearWeight to 1 through the facial target");
                    facial.ChangeMorphWeight(property, 1f, morph, false);
                }
            }
            for (int index = 0; index < character.TearControllers.Count; index++)
            {
                TearController controller = character.TearControllers[index];
                if (controller == null) continue;
                Debug.Log($"{Tag} [tears] after weight: controller {index} weight={controller.Weight:F3} "
                          + $"0active={(controller.TearObject_0 != null && controller.TearObject_0.activeSelf)} "
                          + $"1active={(controller.TearObject_1 != null && controller.TearObject_1.activeSelf)}");
                DumpTearPartWidth($"[tears]   {index}a weighted", controller.TearObject_0);
            }
        }

        // the ribbon points along its own local +Y, so whichever transform it is hung from decides whether the
        // stream runs down the cheek or up over the eye - print the attach bones' orientation, and the head's
        Transform head = null;
        foreach (Transform candidate in character.GetComponentsInChildren<Transform>(true))
        {
            if (candidate.name == "Head") { head = candidate; break; }
        }
        if (head != null)
        {
            var attach = new List<string> { "Eye_tear_attach_01_L", "Eye_tear_attach_02_L", "Eye_tear_attach_03_L",
                                            "Eye_tear_attach_01_R", "Eye_tear_attach_02_R", "Eye_tear_attach_03_R" };
            Debug.Log($"{Tag} [tears] Head rot={VectorText(head.eulerAngles)} up={VectorText(head.up)}");
            foreach (string name in attach)
            {
                Transform bone = head.Find(name);
                if (bone == null) { Debug.Log($"{Tag} [tears]   {name}: not found under Head"); continue; }
                Debug.Log($"{Tag} [tears]   {name} localRot={VectorText(bone.localEulerAngles)} "
                          + $"worldRot={VectorText(bone.eulerAngles)} up={VectorText(bone.up)} "
                          + $"forward={VectorText(bone.forward)}");
            }
        }
        for (int index = 0; index < character.TearControllers.Count; index++)
        {
            TearController controller = character.TearControllers[index];
            if (controller == null) continue;
            Debug.Log($"{Tag} [tears] controller {index}: dir={controller.CurrentDir} "
                      + $"weight={controller.Weight:F3} object={controller.CurrenObject} speed={controller.Speed:F3}");
            DumpTearPart($"[tears]   {index}a", controller.TearObject_0);
            DumpTearPart($"[tears]   {index}b", controller.TearObject_1);
        }
    }

    private static void DumpTearPart(string label, GameObject part)
    {
        if (part == null)
        {
            Debug.Log($"{Tag} {label} (null)");
            return;
        }
        Transform transform = part.transform;
        Debug.Log($"{Tag} {label} '{part.name}' active={part.activeSelf} "
                  + $"localPos={VectorText(transform.localPosition)} worldPos={VectorText(transform.position)} "
                  + $"localRot={VectorText(transform.localEulerAngles)} localScale={VectorText(transform.localScale)} "
                  + $"lossyScale={VectorText(transform.lossyScale)}");
        var chain = new List<string>();
        for (Transform up = transform.parent; up != null && chain.Count < 6; up = up.parent) chain.Add(up.name);
        Debug.Log($"{Tag} {label} parent chain: {string.Join(" < ", chain)}");

        AssetHolder holder = part.GetComponent<AssetHolder>();
        if (holder != null && holder._assetTableValue != null)
        {
            Debug.Log($"{Tag} {label} sheet grid: "
                      + $"{holder._assetTableValue["texture_width"]}x{holder._assetTableValue["texture_height"]} "
                      + $"cells {holder._assetTableValue["texture_part_width"]}x"
                      + $"{holder._assetTableValue["texture_part_height"]}");
        }

        // what is actually attached, because that says how the ribbon is meant to move: an Animator, a
        // CySpringDataContainer, a DynamicBone, or nothing at all
        var types = new Dictionary<string, int>();
        foreach (Component component in part.GetComponentsInChildren<Component>(true))
        {
            if (component == null) continue;
            string name = component.GetType().Name;
            types[name] = types.TryGetValue(name, out int count) ? count + 1 : 1;
        }
        Debug.Log($"{Tag} {label} components: "
                  + string.Join(", ", types.OrderBy(pair => pair.Key).Select(pair => pair.Key + (pair.Value > 1 ? "x" + pair.Value : ""))));
        foreach (CySpringDataContainer spring in part.GetComponentsInChildren<CySpringDataContainer>(true))
        {
            Debug.Log($"{Tag} {label}   CySpringDataContainer '{spring.name}': "
                      + $"springParam={spring.springParam.Count} connected={spring.ConnectedBoneList.Count} "
                      + $"dynamicBones={spring.DynamicBones.Count} collisions={spring.collisionParam.Count}");
        }
        foreach (CySpring spring in part.GetComponentsInChildren<CySpring>(true))
        {
            Debug.Log($"{Tag} {label}   CySpring '{spring.name}' on '{spring.gameObject.name}'");
        }

        // the parts sit switched off until a controller raises their weight, and skinning only evaluates on an
        // active renderer, so the measurement happens on an activated copy of the state and the state is restored
        bool wasActive = part.activeSelf;
        part.SetActive(true);
        try
        {
            foreach (SkinnedMeshRenderer renderer in part.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Mesh mesh = renderer.sharedMesh;
                Debug.Log($"{Tag} {label} skinned '{renderer.name}' mesh '{(mesh != null ? mesh.name : "null")}' "
                          + $"verts={(mesh != null ? mesh.vertexCount : 0)} "
                          + $"bindLocalBounds={(mesh != null ? BoundsText(mesh.bounds) : "none")} "
                          + $"bones={renderer.bones.Length} rootBone={(renderer.rootBone ? renderer.rootBone.name : "none")}");
                Transform[] bones = renderer.bones;
                for (int b = 0; b < bones.Length && b < 8; b++)
                {
                    Transform bone = bones[b];
                    if (bone == null) continue;
                    Debug.Log($"{Tag} {label}   bone[{b}] '{bone.name}' localPos={VectorText(bone.localPosition)} "
                              + $"localScale={VectorText(bone.localScale)} worldPos={VectorText(bone.position)}");
                }
                if (bones.Length > 8)
                {
                    Debug.Log($"{Tag} {label}   ... {bones.Length - 8} more bones");
                }
                if (bones.Length > 0 && bones[bones.Length - 1] != null)
                {
                    Debug.Log($"{Tag} {label}   last bone '{bones[bones.Length - 1].name}' "
                              + $"worldPos={VectorText(bones[bones.Length - 1].position)}");
                }

                Animator animator = part.GetComponent<Animator>() ?? part.GetComponentInChildren<Animator>(true);
                DumpTearAnimator(label, animator, part);

                // the game raises the weight, which activates the object, and only then does the animator run -
                // an inactive animator never evaluates, so this is the state the mesh is actually drawn in
                if (animator != null && animator.runtimeAnimatorController != null)
                {
                    animator.speed = 1f;
                    animator.Play(0, 0, 0f);
                    for (int step = 0; step < 45; step++) animator.Update(1f / 30f);
                    DumpTearBaked(label + " played", renderer, -1f);
                    animator.speed = 0f;
                }

                foreach (float time in new[] { 0f, 0.25f, 0.5f, 0.75f, 0.999f })
                {
                    if (animator != null && animator.runtimeAnimatorController != null && animator.avatar != null)
                    {
                        // speed 1 with a tiny step: the pose is set by the normalised time, and the step is what
                        // makes Unity evaluate it at all (a zero step often evaluates nothing)
                        animator.speed = 1f;
                        animator.Play(0, 0, time);
                        animator.Update(0.0001f);
                    }
                    if (bones.Length > 0)
                    {
                        var moved = new List<string>();
                        for (int b = 0; b < bones.Length && b < 5; b++)
                        {
                            if (bones[b] != null) moved.Add($"{bones[b].name}{VectorText(bones[b].position)}");
                        }
                        Debug.Log($"{Tag} {label} t={time:F3} joints: {string.Join(" ", moved)}");
                    }
                    DumpTearBaked(label, renderer, time);
                }
                if (animator != null && animator.runtimeAnimatorController != null)
                {
                    animator.speed = 1f;
                }
            }
        }
        catch (Exception exception)
        {
            Debug.Log($"{Tag} {label} measurement failed: {exception.GetType().Name}: {exception.Message}");
        }
        finally
        {
            part.SetActive(wasActive);
        }
    }

    /// <summary>
    /// Every asset in the tear bundles, with curve counts for anything that is an animation. The clip the
    /// reconstruction picks (`default_animation`) is empty as loaded; if the real animation lives in a sibling
    /// clip, it is in one of these bundles and this finds it.
    /// </summary>
    private static void DumpTearBundles()
    {
        var main = UmaViewerMain.Instance;
        if (main == null) return;
        foreach (var pair in main.AbList)
        {
            if (pair.Key.IndexOf("tear", StringComparison.OrdinalIgnoreCase) < 0) continue;
            AssetBundle bundle = null;
            try
            {
                bundle = UmaAssetManager.LoadAssetBundle(pair.Value, true, false);
            }
            catch (Exception exception)
            {
                Debug.Log($"{Tag} [tears] bundle '{pair.Key}' failed: {exception.GetType().Name}");
                continue;
            }
            if (bundle == null) continue;
            UnityEngine.Object[] assets = bundle.LoadAllAssets();
            Debug.Log($"{Tag} [tears] bundle '{pair.Key}': {assets.Length} asset(s)");
            foreach (UnityEngine.Object asset in assets)
            {
                if (asset == null) continue;
                var clip = asset as AnimationClip;
                if (clip != null)
                {
                    Debug.Log($"{Tag} [tears]   AnimationClip '{clip.name}' legacy={clip.legacy} "
                              + $"length={clip.length:F3} bindings={AnimationUtility.GetCurveBindings(clip).Length} "
                              + $"humanMotion={clip.humanMotion} motionCurves={clip.hasMotionCurves}");
                    continue;
                }
                var controller = asset as RuntimeAnimatorController;
                if (controller != null)
                {
                    Debug.Log($"{Tag} [tears]   controller '{asset.name}' "
                              + $"clips={string.Join(",", controller.animationClips.Select(c => c != null ? c.name : "null"))}");
                    continue;
                }
                Debug.Log($"{Tag} [tears]   {asset.GetType().Name} '{asset.name}'");
            }
        }
    }


    /// <summary>The posed extent only, for re-measuring a part after the weight has been driven.</summary>
    private static void DumpTearPartWidth(string label, GameObject part)
    {
        if (part == null) return;
        bool wasActive = part.activeSelf;
        part.SetActive(true);
        try
        {
            foreach (SkinnedMeshRenderer renderer in part.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                DumpTearBaked(label, renderer, -1f);
            }
        }
        catch (Exception exception)
        {
            Debug.Log($"{Tag} {label} measure failed: {exception.GetType().Name}: {exception.Message}");
        }
        finally
        {
            part.SetActive(wasActive);
        }
    }

    /// <summary>The posed extent of a skinned mesh: BakeMesh runs the skinning, reading vertices does not.</summary>
    private static void DumpTearBaked(string label, SkinnedMeshRenderer renderer, float time)
    {
        var baked = new Mesh();
        try
        {
            renderer.BakeMesh(baked, false);
            Vector3[] vertices = baked.vertices;
            if (vertices.Length == 0)
            {
                Debug.Log($"{Tag} {label} t={time:F3} baked: no vertices");
                return;
            }
            Vector3 min = vertices[0], max = vertices[0];
            foreach (Vector3 vertex in vertices)
            {
                min = Vector3.Min(min, vertex);
                max = Vector3.Max(max, vertex);
            }
            Debug.Log($"{Tag} {label} t={time:F3} baked={vertices.Length} verts "
                      + $"min={VectorText(min)} max={VectorText(max)} size={VectorText(max - min)}");
        }
        catch (Exception exception)
        {
            Debug.Log($"{Tag} {label} t={time:F3} BakeMesh failed: {exception.GetType().Name}: {exception.Message}");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(baked);
        }
    }

    /// <summary>
    /// The clip's serialized fields, to a shallow depth: which curve lists exist and how big they are. This is
    /// how you tell "the clip is empty" apart from "the clip has data that GetCurveBindings does not report".
    /// </summary>
    private static void DumpClipSerialized(string label, AnimationClip clip)
    {
        try
        {
            var serialized = new SerializedObject(clip);
            SerializedProperty iterator = serialized.GetIterator();
            int printed = 0;
            while (iterator.NextVisible(true) && printed < 60)
            {
                if (iterator.depth > 2) continue;
                if (iterator.propertyType == SerializedPropertyType.Generic && iterator.propertyPath.Contains(".")) continue;
                string detail;
                switch (iterator.propertyType)
                {
                    case SerializedPropertyType.Integer:
                    case SerializedPropertyType.Enum:
                    case SerializedPropertyType.Boolean:
                    case SerializedPropertyType.Float:
                        detail = iterator.ToString() == null ? iterator.propertyType.ToString() : iterator.propertyType.ToString();
                        break;
                    default:
                        detail = iterator.propertyType.ToString();
                        break;
                }
                string size = iterator.isArray ? $"[{iterator.arraySize}]" : "";
                Debug.Log($"{Tag} {label}     field {iterator.propertyPath} = {detail}{size}");
                printed++;
            }
        }
        catch (Exception exception)
        {
            Debug.Log($"{Tag} {label}     serialized dump failed: {exception.GetType().Name}");
        }
    }

    private static void DumpTearAnimator(string label, Animator animator, GameObject part)
    {
        if (animator == null)
        {
            Debug.Log($"{Tag} {label} no Animator");
            return;
        }
        var runtime = animator.runtimeAnimatorController;
        Debug.Log($"{Tag} {label} animator enabled={animator.enabled} "
                  + $"controller='{(runtime != null ? runtime.name : "none")}' "
                  + $"clips={(runtime != null ? runtime.animationClips.Length : 0)}");

        // a *humanoid* clip has no generic curve bindings at all - its motion lives in muscle curves, and what
        // maps them onto this skeleton is the avatar. `bindings=0` therefore says nothing about whether the clip
        // animates anything, which is exactly the trap this probe fell into first time round.
        Avatar avatar = animator.avatar;
        if (avatar == null)
        {
            Debug.Log($"{Tag} {label} avatar: NONE - a humanoid clip cannot be applied without one");
        }
        else
        {
            Debug.Log($"{Tag} {label} avatar '{avatar.name}' valid={avatar.isValid} human={avatar.isHuman}");
            if (avatar.isHuman)
            {
                HumanBone[] human = avatar.humanDescription.human;
                Debug.Log($"{Tag} {label}   human bones mapped: {human.Length}");
                foreach (HumanBone bone in human)
                {
                    Debug.Log($"{Tag} {label}     {bone.humanName} -> '{bone.boneName}'");
                }
            }
        }
        if (runtime == null) return;
        AnimatorOverrideController overrides = runtime as AnimatorOverrideController;
        if (overrides != null && overrides.runtimeAnimatorController != null)
        {
            Debug.Log($"{Tag} {label}   override over '{overrides.runtimeAnimatorController.name}'");
        }
        // the override only replaces the one state it is named for; the rest of the base controller - which is
        // where a chain that bends down the cheek would have to live - is a different set of clips entirely
        RuntimeAnimatorController baseController = overrides != null && overrides.runtimeAnimatorController != null
            ? overrides.runtimeAnimatorController
            : runtime;
        if (baseController != null && !ReferenceEquals(baseController, runtime))
        {
            AnimationClip[] baseClips = baseController.animationClips;
            Debug.Log($"{Tag} {label}   base controller '{baseController.name}' clips={baseClips.Length}");
            foreach (AnimationClip baseClip in baseClips)
            {
                if (baseClip == null) continue;
                EditorCurveBinding[] baseBindings = AnimationUtility.GetCurveBindings(baseClip);
                Debug.Log($"{Tag} {label}     base clip '{baseClip.name}' length={baseClip.length:F3} "
                          + $"bindings={baseBindings.Length}");
                for (int b = 0; b < baseBindings.Length && b < 8; b++)
                {
                    Debug.Log($"{Tag} {label}       {baseBindings[b].path} | {baseBindings[b].type.Name} | "
                              + $"{baseBindings[b].propertyName}");
                }
            }
        }
        var animatorController = baseController as UnityEditor.Animations.AnimatorController;
        if (animatorController != null)
        {
            foreach (var layer in animatorController.layers)
            {
                Debug.Log($"{Tag} {label}   layer '{layer.name}' states={layer.stateMachine.states.Length}");
                foreach (var state in layer.stateMachine.states)
                {
                    Debug.Log($"{Tag} {label}     state '{state.state.name}' "
                              + $"clip='{(state.state.motion != null ? state.state.motion.name : "none")}'");
                }
            }
        }
        foreach (AnimationClip clip in runtime.animationClips)
        {
            if (clip == null) continue;
            EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(clip);
            Debug.Log($"{Tag} {label}   clip '{clip.name}' length={clip.length:F3} "
                      + $"loop={clip.isLooping} bindings={bindings.Length} humanMotion={clip.humanMotion} "
                      + $"motionCurves={clip.hasMotionCurves} genericRoot={clip.hasGenericRootTransform}");
            for (int b = 0; b < bindings.Length && b < 12; b++)
            {
                EditorCurveBinding binding = bindings[b];
                AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, binding);
                string keys = curve == null || curve.length == 0
                    ? "no keys"
                    : $"keys={curve.length} first={curve.keys[0].value:F3} last={curve.keys[curve.length - 1].value:F3}";
                Debug.Log($"{Tag} {label}     {binding.path} | {binding.type.Name} | {binding.propertyName} | {keys}");
            }
            if (bindings.Length > 12)
            {
                Debug.Log($"{Tag} {label}     ... {bindings.Length - 12} more bindings");
            }
            // GetCurveBindings only reports generic curves. A clip can still carry data the API does not list
            // (muscle curves, additive data), so the serialized fields are printed too rather than trusted.
            DumpClipSerialized(label, clip);
        }
    }

    private static string VectorText(Vector3 value) => $"({value.x:F3},{value.y:F3},{value.z:F3})";

    private static string BoundsText(UnityEngine.Bounds bounds) =>
        $"center={VectorText(bounds.center)} size={VectorText(bounds.size)}";

    private static void DumpMorphState(UmaContainerCharacter character)
    {
        FaceDrivenKeyTarget facial = character.FaceDrivenKeyTarget;
        Debug.Log($"{Tag} [morphs] character {character.name}, FaceDrivenKeyTarget "
                  + (facial == null ? "NULL" : $"'{facial.name}'"));
        Debug.Log($"{Tag} [morphs] CheekTex_0={(character.CheekTex_0 ? character.CheekTex_0.name : "NULL")} "
                  + $"CheekTex_1={(character.CheekTex_1 ? character.CheekTex_1.name : "NULL")} "
                  + $"CheekTex_2={(character.CheekTex_2 ? character.CheekTex_2.name : "NULL")}");
        Debug.Log($"{Tag} [morphs] StaticTear_L={(character.StaticTear_L ? character.StaticTear_L.name : "NULL")} "
                  + $"StaticTear_R={(character.StaticTear_R ? character.StaticTear_R.name : "NULL")} "
                  + $"manga L/R={character.LeftMangaObject.Count}/{character.RightMangaObject.Count} "
                  + $"tearControllers={character.TearControllers.Count}");

        foreach (var renderer in character.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            string lower = renderer.name.ToLowerInvariant();
            if (!lower.Contains("cheek") && !lower.Contains("face") && !lower.Contains("eyebrow") && !lower.Contains("manga"))
                continue;
            var material = renderer.sharedMaterial;
            Debug.Log($"{Tag} [morphs] renderer '{renderer.name}' active={renderer.gameObject.activeSelf} "
                      + $"material '{(material ? material.name : "none")}' "
                      + $"mainTex '{(material && material.mainTexture ? material.mainTexture.name : "none")}' "
                      + $"shader '{(material && material.shader ? material.shader.name : "none")}'");
        }

        if (facial == null) return;
        Debug.Log($"{Tag} [morphs] OtherMorphs: {facial.OtherMorphs.Count}");
        foreach (var morph in facial.OtherMorphs)
        {
            if (morph == null) continue;
            Debug.Log($"{Tag} [morphs]   '{morph.name}' locator={(morph.locator ? morph.locator.name : "none")} "
                      + $"binds={morph.BindProperties.Count}");
            foreach (var property in morph.BindProperties)
            {
                if (property == null) continue;
                var prefabs = new List<string>();
                foreach (var prefab in property.BindPrefabs) if (prefab) prefabs.Add(prefab.name);
                Debug.Log($"{Tag} [morphs]     {property.Type} {property.Part} "
                          + $"property='{property.PropertyName}' "
                          + $"material='{(property.BindMaterial ? property.BindMaterial.name : "none")}' "
                          + $"texture='{(property.BindTexture ? property.BindTexture.name : "none")}' "
                          + $"prefabs=[{string.Join(",", prefabs)}]");
            }
        }
    }

    /// <summary>
    /// The eye-highlight numbers the game actually ships: how far <c>_Limit</c> drops in the teary state, and
    /// the throb's rate and amplitude - the three values docs/UMA_EYE_SHADER.md leaves open.
    ///
    /// This is worth reading here rather than reaching for an extractor for two reasons. The harness already
    /// loads the game's own bundles, and UmaContainerCharacter swaps each material's shader for the game's
    /// *shipped* compiled shader out of shader.a (<see cref="UmaAssetManager.EyeShader"/>), so the property list
    /// and the values below are the real ones rather than a port's. What a compiled shader can give is
    /// properties and defaults, never source - the decompile stays the source of truth for the logic.
    ///
    /// Prints every property of the eye/face/cheek materials rather than a guessed subset: the property names
    /// on the shipped shader are not known in advance, so a filter is what would hide the answer. Then it prints
    /// chara_shader_effect, the one asset the game keeps runtime material-driving numbers in, and scans a few
    /// motion bundles for clips that animate a material property - where a keyframed teary threshold would live.
    /// </summary>
    private static void DumpShaderFacts(UmaContainer container)
    {
        Debug.Log("[shaders] ---- eye/face/cheek materials on the loaded model ----");
        var seen = new HashSet<string>();
        foreach (Renderer renderer in container.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null) continue;
            // sharedMaterials, not materials: the latter instantiates a copy per call and leaks it
            foreach (Material material in renderer.sharedMaterials)
            {
                if (material == null || material.shader == null) continue;
                string shaderName = material.shader.name;
                bool facial = Contains(shaderName, "eye") || Contains(shaderName, "face") || Contains(shaderName, "cheek")
                              || Contains(material.name, "eye") || Contains(material.name, "face");
                if (!facial) continue;
                // several renderers share one material; print each distinct one once
                if (!seen.Add(shaderName + "|" + material.name)) continue;

                int propertyCount = material.shader.GetPropertyCount();
                Debug.Log($"[shaders] '{material.name}' on '{renderer.name}' uses shader '{shaderName}', "
                          + $"{propertyCount} properties");
                for (int i = 0; i < propertyCount; i++)
                {
                    string property = material.shader.GetPropertyName(i);
                    var type = material.shader.GetPropertyType(i);
                    string value;
                    try
                    {
                        switch (type)
                        {
                            case ShaderPropertyType.Float:
                            case ShaderPropertyType.Range:
                                value = material.GetFloat(property).ToString("0.0000"); break;
                            case ShaderPropertyType.Int:
                                value = material.GetInt(property).ToString(); break;
                            case ShaderPropertyType.Color:
                                var colour = material.GetColor(property);
                                value = $"({colour.r:F3},{colour.g:F3},{colour.b:F3},{colour.a:F3})"; break;
                            case ShaderPropertyType.Vector:
                                var vector = material.GetVector(property);
                                value = $"({vector.x:F3},{vector.y:F3},{vector.z:F3},{vector.w:F3})"; break;
                            case ShaderPropertyType.Texture:
                                var texture = material.GetTexture(property);
                                value = texture != null ? texture.name : "<none>"; break;
                            default:
                                value = "?"; break;
                        }
                    }
                    catch (Exception exception)
                    {
                        value = "(" + exception.GetType().Name + ")";
                    }
                    Debug.Log($"[shaders]    {property} [{type}] = {value}");
                }
            }
        }

        DumpShaderEffectData("head", container.HeadShaderEffectData);
        DumpShaderEffectData("body", container.BodyShaderEffectData);

        // The shipped eye shader has a _WetTex slot and a _WetRate that nothing in this viewer writes, and no
        // exported character carries a wet texture - so the teary state may be a wet overlay the game assigns at
        // runtime rather than only a _Limit drop. Name scan only, no bundle loading: whether such assets exist at
        // all is the question, and it is answerable from the asset list. Counted by part rather than listed,
        // because "wet" turns out to match thousands of them (every texture has a wet variant).
        var viewer = UmaViewerMain.Instance;
        if (viewer != null)
        {
            var wet = viewer.AbList.Keys.Where(name => Contains(name, "wet")).ToList();
            Debug.Log($"[shaders] asset names containing 'wet': {wet.Count}");
            foreach (var part in new[] { "eyehi", "eye", "face", "cheek", "tear", "hair", "body", "attach" })
            {
                Debug.Log($"[shaders]   wet+{part}: {wet.Count(name => Contains(name, part))}");
            }
            foreach (var name in wet.Where(name => Contains(name, "eye")).Take(10))
            {
                Debug.Log($"[shaders]   eye wet: {name}");
            }
        }

        // A keyframed teary threshold would be a material curve on some clip in a motion bundle. Capped and
        // fully guarded: these are large bundles and a failure in here must cost the probe, not the export.
        // Character motions are looked at, not the first eight bundles: alphabetically those are campaign
        // backgrounds, which carry a background clip and nothing facial.
        int scanned = 0, clips = 0, materialCurves = 0, highlightCurves = 0;
        var main = UmaViewerMain.Instance;
        if (main != null)
        {
            var candidates = main.AbMotions.Where(entry => Contains(entry.Name, "chara")).ToList();
            Debug.Log($"[shaders] motion scan: {candidates.Count} character motion bundle(s) of "
                      + $"{main.AbMotions.Count} available, sampling 8");
            foreach (var entry in candidates.Take(8))
            {
                try
                {
                    var bundle = UmaAssetManager.LoadAssetBundle(entry, true, false);
                    if (bundle == null) continue;
                    scanned++;
                    var loaded = bundle.LoadAllAssets<AnimationClip>();
                    int before = clips;
                    foreach (var clip in loaded)
                    {
                        if (clip == null) continue;
                        clips++;
                        foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                        {
                            if (binding.propertyName.IndexOf("aterial", StringComparison.Ordinal) < 0) continue;
                            materialCurves++;
                            if (binding.propertyName.IndexOf("_Limit", StringComparison.Ordinal) < 0
                                && binding.propertyName.IndexOf("_High", StringComparison.Ordinal) < 0) continue;
                            highlightCurves++;
                            Debug.Log($"[shaders] MATERIAL CURVE '{entry.Name}' clip '{clip.name}' "
                                      + $"{binding.path} -> {binding.propertyName}");
                        }
                    }
                    Debug.Log($"[shaders]   motion '{entry.Name}': {clips - before} clip(s)");
                }
                catch (Exception exception)
                {
                    Debug.Log($"[shaders] skipping motion '{entry.Name}': "
                              + $"{exception.GetType().Name}: {exception.Message}");
                }
            }
        }
        else
        {
            Debug.Log("[shaders] no UmaViewerMain instance, skipped the motion scan");
        }
        Debug.Log($"[shaders] motion scan: {scanned} bundle(s), {clips} clip(s), {materialCurves} material "
                  + $"curve(s), {highlightCurves} on _Limit/_High");
        Debug.Log("[shaders] done");
    }

    private static bool Contains(string text, string needle) =>
        !string.IsNullOrEmpty(text) && text.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

    /// <summary>
    /// chara_shader_effect, printed as the serialised fields it is stored as. This is the one asset that carries
    /// runtime material-driving numbers rather than a static material value: CharaShaderEffectData.updateUV
    /// writes _UVEmissiveScroll and _UVEmissivePower from these params every frame, which is the mechanism
    /// behind the animated tears. The fields are private, so this goes through SerializedObject.
    /// </summary>
    private static void DumpShaderEffectData(string label, CharaShaderEffectData data)
    {
        if (data == null)
        {
            Debug.Log($"[shaders] chara_shader_effect ({label}): none");
            return;
        }
        var serialized = new SerializedObject(data);
        var settings = serialized.FindProperty("_settingDataArray");
        int count = settings != null && settings.isArray ? settings.arraySize : 0;
        Debug.Log($"[shaders] chara_shader_effect ({label}) '{data.name}': {count} setting(s)");
        for (int i = 0; i < count; i++)
        {
            var element = settings.GetArrayElementAtIndex(i);
            var type = element.FindPropertyRelative("Type");
            var target = element.FindPropertyRelative("TargetMaterial");
            var parameters = element.FindPropertyRelative("ParamArray");
            var curves = element.FindPropertyRelative("ParamCurveArray");

            string kind = "?";
            if (type != null && type.enumNames != null
                && type.enumValueIndex >= 0 && type.enumValueIndex < type.enumNames.Length)
            {
                kind = type.enumNames[type.enumValueIndex];
            }
            string targetName = target != null && target.objectReferenceValue != null
                ? target.objectReferenceValue.name
                : "none";
            var values = new List<string>();
            if (parameters != null && parameters.isArray)
            {
                for (int p = 0; p < parameters.arraySize; p++)
                    values.Add(parameters.GetArrayElementAtIndex(p).intValue.ToString());
            }
            int curveCount = curves != null && curves.isArray ? curves.arraySize : 0;
            Debug.Log($"[shaders]   [{i}] {kind} material '{targetName}' "
                      + $"params=[{string.Join(",", values)}] curves={curveCount}");
        }
    }

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

    /// <summary>
    /// Explains one sound asset's transcript - the text half of the audio export, so the join against the
    /// game data can be checked without exporting (or playing) anything. Accepts a story sheet
    /// (041047007), an asset name (snd_voi_story_041047007, snd_voi_home_100100) or a full asset path.
    /// </summary>
    private static void PrintTranscript(string query)
    {
        // a story sheet is also its timeline's name; print what the sheet resolves to first
        const string storyPrefix = "snd_voi_story_";
        string sheet = query;
        int at = sheet.IndexOf(storyPrefix, StringComparison.OrdinalIgnoreCase);
        if (at >= 0) sheet = sheet.Substring(at + storyPrefix.Length);
        sheet = Path.GetFileNameWithoutExtension(sheet);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        Dictionary<int, List<TranscriptLine>> byCue = AudioTranscripts.ForSheet(sheet);
        if (byCue.Count > 0)
        {
            int lines = 0;
            foreach (KeyValuePair<int, List<TranscriptLine>> pair in byCue.OrderBy(p => p.Key))
            {
                foreach (TranscriptLine line in pair.Value)
                {
                    Debug.Log($"{Tag} cue {pair.Key,4} {line.Speaker}: {OneLine(line.Text)}"
                              + $"  [story {line.StoryId} {line.StoryTitle}]");
                    lines++;
                }
            }
            Debug.Log($"{Tag} sheet {sheet}: {byCue.Count} cue(s) carry text, {lines} line(s), "
                      + $"resolved in {stopwatch.ElapsedMilliseconds} ms");
        }
        else
        {
            Debug.Log($"{Tag} sheet {sheet}: no story timeline carries lines for it "
                      + $"(resolved in {stopwatch.ElapsedMilliseconds} ms)");
        }

        UmaDatabaseEntry awb = FindSoundAsset(query);
        if (awb == null)
        {
            Fail($"no sound asset matches '{query}'");
            return;
        }

        List<SoundCues.Cue> cues = SoundCues.Of(awb);
        Debug.Log($"{Tag} {awb.Name}: {cues?.Count ?? 0} clip(s)"
                  + (cues == null ? ", no acb next to it so the cue names are unknown" : ""));
        Debug.Log($"{Tag} character voice table: {UmaDatabaseController.SystemVoiceTextCount} cue name(s)");
        if (cues == null) return;

        for (int i = 0; i < cues.Count; i++)
        {
            string text = AudioTranscripts.Summarize(AudioTranscripts.ForClip(awb, i));
            Debug.Log($"{Tag} clip {i,4} {cues[i].Name ?? "-"}: {(string.IsNullOrEmpty(text) ? "-" : OneLine(text))}");
        }
    }

    /// <summary>A sound asset by full path, asset name, or the bare name its file carries.</summary>
    private static UmaDatabaseEntry FindSoundAsset(string query)
    {
        var main = UmaViewerMain.Instance;
        if (main?.AbList == null) return null;

        string file = Path.GetFileNameWithoutExtension(query);
        foreach (string candidate in new[] { query, $"sound/c/{query}.awb", $"sound/v/{query}.awb", $"sound/c/{file}.awb", $"sound/v/{file}.awb" })
        {
            if (main.AbList.TryGetValue(candidate, out UmaDatabaseEntry entry)) return entry;
        }
        return null;
    }


    private static bool _audioExported;

    /// <summary>
    /// Writes one clip of a sound asset to an mp3, with its transcript in the tag. Accepts a story sheet
    /// (041047007), the asset name (snd_voi_story_041047007) or a full asset path.
    /// </summary>
    private static bool StartAudioExport(Options options)
    {
        string query = options.ExportAudio;
        var main = UmaViewerMain.Instance;
        if (main?.AbList == null) { Fail("no asset list"); return false; }

        UmaDatabaseEntry entry = null;
        foreach (string candidate in new[]
                 {
                     query,
                     $"sound/c/{query}.awb",
                     $"sound/c/snd_voi_story_{query}.awb",
                     $"sound/v/{query}.awb",
                 })
        {
            if (main.AbList.TryGetValue(candidate, out entry)) break;
            entry = null;
        }
        if (entry == null) { Fail($"no sound asset matches '{query}'"); return false; }

        var streams = UmaViewerBuilder.Instance.LoadAudioStreams(entry);
        if (streams.Count == 0) { Fail($"{entry.Name}: no clips (file missing?)"); return false; }
        if (options.ExportClip >= streams.Count)
        {
            Fail($"{entry.Name} has {streams.Count} clip(s), so -umaExportClip {options.ExportClip} is out of range");
            return false;
        }

        string path = string.IsNullOrEmpty(options.OutPath)
            ? Path.Combine("HeadlessExports", Path.GetFileNameWithoutExtension(entry.Name) + ".mp3")
            : options.OutPath;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));

        AudioTranscriptTag? tag = AudioTranscripts.Tag(entry, options.ExportClip);
        SessionState.SetString(KeyOutPath, path);
        _audioExported = false;
        Debug.Log($"{Tag} exporting {entry.Name} clip {options.ExportClip} of {streams.Count} to {path}"
                  + (tag.HasValue ? " with a transcript tag" : " (no transcript for this clip)"));
        UmaViewerBuilder.Instance.StartCoroutine(RunAudioExport(streams, path, options.ExportClip, tag));
        return true;
    }

    private static System.Collections.IEnumerator RunAudioExport(List<UmaWaveStream> streams, string path,
                                                                 int clip, AudioTranscriptTag? tag)
    {
        yield return AudioExporter.ExportAudio(streams, path, clip, tag);
        _audioExported = true;
    }

    /// <summary>Reads the tag back out of the written file, so a broken writer is visible here.</summary>
    private static void ReportTag(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) { Fail($"no mp3 at {path}"); return; }
        byte[] head = File.ReadAllBytes(path);
        Debug.Log($"{Tag} wrote {head.Length} bytes to {path}");
        if (head.Length < 10 || head[0] != (byte)'I' || head[1] != (byte)'D' || head[2] != (byte)'3')
        {
            Debug.Log($"{Tag} the file carries no ID3 tag - the clip has no transcript");
            return;
        }

        int size = ((head[6] & 0x7F) << 21) | ((head[7] & 0x7F) << 14) | ((head[8] & 0x7F) << 7) | (head[9] & 0x7F);
        Debug.Log($"{Tag} ID3v2.{head[3]} tag, {size} bytes of frames");
        int at = 10;
        while (at + 10 <= 10 + size)
        {
            string id = System.Text.Encoding.ASCII.GetString(head, at, 4);
            if (id.Trim((char)0).Length == 0) break;
            int frameSize = (head[at + 4] << 24) | (head[at + 5] << 16) | (head[at + 6] << 8) | head[at + 7];
            if (frameSize <= 0 || at + 10 + frameSize > head.Length) break;
            byte encoding = head[at + 10];
            int textAt = at + 10 + 1;
            if (id == "USLT" || id == "COMM") textAt += 7;   // language (3) + empty utf-16 descriptor (4)
            int length = at + 10 + frameSize - textAt;
            string text = encoding == 1
                ? System.Text.Encoding.Unicode.GetString(head, textAt, Math.Max(0, length)).TrimStart('\uFEFF')
                : System.Text.Encoding.UTF8.GetString(head, textAt, Math.Max(0, length));
            Debug.Log($"{Tag}   {id} ({frameSize} bytes): {text.Replace("\r\n", " / ").Replace("\n", " / ").Trim((char)0)}");
            at += 10 + frameSize;
        }
    }

    /// <summary>One log line for a multi-line dialogue line.</summary>
    private static string OneLine(string text) =>
        string.IsNullOrEmpty(text) ? "" : text.Replace("\r\n", " / ").Replace("\r", " ").Replace("\n", " / ");

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