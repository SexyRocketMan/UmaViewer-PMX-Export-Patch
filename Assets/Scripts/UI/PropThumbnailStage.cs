using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

/// <summary>
/// Draws a page of props into one texture, so the prop browser can show pictures of them.
///
/// The problem this solves: the prop ids are unreadable (`pfb_chr_prop1003_10`), the game ships no names for
/// them, and only a quarter of them name themselves through their bone names (see docs/PROP_LABELS.md). What
/// is left is looking at them, and looking at 1 229 models one click at a time is what the browser exists to
/// avoid. So this renders them - live, from the same bundles the scene loads, so there is no bake step and no
/// cache to keep in step with the game.
///
/// Why one texture and not one per card: a page is a grid of 24 models, and 24 cameras rendering 24 render
/// textures every frame is 24 times the work of the obvious alternative. Instead the models are laid out in a
/// row on a stage of their own, one orthographic camera frames all of them, and each card reads its own square
/// of that single texture through a `RawImage.uvRect`. One render per frame, however many cards are on screen.
///
/// The stage sits 4 000 units below the origin, which is well past the viewer camera's 1 000 unit far plane: the
/// models it holds are real scene objects with real materials, so they have to be *somewhere*, and this is a
/// place the game camera can never see. It also has to be somewhere the light and the shaders behave: they are
/// the same directional light and the same game shaders the viewport uses, so a thumbnail looks like the prop.
///
/// Each model is scaled to fill its cell, because props range from a coin to a building and a grid of photos at
/// true scale would be a grid of one visible prop and twenty specks. The turntable spins the *slot*, not the
/// model, so a model that is off-centre still spins in place.
/// </summary>
public class PropThumbnailStage : MonoBehaviour
{
    /// <summary>
    /// Cells across the atlas. Set by whoever builds the stage, so the atlas and the page are the same shape:
    /// the browser asks for as many columns as its window fits, and no card can then read a cell nobody drew.
    /// </summary>
    public int Columns = 6;

    /// <summary>Cells down the atlas, for the same reason.</summary>
    public int Rows = 4;

    /// <summary>Pixels per cell. The cards draw a cell at about 160 screen pixels, so 256 has room.</summary>
    public const int CellPixels = 256;

    /// <summary>World units per cell on the stage. Only the ratio to <see cref="Fill"/> matters.</summary>
    private const float CellUnits = 1.25f;

    /// <summary>How much of a cell a model may fill: a little air around each one.</summary>
    private const float Fill = 0.78f;

    /// <summary>Turntable speed, degrees per second. Slow enough to read a label off.</summary>
    private const float SpinDegreesPerSecond = 22f;

    /// <summary>Where the stage stands, in world units: outside the viewer camera's far plane.</summary>
    private static readonly Vector3 StageOrigin = new Vector3(0f, -4000f, 0f);

    /// <summary>A cell of the atlas: the model in it, and the pivot the turntable turns.</summary>
    private class Cell
    {
            public Transform Holder;
        public UmaDatabaseEntry Entry;
        public Transform Pivot;
        public GameObject Model;
        public bool Empty;
    }

    private readonly List<Cell> _cells = new List<Cell>();
    private readonly Queue<UmaDatabaseEntry> _queue = new Queue<UmaDatabaseEntry>();
    private readonly Dictionary<string, UmaDatabaseEntry> _loaded = new Dictionary<string, UmaDatabaseEntry>(StringComparer.Ordinal);

    private Camera _camera;
    private RenderTexture _atlas;
    private bool _spin;
    private string _keepForDisplayed = "";
    private HashSet<string> _keepNames = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>The texture the cards sample, or null before the first <see cref="EnsureTargets"/>.</summary>
    public RenderTexture Atlas => _atlas;

    /// <summary>How many models the stage can hold at once: one per card on a full page.</summary>
    public int Capacity => Columns * Rows;

    /// <summary>Models placed, including the empty cells a failed prop left behind.</summary>
    public int Count => _cells.Count;

    /// <summary>True once every queued model has been placed.</summary>
    public bool Idle => _queue.Count == 0;

    /// <summary>Props that could not be instantiated, by asset path, so the UI can say which and why.</summary>
    public List<string> Failures { get; } = new List<string>();

