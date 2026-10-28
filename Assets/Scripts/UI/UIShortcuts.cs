using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// The viewer's keyboard shortcuts.
///
/// The <b>keys</b> live in `Config.json` (<see cref="Config.ViewerShortcuts"/>), so they can be rebound
/// without a build. The <b>actions</b> and the rules about when each one applies stay here, because a
/// shortcut is only useful when it does not fight the mode the viewer is in:
///
///   * a text box has focus - every shortcut is off, the field owns the keyboard (see
///     <see cref="TypingInField"/>);
///   * the camera is in free mode - a shortcut on a key the camera flies with is off, because the camera
///     is being flown and <see cref="CameraOrbit"/> already reads that key as an axis (see
///     <see cref="CameraKeys"/>). Export and the rain look do not collide, so they keep working;
///   * pose mode is on - navigation is off, because the list walk loads whatever it lands on and would
///     replace the model being posed mid-pose, and so is recording, because
///     <see cref="PoseManager.EnablePoseMode"/> disables the animator the recorder samples.
///
/// Each entry in the config list is one key, so the same action can have several (the list walk has an
/// arrow and a letter each by default). An action whose name is not in <see cref="Definitions"/> is
/// reported and skipped rather than silently doing nothing.
/// </summary>
public class UIShortcuts
{
    /// <summary>How long a repeatable key has to be held before it starts repeating.</summary>
    private const float RepeatDelay = 0.5f;

    /// <summary>How often a held key repeats. The list loads every entry it lands on, so it is brisk.</summary>
    private const float RepeatInterval = 0.03f;

    /// <summary>
    /// The keys the free camera reads as movement: <see cref="CameraOrbit"/>'s free mode is driven by the
    /// Horizontal and Vertical axes, which are WASD and the arrow keys, so those keys are the camera's
    /// while it is flying. Matched against the key a shortcut was actually given, so rebinding a shortcut
    /// to a key the camera does not use makes it work in free mode again.
    /// </summary>
    private static readonly HashSet<KeyCode> CameraKeys = new HashSet<KeyCode>
    {
        KeyCode.W, KeyCode.A, KeyCode.S, KeyCode.D,
        KeyCode.LeftArrow, KeyCode.RightArrow, KeyCode.UpArrow, KeyCode.DownArrow,
    };

    /// <summary>What a shortcut does, which decides what suspends it (see the class summary).</summary>
    private enum Scope
    {
        /// <summary>Changes what is loaded or shown: the panel cycle and the list walk.</summary>
        Navigation,

        /// <summary>Does something to what is already loaded.</summary>
        Action,
    }

    private class Definition
    {
        public Action Run;
        public Scope Scope;
        public bool Repeat;
        /// <summary>True for actions that need the character's animator running.</summary>
        public bool NeedsAnimator;
    }

    private class Binding
    {
        public KeyCode Key;
        public bool Alt;
        public Definition Definition;
        public string Name;

        /// <summary>Unscaled time this may fire again at, or 0 while the key is not held.</summary>
        public float NextRepeat;
    }

    private readonly UmaViewerUI ui;
    private readonly List<Binding> bindings = new List<Binding>();

    public UIShortcuts(UmaViewerUI ui)
    {
        this.ui = ui;

        var definitions = Definitions();
        var wanted = Config.Instance != null ? Config.Instance.ViewerShortcuts : null;
        if (wanted == null || wanted.Count == 0) wanted = Config.DefaultViewerShortcuts();

        foreach (var entry in wanted)
        {
            if (entry == null || string.IsNullOrEmpty(entry.Name)) continue;

            if (!definitions.TryGetValue(entry.Name, out Definition definition))
            {
                Debug.LogWarning($"[Shortcuts] Config.json names an action this build does not have: "
                                 + $"'{entry.Name}'. Known: {string.Join(", ", definitions.Keys)}");
                continue;
            }

            // KeyCode is parsed from a name rather than serialised as a number: Config.json is meant to be
            // edited by hand, and "R" says what "114" does not.
            if (!Enum.TryParse(entry.Key, true, out KeyCode key) || key == KeyCode.None)
            {
                Debug.LogError($"[Shortcuts] Config.json gives '{entry.Name}' an unknown key '{entry.Key}'; "
                               + "that shortcut is off. Use a UnityEngine.KeyCode name, e.g. R, E, UpArrow.");
                continue;
            }

            bindings.Add(new Binding { Key = key, Alt = entry.Alt, Definition = definition, Name = entry.Name });
        }

        Debug.Log($"[Shortcuts] {bindings.Count} binding(s): "
                  + string.Join(", ", bindings.ConvertAll(b =>
                        $"{(b.Alt ? "Alt+" : "")}{b.Key}{(b.Definition.Repeat ? " (repeat)" : "")} -> {b.Name}")));
    }

