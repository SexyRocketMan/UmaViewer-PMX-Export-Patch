using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// What the command line runner needs from the thing hosting it, in the four places the editor and a built
/// player differ: how a frame gets here, how the process ends, how a scene gets loaded, and how state survives
/// the fact that entering play mode reloads every static field in the editor.
///
/// The runner itself - `UmaHeadlessExport` - is ordinary runtime code and is compiled into both. That is the
/// point of this file: the export, record, scan, dump and screenshot jobs are all plain Unity work, and only
/// these four things were ever editor-shaped.
/// </summary>
public static class CliHost
{
    /// <summary>True when the editor is running the show; the player bootstrap sets it false.</summary>
    public static bool IsEditor
    {
        get
        {
#if UNITY_EDITOR
            return true;
#else
            return false;
#endif
        }
    }

    /// <summary>The frame every stage of a run is advanced on. Set by whoever arms the runner.</summary>
    private static Action _tick;

    /// <summary>
    /// Starts calling <paramref name="tick"/> once per frame. In the editor that is an
    /// `EditorApplication.update` subscription - which the domain reload drops, so it is re-armed after every
    /// reload (see `UmaHeadlessExport.Bootstrap`); in a player it is the driver object the bootstrap creates.
    /// </summary>
    public static void Arm(Action tick)
    {
        _tick = tick;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.update -= Tick;
        UnityEditor.EditorApplication.update += Tick;
#endif
    }

    /// <summary>Stops the per-frame ticks. Called when a run finishes or fails.</summary>
    public static void Disarm()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.update -= Tick;
#endif
        _tick = null;
    }

    /// <summary>The frame callback the editor subscription and the player driver both call.</summary>
    public static void Tick()
    {
        _tick?.Invoke();
    }

    /// <summary>
    /// Ends the run. The editor exits the editor; a player quits the application. Both with the run's own exit
    /// code, so a caller reads the result the same way in either host.
    /// </summary>
    public static void Exit(int code)
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(code);
#else
        Application.Quit(code);
#endif
    }

    /// <summary>
    /// Enters play mode, which is how an editor reaches the viewer's runtime load path for the first time.
    /// A player is already past that point, so this is a no-op that reports "already there".
    /// </summary>
    public static bool EnterPlayMode()
    {
#if UNITY_EDITOR
        if (!UnityEditor.EditorApplication.isPlaying) UnityEditor.EditorApplication.EnterPlaymode();
        return true;
#else
        return true;
#endif
    }

    /// <summary>Whether the runtime scene is live.</summary>
    public static bool IsPlaying
    {
        get
        {
#if UNITY_EDITOR
            return UnityEditor.EditorApplication.isPlaying;
#else
            return Application.isPlaying;
#endif
        }
    }

    /// <summary>
    /// Opens the viewer scene. The editor opens it as an asset before entering play mode; a player was built with
    /// it as its first scene and can only load it by name, so the path is reduced to the scene name and the load
    /// is skipped when that is already the scene on screen.
    /// </summary>
    public static void OpenScene(string path)
    {
#if UNITY_EDITOR
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene(path, UnityEditor.SceneManagement.OpenSceneMode.Single);
#else
        string name = System.IO.Path.GetFileNameWithoutExtension(path);
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == name) return;
        if (Application.CanStreamedLevelBeLoaded(name))
        {
            UnityEngine.SceneManagement.SceneManager.LoadScene(name);
            return;
        }
        Debug.LogWarning($"[cli] no scene called '{name}' in this build; running in "
                         + $"'{UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}' instead. "
                         + "The viewer scene has to be the build's first scene, or listed in its build settings.");
#endif
    }

    /// <summary>A monotonic clock in seconds, for the deadlines a run sets itself.</summary>
    public static double Now => Time.realtimeSinceStartup;

    /// <summary>
    /// An object that survives a scene load, for the player side. The editor has no equivalent need - its ticks
    /// come from the editor itself - so this is only used by the player bootstrap.
    /// </summary>
    public static GameObject KeepAlive(string name)
    {
        var host = new GameObject(name);
        UnityEngine.Object.DontDestroyOnLoad(host);
        return host;
    }
}

/// <summary>
/// The run's own state, across frames.
///
/// In the editor this has to be `SessionState`, because entering play mode reloads the script domain: every
/// static field is cleared at exactly the moment a run has the most to remember. In a player nothing reloads, so
/// a dictionary is both sufficient and simpler. The two are one wrapper so the runner never has to care which
/// host it is in.
/// </summary>
public static class CliState
{
#if !UNITY_EDITOR
    private static readonly Dictionary<string, string> Values = new Dictionary<string, string>(StringComparer.Ordinal);
#endif

    public static string GetString(string key, string fallback)
    {
#if UNITY_EDITOR
        return UnityEditor.SessionState.GetString(key, fallback);
#else
        return Values.TryGetValue(key, out string value) ? value : fallback;
#endif
    }

    public static void SetString(string key, string value)
    {
#if UNITY_EDITOR
        UnityEditor.SessionState.SetString(key, value);
#else
        if (value == null) Values.Remove(key);
        else Values[key] = value;
#endif
    }

    public static int GetInt(string key, int fallback)
    {
#if UNITY_EDITOR
        return UnityEditor.SessionState.GetInt(key, fallback);
#else
        return Values.TryGetValue(key, out string value) && int.TryParse(value, out int parsed) ? parsed : fallback;
#endif
    }

    public static void SetInt(string key, int value)
    {
#if UNITY_EDITOR
        UnityEditor.SessionState.SetInt(key, value);
#else
        Values[key] = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
#endif
    }
}
