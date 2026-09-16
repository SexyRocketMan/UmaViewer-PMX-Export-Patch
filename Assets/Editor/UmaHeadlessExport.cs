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
///   -umaOut &lt;path&gt;       .pmx file to write. Defaults to &lt;project&gt;/HeadlessExports/&lt;char&gt;_&lt;costume&gt;.pmx
///   -umaScene &lt;path&gt;     scene to boot, default Assets/Scenes/Version2.unity
///   -umaListChars        print every character id together with its costume ids, then exit
///   -umaListCostumes     print the costume ids of -umaChar, then exit
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

    private enum Stage
    {
        Idle = 0,
        WaitPlayMode,
        WaitStartup,
        WaitModel,
        Settle,
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
        public bool ListChars;
        public bool ListCostumes;
        public double TimeoutSeconds = 600;
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
                    case "-umaListChars": options.ListChars = true; break;
                    case "-umaListCostumes": options.ListCostumes = true; break;
                    case "-umaTimeout": options.TimeoutSeconds = double.Parse(Next()); break;
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

        if (!options.ListChars && !options.ListCostumes && options.CharId < 0)
        {
            Debug.LogError($"{Tag} nothing to do: pass -umaChar <id> (optionally -umaOut), or -umaListChars / -umaListCostumes");
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
                    if (!EditorApplication.isPlaying) return;
                    Debug.Log($"{Tag} play mode entered");
                    CurrentStage = Stage.WaitStartup;
                    break;

                case Stage.WaitStartup:
                {
                    var main = UmaViewerMain.Instance;
                    var builder = UmaViewerBuilder.Instance;
                    if (main == null || builder == null) return;
                    // UmaViewerMain.Start() fills the shader list as its very last step.
                    if (builder.ShaderList == null || builder.ShaderList.Count == 0) return;
                    if (main.Characters.Count == 0) return;

                    Debug.Log($"{Tag} viewer booted: {main.Characters.Count} characters, {main.AbChara.Count} chara assets");

                    if (options.ListChars)
                    {
                        PrintCharacters();
                        Succeed();
                        return;
                    }

                    var chara = main.Characters.FirstOrDefault(c => c.Id == options.CharId);
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

                    Debug.Log($"{Tag} loading character {chara.Id} ({chara.Name}) costume {costumeId}");
                    StartLoad(chara, costumeId);
                    CurrentStage = Stage.WaitModel;
                    break;
                }

                case Stage.WaitModel:
                {
                    var container = UmaViewerBuilder.Instance != null ? UmaViewerBuilder.Instance.CurrentUMAContainer : null;
                    if (container == null) return;
                    _settleFramesLeft = options.ExtraFrames;
                    CurrentStage = Stage.Settle;
                    break;
                }

                case Stage.Settle:
                    if (_settleFramesLeft-- > 0) return;
                    CurrentStage = Stage.Export;
                    break;

                case Stage.Export:
                {
                    var container = UmaViewerBuilder.Instance.CurrentUMAContainer;
                    if (container == null)
                    {
                        Fail("model container disappeared before export");
                        return;
                    }
                    string exportPath = SessionState.GetString(KeyOutPath, "");
                    Debug.Log($"{Tag} exporting to {exportPath}");
                    ModelExporter.ExportModel(container, exportPath);
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
