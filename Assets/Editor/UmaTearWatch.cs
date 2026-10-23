#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Watches the animated tears in a normally running editor session, for when batch mode disagrees with what the
/// scene view shows.
///
/// Toggle it from **Tools &gt; Uma Tear Watch**, press play, load a character and raise a `Tear*_Ctrl` weight in
/// the morph panel. It appends a sample five times a second to `C:\tmp\uma_tear_watch.txt`: the controller's own
/// fields (weight, speed, which object is selected, which eye), the animator's state time and culling mode, and the
/// posed extent of the card through `BakeMesh`. A card that visibly rolls should show its size change there; if it
/// does not, the deformation is coming from somewhere else entirely.
///
/// The interesting columns to compare once it has run:
///   - `speed=` should be non-zero. `TearController.SetSpeed` writes `Animator.speed`, so a `TearSpeed` slider
///     left at 0 stops the animator completely.
///   - `culling=` is `CullUpdateTransforms` by default, and in that mode an animator skips writing bone
///     transforms while the renderer is not drawn - which is the most likely reason batch mode measured the card
///     as rigid. The watcher forces `AlwaysAnimate` while it is running, so the two rows either side of a toggle
///     are worth comparing.
/// </summary>
[InitializeOnLoad]
public static class UmaTearWatch
{
    private const string MenuPath = "Tools/Uma Tear Watch";
    private const string PrefKey = "UmaTearWatch.enabled";
    private const string OutputPath = @"C:\tmp\uma_tear_watch.txt";

    private static bool _enabled;
    private static readonly StringBuilder _buffer = new StringBuilder();
    private static int _frameCounter;

    static UmaTearWatch()
    {
        _enabled = EditorPrefs.GetBool(PrefKey, false);
        EditorApplication.update += Update;
    }

    [MenuItem(MenuPath)]
    private static void Toggle()
    {
        _enabled = !_enabled;
        EditorPrefs.SetBool(PrefKey, _enabled);
        if (_enabled)
        {
            _buffer.Clear();
            _buffer.AppendLine("# uma tear watch " + DateTime.Now.ToString("s", CultureInfo.InvariantCulture));
        }
        Flush();
        Debug.Log($"[tearwatch] {(_enabled ? "on" : "off")} - writing {OutputPath}");
    }

    [MenuItem(MenuPath, true)]
    private static bool ToggleValidate() => true;

    private static void Update()
    {
        if (!_enabled) return;
        // five samples a second is plenty and keeps the file readable
        if (++_frameCounter % 12 != 0) return;
        if (!Application.isPlaying) return;

        foreach (var container in UnityEngine.Object.FindObjectsOfType<UmaContainerCharacter>())
        {
            if (container == null || container.TearControllers == null) continue;
            for (int index = 0; index < container.TearControllers.Count; index++)
            {
                TearController controller = container.TearControllers[index];
                if (controller == null) continue;
                GameObject part = controller.CurrenObject < 1 ? controller.TearObject_0 : controller.TearObject_1;
                if (part == null) continue;
                Animator animator = part.GetComponent<Animator>();
                string state = "no-animator";
                if (animator != null)
                {
                    // the default culling mode skips bond transforms for undrawn renderers; force the honest mode
                    if (animator.cullingMode != AnimatorCullingMode.AlwaysAnimate)
                    {
                        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    }
                    AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(0);
                    state = $"norm={F(info.normalizedTime)} len={F(info.length)} speed={F(animator.speed)} "
                            + $"culling={animator.cullingMode} active={part.activeSelf}";
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
                        string tip = bones.Length > 0 && bones[bones.Length - 1] != null
                            ? bones[bones.Length - 1].name + V(bones[bones.Length - 1].position)
                            : "none";
                        _buffer.AppendLine(
                            $"ctrl{index} '{part.name}' weight={F(controller.Weight)} "
                            + $"ctrlSpeed={F(controller.Speed)} dir={controller.CurrentDir} "
                            + $"object={controller.CurrenObject} | {state} | "
                            + $"size={V(max - min)} min={V(min)} max={V(max)} tip={tip}");
                    }
                    finally
                    {
                        UnityEngine.Object.DestroyImmediate(baked);
                    }
                }
            }
        }
        Flush();
    }

    private static void Flush()
    {
        if (_buffer.Length == 0) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(OutputPath) ?? ".");
            File.AppendAllText(OutputPath, _buffer.ToString());
            _buffer.Clear();
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[tearwatch] could not write {OutputPath}: {exception.Message}");
            _buffer.Clear();
        }
    }

    private static string F(float value) => value.ToString("0.000", CultureInfo.InvariantCulture);

    private static string V(Vector3 value) =>
        "(" + F(value.x) + "," + F(value.y) + "," + F(value.z) + ")";
}
#endif
