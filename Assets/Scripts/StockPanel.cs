using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// The STOCK materials view (#126, the locked M3 design): a panel at the flyout position, opened from the build rail's
/// STOCK entry, listing one dense row per DISCOVERED material (swatch, name, count; a zero count greyed) under family
/// headers, narrowed by a filter field and scrollable. Undiscovered materials get no row and no placeholder: a material
/// appears on no menu until the stockpile has held it. Selecting a row shows it on the detail card (material mode);
/// selecting it again deselects. It is a view: counts and discovery come from the Stockpile, families and copy from
/// BuildPresentation.
/// One open thing: the panel and a category flyout are never open together, and opening the panel puts any tool down
/// (arming a type or selecting DIG closes it again). Esc unwinds one level per press, only in Neutral and never on a
/// press the controller spent or the filter field took: a selected row clears first, then the panel closes.
/// Built in code like the rest of the chrome (own overlay Canvas, no scene-authored UI); rows re-read the stockpile on
/// open and on the 0.25 s unscaled rhythm while open.
/// Mana (#145): discovered mana types follow the materials under a Mana header, one read-only row each (color chip, name,
/// the global sum across every store); the filter applies to them too. Mana isn't a build material, so a mana row selects
/// nothing.
/// </summary>
[DisallowMultipleComponent]
public class StockPanel : MonoBehaviour
{
    public const string TitleText = "Materials";

    [Header("References")]
    [SerializeField] private PlacementController placementController;
    [SerializeField] private PlacementManager placementManager;
    [SerializeField] private Stockpile stockpile;
    [SerializeField] private DetailCard detailCard;
    [SerializeField] private BuildRail buildRail; // Its flyout closes when the panel opens
    [SerializeField] private ManaFlow manaFlow;   // #145: the mana rows' source; unwired, found at Start

    [Header("Layout")]
    [SerializeField] private float leftOffset = 54f;        // BuildRail.railWidth: the flyout position
    [SerializeField] private float topOffset = 36f;         // BuildRail.topOffset: under the top bar
    [SerializeField] private float panelWidth = 400f;
    [SerializeField] private float padding = 6f;
    [SerializeField] private float titleHeight = 24f;
    [SerializeField] private float filterHeight = 26f;
    [SerializeField] private float headerRowHeight = 22f;
    [SerializeField] private float rowHeight = 26f;
    [SerializeField] private float rowGap = 2f;
    [SerializeField] private float swatchSize = 12f;
    [SerializeField] private float maxListHeight = 360f;    // Past this the list scrolls
    [SerializeField] private float scrollSensitivity = 24f;
    [SerializeField] private int titleFontSize = 13;
    [SerializeField] private int headerFontSize = 11;
    [SerializeField] private int rowFontSize = 13;
    [SerializeField] private float refreshSeconds = 0.25f;  // How often counts and discovery re-check while open (unscaled)

    [Header("Colors")]
    [SerializeField] private Color panelColor = new Color(0f, 0f, 0f, 0.6f);                 // Matches the build rail
    [SerializeField] private Color rowColor = new Color(0.25f, 0.25f, 0.30f, 1f);
    [SerializeField] private Color rowSelectedColor = new Color(0.85f, 0.65f, 0.15f, 1f);
    [SerializeField] private Color fieldColor = new Color(0.12f, 0.12f, 0.15f, 1f);
    [SerializeField] private Color secondaryTextColor = new Color(0.82f, 0.82f, 0.82f, 1f);
    [SerializeField] private Color zeroCountColor = new Color(0.5f, 0.5f, 0.5f, 1f);

    /// <summary>The mode owner. Assigning it also subscribes the panel to its mode changes (it closes on arm / dig).</summary>
    public PlacementController PlacementController
    {
        get => placementController;
        set
        {
            if (placementController != null) placementController.ModeChanged -= OnModeChanged;
            placementController = value;
            if (placementController != null) placementController.ModeChanged += OnModeChanged;
        }
    }
    public PlacementManager PlacementManager { get => placementManager; set => placementManager = value; }
    public Stockpile Stockpile { get => stockpile; set => stockpile = value; }
    public DetailCard DetailCard { get => detailCard; set => detailCard = value; }
    public BuildRail BuildRail { get => buildRail; set => buildRail = value; }
    public ManaFlow Mana { get => manaFlow; set => manaFlow = value; }
    public Color RowColor => rowColor;
    public Color RowSelectedColor => rowSelectedColor;
    public Color ZeroCountColor => zeroCountColor;
    public float MaxListHeight { get => maxListHeight; set => maxListHeight = value; }

