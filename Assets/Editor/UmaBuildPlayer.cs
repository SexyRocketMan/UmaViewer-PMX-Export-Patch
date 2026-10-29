#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// A Windows player build of the viewer from the command line, so the *shipped* build can be run against
/// the same code the editor-side harness drives.
///
/// Usage:
///
///   Unity.exe -batchmode -projectPath &lt;project&gt; -executeMethod UmaBuildPlayer.BuildWindows ^
///       -umaBuildOut &lt;dir&gt; -logFile &lt;log&gt;
///
/// `-umaBuildOut` defaults to &lt;project&gt;/Build/StandaloneWindows64 and the executable is named
/// `UmaViewer.exe`. The scenes are the enabled ones in EditorBuildSettings (Version2.unity boots the
/// viewer, LiveScene.unity is the live show scene), which is what the CI workflow's unity-builder does with
/// its defaults.
///
/// The editor's own `Config.json` is copied next to the executable, because that is where the player looks
/// for it (`Config.configPath` is `Application.dataPath + "/../Config.json"`, and in a player `dataPath` is
/// the `&lt;exe&gt;_Data` folder). Without it the build starts with defaults and cannot find the game data
/// folder, which is a confusing first run rather than a useful test.
///
/// This is the one thing the headless exporter cannot do: `UmaHeadlessExport` lives behind
/// `#if UNITY_EDITOR` and is never compiled into a player, so an exported model from the released exe is
/// always produced by the manual *Export Model* button. A build is what you use to check that button.
/// </summary>
public static class UmaBuildPlayer
{
    private const string Tag = "[UmaBuildPlayer]";

    /// <summary>Batch entry point for -executeMethod.</summary>
    public static void BuildWindows()
    {
        string project = Path.GetDirectoryName(Application.dataPath);
        string outDir = Argument("-umaBuildOut");
        if (string.IsNullOrEmpty(outDir))
        {
            outDir = Path.Combine(project, "Build", "StandaloneWindows64");
        }
        outDir = Path.GetFullPath(outDir);
        Directory.CreateDirectory(outDir);

        string[] scenes = EditorBuildSettings.scenes
            .Where(scene => scene.enabled)
            .Select(scene => scene.path)
            .ToArray();
        if (scenes.Length == 0)
        {
            Debug.LogError($"{Tag} FAILED: no enabled scenes in EditorBuildSettings");
            EditorApplication.Exit(1);
            return;
        }

        var options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = Path.Combine(outDir, "UmaViewer.exe"),
            target = BuildTarget.StandaloneWindows64,
            targetGroup = BuildTargetGroup.Standalone,
            options = BuildOptions.None
        };

        Debug.Log($"{Tag} building {options.target} to {options.locationPathName}");
        Debug.Log($"{Tag} scenes: {string.Join(", ", scenes)}");
        Debug.Log($"{Tag} scripting backend: "
                  + PlayerSettings.GetScriptingBackend(BuildTargetGroup.Standalone)
                  + $", version {Application.version}");

        BuildReport report;
        try
        {
            report = BuildPipeline.BuildPlayer(options);
        }
        catch (Exception e)
        {
            Debug.LogError($"{Tag} FAILED: {e.GetType().Name}: {e.Message}\n{e.StackTrace}");
            EditorApplication.Exit(1);
            return;
        }

        BuildSummary summary = report.summary;
        Debug.Log($"{Tag} result {summary.result}, {summary.totalSize / (1024 * 1024)} MB, "
                  + $"{summary.totalErrors} error(s), {summary.totalWarnings} warning(s), "
                  + $"took {summary.totalTime.TotalSeconds:F0}s");

        if (summary.result != BuildResult.Succeeded)
        {
            foreach (string step in report.steps.Where(s => s.messages.Any(m => m.type == LogType.Error))
                                                .Select(s => s.name))
            {
                Debug.LogError($"{Tag} failed in step {step}");
            }
            Debug.LogError($"{Tag} FAILED: build result {summary.result}");
            EditorApplication.Exit(1);
            return;
        }

        // the config the editor was using, so the build starts on the same game data folder
        string config = Path.Combine(project, "Config.json");
        string configCopy = Path.Combine(outDir, "Config.json");
        if (File.Exists(config))
        {
            File.Copy(config, configCopy, true);
            Debug.Log($"{Tag} copied Config.json next to the executable");
        }
        else
        {
            Debug.LogWarning($"{Tag} {config} does not exist, the build will start without a configured "
                             + "game data folder");
        }

        var exe = new FileInfo(options.locationPathName);
        long folderBytes = Directory.GetFiles(outDir, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
        Debug.Log($"{Tag} OK {exe.FullName} ({exe.Length / 1024} KB executable, "
                  + $"{folderBytes / (1024 * 1024)} MB for the whole folder)");
        EditorApplication.Exit(0);
    }

    /// <summary>The value of a `-umaX value` argument, or null.</summary>
    private static string Argument(string name)
    {
        string[] argv = Environment.GetCommandLineArgs();
        for (int i = 0; i < argv.Length - 1; i++)
        {
            if (argv[i] == name) return argv[i + 1];
        }
        return null;
    }
}
#endif
