using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

public class PageManager : MonoBehaviour
{
    public ScrollRect ScrollRect;
    public InputField PageText;
    public InputField SearchText;
    public int ShowCount;
    public GameObject ContainerPrefab;

    /// <summary>Tint of the row the keyboard selection sits on, for containers with no highlight image.</summary>
    public Color SelectionColor = new Color(1f, 0.82f, 0.45f, 1f);

    /// <summary>
    /// An optional second list the walk goes through before it leaves an entry: the clips inside the awb
    /// a sounds entry picked. Whoever owns that list draws its rows, so they are only clicked from here.
    /// Set from code rather than in the inspector, because this component sits on a prefab that every
    /// paged list shares and a value there would apply to all of them at once.
    /// </summary>
    public ScrollRect SubList { get; set; }

    private int totalPage;
    private int currentPage = 0;
    private List<Entry> Entries;
    private List<Entry> SubEntries;

    /// <summary>Rows drawn right now, in display order, so keyboard selection can walk them.</summary>
    private readonly List<Row> Rows = new List<Row>();
    private int selectedRow = -1;

    /// <summary>Which of the sub list's rows is selected, or -1 while the selection is on an entry.</summary>
    private int selectedSub = -1;

    /// <summary>Live page managers, so a shortcut can find the list inside the panel on screen.</summary>
    private static readonly List<PageManager> Instances = new List<PageManager>();

    private class Row
    {
        public UmaUIContainer Container;
        public Entry Entry;
    }

    private void OnEnable() => Instances.Add(this);
    private void OnDisable() => Instances.Remove(this);

    /// <summary>
    /// The page manager driving a list shown inside <paramref name="panel"/>, or null. A panel can hold
    /// several lists and only the one on screen counts: the characters panel keeps the mob list around
    /// while the normal one is showing.
    /// </summary>
    public static PageManager ActiveUnder(GameObject panel)
    {
        if (!panel) return null;
        foreach (var instance in Instances)
        {
            if (instance && instance.ScrollRect && instance.ScrollRect.gameObject.activeInHierarchy
                && instance.ScrollRect.transform.IsChildOf(panel.transform))
                return instance;
        }
        return null;
    }

    private bool Searching => SearchText && SearchText.text != "";

    /// <summary>The entries on show, which are the search results only while a search is typed.</summary>
    private List<Entry> Shown => Searching ? SubEntries : Entries;

    public void Initialize(List<Entry> entries, ScrollRect scrollRect)
    {
        ScrollRect = scrollRect;
        Entries = entries;
        if (SearchText)
        {
            SearchText.text = "";
            SearchText.interactable = false;
        }
        ShowCount = Mathf.Max(1, Mathf.FloorToInt(ScrollRect.GetComponent<RectTransform>().rect.height / ContainerPrefab.GetComponent<RectTransform>().rect.height));
        totalPage = Mathf.CeilToInt(entries.Count / (float)ShowCount);
        if (totalPage > 0)
        {
            currentPage = 0;
            LoadPage(currentPage);
            if (SearchText)
                SearchText.interactable = true;
        }
        else
        {
            Clear();
        }
    }

    public void AddEntries(Entry entry)
    {
        if (!ScrollRect) return;

        if (SearchText)
            SearchText.text = "";
        Entries.Add(entry);
        totalPage = Mathf.CeilToInt(Entries.Count / (float)ShowCount);
        if (currentPage == totalPage - 1)
        {
            LoadPage(currentPage);
        }
        else
        {
            ((Text)PageText.placeholder).text = $"{currentPage + 1} / {totalPage}";
        }
    }

    public void RemoveEntry(Entry entry)
    {
        if (!ScrollRect) return;

        if (SearchText)
            SearchText.text = "";
        Entries.Remove(entry);
        totalPage = Mathf.CeilToInt(Entries.Count / (float)ShowCount);
        if (currentPage > totalPage - 1)
        {
            currentPage = Mathf.Max(totalPage - 1, 0);
            LoadPage(currentPage);
        }
        else
        {
            ((Text)PageText.placeholder).text = $"{currentPage + 1} / {totalPage}";
        }
    }

    public void ResetCtrl()
    {
        totalPage = 0;
        currentPage = 0;
        PageText.text = "";
        if (SearchText)
        {
            SearchText.text = "";
            SearchText.interactable = false;
        }
        SubEntries = null;
        Entries = null;
        Rows.Clear();
        selectedRow = -1;
        selectedSub = -1;
        ((Text)PageText.placeholder).text = $"- / -";
    }

    private void LoadPage(int index)
    {
        if (totalPage == 0) return;
        var entries = Shown;
        Clear();
        PageText.text = "";
        ((Text)PageText.placeholder).text = $"{currentPage + 1} / {totalPage}";
        var start = ShowCount * index;
        for (int i = 0;i< ShowCount; i++)
        {
            if (start + i >= entries.Count) break;
            var entry = entries[start + i];
            AddRow(entry.Name, entry, container => entry.OnClick(container));
        }
    }