    /// <summary>Logs what the stage measured and changed. Off in the window, on for the headless atlas.</summary>
    public bool Verbose { get; set; }

    /// <summary>
    /// Turns <see cref="Verbose"/> on for every stage built after this is set. The headless self-check uses it to
    /// see the browser's own framing passes, which are otherwise silent.
    /// </summary>
    public static bool AlwaysVerbose;

    /// <summary>The prefab name a cell holds, for the atlas report. Empty when the cell holds nothing.</summary>
    public string CellName(int index)
    {
        if (index < 0 || index >= _cells.Count) return "";
        var entry = _cells[index].Entry;
        return entry != null ? Path.GetFileName(entry.Name) : "";
    }

    /// <summary>
    /// Builds a stage of the given shape. There is one per browser window: it exists while the grid is on screen
    /// and is destroyed with it, so nothing renders and no bundles stay loaded once the user goes back to the
    /// list.
    /// </summary>
    public static PropThumbnailStage Create(Transform parent, int columns = 6, int rows = 4)
    {
        var host = new GameObject("PropThumbnailStage");
        if (parent) host.transform.SetParent(parent, false);
        host.transform.position = StageOrigin;
        var stage = host.AddComponent<PropThumbnailStage>();
        stage.Verbose = AlwaysVerbose;
        stage.Columns = Mathf.Max(1, columns);
        stage.Rows = Mathf.Max(1, rows);
        stage.EnsureTargets();
        return stage;
    }

    private void EnsureTargets()
    {
        if (_camera) return;

        var camObject = new GameObject("Camera");
        camObject.transform.SetParent(transform, false);
        camObject.transform.localPosition = new Vector3(0f, 0f, -CellUnits * 3f);
        camObject.transform.localRotation = Quaternion.identity;      // looking along +Z, at the row of cells
        _camera = camObject.AddComponent<Camera>();
        _camera.orthographic = true;
        _camera.orthographicSize = Rows * CellUnits * 0.5f;
        _camera.nearClipPlane = 0.01f;
        _camera.farClipPlane = CellUnits * 8f;
        _camera.clearFlags = CameraClearFlags.SolidColor;
        _camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
        _camera.cullingMask = ~LayerMask.GetMask("UI");   // the UI is a canvas, not a prop
        Debug.Log($"[stage] built at {transform.position} with lossy scale {transform.lossyScale}");
        _camera.allowHDR = false;
        _camera.allowMSAA = true;
        _camera.enabled = false;                          // rendered by hand, once per frame

        _atlas = new RenderTexture(Columns * CellPixels, Rows * CellPixels, 24, RenderTextureFormat.ARGB32)
        {
            antiAliasing = Mathf.Max(1, QualitySettings.antiAliasing),
            name = "PropThumbnailAtlas",
        };
        _atlas.Create();
        _camera.targetTexture = _atlas;
    }

    /// <summary>True once <see cref="BuildRoutine"/> has placed the page and settled its framing.</summary>
    public bool Built { get; private set; }

    /// <summary>
    /// Fills the stage and frames every model, spread over frames: one prop per frame so the window stays
    /// responsive, then one swept-fit pass per frame so each pass measures a picture that was actually drawn.
    ///
    /// The caller keeps the stage alive until this finishes; <see cref="Built"/> is how it knows.
    /// </summary>
    public IEnumerator BuildRoutine(IEnumerable<UmaDatabaseEntry> entries, int refitPasses = 4)
    {
        Built = false;
        Show(entries);
        while (Step()) yield return null;

        yield return null;                       // let the page reach the atlas before measuring it
        for (int pass = 0; pass < refitPasses; pass++)
        {
            bool changed = SweepFix();
            yield return null;                   // the correction only reaches the atlas on the next frame
            if (!changed) break;
        }
        RenderAtlas();
        Built = true;
    }

