using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Trims a list down to the rows matching what is typed into a search box, by hiding the rest. It works
/// on the rows the list already has, so unlike <see cref="PageManager"/> it needs no entries, no row
/// prefab and no paging, and it suits every list - the ones built once at startup as well as the ones
/// rebuilt whenever a character is picked.
/// </summary>
public class ListSearch : MonoBehaviour
{
    /// <summary>The list to trim: the rows are the direct children of its scroll rect.</summary>
    public ScrollRect List;

    /// <summary>The box to read, defaulting to one on this object or below it. Wired up in code.</summary>
    public InputField Input;

    /// <summary>Rows hidden by the current search, so that clearing it restores only those.</summary>
    private readonly List<GameObject> hidden = new List<GameObject>();

    private int lastRowCount = -1;

    private void OnEnable()
    {
        if (!Input) Input = GetComponentInChildren<InputField>(true);
        if (Input)
        {
            Input.interactable = true;
            Input.onValueChanged.AddListener(OnSearch);
        }
        else
        {
            Debug.LogWarning($"{name}: no InputField to search with.", this);
        }
        if (!List) Debug.LogWarning($"{name}: no list assigned, so the search box does nothing.", this);
    }

    private void OnDisable()
    {
        if (Input) Input.onValueChanged.RemoveListener(OnSearch);
    }

    /// <summary>Trims the list and goes back to the top of what is left.</summary>
    public void OnSearch(string term)
    {
        Apply(term);
        if (List) List.verticalNormalizedPosition = 1;
    }

    /// <summary>
    /// A list that is rebuilt under a live search - picking a different character rebuilds its costume
    /// list - comes back with rows that were never filtered, so watch the row count and filter again.
    /// </summary>
    private void LateUpdate()
    {
        int rowCount = List ? List.content.childCount : 0;
        if (rowCount == lastRowCount) return;
        lastRowCount = rowCount;
        Apply(Input ? Input.text : "");
    }

    private void Apply(string term)
    {
        if (!List) return;
        hidden.RemoveAll(row => !row);

        var content = List.content;
        for (int i = 0; i < content.childCount; i++)
        {
            // anything that is not a row is left exactly as the list built it
            var row = content.GetChild(i).GetComponent<UmaUIContainer>();
            if (row) Show(row, string.IsNullOrEmpty(term) || row.Matches(term));
        }
    }

    private void Show(UmaUIContainer row, bool shown)
    {
        if (shown)
        {
            if (hidden.Remove(row.gameObject)) row.gameObject.SetActive(true);
        }
        else if (!hidden.Contains(row.gameObject))
        {
            hidden.Add(row.gameObject);
            row.gameObject.SetActive(false);
        }
    }
}