    /// <summary>Moves the selection by <paramref name="step"/> and loads what it lands on.</summary>
    public void MoveSelection(int step)
    {
        var entries = Shown;
        if (entries == null || entries.Count == 0) return;

        if (step > 0) MoveForward(entries);
        else MoveBackward(entries);
    }

    /// <summary>
    /// Forward goes into the selected entry's sub rows first and plays each of them, and only once they
    /// run out moves on to the next entry. That is what lets a held key get through everything the list
    /// holds, clips included, rather than stopping at the entry they belong to.
    /// </summary>
    private void MoveForward(List<Entry> entries)
    {
        var subRows = SubRows();
        int current = SelectedSubRow(subRows);
        if (current < 0) current = selectedSub;

        if (current + 1 < subRows.Count)
        {
            SelectSubRow(subRows, current + 1);
            return;
        }
        MoveEntry(entries, 1);
    }

    /// <summary>
    /// Back is the exact reverse of forward: the previous row of the sub list, and once those run out it
    /// steps back an entry and lands on the *last* row of that one. Stepping out to the entry row first,
    /// the way this used to, is what made going back cost two keystrokes per entry.
    /// </summary>
    private void MoveBackward(List<Entry> entries)
    {
        var subRows = SubRows();
        int current = SelectedSubRow(subRows);
        if (current < 0) current = selectedSub;

        if (current > 0)
        {
            SelectSubRow(subRows, current - 1);
            return;
        }

        MoveEntry(entries, -1);
        var previousSubRows = SubRows();
        if (previousSubRows.Count > 0)
            SelectSubRow(previousSubRows, previousSubRows.Count - 1);
    }

    /// <summary>Moves one entry, turning the page when it walks off the one on screen, and loads it.</summary>
    private void MoveEntry(List<Entry> entries, int step)
    {
        // The first press starts at the top (or the bottom) of the page on screen, so paging and then
        // pressing a key carries on from what is being looked at. After that the selection is one index
        // through the whole list, which is what carries it over a page boundary.
        int index = selectedRow < 0
            ? currentPage * ShowCount + (step > 0 ? 0 : Rows.Count - 1)
            : currentPage * ShowCount + selectedRow + step;
        index = (index % entries.Count + entries.Count) % entries.Count;

        if (index / ShowCount != currentPage)
        {
            currentPage = index / ShowCount;
            LoadPage(currentPage);              // the rows on screen belong to a different page now
        }
        if (Rows.Count == 0) return;

        selectedRow = Mathf.Min(index % ShowCount, Rows.Count - 1);
        selectedSub = -1;
        HighlightSelection();

        var row = Rows[selectedRow];
        if (row.Entry != null)
            row.Entry.OnClick(row.Container);

        // Loading an entry usually redraws its sub rows. Going forward means the key that landed on the
        // entry also plays the first of them, so one keystroke is enough to start on the sound; going
        // back instead stops on the entry, so stepping up the list stays silent.
        var subRows = SubRows();
        ClearSubHighlight(subRows);
        if (step > 0 && subRows.Count > 0)
            SelectSubRow(subRows, 0);
    }

    /// <summary>
    /// Walks one sub row. Those rows are drawn by whoever owns that list rather than here, so a row is
    /// selected by clicking it: that is what highlights it and, for the sounds list, plays it.
    /// </summary>
    private void SelectSubRow(List<UmaUIContainer> subRows, int index)
    {
        if (index < 0 || index >= subRows.Count) return;

        selectedSub = index;
        var row = subRows[index];
        if (row.Button) row.Button.onClick.Invoke();
        ScrollRowIntoView(SubList, row.transform);
    }

    /// <summary>The sub list's rows, or an empty list when there is no sub list.</summary>
    private List<UmaUIContainer> SubRows()
    {
        var rows = new List<UmaUIContainer>();
        if (!SubList || !SubList.content) return rows;

        var content = SubList.content;
        for (int i = 0; i < content.childCount; i++)
        {
            var row = content.GetChild(i).GetComponent<UmaUIContainer>();
            // Only rows that are on screen: a hidden one is either filtered out by a search or on its way
            // out after the list was redrawn, and the walk must not land on either.
            if (row && row.gameObject.activeSelf) rows.Add(row);
        }
        return rows;
    }

    /// <summary>
    /// Which sub row is showing as selected, or -1. Reading it back off the highlights keeps the walk in
    /// step with a click on the list, which the page manager never sees.
    /// </summary>
    private static int SelectedSubRow(List<UmaUIContainer> subRows)
    {
        for (int i = 0; i < subRows.Count; i++)
        {
            var highlight = subRows[i].ToggleImage;
            if (highlight && highlight.enabled) return i;
        }
        return -1;
    }

    private static void ClearSubHighlight(List<UmaUIContainer> subRows)
    {
        foreach (var row in subRows)
        {
            if (row.ToggleImage) row.ToggleImage.enabled = false;
        }
    }

