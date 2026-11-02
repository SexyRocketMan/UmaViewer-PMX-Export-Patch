using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SFB;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Fills the prop panel's grid view: a card per prop, each showing the prop itself rather than its id.
///
/// The panel has two views of the same list - the text rows it started with, and this grid - and they share
/// everything except what a page is made of: the same `PageManager`, the same page controls, the same search
/// box, the same keyboard walk. That is deliberate. A second window with its own paging and its own search
/// would be two lists to keep in step, and the user would have to remember which one they were in.
///
/// What this adds on top of the list is only what a picture needs: a template for a card (the panel's own row,
/// made square, with a picture and a mark box), the stage that draws the pictures, and the set of cards marked
/// for export. `docs/PROP_BROWSER.md` has the measurements behind the layout.
/// </summary>
public class PropGrid : MonoBehaviour
{
    /// <summary>One grid per panel, made the first time the grid view is used.</summary>
    public static PropGrid Instance { get; private set; }

    private const float LabelHeight = 30f;
    private const float PickSize = 22f;
    private static readonly Color CardColour = new Color(1f, 1f, 1f, 0.06f);
    private static readonly Color CardSelectedColour = new Color(1f, 0.83f, 0.35f, 0.22f);
    private static readonly Color AccentColour = new Color(1f, 0.83f, 0.35f, 0.95f);
    private static readonly Color PickOffColour = new Color(0f, 0f, 0f, 0.55f);

    private UmaViewerUI _ui;
    private Font _font;
    private Color _labelColour = Color.white;

    private ScrollRect _list;
    private RectTransform _content;
    private GameObject _cardTemplate;
    private PropThumbnailStage _stage;
    private Coroutine _stageRoutine;

    private readonly List<UmaUIContainer> _cards = new List<UmaUIContainer>();
    private readonly HashSet<string> _selected = new HashSet<string>(StringComparer.Ordinal);
    private readonly Dictionary<string, UmaDatabaseEntry> _entries = new Dictionary<string, UmaDatabaseEntry>(StringComparer.Ordinal);

    /// <summary>The grid's own scroll view, for the page manager to draw into.</summary>
    public ScrollRect List => _list;

    /// <summary>The template cards are stamped from.</summary>
    public GameObject CardTemplate => _cardTemplate;

    /// <summary>The cards on screen, in page order. Read by the headless self-check.</summary>
    public IReadOnlyList<UmaUIContainer> Cards => _cards;

    /// <summary>The stage drawing the pictures, so the self-check can read its atlas back.</summary>
    public PropThumbnailStage Stage => _stage;

    /// <summary>How many props are marked for export.</summary>
    public int SelectedCount => _selected.Count;

    /// <summary>
    /// Builds the grid inside <paramref name="area"/>: a scroll view the page manager can draw cards into, sized
    /// by the area, and the card template built from the panel's own row prefab.
    /// </summary>
    public static PropGrid Create(UmaViewerUI ui, RectTransform area)
    {
        if (Instance != null) return Instance;
        if (ui == null || area == null) return null;

        var host = new GameObject("PropGrid", typeof(RectTransform));
        host.transform.SetParent(area, false);
        var rect = (RectTransform)host.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        var grid = host.AddComponent<PropGrid>();
        grid._ui = ui;
        grid.Build();
        Instance = grid;
        return grid;
    }

    private void Build()
    {
        _font = TakeRowFont();

        // the scroll view the page manager draws into: a viewport with a mask, and a content it can fill with
        var listObject = new GameObject("List", typeof(RectTransform), typeof(ScrollRect));
        listObject.transform.SetParent(transform, false);
        var listRect = (RectTransform)listObject.transform;
        listRect.anchorMin = Vector2.zero;
        listRect.anchorMax = Vector2.one;
        listRect.offsetMin = Vector2.zero;
        listRect.offsetMax = Vector2.zero;

        _list = listObject.GetComponent<ScrollRect>();
        _list.horizontal = false;
        _list.vertical = true;
        _list.movementType = ScrollRect.MovementType.Clamped;
        _list.scrollSensitivity = 40f;
        _list.inertia = true;

        var viewport = NewRect("Viewport", listObject.transform);
        viewport.anchorMin = Vector2.zero;
        viewport.anchorMax = Vector2.one;
        viewport.offsetMin = new Vector2(0f, 0f);
        viewport.offsetMax = new Vector2(-6f, 0f);      // a little room for the scrollbar-less edge
        viewport.gameObject.AddComponent<RectMask2D>();

        _content = NewRect("Content", viewport);
        _content.anchorMin = new Vector2(0f, 1f);
        _content.anchorMax = new Vector2(1f, 1f);
        _content.pivot = new Vector2(0.5f, 1f);
        _content.offsetMin = Vector2.zero;
        _content.offsetMax = Vector2.zero;
        var fitter = _content.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        _list.viewport = viewport;
        _list.content = _content;

        _cardTemplate = BuildCardTemplate();
        Debug.Log($"[PropGrid] built inside {((RectTransform)transform.parent).rect.size}");
    }

