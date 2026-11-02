using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SFB;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// The prop grid: every prop as a picture you can recognise, instead of a list of ids you cannot.
///
/// Why it exists: the prop panel lists about 1 400 prefabs by name, the names are ids (`pfb_chr_prop1003_10`),
/// and the game ships no name for a prop anywhere in its data (docs/PROP_LABELS.md). Only a quarter of them say
/// anything about themselves through their bone names. So the only reliable way to find the prop you want is to
/// look at it, and that is what this window is for: a page of thumbnails, rendered live from the same bundles
/// the scene loads, turning slowly so a book does not look like a box.
///
/// It is an overlay rather than a replacement - the list stays what it was, this opens on top of it and closes
/// back to it, because for a known id the list is faster and nothing here should take that away.
///
/// What a card does: clicking it loads that prop into the scene, exactly as a list row does. The small square in
/// its corner marks it for export instead, so several props can be written out in one go - the other thing a
/// grid of pictures makes possible that a list of ids does not.
/// </summary>
public class PropBrowser : MonoBehaviour
{
    /// <summary>The open window, or null. One at a time: two would need two stages and two sets of props.</summary>
    public static PropBrowser Instance { get; private set; }

    private const int MaxColumns = 6;

    /// <summary>
    /// Rows of cards on a page. The atlas is six columns by this many rows of 256 pixel cells, so the ceiling is
    /// what keeps the texture at 1536x1536 - beyond that a page of thumbnails costs more memory than it returns.
    /// </summary>
    private const int MaxRows = 6;
    private const float CardSize = 170f;
    private const float CardSpacing = 8f;
    private const float LabelHeight = 30f;
    private const float PickSize = 22f;

    private static readonly Color BackdropColour = new Color(0f, 0f, 0f, 0.82f);
    private static readonly Color PanelColour = new Color(0.14f, 0.15f, 0.18f, 0.98f);
    private static readonly Color CardColour = new Color(1f, 1f, 1f, 0.06f);
    private static readonly Color CardSelectedColour = new Color(1f, 0.83f, 0.35f, 0.22f);
    private static readonly Color AccentColour = new Color(1f, 0.83f, 0.35f, 0.95f);
    private static readonly Color PickOffColour = new Color(0f, 0f, 0f, 0.55f);

    private UmaViewerUI _ui;
    private Font _font;
    private Color _labelColour = Color.white;

    private RectTransform _grid;
    private GridLayoutGroup _layout;
    private Text _title;
    private Text _pageLabel;
    private Text _status;
    private InputField _search;
    private Button _prev;
    private Button _next;
    private Button _export;
    private GameObject _cardTemplate;

    private PropThumbnailStage _stage;
    private Coroutine _stageRoutine;
    private readonly List<UmaUIContainer> _cards = new List<UmaUIContainer>();
    private readonly HashSet<string> _selected = new HashSet<string>(StringComparer.Ordinal);
    private List<UmaDatabaseEntry> _all = new List<UmaDatabaseEntry>();
    private List<UmaDatabaseEntry> _shown = new List<UmaDatabaseEntry>();
    private int _page;
    private int _columns = MaxColumns;
    private int _rows = 1;
    private int _lastPerPage;

    private int PerPage => Mathf.Max(1, _columns * _rows);

    private int PageCount => Mathf.Max(1, Mathf.CeilToInt(_shown.Count / (float)PerPage));

    /// <summary>Opens the window on top of the viewer's UI. Does nothing when it is already open.</summary>
    public static PropBrowser Open(UmaViewerUI ui)
    {
        if (Instance != null) return Instance;
        if (ui == null || ui.canvasScaler == null)
        {
            Debug.LogWarning("[PropBrowser] no canvas to open in, is the viewer UI up?");
            return null;
        }

        var host = new GameObject("PropBrowser", typeof(RectTransform), typeof(Image));
        host.transform.SetParent(ui.canvasScaler.transform, false);
        host.transform.SetAsLastSibling();

        var browser = host.AddComponent<PropBrowser>();
        browser._ui = ui;
        browser.Build();
        Instance = browser;
        return browser;
    }