    /// <summary>
    /// Marks the selected row: the container's own highlight image where it has one, plus Unity's
    /// selected tint, which is all the containers shared by the props, scenes and sounds lists have.
    /// </summary>
    private void HighlightSelection()
    {
        for (int i = 0; i < Rows.Count; i++)
        {
            var highlight = Rows[i].Container.ToggleImage;
            if (highlight) highlight.enabled = i == selectedRow;
        }
        var button = Rows[selectedRow].Container.Button;
        if (button) button.Select();
        ScrollRowIntoView(ScrollRect, Rows[selectedRow].Container.transform);
    }

    /// <summary>Scrolls a row into view. Rows share a height, so its sibling index * height is its offset.</summary>
    private static void ScrollRowIntoView(ScrollRect list, Transform row)
    {
        if (!list || !list.viewport || !row) return;

        var content = (RectTransform)list.content;
        float rowHeight = ((RectTransform)row).rect.height;
        float contentHeight = content.rect.height;
        float viewHeight = list.viewport.rect.height;
        if (rowHeight <= 0 || contentHeight <= viewHeight) return;

        float overflow = contentHeight - viewHeight;
        float fromTop = (row.GetSiblingIndex() + 0.5f) * rowHeight;         // content top to the row's centre
        float scroll = Mathf.Clamp(fromTop - viewHeight * 0.5f, 0, overflow);
        list.verticalNormalizedPosition = 1 - scroll / overflow;            // 1 is the top of the list
    }

    private UmaUIContainer AddRow(string text, Entry entry, Action<UmaUIContainer> onClick)
    {
        var container = Instantiate(ContainerPrefab, ScrollRect.content).GetComponent<UmaUIContainer>();
        container.Name = container.name = text;
        if (entry != null)
        {
            if (entry.FontSize > 0)
            {
                container.FontSize = entry.FontSize;
            }
            if (entry.Sprite != null && container.Image)
            {
                container.Image.sprite = entry.Sprite;
                container.Image.enabled = true;
            }
        }
        if (container.Button)
        {
            // Rows take no part in Unity's own keyboard navigation, which would otherwise move the
            // selection to the next row at the same time as the shortcut walks the list. The shared
            // container's selected tint is only 4% off white, so it gets a readable one instead.
            var colors = container.Button.colors;
            colors.selectedColor = SelectionColor;
            container.Button.colors = colors;
            container.Button.navigation = new Navigation { mode = Navigation.Mode.None };
            container.Button.onClick.AddListener(() => Activate(entry, container, onClick));
        }
        Rows.Add(new Row { Container = container, Entry = entry });
        return container;
    }

    private void Activate(Entry entry, UmaUIContainer container, Action<UmaUIContainer> onClick)
    {
        if (entry != null)
        {
            // a click means the entry itself is what the keys should carry on from
            selectedRow = Rows.FindIndex(row => row.Container == container);
            selectedSub = -1;
        }
        onClick(container);
    }

    public void JumpToPage(string index)
    {
        PageText.text = "";
        if (totalPage == 0|| index == "") return;
        currentPage = Mathf.Clamp(int.Parse(index) - 1, 0, totalPage - 1);
        LoadPage(currentPage);
    }

    public void OnSearch(string val)
    {
        Clear();
        if (Entries == null) return;
        if(val == "")
        {
            Initialize(Entries, ScrollRect);
        }
        else
        {
            SubEntries = new List<Entry>(Entries.Where(a => a.Matches(val)));
            ShowCount = Mathf.Max(1, Mathf.FloorToInt(ScrollRect.GetComponent<RectTransform>().rect.height / ContainerPrefab.GetComponent<RectTransform>().rect.height));
            totalPage = Mathf.CeilToInt(SubEntries.Count / (float)ShowCount);
            if (totalPage > 0)
            {
                currentPage = 0;
                LoadPage(currentPage);
            }
        }
    }

    public void Clear()
    {
        if (!ScrollRect) return;
        for (int i = ScrollRect.content.childCount - 1; i >= 0; i--)
        {
            Destroy(ScrollRect.content.GetChild(i).gameObject);
        }
        Rows.Clear();
        selectedRow = -1;
        selectedSub = -1;
    }

    public void NextPage()
    {
        if (totalPage == 0) return;
        currentPage = Mathf.Clamp(currentPage + 1, 0, totalPage - 1);
        LoadPage(currentPage);
    }

    public void PrevPage()
    {
        if (totalPage == 0) return;
        currentPage = Mathf.Clamp(currentPage - 1, 0, totalPage - 1);
        LoadPage(currentPage);
    }

    public class Entry
    {
        public string Name;
        /// <summary>Extra text a search matches as well, for what the name does not show: id, other languages.</summary>
        public string Keywords;
        public int FontSize;
        public Sprite Sprite;
        public UnityEngine.Events.UnityAction<UmaUIContainer> OnClick;

        /// <summary>True when the entry's name or keywords contain <paramref name="term"/>.</summary>
        public bool Matches(string term) =>
            UmaUIContainer.Contains(Name, term) || UmaUIContainer.Contains(Keywords, term);
    }
}