    /// <summary>
    /// Every action a shortcut can be pointed at. The list walk repeats while held; nothing else does.
    /// </summary>
    private Dictionary<string, Definition> Definitions() =>
        new Dictionary<string, Definition>(StringComparer.OrdinalIgnoreCase)
        {
            { "PanelPrev", new Definition { Run = () => CyclePanel(-1), Scope = Scope.Navigation } },
            { "PanelNext", new Definition { Run = () => CyclePanel(1), Scope = Scope.Navigation } },
            { "ListUp", new Definition { Run = () => MoveSelection(-1), Scope = Scope.Navigation, Repeat = true } },
            { "ListDown", new Definition { Run = () => MoveSelection(1), Scope = Scope.Navigation, Repeat = true } },

            // the same calls the export model button and the auto record button make
            { "ExportModel", new Definition { Run = () => ui.ModelSettings.ExportModel(), Scope = Scope.Action } },
            { "AutoRecord", new Definition { Run = () => ui.AutoRecordVMD(), Scope = Scope.Action, NeedsAnimator = true } },

            // the rain look: the wet texture set the game uses when a race is rained on
            { "RainLook", new Definition { Run = () => ui.ModelSettings.ToggleWetLook(), Scope = Scope.Action } },

            // a measurement knob, not a feature - see CycleDirtRates
            { "DirtRates", new Definition { Run = CycleDirtRates, Scope = Scope.Action } },
        };

    /// <summary>The steps the dirt knob walks: off, then quarters up to full.</summary>
    private static readonly float[] DirtSteps = { 0f, 0.25f, 0.5f, 0.75f, 1f };

    private int dirtStep = -1;

    /// <summary>
    /// A measurement knob, <b>not</b> the dirty look. The game's dirt is a shader slot - `_DirtTex`, unbound
    /// on every material this project exports - plus a rate that exists twice: per material as a vector, and
    /// globally as this three-float array. Nothing in the repo says what the rate should be, because nobody
    /// has watched the game apply it, so this steps the globals through 0, 0.25, 0.5, 0.75 and 1 and logs
    /// what it set. Once the response has been seen, a real `DirtyLook` action can pick numbers.
    ///
    /// Set here rather than through <see cref="UmaViewerGlobalShader"/> because that component pushes its
    /// fields in `Start()` only - changing them later would not reach the shader.
    /// </summary>
    private void CycleDirtRates()
    {
        dirtStep = (dirtStep + 1) % DirtSteps.Length;
        float rate = DirtSteps[dirtStep];
        Shader.SetGlobalFloatArray("_DirtRate", new[] { rate, rate, rate });
        Debug.Log($"[Dirt] global _DirtRate = {rate} (step {dirtStep + 1}/{DirtSteps.Length}, all three zones). "
                  + "Materials keep _DirtTex unbound, so a rate alone may show nothing: the per-material "
                  + "side is a Vector named _DirtRate plus a _DirtScale, and the map is the tex_*_dirt asset. "
                  + "See docs/HANDOFF_OPTIONS_AND_RELEASE.md.");
    }

    public void Update()
    {
        if (TypingInField())
        {
            ReleaseAll();
            return;
        }

        bool posing = ui.PoseManager != null && ui.PoseManager.PoseModeOn;
        bool flying = IsFreeCamera();
        bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);