    private void Build()
    {
        _font = TakeRowFont();
        _all = PropCatalog.Entries();
        _shown = new List<UmaDatabaseEntry>(_all);

        var root = (RectTransform)transform;
        Stretch(root);
        var backdrop = GetComponent<Image>();
        backdrop.color = BackdropColour;
        backdrop.raycastTarget = true;      // the window is modal: clicks must not reach the panel underneath

        var window = NewRect("Window", root);
        window.anchorMin = new Vector2(0.03f, 0.04f);
        window.anchorMax = new Vector2(0.97f, 0.96f);
        window.offsetMin = Vector2.zero;
        window.offsetMax = Vector2.zero;
        window.gameObject.AddComponent<Image>().color = PanelColour;

        BuildHeader(window);
        BuildGrid(window);
        BuildFooter(window);

        ShowPage(0);
        Debug.Log($"[PropBrowser] {_all.Count} prop(s) in the manifest");
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

    private void BuildHeader(RectTransform window)
    {
        var header = NewRect("Header", window);
        header.anchorMin = new Vector2(0f, 1f);
        header.anchorMax = new Vector2(1f, 1f);
        header.pivot = new Vector2(0.5f, 1f);
        header.offsetMin = new Vector2(12f, -56f);
        header.offsetMax = new Vector2(-12f, -8f);

        _title = NewLabel(header, "Title", "Props", 20, TextAnchor.MiddleLeft);
        Put(_title.rectTransform, new Vector2(0f, 0f), new Vector2(0.16f, 1f), new Vector2(4f, 0f), Vector2.zero);

        _search = NewSearch(header, "search a name or family: 1003, toon, rich");
        Put((RectTransform)_search.transform, new Vector2(0.16f, 0.12f), new Vector2(0.62f, 0.88f), Vector2.zero, Vector2.zero);

        _prev = NewButton(header, "◀", () => ShowPage(_page - 1));
        Put((RectTransform)_prev.transform, new Vector2(0.63f, 0.12f), new Vector2(0.67f, 0.88f), Vector2.zero, Vector2.zero);

        _pageLabel = NewLabel(header, "Page", "- / -", 15, TextAnchor.MiddleCenter);
        Put(_pageLabel.rectTransform, new Vector2(0.67f, 0.12f), new Vector2(0.78f, 0.88f), Vector2.zero, Vector2.zero);

        _next = NewButton(header, "▶", () => ShowPage(_page + 1));
        Put((RectTransform)_next.transform, new Vector2(0.78f, 0.12f), new Vector2(0.82f, 0.88f), Vector2.zero, Vector2.zero);

        // the tickbox the user asked for: unticking it puts the panel's own list back in front of the grid
        var gridToggle = NewToggle(header, "Grid view");
        Put((RectTransform)gridToggle.transform, new Vector2(0.83f, 0.12f), new Vector2(0.92f, 0.88f), Vector2.zero, Vector2.zero);
        gridToggle.SetIsOnWithoutNotify(true);
        gridToggle.onValueChanged.AddListener(on =>
        {
            if (!on) Close();
        });

        var close = NewButton(header, "✕", Close);
        Put((RectTransform)close.transform, new Vector2(0.93f, 0.12f), new Vector2(1f, 0.88f), Vector2.zero, Vector2.zero);
    }

    private void BuildGrid(RectTransform window)
    {
        var grid = NewRect("Grid", window);
        grid.anchorMin = new Vector2(0f, 0f);
        grid.anchorMax = new Vector2(1f, 1f);
        grid.offsetMin = new Vector2(16f, 64f);
        grid.offsetMax = new Vector2(-16f, -64f);
        _grid = grid;

        _layout = grid.gameObject.AddComponent<GridLayoutGroup>();
        _layout.cellSize = new Vector2(CardSize, CardSize);
        _layout.spacing = new Vector2(CardSpacing, CardSpacing);
        _layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        _layout.constraintCount = MaxColumns;
        _layout.childAlignment = TextAnchor.UpperCenter;
        _layout.startCorner = GridLayoutGroup.Corner.UpperLeft;
    }

    private void BuildFooter(RectTransform window)
    {
        var footer = NewRect("Footer", window);
        footer.anchorMin = new Vector2(0f, 0f);
        footer.anchorMax = new Vector2(1f, 0f);
        footer.pivot = new Vector2(0.5f, 0f);
        footer.offsetMin = new Vector2(12f, 8f);
        footer.offsetMax = new Vector2(-12f, 56f);

        _status = NewLabel(footer, "Status", "", 14, TextAnchor.MiddleLeft);
        Put(_status.rectTransform, new Vector2(0f, 0f), new Vector2(0.66f, 1f), new Vector2(4f, 0f), Vector2.zero);

        _export = NewButton(footer, "Export selected", ExportSelected);
        Put((RectTransform)_export.transform, new Vector2(0.66f, 0.12f), new Vector2(0.84f, 0.88f), Vector2.zero, Vector2.zero);

        var clear = NewButton(footer, "Clear selection", ClearSelection);
        Put((RectTransform)clear.transform, new Vector2(0.84f, 0.12f), new Vector2(1f, 0.88f), Vector2.zero, Vector2.zero);
    }

    private void Update()
    {
        // the panel's rect is zero until the canvas has laid out once, so the page that fits is only known a
        // frame after the window opens; recompute when it changes rather than at a guessed size
        if (_layout == null) return;
        RefreshLayoutMetrics();
        if (PerPage == _lastPerPage) return;
        _lastPerPage = PerPage;
        ShowPage(_page);
    }

    // ------------------------------------------------------------------------------------------ the page

    private void ShowPage(int page)
    {
        _page = Mathf.Clamp(page, 0, PageCount - 1);

        foreach (var card in _cards)
        {
            if (card) Destroy(card.gameObject);
        }
        _cards.Clear();

        RefreshLayoutMetrics();
        _lastPerPage = PerPage;

        EnsureStage();

        var slice = _shown.Skip(_page * PerPage).Take(PerPage).ToList();
        foreach (var entry in slice)
        {
            var card = NewCard(entry, _cards.Count);
            if (card != null) _cards.Add(card);
        }

        _pageLabel.text = $"{_page + 1} / {PageCount}";
        _prev.interactable = _page > 0;
        _next.interactable = _page < PageCount - 1;
        RefreshSelectionVisuals();
        RefreshStatus();

        if (_stage == null) return;
        if (_stageRoutine != null) StopCoroutine(_stageRoutine);
        _stageRoutine = StartCoroutine(_stage.BuildRoutine(slice));
    }

    private void RefreshLayoutMetrics()
    {
        float width = _grid.rect.width;
        float height = _grid.rect.height;
        if (width <= 1f || height <= 1f) return;

        int columns = Mathf.FloorToInt((width + CardSpacing) / (CardSize + CardSpacing));
        _columns = Mathf.Clamp(columns, 1, MaxColumns);
        _rows = Mathf.Clamp(Mathf.FloorToInt((height + CardSpacing) / (CardSize + CardSpacing)), 1, MaxRows);
        _layout.constraintCount = _columns;
    }

    /// <summary>
    /// Makes sure a stage of exactly this page's shape exists. The atlas is one square per card, so a window
    /// that shows six columns by six rows needs a 6x6 atlas - a fixed 6x4 one would leave twelve cards reading
    /// a cell nobody drew, which is how the first self-check came back with 36 cards and 24 cells.
    /// </summary>
    private void EnsureStage()
    {
        if (_stage != null && _stage.Columns == _columns && _stage.Rows == _rows) return;

        if (_stage != null)
        {
            if (_stageRoutine != null) StopCoroutine(_stageRoutine);
            _stageRoutine = null;
            _stage.Clear();
            Destroy(_stage.gameObject);
        }
        // parented to nothing on purpose: the browser lives under a canvas, and a canvas hands its
        // children a scaled transform (0.33 on a 1440p screen), which shrinks the whole stage while the
        // camera's orthographic size stays in world units - the page then draws as a blob in the middle
        // of the atlas and most cells come back empty
        _stage = PropThumbnailStage.Create(null, _columns, _rows);
        _stage.Spin(true);
    }

    private UmaUIContainer NewCard(UmaDatabaseEntry entry, int index)
    {
        if (_cardTemplate == null) _cardTemplate = BuildCardTemplate();

        var card = Instantiate(_cardTemplate, _grid).GetComponent<UmaUIContainer>();
        card.gameObject.SetActive(true);
        card.name = Path.GetFileName(entry.Name);
        card.Id = entry.Name;
        card.Keywords = PropCatalog.Family(entry.Name);
        card.Name = PropCatalog.Label(entry);

        var preview = card.transform.Find("Preview");
        if (preview)
        {
            var raw = preview.GetComponent<RawImage>();
            if (raw && _stage != null && _stage.Atlas != null)
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
                button.onClick.AddListener(() => ToggleSelection(entry));
            }
        }

        if (card.Button)
        {
            card.Button.onClick.RemoveAllListeners();
            card.Button.onClick.AddListener(() =>
            {
                Debug.Log($"[PropBrowser] loading {entry.Name}");
                UmaViewerBuilder.Instance.LoadProp(entry);
                // LoadProp unloads every bundle, including the ones this page's pictures are drawn from, so
                // the page is rebuilt from scratch: leaving it would show a grid of thumbnails that are no
                // longer backed by anything
                if (_stage != null)
                {
                    _stage.Clear();
                    ShowPage(_page);
                }
            });
        }
        return card;
    }