    public bool IsOpen { get; private set; }

    /// <summary>The material whose row is selected (shown on the detail card); null when none is.</summary>
    public MaterialType? SelectedMaterial { get; private set; }

    /// <summary>The filter text: rows whose name contains it, ignoring case. Empty shows every discovered material.</summary>
    public string Filter { get; private set; } = "";

    /// <summary>One line of the list: a family header, a material's row, or (#145) a mana type's read-only row.</summary>
    public readonly struct Row
    {
        public readonly bool isHeader;
        public readonly MaterialFamily family;
        public readonly MaterialType material; // Meaningless for a header or a mana row
        public readonly string name;           // The family's name for a header
        public readonly int count;             // 0 for a header
        public readonly string manaType;       // #145: the mana type's id on a mana row; null otherwise
        public readonly Color color;           // #145: a mana row's chip color

        public Row(bool isHeader, MaterialFamily family, MaterialType material, string name, int count,
            string manaType = null, Color color = default)
        {
            this.isHeader = isHeader;
            this.family = family;
            this.material = material;
            this.name = name;
            this.count = count;
            this.manaType = manaType;
            this.color = color;
        }

        public bool IsMana => manaType != null;
    }

    // ---- input ----
    private InputConfig.Actions input; // Bindings come from InputConfig (rebindable); enabled with this component
    private float refreshTimer;

    // ---- chrome (code-built; a recompile can null or empty any of it, so every reader guards for null and length, #83) ----
    private Canvas canvas;
    // #137: true once Build has wired this chrome in the current domain. NonSerialized, so a domain reload resets it
    // while the canvas reference survives: that pair is how OnEnable tells a reload (row and filter listeners and the
    // ModeChanged subscription lost) from an ordinary re-enable.
    [System.NonSerialized] private bool chromeWired;
    private RectTransform panelRect;
    private InputField filterField;
    private RectTransform listRect;
    private ScrollRect scrollRect;
    private RectTransform contentRect;
    private RowView[] rowViews;
    private string builtSignature;
    private Font font;

    private sealed class RowView
    {
        public bool isHeader;
        public MaterialType material;
        public RectTransform rect;
        public Image background;
        public Text name;
        public Text count;
    }

    // ---- lifecycle ----

    private void Awake()
    {
        EnsureInput();
    }

    // #137: a domain reload mid-Play (Recompile And Continue Playing) re-runs OnEnable but not Awake or Start, so the
    // input is ensured here and chrome that outlived the reload is rebuilt with its wiring.
    private void OnEnable()
    {
        EnsureInput();
        input.Enable();
        if (canvas != null && !chromeWired) RebuildAfterReload();
    }

    private void EnsureInput() => input ??= InputConfig.CreateActions();

    private void RebuildAfterReload()
    {
        DestroyChrome();
        Build();
        if (placementController == null) return;
        placementController.ModeChanged -= OnModeChanged; // Never twice, whatever survived
        placementController.ModeChanged += OnModeChanged;
    }

    private void OnDisable()
    {
        input?.Disable();
    }

    private void OnDestroy()
    {
        input?.Dispose();
        if (placementController != null) placementController.ModeChanged -= OnModeChanged;
        DestroyChrome();
    }

    private void Start()
    {
        PlacementController = placementController ?? FindAnyObjectByType<PlacementController>(); // Through the setter: subscribes once
        placementManager ??= FindAnyObjectByType<PlacementManager>();
        stockpile ??= FindAnyObjectByType<Stockpile>();
        detailCard ??= FindAnyObjectByType<DetailCard>();
        buildRail ??= FindAnyObjectByType<BuildRail>();
        manaFlow ??= FindAnyObjectByType<ManaFlow>();

        PlacementController.EnsureEventSystem();
        Build();
    }

    private void Update()
    {
        if (input?.Abort.WasPressedThisFrame() ?? false) HandleAbort();

        if (!IsOpen) return;
        refreshTimer += Time.unscaledDeltaTime; // Counts and discovery track the stockpile even while paused
        if (refreshTimer < refreshSeconds) return;
        refreshTimer = 0f;
        Repaint();
    }