    private Font TakeRowFont()
    {
        var prefab = _ui != null ? _ui.UmaContainerPrefab : null;
        var sample = prefab != null ? prefab.GetComponentInChildren<Text>(true) : null;
        if (sample != null)
        {
            _labelColour = sample.color;
            if (sample.font != null) return sample.font;
        }
        return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }

    // ---------------------------------------------------------------------------------------- the page

    /// <summary>
    /// Called by the page manager once a page of cards exists: remembers which props they are, wires each card's
    /// picture slot to its square of the atlas, and hands the page to the stage.
    /// </summary>
    public void OnPageDrawn(List<PageManager.Entry> page, int startIndex)
    {
        EnsureStage();
        if (_stage == null) return;

        var entries = new List<UmaDatabaseEntry>();
        foreach (var entry in page)
        {
            if (entry != null && _entries.TryGetValue(entry.Id ?? "", out var manifest)) entries.Add(manifest);
        }

        RefreshSelectionVisuals();
        Debug.Log($"[PropGrid] page at {startIndex}: {_cards.Count} card(s), {entries.Count} prop(s) to draw "
                  + $"for a {_stage.Columns}x{_stage.Rows} stage");

        if (_stageRoutine != null) _ui.StopCoroutine(_stageRoutine);
        _stageRoutine = entries.Count > 0 ? _ui.StartCoroutine(_stage.BuildRoutine(entries)) : null;
    }

    /// <summary>Called for every card the page manager stamps, with its index on the page.</summary>
    public void DecorateCard(UmaUIContainer card, int index)
    {
        if (card == null) return;
        _cards.Add(card);

        if (_entries.TryGetValue(card.Id ?? "", out var entry))
        {
            card.Name = PropCatalog.Label(entry);
            card.Keywords = PropCatalog.Family(entry.Name);
        }

        var preview = card.transform.Find("Preview");
        if (preview)
        {
            var raw = preview.GetComponent<RawImage>();
            if (raw != null && _stage != null && _stage.Atlas != null)
            {
                raw.texture = _stage.Atlas;
                raw.uvRect = _stage.CellRect(index);
            }
        }

        var pick = card.transform.Find("Pick");
        if (pick)
        {
            var button = pick.GetComponent<Button>();
            if (button)
            {
                button.onClick.RemoveAllListeners();
                string id = card.Id;
                button.onClick.AddListener(() => ToggleSelection(id));
            }
        }
    }

    /// <summary>
    /// Called before the page's cards are stamped: the previous page is gone, so its cards are dropped here
    /// rather than in <see cref="OnPageDrawn"/>, and the stage is made the right shape first so that a card can
    /// be handed its square of the atlas as it is built.
    /// </summary>
    public void OnPageStarting()
    {
        _cards.Clear();
        EnsureStage();
    }

    /// <summary>
    /// Makes sure a stage of exactly this page's shape exists. A card reads one cell, so the atlas has to be as
    /// many columns and rows as the page - a stage of the wrong shape leaves cards reading cells nobody drew.
    /// </summary>
    private void EnsureStage()
    {
        int columns = Mathf.Max(1, _ui.PropPageCtrl != null ? _ui.PropPageCtrl.GridColumns : 1);
        int rows = Mathf.Max(1, _ui.PropPageCtrl != null ? _ui.PropPageCtrl.GridRows : 1);

        if (_stage != null && _stage.Columns == columns && _stage.Rows == rows) return;
        if (_stage != null)
        {
            if (_stageRoutine != null) _ui.StopCoroutine(_stageRoutine);
            _stageRoutine = null;
            _stage.Clear();
            Destroy(_stage.gameObject);
        }

        // parented to nothing on purpose: a canvas hands its children a scaled transform (0.33 on a 1440p
        // screen), which shrinks a world-space stage while the camera's orthographic size stays in world units
        _stage = PropThumbnailStage.Create(null, columns, rows);
        _stage.Spin(true);
    }

    /// <summary>Stops drawing and releases the page's bundles. Called when the panel leaves the grid view.</summary>
    public void Stop()
    {
        if (_stageRoutine != null)
        {
            _ui.StopCoroutine(_stageRoutine);
            _stageRoutine = null;
        }
        if (_stage == null) return;
        _stage.Clear();
        Destroy(_stage.gameObject);
        _stage = null;
    }