    /// <summary>
    /// The card every page is stamped from: the viewer's own panel row, made square, with a picture on top, its
    /// name along the bottom and a mark box in the corner. Built once here rather than saved as a prefab so the
    /// browser needs no scene or asset changes - and so it inherits the project's font and colours.
    /// </summary>
    private GameObject BuildCardTemplate()
    {
        var template = new GameObject("PropCard", typeof(RectTransform), typeof(UmaUIContainer));
        template.transform.SetParent(transform, false);
        var rect = (RectTransform)template.transform;
        rect.sizeDelta = new Vector2(CardSize, CardSize);

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

    // ---------------------------------------------------------------------------------------- selection

    private void ToggleSelection(UmaDatabaseEntry entry)
    {
        if (entry == null) return;
        if (!_selected.Remove(entry.Name)) _selected.Add(entry.Name);
        RefreshSelectionVisuals();
        RefreshStatus();
    }

    private void ClearSelection()
    {
        _selected.Clear();
        RefreshSelectionVisuals();
        RefreshStatus();
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
        if (_export) _export.interactable = _selected.Count > 0;
    }

    private void RefreshStatus()
    {
        if (_status == null) return;
        string failures = _stage != null && _stage.Failures.Count > 0
            ? $"   ({_stage.Failures.Count} could not be drawn)"
            : "";
        _status.text = $"{_shown.Count} of {_all.Count} prop(s), {_selected.Count} marked{failures}";
        if (_title) _title.text = $"Props{(_shown.Count != _all.Count ? $" ({_shown.Count} shown)" : "")}";
    }

    // ------------------------------------------------------------------------------------------- search

    /// <summary>Jumps to a page, clamped to the ones that exist. Used by the self-check and by future keys.</summary>
    public void GoToPage(int page) => ShowPage(page);

    /// <summary>Types into the search box, as the user would: the same filtering, through the same field.</summary>
    public void SetSearch(string term)
    {
        if (_search != null) _search.text = term;
        else OnSearch(term);
    }

    private void OnSearch(string term)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            _shown = new List<UmaDatabaseEntry>(_all);
        }
        else
        {
            string needle = term.Trim();
            _shown = _all.Where(entry =>
                    entry.Name.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    PropCatalog.Label(entry).IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();
        }
        ShowPage(0);
    }