    /// <summary>
    /// Queues a page of props for the stage, replacing whatever it held. The models are placed one per
    /// <see cref="Step"/> call, so a page fills in progressively instead of freezing the window for the
    /// two seconds it takes to load twenty-four bundles.
    /// </summary>
    public void Show(IEnumerable<UmaDatabaseEntry> entries)
    {
        Clear();
        EnsureTargets();
        Failures.Clear();
        foreach (var entry in entries.Take(Capacity))
        {
            if (entry != null) _queue.Enqueue(entry);
        }
        RenderAtlas();      // clear the atlas so the first card is not left showing the previous page
    }

    /// <summary>
    /// Places the next queued prop. Returns true while there is more to do, so a caller can loop it in one
    /// frame (the headless atlas) or spread it over frames (the window).
    /// </summary>
    public bool Step()
    {
        if (_queue.Count == 0) return false;
        if (_cells.Count >= Capacity)
        {
            _queue.Clear();
            return false;
        }

        var entry = _queue.Dequeue();
        Step(entry);
        return _queue.Count > 0;
    }

    private void Step(UmaDatabaseEntry entry)
    {
        var cell = new Cell { Entry = entry };
        _cells.Add(cell);

        try
        {
            var prefab = entry.Get<GameObject>();
            if (!prefab)
            {
                Fail(entry, "the bundle holds no prefab");
                return;
            }

            var pivot = new GameObject("Cell" + (_cells.Count - 1));
            pivot.transform.SetParent(transform, false);
            pivot.transform.localPosition = CellPosition(_cells.Count - 1);
            cell.Pivot = pivot.transform;

            var holder = new GameObject("Model").transform;
            holder.SetParent(pivot.transform, false);
            cell.Holder = holder;
            cell.Model = Instantiate(prefab, holder);
            cell.Model.name = Path.GetFileName(entry.Name);
            Fit(cell.Model, holder, pivot.transform.position);
            Track(entry);
        }
        catch (Exception e)
        {
            Fail(entry, e.GetType().Name + ": " + e.Message);
        }
    }

    /// <summary>
    /// Scales a model to roughly fill its cell and moves it so its middle sits on the pivot. This is only the
    /// opening guess - <see cref="SweepFix"/> corrects it from what was actually drawn - so it aims small
    /// and never clips a model at its cell edge.
    ///
    /// It cannot be exact, and the reason is worth writing down: a prop's `Renderer.bounds` is not the shape it
    /// draws. Measured on the first page of `pfb_chr_prop` sets, bounds-based scaling left every book at a third
    /// of its cell, because a skinned prop's bounds follow its mesh and its bones rather than the transform that
    /// was just scaled. What the grid cares about is the drawn size, so the drawn size is what gets measured.
    /// </summary>
    private static void Fit(GameObject model, Transform holder, Vector3 cellCentre)
    {
        var bounds = BoundsOf(model);
        float widest = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
        if (widest > 1e-5f)
        {
            model.transform.localScale = Vector3.one * (CellUnits * Fill * 0.4f / widest);
            bounds = BoundsOf(model);
        }
        // the *holder* carries the offset, never the model: moving the model would leave it orbiting the
        // pivot as the turntable turns, which for a prop modelled off its own origin means it swings out of
        // its cell within a second - a page that fills correctly and is then empty except for the few props
        // that happen to sit near their origin
        holder.position += cellCentre - bounds.center;
    }

    private static Bounds BoundsOf(GameObject model)
    {
        bool any = false;
        var bounds = new Bounds(model.transform.position, Vector3.zero);
        foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
        {
            if (!renderer) continue;
            if (!any) { bounds = renderer.bounds; any = true; }
            else bounds.Encapsulate(renderer.bounds);
        }
        return bounds;
    }

    /// <summary>Where cell <paramref name="index"/> stands: left to right, top row first.</summary>
    private Vector3 CellPosition(int index)
    {
        int column = index % Columns;
        int row = index / Columns;
        float x = (column - (Columns - 1) * 0.5f) * CellUnits;
        float y = ((Rows - 1) * 0.5f - row) * CellUnits;
        return new Vector3(x, y, 0f);
    }

    /// <summary>The square of the atlas a card at <paramref name="index"/> should show.</summary>
    public Rect CellRect(int index)
    {
        if (index < 0 || index >= Capacity) return new Rect(0f, 0f, 0f, 0f);
        int column = index % Columns;
        int row = index / Columns;
        return new Rect(column / (float)Columns, 1f - (row + 1) / (float)Rows,
                        1f / Columns, 1f / Rows);
    }