    private void LateUpdate()
    {
        TextInputFocus.Track(); // The filter field is the game's only text field
    }

    // ---- what the panel reads ----

    /// <summary>The stockpile the rows read: the wired one, else the placement manager's.</summary>
    public Stockpile CurrentStockpile => stockpile != null ? stockpile : placementManager != null ? placementManager.Stockpile : null;

    /// <summary>The rows the panel shows now: Rows over the current stockpile and filter.</summary>
    public List<Row> CurrentRows() => Rows(CurrentStockpile, manaFlow, Filter);

    /// <summary>
    /// The list (#126): one row per discovered catalog material whose display name contains the filter (ignoring case),
    /// in catalog order within its family, the families in BuildPresentation's order, each led by its header. A family
    /// with no visible row has no header; an undiscovered material has no row. Nothing with no stockpile.
    /// </summary>
    public static List<Row> Rows(Stockpile stockpile, string filter)
    {
        var rows = new List<Row>();
        if (stockpile == null) return rows;

        string needle = (filter ?? "").Trim();
        foreach (MaterialFamily family in BuildPresentation.FamilyOrder)
        {
            bool headed = false;
            foreach (MaterialDefinition d in stockpile.Definitions)
            {
                if (d == null || BuildPresentation.FamilyOf(d.type) != family || !stockpile.IsDiscovered(d.type)) continue;
                string name = stockpile.DisplayName(d.type);
                if (needle.Length > 0 && name.IndexOf(needle, System.StringComparison.OrdinalIgnoreCase) < 0) continue;

                if (!headed) rows.Add(new Row(true, family, default, BuildPresentation.FamilyName(family), 0));
                headed = true;
                rows.Add(new Row(false, family, d.type, name, stockpile.Count(d.type)));
            }
        }
        return rows;
    }