    // ------------------------------------------------------------------------------------------- export

    /// <summary>
    /// Writes the marked props out as PMX files into a folder the user picks.
    ///
    /// One prop at a time through the same `LoadProp` + `ModelExporter.ExportModel` path the panel's own export
    /// button uses, so a batch writes exactly what a single export would. `LoadProp` unloads every bundle,
    /// including the ones the cards' pictures are reading from, so the page is rebuilt afterwards - without that
    /// every thumbnail on screen would go blank the moment the first prop was exported.
    /// </summary>
    private void ExportSelected()
    {
        var picks = _all.Where(entry => _selected.Contains(entry.Name)).ToList();
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
                Debug.LogError($"[PropBrowser] export of {entry.Name} failed: {e}");
            }
        }

        Debug.Log($"[PropBrowser] exported {written} of {picks.Count} prop(s) to {folders[0]}"
                  + (failed.Count > 0 ? $"; failed: {string.Join(", ", failed)}" : ""));
        _ui.ShowMessage($"{written} of {picks.Count} prop(s) exported to {folders[0]}"
                        + (failed.Count > 0 ? $" - {failed.Count} failed, see the log" : ""),
                        failed.Count > 0 ? UIMessageType.Warning : UIMessageType.Success);

        if (_stage != null)
        {
            _stage.Clear();
            ShowPage(_page);
        }
    }

    // -------------------------------------------------------------------------------------------- close

    public void Close()
    {
        if (_stageRoutine != null)
        {
            StopCoroutine(_stageRoutine);
            _stageRoutine = null;
        }
        if (_stage != null)
        {
            _stage.Clear();
            Destroy(_stage.gameObject);
            _stage = null;
        }
        Debug.Log("[PropBrowser] closed, back to the prop list");
        Instance = null;
        Destroy(gameObject);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>The cards on screen, in page order. Read by the headless self-check.</summary>
    public IReadOnlyList<UmaUIContainer> Cards => _cards;

    /// <summary>The stage drawing the thumbnails, so the self-check can read its atlas back.</summary>
    public PropThumbnailStage Stage => _stage;

    /// <summary>
    /// One line describing what the window built: how big it came out, how many cards fit, and what the
    /// thumbnails are doing. A window cannot be looked at in batch mode, so this is what the self-check reads
    /// to know the layout resolved and the stage filled.
    /// </summary>
    public string Describe()
    {
        var windowRect = ((RectTransform)transform).rect;
        var gridRect = _grid != null ? _grid.rect : new Rect();
        string atlas = _stage != null && _stage.Atlas != null ? $"{_stage.Atlas.width}x{_stage.Atlas.height}" : "none";
        string stage = _stage == null ? "none" : _stage.Built ? "built" : "building";
        int cells = _stage != null ? _stage.Count : 0;
        int failures = _stage != null ? _stage.Failures.Count : 0;
        string firstCard = _cards.Count > 0
            ? $"{_cards[0].Name} {((RectTransform)_cards[0].transform).rect.size.x:F0}px"
            : "no cards";
        return $"window {windowRect.width:F0}x{windowRect.height:F0}, grid {gridRect.width:F0}x{gridRect.height:F0}, "
               + $"{_columns} column(s) x {_rows} row(s) = {PerPage} per page, "
               + $"page {_page + 1}/{PageCount} of {_shown.Count} shown ({_all.Count} in the manifest), "
               + $"cards {_cards.Count} [{firstCard}], atlas {atlas}, stage {stage} with {cells} cell(s), "
               + $"{failures} failure(s), {_selected.Count} marked";
    }

    // ---------------------------------------------------------------------------------- small UI helpers

    private static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    /// <summary>Anchors a rect across two normalised corners of its parent, with the given pixel insets.</summary>
    private static void Put(RectTransform rect, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
    {
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
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

    private Button NewButton(Transform parent, string text, UnityAction onClick)
    {
        var rect = NewRect(text, parent);
        var image = rect.gameObject.AddComponent<Image>();
        image.color = new Color(1f, 1f, 1f, 0.14f);
        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        button.onClick.AddListener(onClick);

        var label = NewLabel(rect, "Text", text, 15, TextAnchor.MiddleCenter);
        Stretch(label.rectTransform);
        return button;
    }

    private Toggle NewToggle(Transform parent, string text)
    {
        var rect = NewRect("Toggle", parent);
        var image = rect.gameObject.AddComponent<Image>();
        image.color = new Color(1f, 1f, 1f, 0.08f);
        var toggle = rect.gameObject.AddComponent<Toggle>();
        toggle.targetGraphic = image;
        toggle.navigation = new Navigation { mode = Navigation.Mode.None };

        var box = NewRect("Box", rect);
        box.anchorMin = new Vector2(0f, 0.5f);
        box.anchorMax = new Vector2(0f, 0.5f);
        box.pivot = new Vector2(0f, 0.5f);
        box.anchoredPosition = new Vector2(8f, 0f);
        box.sizeDelta = new Vector2(16f, 16f);
        var boxImage = box.gameObject.AddComponent<Image>();
        boxImage.color = new Color(1f, 1f, 1f, 0.25f);

        var check = NewRect("Check", box);
        check.anchorMin = new Vector2(0.18f, 0.18f);
        check.anchorMax = new Vector2(0.82f, 0.82f);
        check.offsetMin = Vector2.zero;
        check.offsetMax = Vector2.zero;
        var checkImage = check.gameObject.AddComponent<Image>();
        checkImage.color = AccentColour;
        checkImage.raycastTarget = false;

        var label = NewLabel(rect, "Text", text, 14, TextAnchor.MiddleLeft);
        label.rectTransform.anchorMin = Vector2.zero;
        label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.offsetMin = new Vector2(30f, 0f);
        label.rectTransform.offsetMax = Vector2.zero;

        toggle.graphic = checkImage;
        toggle.isOn = true;
        return toggle;
    }

    private InputField NewSearch(Transform parent, string placeholderText)
    {
        var rect = NewRect("Search", parent);
        var image = rect.gameObject.AddComponent<Image>();
        image.color = new Color(0f, 0f, 0f, 0.45f);

        var field = rect.gameObject.AddComponent<InputField>();

        var text = NewLabel(rect, "Text", "", 15, TextAnchor.MiddleLeft);
        text.rectTransform.anchorMin = Vector2.zero;
        text.rectTransform.anchorMax = Vector2.one;
        text.rectTransform.offsetMin = new Vector2(8f, 2f);
        text.rectTransform.offsetMax = new Vector2(-8f, -2f);
        text.supportRichText = false;
        text.raycastTarget = true;

        var placeholder = NewLabel(rect, "Placeholder", placeholderText, 15, TextAnchor.MiddleLeft);
        placeholder.rectTransform.anchorMin = Vector2.zero;
        placeholder.rectTransform.anchorMax = Vector2.one;
        placeholder.rectTransform.offsetMin = new Vector2(8f, 2f);
        placeholder.rectTransform.offsetMax = new Vector2(-8f, -2f);
        placeholder.color = new Color(1f, 1f, 1f, 0.35f);
        placeholder.fontStyle = FontStyle.Italic;

        field.textComponent = text;
        field.placeholder = placeholder;
        field.targetGraphic = image;
        field.onValueChanged.AddListener(OnSearch);
        return field;
    }
}