    /// <summary>
    /// The square of pixels a model covered in one cell, or <see cref="CellBox.Empty"/> when nothing was drawn.
    /// </summary>
    private struct CellBox
    {
        public bool Any;
        public int MinX, MinY, MaxX, MaxY;

        public float Width => MaxX - MinX + 1;
        public float Height => MaxY - MinY + 1;
        public Vector2 Centre => new Vector2((MinX + MaxX) * 0.5f, (MinY + MaxY) * 0.5f);
        public static CellBox Empty => new CellBox { MinX = int.MaxValue, MinY = int.MaxValue, MaxX = -1, MaxY = -1 };
    }

    /// <summary>The angles the turntable is sampled at when deciding how big a prop can be. Every 45 degrees is
    /// enough for the widest projection of anything box-shaped, which is what a cell has to hold.</summary>
    private static readonly float[] SweepAngles = { 0f, 45f, 90f, 135f };

    /// <summary>
    /// Sizes and centres every model so that it fits inside its cell *at every angle of the turntable*, not just
    /// the one it happens to be showing.
    ///
    /// The footprint of a turning object is the union of its projections, so that is what gets measured: the page
    /// is drawn four times, a quarter turn apart, and the four footprints of each cell are merged into one. Scale
    /// so the merged footprint fits the cell and move the model so the merged footprint's middle sits on the
    /// cell's middle, and nothing can leave its square however fast the table turns.
    ///
    /// Both halves of that matter. The first version measured a single angle, so a flat prop that fit from the
    /// front was cut in half at 90 degrees; measuring two and combining them as `sqrt(w0^2 + w90^2)` fixed the
    /// size but not the drift, because the centre of a projection is not the centre of rotation. Merged
    /// footprints answer both.
    ///
    /// Returns true when it changed anything, so a caller can stop once the page has settled.
    /// </summary>
    public bool SweepFix()
    {
        if (_cells.Count == 0) return false;

        float target = CellPixels * Fill;
        float worldPerPixel = CellUnits / CellPixels;

        CellBox[] swept = null;
        foreach (float angle in SweepAngles)
        {
            var boxes = MeasureAt(angle);
            if (boxes == null) return false;
            if (swept == null) swept = boxes;
            else Merge(swept, boxes);
        }

        bool changed = false;
        for (int index = 0; index < _cells.Count; index++)
        {
            var cell = _cells[index];
            if (cell.Model == null) continue;

            var box = swept[index];
            // nothing drawn from any side: a prop whose material is fully transparent, or too small to see
            if (!box.Any) continue;

            float extent = Mathf.Max(box.Width, box.Height);
            float factor = Mathf.Clamp(target / Mathf.Max(extent, 1f), 0.2f, 6f);

            if (Verbose)
            {
                Debug.Log($"[stage] cell {index}: swept {box.Width:F0}x{box.Height:F0}px "
                          + $"({100f * box.Width / CellPixels:F0}% x {100f * box.Height / CellPixels:F0}% of the cell) "
                          + $"-> factor {factor:F2}, scale {cell.Model.transform.lossyScale.x:F3}");
            }

            if (Mathf.Abs(factor - 1f) > 0.02f)
            {
                cell.Model.transform.localScale *= factor;
                changed = true;
            }

            var centre = box.Centre;
            var offset = new Vector3((centre.x - CellPixels * 0.5f) * worldPerPixel,
                                     (centre.y - CellPixels * 0.5f) * worldPerPixel, 0f);
            // a pixel of slack: coarser than that leaves a model a few pixels off centre, which is enough for a
            // prop that fills its cell to poke into the next one at some angle
            if (offset.sqrMagnitude > worldPerPixel * worldPerPixel)
            {
                cell.Holder.position -= offset;
                changed = true;
            }
        }

        // leave the page facing the camera again; the turntable takes it from there
        foreach (var cell in _cells)
        {
            if (cell.Pivot) cell.Pivot.localRotation = Quaternion.identity;
        }
        RenderAtlas();
        return changed;
    }