    /// <summary>Draws the page again, e.g. after a prop load unloaded the bundles the pictures came from.</summary>
    public void Reload()
    {
        if (_stage != null) _stage.Clear();
        if (_ui != null && _ui.PropPageCtrl != null) _ui.PropPageCtrl.Reload();
    }

    /// <summary>Fills the manifest table the cards look their entries up in. Called when the prop list is built.</summary>
    public void SetEntries(IEnumerable<UmaDatabaseEntry> entries)
    {
        _entries.Clear();
        foreach (var entry in entries)
        {
            if (entry != null) _entries[entry.Name] = entry;
        }
    }

    // -------------------------------------------------------------------------------------- selection

    private void ToggleSelection(string id)
    {
        if (string.IsNullOrEmpty(id)) return;
        if (!_selected.Remove(id)) _selected.Add(id);
        RefreshSelectionVisuals();
        if (_ui != null) _ui.RefreshPropGridButtons();
    }

    /// <summary>Clears every mark. Called from the panel's own row.</summary>
    public void ClearSelection()
    {
        _selected.Clear();
        RefreshSelectionVisuals();
        if (_ui != null) _ui.RefreshPropGridButtons();
    }

    private void RefreshSelectionVisuals()
    {
        foreach (var card in _cards)
        {
            if (!card) continue;
            bool selected = _selected.Contains(card.Id);
            if (card.Image) card.Image.color = selected ? CardSelectedColour : CardColour;
            var pick = card.transform.Find("Pick");
            if (!pick) continue;
            var image = pick.GetComponent<Image>();
            if (image) image.color = selected ? AccentColour : PickOffColour;
        }
    }

    /// <summary>Every prop marked for export, in the order the manifest lists them.</summary>
    public List<UmaDatabaseEntry> SelectedEntries() =>
        _entries.Values.Where(entry => _selected.Contains(entry.Name)).OrderBy(entry => entry.Name, StringComparer.Ordinal).ToList();

    // ----------------------------------------------------------------------------------------- export

    /// <summary>
    /// Writes the marked props out as PMX files into a folder the user picks.
    ///
    /// One prop at a time through the same `LoadProp` + `ModelExporter.ExportModel` path the panel's own export
    /// button uses, so a batch writes exactly what a single export would. `LoadProp` unloads every bundle,
    /// including the ones the cards are drawn from, so the page is redrawn afterwards.
    /// </summary>
    public void ExportSelected()
    {
        var picks = SelectedEntries();
        if (picks.Count == 0) return;

        string[] folders = StandaloneFileBrowser.OpenFolderPanel(
            "Export the selected props to", Config.Instance.ModelSaveFolder(Config.Instance.MainPath), false);
        if (folders == null || folders.Length == 0 || string.IsNullOrEmpty(folders[0])) return;

        int written = 0;
        var failed = new List<string>();
        foreach (var entry in picks)
        {
            string path = Path.Combine(folders[0], Path.GetFileName(entry.Name) + ".pmx");
            try
            {
                UmaViewerBuilder.Instance.LoadProp(entry);
                var container = UmaViewerBuilder.Instance.CurrentOtherContainer;
                if (container == null) throw new Exception("the prop did not load");
                ModelExporter.ExportModel(container, path);
                written++;
            }
            catch (Exception e)
            {
                failed.Add(Path.GetFileName(entry.Name));
                Debug.LogError($"[PropGrid] export of {entry.Name} failed: {e}");
            }
        }

        Debug.Log($"[PropGrid] exported {written} of {picks.Count} prop(s) to {folders[0]}"
                  + (failed.Count > 0 ? $"; failed: {string.Join(", ", failed)}" : ""));
        _ui?.ShowMessage($"{written} of {picks.Count} prop(s) exported to {folders[0]}"
                         + (failed.Count > 0 ? $" - {failed.Count} failed, see the log" : ""),
                         failed.Count > 0 ? UIMessageType.Warning : UIMessageType.Success);
        Reload();
    }

    // ---------------------------------------------------------------------------------- card template

