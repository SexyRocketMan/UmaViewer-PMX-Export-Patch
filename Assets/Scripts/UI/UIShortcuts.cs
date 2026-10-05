using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// The viewer's keyboard shortcuts. Every key is one <see cref="Bind"/> call in the constructor, so a
/// new shortcut is one more line there and nothing else.
/// </summary>
public class UIShortcuts
{
    /// <summary>How long a repeatable key has to be held before it starts repeating.</summary>
    private const float RepeatDelay = 0.5f;

    /// <summary>How often a held key repeats. The list loads every entry it lands on, so it is brisk.</summary>
    private const float RepeatInterval = 0.03f;

    private readonly UmaViewerUI ui;
    private readonly List<Binding> bindings = new List<Binding>();

    private class Binding
    {
        public KeyCode Key;
        public bool Alt;
        public bool Repeat;
        public Action Action;

        /// <summary>Unscaled time this may fire again at, or 0 while the key is not held.</summary>
        public float NextRepeat;
    }

    public UIShortcuts(UmaViewerUI ui)
    {
        this.ui = ui;
        Bind(KeyCode.LeftArrow, () => CyclePanel(-1));
        Bind(KeyCode.RightArrow, () => CyclePanel(1));
        BindListWalk(-1, KeyCode.UpArrow, KeyCode.W);
        BindListWalk(1, KeyCode.DownArrow, KeyCode.S);

        // the same calls the export model button and the auto record button make
        Bind(KeyCode.E, () => ui.ModelSettings.ExportModel());
        Bind(KeyCode.A, ui.AutoRecordVMD);
    }

    /// <summary>
    /// Binds one direction of the list walk to every key that drives it: the arrows and, since the hand on
    /// the mouse is usually the right one, W and S as well.
    /// </summary>
    private void BindListWalk(int step, params KeyCode[] keys)
    {
        foreach (var key in keys)
            Bind(key, () => MoveSelection(step), repeat: true);
    }

    public void Update()
    {
        if (TypingInField())
        {
            ReleaseAll();
            return;
        }

        bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
        foreach (var binding in bindings)
        {
            // exact modifier match, so a binding needing alt only fires with alt held and a plain one
            // only fires without it
            if (binding.Alt != alt || !Input.GetKey(binding.Key))
            {
                binding.NextRepeat = 0;
                continue;
            }

            if (Input.GetKeyDown(binding.Key))
            {
                Fire(binding);
                binding.NextRepeat = binding.Repeat ? Time.unscaledTime + RepeatDelay : 0;
            }
            else if (binding.NextRepeat > 0 && Time.unscaledTime >= binding.NextRepeat)
            {
                Fire(binding);
                binding.NextRepeat = Time.unscaledTime + RepeatInterval;
            }
        }
    }

    private void Bind(KeyCode key, Action action, bool alt = false, bool repeat = false) =>
        bindings.Add(new Binding { Key = key, Alt = alt, Repeat = repeat, Action = action });

    /// <summary>
    /// The arrow keys are also Unity's own UI navigation, so whatever button was selected is dropped
    /// first, or the selection would move at the same time as the shortcut does its own thing.
    /// </summary>
    private static void Fire(Binding binding)
    {
        if (EventSystem.current) EventSystem.current.SetSelectedGameObject(null);
        binding.Action();
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