    /// <summary>
    /// Draws the page with every model turned by <paramref name="degrees"/> and returns what each cell covered.
    /// Null when the atlas could not be read.
    /// </summary>
    private CellBox[] MeasureAt(float degrees)
    {
        foreach (var cell in _cells)
        {
            if (cell.Pivot) cell.Pivot.localRotation = Quaternion.Euler(0f, degrees, 0f);
        }
        RenderAtlas();
        var pixels = ReadPixels();
        if (pixels == null) return null;

        var boxes = new CellBox[_cells.Count];
        for (int index = 0; index < _cells.Count; index++)
        {
            boxes[index] = _cells[index].Model != null ? Measure(pixels, index) : CellBox.Empty;
        }
        return boxes;
    }

    /// <summary>
    /// How many cells a prop spills out of when the page is turned by <paramref name="degrees"/>. A cell that
    /// fits at 0 degrees and not at 45 is exactly the clipping the swept fitting exists to stop, and a still
    /// picture of one angle cannot show it - so it is a number a caller can assert on instead.
    /// </summary>
    public int OverflowAt(float degrees)
    {
        var boxes = MeasureAt(degrees);
        if (boxes == null) return -1;
        int overflowing = 0;
        foreach (var box in boxes)
        {
            if (!box.Any) continue;
            if (box.MinX <= 1 || box.MinY <= 1 || box.MaxX >= CellPixels - 2 || box.MaxY >= CellPixels - 2)
                overflowing++;
        }
        return overflowing;
    }

    /// <summary>Grows <paramref name="into"/> to cover the footprints in <paramref name="with"/>.</summary>
    private static void Merge(CellBox[] into, CellBox[] with)
    {
        for (int i = 0; i < into.Length && i < with.Length; i++)
        {
            var a = into[i];
            var b = with[i];
            if (!b.Any) continue;
            if (!a.Any)
            {
                into[i] = b;
                continue;
            }
            a.MinX = Mathf.Min(a.MinX, b.MinX);
            a.MinY = Mathf.Min(a.MinY, b.MinY);
            a.MaxX = Mathf.Max(a.MaxX, b.MaxX);
            a.MaxY = Mathf.Max(a.MaxY, b.MaxY);
            into[i] = a;
        }
    }



    /// <summary>Reads the atlas pixels, or null when there is nothing to read.</summary>
    private Color32[] ReadPixels()
    {
        if (!_atlas) return null;
        var previous = RenderTexture.active;
        RenderTexture.active = _atlas;
        var texture = new Texture2D(_atlas.width, _atlas.height, TextureFormat.ARGB32, false);
        texture.ReadPixels(new Rect(0, 0, _atlas.width, _atlas.height), 0, 0);
        texture.Apply();
        RenderTexture.active = previous;
        var pixels = texture.GetPixels32();
        Destroy(texture);
        return pixels;
    }

    /// <summary>Where cell <paramref name="index"/>'s model actually drew, in its own cell's pixels.</summary>
    private CellBox Measure(Color32[] pixels, int index)
    {
        var box = CellBox.Empty;
        var rect = CellRect(index);
        int x0 = Mathf.RoundToInt(rect.x * _atlas.width);
        int y0 = Mathf.RoundToInt(rect.y * _atlas.height);
        int width = _atlas.width;
        int height = _atlas.height;

        for (int y = 0; y < CellPixels; y++)
        {
            int py = y0 + y;
            if (py < 0 || py >= height) continue;
            for (int x = 0; x < CellPixels; x++)
            {
                int px = x0 + x;
                if (px < 0 || px >= width) continue;
                if (pixels[py * width + px].a <= 8) continue;
                box.Any = true;
                if (x < box.MinX) box.MinX = x;
                if (x > box.MaxX) box.MaxX = x;
                if (y < box.MinY) box.MinY = y;
                if (y > box.MaxY) box.MaxY = y;
            }
        }
        return box;
    }

    /// <summary>Turns the turntable on or off. Off stops the per-frame render as well.</summary>
    public void Spin(bool on)
    {
        _spin = on;
        if (!on) RenderAtlas();
    }