    /// <summary>
    /// The card a page is stamped from: the panel's own row, made square, with a picture on top, its name along
    /// the bottom and a mark box in the corner. Built from `UmaContainerPrefab` so a card inherits the project's
    /// font, colours and button behaviour instead of inventing its own.
    /// </summary>
    private GameObject BuildCardTemplate()
    {
        var template = new GameObject("PropCard", typeof(RectTransform), typeof(UmaUIContainer));
        template.transform.SetParent(transform, false);
        var rect = (RectTransform)template.transform;
        rect.sizeDelta = new Vector2(_ui.PropPageCtrl != null ? _ui.PropPageCtrl.GridCellSize.x : 170f,
                                     _ui.PropPageCtrl != null ? _ui.PropPageCtrl.GridCellSize.y : 170f);

        var background = template.AddComponent<Image>();
        background.color = CardColour;

        var button = template.AddComponent<Button>();
        button.targetGraphic = background;
        var colours = button.colors;
        colours.highlightedColor = new Color(1f, 1f, 1f, 0.18f);
        colours.pressedColor = new Color(1f, 1f, 1f, 0.3f);
        colours.selectedColor = new Color(1f, 1f, 1f, 0.18f);
        button.colors = colours;
        button.navigation = new Navigation { mode = Navigation.Mode.None };

        var preview = NewRect("Preview", rect);
        preview.anchorMin = Vector2.zero;
        preview.anchorMax = Vector2.one;
        preview.offsetMin = new Vector2(4f, LabelHeight + 2f);
        preview.offsetMax = new Vector2(-4f, -4f);
        var raw = preview.gameObject.AddComponent<RawImage>();
        raw.raycastTarget = false;

        var label = NewLabel(rect, "Name", "", 13, TextAnchor.MiddleCenter);
        var labelRect = label.rectTransform;
        labelRect.anchorMin = new Vector2(0f, 0f);
        labelRect.anchorMax = new Vector2(1f, 0f);
        labelRect.pivot = new Vector2(0.5f, 0f);
        labelRect.offsetMin = new Vector2(4f, 2f);
        labelRect.offsetMax = new Vector2(-4f, LabelHeight);
        label.horizontalOverflow = HorizontalWrapMode.Wrap;

        var pickRect = NewRect("Pick", rect);
        pickRect.anchorMin = new Vector2(0f, 1f);
        pickRect.anchorMax = new Vector2(0f, 1f);
        pickRect.pivot = new Vector2(0f, 1f);
        pickRect.anchoredPosition = new Vector2(4f, -4f);
        pickRect.sizeDelta = new Vector2(PickSize, PickSize);
        var pickImage = pickRect.gameObject.AddComponent<Image>();
        pickImage.color = PickOffColour;
        var pickButton = pickRect.gameObject.AddComponent<Button>();
        pickButton.targetGraphic = pickImage;
        pickButton.navigation = new Navigation { mode = Navigation.Mode.None };

        var container = template.GetComponent<UmaUIContainer>();
        container.Text = label;
        container.Button = button;
        container.Image = background;

        template.SetActive(false);
        return template;
    }

    // ---------------------------------------------------------------------------------- small helpers

    private static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private Text NewLabel(Transform parent, string name, string text, int size, TextAnchor align)
    {
        var rect = NewRect(name, parent);
        var label = rect.gameObject.AddComponent<Text>();
        label.font = _font;
        label.fontSize = size;
        label.color = _labelColour;
        label.alignment = align;
        label.text = text;
        label.raycastTarget = false;
        label.horizontalOverflow = HorizontalWrapMode.Overflow;
        label.verticalOverflow = VerticalWrapMode.Overflow;
        return label;
    }

    /// <summary>One line about what the grid built, for the headless self-check.</summary>
    public string Describe()
    {
        var rect = ((RectTransform)transform).rect;
        string atlas = _stage != null && _stage.Atlas != null ? $"{_stage.Atlas.width}x{_stage.Atlas.height}" : "none";
        string stage = _stage == null ? "none" : _stage.Built ? "built" : "building";
        int cells = _stage != null ? _stage.Count : 0;
        int failures = _stage != null ? _stage.Failures.Count : 0;
        var live = _cards.Where(card => card).ToList();
        string first = live.Count > 0
            ? $"{live[0].Name} {((RectTransform)live[0].transform).rect.size.x:F0}px"
            : "no cards";
        int columns = _ui.PropPageCtrl != null ? _ui.PropPageCtrl.GridColumns : 0;
        int rows = _ui.PropPageCtrl != null ? _ui.PropPageCtrl.GridRows : 0;
        string page = _ui.PropPageCtrl != null ? _ui.PropPageCtrl.PageLabel : "?";
        return $"grid {rect.width:F0}x{rect.height:F0}, {columns} column(s) x {rows} row(s), page {page}, "
               + $"cards {live.Count}/{_cards.Count} [{first}], atlas {atlas}, stage {stage} with {cells} cell(s), "
               + $"{failures} failure(s), {_selected.Count} marked";
    }
}
