using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Builds the viewer as a Windows player, so the command line can be used without Unity installed.
///
/// This exists because the CLI is meant to be a feature of the *shipped* viewer: the runner lives in
/// `Assets/Scripts/Cli/` and is compiled into the player, and the only way to know that stays true - and that the
/// flags still work outside the editor - is to build one and run a job through it.
/// </summary>
public static class UmaPlayerBuild
{
    // BuildDev/ is already ignored by git: a build is 124 MB and does not belong in the repository
    private const string OutputDir = "BuildDev/uma-cli";
    private const string Executable = "UmaViewer.exe";

    /// <summary>
    /// Builds the player to `BuildDev/uma-cli/UmaViewer.exe`. Run it with:
    /// `Unity.exe -batchmode -projectPath . -executeMethod UmaPlayerBuild.BuildWindows -logFile build.log`
    /// </summary>
    public static void BuildWindows()
    {
        string[] scenes = EditorBuildSettings.scenes
            .Where(scene => scene.enabled)
            .Select(scene => scene.path)
            .ToArray();
        if (scenes.Length == 0)
        {
            Debug.LogError("[build] no enabled scenes in the build settings");
            EditorApplication.Exit(1);
            return;
        }

        string projectRoot = Path.GetDirectoryName(Application.dataPath);
        string output = Path.GetFullPath(Path.Combine(projectRoot, OutputDir, Executable));
        Directory.CreateDirectory(Path.GetDirectoryName(output));

        var options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = output,
            target = BuildTarget.StandaloneWindows64,
            targetGroup = BuildTargetGroup.Standalone,
            options = BuildOptions.None,
        };

        Debug.Log($"[build] {scenes.Length} scene(s) -> {output} ({scenes[0]} first)");
        BuildReport report = BuildPipeline.BuildPlayer(options);
        BuildSummary summary = report.summary;

        Debug.Log($"[build] {summary.result}: {summary.totalSize / (1024 * 1024)} MB in "
                  + $"{summary.totalTime.TotalSeconds:F0}s, {summary.totalErrors} error(s), "
                  + $"{summary.totalWarnings} warning(s)");
        foreach (var step in report.steps)
        {
            foreach (var message in step.messages.Where(m => m.type == LogType.Error))
            {
                Debug.LogError($"[build] {step.name}: {message.content}");
            }
        }

        EditorApplication.Exit(summary.result == BuildResult.Succeeded ? 0 : 1);
    }
}
