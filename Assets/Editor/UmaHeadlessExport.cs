#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

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
///   -umaRecordVmd &lt;path&gt; record one loop of the playing animation to a .vmd, then exit (character only)
///   -umaRecordFps &lt;n&gt;    frame rate of that recording, default 30
///   -umaRecordMode &lt;m&gt;   deterministic (default, one pinned frame per sample) or realtime (the legacy
///                        FixedUpdate sampler, useful to compare behaviour)
///   -umaTimeout &lt;sec&gt;    abort after this many seconds, default 600
///   -umaExtraFrames &lt;n&gt;  frames to let the model settle before exporting, default 30
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
        Done
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
        public string Variant = "";
        public int MorphNameMode = -1;
        public string RecordVmd = "";
        public int RecordFps = 30;
        public int RecordReduction = 0;
        public string RecordMode = "deterministic";
        public double TimeoutSeconds = 600;
        public double BootTimeoutSeconds = 120;
        public int ExtraFrames = 30;

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
                    case "-umaVariant": options.Variant = Next(); break;
                    case "-umaMorphNameMode": options.MorphNameMode = int.Parse(Next()); break;
                    case "-umaRecordVmd": options.RecordVmd = Next(); break;
                    case "-umaRecordFps": options.RecordFps = int.Parse(Next()); break;
                    case "-umaRecordReduction": options.RecordReduction = int.Parse(Next()); break;
                    case "-umaRecordMode": options.RecordMode = Next(); break;
                    case "-umaTimeout": options.TimeoutSeconds = double.Parse(Next()); break;
                    case "-umaBootTimeout": options.BootTimeoutSeconds = double.Parse(Next()); break;
                    case "-umaExtraFrames": options.ExtraFrames = int.Parse(Next()); break;
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
                        return;
                    }

                    CurrentStage = string.IsNullOrEmpty(options.RecordVmd) ? Stage.Export : Stage.RecordVmd;
                    break;

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
                                  + $"in {options.RecordMode} mode");

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

                    if (options.RecordReduction > 0) recorder.KeyReductionLevel = options.RecordReduction;
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

                    if (options.DumpMaterials)
                    {
                        string stem = SessionState.GetInt(KeyIsProp, 0) == 1
                            ? SceneStem(SessionState.GetString(KeyPropName, ""))
                            : "";
                        DumpMaterials(container.gameObject, stem);
                    }

                    Debug.Log($"{Tag} exporting to {exportPath}");
                    if (container is UmaContainerCharacter character)
                    {
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
    /// Reports what the loaded model's materials actually resolved to. Props and scenes get no
    /// material post-processing at all in UmaContainerProp, so materials the game assigns at runtime
    /// (weather/banner texture sets) stay empty and render flat white.
    /// </summary>
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
