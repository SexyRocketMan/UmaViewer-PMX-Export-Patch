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
///   * pose mode is on - every shortcut is off, because pose mode owns the keyboard (see
///     <see cref="Suspended"/>);
///   * the camera is in free mode - a shortcut on a key the camera flies with is off, because the camera
///     is being flown and <see cref="CameraOrbit"/> already reads that key as an axis (see
///     <see cref="CameraKeys"/>). Matched against the key a shortcut was actually given, so a rebinding
///     onto a key the camera does not fly with keeps working.
///
/// Each entry in the config list is one key, so the same action can have several (the list walk has an
/// arrow and a letter each by default). An action whose name is not in <see cref="Definitions"/> is
/// reported and skipped rather than silently doing nothing, and so is an unknown key name.
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

    private class Definition
    {
        public Action Run;
        public bool Repeat;
    }

    private class Binding
    {
        public KeyCode Key;
        public bool Alt;
        public Definition Definition;
        public string Name;

        /// <summary>Unscaled time this may fire again at, or 0 while the key is not held.</summary>
        public float NextRepeat;

        /// <summary>How a binding is named in the log: the key it is on, then the action it runs.</summary>
        public string Label => $"{(Alt ? "Alt+" : "")}{Key} -> {Name}";
    }

    private readonly UmaViewerUI ui;
    private readonly List<Binding> bindings = new List<Binding>();

    public UIShortcuts(UmaViewerUI ui)
    {
        this.ui = ui;

        var definitions = Definitions();
        var wanted = ReadConfig();

        if (wanted.Count == 0)
        {
            Debug.LogWarning($"[Shortcuts] {Config.configPath} binds no keys at all: every shortcut is off. "
                             + "That is what an empty ViewerShortcuts list means - delete a line to drop one "
                             + "key, delete them all to drop every key.");
        }

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

        Debug.Log($"[Shortcuts] {bindings.Count} binding(s) from {Config.configPath}: "
                  + string.Join(", ", bindings.ConvertAll(b =>
                        $"{(b.Definition.Repeat ? "held " : "")}{b.Label}")));

        Probe();
    }

    /// <summary>
    /// The list of keys to bind: the one in Config.json, or the built-in defaults when the file has no list
    /// at all.
    ///
    /// The defaults used to be a *silent* fallback, and that is what hid the real defect for two releases:
    /// the shortcuts were built before the config existed, so they always bound these defaults while every
    /// run logged a plausible binding list. The fallback now has to say so, naming the file it read and the
    /// list it wanted.
    /// </summary>
    private static List<ShortcutConfig> ReadConfig()
    {
        var config = Config.Ensure();
        var wanted = config.ViewerShortcuts;
        if (wanted != null) return wanted;

        Debug.LogWarning($"[Shortcuts] {Config.configPath} has no ViewerShortcuts list, so the built-in "
                         + "defaults are used and no key can be rebound. Add the list back to that file.");
        return Config.DefaultViewerShortcuts();
    }

    /// <summary>
    /// Every action a shortcut can be pointed at. The list walk repeats while held; nothing else does.
    /// </summary>
    private Dictionary<string, Definition> Definitions() =>
        new Dictionary<string, Definition>(StringComparer.OrdinalIgnoreCase)
        {
            { "PanelPrev", new Definition { Run = () => CyclePanel(-1) } },
            { "PanelNext", new Definition { Run = () => CyclePanel(1) } },
            { "ListUp", new Definition { Run = () => MoveSelection(-1), Repeat = true } },
            { "ListDown", new Definition { Run = () => MoveSelection(1), Repeat = true } },

            // the same calls the export model button and the auto record button make
            { "ExportModel", new Definition { Run = () => ui.ModelSettings.ExportModel() } },
            { "AutoRecord", new Definition { Run = () => ui.AutoRecordVMD() } },

            // the rain look: the wet texture set the game uses when a race is rained on
            { "RainLook", new Definition { Run = () => ui.ModelSettings.ToggleWetLook() } },
        };

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
    /// Whether the mode the viewer is in owns this key right now. See the class summary for the cases and
    /// why each one is what it is.
    ///
    /// Pose mode suspends <b>everything</b>, which is narrower than it sounds and was the release's defect
    /// the other way round: the pose system's own keys are `H` (help), `Space` (IK toggle) and the
    /// Alt/Shift/Ctrl combinations <see cref="TransformGizmo"/> and <see cref="HandleManager"/> read, so
    /// none of them is a plain letter this class binds - and every action this class can run is wrong while
    /// a pose is being made. The list walk loads whatever it lands on and would replace the model mid-pose;
    /// export writes the model as it happens to be posed; recording samples the animator
    /// <see cref="PoseManager.EnablePoseMode"/> has just disabled; the rain look swaps the textures the pose
    /// is being judged under. Suspending only some of them left `E` and `R` live, which is exactly what a
    /// posed user hit.
    /// </summary>
    private static bool Suspended(Binding binding, bool posing, bool flying)
    {
        if (posing) return true;
        if (flying && CameraKeys.Contains(binding.Key)) return true;
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

    /// <summary>
    /// With `UMA_SHORTCUT_PROBE=1`, one line per binding per context saying whether it would fire. The
    /// contexts *are* the suspension rules, so this is the way to check them without flying the camera or
    /// posing a model by hand - the one thing the keybind release never did. `Tools/verify_keybinds.ps1`
    /// asserts on these lines.
    /// </summary>
    private void Probe()
    {
        if (System.Environment.GetEnvironmentVariable("UMA_SHORTCUT_PROBE") != "1") return;

        foreach (var binding in bindings)
        {
            foreach (var context in new[] { "idle", "flying", "posing" })
            {
                bool posing = context == "posing";
                bool flying = context == "flying";
                Debug.Log($"[Shortcuts] probe {context} {binding.Label} "
                          + $"{(Suspended(binding, posing, flying) ? "SUSPENDED" : "FIRE")}");
            }
        }
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