    private void Update()
    {
        if (!_spin || _cells.Count == 0) return;
        float degrees = SpinDegreesPerSecond * Time.unscaledDeltaTime;
        foreach (var cell in _cells)
        {
            if (cell?.Pivot) cell.Pivot.Rotate(0f, degrees, 0f, Space.World);
        }
        RenderAtlas();
    }

    /// <summary>Draws the stage into the atlas once, by hand: the camera is disabled, nothing else renders it.</summary>
    public void RenderAtlas()
    {
        if (!_camera || !_atlas) return;
        _camera.Render();
    }

    /// <summary>Reads the atlas back as a texture. The headless atlas writes it as a PNG; the window never does.</summary>
    public Texture2D ReadAtlas()
    {
        if (!_atlas) return null;
        RenderAtlas();
        var previous = RenderTexture.active;
        RenderTexture.active = _atlas;
        var texture = new Texture2D(_atlas.width, _atlas.height, TextureFormat.ARGB32, false);
        texture.ReadPixels(new Rect(0, 0, _atlas.width, _atlas.height), 0, 0);
        texture.Apply();
        RenderTexture.active = previous;
        return texture;
    }

    /// <summary>
    /// Empties the stage and releases the bundles it loaded, so browsing ten pages does not hold ten pages'
    /// worth of props in memory. Bundles of the prop the viewport is showing are kept: they are also what the
    /// scene's own materials point at, and unloading them would break the model on screen.
    /// </summary>
    public void Clear()
    {
        foreach (var cell in _cells)
        {
            if (cell.Model) Destroy(cell.Model);
            if (cell.Pivot) Destroy(cell.Pivot.gameObject);
        }
        _cells.Clear();
        _queue.Clear();
        ReleaseBundles();
        RenderAtlas();
    }

    private void OnDestroy()
    {
        Clear();
        if (_camera) _camera.targetTexture = null;
        if (_atlas)
        {
            _atlas.Release();
            Destroy(_atlas);
            _atlas = null;
        }
    }

    private void Fail(UmaDatabaseEntry entry, string why)
    {
        Failures.Add($"{entry.Name}: {why}");
        Debug.LogWarning($"[PropThumbnailStage] {entry.Name}: {why}");
        var index = _cells.Count - 1;
        if (index >= 0 && index < _cells.Count) _cells[index].Empty = true;
    }

    /// <summary>
    /// Remembers every bundle a prop's model pulled in - the prefab and, through it, the textures - so the page
    /// can be released again. `SearchAB` is the same walk the loader did, so the two agree on what is loaded.
    /// </summary>
    private void Track(UmaDatabaseEntry entry)
    {
        foreach (var loaded in UmaAssetManager.SearchAB(UmaViewerMain.Instance, entry))
        {
            if (loaded != null && !_loaded.ContainsKey(loaded.Name)) _loaded[loaded.Name] = loaded;
        }
    }

    private void ReleaseBundles()
    {
        var keep = KeepNames();
        foreach (var pair in _loaded)
        {
            if (keep.Contains(pair.Key)) continue;
            UmaAssetManager.UnloadAssetBundle(pair.Value, true);
        }
        _loaded.Clear();
    }

    /// <summary>
    /// The bundle names the viewport still needs: the prop it shows, and everything that prop was loaded with.
    /// Recomputed only when the shown prop changes, because finding its manifest entry is a walk of the list.
    /// </summary>
    private HashSet<string> KeepNames()
    {
        var displayed = UmaViewerBuilder.Instance != null ? UmaViewerBuilder.Instance.CurrentOtherContainer : null;
        string name = displayed != null ? displayed.name : "";
        if (name == _keepForDisplayed) return _keepNames;

        _keepForDisplayed = name;
        _keepNames = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(name)) return _keepNames;

        var main = UmaViewerMain.Instance;
        if (main == null || main.AbList == null) return _keepNames;
        UmaDatabaseEntry shown = null;
        foreach (var entry in main.AbList.Values)
        {
            if (Path.GetFileName(entry.Name) == name) { shown = entry; break; }
        }
        if (shown == null) return _keepNames;
        foreach (var used in UmaAssetManager.SearchAB(main, shown))
        {
            if (used != null) _keepNames.Add(used.Name);
        }
        return _keepNames;
    }
}