    /// <summary>
    /// The list with mana (#145): the material rows, then one row per discovered mana type whose name contains the filter
    /// (catalog order, the global sum as its count) under the Mana header. An undiscovered type has no row anywhere.
    /// </summary>
    public static List<Row> Rows(Stockpile stockpile, ManaFlow mana, string filter)
    {
        List<Row> rows = Rows(stockpile, filter);
        if (mana == null) return rows;
        string needle = (filter ?? "").Trim();
        bool headed = false;
        foreach (string type in mana.DiscoveredTypes())
        {
            string name = mana.DisplayName(type);
            if (needle.Length > 0 && name.IndexOf(needle, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
            MaterialFamily family = BuildPresentation.ManaFamily;
            if (!headed) rows.Add(new Row(true, family, default, BuildPresentation.FamilyName(family), 0));
            headed = true;
            rows.Add(new Row(false, family, default, name, mana.TotalOf(type), type, mana.ColorOf(type)));
        }
        return rows;
    }

    // ---- state ----

    /// <summary>
    /// Opens the panel: any tool is put down (the controller goes Neutral) and the rail's flyout closes, so the panel is
    /// the one open thing.
    /// </summary>
    public void Open()
    {
        if (IsOpen) return;
        if (placementController != null && placementController.Mode != PlacementController.ToolMode.Neutral)
            placementController.ClearSelection();
        if (buildRail != null) buildRail.CloseFlyout();
        IsOpen = true;
        refreshTimer = 0f;
        Repaint();
        if (buildRail != null) buildRail.Repaint(); // The STOCK entry's tint
    }

    /// <summary>Closes the panel: the selection clears (the card leaves material mode) and the filter empties.</summary>
    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        SetSelection(null);
        SetFilter("");
        if (filterField != null && EventSystem.current != null && EventSystem.current.currentSelectedGameObject == filterField.gameObject)
            EventSystem.current.SetSelectedGameObject(null); // The field leaves with the panel: no typing into a hidden field
        Repaint();
        if (buildRail != null) buildRail.Repaint();
    }

    public void Toggle()
    {
        if (IsOpen) Close();
        else Open();
    }

    /// <summary>Sets the filter; the rows follow at once.</summary>
    public void SetFilter(string filter)
    {
        Filter = filter ?? "";
        if (filterField != null && filterField.text != Filter) filterField.SetTextWithoutNotify(Filter);
        Repaint();
    }

    /// <summary>
    /// A row's click: selects its material (the detail card describes it), or deselects it if it is the selected one.
    /// False, and nothing changes, while the panel is closed or for a material that has no row (undiscovered).
    /// </summary>
    public bool SelectRow(MaterialType material)
    {
        if (!IsOpen) return false;
        Stockpile current = CurrentStockpile;
        if (current == null || !current.IsDiscovered(material)) return false;

        SetSelection(SelectedMaterial == material ? (MaterialType?)null : material);
        Repaint();
        return true;
    }

    /// <summary>
    /// Esc at the panel's level: clears a selected row, else closes the panel. Not while a tool is selected or on the frame
    /// the controller spent the press (the panel is Neutral-only anyway), and not while the filter field has the press.
    /// True when the press was the panel's.
    /// </summary>
    public bool HandleAbort()
    {
        if (!IsOpen) return false;
        if (placementController != null &&
            (placementController.Mode != PlacementController.ToolMode.Neutral || placementController.ExitedToolThisFrame
             || placementController.HasPlacedSelection)) // #142: that Esc clears the placed selection first
            return false;
        if (TextInputFocus.IsTyping) return false; // That Esc defocuses the field

        if (SelectedMaterial != null)
        {
            SetSelection(null);
            Repaint();
        }
        else Close();
        return true;
    }

    private void SetSelection(MaterialType? material)
    {
        SelectedMaterial = material;
        if (detailCard != null) detailCard.ShowMaterial(material);
        // #142: the card shows one thing; the most recent selection wins, so a material replaces a placed building.
        if (material != null && placementController != null) placementController.ClearPlacedSelection();
    }

    // Arming a type or selecting DIG, from anywhere, closes the panel: what's open never disagrees with the mode. A placed
    // building selected after the material (#142) takes the card: the row's selection clears.
    private void OnModeChanged()
    {
        if (placementController == null) return;
        if (IsOpen && placementController.Mode != PlacementController.ToolMode.Neutral) Close();
        else if (SelectedMaterial != null && placementController.HasPlacedSelection)
        {
            SetSelection(null);
            Repaint();
        }
    }

    // ---- chrome ----

    /// <summary>Builds the (hidden) panel. Start calls this; public so tests can paint without Play.</summary>
    public void Build()
    {
        if (canvas != null) return;

        var canvasGo = new GameObject("StockPanelCanvas");
        canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvasGo.AddComponent<CanvasScaler>();
        canvasGo.AddComponent<GraphicRaycaster>(); // Rows, the field and the scroll take pointer input

        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        var panelGo = new GameObject("Panel");
        panelGo.transform.SetParent(canvasGo.transform, false);
        panelGo.AddComponent<Image>().color = panelColor; // A raycast target: a press on the panel never reaches the board
        panelRect = panelGo.GetComponent<RectTransform>();
        panelRect.anchorMin = panelRect.anchorMax = new Vector2(0f, 1f);
        panelRect.pivot = new Vector2(0f, 1f);
        panelRect.anchoredPosition = new Vector2(leftOffset, -topOffset);

        float inner = panelWidth - 2f * padding;
        float y = padding;
        Text title = CreateText(panelGo.transform, "Title", titleFontSize, secondaryTextColor, TextAnchor.MiddleLeft);
        title.text = TitleText;
        PlaceTopLeft(title.rectTransform, padding + 2f, y, inner, titleHeight);
        y += titleHeight + padding;

        filterField = CreateFilterField(panelGo.transform);
        PlaceTopLeft((RectTransform)filterField.transform, padding, y, inner, filterHeight);
        y += filterHeight + padding;

        CreateList(panelGo.transform);
        PlaceTopLeft(listRect, padding, y, inner, 0f);

        chromeWired = true; // Rows are built (and wired) later, by Repaint, under this same flag
        Repaint();
    }

    /// <summary>Destroys everything Build made.</summary>
    public void DestroyChrome()
    {
        if (canvas != null) DestroyChromeObject(canvas.gameObject);

        canvas = null;
        chromeWired = false;
        panelRect = null;
        filterField = null;
        listRect = null;
        scrollRect = null;
        contentRect = null;
        rowViews = null;
        builtSignature = null;
    }

    private static void DestroyChromeObject(Object target)
    {
        if (Application.isPlaying) Destroy(target);
        else DestroyImmediate(target);
    }

    /// <summary>
    /// Repaints from the stockpile: hidden while closed; otherwise the rows (rebuilt when the set changes, e.g. on a
    /// discovery or a filter edit), their live counts (greyed at zero) and the selected row's tint, with the list sized to
    /// its rows up to maxListHeight and scrolling past it.
    /// </summary>
    public void Repaint()
    {
        if (canvas == null || panelRect == null) return; // Not built (EditMode), or lost to a recompile

        panelRect.gameObject.SetActive(IsOpen);
        if (!IsOpen) return;

        List<Row> rows = CurrentRows();
        EnsureRowViews(rows);

        float y = 0f;
        for (int i = 0; i < rows.Count && rowViews != null && i < rowViews.Length; i++)
        {
            RowView view = rowViews[i];
            float height = rows[i].isHeader ? headerRowHeight : rowHeight;
            if (view?.rect == null || view.name == null) { y += height + rowGap; continue; }

            PlaceTopLeft(view.rect, 0f, y, panelWidth - 2f * padding, height);
            y += height + rowGap;
            if (rows[i].isHeader) continue;

            bool zero = rows[i].count == 0; // Only the count greys: the name stays legible on the amber selected row
            if (view.count != null)
            {
                view.count.text = rows[i].count.ToString();
                view.count.color = zero ? zeroCountColor : Color.white;
            }
            bool selected = !rows[i].IsMana && SelectedMaterial == rows[i].material;
            if (view.background != null) view.background.color = selected ? rowSelectedColor : rowColor;
        }

        float contentHeight = Mathf.Max(0f, y - rowGap);
        float listHeight = Mathf.Min(contentHeight, maxListHeight);
        if (contentRect != null) contentRect.sizeDelta = new Vector2(0f, contentHeight);
        if (listRect != null) listRect.sizeDelta = new Vector2(listRect.sizeDelta.x, listHeight);

        float top = padding + titleHeight + padding + filterHeight + padding;
        panelRect.sizeDelta = new Vector2(panelWidth, top + listHeight + (listHeight > 0f ? padding : 0f));
    }

    /// <summary>True when the list can scroll: its rows are taller than the visible list.</summary>
    public bool IsListOverflowing => contentRect != null && listRect != null && contentRect.sizeDelta.y > listRect.sizeDelta.y + 0.5f;

    // The row objects match the row set: rebuilt whenever it changes, or when the views are gone while the chrome stands.
    private void EnsureRowViews(List<Row> rows)
    {
        if (contentRect == null) return;
        string signature = Signature(rows);
        if (signature == builtSignature && HasRowViews(rows.Count)) return;

        for (int i = contentRect.childCount - 1; i >= 0; i--)
        {
            GameObject stale = contentRect.GetChild(i).gameObject;
            stale.SetActive(false); // Destroy is deferred in Play: out of sight (and out of Find's reach) at once
            DestroyChromeObject(stale);
        }

        rowViews = new RowView[rows.Count];
        for (int i = 0; i < rows.Count; i++) rowViews[i] = rows[i].isHeader ? CreateHeader(rows[i]) : CreateRow(rows[i]);
        builtSignature = signature;
    }

    private bool HasRowViews(int count)
    {
        if (rowViews == null || rowViews.Length != count) return false;
        foreach (RowView view in rowViews)
            if (view?.rect == null) return false;
        return true;
    }

    private static string Signature(List<Row> rows)
    {
        var parts = new string[rows.Count];
        for (int i = 0; i < rows.Count; i++)
            parts[i] = rows[i].isHeader ? "#" + rows[i].family : rows[i].IsMana ? "~" + rows[i].manaType : rows[i].material.ToString();
        return string.Join("|", parts);
    }

    private RowView CreateHeader(Row row)
    {
        var go = new GameObject(row.family + "Header", typeof(RectTransform));
        go.transform.SetParent(contentRect, false);
        var view = new RowView { isHeader = true, rect = (RectTransform)go.transform };
        view.name = CreateText(go.transform, "Name", headerFontSize, secondaryTextColor, TextAnchor.LowerLeft);
        view.name.text = row.name.ToUpperInvariant();
        Stretch(view.name.rectTransform, 4f, 2f, 4f, 0f);
        return view;
    }

    private RowView CreateRow(Row row)
    {
        var view = new RowView { material = row.material };
        MaterialType material = row.material;

        var go = new GameObject(row.IsMana ? row.manaType + "ManaRow" : row.material + "Row");
        go.transform.SetParent(contentRect, false);
        view.background = go.AddComponent<Image>();
        view.rect = view.background.rectTransform;
        view.background.color = rowColor;
        if (!row.IsMana) // #145: mana rows are read-only (mana isn't a build material)
        {
            var button = go.AddComponent<Button>();
            button.targetGraphic = view.background;
            button.onClick.AddListener(() => SelectRow(material));
        }

        var swatchGo = new GameObject("Swatch");
        swatchGo.transform.SetParent(go.transform, false);
        var swatch = swatchGo.AddComponent<Image>();
        swatch.raycastTarget = false;
        Stockpile current = CurrentStockpile;
        Color swatchColor = row.color;
        swatch.enabled = row.IsMana || (current != null && current.TryGetColor(row.material, out swatchColor)); // No catalog color, no swatch
        swatch.color = swatchColor;
        swatch.rectTransform.anchorMin = swatch.rectTransform.anchorMax = new Vector2(0f, 0.5f);
        swatch.rectTransform.pivot = new Vector2(0f, 0.5f);
        swatch.rectTransform.anchoredPosition = new Vector2(8f, 0f);
        swatch.rectTransform.sizeDelta = new Vector2(swatchSize, swatchSize);

        view.name = CreateText(go.transform, "Name", rowFontSize, Color.white, TextAnchor.MiddleLeft);
        view.name.text = row.name;
        Stretch(view.name.rectTransform, 8f + swatchSize + 8f, 0f, 60f, 0f);

        view.count = CreateText(go.transform, "Count", rowFontSize, Color.white, TextAnchor.MiddleRight);
        Stretch(view.count.rectTransform, 0f, 0f, 10f, 0f);
        return view;
    }

    private InputField CreateFilterField(Transform parent)
    {
        var go = new GameObject("Filter");
        go.transform.SetParent(parent, false);
        var background = go.AddComponent<Image>();
        background.color = fieldColor;
        var field = go.AddComponent<InputField>();
        field.targetGraphic = background;
        field.lineType = InputField.LineType.SingleLine;

        Text text = CreateText(go.transform, "Text", rowFontSize, Color.white, TextAnchor.MiddleLeft);
        text.supportRichText = false;
        Stretch(text.rectTransform, 8f, 2f, 8f, 2f);

        Text placeholder = CreateText(go.transform, "Placeholder", rowFontSize, zeroCountColor, TextAnchor.MiddleLeft);
        placeholder.text = "Filter materials…";
        placeholder.fontStyle = FontStyle.Italic;
        Stretch(placeholder.rectTransform, 8f, 2f, 8f, 2f);

        field.textComponent = text;
        field.placeholder = placeholder;
        field.onValueChanged.AddListener(SetFilter);
        return field;
    }

    // A ScrollRect over a masked viewport; the content's rows are laid out by Repaint.
    private void CreateList(Transform parent)
    {
        var listGo = new GameObject("List");
        listGo.transform.SetParent(parent, false);
        listRect = listGo.AddComponent<RectTransform>();
        var viewportImage = listGo.AddComponent<Image>();
        viewportImage.color = new Color(0f, 0f, 0f, 0f); // Invisible, but a raycast target: drags and the wheel land here
        listGo.AddComponent<RectMask2D>();

        var contentGo = new GameObject("Content", typeof(RectTransform));
        contentGo.transform.SetParent(listGo.transform, false);
        contentRect = (RectTransform)contentGo.transform;
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.anchoredPosition = Vector2.zero;
        contentRect.sizeDelta = Vector2.zero;

        scrollRect = listGo.AddComponent<ScrollRect>();
        scrollRect.viewport = listRect;
        scrollRect.content = contentRect;
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        scrollRect.scrollSensitivity = scrollSensitivity;
    }

    // ---- chrome helpers ----

    private Text CreateText(Transform parent, string name, int size, Color color, TextAnchor alignment)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var text = go.AddComponent<Text>();
        text.font = font;
        text.fontSize = size;
        text.color = color;
        text.alignment = alignment;
        text.raycastTarget = false;
        return text;
    }

    // Fills the parent, inset by the given margins.
    private static void Stretch(RectTransform rect, float left, float bottom, float right, float top)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
    }

    // x right and y down from the parent's top-left corner.
    private static void PlaceTopLeft(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(width, height);
    }
}
