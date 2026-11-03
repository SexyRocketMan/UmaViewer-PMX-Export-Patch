using System;
using UnityEngine;

/// <summary>
/// The player's way into the command line: when the viewer is started with flags the CLI understands, this takes
/// over before the user interface is ever clicked, runs one job, and quits with that job's exit code.
///
/// It is the reason the runner was moved out of `Assets/Editor/`: the same code now runs in a build, so a user
/// with `UmaViewer.exe` and no Unity install can export a model, record a motion or dump the props the way the
/// editor can. Nothing here decides *what* to do - that is <see cref="UmaHeadlessExport"/>, unchanged; this only
/// detects the flags, keeps a driver object alive to deliver frames, and makes the window not matter.
/// </summary>
public static class UmaCliBootstrap
{
    /// <summary>True once a CLI run has taken the process over, so nothing else tries to.</summary>
    public static bool Engaged { get; private set; }

    /// <summary>
    /// Runs after the first scene loads: the last moment before the viewer's own scripts would start presenting
    /// themselves, and the first moment this code can see a loaded scene.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (Engaged) return;

        string[] argv = Environment.GetCommandLineArgs();
        if (!UmaHeadlessExport.LooksLikeCommandLine(argv)) return;
        if (!Application.isPlaying) return;

        Engaged = true;
        MakeHeadless();
        Debug.Log($"[UmaViewer] command line mode: {argv.Length} argument(s), "
                  + $"{Application.productName} {Application.version}");

        // the driver has to outlive the scene: a run crosses several scene loads' worth of waiting
        var host = CliHost.KeepAlive("UmaCliDriver");
        host.AddComponent<UmaCliDriver>();

        UmaHeadlessExport.Begin(argv);
    }

    /// <summary>
    /// Hides what a run has no one to look at. The UI stays *alive* - a prop's texture-set rows are built into the
    /// materials panel, and `--variant` drives them - but nothing is drawn, the cursor is released, and the window
    /// is not asked to be interactive.
    /// </summary>
    private static void MakeHeadless()
    {
        foreach (var canvas in UnityEngine.Object.FindObjectsOfType<Canvas>(true))
        {
            if (!canvas) continue;
            canvas.enabled = false;      // stops drawing; the objects and their scripts stay
        }
        foreach (var camera in UnityEngine.Object.FindObjectsOfType<Camera>(true))
        {
            // nothing to look at, and the run drives its own camera when it needs one: do not spend a render on it
            if (camera && camera.targetTexture == null) camera.enabled = false;
        }

        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;
        Application.targetFrameRate = 60;
        Screen.sleepTimeout = SleepTimeout.NeverSleep;
    }
}

/// <summary>
/// Delivers one frame per <c>Update</c> to the run. The editor gets its frames from
/// `EditorApplication.update`; a player has only this.
/// </summary>
public class UmaCliDriver : MonoBehaviour
{
    private void Awake() => DontDestroyOnLoad(gameObject);

    private void Update() => CliHost.Tick();
}