        foreach (var binding in bindings)
        {
            // exact modifier match, so a binding needing alt only fires with alt held and a plain one
            // only fires without it. A suspended binding drops its hold, so releasing context cannot
            // resume a repeat that started in the other mode.
            if (Suspended(binding, posing, flying) || binding.Alt != alt || !Input.GetKey(binding.Key))
            {
                binding.NextRepeat = 0;
                continue;
            }

            if (Input.GetKeyDown(binding.Key))
            {
                Fire(binding);
                binding.NextRepeat = binding.Definition.Repeat ? Time.unscaledTime + RepeatDelay : 0;
            }
            else if (binding.NextRepeat > 0 && Time.unscaledTime >= binding.NextRepeat)
            {
                Fire(binding);
                binding.NextRepeat = Time.unscaledTime + RepeatInterval;
            }
        }
    }

    /// <summary>
    /// Whether the mode the viewer is in owns this key right now. See the class summary for the three
    /// cases and why each one is what it is.
    /// </summary>
    private static bool Suspended(Binding binding, bool posing, bool flying)
    {
        if (flying && CameraKeys.Contains(binding.Key)) return true;
        if (posing && (binding.Definition.Scope == Scope.Navigation || binding.Definition.NeedsAnimator)) return true;
        return false;
    }

    /// <summary>
    /// The camera is in free mode. Read through the settings the camera itself follows
    /// (<see cref="CameraOrbit.Update"/> compares `CameraMode` against <see cref="UISettingsCamera.CameraMode"/>
    /// every frame), with a guard for a scene that has not assigned the dropdown - the same assignment
    /// <see cref="CameraOrbit"/> needs, so this cannot be the only thing that breaks.
    /// </summary>
    private bool IsFreeCamera()
    {
        var settings = ui.CameraSettings;
        return settings != null && settings.CameraModeDropdown != null && settings.CameraMode == 1;
    }

    /// <summary>
    /// The arrow keys are also Unity's own UI navigation, so whatever button was selected is dropped
    /// first, or the selection would move at the same time as the shortcut does its own thing.
    /// </summary>
    private static void Fire(Binding binding)
    {
        if (EventSystem.current) EventSystem.current.SetSelectedGameObject(null);
        binding.Definition.Run();
    }

    /// <summary>Forgets every in-progress hold, so a repeat does not carry over into a search box.</summary>
    private void ReleaseAll()
    {
        foreach (var binding in bindings) binding.NextRepeat = 0;
    }

    /// <summary>
    /// True while a text box has focus. It owns the arrow keys and every letter, so without this the
    /// search bar would switch panels or walk the list as it was typed into.
    /// </summary>
    private static bool TypingInField()
    {
        var selected = EventSystem.current ? EventSystem.current.currentSelectedGameObject : null;
        if (!selected) return false;

        var field = selected.GetComponent<InputField>();
        if (field) return field.isFocused;
        var textMeshField = selected.GetComponent<TMPro.TMP_InputField>();
        return textMeshField && textMeshField.isFocused;
    }

    /// <summary>Shows the next panel of <see cref="UmaViewerUI.TogglablePanels"/>, wrapping at the ends.</summary>
    private void CyclePanel(int step)
    {
        var panels = ui.TogglablePanels;
        if (panels == null || panels.Count == 0) return;

        int current = panels.FindIndex(panel => panel && panel.activeSelf);
        int next = current < 0
            ? (step > 0 ? 0 : panels.Count - 1)
            : (current + step % panels.Count + panels.Count) % panels.Count;
        if (next != current)
            ui.ToggleUIPanel(panels[next]);
    }

    /// <summary>
    /// Walks the list of the panel on screen, loading each entry it lands on, which is what makes the
    /// keys a way to browse.
    /// </summary>
    private void MoveSelection(int step)
    {
        var panel = ActivePanel();
        if (!panel || SkipsKeyBrowsing(panel)) return;

        PageManager.ActiveUnder(panel)?.MoveSelection(step);
    }

    /// <summary>
    /// A character takes long enough to load that stepping through them one keypress at a time is no way
    /// to compare them, so the characters panel is the one panel the list keys leave alone.
    /// </summary>
    private bool SkipsKeyBrowsing(GameObject panel) => panel == ui.SelectCharacterPannel;

    private GameObject ActivePanel() =>
        ui.TogglablePanels?.Find(candidate => candidate && candidate.activeSelf);
}
